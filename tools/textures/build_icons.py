#!/usr/bin/env python3
"""Isometric cube icons for block items, drawn from the terrain texture atlas (top/side layers per block).

Usage: python3 tools/textures/build_icons.py [--check]
Output: assets/icons/items/<block>.png (128x128). Model-based item icons come from tools/blender/build_all.py.
"""
import json
import pathlib
import sys

import numpy as np
from PIL import Image, ImageDraw

from content_data import load_json

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
TERRAIN = REPO / "assets" / "textures" / "terrain"
OUT = REPO / "assets" / "icons" / "items"
SIZE = 128

# Face parallelograms (origin corner, u edge, v edge) in icon pixels, with brightness.
FACES = {
    "top": ((64, 10), (54, 27), (-54, 27), 1.0),
    "left": ((10, 37), (54, 27), (0, 56), 0.78),
    "right": ((64, 64), (54, -27), (0, 56), 0.6),
}


def block_items():
    manifest = load_json(REPO / "data" / "manifest.json")
    blocks, items = {}, set()
    for rel in manifest["files"]:
        doc = load_json(REPO / "data" / rel)
        for b in doc.get("blocks", []):
            blocks[b["id"]] = b
        items.update(i["id"] for i in doc.get("items", []))
    return {bid: b for bid, b in blocks.items() if b.get("autoItem", True) and "drop" not in b and bid not in items}


# Layer types whose albedo alpha is opacity; every other layer stores height there (drawn opaque).
ALPHA_TYPES = {"translucent", "leaves", "web", "rope"}


def layer_tile(atlas: Image.Image, meta, name: str) -> Image.Image:
    layers = {layer["name"]: layer for layer in meta["layers"]}
    if name not in layers:
        raise KeyError(f"texture layer '{name}' missing from terrain atlas")
    row, col = divmod(layers[name]["index"], meta["columns"])
    s = meta["size"]
    tile = atlas.crop((col * s, row * s, (col + 1) * s, (row + 1) * s))
    if layers[name]["type"] not in ALPHA_TYPES:
        tile.putalpha(255)
    return tile


def face(tile: Image.Image, origin, u, v, shade) -> Image.Image:
    """Maps a square texture onto a parallelogram (affine), shaded, on a transparent canvas."""
    s = tile.size[0]
    # Output pixel p = origin + a*u + b*v (a, b in [0,1]) -> texture (a*s, b*s). Invert the 2x2 basis.
    m = np.array([[u[0], v[0]], [u[1], v[1]]], dtype=np.float64)
    inv = np.linalg.inv(m) * s
    ox, oy = origin
    coeffs = (inv[0, 0], inv[0, 1], -(inv[0, 0] * ox + inv[0, 1] * oy),
              inv[1, 0], inv[1, 1], -(inv[1, 0] * ox + inv[1, 1] * oy))
    out = tile.transform((SIZE, SIZE), Image.AFFINE, coeffs, resample=Image.BILINEAR)
    mask = Image.new("L", (SIZE, SIZE), 0)
    corners = [origin, (ox + u[0], oy + u[1]), (ox + u[0] + v[0], oy + u[1] + v[1]), (ox + v[0], oy + v[1])]
    ImageDraw.Draw(mask).polygon(corners, fill=255)
    arr = np.asarray(out).astype(np.float32)
    arr[..., :3] *= shade
    arr[..., 3] = arr[..., 3] * (np.asarray(mask) / 255.0)
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA")


def icon(atlas, meta, block) -> Image.Image:
    tex = block.get("textures", {})
    top = tex.get("top") or tex.get("all") or tex.get("side")
    side = tex.get("side") or tex.get("all") or tex.get("top")
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    for name, (origin, u, v, shade) in FACES.items():
        tile = layer_tile(atlas, meta, top if name == "top" else side)
        canvas.alpha_composite(face(tile, origin, u, v, shade))
    return canvas


def main() -> int:
    blocks = block_items()
    meta = json.loads((TERRAIN / "layers.json").read_text(encoding="utf-8"))
    names = {layer["name"] for layer in meta["layers"]}
    missing = [f"{bid}: {t}" for bid, b in blocks.items() for t in b.get("textures", {}).values() if t not in names]
    if missing:
        print("Blocks reference missing layers:", ", ".join(missing))
        return 1
    if "--check" in sys.argv:
        print(f"{len(blocks)} block icons resolvable")
        return 0
    atlas = Image.open(TERRAIN / "terrain_albedo.png").convert("RGBA")
    OUT.mkdir(parents=True, exist_ok=True)
    for bid, block in sorted(blocks.items()):
        icon(atlas, meta, block).save(OUT / f"{bid}.png", optimize=True)
    print(f"wrote {len(blocks)} block icons to {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
