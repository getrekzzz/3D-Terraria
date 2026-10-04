"""Beards and moustaches: the styles, where facial hair grows and how thick it stands; beardlocks shapes the mass over
the face and the moustache, beardbib the part that hangs below the jaw, and beardsdf sculpts them into one mesh.

A beard grows below a cheek line that runs from beside the nose wings across the cheeks to the sideburns (where it
reaches up under the head hair), over the jaw and chin and down the throat to a neckline, and on the upper lip; never on
the lips or round the ears. It thins out over its last centimetre, where it fades into the skin.

Head-local units (headshape) throughout.
"""
from dataclasses import dataclass

import numpy as np

F = np.float32


@dataclass(frozen=True)
class Style:
    cheek: float        # how far up the cheeks it grows (0 a chin beard only .. 1 up to the sideburns)
    mass: float         # thickness over the chin
    length: float       # how far it hangs below the chin
    spread: float       # how broad its hanging part stays (0 gathers to a point .. 1 as wide as the jaw)
    width: float        # how far round the jaw its hanging part reaches (azimuth from the chin, radians)
    forks: int          # tapered points the hanging part gathers into (0: each clump ends in its own)
    moustache: float    # moustache size (0 none)
    droop: float        # how far the moustache's ends fall past the corners of the mouth
    mess: float         # random turn of the locks
    lock: float         # lock width over the face
    neck: float = 0.0   # how much lower than usual the neckline runs
    depth: float = 1.0  # how far back under the chin toward the throat the hanging part fills (1 all the way)


STYLES = {
    "short": Style(1.0, 0.0055, 0.0, 1.0, 0.0, 0, 0.75, 0.25, 0.03, 0.016),
    "full": Style(1.0, 0.011, 0.045, 0.75, 0.95, 0, 1.0, 0.55, 0.05, 0.019),
    "bushy": Style(1.0, 0.016, 0.07, 0.9, 1.1, 0, 1.25, 0.7, 0.12, 0.021),
    "long": Style(0.92, 0.012, 0.22, 0.4, 0.95, 2, 1.1, 1.0, 0.04, 0.019, 0.008),
    "pointed": Style(0.8, 0.01, 0.08, 0.3, 0.6, 1, 0.9, 0.6, 0.03, 0.017, 0.0, 0.8),
    "goatee": Style(0.0, 0.01, 0.035, 0.3, 0.4, 1, 0.9, 0.75, 0.03, 0.014, -0.045, 0.55),
}

# The cheek line's height by azimuth from the front (head-local z): beside the nose wings, dipping round the corners of
# the moustache, rising across the cheek to the sideburn in front of the ear; and for a chin beard, round the mouth.
LINE_A = np.asarray((0.0, 0.24, 0.45, 0.65, 0.85, 1.0, 1.15, 1.3, 1.5, 2.0))
CHEEK_Z = np.asarray((-0.05, -0.048, -0.054, -0.051, -0.044, -0.036, -0.025, -0.014, -0.012, -0.03))
CHIN_A = np.asarray((0.0, 0.24, 0.3, 0.36, 0.42, 0.5))
CHIN_Z = np.asarray((-0.05, -0.05, -0.058, -0.075, -0.13, -0.3))
EAR = ((-0.012, -0.02), (0.022, 0.04))          # (y, z) centre and radii of the bare zone round each ear
LIPS = (-0.0705, 0.0275, 0.0105)                # z centre, half width and half height of the bare mouth


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _below(a, z, line_a, line_z):
    """How far below a line given as heights by azimuth each point lies, measured across the face (head-local;
    negative above it): its edge thins out over the same distance however steeply the line runs."""
    px = a * 0.09
    d = np.full(len(a), np.inf)
    for i in range(len(line_a) - 1):
        ax, az = line_a[i] * 0.09, line_z[i]
        bx, bz = line_a[i + 1] * 0.09 - ax, line_z[i + 1] - az
        u = np.clip(((px - ax) * bx + (z - az) * bz) / (bx * bx + bz * bz), 0.0, 1.0)
        d = np.minimum(d, np.hypot(px - ax - u * bx, z - az - u * bz))
    return np.where(z < np.interp(a, line_a, line_z), d, -d)


def coverages(q, st, mouth=True):
    """How much beard grows at each point (0 bare skin .. 1 full), thinning out at every edge: softly over the cheeks
    and down the throat, crisply round the lips (unless ``mouth`` is False: the sculpted beard reads the lips' own
    shape instead), and clear of the ears."""
    q = np.atleast_2d(np.asarray(q, dtype=F))
    x, y, z = np.abs(q[:, 0]), q[:, 1], q[:, 2]
    a = np.arctan2(x, y)
    if st.cheek > 0:          # lower on the cheeks for a smaller beard, still up to the sideburns
        m = _ss(-0.004, 0.008, _below(a, z + (1 - st.cheek) * 0.06 * _ss(1.45, 0.6, a), LINE_A, CHEEK_Z))
    else:
        m = _ss(-0.004, 0.008, _below(a, z, CHIN_A, CHIN_Z))
    m = m * _ss(-0.008, 0.01, y + z + 4.0 * x * x + 0.097 + st.neck)
    m = m * _ss(-0.004, 0.007, y + 0.013)
    (cy, cz), (ry, rz) = EAR
    m = m * np.where(x > 0.04, _ss(1.0, 1.3, np.hypot((y - cy) / ry, (z - cz) / rz)), 1.0)
    if not mouth:
        return m
    zc, hw, hh = LIPS
    lips = (np.hypot(q[:, 0] / hw, (z - zc) / hh) - 1.0) * 0.01
    return m * (1 - (1 - _ss(0.0005, 0.0035, lips)) * _ss(0.04, 0.06, y))


def masses(q, st):
    """How thick the beard's layer over the skin is: a few millimetres on the cheeks, fuller down the jaw, fullest at
    the chin; thin on the upper lip, which carries the moustache's own locks."""
    q = np.atleast_2d(np.asarray(q, dtype=F))
    x, y, z = np.abs(q[:, 0]), q[:, 1], q[:, 2]
    a = np.arctan2(x, y)
    low = _ss(-0.04, -0.095, z)
    front = _ss(1.35, 0.3, a)
    t = 0.003 + (st.mass * 0.7 - 0.003) * low * (0.6 + 0.4 * front)
    zc, hw, hh = LIPS
    mouth = _ss(0.0, 0.012, (np.hypot(q[:, 0] / hw, (z - zc) / hh) - 1.0) * 0.01)
    t = t * (1 - 0.6 * (1 - mouth) * _ss(0.04, 0.06, y))  # thinner round the mouth: the lips sit in it, not in a hole
    lip = _ss(-0.074, -0.062, z) * _ss(0.036, 0.026, x) * _ss(0.04, 0.06, y)       # (the upper lip)
    return (t * (1 - lip) + 0.0022 * lip).astype(F)


def jaw_edge(a):
    """Height of the jaw's lower edge by azimuth from the chin (where a beard turns under it)."""
    return -0.085 - 0.025 * _ss(1.3, 0.35, np.abs(a))


def _tree(objs):
    """One BVH (world space) over the faces of several meshes."""
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in objs:
        base = len(verts)
        mw = o.matrix_world
        verts += [mw @ v.co for v in o.data.vertices]
        polys += [[base + i for i in poly.vertices] for poly in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys) if polys else None


def grow(head, p, under=()):
    """The character's beard: ``p["beard"]`` is its colour, ``p["beardStyle"]`` one of STYLES (full by default);
    ``under`` are the body and the clothes it may hang onto. Whatever hangs over them is kept in front of them."""
    from .beardbib import Bib
    from .beardlocks import Beard
    from .beardsdf import Sculpt
    from .paint import hex3
    style = p.get("beardStyle", "full")
    beard = Beard(head, style, _tree(under), seed=sum(ord(ch) for ch in style))
    bib = Bib(beard) if beard.st.length > 0 else None
    sculpt = Sculpt(beard, bib)
    hairy = p.get("hairStyle", "short") != "bald" and p.get("hat") != "hood"
    obj = sculpt.finish(sculpt.build(), hex3(p["beard"]), hex3(p.get("skin", "#e0b090")),
                        hex3(p.get("hair", p["beard"])) if hairy else None)
    return obj
