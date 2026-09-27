"""Creatures: gels, floating eyes and bats, plus the rigid-skinning helpers other creature modules use (people are
built by bgen/human). Every generator returns the list of objects to export (armature first when rigged). Front faces
Blender +Y."""
import math

import bpy
from mathutils import Matrix, Vector

from .mesh import MeshBuilder

FPS = 24


def _armature(name, bones):
    data = bpy.data.armatures.new(name + "_rig")
    arm = bpy.data.objects.new(name + "_rig", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bone, (head, tail, parent) in bones.items():
        eb = data.edit_bones.new(bone)
        eb.head, eb.tail = Vector(head), Vector(tail)
        eb.roll = 0.0
    for bone, (_, _, parent) in bones.items():
        if parent:
            data.edit_bones[bone].parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def _bind(mesh_obj, arm):
    mesh_obj.parent = arm
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm


def _action(arm, name, length_s, keys):
    """keys: {time_fraction: {bone: (rx, ry, rz)}}; every bone used anywhere is keyed at every listed time."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    used = sorted({b for pose in keys.values() for b in pose})
    for t, pose in sorted(keys.items()):
        frame = 1 + round(t * length_s * FPS)
        for bone in used:
            pb = arm.pose.bones[bone]
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = pose.get(bone, (0.0, 0.0, 0.0))
            pb.keyframe_insert("rotation_euler", frame=frame)
    track = arm.animation_data.nla_tracks.new()
    track.name = name
    track.strips.new(name, 1, action)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_euler = (0.0, 0.0, 0.0)


def gel(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0.42), 0.5, "prop:gel", "#ffffff", subdivisions=3, scale=(1, 1, 0.8), jitter=0.02, rng=rng)
    for sx in (-1, 1):
        mb.sphere((sx * 0.15, 0.4, 0.55), 0.07, "prop:plain", "#101018", subdivisions=1)
        mb.sphere((sx * 0.15 + 0.02, 0.45, 0.57), 0.02, "prop:plain", "#ffffff", subdivisions=1)
    mb.sphere((0.05, 0.05, 0.25), 0.12, "prop:gel", "#f0f0f0", subdivisions=1)  # something swallowed
    return None


def eye(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0), 0.5, "prop:plain", "#f4efe8", subdivisions=3)
    # The iris glows (demon eyes burn red in the dark) unless "glow" is false.
    iris = "prop:plain:glow" if p.get("glow", True) else "prop:plain"
    mb.sphere((0, 0.42, 0), 0.2, iris, p.get("iris", "#e0201a"), subdivisions=2, scale=(1, 0.45, 1))
    mb.sphere((0, 0.5, 0), 0.09, "prop:plain", "#050505", subdivisions=1, scale=(1, 0.4, 1))
    for k in range(5):
        a = k * 2 * math.pi / 5 + rng.random() * 0.4
        base = Vector((math.cos(a) * 0.25, -0.35, math.sin(a) * 0.25))
        mb.tube([base, base + Vector((math.cos(a) * 0.15, -0.35, math.sin(a) * 0.15)), base + Vector((math.cos(a) * 0.1, -0.75, math.sin(a) * 0.1 - 0.1))],
                [0.06, 0.04, 0.01], "prop:flesh", "#c05050", segments=5)
    return None


BAT_BONES = {
    "body": ((0, -0.1, 0), (0, 0.15, 0), None),
    "wing.R": ((0.06, 0, 0.02), (0.45, 0, 0.05), "body"),
    "wing.L": ((-0.06, 0, 0.02), (-0.45, 0, 0.05), "body"),
}


def _bat_actions(arm):
    _action(arm, "fly", 0.35, {0.0: {"wing.R": (0, 0.9, 0), "wing.L": (0, -0.9, 0)}, 0.5: {"wing.R": (0, -0.7, 0), "wing.L": (0, 0.7, 0)},
                               1.0: {"wing.R": (0, 0.9, 0), "wing.L": (0, -0.9, 0)}})


def bat(mb: MeshBuilder, p, rng):
    fur = p.get("fur", "#4a3a3a")
    mb.bone = "body"
    mb.sphere((0, 0, 0), 0.13, "prop:cloth", fur, subdivisions=2, scale=(0.9, 1.2, 0.9))
    mb.sphere((0, 0.15, 0.05), 0.09, "prop:cloth", fur, subdivisions=1)
    for sx in (-1, 1):
        mb.cylinder((sx * 0.05, 0.15, 0.12), (sx * 0.07, 0.17, 0.22), 0.03, 0.0, "prop:cloth", fur, segments=4)
        mb.sphere((sx * 0.04, 0.23, 0.07), 0.018, "prop:plain:glow", "#ff4020", subdivisions=1)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        mb.extrude_polygon([(sx * 0.06, 0.02), (sx * 0.5, 0.1), (sx * 0.55, -0.05), (sx * 0.42, -0.1), (sx * 0.3, -0.04), (sx * 0.18, -0.1)],
                           0.01, "prop:cloth", p.get("wing", "#3a2a2a"), matrix=Matrix.Rotation(math.pi / 2, 4, "X"))
    mb.bone = None
    return {"rig": BAT_BONES, "actions": _bat_actions}


GENERATORS = {"gel": gel, "eye": eye, "bat": bat}


def finish(mesh_obj, rig_info, name):
    """Adds the armature (if any) and its actions; returns the objects to export (armature first)."""
    if not rig_info:
        return [mesh_obj]
    arm = _armature(name, rig_info["rig"])
    _bind(mesh_obj, arm)
    rig_info["actions"](arm)
    return [arm, mesh_obj]
