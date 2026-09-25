"""Creatures: a rigidly skinned humanoid (player, undead) with keyframed actions, gels, floating eyes and bats.
Every generator returns the list of objects to export (armature first when rigged). Front faces Blender +Y."""
import math

import bpy
from mathutils import Matrix, Vector

from .mesh import MeshBuilder

FPS = 24

# name: (head, tail, parent). Character faces +Y, so its right side is +X.
HUMANOID_BONES = {
    "root": ((0, 0, 0), (0, 0, 0.2), None),
    "hips": ((0, 0, 0.95), (0, 0, 1.1), "root"),
    "chest": ((0, 0, 1.1), (0, 0, 1.45), "hips"),
    "head": ((0, 0, 1.45), (0, 0, 1.8), "chest"),
}
for side, sx in (("R", 1), ("L", -1)):
    HUMANOID_BONES.update({
        f"upper_arm.{side}": ((0.25 * sx, 0, 1.43), (0.26 * sx, 0, 1.16), "chest"),
        f"forearm.{side}": ((0.26 * sx, 0, 1.16), (0.27 * sx, 0, 0.92), f"upper_arm.{side}"),
        f"hand.{side}": ((0.27 * sx, 0, 0.92), (0.27 * sx, 0, 0.8), f"forearm.{side}"),
        f"thigh.{side}": ((0.1 * sx, 0, 0.93), (0.1 * sx, 0, 0.5), "hips"),
        f"shin.{side}": ((0.1 * sx, 0, 0.5), (0.1 * sx, 0, 0.1), f"thigh.{side}"),
        f"foot.{side}": ((0.1 * sx, 0, 0.1), (0.1 * sx, 0.14, 0.02), f"shin.{side}"),
    })


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


def _humanoid_actions(arm):
    def walk(amp, knee):
        return {
            0.0: {"thigh.R": (amp, 0, 0), "thigh.L": (-amp, 0, 0), "shin.L": (-knee, 0, 0), "upper_arm.R": (-amp * 0.8, 0, 0), "upper_arm.L": (amp * 0.8, 0, 0)},
            0.25: {"shin.R": (-knee * 0.3, 0, 0), "shin.L": (-knee * 0.3, 0, 0)},
            0.5: {"thigh.R": (-amp, 0, 0), "thigh.L": (amp, 0, 0), "shin.R": (-knee, 0, 0), "upper_arm.R": (amp * 0.8, 0, 0), "upper_arm.L": (-amp * 0.8, 0, 0)},
            0.75: {"shin.R": (-knee * 0.3, 0, 0), "shin.L": (-knee * 0.3, 0, 0)},
            1.0: {"thigh.R": (amp, 0, 0), "thigh.L": (-amp, 0, 0), "shin.L": (-knee, 0, 0), "upper_arm.R": (-amp * 0.8, 0, 0), "upper_arm.L": (amp * 0.8, 0, 0)},
        }
    _action(arm, "idle", 2.0, {0.0: {"chest": (0.0, 0, 0), "upper_arm.R": (0, 0, 0.06), "upper_arm.L": (0, 0, -0.06)},
                               0.5: {"chest": (0.04, 0, 0), "upper_arm.R": (0, 0, 0.1), "upper_arm.L": (0, 0, -0.1)},
                               1.0: {"chest": (0.0, 0, 0), "upper_arm.R": (0, 0, 0.06), "upper_arm.L": (0, 0, -0.06)}})
    _action(arm, "walk", 0.9, walk(0.55, 0.7))
    _action(arm, "run", 0.6, walk(0.9, 1.2))
    _action(arm, "jump", 0.3, {0.0: {"thigh.R": (0.6, 0, 0), "shin.R": (-1.0, 0, 0), "thigh.L": (-0.2, 0, 0), "upper_arm.R": (0.3, 0, 0.3), "upper_arm.L": (0.3, 0, -0.3)},
                               1.0: {"thigh.R": (0.6, 0, 0), "shin.R": (-1.0, 0, 0), "thigh.L": (-0.2, 0, 0), "upper_arm.R": (0.3, 0, 0.3), "upper_arm.L": (0.3, 0, -0.3)}})
    _action(arm, "fall", 0.3, {0.0: {"upper_arm.R": (0.2, 0, 0.9), "upper_arm.L": (0.2, 0, -0.9), "thigh.R": (0.2, 0, 0), "thigh.L": (-0.1, 0, 0)},
                               1.0: {"upper_arm.R": (0.2, 0, 0.9), "upper_arm.L": (0.2, 0, -0.9), "thigh.R": (0.2, 0, 0), "thigh.L": (-0.1, 0, 0)}})
    _action(arm, "swing", 0.4, {0.0: {"upper_arm.R": (2.7, 0, 0.2), "chest": (0, 0, 0.25)},
                                0.45: {"upper_arm.R": (0.5, 0, 0.1), "forearm.R": (0.2, 0, 0), "chest": (0.1, 0, -0.2)},
                                1.0: {"upper_arm.R": (0.3, 0, 0.05), "chest": (0, 0, 0)}})
    _action(arm, "use", 0.3, {0.0: {"upper_arm.R": (0.3, 0, 0)}, 0.4: {"upper_arm.R": (1.5, 0, 0), "forearm.R": (0.1, 0, 0)}, 1.0: {"upper_arm.R": (1.2, 0, 0)}})
    _action(arm, "hurt", 0.25, {0.0: {"chest": (0, 0, 0)}, 0.4: {"chest": (-0.3, 0, 0), "head": (-0.2, 0, 0)}, 1.0: {"chest": (0, 0, 0)}})
    _action(arm, "death", 0.8, {0.0: {"root": (0, 0, 0)}, 0.7: {"root": (-1.45, 0, 0), "upper_arm.R": (0, 0, 1.2), "upper_arm.L": (0, 0, -1.2)},
                                1.0: {"root": (-1.52, 0, 0), "upper_arm.R": (0, 0, 1.3), "upper_arm.L": (0, 0, -1.3)}})


def humanoid(mb: MeshBuilder, p, rng):
    skin, shirt, pants = p.get("skin", "#e8b890"), p.get("shirt", "#4a7ac0"), p.get("pants", "#3a4a6a")
    skin_mat = f"prop:{p.get('skinLayer', 'plain')}"
    cloth = "prop:cloth"
    mb.bone = "head"
    mb.box((-0.15, -0.15, 1.46), (0.15, 0.15, 1.78), skin_mat, skin)
    mb.box((-0.16, -0.17, 1.66), (0.16, 0.14, 1.82), "prop:cloth", p.get("hair", "#5a3a20"))
    for sx in (-1, 1):
        mb.box((sx * 0.07 - 0.025, 0.148, 1.6), (sx * 0.07 + 0.025, 0.16, 1.65), "prop:plain", p.get("eyes", "#202030"))
    mb.bone = "chest"
    mb.box((-0.19, -0.11, 1.12), (0.19, 0.11, 1.46), cloth, shirt)
    mb.bone = "hips"
    mb.box((-0.18, -0.1, 0.9), (0.18, 0.1, 1.12), cloth, pants)
    mb.box((-0.185, -0.105, 1.06), (0.185, 0.105, 1.12), "prop:dark_planks", "#ffffff")
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"upper_arm.{side}"
        mb.box((sx * 0.25 - 0.065, -0.065, 1.15), (sx * 0.25 + 0.065, 0.065, 1.45), cloth, shirt)
        mb.bone = f"forearm.{side}"
        mb.box((sx * 0.265 - 0.055, -0.055, 0.92), (sx * 0.265 + 0.055, 0.055, 1.17), skin_mat, skin)
        mb.bone = f"hand.{side}"
        mb.box((sx * 0.27 - 0.05, -0.05, 0.8), (sx * 0.27 + 0.05, 0.05, 0.93), skin_mat, skin)
        mb.bone = f"thigh.{side}"
        mb.box((sx * 0.1 - 0.08, -0.08, 0.5), (sx * 0.1 + 0.08, 0.08, 0.93), cloth, pants)
        mb.bone = f"shin.{side}"
        mb.box((sx * 0.1 - 0.07, -0.07, 0.1), (sx * 0.1 + 0.07, 0.07, 0.52), cloth, pants)
        mb.bone = f"foot.{side}"
        mb.box((sx * 0.1 - 0.075, -0.09, 0.0), (sx * 0.1 + 0.075, 0.16, 0.12), "prop:dark_planks", p.get("boots", "#ffffff"))
    _outfit(mb, p)
    mb.bone = None
    return {"rig": HUMANOID_BONES, "actions": _humanoid_actions}


def _outfit(mb: MeshBuilder, p):
    """Optional clothing for town folk: hat (cap/wide/hood/wreath), beard, long coat, apron."""
    hat, hat_color = p.get("hat"), p.get("hatColor", "#5a3a1a")
    mb.bone = "head"
    if hat == "cap":
        mb.box((-0.17, -0.18, 1.78), (0.17, 0.16, 1.86), "prop:cloth", hat_color)
        mb.box((-0.12, 0.14, 1.78), (0.12, 0.24, 1.8), "prop:cloth", hat_color)
    elif hat == "wide":
        mb.cylinder((0, 0, 1.78), (0, 0, 1.8), 0.3, None, "prop:cloth", hat_color, segments=16)
        mb.cylinder((0, 0, 1.8), (0, 0, 1.96), 0.15, 0.13, "prop:cloth", hat_color, segments=12)
    elif hat == "hood":
        mb.box((-0.18, -0.19, 1.5), (0.18, 0.12, 1.88), "prop:cloth", hat_color)
    elif hat == "wreath":
        ring = [(math.cos(a) * 0.17, math.sin(a) * 0.17, 1.8) for a in (i * 2 * math.pi / 14 for i in range(15))]
        mb.tube(ring, [0.025] * len(ring), "prop:leaves", hat_color, segments=5)
        for a in (0.4, 1.9, 3.3, 4.8):
            mb.sphere((math.cos(a) * 0.17, math.sin(a) * 0.17, 1.82), 0.035, "prop:plain", p.get("flower", "#f0d040"), subdivisions=1)
    if "beard" in p:
        mb.box((-0.13, 0.1, 1.42), (0.13, 0.19, 1.56), "prop:cloth", p["beard"])
    if "coat" in p:
        mb.bone = "chest"
        mb.box((-0.205, -0.125, 1.12), (0.205, 0.125, 1.47), "prop:cloth", p["coat"])
        mb.bone = "hips"
        mb.box((-0.2, -0.12, 0.62), (0.2, 0.12, 1.12), "prop:cloth", p["coat"])
    if "horns" in p:
        mb.bone = "head"
        for sx in (-1, 1):
            mb.tube([Vector((sx * 0.1, 0.0, 1.78)), Vector((sx * 0.16, -0.02, 1.9)), Vector((sx * 0.13, -0.08, 1.98))], [0.035, 0.025, 0.005], "prop:clay", p["horns"], segments=5)
    if "wings" in p:
        mb.bone = "chest"
        for sx in (-1, 1):
            mb.extrude_polygon([(sx * 0.05, 1.35), (sx * 0.55, 1.55), (sx * 0.6, 1.2), (sx * 0.4, 1.28), (sx * 0.3, 1.1), (sx * 0.1, 1.2)],
                               0.012, "prop:cloth", p["wings"], matrix=Matrix.Translation((0, -0.14, 0)))
    if "tail" in p:
        mb.bone = "hips"
        mb.tube([Vector((0, -0.1, 0.95)), Vector((0, -0.3, 0.8)), Vector((0, -0.42, 0.9)), Vector((0, -0.5, 1.05))], [0.03, 0.025, 0.02, 0.005], "prop:flesh", p["tail"], segments=5)
    if "apron" in p:
        mb.bone = "chest"
        mb.box((-0.14, 0.11, 1.12), (0.14, 0.125, 1.38), "prop:cloth", p["apron"])
        mb.bone = "hips"
        mb.box((-0.15, 0.1, 0.7), (0.15, 0.115, 1.12), "prop:cloth", p["apron"])


def gel(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0.42), 0.5, "prop:gel", "#ffffff", subdivisions=3, scale=(1, 1, 0.8), jitter=0.02, rng=rng)
    for sx in (-1, 1):
        mb.sphere((sx * 0.15, 0.4, 0.55), 0.07, "prop:plain", "#101018", subdivisions=1)
        mb.sphere((sx * 0.15 + 0.02, 0.45, 0.57), 0.02, "prop:plain", "#ffffff", subdivisions=1)
    mb.sphere((0.05, 0.05, 0.25), 0.12, "prop:gel", "#f0f0f0", subdivisions=1)  # something swallowed
    return None


def eye(mb: MeshBuilder, p, rng):
    mb.sphere((0, 0, 0), 0.5, "prop:plain", "#f4efe8", subdivisions=3)
    mb.sphere((0, 0.42, 0), 0.2, "prop:plain", p.get("iris", "#3a8a5a"), subdivisions=2, scale=(1, 0.45, 1))
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


GENERATORS = {"humanoid": humanoid, "gel": gel, "eye": eye, "bat": bat}


def finish(mesh_obj, rig_info, name):
    """Adds the armature (if any) and its actions; returns the objects to export (armature first)."""
    if not rig_info:
        return [mesh_obj]
    arm = _armature(name, rig_info["rig"])
    _bind(mesh_obj, arm)
    rig_info["actions"](arm)
    return [arm, mesh_obj]
