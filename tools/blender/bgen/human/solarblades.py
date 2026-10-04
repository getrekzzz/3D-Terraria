"""A Selenian's flame blades (armor ``solar``, helmet ``solar``): each blade curls up from its root like a tongue of
fire, widest a third of the way up and running to a point, its belly bulging to the outside of the curl, thin at the
edges with a ridge down the middle; it is coloured from the armour's dark red (``trim``) at the root through its
orange (``armorColor``) to a hot yellow tip, and a glowing flame (``glow``) is inlaid along its middle. Blades are
bound like the piece they grow from (the nearest vertex of their host's mesh)."""
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

from .features import spline, sweep
from .garments import mesh_from, set_slot

SEGS = 10


def _mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def _ramp(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def _width(t):
    """The blade's width along it (1 at its widest), from a broad root to a sharp point."""
    return math.sin(math.pi * (0.24 + 0.76 * t)) ** 0.85


class Forge:
    """Collects blades (and their inlaid flames) for one character, then makes them into two objects."""

    def __init__(self, root, body, tip, glow):
        self.colors = (root, body, tip)
        self.glow = glow
        self.parts = {}
        for kind in ("blades", "blade_glow"):
            bm = bmesh.new()
            self.parts[kind] = (bm, bm.loops.layers.color.new("Color"), [])
        self.trees = {}

    def surface(self, host, origin, direction, reach=0.4):
        """The point (and normal) where the host's outer surface lies from ``origin`` along ``direction`` (found by
        a ray coming in from outside), or (None, None)."""
        if host.name not in self.trees:
            self.trees[host.name] = BVHTree.FromObject(host, bpy.context.evaluated_depsgraph_get())
        d = Vector(direction).normalized()
        loc, n, _, _ = self.trees[host.name].ray_cast(Vector(origin) + d * reach, -d, reach * 2)
        return loc, n

    def blade(self, host, root, out, bend, length, width, curl=0.9, thick=0.006, sink=0.008):
        """A blade from ``root`` (on the host's face, sunk ``sink`` into it) heading ``out`` and curling toward
        ``bend`` through ``curl`` radians over its ``length``; its width lies in the plane of the curl."""
        if root is None:
            return
        out = Vector(out).normalized()
        bend = Vector(bend)
        bend = (bend - out * bend.dot(out)).normalized()
        pts, p, n = [], Vector(root) - out * sink, 10
        step = (length + sink) / n
        for i in range(n + 1):
            pts.append(p.copy())
            a = curl * (i / n) ** 1.5
            p = p + (out * math.cos(a) + bend * math.sin(a)) * step
        path = spline(pts, 16)
        side = out.cross(bend)
        start = sink / (length + sink)

        def metal(t, a):
            w = width * _width(t)
            return math.sin(a) * (thick * (1 - 0.75 * t) + 0.0008) / 2, w / 2 * (math.cos(a) + 0.25)

        def flame(t, a):                    # (t runs along the inlay, from the root's face to near the tip)
            w = width * 0.42 * math.sin(math.pi * t) ** 0.7 * (1 - 0.3 * t)
            full = start + t * (0.92 - start)
            return math.sin(a) * (thick * (1 - 0.75 * full) + 0.0016) / 2, w / 2 * (math.cos(a) + 0.4)

        self._add("blades", host, path, metal, side, self._metal_color)
        glow_path = path[int(len(path) * start):int(len(path) * 0.92)]
        if len(glow_path) > 3:
            self._add("blade_glow", host, glow_path, flame, side, lambda t: self.glow)

    def _metal_color(self, t):
        root, body, tip = self.colors
        return _mix(_mix(root, body, _ramp(0.05, 0.4, t)), tip, _ramp(0.55, 1.0, t))

    def _add(self, kind, host, path, section, side, color):
        bm, layer, owner = self.parts[kind]
        for v, t, _ in sweep(bm, path, section, SEGS, up=side):
            owner.append((v, host))
            c = color(t)
            for loop in v.link_loops:
                loop[layer] = (*c, 1.0)

    def finish(self):
        """The blades (metal) and their flames (glow), each vertex bound like its host's nearest vertex."""
        from .rig import read_weights, write_weights
        out = []
        for kind, slot in (("blades", "char:metal"), ("blade_glow", "char:glow")):
            bm, layer, owner = self.parts[kind]
            if not owner:
                bm.free()
                continue
            bm.verts.index_update()
            hosts = {v.index: host for v, host in owner}
            obj = mesh_from(bm, kind)
            set_slot(obj, slot)
            weights = []
            for v in obj.data.vertices:
                host = hosts[v.index]
                kd, w = self._kd(host, read_weights)
                weights.append(dict(w[kd.find(v.co)[1]]))
            write_weights(obj, weights)
            out.append(obj)
        return out

    def _kd(self, host, read_weights):
        key = "kd:" + host.name
        if key not in self.trees:
            kd = KDTree(len(host.data.vertices))
            for i, v in enumerate(host.data.vertices):
                kd.insert(v.co, i)
            kd.balance()
            self.trees[key] = (kd, read_weights(host))
        return self.trees[key]
