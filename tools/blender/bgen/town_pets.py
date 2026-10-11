"""Town pets (P5-20): a small quadruped body plan shared by the licensed Cat, Dog and Bunny, plus the license card.

Creatures: front faces Blender +Y, origin at the bottom of the hitbox, sizes in metres. One rig (body, head, tail,
four legs) with walk, idle, sit and lie actions. ``species`` picks the head, ears, tail and gait:
  cat:   pointed ears, whiskers, a long upright tail; slinking walk.
  dog:   floppy ears, a muzzle with a nose, a wagging tail; trotting walk.
  bunny: long upright ears, a cotton tail, short legs; hopping walk.
Params: species, fur, belly, eyes, size (overall scale).
pet_license(p): color (card), paw (print colour).
"""
from mathutils import Vector

from . import generator
from .creatures import _action
from .mesh import MeshBuilder


def _bones(s, hip):
    legs = {f"leg.{k}": ((sx * 0.08 * s, y * s, hip * s), (sx * 0.08 * s, y * s, 0.0), "body")
            for k, sx, y in (("FR", 1, 0.14), ("FL", -1, 0.14), ("BR", 1, -0.14), ("BL", -1, -0.14))}
    return {"body": ((0, -0.18 * s, hip * s), (0, 0.18 * s, hip * s), None),
            "head": ((0, 0.18 * s, (hip + 0.08) * s), (0, 0.3 * s, (hip + 0.2) * s), "body"),
            "tail": ((0, -0.2 * s, hip * s), (0, -0.4 * s, (hip + 0.1) * s), "body"), **legs}


def _actions(species):
    hop = species == "bunny"
    swing = 0.25 if hop else 0.5

    def legs(a, front_with_back=False):
        return {"leg.FR": (a, 0, 0), "leg.FL": (a if hop else -a, 0, 0),
                "leg.BR": (-a if not front_with_back else a, 0, 0), "leg.BL": (-a if hop else a, 0, 0)}

    wag = 0.45 if species == "dog" else 0.18

    def actions(arm):
        if hop:
            _action(arm, "walk", 0.45, {0.0: {**legs(swing), "body": (0.15, 0, 0), "tail": (0, 0, 0)},
                                        0.5: {**legs(-swing), "body": (-0.12, 0, 0), "tail": (0.2, 0, 0)},
                                        1.0: {**legs(swing), "body": (0.15, 0, 0), "tail": (0, 0, 0)}})
        else:
            _action(arm, "walk", 0.5, {0.0: {**legs(swing), "tail": (0, 0, wag)}, 0.5: {**legs(-swing), "tail": (0, 0, -wag)},
                                       1.0: {**legs(swing), "tail": (0, 0, wag)}})
        _action(arm, "idle", 2.4, {0.0: {"head": (0, 0, 0), "tail": (0, 0, wag)}, 0.5: {"head": (0.1, 0, 0.15), "tail": (0, 0, -wag)},
                                   1.0: {"head": (0, 0, 0), "tail": (0, 0, wag)}})
        sit = {"body": (0.5, 0, 0), "head": (-0.45, 0, 0), "leg.FR": (-0.5, 0, 0), "leg.FL": (-0.5, 0, 0),
               "leg.BR": (-1.2, 0, 0), "leg.BL": (-1.2, 0, 0), "tail": (-0.3, 0, 0)}
        _action(arm, "sit", 2.0, {0.0: sit, 1.0: sit})
        lie = {"body": (0, 0, 0), "head": (0.1, 0, 0), "leg.FR": (-1.4, 0, 0), "leg.FL": (-1.4, 0, 0),
               "leg.BR": (1.4, 0, 0), "leg.BL": (1.4, 0, 0), "tail": (0.4, 0, 0.3)}
        _action(arm, "lie", 3.0, {0.0: lie, 0.5: {**lie, "head": (0.18, 0, 0.05)}, 1.0: lie})
    return actions


@generator
def town_pet(mb: MeshBuilder, p, rng):
    """A small, round town pet (cat, dog or bunny) with big glossy eyes."""
    species = p.get("species", "cat")
    s = float(p.get("size", 1.0))
    fur, belly, eyes = p.get("fur", "#d89a5a"), p.get("belly", "#f4e8d8"), p.get("eyes", "#3a8a3a")
    hip = 0.2 if species == "bunny" else 0.26

    def v(x, y, z):
        return (x * s, y * s, z * s)

    mb.bone = "body"
    mb.sphere(v(0, 0.05, hip + 0.02), 0.13 * s, "prop:fur", fur, subdivisions=2, scale=(0.9, 1.35 if species != "bunny" else 1.1, 0.95), shade=0.3)
    mb.sphere(v(0, 0.05, hip - 0.04), 0.09 * s, "prop:fur", belly, subdivisions=1, scale=(0.8, 1.3, 0.6))
    mb.bone = "head"
    hz = hip + 0.17
    mb.sphere(v(0, 0.22, hz), 0.1 * s, "prop:fur", fur, subdivisions=2, scale=(1.05, 0.95, 0.95), shade=0.2)
    for sx in (1, -1):
        mb.sphere(v(sx * 0.045, 0.3, hz + 0.02), 0.022 * s, "prop:plain", "#ffffff", subdivisions=1)
        mb.sphere(v(sx * 0.047, 0.315, hz + 0.022), 0.014 * s, "prop:plain", eyes, subdivisions=1)
        if species == "cat":
            mb.cylinder(v(sx * 0.05, 0.2, hz + 0.07), v(sx * 0.07, 0.2, hz + 0.15), 0.035 * s, 0.0, "prop:fur", fur, segments=4, smooth=False)
            for k in (-1, 1):  # whiskers
                mb.cylinder(v(sx * 0.03, 0.31, hz - 0.02), v(sx * 0.13, 0.3, hz - 0.02 + 0.015 * k), 0.003 * s, 0.002 * s, "prop:plain", "#f0f0f0", segments=3)
        elif species == "dog":
            mb.sphere(v(sx * 0.085, 0.2, hz + 0.02), 0.035 * s, "prop:fur", _darker(fur), subdivisions=1, scale=(0.5, 0.9, 1.6))
        else:
            mb.tube([Vector(v(sx * 0.03, 0.2, hz + 0.07)), Vector(v(sx * 0.04, 0.19, hz + 0.17)), Vector(v(sx * 0.045, 0.17, hz + 0.26))],
                    [0.025 * s, 0.03 * s, 0.012 * s], "prop:fur", fur, segments=6)
    if species == "dog":
        mb.sphere(v(0, 0.31, hz - 0.03), 0.05 * s, "prop:fur", belly, subdivisions=1, scale=(1, 1.1, 0.8))
    mb.sphere(v(0, 0.33 if species != "dog" else 0.36, hz - 0.015), 0.014 * s, "prop:plain", "#2a1a1a" if species != "bunny" else "#e88a9a", subdivisions=1)
    mb.bone = "tail"
    if species == "bunny":
        mb.sphere(v(0, -0.15, hip + 0.04), 0.05 * s, "prop:fur", "#ffffff", subdivisions=1)
    elif species == "cat":
        mb.tube([Vector(v(0, -0.14, hip + 0.04)), Vector(v(0, -0.24, hip + 0.1)), Vector(v(0, -0.28, hip + 0.24))],
                [0.025 * s, 0.022 * s, 0.015 * s], "prop:fur", fur, segments=6)
    else:
        mb.tube([Vector(v(0, -0.14, hip + 0.05)), Vector(v(0, -0.22, hip + 0.12)), Vector(v(0, -0.25, hip + 0.18))],
                [0.03 * s, 0.025 * s, 0.012 * s], "prop:fur", fur, segments=6)
    leg = 0.025 if species != "bunny" else 0.03
    for bone, sx, y in (("leg.FR", 1, 0.14), ("leg.FL", -1, 0.14), ("leg.BR", 1, -0.1), ("leg.BL", -1, -0.1)):
        mb.bone = bone
        mb.tube([Vector(v(sx * 0.06, y, hip)), Vector(v(sx * 0.06, y, 0.03))], [leg * 1.3 * s, leg * s], "prop:fur", fur, segments=6)
        mb.sphere(v(sx * 0.06, y + 0.01, 0.02), leg * 1.2 * s, "prop:fur", belly, subdivisions=1, scale=(1, 1.4, 0.6))
    mb.bone = None
    return {"rig": _bones(s, hip), "actions": _actions(species)}


def _darker(hex_color, f=0.75):
    c = hex_color.lstrip("#")
    return "#" + "".join(f"{int(int(c[i:i + 2], 16) * f):02x}" for i in (0, 2, 4))


@generator
def pet_license(mb: MeshBuilder, p, rng):
    """A stiff paper licence with a paw print and a ribbon seal."""
    color, paw = p.get("color", "#f0e6c8"), p.get("paw", "#8a5a3a")
    mb.box((-0.11, -0.006, 0.0), (0.11, 0.006, 0.16), "prop:cloth", color, bevel=0.004)
    mb.sphere((0.0, 0.009, 0.075), 0.03, "prop:plain", paw, subdivisions=1, scale=(1.1, 0.15, 0.9))
    for x, z in ((-0.035, 0.115), (-0.012, 0.13), (0.012, 0.13), (0.035, 0.115)):
        mb.sphere((x, 0.009, z), 0.011, "prop:plain", paw, subdivisions=1, scale=(1, 0.15, 1.2))
    mb.cylinder((0.07, 0.006, 0.03), (0.07, 0.014, 0.03), 0.022, None, "prop:plain", "#c83a3a", segments=10)
