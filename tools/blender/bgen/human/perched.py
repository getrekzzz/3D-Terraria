"""The caps perched on the finished hair (crowns.py finds its surface): a striped paper party cone tipped to one side
with a pompom, and a nurse's cap, a soft white crown behind a stiff cuff rolled back at the top with a red cross."""
import math

import bmesh
from mathutils import Vector

from .crowns import _finish
from .features import paint, round_section, sweep
from .paint import hex3


def party(head, surf, p):
    """A paper party cone perched on top of the hair, tipped to one side, striped, with a pompom."""
    base = hex3(p.get("hatColor", "#ff6ae0"))
    stripe = tuple(c + (1 - c) * 0.8 for c in base)
    axis = Vector((0.32, 0.12, 1.0)).normalized()
    c0 = Vector((0.0, -0.005, 0.02))
    loc, n = surf.hit(c0 + axis * 0.4, -axis)
    seat = loc if loc is not None else c0 + axis * 0.11
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    r0, height = 0.04, 0.12
    path = [seat - axis * 0.01 + axis * (height * i / 24) for i in range(25)]
    verts = sweep(bm, path, round_section(lambda t: max(r0 * (1 - t), 0.0012)), 28, caps=(True, False))
    paint(bm, verts, lambda t, a: stripe if math.sin(a * 2 + t * 14) > 0.35 else base)
    tip = path[-1]
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.014)
    for v in res["verts"]:
        d = v.co.normalized()
        lump = 1 + 0.12 * math.sin(d.x * 9 + d.y * 4) * math.sin(d.z * 8 + d.x * 3)
        v.co = tip + axis * 0.008 + v.co * lump
    for f in {f for v in res["verts"] for f in v.link_faces}:
        for loop in f.loops:
            loop[layer] = (*stripe, 1.0)
    return _finish(bm, head, "party_hat", "char:cloth")


def nurse(head, surf, p):
    """A nurse's cap: a soft white crown over the top of the hair behind a stiff cuff standing up at the front, its
    top edge rolled back, with a red cross on the cuff."""
    base = hex3(p.get("hatColor", "#ffffff"))
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    cols, rows, depth = 25, 9, 7
    band, crown = [], []
    for j in range(cols):
        a = math.radians(-54 + 108 * j / (cols - 1))
        d = Vector((math.sin(a), 0.3, math.cos(a))).normalized()
        foot = _probe(surf, d)
        lean = Vector((0, -0.35, 0.1)) * (1 - 0.6 * abs(math.sin(a)))
        rise = 0.03 * (1 - 0.38 * (a / math.radians(54)) ** 2)
        band.append([foot - d * 0.002 + (d + lean) * rise * t
                     + (Vector((0, -1, 0.3)) * 0.016 - d * 0.004) * max(0.0, t - 0.72) ** 2 / 0.0784
                     for t in (i / (rows - 1) for i in range(rows))])
        col = []
        for k in range(depth):
            s = k / (depth - 1)
            ak, th = a * (1 - 0.3 * s), math.radians(48 * s)
            dk = Vector((math.sin(ak), 0.3, math.cos(ak))).normalized()
            dk = Vector((dk.x, dk.y * math.cos(th) - dk.z * math.sin(th), dk.y * math.sin(th) + dk.z * math.cos(th)))
            puff = 0.0025 + 0.0065 * math.sin(math.pi * min(s * 1.15, 1.0)) * (1 - 0.6 * (a / math.radians(54)) ** 2)
            col.append(_probe(surf, dk) + dk * puff)
        crown.append(col)
    band = [list(r) for r in zip(*band)]
    crown = [list(r) for r in zip(*crown)]
    _sheet(bm, band, 0.0017)
    _sheet(bm, crown, 0.0012)
    for f in bm.faces:
        for loop in f.loops:
            loop[layer] = (*base, 1.0)
    red = (0.82, 0.12, 0.14)
    lu = sum((a - b).length for a, b in zip(band[4][1:], band[4]))
    lv = sum((r1[12] - r0[12]).length for r0, r1 in zip(band, band[1:]))
    for w, h in ((0.0034, 0.0105), (0.0105, 0.0034)):       # the red cross, laid on the cuff's front
        g = [[_on_sheet(band, 0.5 + w * x / lu, 0.42 + h * y / lv, 0.0024) for x in (-1, 0, 1)] for y in (-1, 0, 1)]
        for f in _sheet(bm, g, 0.0005):
            for loop in f.loops:
                loop[layer] = (*red, 1.0)
    return _finish(bm, head, "nurse_cap", "char:cloth")


def _probe(surf, d):
    loc, _ = surf.hit(d * 0.4, -d)
    return loc if loc is not None else d * 0.11


def _normals(grid):
    rows, cols = len(grid), len(grid[0])
    return [[(grid[i][min(j + 1, cols - 1)] - grid[i][max(j - 1, 0)]).cross(
        grid[min(i + 1, rows - 1)][j] - grid[max(i - 1, 0)][j]).normalized() for j in range(cols)] for i in range(rows)]


def _on_sheet(grid, u, v, lift):
    """The point at (u across, v up) on a grid sheet, lifted off its front (the side facing forward) by ``lift``."""
    rows, cols = len(grid), len(grid[0])
    x, y = u * (cols - 1), v * (rows - 1)
    j, i = min(int(x), cols - 2), min(int(y), rows - 2)
    fx, fy = x - j, y - i
    pt = (grid[i][j] * (1 - fx) + grid[i][j + 1] * fx) * (1 - fy) + (grid[i + 1][j] * (1 - fx) + grid[i + 1][j + 1] * fx) * fy
    n = _normals(grid)[i][j]
    return pt + n * lift * (1 if n.y > 0 else -1)


def _sheet(bm, grid, thick):
    """A thick sheet from a grid of points: two skins offset along the normal and a rim. Returns its faces."""
    norms = _normals(grid)
    rows, cols = len(grid), len(grid[0])
    skins = [[[bm.verts.new(p + n * thick * side) for p, n in zip(r, nr)] for r, nr in zip(grid, norms)]
             for side in (1, -1)]
    faces = []
    for g in skins:
        for i in range(rows - 1):
            for j in range(cols - 1):
                faces.append(bm.faces.new((g[i][j], g[i][j + 1], g[i + 1][j + 1], g[i + 1][j])))
    rim = ([(i, 0) for i in range(rows)] + [(rows - 1, j) for j in range(1, cols)]
           + [(i, cols - 1) for i in range(rows - 2, -1, -1)] + [(0, j) for j in range(cols - 2, 0, -1)])
    for (i0, j0), (i1, j1) in zip(rim, rim[1:] + rim[:1]):
        a, b = skins
        faces.append(bm.faces.new((a[i0][j0], b[i0][j0], b[i1][j1], a[i1][j1])))
    return faces
