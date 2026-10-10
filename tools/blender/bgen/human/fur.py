"""A fur coat (``fur``: its colour; ``furBelly``: a paler chest, belly and throat; ``furLength``: tuft length
scale): a dense undercoat over the skin with big clumped tufts laid on it, each rooted in the undercoat, lying along
the fur's flow (down the body, out along the limbs toward the hands and feet, back from the face) and splitting into
strands whose tips lift off, so the outline breaks up shaggily; over the shoulders it falls down and over their
edge, and long tufts stay narrow locks. Tufts are longest where a creature's fur is longest: a yeti's (``muzzle:
ape``, long soft locks that hang under their weight; on its head swept up off the brow, back over the crown and down
elsewhere) on the shoulders, forearms and calves, a werewolf's (``muzzle: wolf``) in a mane over the neck and
shoulders and a ruff round the cheeks, with short fur over the muzzle, a paler mask round the mouth and throat, and
a short coat on the backs of the ears (bare in their hollows). ``furKind`` picks the coat (wolf or ape by the
muzzle, by default): ``goat`` is a goat-demon's (the Krampus's) shaggy hide, long hanging locks thickest down the
thighs and shins in shaggy goat legs, shortening toward the ankles so the hooves show under them (``feet: hooves``,
hooves.py), and a shaggy mane hanging from the crown round a bare face as a yeti's does (its own beard and horns).
The fur parts round the face, the hands' palms and fingers and where trousers cover it (showing through their
tears); the head's coat stops where its neck sinks into the body's. Tufts come from tufts.py; heads are painted
here.
"""
import numpy as np
from mathutils.bvhtree import BVHTree

from . import tufts
from .coverings import arrays, decimate, make_mesh, noise, smooth_normals, ss, strip, submesh
from .hands import hand_axes
from .paint import hex3

# tuft length per bone (times the coat's base length) for each kind of fur; hang draws long tufts down
LENGTHS = {
    "ape": {"hips": 1.05, "spine": 0.95, "chest": 0.95, "clavicle": 1.3, "neck": 1.15, "head": 0.9,
            "upper_arm": 1.3, "forearm": 1.75, "hand": 0.35, "thigh": 1.15, "shin": 1.55, "foot": 0.45, "toe": 0.3},
    "wolf": {"hips": 0.85, "spine": 0.9, "chest": 0.95, "clavicle": 1.0, "neck": 1.1, "head": 0.75,
             "upper_arm": 0.95, "forearm": 1.1, "hand": 0.35, "thigh": 0.9, "shin": 1.05, "foot": 0.5, "toe": 0.35},
}
LENGTHS["goat"] = {"hips": 1.05, "spine": 0.85, "chest": 0.8, "clavicle": 1.0, "neck": 1.0, "head": 0.8,
                   "upper_arm": 0.85, "forearm": 1.1, "hand": 0.35, "thigh": 1.5, "shin": 1.65, "foot": 0.75, "toe": 0.3}
HANG = {"forearm": 0.9, "upper_arm": 0.5, "shin": 0.25, "chest": 0.0, "clavicle": 0.3}


def _group(bone):
    name = bone.split(".")[0]
    return "hand" if name in ("thumb", "index", "middle", "ring", "pinky") else name


class Coat:
    def __init__(self, fr, head, p):
        self.fr, self.head, self.p, self.s = fr, head, p, fr.s
        self.kind = p.get("furKind", "wolf" if p.get("muzzle") == "wolf" else "ape")
        shaggy = self.kind != "wolf"
        self.color = np.asarray(hex3(p["fur"]))
        self.belly = np.asarray(hex3(p.get("furBelly", p["fur"])))
        self.base = (0.12 if self.kind == "goat" else 0.13 if shaggy else 0.1) * self.s * p.get("furLength", 1.0)
        self.under = (0.02 if shaggy else 0.012) * self.s
        self.lift = 0.08 if shaggy else 0.035
        self.spacing = 0.58 if shaggy else 0.73
        self.widest = (0.052 if shaggy else 0.037) * self.s                # tufts as locks, not cards or chips
        self.style = "lock" if shaggy else "tuft"                           # long shaggy locks, or dense fur
        self.head_aspect = 0.36 if self.style == "tuft" else None           # finer tufts over a skull (not chips)
        self.dirs = {}
        for name, a, b, *_ in fr.bones():
            g = _group(name)
            if g in ("upper_arm", "forearm", "hand", "thigh", "shin", "foot", "toe"):
                d = np.asarray(b - a)
            elif g == "clavicle":           # down over the shoulders, a little out and back
                d = np.asarray((0.45 if name.endswith("R") else -0.45, -0.15, -1.0))
            elif g == "neck":
                d = np.asarray((0.0, -0.4, -1.0))
            else:
                d = np.asarray((0.0, 0.0, -1.0))
            d = d / np.linalg.norm(d)
            h = HANG.get(g, 0.0) * (1.0 if shaggy else 0.4)
            d = d + np.asarray((0.0, 0.0, -h))
            self.dirs[name] = d / np.linalg.norm(d)

    def spaced(self, kind):
        """Tuft spacing on a part (times the coat's): closer on the head; sparser on a yeti's hands and feet, whose
        short tufts would otherwise take a great many."""
        if self.style != "lock":
            return 0.95 if kind == "head" else 1.0
        return 0.85 if kind == "head" else 1.0 if kind == "body" else 2.0

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
            if self.kind != "wolf":         # up off the brow, straight back over the top, hanging down elsewhere
                rim = (1.0 - ss(1.1, 1.9, self.face(q))) * ss(-0.01, 0.03, q[:, 2])
                top = (ss(0.03, 0.07, q[:, 2]) * ss(-0.04, 0.02, q[:, 1]) * ss(0.075, 0.03, np.abs(q[:, 0])))[:, None]
                hang = np.asarray((0.0, -0.35, -1.2)) * (1.0 - top) + np.asarray((0.0, -1.0, 0.35)) * top
                away = away * rim[:, None] + hang * (1.0 - rim[:, None])
            hw = np.array([w.get("head", 0.0) for w in W])
            out = out * (1 - hw[:, None]) + away * hw[:, None]
        return out

    def length(self, W, X):
        """Tuft length (metres) at each point of the body from its bones."""
        table = LENGTHS[self.kind]
        L = np.array([sum(table.get(_group(b), 1.0) * x for b, x in w.items()) for w in W])
        j, s = self.fr.j, self.s
        top = ss(j["chest"].z, j["shoulder.R"].z + 0.04 * s, X[:, 2])       # shoulders and the top of the back
        L *= 1.0 + (0.35 if self.kind == "ape" else 0.15) * top       # (a yeti's hulking, shaggy shoulders)
        if self.kind == "goat":     # shorter toward the ankles, so the hooves show under the shaggy legs
            L *= 0.3 + 0.7 * ss(j["ankle.R"].z + 0.03 * s, j["ankle.R"].z + 0.3 * s, X[:, 2])
        if self.kind == "wolf":     # a ruff on the chest, shorter on the belly and the backs of the thighs
            L *= 1.0 + 0.12 * ss(j["spine"].z, j["chest"].z + 0.06 * s, X[:, 2]) * (X[:, 1] > j["chest"].y)
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
        low = ss(-0.215, -0.165, z)                  # gone where the head's neck sinks into the body's (its coat is there)
        if self.kind != "wolf":
            face = self.face(q)
            amount = ss(0.85, 1.25, face) * low
            rim = 0.45 + 0.55 * ss(1.3, 3.0, face)   # short round the face, so it shows in profile
            length = amount * (0.75 + 0.5 * neck) * rim
            return amount, length, np.zeros(len(q)), ss(0.9, 1.7, face) * 0.5
        shape = self.head.shape
        nose, lip, under = shape.regions(q)
        eye = np.min([np.linalg.norm(q - self.head.local(self.head.eyes[sx])[:], axis=1) for sx in (1, -1)], axis=0)
        ear = self.ear(q)
        back = self.ear_back(q)                      # the ears' backs take a short coat
        amount = (1.0 - nose) * (1.0 - lip) * ss(0.017, 0.024, eye) * (1.0 - ear * (1.0 - back)) * low
        cheek = ss(0.02, 0.05, np.abs(x)) * ss(0.11, 0.06, y) * ss(0.02, -0.03, z)
        muzzle = (1.0 - 0.65 * ss(0.07, 0.1, y)) * ss(0.19, 0.165, y)    # short over the muzzle, none on the nose
        length = amount * ss(0.024, 0.042, eye) * (muzzle * (0.5 + 0.5 * cheek + 0.85 * neck) * (1.0 - ear) + 0.35 * ear * back)
        pale = ss(-0.028, -0.045, z) * ss(0.02, 0.05, y)       # a paler mask round the mouth, cheeks and throat
        thin = ss(0.02, 0.045, eye) * (1.0 - 0.7 * ss(0.08, 0.14, y)) * (1.0 - 0.8 * ear)
        return amount, length, np.maximum(under, 0.85 * pale), thin

    @staticmethod
    def face(q):
        """A yeti's bare face over head-local points: below 1 inside it, growing away from it (9 and more behind)."""
        x, y, z = q[:, 0], q[:, 1], q[:, 2]
        face = ((x / 0.062) ** 2 + ((z + 0.035) / 0.074) ** 2) * (y > 0.0) + (y <= 0.0) * 9.0
        return np.where(y > 0.03, face, face + (0.03 - y) * 60.0)

    def ear_back(self, q):
        """1 behind the plane of a wolf's ears (their backs), 0 in front (their hollows)."""
        from .muzzles import ear_frame
        out = np.zeros(len(q))
        for sx in (1, -1):
            root, _, face, _ = ear_frame(sx)
            mine = np.sign(q[:, 0]) == sx
            out[mine] = ss(0.0, -0.004, (q[mine] - root) @ face)
        return out

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
    Vb, Tb, _ = arrays(parts["body"])
    body_tree = BVHTree.FromPolygons([_v(x) for x in Vb], Tb.tolist())
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
            amount = amount * _off_body(V, body_tree, s)
            length = length * amount * coat.base * LENGTHS[coat.kind]["head"]
        elif kind.startswith("foot"):
            amount = _foot_top(fr, kind[-1], V, s, p.get("feet"))
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
        under = tone * (0.74 + 0.14 * noise(Vs, 0.03 * s, 11)[:, None])        # darker deep in the coat
        uv = np.stack([Vs[:, 2] * 8.0, np.arctan2(Vs[:, 0], Vs[:, 1] + 0.02) / np.pi], 1)
        out.append(make_mesh(f"fur_coat_{kind}", Vs + Ns * h[:, None], Ts, under, "char:hair", Ws, uv))
        decimate(out[-1], 0.4 if kind == "head" else 0.6)
        tuft = tufts.build(f"fur_tufts_{kind}", Vs, Ts, Ns, Ws, flow, length[idx], h, tone, coat.lift * (0.35 if kind == "head" else 1.0),
                           seed=len(out) * 7919 + 13, spacing=coat.spacing * coat.spaced(kind),
                           min_len=0.013 * s if kind == "head" else None, max_width=coat.widest, style=coat.style,
                           aspect=coat.head_aspect if kind == "head" else None)
        if tuft is not None:
            out.append(tuft)
        if kind != "head":
            strip(obj, amount > 0.5)
    if coat.kind != "goat":                      # (a goat-demon's bare head keeps the skin painter's colours)
        coat.paint_head(parts["head"])
    for hand in parts["hands"]:
        skin = np.asarray(hex3(p.get("faceSkin", p.get("skin", "#8a7a6a"))))
        X, _, _ = arrays(hand)
        _paint(hand, np.tile(skin * 0.9, (len(X), 1)))
    strip(parts["head"], coat.head_fields(arrays(parts["head"])[0])[0] > 0.6)

    return out


def _off_body(X, tree, s):
    """0 where head points lie inside the body or close over it (the head's neck sunk into the body's, whose coat
    covers it), 1 clear of it."""
    out = np.ones(len(X))
    for i, x in enumerate(X):
        loc, nrm, _, _ = tree.find_nearest(_v(x), 0.03 * s)
        if loc is not None:
            out[i] = ss(0.004 * s, 0.018 * s, (_v(x) - loc).dot(nrm))
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


def _foot_top(fr, side, V, s, feet):
    """The top and sides of a foot (not the sole; on ape feet not the toes either; on hooves the pastern above them)."""
    if feet == "hooves":
        from .hooves import coronet
        return ss(coronet(fr, side) + 0.004 * s, coronet(fr, side) + 0.018 * s, V[:, 2])
    ape = feet == "ape"
    j = fr.j
    heel, toe = np.asarray(j[f"heel.{side}"]), np.asarray(j[f"toe.{side}"])
    fwd = (toe - heel) * np.asarray((1.0, 1.0, 0.0))
    length = np.linalg.norm(fwd) * 1.22
    a = (V - heel) @ (fwd / np.linalg.norm(fwd))
    top = ss(0.014 * s, 0.032 * s, V[:, 2])
    return top * ss(length * 0.74, length * 0.6, a) if ape else top
