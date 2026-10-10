"""Creature heads sculpted into the head's distance field (head-local units, see headshape), in place of a person's
nose and mouth (``muzzle``):

- wolf: a long muzzle from a steep stop between the eyes to a broad black nose pad with two curled nostrils, the
  upper jaw's lips hanging over a shorter lower jaw along a wide mouth that runs back under the cheeks, puffed
  cheeks, and tall pointed ears standing up on top of the skull, cupped forward (in place of the ears at the sides);
- ape: a jutting muzzle under a flat, broad nose with flared nostrils, a wide thin-lipped mouth across it and small
  eyes set deep under the brow (the brow ridge and jaw come from ``brow`` and ``jaw``).

Muzzle wraps the head's own shape: everything else (skull, brow, eyes, neck, other parts' use of the field) is the
person's head underneath.
"""
import hashlib
import pathlib

import numpy as np

from . import sdf
from .sdf import F, carve, ellipsoid, round_cone, smin, sphere, tube

SOURCE = hashlib.sha1(pathlib.Path(__file__).read_bytes()).hexdigest()[:12]

# wolf: muzzle stations from the stop back by the eyes to the nose (y, centre z, half-width, half-height)
UPPER = [(0.07, -0.032, 0.041, 0.04), (0.1, -0.035, 0.034, 0.033), (0.13, -0.037, 0.03, 0.026),
         (0.16, -0.038, 0.025, 0.021), (0.185, -0.039, 0.022, 0.018), (0.198, -0.04, 0.021, 0.0165)]
LOWER = [(0.035, -0.088, 0.04, 0.024), (0.06, -0.084, 0.035, 0.02), (0.09, -0.079, 0.029, 0.016), (0.125, -0.074, 0.022, 0.0125),
         (0.155, -0.07, 0.017, 0.0105), (0.18, -0.067, 0.0135, 0.009)]
NOSE = ((0.0, 0.201, -0.029), (0.017, 0.0125, 0.0115))
EAR = dict(root=(0.047, -0.016, 0.072), height=0.118, width=0.088, thick=0.02)      # thick: half, at the root


def ear_frame(sx):
    """(root, up, face, across) of a wolf's ear on side ``sx``: where it stands on the skull, the way it points,
    the way its hollow faces and across it (head-local)."""
    r = EAR["root"]
    up = np.asarray((sx * 0.28, -0.1, 1.0))
    up /= np.linalg.norm(up)
    face = np.asarray((sx * 0.3, 1.0, 0.0))
    face -= up * face.dot(up)
    face /= np.linalg.norm(face)
    return np.asarray((sx * r[0], r[1], r[2])), up, face, np.cross(up, face)


def _resample(pts, n):
    """``n`` points evenly spaced along a polyline."""
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    acc = np.concatenate([[0.0], np.cumsum(seg)])
    t = np.linspace(0.0, acc[-1], n)
    return np.stack([np.interp(t, acc, pts[:, i]) for i in range(3)], 1).astype(F)


def _chain(p, stations, k=0.02, below=0.0):
    d = None
    for i, (y, z, w, h) in enumerate(stations):
        ny = stations[i + 1][0] if i + 1 < len(stations) else y + h
        e = ellipsoid(p, (0.0, y, z - below * h), (w, max(ny - y, 0.012) * 1.05, h))
        d = e if d is None else smin(d, e, k)
    return d


def mouth_line(kind):
    """Points along the mouth (head-local), from one corner round the front to the other."""
    if kind == "wolf":
        ys = np.linspace(0.06, 0.19, 9)
        side = [(0.034 - 0.017 * (y - 0.06) / 0.13, y, -0.066 + 0.006 * ((y - 0.06) / 0.13)) for y in ys]
        right = [(x, y, z) for x, y, z in side]
        front = [(0.0, 0.198, -0.0585)]
        left = [(-x, y, z) for x, y, z in side[::-1]]
        return np.asarray(right[::-1] + front + left, dtype=F)
    xs = np.linspace(-0.04, 0.04, 13)
    return np.asarray([(x, 0.118 - 22.0 * x * x, -0.074 + 3.0 * x * x) for x in xs], dtype=F)


class Muzzle:
    """A HeadShape (``base``) with a creature's muzzle; everything not overridden is the base's."""

    def __init__(self, base, kind):
        self.base, self.kind = base, kind
        if kind == "ape":            # small eyes under the brow
            base.lid_up *= 0.72
            base.lid_down *= 0.75
        self._line = None

    def __getattr__(self, name):
        return getattr(self.base, name)

    def key(self):
        return (self.base.key(), self.kind, SOURCE)

    def bounds(self):
        lo, hi = self.base.bounds()
        lo, hi = list(lo), list(hi)
        if self.kind == "wolf":
            hi[1] = max(hi[1], 0.24)
            hi[2] = max(hi[2], EAR["root"][2] + EAR["height"] + 0.02)
            lo[0], hi[0] = min(lo[0], -0.1), max(hi[0], 0.1)
        else:
            hi[1] = max(hi[1], 0.15)
        return tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi)

    def lips(self, p):
        return np.ones(len(p), dtype=F)

    # ------------------------------------------------------------------ the field
    def field(self, p, ears=True, face=True):
        wolf = self.kind == "wolf"
        b = self.base
        d = b.field(p, ears=ears and not wolf, face=False)
        d = self._mass(p, d)
        if wolf and ears:
            for sx in (1, -1):
                d = sdf.region(p, d, self._ear_centre(sx), 0.105, lambda q, dd, sx=sx: smin(dd, self._ear(q, sx), 0.016))
        if face:
            for sx in (1, -1):
                d = sdf.region(p, d, b.eyes[sx], 0.047, lambda q, dd, sx=sx: b._eye(q, dd, sx))
            d = sdf.region(p, d, (0.0, 0.14 if wolf else 0.09, -0.05), 0.12 if wolf else 0.075, self._features)
        return d

    def _mass(self, p, d):
        if self.kind == "wolf":
            upper = _chain(p, UPPER)
            upper = smin(upper, round_cone(p, (0, 0.082, 0.004), (0, 0.19, -0.022), 0.012, 0.0085), 0.016)   # bridge
            upper = smin(upper, ellipsoid(p, *NOSE), 0.008)
            lower = _chain(p, LOWER, 0.016)
            m = smin(upper, lower, 0.004)
            for sx in (1, -1):        # cheeks puffed out behind the muzzle, and the flews over the lower jaw
                m = smin(m, ellipsoid(p, (sx * 0.04, 0.045, -0.046), (0.017, 0.024, 0.021)), 0.025)
                m = smin(m, round_cone(p, (sx * 0.03, 0.075, -0.06), (sx * 0.017, 0.17, -0.056), 0.012, 0.007), 0.01)
            return smin(d, m, 0.02)
        m = ellipsoid(p, (0.0, 0.074, -0.068), (0.05, 0.043, 0.04))            # the jutting lower face
        m = smin(m, ellipsoid(p, (0.0, 0.088, -0.083), (0.042, 0.032, 0.026)), 0.012)
        m = smin(m, ellipsoid(p, (0.0, 0.096, -0.033), (0.028, 0.011, 0.016)), 0.012)   # the flat nose
        for sx in (1, -1):
            m = smin(m, sphere(p, (sx * 0.019, 0.097, -0.041), 0.0115), 0.008)          # nostril wings
        return smin(d, m, 0.02)

    def _ear_centre(self, sx):
        r = EAR["root"]
        return (sx * (r[0] + 0.01), r[1], r[2] + EAR["height"] * 0.3)

    def _ear(self, p, sx):
        """A tall pointed ear standing up from the skull: a cone flattened front to back, its back rounded, as thick
        as a finger at the root and thinning toward the tip, leaning out a little, its front dished in a shallow
        hollow."""
        root, up, face, across = ear_frame(sx)
        H, W, T = EAR["height"], EAR["width"], EAR["thick"]
        q = p - root.astype(F)
        u, v, w = q @ up.astype(F), q @ across.astype(F), q @ face.astype(F)
        k = W / (2 * T)                          # how much flatter than round
        flat = np.stack([v, u, w * F(k)], 1)
        d = round_cone(flat, (0.0, -0.012, 0.0), (0.0, H, 0.0), W / 2, 0.01) / F(k ** 0.5)   # (a rounded tip)
        cup = ellipsoid(np.stack([v, u, w], 1), (0.0, H * 0.34, T * 1.2), (W * 0.24, H * 0.3, T * 0.85))
        return carve(d, cup, 0.005)

    def _surface(self, a, b, iters=24):
        """Where segments a -> b (inside to outside) cross the muzzle's uncut surface."""
        f = lambda q: self._mass(q, self.base.field(q, ears=False, face=False))
        a, b = np.asarray(a, dtype=F), np.asarray(b, dtype=F)
        lo, hi = np.zeros(len(a), dtype=F), np.ones(len(a), dtype=F)
        for _ in range(iters):
            mid = (lo + hi) * F(0.5)
            inside = f(a + (b - a) * mid[:, None]) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        return a + (b - a) * lo[:, None]

    def line(self):
        """The mouth line laid on the surface."""
        if self._line is None:
            pts = mouth_line(self.kind)
            axis = np.stack([pts[:, 0] * 0.2, pts[:, 1] - 0.03, pts[:, 2]], 1)
            self._line = _resample(self._surface(axis, pts + (pts - axis) * 2.0), 90)
        return self._line

    def _features(self, p, d):
        line = self.line()[::5]
        n = len(line)
        wolf = self.kind == "wolf"
        r = (0.0016 if wolf else 0.0019) * (0.5 + 0.5 * np.sin(np.pi * (np.arange(n) + 0.5) / n))
        d = carve(d, tube(p, list(line), list(r)), 0.0018)
        if wolf:
            for sx in (1, -1):          # nostrils curling out to the sides of the pad
                d = carve(d, ellipsoid(p, (sx * 0.0085, 0.2155, -0.0315), (0.0045, 0.004, 0.0028)), 0.0016)
                d = carve(d, round_cone(p, (sx * 0.01, 0.215, -0.033), (sx * 0.0175, 0.206, -0.036), 0.0018, 0.001), 0.0012)
            d = carve(d, round_cone(p, (0, 0.2185, -0.031), (0, 0.205, -0.052), 0.0011, 0.0009), 0.0012)   # the philtrum
        else:
            for sx in (1, -1):
                d = carve(d, ellipsoid(p, (sx * 0.012, 0.106, -0.044), (0.0068, 0.005, 0.0042)), 0.0016)
        return d

    # ------------------------------------------------------------------ painting helpers
    def regions(self, q):
        """Per head-local point: how much it is nose pad, lip and muzzle underside (each 0..1)."""
        q = np.asarray(q, dtype=F).reshape(-1, 3)
        line = self.line()
        dl = np.min(np.linalg.norm(q[:, None, :] - line[None, :, :], axis=2), axis=1)
        w = 0.002 if self.kind == "wolf" else 0.0032
        lip = np.clip(1.0 - (dl - w) / 0.003, 0.0, 1.0)
        if self.kind == "wolf":
            c, r = NOSE
            nd = np.linalg.norm((q - np.asarray(c, dtype=F)) / np.asarray(r, dtype=F) * F(0.92), axis=1)
            nose = np.clip((1.12 - nd) / 0.12, 0.0, 1.0) * (q[:, 1] > 0.19)
            under = np.clip((-0.058 - q[:, 2]) / 0.012, 0.0, 1.0) * np.clip((q[:, 1] - 0.03) / 0.04, 0.0, 1.0)
        else:
            nd = np.linalg.norm((q - np.asarray((0.0, 0.1, -0.038), dtype=F)) / np.asarray((0.03, 0.02, 0.02), dtype=F), axis=1)
            nose = np.clip((1.0 - nd) / 0.3, 0.0, 1.0)
            under = np.zeros(len(q))
        return nose, lip, under


def fangs(head):
    """A wolf's upper canines hanging from under its lips past the lower jaw (ivory, carried by the head)."""
    import bmesh
    from .garments import colorize, mesh_from, set_slot
    from .paws import _hook
    from .rig import rigid
    bm = bmesh.new()
    for sx in (1, -1):
        pts = [head.at((sx * 0.0165, 0.176, -0.05)), head.at((sx * 0.0172, 0.178, -0.062)), head.at((sx * 0.0176, 0.177, -0.072)),
               head.at((sx * 0.0172, 0.173, -0.08)), head.at((sx * 0.0165, 0.169, -0.0855))]
        h = head.h
        _hook(bm, pts, [0.0046 * h, 0.0043 * h, 0.0033 * h, 0.0018 * h, 0.0], flat=0.8)
        pts = [head.at((sx * 0.009, 0.183, -0.054)), head.at((sx * 0.0095, 0.185, -0.064)), head.at((sx * 0.0095, 0.184, -0.0695))]
        _hook(bm, pts, [0.0022 * h, 0.0019 * h, 0.0], flat=0.7)        # incisors beside them
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.smooth = True
    colorize(bm, "#e8dcc0")
    obj = mesh_from(bm, "fangs")
    set_slot(obj, "char:bone")
    rigid(obj, "head")
    return obj
