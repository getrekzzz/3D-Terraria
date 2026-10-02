#!/usr/bin/env python3
"""Terraria-style pixel sprites for the UI, drawn from the ASCII maps in ui_sprites.py and ui_sprites_b.py.

Usage: python3 tools/textures/build_ui.py [--check] [--only=GLOB[,GLOB...]]
Passes:
  sprites  ui_sprites.py + ui_sprites_b.py               -> assets/ui/<name>.png
`--only` globs match a name or "<pass>/<name>" (heart*, sprites/*). `--check` needs neither numpy nor Pillow (CI runs
it): it validates every map and fails when a sprite has no output file.
Output is deterministic: cells are drawn 2x2 nearest-neighbour with a 1 px OUTLINE ring (8-neighbour alpha dilation).
"""
import fnmatch
import pathlib
import sys

import ui_sprites
import ui_sprites_b

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
OUT = {"sprites": REPO / "assets" / "ui"}
OUTLINE = ui_sprites.OUTLINE
PALETTE = ui_sprites.PALETTE
MAPS = {**ui_sprites.SPRITES, **ui_sprites_b.SPRITES}
PLAIN = ui_sprites.PLAIN | ui_sprites_b.PLAIN


def rows(text):
    return [r.strip() for r in text.strip("\n").splitlines() if r.strip()]


def sprite_maps():
    """Every UI sprite name -> (rows, letter swap), including colour-swapped variants."""
    maps = {name: (rows(text), {}) for name, text in MAPS.items()}
    for name, (source, swap) in ui_sprites.SWAPS.items():
        maps[name] = (maps[source][0], swap)
    return maps


def map_errors(name, cells, letters, size=None):
    errors = []
    widths = {len(r) for r in cells}
    if len(widths) != 1:
        errors.append(f"{name}: rows have different widths {sorted(widths)}")
    if size and (len(cells), max(widths)) != size:
        errors.append(f"{name}: must be {size[1]}x{size[0]} cells, is {max(widths)}x{len(cells)}")
    bad = sorted({ch for r in cells for ch in r if ch != "." and ch not in letters})
    if bad:
        errors.append(f"{name}: unknown letters {''.join(bad)}")
    return errors


def validate():
    errors = []
    for name, (cells, swap) in sprite_maps().items():
        errors += map_errors(name, cells, set(PALETTE))
        errors += [f"{name}: swap to unknown letter '{t}'" for t in swap.values() if t not in PALETTE]
    return errors


def check():
    errors = validate()
    maps = sprite_maps()
    errors += [f"missing assets/ui/{name}.png (run build_ui.py)" for name in maps if not (OUT["sprites"] / f"{name}.png").exists()]
    if errors:
        print("\n".join(errors))
        return 1
    print(f"{len(maps)} UI sprites: ok")
    return 0


# --- Rendering (numpy and Pillow are imported only here) ---

def rgb(hex_colour):
    return tuple(int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))


def paint(cells, colours):
    import numpy as np
    arr = np.zeros((len(cells), len(cells[0]), 4), np.uint8)
    for y, row in enumerate(cells):
        for x, ch in enumerate(row):
            if ch != ".":
                arr[y, x] = (*colours[ch], 255)
    return arr.repeat(2, axis=0).repeat(2, axis=1)


def outlined(arr, pad):
    """Adds a 1 px OUTLINE ring around opaque pixels, growing the canvas by `pad` px on each side first."""
    import numpy as np
    if pad:
        arr = np.pad(arr, ((pad, pad), (pad, pad), (0, 0)))
    solid = arr[..., 3] > 0
    h, w = solid.shape
    padded = np.pad(solid, 1)
    grown = np.zeros_like(solid)
    for dy in range(3):
        for dx in range(3):
            grown |= padded[dy:dy + h, dx:dx + w]
    out = arr.copy()
    out[grown & ~solid] = (*rgb(OUTLINE), 255)
    return out


def render_sprite(name, cells, swap):
    colours = {k: rgb(PALETTE[swap.get(k, k)]) for k in PALETTE}
    arr = paint(cells, colours)
    return arr if name in PLAIN else outlined(arr, 1)


def save(arr, path):
    from PIL import Image
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(arr, "RGBA").save(path, optimize=True)


def selected(globs, kind, name):
    return not globs or any(fnmatch.fnmatch(name, g) or fnmatch.fnmatch(f"{kind}/{name}", g) for g in globs)


def build(globs):
    written = 0
    for name, (cells, swap) in sprite_maps().items():
        if selected(globs, "sprites", name):
            save(render_sprite(name, cells, swap), OUT["sprites"] / f"{name}.png")
            written += 1
    print(f"wrote {written} UI images")


def main():
    globs = [g for a in sys.argv[1:] if a.startswith("--only=") for g in a[len("--only="):].split(",") if g]
    if "--check" in sys.argv:
        return check()
    errors = validate()
    if errors:
        print("\n".join(errors))
        return 1
    build(globs)
    return 0


if __name__ == "__main__":
    sys.exit(main())
