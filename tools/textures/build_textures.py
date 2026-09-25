#!/usr/bin/env python3
"""Builds the terrain texture arrays from materials.json.

Outputs per set (assets/textures/<set>/):
  <set>_albedo.png    RGBA atlas: colour + opacity
  <set>_material.png  RGBA atlas: tangent normal X, normal Y, roughness, metallic
  layers.json         layer name -> slice index (consumed by the game and the content validator)
  *.png.import        Godot importer settings (2D texture array, VRAM compressed)
Sets: terrain (materials.json -> assets/textures/terrain), props (props.json -> assets/textures/props).
Usage: python3 tools/textures/build_textures.py [--set=terrain|props] [--check]
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
COLUMNS = 8

# Texture sets: recipe file -> output folder. Terrain layers are referenced by blocks; prop layers by Blender
# materials named "prop:<layer>".
SETS = {
    "terrain": ("materials.json", "terrain", "terrain"),
    "props": ("props.json", "props", "props"),
}


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


def import_file(folder: str, name: str, rows: int, srgb: bool) -> str:
    return f"""[remap]

importer="2d_array_texture"
type="CompressedTexture2DArray"

[deps]

source_file="res://assets/textures/{folder}/{name}"

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
    chosen = [a.split("=", 1)[1] for a in sys.argv if a.startswith("--set=")] or list(SETS)
    for name in chosen:
        if name not in SETS:
            print(f"unknown texture set '{name}' (known: {', '.join(SETS)})")
            return 1
        code = build_set(name)
        if code:
            return code
    return 0


def build_set(set_name: str) -> int:
    recipe_file, folder, prefix = SETS[set_name]
    out = REPO / "assets" / "textures" / folder
    recipes = json.loads((HERE / recipe_file).read_text(encoding="utf-8"))["materials"]
    names = [r["name"] for r in recipes]
    if len(set(names)) != len(names):
        print(f"{set_name}: duplicate recipe names")
        return 1
    if set_name == "terrain":
        missing = sorted(referenced_layers() - set(names))
        if missing:
            print("Missing texture recipes for:", ", ".join(missing))
            return 1
    if "--check" in sys.argv:
        print(f"{set_name}: {len(names)} recipes OK")
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
        recipe = next(x for x in recipes if x["name"] == name)
        entry = {"name": name, "index": i, "type": recipe["type"]}
        if "sway" in recipe:
            entry["sway"] = recipe["sway"]  # foliage wind sway used by the prop shader
        layers.append(entry)
    # Unused slices repeat the "missing" layer so sampling never returns garbage.
    missing_index = names.index("missing")
    for i in range(len(names), rows * COLUMNS):
        r, c = divmod(i, COLUMNS)
        mr, mc = divmod(missing_index, COLUMNS)
        albedo[r * size:(r + 1) * size, c * size:(c + 1) * size] = albedo[mr * size:(mr + 1) * size, mc * size:(mc + 1) * size]
        material[r * size:(r + 1) * size, c * size:(c + 1) * size] = material[mr * size:(mr + 1) * size, mc * size:(mc + 1) * size]

    out.mkdir(parents=True, exist_ok=True)
    Image.fromarray(albedo, "RGBA").save(out / f"{prefix}_albedo.png", optimize=True)
    Image.fromarray(material, "RGBA").save(out / f"{prefix}_material.png", optimize=True)
    (out / "layers.json").write_text(json.dumps({"columns": COLUMNS, "rows": rows, "size": size, "layers": layers}, indent=1) + "\n", encoding="utf-8")
    for file, srgb in ((f"{prefix}_albedo.png", True), (f"{prefix}_material.png", False)):
        (out / f"{file}.import").write_text(import_file(folder, file, rows, srgb), encoding="utf-8")
    print(f"{set_name}: wrote {len(names)} layers ({rows}x{COLUMNS} atlas) to {out.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
