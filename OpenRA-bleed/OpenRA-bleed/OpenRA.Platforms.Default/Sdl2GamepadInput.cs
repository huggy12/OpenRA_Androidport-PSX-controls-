#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using OpenRA.Primitives;
using OpenRA.Widgets;
using SDL2;

namespace OpenRA.Platforms.Default
{
	/// <summary>
	/// Console-style gamepad controls (inspired by the PlayStation port of Red Alert).
	/// The analog stick / d-pad drives an on-screen cursor and face buttons act as mouse buttons,
	/// with the remaining buttons mapped to the most important hotkeys.
	/// </summary>
	/// <remarks>
	/// Left stick / D-pad: in menus the cursor is locked to the buttons and these step between them.
	/// In game they move the cursor over the map (push against a screen edge, the sidebar or the command
	/// bar to scroll). The cursor can't enter the sidebar / command bar on its own.
	/// Right stick: scroll the map (or lists in menus).
	/// A / Cross: select / give orders (left click). Hold and move to drag a selection box, tap twice to select all units of the same type.
	/// B / Circle: menus: back. Map: cancel the current mode (placing a building, selling...) or jump to the command bar.
	/// Command bar: back to the map. Sidebar: right click (hold / cancel production).
	/// X / Square: cycle repair / sell / power down modes.
	/// Y / Triangle: jump the cursor into the sidebar and back to the map.
	/// L1: next build tab. R1 (hold): fast cursor and map scrolling.
	/// L2: select all units on screen. R2: attack-move (then A on the target).
	/// L3: deploy. R3: deselect (right click).
	/// Start: game menu. Back / Select: jump to base.
	/// </remarks>
	sealed class Sdl2GamepadInput
	{
		const float StickDeadzone = 0.22f;
		const float ScrollThreshold = 0.45f;
		const float TriggerThreshold = 0.35f;

		// Cursor speeds are given as a fraction of the effective screen height per second
		const float StickSpeed = 1.6f;
		const float DpadSpeed = 1.2f;
		const float FastMultiplier = 2.2f;

		// The stick response is mostly "digital": any push past the deadzone starts at this fraction
		// of full speed, so the cursor responds immediately and changes direction without lag
		const float StickMinResponse = 0.5f;

		// Holding the d-pad over the interface repeats the snap after this delay / interval
		const float SnapRepeatDelay = 0.35f;
		const float SnapRepeatInterval = 0.12f;

		// Stick deflection needed to scroll the map when the cursor is pushed against a screen edge
		const float EdgePushThreshold = 0.3f;

		// Repeat rate when holding the stick to step between buttons (slow for small pushes, fast for full pushes)
		const float StickSnapSlowInterval = 0.24f;
		const float StickSnapFastInterval = 0.11f;

		// Map scroll speed multiplier while the scroll boost button is held
		const float ScrollBoost = 1.6f;

		// How often the cursor is pulled back onto the nearest button when it rests on the interface
		const float LockCheckInterval = 0.1f;
		const float MenuScrollInterval = 0.12f;

		// Location of the first production icon in the Red Alert sidebar, relative to the top-right corner
		const int SidebarWidth = 250;
		static readonly int2 SidebarIconOffset = new(-SidebarWidth + 42 + 31, 300 + 23);

		readonly Dictionary<int, IntPtr> controllers = [];
		readonly Stopwatch clock = Stopwatch.StartNew();
		readonly Dictionary<SDL.SDL_GameControllerButton, Keycode> buttonKeys = new()
		{
			{ SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X, Keycode.F13 },
			{ SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER, Keycode.PAGEDOWN },
			{ SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START, Keycode.ESCAPE },
			{ SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK, Keycode.H },
			{ SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK, Keycode.F },
		};

		readonly Keycode[] scrollKeys = [Keycode.UP, Keycode.DOWN, Keycode.LEFT, Keycode.RIGHT];
		readonly bool[] scrollKeysHeld = new bool[4];

		double lastTime;
		bool boostHeld;
		bool menuNavActive;
		int2 lastMenuDpad;
		double nextMenuNavTime;
		double nextLockCheckTime;
		double nextMenuScrollTime;
		Vector2 cursor;
		bool cursorInitialized;
		int2 lastSentPosition;
		MouseButton heldButtons;
		bool selectAllHeld;
		bool attackMoveHeld;
		int2? mapCursorBeforeHud;

		enum CircleAction { None, Back, RightClick }
		CircleAction circleAction;

		public bool HasControllers => controllers.Count > 0;

		/// <summary>Keep the virtual cursor in sync with real mouse / touch input.</summary>
		public void SyncCursor(int2 position)
		{
			cursor = new Vector2(position.X, position.Y);
			lastSentPosition = position;
			cursorInitialized = true;
		}

		public bool HandleEvent(in SDL.SDL_Event e, Sdl2PlatformWindow device, IInputHandler inputHandler, Modifiers mods)
		{
			switch (e.type)
			{
				case SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED:
				{
					var controller = SDL.SDL_GameControllerOpen(e.cdevice.which);
					if (controller != IntPtr.Zero)
					{
						var instance = SDL.SDL_JoystickInstanceID(SDL.SDL_GameControllerGetJoystick(controller));
						controllers[instance] = controller;
						GamepadNavigation.Connected = true;
						Log.Write("debug", $"Gamepad connected: {SDL.SDL_GameControllerName(controller)}");
						Console.WriteLine($"Gamepad connected: {SDL.SDL_GameControllerName(controller)}");
						EnsureCursor(device);
					}

					return true;
				}

				case SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED:
				{
					if (controllers.Remove(e.cdevice.which, out var controller))
						SDL.SDL_GameControllerClose(controller);

					if (controllers.Count == 0)
					{
						GamepadNavigation.Connected = false;
						GamepadNavigation.ScrollSpeedMultiplier = 1f;
						ReleaseAll(inputHandler, mods);
					}

					return true;
				}

				case SDL.SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
				case SDL.SDL_EventType.SDL_CONTROLLERBUTTONUP:
				{
					EnsureCursor(device);
					var down = e.type == SDL.SDL_EventType.SDL_CONTROLLERBUTTONDOWN;
					var button = (SDL.SDL_GameControllerButton)e.cbutton.button;

					switch (button)
					{
						case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A:
							SendMouseButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, down, inputHandler, mods);
							break;

						case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B:
							HandleCircle(down, device, inputHandler, mods);
							break;

						case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y:
							if (down && !IsMenuMode())
								ToggleSidebar(device, inputHandler, mods);
							break;

						case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK:
							// Deselect (right click on the map)
							SendMouseButton(MouseButton.Right, (byte)SDL.SDL_BUTTON_RIGHT, down, inputHandler, mods);
							break;

						case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER:
							// Speeds up both the cursor and map scrolling while held
							boostHeld = down;
							GamepadNavigation.ScrollSpeedMultiplier = down ? ScrollBoost : 1f;
							break;

						default:
							if (buttonKeys.TryGetValue(button, out var key))
								SendKey(key, down, inputHandler, mods);
							break;
					}

					return true;
				}

				case SDL.SDL_EventType.SDL_CONTROLLERAXISMOTION:
				case SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMAPPED:
					return true;
			}

			return false;
		}

		public void Update(Sdl2PlatformWindow device, IInputHandler inputHandler, Modifiers mods)
		{
			var now = clock.Elapsed.TotalSeconds;
			var dt = (float)Math.Min(now - lastTime, 0.1);
			lastTime = now;

			if (controllers.Count == 0)
				return;

			EnsureCursor(device);

			// Combine the input from all connected controllers
			var leftStick = Vector2.Zero;
			var rightStick = Vector2.Zero;
			var leftTrigger = 0f;
			var rightTrigger = 0f;
			int dpadX = 0, dpadY = 0;
			foreach (var controller in controllers.Values)
			{
				leftStick += ReadStick(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);
				rightStick += ReadStick(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY);
				leftTrigger = Math.Max(leftTrigger, ReadAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT));
				rightTrigger = Math.Max(rightTrigger, ReadAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT));

				if (IsPressed(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT))
					dpadX = -1;
				if (IsPressed(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT))
					dpadX = 1;
				if (IsPressed(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP))
					dpadY = -1;
				if (IsPressed(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN))
					dpadY = 1;
			}

			var screen = device.EffectiveWindowSize;
			var speedMultiplier = boostHeld ? FastMultiplier : 1f;

			// Game code can ask for the cursor to move (e.g. to the middle of the map when placing a building)
			if (GamepadNavigation.PendingCursorWarp is int2 warp)
			{
				GamepadNavigation.PendingCursorWarp = null;
				cursor = new Vector2(Math.Clamp(warp.X, 0, screen.Width - 1), Math.Clamp(warp.Y, 0, screen.Height - 1));
				SendMotion(inputHandler, mods);
			}

			// Menus: the cursor is locked to the buttons. Free movement is only allowed while
			// A / Cross is held, so that sliders and scrollbars can still be dragged.
			if (heldButtons == MouseButton.None && IsMenuMode())
			{
				NavigateLockedCursor(now, leftStick, new int2(dpadX, dpadY), true, inputHandler, mods);

				// The right stick scrolls lists
				if (Math.Abs(rightStick.Y) > ScrollThreshold && now >= nextMenuScrollTime)
				{
					SendWheel(rightStick.Y < 0 ? 1 : -1, inputHandler, mods);
					nextMenuScrollTime = now + MenuScrollInterval;
				}

				// Release anything held for map scrolling
				for (var i = 0; i < scrollKeysHeld.Length; i++)
					SetScrollKey(i, false, inputHandler, mods);

				return;
			}

			var stickMagnitude = leftStick.Length();
			var dpad = new int2(dpadX, dpadY);
			bool edgeUp = false, edgeDown = false, edgeLeft = false, edgeRight = false;

			if (IsOverInterface())
			{
				// In the sidebar / command bar (entered with Y / Triangle) the cursor is locked to the buttons
				NavigateLockedCursor(now, leftStick, dpad, false, inputHandler, mods);
			}
			else
			{
				menuNavActive = false;
				lastMenuDpad = int2.Zero;

				// On the map the cursor moves freely, but can't enter the sidebar or command bar:
				// they act like screen edges, so pushing into them scrolls the map.
				var velocity = Vector2.Zero;
				if (stickMagnitude > 0)
				{
					var response = StickMinResponse + (1 - StickMinResponse) * Math.Min(stickMagnitude, 1f);
					velocity += leftStick / stickMagnitude * response * StickSpeed * screen.Height;
				}

				if (dpad != int2.Zero)
				{
					// Constant speed so the cursor stops and turns immediately
					var direction = new Vector2(dpad.X, dpad.Y);
					velocity += direction / direction.Length() * DpadSpeed * screen.Height;
				}

				var blocked = int2.Zero;
				if (velocity != Vector2.Zero)
				{
					blocked = MoveWithKeepout(velocity * speedMultiplier * dt, screen);
					SendMotion(inputHandler, mods);
				}

				// Edge scrolling only happens while the cursor is pushed against an edge (or the sidebar / command bar)
				// by the stick or d-pad. The engine's own edge scrolling is disabled so a resting cursor doesn't scroll.
				var pushX = leftStick.X < -EdgePushThreshold || dpad.X < 0 ? -1 : leftStick.X > EdgePushThreshold || dpad.X > 0 ? 1 : 0;
				var pushY = leftStick.Y < -EdgePushThreshold || dpad.Y < 0 ? -1 : leftStick.Y > EdgePushThreshold || dpad.Y > 0 ? 1 : 0;
				edgeUp = pushY < 0 && (cursor.Y <= 0 || blocked.Y < 0);
				edgeDown = pushY > 0 && (cursor.Y >= screen.Height - 1 || blocked.Y > 0);
				edgeLeft = pushX < 0 && (cursor.X <= 0 || blocked.X < 0);
				edgeRight = pushX > 0 && (cursor.X >= screen.Width - 1 || blocked.X > 0);
			}

			// Right stick scrolls the map directly. Both use the keyboard scroll hotkeys.
			SetScrollKey(0, rightStick.Y < -ScrollThreshold || edgeUp, inputHandler, mods);
			SetScrollKey(1, rightStick.Y > ScrollThreshold || edgeDown, inputHandler, mods);
			SetScrollKey(2, rightStick.X < -ScrollThreshold || edgeLeft, inputHandler, mods);
			SetScrollKey(3, rightStick.X > ScrollThreshold || edgeRight, inputHandler, mods);

			// Left trigger selects all units on screen
			var selectAll = leftTrigger > TriggerThreshold;
			if (selectAll != selectAllHeld)
			{
				selectAllHeld = selectAll;
				SendKey(Keycode.Q, selectAll, inputHandler, mods);
			}

			// Right trigger starts an attack-move (then A / Cross on the target)
			var attackMove = rightTrigger > TriggerThreshold;
			if (attackMove != attackMoveHeld)
			{
				attackMoveHeld = attackMove;
				SendKey(Keycode.A, attackMove, inputHandler, mods);
			}
		}

		void EnsureCursor(Sdl2PlatformWindow device)
		{
			if (cursorInitialized)
				return;

			var screen = device.EffectiveWindowSize;
			cursor = new Vector2(screen.Width / 2, screen.Height / 2);
			lastSentPosition = new int2(screen.Width / 2, screen.Height / 2);
			cursorInitialized = true;
		}

		int2 CursorPosition => new((int)cursor.X, (int)cursor.Y);

		static bool IsMenuMode()
		{
			try
			{
				return GamepadNavigation.IsMenuMode();
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
				return false;
			}
		}

		/// <summary>
		/// Locked navigation for menus and the in-game sidebar / command bar: the stick and d-pad step
		/// between buttons and the cursor never rests off a button.
		/// </summary>
		void NavigateLockedCursor(double now, Vector2 leftStick, int2 dpad, bool scrollLists, IInputHandler inputHandler, Modifiers mods)
		{
			// Any stick movement past the deadzone steps between buttons
			var direction = Vector2.Zero;
			var interval = SnapRepeatInterval;
			var stickMagnitude = leftStick.Length();
			if (dpad != int2.Zero)
				direction = new Vector2(dpad.X, dpad.Y);
			else if (stickMagnitude > 0)
			{
				direction = leftStick;
				interval = StickSnapInterval(stickMagnitude);
			}

			if (direction != Vector2.Zero)
			{
				var isNewPress = !menuNavActive || dpad != lastMenuDpad;
				if (isNewPress || now >= nextMenuNavTime)
				{
					NavigateMenu(direction, scrollLists, inputHandler, mods);
					nextMenuNavTime = now + (isNewPress ? SnapRepeatDelay : interval);
				}

				menuNavActive = true;
			}
			else
				menuNavActive = false;

			lastMenuDpad = dpad;

			// Keep the cursor on a button (e.g. after a new menu opens or the list under it scrolls)
			if (direction == Vector2.Zero && heldButtons == MouseButton.None && now >= nextLockCheckTime)
			{
				nextLockCheckTime = now + LockCheckInterval;
				LockToNearestButton(inputHandler, mods);
			}
		}

		static float StickSnapInterval(float stickMagnitude)
		{
			var ramp = Math.Clamp(stickMagnitude, 0, 1);
			return StickSnapSlowInterval + (StickSnapFastInterval - StickSnapSlowInterval) * ramp;
		}

		/// <summary>Moves the cursor onto the nearest button if it isn't already on one.</summary>
		void LockToNearestButton(IInputHandler inputHandler, Modifiers mods)
		{
			try
			{
				var position = CursorPosition;
				if (!GamepadNavigation.IsOnTarget(position) && GamepadNavigation.FindNearestTarget(position) is int2 nearest)
					MoveCursorTo(nearest, inputHandler, mods);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
			}
		}

		void NavigateMenu(Vector2 direction, bool scrollLists, IInputHandler inputHandler, Modifiers mods)
		{
			try
			{
				var target = GamepadNavigation.FindTarget(CursorPosition, direction, false);
				if (target != null)
					MoveCursorTo(target.Value, inputHandler, mods);

				// At the end of a visible list: scroll it so the next items come into view
				else if (scrollLists && Math.Abs(direction.Y) >= Math.Abs(direction.X))
					SendWheel(direction.Y < 0 ? 1 : -1, inputHandler, mods);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
			}
		}

		void MoveCursorTo(int2 position, IInputHandler inputHandler, Modifiers mods)
		{
			cursor = new Vector2(position.X, position.Y);
			SendMotion(inputHandler, mods);
		}

		void SendWheel(int delta, IInputHandler inputHandler, Modifiers mods)
		{
			inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, CursorPosition, new int2(0, delta), mods, 0));
		}

		bool IsOverInterface()
		{
			try
			{
				return GamepadNavigation.IsOverInterface(CursorPosition);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
				return false;
			}
		}

		static bool IsInterfaceAt(Vector2 position)
		{
			try
			{
				return GamepadNavigation.IsOverInterface(new int2((int)position.X, (int)position.Y));
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
				return false;
			}
		}

		/// <summary>
		/// Moves the map cursor, stopping at the screen edges and at the sidebar / command bar.
		/// Returns the direction on each axis in which the movement was blocked by the interface.
		/// </summary>
		int2 MoveWithKeepout(Vector2 delta, Size screen)
		{
			var blocked = int2.Zero;
			var x = Math.Clamp(cursor.X + delta.X, 0, screen.Width - 1);
			if (x != cursor.X && IsInterfaceAt(new Vector2(x, cursor.Y)))
			{
				x = FurthestFreePosition(cursor.X, x, v => new Vector2(v, cursor.Y));
				blocked = new int2(Math.Sign(delta.X), 0);
			}

			var y = Math.Clamp(cursor.Y + delta.Y, 0, screen.Height - 1);
			if (y != cursor.Y && IsInterfaceAt(new Vector2(x, y)))
			{
				y = FurthestFreePosition(cursor.Y, y, v => new Vector2(x, v));
				blocked = new int2(blocked.X, Math.Sign(delta.Y));
			}

			cursor = new Vector2(x, y);
			return blocked;
		}

		/// <summary>Binary search between a free and a blocked coordinate for the closest free position to the interface.</summary>
		static float FurthestFreePosition(float free, float blocked, Func<float, Vector2> toPosition)
		{
			for (var i = 0; i < 8; i++)
			{
				var mid = (free + blocked) / 2;
				if (IsInterfaceAt(toPosition(mid)))
					blocked = mid;
				else
					free = mid;
			}

			return free;
		}

		void SendMotion(IInputHandler inputHandler, Modifiers mods)
		{
			var position = CursorPosition;
			if (position == lastSentPosition)
				return;

			var delta = position - lastSentPosition;
			lastSentPosition = position;
			inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, heldButtons, position, delta, mods, 0));
		}

		void SendMouseButton(MouseButton button, byte sdlButton, bool down, IInputHandler inputHandler, Modifiers mods)
		{
			// Ignore repeated presses (e.g. the same button on two controllers)
			if (down == heldButtons.HasFlag(button))
				return;

			var position = CursorPosition;
			if (down)
			{
				heldButtons |= button;
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, button, position, int2.Zero, mods,
					MultiTapDetection.DetectFromMouse(sdlButton, position)));
			}
			else
			{
				heldButtons &= ~button;
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, button, position, int2.Zero, mods,
					MultiTapDetection.InfoFromMouse(sdlButton)));
			}
		}

		static void SendKey(Keycode key, bool down, IInputHandler inputHandler, Modifiers mods)
		{
			inputHandler.OnKeyInput(new KeyInput
			{
				Event = down ? KeyInputEvent.Down : KeyInputEvent.Up,
				Key = key,
				Modifiers = mods,
				UnicodeChar = (char)key,
				MultiTapCount = down ? MultiTapDetection.DetectFromKeyboard(key, mods) : MultiTapDetection.InfoFromKeyboard(key, mods),
				IsRepeat = false
			});
		}

		void SetScrollKey(int index, bool held, IInputHandler inputHandler, Modifiers mods)
		{
			if (scrollKeysHeld[index] == held)
				return;

			scrollKeysHeld[index] = held;
			SendKey(scrollKeys[index], held, inputHandler, mods);
		}

		/// <summary>
		/// B / Circle depends on context: back in menus, cancel the current mode or open the command bar on the map,
		/// return to the map from the command bar, and right click (hold / cancel production) in the sidebar.
		/// </summary>
		void HandleCircle(bool down, Sdl2PlatformWindow device, IInputHandler inputHandler, Modifiers mods)
		{
			if (!down)
			{
				if (circleAction == CircleAction.Back)
					SendKey(Keycode.ESCAPE, false, inputHandler, mods);
				else if (circleAction == CircleAction.RightClick)
					SendMouseButton(MouseButton.Right, (byte)SDL.SDL_BUTTON_RIGHT, false, inputHandler, mods);

				circleAction = CircleAction.None;
				return;
			}

			if (IsMenuMode())
			{
				circleAction = CircleAction.Back;
				SendKey(Keycode.ESCAPE, true, inputHandler, mods);
				return;
			}

			circleAction = CircleAction.None;
			var zone = ZoneAt(CursorPosition);
			if (zone == "commandbar")
				ReturnToMap(device, inputHandler, mods);
			else if (zone != null || IsOverInterface() || IsOrderModeActive())
			{
				// Sidebar: hold / cancel production. Map: cancel building placement, sell mode, etc.
				circleAction = CircleAction.RightClick;
				SendMouseButton(MouseButton.Right, (byte)SDL.SDL_BUTTON_RIGHT, true, inputHandler, mods);
			}
			else
				JumpToZone("commandbar", inputHandler, mods);
		}

		/// <summary>Y / Triangle: jump into the sidebar, or back to the map.</summary>
		void ToggleSidebar(Sdl2PlatformWindow device, IInputHandler inputHandler, Modifiers mods)
		{
			if (ZoneAt(CursorPosition) == "sidebar")
				ReturnToMap(device, inputHandler, mods);
			else
				JumpToZone("sidebar", inputHandler, mods);
		}

		void JumpToZone(string zone, IInputHandler inputHandler, Modifiers mods)
		{
			try
			{
				// Aim for the first build icon in the sidebar, or the nearest command bar button
				var screen = Game.Renderer.Resolution;
				var aim = zone == "sidebar" ? new int2(screen.Width, 0) + SidebarIconOffset : new int2(0, screen.Height);
				var target = GamepadNavigation.NearestTargetInZone(zone, aim);
				if (target == null)
					return;

				if (!IsOverInterface())
					mapCursorBeforeHud = CursorPosition;

				MoveCursorTo(target.Value, inputHandler, mods);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
			}
		}

		void ReturnToMap(Sdl2PlatformWindow device, IInputHandler inputHandler, Modifiers mods)
		{
			var screen = device.EffectiveWindowSize;
			var target = mapCursorBeforeHud ?? new int2(screen.Width / 2, screen.Height / 2);
			mapCursorBeforeHud = null;
			MoveCursorTo(target, inputHandler, mods);
		}

		static string ZoneAt(int2 position)
		{
			try
			{
				return GamepadNavigation.ZoneAt(position);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
				return null;
			}
		}

		static bool IsOrderModeActive()
		{
			try
			{
				return GamepadNavigation.IsOrderModeActive();
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Gamepad navigation failed: {e}");
				return false;
			}
		}

		void ReleaseAll(IInputHandler inputHandler, Modifiers mods)
		{
			if (heldButtons.HasFlag(MouseButton.Left))
				SendMouseButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, false, inputHandler, mods);

			if (heldButtons.HasFlag(MouseButton.Right))
				SendMouseButton(MouseButton.Right, (byte)SDL.SDL_BUTTON_RIGHT, false, inputHandler, mods);

			for (var i = 0; i < scrollKeysHeld.Length; i++)
				SetScrollKey(i, false, inputHandler, mods);

			boostHeld = false;
			GamepadNavigation.ScrollSpeedMultiplier = 1f;
			if (selectAllHeld)
			{
				selectAllHeld = false;
				SendKey(Keycode.Q, false, inputHandler, mods);
			}

			if (attackMoveHeld)
			{
				attackMoveHeld = false;
				SendKey(Keycode.A, false, inputHandler, mods);
			}

			if (circleAction == CircleAction.Back)
				SendKey(Keycode.ESCAPE, false, inputHandler, mods);

			circleAction = CircleAction.None;
		}

		static float ReadAxis(IntPtr controller, SDL.SDL_GameControllerAxis axis)
		{
			return Math.Clamp(SDL.SDL_GameControllerGetAxis(controller, axis) / 32767f, -1f, 1f);
		}

		static Vector2 ReadStick(IntPtr controller, SDL.SDL_GameControllerAxis xAxis, SDL.SDL_GameControllerAxis yAxis)
		{
			var stick = new Vector2(ReadAxis(controller, xAxis), ReadAxis(controller, yAxis));
			var magnitude = stick.Length();
			if (magnitude < StickDeadzone)
				return Vector2.Zero;

			// Rescale so that output starts from zero at the edge of the deadzone
			var scaled = Math.Min((magnitude - StickDeadzone) / (1 - StickDeadzone), 1f);
			return stick / magnitude * scaled;
		}

		static bool IsPressed(IntPtr controller, SDL.SDL_GameControllerButton button)
		{
			return SDL.SDL_GameControllerGetButton(controller, button) != 0;
		}
	}
}
