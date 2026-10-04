"""Clothes for skeletons whose parameters ask for them: the garments are cut, as for people (``dress``), from a form
shaped like cloth hung on the bones (``hanger``: over the ribcage, collarbones and shoulder heads, falling plumb from
the lower ribs to the hip bones, loose tubes sagging round the limb bones, a thin neck round the vertebrae), which is
then thrown away, and fitted to the bones (skelfit.py); the head lends the cut no neck of its own, so the collar closes
round the vertebrae and those stay. Shirts and coats get a lining, since the bones leave the collar and cuffs open
onto the cloth's inside, and a cap is settled down onto the skull, which is smaller than the head it was shaped for.
Skeletons without outfit parameters stay bare (only a hat or horns, if asked for)."""
import bmesh
import bpy
import numpy as np
from mathutils import Vector

from . import accessories, rig, skelfit
from . import frame as frame_mod
from .blobs import Blob
from .body import limb_rotation
from .tailoring import Limb, ridge, ss, wobble

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
        body = hanger(frame)
        fingers = [b.name for b in arm.data.bones if b.name.split(".")[0] in frame_mod.FINGERS]
        rig.heat_weights(body, arm, drop=fingers + ["root"])
        rig.clean_limb_weights(body, arm, frame)
        out = dress(frame, _Neckless(head), {"body": body, "head": None}, {k: v for k, v in p.items() if k != "mask"})
        skin = skelfit.world(body, "co")         # what the clothes leave of the body (dress strips the rest)
        _remove(body)
        worn = [o for o in out if o.name.split(".")[0] not in LOOSE and not o.name.startswith("hat_")]
        sheets = [o for o in worn if o.name.split(".")[0] in SHEETS]
        _slacken(worn, frame)
        bare = [b for b in bones if b.name.startswith(("skull", "hand"))]
        covered = skelfit.hide([b for b in bones if b not in bare], skin, 0.035 * frame.s)
        skelfit.lift(worn, sheets, covered, 0.005 * frame.s, 0.035 * frame.s)
        bones = bare + covered
        out += [_lining(o, 0.0025 * frame.s) for o in out if o.name.split(".")[0] in ("shirt", "coat")]
    for o in out:
        if o.name.startswith("hat_") and p.get("hat") in ("cap", "hood", "wide"):
            _settle(o, head, 0.0105 * head.h)
    return out, bones


class _Neckless:
    """The head as the clothes see it on a skeleton: no neck to make room for (the collar closes round the vertebrae)
    and none to bury the form's own in (so the vertebrae it holds stay uncovered)."""

    def __init__(self, head):
        self._head = head

    def __getattr__(self, name):
        return getattr(self._head, name)

    def neck_depth(self, X):
        return np.zeros(len(X))

    def bury_neck(self, body_obj):
        pass


def hanger(frame):
    """The form a skeleton's clothes are cut from: the shape cloth takes hung on the bones, not a person's body (no
    chest, belly, buttocks or muscle). It lies over the ribcage and shoulder blades, the collarbones and the shoulder
    heads, falls plumb from the lower ribs past the spine to the hip bones, wraps the limb bones in loose tubes that
    sag a little below them and the cervical vertebrae in a thin neck; the feet are a person's (boots are their
    shells)."""
    j, s, fem = frame.j, frame.s, frame.female
    B = Blob("body_blob", 0.0085 * s)
    at = lambda *c: Vector(c) * s

    def e(c, r, rot=None):
        B.ellipsoid(c, tuple(x * s for x in r), rot)

    e(at(0, -0.03, 1.33), (0.122 if fem else 0.132, 0.092, 0.12))         # over the ribcage and shoulder blades
    e(at(0, -0.027, 1.205), (0.116 if fem else 0.124, 0.088, 0.1))
    e(at(0, -0.023, 1.09), (0.114 if fem else 0.12, 0.084, 0.1))          # hanging plumb past the spine
    e(at(0, -0.014, 0.975), (0.158 if fem else 0.148, 0.084, 0.088))      # the hip bones
    B.capsule(at(0, -0.05, 1.42), at(0, -0.048, 1.56), 0.03 * s)          # round the cervical vertebrae
    sag = at(0, 0, -0.006)
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
        up, fw = (el - sh).normalized(), (wr - el).normalized()
        B.capsule(at(sx * 0.03, -0.018, 1.435), sh + at(0, 0, 0.014), 0.026 * s)    # over the collarbone
        B.ball(sh + at(sx * 0.004, 0, 0.0), 0.038 * s)                             # the shoulder head
        mid = el.lerp(wr, 0.45)
        B.capsule(sh + up * 0.03 * s + sag, el + sag, 0.03 * s)
        B.ball(el + sag, 0.029 * s)
        B.capsule(el + sag, mid + sag, 0.027 * s)
        B.capsule(mid + sag, wr - fw * 0.012 * s + sag * 0.5, 0.022 * s)
        hip, kn, an = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"]
        B.capsule(hip + at(sx * 0.012, -0.004, 0), hip.lerp(kn, 0.45), 0.064 * s)
        B.capsule(hip.lerp(kn, 0.45), kn, 0.05 * s)
        B.ball(kn, 0.049 * s)
        B.capsule(kn, kn.lerp(an, 0.5), 0.042 * s)
        B.capsule(kn.lerp(an, 0.5), an, 0.032 * s)
        B.ball(an + at(0, 0, 0.004), 0.029 * s)
        heel, ball, toe = j[f"heel.{side}"], j[f"ball.{side}"], j[f"toe.{side}"]      # (as body.py's)
        B.ball(heel + at(0, 0.01, 0.026), 0.034 * s)
        fd = toe - heel
        e(heel.lerp(toe, 0.45) + at(0, 0, 0.026), (fd.length * 0.56 / s, 0.041, 0.03), limb_rotation(fd))
        e(ball.lerp(toe, 0.25) + at(0, 0, 0.018), (0.046, 0.044, 0.02), limb_rotation(toe - ball))
        e(heel.lerp(an, 0.5) + at(0, 0.03, 0.01), (0.05, 0.034, 0.04), limb_rotation(ball - an))
        B.ellipsoid(Vector((an.x, (heel.y + toe.y) / 2, -0.03 * s)), (0.07 * s, 0.2 * s, 0.034 * s), negative=True)
    return B.to_mesh("body", 16000, smooth_iterations=2)


def _slacken(objs, frame):
    """Slack cloth on bones: uneven shallow folds falling plumb from the ribcage to the belt and down the trouser
    legs, and the sleeves smooth over the bone along the top but sagging into a few slanting folds underneath. Each
    garment moves along its normals by a function of position alone, so a coat and the shirt under it fold together."""
    j, s = frame.j, frame.s
    cy, waist = j["spine"].y, j["hip.R"].z + 0.1 * s
    for o in objs:
        kind = o.name.split(".")[0]
        if kind not in ("shirt", "coat", "pants"):
            continue
        X, N = skelfit.world(o, "co"), skelfit.world(o, "normal")
        z, d, arm = X[:, 2], np.zeros(len(X)), np.zeros(len(X), dtype=bool)
        for side, sx in (("R", 1), ("L", -1)):
            if kind == "pants":
                leg = Limb(X, j[f"hip.{side}"], j[f"ankle.{side}"])
                fold = ridge(leg.phi * 5 / (2 * np.pi) + 0.4 * wobble(leg.phi, 2.1 * sx) + 0.5 * np.sin(leg.t * 5 + sx)) - 0.55
                env = ss(0.06, 0.24, leg.t) * ss(0.8, 0.68, leg.t) * (0.6 + 0.4 * np.sin(leg.t * 9 + leg.phi + sx))
                d = np.where((X[:, 0] * sx > 0) & (z < j["hip.R"].z), 0.0045 * s * env * fold, d)
                continue
            limb = Limb(X, j[f"shoulder.{side}"], j[f"wrist.{side}"])
            on = (X[:, 0] * sx > abs(j[f"shoulder.{side}"].x) * 0.7) & (limb.t > 0.05) & (limb.r < 0.085 * s)
            # (smooth over the bone along the top, sagging into a few uneven slanting folds underneath)
            u = limb.t * 5 + 0.3 * wobble(limb.phi, 1.7 * sx) + 0.2 * np.sin(limb.t * 13 + sx)
            sag = ss(-0.1, 0.7, -N[:, 2]) * ss(0.12, 0.3, limb.t) * ss(0.95, 0.86, limb.t)
            d = np.where(on, 0.0035 * s * sag * (ridge(u) - 0.55), d)
            arm |= on
        if kind != "pants":
            az = np.arctan2(X[:, 0], X[:, 1] - cy)
            env = (0.35 + 0.65 * ss(1.26 * s, 1.16 * s, z)) * ss(1.43 * s, 1.3 * s, z) * ss(waist + 0.01 * s, waist + 0.05 * s, z)
            fold = ridge(az * 8 / (2 * np.pi) + 0.45 * wobble(2 * az, 0.9)) - 0.55
            d = np.where(arm, d, 0.0065 * s * env * (0.55 + 0.45 * np.sin(3 * az + 0.7)) * fold)
        m = np.linalg.inv(np.asarray(o.matrix_world, dtype=float)[:3, :3])
        local = np.empty(len(o.data.vertices) * 3, dtype=np.float32)
        o.data.vertices.foreach_get("co", local)
        o.data.vertices.foreach_set("co", (local.reshape(-1, 3) + (N * d[:, None]) @ m.T).astype(np.float32).ravel())
        o.data.update()


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
