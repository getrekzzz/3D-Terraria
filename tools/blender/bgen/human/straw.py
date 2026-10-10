"""Straw for the scarecrow (scarecrow.py, ``straw``: its colour): tufts of thin three-sided stalks, each tapering
to a point and bending under its own weight, sticking out of the cuffs over the gloves, out of the boot tops where
the trousers are stuffed into them, from under the sack's skirt at the collar, and through a few ragged holes torn in
the shirt and trousers (dark ragged decals laid onto the cloth). Each stalk is carried by the body weights where it
starts, so it moves with the cloth it comes out of. All placement is seeded.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import matter
from .features import round_section, sweep

PALE, OCHRE = np.asarray((0.95, 0.88, 0.62)), np.asarray((0.6, 0.43, 0.18))
# holes: (garment, joint, offset from it / s, the side the cloth faces there)
HOLES = (("shirt", "spine", (-0.075, 0.0, 0.07), (0, 1, 0)), ("shirt", "chest", (0.075, 0.0, 0.01), (0, -1, 0)),
         ("pants", "knee.R", (0.0, 0.0, 0.15), (0.2, 1, 0)))


def strand(bm, layer, base, d, length, r, color, rng, sag=0.35):
    """One stalk from ``base`` along ``d``: three-sided, tapering to a point, bending downward by ``sag``."""
    d = Vector(tuple(d)).normalized()
    wob = Vector(tuple(rng.normal(0, 1, 3))) * 0.3
    p = Vector(tuple(base))
    pts = [p.copy()]
    for _ in range(3):
        d = (d + Vector((0, 0, -sag * 0.4)) + wob * 0.35).normalized()
        p = p + d * (length / 3)
        pts.append(p.copy())
    n0 = len(bm.faces)
    out = sweep(bm, pts, round_section(lambda t: r * (1 - 0.7 * t)), 3, caps=(False, True))
    along = {v: t for v, t, _ in out}
    bm.faces.ensure_lookup_table()
    for f in bm.faces[n0:]:
        for loop in f.loops:
            k = 0.74 + 0.26 * along.get(loop.vert, 1.0)
            loop[layer] = (color[0] * k, color[1] * k, color[2] * k, 1.0)


def _shade(base, rng):
    """One stalk's colour: the straw's, paler or more ochre."""
    c = base * rng.uniform(0.82, 1.1)
    m = rng.uniform(-0.3, 0.35)
    return np.clip(c + (PALE - c) * max(m, 0) + (OCHRE - c) * max(-m, 0), 0, 1)


def _tree(objs):
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in objs:
        base = len(verts)
        verts += [v.co.copy() for v in o.data.vertices]
        polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys)


def _rim(obj, centre, axis, band):
    """How far out from ``axis`` through ``centre`` the vertices of ``obj`` within ``band`` of its plane reach."""
    co = matter.coords(obj) - np.asarray(tuple(centre))
    a = np.asarray(tuple(axis))
    h = co @ a
    rad = np.linalg.norm(co - h[:, None] * a, axis=1)
    m = (np.abs(h) < band) & (rad < 0.12)
    return float(np.percentile(rad[m], 90)) if m.any() else 0.04


def _ring(axis):
    e1 = axis.orthogonal().normalized()
    return e1, axis.cross(e1).normalized()


def _hole(bm, layer, tree, loc, n, radius, cloth, rng):
    """A ragged dark hole laid onto the cloth round ``loc``, with a frayed lighter edge."""
    t1 = n.orthogonal().normalized()
    t2 = n.cross(t1)
    k, ring, fray = 18, [], []
    centre = bm.verts.new(loc + n * 0.0016)
    for i in range(k):
        a = 2 * math.pi * i / k
        rr = radius * rng.uniform(0.7, 1.15)
        for scale, out in ((1.0, ring), (1.3, fray)):
            p = loc + (t1 * math.cos(a) + t2 * math.sin(a)) * rr * scale
            hit, hn, _, _ = tree.ray_cast(p + n * 0.03, -n, 0.08)
            hit, hn = (hit, hn) if hit is not None else (p, n)
            out.append(bm.verts.new(hit + hn * (0.0016 if scale == 1.0 else 0.0012)))
    dark, edge = (0.07, 0.05, 0.03, 1.0), tuple(cloth * 1.18) + (1.0,)
    for i in range(k):
        j = (i + 1) % k
        for face, cols in (((centre, ring[i], ring[j]), (dark, dark, dark)), ((ring[i], fray[i], fray[j], ring[j]), (dark, edge, edge, dark))):
            f = bm.faces.new(face)
            for loop, c in zip(f.loops, cols):
                loop[layer] = c


def stuff(fr, head, sack, cloth, color, lookup):
    """Every tuft and hole as one object, weighted stalk by stalk."""
    from .garments import mesh_from
    from .rig import write_weights
    s, j = fr.s, fr.j
    tree, body_w = lookup
    rng = np.random.default_rng(matter.seed_of("straw"))
    base = matter.hexrgb(color)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    groups = []

    def add(at, d, length, r=0.0024 * s, sag=0.35):
        v0 = len(bm.verts)
        strand(bm, layer, at, d, length, r, _shade(base, rng), rng, sag)
        groups.append((v0, len(bm.verts), at))

    up = Vector((0, 0, 1))
    if "shirt" in cloth:                    # cuffs: out over the gloves
        for side in ("R", "L"):
            sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
            a = (wr - sh).normalized()
            c = sh + (wr - sh) * 0.97
            r = _rim(cloth["shirt"], c, a, 0.012 * s)
            e1, e2 = _ring(a)
            for k in range(30):
                phi = 2 * math.pi * k / 30 + rng.uniform(-0.1, 0.1)
                e = e1 * math.cos(phi) + e2 * math.sin(phi)
                beta = rng.uniform(0.25, 1.05)
                add(c - a * 0.012 * s + e * r * 0.7, a * math.cos(beta) + e * math.sin(beta), rng.uniform(0.035, 0.07) * s, sag=0.25)
    if "boots" in cloth:                    # out of the boot tops, where the trousers are stuffed in
        for side, sx in (("R", 1), ("L", -1)):
            co = matter.coords(cloth["boots"])
            co = co[co[:, 0] * sx > 0]
            top = float(co[:, 2].max())
            kn, an = j[f"knee.{side}"], j[f"ankle.{side}"]
            c = kn.lerp(an, (kn.z - top) / (kn.z - an.z))
            r = _rim(cloth["boots"], c, up, 0.012 * s)
            for k in range(26):
                phi = 2 * math.pi * k / 26 + rng.uniform(-0.1, 0.1)
                e = Vector((math.cos(phi), math.sin(phi), 0))
                add(c + e * r * 0.8 - up * 0.008 * s, e * 0.75 + up * rng.uniform(-0.6, 0.55), rng.uniform(0.03, 0.06) * s, sag=0.3)
    # the collar: from under the sack's skirt, out over the shoulders
    q = (matter.coords(sack) - np.asarray(tuple(head.c))) / head.h
    ang = np.arctan2(q[:, 0], q[:, 1] + 0.02)
    bins = np.round(ang / (2 * np.pi) * 48).astype(int) % 48
    for k in range(44):
        b = (int(k * 48 / 44) + int(rng.integers(0, 2))) % 48
        sel = np.flatnonzero(bins == b)
        if not len(sel):
            continue
        low = sel[np.argmin(q[sel, 2])]
        hem = head.at(tuple(q[low]))
        e = Vector((q[low, 0], q[low, 1] + 0.02, 0)).normalized()
        beta = rng.uniform(0.15, 0.8)
        add(hem - e * 0.014 * head.h + up * 0.01 * head.h, e * math.cos(beta) - up * math.sin(beta), rng.uniform(0.035, 0.07) * head.h, sag=0.25)
    # holes torn in the clothes, straw poking out
    for i, (name, joint, off, facing) in enumerate(HOLES):
        if name not in cloth:
            continue
        cl = _tree([cloth[name]])
        n0 = Vector(facing).normalized()
        aim = j[joint] + Vector(off) * s
        loc, n, _, _ = cl.ray_cast(aim + n0 * 0.4 * s, -n0, 0.8 * s)
        if loc is None:
            continue
        v0 = len(bm.verts)
        _hole(bm, layer, cl, loc, n, 0.02 * s, _mean_color(cloth[name]), rng)
        groups.append((v0, len(bm.verts), loc))
        t1 = n.orthogonal().normalized()
        for k in range(12):
            phi = rng.uniform(0, 2 * math.pi)
            t = t1 * math.cos(phi) + n.cross(t1) * math.sin(phi)
            beta = rng.uniform(0.0, 0.8)
            add(loc - n * 0.004 * s + t * rng.uniform(0, 0.008) * s, n * math.cos(beta) + t * math.sin(beta), rng.uniform(0.02, 0.045) * s, sag=0.2)
    obj = mesh_from(bm, "straw")
    weights = [None] * len(obj.data.vertices)
    for v0, v1, at in groups:
        w = body_w[tree.find(at)[1]]
        for i in range(v0, v1):
            weights[i] = w
    write_weights(obj, [w or {"chest": 1.0} for w in weights])
    matter.slot(obj, "char:cloth")
    return [obj]


def _mean_color(obj):
    """A garment's mean colour (sRGB), for the frayed edge of a hole in it."""
    me = obj.data
    layer = me.color_attributes.get("Color") or me.color_attributes[0]
    c = np.empty(len(layer.data) * 4, dtype=np.float32)
    layer.data.foreach_get("color_srgb", c)
    return c.reshape(-1, 4)[:, :3].mean(0).astype(float)
