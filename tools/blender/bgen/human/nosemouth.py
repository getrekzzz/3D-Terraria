"""The nose and mouth in the head's distance field (head-local units, see headshape).

The nose is one connected form: a bridge from between the eyes to a single rounded tip, the broad base of the
sidewalls, and a lower part in which the wings and the columella are one mass, the wings swelling out of it and
settling into the cheeks over a soft groove; the nostrils open underneath. A long nose (``noseLength``) grows bigger
overall and reaches further forward to a drooping point while its base stays on the face, the underside running in one
piece from the tip back to the nostrils; a wide one (``noseWidth``) spreads its tip, wings and nostrils and projects
less. The lips have a soft cupid's bow and a small tubercle, a mouth line turning down slightly at the corners, the
philtrum above as a shallow dip and the hollow below; ``mouthWidth`` widens the mouth, ``underbite`` carries the lower
lip and chin forward. Age lets the cheek sag over a soft nasolabial fold.
"""
import math

import numpy as np

from .sdf import F, carve, ellipsoid, round_cone, smin, tube

NOSTRIL_NORMAL = np.asarray((0.0, 0.4, -0.92)) / math.hypot(0.4, 0.92)   # nostrils open down, a little forward
FOLD_N = 6                                  # points along each nasolabial fold


def _basis(x_dir, up=(0.0, 0.0, 1.0)):
    x = np.asarray(x_dir, dtype=float)
    x /= np.linalg.norm(x)
    z = np.asarray(up, dtype=float) - x * np.dot(up, x)
    z /= np.linalg.norm(z)
    return np.stack([x, np.cross(z, x), z], 1)


def size(shape):
    """How much bigger than a person's the nose is overall (a long nose is a big one)."""
    return 1.0 + 0.35 * max(0.0, shape.nose_len - 1.0)


def tip_at(shape, x, y, z):
    """A point of the nose's bridge and tip (designed for the male nose) scaled for this face: women's tips turn up a
    little, a long nose projects further and droops at its end, a wide one (``noseWidth``) spreads flatter."""
    n, g, L, w = shape.n, size(shape), shape.nose_len, shape.nose_w
    low = 1.0 if z < -0.03 else 0.0
    return (x * n * g * w, 0.084 + (y - 0.084) * n * L / w ** 0.5, z * n * (1 + 0.15 * (g - 1)) + 0.001 * shape.fem * low
            - 0.007 * (L - 1.0) * low)


def base_at(shape, x, y, z):
    """A point of the nose's base (wings, nostrils, columella), which stays on the face however long the nose."""
    n, g, w = shape.n, size(shape), shape.nose_w
    return (x * n * g * w, 0.084 + (y - 0.084) * n * g / w ** 0.5, z * n * (1 + 0.15 * (g - 1)))


def nose(shape, p, d):
    n, g, L = shape.n, size(shape), shape.nose_len
    t, b = (lambda *q: tip_at(shape, *q)), (lambda *q: base_at(shape, *q))
    s = n * g
    point = 1.0 / L ** 0.3                     # a long nose narrows toward its point
    form = round_cone(p, t(0, 0.0848, 0.003), t(0, 0.0982, -0.027), 0.0046 * s, 0.0055 * s * point)          # bridge
    form = smin(form, round_cone(p, b(0, 0.077, 0.0), b(0, 0.0872, -0.037), 0.0074 * s, 0.0102 * s), 0.006 * s)  # sidewalls
    tip = t(0, 0.0995, -0.035)
    w = shape.nose_w
    form = smin(form, ellipsoid(p, tip, (0.0074 * s * point * w, 0.0078 * s * point, 0.0068 * s * point)), 0.007 * s)
    if L > 1:      # the long nose's mass carried out to its point, and its underside back to the nostrils
        form = smin(form, round_cone(p, t(0, 0.081, -0.008), t(0, 0.0972, -0.033), 0.0098 * s, 0.0064 * s * point), 0.008 * s)
        form = smin(form, round_cone(p, b(0, 0.0945, -0.0418), t(0, 0.0985, -0.0385), 0.0062 * s, 0.0058 * s * point), 0.008 * s)
    # The lower nose: the lobule's flat underside from the tip back to the lip, which the nostrils open in, and the
    # wings swelling out of it on either side.
    form = smin(form, ellipsoid(p, b(0, 0.0962, -0.0418), (0.0088 * s * w, 0.0086 * s, 0.0046 * s)), 0.006 * s)
    for sx in (1, -1):
        form = smin(form, ellipsoid(p, b(sx * 0.0116, 0.0902, -0.0414), (0.0066 * s * w ** 0.5, 0.0062 * s, 0.0056 * s * w ** 0.3),
                                    _basis((0.45 * sx, 1.0, -0.1), (0, 0, 1))), 0.006 * s)
    d = smin(d, form, 0.007 * s)
    a = shape.anchors
    for i, sx in enumerate((1, -1)):
        if a is not None:
            # A soft groove round the back of each wing where it meets the cheek, then the nostril, opening below.
            out = np.asarray((sx, 0.25, 0.0)) / math.hypot(1, 0.25)
            radii = [0.0003 * s, 0.0006 * s, 0.0004 * s]
            d = carve(d, tube(p, [c - out * r * 0.3 for c, r in zip(a["alar"][3 * i:3 * i + 3], radii)], radii), 0.0024 * s)
            d = carve(d, ellipsoid(p, a["nostril"][i] - NOSTRIL_NORMAL * 0.0004 * s, (0.0028 * s * w ** 0.5, 0.00125 * s * w ** 0.5, 0.0014 * s),
                                   _basis((-0.3 * sx, 0.95, 0.06), NOSTRIL_NORMAL)), 0.0012 * s)
    return d


def anchor_segments(shape):
    """(key, inside, outside) segments whose surface crossings place the nostrils and the grooves round the wings."""
    out = []
    for sx in (1, -1):        # across the underside, between the columella and the wing
        c = np.asarray(base_at(shape, sx * 0.0056, 0.0972, -0.0462))
        out.append(("nostril", c - NOSTRIL_NORMAL * 0.004, c + NOSTRIL_NORMAL * 0.006))
        o = np.asarray((sx, 0.25, 0.0)) / math.hypot(1, 0.25)
        for q in ((0.0162, 0.0928, -0.0362), (0.0186, 0.0885, -0.0418), (0.0172, 0.0868, -0.047)):
            c = np.asarray(base_at(shape, sx * q[0], q[1], q[2]))
            out.append(("alar", c - o * 0.005, c + o * 0.007))
        if sx > 0:            # the philtrum and the hollow under the lower lip, on the mid-line
            up = shape.mouth_up
            out.append(("philtrum", (0.0, 0.06, -0.0545 + 0.8 * up), (0.0, 0.14, -0.0545 + 0.8 * up)))
            out.append(("underlip", (0.0, 0.06, -0.0852 + up), (0.0, 0.14, -0.0852 + up)))
        if shape.old:         # the nasolabial fold, from beside the wing down to past the corner of the mouth
            for t in np.linspace(0.0, 1.0, FOLD_N):
                x, z = sx * (0.0205 + 0.0125 * t), -0.044 - 0.031 * t
                out.append(("fold", (x * 0.6, 0.05, z), (x * 1.3, 0.12, z)))
    return out


def lips(shape, p):
    """The lips' own masses (also where the painter puts their colour): the upper lip with a small tubercle and a soft
    cupid's bow, the fuller lower lip, both following the dental arch and thinning toward the corners of the mouth.
    Women's are fuller, an old man's thinner."""
    fem = shape.fem
    if shape.snout > 0 or shape.alien:        # a snout (or an alien's mouth) has no lips
        return np.ones(len(p), dtype=F)
    full = 1.0 + 0.2 * fem - 0.3 * shape.old
    q = np.asarray(p, dtype=F).copy()
    q[:, 2] -= F(shape.mouth_up)
    q[:, 1] += F(0.0014 * fem)          # (set back a little: full, not pouting)
    q[:, 0] /= F(shape.mouth_w)
    q[:, 1] += F(14.0) * q[:, 0] * q[:, 0]
    thin = F(1) + F(0.45) * (q[:, 0] / F(0.0275)) ** 2
    q[:, 1] = F(0.09) + (q[:, 1] - F(0.09)) * thin
    q[:, 2] = F(-0.07) + (q[:, 2] + F(0.07)) * thin
    upper = ellipsoid(q, (0, 0.0907, -0.0652 + 0.0004 * fem), (0.0285 - 0.0015 * fem, 0.0064 + 0.0002 * fem, 0.0049 * full))
    upper = smin(upper, ellipsoid(q, (0, 0.0942, -0.0666), (0.0068, 0.0034, 0.0034 * full)), 0.004)
    for sx in (1, -1):
        upper = smin(upper, ellipsoid(q, (sx * 0.0045, 0.0924, -0.0628 + 0.0004 * fem), (0.0058, 0.0034, 0.0022 * full)), 0.006)
    ub = shape.underbite             # an underbite carries the lower lip out in front of the upper
    lower = ellipsoid(q, (0, 0.0886 + 0.008 * ub, -0.0748 + 0.0015 * ub), (0.025 - 0.0015 * fem, 0.0074 + 0.0003 * fem + 0.001 * ub, 0.006 * full))
    return smin(upper, lower, 0.002)


def mouth(shape, p, d):
    d = smin(d, lips(shape, p), 0.0045)
    a = shape.anchors
    if a is not None:
        # A rounded groove between the lips with soft dimples at the corners, sunk just into the surface found for it.
        xs = a["mouth"][:, 0] / (0.0218 * shape.mouth_w)
        radii = 0.00075 * (0.55 + 0.45 * np.cos(xs * np.pi / 2))
        sink = radii * 0.35 + 0.0011 * xs ** 4          # the ends sink under the skin, so the line fades out
        d = carve(d, tube(p, list(a["mouth"] - np.stack([0 * radii, sink, 0 * radii], 1)), list(radii)), 0.0016)
    if a is not None and "fold" in a:
        # Age: the cheek sags over the nasolabial fold, a wide shallow dip in front of it (no cut, no pit at the end).
        for i, sx in enumerate((1, -1)):
            line = a["fold"][FOLD_N * i:FOLD_N * (i + 1)]
            out = np.asarray((sx * 0.55, 1.0, 0.0)) / math.hypot(0.55, 1.0)
            side = np.asarray((sx, -0.55, 0.0)) / math.hypot(1.0, 0.55)
            w = np.sin(np.pi * (np.arange(FOLD_N) + 0.5) / FOLD_N)
            r = 0.006 * (0.6 + 0.4 * w)
            d = carve(d, tube(p, list(line + out * (r - 0.0006 * w)[:, None]), list(r)), 0.003)
            r = r * 1.3
            d = smin(d, tube(p, list(line + side * 0.006 - out * (r + 0.0002)[:, None]), list(r)), 0.006)
    if a is not None:
        # The philtrum's shallow dip and the hollow under the lower lip, each sunk a set depth into the surface found
        # for it (so a heavier jaw or an underbite never leaves them as a void inside the flesh).
        d = carve(d, ellipsoid(p, a["philtrum"][0] + np.asarray((0.0, 0.001 - 0.0004, 0.0)), (0.003, 0.001, 0.0035)), 0.002)
        d = carve(d, ellipsoid(p, a["underlip"][0] + np.asarray((0.0, 0.0055 - 0.0013, 0.0)), (0.015, 0.0055, 0.0036)), 0.006)
    return d
