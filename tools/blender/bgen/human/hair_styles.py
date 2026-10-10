"""The hair style catalogue for the character creator (X-09): every style is scalp hair only (no brows, lashes or
beard), built around a given Head so it can be exported as its own model on the head bone and recoloured in game.

Most styles are sculpted from locks exactly as a character's own hair is (locks.Groom: short, messy, long, bob,
ponytail, bun and buzz as townsfolk wear them, and the catalogue's pixie, side_part, slicked_back, spiky, topknot,
undercut and wavy_long); a woman's short hair stays short here (townsfolk wear it as a bob). Long hair rests on the
body and clothes it is given (``body_tree``, ``worn_tree``). The rest are built from lifted scalp shells, hanging
curtains and strand tubes (hair_styles2): a mohawk's crest (over the close crop), afro, curly, mullet, braid,
pigtails, twin_buns and dreadlocks.
"""
import math
import random

from mathutils import Vector

from . import locks
from .crop import crop
from .hair_styles2 import Scalp, Strands, clumps, curtain, hairline, shell
from .head import smooth

STYLES = ["short", "messy", "long", "buzz", "ponytail", "bun", "mohawk", "spiky", "afro", "curly", "bob", "pixie",
          "side_part", "slicked_back", "undercut", "mullet", "braid", "pigtails", "twin_buns", "topknot", "dreadlocks",
          "wavy_long"]
SCULPTED = {"short", "messy", "long", "buzz", "ponytail", "bun", "bob", "pixie", "side_part", "slicked_back", "spiky",
            "topknot", "undercut", "wavy_long"}


def build(head, head_obj, style, color, body_tree=None, worn_tree=None, tie_color=None, seed=1):
    """Blender mesh objects for one hair style around ``head`` (whose mesh is ``head_obj``); a tied style's band is
    named ``hair_tie``."""
    if style in SCULPTED:
        assert style in locks.STYLES, style
        g = locks.Groom(head, style, color, None, body_tree, seed, worn_tree, tie_color, bob=False)
        g.skin_edge = False
        return g.build(head_obj)
    if style not in _BUILDERS:
        raise KeyError(f"unknown hair style '{style}' (known: {', '.join(STYLES)})")
    return _BUILDERS[style](head, head_obj, color)


def _top(q):
    """1 on top of the head, fading toward the nape and over the ears."""
    return 0.6 + 0.4 * smooth(-0.02, 0.09, q.z)


def _plain(head, head_obj, color, thick, **kw):
    return shell(head, head_obj, color, lambda q: thick * _top(q) * (0.75 + 0.35 * clumps(q)), **kw)


# ---------------------------------------------------------------- volume
def mohawk(head, head_obj, color):
    """A crest down the middle of the head, toothed along its edge and leaning back, over a close crop."""
    def depth(q):
        ridge = smooth(0.02, 0.003, abs(q.x))
        teeth = 0.85 + 0.15 * abs(math.sin(math.atan2(q.y, q.z) * 7))
        return 0.003 + 0.045 * ridge * teeth * smooth(-0.05, 0.06, q.z)
    crest = shell(head, head_obj, color, depth, keep=lambda q: abs(q.x) < 0.024,
                  push=lambda q: Vector((0, -0.2 * depth(q), 0.1 * depth(q))), name="hair_crest")
    under = locks.Groom(head, "buzz", color)
    under.skin_edge = False
    return [crop(under, head_obj), crest]


def afro(head, head_obj, color):
    centre, radii = Vector((0, -0.03, 0.035)), Vector((0.15, 0.14, 0.125))

    def depth(q):
        d = Vector(((q - centre).x / radii.x, (q - centre).y / radii.y, (q - centre).z / radii.z)).length
        lump = 1.0 + 0.12 * math.sin(q.x * 90) * math.sin(q.y * 80 + q.z * 70)
        return max(0.008, (1.0 - d) * 0.12 * smooth(0.0, 0.04, q.z - hairline(q))) * lump
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
def mullet(head, head_obj, color):
    base = _plain(head, head_obj, color, 0.01)
    scalp = Scalp(head, head_obj)
    back = curtain(head, scalp, color, 1.9, 2 * math.pi - 1.9, 0.02, -0.17, 18, 9, 0.007, flare=0.025, ragged=0.015, name="hair_mullet")
    return [base, back]


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
            pts.append(head.at((x, _back(r, t) - y, z)))
            radii.append(0.0075 * head.h * (1 - 0.35 * t))
        s.tube(pts, radii, seg=6)
    r, z = prof[-1]
    tuft = [head.at((0, _back(r, 1.0), z - dz)) for dz in (0.0, 0.015, 0.035, 0.05)]
    s.tube(tuft, [x * head.h for x in (0.008, 0.011, 0.009, 0.003)], seg=8)
    return [base, s.finish("hair_braid")]


def _back(r, t):
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


def twin_buns(head, head_obj, color):
    base = shell(head, head_obj, color, lambda q: 0.007 * (0.85 + 0.25 * clumps(q, 9, 0.2)),
                 shade=lambda q: 0.82 + 0.18 * math.sin(q.x * 300 + q.y * 120))
    scalp, s = Scalp(head, head_obj), Strands(head, color)
    radius = 0.031
    for d in [(sx * 0.55, -0.25, 0.8) for sx in (1, -1)]:
        p, n = scalp.surface(d)
        s.ball(p + n * radius * 0.75 * head.h, radius * head.h, n, squash=0.8)
    return [base, s.finish("hair_buns")]


_BUILDERS = {"mohawk": mohawk, "afro": afro, "curly": curly, "mullet": mullet, "braid": braid, "pigtails": pigtails,
             "twin_buns": twin_buns, "dreadlocks": dreadlocks}
