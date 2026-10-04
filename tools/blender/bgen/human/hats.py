"""Wide hats sculpted as distance fields in head-local units (like headwear): a crown fitted over the skull and the hair
left below its band, a brim with thickness, curl and a bound edge, and a band round the crown. ``hatShape``:

- brim: a soft wide-brimmed hat, its crown creased down the middle and pinched at the front, the brim's sides
  curled up and its front snapped down (the default);
- floppy: a battered slouch hat, the crown dented and leaning, the wide brim sagging in waves;
- top: a top hat, its tall crown waisted and flaring a little, the narrow brim rolled up at the sides;
- pointed: a wizard's hat, a tall cone that bends back and wrinkles where it bends, over a wide wavy brim;
- tricorn: a low crown, its brim cocked up steeply on three sides and pinched into three low points, the front
  one over the face, its edge bound with braid (no band);
- bucket: a fisherman's hat, a rounded crown over a short brim that slopes down;
- mushroom: a mushroom's domed cap, its rim rolled under (the hat of a mushroom person);
- shako: a soldier's (or nutcracker's) tall flaring cap with a peak, gold bands, a badge and a plume, worn as a cap.
"""
import math

import numpy as np

from .headwear import _cranium, _rounded, _smooth
from .sdf import F, carve, ellipsoid, gradient, smin, tube

SHAPES = ("brim", "floppy", "top", "pointed", "tricorn", "bucket", "mushroom", "shako")
# the band's height over the brim (shako: its lower band and the rim round its top); hatmesh lays it on the crown
BAND = {"brim": ((0.0, 0.019),), "floppy": ((0.0, 0.016),), "top": ((0.0, 0.024),), "pointed": ((0.0, 0.02),),
        "bucket": ((0.0, 0.014),), "shako": ((0.0, 0.014), (0.117, 0.129))}
# band height at the crown's centre (head-local z) and pitch (front up) per shape
SEAT = {"brim": (0.044, 7), "floppy": (0.04, 4), "top": (0.046, 5), "pointed": (0.038, 6), "tricorn": (0.032, 8),
        "bucket": (0.036, 5), "mushroom": (0.074, 9), "shako": (0.04, 3)}


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

    def band_rows(self, heights, n=160):
        """Points on the crown's surface all round it at each of ``heights`` (hat-local, over the brim) and the
        surface's normals there, head-local: (len(heights), n, 3) each."""
        th = np.linspace(0, 2 * np.pi, n, endpoint=False)
        z = np.repeat(np.asarray(heights, dtype=F), n)
        d = np.stack([np.tile(np.cos(th), len(heights)), np.tile(np.sin(th), len(heights)), np.zeros(len(z))], 1).astype(F)

        def at(r):
            q = d * r[:, None] + np.stack([np.zeros_like(z), np.full_like(z, self.yc), z], 1)
            return (self.origin + q @ self.rot.T).astype(F)

        def crown(pts):
            return self.parts(pts)["crown"]
        lo, hi = np.zeros(len(z), F), np.full(len(z), 0.25, F)
        for _ in range(26):
            mid = (lo + hi) / 2
            inside = crown(at(mid)) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        pts = at(lo)
        g = gradient(crown, pts, 0.0005)
        g /= np.maximum(np.linalg.norm(g, axis=1), F(1e-9))[:, None]
        return pts.reshape(len(heights), n, 3), g.reshape(len(heights), n, 3)

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
        elif s == "floppy":
            c = self.crown(q, self.height(0.088, 0.028), taper=-0.14, rr=0.03, lean=0.08)
            x, y, z = q[:, 0], q[:, 1] - F(self.yc), q[:, 2]
            c = c + F(0.011) * np.exp(-(((x + F(0.03)) / F(0.035)) ** 2 + ((y - F(0.03)) / F(0.04)) ** 2)) * _smooth(F(0.05), F(0.09), z)
            c = c + F(0.0025) * np.sin(x * F(61) + F(1.3)) * np.sin(y * F(47)) * _smooth(F(0.02), F(0.08), z)
            out["brim"] = self.brim(q, lambda th: F(0.094) * (1 + F(0.05) * np.sin(F(9) * th) + F(0.03) * np.sin(F(17) * th + F(2))),
                                    lambda u, th: -F(0.032) * np.clip(u, 0, None) ** 1.6 * (F(0.62) + F(0.38) * np.sin(F(2) * th + F(0.6)))
                                    + F(0.012) * u * u * np.sin(F(5) * th + F(1)))
        elif s == "top":
            c = self.crown(q, 0.15, taper=0.07, rr=0.011, n=2.3, waist=0.05)
            out["brim"] = self.brim(q, lambda th: F(0.042) + F(0.008) * np.cos(th) ** 2,
                                    lambda u, th: F(0.03) * u * u * np.cos(th) ** 2 - F(0.006) * u * np.sin(th) ** 2)
        elif s == "pointed":
            c = self._cone(q)
            out["brim"] = self.brim(q, 0.1, lambda u, th: -F(0.022) * np.clip(u, 0, None) ** 1.5
                                    + F(0.012) * u * u * np.sin(F(3) * th + F(0.7)), t=0.0042)
        elif s == "shako":          # a tall flaring cylinder with a peak, a badge and a plume (no brim)
            H = 0.13
            c = self.liner(p, q, self.crown(q, H, taper=0.08, rr=0.009, n=2.4))
            x, y, z = q[:, 0], q[:, 1] - F(self.yc), q[:, 2]
            # a stiff peak leaving the crown under the band, dipping a little toward its rounded edge
            dip = F(math.tan(math.radians(17)))
            side = (z + F(0.001) + dip * (y - F(self.ry))) / np.sqrt(F(1) + dip * dip)
            plan = (np.sqrt((x / F(0.074)) ** 2 + ((y - F(self.ry - 0.035)) / F(0.083)) ** 2) - F(1)) * F(0.06)
            out["peak"] = np.maximum(_rounded(np.abs(side) - F(0.0021), plan, 0.0017), F(self.ry - 0.035) - y)
            out["badge"] = ellipsoid(q, (0, self.yc + 1.035 * self.ry + 0.002, 0.066), (0.022, 0.006, 0.028))
            plume = ellipsoid(q, (0, self.yc + 0.55 * self.ry, H + 0.035), (0.017, 0.017, 0.04))
            out["plume"] = plume + F(0.002) * np.sin(np.arctan2(y - F(0.55 * self.ry), x) * F(9))
            out["crown"] = c
            return out
        else:                       # bucket
            c = self.crown(q, self.height(0.078, 0.022), taper=-0.12, rr=0.03, n=2.1)
            out["brim"] = self.brim(q, 0.046, lambda u, th: -F(0.42 * 0.046) * u - F(0.004) * u * u, t=0.0042, edge=0.001)
        c = self.liner(p, q, c, 0.066 if s == "pointed" else 0.045)
        out["crown"] = c
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

    def _tricorn_shape(self, th):
        """The tricorn brim's width and how high its edge is cocked up, round the crown (th, from the hat's x axis):
        the corners (front, back left, back right) reach furthest and stay low, pinched to points; between them the
        brim is turned up steeply, highest mid-side."""
        c = th - F(math.pi / 2)                                    # 0 at the front corner
        side = np.abs(np.sin(F(1.5) * c))                          # 0 at a corner .. 1 mid-side
        width = F(0.05) + F(0.042) * np.abs(np.cos(F(1.5) * c)) ** F(2.5)
        low = F(0.014) - F(0.012) * np.clip(np.cos(c), 0, 1) ** 8   # the front point dips over the face
        return width, low + (F(0.07) - low) * side ** F(0.7)

    def _tricorn(self, p, q):
        """A low crown and its brim cocked up on three sides into three points, the front one over the face."""
        c = self.liner(p, q, self.crown(q, self.height(0.064, 0.02), taper=-0.12, rr=0.034))

        def width(th):
            return self._tricorn_shape(th)[0]

        def lift(u, th):
            return self._tricorn_shape(th)[1] * np.clip(u, 0, 1.2) ** F(2.4)
        return {"crown": c, "brim": self.brim(q, width, lift, t=0.0046, edge=0.0012)}

    def trim_paths(self):
        """The tricorn brim's edge, where the binding runs: one closed head-local polyline."""
        th = np.linspace(0, 2 * np.pi, 241)[:-1].astype(F)
        w, h = self._tricorn_shape(th)
        r0 = 1 / np.sqrt((np.cos(th) / self.rx) ** 2 + (np.sin(th) / self.ry) ** 2)
        r = r0 + w - 0.0012
        q = np.stack([r * np.cos(th), r * np.sin(th) + self.yc, h * ((r - r0) / w) ** 2.4], 1)
        return [np.vstack([q, q[:1]]) @ self.rot.T + self.origin]

    def gill_side(self, q):
        """The mushroom's dome and the hollow under it (hat-local); negative where the gills are nearer."""
        yc = self.yc
        cap = ellipsoid(q, (0, yc, 0.0), (0.148, 0.152, 0.1))
        under = ellipsoid(q, (0, yc, -0.036), (0.132, 0.136, 0.07))
        return cap, under, np.abs(under) - np.abs(cap)

    def _mushroom(self, q):
        yc = self.yc
        cap, under, side = self.gill_side(q)
        self._gill = side < 0
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

    def filled(self, p):
        """The hat as a solid, its inside filled (what the hair under it is cut back to, so none pokes through)."""
        parts = self.parts(p)
        d = parts["crown"]
        if "brim" in parts:
            d = smin(d, parts["brim"][0], 0.004)
        for name, k in (("peak", 0.003), ("plume", 0.004), ("badge", 0.0028)):
            if name in parts:
                d = smin(d, parts[name], k)
        return d

    def field(self, p):
        return carve(self.filled(p), self.inner(p), 0.002)
