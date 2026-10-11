"""Creatures with their own body plans (P3-43): canines (the wolf and hellhound), eight-legged spiders, a lizard, a
tortoise, the mimic (a toothy chest on stubby legs), the snow flinx (a fur ball) and a shark. Rigid parts on small
rigs with looping walk/idle/attack actions, like bgen.fauna. Front faces Blender +Y; the origin is the bottom of the
creature's hitbox; sizes in metres fit the enemies' boxes (cells are 0.5 m)."""
import math

from mathutils import Matrix, Vector

from . import generator
from .creatures import _action
from .detail import teeth_row
from .mesh import MeshBuilder


def _legs4(spread, front, back, hip):
    return {f"leg.{k}": ((sx * spread, y, hip), (sx * spread, y, 0.0), "body")
            for k, sx, y in (("FR", 1, front), ("FL", -1, front), ("BR", 1, back), ("BL", -1, back))}


def _gait(arm, legs, swing, extra=None, period=0.5, idle=None, attack=None):
    """A trotting walk (diagonal pairs), a breathing idle and a lunge."""
    def pose(a, more=None):
        return {**{b: (a * (1 if b.endswith(("FR", "BL", "R1", "L2", "R3", "L4")) else -1), 0, 0) for b in legs}, **(more or {})}
    _action(arm, "walk", period, {0.0: pose(swing, extra), 0.5: pose(-swing, extra), 1.0: pose(swing, extra)})
    _action(arm, "idle", 2.5, idle or {0.0: {"body": (0.0, 0, 0)}, 0.5: {"body": (0.03, 0, 0)}, 1.0: {"body": (0.0, 0, 0)}})
    if attack:
        _action(arm, "attack", 0.5, attack)


CANINE_BONES = {"body": ((0, -0.3, 0.6), (0, 0.3, 0.6), None), "head": ((0, 0.35, 0.7), (0, 0.6, 0.85), "body"),
                "tail": ((0, -0.38, 0.66), (0, -0.7, 0.5), "body"), **_legs4(0.13, 0.28, -0.3, 0.58)}


@generator
def canine(mb: MeshBuilder, p, rng):
    """A wolf: deep chest, tucked waist, a long muzzle with fangs, pointed ears, a bushy tail; ``eyes`` glow."""
    fur, belly, eyes = p.get("fur", "#7a7a86"), p.get("belly", "#b8b8c0"), p.get("eyes", "#ffd040")
    mb.bone = "body"
    mb.sphere((0, 0.18, 0.62), 0.2, "prop:fur", fur, subdivisions=2, scale=(0.95, 1.25, 1.1), shade=0.35)   # chest
    mb.sphere((0, -0.2, 0.62), 0.16, "prop:fur", fur, subdivisions=2, scale=(0.9, 1.3, 0.95), shade=0.35)   # loins
    mb.sphere((0, 0.15, 0.5), 0.13, "prop:fur", belly, subdivisions=1, scale=(0.8, 1.3, 0.6))
    if p.get("mane"):
        for k in range(7):
            a = (k - 3) * 0.35
            mb.cylinder((math.sin(a) * 0.12, 0.25, 0.74), (math.sin(a) * 0.2, 0.15, 0.95), 0.05, 0.0, "prop:plain:glow", p["mane"], segments=5)
    mb.bone = "head"
    mb.sphere((0, 0.42, 0.82), 0.12, "prop:fur", fur, subdivisions=2, scale=(1.0, 1.1, 0.95))
    mb.tube([Vector((0, 0.48, 0.79)), Vector((0, 0.6, 0.76)), Vector((0, 0.68, 0.74))], [0.075, 0.055, 0.035], "prop:fur", fur, segments=8)
    mb.sphere((0, 0.69, 0.75), 0.022, "prop:plain", "#141010", subdivisions=1)  # nose
    for sx in (1, -1):
        mb.cylinder((sx * 0.07, 0.39, 0.9), (sx * 0.08, 0.37, 1.02), 0.04, 0.0, "prop:fur", fur, segments=5)      # ear
        mb.sphere((sx * 0.06, 0.52, 0.85), 0.018, "prop:plain:glow", eyes, subdivisions=1)
        mb.cylinder((sx * 0.025, 0.64, 0.73), (sx * 0.025, 0.65, 0.69), 0.01, 0.0, "prop:plain", "#f0ece0", segments=4)  # fang
    mb.bone = "tail"
    mb.tube([Vector((0, -0.36, 0.66)), Vector((0, -0.52, 0.62)), Vector((0, -0.66, 0.5)), Vector((0, -0.7, 0.4))],
            [0.05, 0.075, 0.06, 0.015], "prop:fur", fur, segments=7)
    for bone, x, y in (("leg.FR", 0.12, 0.25), ("leg.FL", -0.12, 0.25), ("leg.BR", 0.12, -0.28), ("leg.BL", -0.12, -0.28)):
        mb.bone = bone
        back = y < 0
        hock = Vector((x, y - (0.06 if back else -0.01), 0.24))
        mb.tube([Vector((x, y, 0.6)), Vector((x, y + (0.04 if back else 0.0), 0.4)), hock], [0.075 if back else 0.06, 0.045, 0.03], "prop:fur", fur, segments=6)
        mb.tube([hock, Vector((x, y + 0.01, 0.04))], [0.03, 0.028], "prop:fur", fur, segments=6)
        mb.sphere((x, y + 0.04, 0.03), 0.04, "prop:fur", _shade(fur, 0.7), subdivisions=1, scale=(1, 1.4, 0.6))  # paw
    mb.bone = None

    def actions(arm):
        legs = [b for b in CANINE_BONES if b.startswith("leg")]
        _gait(arm, legs, 0.5, {"tail": (0.2, 0, 0), "head": (-0.05, 0, 0)}, 0.42,
              idle={0.0: {"tail": (0, 0, 0.3), "head": (0, 0, 0)}, 0.5: {"tail": (0, 0, -0.3), "head": (0.08, 0, 0)}, 1.0: {"tail": (0, 0, 0.3), "head": (0, 0, 0)}},
              attack={0.0: {"head": (0, 0, 0), "body": (0, 0, 0)}, 0.4: {"head": (-0.5, 0, 0), "body": (-0.15, 0, 0)}, 1.0: {"head": (0, 0, 0), "body": (0, 0, 0)}})
    return {"rig": CANINE_BONES, "actions": actions}


def _spider_bones():
    bones = {"body": ((0, -0.05, 0.3), (0, 0.15, 0.3), None), "abdomen": ((0, -0.08, 0.32), (0, -0.4, 0.36), "body")}
    for i in range(4):
        y = 0.12 - i * 0.07
        for side, sx in (("R", 1), ("L", -1)):
            bones[f"leg.{side}{i + 1}"] = ((sx * 0.1, y, 0.3), (sx * 0.5, y + (0.25 - i * 0.16), 0.0), "body")
    return bones


SPIDER_BONES = _spider_bones()


@generator
def spider(mb: MeshBuilder, p, rng):
    """An eight-legged spider: cephalothorax with eight eyes and fangs, a swollen abdomen with a marking, and jointed
    legs arching high over the body."""
    shell, mark, eyes = p.get("shell", "#1a1a1a"), p.get("mark", "#c02020"), p.get("eyes", "#ff3020")
    hair = "prop:fur" if p.get("hairy") else "prop:chitin"
    mb.bone = "body"
    mb.sphere((0, 0.06, 0.3), 0.13, "prop:chitin", shell, subdivisions=2, scale=(1.0, 1.15, 0.75))
    for k in range(8):
        a = (k % 4 - 1.5) * 0.28
        mb.sphere((math.sin(a) * 0.09, 0.17 + (0.0 if k < 4 else -0.02), 0.36 + (0.02 if k < 4 else 0.05)), 0.016 if k in (1, 2) else 0.011,
                  "prop:plain:glow", eyes, subdivisions=1)
    for sx in (1, -1):
        mb.tube([Vector((sx * 0.04, 0.18, 0.26)), Vector((sx * 0.045, 0.24, 0.2)), Vector((sx * 0.025, 0.25, 0.14))], [0.028, 0.02, 0.003],
                "prop:chitin", _shade(shell, 0.6), segments=5)
    mb.bone = "abdomen"
    mb.sphere((0, -0.3, 0.36), 0.2, hair, shell, subdivisions=2, scale=(0.95, 1.3, 0.9), shade=0.3)
    mb.sphere((0, -0.3, 0.53), 0.06, "prop:plain", mark, subdivisions=1, scale=(0.8, 1.6, 0.25))  # hourglass marking
    for i in range(4):
        y = 0.12 - i * 0.07
        reach = 0.25 - i * 0.16
        for side, sx in (("R", 1), ("L", -1)):
            mb.bone = f"leg.{side}{i + 1}"
            hip = Vector((sx * 0.1, y, 0.3))
            knee = Vector((sx * 0.32, y + reach * 0.5, 0.5))
            foot = Vector((sx * 0.55, y + reach, 0.0))
            mb.tube([hip, knee, knee.lerp(foot, 0.5) + Vector((sx * 0.02, 0, 0.04)), foot], [0.026, 0.02, 0.014, 0.005], hair, shell, segments=5)
    mb.bone = None

    def actions(arm):
        legs = [b for b in SPIDER_BONES if b.startswith("leg")]
        _gait(arm, legs, 0.35, None, 0.32, attack={0.0: {"body": (0, 0, 0)}, 0.35: {"body": (-0.35, 0, 0), "leg.R1": (-0.8, 0, 0), "leg.L1": (-0.8, 0, 0)},
                                                  1.0: {"body": (0, 0, 0)}})
    return {"rig": SPIDER_BONES, "actions": actions}


LIZARD_BONES = {"body": ((0, -0.25, 0.18), (0, 0.25, 0.18), None), "head": ((0, 0.28, 0.2), (0, 0.5, 0.2), "body"),
                "tail": ((0, -0.28, 0.18), (0, -0.75, 0.1), "body"), **_legs4(0.14, 0.2, -0.2, 0.17)}


@generator
def lizard(mb: MeshBuilder, p, rng):
    """A low lizard: flat scaled body, splayed legs, a wedge head and a long tapering tail (the salamander)."""
    skin, belly, spots = p.get("skin", "#c8784a"), p.get("belly", "#e8c890"), p.get("spots", "#3a2a1a")
    mb.bone = "body"
    mb.sphere((0, 0, 0.17), 0.13, "prop:chitin", skin, subdivisions=2, scale=(1.0, 2.2, 0.6), shade=0.4)
    for k in range(5):
        mb.sphere(((k % 2 - 0.5) * 0.08, -0.2 + k * 0.1, 0.24), 0.025, "prop:plain", spots, subdivisions=1, scale=(1, 1, 0.3))
    mb.bone = "head"
    mb.sphere((0, 0.36, 0.2), 0.08, "prop:chitin", skin, subdivisions=2, scale=(1.0, 1.6, 0.6))
    for sx in (1, -1):
        mb.sphere((sx * 0.05, 0.38, 0.25), 0.018, "prop:plain:glow", p.get("eyes", "#ffd040"), subdivisions=1)
    mb.bone = "tail"
    mb.tube([Vector((0, -0.25, 0.17)), Vector((0, -0.45, 0.14)), Vector((0.05, -0.65, 0.1)), Vector((0.1, -0.85, 0.07))],
            [0.08, 0.055, 0.03, 0.006], "prop:chitin", skin, segments=7)
    for bone, x, y in (("leg.FR", 0.1, 0.18), ("leg.FL", -0.1, 0.18), ("leg.BR", 0.1, -0.18), ("leg.BL", -0.1, -0.18)):
        mb.bone = bone
        sx = 1 if x > 0 else -1
        elbow = Vector((x + sx * 0.12, y, 0.15))
        mb.tube([Vector((x, y, 0.17)), elbow, Vector((x + sx * 0.16, y + 0.04, 0.0))], [0.03, 0.022, 0.018], "prop:chitin", belly, segments=5)
    mb.bone = None

    def actions(arm):
        legs = [b for b in LIZARD_BONES if b.startswith("leg")]
        _gait(arm, legs, 0.45, {"tail": (0, 0, 0.25)}, 0.36,
              attack={0.0: {"head": (0, 0, 0)}, 0.4: {"head": (-0.4, 0, 0)}, 1.0: {"head": (0, 0, 0)}})
    return {"rig": LIZARD_BONES, "actions": actions}


TORTOISE_BONES = {"body": ((0, -0.3, 0.4), (0, 0.3, 0.4), None), "head": ((0, 0.45, 0.35), (0, 0.7, 0.42), "body"),
                  **_legs4(0.3, 0.3, -0.3, 0.3)}


@generator
def tortoise(mb: MeshBuilder, p, rng):
    """A giant tortoise: a high domed shell of hexagonal scutes with a rim, a beaked head and stumpy legs."""
    shell, scute, skin = p.get("shell", "#5a7a3a"), p.get("scute", "#8a6a3a"), p.get("skin", "#8a8a5a")
    mb.bone = "body"
    mb.sphere((0, 0, 0.42), 0.5, "prop:chitin", shell, subdivisions=3, scale=(0.95, 1.15, 0.72), shade=0.3)
    mb.cylinder((0, 0, 0.2), (0, 0, 0.27), 0.58, 0.55, "prop:chitin", _shade(shell, 0.7), segments=20)   # rim
    for k in range(7):
        a = k * 2 * math.pi / 7
        mb.cylinder((math.cos(a) * 0.3, math.sin(a) * 0.36, 0.67), (math.cos(a) * 0.33, math.sin(a) * 0.39, 0.7), 0.13, 0.11,
                    "prop:chitin", scute, segments=6)
    mb.cylinder((0, 0, 0.76), (0, 0, 0.79), 0.15, 0.13, "prop:chitin", scute, segments=6)
    mb.bone = "head"
    mb.tube([Vector((0, 0.45, 0.33)), Vector((0, 0.6, 0.38)), Vector((0, 0.72, 0.4))], [0.1, 0.085, 0.075], "prop:chitin", skin, segments=8)
    mb.sphere((0, 0.76, 0.41), 0.09, "prop:chitin", skin, subdivisions=2, scale=(0.9, 1.1, 0.85))
    for sx in (1, -1):
        mb.sphere((sx * 0.06, 0.8, 0.45), 0.016, "prop:plain", "#101010", subdivisions=1)
    for bone, x, y in (("leg.FR", 0.32, 0.3), ("leg.FL", -0.32, 0.3), ("leg.BR", 0.32, -0.3), ("leg.BL", -0.32, -0.3)):
        mb.bone = bone
        mb.cylinder((x, y, 0.3), (x * 1.08, y, 0.0), 0.11, 0.12, "prop:chitin", skin, segments=8)
    mb.bone = None

    def actions(arm):
        legs = [b for b in TORTOISE_BONES if b.startswith("leg")]
        _gait(arm, legs, 0.3, None, 0.8, attack={0.0: {"head": (0, 0, 0)}, 0.4: {"head": (-0.5, 0, 0)}, 1.0: {"head": (0, 0, 0)}})
    return {"rig": TORTOISE_BONES, "actions": actions}


MIMIC_BONES = {"body": ((0, -0.2, 0.25), (0, 0.2, 0.25), None), "lid": ((0, -0.28, 0.62), (0, 0.28, 0.62), "body"),
               **_legs4(0.25, 0.15, -0.15, 0.22)}


@generator
def mimic(mb: MeshBuilder, p, rng):
    """A treasure chest that is a mouth: banded box and lid hinged at the back, rows of fangs along both rims, a long
    tongue and stubby clawed legs; the lid gapes when it attacks."""
    wood, band, gold = p.get("wood", "#8a5a2a"), p.get("band", "#4a4a52"), p.get("gold", "#e8c040")
    w, d, h = 0.72, 0.5, 0.42
    mb.bone = "body"
    mb.box((-w / 2, -d / 2, 0.18), (w / 2, d / 2, 0.18 + h), "prop:planks", wood)
    mb.box((-w / 2 + 0.03, -d / 2 + 0.03, 0.18 + h - 0.06), (w / 2 - 0.03, d / 2 - 0.03, 0.18 + h + 0.001), "prop:flesh", "#4a0a12")  # maw
    for x in (-0.24, 0.24):
        mb.box((x - 0.035, -d / 2 - 0.01, 0.18), (x + 0.035, d / 2 + 0.01, 0.18 + h + 0.005), "prop:iron:metal", band)
    teeth_row(mb, (-w / 2 + 0.04, d / 2 - 0.02, 0.18 + h), (w / 2 - 0.04, d / 2 - 0.02, 0.18 + h), 9, 0.07, (0, 0, 1), 0.02, rng=rng)
    mb.tube([Vector((0, 0.0, 0.18 + h)), Vector((0.03, 0.2, 0.18 + h + 0.03)), Vector((0.02, 0.36, 0.18 + h - 0.06)), Vector((0, 0.42, 0.3))],
            [0.05, 0.045, 0.035, 0.02], "prop:flesh", "#d04a6a", segments=6)  # tongue
    mb.bone = "lid"
    lid = [(-d / 2, 0), (d / 2, 0), (d / 2 - 0.02, 0.1), (d / 4, 0.17), (-d / 4, 0.17), (-d / 2 + 0.02, 0.1)]
    mb.extrude_polygon(lid, w, "prop:planks", wood, matrix=Matrix.Translation((0, 0, 0.18 + h + 0.01)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    teeth_row(mb, (-w / 2 + 0.04, d / 2 - 0.02, 0.18 + h + 0.01), (w / 2 - 0.04, d / 2 - 0.02, 0.18 + h + 0.01), 9, 0.07, (0, 0, -1), 0.02, rng=rng)
    mb.box((-0.06, d / 2, 0.18 + h - 0.05), (0.06, d / 2 + 0.03, 0.18 + h + 0.06), "prop:gold", gold)  # latch
    for sx in (1, -1):
        mb.sphere((sx * 0.14, d / 2 - 0.05, 0.18 + h + 0.13), 0.03, "prop:plain:glow", p.get("eyes", "#ffe060"), subdivisions=1)
    for bone, x, y in (("leg.FR", 0.25, 0.15), ("leg.FL", -0.25, 0.15), ("leg.BR", 0.25, -0.15), ("leg.BL", -0.25, -0.15)):
        mb.bone = bone
        mb.tube([Vector((x, y, 0.22)), Vector((x * 1.15, y, 0.1)), Vector((x * 1.2, y + 0.04, 0.0))], [0.05, 0.04, 0.03], "prop:planks", _shade(wood, 0.7), segments=6)
    mb.bone = None

    def actions(arm):
        legs = [b for b in MIMIC_BONES if b.startswith("leg")]
        _gait(arm, legs, 0.5, {"lid": (0.25, 0, 0)}, 0.3,
              idle={0.0: {"lid": (0.0, 0, 0)}, 0.5: {"lid": (0.06, 0, 0)}, 1.0: {"lid": (0.0, 0, 0)}},
              attack={0.0: {"lid": (0.0, 0, 0)}, 0.3: {"lid": (0.9, 0, 0), "body": (-0.1, 0, 0)}, 0.6: {"lid": (0.0, 0, 0)}, 1.0: {"lid": (0.0, 0, 0)}})
    return {"rig": MIMIC_BONES, "actions": actions}


FLINX_BONES = {"body": ((0, -0.1, 0.33), (0, 0.1, 0.33), None), **_legs4(0.14, 0.08, -0.08, 0.12)}


@generator
def flinx(mb: MeshBuilder, p, rng):
    """The snow flinx: a round ball of shaggy fur (tufts all over), tiny dark eyes and feet."""
    fur = p.get("fur", "#f0f4ff")
    mb.bone = "body"
    mb.sphere((0, 0, 0.33), 0.3, "prop:fur", fur, subdivisions=3, jitter=0.04, rng=rng, shade=0.3)
    for k in range(26):  # shaggy tufts
        z = 1 - 2 * (k + 0.5) / 26
        r = math.sqrt(max(0.0, 1 - z * z))
        a = k * 2.399963
        d = Vector((math.cos(a) * r, math.sin(a) * r, z))
        base = Vector((0, 0, 0.33)) + d * 0.27
        mb.cylinder(tuple(base), tuple(base + d * 0.09 + Vector((0, 0, -0.03))), 0.05, 0.0, "prop:fur", fur, segments=5)
    for sx in (1, -1):
        mb.sphere((sx * 0.1, 0.26, 0.4), 0.03, "prop:plain", "#101018", subdivisions=1)
    mb.sphere((0, 0.29, 0.33), 0.025, "prop:plain", "#3a3a4a", subdivisions=1)
    for bone, x, y in (("leg.FR", 0.14, 0.08), ("leg.FL", -0.14, 0.08), ("leg.BR", 0.14, -0.08), ("leg.BL", -0.14, -0.08)):
        mb.bone = bone
        mb.sphere((x, y + 0.03, 0.05), 0.06, "prop:fur", _shade(fur, 0.85), subdivisions=1, scale=(1, 1.3, 0.7))
    mb.bone = None

    def actions(arm):
        legs = [b for b in FLINX_BONES if b.startswith("leg")]
        _gait(arm, legs, 0.6, {"body": (0.1, 0, 0)}, 0.3)
    return {"rig": FLINX_BONES, "actions": actions}


SHARK_BONES = {"body": ((0, -0.1, 0.25), (0, 0.3, 0.25), None), "tail": ((0, -0.12, 0.25), (0, -0.55, 0.25), "body")}


@generator
def shark(mb: MeshBuilder, p, rng):
    """A shark: torpedo body, pale belly, a tall dorsal fin, pectoral fins, a crescent tail and a toothy underslung jaw."""
    skin, belly = p.get("skin", "#6a7a8a"), p.get("belly", "#e8ecf0")
    mb.bone = "body"
    mb.sphere((0, 0.05, 0.25), 0.17, "prop:chitin", skin, subdivisions=3, scale=(0.85, 2.9, 0.95), shade=0.45)
    mb.sphere((0, 0.12, 0.17), 0.12, "prop:plain", belly, subdivisions=2, scale=(0.75, 2.6, 0.5))
    mb.extrude_polygon([(-0.05, 0.37), (0.12, 0.37), (-0.12, 0.62)], 0.03, "prop:chitin", skin,
                       matrix=Matrix.Rotation(math.pi / 2, 4, "Z") @ Matrix.Translation((0, 0, 0.0)))  # dorsal fin
    for sx in (1, -1):
        mb.extrude_polygon([(0.0, 0.0), (0.22, -0.06), (0.06, 0.06)], 0.015, "prop:chitin", skin,
                           matrix=Matrix.Translation((sx * 0.1, 0.18, 0.17)) @ Matrix.Rotation(sx * 0.3, 4, "Y") @ Matrix.Diagonal((sx, 1, 1, 1))
                           @ Matrix.Rotation(math.pi / 2, 4, "X"))
        mb.sphere((sx * 0.085, 0.45, 0.29), 0.018, "prop:plain", "#050505", subdivisions=1)
    teeth_row(mb, (-0.07, 0.48, 0.2), (0.07, 0.48, 0.2), 7, 0.03, (0, 0, 1), 0.01, rng=rng)
    mb.bone = "tail"
    mb.tube([Vector((0, -0.25, 0.25)), Vector((0, -0.45, 0.26)), Vector((0, -0.55, 0.27))], [0.1, 0.05, 0.03], "prop:chitin", skin, segments=8)
    mb.extrude_polygon([(-0.02, 0.27), (0.25, 0.48), (0.05, 0.27), (0.18, 0.08)], 0.02, "prop:chitin", skin,
                       matrix=Matrix.Translation((0, -0.55, 0)) @ Matrix.Rotation(math.pi / 2, 4, "Z"))
    mb.bone = None

    def actions(arm):
        swim = {0.0: {"tail": (0, 0, 0.35), "body": (0, 0, -0.08)}, 0.5: {"tail": (0, 0, -0.35), "body": (0, 0, 0.08)},
                1.0: {"tail": (0, 0, 0.35), "body": (0, 0, -0.08)}}
        _action(arm, "fly", 0.6, swim)
        _action(arm, "attack", 0.4, {0.0: {"body": (0, 0, 0)}, 0.4: {"body": (-0.25, 0, 0), "tail": (0, 0, 0.5)}, 1.0: {"body": (0, 0, 0)}})
    return {"rig": SHARK_BONES, "actions": actions}


def _shade(hexv, k):
    h = hexv.lstrip("#")
    return "#" + "".join(f"{min(255, int(int(h[i:i + 2], 16) * k)):02x}" for i in (0, 2, 4))
