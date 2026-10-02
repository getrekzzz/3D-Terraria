"""Building blocks for the hair style catalogue (hair_styles.py), made with the same techniques as hair.py:

- ``shell``: the scalp duplicated and lifted along its normals by a per-style thickness field, feathered at a
  (style-shifted) hairline, optionally masked to a region (crests, undercut tops) and swept sideways;
- ``Scalp``: ray queries against the head mesh, so hanging hair (curtains, locks, braids) falls straight down
  from the widest point above it and never cuts into the face, ears or neck;
- ``Strands``: tubes along a path (locks, braids, tails, spikes) and wrapped balls (buns) in one mesh.

Every surface carries strand UVs (U along the hair, V across it) for the game's anisotropic hair shader, and a
vertex colour that is the chosen hair colour with root, clump and streak shading.
"""
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .hair import _in_ear, _mesh_from, _strand_uv, hairline
from .head import smooth
from .paint import hex3

AY = -0.012                      # head-local y of the vertical axis hanging hair is measured around
EAR = Vector((0.077, -0.002, -0.012))


def ear_fade(q, r0=0.03, r1=0.058) -> float:
    """0 at the ear, 1 well away from it: keeps lifted hair from swallowing the ears."""
    return smooth(r0, r1, (Vector((abs(q.x), q.y, q.z)) - EAR).length)


def azimuth(q) -> float:
    """0 at the face, +-pi at the back."""
    return math.atan2(q.x, q.y)


def default_shade(q):
    phi = math.atan2(q.x, -q.y)
    return 0.82 + 0.18 * math.sin(phi * 70 + q.z * 40)


def shell(head, head_obj, color, depth, line=None, keep=None, push=None, shade=default_shade, name="hair_shell"):
    """A hair volume over the scalp. ``depth(q)`` is the lift (head-local metres) at head-local point q,
    ``line(q)`` shifts the hairline (negative = lower, e.g. a fringe), ``keep(q)`` masks a region and
    ``push(q)`` adds a sideways sweep (a Vector, head-local). Both fade to nothing at the hairline and the ears."""
    hl = (lambda q: hairline(q) + line(q)) if line else hairline
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.normal_update()
    drop = []
    for f in bm.faces:
        q = head.local(f.calc_center_median())
        if q.z < hl(q) - 0.004 or _in_ear(q) or q.z < -0.13 or (keep is not None and not keep(q)):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in [v for v in bm.verts if v.is_boundary]:
        q = head.local(v.co)
        if not _in_ear(q) and q.z > -0.12 and q.z < hl(q) + 0.004:
            v.co = head.at((q.x, q.y, hl(q) - 0.004))
    bm.normal_update()
    moves = []
    for v in bm.verts:
        q = head.local(v.co)
        edge = smooth(0.0, 0.012, q.z - hl(q)) * ear_fade(q)
        off = v.normal * depth(q)
        if push:
            off = off + push(q)
        moves.append((v, off * head.h * edge))
    for v, off in moves:
        v.co += off
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    base = hex3(color)
    for f in bm.faces:
        for loop in f.loops:
            q = head.local(loop.vert.co)
            k = shade(q) * (0.75 + 0.25 * smooth(-0.02, 0.06, q.z))
            loop[col] = (base[0] * k, base[1] * k, base[2] * k, 1.0)
    _strand_uv(bm, head, uv)
    return _mesh_from(bm, name)


def clumps(q, freq=9.0, amp=0.45):
    """The scalp shell's clump field (as hair.scalp_shell), 0.1..1."""
    phi = math.atan2(q.x, -q.y)
    return 0.55 + amp * math.sin(phi * freq + q.z * 60) * math.sin(phi * 4 - q.z * 25)


class Scalp:
    """Ray queries against the head mesh, in head-local units."""

    def __init__(self, head, head_obj):
        self.head = head
        self.tree = BVHTree.FromObject(head_obj, bpy.context.evaluated_depsgraph_get())

    def radius(self, a, z) -> float:
        """Horizontal distance from the hanging axis to the head's outer surface at azimuth a, height z; ears
        (separate meshes) are kept clear by a minimum radius beside them."""
        d = Vector((math.sin(a), math.cos(a), 0.0))
        o = self.head.at((d.x * 0.4, AY + d.y * 0.4, z))
        hit, _, _, _ = self.tree.ray_cast(o, -d, 0.8 * self.head.h)
        r = 0.0
        if hit is not None:
            q = self.head.local(hit)
            r = math.hypot(q.x, q.y - AY)
        side = smooth(0.8, 0.95, abs(math.sin(a)))
        ear = side * smooth(-0.075, -0.045, z) * smooth(0.035, 0.015, z)
        return max(r, 0.1 * ear)

    def hang(self, a, z0, z1, steps, off):
        """[(r, z)] down from z0 to z1: hair hugging the head, then falling straight from its widest point."""
        out, r = [], 0.0
        for i in range(steps + 1):
            z = z0 + (z1 - z0) * i / steps
            r = max(r, self.radius(a, z) + off)
            out.append((r, z))
        return out

    def point(self, a, r, z) -> Vector:
        return self.head.at((math.sin(a) * r, AY + math.cos(a) * r, z))

    def surface(self, direction):
        """(world point, world normal) where a ray toward the skull centre along head-local ``direction`` meets
        the head, or (None, None)."""
        d = Vector(direction).normalized()
        o = self.head.at(d * 0.4)
        hit, n, _, _ = self.tree.ray_cast(o, -d, 0.8 * self.head.h)
        return hit, n


def curtain(head, scalp, color, a0, a1, z0, z1, cols, rows, off, flare=0.0, wave=0.0, curl=0.0, ragged=0.0, name="hair_curtain"):
    """A sheet of hanging hair from azimuth a0 round the back to a1 (a1 > a0, both measured from the face):
    hugging the head under the shell at z0, falling to z1 with flare, waves, a tucked-under tip and a ragged
    end."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.new("Color")
    base = hex3(color)
    grid = []
    for k in range(cols + 1):
        a = a0 + (a1 - a0) * k / cols
        end = z1 + ragged * math.sin(k * 2.39) * math.sin(k * 0.71 + 1.0)
        prof = scalp.hang(a, z0, end, rows, off)
        column = []
        for i, (r, z) in enumerate(prof):
            t = i / rows
            r += flare * t * t + wave * math.sin(z * 70 + a * 3) * smooth(0.0, 0.3, t)
            r -= curl * smooth(0.6, 1.0, t)
            column.append(bm.verts.new(scalp.point(a, r, z)))
        grid.append(column)
    span = (z0 - z1) * 12
    for k in range(cols):
        for i in range(rows):
            f = bm.faces.new((grid[k][i], grid[k + 1][i], grid[k + 1][i + 1], grid[k][i + 1]))
            for loop, (kk, ii) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))):
                loop[uv].uv = (ii / rows * span, kk / cols * 6.0)
                s = (0.8 + 0.2 * math.sin(kk * 2.3 + ii * 0.4)) * (0.8 + 0.2 * smooth(0.0, 0.3, ii / rows))
                loop[col] = (base[0] * s, base[1] * s, base[2] * s, 1.0)
    return _mesh_from(bm, name)


class Strands:
    """Tubes and wrapped balls of hair collected into one mesh."""

    def __init__(self, head, color):
        self.head = head
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.col = self.bm.loops.layers.color.new("Color")
        self.base = hex3(color)
        self.n = 0

    def _paint(self, f, uvs, shades):
        for loop, u, s in zip(f.loops, uvs, shades):
            loop[self.uv].uv = u
            loop[self.col] = (self.base[0] * s, self.base[1] * s, self.base[2] * s, 1.0)

    def tube(self, pts, radii, seg=8, tint=1.0, twist=0.0):
        """A tube through world points ``pts`` with per-point radii (world); U runs along it."""
        self.n += 1
        frames, normal = [], None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if normal is None:
                normal = t.orthogonal().normalized()
            normal = (normal - t * normal.dot(t)).normalized()
            frames.append((t, normal, t.cross(normal)))
        rings, length, us = [], 0.0, []
        for i, (p, r, (t, n, b)) in enumerate(zip(pts, radii, frames)):
            if i:
                length += (p - pts[i - 1]).length
            us.append(length / self.head.h * 12)
            rot = twist * i
            rings.append([self.bm.verts.new(p + (n * math.cos(2 * math.pi * k / seg + rot) + b * math.sin(2 * math.pi * k / seg + rot)) * r)
                          for k in range(seg)])
        for i in range(len(rings) - 1):
            root = 0.75 + 0.25 * smooth(0.0, 0.25, i / len(rings))
            for k in range(seg):
                f = self.bm.faces.new((rings[i][k], rings[i][(k + 1) % seg], rings[i + 1][(k + 1) % seg], rings[i + 1][k]))
                vs = [k / seg * 1.5, (k + 1) / seg * 1.5]
                sh = [tint * root * (0.82 + 0.18 * math.sin(kk * 2.1 + self.n * 1.7)) for kk in (k, k + 1, k + 1, k)]
                self._paint(f, [(us[i], vs[0]), (us[i], vs[1]), (us[i + 1], vs[1]), (us[i + 1], vs[0])], sh)
        tip = self.bm.verts.new(pts[-1] + frames[-1][0] * radii[-1])
        for k in range(seg):
            f = self.bm.faces.new((rings[-1][k], rings[-1][(k + 1) % seg], tip))
            self._paint(f, [(us[-1], k / seg * 1.5), (us[-1], (k + 1) / seg * 1.5), (us[-1] + 0.1, (k + 0.5) / seg * 1.5)], [tint * 0.9] * 3)

    def ball(self, center, radius, axis, squash=0.8, seg=16, rings=10):
        """A bun: hair wrapped round ``axis`` (flattened along it); U runs around the wrap."""
        z = Vector(axis).normalized()
        x = z.orthogonal().normalized()
        y = z.cross(x)
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=rings, radius=1.0)
        verts = res["verts"]
        unit = {v: v.co.copy() for v in verts}
        for v in verts:
            c = unit[v]
            v.co = center + (x * c.x + y * c.y) * radius + z * c.z * radius * squash
        for f in {f for v in verts for f in v.link_faces}:
            mid = sum((unit[v] for v in f.verts), Vector()) / len(f.verts)
            am = math.atan2(mid.y, mid.x)
            uvs, shades = [], []
            for loop in f.loops:
                c = unit[loop.vert]
                a = math.atan2(c.y, c.x) if math.hypot(c.x, c.y) > 1e-4 else am
                a += 2 * math.pi * round((am - a) / (2 * math.pi))
                el = math.acos(max(-1.0, min(1.0, c.z)))
                uvs.append((a * 0.7 + el * 0.4, el * 1.5))
                shades.append(0.85 + 0.15 * math.sin(a * 5 + el * 3))
            self._paint(f, uvs, shades)

    def finish(self, name):
        return _mesh_from(self.bm, name)
