"""Material generators: recipe dict -> (albedo rgb, height, roughness, metallic, alpha), all floats in [0, 1].

Generators live in gen_natural (soils, grass), gen_rock (stone, ores), gen_built (bricks, planks, glass, metal,
cloth) and gen_plants (leaves, bark). Recipes: materials.json (terrain) and props.json (models).
"""
import zlib

import numpy as np

import gen_built
import gen_natural
import gen_plants
import gen_rock


def seed_of(name: str) -> int:
    return zlib.crc32(name.encode("utf-8"))


GENERATORS = {
    "soil": gen_natural.soil, "grass": gen_natural.grass,
    "rock": gen_rock.rock, "veined": gen_rock.veined, "speckled": gen_rock.speckled, "ore": gen_rock.ore,
    "planks": gen_built.planks, "bricks": gen_built.bricks, "translucent": gen_built.translucent, "web": gen_built.web, "rope": gen_built.rope,
    "hive": gen_built.hive, "missing": gen_built.missing, "metal": gen_built.metal, "cloth": gen_built.cloth,
    "leather": gen_built.leather,
    "leaves": gen_plants.leaves, "foliage": gen_plants.foliage, "bark": gen_plants.bark, "cap": gen_plants.cap,
    "ribbed": gen_plants.ribbed, "sprig": gen_plants.sprig,
}

# Types whose alpha is opacity (cut-out and translucent surfaces); every other layer stores height in alpha.
ALPHA_TYPES = {"translucent", "leaves", "web", "sprig", "rope"}


def generate(recipe: dict):
    seed = seed_of(recipe["name"])
    result = GENERATORS[recipe["type"]](recipe, seed)
    if len(result) == 4:
        rgb, height, rough, metal = result
        alpha = np.ones_like(height)
    else:
        rgb, height, rough, metal, alpha = result
    return np.clip(rgb, 0, 1), np.clip(height, 0, 1), np.clip(rough, 0, 1), np.clip(metal, 0, 1), np.clip(alpha, 0, 1)
