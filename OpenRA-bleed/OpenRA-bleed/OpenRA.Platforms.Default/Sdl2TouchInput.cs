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
using OpenRA.Widgets;
using SDL2;

namespace OpenRA.Platforms.Default
{
	/// <summary>
	/// Touch screen gestures, enabled by the TouchControls setting.
	/// </summary>
	/// <remarks>
	/// On the map: one finger tap selects units or gives orders (move, attack, deploy...), tap and hold attack-moves
	/// the selected units to that spot, one finger drag scrolls the map, and two fingers drag a selection box
	/// (from the first finger to the second).
	/// On menus and the HUD: a tap is a click and a drag is a mouse drag (sliders, scrollbars).
	/// Holding a finger on the build sidebar is a right click (hold / cancel production).
	/// While disabled, SDL's default touch handling (a tap is a left click) is left untouched.
	/// </remarks>
	sealed class Sdl2TouchInput
	{
		// How far (in effective screen pixels) a finger can move before a tap becomes a drag
		const float TapSlop = 10f;

		// How long a finger must rest in place for a tap and hold
		const double HoldTime = 0.5;

		// Where the (invisible) pointer goes when the last finger lifts, so nothing stays hovered and no tooltips linger
		static readonly int2 LiftedPosition = new(-10000, -10000);

		enum Gesture { None, Pending, Pan, Drag, Box, MoveView, Finished }

		readonly Dictionary<long, Vector2> fingers = [];
		readonly Stopwatch clock = Stopwatch.StartNew();

		Gesture gesture;
		long primaryFinger;
		long boxFinger;
		Vector2 start;
		double startTime;
		bool startOnWorld;
		MouseButton heldButtons;
		int2 lastSentPosition;

		// The last on-screen pointer position, which the gamepad cursor follows
		int2 lastTouchedPosition;

		static bool Enabled => Game.Settings?.Game?.TouchControls ?? false;

		/// <summary>
		/// Handles touch events (and drops SDL's emulated mouse events for touches) while touch controls are enabled.
		/// Returns true if the event was consumed.
		/// </summary>
		public bool HandleEvent(in SDL.SDL_Event e, Sdl2PlatformWindow device, IInputHandler inputHandler, Modifiers mods, Action<int2> syncCursor)
		{
			switch (e.type)
			{
				case SDL.SDL_EventType.SDL_FINGERDOWN:
				case SDL.SDL_EventType.SDL_FINGERMOTION:
				case SDL.SDL_EventType.SDL_FINGERUP:
				{
					if (!Enabled)
						return false;

					var screen = device.EffectiveWindowSize;
					var position = new Vector2(
						Math.Clamp(e.tfinger.x, 0f, 1f) * (screen.Width - 1),
						Math.Clamp(e.tfinger.y, 0f, 1f) * (screen.Height - 1));

					GamepadNavigation.TouchActive = true;
					try
					{
						if (e.type == SDL.SDL_EventType.SDL_FINGERDOWN)
							FingerDown(e.tfinger.fingerId, position, inputHandler, mods);
						else if (e.type == SDL.SDL_EventType.SDL_FINGERMOTION)
							FingerMotion(e.tfinger.fingerId, position, inputHandler, mods);
						else
							FingerUp(e.tfinger.fingerId, position, inputHandler, mods);
					}
					catch (Exception ex)
					{
						Log.Write("debug", $"Touch input failed: {ex}");
					}

					// Keep the gamepad cursor where the player last touched
					syncCursor(lastTouchedPosition);
					return true;
				}

				// SDL also emulates the mouse for touches; those events would duplicate the gestures
				case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
				case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
					return Enabled && e.button.which == SDL.SDL_TOUCH_MOUSEID;

				case SDL.SDL_EventType.SDL_MOUSEMOTION:
					return Enabled && e.motion.which == SDL.SDL_TOUCH_MOUSEID;

				case SDL.SDL_EventType.SDL_MOUSEWHEEL:
					return Enabled && e.wheel.which == SDL.SDL_TOUCH_MOUSEID;
			}

			return false;
		}

		public void Update(IInputHandler inputHandler, Modifiers mods)
		{
			if (!Enabled)
			{
				if (gesture != Gesture.None || fingers.Count > 0)
					Cancel(inputHandler, mods);

				return;
			}

			// One tap both selects and gives orders, which needs the Classic mouse style
			var gameSettings = Game.Settings?.Game;
			if (gameSettings != null && gameSettings.MouseControlStyle != MouseControlStyle.Classic)
			{
				gameSettings.MouseControlStyle = MouseControlStyle.Classic;
				Log.Write("debug", "Touch controls enabled: switched mouse control style to Classic");
			}

			if (gesture != Gesture.Pending || clock.Elapsed.TotalSeconds - startTime < HoldTime)
				return;

			try
			{
				var position = ToInt2(start);
				if (startOnWorld)
				{
					// Tap and hold: attack-move to this spot (a plain click while placing a building, targeting a power, etc.)
					gesture = Gesture.Finished;
					if (!GamepadNavigation.IsOrderModeActive())
					{
						SendKey(Keycode.A, true, inputHandler, mods);
						SendKey(Keycode.A, false, inputHandler, mods);
					}

					Click(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, position, inputHandler, mods);
				}
				else if (GamepadNavigation.ZoneAt(position) == "sidebar")
				{
					// Hold / cancel production
					gesture = Gesture.Finished;
					Click(MouseButton.Right, (byte)SDL.SDL_BUTTON_RIGHT, position, inputHandler, mods);
				}

				// Elsewhere a long press is still a normal click when the finger lifts
			}
			catch (Exception ex)
			{
				Log.Write("debug", $"Touch input failed: {ex}");
			}
		}

		void FingerDown(long id, Vector2 position, IInputHandler inputHandler, Modifiers mods)
		{
			fingers[id] = position;

			if (fingers.Count == 1)
			{
				primaryFinger = id;

				// Minimap: move the camera to the touched spot (and follow the finger), without clicking.
				// The cursor stays where it was, so a connected gamepad doesn't snap it onto the sidebar.
				if (GamepadNavigation.TouchMoveViewAt(ToInt2(position)))
				{
					gesture = Gesture.MoveView;
					startOnWorld = false;
					return;
				}

				gesture = Gesture.Pending;
				start = position;
				startTime = clock.Elapsed.TotalSeconds;
				startOnWorld = GamepadNavigation.IsWorldViewAt(ToInt2(position));

				// Hover first, so tooltips and the order under the finger are up to date
				SendMove(ToInt2(position), inputHandler, mods);
				return;
			}

			// A second finger on the map starts a selection box from the first finger to the second
			if (fingers.Count == 2 && startOnWorld && (gesture == Gesture.Pending || gesture == Gesture.Pan)
				&& fingers.TryGetValue(primaryFinger, out var anchor))
			{
				gesture = Gesture.Box;
				boxFinger = id;
				SendMove(ToInt2(anchor), inputHandler, mods);
				SendButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, true, ToInt2(anchor), inputHandler, mods);
				SendMove(ToInt2(position), inputHandler, mods);
			}
		}

		void FingerMotion(long id, Vector2 position, IInputHandler inputHandler, Modifiers mods)
		{
			if (!fingers.TryGetValue(id, out var previous))
				return;

			fingers[id] = position;

			switch (gesture)
			{
				case Gesture.Pending when id == primaryFinger:
					if (Vector2.Distance(position, start) <= TapSlop)
						break;

					if (startOnWorld)
					{
						// One finger drag scrolls the map (including the distance moved before the drag was recognised)
						gesture = Gesture.Pan;
						GamepadNavigation.ScrollWorldView(start - position);
					}
					else
					{
						// Drag sliders, scrollbars, etc.
						gesture = Gesture.Drag;
						SendButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, true, ToInt2(start), inputHandler, mods);
						SendMove(ToInt2(position), inputHandler, mods);
					}

					break;

				case Gesture.Pan when id == primaryFinger:
					GamepadNavigation.ScrollWorldView(previous - position);
					break;

				case Gesture.MoveView when id == primaryFinger:
					GamepadNavigation.TouchMoveViewAt(ToInt2(position));
					break;

				case Gesture.Drag when id == primaryFinger:
				case Gesture.Box when id == boxFinger:
					SendMove(ToInt2(position), inputHandler, mods);
					break;
			}
		}

		void FingerUp(long id, Vector2 position, IInputHandler inputHandler, Modifiers mods)
		{
			if (!fingers.Remove(id))
				return;

			switch (gesture)
			{
				case Gesture.Pending when id == primaryFinger:
					// Tap: select units or give an order. Two quick taps select all units of that type.
					gesture = Gesture.Finished;
					Click(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, ToInt2(start), inputHandler, mods);
					break;

				case Gesture.Pan when id == primaryFinger:
				case Gesture.MoveView when id == primaryFinger:
					gesture = Gesture.Finished;
					break;

				case Gesture.Drag when id == primaryFinger:
					gesture = Gesture.Finished;
					SendMove(ToInt2(position), inputHandler, mods);
					SendButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, false, ToInt2(position), inputHandler, mods);
					break;

				case Gesture.Box when id == primaryFinger || id == boxFinger:
					// Lifting either finger completes the selection box
					gesture = Gesture.Finished;
					SendButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, false, lastSentPosition, inputHandler, mods);
					break;
			}

			if (fingers.Count == 0)
			{
				gesture = Gesture.None;

				// Lift the pointer off the screen, clearing hover highlights and tooltips
				SendMove(LiftedPosition, inputHandler, mods);
			}
		}

		void Cancel(IInputHandler inputHandler, Modifiers mods)
		{
			if (heldButtons.HasFlag(MouseButton.Left))
				SendButton(MouseButton.Left, (byte)SDL.SDL_BUTTON_LEFT, false, lastSentPosition, inputHandler, mods);

			if (heldButtons.HasFlag(MouseButton.Right))
				SendButton(MouseButton.Right, (byte)SDL.SDL_BUTTON_RIGHT, false, lastSentPosition, inputHandler, mods);

			fingers.Clear();
			gesture = Gesture.None;
		}

		static int2 ToInt2(Vector2 v) => new((int)v.X, (int)v.Y);

		void Click(MouseButton button, byte sdlButton, int2 position, IInputHandler inputHandler, Modifiers mods)
		{
			SendMove(position, inputHandler, mods);
			SendButton(button, sdlButton, true, position, inputHandler, mods);
			SendButton(button, sdlButton, false, position, inputHandler, mods);
		}

		void SendMove(int2 position, IInputHandler inputHandler, Modifiers mods)
		{
			if (position == lastSentPosition)
				return;

			var delta = position - lastSentPosition;
			lastSentPosition = position;
			if (position != LiftedPosition)
				lastTouchedPosition = position;

			inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, heldButtons, position, delta, mods, 0));
		}

		void SendButton(MouseButton button, byte sdlButton, bool down, int2 position, IInputHandler inputHandler, Modifiers mods)
		{
			if (down == heldButtons.HasFlag(button))
				return;

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
	}
}
