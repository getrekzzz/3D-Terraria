"""Procedural mesh building on bmesh: primitives with per-part material and vertex colour, merged into one object.

Conventions (see docs/architecture/assets.md):
  * 1 Blender unit = 1 metre, Z up. The glTF exporter converts to Y up; Blender +Y becomes glTF -Z.
  * Tile objects: origin at the centre of the footprint's bottom face, front facing Blender +Y (the player).
  * Items: origin at the grip, blade/head along +Z.
"""
import math
import random
import zlib

import bpy  # must precede bmesh when running as the standalone bpy module
import bmesh
from mathutils import Matrix, Vector


def rng_for(*parts) -> random.Random:
    """Deterministic RNG from names/numbers (stable across runs and platforms)."""
    return random.Random(zlib.crc32("|".join(str(p) for p in parts).encode("utf-8")))


def hex_color(value, alpha=1.0):
    if isinstance(value, (tuple, list)):
        return tuple(value) + ((alpha,) if len(value) == 3 else ())
    h = value.lstrip("#")
    return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0, alpha)


class MeshBuilder:
    """Accumulates primitives into one bmesh; each primitive gets a material slot and a vertex colour."""

    def __init__(self, name: str):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("Color")
        self.materials: list[str] = []
        self.smooth_faces: set = set()
        # Rigid skinning: parts created while ``bone`` is set are weighted 100 % to that bone.
        self.bone: str | None = None
        self.bones: list[str] = []
        # Created up front: adding a layer later would invalidate BMVert references held by callers.
        self.deform = self.bm.verts.layers.deform.verify()

    # ------------------------------------------------------------------ bookkeeping
    def _material_index(self, material: str) -> int:
        if material not in self.materials:
            self.materials.append(material)
        return self.materials.index(material)

    def _finish_part(self, geom_verts, material: str, color, smooth: bool, matrix: Matrix | None = None, shade: float = 0.0):
        """Transforms a new part and assigns material and vertex colour. ``shade`` darkens the part's underside
        (baked self-shadowing for foliage: 0 = none, 0.5 = bottom at half brightness)."""
        verts = [v for v in geom_verts if isinstance(v, bmesh.types.BMVert)]
        if matrix is not None:
            bmesh.ops.transform(self.bm, matrix=matrix, verts=verts)
        faces = {f for v in verts for f in v.link_faces}
        index = self._material_index(material)
        rgba = hex_color(color)
        if self.bone is not None:
            if self.bone not in self.bones:
                self.bones.append(self.bone)
            group = self.bones.index(self.bone)
            for v in verts:
                v[self.deform][group] = 1.0
        z0 = min(v.co.z for v in verts)
        z1 = max(v.co.z for v in verts)
        for f in faces:
            f.material_index = index
            for loop in f.loops:
                if shade > 0 and z1 > z0:
                    k = (1 - shade) + shade * (loop.vert.co.z - z0) / (z1 - z0)
                    loop[self.color_layer] = (rgba[0] * k, rgba[1] * k, rgba[2] * k, rgba[3])
                else:
                    loop[self.color_layer] = rgba
            if smooth:
                self.smooth_faces.add(f)
        return verts

    # ------------------------------------------------------------------ primitives
    def box(self, lo, hi, material, color="#ffffff", matrix: Matrix | None = None):
        lo, hi = Vector(lo), Vector(hi)
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        scale = Matrix.Diagonal((*(hi - lo), 1.0))
        m = Matrix.Translation((lo + hi) / 2) @ scale
        if matrix is not None:
            m = matrix @ m
        return self._finish_part(res["verts"], material, color, False, m)

    def cylinder(self, p0, p1, r0, r1=None, material="plain", color="#ffffff", segments=10, smooth=True, caps=True, shade=0.0):
        """Tapered cylinder (or cone when r1 == 0) between two points."""
        r1 = r0 if r1 is None else r1
        p0, p1 = Vector(p0), Vector(p1)
        axis = p1 - p0
        length = max(axis.length, 1e-5)
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segments,
                                    radius1=r0, radius2=max(r1, 0.0), depth=length)
        rot = axis.normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()
        m = Matrix.Translation((p0 + p1) / 2) @ rot
        return self._finish_part(res["verts"], material, color, smooth, m, shade)

    def sphere(self, center, radius, material, color="#ffffff", subdivisions=2, scale=(1, 1, 1),
               jitter=0.0, rng: random.Random | None = None, smooth=True, rotation=None, shade=0.0):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subdivisions, radius=radius)
        verts = res["verts"]
        if jitter > 0:
            r = rng or random.Random(0)
            for v in verts:
                v.co *= 1.0 + (r.random() - 0.5) * 2 * jitter
        m = Matrix.Translation(Vector(center)) @ (rotation or Matrix.Identity(4)) @ Matrix.Diagonal((*scale, 1.0))
        return self._finish_part(verts, material, color, smooth, m, shade)

    def lathe(self, profile, material, color="#ffffff", segments=16, smooth=True, matrix: Matrix | None = None):
        """Surface of revolution around Z from (radius, height) points, bottom to top; closed ends."""
        rings = []
        for r, z in profile:
            ring = []
            for i in range(segments):
                a = 2 * math.pi * i / segments
                ring.append(self.bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
            rings.append(ring)
        faces = []
        for a, b in zip(rings, rings[1:]):
            for i in range(segments):
                j = (i + 1) % segments
                faces.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        if profile[0][0] > 1e-5:
            faces.append(self.bm.faces.new(list(reversed(rings[0]))))
        if profile[-1][0] > 1e-5:
            faces.append(self.bm.faces.new(rings[-1]))
        verts = [v for ring in rings for v in ring]
        return self._finish_part(verts, material, color, smooth, matrix)

    def extrude_polygon(self, points2d, depth, material, color="#ffffff", matrix: Matrix | None = None):
        """Flat polygon in the XZ plane (x, z) extruded along Y by depth (centred)."""
        front = [self.bm.verts.new((x, -depth / 2, z)) for x, z in points2d]
        back = [self.bm.verts.new((x, depth / 2, z)) for x, z in points2d]
        self.bm.faces.new(front)
        self.bm.faces.new(list(reversed(back)))
        n = len(points2d)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((front[i], back[i], back[j], front[j]))
        return self._finish_part(front + back, material, color, False, matrix)

    def tube(self, points, radii, material, color="#ffffff", segments=8, smooth=True):
        """Swept tube along a polyline with per-point radius (branches, trunks, roots, tentacles)."""
        pts = [Vector(p) for p in points]
        rings = []
        prev_side = None
        for i, p in enumerate(pts):
            tangent = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            side = prev_side or (tangent.orthogonal().normalized())
            side = (side - tangent * side.dot(tangent)).normalized()
            prev_side = side
            up = tangent.cross(side)
            ring = []
            for k in range(segments):
                a = 2 * math.pi * k / segments
                ring.append(self.bm.verts.new(p + (side * math.cos(a) + up * math.sin(a)) * radii[i]))
            rings.append(ring)
        for a, b in zip(rings, rings[1:]):
            for k in range(segments):
                j = (k + 1) % segments
                self.bm.faces.new((a[k], a[j], b[j], b[k]))
        self.bm.faces.new(list(reversed(rings[0])))
        if radii[-1] > 1e-5:
            self.bm.faces.new(rings[-1])
        return self._finish_part([v for r in rings for v in r], material, color, smooth)

    # ------------------------------------------------------------------ foliage cards
    def card(self, base, up, side, length, width, material, color, normal_of, flip=False):
        """One alpha-tested leaf card (``card:<layer>`` slot): texture v runs from the twig at ``base`` along ``up``.
        ``normal_of(position)`` gives the shading normal per corner (crowns shade as one soft volume); ``color`` is
        an RGBA tuple, per card, carrying tint and baked occlusion."""
        uv = self.bm.loops.layers.uv.verify()
        hint = self.bm.loops.layers.float_color.get("NormalHint") or self.bm.loops.layers.float_color.new("NormalHint")
        base, up, side = Vector(base), Vector(up).normalized(), Vector(side).normalized()
        corners = [base - side * width * 0.5, base + side * width * 0.5, base + side * width * 0.5 + up * length,
                   base - side * width * 0.5 + up * length]
        face = self.bm.faces.new([self.bm.verts.new(q) for q in corners])
        face.material_index = self._material_index(material)
        face.smooth = True
        self.smooth_faces.add(face)
        for loop, (u, v) in zip(face.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv].uv = (1 - u if flip else u, v)
            n = normal_of(loop.vert.co)
            loop[hint] = (n.x, n.y, n.z, 2.0)
            loop[self.color_layer] = color
        return face

    def leaf_cards(self, center, radius, material, color="#ffffff", rng: random.Random | None = None, count=40,
                   scale=(1, 1, 1), size=0.5, droop=0.0, shade=0.5, core=0.4):
        """A crown volume filled with leaf cards: scattered through an ellipsoid, each growing outward from its
        twig end, facing a random way. Normals follow the ellipsoid so the mass shades softly; cards deep inside
        and on the underside are darker (baked self-shadowing, ``shade`` 0–1)."""
        r = rng or random.Random(0)
        c, sc = Vector(center), Vector(scale)
        axes = Vector((radius * sc.x, radius * sc.y, radius * sc.z))
        rgba = hex_color(color)

        def normal_of(q):
            d = q - c
            return Vector((d.x / axes.x ** 2, d.y / axes.y ** 2, d.z / axes.z ** 2)).normalized()

        for _ in range(count):
            d = Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1) + 0.3)).normalized()
            depth = r.random() ** 0.5
            p = c + Vector((d.x * axes.x, d.y * axes.y, d.z * axes.z)) * (core + (1 - core) * depth)
            out = Vector((d.x * sc.x, d.y * sc.y, d.z * sc.z)).normalized()
            up = out + Vector((r.uniform(-0.6, 0.6), r.uniform(-0.6, 0.6), r.uniform(-0.3, 0.6) - droop))
            side = up.cross(Vector((r.uniform(-1, 1), r.uniform(-1, 1), r.uniform(-1, 1))))
            if up.length < 1e-3 or side.length < 1e-3:
                continue
            up.normalize()
            s = size * radius * r.uniform(0.75, 1.2)
            height = max(-1.0, min(1.0, d.z * (core + (1 - core) * depth)))
            k = (1 - shade * 0.5 + shade * 0.5 * height) * (1 - shade * 0.6 * (1 - depth))
            self.card(p - up * s * 0.3, up, side, s, s, material, (rgba[0] * k, rgba[1] * k, rgba[2] * k, rgba[3]),
                      normal_of, flip=r.random() < 0.5)

    # ------------------------------------------------------------------ output
    def build(self, material_factory) -> bpy.types.Object:
        """Creates the Blender object. ``material_factory(name)`` returns a bpy material for a slot name."""
        if not self.bones:  # rigged parts must stay separate so each keeps its own bone
            bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        for f in self.bm.faces:
            f.smooth = f in self.smooth_faces
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        if "NormalHint" in mesh.color_attributes:
            # Leaf cards carry their shading normals in a temporary attribute (alpha 2 marks them).
            hints = mesh.color_attributes["NormalHint"]
            buf = [0.0] * (len(mesh.loops) * 4)
            hints.data.foreach_get("color", buf)
            normals = [(buf[i * 4], buf[i * 4 + 1], buf[i * 4 + 2]) if buf[i * 4 + 3] > 1.5 else (0.0, 0.0, 0.0)
                       for i in range(len(mesh.loops))]
            mesh.color_attributes.remove(hints)
            mesh.normals_split_custom_set(normals)
        if "Color" in mesh.color_attributes:
            color = mesh.color_attributes["Color"]
            mesh.color_attributes.active_color = color
            mesh.color_attributes.render_color_index = mesh.color_attributes.find("Color")
        for name in self.materials:
            mesh.materials.append(material_factory(name))
        obj = bpy.data.objects.new(self.name, mesh)
        for bone in self.bones:
            obj.vertex_groups.new(name=bone)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.armatures, bpy.data.actions):
        for item in list(block):
            block.remove(item)


def bounds(objects) -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi
