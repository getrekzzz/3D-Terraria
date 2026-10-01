"""Boss trophies and treasure bags (P4-04). A trophy is a walnut plaque in a bevelled gold frame with the boss's own
creature model, built by its generator and fitted onto the plaque as a bust; a treasure bag is a tied cloth sack with a
gold clasp and an emblem. Front faces Blender +Y; a trophy's back lies on the footprint's -Y face (the wall)."""
import math

from mathutils import Matrix, Vector

from . import generator
from .mesh import MeshBuilder


def _creature_generator(name):
    """The creature generator `name`, looked up like build_all.build: the bosses modules first, then the rest, then the
    @generator registry."""
    from . import (GENERATORS, beasts, bosses, bosses_hard, bosses_lunar, bosses_pre, bosses_pre2, bosses_pre3, creatures, critters,
                   fauna, festive, horrors, lunar, mechs)
    for module in (bosses, bosses_lunar, bosses_hard, bosses_pre3, bosses_pre2, bosses_pre, critters, festive, lunar, horrors, mechs,
                   beasts, fauna, creatures):
        if name in module.GENERATORS:
            return module.GENERATORS[name]
    return GENERATORS[name]


def _bust(mb: MeshBuilder, creature, params, rng, center, width, depth):
    """Builds a creature model into `mb` and fits it into a `width` square (x, z) centred on `center`, at most `depth`
    deep, its back at center.y. Rig weights are dropped: the bust is a rigid prop."""
    before = set(mb.bm.verts)
    bone, bones = mb.bone, list(mb.bones)
    _creature_generator(creature)(mb, params, rng)
    mb.bone, mb.bones[:] = bone, bones
    verts = [v for v in mb.bm.verts if v not in before]
    if not verts:
        return
    for v in verts:
        v[mb.deform].clear()
    lo = Vector((min(v.co.x for v in verts), min(v.co.y for v in verts), min(v.co.z for v in verts)))
    hi = Vector((max(v.co.x for v in verts), max(v.co.y for v in verts), max(v.co.z for v in verts)))
    size = hi - lo
    s = width / max(size.x, size.z, 1e-4)
    sy = min(s, depth / max(size.y, 1e-4))
    mid = (lo + hi) / 2
    for v in verts:
        c = v.co
        v.co = Vector((center[0] + (c.x - mid.x) * s, center[1] + (c.y - lo.y) * sy, center[2] + (c.z - mid.z) * s))


@generator
def trophy(mb: MeshBuilder, p, rng):
    """A wall trophy: params creature (generator) and creatureParams, frame colour, wood colour."""
    w, h, d = p["size"]
    back = -d / 2
    board = min(w, h) * 0.86
    cz = h / 2
    wood, gold = p.get("wood", "#9a6a44"), p.get("frame", "#ffffff")
    t = 0.05
    if p.get("creature"):  # first: a creature generator may transform everything built so far
        _bust(mb, p["creature"], p.get("creatureParams", {}), rng, (0.0, back + t + 0.01, cz + 0.05), board * 0.72, min(d * 0.9, 0.42))
    mb.box((-board / 2, back, cz - board / 2), (board / 2, back + t, cz + board / 2), "prop:planks", wood)
    rim = 0.1
    for sx in (-1, 1):  # bevelled frame: outer bars, then a thinner inner lip
        mb.box((sx * board / 2 - rim / 2, back, cz - board / 2 - rim / 2), (sx * board / 2 + rim / 2, back + t + 0.03, cz + board / 2 + rim / 2), "prop:gold", gold)
        mb.box((-board / 2 - rim / 2, back, cz + sx * board / 2 - rim / 2), (board / 2 + rim / 2, back + t + 0.03, cz + sx * board / 2 + rim / 2), "prop:gold", gold)
    lip = board - rim * 1.6
    for sx in (-1, 1):
        mb.box((sx * lip / 2 - 0.015, back + t, cz - lip / 2), (sx * lip / 2 + 0.015, back + t + 0.015, cz + lip / 2), "prop:gold", gold)
        mb.box((-lip / 2, back + t, cz + sx * lip / 2 - 0.015), (lip / 2, back + t + 0.015, cz + sx * lip / 2 + 0.015), "prop:gold", gold)
    # A small engraved name plate under the bust.
    mb.box((-0.18, back + t, cz - board / 2 + 0.06), (0.18, back + t + 0.02, cz - board / 2 + 0.14), "prop:gold", gold)


@generator
def treasure_bag(mb: MeshBuilder, p, rng):
    """A tied sack bulging with loot: params color (cloth), trim (band and drawstring), emblem (clasp jewel)."""
    cloth, trim, emblem = p.get("color", "#6a4a8a"), p.get("trim", "#e0b040"), p.get("emblem", "#ff4060")
    profile = [(0.0, 0.0), (0.09, 0.005), (0.13, 0.04), (0.145, 0.1), (0.13, 0.16), (0.085, 0.2), (0.045, 0.225), (0.05, 0.24), (0.075, 0.27),
               (0.06, 0.285), (0.0, 0.29)]
    jitter = Matrix.Diagonal((1.0 + rng.uniform(-0.04, 0.04), 0.92, 1.0, 1.0))
    mb.lathe(profile, "prop:cloth", cloth, segments=18, matrix=jitter)
    for k in range(6):  # folds where the neck is gathered
        a = k * math.pi / 3 + rng.uniform(-0.2, 0.2)
        mb.cylinder((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.23), (math.cos(a) * 0.075, math.sin(a) * 0.075, 0.28), 0.012, 0.008, "prop:cloth", cloth, segments=6)
    mb.cylinder((0, 0, 0.215), (0, 0, 0.235), 0.055, 0.055, "prop:cloth", trim, segments=16)  # drawstring
    for sx in (-1, 1):
        mb.cylinder((sx * 0.02, 0.05, 0.225), (sx * 0.06, 0.07, 0.15), 0.006, 0.006, "prop:cloth", trim, segments=5)
    mb.cylinder((0, 0, 0.1), (0, 0, 0.125), 0.147, 0.142, "prop:cloth", trim, segments=18)  # band
    mb.box((-0.035, 0.12, 0.085), (0.035, 0.15, 0.14), "prop:gold", "#ffffff")  # clasp
    mb.sphere((0, 0.152, 0.112), 0.02, "prop:crystal:glow", emblem, subdivisions=1, scale=(1, 0.5, 1))
