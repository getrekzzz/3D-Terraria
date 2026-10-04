"""A fur coat (``fur``: its colour; ``furBelly``: a paler chest, belly and throat; ``furLength``: tuft length scale):
a dense undercoat over the skin with big clumped tufts laid on it, each rooted in the undercoat, lying along the
fur's flow (down the body, out along the limbs toward the hands and feet, back from the face) and splitting into
strands whose tips lift off, so the outline breaks up shaggily. Tufts are longest where a creature's fur is longest: a
yeti's (``muzzle: ape``) on the shoulders, forearms and calves, a werewolf's (``muzzle: wolf``) in a mane over the
neck and shoulders and a ruff round the cheeks. The fur parts round the face, the hands' palms and fingers and
where trousers cover it (showing through their tears). Tufts come from tufts.py; heads are painted here.
"""
import numpy as np

from . import tufts
from .coverings import arrays, decimate, make_mesh, noise, smooth_normals, ss, strip, submesh
from .hands import hand_axes
from .paint import hex3

# tuft length per bone (times the coat's base length) for each kind of fur; hang draws long tufts down
LENGTHS = {
    "ape": {"hips": 0.9, "spine": 0.9, "chest": 0.95, "clavicle": 1.6, "neck": 1.3, "head": 0.9,
            "upper_arm": 1.25, "forearm": 1.75, "hand": 0.35, "thigh": 1.05, "shin": 1.55, "foot": 0.45, "toe": 0.3},
    "wolf": {"hips": 0.85, "spine": 0.9, "chest": 1.15, "clavicle": 1.5, "neck": 1.7, "head": 0.85,
             "upper_arm": 0.95, "forearm": 1.1, "hand": 0.35, "thigh": 0.9, "shin": 1.05, "foot": 0.5, "toe": 0.35},
}
HANG = {"forearm": 0.9, "upper_arm": 0.5, "shin": 0.25, "chest": 0.0, "clavicle": 0.3}


def _group(bone):
    name = bone.split(".")[0]
    return "hand" if name in ("thumb", "index", "middle", "ring", "pinky") else name


class Coat:
    def __init__(self, fr, head, p):
        self.fr, self.head, self.p, self.s = fr, head, p, fr.s
        self.kind = "wolf" if p.get("muzzle") == "wolf" else "ape"
        self.color = np.asarray(hex3(p["fur"]))
        self.belly = np.asarray(hex3(p.get("furBelly", p["fur"])))
        self.base = (0.13 if self.kind == "ape" else 0.1) * self.s * p.get("furLength", 1.0)
        self.under = (0.03 if self.kind == "ape" else 0.016) * self.s
        self.lift = 0.14 if self.kind == "ape" else 0.06
        self.spacing = 0.52 if self.kind == "ape" else 0.6
        self.dirs = {}
        for name, a, b, *_ in fr.bones():
            g = _group(name)
            if g in ("upper_arm", "forearm", "hand", "thigh", "shin", "foot", "toe"):
                d = np.asarray(b - a)
            elif g == "clavicle":
                d = np.asarray((1.0 if name.endswith("R") else -1.0, 0.0, -0.8))
            elif g == "neck":
                d = np.asarray((0.0, -0.4, -1.0))
            else:
                d = np.asarray((0.0, 0.0, -1.0))
            d = d / np.linalg.norm(d)
            h = HANG.get(g, 0.0) * (1.0 if self.kind == "ape" else 0.4)
            d = d + np.asarray((0.0, 0.0, -h))
            self.dirs[name] = d / np.linalg.norm(d)

    # ------------------------------------------------------------------ fields over a part's vertices
    def flow(self, X, W, head=False):
        """The direction fur lies at each point (before it is laid into the surface)."""
        out = np.zeros((len(X), 3))
        for i, w in enumerate(W):
            for b, x in w.items():
                if b != "head" and b in self.dirs:
                    out[i] += self.dirs[b] * x
        if head:
            q = np.array([self.head.local(_v(x))[:] for x in X])
            away = q - np.asarray((0.0, 0.2 if self.kind == "wolf" else 0.15, -0.045))
            away /= np.linalg.norm(away, axis=1, keepdims=True)
            away[:, 2] -= 0.55 * ss(0.02, -0.1, q[:, 2])
            hw = np.array([w.get("head", 0.0) for w in W])
            out = out * (1 - hw[:, None]) + away * hw[:, None]
        return out

    def length(self, W, X):
        """Tuft length (metres) at each point of the body from its bones."""
        table = LENGTHS[self.kind]
        L = np.array([sum(table.get(_group(b), 1.0) * x for b, x in w.items()) for w in W])
        j, s = self.fr.j, self.s
        top = ss(j["chest"].z, j["shoulder.R"].z + 0.04 * s, X[:, 2])       # shoulders and the top of the back
        L *= 1.0 + (0.35 if self.kind == "ape" else 0.45) * top
        if self.kind == "wolf":     # a ruff on the chest, shorter on the belly and the backs of the thighs
            L *= 1.0 + 0.25 * ss(j["spine"].z, j["chest"].z + 0.06 * s, X[:, 2]) * (X[:, 1] > j["chest"].y)
        return L * self.base

    def belly_mix(self, X):
        """How far each body point takes the paler belly colour (front of the torso and the inner limbs)."""
        j, s = self.fr.j, self.s
        front = ss(0.02 * s, 0.09 * s, X[:, 1] - j["spine"].y) * ss(0.12 * s, 0.05 * s, np.abs(X[:, 0]))
        torso = ss(j["hip.R"].z - 0.05 * s, j["pelvis"].z + 0.05 * s, X[:, 2]) * ss(j["neck"].z + 0.04 * s, j["chest"].z, X[:, 2])
        return front * torso

    def head_fields(self, X):
        """(fur amount, tuft length, belly mix, undercoat scale) over the head mesh: a bare face (a wolf's nose,
        lips, eyelids and ears), short fur on a wolf's muzzle, a ruff round the cheeks and a mane down the neck."""
        q = np.array([self.head.local(_v(x))[:] for x in X])
        x, y, z = q[:, 0], q[:, 1], q[:, 2]
        neck = ss(-0.06, -0.16, z)
        if self.kind == "ape":
            face = ((x / 0.056) ** 2 + ((z + 0.035) / 0.074) ** 2) * (y > 0.0) + (y <= 0.0) * 9.0
            face = np.where(y > 0.03, face, face + (0.03 - y) * 60.0)
            amount = ss(0.85, 1.25, face)
            ruff = ss(1.2, 1.7, face) * (1.0 - ss(1.9, 3.2, face))
            length = amount * (0.75 + 0.3 * ruff + 0.5 * neck)
            return amount, length, np.zeros(len(q)), ss(0.9, 1.7, face) * 0.5
        shape = self.head.shape
        nose, lip, under = shape.regions(q)
        eye = np.min([np.linalg.norm(q - self.head.local(self.head.eyes[sx])[:], axis=1) for sx in (1, -1)], axis=0)
        ear = self.ear(q)
        amount = (1.0 - nose) * (1.0 - lip) * ss(0.017, 0.024, eye) * (1.0 - ear)
        cheek = ss(0.02, 0.05, np.abs(x)) * ss(0.11, 0.06, y) * ss(0.02, -0.03, z)
        length = amount * ss(0.032, 0.055, eye) * (1.0 - ss(0.07, 0.1, y)) * (0.5 + 0.7 * cheek + 0.85 * neck)
        return amount, length, under, ss(0.02, 0.045, eye) * (1.0 - 0.5 * ss(0.1, 0.16, y))

    def ear(self, q):
        """How far head-local points belong to a wolf's ears (which stand bare above the skull): near the ears'
        own surface (muzzles.py) and clear of the skull's."""
        shape = self.head.shape
        d = np.min([shape._ear(q.astype(np.float32), sx) for sx in (1, -1)], axis=0)
        return ss(0.005, 0.0, d) * ss(0.06, 0.075, q[:, 2])

    # ------------------------------------------------------------------ painting skin parts
    def paint_head(self, head_obj):
        """The bare parts of the head: a yeti's face skin (``faceSkin``), a wolf's black nose and lips."""
        X, _, _ = arrays(head_obj)
        q = np.array([self.head.local(_v(x))[:] for x in X])
        skin = np.asarray(hex3(self.p.get("faceSkin", self.p.get("skin", "#8a7a6a"))))
        fur = self.color * 0.55
        amount, _, under, _ = self.head_fields(X)
        rgb = skin[None, :] * (1 - amount[:, None]) + fur[None, :] * amount[:, None]
        eye = np.min([np.linalg.norm(q - self.head.local(self.head.eyes[sx])[:], axis=1) for sx in (1, -1)], axis=0)
        rgb *= (0.55 + 0.45 * ss(0.012, 0.026, eye))[:, None]               # dark round the eyes
        if self.kind == "wolf":
            nose, lip, _ = self.head.shape.regions(q)
            black = np.maximum(nose, lip)
            rgb = rgb * (1 - black[:, None]) + np.asarray((0.06, 0.05, 0.05))[None, :] * black[:, None]
            rgb = rgb * (1 - under[:, None] * 0.5) + self.belly[None, :] * 0.8 * under[:, None] * 0.5
            _, _, N = arrays(head_obj)
            face = np.stack([np.sign(q[:, 0]) * 0.3, np.ones(len(q)), np.zeros(len(q))], 1)
            inner = ss(0.1, 0.5, np.einsum("ij,ij->i", N, face / np.linalg.norm(face, axis=1, keepdims=True)))
            ear = self.ear(q)[:, None]
            cup = np.asarray((0.34, 0.24, 0.22))[None, :] * inner[:, None] + (self.color * 0.85)[None, :] * (1 - inner[:, None])
            rgb = rgb * (1 - ear) + cup * ear
        else:
            nose, lip, _ = self.head.shape.regions(q)
            rgb *= (1.0 - 0.5 * nose - 0.45 * lip)[:, None]
        _paint(head_obj, rgb)


def _v(x):
    from mathutils import Vector
    return Vector(tuple(x))


def _paint(obj, rgb):
    me = obj.data
    layer = me.color_attributes.get("Color") or me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    idx = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", idx)
    rgba = np.ones((len(idx), 4), dtype=np.float32)
    rgba[:, :3] = np.clip(rgb[idx], 0, 1)
    layer.data.foreach_set("color_srgb", rgba.ravel())


def grow(fr, head, parts, p, worn):
    from .rig import read_weights
    coat = Coat(fr, head, p)
    s = fr.s
    pants = [o for o in worn if o.name.startswith("pants")]
    out = []
    jobs = [("body", parts["body"])] + [("head", parts["head"])] + [(f"hand{side}", h) for side, h in zip("RL", parts["hands"])]
    jobs += [(f"foot{side}", f) for side, f in zip("RL", parts["feet"])]
    for kind, obj in jobs:
        V, T, N = arrays(obj)
        W = read_weights(obj)
        thin = np.ones(len(V))
        if kind == "body":
            amount = np.ones(len(V))
            if pants:
                amount = _uncovered(V, pants, s)
            length = coat.length(W, V) * amount
            belly = coat.belly_mix(V)
        elif kind == "head":
            amount, length, belly, thin = coat.head_fields(V)
            length = length * coat.base * LENGTHS[coat.kind]["head"]
        elif kind.startswith("foot"):
            amount = _foot_top(fr, kind[-1], V, s, p.get("feet") == "ape")
            length = amount * coat.base * LENGTHS[coat.kind]["foot"]
            belly = np.zeros(len(V))
        else:
            amount = _hand_back(fr, kind[-1], V, s)
            length = amount * coat.base * LENGTHS[coat.kind]["hand"]
            belly = np.zeros(len(V))
        keep = amount > 0.02
        if not keep.any():
            continue
        Vs, Ts, idx = submesh(V, T, keep)
        Ns = smooth_normals(Vs, Ts, N[idx])
        Ws = [W[i] for i in idx]
        flow = coat.flow(Vs, Ws, head=kind == "head")
        h = coat.under * amount[idx] * (0.6 + 0.8 * noise(Vs, 0.045 * s, 7, 2)) * thin[idx]
        h = np.maximum(h, 0.0015 * s)
        tone = coat.color[None, :] * (1 - belly[idx, None]) + coat.belly[None, :] * belly[idx, None]
        under = tone * (0.76 + 0.14 * noise(Vs, 0.03 * s, 11)[:, None])
        uv = np.stack([Vs[:, 2] * 8.0, np.arctan2(Vs[:, 0], Vs[:, 1] + 0.02) / np.pi], 1)
        out.append(make_mesh(f"fur_coat_{kind}", Vs + Ns * h[:, None], Ts, under, "char:hair", Ws, uv))
        if kind == "head":
            decimate(out[-1], 0.4)
        tuft = tufts.build(f"fur_tufts_{kind}", Vs, Ts, Ns, Ws, flow, length[idx], h, tone, coat.lift * (0.35 if kind == "head" else 1.0),
                           seed=len(out) * 7919 + 13, spacing=coat.spacing, min_len=0.014 * s if kind == "head" else None)
        if tuft is not None:
            out.append(tuft)
        if kind != "head":
            strip(obj, amount > 0.5)
    coat.paint_head(parts["head"])
    for hand in parts["hands"]:
        skin = np.asarray(hex3(p.get("faceSkin", p.get("skin", "#8a7a6a"))))
        X, _, _ = arrays(hand)
        _paint(hand, np.tile(skin * 0.9, (len(X), 1)))
    strip(parts["head"], coat.head_fields(arrays(parts["head"])[0])[0] > 0.6)

    return out


def _uncovered(V, garments, s):
    """1 where no garment lies over the skin (torn holes, below the hems), fading under their edges."""
    import bpy
    from mathutils.bvhtree import BVHTree
    dg = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(o, dg) for o in garments]
    d = np.full(len(V), 1.0)
    for i, co in enumerate(V):
        for t in trees:
            hit = t.find_nearest(_v(co), 0.05 * s)
            if hit[0] is not None:
                d[i] = min(d[i], hit[3])
    return ss(0.006 * s, 0.02 * s, d)


def _hand_back(fr, side, V, s):
    """The back of the hand up to the knuckles, and all round the wrist."""
    along, thumb, dorsal = hand_axes(fr, side)
    w = np.asarray(fr.j[f"wrist.{side}"])
    q = V - w
    a, back = q @ np.asarray(along), q @ np.asarray(dorsal)
    return np.maximum(ss(-0.004 * s, 0.006 * s, back) * ss(0.075 * s, 0.05 * s, a), ss(0.01 * s, -0.01 * s, a))


def _foot_top(fr, side, V, s, ape):
    """The top and sides of a foot (not the sole; on ape feet not the toes either)."""
    j = fr.j
    heel, toe = np.asarray(j[f"heel.{side}"]), np.asarray(j[f"toe.{side}"])
    fwd = (toe - heel) * np.asarray((1.0, 1.0, 0.0))
    length = np.linalg.norm(fwd) * 1.22
    a = (V - heel) @ (fwd / np.linalg.norm(fwd))
    top = ss(0.014 * s, 0.032 * s, V[:, 2])
    return top * ss(length * 0.74, length * 0.6, a) if ape else top
