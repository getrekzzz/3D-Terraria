"""Tails growing from the tailbone (their roots sunk into the body, out through a hemmed slit in the clothes over it,
wingroot.py), swept along smooth curves and carried by the hips. ``tailShape``:

- lizard: thick at the root and tapering to a fine tip as it sweeps down, back and round, a row of scutes along
  the top, banded scales and a paler belly;
- fox: slim at the root, swelling into a bushy brush of fur clumps that curls up to a pale tufted tip;
- wolf: a shaggy brush hanging down behind the legs, darker toward the tip;
- devil: a long thin whip curling up behind and ending in a flat spade;
- snake: a serpent's body as thick as a thigh coming out from under the back of a robe and lying on the ground in a
  long S (serpent.py).
"""
import math

import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import wingroot
from .features import paint, spline, sweep
from .garments import mesh_from, set_slot
from .paint import hex3
from .rig import rigid

# control points relative to where the tail leaves the body (frame units: x right, y forward, z up)
PATHS = {
    "lizard": [(0, 0.05, 0.02), (0, -0.02, -0.01), (0, -0.12, -0.09), (0.025, -0.25, -0.22), (0.07, -0.38, -0.33),
               (0.14, -0.5, -0.38), (0.23, -0.58, -0.37), (0.31, -0.62, -0.32)],
    "fox": [(0, 0.04, 0.02), (0, -0.02, -0.01), (0.01, -0.09, -0.07), (0.035, -0.18, -0.14), (0.075, -0.28, -0.16),
            (0.11, -0.36, -0.11), (0.125, -0.41, -0.03)],
    "wolf": [(0, 0.04, 0.02), (0, -0.03, -0.02), (0, -0.11, -0.11), (0.015, -0.17, -0.23), (0.025, -0.19, -0.35),
             (0.03, -0.18, -0.45)],
    "devil": [(0, 0.04, 0.02), (0, -0.03, -0.02), (0.02, -0.15, -0.13), (0.06, -0.29, -0.2), (0.11, -0.41, -0.17),
              (0.14, -0.49, -0.08), (0.145, -0.52, 0.03), (0.13, -0.5, 0.12)],
}


# the root's radius where it leaves the body (frame units): it comes out low enough to clear the belt
ROOT = {"lizard": 0.062, "fox": 0.036, "devil": 0.021}


def _exit(fr, body, garments, shape):
    """Where the tailbone's line meets the back of the body or the clothes over it, and what it meets there."""
    s = fr.s
    z = fr.j["pelvis"].z + 0.02 * s
    if shape in ROOT:            # under the belt (round the waist, hip height + 0.1)
        z = min(z, fr.j["hip.R"].z + (0.07 - ROOT[shape]) * s)
    best = None
    for o in [body] + list(garments):
        if o.type != "MESH" or not len(o.data.polygons):
            continue
        tree = BVHTree.FromObject(o, __import__("bpy").context.evaluated_depsgraph_get())
        loc, _, _, _ = tree.ray_cast(Vector((0, -0.6 * s, z)), Vector((0, 1, 0)), 0.6 * s)
        if loc is not None and (best is None or loc.y < best[0].y):
            best = (loc, o)
    return best if best is not None else (Vector((0, -0.11 * s, z)), None)


def _fur(t, a, amp, clumps=7, rows=5):
    """Locks of fur round the tail in staggered rows, each swelling to a peaked tip that overlaps the root of the
    next, so the outline breaks into tufts."""
    c = a * clumps / (2 * math.pi)
    ridge = (1 - abs(2 * (c % 1.0) - 1)) ** 0.7
    k = (t * rows + 0.5 * (int(c) % 2)) % 1.0
    lock = (k / 0.72) ** 1.3 if k < 0.72 else (1 - k) / 0.28
    return 1 + amp * (ridge * lock - 0.35) * min(1.0, t * 8)


def tail(fr, body, p, garments=()):
    shape = p.get("tailShape", "devil")
    if shape == "snake":
        from . import serpent
        return serpent.tail(fr, p, garments)
    s = fr.s
    base = hex3(p["tail"])
    exit_pt, cloth = _exit(fr, body, garments, shape)
    ctrl = [exit_pt + Vector(c) * s for c in PATHS.get(shape, PATHS["devil"])]
    if shape == "devil":         # the spade continues straight on from the whip's end
        d = (ctrl[-1] - ctrl[-2]).normalized()
        ctrl += [ctrl[-1] + d * 0.035 * s, ctrl[-1] + d * 0.075 * s]
    path = spline(ctrl, 110 if shape in ("fox", "wolf") else 72)
    seam = 1 - (0.11 * s) / sum((b - a).length for a, b in zip(path, path[1:])) if shape == "devil" else 1.0
    pale = tuple(c + (1 - c) * 0.55 for c in base)
    if shape == "lizard":
        def section(t, a):
            r = s * (0.004 + 0.056 * (1 - t) ** 1.15) * (1 + 0.25 * max(0.0, 1 - t / 0.08) ** 2)
            scute = 1 + 0.16 * max(0.0, math.cos(a)) ** 14 * (0.55 + 0.45 * math.sin(t * 90)) * (1 - t)
            return math.cos(a) * r * scute, math.sin(a) * r * 1.06

        def tone(t, a):
            band = 0.9 + 0.1 * math.sin(t * 140)
            belly = max(0.0, -math.cos(a)) ** 1.5
            return tuple((c + (p_ - c) * belly * 0.7) * band * (0.85 + 0.15 * max(0.0, math.cos(a))) for c, p_ in zip(base, pale))
    elif shape in ("fox", "wolf"):
        fox = shape == "fox"
        peak, width = (0.058, 0.75) if fox else (0.054, 0.8)

        def section(t, a):
            r = s * (0.011 * (1 - t) + peak * math.sin(math.pi * t ** width) ** 0.6)
            r *= _fur(t, a, 0.2 if fox else 0.34, 7, 6 if fox else 5)
            return math.cos(a) * r, math.sin(a) * r

        tip = (0.96, 0.93, 0.86) if fox else tuple(c * 0.45 for c in base)

        def tone(t, a):
            k = max(0.0, min(1.0, (t - (0.74 if fox else 0.6)) / 0.14))
            streak = 0.9 + 0.1 * math.sin(a * 9 + t * 30)
            return tuple((c + (q - c) * k) * streak for c, q in zip(base, tip))
    else:                         # devil: a whip and a flat spade, its broad side facing out
        def section(t, a):
            if t <= seam:
                r = s * (0.0055 + 0.016 * (1 - t / seam) ** 1.3)
                return math.cos(a) * r, math.sin(a) * r
            u = (t - seam) / (1 - seam)
            w = 0.04 * (math.sin(math.pi / 2 * u / 0.3) if u < 0.3 else ((1 - u) / 0.7) ** 1.3) + 0.0055 * (1 - u)
            th = max(0.0035, w * 0.17)
            return math.cos(a) * th * s, math.sin(a) * w * s

        def tone(t, a):
            return tuple(c * (0.75 + 0.25 * min(1.0, t * 1.5)) for c in base)
    bm = bmesh.new()
    verts = sweep(bm, path, section, 28 if shape in ("fox", "wolf") else 16, up=Vector((0, 0, 1)))
    paint(bm, verts, tone)
    if shape in ("fox", "wolf"):
        _strand_uvs(bm, verts, sum((b - a).length for a, b in zip(path, path[1:])))
    obj = mesh_from(bm, "tail")
    set_slot(obj, "char:hair" if shape in ("fox", "wolf") else "char:skin")
    rigid(obj, "hips")
    if shape in ROOT:            # out through a hemmed slit in the seat of whatever covers the tailbone
        r = ROOT[shape] * s
        hem = wingroot.slit(cloth, exit_pt, Vector((0, -1, 0)), s, 1.12 * r + 0.004 * s, 1.2 * r + 0.005 * s)
        if hem is not None:
            wingroot.sew(cloth, hem)
    return obj


def _strand_uvs(bm, verts, length):
    """UVs running along the fur (U = along the tail, V round it), so the hair shader streaks it lengthwise."""
    uv = bm.loops.layers.uv.new("UVMap")
    ta = {v: (t, a) for v, t, a in verts}
    for f in bm.faces:
        angles = [ta[l.vert][1] for l in f.loops]
        wrap = max(angles) - min(angles) > math.pi
        for loop in f.loops:
            t, a = ta[loop.vert]
            if wrap and a < math.pi:
                a += 2 * math.pi
            loop[uv].uv = (t * length * 6.0, a / math.pi)
