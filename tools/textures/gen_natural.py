"""Natural terrain materials: soils (loam, clay, sand, snow, mud, ash, silt, cloud) and grass tops.

Every generator returns (rgb, height, roughness, metallic[, alpha]) as float arrays in [0,1].
"""
import numpy as np

import synth as s


def finish(rgb, height, ao=0.45, sigma=2.5, jitter_seed=None, jitter=0.05):
    """Bakes cavity occlusion into the albedo and adds low-frequency colour drift."""
    if jitter_seed is not None:
        rgb = s.hue_jitter(rgb, jitter_seed, jitter)
    occ = s.cavity(height, sigma, 5.0)
    return np.clip(rgb * (1 - ao + ao * occ)[..., None], 0, 1)


def _white(seed, blur=0.0):
    w = s.rng(seed).random((s.SIZE, s.SIZE)).astype(np.float32)
    return s.normalize(s.blur(w, blur)) if blur > 0 else w


def _pebbles(r, seed, rgb, height, count, size=(0.05, 0.16), stone=(0.46, 0.44, 0.41)):
    """Irregular stones half buried in a soil, each with its own grey-brown tone and a dusting of soil."""
    f1, _, pid = s.voronoi(seed, count, warp_amount=3.5)
    rr = s.rng(seed + 1)
    radius = rr.uniform(*size, count)[pid] * (0.85 + 0.3 * s.spectral_noise(seed + 2, falloff=1.4))
    dome = np.clip(1 - f1 / radius, 0, 1)
    inside = dome > 0
    stone = np.array(stone)
    tone = rr.random(count)[pid]
    col = s.lerp_color(tone, np.array(s.hex_rgb(r["dark"])) * 0.7 + stone * 0.3, stone * 1.1)
    dust = s.spectral_noise(seed + 3, falloff=1.0)[..., None]
    col = col * (0.65 + 0.4 * np.sqrt(dome))[..., None] * (1 - dust * 0.25) + rgb * dust * 0.25
    rgb = np.where(inside[..., None], col, rgb)
    height = np.where(inside, np.maximum(height, 0.45 + 0.35 * np.sqrt(dome)), height)
    return rgb, height


def _loam(r, seed):
    broad = s.spectral_noise(seed, falloff=2.6)
    fine = s.spectral_noise(seed + 1, falloff=1.2)
    f1, _, cid = s.voronoi(seed + 2, 70, warp_amount=3.0)
    clod = np.sqrt(np.clip(1 - f1 * 0.9, 0, 1))
    crumbs = s.band_noise(seed + 4, 60, width=0.6)
    grit = _white(seed + 5)
    tone = s.rng(seed + 3).random(70)[cid]
    height = clod * 0.45 + broad * 0.25 + crumbs * 0.2 + fine * 0.1
    t = broad * 0.35 + tone * 0.2 + fine * 0.25 + crumbs * 0.2 + (grit - 0.5) * r.get("grit", 0.25)
    rgb = s.tone_map(t, r)
    if r.get("pebbles", 0):
        rgb, height = _pebbles(r, seed + 6, rgb, height, r["pebbles"])
    roots = np.abs(s.band_noise(seed + 7, 12, width=0.5) - 0.5) < 0.01
    rgb[roots] *= 0.62
    rgb = finish(rgb, height, 0.5, jitter_seed=seed + 8)
    return rgb, height, np.full_like(t, r.get("roughness", 0.95)) - (1 - clod) * 0.04, np.zeros_like(t)


def _clay(r, seed):
    broad = s.spectral_noise(seed, falloff=2.4)
    fine = s.spectral_noise(seed + 1, falloff=1.4)
    _, edge, cid = s.voronoi(seed + 2, 26, warp_amount=5.0)
    crack = np.clip(1 - edge / 0.035, 0, 1)
    plate = s.rng(seed + 3).random(26)[cid]
    height = 0.6 + broad * 0.2 + fine * 0.1 + plate * 0.08 - crack * 0.45
    t = broad * 0.4 + fine * 0.3 + plate * 0.2 + (_white(seed + 4) - 0.5) * r.get("grit", 0.12)
    rgb = s.tone_map(t, r) * (1 - crack * 0.35)[..., None]
    rgb = finish(rgb, height, 0.4, jitter_seed=seed + 5)
    return rgb, height, np.full_like(t, r.get("roughness", 0.9)), np.zeros_like(t)


def _sand(r, seed):
    n = s.SIZE
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    kx, ky = 7, 3
    phase = (kx * x + ky * y) + s.spectral_noise(seed, falloff=2.8) * 1.6
    w = phase % 1.0
    ripple = np.where(w < 0.72, w / 0.72, (1 - w) / 0.28)
    amp = 0.4 + 0.6 * s.spectral_noise(seed + 1, falloff=3.0)
    broad = s.spectral_noise(seed + 2, falloff=2.4)
    grains = _white(seed + 3)
    height = ripple * amp * 0.5 + broad * 0.3 + grains * 0.12
    t = broad * 0.45 + grains * r.get("grit", 0.35) + ripple * amp * 0.2
    rgb = s.tone_map(t, r)
    heavy = grains > 0.985
    rgb[heavy] *= 0.55
    shells = _white(seed + 4) > 0.9975
    rgb[shells] = np.array(s.hex_rgb(r["light"])) * 1.1
    rgb = finish(rgb, height, 0.35, jitter_seed=seed + 5, jitter=0.04)
    return rgb, height, np.full_like(t, r.get("roughness", 0.97)), np.zeros_like(t)


def _snow(r, seed):
    drifts = s.spectral_noise(seed, falloff=3.0)
    ripples = s.band_noise(seed + 1, 18, width=0.5)
    fine = s.spectral_noise(seed + 2, falloff=1.4)
    height = drifts * 0.65 + ripples * 0.2 + fine * 0.15
    rgb = s.tone_map(0.35 + height * 0.65, r)
    occ = s.cavity(height, 4.0, 6.0)
    rgb = rgb * (1 - (1 - occ)[..., None] * np.array([0.22, 0.12, 0.0]))  # blue in the hollows
    sparkle = _white(seed + 3) > 0.996
    rgb[sparkle] = 1.0
    rough = np.where(sparkle, 0.12, r.get("roughness", 0.7))
    return rgb, height, rough, np.zeros_like(height)


def _mud(r, seed):
    broad = s.spectral_noise(seed, falloff=2.4)
    blobs = s.spectral_noise(seed + 1, falloff=2.0)
    fine = s.spectral_noise(seed + 2, falloff=1.3)
    puddle = np.clip((0.38 - broad) / 0.08, 0, 1)
    height = (blobs * 0.5 + fine * 0.2 + broad * 0.3) * (1 - puddle * 0.8) + puddle * 0.15
    t = blobs * 0.4 + fine * 0.3 + broad * 0.3 + (_white(seed + 3) - 0.5) * r.get("grit", 0.15)
    rgb = s.tone_map(t, r) * (1 - puddle * 0.3)[..., None]
    rgb, height = _pebbles(r, seed + 4, rgb, height, 14, (0.05, 0.12), stone=(0.3, 0.28, 0.26))
    rgb = finish(rgb, height, 0.4, jitter_seed=seed + 5)
    rough = np.clip(r.get("roughness", 0.6) * (1 - puddle * 0.8), 0.08, 1)
    return rgb, height, rough, np.zeros_like(t)


def _powder(r, seed):
    """Ash and silt: fine, faintly layered powder with flakes."""
    broad = s.spectral_noise(seed, falloff=2.6)
    fine = s.spectral_noise(seed + 1, falloff=1.1)
    y = np.mgrid[0:s.SIZE, 0:s.SIZE][0].astype(np.float32) / s.SIZE
    layers = 0.5 + 0.5 * np.sin(2 * np.pi * (y * 9 + broad * 1.5))
    flakes = s.voronoi(seed + 2, 120)[1] < 0.05
    height = broad * 0.4 + fine * 0.35 + layers * 0.15 + flakes * 0.1
    t = broad * 0.4 + fine * 0.3 + layers * 0.15 + (_white(seed + 3) - 0.5) * r.get("grit", 0.3)
    rgb = s.tone_map(t, r)
    rgb[flakes] *= 1.12
    rgb = finish(rgb, height, 0.35, jitter_seed=seed + 4)
    return rgb, height, np.full_like(t, r.get("roughness", 0.97)), np.zeros_like(t)


def _cloud(r, seed):
    billow = 1 - np.abs(s.spectral_noise(seed, falloff=2.2) - 0.5) * 2
    puffs = s.voronoi(seed + 1, 40, warp_amount=6)[0]
    height = np.clip(billow * 0.6 + (1 - puffs) * 0.4, 0, 1)
    rgb = s.tone_map(0.3 + height * 0.7, r)
    rgb = finish(rgb, height, 0.3, sigma=5)
    return rgb, height, np.full_like(height, 1.0), np.zeros_like(height)


SOIL_STYLES = {"loam": _loam, "clay": _clay, "sand": _sand, "snow": _snow, "mud": _mud, "powder": _powder, "cloud": _cloud}


def soil(r, seed):
    return SOIL_STYLES[r.get("style", "loam")](r, seed)


def grass(r, seed):
    """Grass seen from above: thousands of tapering blades in clumps over dark soil, drawn with a height buffer."""
    n = s.SIZE
    base, dark, light = s.palette(r)
    moss = r.get("style") == "moss"
    soil_col = np.array(s.hex_rgb(r.get("soil", "#3b2a1c")))
    dirt = s.spectral_noise(seed, falloff=1.8)
    rgb = soil_col[None, None, :] * (0.7 + 0.5 * dirt[..., None])
    height = dirt * 0.12
    rr = s.rng(seed + 1)
    count = int(n * n / (2.2 if moss else 8.5))
    clumps = rr.random((max(count // 40, 1), 2)) * n
    dry = s.hex_rgb(r.get("dry", "#a09a50"))
    for i in range(count):
        if rr.random() < 0.7:
            c = clumps[rr.integers(len(clumps))]
            cx, cy = (c + rr.normal(0, n * 0.035, 2)) % n
        else:
            cx, cy = rr.random(2) * n
        length = rr.uniform(2.5, 5.0) if moss else rr.uniform(6.0, 14.0)
        width = rr.uniform(1.0, 1.6) if moss else rr.uniform(1.3, 2.3)
        ang = rr.random() * np.pi * 2
        half = int(length) + 2
        region, ox, oy = s.stamp_window(n, cx, cy, half)
        along = ox * np.cos(ang) + oy * np.sin(ang)
        across = -ox * np.sin(ang) + oy * np.cos(ang)
        t = along / length
        inside = (t >= 0) & (t <= 1) & (np.abs(across) < width * 0.5 * (1 - t * 0.8))
        h = 0.2 + 0.8 * t * (0.6 + 0.4 * rr.random())
        upd = inside & (h > height[region])
        if not upd.any():
            continue
        tone = rr.random()
        tip = np.array(s.lerp_color(np.array([[tone]]), base, light)[0, 0])
        root = np.array(dark) * (0.95 if moss else 0.75)
        if rr.random() < 0.05:
            tip = np.array(dry)
        col = root[None, None, :] + (tip - root)[None, None, :] * np.clip(t, 0, 1)[..., None]
        col *= (0.85 + 0.3 * (np.abs(across) < width * 0.18))[..., None]  # midrib catches light
        sub_rgb = rgb[region]
        sub_h = height[region]
        sub_rgb[upd] = col[upd]
        sub_h[upd] = h[upd]
        rgb[region] = sub_rgb
        height[region] = sub_h
    if "flower" in r:
        fc = np.array(s.hex_rgb(r["flower"]))
        for _ in range(26):
            cx, cy = rr.random(2) * n
            region, ox, oy = s.stamp_window(n, cx, cy, 4)
            ang = np.arctan2(oy, ox)
            rad = np.sqrt(ox * ox + oy * oy)
            petal = rad < 2.2 + 1.2 * np.cos(ang * 5)
            sub_rgb, sub_h = rgb[region], height[region]
            sub_rgb[petal] = fc
            sub_rgb[rad < 1.0] = (1.0, 0.85, 0.2)
            sub_h[petal] = 1.0
            rgb[region], height[region] = sub_rgb, sub_h
    rgb = finish(rgb, height, 0.55, sigma=1.5, jitter_seed=seed + 2, jitter=0.06)
    rough = np.where(height > 0.2, 0.78, 0.95)
    return rgb, height, rough, np.zeros_like(height)
