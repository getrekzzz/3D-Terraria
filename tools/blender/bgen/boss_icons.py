"""Boss head icons for the boss bar, rendered from the boss models the game uses (assets/models/creatures/*.glb are
imported as they are, never rebuilt). Each non-minor boss gets assets/icons/bosses/<id>.png: a front view of the
top of the model (its head), posed on the first frame of its idle action when it has one."""
import pathlib
import re

import bpy

from . import export
from .materials import PropMaterials

# Share of the model's height (from the top) that frames the head; bosses whose head is a small part of a tall body
# get a tighter crop, whole-body bosses (slimes, the Eye, worm heads) a wider one.
HEAD_SHARE = {
    "king_slime": 1.0, "queen_slime": 1.0, "eye_of_cthulhu": 1.0, "brain_of_cthulhu": 1.0, "retinazer": 1.0,
    "spazmatism": 1.0, "eater_of_worlds": 1.0, "the_destroyer": 1.0, "skeletron": 1.0, "skeletron_prime": 1.0,
    "wall_of_flesh": 1.0, "moon_lord_head": 1.0, "moon_lord_hand": 1.0, "martian_saucer": 1.0, "flying_dutchman": 0.8,
    "moon_lord": 0.3, "golem": 0.45, "deerclops": 0.45, "lunatic_cultist": 0.2, "dark_mage": 0.2, "ogre": 0.25,
    "santa_nk1": 0.45, "ice_queen": 0.3, "pumpking": 0.5, "mourning_wood": 0.5, "everscream": 0.5,
}

# Extra turn (radians about Z) for models whose face does not point at the front camera.
YAW = {}


def render_boss_icons(repo: pathlib.Path, bosses: dict, wanted, samples=16):
    """bosses: boss id -> model name. Renders every wanted one; returns the written icon paths."""
    from .mesh import reset_scene
    written = []
    for bid, model in sorted(bosses.items()):
        if not wanted(bid):
            continue
        reset_scene()
        bpy.ops.import_scene.gltf(filepath=str(repo / "assets" / "models" / "creatures" / f"{model}.glb"))
        objs = list(bpy.context.scene.objects)
        _pose_idle(objs)
        # The glTF importer adds an icosphere as the bones' display shape: keep it out of the frame and the render.
        shapes = {pb.custom_shape for o in objs if o.type == "ARMATURE" for pb in o.pose.bones if pb.custom_shape}
        for shape in shapes:
            shape.hide_render = True
        meshes = [o for o in objs if o.type == "MESH" and o not in shapes]
        _texture(repo, meshes)
        rel = f"assets/icons/bosses/{bid}.png"
        for obj in objs:  # turn bosses that face along X toward the camera
            if obj.parent is None:
                obj.rotation_euler.z += YAW.get(bid, 0.0)
        export.render_icon(meshes, repo / rel, "boss", size=64, samples=samples, head_share=HEAD_SHARE.get(bid, 0.6))
        written.append(rel)
        print(f"boss icon {bid}: {rel}")
    return written


def _pose_idle(objs):
    for arm in (o for o in objs if o.type == "ARMATURE"):
        action = next((a for a in bpy.data.actions if a.name.split("|")[-1].lower().startswith("idle")), None)
        if action is not None:
            arm.animation_data_create()
            arm.animation_data.action = action
            if action.slots:
                arm.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()


def _texture(repo, meshes):
    """Swaps the imported vertex-colour materials for the textured icon ones (names carry the prop slot; importer
    suffixes like '.001' are dropped). Character slots keep their imported vertex-colour look."""
    props = PropMaterials(repo)
    for obj in meshes:
        for i, mat in enumerate(obj.data.materials):
            if mat is None:
                continue
            slot = re.sub(r"\.\d+$", "", mat.name)
            try:
                obj.data.materials[i] = props.material(slot)
            except (KeyError, ValueError):
                pass
