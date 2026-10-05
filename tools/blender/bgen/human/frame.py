"""Human proportions and skeleton: joint positions from body parameters and the armature built from them.

Frame: Blender Z up, 1 unit = 1 m, the character faces +Y and its right side is +X (see bgen/creatures.py).
``legs`` (1) scales the legs' length, bringing the rest of the body down with them (a goblin's short legs).

The rest pose is an A-pose (arms 40 degrees from the body) so skinning separates arms from the torso; actions pose
the arms down. Bone names keep the old humanoid rig's names (hips, chest, head, upper_arm.R, forearm.R, hand.R,
thigh.R, shin.R, foot.R) so the game's attachment points (hand.R holds items) are unchanged.
"""
import math

import bpy
from mathutils import Matrix, Vector

FINGERS = ("thumb", "index", "middle", "ring", "pinky")


class Frame:
    """Joint positions for one body. ``p`` holds the character parameters (see human/__init__.py)."""

    def __init__(self, p: dict):
        female = p.get("body") == "female"
        self.female = female
        self.old = p.get("age") == "old"
        self.height = p.get("height", 1.66 if female else 1.78)
        s = self.s = self.height / 1.78
        self.build = p.get("build", 0.5)
        self.weight = p.get("weight", 0.45)
        shoulder_w = (0.162 if female else 0.182) * s
        hip_w = (0.092 if female else 0.088) * s
        stoop = self.stoop = 0.035 * s if self.old else 0.0     # rounded shoulders and a forward head
        self.j = j = {}
        j["ground"] = Vector((0, 0, 0))
        j["pelvis"] = Vector((0, -0.005 * s, 0.975 * s))
        j["spine"] = Vector((0, -0.012 * s, 1.07 * s))
        j["chest"] = Vector((0, -0.02 * s + stoop * 0.3, 1.24 * s))
        j["neck"] = Vector((0, -0.032 * s + stoop, 1.455 * s))
        j["head"] = Vector((0, -0.012 * s + stoop * 1.4, 1.575 * s))
        j["crown"] = Vector((0, -0.008 * s + stoop * 1.5, self.height - 0.012 * s))
        # Head centre (skull mass) and the face: eyes sit at mid-head height.
        self.head_center = j["head"] + Vector((0, 0.01 * s, 0.078 * s))
        for side, sx in (("R", 1), ("L", -1)):
            j[f"clavicle.{side}"] = Vector((sx * 0.022 * s, 0.018 * s + stoop, 1.44 * s))
            sh = Vector((sx * shoulder_w, -0.018 * s + stoop, 1.408 * s))
            j[f"shoulder.{side}"] = sh
            a = math.radians(40)
            down = Vector((sx * math.sin(a), 0, -math.cos(a)))
            elbow = sh + down * 0.298 * s
            fore = (down + Vector((0, 0.09, 0))).normalized()
            wrist = elbow + fore * 0.252 * s
            j[f"elbow.{side}"] = elbow
            j[f"wrist.{side}"] = wrist
            j[f"hand_end.{side}"] = wrist + fore * 0.1 * s
            self.arm_dir = {**getattr(self, "arm_dir", {}), side: fore}
            hip = Vector((sx * hip_w, 0.0, 0.935 * s))
            knee = Vector((sx * (hip_w + 0.006 * s), 0.012 * s, 0.505 * s))
            ankle = Vector((sx * (hip_w + 0.01 * s), -0.018 * s, 0.078 * s))
            j[f"hip.{side}"] = hip
            j[f"knee.{side}"] = knee
            j[f"ankle.{side}"] = ankle
            j[f"ball.{side}"] = ankle + Vector((sx * 0.012 * s, 0.14 * s, -0.056 * s))
            j[f"toe.{side}"] = ankle + Vector((sx * 0.016 * s, 0.205 * s, -0.062 * s))
            j[f"heel.{side}"] = ankle + Vector((0, -0.055 * s, -0.06 * s))
            self._hand(side, sx)
        legs = self.legs = p.get("legs", 1.0)       # shorter legs: everything from the hips up comes down with them
        drop = self.drop = (1.0 - legs) * (0.935 - 0.078) * s
        if legs != 1.0:
            ankle_z = 0.078 * s
            for name, v in j.items():
                if name.startswith("knee."):
                    v.z = ankle_z + (v.z - ankle_z) * legs
                elif not name.startswith(("ankle.", "ball.", "toe.", "heel.", "ground")):
                    v.z -= drop
            self.head_center.z -= drop

    def _hand(self, side, sx):
        """Finger joints from the wrist: palm plane faces the body (thumb forward), fingers continue the forearm."""
        s = self.s * (0.94 if self.female else 1.0)
        j = self.j
        wrist = j[f"wrist.{side}"]
        along = self.arm_dir[side]
        fwd = Vector((0, 1, 0))
        fwd = (fwd - along * fwd.dot(along)).normalized()   # thumb side
        # Knuckles fan across the palm, from index (front) to pinky (back).
        spread = {"index": 0.031, "middle": 0.0095, "ring": -0.0115, "pinky": -0.031}
        palm = {"index": 0.094, "middle": 0.095, "ring": 0.09, "pinky": 0.082}
        seg = {"index": (0.042, 0.025, 0.02), "middle": (0.047, 0.029, 0.021), "ring": (0.044, 0.028, 0.02), "pinky": (0.034, 0.02, 0.018)}
        for f in ("index", "middle", "ring", "pinky"):
            base = wrist + along * palm[f] * s + fwd * spread[f] * s
            d = (along + fwd * spread[f] * 1.8).normalized()
            p = base
            for k, length in enumerate(seg[f]):
                j[f"{f}{k + 1}.{side}"] = p
                p = p + d * length * s
            j[f"{f}_tip.{side}"] = p
        # Thumb: from the base of the palm, angled forward and out.
        tb = wrist + along * 0.034 * s + fwd * 0.022 * s
        td = (along * 0.55 + fwd * 0.75 + Vector((sx * 0.1, 0, 0))).normalized()
        p = tb
        for k, length in enumerate((0.045, 0.034, 0.028)):
            j[f"thumb{k + 1}.{side}"] = p
            p = p + td * length * s
        j[f"thumb_tip.{side}"] = p

    def bones(self):
        """(name, head, tail, parent, roll_toward) for every bone; roll_toward aligns the bone's Z axis."""
        j = self.j
        fwd = Vector((0, 1, 0))
        out = [
            ("root", j["ground"], j["ground"] + Vector((0, 0, 0.2)), None, fwd),
            ("hips", j["pelvis"], j["spine"], "root", fwd),
            ("spine", j["spine"], j["chest"], "hips", fwd),
            ("chest", j["chest"], j["neck"], "spine", fwd),
            ("neck", j["neck"], j["head"], "chest", fwd),
            ("head", j["head"], j["crown"], "neck", fwd),
        ]
        for side in ("R", "L"):
            out += [
                (f"clavicle.{side}", j[f"clavicle.{side}"], j[f"shoulder.{side}"], "chest", fwd),
                (f"upper_arm.{side}", j[f"shoulder.{side}"], j[f"elbow.{side}"], f"clavicle.{side}", fwd),
                (f"forearm.{side}", j[f"elbow.{side}"], j[f"wrist.{side}"], f"upper_arm.{side}", fwd),
                (f"hand.{side}", j[f"wrist.{side}"], j[f"hand_end.{side}"], f"forearm.{side}", fwd),
                (f"thigh.{side}", j[f"hip.{side}"], j[f"knee.{side}"], "hips", fwd),
                (f"shin.{side}", j[f"knee.{side}"], j[f"ankle.{side}"], f"thigh.{side}", fwd),
                (f"foot.{side}", j[f"ankle.{side}"], j[f"ball.{side}"], f"shin.{side}", Vector((0, 0, 1))),
                (f"toe.{side}", j[f"ball.{side}"], j[f"toe.{side}"], f"foot.{side}", Vector((0, 0, 1))),
            ]
            for f in FINGERS:
                parent = f"hand.{side}"
                for k in (1, 2, 3):
                    tail = j[f"{f}{k + 1}.{side}"] if k < 3 else j[f"{f}_tip.{side}"]
                    name = f"{f}.0{k}.{side}"
                    out.append((name, j[f"{f}{k}.{side}"], tail, parent, fwd))
                    parent = name
        return out


def build_armature(frame: Frame, name: str) -> bpy.types.Object:
    data = bpy.data.armatures.new(name + "_rig")
    arm = bpy.data.objects.new(name + "_rig", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    specs = frame.bones()
    for bone, head, tail, _, roll in specs:
        eb = data.edit_bones.new(bone)
        eb.head, eb.tail = head, tail
        eb.align_roll(roll)
    for bone, _, _, parent, _ in specs:
        if parent:
            data.edit_bones[bone].parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def bone_matrix(arm: bpy.types.Object, bone: str) -> Matrix:
    """Rest orientation of a bone in armature space (3x3)."""
    return arm.data.bones[bone].matrix_local.to_3x3()
