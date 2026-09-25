#!/usr/bin/env python3
"""Builds the terrain texture arrays from materials.json.

Outputs (assets/textures/terrain/):
  terrain_albedo.png    RGBA atlas: colour + opacity
  terrain_material.png  RGBA atlas: tangent normal X, normal Y, roughness, metallic
  layers.json           layer name -> slice index (consumed by the game and the content validator)
  *.png.import          Godot importer settings (2D texture array, VRAM compressed)
Usage: python3 tools/textures/build_textures.py [--check]
"""
import json
import pathlib
import re
import sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from PIL import Image

import generators
import synth

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
OUT = REPO / "assets" / "textures" / "terrain"
COLUMNS = 8


def referenced_layers() -> set:
    names = set()
    for path in (REPO / "data" / "blocks").glob("*.json"):
        text = re.sub(r"^\s*//.*$", "", path.read_text(encoding="utf-8"), flags=re.M)
        for block in json.loads(text).get("blocks", []):
            for key in ("all", "top", "side", "bottom"):
                if key in block.get("textures", {}):
                    names.add(block["textures"][key])
    return names


def render(recipe: dict):
    rgb, height, rough, metal, alpha = generators.generate(recipe)
    normal = synth.normal_from_height(height, recipe.get("normal_strength", 6.0))
    albedo = np.dstack([rgb, alpha])
    material = np.dstack([normal, rough, metal])
    return recipe["name"], (albedo * 255 + 0.5).astype(np.uint8), (material * 255 + 0.5).astype(np.uint8)


def import_file(name: str, rows: int, srgb: bool) -> str:
    return f"""[remap]

importer="2d_array_texture"
type="CompressedTexture2DArray"

[deps]

source_file="res://assets/textures/terrain/{name}"

[params]

compress/mode=2
compress/high_quality=false
compress/lossy_quality=0.7
compress/hdr_compression=1
compress/channel_pack=0
mipmaps/generate=true
mipmaps/limit=-1
slices/horizontal={COLUMNS}
slices/vertical={rows}
"""


def main() -> int:
    recipes = json.loads((HERE / "materials.json").read_text(encoding="utf-8"))["materials"]
    names = [r["name"] for r in recipes]
    missing = sorted(referenced_layers() - set(names))
    if missing:
        print("Missing texture recipes for:", ", ".join(missing))
        return 1
    if "--check" in sys.argv:
        print(f"{len(names)} recipes cover all referenced layers")
        return 0

    with ProcessPoolExecutor() as pool:
        rendered = {name: (a, m) for name, a, m in pool.map(render, recipes)}

    rows = (len(names) + COLUMNS - 1) // COLUMNS
    size = synth.SIZE
    albedo = np.zeros((rows * size, COLUMNS * size, 4), np.uint8)
    material = np.zeros_like(albedo)
    layers = []
    for i, name in enumerate(names):
        r, c = divmod(i, COLUMNS)
        a, m = rendered[name]
        albedo[r * size:(r + 1) * size, c * size:(c + 1) * size] = a
        material[r * size:(r + 1) * size, c * size:(c + 1) * size] = m
        layers.append({"name": name, "index": i, "type": next(x["type"] for x in recipes if x["name"] == name)})
    # Unused slices repeat the "missing" layer so sampling never returns garbage.
    missing_index = names.index("missing")
    for i in range(len(names), rows * COLUMNS):
        r, c = divmod(i, COLUMNS)
        mr, mc = divmod(missing_index, COLUMNS)
        albedo[r * size:(r + 1) * size, c * size:(c + 1) * size] = albedo[mr * size:(mr + 1) * size, mc * size:(mc + 1) * size]
        material[r * size:(r + 1) * size, c * size:(c + 1) * size] = material[mr * size:(mr + 1) * size, mc * size:(mc + 1) * size]

    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray(albedo, "RGBA").save(OUT / "terrain_albedo.png", optimize=True)
    Image.fromarray(material, "RGBA").save(OUT / "terrain_material.png", optimize=True)
    (OUT / "layers.json").write_text(json.dumps({"columns": COLUMNS, "rows": rows, "size": size, "layers": layers}, indent=1) + "\n", encoding="utf-8")
    for file, srgb in (("terrain_albedo.png", True), ("terrain_material.png", False)):
        (OUT / f"{file}.import").write_text(import_file(file, rows, srgb), encoding="utf-8")
    print(f"wrote {len(names)} layers ({rows}x{COLUMNS} atlas) to {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
