"""Monster faces in the head's distance field (head-local units, see headshape), chosen by the ``face`` parameter:

- cyclops: one huge bulging eye in a stretched socket where the eyes and the bridge of the nose were, heavy puffed
  lids round it and two slit nostrils under it (the eyeball is built by monsterparts; no brows);
- brute: a flat-topped, squarish skull with a heavy bar of brow over sunken eyes and a scar across the forehead and
  down one cheek (its stitches and the bolts in the neck are monsterparts);
- ghoul: a swollen, lumpy brow over deep sockets, a stretched lipless mouth bared wide over teeth (monsterparts) and
  veins standing out over the skull and temples;
- void: no face at all, the front of the head scooped into a smooth dark hollow the hood's shadow swallows (glowing
  eyes float in it, monsterparts).

Skin tints for each (sockets, scars, veins, gums) come from tint(), which the skin painter calls.
"""
import functools
import hashlib
import math
import pathlib

import numpy as np

from .sdf import F, carve, ellipsoid, smax, smin, sphere, tube

EYELESS = ("cyclops", "void")           # faces without a person's two eyes, lids, lashes or painted brows
CYC_C = np.asarray((0.0, 0.071, -0.004))       # the cyclops eye's centre and radius
CYC_R = 0.031
MOUTH = (np.asarray((0.0, 0.097, -0.0725)), np.asarray((0.041, 0.018, 0.0122)))   # the ghoul's mouth (arched space)
ARCH = 14.0                                     # how fast the mouth's arch turns back round the face


def digest():
    """Changes whenever this module does: it joins the head's cache key, so every mesh fitted to a monster's head
    (hair, hood, caps) is made afresh."""
    return hashlib.sha1(pathlib.Path(__file__).read_bytes()).hexdigest()[:12]


def roundbox(p, c, half, r):
    q = np.abs(p - np.asarray(c, dtype=F)) - (np.asarray(half, dtype=F) - F(r))
    out = np.linalg.norm(np.maximum(q, F(0)), axis=1)
    return (out + np.minimum(q.max(axis=1), F(0)) - F(r)).astype(F)


# ---------------------------------------------------------------------------------------------------- the skull
def skull(shape, p, d):
    """Changes to the vault, which the hair is laid over as well (applied with or without the face)."""
    kind = shape.monster
    if kind == "brute":
        # Squared off: a rounded box fills the vault out at the temples and the crown, then the top is cut flat.
        d = smin(d, roundbox(p, (0, -0.018, 0.046), (0.0735, 0.084, 0.056), 0.024), 0.016)
        d = smax(d, p[:, 2] - F(0.094), 0.012)
    elif kind == "ghoul":
        # A swollen, lumpy forehead bulging forward over the brow.
        for c, r in (((0.0, 0.058, 0.05), (0.05, 0.04, 0.042)), ((0.026, 0.07, 0.028), (0.024, 0.018, 0.018)),
                     ((-0.024, 0.071, 0.03), (0.022, 0.017, 0.019)), ((0.012, 0.062, 0.074), (0.026, 0.024, 0.02))):
            d = smin(d, ellipsoid(p, c, r), 0.018)
        d = smin(d, _veins(p), 0.0035)
    return d


VEINS = (  # polylines over the vault (direction cosines from the skull's centre) for the ghoul's raised veins
    ((0.98, 0.1, -0.15), (0.93, 0.05, 0.3), (0.8, -0.05, 0.58), (0.55, -0.12, 0.82), (0.25, -0.05, 0.96)),
    ((0.93, 0.05, 0.3), (0.75, 0.3, 0.55), (0.45, 0.45, 0.75)),
    ((-0.97, 0.15, -0.1), (-0.9, 0.0, 0.4), (-0.7, -0.2, 0.68), (-0.4, -0.35, 0.84)),
    ((-0.9, 0.0, 0.4), (-0.72, 0.35, 0.58), (-0.5, 0.5, 0.68), (-0.2, 0.62, 0.74)),
    ((0.1, -0.7, 0.7), (0.3, -0.4, 0.86), (0.2, 0.0, 0.98), (0.05, 0.35, 0.92)),
    ((-0.6, -0.75, 0.25), (-0.45, -0.6, 0.62), (-0.15, -0.6, 0.78)),
)


@functools.lru_cache(maxsize=1)
def _vein_points():
    """The veins as head-local polylines lying just under the vault's surface (its cranium ellipsoid)."""
    c, r = np.asarray((0.0, -0.012, 0.022)), np.asarray((0.0755, 0.095, 0.086))
    out = []
    for line in VEINS:
        pts = []
        for a, b in zip(line, line[1:]):
            for t in np.linspace(0, 1, 4, endpoint=False):
                v = np.asarray(a) * (1 - t) + np.asarray(b) * t
                v = v / np.linalg.norm(v)
                wob = 0.06 * math.sin(len(pts) * 1.7 + v[0] * 9)
                v = v + np.cross(v, (0, 0, 1)) * wob
                v = v / np.linalg.norm(v)
                k = 1.0 / np.linalg.norm(v / r)          # onto the ellipsoid along v
                pts.append(c + v * k * 1.004)
        out.append(np.asarray(pts))
    return out


def _veins(p):
    d = None
    for line in _vein_points():
        n = len(line)
        radii = [0.0021 * (0.5 + 0.5 * math.sin(math.pi * (i + 0.5) / n)) + 0.0008 for i in range(n)]
        v = tube(p, list(line), radii)
        d = v if d is None else np.minimum(d, v)
    return d


# ---------------------------------------------------------------------------------------------------- the face
def face(shape, p, d):
    """The face (eyes, nose, mouth) of a monster head; person's features are borrowed from the shape where wanted."""
    kind = shape.monster
    if kind == "cyclops":
        d = _region(p, d, CYC_C, 0.07, lambda q, dd: _cyclops(q, dd))
        d = _region(p, d, (0, 0.09, -0.06), 0.05, lambda q, dd: _slit_nostrils(q, dd))
        return _region(p, d, (0, 0.082, -0.071), 0.046, shape._mouth)
    if kind == "void":
        return carve(d, ellipsoid(p, (0, 0.122, -0.026), (0.058, 0.062, 0.074)), 0.016)
    for sx in (1, -1):
        d = _region(p, d, shape.eyes[sx], 0.047, lambda q, dd, sx=sx: shape._eye(q, dd, sx))
    d = _region(p, d, (0, 0.09, -0.03), 0.058, shape._nose)
    if kind == "ghoul":
        return _region(p, d, MOUTH[0], 0.06, lambda q, dd: _ghoul_mouth(q, dd))
    d = _region(p, d, (0, 0.082, -0.071), 0.046, shape._mouth)
    if kind == "brute":
        # A heavy bar of brow straight across, overhanging the eyes.
        d = smin(d, tube(p, [(-0.058, 0.066, 0.015), (-0.031, 0.0905, 0.0165), (0.0, 0.093, 0.018), (0.031, 0.0905, 0.0165),
                             (0.058, 0.066, 0.015)], [0.0095, 0.0126, 0.0118, 0.0126, 0.0095]), 0.012)
        lines = scars(shape)
        if lines is not None:
            for line, normal in lines:
                n = len(line)
                r = 0.0011 * (0.4 + 0.6 * np.sin(np.pi * (np.arange(n) + 0.5) / n))
                d = carve(d, tube(p, list(line - normal * (r * 0.2)[:, None]), list(r)), 0.0016)
    return d


def _region(p, d, centre, radius, fn):
    from .sdf import region
    return region(p, d, centre, radius, fn)


def _cyclops(p, d):
    C, R = CYC_C, CYC_R
    Rs = R + 0.0028
    # A swelling where the eyes and the bridge of the nose were, for the socket to stretch over, then room for the eye.
    d = smin(d, ellipsoid(p, C + (0, -0.006, 0.0), (0.05, 0.024, 0.04)), 0.016)
    d = carve(d, sphere(p, C, R + 0.0004), 0.0008)
    d = smin(d, sphere(p, C, Rs), 0.011)
    u, v, w = p[:, 0] - F(C[0]), p[:, 2] - F(C[2]), p[:, 1] - F(C[1])
    # Puffed rolls under and over the opening, the lower one heavy and sagging.
    half = F(0.0272)
    t = np.clip(u / half, -1, 1)
    up = F(0.0058) + F(0.0142) * (1 - t * t) ** F(0.8)            # the opening: a wide almond, heavy-lidded on top
    lo = -(F(0.0042) + F(0.0148) * (1 - t * t) ** F(0.7))
    on = np.sqrt(np.maximum(F(Rs * Rs) - u * u - v * v, 0))
    roll = np.abs(w - on) + np.maximum(np.abs(v - (lo - F(0.0028))) - F(0.0016), 0) - F(0.0026)
    roll = np.maximum(roll, np.abs(u) - half - F(0.006))
    d = smin(d, roll, 0.004)
    brow = np.abs(w - on) + np.maximum(np.abs(v - (up + F(0.0034))) - F(0.0012), 0) - F(0.0019)
    brow = np.maximum(brow, np.abs(u) - half - F(0.004))
    d = smin(d, brow, 0.003)
    opening = np.maximum.reduce([v - up, lo - v, np.abs(u) - half]) * F(0.8)
    opening = np.maximum.reduce([opening, F(0.004) - w, w - F(Rs + 0.004)])
    return carve(d, opening, 0.0012)


def _slit_nostrils(p, d):
    for sx in (1, -1):
        d = carve(d, ellipsoid(p, (sx * 0.0058, 0.0905, -0.0485), (0.0016, 0.006, 0.0036)), 0.0016)
    return d


def _ghoul_mouth(p, d):
    c, r = MOUTH
    # A wide opening cut deep into the face, wrapping round it with the dental arch.
    return carve(d, ellipsoid(_arched(p), c, r), 0.004)


def _arched(p):
    q = np.array(p, dtype=F)
    q[:, 1] += F(ARCH) * q[:, 0] * q[:, 0]
    return q


def scars(shape):
    """The brute's scars as (surface points, outward direction) in head-local units: across the forehead and down
    the left cheek. Found once on the surface without them (None while that is being worked out)."""
    if getattr(shape, "_scars", False) is not False:
        return shape._scars
    shape._scars = None
    lines, segs = [], []
    xs = np.linspace(-0.047, 0.04, 14)
    fore = [((x, 0.0, 0.05 + 0.006 * math.sin(x * 70) - 0.06 * x), (0, 1, 0)) for x in xs]
    zs = np.linspace(-0.016, -0.07, 9)
    out = np.asarray((-0.72, 0.69, 0.0))
    cheek = [((-0.012 + 0.08 * z, 0.03, z), tuple(out)) for z in zs]
    for line in (fore, cheek):
        a = np.asarray([q for q, _ in line])
        n = np.asarray([v for _, v in line], dtype=float)
        segs.append((a, n))
    found = []
    for a, n in segs:
        hits = shape._surface(a, a + n * 0.14)
        found.append((hits, n.astype(F)))
    shape._scars = found
    return found


# ---------------------------------------------------------------------------------------------------- skin tints
def tint(head, kind, q, c):
    """The skin colour ``c`` at head-local ``q`` with the monster's marks."""
    from .head import smooth
    from .paint import gauss, mix, mul
    if kind == "void":
        return (0.012, 0.012, 0.016)
    p = np.asarray(q[:])
    if kind == "cyclops":
        r = np.linalg.norm(p - CYC_C)
        rim = gauss(max(0.0, r - CYC_R - 0.003), 0.006) * (1.0 if p[1] > CYC_C[1] - 0.01 else 0.0)
        c = mix(c, mul(c, (1.0, 0.55, 0.5)), rim * 0.55)
        c = mix(c, mul(c, (0.5, 0.45, 0.45)), gauss(max(0.0, r - CYC_R - 0.0035), 0.0025) * 0.6 * (p[1] > CYC_C[1]))
        for sx in (1, -1):
            c = mix(c, (0.08, 0.05, 0.05), gauss(math.hypot((p[0] - sx * 0.0058) / 0.6, p[2] + 0.0485), 0.0035) * (p[1] > 0.085))
        return c
    for sx in (1, -1):          # sunken, shadowed sockets
        e = head.local(head.eyes[sx])
        dd = math.hypot(p[0] - e.x, (p[2] - e.z) * 1.15)
        c = mix(c, mul(c, (0.42, 0.4, 0.42)), gauss(dd, 0.02) * 0.75 * smooth(0.03, 0.06, p[1]))
    if kind == "brute":
        lip = float(head.shape.lips(np.asarray([p], dtype=F))[0]) if p[1] > 0.075 and p[2] < -0.05 else 1.0
        c = mix(c, mul(c, (0.45, 0.48, 0.48)), (1.0 - smooth(0.0002, 0.0012, lip)) * 0.8)     # grey, bloodless lips
        lines = scars(head.shape) or ()
        for line, _ in lines:
            dist = np.min(np.linalg.norm(line - p, axis=1))
            c = mix(c, mul(c, (0.62, 0.45, 0.45)), gauss(dist, 0.0028) * 0.85)
    elif kind == "ghoul":
        m, r = MOUTH
        inside = np.linalg.norm((_arched(p[None])[0] - m) / (r + 0.006))
        c = mix(c, (0.5, 0.13, 0.15), 1.0 - smooth(0.95, 1.2, inside))           # gums
        c = mix(c, (0.1, 0.02, 0.03), 1.0 - smooth(0.6, 0.85, inside))            # the throat
        best = min(np.min(np.linalg.norm(line - p, axis=1)) for line in _vein_points())
        c = mix(c, mul(c, (0.55, 0.32, 0.45)), gauss(best, 0.004) * 0.8)
    return c
