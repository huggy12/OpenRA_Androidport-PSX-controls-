#!/bin/sh
# Builds the APK, installs it on the USB-connected phone, launches it and reports crashes.
# Usage: ./run-on-phone.sh [seconds-to-wait]
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
WAIT=${1:-45}
"$HERE/build-apk.sh" > "$HERE/../build.log" 2>&1 || { grep -E " error " "$HERE/../build.log" | sed 's/\[C:.*//' | sort -u | head -20; exit 1; }
cp "$HERE"/bin/Release/net10.0-android/net.openra.android.ra-Signed.apk "$HERE/../OpenRA-RedAlert-android.apk"

APK=$(cygpath -w "$HERE/../OpenRA-RedAlert-android.apk")
export MSYS_NO_PATHCONV=1
ADB="$HERE/../tools/sdk/platform-tools/adb.exe"
FILES=/storage/emulated/0/Android/data/net.openra.android.ra/files
"$ADB" install -r "$APK" | tail -1
"$ADB" logcat -c
"$ADB" shell am start -n net.openra.android.ra/net.openra.android.OpenRAActivity > /dev/null
sleep "$WAIT"
echo "PID: $("$ADB" shell pidof net.openra.android.ra || true)"
"$ADB" logcat -d -v brief > "$HERE/../logcat.txt"
grep -E "F/libc|F/DEBUG.*(#0[0-9]|Abort)|FATAL|AndroidRuntime" "$HERE/../logcat.txt" | head -25 || true
echo "--- console.log"; "$ADB" shell cat $FILES/console.log | tail -8
echo "--- crash.log"; "$ADB" shell cat $FILES/crash.log 2>/dev/null | head -25 || true
"$ADB" exec-out screencap -p > "$HERE/../screen.png"
