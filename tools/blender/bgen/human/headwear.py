"""Soft headwear sculpted as distance fields over the bare skull (headshape), in head-local units like the head:

- a cap: a cloth crown that sits on the skull (over the hair it leaves below its band), its rim low at the nape and
  over the brow at the front, with a turned band and, unless asked not to, a stiffened peak;
- a hood: a cloth shell standing off the skull that hangs looser round the jaw and neck as a cowl, cut in an oval
  round the face with a rolled hem there and at the neck.

Both are shells (an outer surface minus an inner one), so their edges show the cloth's thickness, and are meshed and
cached with fieldmesh like the head they are fitted to."""
import math

import numpy as np

from . import fieldmesh
from .sdf import F, carve, ellipsoid, round_cone, smin


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


def hood_field(shape):
    fem = shape.fem

    def field(p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        # Loose cloth over a soft volume round the head, falling round the jaw into a capelet over the shoulders.
        vol = smin(_cranium(p, fem), ellipsoid(p, (0, 0.006, -0.062), (0.074, 0.084, 0.085)), 0.05)
        vol = smin(vol, round_cone(p, (0, -0.02, -0.1), (0, -0.035, -0.27), 0.07, 0.16), 0.07)
        t = F(0.014) + F(0.006) * _smooth(F(0.0), F(-0.1), y) + F(0.006) * _smooth(F(0.0), F(-0.12), z)
        # The outer surface rises to a soft point at the back of the crown and falls in loose vertical folds.
        outer = smin(vol - t, ellipsoid(p, (0, -0.098, 0.05), (0.032, 0.046, 0.04)), 0.026)
        ang = np.arctan2(x, -y)
        outer = outer - F(0.0025) * np.sin(ang * F(7.0)) * _smooth(F(0.04), F(-0.12), z)
        d = carve(outer, vol - (t - F(0.005)), 0.0015)
        # The face opening: an oval seen from the front, only ahead of the ears; its edge rolls over.
        oval = (np.sqrt((x / F(0.06)) ** 2 + ((z + F(0.032)) / F(0.092)) ** 2) - F(1)) * F(0.06)
        oval = np.maximum(oval, F(0.012) - y)
        d = carve(d, oval, 0.004)
        d = carve(d, z + F(0.235), 0.004)                               # the hem over the shoulders
        return d
    return field


BOUNDS = ((-0.19, -0.2, -0.25), (0.19, 0.17, 0.16))


def mesh(head, kind, visor=True):
    """The headwear's mesh object, placed on the head (world coordinates)."""
    shape = head.shape
    if kind == "cap":
        field, key, target = cap_field(shape, visor), ("cap", shape.key(), visor), 9000
    else:
        field, key, target = hood_field(shape), ("hood", shape.key()), 12000
    obj = fieldmesh.build(field, BOUNDS, 0.0011, target, f"hat_{kind}", key, ("headshape.py", "earshape.py", "headwear.py"), "headwear")
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", (co.reshape(-1, 3) * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return obj
