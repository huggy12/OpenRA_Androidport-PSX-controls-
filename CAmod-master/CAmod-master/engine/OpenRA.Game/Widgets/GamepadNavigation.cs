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
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Widgets
{
	/// <summary>
	/// Console-style directional navigation for gamepads: finds the button (or other interactive
	/// widget area) that the cursor should snap to when a direction is pressed.
	/// </summary>
	public static class GamepadNavigation
	{
		// Targets must lie within this angle of the requested direction (tan 60°)
		const float MaxSlope = 1.73f;

		// Weight applied to sideways offset when choosing between targets
		const int PerpendicularWeight = 3;

		// How far beyond the current button to look for the world view when leaving the UI
		const int ExitProbeDistance = 24;

		/// <summary>True while a gamepad is connected (set by the platform layer).</summary>
		public static bool Connected;

		/// <summary>True when the last pointer input came from touch controls (hides the software cursor and edge scrolling).</summary>
		public static bool TouchActive;

		/// <summary>The gamepad zone that holds the support power (special abilities) icons.</summary>
		public const string SupportPowersZone = "supportpowers";

		/// <summary>Multiplier applied to keyboard/edge map scrolling (the gamepad's scroll boost button).</summary>
		public static float ScrollSpeedMultiplier = 1f;

		/// <summary>A cursor position requested by game code, applied by the platform layer when a gamepad is connected.</summary>
		public static int2? PendingCursorWarp;

		// Zones closer than this to a screen edge are extended to the edge, so the cursor can't slip behind them
		const int ZoneEdgeSnap = 24;

		static readonly Dictionary<string, Rectangle> Zones = [];
		static long zonesTime = -1;

		/// <summary>True while a building placement, sell, repair, attack-move (etc.) mode is active.</summary>
		public static bool IsOrderModeActive()
		{
			var world = Game.OrderManager?.World;
			return world != null && world.Type == WorldType.Regular && !world.IsDefaultInputMode;
		}

		/// <summary>The HUD zones (e.g. "sidebar", "commandbar") that the free gamepad cursor must stay out of.</summary>
		static Dictionary<string, Rectangle> GetZones()
		{
			// Recalculate at most once per frame
			if (zonesTime == Game.RunTime)
				return Zones;

			zonesTime = Game.RunTime;
			Zones.Clear();
			CollectZones(Ui.Root);

			var screen = Game.Renderer.Resolution;
			foreach (var zone in new List<string>(Zones.Keys))
			{
				var r = Zones[zone];
				var left = r.Left <= ZoneEdgeSnap ? 0 : r.Left;
				var top = r.Top <= ZoneEdgeSnap ? 0 : r.Top;
				var right = r.Right >= screen.Width - ZoneEdgeSnap ? screen.Width : r.Right;
				var bottom = r.Bottom >= screen.Height - ZoneEdgeSnap ? screen.Height : r.Bottom;
				Zones[zone] = Rectangle.FromLTRB(left, top, right, bottom);
			}

			return Zones;
		}

		static void CollectZones(Widget widget)
		{
			if (!widget.IsVisible())
				return;

			var bounds = widget.GamepadZone != null ? widget.GamepadZoneBounds : Rectangle.Empty;
			if (bounds.Width > 0 && bounds.Height > 0)
			{
				Zones[widget.GamepadZone] = Zones.TryGetValue(widget.GamepadZone, out var existing) ? Rectangle.Union(existing, bounds) : bounds;
			}

			foreach (var child in widget.Children)
				CollectZones(child);
		}

		/// <summary>Returns the HUD zone containing the position, or null.</summary>
		public static string ZoneAt(int2 position)
		{
			foreach (var kv in GetZones())
				if (kv.Value.Contains(position))
					return kv.Key;

			return null;
		}

		/// <summary>True if the cursor is inside the widget's zone (or bounds), i.e. the player is using it.</summary>
		public static bool IsWidgetActive(Widget widget)
		{
			var position = Viewport.LastMousePos;
			if (widget.GamepadZone != null && GetZones().TryGetValue(widget.GamepadZone, out var zone))
				return zone.Contains(position);

			return widget.RenderBounds.Contains(position);
		}

		/// <summary>Finds the button inside a zone that is closest to the given position.</summary>
		/// <summary>Returns the scrollable list (e.g. the build palette) under the position, or null.</summary>
		public static Widget ScrollListAt(int2 position)
		{
			var hit = HitTest(Ui.Root, position);
			return hit != null && hit.ScrollListRowSize > 0 ? hit : null;
		}

		/// <summary>
		/// Gamepad: if the cursor is on the first or last visible row of a scrollable list and steps past it,
		/// scrolls the list instead (the cursor stays put and the next items move under it). Returns true if it scrolled.
		/// </summary>
		public static bool TryScrollListAt(int2 position, Vector2 direction)
		{
			if (direction == Vector2.Zero)
				return false;

			var list = ScrollListAt(position);
			if (list == null)
				return false;

			var step = Math.Abs(direction.Y) >= Math.Abs(direction.X)
				? new int2(0, Math.Sign(direction.Y))
				: new int2(Math.Sign(direction.X), 0);

			return list.ShouldGamepadScrollList(position, step) && list.ScrollListRows(step.X + step.Y);
		}

		public static int2? NearestTargetInZone(string zone, int2 from)
		{
			if (!GetZones().TryGetValue(zone, out var bounds))
				return null;

			int2? best = null;
			var bestDistance = int.MaxValue;
			foreach (var t in CollectTargets())
			{
				if (!bounds.Contains(t.Center))
					continue;

				var distance = (t.Center - from).LengthSquared;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = t.Center;
				}
			}

			return best;
		}

		/// <summary>Asks the platform to move the gamepad cursor (ignored when no gamepad is connected).</summary>
		public static void RequestCursorWarp(int2 position)
		{
			if (Connected)
				PendingCursorWarp = position;
		}

		readonly struct Target
		{
			public readonly Rectangle Bounds;
			public readonly Widget Owner;

			public Target(Rectangle bounds, Widget owner)
			{
				Bounds = bounds;
				Owner = owner;
			}

			public int2 Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
		}

		/// <summary>True if the position is over the in-game world view (not a menu, HUD panel or the radar).</summary>
		public static bool IsWorldViewAt(int2 position)
		{
			var world = Game.OrderManager?.World;
			return world != null && world.Type == WorldType.Regular && ZoneAt(position) == null && IsFreeCursorArea(position);
		}

		/// <summary>Touch controls: moves the camera if the position is on a minimap. Returns false for anything else.</summary>
		public static bool TouchMoveViewAt(int2 position)
		{
			var hit = HitTest(Ui.Root, position);
			return hit != null && hit.TouchMoveView(position);
		}

		/// <summary>Scrolls the in-game world view by the given number of (effective) screen pixels.</summary>
		public static void ScrollWorldView(Vector2 delta)
		{
			Game.CurrentWorldRenderer?.Viewport.Scroll(new float2(delta.X, delta.Y), false);
		}

		/// <summary>True if the cursor is over an area (e.g. the world view or radar) that uses free cursor movement.</summary>
		public static bool IsFreeCursorArea(int2 position)
		{
			var hit = HitTest(Ui.Root, position);
			return hit != null && hit.GamepadFreeCursor;
		}

		/// <summary>
		/// True when a menu (rather than the world view) occupies the middle of the screen.
		/// In menus the gamepad cursor is locked to the buttons.
		/// </summary>
		public static bool IsMenuMode()
		{
			var center = new int2(Game.Renderer.Resolution.Width / 2, Game.Renderer.Resolution.Height / 2);
			var hit = HitTest(Ui.Root, center);
			return hit == null || !hit.GamepadFreeCursor;
		}

		/// <summary>True if the position is over a button (or other navigation target).</summary>
		public static bool IsOnTarget(int2 position)
		{
			foreach (var t in CollectTargets())
				if (t.Bounds.Contains(position))
					return true;

			return false;
		}

		/// <summary>True if the cursor is over a snappable target or a widget that isn't a free cursor area.</summary>
		public static bool IsOverInterface(int2 position)
		{
			if (ZoneAt(position) != null)
				return true;

			var hit = HitTest(Ui.Root, position);
			return hit == null || !hit.GamepadFreeCursor;
		}

		/// <summary>
		/// Finds the position to move the cursor to when pressing the given direction.
		/// Returns null if the cursor should instead move freely (no target, or leaving the UI into the world view).
		/// </summary>
		public static int2? FindTarget(int2 from, Vector2 direction, bool allowExitToWorld = true, string withinZone = null)
		{
			if (direction == Vector2.Zero)
				return null;

			direction = Vector2.Normalize(direction);

			var targets = CollectTargets();

			// Optionally keep navigation inside one HUD zone (e.g. the support powers bar)
			if (withinZone != null && GetZones().TryGetValue(withinZone, out var zoneBounds))
				targets.RemoveAll(t => !zoneBounds.Contains(t.Center));

			Target? current = null;
			foreach (var t in targets)
				if (t.Bounds.Contains(from))
					current = t;

			// Moving off the edge of the UI into the world view (e.g. left from the sidebar) frees the cursor
			var probeOrigin = current?.Bounds ?? new Rectangle(from.X, from.Y, 0, 0);
			var probe = EdgePoint(probeOrigin, from, direction) + new int2((int)(direction.X * ExitProbeDistance), (int)(direction.Y * ExitProbeDistance));
			var probeIsWorld = allowExitToWorld && IsFreeCursorArea(probe);

			Target? best = null;
			var bestScore = int.MaxValue;
			foreach (var t in targets)
			{
				if (current.HasValue && t.Owner == current.Value.Owner && t.Bounds == current.Value.Bounds)
					continue;

				if (t.Bounds.Contains(from))
					continue;

				var offset = t.Center - from;
				var along = offset.X * direction.X + offset.Y * direction.Y;
				var across = Math.Abs(offset.X * direction.Y - offset.Y * direction.X);
				if (along <= 0 || across > along * MaxSlope)
					continue;

				var score = (int)(along + PerpendicularWeight * across);
				if (score < bestScore)
				{
					bestScore = score;
					best = t;
				}
			}

			if (best == null)
				return null;

			// Prefer moving into the world view over jumping across it to a distant button
			if (probeIsWorld && !best.Value.Bounds.Contains(probe))
				return null;

			return best.Value.Center;
		}

		/// <summary>Finds the target closest to the given position, if any are visible.</summary>
		public static int2? FindNearestTarget(int2 from)
		{
			int2? best = null;
			var bestDistance = int.MaxValue;
			foreach (var t in CollectTargets())
			{
				var distance = (t.Center - from).LengthSquared;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = t.Center;
				}
			}

			return best;
		}

		static int2 EdgePoint(Rectangle bounds, int2 from, Vector2 direction)
		{
			// Ignore small components of diagonal stick directions
			const float AxisThreshold = 0.38f;
			var x = direction.X > AxisThreshold ? bounds.Right : direction.X < -AxisThreshold ? bounds.Left : from.X;
			var y = direction.Y > AxisThreshold ? bounds.Bottom : direction.Y < -AxisThreshold ? bounds.Top : from.Y;
			return new int2(x, y);
		}

		static List<Target> CollectTargets()
		{
			var result = new List<Target>();
			var rects = new List<Rectangle>();
			var screen = new Rectangle(0, 0, Game.Renderer.Resolution.Width, Game.Renderer.Resolution.Height);
			Collect(Ui.Root, rects, result, screen);
			return result;
		}

		static void Collect(Widget widget, List<Rectangle> rects, List<Target> result, Rectangle screen)
		{
			if (!widget.IsVisible())
				return;

			rects.Clear();
			widget.GetGamepadTargets(rects);
			foreach (var r in rects)
			{
				if (r.Width <= 0 || r.Height <= 0 || !screen.IntersectsWith(r))
					continue;

				// Only consider targets that would actually receive a click at their center
				// (i.e. not hidden behind a dialog or scrolled out of view).
				var target = new Target(r, widget);
				if (screen.Contains(target.Center) && HitTest(Ui.Root, target.Center) == widget)
					result.Add(target);
			}

			foreach (var child in widget.Children)
				Collect(child, rects, result, screen);
		}

		/// <summary>Returns the topmost widget that consumes mouse input at the given position.</summary>
		static Widget HitTest(Widget widget, int2 position)
		{
			if (!widget.IsVisible() || !widget.EventBoundsContains(position))
				return null;

			for (var i = widget.Children.Count - 1; i >= 0; i--)
			{
				var hit = HitTest(widget.Children[i], position);
				if (hit != null)
					return hit;
			}

			return widget.GamepadBlocksInput && widget.EventBounds.Contains(position) ? widget : null;
		}
	}
}
