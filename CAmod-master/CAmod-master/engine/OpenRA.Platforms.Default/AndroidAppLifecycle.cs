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
using System.Runtime.InteropServices;
using SDL2;

namespace OpenRA.Platforms.Default
{
	/// <summary>
	/// Android: stops the game using the phone while it sleeps or the app is in the background.
	/// SDL already freezes the game thread then (the game carries on where it left off on return),
	/// but the OpenAL audio device would keep its output stream and mixer thread running, so pause it too.
	/// </summary>
	static class AndroidAppLifecycle
	{
		static readonly object SyncObject = new();

		// SDL calls the watcher from Android's UI thread, before the game thread is frozen
		static SDL.SDL_EventFilter eventWatch;
		static IntPtr audioDevice;
		static bool inBackground;

		[DllImport("soft_oal")]
		static extern void alcDevicePauseSOFT(IntPtr device);

		[DllImport("soft_oal")]
		static extern void alcDeviceResumeSOFT(IntPtr device);

		public static void Initialize()
		{
			if (!Platform.IsAndroid || eventWatch != null)
				return;

			// Keep a reference to the delegate so the garbage collector can't free it while SDL holds the pointer
			eventWatch = OnEvent;
			SDL.SDL_AddEventWatch(eventWatch, IntPtr.Zero);
		}

		/// <summary>Registers the OpenAL device to pause in the background (IntPtr.Zero when it is closed).</summary>
		public static void SetAudioDevice(IntPtr device)
		{
			if (!Platform.IsAndroid)
				return;

			lock (SyncObject)
			{
				audioDevice = device;
				if (inBackground && device != IntPtr.Zero)
					alcDevicePauseSOFT(device);
			}
		}

		static int OnEvent(IntPtr userdata, IntPtr sdlEvent)
		{
			try
			{
				switch ((SDL.SDL_EventType)(uint)Marshal.ReadInt32(sdlEvent))
				{
					case SDL.SDL_EventType.SDL_APP_WILLENTERBACKGROUND:
					case SDL.SDL_EventType.SDL_APP_DIDENTERBACKGROUND:
						SetBackground(true);
						break;

					case SDL.SDL_EventType.SDL_APP_WILLENTERFOREGROUND:
					case SDL.SDL_EventType.SDL_APP_DIDENTERFOREGROUND:
						SetBackground(false);
						break;
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Android lifecycle handling failed: {e}");
			}

			// Event watchers can't drop events; the return value is ignored
			return 1;
		}

		static void SetBackground(bool background)
		{
			lock (SyncObject)
			{
				if (inBackground == background)
					return;

				inBackground = background;
				if (audioDevice == IntPtr.Zero)
					return;

				if (background)
					alcDevicePauseSOFT(audioDevice);
				else
					alcDeviceResumeSOFT(audioDevice);
			}
		}
	}
}
