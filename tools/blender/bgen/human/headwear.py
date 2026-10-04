"""Soft headwear sculpted as distance fields over the bare skull (headshape), in head-local units like the head:

- a cap: a cloth crown that sits on the skull (over the hair it leaves below its band), its rim low at the nape and
  over the brow at the front, with a turned band and, unless asked not to, a stiffened peak;
- a hood (hood.py): a cloth shell standing off the skull, falling past the jaw into a cowl over the shoulders.

Both are shells (an outer surface minus an inner one), so their edges show the cloth's thickness, and are meshed and
cached with fieldmesh like the head they are fitted to."""
import math

import numpy as np

from . import fieldmesh
from .sdf import F, carve, ellipsoid, smin


def _shell(base, t, thickness, k=0.0015):
    """The cloth between ``t - thickness`` and ``t`` off the surface ``base``."""
    return carve(base - t, base - (t - F(thickness)), k)


def _smooth(a, b, x):
    s = np.clip((x - a) / (b - a), 0.0, 1.0)
    return s * s * (3 - 2 * s)


def _cranium(p, fem):
    """The skull's vault alone (headshape's cranium, forehead and occiput, without the temples' hollows): cloth
    bridges those."""
    d = ellipsoid(p, (0, -0.012, 0.022), (0.0755, 0.095, 0.086))
    d = smin(d, ellipsoid(p, (0, 0.034, 0.03 + 0.003 * fem), (0.061, 0.05, 0.058)), 0.03)
    return smin(d, ellipsoid(p, (0, -0.056, -0.008), (0.058, 0.054, 0.06)), 0.03)


def cap_field(shape, visor=True):
    fem = shape.fem

    def field(p):
        base = _cranium(p, fem)
        y, z = p[:, 1], p[:, 2]
        rim = F(0.022) + F(0.02) * np.clip(y / F(0.09), -1, 1)           # 0.002 at the nape .. 0.042 over the brow
        t = F(0.012) + F(0.003) * _smooth(F(0.03), F(0.0), z - rim)     # the band sits a little looser
        d = _shell(base, t, 0.003)
        band = _shell(base, t + F(0.0015), 0.0048)                      # the turned band round the rim
        band = carve(band, rim + F(0.012) - z, 0.0015)
        d = smin(d, band, 0.001)
        d = carve(d, z - rim, 0.0012)
        if visor:      # a stiffened peak growing out of the band
            pitch = math.radians(16)
            rot = np.asarray(((1, 0, 0), (0, math.cos(pitch), math.sin(pitch)), (0, -math.sin(pitch), math.cos(pitch))))
            peak = ellipsoid(p, (0, 0.086, 0.043), (0.055, 0.05, 0.003), rot)
            peak = carve(peak, y - F(0.068), 0.002)                      # only in front of the forehead
            d = smin(d, peak, 0.005)
        return d
    return field


BOUNDS = ((-0.19, -0.2, -0.25), (0.19, 0.17, 0.16))


def mesh(head, kind, visor=True, under=()):
    """The headwear's mesh object, placed on the head (world coordinates); a hood's cowl drapes over ``under`` (the
    body and the garments on its shoulders)."""
    shape = head.shape
    if kind == "cap":
        obj = fieldmesh.build(cap_field(shape, visor), BOUNDS, 0.0011, 9000, "hat_cap", ("cap", shape.key(), visor),
                              ("headshape.py", "earshape.py", "headwear.py"), "headwear")
    else:
        from . import hood
        obj = hood.mesh(head, under)
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", (co.reshape(-1, 3) * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return obj
