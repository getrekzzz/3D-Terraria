"""Hair of snakes (Medusa's: the ``snakes`` parameter, their colour, over a ``buzz`` crop of the hair colour): a score
of serpents rooted over the scalp, those on the crown rising and curling out, the rest writhing out and down in an S,
the lowest hanging to the shoulders, each lifting its head at the end to look outward. A snake is a tapering body swept along its curve, banded darker over the back and pale on the belly, with
a wedge-shaped head, two beady eyes and a forked tongue. All of it is carried by the head.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .features import paint, spline, sweep
from .garments import mesh_from, set_slot
from .monsterparts import uv_sphere
from .paint import hex3
from .rig import rigid

# Each snake: the (azimuth from the front toward the character's right, elevation) of its root on the vault (degrees),
# how far it falls (negative: rises) and how far it reaches out from the head before lifting its own head (head-local).
ROOTS = [(-30, 72, -0.02, 0.05), (30, 72, -0.02, 0.05), (-75, 60, 0.0, 0.06), (75, 60, 0.0, 0.06), (180, 66, -0.01, 0.055),
         (0, 58, -0.015, 0.04),
         (-52, 36, 0.05, 0.065), (52, 36, 0.05, 0.065), (-100, 38, 0.06, 0.07), (100, 38, 0.06, 0.07),
         (-140, 42, 0.07, 0.065), (140, 42, 0.07, 0.065), (-165, 30, 0.09, 0.06), (165, 30, 0.09, 0.06),
         (-112, 12, 0.13, 0.05), (112, 12, 0.13, 0.05), (-140, 8, 0.16, 0.05), (140, 8, 0.16, 0.05),
         (-168, 6, 0.17, 0.045), (168, 6, 0.17, 0.045), (-82, 16, 0.1, 0.055), (82, 16, 0.1, 0.055)]


def _surface(shape, direction):
    """Where a ray from the skull's centre along ``direction`` leaves the bare skull (head-local)."""
    c = np.asarray((0.0, -0.012, 0.03))
    a = c[None]
    b = (c + np.asarray(direction) * 0.2)[None]
    lo, hi = 0.0, 1.0
    for _ in range(24):
        mid = (lo + hi) / 2
        q = (a + (b - a) * mid).astype(np.float32)
        if shape.field(q, ears=False, face=False)[0] < 0:
            lo = mid
        else:
            hi = mid
    return Vector((a + (b - a) * lo)[0])


def build(head, color):
    """The snakes as one object (head-local placement, metres out)."""
    base = hex3(color)
    dark = tuple(c * 0.45 for c in base)
    belly = tuple(c + (1 - c) * 0.5 for c in base)
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    h = head.h
    up = Vector((0, 0, 1))
    for k, (az, el, drop, reach) in enumerate(ROOTS):
        a, e = math.radians(az), math.radians(el)
        n = Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))
        root = _surface(head.shape, n)
        out = Vector((math.sin(a), math.cos(a) * 0.7, 0)).normalized()
        side = up.cross(out).normalized()
        wig = 0.016 * (1 if k % 2 else -1) * (0.8 + 0.4 * math.sin(k * 1.9))
        rise = 0.026 + 0.01 * math.sin(k * 2.3)
        pts = [root - n * 0.012, root + n * 0.004, root + n * rise]
        for i in range(3):                       # writhing out and down in an S
            pts.append(pts[-1] + out * (reach / 3) - up * (drop / 3) + side * wig * (1 if i % 2 else -1) * (1.2 - 0.3 * i))
        look = (out + up * (0.6 + 0.25 * math.sin(k * 1.3)) + side * 0.3 * math.sin(k * 2.9)).normalized()
        pts.append(pts[-1] + out * 0.012 + up * 0.02)
        pts.append(pts[-1] + look * 0.022)
        pts = [head.at(q[:]) for q in pts]
        path = spline(pts, 40)
        thick = 0.0098 * (0.9 + 0.2 * math.sin(k * 3.7))

        def section(t, ang, thick=thick):
            r = h * thick * (1.0 - 0.35 * t ** 1.5)
            return math.cos(ang) * r, math.sin(ang) * r

        verts = sweep(bm, path, section, 10, up=side)

        def tone(t, ang, k=k):
            band = 1.0 if math.sin(t * 42 + k) > 0.35 else 0.0
            back = max(0.0, math.cos(ang)) ** 0.7
            c = tuple(x + (d - x) * band * back * 0.75 for x, d in zip(base, dark))
            pale = max(0.0, -math.cos(ang)) ** 1.5
            return tuple(x + (b - x) * pale * 0.8 for x, b in zip(c, belly))
        paint(bm, verts, tone)
        _head(bm, layer, path[-1], (path[-1] - path[-4]).normalized(), h, thick, base, dark, k)
    obj = mesh_from(bm, "hair_snakes")
    set_slot(obj, "char:skin")
    rigid(obj, "head")
    return obj


def _head(bm, layer, neck, d, h, thick, base, dark, k):
    """A snake's head carried forward from its neck: a wedge (broad behind the eyes, blunt at the snout), two eyes,
    a forked tongue."""
    up = Vector((0, 0, 1))
    side = d.cross(up)
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    top = side.cross(d).normalized()
    if top.z < 0:
        top = -top
    L, W, H = 0.03 * h, 0.0095 * h * thick / 0.0068, 0.0066 * h * thick / 0.0068
    centre = neck + d * L * 0.35
    verts = uv_sphere(bm, 14, 9)
    for v in verts:
        x, y, z = v.co
        fwd = y                                     # -1 at the neck .. 1 at the snout
        w = W * (1.0 - 0.38 * max(0.0, fwd) ** 1.6) * (0.75 + 0.25 * min(1.0, fwd + 1.2))
        hh = H * (1.0 - 0.35 * max(0.0, fwd) ** 1.3) * (0.85 if z < 0 else 1.0)
        v.co = centre + d * (L * 0.5 * fwd) + side * (x * w) + top * (z * hh)
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        for loop in f.loops:
            q = loop.vert.co - centre
            c = dark if q.dot(top) > 0.4 * H and math.sin(k + q.dot(d) * 900) > -0.2 else base
            loop[layer] = (*c, 1.0)
    for sx in (1, -1):
        eye = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.0021 * h)
        p = centre + d * (L * 0.18) + side * (sx * W * 0.78) + top * (H * 0.42)
        bmesh.ops.translate(bm, vec=p, verts=eye["verts"])
        for f in {f for v in eye["verts"] for f in v.link_faces}:
            for loop in f.loops:
                loop[layer] = (0.95, 0.78, 0.1, 1.0)
    # The forked tongue, flicking out of the snout and down a little.
    tip = centre + d * (L * 0.5)
    for sx in (1, -1):
        prong = [tip - d * 0.002 * h, tip + d * 0.008 * h - top * 0.0015 * h,
                 tip + d * 0.014 * h + side * sx * 0.0028 * h - top * 0.0024 * h]
        vs = sweep(bm, prong, lambda t, a: (math.cos(a) * 0.0007 * h * (1 - 0.6 * t), math.sin(a) * 0.0007 * h * (1 - 0.6 * t)), 5)
        paint(bm, vs, lambda t, a: (0.75, 0.12, 0.16))
