"""Implicit modelling with metaballs: soft tissue as overlapping capsules and ellipsoids that blend like muscle
masses, polygonised by Blender, then cleaned (decimated, smoothed) into a game mesh.

All sizes are *surface* sizes in metres: Blender's metaball surface sits at 0.574 x the element radius for the
threshold/stiffness used here, so the helpers convert.
"""
import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

SURFACE = 0.574  # surface radius / element radius at threshold 0.6, stiffness 2
THRESHOLD = 0.6


class Blob:
    def __init__(self, name: str, resolution: float):
        self.data = bpy.data.metaballs.new(name)
        self.data.resolution = resolution
        self.data.render_resolution = resolution
        self.data.threshold = THRESHOLD
        self.obj = bpy.data.objects.new(name, self.data)
        bpy.context.scene.collection.objects.link(self.obj)

    def capsule(self, p0, p1, r, negative=False, stiffness=2.0):
        """Capsule between two points with surface radius r (a limb segment)."""
        p0, p1 = Vector(p0), Vector(p1)
        e = self.data.elements.new(type="CAPSULE")
        e.co = (p0 + p1) / 2
        e.radius = r / SURFACE
        e.size_x = max((p1 - p0).length / 2, 1e-4)
        e.rotation = (p1 - p0).to_track_quat("X", "Z") if (p1 - p0).length > 1e-6 else Quaternion()
        e.use_negative = negative
        e.stiffness = stiffness
        return e

    def ellipsoid(self, c, radii, rotation: Quaternion | None = None, negative=False, stiffness=2.0):
        """Ellipsoid with surface semi-axes ``radii`` (x, y, z), optionally rotated."""
        base = max(radii)
        e = self.data.elements.new(type="ELLIPSOID")
        e.co = Vector(c)
        e.radius = base / SURFACE
        e.size_x, e.size_y, e.size_z = (r / base for r in radii)
        e.rotation = rotation or Quaternion()
        e.use_negative = negative
        e.stiffness = stiffness
        return e

    def ball(self, c, r, negative=False, stiffness=2.0):
        e = self.data.elements.new(type="BALL")
        e.co = Vector(c)
        e.radius = r / SURFACE
        e.use_negative = negative
        e.stiffness = stiffness
        return e

    def to_mesh(self, name: str, target_tris: int, smooth_iterations: int = 2) -> bpy.types.Object:
        """Polygonises the field and reduces it to about ``target_tris`` triangles with smooth shading."""
        dg = bpy.context.evaluated_depsgraph_get()
        mesh = bpy.data.meshes.new_from_object(self.obj.evaluated_get(dg))
        mesh.name = name
        bpy.data.objects.remove(self.obj)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
        if tris > target_tris:
            dec = obj.modifiers.new("decimate", "DECIMATE")
            dec.ratio = target_tris / tris
            dec.use_collapse_triangulate = True
        if smooth_iterations:
            sm = obj.modifiers.new("smooth", "CORRECTIVE_SMOOTH")
            sm.iterations = smooth_iterations
            sm.smooth_type = "LENGTH_WEIGHTED"
            sm.use_only_smooth = True
        apply_modifiers(obj)
        for poly in obj.data.polygons:
            poly.use_smooth = True
        return obj


def apply_modifiers(obj: bpy.types.Object):
    dg = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    old = obj.data
    obj.modifiers.clear()
    obj.data = mesh
    bpy.data.meshes.remove(old)


def displace(obj: bpy.types.Object, fn):
    """Moves every vertex by fn(position, normal) -> offset vector (surface sculpting after polygonisation)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    moves = [(v, fn(v.co.copy(), v.normal.copy())) for v in bm.verts]
    for v, d in moves:
        v.co += d
    bm.to_mesh(obj.data)
    bm.free()


def track(direction: Vector, up=Vector((0, 0, 1))) -> Quaternion:
    """Rotation taking +X to ``direction`` (element long axis), keeping +Z near ``up``."""
    return Vector(direction).normalized().to_track_quat("X", "Z")


def rot_between(a: Vector, b: Vector) -> Matrix:
    return Vector(a).rotation_difference(Vector(b)).to_matrix().to_4x4()
