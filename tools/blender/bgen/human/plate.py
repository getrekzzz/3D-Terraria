"""An empty suit of plate armour walking on its own (``anatomy: armor``, possessed armour): every plate a hollow steel
shell riding one bone of the human rig, overlapping its neighbours. A keeled breastplate and backplate rising to a
two-lame gorget that the helm (``hat``/``hatShape``, accessories) rests over, a fauld of three hooped lames over the
hips with tassets hanging over the thighs, domed pauldrons with lames down the arm, rerebraces, couters, vambraces
and gauntlets with articulated fingers (gauntlet.py), cuisses with knee cops, greaves and pointed sabatons, dark
mail behind the knees and inside the elbows (the limbs in platelimbs.py; tubes and lames are lathed grids, lathe.py,
so no thin shell crumples). Rolled edges and the insides of the plates are dark, rivets are set along the edges, and
inside, where the plates part, there is only a dim red glow; two eyes burn in the helm's slit.

Colours from ``armor`` (the steel), ``rivets``, ``glow`` (inside the suit) and ``eyes``.
"""
import numpy as np

from . import matter
from .matter import F
from .lathe import Lathe
from .sdf import ellipsoid, round_cone, smax, smin

SOURCES = ("plate.py", "platelimbs.py")


def plane(c, n):
    c, n = np.asarray(tuple(c), dtype=F), np.asarray(tuple(n), dtype=float)
    n = (n / np.linalg.norm(n)).astype(F)
    return lambda p: (p - c) @ n                           # > 0 is cut away


def ball_cut(c, r):
    c = np.asarray(tuple(c), dtype=F)
    return lambda p: F(r) - np.linalg.norm(p - c, axis=1)      # inside the ball is cut away


def tube_cut(a, d, r):
    a, d = np.asarray(tuple(a), dtype=F), np.asarray(tuple(d), dtype=F)
    def cut(p):
        q = p - a
        return F(r) - np.linalg.norm(q - (q @ d)[:, None] * d, axis=1)
    return cut


class Plate:
    """A hollow plate: the shell inside the outer surface ``outer`` (thickness ``t``), trimmed by ``cuts`` (each > 0
    where it removes), its edges rolled outward; ``solid`` fills it (small cups: no thin shell to crumple)."""

    def __init__(self, name, bone, outer, cuts, t, lo, hi, target=1800, roll=1.0, solid=False):
        self.name, self.bone, self.outer, self.cuts, self.t = name, bone, outer, cuts, t
        self.lo, self.hi, self.target, self.roll, self.solid = lo, hi, target, roll, solid

    def edge(self, p):
        """How far in from the nearest cut edge (metres)."""
        return -np.max(np.stack([c(p) for c in self.cuts], 1), 1) if self.cuts else np.full(len(p), F(1))

    def field(self, p):
        o = self.outer(p)
        if self.solid:
            d = o
        else:
            bead = F(self.t * 0.55 * self.roll) * np.clip(F(1) - self.edge(p) / F(self.t * 2.2), 0, 1) ** 2
            d = np.maximum(o - bead, -(o + F(self.t) + bead))
        for c in self.cuts:
            d = smax(d, c(p), self.t * 0.3)
        return d

    def make(self, key, s, steel):
        obj = matter.mesh(self.field, (self.lo, self.hi), 0.0017 * s, self.target, self.name, key, SOURCES)
        _paint(obj, self, steel)
        return obj


def pieces(fr, head):
    j, s = fr.j, fr.s
    V = lambda v: np.asarray(tuple(v), dtype=float)
    S = lambda *v: np.asarray(v, dtype=float) * s
    t = 0.0055 * s
    out = []
    # cuirass: a keeled barrel from the waist to the collar, open at the neck and the arms
    cc = S(0, 0.004, 1.27)

    def cuirass(p):
        o = ellipsoid(p, cc, (0.156 * s, 0.13 * s, 0.25 * s))
        o = smin(o, ellipsoid(p, S(0, 0.034, 1.32), (0.142 * s, 0.112 * s, 0.16 * s)), 0.03 * s)
        x, y = p[:, 0], p[:, 1]
        keel = F(0.014 * s) * np.clip(F(1) - np.abs(x) / F(0.05 * s), 0, 1) ** 1.5 * (y > F(0))
        return o - keel
    cuts = [plane(S(0, 0, 1.075), (0, 0, -1)), tube_cut(S(0, -0.02, 0), (0, 0, 1), 0.082 * s), ]
    for side, sx in (("R", 1), ("L", -1)):
        cuts.append(ball_cut(V(j[f"shoulder.{side}"]) + S(-sx * 0.005, 0, -0.035), 0.085 * s))
    out.append(Plate("cuirass", "chest", cuirass, cuts, t, cc - S(0.2, 0.18, 0.26), cc + S(0.2, 0.18, 0.26), 7500))
    # plackart: a second plate over the belly, rising to a point under the chest
    pk = S(0, 0.012, 1.2)
    vee = [plane(S(0, 0.0, 1.31), (0.55, 0, 1)), plane(S(0, 0.0, 1.31), (-0.55, 0, 1)), plane(S(0, 0, 1.078), (0, 0, -1)),
           plane(S(0, -0.02, 0), (0, -1, 0))]
    out.append(Plate("plackart", "spine", lambda p: cuirass(p) - F(0.006 * s), vee, t, pk - S(0.2, 0.18, 0.16), pk + S(0.2, 0.18, 0.16), 3200))
    # gorget: two lames from the collar up into the helm
    top = head.c.z - 0.118 * head.h
    for k, (z0, z1, r0, r1, bone) in enumerate(((1.395 * s, 1.475 * s, 0.13 * s, 0.098 * s, "chest"),
                                               (1.462 * s, top, 0.1 * s, 0.082 * s, "neck"))):
        a, b = S(0, -0.024, 0) + (0, 0, z0), S(0, -0.02, 0) + (0, 0, z1)
        outer = (lambda a, b, r0, r1: lambda p: round_cone(p * np.asarray((1, 0.9, 1), dtype=F), a * (1, 0.9, 1), b * (1, 0.9, 1), r0, r1))(a, b, r0, r1)
        out.append(Lathe(f"gorget{k}", bone, outer, a, b, t, s, rings=5, segs=40))
    # fauld: three hooped lames over the hips, each a little wider than the one above
    for k in range(3):
        z0 = (1.095 - 0.062 * k) * s
        r0, r1 = (0.138 + 0.01 * k) * s, (0.15 + 0.01 * k) * s
        a, b = S(0, -0.012, 0) + (0, 0, z0), S(0, -0.012, 0) + (0, 0, z0 - 0.08 * s)
        outer = (lambda a, b, r0, r1: lambda p: round_cone(p * np.asarray((1, 1.3, 1), dtype=F), a * (1, 1.3, 1), b * (1, 1.3, 1), r0, r1) / F(1.15))(a, b, r0, r1)
        out.append(Lathe(f"fauld{k}", "hips" if k else "spine", outer, a, b, t, s, rings=5, segs=48))
    from . import platelimbs
    return out + platelimbs.pieces(fr, t)


def _paint(obj, piece, steel):
    co = matter.coords(obj).astype(F)
    o = piece.outer(co)
    inside = np.clip((-o - F(piece.t * 0.4)) / F(piece.t * 0.4), 0, 1)[:, None]      # the plate's inner face
    edge = np.clip(F(1) - piece.edge(co) / F(piece.t * 2.6), 0, 1)[:, None]
    streak = matter.noise(co * np.asarray((1.0, 1.0, 0.08)), 70.0, 5)[:, None]
    grime = matter.fbm(co, 8.0, 3, 9)[:, None]
    rgb = steel * (0.92 + 0.12 * streak) * (0.9 + 0.14 * grime) * (1 - 0.32 * edge)
    rgb = rgb * (1 - inside) + steel * 0.5 * inside
    matter.paint(obj, rgb)


def build(fr, head, arm, p):
    from . import accessories, gauntlet, platelimbs
    from .rig import rigid
    s = fr.s
    steel = matter.hexrgb(p.get("armor", "#9aa0aa"))
    out = []
    key = ("plate", matter.frame_key(fr), round(head.h, 4))
    for piece in pieces(fr, head):
        obj = piece.make(key, s, steel)
        rigid(obj, piece.bone)
        matter.slot(obj, "char:metal")
        out.append(obj)
    out += gauntlet.build(fr, steel, key)
    out += gauntlet.rivets(fr, head, out, p.get("rivets", "#8a7448"))
    out += platelimbs.voiders(fr, key, steel)
    if p.get("hat"):
        out.append(accessories.hat(head, p))
    out.append(gauntlet.eyes(head, p.get("eyes", "#ff3a3a")))
    out.append(gauntlet.core(fr, arm, p.get("glow", "#3a0806")))
    return out
