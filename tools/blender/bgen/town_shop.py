"""Small goods the town residents sell (P5-15..P5-18): seed packets, framed paintings, golf clubs and balls, cogs.

Items are modelled around the origin (Z up, about 0.3 m tall). Objects get ``p['size']`` = [width, height, depth]
in metres from tile-object data (origin at the centre of the bottom face, front facing +Y, Z up); wall objects hang
on the footprint's -Y face.
seed_packet(p): color, seed.      painting(p): frame, colors (canvas bands), style (stripes | sun | figure).
golf_club(p): head (iron | driver | wedge | putter), metal, grip.   golf_ball(p): color.   cog(p): metal.
"""
import math

from . import generator
from .mesh import MeshBuilder


@generator
def seed_packet(mb: MeshBuilder, p, rng):
    """A paper seed pouch tied at the neck, a few seeds spilled in front."""
    color = p.get("color", "#6ab04a")
    mb.lathe([(0.0, 0.0), (0.08, 0.01), (0.1, 0.07), (0.09, 0.13), (0.035, 0.17), (0.0, 0.17)], "prop:cloth", color, segments=12)
    mb.lathe([(0.036, 0.165), (0.045, 0.2), (0.0, 0.205)], "prop:cloth", color, segments=8)
    mb.cylinder((0, 0, 0.163), (0, 0, 0.175), 0.04, None, "prop:cloth", "#d8c8a0", segments=10)
    seed = p.get("seed", "#c8a060")
    for k in range(3):
        mb.sphere((-0.05 + 0.05 * k, 0.11, 0.015), 0.015, "prop:plain", seed, subdivisions=1, scale=(1, 0.8, 0.7))


@generator
def painting(mb: MeshBuilder, p, rng):
    """A framed canvas on the wall: a wooden frame around coloured bands with a simple motif."""
    w, h, d = p["size"]
    y = -d / 2 + 0.02
    frame = p.get("frame", "#8a5a2a")
    t = min(w, h) * 0.07
    mb.box((-w / 2 + 0.03, y, 0.03), (w / 2 - 0.03, y + 0.05, h - 0.03), "prop:planks", frame)
    colors = p.get("colors", ["#4a7ab0", "#e8d080", "#4a8a3a"])
    lo_z, hi_z = 0.03 + t, h - 0.03 - t
    band = (hi_z - lo_z) / len(colors)
    for i, c in enumerate(colors):
        mb.box((-w / 2 + 0.03 + t, y + 0.05, lo_z + band * i), (w / 2 - 0.03 - t, y + 0.06, lo_z + band * (i + 1)), "prop:plain", c)
    style = p.get("style", "stripes")
    cz = (lo_z + hi_z) / 2
    if style == "sun":
        mb.sphere((w * 0.15, y + 0.065, cz + band * 0.6), min(w, h) * 0.12, "prop:plain", p.get("motif", "#ffe070"), subdivisions=1, scale=(1, 0.15, 1))
    elif style == "figure":
        m = p.get("motif", "#2a2a3a")
        s = min(w, h)
        mb.box((-s * 0.06, y + 0.06, lo_z + band * 0.3), (s * 0.06, y + 0.075, cz + s * 0.12), "prop:plain", m)
        mb.sphere((0, y + 0.07, cz + s * 0.2), s * 0.07, "prop:plain", m, subdivisions=1, scale=(1, 0.2, 1))


_CLUB_HEADS = {"iron": (0.07, 0.035, 0.018), "driver": (0.08, 0.06, 0.045), "wedge": (0.06, 0.045, 0.014), "putter": (0.09, 0.025, 0.025)}


@generator(view="item")
def golf_club(mb: MeshBuilder, p, rng):
    """A long shaft with a rubber grip and a club head (iron, driver, wedge or putter)."""
    metal, grip = p.get("metal", "#c8ccd4"), p.get("grip", "#2a2a2a")
    mb.cylinder((0, 0, 0.0), (0, 0, 0.62), 0.01, 0.008, "prop:iron", metal, segments=8)
    mb.cylinder((0, 0, 0.0), (0, 0, 0.16), 0.016, 0.014, "prop:leather", grip, segments=8)
    hx, hy, hz = _CLUB_HEADS.get(p.get("head", "iron"), _CLUB_HEADS["iron"])
    mb.box((-0.01, -hy / 2, 0.62), (hx, hy / 2, 0.62 + hz), "prop:iron", p.get("headColor", metal), bevel=0.005)


@generator
def golf_ball(mb: MeshBuilder, p, rng):
    """A dimpled white ball."""
    mb.sphere((0, 0, 0.05), 0.05, "prop:plain", p.get("color", "#f4f4f0"), subdivisions=2)


@generator
def cog(mb: MeshBuilder, p, rng):
    """A toothed gear wheel lying flat."""
    metal = p.get("metal", "#b08a4a")
    mb.cylinder((0, 0, 0.0), (0, 0, 0.03), 0.09, None, "prop:iron", metal, segments=16)
    for k in range(10):
        a = 2 * math.pi * k / 10
        c, s = math.cos(a), math.sin(a)
        mb.box((c * 0.09 - 0.015, s * 0.09 - 0.015, 0.0), (c * 0.09 + 0.015, s * 0.09 + 0.015, 0.03), "prop:iron", metal)
    mb.cylinder((0, 0, -0.002), (0, 0, 0.032), 0.025, None, "prop:plain", "#2a2a2a", segments=10)


@generator
def crystal_ball(mb: MeshBuilder, p, rng):
    """A glowing glass sphere on a carved wooden stand."""
    w, h, d = p["size"]
    r = min(w, d) * 0.32
    mb.lathe([(0.0, 0.0), (r * 0.9, 0.0), (r * 0.7, h * 0.12), (r * 0.35, h * 0.3), (r * 0.6, h * 0.42), (0.0, h * 0.44)],
             "prop:dark_planks", p.get("stand", "#5a3a20"), segments=14)
    mb.sphere((0, 0, h * 0.44 + r), r, "prop:crystal:glow", p.get("color", "#b090ff"), subdivisions=2)


@generator
def star_topper(mb: MeshBuilder, p, rng):
    """A five-pointed golden star standing on a short peg."""
    w, h, d = p["size"]
    s = min(w, h) * 0.4
    pts = []
    for k in range(10):
        a = math.pi / 2 + math.pi * k / 5
        rr = s if k % 2 == 0 else s * 0.45
        pts.append((rr * math.cos(a), rr * math.sin(a)))
    from mathutils import Matrix
    m = Matrix.Translation((0, 0.03, h * 0.5)) @ Matrix.Rotation(math.pi / 2, 4, "X")
    mb.extrude_polygon(pts, 0.06, "prop:gold", p.get("color", "#ffe060"), matrix=m)
    mb.cylinder((0, 0, 0), (0, 0, h * 0.5 - s * 0.4), 0.02, None, "prop:gold", "#c8a040", segments=6)
