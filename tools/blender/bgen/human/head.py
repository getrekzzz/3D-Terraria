"""The head: skull, jaw, cheekbones, brow, nose and lips as a fine blob; then sculpted detail on locally subdivided
regions (eyelids wrapping the eyeballs, the mouth line, nostrils, philtrum); separate eyeballs and ears.

Head-local coordinates: origin at the skull's centre, x to the character's right, y forward (face), z up.
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .blobs import Blob, apply_modifiers
from .body import limb_rotation


class Head:
    def __init__(self, frame, p):
        self.frame = frame
        self.p = p
        self.h = frame.s * (0.95 if frame.female else 1.0)
        self.c = frame.head_center.copy()
        h = self.h
        self.eye_r = 0.0118 * h
        self.eyes = {sx: self.at((sx * 0.0315, 0.0695, -0.002)) for sx in (1, -1)}

    def at(self, local) -> Vector:
        return self.c + Vector(local) * self.h

    def local(self, world: Vector) -> Vector:
        return (world - self.c) / self.h

    # ------------------------------------------------------------------ shape
    def build(self) -> bpy.types.Object:
        fem, h = self.frame.female, self.h
        male = 0.0 if fem else 1.0
        fat = 0.85 + 0.35 * self.frame.weight
        B = Blob("head_blob", 0.0024 * h)

        def e(c, r, rot=None, neg=False):
            B.ellipsoid(self.at(c), tuple(x * h for x in r), rot, negative=neg)

        e((0, -0.014, 0.024), (0.073, 0.094, 0.083))                       # cranium
        e((0, 0.042, 0.036), (0.058, 0.04, 0.048))                         # forehead
        for sx in (-1, 1):
            e((sx * 0.056, 0.018, 0.018), (0.024, 0.045, 0.04))            # temples
            e((sx * 0.05, 0.054, -0.012), (0.023, 0.02, 0.016))            # cheekbones
            e((sx * 0.043, 0.052, -0.04), (0.029 * fat, 0.028, 0.032))     # cheeks
            B.capsule(self.at((sx * 0.058, -0.004, -0.052)), self.at((sx * 0.022, 0.072, -0.095)), (0.018 + 0.003 * male) * h)  # jaw
            e((sx * 0.031, 0.086, -0.001), (0.017, 0.011, 0.012), neg=True)  # eye socket
        e((0, 0.05, -0.03), (0.05, 0.045, 0.042))                          # maxilla
        e((0, 0.038, -0.068), (0.053, 0.05, 0.036))                        # lower face
        B.ball(self.at((0, 0.082, -0.096)), (0.016 + 0.004 * male) * h)     # chin
        B.capsule(self.at((-0.041, 0.077, 0.015)), self.at((0.041, 0.077, 0.015)), (0.008 + 0.004 * male) * h)  # brow ridge
        # Nose: bridge, tip, wings, septum.
        B.capsule(self.at((0, 0.087, 0.008)), self.at((0, 0.109, -0.033)), 0.0058 * h)
        B.ball(self.at((0, 0.111, -0.039)), 0.0094 * h)
        for sx in (-1, 1):
            B.ball(self.at((sx * 0.0122, 0.095, -0.045)), 0.0071 * h)
        B.capsule(self.at((0, 0.1, -0.049)), self.at((0, 0.092, -0.055)), 0.004 * h)
        # Lips.
        e((0, 0.092, -0.064), (0.025, 0.0105, 0.0068))
        e((0, 0.09, -0.0752), (0.023, 0.011, 0.0078))
        # Neck continues down into the body (covering the body's neck).
        B.capsule(self.at((0, -0.012, -0.03)), self.at((0, -0.03, -0.2)), (0.054 if fem else 0.062) * h)
        obj = B.to_mesh("head", 9000, smooth_iterations=1)
        self._sculpt(obj)
        return obj

    def _sculpt(self, obj):
        """Refines the eye and mouth regions, then sculpts lids, the mouth line and nostrils."""
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        for _ in range(2):
            region = [f for f in bm.faces if self._detail_region(self.local(f.calc_center_median()))]
            edges = list({e for f in region for e in f.edges})
            bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True, smooth=0.5)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.normal_update()
        for v in bm.verts:
            v.co = self._sculpted(v.co.copy(), v.normal.copy())
        # Cut away the neck's bottom cap (it tucks into the body).
        bottom = [f for f in bm.faces if self.local(f.calc_center_median()).z < -0.15]
        bmesh.ops.delete(bm, geom=bottom, context="FACES")
        bm.to_mesh(obj.data)
        bm.free()
        for poly in obj.data.polygons:
            poly.use_smooth = True

    def _detail_region(self, q: Vector) -> bool:
        for sx in (1, -1):
            if abs(q.x - sx * 0.0315) < 0.026 and abs(q.z + 0.002) < 0.02 and q.y > 0.045:
                return True
        return abs(q.x) < 0.034 and -0.086 < q.z < -0.036 and q.y > 0.06

    def _sculpted(self, co: Vector, n: Vector) -> Vector:
        q = self.local(co)
        for sx in (1, -1):
            e = self.local(self.eyes[sx])
            d = q - e
            if abs(d.x) < 0.026 and abs(d.z) < 0.022 and d.y > -0.004:
                q = self._eyelid(q, e, sx)
        q = self._mouth(q)
        return self.c + q * self.h

    def _eyelid(self, q, e, sx):
        """Wraps skin around the eyeball: inside the almond-shaped opening the skin sinks behind the eye; around it,
        the lids hug the eyeball's curve, thickening away from the margin, and fade back into the socket."""
        R = self.eye_r / self.h
        d = q - e
        x = d.x * sx                       # + toward the outer corner
        z = d.z - 0.06 * x                 # outer corner sits a little higher
        half_w = 0.0148
        u = max(0.0, 1.0 - (x / half_w) ** 2)
        upper = 0.0058 * u ** 0.5
        lower = 0.0044 * u ** 0.65
        radial = math.hypot(d.x, d.z)
        dirn = d.normalized() if d.length > 1e-6 else Vector((0, 1, 0))
        if -lower < z < upper and u > 0:
            return e + dirn * (R - 0.0022)          # opening: hidden behind the eyeball
        gap = (z - upper) if z >= upper else (-lower - z)
        gap = max(gap, 0.0) + max(0.0, abs(x) - half_w)
        lid = R + 0.0009 + min(gap, 0.012) * 0.3
        if z >= upper and 0.006 < gap < 0.009:
            lid -= 0.0006                           # upper lid crease
        blend = 1.0 - smooth(0.012, 0.021, radial)
        return q.lerp(e + dirn * lid, blend)

    def _mouth(self, q):
        """Mouth line (a crease between the lips, deeper at the corners), philtrum groove and nostrils."""
        mz = -0.0697
        if abs(q.x) < 0.026 and abs(q.z - mz) < 0.004 and q.y > 0.07:
            k = (1.0 - abs(q.z - mz) / 0.004) * (1.0 - smooth(0.018, 0.026, abs(q.x)))
            q = q - Vector((0, 0.0024 + 0.0018 * smooth(0.012, 0.022, abs(q.x)), 0)) * k
        if abs(q.x) < 0.006 and -0.06 < q.z < -0.052 and q.y > 0.08:
            q = q - Vector((0, 0.0009 * (1 - abs(q.x) / 0.006), 0))
        # Nasolabial folds: shallow grooves from the nose wings to beside the mouth corners.
        for sx in (1, -1):
            a, b = Vector((sx * 0.018, 0, -0.047)), Vector((sx * 0.03, 0, -0.078))
            p2 = Vector((q.x, 0, q.z))
            t = max(0.0, min(1.0, (p2 - a).dot(b - a) / (b - a).length_squared))
            dist = (p2 - (a + (b - a) * t)).length
            if dist < 0.004 and q.y > 0.06:
                q = q - Vector((0, 0.0011 * (1 - dist / 0.004) * math.sin(t * math.pi) ** 0.5, 0))
            corner = Vector((q.x - sx * 0.026, 0, q.z + 0.0697))
            if corner.length < 0.005 and q.y > 0.07:
                q = q - Vector((0, 0.0016 * (1 - corner.length / 0.005), 0))
        for sx in (1, -1):
            nd = Vector((q.x - sx * 0.0072, 0, q.z + 0.0508))
            if nd.length < 0.0045 and q.y > 0.085:
                q = q + Vector((0, -0.001, 0.0035)) * (1 - nd.length / 0.0045)
        return q

    # ------------------------------------------------------------------ parts
    def ears(self):
        """Ears: a flattened plate with a rolled helix rim, a hollow concha and a lobe."""
        parts = []
        for sx in (1, -1):
            B = Blob(f"ear_{sx}", 0.0012 * self.h)
            c = self.at((sx * 0.077, -0.002, -0.012))
            out = Vector((sx, -0.25, 0)).normalized()
            rot = limb_rotation(Vector((0, 0.18, 1)))
            h = self.h
            B.ellipsoid(c + out * 0.004 * h, (0.029 * h, 0.017 * h, 0.0045 * h), rot)
            pts = [c + Vector((sx * 0.006, -0.012 * math.cos(a) - 0.004, 0.028 * math.sin(a) + 0.002)) * h for a in
                   (i * math.pi / 9 - math.pi * 0.35 for i in range(12))]
            for a, b in zip(pts, pts[1:]):
                B.capsule(a + out * 0.006 * h, b + out * 0.006 * h, 0.0038 * h)
            B.ellipsoid(c + out * 0.009 * h + Vector((0, 0.002, -0.004)) * h, (0.011 * h, 0.009 * h, 0.004 * h), rot, negative=True)
            B.ball(c + Vector((0, 0.004, -0.028)) * h + out * 0.004 * h, 0.0068 * h)
            if self.p.get("ears") == "pointed":  # goblins, elves: a long blade swept back and up from the helix
                for k in range(9):
                    t = k / 8
                    q = c + out * 0.006 * h + Vector((sx * 0.052 * t, -0.034 * t, 0.02 + 0.03 * t)) * h
                    B.ellipsoid(q, ((0.016 - 0.012 * t) * h, (0.011 - 0.008 * t) * h, 0.004 * h), rot)
            parts.append(B.to_mesh(f"ear_{sx}", 1400, smooth_iterations=1))
        return parts

    def eyeballs(self, iris_hex: str):
        """Eyeballs built around the gaze axis: exact rings for the pupil, iris (radial fibres) and dark limbus,
        a bulging cornea and a sclera that reddens toward the sides."""
        from .paint import hex3
        iris = hex3(iris_hex)
        R = self.eye_r
        # Angles from the gaze axis where rings are placed (radians); faces take the colour of their band.
        rings = [0.0, 0.08, 0.15, 0.165, 0.22, 0.29, 0.35, 0.385, 0.405, 0.46, 0.6, 0.85, 1.15, 1.5, 1.9, 2.4, 2.9, math.pi]
        seg = 36
        objs = []
        for sx in (1, -1):
            bm = bmesh.new()
            col = bm.loops.layers.color.new("Color")
            verts = []
            for a in rings:
                r = R * (1.0 + (0.06 * (1 - a / 0.4) ** 1.5 if a < 0.4 else 0.0))   # cornea bulge
                ring = []
                for k in range(seg if 0 < a < math.pi else 1):
                    t = 2 * math.pi * k / seg
                    ring.append(bm.verts.new((r * math.sin(a) * math.cos(t), r * math.cos(a), r * math.sin(a) * math.sin(t))))
                verts.append(ring)

            def band(a0, a1, t):
                mid = (a0 + a1) / 2
                if mid < 0.155:
                    return (0.015, 0.015, 0.02)
                if mid < 0.39:
                    k = (mid - 0.155) / 0.23
                    spoke = 0.8 + 0.4 * (0.5 + 0.5 * math.sin(t * 29.0)) * (0.5 + 0.5 * math.sin(t * 11.0 + 1.3))
                    return tuple(min(1.0, iris[i] * spoke * (1.25 - 0.6 * k)) for i in range(3))
                if mid < 0.41:
                    return tuple(x * 0.3 for x in iris)
                red = min(1.0, (mid - 0.41) / 1.4)
                return (0.93, 0.89 - 0.12 * red, 0.86 - 0.15 * red)

            for i in range(len(rings) - 1):
                a, b = verts[i], verts[i + 1]
                for k in range(seg):
                    t = 2 * math.pi * (k + 0.5) / seg
                    if len(a) == 1:
                        face = bm.faces.new((a[0], b[(k + 1) % seg], b[k]))
                    elif len(b) == 1:
                        face = bm.faces.new((a[k], a[(k + 1) % seg], b[0]))
                    else:
                        face = bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
                    c = band(rings[i], rings[i + 1], t)
                    for loop in face.loops:
                        loop[col] = (*c, 1.0)
                    face.smooth = True
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            bmesh.ops.translate(bm, verts=bm.verts, vec=self.eyes[sx])
            me = bpy.data.meshes.new(f"eye_{sx}")
            bm.to_mesh(me)
            bm.free()
            obj = bpy.data.objects.new(f"eye_{sx}", me)
            bpy.context.scene.collection.objects.link(obj)
            objs.append(obj)
        return objs


def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)
