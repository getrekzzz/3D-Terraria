"""Skinning and pose authoring for generated humans.

Skinning: the body gets Blender's bone-heat weights; the head blends from the body's neck weights to the head bone;
hands use chain weights (nearest bone segment, blended with its parent/children across each knuckle); rigid parts
(eyes, nails, hats) are bound to one bone; hair takes the head's weights, so what hangs past the neck follows the body.

Poses are authored in armature space: ``swing(bone, toward, angle)`` turns a bone's rest direction toward a world
direction, ``twist`` turns it about its rest direction, ``roll`` about the direction it points after the ops before it, ``axis`` about an arbitrary axis, ``local`` about one of the bone's own
rest axes. Rotations are converted to each bone's
local rest frame, so they compose naturally down the chain (a bent elbow stays bent when the shoulder moves).
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.kdtree import KDTree

from .frame import FINGERS
from .head import smooth


# ---------------------------------------------------------------------------------------------------- skinning
def heat_weights(obj, arm, drop=()):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    obj.parent = None
    obj.modifiers.clear()
    for name in drop:
        g = obj.vertex_groups.get(name)
        if g is not None:
            obj.vertex_groups.remove(g)
    normalize(obj)


def clean_limb_weights(obj, arm, frame):
    """Heat weights leak between close surfaces in the rest pose: keep arm bones off the torso's flanks (a web forms
    when the arms come down) and each leg's bones off the other leg."""
    s = frame.s

    def seg_dist(p, bone):
        b = arm.data.bones[bone]
        a, t = b.head_local, b.tail_local
        ab = t - a
        u = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        return (p - (a + ab * u)).length

    weights = read_weights(obj)
    for v, w in zip(obj.data.vertices, weights):
        for side, sx in (("R", 1), ("L", -1)):
            limb = [f"upper_arm.{side}", f"forearm.{side}", f"hand.{side}"]
            if any(b in w for b in limb):
                if min(seg_dist(v.co, b) for b in limb) > 0.068 * s:
                    for b in limb:
                        w.pop(b, None)
            if v.co.x * sx < -0.012 * s:          # the other side of the body
                for b in (f"thigh.{side}", f"shin.{side}", f"foot.{side}", f"toe.{side}") + tuple(limb):
                    w.pop(b, None)
        if not w:
            w["spine"] = 1.0
    write_weights(obj, weights)


def read_weights(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    return [{names[g.group]: g.weight for g in v.groups if g.weight > 1e-4} for v in obj.data.vertices]


def write_weights(obj, weights):
    """Gives vertex i exactly the weights ``weights[i]`` (normalised): whatever it carried before goes, including
    deform data a copy of another mesh brought along (clearing the groups drops it)."""
    obj.vertex_groups.clear()
    groups = {name: obj.vertex_groups.new(name=name) for name in dict.fromkeys(n for w in weights for n in w)}
    for i, w in enumerate(weights):
        total = sum(w.values()) or 1.0
        for name, value in w.items():
            groups[name].add([i], value / total, "REPLACE")


def normalize(obj):
    write_weights(obj, read_weights(obj))


def rigid(obj, bone):
    write_weights(obj, [{bone: 1.0} for _ in obj.data.vertices])


def body_lookup(body):
    tree = KDTree(len(body.data.vertices))
    for i, v in enumerate(body.data.vertices):
        tree.insert(v.co, i)
    tree.balance()
    return tree, read_weights(body)


ARM_BONES = ("upper_arm", "forearm", "hand", "thumb", "index", "middle", "ring", "pinky")


def head_weights(obj, head, body_tree, body_w, torso_only=False):
    """Skull and face follow the head; the neck blends from the body's neck weights up into the head. With
    ``torso_only`` (hair lying on the shoulders) the arms' share is left out, so raising an arm doesn't drag it."""
    out = []
    for v in obj.data.vertices:
        q = head.local(v.co)
        t = smooth(-0.15, -0.075, q.z) if q.y < 0.03 else smooth(-0.16, -0.1, q.z)
        _, idx, _ = body_tree.find(v.co)
        body = body_w[idx]
        if torso_only:
            body = {k: x for k, x in body.items() if k.split(".")[0] not in ARM_BONES} or {"chest": 1.0}
            total = sum(body.values())
            body = {k: x / total for k, x in body.items()}
        w = {k: x * (1 - t) for k, x in body.items()}
        w["head"] = w.get("head", 0.0) + t
        out.append(w)
    write_weights(obj, out)


def chain_weights(obj, arm, bones, sigma, body_tree=None, body_w=None, blend_bone=None):
    """Nearest bone segment among ``bones`` blended with its parent/children by distance difference; vertices behind
    ``blend_bone``'s head take the body's weights (so a hand meets the forearm seamlessly)."""
    segs = {b: (arm.data.bones[b].head_local, arm.data.bones[b].tail_local) for b in bones}
    rel = {b: set() for b in bones}
    for b in bones:
        p = arm.data.bones[b].parent
        if p is not None and p.name in rel:
            rel[b].add(p.name)
            rel[p.name].add(b)
    out = []
    for v in obj.data.vertices:
        d = {}
        for b, (a, t) in segs.items():
            ab = t - a
            u = max(0.0, min(1.0, (v.co - a).dot(ab) / max(ab.length_squared, 1e-12)))
            d[b] = (v.co - (a + ab * u)).length
        b0 = min(d, key=d.get)
        w = {b: math.exp(-((d[b] - d[b0]) / sigma) ** 2) for b in rel[b0] | {b0}}
        if blend_bone is not None and body_tree is not None:
            head = arm.data.bones[blend_bone].head_local
            axis = (arm.data.bones[blend_bone].tail_local - head).normalized()
            behind = -(v.co - head).dot(axis)
            k = smooth(-0.005, 0.02, behind)
            if k > 0:
                _, idx, _ = body_tree.find(v.co)
                w = {b: x * (1 - k) for b, x in w.items()}
                for b, x in body_w[idx].items():
                    w[b] = w.get(b, 0.0) + x * k
        out.append({b: x for b, x in w.items() if x > 0.01})
    write_weights(obj, out)


def hand_bones(side):
    bones = [f"forearm.{side}", f"hand.{side}"]
    for f in FINGERS:
        bones += [f"{f}.0{k}.{side}" for k in (1, 2, 3)]
    return bones


# ---------------------------------------------------------------------------------------------------- posing
class Poser:
    """Converts armature-space rotation recipes into local bone quaternions."""

    def __init__(self, arm):
        self.arm = arm
        self.rest = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}
        self.dir = {b.name: (b.tail_local - b.head_local).normalized() for b in arm.data.bones}

    def rotation(self, bone, ops) -> Quaternion:
        q = Quaternion()
        for op in ops:
            kind = op[0]
            if kind == "swing":
                toward, angle = Vector(op[1]).normalized(), op[2]
                current = q @ self.dir[bone]          # swings compose from where the bone already points
                axis = current.cross(toward)
                if axis.length < 1e-6:
                    continue
                r = Quaternion(axis.normalized(), angle)
            elif kind == "twist":
                r = Quaternion(self.dir[bone], op[1])
            elif kind == "roll":                  # about where the bone points now (an upper arm's inward rotation)
                r = Quaternion((q @ self.dir[bone]).normalized(), op[1])
            elif kind == "axis":
                r = Quaternion(Vector(op[1]).normalized(), op[2])
            elif kind == "local":                 # about one of the bone's own rest axes (e.g. X flexes a wrist)
                r = Quaternion((self.rest[bone] @ Vector(op[1])).normalized(), op[2])
            else:
                continue
            q = r @ q
        rest = self.rest[bone]
        return (rest.inverted() @ q.to_matrix() @ rest).to_quaternion()

    def location(self, bone, offset) -> Vector:
        return self.rest[bone].inverted() @ Vector(offset)


def keyframe_action(arm, poser, name, length_s, keys, fps=24):
    """keys: {time_fraction: {bone: [ops...] | {"rot": [...], "loc": (x, y, z)}}}. Bones used anywhere are keyed
    at every listed time (unlisted = rest)."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    used = sorted({b for pose in keys.values() for b in pose})
    moved = sorted({b for pose in keys.values() for b, spec in pose.items() if isinstance(spec, dict) and "loc" in spec})
    for t, pose in sorted(keys.items()):
        frame = round(t * length_s * fps)
        for bone in used:
            pb = arm.pose.bones[bone]
            pb.rotation_mode = "QUATERNION"
            spec = pose.get(bone, [])
            ops = spec.get("rot", []) if isinstance(spec, dict) else spec
            pb.rotation_quaternion = poser.rotation(bone, ops)
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            if bone in moved:
                loc = spec.get("loc", (0, 0, 0)) if isinstance(spec, dict) else (0, 0, 0)
                pb.location = poser.location(bone, loc)
                pb.keyframe_insert("location", frame=frame)
    track = arm.animation_data.nla_tracks.new()
    track.name = name
    track.strips.new(name, 1, action)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
