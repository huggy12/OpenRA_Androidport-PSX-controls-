# OpenRA Red Alert for Android

A personal port of [OpenRA](https://github.com/OpenRA/OpenRA) to Android, based on the bleed branch at commit
[`b4d95a5`](https://github.com/OpenRA/OpenRA/commit/b4d95a56717d8d0070f195e343f6c2728ce87900) (November 2026), playing the Red Alert mod
with a Bluetooth controller using console-style controls inspired by the PlayStation version of Red Alert.

> **Made with Claude Code.** This port (the Android app, the engine changes, the native library builds and the
> controller controls) was written by [Claude Code](https://claude.com/claude-code), Anthropic's AI coding agent,
> directed and tested on a real phone by the repository owner.

![Gameplay preview: a skirmish on a phone, controlled with a Bluetooth controller](docs/preview.gif)

*Skirmish on a vivo phone (Snapdragon, Android 16) with a GameSir G8+ controller.*

This is an unofficial fork. It is not endorsed by or affiliated with the OpenRA project or EA, and it does not
include any Red Alert game files: on first launch the game's content installer downloads the freeware files.

## Download and install

**APK: [`apk/OpenRA-RedAlert-android.apk`](apk/OpenRA-RedAlert-android.apk)**
([direct download](https://github.com/huggy12/OpenRA_Androidport-PSX-controls-/raw/main/apk/OpenRA-RedAlert-android.apk), about 39 MB)

1. Download the APK on your phone (or copy it over from a PC).
2. Open it and allow your browser / file manager to "install unknown apps" when Android asks.
3. Pair your Bluetooth controller in Android's Bluetooth settings, then start **OpenRA - Red Alert**.
4. On first launch choose **Quick Install** to download the freeware Red Alert game files (needs internet).

Requirements: a 64-bit ARM phone (arm64-v8a) running Android 7.0 or newer with OpenGL ES 3.0.
The APK is signed with a debug key, so Android may show an "unknown developer" warning.
It is built from the code in this repository; the controller layout is listed in
[`OpenRA.Android/README.md`](OpenRA.Android/README.md#controls).

## Layout

| Path | Contents |
|------|----------|
| `OpenRA-bleed/OpenRA-bleed/` | The OpenRA engine, with Android and gamepad changes (see the second commit for the full diff) |
| `OpenRA.Android/` | The Android app (.NET 10 for Android): SDL activity, app entry point, packaging and phone test scripts. Its README lists the controller controls |
| `native/` | CMake projects and build script for the native libraries (SDL2, OpenAL Soft, FreeType, Lua 5.1 and the `libmain.so` bridge) |
| `scripts/setup-toolchain.sh` | Downloads the toolchain into `tools/` (not committed) |

## Building (Windows, Git Bash)

```bash
scripts/setup-toolchain.sh     # once: .NET 10 + Android workload, JDK 17, Android SDK/NDK, CMake, Ninja, library sources
native/build-native.sh         # once: native libraries for arm64-v8a
OpenRA.Android/build-apk.sh    # builds OpenRA.Android/bin/Release/net10.0-android/net.openra.android.ra-Signed.apk
```

With a phone connected over USB debugging, `OpenRA.Android/run-on-phone.sh` builds, installs, launches and reports crashes.
To publish a new build, copy the signed APK over `apk/OpenRA-RedAlert-android.apk` and commit it.

## Engine changes

- Android support: platform detection, bundled assembly loading, GLES 3 window setup, UI scaling for phone screens,
  dialogs shrunk to fit, high precision shaders, Mono runtime workarounds
- Gamepad input (`OpenRA.Platforms.Default/Sdl2GamepadInput.cs`, `OpenRA.Game/Widgets/GamepadNavigation.cs`):
  virtual cursor, button snapping in menus, sidebar / command bar zones toggled with Triangle / Circle,
  push-to-edge scrolling, translucent HUD panels when not in use
- Red Alert mod: production tab cycling and a repair / sell / power down cycling hotkey

## License

OpenRA is licensed under the GNU GPL v3 (see `OpenRA-bleed/OpenRA-bleed/COPYING`); the changes here are under the same license.
SDL2 (zlib), OpenAL Soft (LGPL), FreeType (FTL) and Lua (MIT) are built from their upstream sources.
