"""Hard plates fitted over the body (armour, carapace, a suit's panels), modelled as distance fields in world space
(metres, the rest pose) and meshed like the head (fieldmesh, cached):

A plate is a shell between two offsets from a smooth *core* standing in for the body part under it (the torso, a
shoulder, a limb segment, fitted to the clothes it covers: platefit.Fitter), cut to an outline drawn in the core's
own surface coordinates (round the torso, along and round a limb) with a rounded rim; panel seams are grooves cut
into its outer face, ridges and raised panels are added to it. The clothes under a plate are pressed in below it,
then after meshing the plate is pushed out wherever it would still come closer to them than its gap; it is shaded
from its field and takes the clothes' skinning weights (averaged over its neighbourhood, so it moves as the surface
under it does). Glowing accent lines are thin strips swept along curves on a plate's face.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .sdf import F, round_cone, smax, smin

SOURCES = ("plates.py", "platefit.py")


# ---------------------------------------------------------------------------------------------------- cores
def _bspline(t):
    """The four uniform cubic B-spline weights at fractions ``t``."""
    t2, t3 = t * t, t * t * t
    return ((1 - t) ** 3 / 6, (3 * t3 - 6 * t2 + 4) / 6, (-3 * t3 + 3 * t2 + 3 * t + 1) / 6, t3 / 6)


def limb(a, b, ra, rb):
    """A limb segment's surface: a cone between spheres at its joints."""
    a, b = tuple(a), tuple(b)
    return lambda p: round_cone(p, a, b, ra, rb)


def ball(c, r):
    c = np.asarray(c, dtype=F)
    return lambda p: np.sqrt(np.einsum("ij,ij->i", p - c, p - c)) - F(r)


# ---------------------------------------------------------------------------------------------------- surface coordinates
def around_torso(fr, p, ref=None, back=False):
    """(u, z): arc length round the torso from the front (+ toward the character's right), or from the back, and
    height."""
    ref = ref or 0.13 * fr.s
    cy = fr.j["spine"].y
    phi = np.arctan2(p[:, 0], (p[:, 1] - F(cy)) * F(-1 if back else 1))
    return phi * F(ref), p[:, 2]


def along(p, a, b, front=(0.0, 1.0, 0.0), ref=0.045):
    """(u, v) on a limb from ``a`` to ``b``: arc length round it from its ``front`` side, and distance along it."""
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    axis = (b - a) / np.linalg.norm(b - a)
    f = np.asarray(front, dtype=float)
    f = f - axis * f.dot(axis)
    f /= np.linalg.norm(f)
    side = np.cross(axis, f)
    q = p - a.astype(F)
    v = q @ axis.astype(F)
    phi = np.arctan2(q @ side.astype(F), q @ f.astype(F))
    return phi * F(ref), v


def box(u, v, cu, cv, hu, hv, r=0.0):
    """A rounded rectangle's distance in (u, v)."""
    qx, qy = np.abs(u - F(cu)) - F(hu - r), np.abs(v - F(cv)) - F(hv - r)
    return np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - F(r)


def shell(core, inner, thick):
    """The band between ``inner`` and ``inner + thick`` off the core's surface."""
    return smax(core - F(inner + thick), F(inner) - core, 0.0)


def groove(d, core, line, outer, depth=0.0012, k=0.0008):
    """``d`` with a seam cut ``depth`` into its outer face (at ``outer`` off the core) where ``line`` < 0."""
    return smax(d, -smax(line, F(outer - depth) - core, k), k)


def plate(core, inner, thick, outline, rim=0.0025, seams=(), extra=None):
    """A plate's field: the shell ``inner`` .. ``inner + thick`` off ``core``, cut to ``outline`` (< 0 inside) with a
    rim rounded over ``rim``, seams (fields of lines, < 0 on them) grooved into its face; ``extra(p, core, d) -> d``
    adds ridges or panels."""
    def field(p):
        c = core(p)
        d = smax(shell(c, inner, thick), outline(p), rim)
        for line in seams:
            d = groove(d, c, line(p), inner + thick)
        return d if extra is None else extra(p, c, d)
    return field


# ---------------------------------------------------------------------------------------------------- meshing and fitting
def mesh(field, bounds, name, key, sources, h=0.001, target=3000):
    """The plate's mesh (world space), cached like the head's."""
    from . import fieldmesh
    lo, hi = bounds
    bounds = (tuple(round(float(v), 4) for v in lo), tuple(round(float(v), 4) for v in hi))
    return fieldmesh.build(field, bounds, h, target, name, key, SOURCES + tuple(sources), "plates", min_piece=40)


def mirror(obj, name):
    """A copy of ``obj`` mirrored to the character's other side."""
    import bpy
    me = obj.data.copy()
    me.name = name
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    co[:, 0] *= -1
    me.vertices.foreach_set("co", co.ravel())
    me.flip_normals()
    me.update()
    out = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(out)
    return out


# ---------------------------------------------------------------------------------------------------- points on a plate
def onto(core, origin, direction, off, reach=0.3):
    """Where rays from ``origin`` along ``direction`` (arrays) cross ``off`` off the core's surface."""
    o, d = np.asarray(origin, dtype=F), np.asarray(direction, dtype=F)
    d = d / np.linalg.norm(d, axis=1, keepdims=True)
    lo, hi = np.zeros(len(o), dtype=F), np.full(len(o), reach, dtype=F)
    for _ in range(30):
        mid = (lo + hi) * F(0.5)
        inside = core(o + d * mid[:, None]) < F(off)
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    return o + d * lo[:, None]


def torso_points(fr, core, us, zs, off, ref=None, back=False):
    """Points ``off`` off the torso core at arc lengths ``us`` round it (see around_torso) and heights ``zs``."""
    ref = ref or 0.13 * fr.s
    cy = fr.j["spine"].y
    phi = np.asarray(us, dtype=float) / ref
    zs = np.asarray(zs, dtype=float)
    origin = np.stack([0 * phi, 0 * phi + cy, zs], 1)
    return onto(core, origin, np.stack([np.sin(phi), np.cos(phi) * (-1 if back else 1), 0 * phi], 1), off)


def limb_points(core, a, b, us, vs, off, front=(0.0, 1.0, 0.0), ref=0.045):
    """Points ``off`` off a limb core at arc lengths ``us`` round it and distances ``vs`` along it (see along)."""
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    axis = (b - a) / np.linalg.norm(b - a)
    f = np.asarray(front, dtype=float)
    f = f - axis * f.dot(axis)
    f /= np.linalg.norm(f)
    side = np.cross(axis, f)
    phi = np.asarray(us, dtype=float) / ref
    origin = a + np.outer(np.asarray(vs, dtype=float), axis)
    return onto(core, origin, np.outer(np.cos(phi), f) + np.outer(np.sin(phi), side), off)


def strip(bm, core, pts, width, height, sink=0.0):
    """A thin rounded strip (a glowing accent line, a raised seam) along points on a plate's face, lying flat on it
    (``sink`` into it); returns the sweep's (vert, t, a) list."""
    from .features import sweep
    from .sdf import gradient
    pts = np.asarray(pts, dtype=F)
    n = gradient(core, pts, 0.001)
    n /= np.linalg.norm(n, axis=1, keepdims=True)
    path = [Vector(p - q * sink) for p, q in zip(pts, n)]
    return sweep(bm, path, lambda t, a: (math.cos(a) * height, math.sin(a) * width), 8, up=Vector(n[0]))
