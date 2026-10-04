"""The Selenian's closed helm (helmets.py ``helmet: solar``), modelled on the head's own distance field (head-local
units, see headshape) like the implant: a smooth shell over the skull and face (its hollows filled in, so the face
reads as a mask) with round cups over the ears, with a heavy brow, a ridge from the brow back over the crown, cheek
guards running down to the chin, vents over the mouth and two slanted eye slits lit from inside (``glow``); a gorget
ring closes the neck under it, and flame blades (solarblades.py) rise along the ridge and sweep back from the
temples. Colours: ``helmetColor`` (default ``armorColor``), with the ridge and brow in ``trim``.
"""
import math

import numpy as np
from mathutils import Vector

from . import fieldmesh, shellmesh
from .paint import _ensure_layer, hex3
from .garments import set_slot
from .rig import rigid
from .sdf import F, ellipsoid, smax, smin

SOURCES = ("headshape.py", "earshape.py", "creature.py", "solarhelm.py")
T, THICK = 0.014, 0.007                 # the shell's outer face off the head, and its thickness


def _parts(shape):
    """Field functions (head-local) for the helm: base (distance off the filled-in head), raised (trim) parts,
    the eye slits and the bottom edge."""
    eyes = {sx: np.asarray(shape.eyes[sx], dtype=F) for sx in (1, -1)}
    ez, ey = float(eyes[1][2]), float(eyes[1][1])

    cups = [np.asarray(ear.centre, dtype=F) for ear in shape.ears.values()]

    def base(p):
        d = smin(shape.field(p, ears=False, face=True), shape.field(p, ears=False, face=False) - F(0.004), 0.03)
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

    def raised(p, b, trim=False):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ridge = smax(np.abs(x) - F(0.008), F(ez + 0.03) - z + F(0.3) * np.maximum(y - F(0.06), F(0)), 0.004)
        brow_z = F(ez + 0.025) + F(0.25) * np.abs(x)
        brow = smax(smax(np.abs(z - brow_z) - F(0.0085), F(0.0) - y, 0.01), np.abs(x) - F(0.072), 0.008)
        cheek = smax(smax(z - F(ez - 0.022) + F(0.25) * (np.abs(x) - F(0.05)), F(0.034) - np.abs(x), 0.006),
                     F(-0.02) - y, 0.01)
        out = smin(ridge, brow, 0.004) if trim else smin(smin(ridge, brow, 0.004), cheek, 0.004)
        return smax(out, -(b - F(T - THICK)), 0.0)

    return base, raised, slits, bottom, ez, ey


def field(shape):
    base, raised, slits, bottom, ez, ey = _parts(shape)

    def f(p):
        b = base(p)
        shell = smax(b - F(T), F(T - THICK) - b, 0.0)
        r = raised(p, b)
        thick = smax(b - F(T + 0.0055), r, 0.003)                    # the raised parts stand proud of the shell
        d = smin(shell, smax(thick, F(T - THICK) - b, 0.0), 0.003)
        d = smax(d, -smax(slits(p), F(T - 0.0005 - THICK - 0.003) - b, 0.0), 0.0015)
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        for k in range(3):                                                # vents over the mouth
            vent = smax(np.abs(z - F(-0.068 - 0.011 * k)) - F(0.0016), np.abs(x) - F(0.022 - 0.003 * k), 0.001)
            vent = smax(vent, F(0.04) - y, 0.002)
            d = smax(d, -smax(vent, F(T - 0.003) - b, 0.0008), 0.0008)
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
    obj = fieldmesh.build(field(shape), bounds, 0.0011, 8000, "helmet", ("solarhelm", shape.key()), SOURCES, "headwear")
    co = shellmesh.place(obj, head)
    base, raised, _, _, ez, ey = _parts(shape)
    trim_part = raised(co.astype(F), base(co.astype(F)), trim=True) < F(0.0015)
    metal, trim = hex3(p.get("helmetColor", p.get("armorColor", "#f07a20"))), hex3(p.get("trim", "#b8301a"))
    me = obj.data
    n = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", n)
    k = 0.86 + 0.16 * np.clip(n.reshape(-1, 3)[:, 2], -1, 1)
    layer = _ensure_layer(me)
    for loop in me.loops:
        i = loop.vertex_index
        layer.data[loop.index].color_srgb = (*(min(1.0, c * k[i]) for c in (trim if trim_part[i] else metal)), 1.0)
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
    """Flame blades along the helm's ridge, sweeping back, and one back from each temple."""
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
    for sx in (1, -1):
        d = Vector((sx * 0.85, 0.15, 0.5))
        root, _ = forge.surface(helm, c, d)
        forge.blade(helm, root, Vector((sx * 0.55, -0.45, 0.7)), Vector((0, -1, 0.1)), 0.08 * h, 0.03 * h, curl=1.1,
                    thick=0.005 * h)
    return forge.finish()
