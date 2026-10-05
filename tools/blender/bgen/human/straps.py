"""The apron's (apron.py) neck strap, waistband and bow: cloth strips of real thickness with stitched edges, swept along
paths laid onto the clothes.

A path is laid by casting rays from outside onto the outermost surface under it (the shirt and its collar, the belt,
the trousers, the apron's own panel and waistband) and lifting it off far enough that the whole width of the strip
clears that surface, then smoothing it; the strip's face turns with the surface under it. The waistband, as wide as
the belt it covers, follows the convex hull of the girth of the belt and the panel round the waist (snug, spanning
the small of the back rather than standing off as a hoop) and is knotted at the back: two loops pinched into the knot
and two tails, cut on the slant, lying over the belt and the seat.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .tailoring import ss


def bvh(objs, keep=None):
    """A BVH of the faces of ``objs`` whose centres pass ``keep``."""
    verts, polys = [], []
    for o in objs:
        base = len(verts)
        verts += [v.co.copy() for v in o.data.vertices]
        polys += [tuple(base + i for i in p.vertices) for p in o.data.polygons if keep is None or keep(p.center)]
    return BVHTree.FromPolygons(verts, polys, epsilon=0.0)


def spline(ctrl, step):
    """Points every ~``step`` along a Catmull-Rom curve through the points ``ctrl``."""
    P = [ctrl[0]] + list(ctrl) + [ctrl[-1]]
    dense = []
    for p0, p1, p2, p3 in zip(P, P[1:], P[2:], P[3:]):
        for t in np.linspace(0.0, 1.0, 16, endpoint=False):
            dense.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3))
    dense = np.array(dense + [ctrl[-1]])
    arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(dense, axis=0), axis=1))])
    at = np.linspace(0.0, arc[-1], max(2, int(arc[-1] / step)) + 1)
    return np.stack([np.interp(at, arc, dense[:, k]) for k in range(3)], 1)


def _hit(trees, q, d):
    """Where a ray from outside along ``d`` first meets the surfaces ``trees`` on its way to ``q``: (point, normal)."""
    hits = [h for h in (t.ray_cast(Vector(q + d * 0.08), Vector(-d), 0.16) for t in trees) if h[0] is not None and h[1].length > 0.5]
    if not hits:
        return None, None
    loc, n, _, _ = min(hits, key=lambda h: h[3])
    return np.array(loc), (np.array(n) if np.dot(n, d) > 0 else -np.array(n))


def lay(trees, pts, outward, lift, fixed=0, iters=3, half=0.0, face=None, taut=False):
    """Lays a strip's path on the outermost surface of ``trees``, ``lift`` off it across the strip's ``half`` width:
    each point moves to where rays from outside along ``outward(p)`` meet a surface (``taut``: only ever outward, so
    the strip spans steps like one under tension), and the path is smoothed, a few times over (the first ``fixed``
    points stay). The strip faces the surface's normal n, or ``face(p, n)``. Returns the points and the (smoothed)
    directions the strip faces."""
    pts = np.array(pts, dtype=float)
    nrm = np.tile((0.0, 0.0, 1.0), (len(pts), 1))
    for it in range(iters + 1):
        for i, p in enumerate(pts):
            d = outward(p)
            loc, n = _hit(trees, p, d)
            if loc is None:
                continue
            nrm[i] = n if face is None else face(p, n)
            side = np.cross(pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)], nrm[i])
            side /= max(np.linalg.norm(side), 1e-9)
            need = lift - np.dot(p - loc, n)
            for off in (half, -half) if half else ():
                eloc, en = _hit(trees, p + side * off, d)
                if eloc is not None:
                    need = max(need, lift - np.dot(p + side * off - eloc, en))
            if i >= fixed:
                pts[i] = p + n * (need if it < iters and not taut else max(0.0, need))
        if it < iters:
            inner = pts[1:-1] * 0.5 + (pts[:-2] + pts[2:]) * 0.25
            pts[max(1, fixed):-1] = inner[max(1, fixed) - 1:]
    for _ in range(8):
        nrm[1:-1] = nrm[1:-1] * 0.5 + (nrm[:-2] + nrm[2:]) * 0.25
    return pts, nrm / np.linalg.norm(nrm, axis=1)[:, None]


def smooth_w(wts, fixed=0, passes=4):
    """Skin weights along a strip, smoothed so neighbouring points move alike (the first ``fixed`` stay)."""
    bones = sorted({b for d in wts for b in d})
    W = np.array([[d.get(b, 0.0) for b in bones] for d in wts])
    for _ in range(passes):
        W[max(1, fixed):-1] = W[max(1, fixed):-1] * 0.5 + (W[max(1, fixed) - 1:-2] + W[max(1, fixed) + 1:]) * 0.25
    return [{b: x for b, x in zip(bones, row) if x > 1e-4} for row in W]


def ellipsoid(bm, centre, radii, segs=12, rings=8):
    """A closed ellipsoid of semi-axes ``radii`` round ``centre``, built in a fixed order; returns its vertices."""
    c, (rx, ry, rz) = Vector(centre), radii
    lo, hi = bm.verts.new(c - Vector((0, 0, rz))), bm.verts.new(c + Vector((0, 0, rz)))
    rs = [[bm.verts.new(c + Vector((rx * math.sin(f) * math.cos(u), ry * math.sin(f) * math.sin(u), -rz * math.cos(f))))
           for u in np.linspace(0.0, 2 * np.pi, segs, endpoint=False)] for f in np.linspace(0.0, np.pi, rings + 1)[1:-1]]
    for k in range(segs):
        m = (k + 1) % segs
        bm.faces.new((lo, rs[0][m], rs[0][k]))
        bm.faces.new((hi, rs[-1][k], rs[-1][m]))
        for r0, r1 in zip(rs, rs[1:]):
            bm.faces.new((r0[k], r0[m], r1[m], r1[k]))
    return [lo, hi] + [v for r in rs for v in r]


def strip_profile(width, thick, s):
    """A cloth strip's cross-section [(across, through, shade of the face to the next point)]: its outer face stitched
    along both edges, its sides rounded."""
    hw, ht = width / 2, thick / 2
    e, a, b = 0.35 * thick, 0.0028 * s, 0.0041 * s
    return [(-hw + e, ht, 1.0), (-hw + a, ht, 0.72), (-hw + b, ht, 1.0), (hw - b, ht, 0.72), (hw - a, ht, 1.0), (hw - e, ht, 0.92),
            (hw, 0.0, 0.88), (hw - e, -ht, 0.8), (-hw + e, -ht, 0.88), (-hw, 0.0, 0.92)]


def neck_strap(a, panel):
    """The neck strap of apron ``a``: sewn onto the bib's top corners, over the collarbones and round the back of the
    neck on the collar."""
    s, j, w = a.s, a.j, a.w
    Ts, W = 0.0032 * s, 0.02 * s
    tree = bvh(a.clothes + [a.head_obj, panel], lambda c: not w._on_arm(c))
    neck = np.array(j["neck"][:])
    x = a.half_width(a.top) - 0.004 * s - W / 2
    P, _, _ = a.place(np.array([[x, x + 0.01 * s]] * 2), np.array([[a.top - 0.028 * s] * 2, [a.top + 0.012 * s] * 2]))
    zc = w.collar_z
    side = []
    for phi, dz in ((0.75, -0.013), (1.5, -0.011), (2.3, -0.008)):
        d = Vector((math.sin(phi), math.cos(phi), 0.0))
        o = Vector((neck[0], neck[1], zc + dz * s))
        loc, _, _, dist = tree.ray_cast(o + d * 0.25 * s, -d, 0.25 * s)
        side.append(np.array((o + d * (0.25 * s - dist if loc else 0.08 * s))[:]))
    back = np.array((0.0, neck[1], zc - 0.006 * s))
    loc, _, _, dist = tree.ray_cast(Vector(back) + Vector((0, -0.25 * s, 0)), Vector((0, 1, 0)), 0.25 * s)
    back[1] -= 0.25 * s - dist if loc else 0.08 * s
    right = [P[0, 0], P[1, 0]] + side
    mirror = lambda p: p * np.array((-1.0, 1.0, 1.0))
    ctrl = right + [back] + [mirror(p) for p in reversed(right)]

    def outward(p):
        d = np.array((p[0] - neck[0], p[1] - neck[1], 0.0))
        d = d / max(np.linalg.norm(d), 1e-6) + np.array((0.0, 0.0, 0.35))
        return d / np.linalg.norm(d)

    def face(p, n):             # flat on the bib, then turning to stand round the neck like a collar band
        k = ss(a.top + 0.02 * s, zc - 0.03 * s, p[2])
        u = n * (1 - k) + outward(p) * k
        return u / np.linalg.norm(u)

    pts, ups = lay([tree], spline(ctrl, 0.011 * s), outward, 0.0018 * s + Ts / 2, iters=6, half=W / 2, face=face, taut=True)
    a.sweep(pts, ups, strip_profile(W, Ts, s), smooth_w([a.torso_w(p) for p in pts]))


def waist_ties(a, panel):
    """The waistband of apron ``a`` over its panel and the belt, knotted at the back in a bow with two loops and two
    tails. The band hugs what it is tied over: its path is the hull of their girth round the body's axis (spanning
    the small of the back, never standing off as a hoop), its inner face just clear of them."""
    from .skirts import hull
    s, w, cy = a.s, a.w, a.j["spine"].y
    Tb, Wb, segs = 0.0035 * s, a.band_w, 64
    # (the girth along each of its edges and across its middle: it lies on the slope from the waist out over the
    # hips like a cone, both edges down on the cloth, instead of a cylinder as wide as its widest edge)
    girth = [hull(w.radial(a.clothes + [panel], a.tie_z + dz, 0.006 * s, segs)) for dz in (Wb * 0.42, 0.0, -Wb * 0.42)]
    rad = np.maximum((girth[0] + girth[2]) / 2, girth[1]) + 0.0008 * s + Tb / 2
    slope = (girth[0] - girth[2]) / (0.84 * Wb)
    ang = 2 * np.pi * np.arange(segs) / segs
    radial = np.stack([np.sin(ang), np.cos(ang), np.zeros(segs)], 1)
    ups = radial - np.outer(slope, (0.0, 0.0, 1.0))
    points = radial * rad[:, None] + np.array((0, cy, a.tie_z))
    hips = w.waist_weights(a.clothes[0])        # (girded like a belt: see Wardrobe.girth)
    a.sweep(points, ups, strip_profile(Wb, Tb, s), [hips(Vector(p)) for p in points], closed=True)
    hips = hips(Vector(points[segs // 2]))      # the knot's, at the back
    bmesh.ops.recalc_face_normals(a.bm, faces=a.bm.faces[:])
    a.bm.normal_update()
    trees = [bvh(a.clothes + [panel], lambda c: not w._on_arm(c)), BVHTree.FromBMesh(a.bm)]
    outer = rad[segs // 2] + Tb / 2               # the band's outer face at the back
    knot = np.array((0.0, cy - outer - 0.0058 * s, a.tie_z - 0.003 * s))
    for v in ellipsoid(a.bm, knot, np.array((0.013, 0.0068, 0.0145)) * s):
        a.vk[v], a.wt[v] = 0.93, hips
    prof = strip_profile(0.026 * s, 0.003 * s, s)
    phis = np.linspace(0.0, 2 * np.pi, 20, endpoint=False)
    d, length, tilt = 0.006 * s, 0.058 * s, math.radians(14)
    for sx in (1, -1):                            # the loops, pinched at the knot, standing out from the back
        X = np.array((sx * math.cos(tilt), 0.0, math.sin(tilt)))
        along = length / 2 * (1 - np.cos(phis))
        out = 0.0098 * s + d * np.sin(phis) - 0.4 * along ** 2 / (2 * outer)
        pts = np.array((0.0, cy, knot[2])) + np.outer(along, X) + np.outer(-(outer + out), (0, 1, 0))
        tang = np.roll(pts, -1, 0) - np.roll(pts, 1, 0)
        ups = np.cross(tang, np.cross(X, (0.0, -1.0, 0.0)))
        ups *= np.sign(np.einsum("ij,ij->i", ups, pts - pts.mean(0)))[:, None]
        a.sweep(pts, ups, prof, [hips] * len(pts), widths=0.45 + 0.55 * np.sin(phis / 2) ** 2, closed=True)
    outward = lambda p: np.array((p[0], p[1] - cy, 0.0)) / max(math.hypot(p[0], p[1] - cy), 1e-6)
    for sx, deg, tl in ((1, 11, 0.15), (-1, 17, 0.13)):   # the tails, lying over the belt and the seat
        t = math.radians(deg)
        u = np.linspace(0.0, 1.0, 14)
        pts = knot + np.array((sx * 0.004 * s, 0, -0.004 * s)) + np.outer(u * tl * s, (sx * math.sin(t), 0, -math.cos(t)))
        pts, ups = lay(trees, pts, outward, 0.0035 * s, fixed=1, iters=4, half=0.013 * s, taut=True)
        wts = []
        for p, k in zip(pts, ss(0.05, 0.5, u)):       # from the knot's weights to the seat's own
            tw = a.torso_w(p)
            wts.append({b: (1 - k) * hips.get(b, 0.0) + k * tw.get(b, 0.0) for b in sorted(set(tw) | set(hips))})
        a.sweep(pts, ups, prof, smooth_w(wts, fixed=1), cut=0.6 * sx)
