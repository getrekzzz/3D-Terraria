"""Material generators: recipe dict -> (albedo RGBA [0,1], height [0,1], roughness [0,1], metallic [0,1])."""
import zlib

import numpy as np

import synth as s


def seed_of(name: str) -> int:
    return zlib.crc32(name.encode("utf-8"))


def soil(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    n = s.spectral_noise(seed, falloff=2.2) * 0.65 + s.band_noise(seed + 1, 40) * 0.35
    grit = s.rng(seed + 2).random((s.SIZE, s.SIZE))
    t = np.clip(n + (grit - 0.5) * r.get("grit", 0.25), 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)])
    pebbles = (s.voronoi(seed + 3, r.get("pebbles", 0), )[0] < 0.18) if r.get("pebbles", 0) else np.zeros_like(t, bool)
    rgb[pebbles] = rgb[pebbles] * 0.8 + np.array(light) * 0.2
    height = t * 0.6 + pebbles * 0.4
    return rgb, height, np.full_like(t, r.get("roughness", 0.95)), np.zeros_like(t)


def rock(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    f1, edge, cid = s.voronoi(seed, r.get("cells", 18))
    cell_tone = s.rng(seed + 1).random(r.get("cells", 18))[cid]
    detail = s.spectral_noise(seed + 2, falloff=1.8)
    broad = s.spectral_noise(seed + 3, falloff=3.0)
    streaks = s.band_noise(seed + 4, 22, width=0.5)
    strata = 0.5 + 0.5 * np.sin(np.linspace(0, 2 * np.pi * r.get("strata_bands", 6), s.SIZE))[:, None] * np.ones((1, s.SIZE))
    t = cell_tone * 0.18 + detail * 0.4 + broad * 0.27 + streaks * 0.15 + strata * r.get("strata", 0.0) * 0.3
    t = np.clip((t - t.min()) / max(t.max() - t.min(), 1e-6), 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.55, base), (1.0, light)])
    warp = s.spectral_noise(seed + 5, falloff=2.0) * 0.05
    crack = np.clip(1 - (edge + warp) / 0.035, 0, 1) * r.get("cracks", 0.5)
    rgb *= (1 - crack * 0.35)[..., None]
    height = np.clip(0.5 + (1 - f1) * 0.15 - crack * 0.4 + detail * 0.25 + broad * 0.15, 0, 1)
    return rgb, height, np.clip(r.get("roughness", 0.85) - crack * 0.1, 0, 1), np.zeros_like(t)


def veined(r, seed):
    rgb, height, rough, metal = rock(r, seed)
    vein_color = s.hex_rgb(r["vein"])
    v = s.band_noise(seed + 9, 6, width=0.7)
    veins = np.clip(1 - np.abs(v - 0.5) / r.get("vein_width", 0.03), 0, 1)
    rgb = rgb * (1 - veins[..., None]) + np.array(vein_color) * veins[..., None]
    return rgb, height, rough * (1 - veins * 0.3), metal


def speckled(r, seed):
    rgb, height, rough, metal = rock(r, seed)
    grains = s.rng(seed + 5).random((s.SIZE, s.SIZE))
    for key, thresh in (("speck_a", 0.93), ("speck_b", 0.97)):
        if key in r:
            m = grains > thresh if key == "speck_a" else grains < 1 - thresh
            rgb[m] = s.hex_rgb(r[key])
    return rgb, height, rough, metal


def ore(r, seed):
    rgb, height, rough, metal = rock(r, seed)
    f1, _, _ = s.voronoi(seed + 11, r.get("nuggets", 22))
    blob = s.spectral_noise(seed + 12, falloff=2.5)
    jag = s.spectral_noise(seed + 13, falloff=1.2)
    size = r.get("nugget_size", 0.32)
    shape = f1 + (jag - 0.5) * 0.35
    nug = (shape < size) & (blob > 0.3)
    shine = np.clip(1 - shape / size, 0, 1) * 0.6 + jag * 0.4
    ore_rgb = s.gradient_map(shine, [(0, s.shade(s.hex_rgb(r["ore"]), 0.6)), (1, s.hex_rgb(r["ore"]))])
    rgb[nug] = ore_rgb[nug]
    height = np.where(nug, height + shine * 0.3, height)
    metal = np.where(nug, r.get("metallic", 0.8), 0.0)
    rough = np.where(nug, 0.35, rough)
    return rgb, np.clip(height, 0, 1), rough, metal


def grass(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    n = s.spectral_noise(seed, falloff=1.6)
    blades = s.band_noise(seed + 1, 70, width=0.4)
    t = np.clip(n * 0.5 + blades * 0.5, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)])
    if "flower" in r:
        m = s.rng(seed + 3).random((s.SIZE, s.SIZE)) > 0.996
        rgb[m] = s.hex_rgb(r["flower"])
    return rgb, t, np.full_like(t, 0.9), np.zeros_like(t)


def planks(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    idx, u, v, gap = s.planks(r.get("count", 4))
    tone = s.rng(seed).random(64)[idx % 64]
    grain = s.band_noise(seed + 1, 18, width=0.6)
    grain = np.roll(grain, 0, 0)
    stretched = np.repeat(grain[::8, :], 8, axis=0)[: s.SIZE]
    t = np.clip(tone * 0.4 + stretched * 0.6, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)])
    seam = np.clip(1 - gap / 0.05, 0, 1)
    rgb *= (1 - seam * 0.6)[..., None]
    height = np.clip(0.7 - seam * 0.6 + stretched * 0.15, 0, 1)
    return rgb, height, np.full_like(t, 0.75), np.zeros_like(t)


def bricks(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    mortar_c = s.hex_rgb(r.get("mortar", "#6a655c"))
    mask, bid, u, v, edge = s.bricks(r.get("rows", 4), r.get("cols", 2), r.get("mortar_width", 0.06))
    tone = s.rng(seed).random(512)[bid % 512]
    noise = s.spectral_noise(seed + 1, falloff=1.7)
    t = np.clip(tone * 0.55 + noise * 0.45, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)])
    wear = np.clip(edge / 0.12, 0, 1)
    rgb *= (0.8 + 0.2 * wear)[..., None]
    rgb = rgb * mask[..., None] + np.array(mortar_c) * (1 - mask[..., None])
    height = mask * (0.6 + 0.25 * wear + noise * 0.15)
    return rgb, height, np.where(mask > 0, r.get("roughness", 0.8), 0.95), np.where(mask > 0, r.get("metallic", 0.0), 0.0)


def translucent(r, seed):
    base, light = s.hex_rgb(r["base"]), s.hex_rgb(r["light"])
    n = s.spectral_noise(seed, falloff=2.4)
    f1, edge, _ = s.voronoi(seed + 1, r.get("facets", 10))
    t = np.clip(n * 0.5 + (1 - f1) * 0.5, 0, 1)
    rgb = s.gradient_map(t, [(0, base), (1, light)])
    rgb[edge < 0.03] = light
    alpha = np.full_like(t, r.get("alpha", 0.5))
    if r.get("frame"):
        y, x = np.mgrid[0:s.SIZE, 0:s.SIZE] / s.SIZE
        border = (np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y)) < 0.05)
        rgb[border] = s.hex_rgb(r["frame"])
        alpha[border] = 1.0
    return rgb, t * 0.3, np.full_like(t, 0.08), np.zeros_like(t), alpha


def leaves(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    f1, edge, cid = s.voronoi(seed, 90)
    tone = s.rng(seed + 1).random(90)[cid]
    rgb = s.gradient_map(tone, [(0, dark), (0.5, base), (1, light)])
    alpha = ((f1 < 0.62) & (edge > 0.02)).astype(np.float32)
    return rgb, (1 - f1), np.full_like(f1, 0.8), np.zeros_like(f1), alpha


def web(r, seed):
    y, x = np.mgrid[0:s.SIZE, 0:s.SIZE].astype(np.float32) / s.SIZE
    ang = np.arctan2(y - 0.5, x - 0.5)
    rad = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2)
    spokes = np.abs(np.sin(ang * 6)) < 0.04
    rings = np.abs(np.sin(rad * 60)) < 0.12
    alpha = (spokes | rings).astype(np.float32) * 0.85
    rgb = np.ones((s.SIZE, s.SIZE, 3), np.float32) * np.array(s.hex_rgb(r["base"]))
    return rgb, alpha, np.full_like(alpha, 0.6), np.zeros_like(alpha), alpha


def hive(r, seed):
    base, dark, light = (s.hex_rgb(r[k]) for k in ("base", "dark", "light"))
    y, x = np.mgrid[0:s.SIZE, 0:s.SIZE].astype(np.float32) / s.SIZE * 8
    q = (x * np.sqrt(3) / 3 - y / 3)
    rr = y * 2 / 3
    frac = np.abs((q * 3) % 1 - 0.5) + np.abs((rr * 3) % 1 - 0.5)
    t = np.clip(frac, 0, 1)
    rgb = s.gradient_map(t, [(0, light), (0.6, base), (1, dark)])
    return rgb, 1 - t, np.full_like(t, 0.5), np.zeros_like(t)


def missing(r, seed):
    y, x = np.mgrid[0:s.SIZE, 0:s.SIZE] // 32
    checker = ((x + y) % 2).astype(np.float32)
    rgb = s.lerp_color(checker, (1, 0, 1), (0.1, 0.1, 0.1))
    return rgb, checker * 0.5, np.full_like(checker, 0.9), np.zeros_like(checker)


GENERATORS = {
    "soil": soil, "rock": rock, "veined": veined, "speckled": speckled, "ore": ore, "grass": grass,
    "planks": planks, "bricks": bricks, "translucent": translucent, "leaves": leaves, "web": web,
    "hive": hive, "missing": missing,
}


def generate(recipe: dict):
    seed = seed_of(recipe["name"])
    result = GENERATORS[recipe["type"]](recipe, seed)
    if len(result) == 4:
        rgb, height, rough, metal = result
        alpha = np.ones_like(height)
    else:
        rgb, height, rough, metal, alpha = result
    return np.clip(rgb, 0, 1), np.clip(height, 0, 1), np.clip(rough, 0, 1), np.clip(metal, 0, 1), np.clip(alpha, 0, 1)
