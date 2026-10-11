"""Portraits from the existing creature models (assets/models/creatures/*.glb are imported as they are, never rebuilt):
town NPC head shots (assets/icons/npcs/<id>.png: housing banners, the Guide book) and creature thumbnails
(assets/icons/creatures/<id>.png: the bestiary). Shared with the boss-bar head icons (boss_icons.py): models are
posed on the first frame of their idle action and get a dark 2 px outline, the Terraria look."""
import pathlib
import re

import bpy
import numpy as np

from . import export
from .materials import PropMaterials

OUTLINE = (0.07, 0.06, 0.09)


def load_posed(repo: pathlib.Path, model: str, tint="#ffffff"):
    """Imports a creature model into the empty scene, posed; returns (all objects, the meshes to frame and render).
    ``tint`` multiplies the vertex colours like the game's per-enemy tint (slime colours share one gel model)."""
    from .mesh import reset_scene
    reset_scene()
    bpy.ops.import_scene.gltf(filepath=str(repo / "assets" / "models" / "creatures" / f"{model}.glb"))
    objs = list(bpy.context.scene.objects)
    for arm in (o for o in objs if o.type == "ARMATURE"):
        action = next((a for a in bpy.data.actions if a.name.split("|")[-1].lower().startswith("idle")), None)
        if action is not None:
            arm.animation_data_create()
            arm.animation_data.action = action
            if action.slots:
                arm.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    # The glTF importer adds an icosphere as the bones' display shape: keep it out of the frame and the render.
    shapes = {pb.custom_shape for o in objs if o.type == "ARMATURE" for pb in o.pose.bones if pb.custom_shape}
    for shape in shapes:
        shape.hide_render = True
    meshes = [o for o in objs if o.type == "MESH" and o not in shapes]
    if tint.lower() != "#ffffff":
        _tint(meshes, tint)
    _texture(repo, meshes)
    return objs, meshes


def neck_height(objs):
    """World height of a humanoid's neck (the bottom of its head shot), or None for other rigs."""
    for arm in (o for o in objs if o.type == "ARMATURE"):
        bone = arm.pose.bones.get("neck") or arm.pose.bones.get("head")
        if bone is not None:
            return (arm.matrix_world @ bone.head).z - 0.03
    return None


def outline(path: pathlib.Path, width=2):
    """Adds a dark outline around the opaque silhouette of an icon (in place)."""
    from PIL import Image
    img = Image.open(path).convert("RGBA")
    a = np.asarray(img).astype(np.float32)
    alpha = a[..., 3] / 255.0
    h, w = alpha.shape
    padded = np.pad(alpha, width)  # no wrap-around: a silhouette cut by the frame edge gets no ring on the far side
    grown = alpha.copy()
    for dy in range(-width, width + 1):
        for dx in range(-width, width + 1):
            if dx * dx + dy * dy <= width * width + 1:
                grown = np.maximum(grown, padded[width + dy:width + dy + h, width + dx:width + dx + w])
    ring = np.clip(grown - alpha, 0, 1)
    out = a.copy()
    rgb_out = np.array(OUTLINE) * 255
    total = alpha + ring * (1 - alpha)
    for c in range(3):
        out[..., c] = np.where(total > 0, (a[..., c] * alpha + rgb_out[c] * ring * (1 - alpha)) / np.maximum(total, 1e-6), 0)
    out[..., 3] = total * 255
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA").save(path)


def render_portraits(repo: pathlib.Path, npcs: dict, creatures: dict, wanted, samples=16, tints=None):
    """npcs / creatures: id -> model; tints: id -> hex. Town NPCs get 3/4 head shots, creatures whole-body
    thumbnails, 128 px."""
    written = []
    jobs = [("npcs", i, m) for i, m in sorted(npcs.items())] + [("creatures", i, m) for i, m in sorted(creatures.items())]
    for folder, cid, model in jobs:
        if not wanted(cid):
            continue
        objs, meshes = load_posed(repo, model, (tints or {}).get(cid, "#ffffff"))
        rel = f"assets/icons/{folder}/{cid}.png"
        if folder == "npcs":
            export.render_icon(meshes, repo / rel, "portrait", size=128, samples=samples, head_share=0.3, head_from_z=neck_height(objs))
        else:
            export.render_icon(meshes, repo / rel, "creature", size=128, samples=samples)
        outline(repo / rel)
        written.append(rel)
        print(f"portrait {cid}: {rel}")
    return written


def _tint(meshes, tint):
    h = tint.lstrip("#")
    k = np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)] + [1.0], np.float32)
    for obj in meshes:
        for attr in obj.data.color_attributes:
            buf = np.empty(len(attr.data) * 4, np.float32)
            attr.data.foreach_get("color", buf)
            attr.data.foreach_set("color", (buf.reshape(-1, 4) * k).ravel())


def _texture(repo, meshes):
    """Swaps the imported vertex-colour materials for the textured icon ones (names carry the prop slot; importer
    suffixes like '.001' are dropped). Character slots keep their imported vertex-colour look."""
    props = PropMaterials(repo)
    for obj in meshes:
        for i, mat in enumerate(obj.data.materials):
            if mat is None:
                continue
            try:
                obj.data.materials[i] = props.material(re.sub(r"\.\d+$", "", mat.name))
            except (KeyError, ValueError):
                pass
