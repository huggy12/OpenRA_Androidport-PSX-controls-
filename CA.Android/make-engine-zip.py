"""Packs the OpenRA engine data and the Combined Arms mod into obj/engine.zip for the APK.

Usage: make-engine-zip.py <CA mod root> <output zip>
The CA mod's own folders (mods/ca, mods/ca-content) go next to the engine's mods/common, so the
engine finds them in its default mod search path.
"""
import os, sys, zipfile

ca_root = os.path.abspath(sys.argv[1])
engine_root = os.path.join(ca_root, "engine")
out = os.path.abspath(sys.argv[2])

engine_files = ["VERSION", "AUTHORS", "COPYING", "global mix database.dat"]
engine_dirs = ["glsl", "mods/common", "mods/common-content"]
ca_dirs = ["mods/ca", "mods/ca-content"]

# Already-compressed formats are stored as-is, which keeps packing quick
stored = {".ogg", ".mp3", ".png", ".oramap", ".zip", ".ttf"}

os.makedirs(os.path.dirname(out), exist_ok=True)
count = 0


def add(z, full, name):
    global count
    ext = os.path.splitext(name)[1].lower()
    z.write(full, name, zipfile.ZIP_STORED if ext in stored else zipfile.ZIP_DEFLATED)
    count += 1


with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for f in engine_files:
        add(z, os.path.join(engine_root, f), f)
    for root, dirs in ((engine_root, engine_dirs), (ca_root, ca_dirs)):
        for d in dirs:
            for base, _, names in os.walk(os.path.join(root, d)):
                for n in names:
                    full = os.path.join(base, n)
                    add(z, full, os.path.relpath(full, root).replace(os.sep, "/"))
print(f"{count} files -> {out} ({os.path.getsize(out) // 1024} KiB)")
