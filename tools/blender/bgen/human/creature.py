"""Creature faces in the head's distance field (head-local units, see headshape): a reptile's head (``snout``) in
place of a person's, on heads without outer ears a shallow ear opening, and an ogre's tusks rising from the lower jaw
in front of the upper lip.

The reptile's skull is long and low: a flat braincase narrower than the jaw muscles that bulge behind and below the
eyes, a ridge over each eye, and the snout tapering forward from between the eyes to a blunt, flattened tip with the
nostrils on it. The lower jaw is a bar on each side from its angle under the ear to the chin, so its underline shows
from the side and the front, with the throat set in above it and running back down into a neck narrower than the
head. A lipless mouth line runs from the tip back under the eyes to a slight upturn.
"""
import numpy as np

from .faceforms import _basis
from .sdf import carve, ellipsoid, round_cone, smin, tube


def tip_y(shape):
    """How far forward the snout reaches."""
    return 0.105 + 0.06 * shape.snout


def _chain(p, stations):
    """A smooth taper through ellipsoids (centre, semi-axes) blended end to end."""
    d = None
    for c, r in stations:
        e = ellipsoid(p, c, r)
        d = e if d is None else smin(d, e, 0.018)
    return d


def _stations(shape, y0, y1, w, h, top=None, bottom=None, n=5):
    """Ellipsoids from y0 back to y1 at the tip (half-widths w, half-heights h, each a (root, tip) pair), hanging from
    a top line or standing on a bottom line (z at the root, z at the tip)."""
    out = []
    step = (y1 - y0) / (n - 1)
    for i in range(n):
        t = i / (n - 1)
        hw, hh = w[0] + (w[1] - w[0]) * t, h[0] + (h[1] - h[0]) * t
        if top is not None:
            z = top[0] + (top[1] - top[0]) * t - hh
        else:
            z = bottom[0] + (bottom[1] - bottom[0]) * t + hh
        hl = step * 0.95 if i < n - 1 else max(hw, hh) * 0.95
        out.append(((0.0, y0 + step * i, z), (hw, hl, hh)))
    return out


def skull(shape, p):
    """A reptile's head in place of a person's skull and face masses (see the module docstring)."""
    ty, br = tip_y(shape), shape.brow
    E = ellipsoid
    # The braincase, long, low and flat on top, and the brow between the eyes sloping down into the snout.
    d = E(p, (0, -0.012, 0.001), (0.049, 0.074, 0.028))
    d = smin(d, E(p, (0, 0.032, 0.0), (0.045, 0.05, 0.029)), 0.024)
    d = smin(d, E(p, (0, -0.046, -0.026), (0.038, 0.032, 0.03)), 0.03)     # the back of the skull over the nape
    for sx in (1, -1):
        # The jaw muscles, wider than the braincase, behind and below the eyes.
        d = smin(d, E(p, (sx * 0.035, -0.004, -0.036), (0.03, 0.044, 0.034)), 0.028)
        # The ridge over each eye: a low ledge along the top of the head, converging toward the snout and level (an
        # outer end turned down reads sad), clear of the upper lid so the eye stays open.
        d = smin(d, E(p, (sx * 0.031, 0.06, 0.016), (0.026, 0.012 + 0.003 * br, 0.0055 + 0.002 * br), _basis((-sx * 0.15, 1, 0.0))), 0.012)
        # The lower jaw's bar, from its angle under the ear to the chin.
        d = smin(d, round_cone(p, (sx * 0.04, -0.008, -0.07), (sx * 0.013, ty - 0.034, -0.071), 0.0125, 0.008), 0.018)
    # The upper jaw, wide between the cheeks and tapering forward to a blunt, flattened tip, with a ridge down its top,
    # and the lower jaw under it, a little shorter.
    upper = _chain(p, _stations(shape, 0.035, ty - 0.017, (0.047, 0.019), (0.031, 0.0135), top=(0.008, -0.034)))
    upper = smin(upper, round_cone(p, (0, 0.074, 0.012), (0, ty - 0.024, -0.028), 0.0115, 0.0085), 0.012)
    lower = _chain(p, _stations(shape, 0.03, ty - 0.021, (0.04, 0.016), (0.02, 0.0105), bottom=(-0.084, -0.0705)))
    d = smin(d, smin(upper, lower, 0.006), 0.02)
    # The throat, set in above the jaw's underline and running back down into the neck.
    return smin(d, round_cone(p, (0, -0.004, -0.14), (0, 0.058, -0.084), 0.038, 0.017), 0.012)


def _mouth_z(shape, y):
    t = np.clip((tip_y(shape) - y) / (tip_y(shape) - 0.056), 0, 1)
    return -0.0605 + 0.0045 * t * t


def anchor_segments(shape):
    """(key, inside, outside) segments whose surface crossings place the mouth line and nostrils."""
    ty = tip_y(shape)
    out = []
    ys = np.linspace(0.056, ty - 0.012, 8)
    for y in ys:
        z = float(_mouth_z(shape, y))
        out.append(("snout_mouth", (0.0, y, z), (0.09, y, z)))
    z = float(_mouth_z(shape, ty))
    out.append(("snout_mouth", (0.0, ty - 0.03, z), (0.0, ty + 0.04, z)))
    for y in ys[::-1]:
        z = float(_mouth_z(shape, y))
        out.append(("snout_mouth", (0.0, y, z), (-0.09, y, z)))
    for sx in (1, -1):
        out.append(("snout_nostril", (sx * 0.007, ty - 0.02, -0.044), (sx * 0.011, ty + 0.02, -0.02)))
    return out


def field(shape, p, d):
    """The snout's mouth line and nostrils, cut into the surface the jaws make."""
    a = shape.anchors
    if a is not None and "snout_mouth" in a:
        line = a["snout_mouth"]
        n = len(line)
        # (wide enough for the mesh to resolve cleanly: a hairline groove comes out ragged)
        r = 0.0021 * (0.5 + 0.5 * np.sin(np.pi * (np.arange(n) + 0.5) / n))
        axis = np.stack([0 * line[:, 0], np.minimum(line[:, 1], tip_y(shape) - 0.03), line[:, 2]], 1)
        inward = axis - line
        inward /= np.maximum(np.linalg.norm(inward, axis=1), 1e-9)[:, None]
        d = carve(d, tube(p, list(line + inward * (r * 0.3)[:, None]), list(r)), 0.002)
        for i, sx in enumerate((1, -1)):
            c = a["snout_nostril"][i]
            d = carve(d, ellipsoid(p, c, (0.0018, 0.0034, 0.0016)), 0.0012)
    return d


def earless(shape, p, d):
    """A reptile's ear opening (tympanum) where the outer ear would be: a round dish sunk under the skin with a crisp
    edge, placed on the surface found for it (an anchor)."""
    a = shape.anchors
    if a is None or "tympanum" not in a:
        return d
    for i, sx in enumerate((1, -1)):
        # A dish sunk well under the skin with a crisp edge (a shallow soft dip reads as a raised nub).
        c = a["tympanum"][i] + np.asarray((sx * 0.0008, 0.0, 0.0))
        d = carve(d, ellipsoid(p, c, (0.003, 0.0072, 0.0066)), 0.0012)
    return d


def tusks(shape, p):
    """Two tusks from the lower jaw at the corners of the mouth, curving up and out in front of the upper lip (the
    painter colours them with ``tusks``)."""
    mw, ub = shape.mouth_w, shape.underbite
    d = None
    for sx in (1, -1):
        base, mid, tip = ((sx * 0.0175 * mw, 0.083 + 0.007 * ub, -0.081), (sx * 0.021 * mw, 0.0925 + 0.004 * ub, -0.066),
                          (sx * 0.0235 * mw, 0.0955 + 0.002 * ub, -0.0525))
        t = smin(round_cone(p, base, mid, 0.0046, 0.0032), round_cone(p, mid, tip, 0.0032, 0.0011), 0.002)
        d = t if d is None else np.minimum(d, t)
    return d


def with_tusks(shape, p, d):
    return smin(d, tusks(shape, p), 0.0015)
