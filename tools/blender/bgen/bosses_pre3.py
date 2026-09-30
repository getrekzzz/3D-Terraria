"""Deerclops and the Wall of Flesh, in the reference game's likeness.

Deerclops: a hunched, shaggy one-eyed giant with branching antlers, long clawed arms and a toothy muzzle, rigged to
idle, walk and swipe. Origin at its feet, facing +Y.
Wall of Flesh: the great mouth (a jaw that chomps), the eyes set in fleshy sockets, the flesh tile the game repeats
over the wall (lumps, ribs and veins) and the Hungry (tethered, toothy flesh balls). Centred, opening toward +Y.
"""
import math

from mathutils import Matrix, Vector

from .bosses_pre import _eyeball
from .bosses_pre2 import clips, wave
from .detail import fang, mix, noise3, organic, teeth_ring, teeth_row, veins
from .mesh import MeshBuilder

UP, FWD, SIDE = (0, 0, 1), (0, 1, 0), (1, 0, 0)

# ---------------------------------------------------------------------------------------------------- Deerclops
FUR, FUR_DARK, FUR_LIGHT, HIDE, ANTLER = "#8e8274", "#4e463e", "#b8ab98", "#2a2420", "#e6dcc8"
J = {  # joints (metres; 4.5 m tall, hunched)
    "hips": Vector((0, -0.1, 1.85)), "chest": Vector((0, 0.25, 3.05)), "neck": Vector((0, 0.55, 3.5)), "head": Vector((0, 0.8, 3.75)),
}
for _s, _x in (("R", 1), ("L", -1)):
    J[f"shoulder.{_s}"] = Vector((_x * 0.72, 0.2, 3.1))
    J[f"elbow.{_s}"] = Vector((_x * 1.0, 0.35, 2.25))
    J[f"wrist.{_s}"] = Vector((_x * 1.05, 0.65, 1.45))
    J[f"hip.{_s}"] = Vector((_x * 0.38, -0.1, 1.8))
    J[f"knee.{_s}"] = Vector((_x * 0.45, 0.25, 1.0))
    J[f"ankle.{_s}"] = Vector((_x * 0.45, -0.15, 0.2))

DEER_BONES = {"hips": (J["hips"] - Vector((0, 0, 0.3)), J["hips"], None), "chest": (J["hips"], J["chest"], "hips"),
              "head": (J["neck"], J["head"] + Vector((0, 0.2, 0.3)), "chest")}
for _s in ("R", "L"):
    DEER_BONES[f"arm.{_s}"] = (J[f"shoulder.{_s}"], J[f"elbow.{_s}"], "chest")
    DEER_BONES[f"fore.{_s}"] = (J[f"elbow.{_s}"], J[f"wrist.{_s}"], f"arm.{_s}")
    DEER_BONES[f"thigh.{_s}"] = (J[f"hip.{_s}"], J[f"knee.{_s}"], "hips")
    DEER_BONES[f"shin.{_s}"] = (J[f"knee.{_s}"], J[f"ankle.{_s}"], f"thigh.{_s}")


def _shaggy(q, n):
    """Fur: tufts pushed out along the normal, strongest on the back and shoulders."""
    tuft = abs(noise3(q, 9.0)) ** 1.5 + 0.5 * abs(noise3(q, 23.0, 2.0)) ** 2
    return n * 0.07 * tuft * (0.6 + 0.4 * max(0.0, -n.y + n.z))


def _fur(q, n):
    streak = 0.5 + 0.5 * math.sin(q.z * 30 + 4 * noise3(q, 5))
    belly = max(0.0, n.y) * max(0.0, 0.8 - abs(n.x))
    return mix(mix(FUR_DARK, FUR, 0.55 + 0.35 * streak), FUR_LIGHT, 0.45 * belly)


def _part(mb, bone, build, tris, material="prop:cloth", paint=_fur, sculpt=_shaggy):
    mb.bone = bone
    organic(mb, build, material, tris=tris, resolution=0.045, paint=paint, sculpt=sculpt, smooth=1)


def deerclops(mb: MeshBuilder, p, rng):
    eye = p.get("eye", "#ff6a20")
    _part(mb, "hips", lambda b: (b.ellipsoid(J["hips"], (0.62, 0.5, 0.45)),), 1400)
    _part(mb, "chest", lambda b: (b.capsule(J["hips"] + Vector((0, 0, 0.25)), J["chest"], 0.62),
                                  b.ellipsoid(J["chest"] + Vector((0, -0.1, 0.1)), (0.85, 0.62, 0.55)),       # hunched shoulders
                                  b.ellipsoid(J["chest"] + Vector((0, -0.35, 0.35)), (0.55, 0.45, 0.4))), 3200)
    # Head: a long muzzle under one huge eye, antlers branching from the brow.
    mb.bone = "head"
    h = J["head"]

    def head(b):
        b.ellipsoid(h, (0.42, 0.45, 0.4))
        b.capsule(h + Vector((0, 0.15, -0.1)), h + Vector((0, 0.7, -0.3)), 0.24)
        b.capsule(J["neck"] - Vector((0, 0.2, 0.1)), h, 0.3)
        b.ball(h + Vector((0, 0.38, 0.12)), 0.26, negative=True)           # the eye socket
    organic(mb, head, "prop:cloth", tris=2200, resolution=0.03, paint=_fur, sculpt=lambda q, n: _shaggy(q, n) * 0.5, smooth=1)
    mb.sphere(h + Vector((0, 0.72, -0.3)), 0.12, "prop:plain", HIDE, subdivisions=2, scale=(1.2, 0.8, 0.8))     # nose
    _eyeball(mb, 0.24, eye, rng, vein_trees=4, sclera="#e8d8c0", pupil=0.28, tris=1200, vein_depth=2, center=h + Vector((0, 0.28, 0.12)))
    mb.sphere(h + Vector((0, 0.3, 0.12)), 0.08, "prop:plain:glow", eye, subdivisions=2, scale=(1, 0.3, 1))    # the eye's glow
    teeth_row(mb, h + Vector((-0.16, 0.62, -0.42)), h + Vector((0.16, 0.62, -0.42)), 6, 0.1, (0, 0.2, -1), 0.025, rng)
    teeth_row(mb, h + Vector((-0.2, 0.35, -0.44)), h + Vector((-0.14, 0.6, -0.44)), 3, 0.08, (0, 0, -1), 0.02, rng)
    teeth_row(mb, h + Vector((0.2, 0.35, -0.44)), h + Vector((0.14, 0.6, -0.44)), 3, 0.08, (0, 0, -1), 0.02, rng)
    for sx in (-1, 1):
        _antler(mb, h + Vector((sx * 0.22, 0.05, 0.32)), sx, rng)
        mb.cylinder(h + Vector((sx * 0.36, -0.05, 0.1)), h + Vector((sx * 0.62, -0.2, 0.18)), 0.09, 0.02, "prop:cloth", FUR, segments=6)  # ears
    for s, sx in (("R", 1), ("L", -1)):
        _part(mb, f"arm.{s}", lambda b, s=s: (b.capsule(J[f"shoulder.{s}"], J[f"elbow.{s}"], 0.3),
                                               b.ball(J[f"shoulder.{s}"], 0.4)), 900)
        _part(mb, f"fore.{s}", lambda b, s=s: (b.capsule(J[f"elbow.{s}"], J[f"wrist.{s}"], 0.24),
                                                b.ellipsoid(J[f"wrist.{s}"] + Vector((0, 0.08, -0.12)), (0.24, 0.2, 0.26))), 900)
        w = J[f"wrist.{s}"]
        for k in range(3):  # long black claws
            a = (k - 1) * 0.35
            base = w + Vector((sx * 0.1 * math.cos(a) * 0 + math.sin(a) * 0.14, 0.18, -0.25))
            fang(mb, base, base + Vector((math.sin(a) * 0.1, 0.28, -0.3)), 0.05, "prop:plain", HIDE)
        _part(mb, f"thigh.{s}", lambda b, s=s: (b.capsule(J[f"hip.{s}"], J[f"knee.{s}"], 0.36),), 900)
        _part(mb, f"shin.{s}", lambda b, s=s: (b.capsule(J[f"knee.{s}"], J[f"ankle.{s}"], 0.22),
                                                b.ellipsoid(J[f"ankle.{s}"] + Vector((0, 0.12, -0.08)), (0.25, 0.35, 0.14))), 800)
    mb.bone = None
    return clips((DEER_BONES, {"idle": (2.4, _deer_idle), "walk": (1.4, _deer_walk), "attack": (0.9, _deer_swipe)}))


def _antler(mb, base, sx, rng):
    """A branching antler: a main beam curving up and out, tines along it."""
    pts = [base]
    d = Vector((sx * 0.5, -0.2, 1.0)).normalized()
    for k in range(5):
        d = (d + Vector((sx * 0.25, -0.1, -0.05))).normalized()
        pts.append(pts[-1] + d * 0.3)
    mb.tube(pts, [0.15, 0.13, 0.11, 0.09, 0.07, 0.035], "prop:plain", ANTLER, segments=7)
    for k in (1, 2, 3, 4, 5):
        root = pts[k]
        tip = root + Vector((sx * rng.uniform(-0.05, 0.15), rng.uniform(0.1, 0.35), 1)).normalized() * (0.6 - k * 0.07)
        mb.tube([root, root.lerp(tip, 0.5) + Vector((0, 0.04, 0)), tip], [0.08, 0.055, 0.018], "prop:plain", ANTLER, segments=5)
        if k in (2, 4):  # forked tines
            fork = root.lerp(tip, 0.6)
            mb.tube([fork, fork + Vector((sx * 0.15, 0.12, 0.2))], [0.04, 0.012], "prop:plain", ANTLER, segments=4)


def _deer_idle(t):
    breath = wave(t, 1)
    return {"chest": [(SIDE, 0.03 * breath)], "head": [(UP, 0.18 * wave(t, 1, 0.3)), (SIDE, -0.05 * breath)],
            "arm.R": [(SIDE, 0.05 * wave(t, 1, 0.1))], "arm.L": [(SIDE, 0.05 * wave(t, 1, 0.6))]}


def _deer_walk(t):
    pose = {"hips": [(UP, 0.08 * wave(t))], "chest": [(UP, -0.1 * wave(t)), (FWD, 0.04 * wave(t, 2))], "head": [(UP, 0.08 * wave(t))]}
    for s, ph in (("R", 0.0), ("L", 0.5)):
        swing = wave(t, 1, ph + 0.25)
        pose[f"thigh.{s}"] = [(SIDE, 0.45 * swing)]
        pose[f"shin.{s}"] = [(SIDE, -0.55 * max(0.0, wave(t, 1, ph + 0.5)))]
        pose[f"arm.{s}"] = [(SIDE, -0.35 * swing)]
        pose[f"fore.{s}"] = [(SIDE, 0.25 + 0.15 * swing)]
    return pose


def _deer_swipe(t):
    raise_ = math.sin(math.pi * min(1.0, t / 0.45)) if t < 0.45 else 0.0
    strike = 0.0 if t < 0.35 else math.sin(math.pi * min(1.0, (t - 0.35) / 0.45))
    return {"chest": [(UP, 0.35 * raise_ - 0.45 * strike), (SIDE, 0.15 * strike)],
            "arm.R": [(SIDE, 2.2 * raise_ + 1.2 * strike * (1 - raise_)), (FWD, -0.4 * raise_)],
            "fore.R": [(SIDE, 0.6 * raise_)], "head": [(SIDE, -0.15 * raise_ + 0.1 * strike)]}


# ---------------------------------------------------------------------------------------------------- Wall of Flesh
FLESH, FLESH_DARK, GUM, THROAT = "#b8505a", "#5a1a24", "#d86a70", "#1a0406"


def _flesh(q, n):
    return mix(FLESH_DARK, FLESH, 0.45 + 0.4 * noise3(q, 1.6) + 0.15 * max(0.0, n.y))


MAW_BONES = {"maw": ((0, -1.2, 0), (0, 0.5, 0), None), "jaw": ((0, 0.6, -0.4), (0, 1.8, -0.9), "maw")}


def maw_mouth(mb: MeshBuilder, p, rng):
    """The mouth: a fleshy mound bulging out of the wall with thick lips around a dark throat and rows of teeth;
    the lower jaw (a bone of its own) chomps."""
    r = p.get("radius", 2.1)
    mb.bone = "maw"

    def mound(b):
        b.ellipsoid((0, 0.1, 0.2), (r * 1.05, r * 0.6, r * 0.8))
        b.ellipsoid((0, r * 0.55, 0.35), (r * 0.8, r * 0.35, r * 0.35))          # upper lip
        b.ball((0, r * 0.95, 0), r * 0.5, negative=True)                         # the opening
    organic(mb, mound, "prop:flesh", tris=2600, resolution=r / 22, paint=_flesh, sculpt=lambda q, n: n * 0.05 * noise3(q, 3))
    mb.sphere((0, r * 0.35, -0.05), r * 0.55, "prop:plain", THROAT, subdivisions=2, scale=(1, 0.35, 0.85))
    teeth_row(mb, (-r * 0.55, r * 0.72, 0.15), (r * 0.55, r * 0.72, 0.15), 11, r * 0.28, (0, 0.25, -1), r * 0.06, rng)
    for k in range(7):  # veins spreading from the lips over the flesh
        a = 2 * math.pi * k / 7 + rng.uniform(-0.2, 0.2)
        veins(mb, (0, 0.1, 0.2), r * 0.95, (math.cos(a), 0.6, math.sin(a)), rng, "prop:flesh", "#6a0a1a", length=r * 0.9, width=0.06, depth=2)
    mb.bone = "jaw"

    def jaw(b):
        b.ellipsoid((0, r * 0.5, -r * 0.45), (r * 0.8, r * 0.45, r * 0.3))
    organic(mb, jaw, "prop:flesh", tris=1200, resolution=r / 22, paint=_flesh)
    teeth_row(mb, (-r * 0.5, r * 0.78, -r * 0.3), (r * 0.5, r * 0.78, -r * 0.3), 10, r * 0.24, (0, 0.2, 1), r * 0.055, rng)
    mb.bone = None
    chomp = lambda speed: (lambda t: {"jaw": [(SIDE, 0.28 * (0.5 + 0.5 * wave(t)))], "maw": [(SIDE, 0.03 * wave(t, 1, 0.3))]})
    return clips((MAW_BONES, {"idle": (1.4, chomp(1)), "walk": (1.0, chomp(1)), "fly": (1.0, chomp(1)), "attack": (0.45, chomp(2))}))


def maw_eye(mb: MeshBuilder, p, rng):
    """A great eye in a fleshy socket with heavy lids, bloodshot, looking out of the wall along +Y."""
    r = p.get("radius", 1.4)
    _eyeball(mb, r, p.get("iris", "#b86a20"), rng, vein_trees=10, sclera="#f0e2d6", blood=0.1, tris=3500)

    def socket(b):
        # A fleshy ring of lids and swollen tissue around the front of the eye, thickest above and below.
        for k in range(12):
            a = 2 * math.pi * k / 12
            ring = Vector((math.cos(a), 0, math.sin(a)))
            thick = 1.0 + 0.5 * abs(math.sin(a))
            b.ellipsoid(ring * r * 0.95 + Vector((0, r * 0.35, 0)), (r * 0.32 * thick, r * 0.3, r * 0.32 * thick))
        b.ellipsoid((0, -r * 0.35, 0), (r * 1.25, r * 0.55, r * 1.2))             # the mound behind
        b.ball((0, r * 0.3, 0), r * 0.98, negative=True)
    organic(mb, socket, "prop:flesh", tris=2400, resolution=r / 22, paint=_flesh, sculpt=lambda q, n: n * 0.04 * noise3(q, 4))


def maw_flesh(mb: MeshBuilder, p, rng):
    """One tile of the wall, size x size (X across, Z up), with its lumpy face at +Y: bulging flesh, ribs showing
    through and veins crawling over it. Tiles repeat, so the edges stay level."""
    s, t = p.get("size", 4.0), p.get("thickness", 3.0)
    mb.box((-s / 2, -t / 2, -s / 2), (s / 2, t / 2 - 0.3, s / 2), "prop:flesh", FLESH_DARK)

    def lumps(b):
        for _ in range(10):
            x, z = rng.uniform(-0.4, 0.4) * s, rng.uniform(-0.4, 0.4) * s
            b.ellipsoid((x, t / 2 - 0.45, z), (rng.uniform(0.5, 0.9), 0.4, rng.uniform(0.5, 0.9)))
        for k in range(3):  # ribs across the tile
            z = (k - 1) * s * 0.3
            b.capsule((-s * 0.45, t / 2 - 0.35, z), (s * 0.45, t / 2 - 0.35, z + rng.uniform(-0.2, 0.2)), 0.16)
    organic(mb, lumps, "prop:flesh", tris=1400, resolution=0.09, paint=lambda q, n: mix(_flesh(q, n), "#e0c8b0", 0.5 if abs((q.z / s) * 3.33 % 1 - 0.5) > 0.47 else 0.0))
    for _ in range(3):
        x0, z0 = rng.uniform(-0.4, 0.4) * s, rng.uniform(-0.4, 0.4) * s
        pts = [Vector((x0, t / 2 - 0.05, z0))]
        for _ in range(4):
            q = pts[-1] + Vector((rng.uniform(-0.7, 0.7), 0.0, rng.uniform(-0.7, 0.7)))
            pts.append(Vector((max(-s / 2, min(s / 2, q.x)), q.y, max(-s / 2, min(s / 2, q.z)))))
        mb.tube(pts, [0.09, 0.08, 0.06, 0.04, 0.02], "prop:flesh", "#6a0a1a", segments=4)


def hungry(mb: MeshBuilder, p, rng):
    """A tethered flesh ball, all mouth: lips ringed with teeth, a beady eye on top, veins over it."""
    r = p.get("radius", 0.55)

    def ball(b):
        b.ball((0, -0.05, 0), r)
        b.ball((0, r * 0.95, 0), r * 0.5, negative=True)
    organic(mb, ball, "prop:flesh", tris=900, resolution=r / 12, paint=_flesh, sculpt=lambda q, n: n * 0.03 * noise3(q, 6))
    mb.sphere((0, r * 0.45, 0), r * 0.42, "prop:plain", THROAT, subdivisions=2, scale=(1, 0.3, 1))
    teeth_ring(mb, (0, r * 0.62, 0), r * 0.42, (0, 1, 0), 9, r * 0.3, r * 0.07, inward=0.7, rng=rng)
    mb.sphere((0, r * 0.2, r * 0.82), r * 0.18, "prop:plain", "#f2e8e0", subdivisions=2)
    mb.sphere((0, r * 0.33, r * 0.88), r * 0.08, "prop:plain:glow", "#e04a20", subdivisions=1)
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        veins(mb, (0, -0.05, 0), r, (math.cos(a), -0.5, math.sin(a)), rng, "prop:flesh", "#6a0a1a", length=r * 1.2, width=0.03, depth=1, segments=4)


GENERATORS = {"deerclops_beast": deerclops, "wof_mouth": maw_mouth, "wof_eye": maw_eye, "wof_flesh": maw_flesh, "wof_hungry": hungry}
