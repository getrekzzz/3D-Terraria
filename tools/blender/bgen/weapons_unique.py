"""Bespoke models for iconic unique swords (P3-40): each generator builds one weapon so it reads as itself at icon
size instead of a recoloured sword. Built on blade_profiles; glowing parts use ``:glow`` slots. Item space: grip at
the origin, blade along +Z, flat in XZ. Sockets grip/tip are set explicitly. Params are optional overrides
(``metal``, ``edge``, ``length``). Axes and the scythe live in weapons_unique_2.
"""
import math

from mathutils import Vector

from . import blade_profiles as bp
from . import generator
from .mesh import MeshBuilder

METAL = "prop:iron:metal"
GLOW = "prop:plain:glow"
GEM = "prop:crystal:glow"


def glow_line(mb, pts, color, y=-0.013, r=0.006):
    """A thin glowing strip along a polyline in the XZ plane (on the blade's front face)."""
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        mb.cylinder((x0, y, z0), (x1, y, z1), r, None, GLOW, color, segments=4, caps=False)


def hilt(mb, p, guard="bar", guard_color="#808080", pommel="ball", gem=None, wrap=None, width=0.09, bottom=-0.18):
    bp.grip(mb, bottom, 0.02, p.get("wrap", wrap))
    bp.pommel(mb, p.get("pommel", pommel), bottom - 0.025, METAL, p.get("guardColor", guard_color), p.get("gem", gem))
    bp.guard(mb, p.get("guard", guard), 0.02, width, METAL, p.get("guardColor", guard_color), p.get("guardGem"))


def finish(mb, tip):
    mb.socket("grip", (0, 0, 0))
    mb.socket("tip", tip)


def _blade(mb, p, profile, length, width, color, edge, glow_edge=True, glow_body=False, z0=0.068, **kw):
    body = METAL + (":glow" if glow_body else "")
    edge_mat = METAL + (":glow" if glow_edge or glow_body else "")
    return bp.blade(mb, profile, z0, length, width, body, p.get("metal", color), p.get("edge", edge), edge_mat, **kw)


@generator("unique_zenith", view="item")
def zenith(mb: MeshBuilder, p, rng):
    """A long prismatic blade: a glowing teal sheath around a pale core with a white spine light, winged teal guard."""
    length, width = p.get("length", 1.3), p.get("width", 0.12)
    hilt(mb, p, "winged", "#1f6a5c", "gem", "#ff7ab0", "#173f3a", width * 0.8)
    _blade(mb, p, "straight", length - 0.07, width, "#18d890", "#5a9aff", glow_body=True, thick=0.008)
    _blade(mb, p, "straight", length - 0.13, width * 0.62, "#a8ffd8", "#e8fff4", thick=0.02, fuller=True,
           fuller_color="#2ac8a0")
    glow_line(mb, [(0, 0.12), (0, length * 0.75)], "#ffffff", y=-0.011, r=0.005)
    finish(mb, (0, 0, length))


TERRA = ([(0, -0.42), (0.1, -0.62), (0.104, -0.44), (0.24, -0.56), (0.244, -0.44), (0.55, -0.52), (0.82, -0.4), (1, 0)],
         [(0, 0.42), (0.1, 0.62), (0.104, 0.44), (0.24, 0.56), (0.244, 0.44), (0.55, 0.52), (0.82, 0.4), (1, 0)])


@generator("unique_terra_blade", view="item")
def terra_blade(mb: MeshBuilder, p, rng):
    """Green blade barbed near the guard with a glowing pale edge; dark crescent guard holding a green gem."""
    length, width = p.get("length", 1.2), p.get("width", 0.12)
    hilt(mb, {**p, "guardGem": p.get("guardGem", "#7aff9a")}, "crescent", "#2a5a3a", "ball", None, "#2a3a20", width)
    tip = _blade(mb, p, TERRA, length - 0.07, width, "#22c050", "#7aff8a", fuller=True, fuller_color="#0e6a2a")
    glow_line(mb, [(0, 0.15), (0, length * 0.7)], "#aaffb0", y=-0.006, r=0.005)
    finish(mb, tip)


@generator("unique_nights_edge", view="item")
def nights_edge(mb: MeshBuilder, p, rng):
    """A hooked purple blade with a glowing violet edge and a spiky dark guard."""
    length, width = p.get("length", 1.1), p.get("width", 0.1)
    hilt(mb, p, "spiky", "#2a1a4a", "spike", None, "#1a1028", width)
    tip = _blade(mb, p, "hooked", length - 0.07, width, "#6a3aff", "#a050ff", fuller=True, fuller_color="#2a1060")
    finish(mb, tip)


TRUE_NIGHT = ([(0, -0.44), (0.3, -0.5), (0.5, -0.82), (0.506, -0.44), (0.86, -0.36), (1, -0.05)],
              [(0, 0.44), (0.24, 0.5), (0.4, 0.8), (0.406, 0.42), (0.62, 0.48), (0.76, 0.92), (0.766, 0.36), (0.9, 0.28), (1, -0.05)])


@generator("unique_true_nights_edge", view="item")
def true_nights_edge(mb: MeshBuilder, p, rng):
    """Night's Edge reforged: wider, hooked on both sides, a brighter edge and a teal winged guard with a green gem."""
    length, width = p.get("length", 1.25), p.get("width", 0.12)
    hilt(mb, {**p, "guardGem": p.get("guardGem", "#5aff9a")}, "winged", "#1a6a5a", "spike", None, "#14302c", width * 0.8)
    tip = _blade(mb, p, TRUE_NIGHT, length - 0.07, width, "#8a4aff", "#c060ff", fuller=True, fuller_color="#3aff9a")
    finish(mb, tip)


@generator("unique_excalibur", view="item")
def excalibur(mb: MeshBuilder, p, rng):
    """A broad golden blade with a fuller, a wide winged guard and a sapphire pommel."""
    length, width = p.get("length", 1.05), p.get("width", 0.11)
    hilt(mb, {**p, "guardGem": p.get("guardGem", "#3a8aff")}, "winged", "#ffd040", "gem", "#3a8aff", "#2a3a8a", width)
    tip = _blade(mb, p, "broad", length - 0.07, width, "#ffd860", "#fff6c8", glow_edge=False, fuller=True,
                 fuller_color="#c89020")
    finish(mb, tip)


@generator("unique_true_excalibur", view="item")
def true_excalibur(mb: MeshBuilder, p, rng):
    """Excalibur with a glowing pink edge and fuller light, a larger guard."""
    length, width = p.get("length", 1.15), p.get("width", 0.12)
    hilt(mb, {**p, "guardGem": p.get("guardGem", "#ff6ad0")}, "winged", "#ff9ad0", "gem", "#ff6ad0", "#6a1a4a", width * 1.15)
    tip = _blade(mb, p, "broad", length - 0.07, width, "#ffe070", "#ff5ac8", fuller=True, fuller_color="#c89020")
    glow_line(mb, [(0, 0.14), (0, length * 0.7)], "#ff4ac0", y=-0.005, r=0.011)
    finish(mb, tip)


@generator("unique_muramasa", view="item")
def muramasa(mb: MeshBuilder, p, rng):
    """A thin blue katana: glowing pale edge, round dark tsuba, long bound grip with a small cap."""
    length, width = p.get("length", 1.05), p.get("width", 0.062)
    bp.grip(mb, -0.26, 0.02, p.get("wrap", "#1a2a5a"), radius=0.018)
    for k in range(4):  # diamond binding of the tsuka
        z = -0.23 + k * 0.065
        mb.box((-0.02, -0.021, z), (0.02, 0.021, z + 0.025), "prop:cloth", "#e8e8f0", matrix=None)
    mb.cylinder((0, 0, -0.29), (0, 0, -0.26), 0.022, 0.02, METAL, "#1a2a4a", segments=8, smooth=False)
    bp.guard(mb, "disc", 0.02, 0.07, METAL, p.get("guardColor", "#1a2a4a"))
    mb.cylinder((0, 0, 0.042), (0, 0, 0.07), 0.024, 0.02, METAL, "#c8b060", segments=8, smooth=False)  # habaki
    tip = _blade(mb, p, "katana", length - 0.07, width, "#4a8aff", "#7ab8ff", z0=0.07, curve=p.get("curve", 0.06),
                 thick=0.018)
    finish(mb, tip)


@generator("unique_meowmere", view="item")
def meowmere(mb: MeshBuilder, p, rng):
    """A cat-head hilt (ears, eyes, whiskers) and a pink blade banded with a glowing rainbow edge."""
    length, width = p.get("length", 1.2), p.get("width", 0.13)
    bp.grip(mb, -0.17, 0.0, p.get("wrap", "#ff8ac8"))
    mb.sphere((0, 0, -0.2), 0.03, METAL, "#ffffff", subdivisions=1)  # paw
    head, fur = 0.1, p.get("fur", "#f4f0f8")
    mb.sphere((0, 0, 0.07), head, "prop:cloth", fur, subdivisions=2, scale=(1.2, 0.5, 0.9))
    for sx in (-1, 1):
        ear = [(sx * 0.035, 0.13), (sx * 0.115, 0.1), (sx * 0.11, 0.21)]
        bp.bevel_slab(mb, ear, 0.035, 0.008, "prop:cloth", fur, rim="#ff9ad0")
        mb.sphere((sx * 0.042, -0.046, 0.08), 0.018, "prop:plain", "#2a1a3a", subdivisions=1, scale=(1, 0.5, 1.2))
        for dz in (-0.008, 0.014):
            mb.cylinder((sx * 0.07, -0.046, 0.045 + dz), (sx * 0.15, -0.04, 0.04 + dz * 1.8), 0.003, None, "prop:plain",
                        "#3a2a3a", segments=3, caps=False)
    mb.sphere((0, -0.05, 0.055), 0.011, "prop:plain", "#ff7ab0", subdivisions=1)  # nose
    z0, blen = 0.15, length - 0.15
    rainbow = p.get("rainbow", ["#ff4a6a", "#ffb03a", "#ffe84a", "#5aff7a", "#4ac8ff", "#a06aff"])
    for i, c in enumerate(rainbow):
        k = 1.0 - i * 0.12
        bp.blade(mb, "leaf", z0, blen * (1 - i * 0.015), width * k, GLOW, c, c, GLOW, thick=0.006 + i * 0.0025)
    tip = bp.blade(mb, "leaf", z0, blen * 0.9, width * 0.28, METAL, p.get("metal", "#ffd0ec"), "#ffffff", thick=0.024)
    finish(mb, (0, 0, z0 + blen))


@generator("unique_star_wrath", view="item")
def star_wrath(mb: MeshBuilder, p, rng):
    """A crimson blade rising from a glowing star guard, with a star pommel."""
    length, width = p.get("length", 1.2), p.get("width", 0.12)
    bp.grip(mb, -0.18, 0.02, p.get("wrap", "#4a0a20"))
    bp.pommel(mb, "star", -0.2, METAL, "#8a1a3a", "#ff7aa0")
    bp.bevel_slab(mb, bp.star_points(0, 0.06, 0.13, 0.055), 0.05, 0.012, METAL, "#8a1a3a", rim="#ff6a9a")
    mb.sphere((0, -0.026, 0.06), 0.03, GEM, "#ffd0e0", subdivisions=1, smooth=False)
    tip = _blade(mb, p, "leaf", length - 0.12, width, "#ff3a6a", "#ffb0c8", z0=0.12, fuller=True, fuller_color="#8a0a2a")
    finish(mb, tip)


@generator("unique_starfury", view="item")
def starfury(mb: MeshBuilder, p, rng):
    """A golden blade with a pink-gold guard and a large glowing star for a pommel."""
    length, width = p.get("length", 0.95), p.get("width", 0.095)
    bp.grip(mb, -0.17, 0.02, p.get("wrap", "#3a2a6a"))
    bp.pommel(mb, "star", -0.2, METAL, "#ff9a3a", p.get("gem", "#ffe060"))
    bp.guard(mb, "crescent", 0.02, width, METAL, p.get("guardColor", "#ff8a5a"), "#ff5ab0")
    tip = _blade(mb, p, "straight", length - 0.07, width, "#ffd84a", "#fff4b0", glow_edge=True, fuller=True,
                 fuller_color="#e09020")
    finish(mb, tip)


@generator("unique_enchanted_sword", view="item")
def enchanted_sword(mb: MeshBuilder, p, rng):
    """A silver blade with a glowing blue edge, a gold crescent guard and a blue gem."""
    length, width = p.get("length", 1.0), p.get("width", 0.1)
    hilt(mb, {**p, "guardGem": p.get("guardGem", "#3a8aff")}, "crescent", "#f2c850", "gem", "#3a8aff", "#2a3a6a", width)
    tip = _blade(mb, p, "straight", length - 0.07, width, "#d0dcf0", "#4ab8ff", fuller=True, fuller_color="#6a8ac8")
    finish(mb, tip)


@generator("unique_breaker_blade", view="item")
def breaker_blade(mb: MeshBuilder, p, rng):
    """A huge grey cleaver with a long two-handed grip, a heavy guard and bolts down the spine."""
    length, width = p.get("length", 1.45), p.get("width", 0.22)
    bp.grip(mb, -0.34, 0.02, p.get("wrap", "#4a2a1a"), radius=0.024)
    bp.pommel(mb, "ball", -0.365, METAL, "#5a2a2a")
    bp.guard(mb, "bar", 0.02, width * 0.75, METAL, p.get("guardColor", "#5a2a2a"))
    tip = _blade(mb, p, "cleaver", length - 0.07, width, "#8a8e96", "#dfe3ea", glow_edge=False, thick=0.03)
    for k in range(4):
        z = 0.2 + k * (length - 0.45) / 3
        mb.cylinder((width * 0.38, -0.022, z), (width * 0.38, 0.022, z), 0.016, None, METAL, "#4a4e56", segments=6,
                    smooth=False)
    finish(mb, tip)


INFLUX = ([(0, -0.45), (0.55, -0.52), (0.6, -0.36), (0.86, -0.3), (1, 0.25)],
          [(0, 0.45), (0.3, 0.52), (0.34, 0.4), (0.78, 0.5), (1, 0.25)])


@generator("unique_influx_waver", view="item")
def influx_waver(mb: MeshBuilder, p, rng):
    """An angular Martian tech blade: steel-teal body, glowing cyan strips, a boxy emitter guard."""
    length, width = p.get("length", 1.25), p.get("width", 0.12)
    bp.grip(mb, -0.18, 0.02, p.get("wrap", "#2a3440"))
    mb.box((-0.03, -0.03, -0.24), (0.03, 0.03, -0.18), METAL, "#3a4a5a")
    mb.box((-0.02, -0.032, -0.225), (0.02, 0.032, -0.2), GLOW, "#6affff")
    mb.box((-0.11, -0.035, 0.02), (0.11, 0.035, 0.075), METAL, p.get("guardColor", "#3a4a5a"))
    for sx in (-1, 1):
        mb.box((sx * 0.11 - 0.025, -0.03, 0.0), (sx * 0.11 + 0.025, 0.03, 0.1), METAL, "#2a3440")
        mb.box((sx * 0.11 - 0.012, -0.034, 0.03), (sx * 0.11 + 0.012, 0.034, 0.07), GLOW, "#6affff")
    tip = _blade(mb, p, INFLUX, length - 0.075, width, "#5ab8d0", "#c8f8ff", z0=0.075, glow_edge=False)
    L = length - 0.075
    glow_line(mb, [(-0.02, 0.1), (-0.02, 0.075 + L * 0.55), (0.0, 0.075 + L * 0.8)], "#6affff", y=-0.014)
    glow_line(mb, [(0.025, 0.1), (0.025, 0.075 + L * 0.3)], "#6affff", y=-0.014)
    finish(mb, tip)
