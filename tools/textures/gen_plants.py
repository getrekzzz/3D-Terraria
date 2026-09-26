"""Plant materials: leaf clusters (cut-out and dense canopy), bark, mushroom caps and ribbed cactus skin."""
import numpy as np

import synth as s
from gen_natural import finish


def _draw_leaves(r, seed, count, length, width, background, cover_alpha):
    """Stamps pointed, veined leaves with per-leaf tone; returns rgb, height, alpha."""
    n = s.SIZE
    base, dark, light = s.palette(r)
    rgb = np.ones((n, n, 3), np.float32) * np.array(background)
    height = np.zeros((n, n), np.float32)
    alpha = np.full((n, n), cover_alpha, np.float32)
    rr = s.rng(seed)
    for _ in range(count):
        cx, cy = rr.random(2) * n
        ang = rr.random() * np.pi * 2
        ln = length * rr.uniform(0.7, 1.3)
        wd = width * rr.uniform(0.7, 1.3)
        half = int(ln) + 2
        region, ox, oy = s.stamp_window(n, cx, cy, half)
        u = ox * np.cos(ang) + oy * np.sin(ang)
        v = -ox * np.sin(ang) + oy * np.cos(ang)
        t = (u + ln * 0.5) / ln
        halfw = wd * 0.5 * np.sin(np.clip(t, 0, 1) * np.pi) ** 0.75
        inside = (t > 0) & (t < 1) & (np.abs(v) < halfw)
        if not inside.any():
            continue
        z = 0.4 + rr.random() * 0.6
        upd = inside & (z > height[region])
        tone = rr.random()
        col = np.array(s.lerp_color(np.array([[tone]]), dark, light)[0, 0]) * 0.75 + np.array(base) * 0.25
        across = np.abs(v) / np.maximum(halfw, 1e-3)
        rib = np.abs(v) < 0.7
        veins = np.abs(np.sin((u * 0.9 - np.abs(v) * 1.2))) < 0.18
        shading = (0.8 + 0.25 * (1 - across)) * np.where(rib, 1.15, 1.0) * np.where(veins & ~rib, 0.92, 1.0)
        leaf = col[None, None, :] * shading[..., None]
        sub_rgb, sub_h, sub_a = rgb[region], height[region], alpha[region]
        sub_rgb[upd] = leaf[upd]
        sub_h[upd] = (z * (0.7 + 0.3 * (1 - across)))[upd]
        sub_a[upd] = 1.0
        rgb[region], height[region], alpha[region] = sub_rgb, sub_h, sub_a
    return rgb, height, alpha


def leaves(r, seed):
    """Cut-out leaf block: gaps are transparent."""
    rgb, height, alpha = _draw_leaves(r, seed, r.get("leaves", 190), 26, 11, s.hex_rgb(r["dark"]), 0.0)
    rgb = finish(rgb, height, 0.4, sigma=2.0, jitter_seed=seed + 1, jitter=0.05)
    return rgb, height, np.full_like(height, 0.65), np.zeros_like(height), alpha


def foliage(r, seed):
    """Dense canopy for tree models: overlapping leaves over dark depth, opaque."""
    rgb, height, _ = _draw_leaves(r, seed, r.get("leaves", 900), r.get("length", 16), r.get("width", 6),
                                  np.array(s.hex_rgb(r["dark"])) * 0.45, 1.0)
    rgb = finish(rgb, height, 0.5, sigma=1.5, jitter_seed=seed + 1, jitter=0.06)
    return rgb, height, np.full_like(height, r.get("roughness", 0.9)), np.zeros_like(height)


def _vertical_blur(a, k):
    """Periodic box blur along the v axis (fibres running along a trunk)."""
    acc = np.zeros_like(a)
    for i in range(-k // 2, k // 2):
        acc += np.roll(a, i, axis=0)
    return acc / k


def bark(r, seed):
    """Deeply furrowed bark: stretched ridges, broken plates, fibres and lichen patches (v runs along the trunk)."""
    ridges = s.band_noise(seed, r.get("furrows", 14) * 2, width=0.5)
    stretched = s.normalize(_vertical_blur(s.warp(ridges, seed + 5, 6.0), 40))
    _, plates, _ = s.voronoi(seed + 1, r.get("plates", 40), warp_amount=3.0)
    fibres = s.normalize(_vertical_blur(s.rng(seed + 3).random(ridges.shape), 12))
    detail = s.spectral_noise(seed + 2, falloff=1.6)
    furrow = np.clip(1 - stretched / 0.3, 0, 1)
    height = np.clip(stretched * 0.6 + plates * 0.25 + fibres * 0.1 + detail * 0.1 - furrow * 0.35, 0, 1)
    t = np.clip(stretched * 0.5 + detail * 0.25 + fibres * 0.15 + plates * 0.2, 0, 1)
    rgb = s.tone_map(t, r) * (1 - furrow * 0.5)[..., None]
    if "lichen" in r:
        patch = s.spectral_noise(seed + 4, falloff=2.2) > 0.68
        rgb[patch] = rgb[patch] * 0.4 + np.array(s.hex_rgb(r["lichen"])) * 0.6
    rgb = finish(rgb, height, 0.5, sigma=2.0)
    return rgb, height, np.full_like(t, 0.92), np.zeros_like(t)


def cap(r, seed):
    """Mushroom cap: mottled skin with raised pale spots."""
    t = s.spectral_noise(seed, falloff=2.2)
    rgb = s.tone_map(t, r)
    f1 = s.voronoi(seed + 1, r.get("spots", 14), warp_amount=3.0)[0]
    size = r.get("spot_size", 0.22)
    spots = np.clip(1 - f1 / size, 0, 1)
    rgb = rgb * (1 - (spots > 0)[..., None]) + np.array(s.hex_rgb(r.get("spot", "#f4efe0"))) * (0.85 + 0.15 * spots)[..., None] * (spots > 0)[..., None]
    height = np.clip(t * 0.5 + np.sqrt(spots) * 0.4, 0, 1)
    rgb = finish(rgb, height, 0.3)
    return rgb, height, np.full_like(t, 0.55), np.zeros_like(t)


def ribbed(r, seed):
    """Vertical ribs (cactus skin, fluted columns) with areoles."""
    n = s.SIZE
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    ribs = 0.5 + 0.5 * np.cos(x / n * 2 * np.pi * r.get("ribs", 6))
    speck = s.rng(seed).random((n, n)) > 0.985
    t = np.clip(ribs * 0.7 + s.spectral_noise(seed + 1, falloff=2.0) * 0.3, 0, 1)
    rgb = s.tone_map(t, r)
    rgb[speck & (ribs > 0.8)] = s.hex_rgb(r.get("speck", "#f0f0d0"))
    rgb = finish(rgb, ribs, 0.4)
    return rgb, ribs, np.full_like(t, 0.7), np.zeros_like(t)


def _stamp_leaf(rgb, height, alpha, cx, cy, ang, ln, wd, color, z, needle=False):
    """One leaf (or needle) from its base at (cx, cy) along angle ``ang``: rounded base, pointed tip, midrib, veins."""
    n = rgb.shape[0]
    half = int(ln) + 3
    region, ox, oy = s.stamp_window(n, cx, cy, half)
    u = ox * np.cos(ang) + oy * np.sin(ang)
    v = -ox * np.sin(ang) + oy * np.cos(ang)
    t = u / ln
    if needle:
        halfw = np.full_like(t, wd * 0.5) * (1 - np.clip(t, 0, 1) ** 4)
    else:
        halfw = wd * 0.5 * np.clip(t * 3.0, 0, 1) ** 0.5 * (1 - np.clip(t, 0, 1) ** 2.2)
    inside = (t > 0) & (t < 1) & (np.abs(v) < halfw)
    if not inside.any():
        return
    across = np.abs(v) / np.maximum(halfw, 1e-3)
    rib = (np.abs(v) < 0.8) & (t < 0.92)
    veins = (np.abs(np.sin(u * 0.55 - np.abs(v) * 0.9)) < 0.16) & ~rib
    shading = (0.78 + 0.3 * (1 - across)) * np.where(rib, 1.18, 1.0) * np.where(veins, 0.9, 1.0)
    # The half facing the light is a touch brighter (leaves are slightly folded along the midrib).
    shading *= np.where(v > 0, 1.05, 0.95)
    leaf = np.asarray(color)[None, None, :] * shading[..., None]
    sub_rgb, sub_h, sub_a = rgb[region], height[region], alpha[region]
    upd = inside & (z >= sub_h)
    sub_rgb[upd] = leaf[upd]
    sub_h[upd] = (z * (0.75 + 0.25 * (1 - across)))[upd]
    sub_a[upd] = 1.0
    rgb[region], height[region], alpha[region] = sub_rgb, sub_h, sub_a


def sprig(r, seed):
    """Leaf card for tree canopies: twigs fanning up from the bottom edge with leaves (or needles) along them,
    transparent in between. Cards are stacked by the hundred in a crown; the texture must read as a small branch."""
    n = s.SIZE
    base, dark, light = s.palette(r)
    rgb = np.zeros((n, n, 3), np.float32)
    height = np.zeros((n, n), np.float32)
    alpha = np.zeros((n, n), np.float32)
    rr = s.rng(seed)
    needles = r.get("style") == "needles"
    twig_color = np.array(s.hex_rgb(r.get("twig", "#4a3526")))
    twigs = r.get("twigs", 4)
    for k in range(twigs):
        # Twigs start near the bottom centre and fan upward.
        spread = (k - (twigs - 1) / 2) / max(twigs - 1, 1)
        ang = -np.pi / 2 + spread * 1.05 + rr.uniform(-0.12, 0.12)
        x, y = n * 0.5 + spread * n * 0.08, n * 0.97
        length = n * rr.uniform(0.6, 0.76) * (1 - abs(spread) * 0.25)
        steps = 24
        pts = []
        for i in range(steps + 1):
            pts.append((x, y, ang))
            ang += rr.uniform(-0.06, 0.06) + spread * 0.012
            x += np.cos(ang) * length / steps
            y += np.sin(ang) * length / steps
        for (x0, y0, a0) in pts[:-1]:  # the twig itself, drawn below the leaves
            _stamp_leaf(rgb, height, alpha, x0, y0, a0, length / steps + 1.5, 2.6, twig_color * rr.uniform(0.85, 1.1), 0.05, needle=True)
        count = r.get("leavesPerTwig", 9)
        for i in range(count):
            t = 0.2 + 0.8 * (i + rr.random() * 0.5) / count
            px, py, pa = pts[min(int(t * steps), steps)]
            side = 1 if i % 2 == 0 else -1
            if needles:
                for _ in range(6):
                    na = pa + side * rr.uniform(0.5, 1.1) * (1 if rr.random() < 0.5 else -1)
                    tone = rr.random()
                    col = np.array(s.lerp_color(np.array([[tone]]), dark, light)[0, 0]) * 0.8 + np.array(base) * 0.2
                    _stamp_leaf(rgb, height, alpha, px, py, na, n * rr.uniform(0.09, 0.13), 2.2, col, rr.uniform(0.3, 1.0), needle=True)
                continue
            la = pa + side * rr.uniform(0.55, 0.95)
            ln = n * r.get("leafLength", 0.2) * rr.uniform(0.8, 1.15) * (1.05 - 0.35 * t)
            tone = rr.random()
            col = np.array(s.lerp_color(np.array([[tone]]), dark, light)[0, 0]) * 0.75 + np.array(base) * 0.25
            if rr.random() < r.get("yellowing", 0.08):
                col = col * 0.7 + np.array((0.62, 0.55, 0.2)) * 0.3
            _stamp_leaf(rgb, height, alpha, px, py, la, ln, ln * r.get("leafWidth", 0.42), col, rr.uniform(0.3, 1.0))
        # A leaf at the tip, pointing on.
        px, py, pa = pts[-1]
        ln = n * r.get("leafLength", 0.2) * 0.8
        if not needles:
            _stamp_leaf(rgb, height, alpha, px, py, pa, ln, ln * r.get("leafWidth", 0.42), np.array(base), 0.9)
    rgb = finish(rgb, height, 0.35, sigma=1.5)
    # Keep the colour of the gaps close to the leaves so mipmapped edges do not darken or glow.
    weight = np.maximum(s.blur(alpha, 6.0), 1e-3)
    fill = np.stack([s.blur(rgb[..., c] * alpha, 6.0) / weight for c in range(3)], -1)
    rgb = np.where(alpha[..., None] > 0, rgb, fill)
    return rgb, height, np.full_like(height, r.get("roughness", 0.6)), np.zeros_like(height), alpha
