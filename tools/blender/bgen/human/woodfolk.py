"""A gnarled wooden creature (``anatomy: wood``, the splinterling): a body grown like a crooked sapling on the human
rig. A twisted trunk of a torso with knots and burls, branching collarbones, spindly limbs swelling at knobbly
joints, root-like feet, twig fingers (twigs.py), and a head like a knotted stump: a gnarled brow over two deep, dark
hollows with a faint ember far inside, a knot of a nose and a jagged mouth carved below. Bark covers it all: long
fissures and plates that run along the trunk, up the face and down each limb, wandering, pinching out and merging,
painted darker in the fissures and paler on the plates. Branches with a few autumn leaves sprout from the crown and a
shoulder, and pale splinters stick out of the wood (twigs.py).

Colours from ``skin`` (the bark) and ``eyes`` (the embers).
"""
import math

import numpy as np

from . import matter
from .matter import F
from .sdf import carve, ellipsoid, round_cone, smin, sphere

SOURCES = ("woodfolk.py",)
PARTS = ("trunk", "leg.R", "leg.L", "arm.R", "arm.L")
GROOVES = {"trunk": 34, "leg": 15, "arm": 12}        # fissures round each part


class Wood:
    def __init__(self, fr, head):
        self.fr, self.head, self.s = fr, head, fr.s
        j, s = fr.j, fr.s
        V = lambda v: np.asarray(tuple(v), dtype=F)
        self.V = V
        self.axes = {"trunk": (V((0, -0.012 * s, 0)), V((0, 0, 1)))}
        for side in ("R", "L"):
            sh, wr = V(j[f"shoulder.{side}"]), V(j[f"wrist.{side}"])
            hip, an = V(j[f"hip.{side}"]), V(j[f"ankle.{side}"])
            self.axes[f"arm.{side}"] = (sh, (wr - sh) / np.linalg.norm(wr - sh))
            self.axes[f"leg.{side}"] = (hip, (an - hip) / np.linalg.norm(an - hip))

    # ------------------------------------------------------------------ the solid
    def parts(self, p):
        """Distances to the trunk (with the neck and head), each leg and each arm."""
        j, s, V = self.fr.j, self.s, self.V
        sq = np.asarray((1.0, 1.22, 1.0), dtype=F)              # (the trunk is flatter front to back)
        pq = p * sq
        spine = [V((0, -0.004 * s, 0.9 * s)), V((0.008 * s, 0.002 * s, 1.07 * s)), V((-0.004 * s, -0.012 * s, 1.27 * s)), V((0, -0.03 * s, 1.41 * s))]
        radii = (0.12 * s, 0.1 * s, 0.124 * s, 0.09 * s)
        trunk = None
        for i in range(3):
            seg = round_cone(pq, spine[i] * sq, spine[i + 1] * sq, radii[i], radii[i + 1])
            trunk = seg if trunk is None else smin(trunk, seg, 0.03 * s)
        trunk = trunk / F(1.12)
        for side, sx in (("R", 1), ("L", -1)):          # collarbones branching out to the shoulders
            sh = V(j[f"shoulder.{side}"])
            trunk = smin(trunk, round_cone(p, V((sx * 0.03 * s, -0.026 * s, 1.425 * s)), sh + V((-sx * 0.01 * s, 0, 0.004 * s)),
                                           0.06 * s, 0.048 * s), 0.03 * s)
        neck = round_cone(p, V(j["neck"]) - V((0, 0, 0.02 * s)), V(j["head"]) + V((0, 0.008 * s, 0.02 * s)), 0.04 * s, 0.032 * s)
        trunk = smin(trunk, neck, 0.025 * s)
        for c, r in (((0.05, 0.078, 1.3), (0.04, 0.032, 0.046)), ((-0.07, -0.07, 1.12), (0.036, 0.03, 0.04))):   # burls
            trunk = smin(trunk, ellipsoid(p, V(c) * F(s), tuple(x * s for x in r)), 0.018 * s)
        out = {"trunk": smin(trunk, self.face(p), 0.03 * s)}
        for side, sx in (("R", 1), ("L", -1)):
            hip, kn, an = V(j[f"hip.{side}"]), V(j[f"knee.{side}"]), V(j[f"ankle.{side}"])
            bend = V((sx * 0.012 * s, 0.01 * s, 0))
            leg = smin(round_cone(p, hip, (hip + kn) / 2 + bend, 0.068 * s, 0.052 * s),
                       round_cone(p, (hip + kn) / 2 + bend, kn, 0.052 * s, 0.044 * s), 0.02 * s)
            leg = smin(leg, sphere(p, kn + V((0, 0.006 * s, 0)), 0.052 * s), 0.02 * s)
            mid = (kn + an) / 2 + V((-sx * 0.006 * s, -0.008 * s, 0))
            leg = smin(leg, smin(round_cone(p, kn, mid, 0.046 * s, 0.038 * s), round_cone(p, mid, an, 0.038 * s, 0.034 * s), 0.016 * s), 0.02 * s)
            leg = smin(leg, sphere(p, an + V((0, 0, 0.004 * s)), 0.04 * s), 0.014 * s)
            if side == "L":
                leg = smin(leg, ellipsoid(p, (hip + kn) / 2 + V((-0.04 * s, 0.03 * s, 0)), (0.03 * s, 0.03 * s, 0.04 * s)), 0.015 * s)
            heel, toe = V(j[f"heel.{side}"]), V(j[f"toe.{side}"])
            for spread, length, r in ((0.0, 1.15, 0.024), (0.5, 0.95, 0.02), (-0.45, 0.9, 0.02)):   # root toes
                tip = an + (toe - an) * length + V((sx * spread * 0.07 * s, 0, 0))
                tip[2] = 0.012 * s
                knee_pt = an + (tip - an) * 0.45 + V((0, 0, 0.018 * s))
                leg = smin(leg, smin(round_cone(p, an, knee_pt, r * 1.25 * s, r * s), round_cone(p, knee_pt, tip, r * s, 0.007 * s), 0.01 * s), 0.016 * s)
            out[f"leg.{side}"] = smin(leg, round_cone(p, an, heel + V((0, -0.01 * s, 0.006 * s)), 0.028 * s, 0.014 * s), 0.014 * s)
            sh, el, wr = V(j[f"shoulder.{side}"]), V(j[f"elbow.{side}"]), V(j[f"wrist.{side}"])
            bend = V((0, -0.008 * s, 0))
            a = smin(round_cone(p, sh - V((sx * 0.01 * s, 0, 0.006 * s)), (sh + el) / 2 + bend, 0.05 * s, 0.038 * s),
                     round_cone(p, (sh + el) / 2 + bend, el, 0.038 * s, 0.033 * s), 0.014 * s)
            a = smin(a, sphere(p, el + V((0, -0.004 * s, 0)), 0.04 * s), 0.014 * s)
            mid = (el + wr) / 2 + V((0, 0.006 * s, 0))
            a = smin(a, smin(round_cone(p, el, mid, 0.037 * s, 0.03 * s), round_cone(p, mid, wr, 0.03 * s, 0.026 * s), 0.012 * s), 0.014 * s)
            if side == "R":
                a = smin(a, ellipsoid(p, (el + wr) / 2 + V((0, -0.02 * s, 0.012 * s)), (0.026 * s, 0.024 * s, 0.026 * s)), 0.012 * s)
            out[f"arm.{side}"] = smin(a, sphere(p, sh + V((0, -0.004 * s, 0.006 * s)), 0.056 * s), 0.016 * s)   # knobbly shoulder
        return out

    def face(self, p):
        """The head in head-local units: a knotted stump with a gnarled brow, a knot nose and a long chin."""
        h = self.head.h
        q = (p - self.V(self.head.c)) / F(h)
        d = ellipsoid(q, (0, 0.004, 0.006), (0.08, 0.086, 0.118))
        d = smin(d, ellipsoid(q, (0, 0.03, -0.092), (0.05, 0.056, 0.05)), 0.03)
        d = smin(d, round_cone(q, (-0.058, 0.064, 0.026), (0.058, 0.064, 0.026), 0.021, 0.021), 0.022)   # brow ridge
        d = smin(d, round_cone(q, (0, 0.074, 0.02), (0, 0.097, -0.024), 0.012, 0.018), 0.012)             # knot nose
        d = smin(d, ellipsoid(q, (-0.05, 0.03, 0.07), (0.032, 0.032, 0.036)), 0.02)                       # a burl on the crown
        return d * F(h)

    def carvings(self, p, d):
        """Deep eye hollows, a jagged mouth and the burls' holes cut into the wood."""
        h, s, V = self.head.h, self.s, self.V
        q = (p - V(self.head.c)) / F(h)
        for sx in (1, -1):
            d = carve(d, ellipsoid(q, (sx * 0.035, 0.09, 0.004), (0.022, 0.05, 0.019)) * F(h), 0.004 * h)
        x = q[:, 0]
        teeth = F(0.004) + F(0.007) * np.abs(((x / F(0.016)) % F(1)) - F(0.5)) * F(2)
        mouth = np.maximum(np.maximum(np.abs(x) - F(0.046), np.abs(q[:, 2] + F(0.058)) - teeth), F(0.04) - q[:, 1]) * F(h)
        d = carve(d, mouth, 0.002 * h)
        d = carve(d, ellipsoid(q, (-0.05, 0.072, 0.07), (0.01, 0.02, 0.012)) * F(h), 0.002 * h)
        return carve(d, ellipsoid(p, V((0.06 * s, 0.11 * s, 1.31 * s)), (0.011 * s, 0.03 * s, 0.014 * s)), 0.003 * s)

    def solid(self, parts):
        d = parts["trunk"]
        for k in PARTS[1:]:
            d = smin(d, parts[k], 0.02 * self.s)
        return d

    def shape(self, p):
        return self.carvings(p, self.solid(self.parts(p)))

    # ------------------------------------------------------------------ bark
    def _fissures(self, p, key, seed):
        """0 on a plate .. 1 in a fissure, round the part ``key``'s axis: N fissures that wander along it."""
        a, d = self.axes[key]
        rel = p - a
        u = rel @ d
        r = rel - u[:, None] * d
        e1 = np.cross(d, (0.0, 1.0, 0.0) if abs(float(d[1])) < 0.9 else (1.0, 0.0, 0.0)).astype(F)
        e1 /= np.linalg.norm(e1)
        e2 = np.cross(d, e1).astype(F)
        th = np.arctan2(r @ e2, r @ e1)
        ring = np.stack([np.cos(th) * F(1.4), np.sin(th) * F(1.4), u * F(9.0 * 0.73 / self.s)], 1)
        fine = np.stack([np.cos(th) * F(3), np.sin(th) * F(3), u * F(40.0 * 0.73 / self.s)], 1)
        n = GROOVES[key.split(".")[0]]
        phase = th * F(n / (2 * math.pi)) + F(2.2) * matter.noise(ring, 1.0, seed) + F(0.35) * matter.noise(fine, 1.0, seed + 1)
        v = phase - np.floor(phase)
        dist = np.minimum(v, F(1) - v)
        width = F(0.06) + F(0.16) * matter.noise(fine * F(0.6), 1.0, seed + 2)
        return F(1) - matter.ss(0.0, 1.0, dist / width).astype(F)

    def bark(self, p, parts=None):
        """0 on the plates .. 1 deep in a fissure, each part's own fissures blended where the parts meet."""
        parts = parts if parts is not None else self.parts(p)
        d = np.stack([parts[k] for k in PARTS], 1)
        w = np.exp(-(d - d.min(1, keepdims=True)) / F(0.01 * self.s))
        w /= w.sum(1, keepdims=True)
        g = np.zeros(len(p), dtype=F)
        for i, k in enumerate(PARTS):
            m = w[:, i] > 0.02
            if m.any():
                g[m] += w[m, i] * self._fissures(p[m], k, 31 + 7 * i)
        q = (p - self.V(self.head.c)) / F(self.head.h)
        face = matter.ss(0.03, 0.07, q[:, 1]) * matter.ss(0.1, 0.0, np.abs(q[:, 2] + 0.02))     # finer on the face
        return (g * (F(1) - F(0.6) * face.astype(F))).astype(F)

    def field(self, p):
        parts = self.parts(p)
        d = self.carvings(p, self.solid(parts))
        near = np.abs(d) < F(0.01)
        if near.any():
            d = d.copy()
            g = self.bark(p[near], {k: v[near] for k, v in parts.items()})
            d[near] += g * F(0.0028 * self.s / 0.73) + F(0.0009) * (matter.noise(p[near], 45.0, 3) - F(0.5))
        return d

    def bounds(self):
        s = self.s
        return (-0.62 * s, -0.2 * s, -0.005), (0.62 * s, 0.26 * s, self.head.c.z + 0.14 * self.head.h)


def paint(obj, wood, base):
    co = matter.coords(obj).astype(F)
    parts = wood.parts(co)
    g = wood.bark(co, parts)[:, None]
    raw = wood.solid(parts)
    hollow = np.clip(-raw / F(0.004 * wood.head.h), 0, 1)[:, None]            # inside the carved eyes, mouth, holes
    blotch = matter.fbm(co, 14.0, 3, 41)[:, None]
    streak = matter.noise(co * np.asarray((1.0, 1.0, 0.12)), 90.0, 43)[:, None]
    rgb = base * (0.9 + 0.2 * blotch) * (0.94 + 0.16 * streak) * (1.16 - 0.55 * g)
    rgb = rgb * (1 - 0.92 * hollow ** 0.7)
    matter.paint(obj, rgb)


def _along(co, a, b):
    """Distance along a -> b (metres) of each point's projection, and the segment length."""
    ab = b - a
    L = float(np.linalg.norm(ab))
    return (co - a) @ (ab / L), L


def weights(wood, co):
    """Skin weights from the body's own parts: the trunk by height (hips, spine, chest, neck, head, the collarbones
    out to the shoulders), each limb along its bones with soft joints, the parts blended where they meet."""
    fr, s, head = wood.fr, wood.s, wood.head
    j = {k: np.asarray(tuple(v), dtype=float) for k, v in fr.j.items()}
    ss = matter.ss
    parts = wood.parts(co.astype(F))
    d = np.stack([parts[k] for k in PARTS], 1).astype(float)
    pw = np.exp(-(d - d.min(1, keepdims=True)) / (0.012 * s))
    pw /= pw.sum(1, keepdims=True)
    z, ax = co[:, 2], np.abs(co[:, 0])
    t = [ss(1.0 * s, 1.07 * s, z), ss(1.17 * s, 1.26 * s, z), ss(1.43 * s, 1.47 * s, z),
         ss(head.c.z - 0.15 * head.h, head.c.z - 0.075 * head.h, z)]
    trunk = {"hips": 1 - t[0], "spine": t[0] * (1 - t[1]), "chest": t[0] * t[1] * (1 - t[2]),
             "neck": t[0] * t[1] * t[2] * (1 - t[3]), "head": t[0] * t[1] * t[2] * t[3]}
    clav = ss(0.05 * s, 0.13 * s, ax) * ss(1.34 * s, 1.39 * s, z) * (1 - t[3])
    for side, sx in (("R", 1), ("L", -1)):
        trunk[f"clavicle.{side}"] = clav * (co[:, 0] * sx > 0)
    keep = 1 - clav
    for k in ("hips", "spine", "chest", "neck", "head"):
        trunk[k] = trunk[k] * keep
    out = [dict() for _ in range(len(co))]

    def add(table, share):
        for bone, w in table.items():
            w = w * share
            for i in np.flatnonzero(w > 1e-3):
                out[i][bone] = out[i].get(bone, 0.0) + float(w[i])

    add(trunk, pw[:, 0])
    for side, k in (("R", 1), ("L", 2)):
        hip, kn, an, ball = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"], j[f"ball.{side}"]
        u, L = _along(co, hip, kn)
        knee = ss(-0.035 * s, 0.035 * s, u - L)
        foot = ss(an[2] + 0.03 * s, an[2] - 0.005 * s, z)
        fwd = ball - an
        fwd[2] = 0
        toe = ss(-0.012 * s, 0.012 * s, (co - ball) @ (fwd / np.linalg.norm(fwd))) * foot
        top = ss(-0.02 * s, 0.05 * s, u)
        add({"hips": 1 - top, f"thigh.{side}": top * (1 - knee), f"shin.{side}": top * knee * (1 - foot),
             f"foot.{side}": top * (foot - toe), f"toe.{side}": top * toe}, pw[:, k])
    for side, k, sx in (("R", 3, 1), ("L", 4, -1)):
        sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
        u, L = _along(co, sh, el)
        v, M = _along(co, el, wr)
        elbow = ss(-0.03 * s, 0.03 * s, u - L)
        hand = ss(M - 0.02 * s, M + 0.005 * s, v) * elbow
        root = ss(-0.03 * s, 0.04 * s, u)
        add({f"clavicle.{side}": 1 - root, f"upper_arm.{side}": root * (1 - elbow), f"forearm.{side}": root * (elbow - hand),
             f"hand.{side}": root * hand}, pw[:, k])
    return [w or {"spine": 1.0} for w in out]


def build(fr, head, arm, p):
    from . import rig, twigs
    s = fr.s
    wood = Wood(fr, head)
    body = matter.mesh(wood.field, wood.bounds(), 0.0021 * s, 52000, "wood", ("wood", matter.frame_key(fr), head.h), SOURCES)
    rig.write_weights(body, weights(wood, matter.coords(body)))
    base = matter.hexrgb(p.get("skin", "#7a5634"))
    paint(body, wood, base)
    matter.slot(body, "char:leather")
    lookup = rig.body_lookup(body)
    return [body] + twigs.build(fr, head, arm, wood, lookup, base, p)
