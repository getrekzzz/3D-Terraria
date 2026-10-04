"""A gingerbread man (``anatomy: cookie``): one soft baked cookie on the human rig. A pillowy slab of a body with
rounded edges, a big round disc of a head on a short neck, puffy round limbs ending in mitten hands (a thumb nub, no
fingers) and rounded stumps for feet; no anatomy. The dough is darker where it baked at the edges and mottled with
tiny pores; white icing is piped in wavy lines round the wrists, ankles and neck, three gumdrops run down the front,
and the face is a cookie's: chocolate-drop eyes ringed with icing, rosy icing cheeks and a piped smile.

Colours from ``skin`` (the dough) and ``eyes`` (the chocolate drops).
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import matter
from .matter import F
from .sdf import ellipsoid, round_cone, smax, smin

SOURCES = ("cookie.py",)
ICING = (0.98, 0.96, 0.92)
GUMDROPS = ((0.86, 0.12, 0.14), (0.2, 0.66, 0.26), (0.96, 0.74, 0.12))


def _disc(q, radius, half, round_r):
    """A puffy disc facing y (a cylinder with well rounded edges), own coordinates."""
    r = np.hypot(q[:, 0], q[:, 2]) - F(radius - round_r)
    t = np.abs(q[:, 1]) - F(half - round_r)
    return np.minimum(np.maximum(r, t), F(0)) + np.hypot(np.maximum(r, F(0)), np.maximum(t, F(0))) - F(round_r)


class Dough:
    def __init__(self, fr, head):
        from .hands import hand_axes
        self.fr, self.head, self.s = fr, head, fr.s
        self.axes = {side: [np.asarray(tuple(v)) for v in hand_axes(fr, side)] for side in ("R", "L")}

    def shape(self, p):
        j, s, head = self.fr.j, self.s, self.head
        V = lambda v: np.asarray(tuple(v), dtype=F)
        torso = matter.round_box(p - V((0, -0.004 * s, 1.18 * s)), (0.22 * s, 0.088 * s, 0.29 * s), 0.1 * s)
        torso = smin(torso, ellipsoid(p, V((0, 0.012 * s, 1.19 * s)), (0.17 * s, 0.078 * s, 0.22 * s)), 0.03 * s)   # puffed front
        q = (p - V(head.c)) / F(head.h)
        disc = _disc(q, 0.142, 0.06, 0.04) * F(head.h)
        neck = round_cone(p, V(j["neck"]) - V((0, 0, 0.05 * s)), V(head.c) - V((0, 0, 0.04 * s)), 0.11 * s, 0.095 * s)
        d = smin(smin(torso, neck, 0.04 * s), disc, 0.045 * s)
        for side in ("R", "L"):
            sh, wr = V(j[f"shoulder.{side}"]), V(j[f"wrist.{side}"])
            along, thumb, dorsal = self.axes[side]
            arm = round_cone(p, sh - V((np.sign(sh[0]) * 0.03 * s, 0, 0.012 * s)), wr, 0.084 * s, 0.068 * s)
            rot = np.stack([along, thumb, dorsal], 1)
            mitt = ellipsoid(p, V(wr) + V(along * 0.05 * s), (0.074 * s, 0.064 * s, 0.05 * s), rot)
            nub = ellipsoid(p, V(wr) + V(along * 0.03 * s + thumb * 0.054 * s), (0.04 * s, 0.03 * s, 0.032 * s), rot)
            arm = smin(arm, smin(mitt, nub, 0.012 * s), 0.025 * s)
            sx = F(np.sign(sh[0]))      # the legs stand apart (offset sideways, which their swing does not see)
            hip, an = V(j[f"hip.{side}"]) + V((sx * 0.048 * s, 0, 0)), V(j[f"ankle.{side}"]) + V((sx * 0.06 * s, 0, 0))
            leg = round_cone(p, hip + V((0, 0, 0.02 * s)), an, 0.1 * s, 0.084 * s)
            foot = ellipsoid(p, an + V((0, 0.03 * s, -0.018 * s)), (0.09 * s, 0.108 * s, 0.07 * s))
            leg = smin(leg, foot, 0.03 * s)
            d = smin(smin(d, arm, 0.03 * s), leg, 0.035 * s)
        return smax(d, F(0.002) - p[:, 2], 0.012 * s)          # flat under the feet

    def field(self, p):
        d = self.shape(p)
        bumps = F(0.0016) * (matter.fbm(p, 40.0, 2, 3) - F(0.5))
        pits = matter.hash3(np.floor(p * 260.0), 9)
        return d + bumps + np.where(pits > F(0.95), F(0.0006), F(0))

    def bounds(self):
        s = self.s
        return (-0.72 * s, -0.24 * s, -0.01), (0.72 * s, 0.26 * s, self.head.c.z + 0.16 * self.head.h)


def _paint_dough(obj, base):
    co, n = matter.coords(obj), matter.normals(obj)
    rim = 1 - np.abs(n[:, 1])
    mottle = matter.fbm(co.astype(F), 16.0, 3, 21)[:, None]
    pore = matter.hash3(np.floor(co * 260.0), 9)[:, None] > 0.96
    k = (1.06 - 0.3 * rim ** 2)[:, None] * (0.92 + 0.16 * mottle)
    rgb = base * k * np.asarray((1.0, 0.97, 0.94)) ** (1 + rim[:, None] * 2)
    rgb = np.where(pore, rgb * 0.86, rgb)
    matter.paint(obj, rgb)


def _ring(dough, centre, axis, start, count, lift, wave, waves):
    """A wavy line of piped icing round a limb (or the neck) about ``axis`` through ``centre``."""
    c, a = np.asarray(tuple(centre), dtype=float), np.asarray(tuple(axis), dtype=float)
    a /= np.linalg.norm(a)
    u = np.cross(a, (0.0, 1.0, 0.0) if abs(a[1]) < 0.9 else (1.0, 0.0, 0.0))
    u /= np.linalg.norm(u)
    v = np.cross(a, u)
    ang = np.linspace(0, 2 * math.pi, count, endpoint=False)
    dirs = np.cos(ang)[:, None] * u + np.sin(ang)[:, None] * v
    base = c + a * (wave * np.sin(ang * waves))[:, None]
    hit = matter.onto(dough.shape, base + dirs * start, -dirs, start)
    return [Vector(tuple(x)) for x in hit + dirs * lift]


def _on_front(dough, points, lift):
    """Points (x, z) projected onto the front of the dough, lifted off it along its normal."""
    pts = np.asarray([(x, 0.5, z) for x, z in points], dtype=float)
    hit = matter.onto(dough.shape, pts, np.tile((0.0, -1.0, 0.0), (len(pts), 1)), 0.9)
    n = matter.gradient(dough.shape, hit)
    return hit + n * lift, n


def _dome(bm, layer, centre, normal, radius, height, color, sink=0.35, cone=0.0):
    """A smooth dome (a gumdrop, a chocolate drop) standing on the surface along ``normal``."""
    n = Vector(tuple(normal)).normalized()
    t = n.orthogonal().normalized()
    b = n.cross(t)
    rings, segs = 7, 16
    verts = []
    for i in range(rings + 1):
        phi = (math.pi / 2) * i / rings
        r = math.cos(phi) * radius * (1 - cone * math.sin(phi))
        z = (math.sin(phi) - sink) * height
        ring = [bm.verts.new(Vector(tuple(centre)) + n * z + (t * math.cos(a) + b * math.sin(a)) * r)
                for a in (2 * math.pi * k / segs for k in range(segs))] if i < rings else [bm.verts.new(Vector(tuple(centre)) + n * (1 - sink) * height)]
        verts.append(ring)
    faces = []
    for r0, r1 in zip(verts, verts[1:]):
        for k in range(segs):
            if len(r1) == 1:
                faces.append(bm.faces.new((r0[k], r0[(k + 1) % segs], r1[0])))
            else:
                faces.append(bm.faces.new((r0[k], r0[(k + 1) % segs], r1[(k + 1) % segs], r1[k])))
    bottom = bm.faces.new(verts[0][::-1])
    for f in faces + [bottom]:
        for loop in f.loops:
            hi = max(0.0, (loop.vert.co - Vector(tuple(centre))).dot(n)) / max(height, 1e-6)
            k = 0.9 + 0.22 * hi
            loop[layer] = (min(1, color[0] * k), min(1, color[1] * k), min(1, color[2] * k), 1.0)


def _paint_bm(bm, layer, color, verts=None):
    for f in bm.faces:
        for loop in f.loops:
            if verts is None or loop.vert in verts:
                loop[layer] = (*color, 1.0)


def decorations(fr, head, dough, eye_color):
    """Icing (wrists, ankles, neck, the face), gumdrop buttons and chocolate-drop eyes: [(object, slot)]."""
    from .garments import mesh_from as _mesh_from
    j, s, h = fr.j, fr.s, head.h

    def mesh_from(bm, name):
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        return _mesh_from(bm, name)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    pipe = 0.0062 * s
    for side in ("R", "L"):
        el, wr = j[f"elbow.{side}"], j[f"wrist.{side}"]
        fw = (wr - el).normalized()
        matter.tube(bm, _ring(dough, wr - fw * 0.03 * s, fw, 0.2 * s, 72, pipe * 0.55, 0.011 * s, 5), pipe, 8, (wr, fw))
        kn, an = j[f"knee.{side}"], j[f"ankle.{side}"]
        dn = (an - kn).normalized()
        c = an - dn * 0.08 * s + Vector(((1 if side == "R" else -1) * 0.058 * s, 0, 0))
        matter.tube(bm, _ring(dough, c, dn, 0.25 * s, 84, pipe * 0.55, 0.012 * s, 6), pipe, 8, (c, dn))
    nc = (j["neck"] + head.at((0, 0, -0.12))) / 2 + Vector((0, 0, 0.004 * s))
    matter.tube(bm, _ring(dough, nc, (0, 0, 1), 0.25 * s, 96, pipe * 0.55, 0.01 * s, 9), pipe, 8, (nc, (0, 0, 1)))
    # the smile: an arc piped across the lower face, a dot at each end
    xs = np.linspace(-0.062, 0.062, 26)
    smile = [(head.c.x + x * h, head.c.z + (-0.035 - 0.032 * (1 - (x / 0.062) ** 2)) * h) for x in xs]
    pts, _ = _on_front(dough, smile, pipe * 0.5)
    matter.tube(bm, [Vector(tuple(x)) for x in pts], lambda t: pipe * (1.05 + 0.15 * math.sin(math.pi * t)), 8)
    _paint_bm(bm, layer, ICING)
    icing = mesh_from(bm, "icing")
    # eyes ringed with icing, and cheeks
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    ring_bm = bmesh.new()
    ring_layer = ring_bm.loops.layers.color.new("Color")
    eyes, n = _on_front(dough, [(head.c.x + sx * 0.048 * h, head.c.z + 0.026 * h) for sx in (1, -1)], 0.0)
    for c, nn in zip(eyes, n):
        _dome(bm, layer, c, nn, 0.021 * h, 0.016 * h, matter.hexrgb(eye_color), sink=0.3)
        t = Vector(tuple(nn)).orthogonal().normalized()
        b = Vector(tuple(nn)).cross(t)
        circle = [Vector(tuple(c)) + (t * math.cos(a) + b * math.sin(a)) * 0.029 * h + Vector(tuple(nn)) * pipe * 0.25
                  for a in np.linspace(0, 2 * math.pi, 40, endpoint=False)]
        matter.tube(ring_bm, circle, pipe * 0.8, 8, (c, nn))
    _paint_bm(ring_bm, ring_layer, ICING)
    cheeks, n = _on_front(dough, [(head.c.x + sx * 0.085 * h, head.c.z - 0.03 * h) for sx in (1, -1)], 0.0)
    for c, nn in zip(cheeks, n):
        _dome(ring_bm, ring_layer, c, nn, 0.02 * h, 0.006 * h, (0.96, 0.56, 0.6), sink=0.4)
    choc = mesh_from(bm, "eye_drops")
    icing2 = mesh_from(ring_bm, "icing_face")
    # gumdrops down the front
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    tops, n = _on_front(dough, [(0, z * s) for z in (1.34, 1.2, 1.06)], 0.0)
    for c, nn, col in zip(tops, n, GUMDROPS):
        _dome(bm, layer, c, nn, 0.04 * s, 0.048 * s, col, sink=0.25, cone=0.28)
    drops = mesh_from(bm, "gumdrops")
    return [(icing, "char:nail"), (icing2, "char:nail"), (choc, "char:eye"), (drops, "char:nail")]


def _mitten(w):
    out = {}
    for b, x in w.items():
        name = b.split(".")
        key = f"hand.{name[-1]}" if name[0] in ("thumb", "index", "middle", "ring", "pinky") else b
        out[key] = out.get(key, 0.0) + x
    return out or {"spine": 1.0}


def build(fr, head, arm, p):
    from . import rig
    s = fr.s
    dough = Dough(fr, head)
    body = matter.mesh(dough.field, dough.bounds(), 0.0028 * s, 30000, "cookie", ("cookie", matter.frame_key(fr), head.h), SOURCES)
    rig.heat_weights(body, arm, drop=["root"])
    rig.write_weights(body, [_mitten(w) for w in rig.read_weights(body)])      # the fingers' share goes to the hand
    from types import SimpleNamespace         # (puffy limbs reach further from their bones than a person's)
    rig.clean_limb_weights(body, arm, SimpleNamespace(s=s, build=1.0, weight=0.9))
    _paint_dough(body, matter.hexrgb(p.get("skin", "#b87a3a")))
    matter.slot(body, "char:skin")
    lookup = rig.body_lookup(body)
    out = [body]
    for obj, slot_name in decorations(fr, head, dough, p.get("eyes", "#1a1a1a")):
        matter.copy_weights(obj, lookup)
        matter.slot(obj, slot_name)
        out.append(obj)
    return out
