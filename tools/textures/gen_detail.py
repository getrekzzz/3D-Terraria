"""Detail maps: a few generic, fine-scale surface grains tiled much smaller than the base textures so terrain stays
crisp up close (rock grain, soil crumbs, sand, grass fibres, wood grain, snow, polished and porous surfaces).

Each layer is RGBA: tangent normal x, normal y, height, 1. Layer order is the DETAIL_LAYERS list.
"""
import numpy as np

import synth as s
from gen_built import streaks

SIZE = 512
DETAIL_LAYERS = ["rock", "soil", "sand", "grass", "wood", "snow", "smooth", "porous"]


def _rock(seed):
    grain = s.normalize(s.blur(s.rng(seed).random((SIZE, SIZE)), 0.7))
    _, edge, cid, ox, oy = s.voronoi_full(seed + 1, 500, SIZE, warp_amount=3.0)
    rr = s.rng(seed + 2)
    facets = rr.normal(0, 0.4, 500)[cid] * ox + rr.normal(0, 0.4, 500)[cid] * oy
    micro = np.clip(1 - edge / 0.04, 0, 1)
    return grain * 0.45 + facets * 0.4 + s.spectral_noise(seed + 3, SIZE, 1.6) * 0.3 - micro * 0.2


def _soil(seed):
    f1, _, _ = s.voronoi(seed, 1800, SIZE, warp_amount=1.5)
    crumbs = np.sqrt(np.clip(1 - f1, 0, 1))
    return crumbs * 0.6 + s.spectral_noise(seed + 1, SIZE, 1.4) * 0.4


def _sand(seed):
    grains = s.normalize(s.blur(s.rng(seed).random((SIZE, SIZE)), 0.8))
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    ripple = 0.5 + 0.5 * np.sin(2 * np.pi * (x * 11 + y * 4 + s.spectral_noise(seed + 1, SIZE, 2.6) * 2))
    return grains * 0.7 + ripple * 0.3


def _grass(seed):
    h = np.zeros((SIZE, SIZE), np.float32)
    rr = s.rng(seed)
    for _ in range(9000):
        cx, cy = rr.random(2) * SIZE
        ang = rr.random() * np.pi * 2
        length = rr.uniform(5, 12)
        region, ox, oy = s.stamp_window(SIZE, cx, cy, int(length) + 1)
        along = ox * np.cos(ang) + oy * np.sin(ang)
        across = -ox * np.sin(ang) + oy * np.cos(ang)
        t = along / length
        inside = (t >= 0) & (t <= 1) & (np.abs(across) < 0.8)
        sub = h[region]
        sub[inside] = np.maximum(sub[inside], (0.3 + 0.7 * t)[inside])
        h[region] = sub
    return h


def _wood(seed):
    return streaks(seed, 60, 0.8, SIZE) * 0.6 + streaks(seed + 1, 20, 0.5, SIZE) * 0.4


def _snow(seed):
    f1, _, _ = s.voronoi(seed, 700, SIZE, warp_amount=2.0)
    return np.sqrt(np.clip(1 - f1, 0, 1)) * 0.5 + s.spectral_noise(seed + 1, SIZE, 2.0) * 0.5


def _smooth(seed):
    return s.spectral_noise(seed, SIZE, 2.4) * 0.6 + s.normalize(s.blur(s.rng(seed + 1).random((SIZE, SIZE)), 1.2)) * 0.4


def _porous(seed):
    pits = s.normalize(s.blur(s.rng(seed).random((SIZE, SIZE)), 0.6))
    pores = np.where(pits > 0.82, -0.6, 0.0)
    return s.spectral_noise(seed + 1, SIZE, 1.5) * 0.7 + pits * 0.3 + pores


BUILDERS = {"rock": _rock, "soil": _soil, "sand": _sand, "grass": _grass, "wood": _wood, "snow": _snow,
            "smooth": _smooth, "porous": _porous}

# Default detail layer and strength per (type, style); recipes may override with "detail" and "detail_strength".
DEFAULTS = {
    ("soil", "loam"): ("soil", 0.7), ("soil", "clay"): ("porous", 0.6), ("soil", "sand"): ("sand", 0.7),
    ("soil", "snow"): ("snow", 0.6), ("soil", "mud"): ("soil", 0.4), ("soil", "powder"): ("sand", 0.6),
    ("soil", "cloud"): ("snow", 0.3), ("grass", None): ("grass", 0.8), ("rock", None): ("rock", 0.8),
    ("rock", "ice"): ("smooth", 0.3), ("speckled", None): ("rock", 0.7), ("veined", None): ("smooth", 0.4),
    ("ore", None): ("rock", 0.7), ("planks", None): ("wood", 0.6), ("bricks", None): ("porous", 0.6),
    ("hive", None): ("smooth", 0.3),
}


def detail_for(recipe: dict):
    """(detail layer index, strength) for a terrain recipe; strength 0 disables detail."""
    if "detail" in recipe:
        return DETAIL_LAYERS.index(recipe["detail"]), recipe.get("detail_strength", 0.7)
    style = recipe.get("style")
    kind = recipe["type"]
    if kind == "soil":
        style = style or "loam"
    name, strength = DEFAULTS.get((kind, style), DEFAULTS.get((kind, None), ("rock", 0.0)))
    return DETAIL_LAYERS.index(name), strength


def build():
    """All detail layers as RGBA uint8 tiles, in DETAIL_LAYERS order."""
    tiles = []
    for i, name in enumerate(DETAIL_LAYERS):
        height = s.normalize(BUILDERS[name](9000 + i * 17))
        normal = s.normal_from_height(height, 7.0)
        rgba = np.dstack([normal, height, np.ones_like(height)])
        tiles.append((rgba * 255 + 0.5).astype(np.uint8))
    return tiles
