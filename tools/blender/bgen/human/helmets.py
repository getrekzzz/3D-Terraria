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
        inside, running down onto the shoulders, with a groove round it and a flange at the foot."""
        s, n = self.s, np.asarray(self.axis, dtype=F)
        base = np.asarray(self.fr.j["neck"], dtype=F)
        t0 = float(np.dot(np.asarray((0, 0, self.collar_z)) - base, n)) - 0.03 * s
        top = max(top, t0 + 0.042 * s)                  # (always over the suit's edge round the neck)

        def cyl(p):
            q = p - base
            t = q @ n
            rho = np.linalg.norm(q - t[:, None] * n, axis=1)
            return rho, t

        def field(p):
            rho, t = cyl(p)
            band = P.box(rho, t, radius + 0.012 * s, (top + t0) / 2, 0.012 * s, (top - t0) / 2, 0.006 * s)
            foot = P.box(rho, t, radius + 0.026 * s, t0 + 0.006 * s, 0.022 * s, 0.007 * s, 0.004 * s)
            d = smin(band, foot, 0.008 * s)
            lip = P.box(rho, t, radius + 0.008 * s, top - 0.002 * s, 0.009 * s, 0.004 * s, 0.002 * s)
            d = smin(d, lip, 0.002 * s)
            return carve(d, P.box(rho, t, radius + 0.024 * s, (top + t0) / 2 + 0.004 * s, 0.0015, 0.0016, 0.0005), 0.0008)

        r = radius + 0.06 * s
        lo = base + n * t0 - r
        hi = base + n * top + r
        bounds = (tuple(np.minimum(lo, hi) - 0.01), tuple(np.maximum(lo, hi) + 0.01))
        obj = P.mesh(field, bounds, name, (self.key, name, round(radius, 4), round(top, 4)), SOURCES, h=0.0011, target=3500)
        self.fitter.push(obj, lambda p: cyl(p)[0] - F(radius), 0.003, reach=0.07, most=0.02)   # out from the axis
        _shade(obj, self.metal, axis=self.axis)
        set_slot(obj, "char:metal")
        self.fitter.bind(obj, stiff=1.0, keep=("neck", "chest", "clavicle", "spine"))
        return obj

    def glass(self, centre, radius, cut, name="dome", window=None):
        """A clear dome: the sphere (``centre``, ``radius``) above the plane ``cut`` below its centre on the neck's
        axis (or only the ``window`` part of it: a function of the direction from the centre, > 0 inside)."""
        n = Vector(self.axis)
        e1 = Vector((1, 0, 0))
        e2 = n.cross(e1)
        bottom = math.pi - math.acos(max(-1.0, min(1.0, cut / radius)))
        bm = bmesh.new()
        rows, segs = 36, 64
        grid = []
        for i in range(rows + 1):
            th = bottom * i / rows
            ring = []
            for k in range(segs if i else 1):
                ph = 2 * math.pi * k / segs
                d = n * math.cos(th) + (e1 * math.cos(ph) + e2 * math.sin(ph)) * math.sin(th)
                ring.append((bm.verts.new(Vector(centre) + d * radius), d))
            grid.append(ring)
        for a, b in zip(grid, grid[1:]):
            for k in range(segs):
                quad = [b[k], b[(k + 1) % segs], a[(k + 1) % len(a)], a[k % len(a)]]
                if window is not None and min(window(q[1]) for q in quad) <= 0:
                    continue
                verts = [q[0] for q in quad]
                bm.faces.new(verts if len(set(verts)) == 4 else verts[:3])
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
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


def _shade(obj, base, axis=None, glow=None, glow_test=None):
    """Metal colour, a little lighter facing up; ``glow_test(co, normal)`` picks vertices painted ``glow``."""
    me = obj.data
    layer = _ensure_layer(me)
    n = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", n)
    n = n.reshape(-1, 3)
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
