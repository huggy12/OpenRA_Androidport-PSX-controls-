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
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	/// <summary>Settings panel for choosing which controller button performs each gamepad action.</summary>
	public class ControllerSettingsLogic : ChromeLogic
	{
		[FluentReference]
		const string ButtonA = "options-gamepad-button.a";

		[FluentReference]
		const string ButtonB = "options-gamepad-button.b";

		[FluentReference]
		const string ButtonX = "options-gamepad-button.x";

		[FluentReference]
		const string ButtonY = "options-gamepad-button.y";

		[FluentReference]
		const string LeftShoulder = "options-gamepad-button.leftshoulder";

		[FluentReference]
		const string RightShoulder = "options-gamepad-button.rightshoulder";

		[FluentReference]
		const string LeftTrigger = "options-gamepad-button.lefttrigger";

		[FluentReference]
		const string RightTrigger = "options-gamepad-button.righttrigger";

		[FluentReference]
		const string LeftStick = "options-gamepad-button.leftstick";

		[FluentReference]
		const string RightStick = "options-gamepad-button.rightstick";

		[FluentReference]
		const string Start = "options-gamepad-button.start";

		[FluentReference]
		const string Back = "options-gamepad-button.back";

		readonly GamepadSettings gamepadSettings;
		readonly Dictionary<GamepadButton, string> buttonNames;

		[ObjectCreator.UseCtor]
		public ControllerSettingsLogic(Action<string, string, Func<Widget, Func<bool>>, Func<Widget, Action>> registerPanel, string panelID, string label)
		{
			gamepadSettings = Game.Settings.Gamepad;
			buttonNames = new Dictionary<GamepadButton, string>
			{
				{ GamepadButton.A, FluentProvider.GetMessage(ButtonA) },
				{ GamepadButton.B, FluentProvider.GetMessage(ButtonB) },
				{ GamepadButton.X, FluentProvider.GetMessage(ButtonX) },
				{ GamepadButton.Y, FluentProvider.GetMessage(ButtonY) },
				{ GamepadButton.LeftShoulder, FluentProvider.GetMessage(LeftShoulder) },
				{ GamepadButton.RightShoulder, FluentProvider.GetMessage(RightShoulder) },
				{ GamepadButton.LeftTrigger, FluentProvider.GetMessage(LeftTrigger) },
				{ GamepadButton.RightTrigger, FluentProvider.GetMessage(RightTrigger) },
				{ GamepadButton.LeftStick, FluentProvider.GetMessage(LeftStick) },
				{ GamepadButton.RightStick, FluentProvider.GetMessage(RightStick) },
				{ GamepadButton.Start, FluentProvider.GetMessage(Start) },
				{ GamepadButton.Back, FluentProvider.GetMessage(Back) },
			};

			registerPanel(panelID, label, InitPanel, ResetPanel);
		}

		Func<bool> InitPanel(Widget panel)
		{
			foreach (var action in GamepadSettings.Actions)
			{
				var dropdown = panel.GetOrNull<DropDownButtonWidget>(action.ToUpperInvariant() + "_DROPDOWN");
				if (dropdown == null)
					continue;

				dropdown.OnMouseDown = _ => ShowButtonDropdown(dropdown, action);
				dropdown.GetText = () => buttonNames[gamepadSettings.GetButton(action)];
			}

			SettingsUtils.AdjustSettingsScrollPanelLayout(panel.Get<ScrollPanelWidget>("SETTINGS_SCROLLPANEL"));

			return () => false;
		}

		Action ResetPanel(Widget panel)
		{
			return gamepadSettings.ResetToDefaults;
		}

		void ShowButtonDropdown(DropDownButtonWidget dropdown, string action)
		{
			ScrollItemWidget SetupItem(GamepadButton button, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => gamepadSettings.GetButton(action) == button,
					() => gamepadSettings.SetButton(action, button));

				var name = buttonNames[button];
				item.Get<LabelWidget>("LABEL").GetText = () => name;
				return item;
			}

			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 500, buttonNames.Keys, SetupItem);
		}
	}
}
