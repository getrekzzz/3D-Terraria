"""The Selenian's closed helm (helmets.py ``helmet: solar``), a distance field in head-local units (see headshape): a
smooth shell over a hull of three ellipsoids round the head (the dome, a rounded mask over the face and a band round
the jaw and nape: clear of the head, none of its forms showing through), built up from hard-edged plates standing
proud of it: a face plate from the brow down over the cheeks, a round rosette over each ear, a heavy brow and a
ridge from the brow back over the crown (their edges meshed as clean lines); vents over the mouth and two slanted eye
slits lit from inside (``glow``); a gorget ring closes the neck under it, and flame blades (solarblades.py) rise along
the ridge. Colours: ``helmetColor`` (default ``armorColor``), with the ridge, brow and rosettes in ``trim``.
"""
import math

import numpy as np
from mathutils import Vector

from . import fieldmesh, shellmesh
from .paint import _ensure_layer, hex3
from .platefit import field_normals, set_normals
from .garments import set_slot
from .rig import rigid
from .sdf import F, ellipsoid, gradient, smax, smin

SOURCES = ("headshape.py", "earshape.py", "creature.py", "solarhelm.py")
T, THICK = 0.014, 0.007                 # the shell's outer face off the head, and its thickness
CRANIUM = ((0.0, -0.006, 0.0), (0.086, 0.12, 0.12))        # the helm's base, a smooth hull round the head: the dome,
MASK = ((0.0, 0.035, -0.052), (0.068, 0.09, 0.082))         # the front drawn over the face (nose, lips and chin inside)
NAPE = ((0.0, 0.0, -0.08), (0.086, 0.09, 0.075))            # and round the jaw and the nape (the ears within the shell)
PLATE, BROW, ROSETTE = 0.003, 0.0058, 0.0042               # how far each stands proud of the shell


def _ease(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def _parts(shape):
    """Field functions (head-local) for the helm: base (distance off the hull), relief (the shell's outer face with
    its plates, given base), trim (the outline of the trim-coloured parts, < 0 inside), the eye slits, the bottom edge
    and the lines along the top and the foot of each plate's edge (for meshing)."""
    eyes = {sx: np.asarray(shape.eyes[sx], dtype=F) for sx in (1, -1)}
    ez, ey = float(eyes[1][2]), float(eyes[1][1])
    ears = [np.asarray(ear.centre, dtype=F) for ear in shape.ears.values()]

    def base(p):                         # (a bigger head than the hull's still fits: the shell follows it out)
        d = smin(smin(ellipsoid(p, *CRANIUM), ellipsoid(p, *MASK), 0.03), ellipsoid(p, *NAPE), 0.03)
        return np.minimum(d, shape.field(p, ears=False, face=False) - F(0.004))

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

    def brow_z(x):
        return F(ez + 0.025) + F(0.25) * np.abs(x)

    def face(p):                         # the face plate: up to the brow, its sides down the cheeks narrowing to the chin
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        yaw = np.arctan2(np.abs(x), y)
        side = (yaw - F(0.6) - F(0.24) * _ease((z + F(0.11)) / F(0.12))) * F(0.09)
        return smax(side, z - brow_z(x) + F(0.002), 0.006)

    def ridge_brow(p):                   # (the brow's ends rounded and sinking to the plate, short of the sides)
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ridge = smax(np.abs(x) - F(0.008), F(ez + 0.03) - z + F(0.3) * np.maximum(y - F(0.06), F(0)), 0.004)
        brow = smax(smax(np.abs(z - brow_z(x)) - F(0.0085), F(0.0) - y, 0.01), np.abs(x) - F(0.063), 0.008)
        return ridge, brow

    def rosette(p):                      # (distance off the ears' axis, across the head)
        c = ears[0]
        return np.sqrt((p[:, 1] - c[1]) ** 2 + (p[:, 2] - c[2]) ** 2) - F(0.024)

    def trim(p):
        ridge, brow = ridge_brow(p)
        return np.minimum(smin(ridge, brow, 0.004), rosette(p))

    def relief(p, b):
        """The shell's outer face: the hull offset by T, with each plate standing proud of it on a crisp edge."""
        x = p[:, 0]
        d = smin(b - F(T), smax(b - F(T + PLATE), face(p), 0.0012), 0.0008)
        ridge, brow = ridge_brow(p)
        sink = F(PLATE + 0.0012) + F(BROW - PLATE - 0.0012) * _ease((F(0.063) - np.abs(x)) / F(0.02))
        d = smin(d, smax(b - F(T + BROW), ridge, 0.0025), 0.0015)
        d = smin(d, smax(b - F(T) - sink, brow, 0.0025), 0.0015)
        return smin(d, smax(b - F(T + ROSETTE), rosette(p), 0.0015), 0.001)

    def outer(fn):                       # (a line on the outer face only)
        return lambda p: np.maximum(fn(p), F(T - 0.003) - base(p))
    lines = [outer(lambda p, k=k: face(p) + F(k)) for k in (0.001, -0.001)]
    lines += [outer(lambda p, k=k: rosette(p) + F(k)) for k in (0.0012, -0.0012)]
    lines += [outer(lambda p, k=k: trim(p) + F(k)) for k in (0.0025, -0.0015)]   # (the second: where its colour starts)
    return base, relief, trim, slits, bottom, ez, ey, lines


def field(shape):
    base, relief, trim, slits, bottom, ez, ey, _ = _parts(shape)

    def f(p):
        b = base(p)
        d = smax(relief(p, b), F(T - THICK) - b, 0.0)
        d = smax(d, -smax(slits(p), F(T - 0.0005 - THICK - 0.003) - b, 0.0), 0.0012)
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        for k in range(3):                                                # vents over the mouth, in the face plate
            vent = smax(np.abs(z - F(-0.067 - 0.012 * k)) - F(0.0021), np.abs(x) - F(0.022 - 0.003 * k), 0.0018)
            vent = smax(vent, F(0.04) - y, 0.002)
            d = smax(d, -smax(vent, F(T + PLATE - 0.0035) - b, 0.001), 0.0012)
        return smax(d, bottom(p), 0.003)
    return f


def glow_field(shape):
    """A thin glowing panel just inside the helm behind each eye slit."""
    base, relief, trim, slits, bottom, ez, ey, _ = _parts(shape)

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
    parts = _parts(shape)
    edge = parts[7][-1]                                                   # (where the trim's colour starts)
    shellmesh.refine(obj, helm, budget=1.2, normals=False, cuts=[(fn, 0.0015) for fn in parts[7]])  # (even triangles,
    _settle(obj, helm)                                 # with clean lines along the top and the foot of each plate's edge)
    n = field_normals(obj, helm, lo=0.2, hi=0.5)       # (shaded from the field throughout: every edge of it is rounded)
    set_normals(obj, n)
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


def _settle(obj, field, steps=2):
    """Puts the vertices (some left on chords where a line was cut across a bend) back onto the surface."""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(F)
    for _ in range(steps):
        g = gradient(field, co, 0.0003)
        co = co - (field(co) / np.maximum(np.einsum("ij,ij->i", g, g), F(1e-9)))[:, None] * g
    me.vertices.foreach_set("co", co.astype(np.float32).ravel())
    me.update()


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
