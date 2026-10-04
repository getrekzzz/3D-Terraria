"""An empty suit of plate armour walking on its own (``anatomy: armor``, possessed armour): every plate a hollow
steel shell riding one bone of the human rig, overlapping its neighbours. A keeled breastplate and backplate rising
to a two-lame gorget that the helm (``hat``/``hatShape``, accessories) rests over, a fauld of three hooped lames over
the hips with tassets hanging over the thighs, domed pauldrons with lames down the arm, rerebraces, couters,
vambraces and gauntlets with articulated fingers (gauntlet.py), cuisses with knee cops, greaves and pointed
sabatons. Rolled edges and the insides of the plates are dark, rivets are set along the edges, and inside, where the
plates part, there is only a dim red glow; two eyes burn in the helm's slit.

Colours from ``armor`` (the steel), ``rivets``, ``glow`` (inside the suit) and ``eyes``.
"""
import numpy as np

from . import matter
from .matter import F
from .sdf import ellipsoid, round_cone, smax, smin, sphere

SOURCES = ("plate.py",)


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
    where it removes), its edges rolled outward."""

    def __init__(self, name, bone, outer, cuts, t, lo, hi, target=1800, roll=1.0):
        self.name, self.bone, self.outer, self.cuts, self.t = name, bone, outer, cuts, t
        self.lo, self.hi, self.target, self.roll = lo, hi, target, roll

    def edge(self, p):
        """How far in from the nearest cut edge (metres)."""
        return -np.max(np.stack([c(p) for c in self.cuts], 1), 1) if self.cuts else np.full(len(p), F(1))

    def field(self, p):
        o = self.outer(p)
        e = self.edge(p)
        bead = F(self.t * 0.55 * self.roll) * np.clip(F(1) - e / F(self.t * 2.2), 0, 1) ** 2
        d = np.maximum(o - bead, -(o + F(self.t) + bead))
        for c in self.cuts:
            d = smax(d, c(p), self.t * 0.3)
        return d

    def make(self, key, s, steel):
        obj = matter.mesh(self.field, (self.lo, self.hi), 0.0017 * s, self.target, self.name, key, SOURCES)
        _paint(obj, self, steel)
        return obj


class Lathe:
    """A plate tube as a regular grid, so no reduction can smear its two close faces together: ``rings`` rings round
    the axis a -> b from u0 to u1, each point where a ray out from the axis leaves ``outer``, the inner face ``t``
    within it and a rolled lip at each end; with ``arc`` (degrees) only that far either side of ``front``."""

    def __init__(self, name, bone, outer, a, b, u0, u1, t, s, front=(0, 1, 0), arc=None, rings=12, segs=36):
        self.name, self.bone, self.outer, self.t, self.s = name, bone, outer, t, s
        self.a, self.b, self.u0, self.u1 = np.asarray(a, dtype=float), np.asarray(b, dtype=float), u0, u1
        self.front, self.arc, self.rings, self.segs = np.asarray(front, dtype=float), arc, rings, segs

    def make(self, key, s, steel):
        import bmesh
        from .garments import mesh_from
        a, b, t = self.a, self.b, self.t
        d = (b - a) / np.linalg.norm(b - a)
        f = self.front - d * (self.front @ d)
        f /= np.linalg.norm(f)
        side = np.cross(d, f)
        closed = self.arc is None
        th = np.linspace(0, 2 * np.pi, self.segs, endpoint=False) if closed else np.radians(np.linspace(-self.arc, self.arc, self.segs // 2 + 1))
        e = np.cos(th)[:, None] * f + np.sin(th)[:, None] * side
        us = np.linspace(self.u0, self.u1, self.rings)
        C = a + (b - a) * us[:, None]
        lo, hi = np.zeros((len(us), len(th))), np.full((len(us), len(th)), 0.3 * s)
        for _ in range(26):                     # each ring's radius: bisection out from the axis
            mid = (lo + hi) / 2
            inside = (self.outer((C[:, None] + e[None] * mid[..., None]).reshape(-1, 3).astype(F)) < 0).reshape(mid.shape)
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        R = (lo + hi) / 2
        rho = 0.8 * t
        rows, kind = [], []
        for i in range(len(us)):
            rows.append(C[i] + e * R[i][:, None])
            kind.append(0.45 if i in (0, len(us) - 1) else 0.0)

        def lip(i, sign, phis):
            centre = C[i] + e * (R[i] - t / 2)[:, None]
            for phi in np.radians(phis):
                rows.append(centre + e * rho * np.cos(phi) + d * sign * rho * abs(np.sin(phi)))
                kind.append(1.0)
        lip(len(us) - 1, 1, (0, 60, 120, 180))
        for i in sorted({len(us) - 1, (2 * len(us)) // 3, len(us) // 3, 0}, reverse=True):
            rows.append(C[i] + e * (R[i] - t)[:, None])
            kind.append(-1.0)
        lip(0, -1, (180, 240, 300))
        bm = bmesh.new()
        grid = [[bm.verts.new(tuple(p)) for p in row] for row in rows]
        n, m = len(grid), len(th)
        for r in range(n):
            g0, g1 = grid[r], grid[(r + 1) % n]
            for k in range(m if closed else m - 1):
                bm.faces.new((g0[k], g0[(k + 1) % m], g1[(k + 1) % m], g1[k]))
        if not closed:                          # the two side edges: flat caps
            for k in (0, m - 1):
                bmesh.ops.triangulate(bm, faces=[bm.faces.new([grid[r][k] for r in range(n)])])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        obj = mesh_from(bm, self.name)
        co = np.concatenate(rows)
        k = np.repeat(np.asarray(kind), m)[:, None]
        streak = matter.noise(co * np.asarray((1.0, 1.0, 0.08)), 70.0, 5)[:, None]
        grime = matter.fbm(co, 8.0, 3, 9)[:, None]
        rgb = steel * (0.92 + 0.12 * streak) * (0.9 + 0.14 * grime) * (1 - 0.32 * np.clip(k, 0, 1))
        matter.paint(obj, np.where(k < 0, steel * 0.5, rgb))
        return obj


def _tube(name, bone, a, b, u0, u1, r0, r1, t, s, keel=0.0, front=(0, 1, 0), bulge=None, half=False):
    """A plate tube round a bone from u0 to u1 of the way a -> b, its radius r0 -> r1, open at both ends (with
    ``half`` only its front half: a tasset); more rings for a longer one."""
    a, b = np.asarray(tuple(a)), np.asarray(tuple(b))
    d = (b - a) / np.linalg.norm(b - a)
    p0, p1 = a + (b - a) * u0, a + (b - a) * u1
    f = np.asarray(front, dtype=float)
    f = f - d * (f @ d)
    f = (f / np.linalg.norm(f)).astype(F)

    def outer(p):
        o = round_cone(p, p0 - d * r0, p1 + d * r1, r0, r1)
        q = p - F(1) * np.asarray(p0, dtype=F)
        if keel:
            side = q - (q @ d.astype(F))[:, None] * d.astype(F)
            n = np.linalg.norm(side, axis=1) + F(1e-9)
            o = o - F(keel) * np.clip((side @ f) / n, 0, 1) ** 8
        if bulge is not None:
            o = smin(o, ellipsoid(p, *bulge), 0.02 * s)
        return o
    rings = max(4, int(np.linalg.norm(p1 - p0) / (0.022 * s)) + 2 + (6 if bulge is not None else 0))
    return Lathe(name, bone, outer, p0, p1, 0.0, 1.0, t, s, front, 96 if half else None, rings, 40 if keel else 32)


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
        out.append(Lathe(f"gorget{k}", bone, outer, a, b, 0.0, 1.0, t, s, rings=5, segs=40))
    # fauld: three hooped lames over the hips, each a little wider than the one above
    for k in range(3):
        z0 = (1.095 - 0.062 * k) * s
        r0, r1 = (0.138 + 0.01 * k) * s, (0.15 + 0.01 * k) * s
        a, b = S(0, -0.012, 0) + (0, 0, z0), S(0, -0.012, 0) + (0, 0, z0 - 0.08 * s)
        outer = (lambda a, b, r0, r1: lambda p: round_cone(p * np.asarray((1, 1.3, 1), dtype=F), a * (1, 1.3, 1), b * (1, 1.3, 1), r0, r1) / F(1.15))(a, b, r0, r1)
        out.append(Lathe(f"fauld{k}", "hips" if k else "spine", outer, a, b, 0.0, 1.0, t, s, rings=5, segs=48))
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = V(j[f"shoulder.{side}"]), V(j[f"elbow.{side}"]), V(j[f"wrist.{side}"])
        hip, kn, an = V(j[f"hip.{side}"]), V(j[f"knee.{side}"]), V(j[f"ankle.{side}"])
        up = (el - sh) / np.linalg.norm(el - sh)
        # pauldron: a dome over the shoulder and two lames below it
        pc = sh + S(sx * 0.012, -0.004, 0.006)
        out.append(Plate(f"pauldron_{side}", f"upper_arm.{side}", (lambda pc: lambda p: ellipsoid(p, pc, (0.094 * s, 0.098 * s, 0.09 * s)))(pc),
                         [plane(pc + up * 0.02 * s, up + np.asarray((-sx * 0.25, 0, 0))), ball_cut(S(0, -0.02, 1.47), 0.1 * s)],
                         t, pc - 0.12 * s, pc + 0.12 * s, 3200))
        for k in range(2):
            c = sh + up * (0.07 + 0.065 * k) * s + S(sx * 0.008, 0, 0)
            rr = (0.084 - 0.01 * k) * s
            outer = (lambda c, rr: lambda p: round_cone(p, c - up * 0.04 * s, c + up * 0.04 * s, rr, rr * 0.94))(c, rr)
            inner_side = plane(c - np.asarray((sx, 0, 0)) * 0.018 * s, (-sx, 0, 0.3))
            out.append(Plate(f"pauldron_lame{k}_{side}", f"upper_arm.{side}", outer, [plane(c - up * 0.034 * s, -up), plane(c + up * 0.034 * s, up), inner_side],
                             t, c - 0.11 * s, c + 0.11 * s, 1200))
        out.append(_tube(f"rerebrace_{side}", f"upper_arm.{side}", sh, el, 0.38, 0.86, 0.056 * s, 0.052 * s, t, s))
        out.append(Plate(f"couter_{side}", f"forearm.{side}", (lambda el: lambda p: ellipsoid(p, el - S(0, 0.006, 0), (0.054 * s, 0.054 * s, 0.054 * s)))(el),
                         [plane(el + S(0, 0.012, 0), (0, 1, 0.2))], t, el - 0.08 * s, el + 0.08 * s, 1400))
        out.append(_tube(f"vambrace_{side}", f"forearm.{side}", el, wr, 0.16, 0.86, 0.053 * s, 0.046 * s, t, s, keel=0.005 * s))
        # legs: cuisses with a keel, knee cops, greaves swelling at the calf, sabatons and pointed toe caps
        out.append(_tube(f"cuisse_{side}", f"thigh.{side}", hip, kn, 0.02, 0.86, 0.084 * s, 0.062 * s, t, s))
        for k in range(2):           # tassets: two lames hanging from the fauld over the front of the thigh
            out.append(_tube(f"tasset{k}_{side}", f"thigh.{side}", hip + S(sx * 0.008, 0, 0), kn, 0.04 + 0.15 * k, 0.23 + 0.15 * k,
                             (0.096 - 0.008 * k) * s, (0.092 - 0.008 * k) * s, t, s, half=True))
        kc = kn + S(0, 0.022, 0)
        out.append(Plate(f"poleyn_{side}", f"shin.{side}", (lambda kc: lambda p: smin(ellipsoid(p, kc, (0.056 * s, 0.05 * s, 0.06 * s)),
                                                                                       ellipsoid(p, kc + S(sx * 0.04, -0.012, 0), (0.016 * s, 0.05 * s, 0.05 * s)), 0.012 * s))(kc),
                         [plane(kn - S(0, 0.004, 0), (0, -1, 0))], t, kc - 0.1 * s, kc + 0.1 * s, 1600))
        calf = (kn + (an - kn) * 0.3 + S(0, -0.03, 0), (0.05 * s, 0.05 * s, 0.1 * s))
        out.append(_tube(f"greave_{side}", f"shin.{side}", kn, an, 0.14, 0.92, 0.056 * s, 0.045 * s, t, s, keel=0.006 * s, bulge=calf))
        out += sabatons(fr, side, t)
    return out


def sabatons(fr, side, t):
    j, s = fr.j, fr.s
    V = lambda v: np.asarray(tuple(v), dtype=float)
    an, heel, ball, toe = (V(j[f"{k}.{side}"]) for k in ("ankle", "heel", "ball", "toe"))
    fwd = toe - heel
    fwd[2] = 0
    fwd /= np.linalg.norm(fwd)

    def foot(p):
        o = ellipsoid(p, (heel + ball) / 2 + np.asarray((0, 0, 0.035 * s)), (0.05 * s, 0.11 * s, 0.05 * s))
        return smin(o, sphere(p, an + np.asarray((0, -0.005 * s, -0.01 * s)), 0.05 * s), 0.02 * s)

    tip = toe + fwd * 0.05 * s + np.asarray((0, 0, 0.012 * s))

    def cap(p):
        return smin(round_cone(p, ball + np.asarray((0, 0, 0.03 * s)), tip, 0.044 * s, 0.008 * s),
                    ellipsoid(p, ball + np.asarray((0, 0.01 * s, 0.026 * s)), (0.046 * s, 0.04 * s, 0.032 * s)), 0.02 * s)
    ground = plane((0, 0, 0.002 * s), (0, 0, -1))
    return [Plate(f"sabaton_{side}", f"foot.{side}", foot, [ground, plane(an + np.asarray((0, 0, 0.02 * s)), (0, 0.4, 1)), plane(ball + fwd * 0.012 * s, fwd)],
                  t, heel - 0.12 * s, ball + 0.12 * s, 1800),
            Plate(f"toecap_{side}", f"toe.{side}", cap, [ground, plane(ball - fwd * 0.004 * s, -fwd)], t, ball - 0.1 * s, tip + 0.06 * s, 1200)]


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
    from . import accessories, gauntlet
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
    if p.get("hat"):
        out.append(accessories.hat(head, p))
    out.append(gauntlet.eyes(head, p.get("eyes", "#ff3a3a")))
    out.append(gauntlet.core(fr, arm, p.get("glow", "#3a0806")))
    return out
