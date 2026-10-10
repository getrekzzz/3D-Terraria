"""A hood sculpted as a distance field in head-local units (like the head and headwear.py).

The cloth rests on the crown with a little air and stands further off the head lower down; it rises behind into a
soft point and lies smooth down the back of the head, and falls straight past the jaw (never tucking under it) into a
cowl that lies on the shoulders and chest of whatever is under it (the body and the garments there, sampled into a
coarse distance grid), in a few broad folds from the neck down that deepen toward its hem. In front the cloth runs forward round the face into an
opening cut on a plane tilted back from the brow to under the chin, so the face sits a few centimetres inside it in
the hood's shadow; the opening's edge and the hem are rolled, and the lining is painted darker. A creature's big ears
come out through slits cut round them (following the head's own ears), their edges rolled.
"""
import hashlib
import math

import numpy as np

from . import fieldmesh
from .headwear import _cranium, _smooth
from .sdf import F, carve, ellipsoid, round_cone, smax, smin

STEP = 0.007                                    # the drape grid's spacing (head-local)
LO, HI = (-0.25, -0.21, -0.37), (0.25, 0.19, -0.05)
BOUNDS = ((-0.25, -0.21, -0.37), (0.25, 0.2, 0.15))
THICK, ROLL = 0.0036, 0.003                     # the cloth, and the radius of its rolled edges
EAR_ROLL = 0.0022                               # the edge of the slit a creature's ear comes through (barely rolled)
SLIT_GAP, SLIT_STEP = 0.0028, 0.001             # how far the slit stands clear of the ear, and its outline's grid
# the opening's plane through its top and bottom, and its half-width at the cheekbones: a hood, and a deep one
OPENING = {"": ((0.0, 0.122, 0.066), (0.0, 0.09, -0.112), 0.077), "deep": ((0.0, 0.168, 0.05), (0.0, 0.118, -0.118), 0.069)}


def drape(head, under):
    """The signed distance (head-local) to the meshes ``under`` the cowl, softened so the cloth bridges hollows, as a
    field and a digest of its samples (for the mesh cache)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in under:
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [[base + i for i in poly.vertices] for poly in o.data.polygons]
    if not polys:           # (nothing under it: the cowl hangs on the head's own neck)
        return (lambda p: np.ones(len(p), dtype=np.float32)), "bare"
    tree = BVHTree.FromPolygons(verts, polys)
    axes = [np.arange(LO[i], HI[i] + STEP * 0.5, STEP, dtype=np.float32) for i in range(3)]
    grid = np.stack(np.meshgrid(*axes, indexing="ij"), -1)
    c, h = Vector(head.c), head.h
    flat = grid.reshape(-1, 3)
    out = np.empty(len(flat), dtype=np.float32)
    for n, q in enumerate(flat):
        w = c + Vector(q.tolist()) * h
        loc, nor, _, dist = tree.find_nearest(w)
        out[n] = dist / h if (w - loc).dot(nor) >= 0 else -dist / h
    f = out.reshape(grid.shape[:3])
    kernel = np.array((1, 4, 6, 4, 1), dtype=np.float32) / 16
    for axis in range(3):
        f = np.apply_along_axis(lambda r: np.convolve(np.pad(r, 2, mode="edge"), kernel, "valid"), axis, f)
    digest = hashlib.sha1(np.round(f * 4000).astype(np.int32).tobytes()).hexdigest()[:16]
    near = fieldmesh.grid_field(f.astype(np.float32), LO, STEP)
    lo, hi = np.asarray(LO, dtype=np.float32), np.asarray(HI, dtype=np.float32)

    def field(p):           # (positive beyond the grid: above it the head takes over)
        return np.maximum(near(p), np.max(np.maximum(lo - p, p - hi), axis=1))
    return field, digest


def _peak(p, sz, k):
    """The back of the hood: the cloth leaves the back of the crown in a soft point and runs straight down to the
    shoulders (at ``sz``, ``k`` their breadth against a person's), bridging the hollow of the nape instead of
    following it."""
    return round_cone(p * np.asarray((0.72, 1.0, 1.0), dtype=F), (0, -0.05, 0.074), (0, -0.045, sz + 0.085),
                      0.046, 0.085 * k)


def _slit(hood, ear):
    """The slit an ear comes out through, as a field (negative inside): where the ear crosses the cloth's outer
    surface (so the slit hugs the ear where it is seen, however slantwise the ear crosses), grown by SLIT_GAP, its outline found on a grid across the side of the hood, measured again as a distance across
    the cloth and blurred, so the slit's edge is a smooth line and its roll is even all round, however steeply or
    shallowly the ear crosses (the ear's own folds and a blade lying along the cloth would ripple a 3D distance)."""
    sx = 1.0 if ear.centre[0] > 0 else -1.0
    c = ear.centre.astype(F)
    ys = np.arange(c[1] - 0.07, c[1] + 0.0705, SLIT_STEP, dtype=F)
    zs = np.arange(c[2] - 0.07, c[2] + 0.0705, SLIT_STEP, dtype=F)
    yy, zz = (a.ravel() for a in np.meshgrid(ys, zs, indexing="ij"))
    lo, hi = np.full(len(yy), F(0.03)), np.full(len(yy), F(0.22))
    for _ in range(26):                                 # the outer surface's |x| over each grid point
        m = (lo + hi) * F(0.5)
        inside = hood.volume(np.stack([m * F(sx), yy, zz], 1)) < F(THICK)
        lo, hi = np.where(inside, m, lo), np.where(inside, hi, m)
    depth = lo.reshape(len(ys), len(zs))
    room = hood.shape.ear_room(np.stack([lo * F(sx), yy, zz], 1), SLIT_GAP).reshape(depth.shape)
    if room.min() >= 0:                                 # (the ear stays clear of the cloth)
        return None
    pts = []                                            # where the outline crosses the grid's lines
    for a, b, axis in ((room[:-1], room[1:], 0), (room[:, :-1], room[:, 1:], 1)):
        i, j = np.nonzero((a < 0) != (b < 0))
        t = a[i, j] / (a[i, j] - b[i, j])
        pts.append(np.stack([ys[i] + (t * SLIT_STEP if axis == 0 else 0), zs[j] + (t * SLIT_STEP if axis == 1 else 0)], 1))
    pts = np.concatenate(pts).astype(F)
    grid = np.stack([yy, zz], 1)
    near = np.full(len(grid), F(1))
    for k in range(0, len(pts), 256):
        near = np.minimum(near, np.min(np.linalg.norm(grid[:, None] - pts[None, k:k + 256], axis=2), axis=1))
    d = (np.where(room.ravel() < 0, -near, near)).reshape(depth.shape)
    kernel = np.array((1, 0, 4, 0, 6, 0, 4, 0, 1), dtype=F) / F(16)       # (about 3 mm across: a smooth outline)
    for _ in range(2):
        for axis in range(2):
            d = np.apply_along_axis(lambda r: np.convolve(np.pad(r, 4, mode="edge"), kernel, "valid"), axis, d)
    both, y0, z0, top = np.stack([d, depth], -1), ys[0], zs[0], np.asarray(d.shape) - 2

    def field(p):           # (a cubic B-spline over the grid, smooth to shade; off the grid, and 2 cm off the cloth, positive)
        out = np.ones(len(p), dtype=F)
        g = np.stack([p[:, 1] - y0, p[:, 2] - z0], 1) / F(SLIT_STEP)
        m = np.all((g >= 1) & (g < top), axis=1) & (p[:, 0] * F(sx) > 0)
        if m.any():
            i = np.floor(g[m]).astype(np.int64)
            wy, wz = (_bspline((g[m][:, k] - i[:, k]).astype(F)) for k in (0, 1))
            acc = np.zeros((len(i), 2), dtype=F)
            for a in range(4):
                for b in range(4):
                    acc += (wy[a] * wz[b])[:, None] * both[i[:, 0] + a - 1, i[:, 1] + b - 1]
            out[m] = np.maximum(acc[:, 0], np.abs(p[m, 0] * F(sx) - acc[:, 1]) - F(0.02))
        return out
    return field


def _bspline(t):
    """The four uniform cubic B-spline weights at ``t`` (0..1) after the second of four nodes."""
    u = F(1) - t
    return (u * u * u / F(6), (F(3) * t ** 3 - F(6) * t * t + F(4)) / F(6),
            (F(-3) * t ** 3 + F(3) * t * t + F(3) * t + F(1)) / F(6), t ** 3 / F(6))


class Hood:
    def __init__(self, shape, under, shoulder, style=""):
        self.shape, self.under = shape, under
        self.slits = None                              # (the ear slits, found on first use)
        sx, self.sz = shoulder                         # the shoulder joint, across and up (head-local)
        self.k = min(max(sx / 0.182, 0.6), 1.0)        # the shoulders' breadth against a person's (a goblin's: narrow)
        self.turn_x = max(sx - 0.05, 0.125 * self.k ** 0.5)     # where the hem turns up (never into the fall)
        top, bottom, self.half_w = OPENING[style]
        self.deep = style == "deep"
        a, b = np.asarray(top), np.asarray(bottom)
        up = (a - b) / np.linalg.norm(a - b)
        self.n = np.asarray((0.0, up[2], -up[1]), dtype=F)        # the opening's plane faces forward and down
        self.u = up.astype(F)
        self.p0 = b.astype(F)
        self.span = float(np.linalg.norm(a - b))

    def plane(self, p):
        """Coordinates on the opening's plane: across, up it from its bottom, and how far ahead of it."""
        q = p - self.p0
        return p[:, 0], q @ self.u, q @ self.n

    def rim(self, x, v, grow=0.0):
        """The opening's outline on its plane (negative inside): broadest at the cheekbones, narrowing to the brow
        and more to the chin."""
        s = np.clip(v / F(self.span), -0.2, 1.2)
        half_w = F(self.half_w + grow) - F(0.02) * (F(1) - _smooth(F(0.0), F(0.5), s)) - F(0.012) * _smooth(F(0.6), F(1.0), s)
        mid, half_h = F(self.span * 0.5), F(self.span * 0.5 + grow)
        m = (np.abs(x / half_w) ** 2.3 + np.abs((v - mid) / half_h) ** 2.3) ** (1 / 2.3)
        return (m - F(1)) * F(0.06)

    def volume(self, p):
        """What the cloth lies on (negative inside): the head with air round it, more lower down; a tunnel from the
        face out to the opening, so the cloth runs forward round the face; the fall past the jaw to the shoulders;
        then whatever is under the cowl; lifted into folds below the ears (outward only, so they never press into
        what is under them)."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        air = F(0.005) + (F(0.009) + F(0.008) * np.clip(np.abs(x) / F(0.07), 0, 1)) * _smooth(F(0.1), F(-0.03), z)
        vol = _cranium(p, self.shape.fem) - air - F(0.005 if self.deep else 0.0)      # (a deep hood stands off more)
        px, v, ahead = self.plane(p)
        tunnel = smax(np.maximum(self.rim(px, v, THICK), ahead - F(0.012)), -ahead - F(0.13), 0.03)
        vol = smin(vol, tunnel, 0.03)
        vol = smin(vol, _peak(p, self.sz, self.k), 0.03)        # the point, a soft fullness behind the crown
        if self.shape.ear_spec == (1.0, 0.0, 0.0):              # (a person's ears stay under the cloth)
            for ear in self.shape.ears.values():
                q = (p - ear.centre.astype(F)) @ ear.rot.astype(F)
                vol = smin(vol, ellipsoid(q, (0, 0, 0.004), (0.026, 0.036, 0.016)), 0.02)
        wide = p * np.asarray((0.85, 1.0, 1.0), dtype=F)               # an oval fall, wider than deep
        vol = smin(vol, round_cone(wide, (0, -0.015, -0.03), (0, -0.028, self.sz + 0.02), 0.082, 0.118 * self.k), 0.035)
        vol = smin(vol, self.under(p) - F(0.002), 0.03)
        return vol - self.folds(p)

    def folds(self, p):
        """How far the cloth is lifted into folds: none on the head, where it lies smooth over the skull; from the
        neck down, a few broad folds deepening onto the shoulders (shallower down the back, where the cowl lies on
        it), each its own depth; in front U-shaped swags across the chest under the chin."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ang = np.arctan2(x, -y)
        u = ang * F(7.0 / (2 * math.pi)) + F(0.3) * np.sin(ang * F(2.0) + F(0.7)) + F(0.06) * np.sin(z * F(24.0))
        hang = (F(0.5) - F(0.5) * np.cos(F(2 * math.pi) * u)) ** F(0.8)
        hang = hang * (F(0.65) + F(0.35) * np.sin(np.mod(np.floor(u), F(7)) * F(2.7) + F(1.0)))   # (no step in front)
        front = _smooth(F(0.5 * math.pi), F(0.8 * math.pi), np.abs(ang))
        down = _smooth(F(-0.05), F(self.sz - 0.025), z) ** F(1.2) * (F(1) - F(0.3) * np.clip(np.cos(ang), 0, 1) ** 2)
        swag = F(0)
        for zc, lift, w in ((-0.152, 0.04, 0.075), (-0.205, 0.075, 0.115)):      # two U-shaped swags, one under the other
            line = F(zc) + F(lift) * (x / F(w)) ** 2
            swag = swag + np.exp(-((z - line) / F(0.012)) ** 2) * _smooth(F(w + 0.01), F(w - 0.03), np.abs(x))
        flat = F(1) - F(0.7) * np.sin(ang) ** 4 * _smooth(F(self.sz + 0.075), F(self.sz + 0.005), z)   # (on the shoulders)
        return F(0.016) * hang * down * flat * (F(1) - F(0.8) * front) + F(0.008) * swag * front + self.drag(p)

    def drag(self, p):
        """Two soft folds the hood hangs in behind, running down and out from under its point toward the shoulder
        blades (one a little longer than the other), fading in from nothing at each end."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        out = np.zeros(len(p), dtype=F)
        for s, z0, x1, z1, amp in ((1.0, 0.004, 0.08, 0.035, 0.0048), (-1.0, -0.012, 0.07, 0.05, 0.0042)):
            a = np.asarray((s * 0.026, z0), dtype=F)
            b = np.asarray((s * x1 * self.k, self.sz + z1), dtype=F)
            q = np.stack([x, z], 1) - a
            ab = b - a
            t = np.clip(q @ ab / F(ab @ ab), 0, 1)
            d = np.linalg.norm(q - t[:, None] * ab, axis=1)
            width = F(0.007) + F(0.009) * t
            out = out + F(amp) * np.exp(-(d / width) ** 2) * np.clip(np.sin(F(math.pi) * t), 0, 1) ** F(0.8)
        return out * _smooth(F(-0.03), F(-0.07), y)

    def opening(self, p):
        """The face opening (negative where cloth is cut away): ahead of the plane, within its outline."""
        px, v, ahead = self.plane(p)
        return smax(-ahead, self.rim(px, v, 0.012), 0.004)

    def hem_z(self, p):
        """The cowl's hem: over the shoulder tops at the sides, lower in front and behind, gently waved, and turning
        up before the shoulder joints, so the cowl ends on the shoulders and never lies on an arm that moves out
        from under it."""
        ang = np.arctan2(p[:, 0], -p[:, 1])
        side = np.abs(np.sin(ang))
        o = np.abs(p[:, 0]) - F(self.turn_x)
        over = F(0.5) * (o + np.sqrt(o * o + F(0.018) ** 2))           # (a rounded turn, no corner at the shoulder)
        return F(self.sz - 0.075) + F(0.08) * side ** 3 + F(0.006) * np.sin(ang * F(7.0) + F(0.5)) + F(1.6) * over

    def below(self, p):
        """How far ``p`` is below the hem, roughly a distance (the hem's slope up past the shoulders divided out)."""
        o = np.abs(p[:, 0]) - F(self.turn_x)
        slope = F(0.8) * (F(1) + o / np.sqrt(o * o + F(0.018) ** 2))
        return (self.hem_z(p) - p[:, 2]) / np.sqrt(F(1) + slope * slope)

    def parts(self, p):
        vol = self.volume(p)
        mid = vol - F(THICK / 2)
        cloth = smin(carve(vol - F(THICK), vol, 0.0012), self.throat(p, vol), 0.003)
        cut = self.opening(p)
        below = self.below(p)
        cloth = carve(carve(cloth, cut, 0.0012), -below, 0.0012)
        lip = np.sqrt((cut - F(ROLL)) ** 2 + mid * mid) - F(ROLL)
        hem = np.sqrt((below + F(ROLL)) ** 2 + mid * mid) - F(ROLL)
        hem = np.maximum(hem, -cut)
        if self.shape.ear_spec != (1.0, 0.0, 0.0):      # a slit round each big ear, its edge rolled
            if self.slits is None:
                self.slits = [s for s in (_slit(self, ear) for ear in self.shape.ears.values()) if s is not None]
            for slit in self.slits:
                hole = slit(p)
                cloth = carve(cloth, hole, 0.002)
                lip = np.minimum(lip, np.sqrt((hole - F(0.0002)) ** 2 + mid * mid) - F(EAR_ROLL))
        return {"crown": cloth, "lip": lip, "hem": hem}

    def throat(self, p, vol):
        """The cloth gathered in front of the throat a little under the chin (a slab filling the hood there, its top
        rising toward the front), so a look down into the opening past the chin meets the lining's shadow, not the
        collar below, even with the head bowed (higher beside the jaw)."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        top = F(-0.146) + F(0.25) * (y - F(0.03)) + F(0.024) * _smooth(F(0.025), F(0.06), np.abs(x))
        return np.maximum(np.maximum(vol, z - top), np.maximum(top - F(0.042) - z, F(-0.012) - y))

    def field(self, p):
        parts = self.parts(p)
        return smin(smin(parts["crown"], parts["lip"], 0.002), parts["hem"], 0.002)

    def turn(self, p):
        """Zero along the outermost line of the rolled edges (the cloth's mid-surface): the lining's colour ends
        there, so it shows on the inner half of each roll."""
        return self.volume(p) - F(THICK / 2)

    def regions(self, co):
        return np.where(self.turn(co) < 0, "under", "crown").astype(object)


def mesh(head, under, style=None):
    """The hood's mesh object (world coordinates) and each face's region (the lining is "under"); ``style`` "deep"
    (hatShape) runs the cloth further forward into an opening that overhangs the face and hides it in shadow."""
    style = style if style in OPENING else ""
    from .shellmesh import centres, place, refine
    shape = head.shape
    joint = head.local(head.frame.j["shoulder.R"])
    shoulder = (round(abs(float(joint.x)), 4), round(float(joint.z), 4))
    field, digest = drape(head, under)
    hood = Hood(shape, field, shoulder, style)
    sources = ("headshape.py", "earshape.py", "creature.py", "headwear.py", "hood.py")
    obj = fieldmesh.build(hood.field, BOUNDS, 0.0015, 26000, "hat_hood", ("hood", shape.key(), shoulder, digest, style),
                          sources, "headwear")
    refine(obj, hood.field, budget=1.2, cuts=(hood.turn,), fine=0.0009)
    co = place(obj, head)
    return obj, hood.regions(centres(obj, co))
