"""Rock materials: chiselled faceted stone with fractures and strata, ice, veined and speckled stone, ores and gems."""
import numpy as np

import synth as s
from gen_natural import finish


def rock(r, seed):
    """Stone built from tilted facets (chisel planes), branching fractures, strata and mineral grain."""
    if r.get("style") == "ice":
        return ice(r, seed)
    n = s.SIZE
    cells = r.get("cells", 18)
    facets = cells * 3
    f1, edge, cid, ox, oy = s.voronoi_full(seed, facets, warp_amount=4.0)
    rr = s.rng(seed + 1)
    relief = r.get("facet", 1.0)
    tilt = (rr.normal(0, 0.55, facets)[cid] * ox + rr.normal(0, 0.55, facets)[cid] * oy) * relief
    facet_tone = rr.random(facets)[cid]
    _, cedge, _ = s.voronoi(seed + 2, cells, warp_amount=7.0)
    width = 0.028 + 0.02 * s.spectral_noise(seed + 3, falloff=2.0)
    # Fractures fade in and out along their length instead of outlining every cell (no crazy paving).
    reach = np.clip((s.spectral_noise(seed + 8, falloff=2.4) - 0.38) / 0.25, 0, 1)
    crack = np.clip(1 - cedge / width, 0, 1) * r.get("cracks", 0.5) * reach
    seams = np.clip(1 - edge / 0.025, 0, 1) * 0.35 * relief
    y = np.mgrid[0:n, 0:n][0].astype(np.float32) / n
    strata = 0.5 + 0.5 * np.sin(2 * np.pi * (y * r.get("strata_bands", 6) + s.spectral_noise(seed + 4, falloff=2.6) * 1.2))
    grain = s.normalize(s.blur(rr.random((n, n)), 0.6))
    broad = s.spectral_noise(seed + 5, falloff=2.8)
    mid = s.spectral_noise(seed + 6, falloff=1.9)
    strata_amount = r.get("strata", 0.0)
    height = 0.55 + tilt * 0.16 + mid * 0.16 + broad * 0.1 + grain * 0.05 - crack * 0.45 - seams * 0.1 + strata * strata_amount * 0.1
    t = broad * 0.3 + mid * 0.22 + facet_tone * 0.15 + grain * 0.15 + tilt * 0.1 + strata * strata_amount * 0.3
    rgb = s.tone_map(s.normalize(t), r)
    conv = s.convexity(height, 1.5)
    rgb *= (1 + np.clip(conv * 3.0, -0.25, 0.25))[..., None]  # worn edges catch light, hollows collect grime
    rgb *= (1 - crack * 0.6)[..., None]
    rgb = finish(rgb, height, 0.45, jitter_seed=seed + 7, jitter=0.045)
    rough = np.clip(r.get("roughness", 0.85) + (grain - 0.5) * 0.12 + crack * 0.1, 0.02, 1)
    return rgb, np.clip(height, 0, 1), rough, np.zeros_like(height)


def ice(r, seed):
    """Glassy ice: deep colour variation, white fracture planes and trapped bubbles over a smooth surface."""
    n = s.SIZE
    depth = s.spectral_noise(seed, falloff=2.6)
    _, edge, _ = s.voronoi(seed + 1, r.get("cells", 8), warp_amount=8.0)
    _, edge2, _ = s.voronoi(seed + 2, r.get("cells", 8) * 4, warp_amount=4.0)
    crack = np.clip(1 - edge / 0.02, 0, 1) * r.get("cracks", 0.8)
    hairline = np.clip(1 - edge2 / 0.012, 0, 1) * 0.5
    rgb = s.tone_map(0.2 + depth * 0.6, r)
    rgb = rgb + (crack + hairline)[..., None] * 0.35
    rr = s.rng(seed + 3)
    for _ in range(60):
        cx, cy = rr.random(2) * n
        rad = rr.uniform(0.6, 2.2)
        region, ox, oy = s.stamp_window(n, cx, cy, 3)
        d = np.sqrt(ox * ox + oy * oy)
        ring = np.clip(1 - np.abs(d - rad) / 0.6, 0, 1)
        sub = rgb[region]
        sub += ring[..., None] * 0.35
        rgb[region] = sub
    height = 0.6 + depth * 0.1 - crack * 0.15
    return np.clip(rgb, 0, 1), height, np.full_like(depth, r.get("roughness", 0.12)) + crack * 0.2, np.zeros_like(depth)


def veined(r, seed):
    """Marble and obsidian: stone crossed by branching veins (warped iso-lines), polished."""
    rgb, height, rough, metal = rock(r, seed)
    vein_color = np.array(s.hex_rgb(r["vein"]))
    main = s.warp(s.band_noise(seed + 9, 5, width=0.7), seed + 10, 10.0)
    minor = s.warp(s.band_noise(seed + 11, 14, width=0.6), seed + 12, 5.0)
    w = r.get("vein_width", 0.03)
    veins = np.maximum(np.clip(1 - np.abs(main - 0.5) / w, 0, 1), np.clip(1 - np.abs(minor - 0.5) / (w * 0.45), 0, 1) * 0.6)
    rgb = rgb * (1 - veins[..., None]) + vein_color * veins[..., None]
    return rgb, height, rough * (1 - veins * 0.3), metal


def speckled(r, seed):
    """Granite (interlocking mineral crystals) or fossil-bearing stone (spiral shells)."""
    rgb, height, rough, metal = rock(r, seed)
    n = s.SIZE
    if "speck_b" in r:
        f1, _, cid = s.voronoi(seed + 20, 1100, warp_amount=1.5)
        pick = s.rng(seed + 21).random(1100)[cid]
        a, b = np.array(s.hex_rgb(r["speck_a"])), np.array(s.hex_rgb(r["speck_b"]))
        crystal = np.where((pick > 0.8)[..., None], rgb * 0.35 + a * 0.65, np.where((pick < 0.12)[..., None], rgb * 0.5 + b * 0.5, rgb))
        rgb = crystal * (0.92 + 0.16 * (1 - f1))[..., None]
        rough = np.where(pick > 0.78, rough * 0.6, rough)
        return rgb, height, rough, metal
    shell = np.array(s.hex_rgb(r["speck_a"]))
    rr = s.rng(seed + 22)
    for _ in range(7):
        cx, cy = rr.random(2) * n
        size = rr.uniform(8, 18)
        region, ox, oy = s.stamp_window(n, cx, cy, int(size) + 2)
        rad = np.sqrt(ox * ox + oy * oy)
        ang = np.arctan2(oy, ox)
        turns = np.log(np.maximum(rad, 0.5) / size) / 0.35 + ang / (2 * np.pi)
        ribs = np.clip(1 - np.abs((turns % 1.0) - 0.5) / 0.12, 0, 1) * (rad < size)
        body = (rad < size).astype(np.float32)
        sub_rgb, sub_h = rgb[region], height[region]
        sub_rgb = sub_rgb * (1 - body[..., None] * 0.6) + shell * body[..., None] * 0.6
        sub_rgb *= (1 - ribs * 0.35)[..., None]
        rgb[region] = sub_rgb
        height[region] = sub_h + body * 0.15 - ribs * 0.08
    return rgb, height, rough, metal


def ore(r, seed):
    """Host rock with embedded ore: crystalline, faceted nuggets with an oxidised halo; gems are clear and glossy."""
    rgb, height, rough, metal = rock(r, seed)
    gem = r.get("metallic", 0.8) < 0.3
    f1, _, _ = s.voronoi(seed + 11, r.get("nuggets", 22), warp_amount=4.0)
    blob = s.spectral_noise(seed + 12, falloff=2.5)
    jag = s.spectral_noise(seed + 13, falloff=1.2)
    size = r.get("nugget_size", 0.32)
    shape = f1 + (jag - 0.5) * (0.15 if gem else 0.35)
    nug = (shape < size) & (blob > (0.2 if gem else 0.3))
    facets = 120 if gem else 260
    _, cedge, ccid, cox, coy = s.voronoi_full(seed + 14, facets)
    rr = s.rng(seed + 15)
    bright = rr.random(facets)[ccid]
    tilt = rr.normal(0, 0.6, facets)[ccid] * cox + rr.normal(0, 0.6, facets)[ccid] * coy
    core = np.clip(1 - shape / size, 0, 1)
    col = np.array(s.hex_rgb(r["ore"]))
    ore_rgb = s.gradient_map(np.clip(bright * 0.6 + core * 0.4, 0, 1),
                             [(0, s.shade(col, 0.35)), (0.55, col), (1.0, s.shade(col, 1.3))])
    ore_rgb *= np.clip(0.75 + tilt * 0.5, 0.45, 1.3)[..., None]  # each crystal face catches light differently
    ore_rgb *= (0.55 + 0.45 * np.clip(core * 3, 0, 1))[..., None]  # darker rim where the ore meets the rock
    if gem:
        # Crystals: sharp facet edges, a bright inner glint on some faces, saturated depth on others.
        ore_rgb *= (1 - np.clip(1 - cedge / 0.05, 0, 1) * 0.6)[..., None]
        glint = (bright > 0.82) & (core > 0.3)
        ore_rgb[glint] = np.minimum(ore_rgb[glint] * 1.6 + 0.2, 1.0)
    halo = np.clip(1 - (shape - size) / 0.1, 0, 1) * (~nug) * (blob > 0.25)
    rgb = rgb * (1 - halo[..., None] * 0.35) + col * halo[..., None] * 0.18
    rgb = np.where(nug[..., None], ore_rgb, rgb)
    height = np.where(nug, height + 0.2 + core * 0.2 + tilt * 0.08, height)
    metal = np.where(nug, r.get("metallic", 0.8), 0.0)
    rough = np.where(nug, (0.06 if gem else 0.28) + bright * 0.12, rough)
    return rgb, np.clip(height, 0, 1), rough, metal
