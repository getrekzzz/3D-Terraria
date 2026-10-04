"""Wide hats sculpted as distance fields in head-local units (like headwear): a crown fitted over the skull and the hair
left below its band, a brim with thickness, curl and a bound edge, and a band round the crown. ``hatShape``:

- brim: a soft wide-brimmed hat, its crown creased down the middle and pinched at the front, the brim's sides
  curled up and its front snapped down (the default);
- floppy: a battered slouch hat, the crown dented and leaning, the wide brim sagging in waves;
- top: a top hat, its tall crown waisted and flaring a little, the narrow brim rolled up at the sides;
- pointed: a wizard's hat, a tall cone that bends back and wrinkles where it bends, over a wide wavy brim;
- tricorn: the brim cocked up into three walls round a low crown, tallest mid-side and pinched into points at
  the front and the back corners, their top edges bound with braid (no band);
- bucket: a fisherman's hat, a rounded crown over a short brim that slopes down, stitched in rings;
- mushroom: a mushroom's domed cap, its rim rolled under (the hat of a mushroom person);
- shako: a soldier's (or nutcracker's) tall flaring cap with a peak, gold bands, a badge and a plume, worn as a cap.
"""
import math

import numpy as np

from . import fieldmesh
from .shellmesh import centres, place, refine
from .headwear import _cranium, _smooth
from .sdf import F, carve, ellipsoid, smin, tube

SHAPES = ("brim", "floppy", "top", "pointed", "tricorn", "bucket", "mushroom", "shako")
# band height at the crown's centre (head-local z) and pitch (front up) per shape
SEAT = {"brim": (0.044, 7), "floppy": (0.04, 4), "top": (0.046, 5), "pointed": (0.038, 6), "tricorn": (0.032, 8),
        "bucket": (0.036, 5), "mushroom": (0.056, 5), "shako": (0.04, 3)}
TRI = {"rb": 0.011, "t": 0.0046, "lean": math.radians(14), "bow": 0.007}     # the tricorn's fold, sheet and walls


def _bend():
    """The tricorn's bend at a fold: its radius and angle, where it ends, and the wall's direction after it."""
    rb, lean = TRI["rb"], TRI["lean"]
    end = math.pi / 2 - lean
    return rb, end, rb * math.sin(end), rb * (1 - math.cos(end)), math.sin(lean), math.cos(lean)


def _wall(tg):
    """The height of a tricorn wall past its bend, tallest mid-side (tg 0) and low at the corners (tg 1)."""
    return F(0.018) + F(0.066) * np.clip(1 - tg * tg, 0, 1) ** 0.8


def _rounded(a, b, k):
    """The intersection of two solids with the edge where they meet rounded to radius k."""
    ax, bx = a + F(k), b + F(k)
    out = np.sqrt(np.maximum(ax, 0) ** 2 + np.maximum(bx, 0) ** 2) + np.minimum(np.maximum(ax, bx), 0)
    return out - F(k)


class Hat:
    """One hat's field and the regions it is coloured by, in head-local units."""

    def __init__(self, shape, fem, clear):
        self.shape, self.fem, self.clear = shape, fem, clear
        zb, pitch = SEAT[shape]
        a = math.radians(pitch)
        self.origin = np.asarray((0.0, -0.01, zb), dtype=F)
        # hat axes (columns) in head-local: x across, y forward, z up, tipped back so the front of the brim rises
        self.rot = np.asarray(((1, 0, 0), (0, math.cos(a), -math.sin(a)), (0, math.sin(a), math.cos(a))), dtype=F)
        self._fit()

    def local(self, p):
        return (p - self.origin) @ self.rot

    def inner(self, p):
        """The hat's inside: off the skull by the hair under the band, close over the bare forehead."""
        front = _smooth(F(0.04), F(0.075), p[:, 1])
        return _cranium(p, self.fem) - (F(self.clear) * (1 - front) + F(0.0025) * front)

    def _fit(self):
        """The crown's base: an oval round the skull and the hair under the band, with room for the cloth."""
        wall = 0.0045
        ang = np.linspace(0, 2 * np.pi, 64, endpoint=False)
        dirs = np.stack([np.cos(ang), np.sin(ang), np.full_like(ang, 0.004)], 1).astype(F)
        lo, hi = np.zeros(len(ang), F), np.full(len(ang), 0.2, F)
        for _ in range(24):
            mid = (lo + hi) / 2
            pts = self.origin + (dirs * mid[:, None]) @ self.rot.T
            inside = self.inner(pts) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        r = lo + wall
        x, y = r * np.cos(ang), r * np.sin(ang)
        self.rx = float((x.max() - x.min()) / 2)
        self.ry = float((y.max() - y.min()) / 2)
        self.yc = float((y.max() + y.min()) / 2)
        # the top of the head (with the hair's room) under the crown, so no crown is cut lower than it
        g = np.stack(np.meshgrid(np.linspace(-0.6, 0.6, 7), np.linspace(-0.6, 0.6, 7)), -1).reshape(-1, 2)
        lo, hi = np.zeros(len(g), F), np.full(len(g), 0.2, F)
        base = np.stack([g[:, 0] * self.rx, self.yc + g[:, 1] * self.ry, np.zeros(len(g))], 1).astype(F)
        for _ in range(22):
            mid = (lo + hi) / 2
            pts = self.origin + (base + np.stack([0 * mid, 0 * mid, mid], 1)) @ self.rot.T
            inside = self.inner(pts) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        self.top = float(lo.max())

    def height(self, h, extra):
        """A crown height of ``h``, but at least ``extra`` over the top of the head."""
        return max(h, self.top + extra)

    def liner(self, p, q, c, top=0.045):
        """The crown ``c`` let out wherever the head under the band would come closer than the cloth's thickness
        (the oval is only a fit), so the band always covers it."""
        lining = _rounded(self.inner(p) - F(0.0032), q[:, 2] - F(top), 0.012)
        return np.minimum(c, np.maximum(lining, -(q[:, 2] + F(0.002))))

    # ------------------------------------------------------------------ parts
    def polar(self, q):
        x, y = q[:, 0], q[:, 1] - F(self.yc)
        th = np.arctan2(y, x)
        r = np.hypot(x, y)
        r0 = F(1) / np.sqrt((np.cos(th) / F(self.rx)) ** 2 + (np.sin(th) / F(self.ry)) ** 2)
        return x, y, th, r, r0

    def crown(self, q, height, taper=0.0, rr=0.012, n=2.4, crease=0.0, pinch=0.0, waist=0.0, lean=0.0):
        """A soft crown: a superelliptic wall growing ``taper`` narrower toward a domed top, its edge rounded."""
        z = q[:, 2]
        t = np.clip(z / F(height), 0, 1.2)
        x = q[:, 0] - F(lean) * z
        y = q[:, 1] - F(self.yc)
        k = F(1) + F(taper) * t - F(waist) * np.sin(F(np.pi) * np.clip(t, 0, 1))
        ax, ay = F(self.rx) * k, F(self.ry) * k
        m = (np.abs(x / ax) ** n + np.abs(y / ay) ** n) ** (1.0 / n)
        side = (m - F(1)) * np.minimum(ax, ay)
        top = height - crease * np.exp(-(x / F(0.024)) ** 2) * _smooth(F(-self.ry), F(-0.2 * self.ry), y)
        d = _rounded(side, z - F(top), rr)
        if pinch:          # two dents in the front of the crown, deepest toward the top
            for sx in (1, -1):
                g = np.exp(-(((x - F(sx * 0.55 * self.rx)) / F(0.024)) ** 2 + ((y - F(0.72 * self.ry)) / F(0.03)) ** 2))
                d = d + F(pinch) * g * _smooth(F(0.25 * height), F(0.9 * height), z)
        return np.maximum(d, -(z + F(0.002)))

    def brim(self, q, width, lift, t=0.0045, edge=0.0016):
        """A brim: a sheet of thickness ``t`` round the crown, its middle at height ``lift(u, th)`` (u = 0 at the
        crown .. 1 at the edge) and its outer edge bound with a rounded welt. Returns (distance, side) where side < 0
        on the underside."""
        _, _, th, r, r0 = self.polar(q)
        w = width(th) if callable(width) else F(width)
        u = (r - r0) / w
        uc = np.maximum(u, F(0))
        f = lift(uc, th)
        df = (lift(uc + F(0.02), th) - f) / (F(0.02) * w)
        a = (q[:, 2] - f) / np.sqrt(F(1) + df * df)
        b = r - r0 - w
        half = F(t / 2) + F(edge) * _smooth(F(0.9), F(1.0), u)
        d = np.where(b < 0, np.abs(a), np.sqrt(a * a + b * b)) - half
        return d, a

    def band(self, q, crown, height, proud=0.0024):
        z = q[:, 2]
        return _rounded(crown - F(proud), np.maximum(z - F(height), -z - F(0.0015)), 0.0012)

    # ------------------------------------------------------------------ shapes
    def parts(self, p):
        """{region: distance} for the hat's parts (the field is their union, carved round the head)."""
        q = self.local(p)
        s = self.shape
        if s == "mushroom":
            return self._mushroom(q)
        if s == "tricorn":
            return self._tricorn(p, q)
        out = {}
        if s == "brim":
            c = self.crown(q, self.height(0.1, 0.03), taper=-0.08, rr=0.016, crease=0.013, pinch=0.007)
            out["brim"] = self.brim(q, 0.068, lambda u, th: F(0.034) * u * u * np.cos(th) ** 2
                                    - F(0.012) * u * np.clip(np.sin(th), 0, 1) ** 2)
            band_h = 0.019
        elif s == "floppy":
            c = self.crown(q, self.height(0.088, 0.028), taper=-0.14, rr=0.03, lean=0.08)
            x, y, z = q[:, 0], q[:, 1] - F(self.yc), q[:, 2]
            c = c + F(0.011) * np.exp(-(((x + F(0.03)) / F(0.035)) ** 2 + ((y - F(0.03)) / F(0.04)) ** 2)) * _smooth(F(0.05), F(0.09), z)
            c = c + F(0.0025) * np.sin(x * F(61) + F(1.3)) * np.sin(y * F(47)) * _smooth(F(0.02), F(0.08), z)
            out["brim"] = self.brim(q, lambda th: F(0.094) * (1 + F(0.05) * np.sin(F(9) * th) + F(0.03) * np.sin(F(17) * th + F(2))),
                                    lambda u, th: -F(0.032) * np.clip(u, 0, None) ** 1.6 * (F(0.62) + F(0.38) * np.sin(F(2) * th + F(0.6)))
                                    + F(0.012) * u * u * np.sin(F(5) * th + F(1)))
            band_h = 0.016
        elif s == "top":
            c = self.crown(q, 0.15, taper=0.07, rr=0.007, n=2.3, waist=0.05)
            out["brim"] = self.brim(q, lambda th: F(0.042) + F(0.008) * np.cos(th) ** 2,
                                    lambda u, th: F(0.03) * u * u * np.cos(th) ** 2 - F(0.006) * u * np.sin(th) ** 2)
            band_h = 0.024
        elif s == "pointed":
            c = self._cone(q)
            out["brim"] = self.brim(q, 0.1, lambda u, th: -F(0.022) * np.clip(u, 0, None) ** 1.5
                                    + F(0.012) * u * u * np.sin(F(3) * th + F(0.7)), t=0.0042)
            band_h = 0.02
        elif s == "shako":          # a tall flaring cylinder with a peak, a badge and a plume (no brim)
            H = 0.13
            c = self.liner(p, q, self.crown(q, H, taper=0.08, rr=0.007, n=2.4))
            x, y, z = q[:, 0], q[:, 1] - F(self.yc), q[:, 2]
            pitch = math.radians(-24)
            rot = np.asarray(((1, 0, 0), (0, math.cos(pitch), math.sin(pitch)), (0, -math.sin(pitch), math.cos(pitch))))
            peak = ellipsoid(q, (0, self.yc + 0.93 * self.ry, 0.0), (0.07, 0.062, 0.0036), rot)
            out["peak"] = np.maximum(peak, F(0.82 * self.ry) - y)
            rim = _rounded(c - F(0.0022), np.maximum(z - F(H - 0.001), F(H - 0.013) - z), 0.0012)
            out["band"] = np.minimum(self.band(q, c, 0.014), rim)
            out["badge"] = ellipsoid(q, (0, self.yc + 1.035 * self.ry + 0.002, 0.066), (0.022, 0.006, 0.028))
            plume = ellipsoid(q, (0, self.yc + 0.55 * self.ry, H + 0.035), (0.017, 0.017, 0.04))
            out["plume"] = plume + F(0.002) * np.sin(np.arctan2(y - F(0.55 * self.ry), x) * F(9))
            out["crown"] = c
            return out
        else:                       # bucket
            c = self.crown(q, self.height(0.078, 0.022), taper=-0.12, rr=0.03, n=2.1)
            out["brim"] = self.brim(q, 0.046, lambda u, th: -F(0.42 * 0.046) * u - F(0.004) * u * u, t=0.004, edge=0.0008)
            _, _, _, r, r0 = self.polar(q)
            stitch = sum(np.exp(-((r - r0 - F(k)) / F(0.0012)) ** 2) for k in (0.012, 0.024, 0.036))
            out["brim"] = (out["brim"][0] + F(0.0007) * stitch, out["brim"][1])
            band_h = 0.014
        c = self.liner(p, q, c, 0.066 if s == "pointed" else 0.045)
        out["crown"] = c
        out["band"] = self.band(q, c, band_h)
        return out

    def _cone(self, q):
        """The wizard's cone: a tube along a spine that rises, bends back and droops, wrinkled where it bends."""
        yc = self.yc
        spine = [(0, yc, -0.01), (0, yc - 0.004, 0.06), (0.002, yc - 0.012, 0.13), (0.008, yc - 0.03, 0.19),
                 (0.016, yc - 0.06, 0.235), (0.026, yc - 0.1, 0.258), (0.034, yc - 0.14, 0.256), (0.038, yc - 0.165, 0.244)]
        radii = [1.0, 0.86, 0.66, 0.46, 0.3, 0.18, 0.085, 0.035]
        r = self.rx
        z = q[:, 2]
        k = F(self.ry / self.rx) + (F(1) - F(self.ry / self.rx)) * _smooth(F(0.0), F(0.1), z)
        qq = np.stack([q[:, 0], F(yc) + (q[:, 1] - F(yc)) / k, z], 1)
        d = tube(qq, spine, [r * t for t in radii])
        ang = np.arctan2(q[:, 0], q[:, 1] - F(yc))
        wr = F(0.0024) * np.sin(z * F(95) + F(1.7) * np.sin(ang * F(2))) * _smooth(F(0.12), F(0.17), z) * (1 - _smooth(F(0.24), F(0.26), z))
        return np.maximum(d + wr * (F(0.6) + F(0.4) * np.cos(ang)), -(z + F(0.002)))

    def _sides(self):
        """The tricorn's fold lines: (normal, tangent, distance from the crown's centre, corner positions along it)."""
        if not hasattr(self, "_folds"):
            normal = lambda a: np.asarray((math.cos(math.radians(a)), math.sin(math.radians(a))))
            folds = {a: math.hypot(self.rx * math.cos(math.radians(a)), self.ry * math.sin(math.radians(a))) + 0.004
                     for a in (30, 150, 270)}
            corners = [(a0, a1, np.linalg.solve(np.stack([normal(a0), normal(a1)]), np.asarray((folds[a0], folds[a1]))))
                       for a0, a1 in ((30, 150), (150, 270), (270, 30))]
            self._folds = []
            for ang, fold in folds.items():
                n = normal(ang)
                tv = np.asarray((-n[1], n[0]))
                ends = [float(cn @ tv) for a0, a1, cn in corners if ang in (a0, a1)]
                self._folds.append((n, tv, fold, min(ends), max(ends)))
        return self._folds

    def _fold_u(self, x, y, side):
        """How far past a fold line (bowed out mid-side) a point lies, and how far along it toward a corner (0..1)."""
        n, tv, fold, lo, hi = side
        tpos = x * F(tv[0]) + y * F(tv[1])
        tg = np.where(tpos > 0, tpos / F(hi), tpos / F(lo))
        return x * F(n[0]) + y * F(n[1]) - F(fold) - F(TRI["bow"]) * np.clip(1 - tg * tg, 0, 1), tg

    def _tricorn(self, p, q):
        """A low crown and its brim cocked up along three fold lines into walls that lean out a little, tallest
        mid-side where they are pinned to the crown and falling to pinched points at the front and back corners."""
        c = self.liner(p, q, self.crown(q, self.height(0.068, 0.02), taper=-0.1, rr=0.028))
        x, y = q[:, 0], q[:, 1] - F(self.yc)
        z = q[:, 2]
        rb, end, ex, ez, dx, dz = _bend()
        best_u = np.full(len(q), -1e9, F)
        dist = np.full(len(q), 1e9, F)
        for side in self._sides():
            u, tg = self._fold_u(x, y, side)
            # distance to the profile (flat, a bend of radius rb, then the wall) and the arc length along it
            d1, s1 = np.hypot(u - np.minimum(u, 0), z), np.minimum(u, 0)
            phi = np.clip(np.arctan2(u, -(z - F(rb))), 0, F(end))
            d2 = np.hypot(u - F(rb) * np.sin(phi), z - F(rb) + F(rb) * np.cos(phi))
            s2 = F(rb) * phi
            l = np.maximum((u - F(ex)) * F(dx) + (z - F(ez)) * F(dz), 0)
            d3 = np.hypot(u - F(ex) - F(dx) * l, z - F(ez) - F(dz) * l)
            s3 = F(rb * end) + l
            dd = np.minimum(np.minimum(d1, d2), d3)
            ss = np.where(dd == d1, s1, np.where(dd == d2, s2, s3))
            over = np.maximum(ss - F(rb * end) - _wall(tg), 0)
            dk = np.sqrt(dd * dd + over * over) - F(TRI["t"] / 2)
            mine = u > best_u
            best_u = np.where(mine, u, best_u)
            dist = np.where(mine, dk, dist)
        return {"crown": c, "brim": (dist, np.zeros_like(dist))}

    def trim_paths(self):
        """The tricorn walls' top edges, where the binding runs: one head-local polyline per wall."""
        rb, end, ex, ez, dx, dz = _bend()
        sides = self._sides()
        out = []
        for side in sides:
            n, tv, fold, lo, hi = side
            tpos = np.linspace(lo * 1.6, hi * 1.6, 161)
            tg = np.where(tpos > 0, tpos / hi, tpos / lo)
            l = _wall(tg) - 0.0012
            u = fold + TRI["bow"] * np.clip(1 - tg * tg, 0, 1) + ex + dx * l
            xy = n[None] * u[:, None] + tv[None] * tpos[:, None]
            mine = np.ones(len(tpos), bool)
            own = self._fold_u(xy[:, 0], xy[:, 1], side)[0]
            for other in sides:
                if other is not side:
                    mine &= own >= self._fold_u(xy[:, 0], xy[:, 1], other)[0]
            q = np.stack([xy[:, 0], xy[:, 1] + self.yc, ez + dz * l], 1)[mine]
            out.append(q @ self.rot.T + self.origin)
        return out

    def _mushroom(self, q):
        yc = self.yc
        cap = ellipsoid(q, (0, yc, 0.0), (0.148, 0.152, 0.1))
        under = ellipsoid(q, (0, yc, -0.036), (0.132, 0.136, 0.07))
        self._gill = np.abs(under) < np.abs(cap)
        cap = carve(cap, under, 0.01)
        x, y = q[:, 0], q[:, 1] - F(yc)
        cap = cap + F(0.003) * np.sin(np.arctan2(y, x) * F(5) + F(0.4)) * _smooth(F(0.09), F(0.14), np.hypot(x, y))
        self._spot = self.spots(q)
        return {"crown": cap - F(0.0016) * self._spot}

    def spots(self, q):
        """Raised pale spots scattered over the mushroom's dome (none on the rim): 0 off a spot .. 1 on one."""
        d = q - np.asarray((0, self.yc, 0.0), dtype=F)
        d = d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), F(1e-6))
        rng = np.random.default_rng(7)
        out = np.zeros(len(q), dtype=F)
        for k in range(13):
            z = 0.35 + 0.6 * (k + 0.5) / 13
            a = k * 2.39996 + rng.uniform(-0.3, 0.3)
            c = np.asarray((math.sqrt(1 - z * z) * math.cos(a), math.sqrt(1 - z * z) * math.sin(a), z), dtype=F)
            edge = math.cos(rng.uniform(0.1, 0.17))
            out = np.maximum(out, _smooth(F(edge - 0.004), F(edge + 0.004), d @ c))
        return out

    def field(self, p):
        parts = self.parts(p)
        d = parts["crown"]
        if "brim" in parts:
            d = smin(d, parts["brim"][0], 0.004)
        for name, k in (("band", 0.0012), ("peak", 0.003), ("plume", 0.004), ("badge", 0.0015)):
            if name in parts:
                d = smin(d, parts[name], k)
        return carve(d, self.inner(p), 0.002)


BOUNDS = ((-0.24, -0.26, -0.12), (0.24, 0.24, 0.34))
GRID, TARGET = 0.0012, 11000


def mesh(head, shape, clear):
    """The hat's mesh (world coordinates) and the region each vertex belongs to (crown, brim, under, band, trim)."""
    hat = Hat(shape, head.shape.fem, clear)
    key = ("widehat", shape, round(clear, 4), head.shape.fem)
    obj = fieldmesh.build(hat.field, BOUNDS, GRID, TARGET, f"hat_{shape}", key, ("headwear.py", "hats.py"), "headwear")
    refine(obj, hat.field)
    co = place(obj, head)
    regions = _regions(hat, centres(obj, co))
    return obj, (_bind(obj, hat, head, regions) if shape == "tricorn" else regions), hat


def _bind(obj, hat, head, regions, r=0.0034):
    """A binding round the tricorn walls' top edges, added to the hat's mesh as its trim."""
    import bmesh
    from mathutils import Vector
    from .features import round_section, spline, sweep
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    n0 = len(bm.faces)
    for path in hat.trim_paths():
        if len(path) > 2:
            sweep(bm, spline([Vector(head.c) + Vector(p) * head.h for p in path], 60), round_section(lambda t: r * head.h), 8)
    for f in bm.faces[n0:]:
        f.smooth = True
    n1 = len(bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    return np.concatenate([regions, np.full(n1 - n0, "trim", dtype=object)])


def _regions(hat, co):
    parts = hat.parts(co)
    names = list(parts)
    dist = np.stack([np.abs(parts[n][0] if isinstance(parts[n], tuple) else parts[n]) for n in names], 1)
    region = np.asarray(names, dtype=object)[np.argmin(dist, 1)]
    region = np.where(np.abs(hat.inner(co)) < 0.0016, "under", region)     # the lining
    if "brim" in parts:
        under = (parts["brim"][1] < 0) & (region == "brim")
        region = np.where(under, "under", region)
    if hat.shape == "mushroom":
        q = hat.local(co)
        gill = hat._gill
        ang = np.arctan2(q[:, 1] - hat.yc, q[:, 0])
        shade = np.where(gill, 0.88 + 0.12 * np.cos(ang * 90) ** 2, 1.0)
        region = np.where(gill, "gill", np.where(hat.spots(q) > 0.5, "spot", region))
        return region, shade
    return region
