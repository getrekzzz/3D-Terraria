"""The walking dead in bone, sculpted as distance fields (skulls, skeltorso, skelgirdle, skellimbs) and meshed and
cached with fieldmesh: the skull with its hinged jaw, the vertebral column, the ribcage and sternum, the pelvis, and
each side's clavicle, scapula, humerus, forearm, hand, femur, shin and foot (right side sculpted, left mirrored).

Every piece is bound to the rig so that bones meet where they meet in life and stay together when the human rig's
actions move them: rigid pieces turn about joints their rounded ends are centred on, the column bends through its
discs (its weights blend between hips, spine, chest, neck and head level by level, the ribcage riding on the chest),
the clavicle and scapula slide from the sternum and back with the shoulder, hands and feet bend at their knuckles.
"""
import bpy
import numpy as np

from . import fieldmesh, skulls
from .bonefield import tidy
from .head import smooth
from .paint import hex3
from .rig import chain_weights, hand_bones, rigid, write_weights
from .skelgirdle import clavicle_field, pelvis_field, scapula_field
from .skellimbs import femur_field, foot_field, forearm_field, hand_field, humerus_field, metatarsal_heads, shin_field
from .skeltorso import Column, column_field, ribcage_field

# Right-side pieces: name -> (field builder, module, sample spacing (m at scale 1), triangles, rig bone).
LIMBS = {
    "clavicle": (clavicle_field, "skelgirdle.py", 0.0009, 900, None),
    "scapula": (scapula_field, "skelgirdle.py", 0.0009, 2200, None),
    "humerus": (humerus_field, "skellimbs.py", 0.001, 1800, "upper_arm"),
    "forearm": (forearm_field, "skellimbs.py", 0.0009, 2400, "forearm"),
    "hand": (hand_field, "skellimbs.py", 0.0006, 5200, None),
    "femur": (femur_field, "skellimbs.py", 0.0011, 2400, "thigh"),
    "shin": (shin_field, "skellimbs.py", 0.0011, 2600, "shin"),
    "foot": (foot_field, "skellimbs.py", 0.0007, 4200, None),
}
SOURCES = ("bonefield.py", "frame.py", "hands.py", "skeltorso.py")


def _mesh(name, fb, module, h, target, key, s):
    field, bounds = fb[0], fb[1]
    bounds = tuple(tuple(round(float(v), 5) for v in b) for b in bounds)
    return tidy(fieldmesh.build(field, bounds, h * s, target, name, (key, name), SOURCES + (module,), "skeleton",
                                min_piece=60))


def _mirror(obj, name):
    me = obj.data.copy()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    co[:, 0] *= -1
    me.vertices.foreach_set("co", co.ravel())
    me.flip_normals()
    me.update()
    out = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(out)
    return out


def _coords(obj):
    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float32)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3).astype(float)


def axial_weights(co, frame, col):
    """Hips, spine, chest, neck and head shares by height up the column: each bone takes over across a band of
    discs (the chest from the eleventh thoracic up, so the ribs' vertebrae ride with the ribcage)."""
    s = frame.s
    t12, t10 = col.thoracic(12)[1][2], col.thoracic(10)[1][2]
    c2 = col.levels[-1][1][2]
    bands = ((1.045 * s, 1.085 * s), (t12, t10), (1.432 * s, 1.47 * s), (c2 + 0.004 * s, col.atlas[2] - 0.006 * s))
    out = []
    for z in co[:, 2]:
        t = [smooth(a, b, z) for a, b in bands]
        w = {"hips": 1 - t[0], "spine": t[0] * (1 - t[1]), "chest": t[0] * t[1] * (1 - t[2]),
             "neck": t[0] * t[1] * t[2] * (1 - t[3]), "head": t[0] * t[1] * t[2] * t[3]}
        out.append({b: x for b, x in w.items() if x > 1e-3})
    return out


def _limb_weights(name, co, frame):
    """Per-vertex weights of a right-side piece (bone names with .R)."""
    s = frame.s
    j = frame.j
    if name == "clavicle":
        a, b = np.asarray(j["clavicle.R"]), np.asarray(j["shoulder.R"])
        t = (co - a) @ (b - a) / ((b - a) @ (b - a))
        return [{"chest": 1 - k, "clavicle.R": k} for k in (smooth(0.05, 0.55, x) for x in t)]
    if name == "scapula":
        return [{"chest": 1 - k, "clavicle.R": k} for k in (smooth(0.07 * s, 0.12 * s, x) for x in co[:, 0])]
    if name == "foot":
        heads = metatarsal_heads(frame)
        h0, h4 = heads[0][0], heads[4][0]
        fwd = np.asarray(j["toe.R"]) - np.asarray(j["ankle.R"])
        fwd[2] = 0
        fwd /= np.linalg.norm(fwd)
        span = h4 - h0
        t = np.clip((co - h0) @ span / (span @ span), -0.3, 1.3)
        ahead = np.einsum("ij,j->i", co - (h0 + np.outer(t, span)), fwd)
        return [{"foot.R": 1 - k, "toe.R": k} for k in (smooth(-0.002 * s, 0.006 * s, x) for x in ahead)]
    return None


def build_skeleton(frame, head, arm, hide=()):
    """The bone meshes, weighted to ``arm``, and the skull's helper (for painting). ``hide`` names right-side pieces to
    leave out on both sides (feet inside boots)."""
    s = frame.s
    key = ("bones", frame.female, frame.old, round(frame.height, 4))
    parts = []
    skull_obj, skull = skulls.mesh(head, 1.0 if frame.female else 0.0)
    rigid(skull_obj, "head")
    parts.append(skull_obj)
    col_fb = column_field(frame)
    col = col_fb[2]
    spine = _mesh("spine", col_fb, "skeltorso.py", 0.001, 9000, key, s)
    write_weights(spine, axial_weights(_coords(spine), frame, col))
    ribcage = _mesh("ribcage", ribcage_field(frame, col), "skeltorso.py", 0.001, 12000, key, s)
    rigid(ribcage, "chest")
    pelvis = _mesh("pelvis", pelvis_field(frame, col), "skelgirdle.py", 0.001, 7000, key, s)
    rigid(pelvis, "hips")
    parts += [spine, ribcage, pelvis]
    for name, (builder, module, h, target, bone) in LIMBS.items():
        if name in hide:
            continue
        right = _mesh(f"{name}_R", builder(frame), module, h, target, key, s)
        left = _mirror(right, f"{name}_L")
        for obj, side in ((right, "R"), (left, "L")):
            if name == "hand":
                chain_weights(obj, arm, [f"hand.{side}"] + hand_bones(side)[2:], 0.003 * s)
            elif bone is not None:
                rigid(obj, f"{bone}.{side}")
            else:
                w = _limb_weights(name, _coords(right), frame)
                write_weights(obj, [{b.replace(".R", f".{side}"): x for b, x in d.items() if x > 1e-3} for d in w])
        parts += [right, left]
    return parts, skull


def paint(parts, skull, head, color):
    """Aged bone in ``color``: mottled toward brown, the skull's orbits and nasal cavity dark inside, its sutures
    drawn in and the teeth a little paler."""
    base = np.asarray(hex3(color))
    brown = base * np.asarray((0.62, 0.52, 0.4))
    for o in parts:
        co = _coords(o)
        n = 0.5 + 0.25 * np.sin(co[:, 2] * 21 + co[:, 0] * 17 + 0.6) + 0.25 * np.sin(co[:, 1] * 27 - co[:, 2] * 9 + 1.9)
        n = n * 0.8 + 0.2 * (0.5 + 0.5 * np.sin(co[:, 0] * 61 + co[:, 2] * 47) * np.sin(co[:, 1] * 53 - co[:, 2] * 29))
        rgb = base + (brown - base) * (0.06 + 0.3 * n * n)[:, None]
        if o.name.startswith("skull"):
            q = (co - np.asarray(head.c)) / head.h
            t = skull.teeth(q)[:, None]
            rgb = rgb * (1 - t) + np.minimum(base * np.asarray((1.07, 1.05, 0.98)), 1.0) * t
            rgb = rgb * skull.shade(q)[:, None]
        me = o.data
        if "Color" not in me.color_attributes:
            me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
        layer = me.color_attributes["Color"]
        me.color_attributes.active_color = layer
        idx = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get("vertex_index", idx)
        rgba = np.concatenate([np.clip(rgb[idx], 0, 1), np.ones((len(idx), 1))], 1).astype(np.float32)
        layer.data.foreach_set("color_srgb", rgba.ravel())
