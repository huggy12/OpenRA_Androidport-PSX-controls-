#!/bin/sh
# Downloads and installs everything needed to build the APK into ../tools (nothing is installed system-wide):
# .NET 10 SDK + android workload, JDK 17, Android SDK (platform 36, build-tools 36), NDK r27c, CMake, Ninja,
# and the SDL2 / OpenAL Soft / FreeType / Lua 5.1 sources used by native/build-native.sh.
# Run from Git Bash on Windows. Accepting the Android SDK licenses is required (sdkmanager --licenses).
set -e
ROOT=$(cd "$(dirname "$0")/.." && pwd)
T="$ROOT/tools"
mkdir -p "$T/dl" "$T/jdk" "$T/ndk" "$T/sdk/cmdline-tools" "$T/cmake" "$T/ninja" "$T/src"
cd "$T/dl"

curl -sSL -o dotnet-install.ps1 https://dot.net/v1/dotnet-install.ps1 &
curl -sSL -o jdk17.zip https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip &
curl -sSL -o ndk.zip https://dl.google.com/android/repository/android-ndk-r27c-windows.zip &
curl -sSL -o cmdline-tools.zip https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip &
curl -sSL -o cmake.zip https://github.com/Kitware/CMake/releases/download/v3.31.6/cmake-3.31.6-windows-x86_64.zip &
curl -sSL -o ninja.zip https://github.com/ninja-build/ninja/releases/download/v1.12.1/ninja-win.zip &
curl -sSL -o SDL2.tar.gz https://github.com/libsdl-org/SDL/releases/download/release-2.32.10/SDL2-2.32.10.tar.gz &
curl -sSL -o openal.tar.gz https://github.com/kcat/openal-soft/archive/refs/tags/1.24.3.tar.gz &
curl -sSL -o freetype.tar.gz https://download.savannah.gnu.org/releases/freetype/freetype-2.13.3.tar.gz &
curl -sSL -o lua51.tar.gz https://www.lua.org/ftp/lua-5.1.5.tar.gz &
wait

cd "$T"
TAR=/c/Windows/System32/tar.exe
$TAR -xf dl/jdk17.zip -C jdk
$TAR -xf dl/ndk.zip -C ndk
$TAR -xf dl/cmdline-tools.zip -C sdk/cmdline-tools
mv sdk/cmdline-tools/cmdline-tools sdk/cmdline-tools/latest
$TAR -xf dl/cmake.zip -C cmake
$TAR -xf dl/ninja.zip -C ninja
(cd src && for f in SDL2 openal freetype lua51; do tar -xzf ../dl/$f.tar.gz; done)

powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$T/dl/dotnet-install.ps1")" -Channel 10.0 -InstallDir "$(cygpath -w "$T/dotnet")" -NoPath
DOTNET_CLI_TELEMETRY_OPTOUT=1 "$T/dotnet/dotnet.exe" workload install android

export JAVA_HOME="$(ls -d "$T"/jdk/jdk-17*)"
SDKMANAGER="$(cygpath -w "$T/sdk/cmdline-tools/latest/bin/sdkmanager.bat")"
SDKROOT="$(cygpath -w "$T/sdk")"
cmd //c "$SDKMANAGER --sdk_root=$SDKROOT --licenses"
cmd //c "$SDKMANAGER --sdk_root=$SDKROOT platform-tools platforms;android-36 build-tools;36.0.0"
echo "Toolchain ready in $T"
