# Combined Arms for Android

Android host for the [Combined Arms](https://github.com/Inq8/CAmod) mod in `../CAmod-master/CAmod-master`.
It uses the same console-style Bluetooth controller controls and touch controls as the Red Alert app in
`../OpenRA.Android` (see the [main README](../README.md#controls)), and installs next to it as a separate app
(`net.openra.android.ca`, shown as **Combined Arms**).

## How it is put together

- **Engine:** CA runs on its own OpenRA fork, [`Inq8/OpenRA` branch `ca-engine/1.09`](https://github.com/Inq8/OpenRA/tree/ca-engine/1.09),
  which is older than the bleed engine the Red Alert app uses. It lives in `../CAmod-master/CAmod-master/engine`
  (where the mod's own `make` would put it), with the Android port changes from `../OpenRA-bleed` applied on top.
  A pristine copy for diffing is in `../ca-engine-dl/OpenRA-ca-engine-1.09`.
  Differences from the Red Alert port:
  - `Directory.Build.props` targets .NET 10 / C# 13 instead of .NET 6 / C# 9.
  - This engine's settings have no `MouseControlStyle`; the gamepad and touch code switch on `Game.UseClassicMouseStyle` instead.
  - `GamepadSettings` is a plain settings section (`Gamepad:` in settings.yaml), registered in `Settings.cs`.
  - The partial texture upload fix isn't needed (this engine has no sub-region uploads).
  - The CA-only shaders (`postprocess_glow`, `postprocess_heat_distortion`, `postprocess_shockwave`) also request high precision.
  - `VERSION` is `ca-engine/1.09`, as the mod's `fetch-engine.sh` would set it.
  - Faster startup: `MapPreview.cs` only works out a map's custom world / player actors (merging CA's whole rule set)
    when the map is used, not for all ~430 CA maps with custom rules at startup. On a vivo phone this cut map loading
    from 52 s to 4 s, and startup to the main menu from about 95 s to about 30 s.
- **Mod:** CA's HUD (`mods/ca/chrome/ingame-player.yaml`) has the gamepad zones and fading, the
  `CycleOrderModes` (F13) and `NextProductionType` (F14) hotkeys are in `mods/ca/hotkeys/ca.yaml`, the Controller
  settings tab is added in `mods/ca/mod.chrome.yaml`, and the lobby chat shrinks with the dialog on small screens.
  CA's own widgets got controller navigation: the scrollable special abilities bar
  (`SupportPowersScrollableWidget.cs`) and the factory tabs (`ProductionTabsCAWidget.cs`).

## Controls that differ from Red Alert

- **L1 / LB** cycles the production types (buildings, defences, infantry, vehicles, aircraft, naval).
  The gamepad sends F14 for this, because CA already uses Page Down to switch between factory tabs.
- When you have several factories of one type, CA shows numbered factory tabs above the build icons. Move the
  cursor up from the build icons in the sidebar to reach them, and press Cross / A to switch factory.
- The build palette scrolls: stepping down from its bottom row (or up from its top row) scrolls by one row, and the
  right stick scrolls it while the cursor is on it. With touch controls, drag up or down on the icons.
  CA's special abilities bar (Triangle / Y twice) scrolls the same way.
  `ClassicProductionLogic` limits the palette to the rows that fit on screen (CA's layout gives `SIDEBAR_PRODUCTION`
  no position, so the engine's own check let 5 rows run off a phone screen), and the palette draws an up / down arrow
  (`scrollpanel-decorations`) on its edges while more items are hidden. Widgets opt in through
  `Widget.ScrollListRowSize` / `ScrollListRows` / `ShouldGamepadScrollList` (`ProductionPaletteWidget.cs`,
  `SupportPowersScrollableWidget.cs`).

## Sleep and background

SDL freezes the game thread while the app is paused (screen off, home button), and `AndroidAppLifecycle.cs` pauses the
OpenAL device at the same time (it would otherwise keep its output stream and mixer thread running). On a vivo phone
the app then uses no measurable CPU and its audio stream shows as stopped. The game resumes where it left off.

## First launch

The game files are not in the APK. On first launch the CA content installer appears: choose **Quick Install** to
download the freeware Red Alert and Tiberian Dawn files (needs an internet connection). The APK is large (about
230 MB) because it includes CA's own music, sounds, maps and missions.

Settings, logs and downloaded content are stored in `Android/data/net.openra.android.ca/files/` on the phone.

## Online play

The mod reports the version string from `mods/ca/mod.yaml`, which is `prep-CA` in this source copy (it was
downloaded from the CA master branch, not a release). PC players can only be joined when the version string and
the game rules match, so for online play build from the same CA release the PC players use, and set its version in
`mods/ca/mod.yaml` and `mods/ca-content/mod.yaml`.

## Building

Uses the same toolchain (`../tools`) and native libraries (`../native/out`) as the Red Alert app.

```bash
./build-apk.sh      # builds bin/Release/net10.0-android/net.openra.android.ca-Signed.apk
./run-on-phone.sh   # builds, installs on a USB-connected phone, launches and reports crashes
```

To build the engine and mod for the desktop (to check that the code compiles), run
`dotnet build OpenRA.Mods.CA/OpenRA.Mods.CA.csproj -c Release` in `../CAmod-master/CAmod-master`.
