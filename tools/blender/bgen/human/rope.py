"""Rope for the scarecrow (scarecrow.py, ``rope``: its colour): a three-strand rope swept along a path with its lay
twisting round it and shaded into the grooves, tied round the sack's neck and round the waist for a belt, each with
a knot on one side and two ends hanging from it, frayed into loose fibres at the tips.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import matter
from .features import spline, sweep


def twisted(bm, layer, path, r, color, lay=7.0):
    """A rope of radius ``r`` along ``path`` (Vectors), its strands making a turn every ``lay`` radii."""
    path = [Vector(p) for p in path]
    length = sum((b - a).length for a, b in zip(path, path[1:]))
    path = spline(path, max(4, int(length / (0.55 * r))))
    w = length / (lay * r) * 2 * math.pi
    wave = lambda t, a: math.cos(3 * a - w * t)

    def section(t, a):
        rr = r * (0.84 + 0.16 * wave(t, a))
        return math.cos(a) * rr, math.sin(a) * rr
    n0 = len(bm.faces)
    out = sweep(bm, path, section, 9)
    shade = {v: 0.66 + 0.34 * (0.5 + 0.5 * wave(t, a)) for v, t, a in out}
    _color(bm, layer, n0, color, lambda v: shade.get(v, 0.8))


def _color(bm, layer, n0, color, shade):
    c = matter.hexrgb(color)
    bm.faces.ensure_lookup_table()
    for f in bm.faces[n0:]:
        for loop in f.loops:
            k = shade(loop.vert)
            loop[layer] = (c[0] * k, c[1] * k, c[2] * k, 1.0)


def knot(bm, layer, centre, along, out, r, color):
    """Two turns of rope crossing over the rope at ``centre`` (``along`` it, ``out`` away from what it binds)."""
    up = along.cross(out).normalized()
    for u, v, off in ((out, up, 0.5), (along, up, 1.3)):
        ring = [centre + out * r * off + (u * math.cos(a) + v * math.sin(a)) * r * 1.5
                for a in np.linspace(0, 2 * math.pi * 1.08, 15)]
        twisted(bm, layer, ring, r * 0.9, color)


def end(bm, layer, path, r, color, seed):
    """A hanging end along ``path``, frayed at the tip into a few loose fibres."""
    from .straw import strand
    twisted(bm, layer, path, r, color)
    rng = np.random.default_rng(seed)
    tip, d = Vector(path[-1]), (Vector(path[-1]) - Vector(path[-2])).normalized()
    fibre = matter.hexrgb(color) * 1.08
    for k in range(7):
        a = 2 * math.pi * k / 7
        side = d.orthogonal().normalized()
        side = side * math.cos(a) + d.cross(side) * math.sin(a)
        strand(bm, layer, tip - d * r + side * r * 0.5, d + side * rng.uniform(0.2, 0.6), r * rng.uniform(2.5, 4.5), r * 0.22,
               fibre * rng.uniform(0.85, 1.1), rng, sag=0.2)


def tie(head, field, color, z, knot_at=-0.55):
    """The rope tied round the sack's gathered neck (head-local height ``z``), its two ends hanging over the skirt."""
    from .garments import mesh_from
    axis = np.asarray((0.0, -0.016, z))
    th = np.linspace(0, 2 * np.pi, 48, endpoint=False) + knot_at
    e = np.stack([np.sin(th), np.cos(th), np.zeros_like(th)], 1)
    lo, hi = np.zeros(len(th)), np.full(len(th), 0.15)
    for _ in range(24):
        mid = (lo + hi) / 2
        inside = field((axis + e * mid[:, None]).astype(np.float32)) < 0
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    rad = lo + 0.003
    r = 0.0065 * head.h
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    ring = [head.at(tuple(axis + e[k % len(th)] * rad[k % len(th)])) for k in range(len(th) + 1)]
    twisted(bm, layer, ring, r, color)
    out, along = Vector(tuple(e[0])), Vector((math.cos(knot_at), -math.sin(knot_at), 0))
    knot(bm, layer, ring[0], along, out, r, color)
    for k, sx in enumerate((-1, 1)):
        p0 = np.asarray(axis + e[0] * (rad[0] + 0.008))
        side = np.asarray(tuple(along)) * sx
        pts = [p0 + side * 0.004 * i + e[0] * (0.026 * i - 0.004 * i * i) + np.asarray((0, 0, -0.022 * i)) for i in range(3)]
        end(bm, layer, [head.at(tuple(p)) for p in pts], r * 0.85, color, 31 + k)
    obj = mesh_from(bm, "sack_tie")
    matter.slot(obj, "char:cloth")
    return [obj]


def belt(fr, p, cloth, color, knot_at=0.55):
    """A rope belt round the waist over the clothes ``cloth`` in place of the leather one, knotted at the right
    hip, its ends hanging and following the thigh a little."""
    from .garments import Wardrobe, mesh_from
    from .rig import write_weights
    s, segs = fr.s, 72
    w = Wardrobe(fr, p)
    z = w.waist - 0.005 * s
    r = 0.0085 * s
    radius = w.radial(cloth, z, 0.022 * s, segs) + 0.5 * r
    cy = fr.j["spine"].y
    k0 = int(round(knot_at / (2 * math.pi) * segs))
    at = lambda k, extra=0.0: Vector((math.sin(2 * math.pi * k / segs) * (radius[k % segs] + extra),
                                      cy + math.cos(2 * math.pi * k / segs) * (radius[k % segs] + extra), z))
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    twisted(bm, layer, [at(k0 + i) for i in range(segs + 1)], r, color)
    th = 2 * math.pi * k0 / segs
    out, along = Vector((math.sin(th), math.cos(th), 0)), Vector((math.cos(th), -math.sin(th), 0))
    knot(bm, layer, at(k0), along, out, r, color)
    for k, sx in enumerate((-1, 1)):
        p0 = at(k0, 1.2 * r) + along * sx * 0.007 * s
        pts = [p0 + along * sx * 0.004 * s * i + out * 0.006 * s * i + Vector((0, 0, -0.035 * s * i)) for i in range(4)]
        end(bm, layer, pts, r * 0.85, color, 41 + k)
    obj = mesh_from(bm, "rope_belt")
    matter.slot(obj, "char:cloth")
    top = z - 1.5 * r
    weights = []
    for v in obj.data.vertices:            # the ring and knot on the hips, the ends handing over to the thigh
        t = 0.7 * min(1.0, max(0.0, (top - v.co.z) / (0.1 * s)))
        weights.append({"hips": 1 - t, "thigh.R": t} if t > 0 else {"hips": 1.0})
    write_weights(obj, weights)
    return [obj]
