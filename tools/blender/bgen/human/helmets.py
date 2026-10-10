"""Helmets and head machinery for machines and aliens (``helmet``, ``implant``; see scifi.py):

- bubble: a Martian's clear glass dome over the whole head, seated in a chunky metal neck ring on the shoulders;
- diver: a Storm Diver's big round metal helmet with a wide glass visor dome across the front, a hub at each side and
  a lamp on the brow, on the same neck ring;
- solar: a Selenian's closed helm with glowing eye slits and a crest of flame blades, on a gorget (solarhelm.py);
- implant: a Cyborg's metal plate over the right side of the skull, ringed with seams and rivets and running down the
  temple into a lens housing round the right eye, whose glowing lens replaces the eye's look.

The glass is its own material (``char:glass``: see-through in the game). Domes and the implant follow the head; the
neck ring follows the neck and shoulders it rests on.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import plates as P
from .garments import mesh_from, set_slot
from .paint import _ensure_layer, hex3
from .platefit import set_normals
from .rig import rigid
from .sdf import F, capsule, carve, ellipsoid, smax, smin, sphere

SOURCES = ("helmets.py",)


def _axis(fr):
    """The neck's axis (pointing up, leaning forward with the neck) and the collar height on it."""
    n = (fr.j["head"] - fr.j["neck"]).normalized()
    return n


def collar_height(fr, garments):
    """The top of the shirt round the neck (where the clothes stop under the head)."""
    s = fr.s
    neck = fr.j["neck"]
    top = neck.z
    for o in garments:
        if o.name.split(".")[0] in ("shirt", "robe", "coat"):
            for v in o.data.vertices:
                if math.hypot(v.co.x - neck.x, v.co.y - neck.y) < 0.14 * s:
                    top = max(top, v.co.z)
    return top


class Shop:
    """Makes the helmet parts for one character, colouring and binding them."""

    def __init__(self, fr, head, p, fitter, garments):
        self.fr, self.head, self.p, self.fitter = fr, head, p, fitter
        self.s = fr.s
        self.metal = hex3(p.get("helmetColor", p.get("armorColor", "#a8b0bc")))
        self.glow = hex3(p.get("glow", "#3affc0"))
        self.collar_z = collar_height(fr, garments)
        self.axis = _axis(fr)
        self.key = (round(fr.height, 4), round(fr.build, 4), round(fr.weight, 4), fr.female, round(head.h, 4),
                    round(self.collar_z, 4), p.get("alien"))

    def reach(self, centre, above):
        """How far the head (and anything on it) reaches from ``centre`` above height ``above``."""
        me = self.head_obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        co = co[co[:, 2] > above]
        return float(np.max(np.linalg.norm(co - np.asarray(centre, dtype=np.float32), axis=1)))

    def neck_radius(self, z):
        """The neck's radius round its axis at height ``z`` (from the head mesh, which runs down into the body)."""
        me = self.head_obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        co = co[np.abs(co[:, 2] - z) < 0.012]
        neck = np.asarray(self.fr.j["neck"], dtype=np.float32)
        if not len(co):
            return 0.06 * self.s
        return float(np.percentile(np.hypot(co[:, 0] - neck[0], co[:, 1] - neck[1]), 90))

    def ring(self, radius, top, name="neck_ring"):
        """A chunky metal ring round the neck, its top at ``top`` on the neck's axis and ``radius`` across its
        inside, running down onto the shoulders, with a groove round it and a flange at the foot: its cross-section
        swept round the axis (true and even all the way round, not a reduced mesh of a field); widened evenly where
        the shoulders would come through it (Fitter.push_round)."""
        s, n = self.s, np.asarray(self.axis, dtype=float)
        base = np.asarray(self.fr.j["neck"], dtype=float)
        t0 = float(np.dot(np.asarray((0, 0, self.collar_z)) - base, n)) - 0.03 * s
        top = max(top, t0 + 0.042 * s)                  # (always over the suit's edge round the neck)

        def section(rho, t):
            band = P.box(rho, t, radius + 0.012 * s, (top + t0) / 2, 0.012 * s, (top - t0) / 2, 0.006 * s)
            foot = P.box(rho, t, radius + 0.026 * s, t0 + 0.006 * s, 0.022 * s, 0.007 * s, 0.004 * s)
            d = smin(band, foot, 0.008 * s)
            return carve(d, P.box(rho, t, radius + 0.024 * s, (top + t0) / 2 + 0.004 * s, 0.0018, 0.0035, 0.0015), 0.0015)

        lo, hi = (radius - 0.006 * s, t0 - 0.01 * s), (radius + 0.06 * s, top + 0.01 * s)
        outline, normal = _outline(section, lo, hi, 0.01 * s, 0.006 * s)
        obj, normals = _revolve(outline, normal, base, n, name)
        self.fitter.push_round(obj, base, n, 0.003, most=0.02)              # widened evenly where it meets the clothes
        set_normals(obj, normals)
        _shade(obj, self.metal, axis=self.axis, normals=normals)
        set_slot(obj, "char:metal")
        self.fitter.bind(obj, stiff=1.0, keep=("neck", "chest", "clavicle", "spine"))
        return obj

    def glass(self, centre, radius, cut, name="dome", window=None):
        """A clear dome: the sphere (``centre``, ``radius``) above the plane ``cut`` below its centre on the neck's
        axis (or only the ``window`` part of it: a function of the direction from the centre, > 0 inside)."""
        n = Vector(self.axis)
        bottom = math.pi - math.acos(max(-1.0, min(1.0, cut / radius)))
        bm = bmesh.new()
        if window is None:
            e1 = Vector((1, 0, 0))
            _cap(bm, Vector(centre), radius, n, e1, n.cross(e1), lambda d: d.dot(n) - math.cos(bottom), 36)
        else:                                                    # (round the window's deepest point)
            dirs = [Vector((math.sin(t) * math.cos(f), math.sin(t) * math.sin(f), math.cos(t)))
                    for t in np.linspace(0.0, math.pi, 73) for f in np.linspace(0.0, 2 * math.pi, 144, endpoint=False)]
            c = max(dirs, key=lambda d: window(d) if d.dot(n) > math.cos(bottom) else -1e9)
            u = n.cross(c).normalized() if abs(n.dot(c)) < 0.99 else Vector((1, 0, 0))
            _cap(bm, Vector(centre), radius, c, u, c.cross(u), lambda d: min(window(d), d.dot(n) - math.cos(bottom)), 12)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        layer = bm.loops.layers.color.new("Color")
        tint = hex3(self.p.get("glass", "#b8e0e8"))
        for f in bm.faces:
            for loop in f.loops:
                loop[layer] = (*tint, 1.0)
        obj = mesh_from(bm, name)
        set_slot(obj, "char:glass")
        rigid(obj, "head")
        return obj


def _cap(bm, centre, radius, c, u, v, inside, rows, segs=64):
    """The part of the sphere (``centre``, ``radius``) where ``inside(direction)`` > 0, round the direction ``c``
    (``u``, ``v`` across it; the part star-shaped round it): rings out to the edge, found along each spoke, so the
    edge is a smooth curve (not the steps of a grid cut short)."""
    spokes = []
    for k in range(segs):
        w = u * math.cos(2 * math.pi * k / segs) + v * math.sin(2 * math.pi * k / segs)
        a, b = 0.0, math.pi
        for _ in range(30):
            m = (a + b) / 2
            a, b = (m, b) if inside(c * math.cos(m) + w * math.sin(m)) > 0 else (a, m)
        spokes.append((w, a))
    rings = [[bm.verts.new(centre + c * radius)]] + [
        [bm.verts.new(centre + (c * math.cos(a * i / rows) + w * math.sin(a * i / rows)) * radius) for w, a in spokes]
        for i in range(1, rows + 1)]
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            quad = (b[k], b[(k + 1) % segs], a[(k + 1) % len(a)], a[k % len(a)])
            bm.faces.new(quad if len(a) > 1 else quad[:3])


def _outline(section, lo, hi, bend, step, h=0.0002):
    """A cross-section ``section(rho, t)`` (a distance; one piece inside the box ``lo``..``hi``) as a closed outline:
    traced on a grid ``h`` fine (marching squares), then points ``step`` apart in length plus ``bend`` per radian
    turned (more of them round its rounded corners and groove than along its flat faces), each set exactly on the
    edge, with the outward normals there."""
    rs, ts = np.arange(lo[0], hi[0] + h, h), np.arange(lo[1], hi[1] + h, h)
    D = section(np.repeat(rs, len(ts)), np.tile(ts, len(rs))).reshape(len(rs), len(ts))
    neg = D < 0
    corner = ((0, 0), (1, 0), (1, 1), (0, 1))
    links = {}
    corner0 = neg[:-1, :-1]
    for i, j in np.argwhere((corner0 != neg[1:, :-1]) | (corner0 != neg[:-1, 1:]) | (corner0 != neg[1:, 1:])):
        cs = [(i + di, j + dj) for di, dj in corner]
        sign = [bool(neg[c]) for c in cs]
        edges = [tuple(sorted((cs[k], cs[(k + 1) % 4]))) for k in range(4)]
        cut = [k for k in range(4) if sign[k] != sign[(k + 1) % 4]]
        if len(cut) == 2:
            pairs = [(edges[cut[0]], edges[cut[1]])]
        else:                                           # a saddle: cut off the corners unlike the middle
            mid = bool(section(np.asarray([rs[i] + h / 2]), np.asarray([ts[j] + h / 2]))[0] < 0)
            pairs = [(edges[(k - 1) % 4], edges[k]) for k in range(4) if sign[k] != mid]
        for e0, e1 in pairs:
            links.setdefault(e0, []).append(e1)
            links.setdefault(e1, []).append(e0)
    start = min(links)
    loop, prev, cur = [start], None, start
    while True:
        nxt = [e for e in links[cur] if e != prev][0]
        if nxt == start:
            break
        loop.append(nxt)
        prev, cur = cur, nxt
    a, b = np.asarray([e[0] for e in loop]), np.asarray([e[1] for e in loop])
    da, db = D[a[:, 0], a[:, 1]], D[b[:, 0], b[:, 1]]
    f = (da / (da - db))[:, None]
    pa, pb = np.stack([rs[a[:, 0]], ts[a[:, 1]]], 1), np.stack([rs[b[:, 0]], ts[b[:, 1]]], 1)
    pts = pa + (pb - pa) * f
    if np.sum(pts[:, 0] * np.roll(pts[:, 1], -1) - np.roll(pts[:, 0], -1) * pts[:, 1]) < 0:
        pts = pts[::-1]                                 # (always the same way round)
    e = 1e-5

    def grad(q):
        g = np.stack([section(q[:, 0] + e, q[:, 1]) - section(q[:, 0] - e, q[:, 1]),
                      section(q[:, 0], q[:, 1] + e) - section(q[:, 0], q[:, 1] - e)], 1) / (2 * e)
        return g / np.maximum(np.linalg.norm(g, axis=1), 1e-12)[:, None]

    def onto(q):
        for _ in range(3):
            q = q - grad(q) * section(q[:, 0], q[:, 1])[:, None]
        return q
    pts = onto(pts)
    g = grad(pts)
    seg = np.roll(pts, -1, 0) - pts
    ang = np.arctan2(g[:, 1], g[:, 0])
    turn = np.abs((np.roll(ang, -1) - ang + math.pi) % (2 * math.pi) - math.pi)
    w = np.linalg.norm(seg, axis=1) + bend * turn
    cum = np.concatenate([[0.0], np.cumsum(w)])
    k = max(16, int(round(cum[-1] / step)))
    u = np.arange(k) * (cum[-1] / k)
    i = np.searchsorted(cum, u, side="right") - 1
    out = onto(pts[i] + seg[i] * ((u - cum[i]) / np.maximum(w[i], 1e-12))[:, None])
    return out, grad(out)


def _revolve(outline, normal, base, axis, name, segs=64, closed=True):
    """Sweeps a cross-section (``outline``: rho, t; with its ``normal``) round ``axis`` through ``base``: a closed
    section makes a tube of even quads; an open one (``closed`` False) runs from the axis back to it, its two ends
    closing to points there. Returns the object and its exact vertex normals."""
    n = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
    e1 = np.cross(n, (0.0, 1.0, 0.0))
    e1 /= np.linalg.norm(e1)
    ph = np.linspace(0.0, 2 * math.pi, segs, endpoint=False)
    out = np.cos(ph)[:, None] * e1 + np.sin(ph)[:, None] * np.cross(n, e1)
    k = len(outline)
    bm, rows, nr = bmesh.new(), [], []
    for b in range(k):
        rho, t = outline[b]
        if not closed and b in (0, k - 1):          # a pole on the axis
            rows.append([bm.verts.new(tuple(base + t * n))] * segs)
            nr.append(n * (1.0 if normal[b, 1] > 0 else -1.0))
            continue
        rows.append([bm.verts.new(tuple(base + t * n + rho * o)) for o in out])
        nr.extend(normal[b, 1] * n + normal[b, 0] * o for o in out)
    for b in range(k if closed else k - 1):
        for a in range(segs):
            quad = (rows[b][a], rows[b][(a + 1) % segs], rows[(b + 1) % k][(a + 1) % segs], rows[(b + 1) % k][a])
            bm.faces.new(tuple(dict.fromkeys(quad)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return mesh_from(bm, name), np.asarray(nr)


def _shade(obj, base, axis=None, normals=None):
    """Metal colour, a little lighter facing up (by ``normals``, the field's, or else the mesh's own)."""
    me = obj.data
    layer = _ensure_layer(me)
    n = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", n)
    n = n.reshape(-1, 3) if normals is None else np.asarray(normals, dtype=np.float32)
    up = np.asarray(axis if axis is not None else (0, 0, 1), dtype=np.float32)
    k = 0.9 + 0.12 * np.clip(n @ up, -1, 1)
    tone = [tuple(min(1.0, c * k[i]) for c in base) for i in range(len(k))]
    for loop in me.loops:
        layer.data[loop.index].color_srgb = (*tone[loop.vertex_index], 1.0)


def bubble(shop):
    """The Martian's glass fishbowl on its neck ring."""
    head, s = shop.head, shop.s
    centre = head.c + Vector(shop.axis) * 0.012 * head.h + Vector((0, 0.008 * head.h, 0))
    neck_top = shop.collar_z + 0.02 * s
    radius = shop.reach(centre, neck_top) + 0.022 * s
    inner = 0.088 * s
    cut = math.sqrt(max(radius * radius - inner * inner, 0.0))
    top = float(np.dot(np.asarray(centre - Vector(shop.axis) * cut - shop.fr.j["neck"]), np.asarray(shop.axis)))
    ring = shop.ring(inner - 0.004 * s, top + 0.006 * s)
    dome = shop.glass(centre, radius, cut - 0.008 * s)
    return [ring, dome]


def collar(fr, head, p, fitter, garments, head_obj):
    """A metal collar ring round the foot of the neck, over the suit's edge (the cyborg's)."""
    shop = Shop(fr, head, p, fitter, garments)
    shop.head_obj = head_obj
    z = shop.collar_z
    top = float(np.dot(np.asarray((0, 0, z + 0.016 * shop.s)) - np.asarray(fr.j["neck"]), np.asarray(shop.axis)))
    return [shop.ring(shop.neck_radius(z) + 0.002 * shop.s, top, name="collar")]


def build(kind, fr, head, p, fitter, garments, head_obj):
    shop = Shop(fr, head, p, fitter, garments)
    shop.head_obj = head_obj
    if kind == "bubble":
        return bubble(shop)
    if kind == "diver":
        from . import diverhelm
        return diverhelm.build(shop)
    if kind == "solar":
        from . import solarhelm
        return solarhelm.build(shop)
    return []
