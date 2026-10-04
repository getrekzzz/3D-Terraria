"""The head: a sculpted surface (headshape: skull, jaw, cheekbones, brow, eyelids, nose, lips and the neck, as a signed
distance field) meshed at sub-millimetre detail, with the ears sculpted in, plus separate eyeballs.

Head-local coordinates: origin at the skull's centre, x to the character's right, y forward (face), z up.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import headshape


class Head:
    def __init__(self, frame, p):
        self.frame = frame
        self.p = p
        base = frame.s * (0.95 if frame.female else 1.0)
        self.h = base * p.get("headSize", 1.0)
        # A bigger head (``headSize``) grows up and out from under the chin, so the neck keeps its length.
        self.c = frame.head_center + Vector((0, 0, 0.11)) * (self.h - base)
        if frame.female:      # a woman's smaller head sits a little lower, so her neck isn't drawn out
            self.c.z -= 0.008 * frame.s
        h = self.h
        self.eye_r = 0.0118 * h
        self.eyes = {sx: self.at((sx * 0.0315, 0.0695, -0.002)) for sx in (1, -1)}
        self._shape = None

    def at(self, local) -> Vector:
        return self.c + Vector(local) * self.h

    def local(self, world: Vector) -> Vector:
        return (world - self.c) / self.h

    @property
    def shape(self) -> headshape.HeadShape:
        if self._shape is None:
            eyes = {sx: np.asarray(self.local(e), dtype=float) for sx, e in self.eyes.items()}
            self._shape = headshape.HeadShape(self.frame, eyes, self.eye_r / self.h, self.p)
        return self._shape

    # ------------------------------------------------------------------ shape
    def build(self) -> bpy.types.Object:
        obj = headshape.mesh(self.shape, h_local=0.00085, target=24000)
        me = obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        me.vertices.foreach_set("co", (co.reshape(-1, 3) * self.h + np.asarray(self.c, dtype=np.float32)).ravel())
        me.update()
        return obj

    def fit_neck(self, head_obj, body_obj):
        """The head's neck runs down into the chest and the body's own (thinner) neck stays inside it: drops the head
        faces the torso covers, so the torso shows over them (bury_neck later drops the body's neck)."""
        tree = BVHTree.FromObject(body_obj, bpy.context.evaluated_depsgraph_get())

        def inside_body(co):
            hit, normal, _, dist = tree.find_nearest(co)
            return hit is not None and (co - hit).dot(normal) < 0 and dist > 0.002 * self.h

        _delete_faces(head_obj, {i for i, poly in enumerate(head_obj.data.polygons)
                                 if self.local(poly.center).z < -0.1 and inside_body(poly.center)})

    def bury_neck(self, body_obj):
        """Drops the body faces inside the head's neck (after clothing is cut from the whole body, so a shirt has
        no gap where the neck stood)."""
        centres = np.array([self.local(poly.center) for poly in body_obj.data.polygons], dtype=np.float32).reshape(-1, 3)
        near = np.flatnonzero(centres[:, 2] > -0.34)
        inside = self.shape.field(centres[near]) < -0.002
        _delete_faces(body_obj, set(near[inside].tolist()))

    def neck_depth(self, X):
        """How far (metres) each point lies inside the head's neck (0 outside it)."""
        q = ((np.asarray(X) - np.asarray(self.c)) / self.h).astype(np.float32)
        out = np.zeros(len(q))
        near = np.flatnonzero(q[:, 2] > -0.34)
        if len(near):
            out[near] = np.maximum(-self.shape.field(q[near]) * self.h, 0.0)
        return out

    # ------------------------------------------------------------------ parts
    def eyeballs(self, iris_hex: str):
        """Eyeballs built around the gaze axis: exact rings for the pupil, iris (radial fibres) and dark limbus,
        a bulging cornea and a sclera that reddens toward the sides."""
        from .paint import hex3
        iris = hex3(iris_hex)
        R = self.eye_r
        # Angles from the gaze axis where rings are placed (radians); faces take the colour of their band.
        rings = [0.0, 0.08, 0.15, 0.165, 0.24, 0.32, 0.4, 0.455, 0.485, 0.53, 0.65, 0.85, 1.15, 1.5, 1.9, 2.4, 2.9, math.pi]
        seg = 36
        objs = []
        for sx in (1, -1):
            bm = bmesh.new()
            col = bm.loops.layers.color.new("Color")
            verts = []
            for a in rings:
                r = R * (1.0 + (0.06 * (1 - a / 0.5) ** 1.5 if a < 0.5 else 0.0))   # cornea bulge
                ring = []
                for k in range(seg if 0 < a < math.pi else 1):
                    t = 2 * math.pi * k / seg
                    ring.append(bm.verts.new((r * math.sin(a) * math.cos(t), r * math.cos(a), r * math.sin(a) * math.sin(t))))
                verts.append(ring)

            def band(a0, a1, t):
                mid = (a0 + a1) / 2
                if mid < 0.155:
                    return (0.015, 0.015, 0.02)
                if mid < 0.47:
                    k = (mid - 0.155) / 0.315
                    spoke = 0.8 + 0.4 * (0.5 + 0.5 * math.sin(t * 29.0)) * (0.5 + 0.5 * math.sin(t * 11.0 + 1.3))
                    return tuple(min(1.0, iris[i] * spoke * (1.25 - 0.6 * k)) for i in range(3))
                if mid < 0.51:
                    return tuple(x * 0.3 for x in iris)
                red = min(1.0, (mid - 0.51) / 1.4)
                return (0.86, 0.82 - 0.1 * red, 0.78 - 0.12 * red)

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


def _delete_faces(obj, indices):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in indices], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()


def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)
