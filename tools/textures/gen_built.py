"""Crafted materials: bricks, planks, honeycomb, glass, webs, metal, cloth and the missing-texture checker."""
import numpy as np

import synth as s
from gen_natural import finish


def streaks(seed, sx, sy, size=None):
    """Periodic noise stretched along x (wood grain, brushed metal): white noise blurred anisotropically."""
    n = size or s.SIZE
    white = s.rng(seed).standard_normal((n, n))
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(n)[None, :]
    k = np.exp(-2 * np.pi ** 2 * ((sx * fx) ** 2 + (sy * fy) ** 2))
    return s.normalize(np.real(np.fft.ifft2(np.fft.fft2(white) * k)))


def bricks(r, seed):
    """Irregular hand-laid bricks: per-brick tone and surface, chipped bevelled edges, recessed sandy mortar, grime.

    Options: rows, cols, mortar_width, stagger (offset every other row; default on), bevel (edge rounding width, 0.07).
    """
    n = s.SIZE
    rows, cols, mw = r.get("rows", 4), r.get("cols", 2), r.get("mortar_width", 0.06)
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    x = x + (s.spectral_noise(seed + 1, falloff=2.6) - 0.5) * 0.012
    y = y + (s.spectral_noise(seed + 2, falloff=2.6) - 0.5) * 0.012
    row = np.floor(y * rows)
    xs = x * cols + (0.5 * (row % 2) if r.get("stagger", True) else 0.0)
    col = np.floor(xs) % cols
    u, v = xs - np.floor(xs), y * rows - row
    edge = np.minimum(np.minimum(u, 1 - u) / cols * rows, np.minimum(v, 1 - v))
    bid = ((row % rows) * cols + col).astype(np.int32)
    rr = s.rng(seed)
    chips = s.spectral_noise(seed + 3, falloff=1.5)
    edge_eff = edge - np.clip(chips - 0.55, 0, 1) * 0.12
    mask = (edge_eff > mw).astype(np.float32)
    bevel = np.clip((edge_eff - mw) / r.get("bevel", 0.07), 0, 1)
    bevel = bevel * bevel * (3 - 2 * bevel)
    surface = s.spectral_noise(seed + 4, falloff=1.6)
    pores = s.rng(seed + 5).random((n, n)) > 0.97
    tone = rr.random(512)[bid]
    broad = s.spectral_noise(seed + 6, falloff=2.6)
    t = tone * 0.5 + surface * 0.35 + broad * 0.15
    brick_rgb = s.tone_map(t, r)
    brick_rgb[pores] *= 0.7
    mortar = np.array(s.hex_rgb(r.get("mortar", "#6a655c")))
    grain = s.rng(seed + 7).random((n, n))
    mortar_rgb = mortar[None, None, :] * (0.8 + 0.35 * grain[..., None])
    rgb = brick_rgb * mask[..., None] + mortar_rgb * (1 - mask[..., None])
    grime = (1 - np.clip((edge - mw) / 0.14, 0, 1)) * 0.22 + np.clip(broad - 0.6, 0, 1) * 0.4
    rgb *= (1 - grime)[..., None]
    height = mask * (0.5 + bevel * 0.35 + surface * 0.15 - pores * 0.1) + (1 - mask) * (0.12 + grain * 0.06)
    rgb = finish(rgb, height, 0.5, sigma=2.0)
    brick_rough, metal = r.get("roughness", 0.82), r.get("metallic", 0.0)
    rough = np.where(mask > 0, brick_rough + (surface - 0.5) * 0.1, 0.95)
    return rgb, height, rough, np.where(mask > 0, metal, 0.0)


def planks(r, seed):
    """Boards with growth rings, grain, knots, butt joints, dark gaps and nail heads."""
    n = s.SIZE
    count = r.get("count", 4)
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    idx = np.floor(y * count).astype(np.int32)
    v = y * count - idx
    rr = s.rng(seed)
    joint = rr.random(count)[idx]
    along = (x - joint) % 1.0
    board = idx * 2 + (along >= 0.5)
    du = np.minimum(np.minimum(along, np.abs(along - 0.5)), 1 - along)
    grain = streaks(seed + 1, 40, 1.2)
    fine = streaks(seed + 2, 12, 0.6)
    phase = rr.random(count * 2)[board] * 10
    ring_warp = streaks(seed + 3, 60, 6) * 3.5
    rings = 0.5 + 0.5 * np.sin(2 * np.pi * (v * rr.uniform(2.5, 4.5, count * 2)[board] + ring_warp + phase))
    # One small knot: the grain swirls around a dark, elongated core.
    kx, ky = rr.random(2)
    dx = (x - kx + 0.5) % 1 - 0.5
    dy = (y - ky + 0.5) % 1 - 0.5
    d2 = (dx * 22) ** 2 + (dy * 48) ** 2
    swirl = np.exp(-d2 * 0.35)
    rings = np.clip(rings * (1 - swirl) + (0.5 + 0.5 * np.sin(np.sqrt(d2) * 5.0)) * swirl, 0, 1)
    rings = np.where(d2 < 0.8, rings * 0.25, rings)
    tone = rr.random(count * 2)[board]
    t = tone * 0.25 + grain * 0.3 + rings * 0.3 + fine * 0.15
    rgb = s.tone_map(t, r)
    gap = np.clip(1 - np.minimum(v, 1 - v) / 0.025, 0, 1)
    butt = np.clip(1 - du * n / 1.6, 0, 1)
    rgb *= (1 - np.maximum(gap, butt) * 0.7)[..., None]
    # Two nails at each end of every board (a little in from the butt joint).
    ndx = np.abs(du - 0.035)
    ndy = np.minimum(np.abs(v - 0.3), np.abs(v - 0.7)) / count
    nails = np.sqrt(ndx * ndx + ndy * ndy) < 0.011
    rgb[nails] = (0.22, 0.2, 0.19)
    cup = -((v - 0.5) ** 2) * 0.15
    height = 0.6 + grain * 0.1 + fine * 0.06 + rings * 0.05 + cup - np.maximum(gap, butt) * 0.5 + nails * 0.08
    rgb = finish(rgb, height, 0.35, sigma=1.5)
    rough = np.clip(r.get("roughness", 0.72) + (fine - 0.5) * 0.12, 0, 1)
    return rgb, height, np.where(nails, 0.35, rough), np.where(nails, 0.8, 0.0)


def hive(r, seed):
    """Honeycomb: waxy hexagonal cells (jittered lattice), glossy honey pooled inside."""
    n = s.SIZE
    cols, rows = 9, 10  # even row count keeps the offset rows seamless
    pts = []
    rr = s.rng(seed)
    for j in range(rows):
        for i in range(cols):
            pts.append(((i + 0.5 * (j % 2) + rr.normal(0, 0.05)) * n / cols, (j + rr.normal(0, 0.05)) * n / rows))
    pts = np.array(pts)
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    f1 = np.full((n, n), 1e9, np.float32)
    f2 = np.full((n, n), 1e9, np.float32)
    for px, py in pts:
        dx = (x - px + n / 2) % n - n / 2
        dy = (y - py + n / 2) % n - n / 2
        d = np.sqrt(dx * dx + dy * dy)
        f2 = np.where(d < f1, f1, np.minimum(f2, d))
        f1 = np.minimum(f1, d)
    edge = (f2 - f1) / (n / cols)
    wall = np.clip(1 - edge / 0.12, 0, 1)
    bowl = np.clip(f1 / (n / cols) * 1.6, 0, 1)
    fill = s.spectral_noise(seed + 1, falloff=2.2)
    t = np.clip(0.35 + wall * 0.5 - (1 - bowl) * 0.25 + fill * 0.2, 0, 1)
    rgb = s.tone_map(t, r)
    height = 0.3 + wall * 0.6 + bowl * 0.1
    rgb = finish(rgb, height, 0.35, sigma=2.0)
    rough = np.where(wall > 0.5, 0.55, r.get("roughness", 0.12))
    return rgb, height, rough, np.zeros_like(t)


def translucent(r, seed):
    """Glass (faint waves, bubbles, optional frame) and crystal (bright facets); alpha is opacity."""
    base, light = s.hex_rgb(r["base"]), s.hex_rgb(r["light"])
    n = s.SIZE
    wave = s.spectral_noise(seed, falloff=2.8)
    f1, edge, cid = s.voronoi(seed + 1, r.get("facets", 10))
    shade_ = s.rng(seed + 2).random(r.get("facets", 10))[cid]
    t = np.clip(wave * 0.4 + shade_ * 0.3 + (1 - f1) * 0.3, 0, 1)
    rgb = s.gradient_map(t, [(0, base), (1, light)])
    rgb[edge < 0.03] = light
    alpha = np.full_like(t, r.get("alpha", 0.5)) + (edge < 0.03) * 0.2
    if r.get("frame"):
        y, x = np.mgrid[0:n, 0:n] / n
        border = np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y)) < 0.05
        rgb[border] = s.hex_rgb(r["frame"])
        alpha[border] = 1.0
    return rgb, wave * 0.2 + (1 - f1) * 0.1, np.full_like(t, 0.05), np.zeros_like(t), np.clip(alpha, 0, 1)


def web(r, seed):
    n = s.SIZE
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    ang = np.arctan2(y - 0.5, x - 0.5)
    rad = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2)
    spokes = np.clip(1 - np.abs(np.sin(ang * 6)) / 0.05, 0, 1)
    rings = np.clip(1 - np.abs(np.sin(rad * 60 + np.sin(ang * 6) * 0.4)) / 0.14, 0, 1) * (rad < 0.5)
    alpha = np.maximum(spokes, rings) * 0.85
    rgb = np.ones((n, n, 3), np.float32) * np.array(s.hex_rgb(r["base"]))
    return rgb, alpha, np.full_like(alpha, 0.6), np.zeros_like(alpha), alpha


def rope(r, seed):
    """A braided rope hanging down the middle of the face; transparent on either side (cut-out like webs)."""
    n = s.SIZE
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    half = r.get("width", 0.09)
    across = np.abs(x - 0.5) / half
    alpha = (across < 1).astype(np.float32)
    twist = np.sin((y * 10 + x * 3) * np.pi * 2) * 0.5 + 0.5
    shade = np.clip(1 - across ** 2, 0, 1) * 0.6 + 0.4
    base, dark, light = s.palette(r)
    t = np.clip(twist * shade, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.6, base), (1.0, light)])
    return rgb, alpha, np.full_like(alpha, 0.85), np.zeros_like(alpha), alpha


def missing(r, seed):
    y, x = np.mgrid[0:s.SIZE, 0:s.SIZE] // 32
    checker = ((x + y) % 2).astype(np.float32)
    rgb = s.lerp_color(checker, (1, 0, 1), (0.1, 0.1, 0.1))
    return rgb, checker * 0.5, np.full_like(checker, 0.9), np.zeros_like(checker)


def metal(r, seed):
    """Brushed metal: directional streaks, scratches, soft tarnish and pitting."""
    base, dark, light = s.palette(r)
    brushed = streaks(seed, 80, 0.8)
    tarnish = s.spectral_noise(seed + 2, falloff=2.6)
    t = np.clip(brushed * 0.35 + tarnish * 0.65, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)])
    scratches = streaks(seed + 3, 160, 0.4) > 0.8
    rgb[scratches] = np.array(light)
    pits = s.rng(seed + 4).random(t.shape) > 0.993
    rgb[pits] *= 0.5
    rough = np.clip(r.get("roughness", 0.35) + (tarnish - 0.5) * 0.3 - scratches * 0.1, 0, 1)
    return rgb, t * 0.3 - pits * 0.2, rough, np.full_like(t, r.get("metallic", 1.0))


def leather(r, seed):
    """Tanned hide: pebbled grain cells with creases between them, scuffed lighter where it is worn."""
    base, dark, light = s.palette(r)
    f1, edge, cid = s.voronoi(seed, 700, warp_amount=1.5)
    pebble = np.sqrt(np.clip(1 - f1, 0, 1))
    crease = np.clip(1 - edge / 0.08, 0, 1)
    wear = s.spectral_noise(seed + 1, falloff=2.4)
    t = np.clip(pebble * 0.35 + wear * 0.45 + s.rng(seed + 2).random(len(np.unique(cid)) + 1)[cid % 700] * 0.2, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)]) * (1 - crease * 0.35)[..., None]
    height = pebble * 0.6 - crease * 0.3 + wear * 0.2
    rough = np.clip(r.get("roughness", 0.55) - wear * 0.15 + crease * 0.1, 0, 1)
    return finish(rgb, height, 0.35, sigma=1.0), height, rough, np.zeros_like(t)


def cloth(r, seed):
    """Woven fabric: interleaved warp and weft threads with fibre fuzz."""
    base, dark, light = s.palette(r)
    n = s.SIZE
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    k = r.get("threads", 64) / n * 2 * np.pi
    warp_ = 0.5 + 0.5 * np.sin(x * k)
    weft = 0.5 + 0.5 * np.sin(y * k)
    checker = ((np.floor(x * k / np.pi) + np.floor(y * k / np.pi)) % 2).astype(np.float32)
    weave = np.where(checker > 0, warp_, weft)
    fuzz = s.spectral_noise(seed, falloff=1.2)
    t = np.clip(weave * 0.55 + s.spectral_noise(seed + 1, falloff=2.2) * 0.3 + fuzz * 0.15, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.5, base), (1.0, light)])
    return finish(rgb, weave, 0.3, sigma=1.0), weave, np.full_like(t, 0.95), np.zeros_like(t)
