"""Tier heads for pickaxes, axes, hammers and bows (items.GENERATORS keeps the names). Each takes ``style``:
stubby, straight, ornate, spiky or flame (bows: simple, recurve, ornate, spiky, flame), so ore tiers get their own
silhouettes instead of a recoloured mesh. Common params: ``metal``/``metalLayer`` (see items._metal), ``edge`` (rim
colour, default lightened metal), ``accent`` (ornament/glow colour), ``gem`` (hex), ``wood`` (haft tint).
Item space: grip at the origin, haft along +Z, heads flat in XZ, thin along Y.
"""
import math

from mathutils import Matrix, Vector

from .blade_profiles import bevel_slab, darken, lighten
from .mesh import MeshBuilder

GLOW = "prop:plain:glow"


def metal_slot(p):
    layer = p.get("metalLayer", "iron")
    return f"prop:{layer}" + (":metal" if layer == "iron" else ""), p.get("metal", "#c0c4cc")


def haft(mb, length, p, radius=0.022, cap=None):
    """Wooden haft with a leather grip band (``wrap``) and a metal butt cap (``cap``, default the metal tint)."""
    mb.cylinder((0, 0, -0.1), (0, 0, length), radius, radius * 0.9, "prop:planks", p.get("wood", "#ffffff"), segments=8)
    mb.cylinder((0, 0, -0.07), (0, 0, 0.1), radius * 1.15, None, "prop:leather", p.get("wrap", "#7a5434"), segments=8)
    layer, tint = metal_slot(p)
    mb.cylinder((0, 0, -0.125), (0, 0, -0.085), radius * 1.05, radius * 1.3, layer, cap or darken(tint, 0.2), segments=8, smooth=False)


def _accent(p, tint):
    return p.get("accent") or p.get("veins") or lighten(tint, 0.5)


def _collar(mb, z0, z1, layer, tint, r=0.032):
    mb.cylinder((0, 0, z0), (0, 0, z1), r, None, layer, darken(tint, 0.25), segments=8, smooth=False)


# ---------------------------------------------------------------- pickaxe
PICKS = {  # half span, arch, thickness, blunt tips, top-edge spikes, flame licks
    "stubby": dict(span=0.22, sag=0.025, h=0.1, tip=0.035),
    "straight": dict(span=0.34, sag=0.07, h=0.085, tip=0.0),
    "ornate": dict(span=0.37, sag=0.1, h=0.085, tip=0.0),
    "spiky": dict(span=0.36, sag=0.08, h=0.085, tip=0.0, spikes=True),
    "flame": dict(span=0.33, sag=0.07, h=0.09, tip=0.0, licks=True),
}


def _pick_outline(span, sag, h, tip, zc, spikes=False, licks=False, n=11):
    xs = [-span + 2 * span * i / (n - 1) for i in range(n)]
    top, low = [], []
    for x in xs:
        u = x / span
        mid = zc + sag * (1 - u * u) - 0.02 * abs(u) ** 3
        half = (h * (1 - abs(u) ** 1.6) + tip) / 2
        lift = 0.0
        if spikes and 0.1 < abs(u) < 0.95 and round(abs(u) * (n - 1) / 2) % 2 == 1:
            lift = 0.045
        if licks and abs(u) < 0.9:
            lift = 0.03 * max(0.0, math.sin(abs(u) * 3 * math.pi))
        top.append((x, mid + half + lift))
        low.append((x, mid - half))
    if tip <= 1e-6:  # pointed ends: one vertex each
        return low + top[-2:0:-1]
    return low + top[::-1]


def pickaxe(mb: MeshBuilder, p, rng):
    layer, tint = metal_slot(p)
    style = p.get("style", "straight")
    s = {**PICKS.get(style, PICKS["straight"]), **{k: p[k] for k in ("span", "sag", "h") if k in p}}
    zc = 0.64
    haft(mb, zc + 0.08, p)
    pts = _pick_outline(s["span"], s["sag"], s["h"], s["tip"], zc, s.get("spikes", False), s.get("licks", False))
    bevel_slab(mb, pts, 0.05, 0.008, layer, tint, rim=p.get("edge") or lighten(tint, 0.4), edge=0.55)
    _collar(mb, zc - 0.03, zc + 0.11, layer, tint)
    accent = _accent(p, tint)
    if style == "ornate":
        bevel_slab(mb, [(0, zc - 0.03), (0.05, zc + 0.05), (0, zc + 0.13), (-0.05, zc + 0.05)], 0.07, 0.01, layer,
                   p.get("guardColor", accent))
        mb.sphere((0, -0.037, zc + 0.05), 0.02, "prop:crystal:glow", p.get("gem", "#ff3a6a"), subdivisions=1, smooth=False)
    elif style == "spiky":
        mb.cylinder((0, 0, zc + 0.1), (0, 0, zc + 0.19), 0.025, 0.0, layer, tint, segments=5, smooth=False)
    elif style == "flame":
        line = [(x, zc + s["sag"] * (1 - (x / s["span"]) ** 2)) for x in (-0.22, -0.11, 0.0, 0.11, 0.22)]
        for (x0, z0), (x1, z1) in zip(line, line[1:]):
            mb.cylinder((x0, -0.027, z0), (x1, -0.027, z1), 0.008, None, GLOW, accent, segments=4, caps=False)
    elif style == "stubby":
        for sx in (-1, 1):
            mb.box((sx * s["span"] - 0.02, -0.03, zc - 0.04), (sx * s["span"] + 0.02, 0.03, zc + 0.04), layer, darken(tint, 0.15))


# ---------------------------------------------------------------- axe
AXES = {
    "stubby": [(0.0, 0.55), (0.13, 0.53), (0.17, 0.6), (0.17, 0.7), (0.13, 0.76), (0.0, 0.74)],
    "straight": [(0.0, 0.52), (0.1, 0.5), (0.22, 0.44), (0.24, 0.62), (0.22, 0.8), (0.1, 0.74), (0.0, 0.72)],
    "ornate": [(0.0, 0.54), (0.08, 0.5), (0.18, 0.4), (0.27, 0.42), (0.29, 0.62), (0.27, 0.84), (0.18, 0.84),
               (0.08, 0.74), (0.0, 0.72)],
    "spiky": [(0.0, 0.52), (0.1, 0.48), (0.22, 0.4), (0.21, 0.48), (0.27, 0.53), (0.23, 0.6), (0.29, 0.66),
              (0.23, 0.72), (0.27, 0.8), (0.22, 0.84), (0.1, 0.76), (0.0, 0.73)],
    "flame": [(0.0, 0.52), (0.1, 0.49), (0.2, 0.42), (0.26, 0.47), (0.23, 0.54), (0.29, 0.6), (0.25, 0.66),
              (0.3, 0.74), (0.24, 0.79), (0.26, 0.88), (0.15, 0.8), (0.0, 0.73)],
}


def axe(mb: MeshBuilder, p, rng):
    layer, tint = metal_slot(p)
    style = p.get("style", "straight")
    haft(mb, 0.78 if style != "stubby" else 0.74, p)
    k = p.get("size", 1.0)  # scales the blade's reach and height about the haft
    pts = [(x * k, 0.63 + (z - 0.63) * k) for x, z in AXES.get(style, AXES["straight"])]
    bevel_slab(mb, pts, 0.034, 0.012, layer, tint, rim=p.get("edge") or lighten(tint, 0.45))
    cheek = [(0.0, 0.57), (0.09, 0.56), (0.11, 0.63), (0.09, 0.7), (0.0, 0.69)]
    bevel_slab(mb, [(x * k, 0.63 + (z - 0.63) * k) for x, z in cheek], 0.046, 0.008, layer, darken(tint, 0.18))
    zs = [z for _, z in pts]
    z0, z1 = min(zs[0], zs[-1]), max(zs[0], zs[-1])
    mb.box((-0.045, -0.032, z0 + 0.01), (0.02, 0.032, z1 - 0.01), layer, darken(tint, 0.2))
    accent = _accent(p, tint)
    if style == "ornate":
        bevel_slab(mb, [(-0.04, 0.58), (-0.16, 0.5), (-0.12, 0.6), (-0.16, 0.7), (-0.04, 0.68)], 0.03, 0.008, layer,
                   p.get("guardColor", accent))
        mb.sphere((0.1, -0.022, 0.63), 0.02, "prop:crystal:glow", p.get("gem", "#3ad0ff"), subdivisions=1, smooth=False)
    elif style == "spiky":
        mb.cylinder((-0.04, 0, 0.63), (-0.17, 0, 0.6), 0.03, 0.0, layer, tint, segments=5, smooth=False)
        mb.cylinder((0, 0, 0.77), (0, 0, 0.88), 0.022, 0.0, layer, tint, segments=5, smooth=False)
    elif style == "flame":
        line = [(0.06, 0.56), (0.15, 0.6), (0.19, 0.68), (0.16, 0.76)]
        for a, b in zip(line, line[1:]):
            mb.cylinder((a[0], -0.019, a[1]), (b[0], -0.019, b[1]), 0.008, None, GLOW, accent, segments=4, caps=False)
    elif style == "stubby":
        mb.box((-0.08, -0.028, 0.6), (-0.04, 0.028, 0.7), layer, darken(tint, 0.3))


# ---------------------------------------------------------------- hammer
HAMMERS = {  # half length, radius, segments, flare of the faces
    "stubby": dict(hl=0.12, r=0.08, seg=8, flare=1.0),
    "straight": dict(hl=0.16, r=0.072, seg=4, flare=1.0),
    "ornate": dict(hl=0.17, r=0.075, seg=8, flare=1.12),
    "spiky": dict(hl=0.15, r=0.07, seg=6, flare=1.0),
    "flame": dict(hl=0.15, r=0.07, seg=8, flare=1.25),
}


def hammer(mb: MeshBuilder, p, rng):
    layer, tint = metal_slot(p)
    wood = p.get("headLayer")
    head_layer, head_tint = (f"prop:{wood}", "#ffffff") if wood else (layer, tint)
    style = p.get("style", "straight")
    s = {**HAMMERS.get(style, HAMMERS["straight"]), **{k: p[k] for k in ("hl", "r") if k in p}}
    zc = 0.68
    haft(mb, zc + s["r"] * 0.6, p)
    hl, r, c, f = s["hl"], s["r"], min(0.018, s["r"] * 0.3), s["flare"]
    prof = [(0.0, -hl), (r * f - c, -hl), (r * f, -hl + c), (r, -hl * 0.55), (r * 0.94, 0.0), (r, hl * 0.55),
            (r * f, hl - c), (r * f - c, hl), (0.0, hl)]
    m = Matrix.Translation((0, 0, zc)) @ Matrix.Rotation(math.pi / 2, 4, "Y") @ Matrix.Rotation(math.pi / s["seg"], 4, "Z")
    mb.lathe(prof, head_layer, head_tint, segments=s["seg"], smooth=False, matrix=m)
    accent = _accent(p, tint)
    rim = p.get("edge") or lighten(tint, 0.35)
    for sx in (-1, 1):  # light striking faces
        mb.cylinder((sx * hl, 0, zc), (sx * (hl + 0.012), 0, zc), r * f * 0.82, None, head_layer, rim, segments=s["seg"], smooth=False)
    if style == "ornate":
        for sx in (-1, 1):
            mb.cylinder((sx * hl * 0.5, 0, zc), (sx * hl * 0.66, 0, zc), r * 1.08, None, layer, p.get("guardColor", accent), segments=8, smooth=False)
        mb.sphere((0, -r * 0.95, zc), 0.024, "prop:crystal:glow", p.get("gem", "#ff3a6a"), subdivisions=1, smooth=False)
    elif style == "spiky":
        for sx in (-1, 1):
            mb.cylinder((sx * (hl + 0.01), 0, zc), (sx * (hl + 0.09), 0, zc), 0.035, 0.0, layer, tint, segments=5, smooth=False)
        mb.cylinder((0, 0, zc + r * 0.8), (0, 0, zc + r + 0.08), 0.03, 0.0, layer, tint, segments=5, smooth=False)
    elif style == "flame":
        for sx in (-1, 1):
            mb.cylinder((sx * hl * 0.25, 0, zc), (sx * hl * 0.35, 0, zc), r * 1.0, None, GLOW, accent, segments=8, smooth=False)
        mb.cylinder((0, 0, zc + r * 0.7), (0.02, 0, zc + r + 0.07), 0.028, 0.0, GLOW, accent, segments=5, smooth=False)
    else:  # stubby / straight: a darker binding band where the haft passes through
        mb.cylinder((-0.03, 0, zc), (0.03, 0, zc), r * 1.06, None, head_layer, darken(head_tint, 0.25), segments=s["seg"], smooth=False)


# ---------------------------------------------------------------- bow
def bow_slot(p):
    limb = p.get("limbLayer")
    layer = f"prop:{limb or 'planks'}" + (":metal" if limb == "iron" else "")
    tint = p.get("metal", "#ffffff") if limb in ("iron", "gold", "copper") else "#ffffff"
    return layer, tint


def bow(mb: MeshBuilder, p, rng):
    """Bow drawn in XZ: the string at x = 0 (the arrow nock), the riser at x = -0.17, limbs to the tips at z = ±0.55."""
    layer, tint = bow_slot(p)
    style = p.get("style", "simple")
    accent = _accent(p, tint if tint != "#ffffff" else "#d8a040")
    recurve = {"simple": 0.0, "recurve": 0.07, "ornate": 0.05, "spiky": 0.03, "flame": 0.06}.get(style, 0.0)
    depth = p.get("depth", 0.2 if style in ("recurve", "ornate") else 0.18)
    pts = []
    for i in range(11):
        t = i / 10
        u = 2 * t - 1
        x = -(depth - 0.02) * math.sin(t * math.pi)
        x -= recurve * (max(0.0, abs(u) - 0.6) / 0.4 - 1)  # tips flick forward; the string stays at x = 0
        pts.append(Vector((x, 0, u * 0.56)))
    radii = [0.036 * (0.4 + 0.6 * math.sin(i / 10 * math.pi)) for i in range(11)]
    mb.tube(pts, radii, layer, tint, segments=8)
    tips = (pts[0], pts[-1])
    mb.cylinder(tips[0], tips[1], 0.004, None, "prop:cloth", p.get("string", "#f0f0e0"), segments=4)
    wrap, rx = p.get("wrap"), pts[5].x
    mb.cylinder((rx, 0, -0.09), (rx, 0, 0.09), 0.04, None,
                "prop:leather" if wrap else "prop:red_cloth", wrap or "#ffffff", segments=8)
    if style == "ornate":
        for t in tips:
            mb.sphere(t, 0.03, layer, p.get("guardColor", accent), subdivisions=1, smooth=False)
        mb.sphere((rx - 0.03, 0, 0), 0.025, "prop:crystal:glow", p.get("gem", "#ff3a6a"), subdivisions=1, smooth=False)
    elif style == "spiky":
        for k in (2, 3, 7, 8):
            q = pts[k]
            out = Vector((-1, 0, 0.6 if k > 5 else -0.6)).normalized()
            mb.cylinder(q, q + out * 0.08, 0.018, 0.0, layer, tint, segments=4, smooth=False)
    elif style == "flame":
        for t in tips:
            mb.cylinder(t, t + Vector((-0.05, 0, 0.07 if t.z > 0 else -0.07)), 0.025, 0.0, GLOW, accent, segments=5, smooth=False)
        mb.cylinder((rx - 0.02, 0, -0.12), (rx - 0.02, 0, 0.12), 0.012, None, GLOW, accent, segments=4)
    elif style == "recurve":
        for t in tips:
            mb.sphere(t, 0.018, layer, darken(tint if tint != "#ffffff" else "#a07040", 0.3), subdivisions=1, smooth=False)
    # The hand holds the riser; the arrow nocks at the string centre (the origin) and flies toward the riser (-X).
    mb.socket("grip", (rx, 0, 0))
    mb.socket("nock", (0, 0, 0), (-1, 0, 0), (0, 0, 1))
