"""The wide hats' meshes (hats.py sculpts their fields): meshed and evened out, cut along the lines where their
colours change, coloured by region, with a band laid round the crown as a strip of its own (so its edges stay clean)
and the tricorn's edge bound with braid."""
import numpy as np

from . import fieldmesh
from .hats import BAND, Hat
from .sdf import F
from .shellmesh import centres, place, refine

BOUNDS = ((-0.24, -0.26, -0.12), (0.24, 0.24, 0.34))
GRID, TARGET = 0.0011, 15000


def mesh(head, shape, clear):
    """The hat's mesh (world coordinates) and the region each vertex belongs to (crown, brim, under, band, trim)."""
    hat = Hat(shape, head.shape.fem, clear)
    key = ("widehat", shape, round(clear, 4), head.shape.fem)
    obj = fieldmesh.build(hat.field, BOUNDS, GRID, TARGET, f"hat_{shape}", key, ("headwear.py", "hats.py", "hatmesh.py"), "headwear")
    refine(obj, hat.field, cuts=_cuts(hat))
    co = place(obj, head)
    regions = _regions(hat, centres(obj, co))
    if shape == "tricorn":
        regions = _bind(obj, hat, head, regions)
    for z0, z1 in BAND.get(shape, ()):
        regions = _band(obj, hat, head, regions, z0, z1)
    return obj, regions, hat


def _cuts(hat):
    """Where the colours change: the crown meets the brim, the brim's top meets its underside."""
    if hat.shape == "mushroom":     # the gills meet the dome; the spots' edges
        return [lambda co: hat.gill_side(hat.local(co))[2], lambda co: hat.spots(hat.local(co)) - F(0.5)]

    def join(co):
        parts = hat.parts(co)
        return parts["brim"][0] - parts["crown"] if "brim" in parts else np.ones(len(co), F)

    def side(co):
        parts = hat.parts(co)
        return parts["brim"][1] if "brim" in parts else np.ones(len(co), F)

    def badge(co):
        parts = hat.parts(co)
        return parts["badge"] - parts["crown"] if "badge" in parts else np.ones(len(co), F)
    return [join, side, badge]


def _band(obj, hat, head, regions, z0, z1, proud=0.0024, e=0.0012):
    """A band round the crown from ``z0`` to ``z1`` over the brim, lying on it with rounded edges, added to the
    hat's mesh (a separate strip, so its edges stay clean)."""
    import bmesh
    section = ((z0, -0.001), (z0, e), (z0 + e, proud), (z1 - e, proud), (z1, e), (z1, -0.001))
    heights = sorted({z for z, _ in section})
    pts, nrm = hat.band_rows(heights)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    n0, n = len(bm.faces), pts.shape[1]
    c = np.asarray(head.c)
    ring = [[bm.verts.new(c + (pts[heights.index(z), k] + nrm[heights.index(z), k] * o) * head.h) for z, o in section]
            for k in range(n)]
    for k in range(n):
        a, b = ring[k], ring[(k + 1) % n]
        for i in range(len(section)):
            j = (i + 1) % len(section)
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.ensure_lookup_table()
    new = [bm.faces[i] for i in range(n0, len(bm.faces))]
    bmesh.ops.recalc_face_normals(bm, faces=new)
    for f in new:
        f.smooth = True
    n1 = len(bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    return np.concatenate([regions, np.full(n1 - n0, "band", dtype=object)])


def _bind(obj, hat, head, regions, r=0.0034):
    """A binding round the tricorn brim's edge, added to the hat's mesh as its trim."""
    import bmesh
    from mathutils import Vector
    from .features import round_section, spline, sweep
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    n0 = len(bm.faces)
    for path in hat.trim_paths():
        if len(path) > 2:
            sweep(bm, spline([Vector(head.c) + Vector(p) * head.h for p in path], 240), round_section(lambda t: r * head.h), 8,
                  caps=(False, False))
    for f in bm.faces[n0:]:
        f.smooth = True
    n1 = len(bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    return np.concatenate([regions, np.full(n1 - n0, "trim", dtype=object)])


def _regions(hat, co):
    parts = hat.parts(co)
    names = list(parts)
    dist = np.stack([np.abs(parts[n][0] if isinstance(parts[n], tuple) else parts[n]) for n in names], 1)
    region = np.asarray(names, dtype=object)[np.argmin(dist, 1)]
    region = np.where(np.abs(hat.inner(co)) < 0.0016, "under", region)     # the lining
    if "brim" in parts:
        under = (parts["brim"][1] < 0) & (region == "brim")
        region = np.where(under, "under", region)
    if hat.shape == "mushroom":
        q = hat.local(co)
        gill = hat._gill
        ang = np.arctan2(q[:, 1] - hat.yc, q[:, 0])
        shade = np.where(gill, 0.88 + 0.12 * np.cos(ang * 90) ** 2, 1.0)
        region = np.where(gill, "gill", np.where(hat.spots(q) > 0.5, "spot", region))
        return region, shade
    return region
