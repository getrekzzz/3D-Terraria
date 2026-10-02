"""Bespoke unique weapons, part two (P3-40): the axe with a face (Lucy) and the Death Sickle scythe. Same conventions
as weapons_unique: grip at the origin, haft along +Z, heads flat in XZ, explicit grip/tip sockets."""
from . import blade_profiles as bp
from . import generator
from .mesh import MeshBuilder
from .weapons_unique import GLOW, METAL, glow_line


def _bezier(p0, p1, p2, n):
    out = []
    for i in range(n + 1):
        t = i / n
        a, b, c = (1 - t) ** 2, 2 * (1 - t) * t, t * t
        out.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))
    return out


@generator("unique_lucy_the_axe", view="item")
def lucy_the_axe(mb: MeshBuilder, p, rng):
    """A red-hafted woodsman's axe whose broad steel blade has a face: wide eyes, brows and a grin."""
    steel = p.get("metal", "#b8bec8")
    mb.cylinder((0, 0, -0.12), (0, 0, 0.84), 0.024, 0.021, "prop:planks", p.get("wood", "#e07a5a"), segments=8)
    mb.cylinder((0, 0, -0.08), (0, 0, 0.1), 0.027, None, "prop:leather", p.get("wrap", "#5a3a24"), segments=8)
    mb.cylinder((0, 0, -0.15), (0, 0, -0.11), 0.026, 0.032, METAL, "#6a6e76", segments=8, smooth=False)
    blade = [(0.0, 0.52), (0.1, 0.5), (0.22, 0.4), (0.3, 0.44), (0.33, 0.62), (0.3, 0.82), (0.22, 0.86), (0.1, 0.76), (0.0, 0.74)]
    bp.bevel_slab(mb, blade, 0.036, 0.016, METAL, steel, rim=p.get("edge", "#eef2f8"))
    mb.box((-0.06, -0.034, 0.53), (0.025, 0.034, 0.73), METAL, "#7a7e88")
    for x, z in ((0.12, 0.67), (0.22, 0.67)):  # eyes
        mb.sphere((x, -0.02, z), 0.032, "prop:plain", "#f8f8f0", subdivisions=2, scale=(1, 0.35, 1.15))
        mb.sphere((x + 0.008, -0.03, z - 0.004), 0.014, "prop:plain", "#18181c", subdivisions=1, scale=(1, 0.4, 1))
        mb.cylinder((x - 0.03, -0.022, z + 0.05), (x + 0.03, -0.022, z + 0.058), 0.007, None, "prop:plain", "#3a3036",
                    segments=4)
    grin = [(0.1, 0.58), (0.14, 0.555), (0.19, 0.555), (0.235, 0.58)]
    for (x0, z0), (x1, z1) in zip(grin, grin[1:]):
        mb.cylinder((x0, -0.02, z0), (x1, -0.02, z1), 0.008, None, "prop:plain", "#3a2028", segments=4)
    mb.socket("grip", (0, 0, 0))
    mb.socket("tip", (0.33, 0, 0.62), (1, 0, 0), (0, 1, 0))


@generator("unique_death_sickle", view="item")
def death_sickle(mb: MeshBuilder, p, rng):
    """A reaper's scythe: a long dark haft with bone bindings and a great curved purple blade with a glowing edge."""
    blade_color, glow = p.get("metal", "#6a4aa8"), p.get("glow", "#c890ff")
    top = 1.1
    mb.cylinder((0, 0, -0.18), (0, 0, top + 0.05), 0.024, 0.021, "prop:dark_planks", p.get("wood", "#5a4a6a"), segments=8)
    for z in (-0.05, 0.45, top - 0.1):
        mb.cylinder((0, 0, z - 0.02), (0, 0, z + 0.02), 0.03, None, "prop:plain", "#e8e0c8", segments=8, smooth=False)
    mb.sphere((0, 0, -0.2), 0.035, "prop:plain", "#e8e0c8", subdivisions=1)
    outer = _bezier((0.05, top + 0.08), (-0.34, top + 0.26), (-0.66, top - 0.3), 9)
    inner = _bezier((0.05, top - 0.04), (-0.3, top + 0.06), (-0.66, top - 0.3), 9)
    pts = outer + inner[-2::-1]
    bp.bevel_slab(mb, pts, 0.032, 0.012, METAL, blade_color, rim=bp.lighten(blade_color, 0.35), edge=0.4)
    edge = [(x, z + 0.012) for x, z in inner[1:-1]]
    glow_line(mb, edge, glow, y=-0.012, r=0.007)
    mb.box((-0.045, -0.03, top - 0.06), (0.06, 0.03, top + 0.1), METAL, "#3a2a4a")
    mb.sphere((0.0, -0.032, top + 0.02), 0.02, "prop:crystal:glow", glow, subdivisions=1, smooth=False)
    mb.socket("grip", (0, 0, 0))
    mb.socket("tip", (-0.66, 0, top - 0.3), (-1, 0, 0), (0, 1, 0))
