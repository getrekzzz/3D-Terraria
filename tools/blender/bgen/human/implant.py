"""The Cyborg's head machinery (``implant``: the colour of its light), modelled on the head's own distance field
(head-local units, see headshape): a metal plate fitted over the right side of the skull from the temple to the back
of the head, its lower edge arching round the ear and running down behind it, bevelled thinner at its rim, split into
panels by a seam that follows the arch and one up to the crown, set with rivets along the arch and cut with vents
behind the ear; a bar down the temple into a housing that rings the right eye, and a glowing lens domed over the eye.
Everything follows the head.
"""
import math

import bmesh
import numpy as np

from . import fieldmesh, shellmesh
from .garments import mesh_from, set_slot
from .paint import _ensure_layer, hex3
from .platefit import field_normals
from .rig import rigid
from .sdf import F, capsule, carve, smax, smin, sphere

BOUNDS = ((-0.012, -0.135, -0.075), (0.105, 0.105, 0.135))
SOURCES = ("headshape.py", "earshape.py", "creature.py", "implant.py")
EAR = (-0.0125, -0.016, 0.0295, 0.043)          # the arch round the ear: its centre (y, z) and half-axes, with room
ARCH = (40, 75, 110, 145, 180, 212)              # rivets along it (degrees from the front, round over the top)


def _smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def _arch(p):
    """Distance outside the arch round the ear (< 0 inside it)."""
    ye, ze, ay, az = EAR
    return (np.sqrt(((p[:, 1] - F(ye)) / F(ay)) ** 2 + ((p[:, 2] - F(ze)) / F(az)) ** 2) - F(1)) * F(min(ay, az))


def _outline(p):
    """The plate's outline on the skull: right of a line near the crown, behind the temple, in front of the nape,
    above a line low behind the ear and rising in front of it, and clear of the arch round the ear."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    top = F(0.012) - x + F(0.006) * np.clip((y + F(0.02)) / F(0.06), -1, 1)       # the edge over the crown
    front = y - F(0.056) + F(0.3) * np.maximum(z - F(0.055), F(0))                # behind the temple
    back = F(-0.094) - y
    bottom = F(-0.055) + F(0.06) * _smooth((y + F(0.04)) / F(0.05)) - z           # low behind the ear, up in front
    d = smax(smax(top, front, 0.012), smax(back, bottom, 0.012), 0.012)
    return smax(d, -_arch(p), 0.006)


def _rivets(shape):
    """Rivet centres on the plate's face: along the arch a little way out from its edge, and down the front edge."""
    ye, ze, ay, az = EAR
    yz = [(ye + (ay + 0.0065) * math.cos(math.radians(a)), ze + (az + 0.0065) * math.sin(math.radians(a))) for a in ARCH]
    yz += [(0.049, 0.018), (0.047, 0.046)]
    yz = np.asarray(yz, dtype=F)
    lo, hi = np.zeros(len(yz), dtype=F), np.full(len(yz), 0.12, dtype=F)
    for _ in range(26):                 # (found along +x from the midline)
        mid = (lo + hi) * F(0.5)
        inside = shape.field(np.stack([mid, yz[:, 0], yz[:, 1]], 1), ears=False, face=False) < F(0.0046)
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    rivets = np.stack([lo, yz[:, 0], yz[:, 1]], 1)
    return rivets[(lo > 0.001) & (_outline(rivets) < -0.0035)]          # (only those on the plate)


def _seams(p, below):
    """Grooves (< 0 in them, ``below`` the plate's outer face): one following the arch, one from its top up to the
    crown, three vents behind the ear."""
    ye, ze, ay, az = EAR
    y, z = p[:, 1], p[:, 2]
    arch = np.abs(_arch(p) - F(0.013)) - F(0.0011)
    up = smax(np.abs(y - F(ye)) - F(0.0011), F(ze + az + 0.012) - z, 0.0006)
    d = np.minimum(arch, up)
    for k in range(3):
        vent = smax(np.abs(z - F(-0.006 - 0.01 * k)) - F(0.0014), np.abs(y - F(-0.075)) - F(0.011 - 0.002 * k), 0.001)
        d = np.minimum(d, vent)
    return smax(d, F(-0.0014) - below, 0.0006)


def field(shape):
    eye = np.asarray(shape.eyes[1], dtype=F)
    rivets = _rivets(shape)
    ex, ey, ez = (float(v) for v in eye)                 # the temple bar: from the plate's front edge to the housing
    ends = shape._surface([(0.02, 0.05, 0.034), (ex + 0.0147, ey - 0.03, ez + 0.0103)],
                          [(0.13, 0.05, 0.034), (ex + 0.0147, ey + 0.05, ez + 0.0103)])
    mids = [ends[0] + (ends[1] - ends[0]) * t for t in np.linspace(0, 1, 6)]
    out = np.asarray((0.9, 0.44, 0.0), dtype=F)
    bar_pts = shape._surface([m - out * 0.03 for m in mids], [m + out * 0.04 for m in mids])

    def f(p):
        skull = shape.field(p, ears=False, face=False)
        edge = _outline(p)
        outer = F(0.0034) + F(0.0016) * _smooth(-edge / F(0.009))              # bevelled thinner toward the rim
        plate = smax(smax(skull - outer, F(0.0007) - skull, 0.0), edge, 0.0016)
        plate = smax(plate, -_seams(p, skull - outer), 0.0006)
        face = shape.field(p)
        q = p - eye
        rho = np.sqrt(q[:, 0] ** 2 + q[:, 2] ** 2)
        ring = smax(smax(face - F(0.0055), F(0.0009) - face, 0.0), smax(F(0.0128) - rho, rho - F(0.0215), 0.0), 0.0012)
        ring = smax(ring, F(eye[1] - 0.012) - p[:, 1], 0.002)
        bar = capsule(p, tuple(bar_pts[0]), tuple(bar_pts[1]), 0.0036)
        for a, b in zip(bar_pts[1:], bar_pts[2:]):
            bar = np.minimum(bar, capsule(p, tuple(a), tuple(b), 0.0036))
        bar = smax(bar, F(0.0006) - face, 0.001)
        d = smin(smin(plate, ring, 0.003), bar, 0.004)
        for c in rivets:
            d = smin(d, sphere(p, c, 0.0026), 0.0008)
        return d
    return f


def build(head, p):
    """[plate and housing (metal), lens (glow)], placed on the head and bound to it."""
    shape = head.shape
    f = field(shape)
    obj = fieldmesh.build(f, BOUNDS, 0.00075, 6500, "implant", ("implant", shape.key()), SOURCES, "headwear", min_piece=60)
    shellmesh.refine(obj, f)                    # (smooth faces shaded from the field)
    n = field_normals(obj, f)
    shellmesh.place(obj, head)
    me = obj.data
    layer = _ensure_layer(me)
    base = hex3(p.get("armorColor", "#9aa6b2"))
    k = 0.88 + 0.14 * np.clip(n[:, 2], -1, 1)
    for loop in me.loops:
        c = tuple(min(1.0, x * k[loop.vertex_index]) for x in base)
        layer.data[loop.index].color_srgb = (*c, 1.0)
    set_slot(obj, "char:metal")
    rigid(obj, "head")
    return [obj, _lens(head, hex3(p.get("implant", "#ff2a2a")))]


def _lens(head, color):
    """A glowing lens domed over the right eye, brighter at its centre."""
    e = head.eyes[1]
    r = head.eye_r + 0.0036 * head.h
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    rows, segs = 8, 24
    top = math.asin(min(1.0, 0.0132 * head.h / r))
    grid, level = [], {}
    for i in range(rows + 1):
        th = top * i / rows
        ring = []
        for k in range(segs if i else 1):
            ph = 2 * math.pi * k / segs
            d = np.asarray((math.sin(th) * math.cos(ph), math.cos(th), math.sin(th) * math.sin(ph)))
            v = bm.verts.new(tuple(np.asarray(e) + d * r))
            level[v] = i / rows
            ring.append(v)
        grid.append(ring)
    for a, b in zip(grid, grid[1:]):
        for k in range(segs):
            verts = (a[0], b[k], b[(k + 1) % segs]) if len(a) == 1 else (a[k], b[k], b[(k + 1) % segs], a[(k + 1) % segs])
            f = bm.faces.new(verts)
            for loop in f.loops:                 # a hot white centre fading to the light's colour at the rim
                g = level[loop.vert] ** 1.5
                loop[layer] = (*(min(1.0, c + (1 - c) * 0.6 * (1 - g)) for c in color), 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = mesh_from(bm, "implant_lens")
    set_slot(obj, "char:glow")
    rigid(obj, "head")
    return obj
