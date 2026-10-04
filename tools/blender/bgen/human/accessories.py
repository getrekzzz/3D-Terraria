"""Hats (cap, wide brim, hood, wreath), a face mask and creature features (horns, bat wings, a spaded tail), fitted to
the generated head and body and bound to the bones that carry them."""
import math

import bmesh
from mathutils import Matrix, Vector

from .garments import colorize, mesh_from, set_slot
from .rig import rigid


def _lathe(bm, centre, profile, segs, scale_y=1.0):
    rings = []
    for r, z in profile:
        rings.append([bm.verts.new(centre + Vector((math.cos(a) * r, math.sin(a) * r * scale_y, z)))
                      for a in (2 * math.pi * k / segs for k in range(segs))] if r > 1e-6 else [bm.verts.new(centre + Vector((0, 0, z)))])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            if len(a) == 1:
                bm.faces.new((a[0], b[k], b[(k + 1) % segs]))
            elif len(b) == 1:
                bm.faces.new((a[k], a[(k + 1) % segs], b[0]))
            else:
                bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))


def hat(head, head_obj, kind, color, flower="#f0d040", visor=True, under=()):
    """A hat on the head; a hood's cowl drapes over ``under`` (the body and the garments on its shoulders)."""
    h = head.h
    if kind in ("cap", "hood"):            # sculpted cloth (headwear): a cap on the skull, a hood standing off it
        from . import headwear
        obj = headwear.mesh(head, kind, visor, under)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bpy_data_remove(obj)
    else:
        bm = bmesh.new()
    top = head.at((0, -0.012, 0.0))
    if kind == "wide":
        brim = [(0.19 * h, 0.05 * h), (0.16 * h, 0.056 * h), (0.098 * h, 0.06 * h)]
        crown = [(0.098 * h, 0.06 * h), (0.092 * h, 0.14 * h), (0.07 * h, 0.155 * h), (0.0, 0.158 * h)]
        _lathe(bm, top, [(0.19 * h, 0.046 * h)] + brim + crown, 40, 1.08)
    elif kind == "wreath":
        for k in range(22):
            a = 2 * math.pi * k / 22
            c = head.at((math.cos(a) * 0.084, math.sin(a) * 0.1 - 0.012, 0.062 - 0.012 * math.sin(a)))
            res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.013 * h)
            bmesh.ops.scale(bm, vec=(1.4, 0.7, 0.5), verts=res["verts"])
            bmesh.ops.rotate(bm, cent=Vector(), matrix=Matrix.Rotation(a + 1.2, 3, "Z"), verts=res["verts"])
            bmesh.ops.translate(bm, vec=c, verts=res["verts"])
    colorize(bm, color)
    if kind == "wreath":
        layer = bm.loops.layers.color["Color"]
        from .paint import hex3
        fc = hex3(flower)
        for i, f in enumerate(bm.faces):
            if (i // 20) % 5 == 2:
                for loop in f.loops:
                    loop[layer] = (*fc, 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = mesh_from(bm, f"hat_{kind}")
    set_slot(obj, "char:cloth")
    rigid(obj, "head")
    return obj


def bpy_data_remove(obj):
    import bpy
    me = obj.data
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(me)


def mask(head, head_obj, color):
    """A smooth face mask (the Lunatic Cultist's): the face lifted into a thin shell over the skin, with eye holes."""
    h = head.h
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.normal_update()
    eyes = [Vector((sx * 0.031, 0.066, -0.002)) for sx in (1, -1)]
    drop = []
    for f in bm.faces:
        q = head.local(f.calc_center_median())
        if not (q.y > 0.02 and abs(q.x) < 0.065 and -0.11 < q.z < 0.05) or any((q - e).length < 0.016 for e in eyes):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * 0.007 * h
    colorize(bm, color)
    obj = mesh_from(bm, "mask")
    set_slot(obj, "char:bone")
    rigid(obj, "head")
    return obj


def horns(head, color):
    bm = bmesh.new()
    h = head.h
    for sx in (1, -1):
        pts = [head.at((sx * 0.035, 0.05, 0.08)), head.at((sx * 0.06, 0.02, 0.13)), head.at((sx * 0.07, -0.04, 0.16)), head.at((sx * 0.055, -0.09, 0.15))]
        radii = [0.016 * h, 0.012 * h, 0.007 * h, 0.001 * h]
        _tube(bm, pts, radii, 10)
    colorize(bm, color)
    obj = mesh_from(bm, "horns")
    set_slot(obj, "char:leather")
    rigid(obj, "head")
    return obj


def wings(frame, color):
    """Bat wings: bony struts fanning from the shoulder blades with a membrane between them."""
    s = frame.s
    bm = bmesh.new()
    for sx in (1, -1):
        root = Vector((sx * 0.06 * s, -0.12 * s, 1.35 * s))
        tips = [Vector((sx * 0.75, -0.3, 1.75)) * s, Vector((sx * 0.85, -0.35, 1.35)) * s, Vector((sx * 0.7, -0.32, 1.0)) * s, Vector((sx * 0.4, -0.25, 0.95)) * s]
        elbow = Vector((sx * 0.35 * s, -0.22 * s, 1.62 * s))
        ring = [bm.verts.new(root), bm.verts.new(elbow)] + [bm.verts.new(t) for t in tips]
        for a, b in zip(ring[2:], ring[3:]):
            mid = bm.verts.new(elbow.lerp((a.co + b.co) / 2, 0.55) + Vector((0, 0.03 * s, 0)))
            bm.faces.new((ring[1], a, mid))
            bm.faces.new((ring[1], mid, b))
        bm.faces.new((ring[0], ring[1], ring[-1]))
        _tube(bm, [root, elbow, tips[0]], [0.018 * s, 0.012 * s, 0.003 * s], 6)
    colorize(bm, color)
    obj = mesh_from(bm, "wings")
    set_slot(obj, "char:leather")
    rigid(obj, "chest")
    return obj


def tail(frame, color):
    s = frame.s
    bm = bmesh.new()
    pts = [Vector((0, -0.09, 0.98)) * s, Vector((0, -0.3, 0.8)) * s, Vector((0, -0.48, 0.86)) * s, Vector((0, -0.6, 1.05)) * s]
    _tube(bm, pts, [0.024 * s, 0.018 * s, 0.012 * s, 0.006 * s], 8)
    tip = pts[-1]
    spade = [bm.verts.new(tip + Vector(v) * s) for v in ((0, 0, 0.07), (0.05, 0.0, 0.0), (0, 0.0, -0.02), (-0.05, 0.0, 0.0))]
    bm.faces.new(spade)
    colorize(bm, color)
    obj = mesh_from(bm, "tail")
    set_slot(obj, "char:leather")
    rigid(obj, "hips")
    return obj


def _tube(bm, pts, radii, segs):
    prev = None
    side_prev = None
    for i, p in enumerate(pts):
        tangent = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = side_prev or tangent.orthogonal().normalized()
        side = (side - tangent * side.dot(tangent)).normalized()
        side_prev = side
        up = tangent.cross(side)
        ring = [bm.verts.new(p + (side * math.cos(a) + up * math.sin(a)) * radii[i]) for a in (2 * math.pi * k / segs for k in range(segs))]
        if prev:
            for k in range(segs):
                bm.faces.new((prev[k], prev[(k + 1) % segs], ring[(k + 1) % segs], ring[k]))
        prev = ring
