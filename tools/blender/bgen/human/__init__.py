"""Detailed human characters (player, town folk, humanoid enemies), generated from parameters in assets.json.

Pipeline: proportions and skeleton (frame) -> body blob (body) and the head's distance field with sculpted features
and ears, plus eyeballs (head) -> hands with fingers and nails (hands) -> skin painting (paint) -> hair, brows and lashes (hair)
-> clothing and accessories (garments) -> skinning and actions (rig, motion). Everything is joined into one mesh
with ``char:<kind>`` material slots that the game maps to its character shader.

Parameters: body (male|female), age (young|adult|old), height, build (muscle 0..1), weight (0..1), skin, eyes,
hair, hairStyle, beard (its colour), beardStyle (short|full|bushy|long|pointed|goatee, default full), shirt, pants, boots,
coat, robe, trim, apron, hat (cap|wide|hood|wreath), hatColor, visor (a cap's peak, default true), hatShape, flower (a
wreath's flowers or a circlet's jewel), mask, horns, hornShape, wings, wingShape, tail, tailShape, brows (a colour,
default the hair's, or none), feet (talons: a bird's clawed feet in place of shod ones), talons (their colour),
anatomy (human|skeleton: bones only, dressed when it has outfit parameters|stone|wood|cookie|armor: a granite golem, a
gnarled wooden creature, a gingerbread man, an empty suit of plate coloured by armor, rivets and glow; matter.py),
armSpread (degrees the arms hang further out, for bulky bodies; keys its own animation set), sack (a scarecrow's
burlap head, with straw, rope and patches, true or a list of colours: scarecrow.py), undead, suit (true when the shirt, pants
and boots stand for the creature's own body, coloured like its skin: they are cut skin-tight and melt into the skin
at their edges, with no seams, folds, collar, cuffs, buttons, boot rims or soles, and no belt unless one is given),
hairTie (a ponytail's or bun's tie, default a darker shirt).

Build and weight above average (0.5, 0.45) add mass: muscle broadens the shoulders, back and chest and thickens the
limbs and neck; fat adds a round belly, love handles, a fuller chest, heavy limbs and a thick neck.

Creature heads (defaults give a person's head): headSize (head scale against the body, 1), earSize (1; 0 leaves only
a recessed ear opening, as on reptiles), earPoint (0 round .. 1 an elf's pointed tip .. 2 a goblin's long blade),
earFlare (0 flat to the head .. 1 standing out sideways), noseLength (1; longer projects further to a drooping
point), brow (0 .. 1 a heavy jutting brow ridge), jaw (0 .. 1 a wide heavy jaw and chin), snout (0 .. 1 a reptile's
snout in place of nose and mouth).

Monsters: face (cyclops | brute | ghoul | void, monsterface.py), sleeves (long, short or 0..1 down the arm), shirt/pants
none (bare), snakes (snakehair.py), wisps (spectre.py), tribalMask, headdress, beads, grassSkirt, bodyPaint
(tribal.py); tailShape snake is a serpent's body under a robe (serpent.py). handSize (1) scales the hands (paws.py).
Creature coverings in place of clothes (described in coverings.py): fur, furBelly, furLength, wraps, moss, mud, muzzle
(wolf|ape), faceSkin, hands (claws), feet (paws|ape), nails, torn.

hatShape by hat: wide: brim (default), floppy, top, pointed, tricorn, bucket, mushroom (hats.py); cap: santa, elf,
helmet, greathelm, bandana, turban, hardhat, beret (capshapes.py), shako (hats.py), party, nurse (perched on the hair,
crowns.py), anything else the plain cap; wreath: leaves (default), fronds, gold, frost (crowns.py).
hornShape: imp (default), demon, goat (features.py). wingShape: bat (default), feather, ice (wings.py).
tailShape: devil (default), lizard, fox, wolf, snake (tails.py), brush (a bushy tail of fur tufts, furtail.py).
"""
import bpy

from . import accessories, apron, beard, coverings, creaturewear, hair, locks, monsterparts, motion, occlusion, rig, underhat
from . import frame as frame_mod
from .garments import Wardrobe, strip_covered
from .tatters import Tatters
from .blobs import Blob
from .body import build_body
from .hands import build_hand
from .head import Head
from .paint import SkinPainter, flat, paint


# Material slots the game's character shader understands (game/Rendering/PropLibrary.cs).
SLOTS = {"char:skin", "char:eye", "char:hair", "char:nail", "char:cloth", "char:leather", "char:metal", "char:bone", "char:glow"}
LOOSE = ("hat_", "mask", "horns", "wing", "tail")      # worn parts long hair does not rest on


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
    coverings.reshape(fr, p)          # (creature hands)
    blob = Blob("body_blob", 0.0085 * fr.s)
    build_body(fr, blob, feet=coverings.shod(p))
    body = blob.to_mesh("body", 16000, smooth_iterations=2)
    head = Head(fr, p)
    coverings.head_shape(head, p)
    head_obj = head.build()
    head.fit_neck(head_obj, body)
    eyes = [] if monsterparts.own_eyes(p) else head.eyeballs(p.get("eyes", "#5a7890"))
    hands, nails = [], []
    for side in ("R", "L"):
        hand, nail = build_hand(fr, side)
        hands.append(hand)
        nails.append(coverings.nail(fr, p, side, nail))
    feet, claws = coverings.feet(fr, p)          # paws and ape feet ([] for shod feet and talons)
    if p.get("feet") == "talons":
        from .talons import build_talons
        for side in ("R", "L"):
            foot, claw = build_talons(fr, side, p.get("talons", "#d8a446"), p.get("claws", "#3a3028"))
            feet.append(foot)
            claws.append(claw)
    painter = SkinPainter(fr, head, p)
    paint(body, painter.body)
    paint(head_obj, painter.face)
    for part in hands:
        paint(part, painter.body)
    for o in (body, head_obj, *hands, *feet):
        set_material(o, "char:skin")
    for o in claws:
        set_material(o, "char:nail")
    for o in nails:
        flat(o, p.get("nails", "#e8c4b8"))
        set_material(o, "char:nail")
    for o in eyes:
        set_material(o, "char:eye")
    parts = {"body": body, "head": head_obj, "hands": hands, "nails": nails, "eyes": eyes, "feet": feet, "claws": claws}
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
    spread = f"_a{round(q['armSpread'])}" if q.get("armSpread") else ""
    return f"{'f' if fr.female else 'm'}{'_old' if fr.old else ''}_{round(fr.height * 100)}_{kind}{spread}"


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
    if p.get("anatomy", "human") != "human":        # stone, wood, cookie, armor (matter.py)
        from . import matter
        return matter.build(name, p)
    p = coverings.undress(p, {**DEFAULTS, **p})
    p = {k: v for k, v in p.items() if not (k in ("shirt", "pants") and v == "none")}     # bare chest, bare legs
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
    for side, foot, claw in zip(("R", "L"), parts["feet"], parts["claws"]):
        from .talons import skin
        skin((foot, claw), arm, fr, side, tree, body_w)
    from mathutils.bvhtree import BVHTree
    bare = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())   # before the clothes strip what they cover
    extras = dress(fr, head, parts, p, (tree, body_w))
    extras += coverings.grow(fr, head, parts, p, extras)      # fur, wraps, moss
    hair = groom(fr, head, parts, p, (tree, body_w), extras, bare)
    extras += hair + accessories.on_hair(head, parts, p, hair)
    extras += monsterparts.build(head, p, (tree, body_w))      # a monster face's own eyes, teeth, stitches, bolts
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
    """A walking skeleton on the human rig (skeletal.py): sculpted bones, eye lights deep in the orbits, the undead
    stance, and the clothes, hat and accessories its parameters ask for (skeldress.py)."""
    import bmesh
    from . import skeldress, skeletal, skulls
    from .garments import mesh_from
    fr = frame_mod.Frame(p)
    head = Head(fr, p)
    arm = frame_mod.build_armature(fr, name)
    bones, skull = skeletal.build_skeleton(fr, head, arm, hide=("foot",) if skeldress.dressed(p) else ())
    extras, bones = skeldress.clothe(fr, head, arm, p, bones)
    skeletal.paint(bones, skull, head, p.get("skin", "#e0d8c0"))
    for o in bones:
        set_material(o, "char:bone")
    bm = bmesh.new()
    for g in skulls.glow_points().values():
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.0062 * head.h)
        bmesh.ops.translate(bm, vec=head.at(g), verts=res["verts"])
    glow = mesh_from(bm, "eye_glow")
    flat(glow, p.get("eyes", "#ff3020"))
    set_material(glow, "char:glow")
    rig.rigid(glow, "head")
    objs = bones + [glow] + extras
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
    """Clothing and accessories; removes the skin they cover (not under tears). With the body's weights (``lookup``)
    a hood's cowl follows the neck and shoulders it hangs on."""
    body = parts["body"]
    talons = p.get("feet") == "talons"
    if talons:
        p = {k: v for k, v in p.items() if k != "boots"}
    w = Wardrobe(fr, p, head)
    robe = bool(p.get("robe"))
    w.fit_collar(body)
    if p.get("undead") or p.get("torn"):
        if not robe and "coat" not in p:
            w.tucked = w.belted = False          # the torn shirt hangs loose over the trousers
        w.tatters = Tatters(w, body, 0.35)
    out, covers = [], []

    def add(obj, test=None, kind=None):
        if obj is not None:
            out.append(obj)
            if test is not None:
                torn = w.tatters is not None and kind in w.tatters.kinds
                covers.append(w.tatters.mask(body, test, kind) if torn else test)
    if robe:
        add(w.shell(body, w.in_robe, "robe", 0.0055, p["robe"], "char:cloth", "robe", w.shirt_cuts(-0.035)), w.in_robe, "shirt")
    elif "coat" in p:       # under a coat the shirt ends with it, and anything glimpsed of it reads as the lining
        from .paint import hex3     # (unless the coat is torn: then the shirt shows in its holes)
        lining = tuple(c * 0.55 for c in hex3(p["coat"])) if w.tatters is None else p.get("shirt", "#8a8070")
        shirt = w.shell(body, w.in_coat, "shirt", 0.0055, lining, "char:cloth", "shirt", w.shirt_cuts(-0.015))
        add(shirt, w.in_shirt, "shirt")
    elif "shirt" in p:
        add(w.shell(body, w.in_shirt, "shirt", 0.0055, p.get("shirt", "#8a8070"), "char:cloth", "shirt", w.shirt_cuts()), w.in_shirt, "shirt")
    if not robe and "pants" in p:        # (none for bare legs; coverings.undress drops unnamed garments)
        add(w.shell(body, w.in_pants, "pants", 0.005, p.get("pants", "#3a4a6a"), "char:cloth", "pants", w.pants_cuts()), w.in_pants, "pants")
    if not talons and "wisps" not in p and coverings.wears(p, "boots"):      # a ghost drifts on no feet (spectre.py)
        add(w.shell(body, w.in_boots, "boots", 0.007, p.get("boots", "#4a3424"), "char:leather", "boots", w.boot_cuts()), w.in_boots)
    elif talons or "wisps" in p:
        covers.append(w.in_boots)          # the talons take the leg's end over
    if "coat" in p:
        add(w.shell(body, w.in_coat, "coat", 0.012, p["coat"], "char:cloth", "coat", w.shirt_cuts(-0.015)))
        add(w.skirt(body, fr.j["knee.R"].z + 0.03 * fr.s, 0.35, p["coat"], "coat_tails", open_front=True, under=list(out)))
    if robe:
        bodice = out[0] if out and out[0].name.startswith("robe") else None
        add(w.skirt(body, fr.j["ankle.R"].z + 0.02 * fr.s, 0.55, p["robe"], "robe_skirt", trim=p.get("trim"), under=list(out)))
        out.extend(w.sash(out[-1], bodice, p.get("trim", p["robe"])))
    if w.belted and coverings.wears(p, "pants") and (not w.suit or "belt" in p):      # a body suit wears only a belt it is given
        out.extend(w.belt(body, p.get("belt", "#3a2618"), over=[o for o in out if o.name.startswith(("coat", "shirt", "pants"))]))
    if "apron" in p:
        out.extend(apron.build(w, body, parts["head"], out + w.extras, p["apron"]))
    out.extend(w.extras)
    strip_covered(body, covers)
    head.bury_neck(body)
    if p.get("hat") and not accessories.rests_on_hair(p):
        under = [body] + [o for o in out if o.name.split(".")[0] in ("shirt", "coat", "robe")]
        hat = accessories.hat(head, p, under)
        if p["hat"] == "hood" and lookup is not None:
            rig.head_weights(hat, head, *lookup, torso_only=True)
        add(hat)
    out.extend(accessories.wear(fr, head, parts, p, list(out)))
    out.extend(creaturewear.wear(fr, head, parts, p, w, list(out), lookup))     # a shaman's outfit, a ghost's hem
    if "sack" in p and lookup is not None:      # a scarecrow: sack head, straw, patches, rope belt (scarecrow.py)
        from . import scarecrow
        out = scarecrow.dress(fr, head, parts, p, out, lookup)
    return out


def groom(fr, head, parts, p, lookup=None, worn=(), bare=None):
    """Hair, beard, brows and lashes, carried by the head; with the body's weights (``lookup``) hair that hangs
    below the head follows the neck and shoulders instead. Long hair and ponytails rest on the body (``bare``: a BVH
    of it from before the clothes trimmed it) and on the clothes ``worn``, a long beard on the clothes."""
    style = p.get("hairStyle", "long" if fr.female else "short")
    color = p.get("hair", "#4a3020")
    out = []
    if style != "bald" and p.get("hat") != "hood":
        hatted = underhat.UnderHat(head, p) if accessories.trims_hair(p) else None
        body_tree = worn_tree = None
        if style in ("long", "ponytail"):
            from mathutils.bvhtree import BVHTree
            body_tree = bare or BVHTree.FromObject(parts["body"], bpy.context.evaluated_depsgraph_get())
            worn_tree = _tree([o for o in worn if o.type == "MESH" and not o.name.startswith(LOOSE)])
        shirt = p.get("shirt", "#6a4a3a").lstrip("#")
        tie = p.get("hairTie") or "#" + "".join(f"{int(int(shirt[i:i + 2], 16) * 0.55):02x}" for i in (0, 2, 4))
        seed = sum(ord(ch) for ch in f"{style}{p.get('skin', '')}")     # not the colour: same shape, shared mesh cache
        out += locks.Groom(head, style, color, hatted, body_tree, seed, worn_tree, tie).build(parts["head"])
    if "beard" in p:
        under = [parts["body"]] + [o for o in worn if not o.name.startswith(("hat_", "mask", "horns", "wings", "tail"))]
        out.append(beard.grow(head, p, under))
    if p.get("brows") != "none":       # (reptiles have neither brows nor lashes)
        out.append(hair.brows_and_lashes(head, parts["head"], p.get("brows", color)))
    for o in out:
        set_material(o, "char:cloth" if o.name.startswith("hair_tie") else "char:hair")
        if lookup is not None:
            rig.head_weights(o, head, *lookup, torso_only=True)
        else:
            rig.rigid(o, "head")
    if "snakes" in p:          # Medusa's hair of snakes over her crop (snakehair.py)
        from . import snakehair
        out.append(snakehair.build(head, p["snakes"]))
    return out


def _tree(objs):
    """One BVH (world space) over several meshes, or None."""
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in objs:
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys) if polys else None


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
