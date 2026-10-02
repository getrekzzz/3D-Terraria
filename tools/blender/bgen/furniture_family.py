"""Furniture families (X-03): one parametric piece per kind (door, chair, table, bed, chest, work bench, bookcase, piano,
sofa, bathtub, lamp, lantern, candle, candelabra, chandelier, grandfather clock, platform), built in any family's
material: ``p['mat']`` is the prop slot (``prop:planks``, ``prop:stone``, ``prop:crystal:clear``…), ``p['tint']`` its
colour, ``p['trim']``/``p['trimTint']`` the metal fittings and ``p['cloth']`` the fabric. Edges are softened with
``_soft`` (three overlapping slabs, a cheap bevel). Origin at the footprint's floor centre, front faces Blender +Y;
``p['size']`` is the footprint in metres. Flames are drawn live by the game (tile-object ``flame`` data)."""
import math

from mathutils import Matrix, Vector

from . import generator


def _soft(mb, lo, hi, mat, tint, r=0.018):
    """A box with softened edges: three slabs, each inset on two axes."""
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    r = min(r, (x1 - x0) / 4, (y1 - y0) / 4, (z1 - z0) / 4)
    mb.box((x0 + r, y0 + r, z0), (x1 - r, y1 - r, z1), mat, tint)
    mb.box((x0, y0 + r, z0 + r), (x1, y1 - r, z1 - r), mat, tint)
    mb.box((x0 + r, y0, z0 + r), (x1 - r, y1, z1 - r), mat, tint)


def _legs(mb, w, d, h, t, mat, tint, inset=0.06):
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (w / 2 - inset - t / 2), sy * (d / 2 - inset - t / 2)
            _soft(mb, (x - t / 2, y - t / 2, 0), (x + t / 2, y + t / 2, h), mat, tint, 0.01)


def _wick(mb, x, y, z, holder, tint):
    mb.cylinder((x, y, z), (x, y, z + 0.02), 0.045, 0.05, holder, tint, segments=10)
    mb.cylinder((x, y, z + 0.02), (x, y, z + 0.14), 0.022, None, "prop:plain", "#f0ead8", segments=8)
    mb.cylinder((x, y, z + 0.14), (x, y, z + 0.155), 0.003, 0.002, "prop:plain", "#1a1410", segments=4)


@generator
def furniture_piece(mb, p, rng):
    """One family piece, chosen by ``p['piece']``."""
    w, h, d = p["size"]
    m, t = p.get("mat", "prop:planks"), p.get("tint", "#ffffff")
    trim, tt = p.get("trim", "prop:gold"), p.get("trimTint", "#ffffff")
    cloth = p.get("cloth", "#b03a3a")
    piece = p["piece"]
    PIECES[piece](mb, w, h, d, m, t, trim, tt, cloth, rng)


def _table(mb, w, h, d, m, t, trim, tt, cloth, rng):
    top = h * 0.8
    _soft(mb, (-w / 2 + 0.02, -d / 2 + 0.02, top), (w / 2 - 0.02, d / 2 - 0.02, top + 0.09), m, t)
    _legs(mb, w, d, top, 0.09, m, t)
    _soft(mb, (-w / 2 + 0.12, -0.03, top * 0.3), (w / 2 - 0.12, 0.03, top * 0.36), m, t, 0.008)


def _chair(mb, w, h, d, m, t, trim, tt, cloth, rng):
    seat = h * 0.36
    _soft(mb, (-w * 0.32, -d * 0.32, seat), (w * 0.32, d * 0.32, seat + 0.07), m, t)
    for sx in (-1, 1):
        for sy in (-1, 1):
            top = h * 0.95 if sy < 0 else seat
            x, y = sx * w * 0.27, sy * d * 0.27
            _soft(mb, (x - 0.04, y - 0.04, 0), (x + 0.04, y + 0.04, top), m, t, 0.01)
    for k in range(2):
        z = seat + 0.2 + k * (h * 0.95 - seat - 0.28)
        _soft(mb, (-w * 0.27, -d * 0.31, z), (w * 0.27, -d * 0.23, z + 0.09), m, t, 0.01)


def _door(mb, w, h, d, m, t, trim, tt, cloth, rng):
    lw, lh, th = w * 0.9, h * 0.97, 0.09
    _soft(mb, (-lw / 2, -th / 2, 0.02), (lw / 2, th / 2, lh), m, t, 0.02)
    for z0, z1 in ((0.15, lh * 0.45), (lh * 0.52, lh - 0.15)):
        for s in (-1, 1):
            mb.box((-lw / 2 + 0.1, s * (th / 2 + 0.005) - 0.004, z0), (lw / 2 - 0.1, s * (th / 2 + 0.005) + 0.004, z1), m, _shade(t, 0.8))
    mb.sphere((lw / 2 - 0.12, th / 2 + 0.04, lh * 0.5), 0.04, trim, tt, subdivisions=1)
    mb.sphere((lw / 2 - 0.12, -th / 2 - 0.04, lh * 0.5), 0.04, trim, tt, subdivisions=1)


def _bed(mb, w, h, d, m, t, trim, tt, cloth, rng):
    _soft(mb, (-w * 0.46, -d * 0.48, 0.15), (w * 0.46, d * 0.48, 0.32), m, t)
    _legs(mb, w * 0.92, d * 0.96, 0.18, 0.1, m, t, inset=0.0)
    _soft(mb, (-w * 0.46, -d * 0.48, 0.15), (w * 0.46, -d * 0.42, h * 0.95), m, t)
    _soft(mb, (-w * 0.46, d * 0.42, 0.15), (w * 0.46, d * 0.48, h * 0.55), m, t)
    mb.box((-w * 0.42, -d * 0.42, 0.32), (w * 0.42, d * 0.42, 0.45), "prop:cloth", "#f0ece0")
    mb.box((-w * 0.43, -d * 0.1, 0.33), (w * 0.43, d * 0.43, 0.48), "prop:cloth", cloth)
    mb.sphere((0, -d * 0.3, 0.5), 0.2, "prop:cloth", "#f8f4ec", subdivisions=1, scale=(1.6, 0.9, 0.45))


def _chest(mb, w, h, d, m, t, trim, tt, cloth, rng):
    body = h * 0.58
    _soft(mb, (-w * 0.46, -d * 0.4, 0), (w * 0.46, d * 0.4, body), m, t)
    lid = [(-d * 0.4, 0), (d * 0.4, 0), (d * 0.38, h * 0.18), (d * 0.25, h * 0.3), (0, h * 0.36), (-d * 0.25, h * 0.3), (-d * 0.38, h * 0.18)]
    mb.extrude_polygon(lid, w * 0.92, m, t, matrix=Matrix.Translation((0, 0, body)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    for x in (-w * 0.3, w * 0.3):
        mb.box((x - 0.04, -d * 0.42, 0), (x + 0.04, d * 0.42, body + 0.02), trim, tt)
    mb.box((-0.07, d * 0.4, body - 0.14), (0.07, d * 0.44, body + 0.04), trim, tt)


def _workbench(mb, w, h, d, m, t, trim, tt, cloth, rng):
    top = h * 0.78
    _soft(mb, (-w / 2 + 0.02, -d / 2 + 0.02, top), (w / 2 - 0.02, d / 2 - 0.02, top + 0.12), m, t)
    _legs(mb, w, d, top, 0.11, m, t)
    _soft(mb, (-w / 2 + 0.1, -d / 2 + 0.1, top * 0.25), (w / 2 - 0.1, d / 2 - 0.1, top * 0.3), m, t, 0.01)
    mb.box((w / 2 - 0.3, -d / 2 + 0.05, top + 0.12), (w / 2 - 0.1, -d / 2 + 0.22, top + 0.26), "prop:iron", "#9aa0a8")


def _bookcase(mb, w, h, d, m, t, trim, tt, cloth, rng):
    _soft(mb, (-w / 2, -d * 0.45, 0), (-w / 2 + 0.07, d * 0.45, h), m, t)
    _soft(mb, (w / 2 - 0.07, -d * 0.45, 0), (w / 2, d * 0.45, h), m, t)
    _soft(mb, (-w / 2, -d * 0.45, 0), (w / 2, -d * 0.38, h), m, _shade(t, 0.85))
    shelves = 4
    for k in range(shelves + 1):
        z = k * (h - 0.06) / shelves
        _soft(mb, (-w / 2, -d * 0.45, z), (w / 2, d * 0.45, z + 0.06), m, t, 0.01)
    covers = ["#8a2a2a", "#2a4a8a", "#2a6a3a", "#8a6a2a", "#5a2a6a", "#3a3a3a"]
    for k in range(shelves):
        z0 = k * (h - 0.06) / shelves + 0.06
        x = -w / 2 + 0.09
        while x < w / 2 - 0.12:
            bw = rng.uniform(0.04, 0.07)
            bh = rng.uniform(0.6, 0.85) * ((h - 0.06) / shelves - 0.06)
            mb.box((x, -d * 0.3, z0), (x + bw, d * 0.38, z0 + bh), "prop:cloth", rng.choice(covers))
            x += bw + 0.008


def _piano(mb, w, h, d, m, t, trim, tt, cloth, rng):
    _soft(mb, (-w / 2, -d * 0.45, h * 0.3), (w / 2, d * 0.1, h), m, t, 0.03)
    _soft(mb, (-w / 2, d * 0.1, h * 0.42), (w / 2, d * 0.45, h * 0.52), m, t)
    mb.box((-w / 2 + 0.08, d * 0.12, h * 0.52), (w / 2 - 0.08, d * 0.4, h * 0.55), "prop:plain", "#f4f0e6")
    for k in range(int((w - 0.16) / 0.06)):
        x = -w / 2 + 0.08 + k * 0.06 + 0.04
        if k % 7 in (0, 3):
            continue
        mb.box((x - 0.012, d * 0.22, h * 0.55), (x + 0.012, d * 0.4, h * 0.575), "prop:plain", "#15120f")
    for sx in (-1, 1):
        _soft(mb, (sx * (w / 2 - 0.05) - 0.04, d * 0.3, 0), (sx * (w / 2 - 0.05) + 0.04, d * 0.4, h * 0.42), m, t, 0.01)
    _soft(mb, (-w / 2, -d * 0.45, 0), (w / 2, -d * 0.1, h * 0.3), m, _shade(t, 0.85))


def _sofa(mb, w, h, d, m, t, trim, tt, cloth, rng):
    _soft(mb, (-w / 2, -d * 0.45, 0.08), (w / 2, d * 0.45, h * 0.35), m, t, 0.03)
    _legs(mb, w, d * 0.9, 0.1, 0.07, m, t, inset=0.04)
    mb.box((-w / 2 + 0.12, -d * 0.35, h * 0.35), (w / 2 - 0.12, d * 0.42, h * 0.48), "prop:cloth", cloth)
    _soft(mb, (-w / 2 + 0.05, -d * 0.45, h * 0.35), (w / 2 - 0.05, -d * 0.28, h * 0.92), "prop:cloth", cloth, 0.05)
    for sx in (-1, 1):
        _soft(mb, (sx * w / 2 - 0.1 if sx > 0 else -w / 2, -d * 0.45, 0.08), ((w / 2) if sx > 0 else -w / 2 + 0.1, d * 0.45, h * 0.62), m, t, 0.03)


def _bathtub(mb, w, h, d, m, t, trim, tt, cloth, rng):
    profile = [(0.0, 0.08), (0.5, 0.08), (0.52, 0.2), (0.55, 0.9), (0.5, 0.95), (0.46, 0.9), (0.44, 0.22), (0.0, 0.22)]
    mb.lathe([(r * min(w, d) * 0.9, z * h * 0.85) for r, z in profile], m, t, segments=24,
             matrix=Matrix.Diagonal((w / min(w, d), d / min(w, d), 1, 1)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.sphere((sx * w * 0.35, sy * d * 0.3, 0.05), 0.06, trim, tt, subdivisions=1)
    mb.box((-w * 0.38, -d * 0.3, h * 0.6), (w * 0.38, d * 0.3, h * 0.62), "prop:plain:clear", "#9ad0f0")
    mb.tube([Vector((-w * 0.42, 0, h * 0.75)), Vector((-w * 0.5, 0, h * 1.0)), Vector((-w * 0.38, 0, h * 1.05))], [0.025, 0.025, 0.02], trim, tt, segments=6)


def _lamp(mb, w, h, d, m, t, trim, tt, cloth, rng):
    mb.cylinder((0, 0, 0), (0, 0, 0.05), 0.16, 0.18, m, t, segments=12)
    mb.cylinder((0, 0, 0.05), (0, 0, h * 0.78), 0.03, 0.03, m, t, segments=8)
    mb.cylinder((0, 0, h * 0.75), (0, 0, h * 0.98), 0.17, 0.09, "prop:cloth:clear", "#f4e0b0", segments=14, caps=False)
    mb.cylinder((0, 0, h * 0.78), (0, 0, h * 0.82), 0.035, 0.035, "prop:plain:glow", "#ffd080", segments=8)


def _lantern(mb, w, h, d, m, t, trim, tt, cloth, rng):
    top = h
    mb.cylinder((0, 0, top - 0.25), (0, 0, top), 0.012, 0.012, trim, tt, segments=6)  # hangs from the ceiling
    body0, body1 = top - 0.6, top - 0.25
    mb.cylinder((0, 0, body1 - 0.04), (0, 0, body1), 0.13, 0.06, m, t, segments=8, smooth=False)
    mb.cylinder((0, 0, body0), (0, 0, body0 + 0.04), 0.12, 0.13, m, t, segments=8, smooth=False)
    mb.cylinder((0, 0, body0 + 0.04), (0, 0, body1 - 0.04), 0.1, 0.1, "prop:plain:clear", "#fff0c8", segments=8, smooth=False)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        x, y = math.cos(a) * 0.105, math.sin(a) * 0.105
        mb.box((x - 0.012, y - 0.012, body0 + 0.04), (x + 0.012, y + 0.012, body1 - 0.04), m, t)


def _candle(mb, w, h, d, m, t, trim, tt, cloth, rng):
    _wick(mb, 0, 0, 0, m, t)


def _candelabra(mb, w, h, d, m, t, trim, tt, cloth, rng):
    mb.cylinder((0, 0, 0), (0, 0, 0.04), 0.12, 0.14, m, t, segments=12)
    mb.cylinder((0, 0, 0.04), (0, 0, 0.32), 0.025, 0.02, m, t, segments=8)
    arm = [Vector((-0.2, 0, 0.42)), Vector((-0.16, 0, 0.32)), Vector((0, 0, 0.3)), Vector((0.16, 0, 0.32)), Vector((0.2, 0, 0.42))]
    mb.tube(arm, [0.018] * len(arm), m, t, segments=6)
    for x, z in ((-0.2, 0.42), (0, 0.42), (0.2, 0.42)):
        if x == 0:
            mb.cylinder((0, 0, 0.32), (0, 0, z), 0.02, 0.02, m, t, segments=6)
        _wick(mb, x, 0, z, m, t)


def _chandelier(mb, w, h, d, m, t, trim, tt, cloth, rng):
    top = h
    mb.cylinder((0, 0, top - 0.5), (0, 0, top), 0.015, 0.015, trim, tt, segments=6)
    hub = top - 0.75
    mb.sphere((0, 0, hub), 0.09, m, t, subdivisions=2, scale=(1, 1, 1.4))
    arms = 6
    for k in range(arms):
        a = k * math.pi * 2 / arms
        tip = Vector((math.cos(a) * w * 0.38, math.sin(a) * d * 0.38, hub + 0.1))
        mb.tube([Vector((0, 0, hub)), Vector((tip.x * 0.6, tip.y * 0.6, hub - 0.12)), tip], [0.02, 0.018, 0.016], m, t, segments=6)
        _wick(mb, tip.x, tip.y, tip.z, m, t)
        mb.sphere((tip.x * 0.5, tip.y * 0.5, hub - 0.2), 0.03, "prop:plain:clear", "#e0f0ff", subdivisions=1, scale=(1, 1, 1.8))


def _clock(mb, w, h, d, m, t, trim, tt, cloth, rng):
    _soft(mb, (-w * 0.35, -d * 0.4, 0), (w * 0.35, d * 0.4, h * 0.1), m, t)
    _soft(mb, (-w * 0.28, -d * 0.35, h * 0.1), (w * 0.28, d * 0.35, h * 0.72), m, t)
    _soft(mb, (-w * 0.34, -d * 0.4, h * 0.72), (w * 0.34, d * 0.4, h * 0.97), m, t, 0.03)
    mb.cylinder((0, d * 0.4, h * 0.85), (0, d * 0.42, h * 0.85), w * 0.22, w * 0.22, "prop:plain", "#f4f0e6", segments=20)
    for k in range(12):
        a = k * math.pi / 6
        mb.box((math.sin(a) * w * 0.18 - 0.008, d * 0.42, h * 0.85 + math.cos(a) * w * 0.18 - 0.008),
               (math.sin(a) * w * 0.18 + 0.008, d * 0.425, h * 0.85 + math.cos(a) * w * 0.18 + 0.008), "prop:plain", "#1a1410")
    mb.tube([Vector((0, d * 0.425, h * 0.85)), Vector((0.05, d * 0.425, h * 0.85 + w * 0.13))], [0.008, 0.005], "prop:plain", "#1a1410", segments=4)
    mb.tube([Vector((0, d * 0.425, h * 0.85)), Vector((w * 0.1, d * 0.425, h * 0.83))], [0.008, 0.005], "prop:plain", "#1a1410", segments=4)
    mb.box((-w * 0.2, d * 0.35, h * 0.2), (w * 0.2, d * 0.37, h * 0.68), "prop:plain:clear", "#d8e8f0")
    mb.tube([Vector((0, d * 0.3, h * 0.66)), Vector((0, d * 0.3, h * 0.32))], [0.01, 0.01], trim, tt, segments=5)
    mb.cylinder((0, d * 0.27, h * 0.3), (0, d * 0.33, h * 0.3), 0.07, 0.07, trim, tt, segments=14)


def _platform(mb, w, h, d, m, t, trim, tt, cloth, rng):
    top = h
    _soft(mb, (-w / 2, -d / 2, top - 0.1), (w / 2, d / 2, top), m, t, 0.012)
    for sx in (-1, 1):
        mb.box((sx * w * 0.3 - 0.03, -d / 2 + 0.02, top - 0.2), (sx * w * 0.3 + 0.03, d / 2 - 0.02, top - 0.1), m, _shade(t, 0.8))


def _shade(hex_color, k):
    c = hex_color.lstrip("#")
    r, g, b = (int(c[i:i + 2], 16) for i in (0, 2, 4))
    return "#%02x%02x%02x" % (int(r * k), int(g * k), int(b * k))


PIECES = {
    "table": _table, "chair": _chair, "door": _door, "bed": _bed, "chest": _chest, "workbench": _workbench,
    "bookcase": _bookcase, "piano": _piano, "sofa": _sofa, "bathtub": _bathtub, "lamp": _lamp, "lantern": _lantern,
    "candle": _candle, "candelabra": _candelabra, "chandelier": _chandelier, "clock": _clock, "platform": _platform,
}
