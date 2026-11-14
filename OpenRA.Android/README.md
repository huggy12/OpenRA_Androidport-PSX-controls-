# OpenRA Red Alert for Android

Android host for the OpenRA engine in `../OpenRA-bleed/OpenRA-bleed`. It runs the Red Alert mod with
console-style Bluetooth controller controls, modelled on the PlayStation version of Red Alert.

## Controls

The controller layout is described in the [main README](../README.md#controls).

## First launch

The Red Alert game files are not in the APK. On first launch the game shows the content installer.
Choose **Quick Install** to download the freeware Red Alert files (needs an internet connection).

Settings, logs and downloaded content are stored in `Android/data/net.openra.android.ra/files/`
on the phone's storage. Look in `Logs/` there (or `crash.log`) if something goes wrong.

## Building

Everything (the .NET 10 SDK, JDK 17, Android SDK/NDK, CMake, Ninja) is installed locally in `../tools`.

1. Native libraries (SDL2, OpenAL Soft, FreeType, Lua 5.1 and the `libmain.so` bridge) are built by
   `../native/build-native.sh` into `../native/out/arm64-v8a`. Only needed once.
2. `./build-apk.sh` (from Git Bash) packs the engine data and builds
   `bin/Release/net10.0-android/net.openra.android.ra-Signed.apk` (signed with a debug key).

To install over USB with debugging enabled:

```bash
../tools/sdk/platform-tools/adb.exe install -r bin/Release/net10.0-android/net.openra.android.ra-Signed.apk
```

With the phone connected over USB, `./run-on-phone.sh [seconds]` builds, installs, launches, waits, then
prints any crash (from logcat and `crash.log`) and saves a screenshot to `../screen.png`.

To view the live log:

```bash
../tools/sdk/platform-tools/adb.exe logcat -s OpenRA DOTNET SDL SDL/APP
```

## How it fits together

- `java/net/openra/android/OpenRAActivity.java` extends SDL's `SDLActivity` (vendored in `java/org/libsdl`).
- SDL calls `SDL_main` in `libmain.so` on its own thread. `MainApplication.cs` registers its managed
  entry point with `libmain.so` at startup, so `SDL_main` runs `OpenRA.Game.InitializeAndRun`.
- The engine data (mods, shaders) ships as `assets/engine.zip` and is extracted to the app's private
  storage on first launch and after each update.
- Engine changes for Android live in the OpenRA tree. They cover Android detection (`Platform.IsAndroid`),
  bundled assembly loading, GLES-only window setup, UI scaling, the BGRA texture fallback, the gamepad
  cursor (`OpenRA.Platforms.Default/Sdl2GamepadInput.cs`) and production tab hotkeys for the RA sidebar.
