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
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Android.App;
using Android.Runtime;
using AndroidLog = Android.Util.Log;

namespace OpenRAAndroid
{
	/// <summary>
	/// Android entry point. SDL's Java activity (OpenRAActivity) starts a native thread that calls
	/// SDL_main in libmain.so, which forwards into <see cref="ManagedMain"/> registered here at startup.
	/// </summary>
	[Application]
	public class MainApplication : Application
	{
		const string Tag = "OpenRA";

		public MainApplication(IntPtr handle, JniHandleOwnership transfer)
			: base(handle, transfer) { }

		[DllImport("main")]
		static extern unsafe void openra_set_main(delegate* unmanaged<int, IntPtr, int> entry);

		public override void OnCreate()
		{
			base.OnCreate();
			unsafe
			{
				openra_set_main(&ManagedMain);
			}
		}

		// Android only gives SDL's native thread ~1MB of stack, which the engine's loading code can overflow
		// (killing the process without any error). Run the game on a dedicated thread with a large stack.
		const int GameThreadStackSize = 64 * 1024 * 1024;

		static string supportDir;
		static StreamWriter startupLog;

		[UnmanagedCallersOnly]
		static int ManagedMain(int argc, IntPtr argv)
		{
			var exitCode = 1;
			try
			{
				InitializeDiagnostics();
				Trace("SDL_main reached managed code");

				var thread = new Thread(() =>
				{
					try
					{
						exitCode = Run();
					}
					catch (Exception e)
					{
						ReportCrash(e);
					}
				}, GameThreadStackSize)
				{
					Name = "OpenRA"
				};

				thread.Start();
				thread.Join();
				Trace($"Game exited with code {exitCode}");
			}
			catch (Exception e)
			{
				ReportCrash(e);
			}

			startupLog?.Flush();

			// OpenRA keeps a lot of static state, so the process can't be reused for another session.
			Java.Lang.JavaSystem.Exit(exitCode);
			return exitCode;
		}

		/// <summary>
		/// Writes a step-by-step startup log and the engine's console output to the app's files directory
		/// (Android/data/net.openra.android.ca/files/), and records any crash in crash.log.
		/// </summary>
		static void InitializeDiagnostics()
		{
			var context = Context;
			supportDir = context.GetExternalFilesDir(null)?.AbsolutePath ?? Path.Combine(context.FilesDir.AbsolutePath, "support");
			Directory.CreateDirectory(supportDir);

			try
			{
				File.Delete(Path.Combine(supportDir, "crash.log"));
				startupLog = new StreamWriter(Path.Combine(supportDir, "startup.log"), false) { AutoFlush = true };
				var console = new StreamWriter(Path.Combine(supportDir, "console.log"), false) { AutoFlush = true };
				Console.SetOut(TextWriter.Synchronized(console));
				Console.SetError(TextWriter.Synchronized(console));
			}
			catch (Exception e)
			{
				AndroidLog.Error(Tag, $"Failed to create log files: {e}");
			}

			AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);
			Trace($"Device: {Android.OS.Build.Manufacturer} {Android.OS.Build.Model}, Android {Android.OS.Build.VERSION.Release} (API {(int)Android.OS.Build.VERSION.SdkInt})");
		}

		static void Trace(string message)
		{
			AndroidLog.Info(Tag, message);
			try
			{
				startupLog?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
			}
			catch { }
		}

		static void ReportCrash(Exception e)
		{
			var text = e?.ToString() ?? "Unknown error";
			AndroidLog.Error(Tag, $"Fatal error: {text}");
			Trace($"FATAL: {text}");
			try
			{
				if (supportDir != null)
					File.AppendAllText(Path.Combine(supportDir, "crash.log"), text + Environment.NewLine);
			}
			catch { }
		}

		static int Run()
		{
			var context = Context;
			var engineDir = Path.Combine(context.FilesDir.AbsolutePath, "engine");
			Trace("Checking engine data");
			ExtractEngineData(context, engineDir);
			Trace("Engine data ready");

			var args = new List<string>
			{
				$"Engine.EngineDir={engineDir}",
				$"Engine.SupportDir={supportDir}",
				"Game.Mod=ca",

				// The gamepad drives a software-rendered cursor, and Discord is not available on Android
				"Graphics.DisableHardwareCursors=True",
				"Game.EnableDiscordService=False",

				// The gamepad scrolls when the cursor is pushed against an edge. The engine's own edge scrolling
				// would keep scrolling whenever the cursor (or last touch) rests on an edge.
				"Game.ViewportEdgeScroll=False",
			};

			ApplyDefaultSettings(args);

			EnsureEngineAssembliesLoaded();
			Trace($"Starting OpenRA: {string.Join(" ", args)}");

			try
			{
				return (int)OpenRA.Game.InitializeAndRun(args.ToArray());
			}
			catch (Exception e)
			{
				ReportCrash(e);
				return 1;
			}
			finally
			{
				OpenRA.Log.Dispose();
			}
		}

		// Bump this when adding new defaults below so that existing installs pick them up once
		const int DefaultsVersion = 2;

		/// <summary>
		/// Applies Android/gamepad friendly defaults once per defaults version. They are passed as
		/// launch arguments, which the engine saves to settings.yaml, so later changes in the
		/// settings menu are kept.
		/// </summary>
		static void ApplyDefaultSettings(List<string> args)
		{
			var stampFile = Path.Combine(supportDir, "android-defaults-version");
			var applied = 0;
			if (File.Exists(stampFile))
				int.TryParse(File.ReadAllText(stampFile).Trim(), out applied);
			else if (File.Exists(Path.Combine(supportDir, "settings.yaml")))
				applied = 1;

			// Version 1: select and command units with the same button, like the PlayStation version of Red Alert
			if (applied < 1)
			{
				args.Add("Game.UseClassicMouseStyle=True");
				args.Add("Game.LockMouseWindow=False");
				args.Add("Graphics.CursorDouble=True");
			}

			// Version 2: start zoomed in, which suits a phone screen
			if (applied < 2)
				args.Add("Graphics.ViewportDistance=Close");

			File.WriteAllText(stampFile, DefaultsVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		/// <summary>
		/// The engine looks up its platform and mod assemblies by name, so make sure they are loaded
		/// (and not considered unused) before the game starts.
		/// </summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		static void EnsureEngineAssembliesLoaded()
		{
			var types = new[]
			{
				typeof(OpenRA.Platforms.Default.DefaultPlatform),
				typeof(OpenRA.Mods.Common.Traits.Mobile),
				typeof(OpenRA.Mods.Cnc.Traits.Chronoshiftable),
				typeof(OpenRA.Mods.CA.Traits.AirstrikeMaster),
			};

			foreach (var t in types)
				Trace($"Loaded {t.Assembly.GetName().Name}");
		}

		/// <summary>Unpacks the engine data (mods, shaders) from the APK whenever the app is installed or updated.</summary>
		static void ExtractEngineData(Android.Content.Context context, string engineDir)
		{
#pragma warning disable CA1422, CS0618 // GetPackageInfo(string, int) is deprecated on API 33+ but still works
			var packageInfo = context.PackageManager.GetPackageInfo(context.PackageName, 0);
#pragma warning restore CA1422, CS0618
			var stamp = $"{packageInfo.LastUpdateTime}";
			var stampFile = Path.Combine(engineDir, ".installed");

			if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp)
				return;

			Trace("Extracting engine data");
			if (Directory.Exists(engineDir))
				Directory.Delete(engineDir, true);

			Directory.CreateDirectory(engineDir);

			// Asset streams are not seekable, so copy the archive to disk before extracting
			var tempZip = Path.Combine(context.CacheDir.AbsolutePath, "engine.zip");
			using (var asset = context.Assets.Open("engine.zip"))
			using (var output = File.Create(tempZip))
				asset.CopyTo(output);

			ZipFile.ExtractToDirectory(tempZip, engineDir);
			File.Delete(tempZip);
			File.WriteAllText(stampFile, stamp);
			Trace("Engine data extracted");
		}
	}
}
