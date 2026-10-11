"""Creatures with their own body plans, part 2 (P3-44): the flying snake (a winged serpent), the bone serpent (a skull
head, vertebra segments and a tail for the segmented worm brain), the eater of souls (a worm-bodied flying mouth), the
crimera (a floating crimson face), the granite and ice elementals (shards orbiting a glowing core) and the antlion
(a head poking out of the sand). Front faces Blender +Y; segment models are centred on their joint."""
import math

from mathutils import Matrix, Vector

from . import generator
from .creatures import _action
from .detail import fang, organic, teeth_ring, teeth_row
from .mesh import MeshBuilder

SERPENT_BONES = {"body": ((0, -0.1, 0.35), (0, 0.25, 0.35), None), "neck": ((0, 0.25, 0.38), (0, 0.55, 0.45), "body"),
                 "tail": ((0, -0.12, 0.35), (0, -0.7, 0.32), "body"),
                 "wing.R": ((0.06, 0.1, 0.42), (0.55, 0.05, 0.5), "body"), "wing.L": ((-0.06, 0.1, 0.42), (-0.55, 0.05, 0.5), "body")}


@generator
def flying_serpent(mb: MeshBuilder, p, rng):
    """A winged serpent: a long scaled body in an S, a flat viper head with a forked tongue, feathered wings."""
    scales, belly, wing = p.get("scales", "#5a8a3a"), p.get("belly", "#c8d870"), p.get("wing", "#d84a2a")
    mb.bone = "body"
    body = [Vector((0.06 * math.sin(t * 6), 0.25 - t * 0.35, 0.35 + 0.03 * math.sin(t * 4))) for t in (i / 6 for i in range(7))]
    mb.tube(body, [0.07, 0.08, 0.085, 0.08, 0.075, 0.07, 0.065], "prop:chitin", scales, segments=9)
    mb.bone = "tail"
    tail = [Vector((0.07 * math.sin(3.6 + t * 4), -0.1 - t * 0.6, 0.35 - t * 0.04)) for t in (i / 6 for i in range(7))]
    mb.tube(tail, [0.065, 0.055, 0.045, 0.035, 0.025, 0.015, 0.003], "prop:chitin", scales, segments=8)
    mb.bone = "neck"
    neck = [Vector((0, 0.25, 0.35)), Vector((0.03, 0.4, 0.4)), Vector((0, 0.52, 0.45))]
    mb.tube(neck, [0.07, 0.06, 0.055], "prop:chitin", scales, segments=9)
    mb.sphere((0, 0.6, 0.46), 0.075, "prop:chitin", scales, subdivisions=2, scale=(1.15, 1.5, 0.6))
    mb.sphere((0, 0.6, 0.43), 0.06, "prop:plain", belly, subdivisions=1, scale=(1.0, 1.4, 0.4))
    for sx in (1, -1):
        mb.sphere((sx * 0.06, 0.63, 0.49), 0.016, "prop:plain:glow", p.get("eyes", "#ffd040"), subdivisions=1)
        mb.tube([Vector((0, 0.7, 0.44)), Vector((sx * 0.015, 0.78, 0.43)), Vector((sx * 0.03, 0.82, 0.44))], [0.006, 0.004, 0.001], "prop:plain", "#d02040", segments=3)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"wing.{side}"
        for k in range(5):  # feathered vanes fanning out
            a = 0.25 - k * 0.12
            root = Vector((sx * 0.06, 0.12 - k * 0.03, 0.43))
            tip = root + Vector((sx * (0.5 - k * 0.06), -0.08 - k * 0.06 + a * 0.2, 0.08 - k * 0.02))
            mb.tube([root, root.lerp(tip, 0.5) + Vector((0, 0, 0.03)), tip], [0.035, 0.03, 0.008], "prop:cloth", wing if k % 2 else _shade(wing, 0.8), segments=4)
    mb.bone = None

    def actions(arm):
        flap = lambda a: {"wing.R": (0, a, 0), "wing.L": (0, -a, 0)}
        wave = lambda a: {"tail": (0, 0, a), "neck": (0, 0, -a * 0.5)}
        _action(arm, "fly", 0.4, {0.0: {**flap(0.7), **wave(0.3)}, 0.5: {**flap(-0.5), **wave(-0.3)}, 1.0: {**flap(0.7), **wave(0.3)}})
        _action(arm, "attack", 0.4, {0.0: {"neck": (0, 0, 0)}, 0.4: {"neck": (-0.5, 0, 0), **flap(0.9)}, 1.0: {"neck": (0, 0, 0)}})
    return {"rig": SERPENT_BONES, "actions": actions}


BONE = "#e6dcc4"


@generator
def bone_serpent_head(mb: MeshBuilder, p, rng):
    """A long reptilian skull: domed cranium, deep eye sockets with a red glint, a snout of teeth and a lower jaw."""
    bone = p.get("bone", BONE)
    mb.sphere((0, -0.05, 0.05), 0.3, "prop:bone", bone, subdivisions=3, scale=(1.0, 1.1, 0.85), shade=0.35)
    mb.sphere((0, 0.32, -0.02), 0.2, "prop:bone", bone, subdivisions=2, scale=(0.8, 1.4, 0.55))     # snout
    mb.sphere((0, 0.25, -0.17), 0.16, "prop:bone", _shade(bone, 0.85), subdivisions=2, scale=(0.85, 1.6, 0.35))  # jaw
    for sx in (1, -1):
        mb.sphere((sx * 0.15, 0.12, 0.12), 0.08, "prop:plain", "#141008", subdivisions=2, scale=(1, 0.7, 0.9))   # socket
        mb.sphere((sx * 0.15, 0.15, 0.12), 0.025, "prop:plain:glow", p.get("eyes", "#ff4a1a"), subdivisions=1)
        mb.cylinder((sx * 0.18, -0.15, 0.2), (sx * 0.3, -0.42, 0.32), 0.05, 0.0, "prop:bone", bone, segments=6)    # horns
    teeth_row(mb, (-0.12, 0.2, -0.08), (0.12, 0.2, -0.08), 6, 0.06, (0, 0.2, -1), 0.016, rng=rng)
    teeth_row(mb, (-0.1, 0.45, -0.07), (0.1, 0.45, -0.07), 4, 0.05, (0, 0.3, -1), 0.014, rng=rng)
    return None


@generator
def bone_serpent_vertebra(mb: MeshBuilder, p, rng):
    """One vertebra of the bone serpent: a spool-shaped centrum, a dorsal spine and a pair of ribs."""
    bone = p.get("bone", BONE)
    mb.lathe([(0.12, -0.3), (0.2, -0.24), (0.14, -0.1), (0.13, 0.1), (0.2, 0.24), (0.12, 0.3)], "prop:bone", bone, segments=12,
             matrix=Matrix.Rotation(-math.pi / 2, 4, "X"))
    mb.cylinder((0, 0, 0.12), (0, -0.12, 0.42), 0.06, 0.01, "prop:bone", bone, segments=6)
    for sx in (1, -1):
        mb.tube([Vector((sx * 0.12, 0.0, 0.0)), Vector((sx * 0.32, -0.02, -0.08)), Vector((sx * 0.3, -0.04, -0.28))], [0.035, 0.028, 0.01],
                "prop:bone", _shade(bone, 0.92), segments=5)
    return None


@generator
def bone_serpent_tail(mb: MeshBuilder, p, rng):
    """The tail: a run of shrinking vertebrae ending in a bony barb."""
    bone = p.get("bone", BONE)
    for k in range(4):
        y = 0.25 - k * 0.2
        r = 0.16 - k * 0.03
        mb.lathe([(r * 0.6, -0.08), (r, -0.04), (r, 0.04), (r * 0.6, 0.08)], "prop:bone", bone, segments=10,
                 matrix=Matrix.Translation((0, y, 0)) @ Matrix.Rotation(-math.pi / 2, 4, "X"))
    mb.cylinder((0, -0.5, 0), (0, -0.85, 0.05), 0.07, 0.0, "prop:bone", bone, segments=6)
    return None


EATER_BONES = {"body": ((0, -0.2, 0), (0, 0.2, 0), None), "tail": ((0, -0.22, 0), (0, -0.7, 0), "body")}


@generator
def eater_of_souls(mb: MeshBuilder, p, rng):
    """The eater of souls: a segmented worm body that ends in a gaping four-lipped mouth ringed with teeth."""
    skin, inner = p.get("skin", "#6a5a8a"), p.get("inner", "#c04a8a")
    mb.bone = "body"
    organic(mb, lambda b: (b.ellipsoid((0, 0.12, 0), (0.24, 0.26, 0.24)), b.ball((0, 0.36, 0), 0.17, negative=True)),
            "prop:flesh", skin, tris=1400, resolution=0.025, paint=lambda q, n: _mixc(_shade(skin, 0.7), skin, 0.5 + 0.5 * n.z))
    for k in range(4):  # lips
        a = k * math.pi / 2 + math.pi / 4
        d = Vector((math.cos(a), 0, math.sin(a)))
        mb.tube([d * 0.18 + Vector((0, 0.32, 0)), d * 0.24 + Vector((0, 0.42, 0)), d * 0.2 + Vector((0, 0.5, 0))], [0.06, 0.05, 0.015],
                "prop:flesh", skin, segments=6)
    mb.sphere((0, 0.25, 0), 0.12, "prop:flesh", inner, subdivisions=2)
    teeth_ring(mb, (0, 0.36, 0), 0.15, (0, 1, 0), 10, 0.07, 0.016, rng=rng)
    mb.bone = "tail"
    for k in range(4):
        mb.sphere((0.03 * math.sin(k), -0.18 - k * 0.15, 0), 0.17 - k * 0.03, "prop:flesh", skin if k % 2 else _shade(skin, 0.8), subdivisions=2)
    mb.bone = None

    def actions(arm):
        _action(arm, "fly", 0.7, {0.0: {"tail": (0, 0, 0.35)}, 0.5: {"tail": (0, 0, -0.35)}, 1.0: {"tail": (0, 0, 0.35)}})
    return {"rig": EATER_BONES, "actions": actions}


CRIMERA_BONES = {"body": ((0, -0.15, 0), (0, 0.15, 0), None),
                 **{f"leg.{k}": ((sx * 0.12, -0.05, -0.12 + i * 0.04), (sx * 0.3, -0.25, -0.35 + i * 0.05), "body")
                    for i, (k, sx) in enumerate((("R1", 1), ("L1", -1), ("R2", 1), ("L2", -1)))}}


@generator
def crimera(mb: MeshBuilder, p, rng):
    """The crimera: a floating lump of crimson flesh with a face (two bulging eyes over a lipless grin of teeth) and
    four thin dangling legs."""
    flesh, eyes = p.get("flesh", "#b83a3a"), p.get("eyes", "#f0e8c8")
    mb.bone = "body"
    organic(mb, lambda b: (b.ellipsoid((0, 0, 0), (0.28, 0.24, 0.3)), b.ellipsoid((0, 0.12, -0.12), (0.22, 0.14, 0.12))),
            "prop:flesh", flesh, tris=1400, resolution=0.025, paint=lambda q, n: _mixc(_shade(flesh, 0.6), flesh, 0.55 + 0.45 * n.z))
    for sx in (1, -1):
        mb.sphere((sx * 0.11, 0.2, 0.08), 0.075, "prop:plain", eyes, subdivisions=2)
        mb.sphere((sx * 0.11, 0.27, 0.08), 0.03, "prop:plain", "#1a0a0a", subdivisions=1)
    mb.sphere((0, 0.24, -0.1), 0.09, "prop:flesh", "#3a0a0a", subdivisions=1, scale=(1.5, 0.5, 0.6))  # mouth
    teeth_row(mb, (-0.1, 0.28, -0.07), (0.1, 0.28, -0.07), 7, 0.035, (0, 0, -1), 0.012, rng=rng)
    teeth_row(mb, (-0.1, 0.28, -0.13), (0.1, 0.28, -0.13), 7, 0.035, (0, 0, 1), 0.012, rng=rng)
    for i, (k, sx) in enumerate((("R1", 1), ("L1", -1), ("R2", 1), ("L2", -1))):
        mb.bone = f"leg.{k}"
        base = Vector((sx * 0.12, -0.05, -0.12 + i * 0.04))
        mb.tube([base, base + Vector((sx * 0.12, -0.08, -0.1)), base + Vector((sx * 0.18, -0.2, -0.24))], [0.025, 0.018, 0.006], "prop:flesh", flesh, segments=5)
    mb.bone = None

    def actions(arm):
        dangle = lambda a: {f"leg.{k}": (a * (1 if k[1] == "1" else -1), 0, 0) for k in ("R1", "L1", "R2", "L2")}
        _action(arm, "fly", 0.8, {0.0: {**dangle(0.3), "body": (0.05, 0, 0)}, 0.5: {**dangle(-0.3), "body": (-0.05, 0, 0)},
                                  1.0: {**dangle(0.3), "body": (0.05, 0, 0)}})
    return {"rig": CRIMERA_BONES, "actions": actions}


ELEMENTAL_BONES = {"core": ((0, 0, 0.6), (0, 0, 0.8), None), "ring": ((0, 0, 0.6), (0, 0.2, 0.6), "core")}


@generator
def elemental(mb: MeshBuilder, p, rng):
    """An elemental: angular rock (or ice) shards orbiting a glowing core, bigger shards forming head and shoulders."""
    rock, core = p.get("color", "#3a4a8a"), p.get("core", "#8ae0ff")
    layer = "prop:crystal" if p.get("ice") else "prop:stone"
    mb.bone = "core"
    mb.sphere((0, 0, 0.6), 0.14, "prop:plain:glow", core, subdivisions=2)
    mb.sphere((0, 0.02, 0.95), 0.17, layer, rock, subdivisions=1, jitter=0.18, rng=rng, smooth=False)   # head
    for sx in (1, -1):
        mb.sphere((sx * 0.06, 0.15, 0.97), 0.025, "prop:plain:glow", core, subdivisions=1)
        mb.sphere((sx * 0.3, 0, 0.8), 0.15, layer, rock, subdivisions=1, jitter=0.2, rng=rng, smooth=False)  # shoulders
    mb.bone = "ring"
    for k in range(9):
        a = k * 2 * math.pi / 9
        r = 0.32 + 0.06 * math.sin(k * 2.3)
        c = Vector((math.cos(a) * r, math.sin(a) * r, 0.5 + 0.12 * math.sin(k * 1.7)))
        size = 0.07 + 0.03 * ((k * 7) % 3)
        mb.sphere(tuple(c), size, layer, rock, subdivisions=1, jitter=0.25, rng=rng, smooth=False, scale=(1, 1, 1.5))
    mb.bone = None

    def actions(arm):
        _action(arm, "fly", 3.0, {0.0: {"ring": (0, 0, 0)}, 0.5: {"ring": (0, 0, math.pi)}, 1.0: {"ring": (0, 0, 2 * math.pi)}})
    return {"rig": ELEMENTAL_BONES, "actions": actions}


ANTLION_BONES = {"head": ((0, 0, 0.1), (0, 0.2, 0.3), None),
                 "jaw.R": ((0.08, 0.2, 0.32), (0.18, 0.45, 0.36), "head"), "jaw.L": ((-0.08, 0.2, 0.32), (-0.18, 0.45, 0.36), "head")}


@generator
def antlion(mb: MeshBuilder, p, rng):
    """An antlion lying in wait: a sand mound with the armoured head poking out and two huge serrated mandibles."""
    shell, sand = p.get("shell", "#c8a060"), p.get("sand", "#d8c08a")
    mb.lathe([(0.55, 0.0), (0.5, 0.05), (0.32, 0.14), (0.0, 0.17)], "prop:stone", sand, segments=16)   # the sand mound
    mb.bone = "head"
    mb.sphere((0, 0.05, 0.25), 0.2, "prop:chitin", shell, subdivisions=2, scale=(1.1, 1.0, 0.75), shade=0.4)
    for sx in (1, -1):
        mb.sphere((sx * 0.1, 0.18, 0.32), 0.035, "prop:plain", "#1a1208", subdivisions=1)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"jaw.{side}"
        pts = [Vector((sx * 0.08, 0.18, 0.3)), Vector((sx * 0.2, 0.32, 0.34)), Vector((sx * 0.16, 0.48, 0.36)), Vector((sx * 0.04, 0.55, 0.35))]
        mb.tube(pts, [0.04, 0.035, 0.025, 0.005], "prop:chitin", _shade(shell, 0.6), segments=6)
        for k in range(3):
            q = pts[1].lerp(pts[2], k / 2)
            fang(mb, q, q + Vector((-sx * 0.05, 0.01, 0)), 0.012)
    mb.bone = None

    def actions(arm):
        snap = lambda a: {"jaw.R": (0, 0, a), "jaw.L": (0, 0, -a)}
        _action(arm, "idle", 2.0, {0.0: {**snap(0.0), "head": (0, 0, 0)}, 0.5: {**snap(0.15), "head": (0.05, 0, 0)}, 1.0: {**snap(0.0), "head": (0, 0, 0)}})
        _action(arm, "attack", 0.5, {0.0: snap(0.0), 0.3: snap(0.6), 0.6: snap(-0.1), 1.0: snap(0.0)})
    return {"rig": ANTLION_BONES, "actions": actions}


def _shade(hexv, k):
    h = hexv.lstrip("#")
    return "#" + "".join(f"{min(255, int(int(h[i:i + 2], 16) * k)):02x}" for i in (0, 2, 4))


def _mixc(a, b, t):
    ha, hb = a.lstrip("#"), b.lstrip("#")
    t = max(0.0, min(1.0, t))
    return "#" + "".join(f"{int(int(ha[i:i + 2], 16) * (1 - t) + int(hb[i:i + 2], 16) * t):02x}" for i in (0, 2, 4))
