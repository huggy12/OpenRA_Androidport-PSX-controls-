#!/bin/sh
# Builds the native libraries (SDL2, OpenAL Soft, FreeType, Lua 5.1, libmain bridge) for arm64 Android
# into native/out/arm64-v8a. Requires the toolchain from scripts/setup-toolchain.sh.
set -e
N=$(cd "$(dirname "$0")" && pwd)
T="$N/../tools"
NDK=$T/ndk/android-ndk-r27c
CMAKE=$T/cmake/cmake-3.31.6-windows-x86_64/bin/cmake.exe
OUT=$N/out/arm64-v8a
mkdir -p $OUT
COMMON="-G Ninja -DCMAKE_MAKE_PROGRAM=$T/ninja/ninja.exe -DCMAKE_TOOLCHAIN_FILE=$NDK/build/cmake/android.toolchain.cmake -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=24 -DCMAKE_BUILD_TYPE=Release -DCMAKE_PLATFORM_NO_VERSIONED_SONAME=ON -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON -DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384"
b() { name=$1; src=$2; shift 2; echo "=== $name"; "$CMAKE" -S "$src" -B $N/build/$name $COMMON "$@" > $N/build-$name.log 2>&1 && "$CMAKE" --build $N/build/$name -j 8 >> $N/build-$name.log 2>&1 || { echo "FAILED $name"; tail -30 $N/build-$name.log; exit 1; }; }
mkdir -p $N/build
# Libraries are 16 KB page aligned so they load on devices using 16 KB memory pages (e.g. recent Pixels)
b lua $N/lua -DLUA_DIR=$T/src/lua-5.1.5
b sdlmain $N/sdlmain
b sdl2 $T/src/SDL2-2.32.10 -DSDL_STATIC=OFF -DSDL_SHARED=ON -DSDL_TEST=OFF
b openal $T/src/openal-soft-1.24.3 -DLIBTYPE=SHARED -DALSOFT_UTILS=OFF -DALSOFT_EXAMPLES=OFF -DALSOFT_TESTS=OFF -DALSOFT_INSTALL=OFF -DALSOFT_BACKEND_OBOE=OFF -DALSOFT_REQUIRE_OPENSL=ON
b freetype $T/src/freetype-2.13.3 -DBUILD_SHARED_LIBS=ON -DFT_DISABLE_ZLIB=ON -DFT_DISABLE_BZIP2=ON -DFT_DISABLE_PNG=ON -DFT_DISABLE_HARFBUZZ=ON -DFT_DISABLE_BROTLI=ON
# Copy into the names the engine's P/Invokes expect
mkdir -p $OUT
cp $N/build/lua/liblua51.so $N/build/sdlmain/libmain.so $N/build/sdl2/libSDL2.so $OUT/
cp $N/build/openal/libopenal.so $OUT/libsoft_oal.so
cp $N/build/freetype/libfreetype.so $OUT/libfreetype6.so
ls -la $OUT
