"""Clothes for skeletons whose parameters ask for them: the garments are cut, as for people (``dress``), from the slim
body the same frame would have, which is then thrown away, and fitted to the bones (skelfit.py). Shirts and coats get
a lining, since the bones leave the collar and cuffs open onto the cloth's inside, and a cap is settled down onto the
skull, which is smaller than the head it was shaped for. Skeletons without outfit parameters stay bare (only a hat or
horns, if asked for)."""
import bmesh
import bpy
import numpy as np

from . import accessories, rig, skelfit
from . import frame as frame_mod
from .blobs import Blob
from .body import build_body

OUTFIT = ("shirt", "pants", "boots", "coat", "robe")
LOOSE = ("mask", "horns", "wings", "tail")       # worn things that do not lie on the body
SHEETS = ("shirt", "pants", "boots", "coat", "coat_tails", "robe_skirt")      # the cloth that lies over bone


def dressed(p):
    return any(k in p for k in OUTFIT)


def clothe(frame, head, arm, p, bones):
    """The garments, hat and accessories for the skeleton ``p``, weighted to ``arm``, and the ``bones`` left to show:
    the bone the clothes cover deeply is cut away and the cloth is lifted over what stays (skelfit.py)."""
    from . import dress
    if not dressed(p):
        out = [accessories.hat(head, p)] if p.get("hat") else []
        if "horns" in p:
            from . import features
            out.append(features.horns(head, p["horns"], p.get("hornShape", "imp")))
    else:
        slim = frame_mod.Frame({**p, "build": 0.2, "weight": 0.1})
        blob = Blob("body_blob", 0.0085 * frame.s)
        build_body(slim, blob)
        body = blob.to_mesh("body", 16000, smooth_iterations=2)
        fingers = [b.name for b in arm.data.bones if b.name.split(".")[0] in frame_mod.FINGERS]
        rig.heat_weights(body, arm, drop=fingers + ["root"])
        rig.clean_limb_weights(body, arm, frame)
        out = dress(frame, head, {"body": body, "head": None}, {k: v for k, v in p.items() if k != "mask"})
        skin = skelfit.world(body, "co")         # what the clothes leave of the body (dress strips the rest)
        _remove(body)
        worn = [o for o in out if o.name.split(".")[0] not in LOOSE and not o.name.startswith("hat_")]
        sheets = [o for o in worn if o.name.split(".")[0] in SHEETS]
        bare = [b for b in bones if b.name.startswith(("skull", "hand"))]
        covered = skelfit.hide([b for b in bones if b not in bare], skin, 0.035 * frame.s)
        skelfit.lift(worn, sheets, covered, 0.005 * frame.s, 0.035 * frame.s)
        bones = bare + covered
        out += [_lining(o, 0.0025 * frame.s) for o in out if o.name.split(".")[0] in ("shirt", "coat")]
    for o in out:
        if o.name.startswith("hat_") and p.get("hat") in ("cap", "hood", "wide"):
            _settle(o, head, 0.0105 * head.h)
    return out, bones


def _remove(obj):
    me = obj.data
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(me)


def _settle(obj, head, depth):
    """Moves a hat ``depth`` in toward the centre of the vault (a skull is the head less its scalp)."""
    c = np.asarray(head.at((0, -0.012, 0.022)), dtype=np.float32)
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    r = co - c
    n = np.linalg.norm(r, axis=1, keepdims=True)
    co = c + r * (1 - depth / np.maximum(n, depth * 2))
    me.vertices.foreach_set("co", co.ravel())
    me.update()


def _lining(obj, depth):
    """The inside of a garment: its main sheet (not the bands round its edges, which are separate pieces) set in by
    ``depth``, turned to face inward and shaded darker; it keeps the garment's weights."""
    lin = obj.copy()
    lin.data = obj.data.copy()
    lin.name = f"{obj.name}_lining"
    bpy.context.scene.collection.objects.link(lin)
    keep = set(skelfit.sheet(lin.data))
    bm = bmesh.new()
    bm.from_mesh(lin.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in bm.verts:
        v.co -= v.normal * depth
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.float_color.get("Color")
    if layer is not None:
        for f in bm.faces:
            for loop in f.loops:
                c = loop[layer]
                loop[layer] = (c[0] * 0.55, c[1] * 0.55, c[2] * 0.55, c[3])
    bm.to_mesh(lin.data)
    bm.free()
    return lin
