"""Turns on mipmaps in the Godot import settings of the portrait icons the UI draws scaled down.

Item icons (128 px, drawn at about a third of that in slots), boss and town NPC heads (boss bars, map markers) and
creature thumbnails are imported with `mipmaps/generate=true`, so the UI's LinearWithMipmaps filter keeps them crisp
instead of aliasing. Godot writes a new icon's .import file with mipmaps off: run this after adding icons (then the
Godot --import), or with --check (CI) to list icons still without them.
Usage: python3 tools/textures/icon_mipmaps.py [--check]
"""
import pathlib
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]
FOLDERS = ("items", "bosses", "npcs", "creatures")
OFF, ON = "mipmaps/generate=false", "mipmaps/generate=true"


def main() -> int:
    check = "--check" in sys.argv
    files = [f for folder in FOLDERS for f in sorted((REPO / "assets" / "icons" / folder).glob("*.png.import"))]
    stale = [f for f in files if ON not in f.read_text(encoding="utf-8")]
    if check:
        for f in stale[:20]:
            print(f"mipmaps off: {f.relative_to(REPO)}")
        print(f"icon mipmaps: {len(files) - len(stale)}/{len(files)} on" + ("" if not stale else " (run tools/textures/icon_mipmaps.py)"))
        return 1 if stale else 0
    for f in stale:
        text = f.read_text(encoding="utf-8")
        f.write_text(text.replace(OFF, ON) if OFF in text else text.replace("[params]\n", f"[params]\n\n{ON}\n", 1), encoding="utf-8")
    print(f"icon mipmaps: turned on for {len(stale)} of {len(files)} icons; run the Godot --import to reimport them")
    return 0


if __name__ == "__main__":
    sys.exit(main())
