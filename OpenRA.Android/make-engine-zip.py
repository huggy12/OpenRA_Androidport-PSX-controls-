"""Packs the OpenRA engine data needed by the Red Alert mod into obj/engine.zip for the APK."""
import os, sys, zipfile

root = os.path.abspath(sys.argv[1])
out = os.path.abspath(sys.argv[2])
files = ["VERSION", "AUTHORS", "COPYING", "global mix database.dat"]
dirs = ["glsl", "mods/common", "mods/common-content", "mods/ra", "mods/ra-content"]

os.makedirs(os.path.dirname(out), exist_ok=True)
count = 0
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for f in files:
        z.write(os.path.join(root, f), f)
        count += 1
    for d in dirs:
        for base, _, names in os.walk(os.path.join(root, d)):
            for n in names:
                full = os.path.join(base, n)
                z.write(full, os.path.relpath(full, root).replace(os.sep, "/"))
                count += 1
print(f"{count} files -> {out} ({os.path.getsize(out) // 1024} KiB)")
