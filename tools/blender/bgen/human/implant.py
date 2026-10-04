"""The Cyborg's head machinery (``implant``: the colour of its light), modelled on the head's own distance field
(head-local units, see headshape): a metal plate fitted over the right side of the skull from the temple to the back
of the head, its edge stepped above the ear, split into panels by seams and set with rivets; a bar down the temple
into a housing that rings the right eye, and a glowing lens domed over the eye. Everything follows the head.
"""
import math

import bmesh
import numpy as np

from . import fieldmesh, shellmesh
from .garments import mesh_from, set_slot
from .paint import _ensure_layer, hex3
from .rig import rigid
from .sdf import F, capsule, carve, smax, smin, sphere

BOUNDS = ((-0.012, -0.135, -0.07), (0.1, 0.105, 0.135))
SOURCES = ("headshape.py", "earshape.py", "creature.py", "implant.py")
RIVETS = [(0.03, 0.07), (-0.01, 0.075), (-0.05, 0.06), (-0.085, 0.03), (-0.095, -0.01), (0.045, 0.098), (0.0, 0.115)]


def _half(p, n, c):
    """Signed distance to a plane through ``c`` with outward normal ``n`` (inside where < 0)."""
    return (p - np.asarray(c, dtype=F)) @ np.asarray(n, dtype=F)


def _outline(p):
    """The plate's outline on the skull: right of a line near the crown, behind the temple, above the ear (and down
    behind it), with rounded corners."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    top = F(0.012) - x + F(0.006) * np.clip((y + F(0.02)) / F(0.06), -1, 1)       # the edge over the crown
    front = y - F(0.062) + F(0.25) * np.maximum(z - F(0.06), F(0))               # behind the temple
    above_ear = F(0.032) - z
    behind_ear = F(-0.042) - y
    lower = np.minimum(above_ear, smax(behind_ear, F(-0.04) - z, 0.004))         # stepped down behind the ear
    d = smax(smax(top, front, 0.012), lower, 0.008)
    return d


def field(shape):
    eye = np.asarray(shape.eyes[1], dtype=F)
    lo, hi = np.zeros(len(RIVETS), dtype=F), np.full(len(RIVETS), 0.12, dtype=F)
    yz = np.asarray(RIVETS, dtype=F)
    for _ in range(26):                 # the rivets sit on the plate's face (found along +x from the midline)
        mid = (lo + hi) * F(0.5)
        inside = shape.field(np.stack([mid, yz[:, 0], yz[:, 1]], 1), ears=False, face=False) < F(0.0042)
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    rivets = np.stack([lo, yz[:, 0], yz[:, 1]], 1)
    rivets = rivets[(lo > 0.001) & (_outline(rivets) < -0.004)]          # (only those on the plate)
    ex, ey, ez = (float(v) for v in eye)                 # the temple bar: from the plate's front edge to the housing
    ends = shape._surface([(0.02, 0.05, 0.034), (ex + 0.0147, ey - 0.03, ez + 0.0103)],
                          [(0.13, 0.05, 0.034), (ex + 0.0147, ey + 0.05, ez + 0.0103)])
    mids = [ends[0] + (ends[1] - ends[0]) * t for t in np.linspace(0, 1, 6)]
    out = np.asarray((0.9, 0.44, 0.0), dtype=F)
    bar_pts = shape._surface([m - out * 0.03 for m in mids], [m + out * 0.04 for m in mids])

    def f(p):
        skull = shape.field(p, ears=False, face=False)
        plate = smax(smax(skull - F(0.0042), F(0.0007) - skull, 0.0), _outline(p), 0.0018)
        for y0, z0 in ((-0.022, None), (None, 0.07)):          # panel seams
            line = np.abs(p[:, 1] - F(y0)) - F(0.0012) if y0 is not None else np.abs(p[:, 2] - F(z0)) - F(0.0012)
            plate = smax(plate, -smax(line, F(0.003) - skull, 0.0006), 0.0006)
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
    obj = fieldmesh.build(field(shape), BOUNDS, 0.00075, 6500, "implant", ("implant", shape.key()), SOURCES, "headwear",
                          min_piece=60)
    shellmesh.place(obj, head)
    me = obj.data
    layer = _ensure_layer(me)
    base = hex3(p.get("armorColor", "#9aa6b2"))
    n = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", n)
    k = 0.88 + 0.14 * np.clip(n.reshape(-1, 3)[:, 2], -1, 1)
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
