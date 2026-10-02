#!/usr/bin/env python3
"""Terraria-style pixel sprites for the UI, drawn from the ASCII maps in ui_sprites*.py and ui_glyphs*.py.

Usage: python3 tools/textures/build_ui.py [--check] [--only=GLOB[,GLOB...]]
Passes:
  sprites  ui_sprites.py + ui_sprites_b.py               -> assets/ui/<name>.png
  buffs    buff_icons.json (glyphs from ui_glyphs*.py)   -> assets/icons/buffs/<icon or id>.png (32x32)
  events   event_icons.json                              -> assets/icons/events/<icon or id>.png (32x32)
`--only` globs match a name or "<pass>/<name>" (heart*, buffs/*). `--check` needs neither numpy nor Pillow (CI runs
it): it validates every map and recipe and fails when a sprite, a buff or an event has no recipe or no output file.
Output is deterministic: cells are drawn 2x2 nearest-neighbour with a 1 px OUTLINE ring (8-neighbour alpha dilation).
"""
import fnmatch
import pathlib
import sys

import ui_glyphs
import ui_glyphs_b
import ui_sprites
import ui_sprites_b
from content_data import REPO, content, load_json

HERE = pathlib.Path(__file__).resolve().parent
OUT = {
    "sprites": REPO / "assets" / "ui",
    "buffs": REPO / "assets" / "icons" / "buffs",
    "events": REPO / "assets" / "icons" / "events",
}
RECIPES = {"buffs": HERE / "buff_icons.json", "events": HERE / "event_icons.json"}
OUTLINE = ui_sprites.OUTLINE
PALETTE = ui_sprites.PALETTE
MAPS = {**ui_sprites.SPRITES, **ui_sprites_b.SPRITES}
PLAIN = ui_sprites.PLAIN | ui_sprites_b.PLAIN
GLYPHS = {**ui_glyphs.GLYPHS, **ui_glyphs_b.GLYPHS}

ICON = 32          # icon size in pixels
GLYPH = 12         # glyph drawing area in cells; a 2-cell margin makes it 16x16 cells = 32 px
TONES = "XHS"      # fg, fg lightened, fg shaded
FIXED = {"W": "#ffffff", "K": OUTLINE}
DEBUFF_FRAME = ("#ff4a4a", "#9a1424")   # outer and inner ring of the 2 px debuff frame


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


def recipes(kind):
    return load_json(RECIPES[kind])


def icon_jobs(kind):
    """(def id, output name, recipe or None, debuff) for every buff or event in the game's data."""
    jobs = []
    book = recipes(kind)
    for d in content(kind):
        jobs.append((d["id"], d.get("icon") or d["id"], book.get(d["id"]), bool(d.get("debuff"))))
    return jobs


def validate():
    errors = []
    glyph_letters = set(PALETTE) | set(TONES) | set(FIXED)
    for name, (cells, swap) in sprite_maps().items():
        errors += map_errors(name, cells, set(PALETTE))
        errors += [f"{name}: swap to unknown letter '{t}'" for t in swap.values() if t not in PALETTE]
    for name, text in GLYPHS.items():
        errors += map_errors(f"glyph {name}", rows(text), glyph_letters, (GLYPH, GLYPH))
    for kind in RECIPES:
        known = {d["id"] for d in content(kind)}
        for key, recipe in recipes(kind).items():
            where = f"{RECIPES[kind].name}: {key}"
            if key not in known:
                errors.append(f"{where}: no such {kind[:-1]} in data/")
            if recipe.get("glyph") not in GLYPHS:
                errors.append(f"{where}: unknown glyph '{recipe.get('glyph')}'")
            for field in ("fg", "bg"):
                value = recipe.get(field, "")
                if len(value) != 7 or value[0] != "#":
                    errors.append(f"{where}: {field} must be #rrggbb")
    return errors


def check():
    errors = validate()
    maps = sprite_maps()
    errors += [f"missing assets/ui/{name}.png (run build_ui.py)" for name in maps if not (OUT["sprites"] / f"{name}.png").exists()]
    counts = {}
    for kind in RECIPES:
        jobs = icon_jobs(kind)
        counts[kind] = len(jobs)
        for def_id, out, recipe, _ in jobs:
            if recipe is None:
                errors.append(f"{kind[:-1]} '{def_id}' has no recipe in tools/textures/{RECIPES[kind].name}")
            elif not (OUT[kind] / f"{out}.png").exists():
                errors.append(f"missing {OUT[kind].relative_to(REPO)}/{out}.png for {kind[:-1]} '{def_id}' (run build_ui.py)")
    if errors:
        print("\n".join(errors))
        return 1
    print(f"{len(maps)} UI sprites, {counts['buffs']} buff icons, {counts['events']} event icons, {len(GLYPHS)} glyphs: ok")
    return 0


# --- Rendering (numpy and Pillow are imported only here) ---

def rgb(hex_colour):
    return tuple(int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))


def mix(c, to, t):
    return tuple(round(a + (b - a) * t) for a, b in zip(c, to))


def scale(c, k):
    return tuple(min(255, round(a * k)) for a in c)


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


def tile(bg, debuff):
    """The 32 px icon square: dark rim, bevel (or the debuff's red frame), a gentle top-to-bottom shade, cut corners."""
    import numpy as np
    base = rgb(bg)
    arr = np.zeros((ICON, ICON, 4), np.uint8)
    for y in range(ICON):
        arr[y, :] = (*scale(base, 1.12 - 0.24 * y / (ICON - 1)), 255)
    light, dark = mix(base, (255, 255, 255), 0.3), scale(base, 0.55)
    last = ICON - 1
    if debuff:
        for ring, colour in ((1, DEBUFF_FRAME[0]), (2, DEBUFF_FRAME[1])):
            arr[ring, ring:ICON - ring] = arr[last - ring, ring:ICON - ring] = (*rgb(colour), 255)
            arr[ring:ICON - ring, ring] = arr[ring:ICON - ring, last - ring] = (*rgb(colour), 255)
    else:
        arr[1, 1:last] = arr[1:last, 1] = (*light, 255)
        arr[last - 1, 1:last] = arr[1:last, last - 1] = (*dark, 255)
    rim = (*rgb(OUTLINE), 255)
    arr[0, :] = arr[last, :] = arr[:, 0] = arr[:, last] = rim
    for y, x in ((0, 0), (0, last), (last, 0), (last, last)):
        arr[y, x] = (0, 0, 0, 0)
    return arr


def render_icon(glyph, fg, bg, debuff):
    import numpy as np
    base = rgb(fg)
    colours = {k: rgb(v) for k, v in PALETTE.items()}
    colours.update({k: rgb(v) for k, v in FIXED.items()})
    colours.update({"X": base, "H": mix(base, (255, 255, 255), 0.45), "S": scale(base, 0.6)})
    cells = ["." * 16, "." * 16] + [".." + r + ".." for r in rows(GLYPHS[glyph])] + ["." * 16, "." * 16]
    art = outlined(paint(cells, colours), 0)
    out = tile(bg, debuff)
    opaque = art[..., 3] > 0
    out[opaque] = art[opaque]
    return out


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
    for kind in RECIPES:
        for def_id, out, recipe, debuff in icon_jobs(kind):
            if recipe is None:
                print(f"{kind[:-1]} '{def_id}' has no recipe in {RECIPES[kind].name}; skipped")
            elif selected(globs, kind, def_id) or selected(globs, kind, out):
                save(render_icon(recipe["glyph"], recipe["fg"], recipe["bg"], debuff), OUT[kind] / f"{out}.png")
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
