"""The hair style catalogue for the character creator: every style is scalp hair only (no brows, lashes or beard),
built around a given Head so it can be exported as its own model on the head bone and recoloured in game.

The first six reproduce groom() exactly (hair.scalp_shell, mane, tail); the rest are built with the same
techniques from hair_styles2 (lifted scalp shells, hanging curtains, strand tubes, braids, wrapped buns).
"""
import math
import random

from mathutils import Vector

from . import hair
from .hair_styles2 import Scalp, Strands, azimuth, clumps, curtain, shell
from .head import smooth

LEGACY = ["short", "messy", "long", "buzz", "ponytail", "bun"]
STYLES = LEGACY + ["mohawk", "spiky", "afro", "curly", "bob", "pixie", "side_part", "slicked_back", "undercut", "mullet",
                   "braid", "pigtails", "twin_buns", "topknot", "dreadlocks", "wavy_long"]


def build(head, head_obj, style, color):
    """Blender mesh objects for one hair style around ``head`` (whose mesh is ``head_obj``)."""
    if style in LEGACY:   # exactly as groom() builds them (no hat)
        shell_style = "buzz" if style == "buzz" else ("short" if style in ("ponytail", "bun") else style)
        out = [hair.scalp_shell(head, head_obj, shell_style, color, False)]
        if style == "long":
            out.append(hair.mane(head, color, 0.3 * head.frame.s))
        if style in ("ponytail", "bun"):
            out.append(hair.tail(head, color, style))
        return out
    if style not in _BUILDERS:
        raise KeyError(f"unknown hair style '{style}' (known: {', '.join(STYLES)})")
    return _BUILDERS[style](head, head_obj, color)


def _top(q):
    """1 on top of the head, fading toward the nape and over the ears (as the scalp shell's thickness)."""
    return 0.6 + 0.4 * smooth(-0.02, 0.09, q.z)


def _plain(head, head_obj, color, thick, **kw):
    return shell(head, head_obj, color, lambda q: thick * _top(q) * (0.75 + 0.35 * clumps(q)), **kw)


def _fringe(drop, width=0.65):
    """Hairline shift lowering the front into a fringe."""
    return lambda q: -drop * smooth(width, width * 0.4, abs(azimuth(q)))


# ---------------------------------------------------------------- short styles
def mohawk(head, head_obj, color):
    def depth(q):
        ridge = smooth(0.02, 0.003, abs(q.x))
        teeth = 0.85 + 0.15 * abs(math.sin(math.atan2(q.y, q.z) * 7))
        return 0.003 + 0.045 * ridge * teeth * smooth(-0.05, 0.06, q.z)
    crest = shell(head, head_obj, color, depth, keep=lambda q: abs(q.x) < 0.024, push=lambda q: Vector((0, -0.2 * depth(q), 0.1 * depth(q))), name="hair_crest")
    return [hair.scalp_shell(head, head_obj, "buzz", color), crest]


def spiky(head, head_obj, color):
    base = _plain(head, head_obj, color, 0.008)
    scalp, s, rng = Scalp(head, head_obj), Strands(head, color), random.Random(11)
    n = 110
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n
        if z < 0.0:
            continue
        a = i * 2.39996
        d = Vector((math.sqrt(1 - z * z) * math.cos(a), math.sqrt(1 - z * z) * math.sin(a), z))
        p, nrm = scalp.surface(d)
        if p is None:
            continue
        q = head.local(p)
        if q.z < hair.hairline(q) + 0.018 or abs(q.x) > 0.07 and q.z < 0.03:
            continue
        flow = (nrm + Vector((0, -0.35, 0.45)) + Vector((rng.gauss(0, 0.2), rng.gauss(0, 0.2), 0))).normalized()
        length = rng.uniform(0.022, 0.036) * head.h * (0.8 + 0.3 * smooth(0.0, 0.1, q.z))
        bend = Vector((0, -0.006, 0.004)) * head.h
        pts = [p - nrm * 0.004 * head.h, p + flow * length * 0.35, p + flow * length * 0.7 + bend * 0.5, p + flow * length + bend]
        s.tube(pts, [x * head.h for x in (0.013, 0.01, 0.006, 0.002)], seg=6)
    return [base, s.finish("hair_spikes")]


def pixie(head, head_obj, color):
    def push(q):
        front = smooth(0.8, 0.2, abs(azimuth(q))) * smooth(0.09, 0.04, q.z)
        return Vector((0.012, 0.003, -0.005)) * front
    return [_plain(head, head_obj, color, 0.008, line=_fringe(0.022), push=push)]


def side_part(head, head_obj, color):
    part = 0.028

    def depth(q):
        gap = 0.3 + 0.7 * smooth(0.0, 0.012, abs(q.x - part)) if q.y > -0.04 and q.z > 0.05 else 1.0
        return 0.015 * _top(q) * (0.8 + 0.3 * clumps(q, 7, 0.3)) * gap

    def push(q):
        side = 1.0 if q.x > part else -1.0
        return Vector((0.011 * side, -0.002, 0.002)) * smooth(0.04, 0.09, q.z) * smooth(-0.06, 0.02, q.y)
    return [shell(head, head_obj, color, depth, line=lambda q: -0.01 * smooth(0.2, -0.5, azimuth(q)) * smooth(-0.9, -0.3, azimuth(q)), push=push)]


def slicked_back(head, head_obj, color):
    return [shell(head, head_obj, color, lambda q: 0.0065 * (0.8 + 0.2 * smooth(-0.04, 0.06, q.z)),
                  push=lambda q: Vector((0, -0.006, 0.002)) * smooth(-0.02, 0.1, q.y),
                  shade=lambda q: 0.82 + 0.18 * math.sin(q.x * 380 + math.sin(q.y * 40) * 2))]


def undercut(head, head_obj, color):
    cut = 0.052

    def depth(q):
        return (0.003 + 0.02 * smooth(cut, cut + 0.016, q.z) * (0.9 + 0.4 * smooth(-0.02, 0.07, q.y))) * (0.85 + 0.25 * clumps(q))
    top = shell(head, head_obj, color, depth, keep=lambda q: q.z > cut and q.y > -0.095,
                push=lambda q: Vector((0.005, -0.008, 0.002)) * smooth(cut, cut + 0.02, q.z), name="hair_top")
    return [hair.scalp_shell(head, head_obj, "buzz", color), top]


def afro(head, head_obj, color):
    centre, radii = Vector((0, -0.03, 0.035)), Vector((0.15, 0.14, 0.125))

    def depth(q):
        d = Vector(((q - centre).x / radii.x, (q - centre).y / radii.y, (q - centre).z / radii.z)).length
        lump = 1.0 + 0.12 * math.sin(q.x * 90) * math.sin(q.y * 80 + q.z * 70)
        return max(0.008, (1.0 - d) * 0.12 * smooth(0.0, 0.04, q.z - hair.hairline(q))) * lump
    return [shell(head, head_obj, color, depth, shade=lambda q: 0.8 + 0.2 * math.sin(q.x * 260) * math.sin(q.z * 240 + q.y * 200))]


def curly(head, head_obj, color):
    def depth(q):
        curl = math.sin(q.x * 160 + q.z * 40) * math.sin(q.y * 150 - q.z * 90) * math.sin(q.z * 130 + q.x * 30)
        return 0.018 * _top(q) * (0.8 + 0.45 * curl)
    base = shell(head, head_obj, color, depth, shade=lambda q: 0.78 + 0.22 * math.sin(q.x * 300 + q.z * 260))
    scalp, s = Scalp(head, head_obj), Strands(head, color)
    for k in range(18):                       # ringlets round the back and sides
        a = 1.7 + (2 * math.pi - 3.4) * k / 17
        prof = scalp.hang(a, 0.0, -0.1 - 0.02 * math.sin(k * 1.3), 12, 0.02)
        pts = []
        for i, (r, z) in enumerate(prof):
            ang = i * 1.4 + k
            pts.append(scalp.point(a + 0.07 * math.cos(ang), r + 0.008 * math.sin(ang), z))
        s.tube(pts, [0.0065 * head.h * (1 - 0.4 * i / 12) for i in range(13)], seg=5)
    return [base, s.finish("hair_ringlets")]


# ---------------------------------------------------------------- hanging hair
def bob(head, head_obj, color):
    base = _plain(head, head_obj, color, 0.015, line=_fringe(0.02, 0.75))
    scalp = Scalp(head, head_obj)
    sheet = curtain(head, scalp, color, 1.3, 2 * math.pi - 1.3, 0.05, -0.1, 30, 10, 0.006, flare=0.006, curl=0.012, name="hair_bob")
    return [base, sheet]


def mullet(head, head_obj, color):
    base = _plain(head, head_obj, color, 0.01)
    scalp = Scalp(head, head_obj)
    back = curtain(head, scalp, color, 1.9, 2 * math.pi - 1.9, 0.02, -0.17, 18, 9, 0.007, flare=0.025, ragged=0.015, name="hair_mullet")
    return [base, back]


def wavy_long(head, head_obj, color):
    base = _plain(head, head_obj, color, 0.012, line=_fringe(0.006))
    scalp = Scalp(head, head_obj)
    sheet = curtain(head, scalp, color, 1.45, 2 * math.pi - 1.45, 0.06, 0.035 - 0.3 * head.frame.s / head.h, 30, 14, 0.008,
                    flare=0.03, wave=0.007, ragged=0.012, name="hair_waves")
    return [base, sheet]


def dreadlocks(head, head_obj, color):
    base = shell(head, head_obj, color, lambda q: 0.012 * _top(q) * (0.7 + 0.5 * clumps(q, 15, 0.6)))
    scalp, s, rng = Scalp(head, head_obj), Strands(head, color), random.Random(5)
    for layer, (count, z0, off) in enumerate(((15, 0.08, 0.016), (13, 0.035, 0.024))):
        lo, hi = 1.3 + 0.1 * layer, 2 * math.pi - 1.3 - 0.1 * layer
        for k in range(count):
            a = lo + (hi - lo) * (k + 0.5 * layer) / (count - 1 + layer)
            end = -0.08 - 0.13 * smooth(0.3, 0.9, -math.cos(a)) - rng.uniform(0, 0.03)
            prof = scalp.hang(a, z0, end, 9, off)
            pts = [scalp.point(a + rng.gauss(0, 0.015) * i / 9, r, z) for i, (r, z) in enumerate(prof)]
            s.tube(pts, [0.0065 * head.h * (1 - 0.25 * i / 9) for i in range(10)], seg=5, tint=rng.uniform(0.85, 1.05))
    return [base, s.finish("hair_locks")]


def braid(head, head_obj, color):
    base = shell(head, head_obj, color, lambda q: 0.007 * (0.85 + 0.25 * clumps(q, 9, 0.2)),
                 push=lambda q: Vector((0, -0.002, -0.001)) * smooth(0.0, 0.1, q.y),
                 shade=lambda q: 0.82 + 0.18 * math.sin(q.x * 300))
    scalp, s = Scalp(head, head_obj), Strands(head, color)
    rings, turns = 44, 7
    prof = scalp.hang(math.pi, -0.01, -0.35 * head.frame.s / head.h, rings, 0.022)
    width = 0.011
    for strand in range(3):
        pts, radii = [], []
        for i, (r, z) in enumerate(prof):
            t = i / rings
            ph = 2 * math.pi * (turns * t + strand / 3)
            w = width * (1 - 0.35 * t) * smooth(0.0, 0.06, t)
            x = w * math.sin(ph)
            y = 0.5 * w * math.sin(2 * ph)
            pts.append(head.at((x, AY_back(r, t) - y, z)))
            radii.append(0.0075 * head.h * (1 - 0.35 * t))
        s.tube(pts, radii, seg=6)
    r, z = prof[-1]
    tuft = [head.at((0, AY_back(r, 1.0), z - dz)) for dz in (0.0, 0.015, 0.035, 0.05)]
    s.tube(tuft, [x * head.h for x in (0.008, 0.011, 0.009, 0.003)], seg=8)
    return [base, s.finish("hair_braid")]


def AY_back(r, t):
    """Head-local y of hair hanging straight down the back at horizontal radius r, leaning out as it falls."""
    return -0.012 - r - 0.03 * t * t


def pigtails(head, head_obj, color):
    base = shell(head, head_obj, color, lambda q: 0.009 * _top(q) * (0.3 + 0.7 * smooth(0.0, 0.01, abs(q.x))) * (0.85 + 0.25 * clumps(q)))
    scalp, s = Scalp(head, head_obj), Strands(head, color)
    for sx in (1, -1):
        p, n = scalp.surface((sx * 0.7, -0.65, 0.15))
        out = Vector((sx * 0.6, -0.4, 0.0)).normalized()
        pts, radii = [], []
        for i in range(15):
            t = i / 14
            q = p + (n * 0.01 + out * 0.02 * math.sqrt(t) + Vector((0, -0.01 * t, -0.07 * t - 0.11 * t * t))) * head.h
            pts.append(q)
            pinch = 1 - 0.5 * smooth(0.08, 0.0, abs(t - 0.12))
            radii.append(head.h * (0.012 * (1 - 0.75 * t) + 0.004 * math.sin(t * math.pi) + 0.002) * pinch)
        pts[0] = p - n * 0.004 * head.h
        s.tube(pts, radii, seg=8)
    return [base, s.finish("hair_pigtails")]


def _buns(head, head_obj, color, spots, radius):
    base = shell(head, head_obj, color, lambda q: 0.007 * (0.85 + 0.25 * clumps(q, 9, 0.2)),
                 shade=lambda q: 0.82 + 0.18 * math.sin(q.x * 300 + q.y * 120))
    scalp, s = Scalp(head, head_obj), Strands(head, color)
    for d in spots:
        p, n = scalp.surface(d)
        s.ball(p + n * radius * 0.75 * head.h, radius * head.h, n, squash=0.8)
    return [base, s.finish("hair_buns")]


def twin_buns(head, head_obj, color):
    return _buns(head, head_obj, color, [(sx * 0.55, -0.25, 0.8) for sx in (1, -1)], 0.031)


def topknot(head, head_obj, color):
    return _buns(head, head_obj, color, [(0, -0.35, 0.94)], 0.036)


_BUILDERS = {"mohawk": mohawk, "spiky": spiky, "afro": afro, "curly": curly, "bob": bob, "pixie": pixie, "side_part": side_part,
             "slicked_back": slicked_back, "undercut": undercut, "mullet": mullet, "braid": braid, "pigtails": pigtails,
             "twin_buns": twin_buns, "topknot": topknot, "dreadlocks": dreadlocks, "wavy_long": wavy_long}
