"""Skirts and sashes (a mixin of garments.Wardrobe): coat tails and robe skirts lofted from under the belt or sash,
and the cloth sash knotted round a robe's waist.

A skirt's top rows hug the garments under it (measured as the radius round the body in each direction, so nothing
stands off as a hoop) and are hidden under the belt or sash; below, it is gathered into small pleats that open into
folds deepening toward the hem, never closer than a little off the hips and legs. Its bottom is finished with a band
of trim turned up inside (torn into tongues on the undead). Skirts and the sash's hanging ends are weighted between
the hips and the thighs.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .head import smooth
from .paint import hex3
from .tailoring import ridge


def smax(a, b, k):
    return 0.5 * (a + b + np.sqrt((a - b) ** 2 + k * k))


class Skirts:
    def radial(self, objs, z, half, segs=72):
        """Radius round the body's axis at each of ``segs`` angles (0 in front): the furthest point of ``objs`` (the
        body, garments) within ``half`` of height ``z`` in that direction."""
        s = self.f.s
        co = np.concatenate([np.array([v.co[:] for v in o.data.vertices]).reshape(-1, 3) for o in objs])
        co = co[(np.abs(co[:, 2] - z) < half) & (np.abs(co[:, 0]) < 0.3 * s)]
        out = np.full(segs, -1.0)
        if not len(co):
            return np.full(segs, 0.15 * s)
        dy = co[:, 1] - self.f.j["spine"].y
        k = np.round(np.arctan2(co[:, 0], dy) / (2 * np.pi) * segs).astype(int) % segs
        np.maximum.at(out, k, np.hypot(co[:, 0], dy))
        have = np.flatnonzero(out > 0)
        out = np.interp(np.arange(segs), np.concatenate([have - segs, have, have + segs]), np.tile(out[have], 3))
        return np.maximum(out, 0.5 * (np.roll(out, 1) + np.roll(out, -1)))

    def skirt_weights(self, co, bottom_z):
        """Hips at the waist, handing over to each thigh by the top of the legs, so the cloth over a leg swings
        with it; behind the calves the shins share it, so a heel kicked back stays under the hem."""
        s, j = self.f.s, self.f.j
        t = 0.9 * smooth(self.waist - 0.03 * s, j["hip.R"].z - 0.12 * s, co.z) if bottom_z < self.waist else 0.0
        side = smooth(-0.05 * s, 0.05 * s, co.x)
        knee = j["knee.R"]
        calf = 0.85 * smooth(knee.z + 0.04 * s, knee.z - 0.16 * s, co.z) * smooth(knee.y + 0.02 * s, knee.y - 0.08 * s, co.y)
        w = {"hips": 1 - t}
        for name, k in (("R", side), ("L", 1 - side)):
            w[f"thigh.{name}"] = t * k * (1 - calf)
            w[f"shin.{name}"] = t * k * calf
        return {b: x for b, x in w.items() if x > 0.01}

    def skirt(self, body, bottom_z, flare, color, name, open_front=False, trim=None, under=()):
        """A skirt from just under the belt or sash to ``bottom_z`` (see the module's notes), with a band of ``trim``
        at the hem."""
        from .garments import mesh_from, set_slot
        from .rig import write_weights
        s, cy = self.f.s, self.f.j["spine"].y
        top = self.waist + 0.008 * s
        segs = 72
        inside = [body, *under]
        hug = self.radial(inside, top, 0.012 * s, segs) + 0.0025 * s
        trim_h = 0.045 * s
        torn = self.tatters is not None
        ts = sorted({0.0, 0.015, 0.035, 0.06, 0.09} | {i / 18 for i in range(2, 19)}
                    | ({1 - trim_h / (top - bottom_z)} if trim else set()))
        folds = 10 if bottom_z < self.f.j["knee.R"].z else 8
        a = 2 * np.pi * np.arange(segs) / segs
        bm = bmesh.new()
        grid, shade = [], {}
        for t in ts:
            z = top + (bottom_z - top) * t
            clear = self.radial(inside, z, 0.025 * s, segs) + 0.006 * s
            crest = ridge(a * folds / (2 * np.pi) + 0.18 * np.sin(a * 3 + t * 2.5) + 0.3 * t)
            pleat = ridge(a * folds * 3 / (2 * np.pi) + 0.4 * np.sin(a * 5 + 1.0))
            gather = smooth(0.0, 0.05, t) * (1 - smooth(0.12, 0.45, t))      # small pleats just under the waist
            deep = t ** 1.1
            r = smax(hug * (1 + flare * t ** 1.3), clear, 0.012 * s)
            r = r + 0.014 * s * deep * (crest - 0.4) + 0.0035 * s * gather * (pleat - 0.5)
            zz = z - (0.008 * s * (crest - 0.5) if t == 1.0 else 0.0)
            row = [bm.verts.new((math.sin(ak) * rk, cy + math.cos(ak) * rk - 0.004 * s * t, zk))
                   for ak, rk, zk in zip(a, r, np.broadcast_to(zz, a.shape))]
            for v, c, p in zip(row, crest, pleat):
                shade[v] = (1.0 - 0.28 * deep * (1.0 - c) ** 2) * (1.0 - 0.18 * gather * (1.0 - p) ** 2)
            grid.append(row)
        if not torn:           # the hem turned up inside: round the bottom edge, then back up as a lining
            bottom = grid[-1]
            for d_in, dz in ((0.003, -0.0025), (0.0065, 0.0005), (0.0065, trim_h / s)):
                row = []
                for v in bottom:
                    out = Vector((v.co.x, v.co.y - cy, 0)).normalized()
                    nv = bm.verts.new(v.co - out * d_in * s + Vector((0, 0, dz * s)))
                    shade[nv] = shade[v] * (0.85 if dz > 0 else 0.95)
                    row.append(nv)
                grid.append(row)
        gap = 1 if open_front else 0
        for i in range(len(grid) - 1):
            for k in range(gap, segs - gap):
                bm.faces.new((grid[i][k], grid[i][(k + 1) % segs], grid[i + 1][(k + 1) % segs], grid[i + 1][k]))
        bm.normal_update()
        f0 = grid[0][segs // 4].link_faces[0]
        if f0.normal.dot(Vector((1, 0, 0))) < 0:
            for f in bm.faces:
                f.normal_flip()
        layer = bm.loops.layers.color.new("Color")
        base, band = hex3(color), hex3(trim) if trim else None
        for f in bm.faces:
            c = band if band and f.calc_center_median().z < bottom_z + trim_h - 1e-4 else base
            for loop in f.loops:
                k = shade[loop.vert]
                loop[layer] = (c[0] * k, c[1] * k, c[2] * k, 1.0)
        if torn:
            self._tear_skirt(bm, bottom_z, cy)
        obj = mesh_from(bm, name)
        set_slot(obj, "char:cloth")
        write_weights(obj, [self.skirt_weights(v.co, bottom_z) for v in obj.data.vertices])
        return obj

    def _tear_skirt(self, bm, bottom_z, cy):
        """The skirt's bottom torn into tongues, darkened toward the tear, its edge turned under."""
        from . import finishing, tatters
        s = self.f.s
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.verts.ensure_lookup_table()
        co = np.array([v.co[:] for v in bm.verts])
        u = np.arctan2(co[:, 0], co[:, 1] - cy) / (2 * np.pi) + 0.5
        hem = self.tatters.skirt_hem
        layer = bm.loops.layers.color.get("Color")
        out = tatters.cut(bm, hem.cut(co[:, 2] - bottom_z, u), {"soft": hem.cut(co[:, 2] - bottom_z, u, False)})
        grime = 1.0 - tatters.ss(0.0, 0.03 * s, np.maximum(out["soft"], 0.0))
        for f in bm.faces:
            for lp in f.loops:
                c, g = lp[layer], grime[lp.vert.index]
                k = 1.0 - g ** 1.5 * (1.0 - tatters.GRIME * 0.7)
                lp[layer] = (c[0] * k[0], c[1] * k[1], c[2] * k[2], 1.0)
        deform = bm.verts.layers.deform.verify()
        bm.normal_update()
        for loop, closed in finishing.open_loops(bm):
            if sum(v.co.z for v in loop) / len(loop) < bottom_z + 0.2 * s:
                tatters.lip(bm, layer, deform, loop, closed, 0.004 * s)

    # ------------------------------------------------------------------ the sash
    def sash(self, skirt, bodice, color):
        """A cloth sash knotted round a robe's waist over the top of its skirt: a soft band with thickness, a knot in
        front a little to one side, and the two ends hanging from it down the skirt."""
        from .rig import rigid, write_weights
        s, cy = self.f.s, self.f.j["spine"].y
        z = self.waist - 0.004 * s
        segs = 72
        under = [o for o in (skirt, bodice) if o is not None]
        radius = self.radial(under, z, 0.024 * s, segs) + 0.0015 * s
        a = 2 * np.pi * np.arange(segs) / segs
        radius = radius + 0.0007 * s * np.sin(a * 9 + 0.7) * np.sin(a * 4)          # soft crumples round it
        rows = ((-0.021, -0.001, 0.72), (-0.0228, 0.0022, 0.86), (-0.0195, 0.0048, 1.0), (-0.005, 0.0054, 0.84),
                (-0.002, 0.005, 1.0), (0.013, 0.0056, 1.0), (0.0198, 0.0046, 0.9), (0.0228, 0.0018, 0.78),
                (0.021, -0.001, 0.7), (-0.021, -0.001, 0.7))
        band = self._strap(rows, list(zip(a, radius)), z, color, "sash", True, slot="char:cloth")
        out = [band]
        ak = 0.3
        d = Vector((math.sin(ak), math.cos(ak), 0))
        r0 = float(np.interp(ak, a, radius, period=2 * np.pi)) + 0.0045 * s
        centre = Vector((0, cy, z - 0.002 * s)) + d * (r0 + 0.008 * s)
        out.append(self._knot(centre, d, color))
        drop = z - self.f.j["knee.R"].z
        for i, (da, length) in enumerate(((-0.05, 0.62), (0.08, 0.5))):
            out.append(self._tail(skirt, ak + da, z - 0.004 * s, length * drop, i, color, f"sash_end{i}"))
        for o in out[:2]:
            rigid(o, "hips")
        for o in out[2:]:
            write_weights(o, [self.skirt_weights(v.co, self.f.j["ankle.R"].z) for v in o.data.vertices])
        return out

    def _knot(self, centre, d, color):
        """The sash's knot: a squat lump of cloth, fuller at the bottom where the ends come out, the wrap pressed
        across it on a slant."""
        from .garments import mesh_from, set_slot
        s = self.f.s
        side, up = Vector((d.y, -d.x, 0)), Vector((0, 0, 1))
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=9, radius=1.0)
        base = hex3(color)
        shade = {}
        for v in bm.verts:
            x, y, zz = (math.copysign(abs(c) ** 0.6, c) for c in v.co)       # a soft, squarish lump
            wrap = math.exp(-((x * 0.8 - zz * 0.6) / 0.26) ** 2)
            fat = 1.0 + 0.12 * (-zz)
            v.co = (centre + side * (x * 0.03 * s * fat) + up * (zz * 0.024 * s)
                    + d * (y * 0.012 * s * (1.0 - 0.4 * wrap)))
            shade[v] = (1.0 - 0.34 * wrap) * (1.0 - 0.14 * max(0.0, -zz)) * (0.9 + 0.1 * max(y, 0.0))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        layer = bm.loops.layers.color.new("Color")
        for f in bm.faces:
            for lp in f.loops:
                k = shade[lp.vert]
                lp[layer] = (base[0] * k, base[1] * k, base[2] * k, 1.0)
        obj = mesh_from(bm, "sash_knot")
        set_slot(obj, "char:cloth")
        return obj

    def _tail(self, skirt, ang, z0, length, seed, color, name):
        """One hanging end of the sash: a ribbon with thickness lying down the skirt, swaying and turning a little,
        its end cut on a slant and hemmed."""
        from .garments import mesh_from, set_slot
        s, cy = self.f.s, self.f.j["spine"].y
        steps = 12
        segs = 72
        ts = np.linspace(0.0, 1.0, steps + 1)
        angs = ang + 0.012 * np.sin(ts * 3.0 + seed * 1.7) * ts
        rad = []
        for t, a in zip(ts, angs):          # resting on the crests of the skirt's folds under its whole width
            ring = self.radial([skirt], z0 - length * t, 0.012 * s, segs)
            k = int(round(a / (2 * np.pi) * segs))
            reach = int(math.ceil(0.022 * s / ring[k % segs] / (2 * np.pi / segs)))
            rad.append(max(ring[(k + d) % segs] for d in range(-reach, reach + 1)))
        rad = np.maximum.accumulate(np.convolve(np.pad(rad, 2, mode="edge"), np.ones(5) / 5, mode="valid"))
        path, outs = [], []
        for t, a, r in zip(ts, angs, rad):
            d = Vector((math.sin(a), math.cos(a), 0))
            path.append(Vector((0, cy, z0 - length * t)) + d * (r + (0.0045 + 0.002 * t) * s))
            outs.append(d)
        base = hex3(color)
        bm = bmesh.new()
        layer = bm.loops.layers.color.new("Color")
        rings, across = [], {}
        oval = [(math.cos(q), math.sin(q)) for q in (2 * math.pi * i / 10 for i in range(10))]
        for k, (p, o) in enumerate(zip(path, outs)):
            t = k / steps
            tan = (path[min(k + 1, steps)] - path[max(k - 1, 0)]).normalized()
            o = (o - tan * o.dot(tan)).normalized()
            sd = tan.cross(o)
            tw = 0.12 * math.sin(t * 2.5 + seed * 2.0) * t
            sd, o = sd * math.cos(tw) + o * math.sin(tw), o * math.cos(tw) - sd * math.sin(tw)
            w, th = 0.036 * s * (1 + 0.1 * t), 0.0046 * s
            slant = (0.014 * s if k == steps else 0.0)
            cup = 0.004 * s * (0.6 + 0.4 * math.sin(t * 5.0 + seed))          # cloth curling at its edges
            rings.append([bm.verts.new(p + sd * (x * w / 2) + o * (y * th / 2 + cup * (0.5 - x * x)) + tan * (x * slant))
                          for x, y in oval])
            across.update((v, x) for v, (x, _) in zip(rings[-1], oval))
        tip = bm.verts.new(sum((v.co for v in rings[-1]), Vector()) / len(rings[-1]))
        faces = []
        for k in range(steps):
            for i in range(10):
                faces.append((bm.faces.new((rings[k][i], rings[k][(i + 1) % 10], rings[k + 1][(i + 1) % 10],
                                            rings[k + 1][i])), 0.72 if k == steps - 1 else 1.0))
        for i in range(10):
            faces.append((bm.faces.new((rings[-1][i], rings[-1][(i + 1) % 10], tip)), 0.72))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        for f, k in faces:
            for lp in f.loops:
                edge = 1.0 - 0.16 * abs(across.get(lp.vert, 0.0)) ** 4        # rounded selvedges a shade darker
                lp[layer] = (base[0] * k * edge, base[1] * k * edge, base[2] * k * edge, 1.0)
        obj = mesh_from(bm, name)
        set_slot(obj, "char:cloth")
        return obj
