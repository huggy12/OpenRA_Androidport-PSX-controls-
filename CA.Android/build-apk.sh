#!/bin/sh
# Builds the Combined Arms APK. Run from Git Bash: ./build-apk.sh
set -e
# Path conversion must stay enabled for the Windows tools invoked below
unset MSYS_NO_PATHCONV
HERE=$(cd "$(dirname "$0")" && pwd)
TOOLS="$HERE/../tools"
export DOTNET_ROOT="$TOOLS/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export JAVA_HOME="$(ls -d "$TOOLS"/jdk/jdk-17*)"
export ANDROID_HOME="$TOOLS/sdk"

python "$HERE/make-engine-zip.py" "$HERE/../CAmod-master/CAmod-master" "$HERE/obj/engine.zip"

"$DOTNET_ROOT/dotnet.exe" build "$HERE/CA.Android.csproj" -c Release \
	-p:TargetPlatform=android-arm64 \
	-p:AndroidSdkDirectory="$(cygpath -w "$ANDROID_HOME")" \
	-p:JavaSdkDirectory="$(cygpath -w "$JAVA_HOME")" "$@"

ls -la "$HERE"/bin/Release/net10.0-android/*-Signed.apk
