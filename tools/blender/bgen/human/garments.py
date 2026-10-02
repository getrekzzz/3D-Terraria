"""Clothing shaped from the body itself: a garment is the body's surface over a region (shirt, trousers, boots,
coat), lifted off the skin with fabric folds at the joints, closed with a hem, and skinned with the body's own
weights. Skin that ends up under clothing is removed so nothing pokes through when limbs bend. Skirts (coats,
robes) are lofted from the waist and weighted between the hips and thighs; a belt ties it together.
"""
import math

import bmesh
import bpy
from mathutils import Vector

from .head import smooth
from .paint import hex3


class Wardrobe:
    def __init__(self, frame, p):
        self.f = frame
        self.p = p
        j, s = frame.j, frame.s
        self.waist = j["hip.R"].z + 0.1 * s
        self.boot_top = j["ankle.R"].z + (0.16 if "boots" in p else 0.05) * s
        self.cuff = p.get("sleeves", "long")
        self.tear = 0.0       # fraction of cloth torn away in ragged patches (the undead)
        self.collar_z = frame.j["neck"].z

    # ------------------------------------------------------------------ regions (on rest-pose body positions)
    def arm_t(self, co, side):
        """How far along the arm (0 shoulder .. 1 wrist) a point lies, or None if it isn't on that arm."""
        j, s = self.f.j, self.f.s
        sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
        ab = wr - sh
        t = (co - sh).dot(ab) / ab.length_squared
        near = (co - (sh + ab * max(0.0, min(1.0, t)))).length
        sx = 1 if side == "R" else -1
        return t if co.x * sx > abs(sh.x) * 0.7 and t > -0.05 and near < 0.085 * s else None

    def _torn(self, c):
        """Ragged holes: patchy low-frequency noise with frayed edges."""
        if self.tear <= 0.0:
            return False
        s = self.f.s
        patch = math.sin(c.x / s * 23 + 1.3) * math.sin(c.z / s * 17 + c.y / s * 11) + 0.35 * math.sin(c.x / s * 97 + c.z / s * 83)
        return patch > 1.0 - self.tear * 1.6

    def neck_radial(self, co):
        n = self.f.j["neck"]
        return ((co.x - n.x) ** 2 + (co.y - n.y) ** 2) ** 0.5

    def fit_collar(self, body):
        """The collar height: just above the highest point of the shoulder girdle around the neck, so one level cut
        crosses the (vertical) neck cleanly and everything outside the neck lies below it."""
        s = self.f.s
        top = self.f.j["neck"].z + 0.05 * s
        ring = [v.co.z for v in body.data.vertices
                if 0.088 * s < self.neck_radial(v.co) < 0.14 * s and v.co.z < top and not self._on_arm(v.co)]
        self.collar_z = max(ring, default=self.f.j["neck"].z) + 0.004 * s

    def sleeve_limit(self):
        return 0.97 if self.cuff == "long" else 0.38

    def in_shirt(self, co):
        j, s = self.f.j, self.f.s
        for side in ("R", "L"):
            t = self.arm_t(co, side)
            if t is not None and t > 0.05:
                return t < self.sleeve_limit()
        if co.z < self.waist - 0.07 * s:
            return False
        return co.z < self.collar_z

    def in_pants(self, co):
        return self.boot_top - 0.01 * self.f.s < co.z < self.waist + 0.012 * self.f.s and not self._on_arm(co)

    def in_boots(self, co):
        return co.z < self.boot_top + 0.01 * self.f.s

    def _on_arm(self, co):
        return any((t := self.arm_t(co, side)) is not None and t > 0.05 for side in ("R", "L"))

    # ------------------------------------------------------------------ clean edges: planes cut before selection
    def shirt_cuts(self):
        j, s = self.f.j, self.f.s
        up = Vector((0, 0, 1))
        n = j["neck"]
        cuts = [(Vector((0, 0, self.waist - 0.07 * s)), up, lambda c: not self._on_arm(c))]
        cuts.append((Vector((0, 0, self.collar_z)), up, lambda c: self.neck_radial(c) < 0.14 * s))
        for side in ("R", "L"):
            sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
            cuts.append((sh + (wr - sh) * self.sleeve_limit(), (wr - sh).normalized(), lambda c, side=side: self.arm_t(c, side) is not None))
        return cuts

    def pants_cuts(self):
        s, up = self.f.s, Vector((0, 0, 1))
        knee = self.f.j["knee.R"].z
        return [(Vector((0, 0, self.waist + 0.012 * s)), up, lambda c: not self._on_arm(c)),
                (Vector((0, 0, self.boot_top - 0.01 * s)), up, lambda c: c.z < knee)]

    def boot_cuts(self):
        return [(Vector((0, 0, self.boot_top + 0.01 * self.f.s)), Vector((0, 0, 1)), lambda c: True)]

    def shell(self, body, test, name, thickness, color, material, folds=1.0, hem=True, cuts=()):
        """A garment over the body faces whose centres pass ``test``, after the body copy is cut cleanly along
        ``cuts`` (plane point, plane normal, face filter); returns the object (or None)."""
        bm = bmesh.new()
        bm.from_mesh(body.data)
        bm.verts.layers.deform.verify()
        for point, normal, filt in cuts:
            faces = [f for f in bm.faces if filt(f.calc_center_median())]
            geom = list({e for f in faces for e in f.edges}) + list({v for f in faces for v in f.verts}) + faces
            bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=point, plane_no=normal)
        bm.normal_update()
        drop = [f for f in bm.faces if not test(f.calc_center_median()) or self._torn(f.calc_center_median())]
        bmesh.ops.delete(bm, geom=drop, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        if not bm.faces:
            bm.free()
            return None
        bm.normal_update()
        j, s = self.f.j, self.f.s
        joints = [j[k] for k in j if k.split(".")[0] in ("elbow", "knee")]
        for v in bm.verts:
            near = min((v.co - p).length for p in joints)
            crease = folds * smooth(0.08 * s, 0.0, near) * math.sin(v.co.z * 180) * 0.0024 * s
            drape = folds * 0.0012 * s * math.sin(v.co.x * 70 + v.co.z * 45) * math.sin(v.co.y * 60)
            v.co += v.normal * (thickness * s + crease + drape)
        if hem:
            boundary = [e for e in bm.edges if e.is_boundary]
            res = bmesh.ops.extrude_edge_only(bm, edges=boundary)
            for v in (x for x in res["geom"] if isinstance(x, bmesh.types.BMVert)):
                ring = [e.other_vert(v) for e in v.link_edges if e.other_vert(v) not in res["geom"]]
                if ring:
                    v.co -= ring[0].normal * thickness * s * 1.6
        colorize(bm, color)
        obj = mesh_from(bm, name)
        for g in body.vertex_groups:            # the copied faces keep the body's weights (same group order)
            obj.vertex_groups.new(name=g.name)
        set_slot(obj, material)
        return obj

    def skirt(self, body, bottom_z, flare, color, name, open_front=False, trim=None):
        """A skirt from the waist to ``bottom_z``: rings follow the body's girth at the top and flare below."""
        s = self.f.s
        j = self.f.j
        top = self.waist - 0.02 * s
        verts = [v.co for v in body.data.vertices if abs(v.co.z - top) < 0.02 * s and not self._on_arm(v.co) and abs(v.co.x) < 0.26 * s]
        segs, rows = 36, 12
        radius = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            d = Vector((math.sin(a), math.cos(a), 0))
            radius.append(max([v.xy.dot(d.xy) for v in verts] or [0.16 * s]) + 0.016 * s)
        bm = bmesh.new()
        grid = []
        for i in range(rows + 1):
            t = i / rows
            z = top + (bottom_z - top) * t
            row = []
            for k in range(segs):
                a = 2 * math.pi * k / segs
                wave = 0.006 * s * math.sin(a * 7 + t * 2) * t
                r = radius[k] * (1 + flare * t ** 1.3) + wave
                row.append(bm.verts.new((math.sin(a) * r, math.cos(a) * r - 0.004 * s * t, z)))
            grid.append(row)
        gap = 1 if open_front else 0
        for i in range(rows):
            for k in range(gap, segs - gap):
                bm.faces.new((grid[i][k], grid[i][(k + 1) % segs], grid[i + 1][(k + 1) % segs], grid[i + 1][k]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        colorize(bm, color)
        if trim:
            layer = bm.loops.layers.color["Color"]
            c = hex3(trim)
            for f in bm.faces:
                if min(v.co.z for v in f.verts) < bottom_z + 0.05 * s:
                    for loop in f.loops:
                        loop[layer] = (*c, 1.0)
        # Loose cloth for the game's sway (X-17): vertex alpha 1 - weight / 2, from 0 at the waist to 0.9 at the hem.
        layer = bm.loops.layers.color["Color"]
        for f in bm.faces:
            for loop in f.loops:
                t = max(0.0, min(1.0, (top - loop.vert.co.z) / max(top - bottom_z, 1e-3)))
                c = loop[layer]
                loop[layer] = (c[0], c[1], c[2], 1.0 - 0.45 * t ** 1.2)
        obj = mesh_from(bm, name)
        set_slot(obj, "char:cloth")
        # Weights: hips at the waist, sharing with each thigh lower down.
        weights = []
        for v in obj.data.vertices:
            t = smooth(top, j["knee.R"].z, v.co.z) if bottom_z < top else 0.0
            side = smooth(-0.06 * s, 0.06 * s, v.co.x)
            weights.append({"hips": 1 - t * 0.75, "thigh.R": t * 0.75 * side, "thigh.L": t * 0.75 * (1 - side)})
        from .rig import write_weights
        write_weights(obj, [{b: w for b, w in d.items() if w > 0.01} for d in weights])
        return obj

    def belt(self, body, color, buckle="#c0a060"):
        """A leather band around the waist (following the body's girth) with a metal buckle in front."""
        s = self.f.s
        z = self.waist - 0.005 * s
        verts = [v.co for v in body.data.vertices if abs(v.co.z - z) < 0.02 * s and not self._on_arm(v.co) and abs(v.co.x) < 0.26 * s]
        segs = 40
        bm = bmesh.new()
        rings = []
        for dz in (-0.017, 0.017):
            ring = []
            for k in range(segs):
                a = 2 * math.pi * k / segs
                d = Vector((math.sin(a), math.cos(a), 0))
                r = max([v.xy.dot(d.xy) for v in verts] or [0.15 * s]) + 0.011 * s
                ring.append(bm.verts.new((d.x * r, d.y * r, z + dz * s)))
            rings.append(ring)
        for k in range(segs):
            bm.faces.new((rings[0][k], rings[0][(k + 1) % segs], rings[1][(k + 1) % segs], rings[1][k]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        colorize(bm, color)
        band = mesh_from(bm, "belt")
        set_slot(band, "char:leather")
        front = max(v.co.y for v in band.data.vertices)
        bm = bmesh.new()
        res = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=(0.05 * s, 0.008 * s, 0.04 * s), verts=res["verts"])
        bmesh.ops.translate(bm, vec=(0, front + 0.002 * s, z), verts=res["verts"])
        colorize(bm, buckle)
        clasp = mesh_from(bm, "buckle")
        set_slot(clasp, "char:metal")
        from .rig import rigid
        rigid(band, "hips")
        rigid(clasp, "hips")
        return [band, clasp]


def strip_covered(body, tests):
    """Deletes body faces lying well inside clothing (every vertex covered, two rings in from the garment's edge)."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    inner = {v.index for v in bm.verts if any(t(v.co) for t in tests)}
    for _ in range(2):
        inner = {v.index for v in bm.verts if v.index in inner and all(n.index in inner for e in v.link_edges for n in e.verts)}
    drop = [f for f in bm.faces if all(v.index in inner for v in f.verts)]
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(body.data)
    bm.free()


def colorize(bm, color):
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    c = hex3(color) if isinstance(color, str) else color
    for f in bm.faces:
        for loop in f.loops:
            loop[layer] = (*c, 1.0)


def mesh_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def set_slot(obj, slot):
    from ..export import simple_material
    obj.data.materials.clear()
    obj.data.materials.append(simple_material(slot))
    for poly in obj.data.polygons:
        poly.material_index = 0
