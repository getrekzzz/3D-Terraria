"""Detailed human characters (player, town folk, humanoid enemies), generated from parameters in assets.json.

Pipeline: proportions and skeleton (frame) -> body blob (body) and the head's distance field with sculpted features
and ears, plus eyeballs (head) -> hands with fingers and nails (hands) -> skin painting (paint) -> hair, brows and lashes (hair)
-> clothing and accessories (garments) -> skinning and actions (rig, motion). Everything is joined into one mesh
with ``char:<kind>`` material slots that the game maps to its character shader.

Parameters: body (male|female), age (young|adult|old), height, build (muscle 0..1), weight (0..1), skin, eyes,
hair, hairStyle, beard, shirt, pants, boots, coat, robe, trim, apron, hat (cap|wide|hood|wreath), hatColor, visor (a cap's
peak, default true), mask, horns, wings, tail, anatomy (human|skeleton), undead.
"""
import math

import bpy

from . import accessories, hair, locks, motion, occlusion, rig
from . import frame as frame_mod
from .garments import Wardrobe, strip_covered
from .blobs import Blob
from .body import build_body
from .hands import build_hand
from .head import Head
from .paint import SkinPainter, flat, paint


# Material slots the game's character shader understands (game/Rendering/PropLibrary.cs).
SLOTS = {"char:skin", "char:eye", "char:hair", "char:nail", "char:cloth", "char:leather", "char:metal", "char:bone", "char:glow"}


def validate_slots(objs):
    for o in objs:
        if o.type == "MESH":
            for m in o.data.materials:
                if m.name not in SLOTS:
                    raise KeyError(f"unknown character material slot '{m.name}' (known: {', '.join(sorted(SLOTS))})")


def set_material(obj: bpy.types.Object, slot: str):
    from ..export import simple_material
    obj.data.materials.clear()
    obj.data.materials.append(simple_material(slot))
    for poly in obj.data.polygons:
        poly.material_index = 0


def _parts(p: dict):
    fr = frame_mod.Frame(p)
    blob = Blob("body_blob", 0.0085 * fr.s)
    build_body(fr, blob)
    body = blob.to_mesh("body", 16000, smooth_iterations=2)
    head = Head(fr, p)
    head_obj = head.build()
    head.fit_neck(head_obj, body)
    eyes = head.eyeballs(p.get("eyes", "#5a7890"))
    hands, nails = [], []
    for side in ("R", "L"):
        hand, nail = build_hand(fr, side)
        hands.append(hand)
        nails.append(nail)
    painter = SkinPainter(fr, head, p)
    paint(body, painter.body)
    paint(head_obj, painter.face)
    for part in hands:
        paint(part, painter.body)
    for o in (body, head_obj, *hands):
        set_material(o, "char:skin")
    for o in nails:
        flat(o, p.get("nails", "#e8c4b8"))
        set_material(o, "char:nail")
    for o in eyes:
        set_material(o, "char:eye")
    parts = {"body": body, "head": head_obj, "hands": hands, "nails": nails, "eyes": eyes}
    return fr, head, parts


def _all(parts):
    out = []
    for v in parts.values():
        out += v if isinstance(v, list) else [v]
    return out


def build_preview(name: str, p: dict):
    """Unrigged parts for inspection renders."""
    _, head, parts = _parts(p)
    head.bury_neck(parts["body"])
    return _all(parts)


DEFAULTS = {"shirt": "#4a7ac0", "pants": "#3a4a6a", "boots": "#5a3c26", "hair": "#5a3a20", "eyes": "#5a7890"}


def motion_params(p: dict) -> dict:
    """The parameters a character's skeleton and actions are built from (as build() and build_skeleton() see them)."""
    p = {**DEFAULTS, **p}
    if p.get("undead"):
        p = {"build": 0.25, "weight": 0.15, **p}
    if p.get("anatomy") == "skeleton":
        p = {**p, "undead": True}
    return p


def motion_key(p: dict) -> str:
    """Characters with equal keys have identical skeletons and actions, so they share one animation set: body,
    stooped age, height and the way they move (people, shambling undead, walking skeletons)."""
    q = motion_params(p)
    fr = frame_mod.Frame(q)
    kind = "skeleton" if q.get("anatomy") == "skeleton" else "undead" if q.get("undead") else "human"
    return f"{'f' if fr.female else 'm'}{'_old' if fr.old else ''}_{round(fr.height * 100)}_{kind}"


def build_motion(p: dict):
    """A character's animation set: its armature (named ``anim_rig``, which the game gives every character's
    armature node) with every action, and a one-triangle mesh so the importer builds a skeleton."""
    import bmesh
    from .garments import mesh_from
    q = motion_params(p)
    fr = frame_mod.Frame(q)
    arm = frame_mod.build_armature(fr, "anim")
    bm = bmesh.new()
    verts = [bm.verts.new(v) for v in ((0, 0, 0), (0.01, 0, 0), (0, 0, 0.01))]
    bm.faces.new(verts)
    stub = mesh_from(bm, "anim_stub")
    set_material(stub, "char:skin")
    rig.rigid(stub, "root")
    stub.parent = arm
    mod = stub.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    motion.animate(arm, fr, q)
    return [arm, stub]


def build(name: str, p: dict):
    """The rigged, animated character: [armature, mesh]."""
    if p.get("anatomy") == "skeleton":
        return build_skeleton(name, p)
    p = {**DEFAULTS, **p}
    if p.get("undead"):
        p = {"build": 0.25, "weight": 0.15, **p}
    fr, head, parts = _parts(p)
    arm = frame_mod.build_armature(fr, name)
    body = parts["body"]
    fingers = [b.name for b in arm.data.bones if b.name.split(".")[0] in frame_mod.FINGERS]
    rig.heat_weights(body, arm, drop=fingers + ["root"])
    rig.clean_limb_weights(body, arm, fr)
    tree, body_w = rig.body_lookup(body)
    rig.head_weights(parts["head"], head, tree, body_w)
    for o in parts["eyes"]:
        rig.rigid(o, "head")
    for side, hand, nail in zip(("R", "L"), parts["hands"], parts["nails"]):
        bones = rig.hand_bones(side)
        rig.chain_weights(hand, arm, bones, 0.006, tree, body_w, blend_bone=f"hand.{side}")
        rig.chain_weights(nail, arm, [b for b in bones if b.split(".")[0] in frame_mod.FINGERS], 0.003)
    extras = dress(fr, head, parts, p, (tree, body_w))
    extras += groom(fr, head, parts, p, (tree, body_w))
    objs = _all(parts) + extras
    for o in objs:
        rest_uvs(o)
    mesh = join(objs, name)
    occlusion.bake(mesh, 0.1 * fr.s)
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    motion.animate(arm, fr, p)
    return [arm, mesh]


def build_skeleton(name: str, p: dict):
    """A walking skeleton on the human rig: bone pieces, glowing eye points, the undead stance."""
    import bmesh
    from . import skeletal
    from .garments import mesh_from
    from .paint import hex3
    fr = frame_mod.Frame(p)
    head = Head(fr, p)
    arm = frame_mod.build_armature(fr, name)
    bones = skeletal.build_skeleton(fr, head, arm)
    base = hex3(p.get("skin", "#e0d8c0"))

    def bone_color(co):
        grime = 0.82 + 0.18 * (0.5 + 0.5 * math.sin(co.x * 90 + co.z * 55) * math.sin(co.y * 70 - co.z * 30))
        return tuple(c * grime for c in base)

    for o in bones:
        paint(o, bone_color)
        set_material(o, "char:bone")
    bm = bmesh.new()
    for sx in (1, -1):
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.0065 * head.h)
        bmesh.ops.translate(bm, vec=head.at((sx * 0.031, 0.066, -0.002)), verts=res["verts"])
    glow = mesh_from(bm, "eye_glow")
    flat(glow, p.get("eyes", "#ff3020"))
    set_material(glow, "char:glow")
    rig.rigid(glow, "head")
    objs = bones + [glow]
    for o in objs:
        rest_uvs(o)
    mesh = join(objs, name)
    occlusion.bake(mesh, 0.1 * fr.s)
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    motion.animate(arm, fr, {**p, "undead": True})
    return [arm, mesh]


def dress(fr, head, parts, p, lookup=None):
    """Clothing and accessories; removes the skin they cover. With the body's weights (``lookup``) a hood's cowl
    follows the neck and shoulders it hangs on."""
    body = parts["body"]
    w = Wardrobe(fr, p, head)
    w.tear = 0.35 if p.get("undead") else 0.0
    w.fit_collar(body)
    out, covers = [], []

    def add(obj, test=None):
        if obj is not None:
            out.append(obj)
            if test is not None:
                covers.append(test)

    if "shirt" in p or "coat" in p or "robe" in p:
        add(w.shell(body, w.in_shirt, "shirt", 0.0055, p.get("shirt", p.get("robe", "#8a8070")), "char:cloth", "shirt", w.shirt_cuts()), w.in_shirt)
    if not p.get("robe"):
        add(w.shell(body, w.in_pants, "pants", 0.005, p.get("pants", "#3a4a6a"), "char:cloth", "pants", w.pants_cuts()), w.in_pants)
    add(w.shell(body, w.in_boots, "boots", 0.007, p.get("boots", "#4a3424"), "char:leather", "boots", w.boot_cuts()), w.in_boots)
    if "coat" in p:
        add(w.shell(body, w.in_coat, "coat", 0.012, p["coat"], "char:cloth", "coat", w.shirt_cuts(-0.015)))
        add(w.skirt(body, fr.j["knee.R"].z + 0.03 * fr.s, 0.35, p["coat"], "coat_tails", open_front=True, under=list(out)))
    if "robe" in p:
        add(w.skirt(body, fr.j["ankle.R"].z + 0.02 * fr.s, 0.55, p["robe"], "robe_skirt", trim=p.get("trim"), under=list(out)))
        out.extend(w.sash(out[-1], p.get("trim", p["robe"])))
    if not p.get("robe"):
        out.extend(w.belt(body, p.get("belt", "#3a2618"), over=[o for o in out if o.name.startswith("coat")]))
    if "apron" in p:
        add(accessories.apron(fr, body, p["apron"]))
    out.extend(w.extras)
    strip_covered(body, covers)
    head.bury_neck(body)
    if p.get("hat"):
        under = [body] + [o for o in out if o.name.split(".")[0] in ("shirt", "coat")]
        hat = accessories.hat(head, parts["head"], p["hat"], p.get("hatColor", "#5a3a1a"), p.get("flower", "#f0d040"),
                              p.get("visor", True), under)
        if p["hat"] == "hood" and lookup is not None:
            rig.head_weights(hat, head, *lookup, torso_only=True)
        add(hat)
    if "mask" in p:
        add(accessories.mask(head, parts["head"], p["mask"]))
    if "horns" in p:
        add(accessories.horns(head, p["horns"]))
    if "wings" in p:
        add(accessories.wings(fr, p["wings"]))
    if "tail" in p:
        add(accessories.tail(fr, p["tail"]))
    return out


def groom(fr, head, parts, p, lookup=None):
    """Hair, beard, brows and lashes, carried by the head; with the body's weights (``lookup``) hair that hangs
    below the head follows the neck and shoulders instead."""
    style = p.get("hairStyle", "long" if fr.female else "short")
    color = p.get("hair", "#4a3020")
    out = []
    if style != "bald" and p.get("hat") != "hood":
        from mathutils.bvhtree import BVHTree
        hatted = p.get("hat") in ("cap", "wide")
        tree = BVHTree.FromObject(parts["body"], bpy.context.evaluated_depsgraph_get()) if style == "long" else None
        seed = sum(ord(ch) for ch in f"{style}{p.get('skin', '')}")     # not the colour: same shape, shared mesh cache
        out += locks.Groom(head, style, color, hatted, tree, seed).build(parts["head"])
    if "beard" in p:
        out.append(hair.beard(head, parts["head"], p["beard"]))
    out.append(hair.brows_and_lashes(head, parts["head"], p.get("brows", color)))
    for o in out:
        set_material(o, "char:hair")
        if lookup is not None:
            rig.head_weights(o, head, *lookup, torso_only=True)
        else:
            rig.rigid(o, "head")
    return out


def rest_uvs(obj):
    """UVMap (unless the part already has strand UVs) = rest (x, z); UVRest = rest (y, z): the game shader
    rebuilds the rest position from them so surface detail stays attached to the skin and cloth."""
    me = obj.data
    has_strands = "UVMap" in me.uv_layers and len(me.uv_layers) > 0
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    rest = me.uv_layers.get("UVRest") or me.uv_layers.new(name="UVRest")
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        if not has_strands:
            uv.data[loop.index].uv = (co.x, co.z)
        rest.data[loop.index].uv = (co.y, co.z)


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.name = name
    # Faces of five or more sides (button caps, buckle plates) stop the exporter computing tangents for the whole
    # mesh, which the hair highlights need.
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
        bm.to_mesh(obj.data)
    bm.free()
    return obj
