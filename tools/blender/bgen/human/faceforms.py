"""The head's bone and fat masses in its distance field (head-local units, see headshape): cranium, forehead and
occiput, brow ridge, cheekbones and the cheeks' fat, the muzzle round the mouth, the lower face and chin; and what age
does to them.

The lower face is one lofted form: its horizontal sections are superellipses that narrow toward the chin, cut below by
the plane of the jaw line and behind by the ramus, both edges rounded. The side of the face so runs in one clean plane
from under the cheekbone down to the jaw line, with no crease between cheek and jaw. Women's faces are softer and
younger: a rounder forehead, a lighter brow, full apple cheeks set low and forward, a short lower face with rounder
sections and a soft jaw line over a small chin (no hollow under the cheekbone). A young man's (``age`` young) is
softened part way: fuller cheeks set a little lower, a fuller lower face and a softer jaw line. The mouth sits back on
the line from the nose to the chin, which comes forward to meet it (no muzzle). Weight fills the lower face and cheeks
and rounds the jaw line; ``jaw`` widens and squares it (below 0 it narrows to a pointed chin jutting forward and down),
``underbite`` carries it forward, ``brow`` builds a jutting ridge over deep-set eyes, ``forehead`` (0..1) lowers the
vault and shortens it behind and slopes the forehead back into the brow (a goblin's, so the face is not left under a
dome). Age thins the cheeks' fat and lets it sink into jowls, hollows the temples, and draws three lines across the
forehead and crow's feet fanning from the outer corners of the eyes.
"""
import numpy as np

from .sdf import F, capsule, carve, ellipsoid, smax, smin, tube

# The lower face's sections from under the chin up to the cheekbones: half-width and how far forward they reach.
LOFT_Z = (-0.135, -0.115, -0.1, -0.085, -0.07, -0.055, -0.04, -0.025, -0.01, 0.0)
WIDTH = {"m": (0.038, 0.045, 0.0505, 0.0555, 0.0585, 0.062, 0.065, 0.066, 0.062, 0.058),
         "f": (0.034, 0.041, 0.0465, 0.051, 0.0545, 0.0585, 0.062, 0.0645, 0.062, 0.058)}
SQUARE = {"m": (2.6, 2.8, 3.0, 2.9, 2.8, 2.85, 2.9, 2.75, 2.4, 2.0),      # the sections' superellipse exponent
          "f": (2.1, 2.2, 2.35, 2.5, 2.65, 2.75, 2.8, 2.75, 2.4, 2.0)}
FRONT = {"m": (0.082, 0.087, 0.089, 0.088, 0.086, 0.0855, 0.083, 0.077, 0.068, 0.062),
         "f": (0.078, 0.084, 0.0875, 0.087, 0.0855, 0.085, 0.0825, 0.077, 0.068, 0.062)}
JAW_WIDE = (0.012, 0.014, 0.014, 0.013, 0.01, 0.006, 0.003, 0.001, 0.0, 0.0)     # added by ``jaw``
FAT_WIDE = (0.018, 0.018, 0.017, 0.015, 0.013, 0.011, 0.009, 0.006, 0.003, 0.0)  # added by weight
LEAN = (0.0, 0.001, 0.002, 0.003, 0.004, 0.004, 0.003, 0.001, 0.0, 0.0)        # taken away by a lean build
YOUNG = (0.0, 0.001, 0.002, 0.003, 0.0035, 0.0035, 0.003, 0.0015, 0.0, 0.0)     # added on a young man (softer cheeks)
LOFT_Y = -0.012                     # the sections' centre line (they are widest over the ramus)


def _basis(x_dir, up=(0.0, 0.0, 1.0)):
    """Local-to-world rotation whose local x runs along ``x_dir`` and local z stays near ``up``."""
    x = np.asarray(x_dir, dtype=float)
    x /= np.linalg.norm(x)
    z = np.asarray(up, dtype=float) - x * np.dot(up, x)
    z /= np.linalg.norm(z)
    y = np.cross(z, x)
    return np.stack([x, y, z], 1)


def _plane(p, at, normal):
    """Signed distance to the plane through ``at`` (y, z) with outward ``normal`` (y, z), in the y-z plane."""
    n = np.asarray(normal, dtype=float)
    n = n / np.linalg.norm(n)
    return ((p[:, 1] - F(at[0])) * F(n[0]) + (p[:, 2] - F(at[1])) * F(n[1])).astype(F)


def jaw_points(shape):
    """The jaw's angle and the bottom of the chin as (y, z) in the mid-plane."""
    fem, jw = shape.fem, shape.jaw
    sq, pt = max(0.0, jw), max(0.0, -jw)      # a square jaw's chin is broad and deep; a pointed one juts forward and down
    angle = (-0.006 + 0.003 * fem, -0.077 + 0.0105 * fem - 0.004 * jw)
    chin = (0.0835 - 0.002 * fem + 0.005 * sq + 0.006 * pt + 0.008 * shape.underbite,
            -0.1135 + 0.0135 * fem - 0.006 * sq - 0.008 * pt - 0.003 * shape.full)
    return angle, chin


def lower_face(shape, p):
    """The lofted lower face (see the module docstring)."""
    fem, jw = shape.fem, shape.jaw
    w = np.asarray(WIDTH["m"]) * (1 - fem) + np.asarray(WIDTH["f"]) * fem
    yg = shape.young * shape.male
    w = (w + (jw if jw > 0 else 1.6 * jw) * np.asarray(JAW_WIDE) + shape.full * np.asarray(FAT_WIDE) - shape.lean * (1 - 0.6 * yg) * np.asarray(LEAN)
         + yg * np.asarray(YOUNG))
    front = np.asarray(FRONT["m"]) * (1 - fem) + np.asarray(FRONT["f"]) * fem
    lz = np.asarray(LOFT_Z)
    front = front + (0.006 * abs(jw) + 0.009 * shape.underbite) * np.clip((-0.055 - lz) / 0.035, 0, 1)
    n = np.asarray(SQUARE["m"]) * (1 - fem) + np.asarray(SQUARE["f"]) * fem + 0.25 * jw + 0.2 * shape.full + 0.8 * min(jw, 0.0)
    z = p[:, 2]
    a = np.interp(z, LOFT_Z, w).astype(F)
    b = (np.interp(z, LOFT_Z, front) - LOFT_Y).astype(F)
    n = np.interp(z, LOFT_Z, n).astype(F)
    # Superellipse sections: distance estimated from the implicit function and its gradient.
    qx, qy = np.abs(p[:, 0]) / a + F(1e-6), np.abs(p[:, 1] - F(LOFT_Y)) / b + F(1e-6)
    k0 = (qx ** n + qy ** n) ** (F(1) / n)
    gx, gy = (qx / k0) ** (n - 1) / a, (qy / k0) ** (n - 1) / b
    d = (k0 - F(1)) / np.maximum(np.sqrt(gx * gx + gy * gy), F(1e-6))
    (ay, az), (cy, cz) = jaw_points(shape)
    soft = 0.009 + 0.008 * fem + 0.008 * shape.full - 0.002 * jw + 0.003 * yg
    d = smax(d, _plane(p, (ay, az), (cz - az, ay - cy)), soft)                # the jaw line
    d = smax(d, _plane(p, (ay, az), (-0.042, -0.012)), 0.012 + 0.012 * fem)    # the ramus, up behind the angle
    return smax(d, p[:, 2] - F(0.004), 0.01)


def masses(shape, p):
    """Skull, brow, cheekbones, cheeks, muzzle, lower face and chin, blended softly."""
    fem, male = shape.fem, shape.male
    br, jw, fh = shape.brow, shape.jaw, shape.forehead
    E = ellipsoid
    # Cranium, forehead (rounder and more upright on women; low and sloping back from the brow with ``forehead``),
    # occiput.
    d = E(p, (0, -0.012 + 0.006 * fh, 0.022 - 0.012 * fh), (0.0755 - 0.004 * fh, 0.095 - 0.022 * fh, 0.086 - 0.026 * fh))
    d = smin(d, E(p, (0, 0.034 + 0.002 * fem - 0.008 * fh, 0.03 + 0.005 * fem - 0.016 * fh),
                  (0.061 + 0.001 * fem, 0.05 + 0.002 * fem, 0.058 + 0.004 * fem - 0.022 * fh)), 0.03)
    d = smin(d, E(p, (0, -0.056 + 0.014 * fh, -0.008 - 0.004 * fh), (0.058 - 0.005 * fh, 0.054 - 0.014 * fh, 0.06 - 0.008 * fh)), 0.03)
    d = smin(d, lower_face(shape, p), 0.02)
    # Muzzle: the maxilla under the nose and the ring of muscle round the mouth, standing a little proud of the loft
    # in the middle only, so the nose's wings stand clear of the face beside them.
    d = smin(d, E(p, (0, 0.062, -0.05), (0.024, 0.03, 0.022)), 0.016)
    d = smin(d, E(p, (0, 0.059, -0.067 + shape.mouth_up), ((0.028 - 0.001 * fem) * shape.mouth_w, 0.027, 0.02)), 0.016)
    for sx in (1, -1):
        # Brow ridge over each eye, lower and further back toward the temple (a heavy, jutting ledge with ``brow``).
        d = smin(d, E(p, (sx * (0.029 + 0.002 * br), 0.0772 + 0.002 * male + 0.006 * br, 0.0165 + 0.001 * fem - 0.0005 * br),
                      (0.021 + 0.006 * br, 0.0085 + 0.0035 * male + 0.0045 * br, 0.0078 + 0.0007 * male + 0.0045 * br),
                      _basis((sx, -0.28, -0.12))), 0.012 + 0.003 * fem + 0.008 * br)
        # Cheekbone body and arch (higher and a little rounder on women).
        d = smin(d, E(p, (sx * 0.048, 0.055 + 0.002 * fem, -0.013 + 0.0045 * fem), (0.0165, 0.0155 + 0.001 * fem, 0.0115 + 0.001 * fem),
                      _basis((sx, 0.5, 0.1))), 0.026)
        d = smin(d, capsule(p, (sx * 0.053, 0.046, -0.01 + 0.003 * fem), (sx * 0.059, 0.006, -0.013 + 0.002 * fem), 0.0038), 0.024)
        # The cheeks' fat, under the cheekbone (a woman's lower and rounder, fuller with weight, thinner with age).
        # (a young man's are fuller too, part way to a woman's, so no hollow runs down from the cheekbone)
        yg = shape.young * male
        sf = fem + 0.5 * yg
        ck = (1.0 + 0.45 * shape.full - 0.25 * shape.lean * (1 - 0.6 * yg) + 0.08 * yg) * (1.0 - 0.15 * shape.old)
        d = smin(d, E(p, (sx * (0.041 - 0.0008 * sf), 0.049 + 0.002 * sf, -0.031 - 0.003 * sf - 0.004 * shape.full - 0.006 * shape.old),
                      (0.021 * ck, 0.022 * ck, 0.02 + 0.002 * sf + 0.004 * shape.full)), 0.029)
        # The mastoid behind the ear, then the temple hollow behind the brow.
        d = smin(d, E(p, (sx * 0.05, -0.032, -0.024), (0.014, 0.016, 0.02)), 0.02)
        d = carve(d, E(p, (sx * 0.077, 0.043, 0.012), (0.0085, 0.024, 0.02)), 0.022)
    # Chin (square and broad on men, small and round on women) and glabella.
    (_, _), (cy, cz) = jaw_points(shape)
    sq, pt = max(0.0, jw), max(0.0, -jw)
    d = smin(d, E(p, (0, cy - 0.0005 - 0.001 * fem, cz + 0.0175 - 0.001 * fem + 0.004 * pt),
                  (0.018 - 0.0045 * fem + 0.008 * sq - 0.018 * pt, 0.0115 - 0.0015 * fem + 0.002 * sq, 0.0145 - 0.0025 * fem + 0.003 * sq + 0.008 * pt)),
             0.012 + 0.004 * fem)
    d = smin(d, E(p, (0, 0.0825 + 0.006 * br, 0.0125), (0.012 + 0.016 * br, 0.0068 + 0.0015 * male + 0.004 * br, 0.0095 + 0.002 * br)), 0.01 + 0.008 * br)
    if br > 0:            # a heavy brow's top sloping back into the forehead (no crease above the ridge)
        d = smin(d, E(p, (0, 0.066 + 0.004 * br, 0.028), (0.03 + 0.016 * br, 0.006 + 0.012 * br, 0.012 + 0.008 * br)), 0.02)
    # The soft floor of the mouth between the jaw's halves: under the chin the profile runs back level to the throat
    # while the jaw line rises to its angle above it; with weight it fills into a soft double chin.
    g = max(0.0, shape.full - 0.2)
    d = smin(d, E(p, (0, 0.046 + 0.004 * g, cz + 0.0145 - 0.009 * g), (0.025 - 0.004 * fem + 0.01 * g, 0.036, 0.0095 + 0.012 * g)), 0.014)
    return d


CROW = (-1, 0, 1)                   # the lines fanning out from the outer corner of each eye (crow's feet)


def age_segments(shape):
    """(key, inside, outside) segments whose surface crossings place the lines of age: three across the forehead,
    three fanning back from the outer corner of each eye."""
    out = []
    for k, z in enumerate((0.044, 0.054, 0.064)):
        for x in np.linspace(-0.026 + 0.003 * k, 0.026 - 0.003 * k, 9):
            zz = z + 0.004 * (x / 0.026) ** 2
            out.append((f"forehead{k}", (x, 0.03, zz), (x, 0.13, zz)))
    for sx in (1, -1):
        e = np.asarray(shape.eyes[sx])
        o = np.asarray((sx * 0.75, 0.66, 0.0))
        for k in CROW:
            for t in np.linspace(0.0, 1.0, 4):
                c = e + np.asarray((sx * (0.0165 + 0.0105 * t), -0.004 - 0.011 * t, 0.0008 + k * (0.0022 + 0.0035 * t)))
                out.append((f"crow{sx}{k}", c - o * 0.012, c + o * 0.02))
    return out


def _line(p, d, line, r, dip):
    """A soft line of age: a wide tube along ``line`` (surface points) dipping only its bottom under the skin, deepest
    in the middle."""
    w = np.sin(np.linspace(0.12, 0.88, len(line)) * np.pi)
    rr = np.full(len(line), r)
    return carve(d, tube(p, list(line + np.stack([0 * rr, rr - dip * w, 0 * rr], 1)), list(rr)), 0.002)


def age(shape, p, d):
    """Age: the cheeks' fat (thinner and lower, see masses) sinks into jowls beside the chin; the temples hollow; two
    soft lines cross the forehead."""
    for sx in (1, -1):
        d = smin(d, ellipsoid(p, (sx * 0.036, 0.054, -0.088), (0.011, 0.011, 0.01)), 0.012)
        d = carve(d, ellipsoid(p, (sx * 0.073, 0.03, 0.006), (0.006, 0.02, 0.016)), 0.014)
    a = shape.anchors
    if a is not None:
        for k in (0, 1, 2):
            d = _line(p, d, a[f"forehead{k}"], 0.003, 0.0009)
        for sx in (1, -1):
            for k in CROW:
                line = a[f"crow{sx}{k}"]
                out = np.asarray((sx * 0.75, 0.66, 0.0)) / np.hypot(0.75, 0.66)
                w = np.sin(np.linspace(0.15, 0.85, len(line)) * np.pi)
                rr = np.full(len(line), 0.0018)
                d = carve(d, tube(p, list(line + out * (rr - 0.0007 * w)[:, None]), list(rr)), 0.0015)
    return d
