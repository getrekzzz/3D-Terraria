"""Fitting wearable armour to the player's body (P3-41). The player model (assets/models/creatures/player.glb) is
imported once; its rest-pose vertices, skin weights and material slots tell how wide the body is around each bone.
Armour shells are built in a bone's local space (+Y along the bone, X and Z across it, exactly as the exported
skeleton's joints), so the game attaches a piece to its bone with an identity transform and scales it by bone length.

Shells are rings of radii sampled from the body: for a slice across the bone, the farthest body vertex in each angular
sector, plus a clearance, smoothed. Decorations (ridges, rivets, plates, horns) are placed on the same rings.
"""
import math
import pathlib

import bpy
import numpy as np
from mathutils import Matrix, Vector

# Blender (x, y, z) -> glTF (x, z, -y): the inverse puts bone-local coordinates into Blender space so the exporter's
# Y-up conversion writes them back unchanged.
TO_BLENDER = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


class BodyFit:
    """The player's body around each bone (rest pose), from the shipped player model and its skeleton."""

    def __init__(self, repo: pathlib.Path):
        from . import human
        from .human import frame as frame_mod
        from .mesh import reset_scene
        reset_scene()
        bpy.ops.import_scene.gltf(filepath=str(repo / "assets" / "models" / "creatures" / "player.glb"))
        mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH" and o.vertex_groups)
        n = len(mesh.data.vertices)
        co = np.empty(n * 3, np.float32)
        mesh.data.vertices.foreach_get("co", co)
        self.co = co.reshape(n, 3) @ np.array(mesh.matrix_world.to_3x3()).T + np.array(mesh.matrix_world.translation)
        names = [g.name for g in mesh.vertex_groups]
        self.weights = np.zeros((n, len(names)), np.float32)
        for v in mesh.data.vertices:
            for g in v.groups:
                self.weights[v.index, g.group] = g.weight
        self.group = {name: i for i, name in enumerate(names)}
        slot = np.zeros(n, np.int32)
        for poly in mesh.data.polygons:
            slot[list(poly.vertices)] = poly.material_index
        self.slot = np.array([mesh.data.materials[i].name for i in range(len(mesh.data.materials))])[slot]
        # The skeleton exactly as exported (bone axes as the game sees them), from the player's own parameters.
        frame = frame_mod.Frame(human.motion_params({"body": "male", "age": "young", "hairStyle": "short"}))
        arm = frame_mod.build_armature(frame, "fit")
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.length = {b.name: b.length for b in arm.data.bones}
        self.frame = frame
        for obj in list(bpy.context.scene.objects):
            bpy.data.objects.remove(obj)

    def local(self, bones, bone, min_weight=0.35, skip=("char:hair", "char:eye")):
        """Body vertices weighted to ``bones`` (summed weight >= min_weight), in ``bone``'s local space (N x 3)."""
        w = sum(self.weights[:, self.group[b]] for b in bones if b in self.group)
        keep = (w >= min_weight) & ~np.isin(self.slot, skip)
        inv = np.array(self.rest[bone].inverted())
        pts = self.co[keep]
        return pts @ inv[:3, :3].T + inv[:3, 3]


class Shell:
    """A conforming tube around a bone: ``rings`` slices from t0 to t1 (fractions of the bone length), ``segments``
    sectors each. ``radii[i][k]`` is the body radius plus clearance; points come from :meth:`point`."""

    def __init__(self, cloud, length, t0, t1, rings=8, segments=16, clearance=0.02, min_radius=0.03, arc=None,
                 smooth=2, profile=None):
        self.length, self.t0, self.t1, self.rings, self.segments = length, t0, t1, rings, segments
        self.arc = arc or (0.0, 2 * math.pi)
        self.closed = arc is None
        ys = np.array([(t0 + (t1 - t0) * i / (rings - 1)) * length for i in range(rings)])
        slab = max(abs(t1 - t0) * length / rings * 0.9, 0.015)
        ang = np.arctan2(cloud[:, 2], cloud[:, 0]) % (2 * math.pi)
        rad = np.hypot(cloud[:, 0], cloud[:, 2])
        sector = np.minimum((ang / (2 * math.pi) * 32).astype(int), 31)  # fine sectors, resampled below
        table = np.full((rings, 32), np.nan)
        for i, y in enumerate(ys):
            near = np.abs(cloud[:, 1] - y) < slab
            for s in range(32):
                sel = near & (sector == s)
                if sel.any():
                    table[i, s] = rad[sel].max()
        # Fill gaps (slices beyond the body, e.g. the top of a helmet): from neighbouring sectors, then rings.
        for i in range(rings):
            row = table[i]
            if np.isnan(row).all():
                continue
            good = ~np.isnan(row)
            idx = np.arange(32)
            table[i] = np.interp(idx, idx[good], row[good], period=32)
        for s in range(32):
            col = table[:, s]
            good = ~np.isnan(col)
            table[:, s] = np.interp(np.arange(rings), np.arange(rings)[good], col[good]) if good.any() else min_radius
        for _ in range(smooth):
            table = (table + np.roll(table, 1, 1) + np.roll(table, -1, 1)) / 3
            table[1:-1] = (table[1:-1] * 2 + table[:-2] + table[2:]) / 4
        table = np.maximum(table, min_radius) + clearance
        if profile is not None:  # scale per ring: domes close toward the top, cuffs flare
            table *= np.array([profile((i / (rings - 1))) for i in range(rings)])[:, None]
        self.ys = ys
        self._table = table

    def angle(self, k):
        a0, a1 = self.arc
        n = self.segments if self.closed else self.segments - 1
        return a0 + (a1 - a0) * k / n

    def radius(self, i, a):
        f = (a % (2 * math.pi)) / (2 * math.pi) * 32
        s0 = int(f) % 32
        s1 = (s0 + 1) % 32
        return self._table[i, s0] * (1 - (f - int(f))) + self._table[i, s1] * (f - int(f))

    def point(self, i, a, lift=0.0):
        """The shell surface at ring ``i`` (may be fractional) and angle ``a``, pushed out by ``lift``."""
        i0 = max(0, min(int(i), self.rings - 2))
        f = i - i0
        r = self.radius(i0, a) * (1 - f) + self.radius(i0 + 1, a) * f + lift
        y = self.ys[i0] * (1 - f) + self.ys[i0 + 1] * f
        return Vector((math.cos(a) * r, y, math.sin(a) * r))

    def build(self, mb, material, color, ring_colors=None, cap=False, rim=None):
        """Adds the shell to ``mb`` (bone-local coordinates). ``ring_colors(i, k)`` colours per vertex (mail rows,
        bands); ``cap`` closes the t1 end with a pole; ``rim`` (colour) adds a rolled lip at both ends."""
        from .mesh import hex_color
        bm = mb.bm
        n = self.segments
        rings = [[bm.verts.new(self.point(i, self.angle(k))) for k in range(n)] for i in range(self.rings)]
        faces = []
        for a, b in zip(rings, rings[1:]):
            for k in range(n if self.closed else n - 1):
                j = (k + 1) % n
                faces.append(bm.faces.new((a[k], b[k], b[j], a[j])))
        verts = [v for r in rings for v in r]
        if cap:
            pole = bm.verts.new(Vector((0, self.ys[-1] + (self.ys[-1] - self.ys[-2]) * 0.6, 0)))
            for k in range(n if self.closed else n - 1):
                faces.append(bm.faces.new((rings[-1][k], pole, rings[-1][(k + 1) % n])))
            verts.append(pole)
        mb._finish_part(verts, material, color, True)
        if ring_colors is not None:
            for i, ring in enumerate(rings):
                for k, v in enumerate(ring):
                    rgba = hex_color(ring_colors(i, k))
                    for loop in v.link_loops:
                        loop[mb.color_layer] = rgba
        if rim is not None:
            for i in (0, self.rings - 1) if not cap else (0,):
                pts = [tuple(self.point(i, self.angle(k), 0.006)) for k in range(n)]
                if self.closed:
                    pts.append(pts[0])
                mb.tube(pts, [0.008] * len(pts), material, rim, segments=3)
        return rings


def export_piece(obj):
    """Turns a piece built in bone-local coordinates into the form the Y-up exporter writes back unchanged."""
    obj.data.transform(TO_BLENDER)


def to_body(obj, rest: Matrix):
    """Moves a bone-local piece onto the rest-pose body (armature space), for icons."""
    obj.data.transform(rest)
