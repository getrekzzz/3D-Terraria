"""GLB export and icon rendering for generated assets."""
import math
import pathlib

import bpy
from mathutils import Euler, Matrix, Vector

from .mesh import bounds

# Icon exposure in stops (see render_icon).
ICON_EXPOSURE = 0.7


def simple_material(slot: str) -> bpy.types.Material:
    """Export-side material: just the slot name plus vertex colour (the game supplies textures)."""
    mat = bpy.data.materials.get(slot)
    if mat is None:
        mat = bpy.data.materials.new(slot)
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = nodes.get("Principled BSDF")
        vcol = nodes.new("ShaderNodeVertexColor")
        vcol.layer_name = "Color"
        links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def export_glb(objects, path: pathlib.Path, texcoords=False, tangents=False):
    """Exports GLB; texture coordinates go along when asked or when a mesh has UVs (leaf cards)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    texcoords = texcoords or any(o.type == "MESH" and len(o.data.uv_layers) > 0 for o in objects)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=str(path),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_texcoords=texcoords,
        export_normals=True,
        export_tangents=tangents,
        export_materials="EXPORT",
        export_image_format="NONE",
        export_vertex_color="ACTIVE",
        export_all_vertex_colors=False,
        export_animations=any(o.type == "ARMATURE" for o in objects),
        export_animation_mode="ACTIONS",
        export_frame_range=False,
        export_skins=True,
        export_extras=False,
        export_cameras=False,
        export_lights=False,
    )


def apply_textured_materials(objects, props):
    """Swaps export materials for textured ones (icon rendering only)."""
    for obj in objects:
        if obj.type != "MESH":
            continue
        for i, slot in enumerate(obj.data.materials):
            obj.data.materials[i] = props.material(slot.name)


def render_icon(objects, path: pathlib.Path, view: str, size: int = 128, samples: int = 24):
    """Renders a transparent icon. view: 'object' (3/4 from the front) or 'item' (tool laid diagonally)."""
    scene = bpy.context.scene
    lo, hi = bounds(objects)
    center = (lo + hi) / 2
    extent = hi - lo

    cam_data = bpy.data.cameras.new("icon_cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("icon_cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    if view == "item":
        # Items are modelled along +Z; lay them on the diagonal like classic inventory icons.
        pivot = bpy.data.objects.new("icon_pivot", None)
        scene.collection.objects.link(pivot)
        for obj in objects:
            if obj.parent is None:
                obj.parent = pivot
        pivot.rotation_euler = Euler((0, math.radians(-45), 0))
        bpy.context.view_layer.update()
        lo, hi = bounds(objects)
        center, extent = (lo + hi) / 2, hi - lo
        direction = Vector((0.0, -1.0, 0.05)).normalized()
    else:
        direction = Vector((0.55, 1.0, 0.55)).normalized()  # front (+Y) right-above
    distance = max(extent.length, 1.0) * 3
    cam.location = center + direction * distance
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    # Fit the orthographic frame to the projected bounds.
    inv = cam.matrix_world.inverted()
    xs, ys = [], []
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            v = inv @ (obj.matrix_world @ Vector(corner))
            xs.append(v.x)
            ys.append(v.y)
    span = max(max(xs) - min(xs), max(ys) - min(ys)) * 1.08
    cam_data.ortho_scale = max(span, 0.05)
    cam.location += cam.matrix_world.to_3x3() @ Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
    cam_data.clip_end = distance * 4

    key = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
    key.data.energy = 3.5
    key.rotation_euler = Euler((math.radians(50), math.radians(10), math.radians(35)))
    fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", "SUN"))
    fill.data.energy = 1.2
    fill.rotation_euler = Euler((math.radians(-60), 0, math.radians(-140)))
    scene.collection.objects.link(key)
    scene.collection.objects.link(fill)
    if scene.world is None:
        scene.world = bpy.data.worlds.new("icon_world")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.55, 0.58, 0.62, 1)
    bg.inputs["Strength"].default_value = 0.8

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    # Terraria-bright icons: the median of a lit item lands near its albedo instead of ~60 % of it.
    scene.view_settings.exposure = ICON_EXPOSURE
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".new.png")
    scene.render.filepath = str(temp)
    bpy.ops.render.render(write_still=True)
    _replace_if_changed(temp, path)


def _pixels(path):
    img = bpy.data.images.load(str(path), check_existing=False)
    try:
        return tuple(img.size), list(img.pixels)
    finally:
        bpy.data.images.remove(img)


def _replace_if_changed(temp, path):
    """Keeps the existing icon when the pixels are identical (PNG metadata differs between runs)."""
    if path.exists() and _pixels(temp) == _pixels(path):
        temp.unlink()
    else:
        temp.replace(path)
