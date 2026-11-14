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
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class ClassicProductionLogic : ChromeLogic
	{
		readonly ProductionPaletteWidget palette;
		readonly World world;

		void SetupProductionGroupButton(ProductionTypeButtonWidget button)
		{
			if (button == null)
				return;

			// Classic production queues are initialized at game start, and then never change.
			var queues = world.LocalPlayer.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => (q.Info.Group ?? q.Info.Type) == button.ProductionGroup)
				.ToArray();

			void SelectTab(bool reverse)
			{
				palette.CurrentQueue = queues.FirstOrDefault(q => q.Enabled);

				// When a tab is selected, scroll to the top because the current row position may be invalid for the new tab
				palette.ScrollToTop();

				// Attempt to pick up a completed building (if there is one) so it can be placed
				palette.PickUpCompletedBuilding();
			}

			button.IsDisabled = () => !queues.Any(q => q.AnyItemsToBuild());
			button.OnMouseUp = mi => SelectTab(mi.Modifiers.HasModifier(Modifiers.Shift));
			button.OnKeyPress = e => SelectTab(e.Modifiers.HasModifier(Modifiers.Shift));
			button.OnClick = () => SelectTab(false);
			button.IsHighlighted = () => queues.Contains(palette.CurrentQueue);

			var chromeName = button.ProductionGroup.ToLowerInvariant();
			var icon = button.Get<ImageWidget>("ICON");
			icon.GetImageName = () => button.IsDisabled() ? chromeName + "-disabled" :
				queues.Any(q => q.AllQueued().Any(i => i.Done)) ? chromeName + "-alert" : chromeName;
		}

		[ObjectCreator.UseCtor]
		public ClassicProductionLogic(Widget widget, World world, ModData modData, Dictionary<string, MiniYaml> logicArgs)
		{
			this.world = world;
			palette = widget.Get<ProductionPaletteWidget>("PRODUCTION_PALETTE");

			var background = widget.GetOrNull("PALETTE_BACKGROUND");
			var foreground = widget.GetOrNull("PALETTE_FOREGROUND");
			if (background != null || foreground != null)
			{
				Widget backgroundTemplate = null;
				Widget backgroundBottom = null;
				Widget foregroundTemplate = null;

				if (background != null)
				{
					backgroundTemplate = background.Get("ROW_TEMPLATE");
					backgroundBottom = background.GetOrNull("BOTTOM_CAP");
				}

				if (foreground != null)
					foregroundTemplate = foreground.Get("ROW_TEMPLATE");

				void UpdateBackground(int _, int icons)
				{
					var rows = Math.Max(palette.MinimumRows, (icons + palette.Columns - 1) / palette.Columns);
					rows = Math.Min(rows, palette.MaximumRows);

					if (background != null)
					{
						background.RemoveChildren();

						var rowHeight = backgroundTemplate.Bounds.Height;
						for (var i = 0; i < rows; i++)
						{
							var row = backgroundTemplate.Clone();
							row.Bounds.Y = i * rowHeight;
							background.AddChild(row);
						}

						if (backgroundBottom == null)
							return;

						backgroundBottom.Bounds.Y = rows * rowHeight;
						background.AddChild(backgroundBottom);
					}

					if (foreground != null)
					{
						foreground.RemoveChildren();

						var rowHeight = foregroundTemplate.Bounds.Height;
						for (var i = 0; i < rows; i++)
						{
							var row = foregroundTemplate.Clone();
							row.Bounds.Y = i * rowHeight;
							foreground.AddChild(row);
						}
					}
				}

				palette.OnIconCountChanged += UpdateBackground;

				// Set the initial palette state
				UpdateBackground(0, 0);
			}

			var typesContainer = widget.Get("PRODUCTION_TYPES");
			foreach (var i in typesContainer.Children)
				SetupProductionGroupButton(i as ProductionTypeButtonWidget);

			// Optional hotkeys (used by gamepad shoulder buttons) to cycle between the production type tabs
			var keyhandler = widget.GetOrNull<LogicKeyListenerWidget>("PRODUCTION_KEYHANDLER");
			if (keyhandler != null)
			{
				var previousKey = new HotkeyReference();
				var nextKey = new HotkeyReference();
				if (logicArgs.TryGetValue("PreviousProductionTabKey", out var previousYaml))
					previousKey = modData.Hotkeys[previousYaml.Value];

				if (logicArgs.TryGetValue("NextProductionTabKey", out var nextYaml))
					nextKey = modData.Hotkeys[nextYaml.Value];

				keyhandler.AddHandler(e =>
				{
					if (e.Event != KeyInputEvent.Down)
						return false;

					if (previousKey.IsActivatedBy(e))
						return CycleTab(typesContainer, true);

					if (nextKey.IsActivatedBy(e))
						return CycleTab(typesContainer, false);

					return false;
				});
			}

			var ticker = widget.Get<LogicTickerWidget>("PRODUCTION_TICKER");
			ticker.OnTick = () =>
			{
				if (palette.CurrentQueue == null || palette.DisplayedIconCount == 0)
				{
					// Select the first active tab
					foreach (var b in typesContainer.Children)
					{
						if (b is not ProductionTypeButtonWidget button || button.IsDisabled())
							continue;

						button.OnClick();
						break;
					}
				}
			};

			// Hook up scroll up and down buttons on the palette
			var scrollDown = widget.GetOrNull<ButtonWidget>("SCROLL_DOWN_BUTTON");

			if (scrollDown != null)
			{
				scrollDown.OnClick = palette.ScrollDown;
				scrollDown.IsVisible = () => palette.TotalIconCount > palette.MaxIconRowOffset * palette.Columns;
				scrollDown.IsDisabled = () => !palette.CanScrollDown;
			}

			var scrollUp = widget.GetOrNull<ButtonWidget>("SCROLL_UP_BUTTON");

			if (scrollUp != null)
			{
				scrollUp.OnClick = palette.ScrollUp;
				scrollUp.IsVisible = () => palette.TotalIconCount > palette.MaxIconRowOffset * palette.Columns;
				scrollUp.IsDisabled = () => !palette.CanScrollUp;
			}

			SetMaximumVisibleRows(palette);
		}

		bool CycleTab(Widget typesContainer, bool reverse)
		{
			var buttons = typesContainer.Children
				.OfType<ProductionTypeButtonWidget>()
				.Where(b => !b.IsDisabled())
				.ToList();

			if (buttons.Count == 0)
				return false;

			var current = buttons.FindIndex(b => b.IsHighlighted());
			var next = current < 0 ? 0 : (current + (reverse ? buttons.Count - 1 : 1)) % buttons.Count;
			buttons[next].OnClick();
			Game.Sound.PlayNotification(world.Map.Rules, null, "Sounds", buttons[next].ClickSound, null);
			return true;
		}

		static void SetMaximumVisibleRows(ProductionPaletteWidget productionPalette)
		{
			var screenHeight = Game.Renderer.Resolution.Height;

			// Get height of currently displayed icons
			var containerWidget = Ui.Root.GetOrNull<ContainerWidget>("SIDEBAR_PRODUCTION");

			if (containerWidget == null)
				return;

			var sidebarProductionHeight = containerWidget.Bounds.Y;

			// Check if icon heights exceed y resolution
			var maxItemsHeight = screenHeight - sidebarProductionHeight;

			var maxIconRowOffest = maxItemsHeight / productionPalette.IconSize.Y - 1;
			productionPalette.MaxIconRowOffset = Math.Min(maxIconRowOffest, productionPalette.MaximumRows);
		}
	}
}
