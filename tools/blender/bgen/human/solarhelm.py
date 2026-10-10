"""The Selenian's closed helm (helmets.py ``helmet: solar``), a distance field in head-local units (see headshape): a
smooth shell over a hull of three ellipsoids round the head (the dome, a rounded mask over the face and a band round
the jaw and nape: clear of the head, none of its forms showing through), with round cups over the ears, a heavy
brow, a ridge from the brow back over the crown, vents over the mouth and two slanted eye slits lit from inside
(``glow``); a gorget ring closes the neck under it, and flame blades (solarblades.py) rise along the ridge. Colours:
``helmetColor`` (default ``armorColor``), with the ridge and brow in ``trim``.
"""
import math

import numpy as np
from mathutils import Vector

from . import fieldmesh, shellmesh
from .paint import _ensure_layer, hex3
from .platefit import field_normals, set_normals
from .garments import set_slot
from .rig import rigid
from .sdf import F, ellipsoid, smax, smin

SOURCES = ("headshape.py", "earshape.py", "creature.py", "solarhelm.py")
T, THICK = 0.014, 0.007                 # the shell's outer face off the head, and its thickness
CRANIUM = ((0.0, -0.006, 0.0), (0.084, 0.12, 0.12))        # the helm's base, a smooth hull round the head: the dome,
MASK = ((0.0, 0.035, -0.05), (0.068, 0.088, 0.08))          # the front drawn over the face (nose, lips and chin inside)
NAPE = ((0.0, 0.0, -0.08), (0.084, 0.09, 0.075))            # and round the jaw and the nape


def _parts(shape):
    """Field functions (head-local) for the helm: base (distance off the filled-in head), raised (trim) parts,
    the eye slits and the bottom edge."""
    eyes = {sx: np.asarray(shape.eyes[sx], dtype=F) for sx in (1, -1)}
    ez, ey = float(eyes[1][2]), float(eyes[1][1])

    cups = [np.asarray(ear.centre, dtype=F) for ear in shape.ears.values()]

    def base(p):                         # (clear of the head by 4 mm or more, without its lumps; a bigger head still fits)
        d = smin(smin(ellipsoid(p, *CRANIUM), ellipsoid(p, *MASK), 0.03), ellipsoid(p, *NAPE), 0.03)
        d = np.minimum(d, shape.field(p, ears=False, face=False) - F(0.004))
        for c in cups:                   # round cups over the ears
            d = smin(d, ellipsoid(p, tuple(c), (0.021, 0.029, 0.037)), 0.016)
        return d

    def slits(p):
        d = np.ones(len(p), dtype=F)
        for sx, e in eyes.items():
            dx, dz = p[:, 0] - e[0], p[:, 2] - F(ez - 0.002)
            c, s = math.cos(0.3), math.sin(0.3) * sx
            u, v = dx * F(c) + dz * F(s), dz * F(c) - dx * F(s)           # (its outer end raised: a scowl)
            el = (np.sqrt((u / F(0.023)) ** 2 + (v / F(0.0062)) ** 2) - F(1)) * F(0.0062)
            d = np.minimum(d, el)
        return smax(d, F(ey - 0.035) - p[:, 1], 0.002)

    def bottom(p):
        return F(-0.112) - F(0.3) * p[:, 1] - p[:, 2]

    def raised(p, b):                    # the trim: a ridge from the brow back over the crown, and the brow
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ridge = smax(np.abs(x) - F(0.008), F(ez + 0.03) - z + F(0.3) * np.maximum(y - F(0.06), F(0)), 0.004)
        brow_z = F(ez + 0.025) + F(0.25) * np.abs(x)
        brow = smax(smax(np.abs(z - brow_z) - F(0.0085), F(0.0) - y, 0.01), np.abs(x) - F(0.072), 0.008)
        return smax(smin(ridge, brow, 0.004), -(b - F(T - THICK)), 0.0)

    return base, raised, slits, bottom, ez, ey


def field(shape):
    base, raised, slits, bottom, ez, ey = _parts(shape)

    def f(p):
        b = base(p)
        shell = smax(b - F(T), F(T - THICK) - b, 0.0)
        thick = smax(b - F(T + 0.0055), raised(p, b), 0.003)                # the trim stands proud of the shell
        d = smin(shell, smax(thick, F(T - THICK) - b, 0.0), 0.003)
        d = smax(d, -smax(slits(p), F(T - 0.0005 - THICK - 0.003) - b, 0.0), 0.0015)
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        for k in range(3):                                                # vents over the mouth
            vent = smax(np.abs(z - F(-0.067 - 0.012 * k)) - F(0.0021), np.abs(x) - F(0.022 - 0.003 * k), 0.0018)
            vent = smax(vent, F(0.04) - y, 0.002)
            d = smax(d, -smax(vent, F(T - 0.003) - b, 0.001), 0.0014)
        return smax(d, bottom(p), 0.004)
    return f


def glow_field(shape):
    """A thin glowing panel just inside the helm behind each eye slit."""
    base, raised, slits, bottom, ez, ey = _parts(shape)

    def f(p):
        b = base(p)
        return smax(smax(b - F(T - THICK + 0.0012), F(T - THICK - 0.0025) - b, 0.0), slits(p) - F(0.004), 0.001)
    return f


def build(shop):
    """[gorget ring, helm (metal), eye glow, flame blades, blade glow]."""
    head, p = shop.head, shop.p
    shape = head.shape
    lo, hi = shape.bounds()
    pad = T + 0.012
    bounds = ((lo[0] - pad, lo[1] - pad, -0.17), (hi[0] + pad, hi[1] + pad, hi[2] + pad))
    helm = field(shape)
    obj = fieldmesh.build(helm, bounds, 0.0011, 6800, "helmet", ("solarhelm", shape.key()), SOURCES, "headwear")
    base, raised, _, _, ez, ey = _parts(shape)
    edge = lambda q: raised(q, base(q)) - F(0.0015)                       # (where the trim's colour starts)
    shellmesh.refine(obj, helm, budget=1.2, normals=False)               # (even triangles)
    shellmesh.cut(obj, edge)                                              # (the trim's colour ends on a clean line)
    n = field_normals(obj, helm)
    set_normals(obj, n)                                                   # (smooth faces shaded from the field)
    co = shellmesh.place(obj, head)
    me = obj.data
    trim_face = edge(shellmesh.centres(obj, co).astype(F)) < 0       # (each face one colour: they meet along the cut)
    metal, trim = hex3(p.get("helmetColor", p.get("armorColor", "#f07a20"))), hex3(p.get("trim", "#b8301a"))
    k = 0.86 + 0.16 * np.clip(n[:, 2], -1, 1)
    layer = _ensure_layer(me)
    for poly in me.polygons:
        col = trim if trim_face[poly.index] else metal
        for li, vi in zip(poly.loop_indices, poly.vertices):
            layer.data[li].color_srgb = (*(min(1.0, c * k[vi]) for c in col), 1.0)
    set_slot(obj, "char:metal")
    rigid(obj, "head")
    eyes = fieldmesh.build(glow_field(shape), bounds, 0.0008, 1500, "helmet_eyes", ("solarhelm-eyes", shape.key()), SOURCES,
                           "headwear", min_piece=20)
    shellmesh.place(eyes, head)
    glow = hex3(p.get("glow", "#ffe070"))
    layer = _ensure_layer(eyes.data)
    for loop in eyes.data.loops:
        layer.data[loop.index].color_srgb = (*glow, 1.0)
    set_slot(eyes, "char:glow")
    rigid(eyes, "head")
    neck_z = head.c.z + (-0.112 + 0.3 * 0.06) * head.h            # the helm's edge at the nape (see bottom)
    top = float(neck_z - shop.fr.j["neck"].z) - 0.004 * shop.s
    ring = shop.ring(shop.neck_radius(neck_z - 0.03 * shop.s) + 0.004 * shop.s, top, name="gorget")
    return [ring, obj, eyes] + _crest(shop, obj, metal, trim)


def _crest(shop, helm, metal, trim):
    """Flame blades along the helm's ridge, sweeping back."""
    from .solarblades import Forge
    head = shop.head
    h = head.h
    forge = Forge(trim, metal, tuple(min(1.0, c * 0.4 + 0.62) for c in (1.0, 0.85, 0.3)), shop.glow)
    c = Vector(head.c)
    for k, (a, length, width) in enumerate(((0.6, 0.11, 0.038), (0.2, 0.125, 0.042), (-0.25, 0.115, 0.04), (-0.7, 0.09, 0.034))):
        d = Vector((0, math.sin(a), math.cos(a)))
        root, _ = forge.surface(helm, c + Vector((0, 0, 0.01 * h)), d)
        out = (d + Vector((0, -0.35, 0.7))).normalized()
        forge.blade(helm, root, out, Vector((0, -1, -0.2)), length * h, width * h, curl=1.0, thick=0.0055 * h)
    return forge.finish()
