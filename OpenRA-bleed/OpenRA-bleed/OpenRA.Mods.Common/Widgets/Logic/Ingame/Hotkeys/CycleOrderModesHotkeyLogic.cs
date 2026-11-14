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

using System.Collections.Generic;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Orders;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	/// <summary>Cycles through the repair, sell and power down modes (and back to normal), for gamepads.</summary>
	[ChromeLogicArgsHotkeys("CycleOrderModesKey")]
	public class CycleOrderModesHotkeyLogic : SingleHotkeyBaseLogic
	{
		readonly World world;

		[ObjectCreator.UseCtor]
		public CycleOrderModesHotkeyLogic(Widget widget, ModData modData, World world, Dictionary<string, MiniYaml> logicArgs)
			: base(widget, modData, "CycleOrderModesKey", "PLAYER_KEYHANDLER", logicArgs)
		{
			this.world = world;
		}

		protected override bool OnHotkeyActivated(KeyInput e)
		{
			if (world.LocalPlayer == null || world.LocalPlayer.WinState != WinState.Undefined)
				return false;

			switch (world.OrderGenerator)
			{
				case RepairOrderGenerator:
					world.OrderGenerator = new SellOrderGenerator(world);
					break;
				case SellOrderGenerator:
					world.OrderGenerator = new PowerDownOrderGenerator(world);
					break;
				case PowerDownOrderGenerator:
					world.CancelInputMode();
					break;
				default:
					world.OrderGenerator = new RepairOrderGenerator(world);
					break;
			}

			return true;
		}
	}
}
