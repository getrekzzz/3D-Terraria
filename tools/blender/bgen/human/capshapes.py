"""Caps with a shape of their own (``hatShape`` for hat = cap), sculpted like headwear's cap in head-local units:

- santa: the cap's crown running up into a long soft cone that flops over to hang behind one ear, a thick fur
  trim for its band and a fur pompom at the tip;
- elf: a pointed cone curling back at the tip, a scalloped cuff turned up round the head and a small pompom;
- helmet: an open-faced metal helmet: a dome with a raised crest, a brow band with rivets, cheek guards, a nasal and
  a flared neck guard;
- greathelm: a closed metal helm round the whole head, with an eye slit, breathing holes and a cross of bands;
- bandana: a cloth pulled tight over the skull and knotted at the back, two short tails hanging from the knot;
- turban: cloth wound round and round the head, the turns crossing over the brow, a jewel pinned there;
- hardhat: a stiff shell with ribs over the crown and a lip all round, deeper over the brow;
- beret: a soft round disc slumped to one side over a narrow band, with a little stalk on top.

Every other cap shape (and a hood) is headwear's own cap; ``shako`` is built with the wide hats (hats.py), ``party``
and ``nurse`` rest on the hair (crowns.py)."""
import math

import numpy as np

from . import fieldmesh, headwear
from .headwear import _cranium, _shell, _smooth
from .sdf import F, carve, ellipsoid, round_cone, smax, smin, sphere, tube

SHAPES = ("santa", "elf", "helmet", "greathelm", "bandana", "turban", "hardhat", "beret")
BOUNDS = ((-0.17, -0.22, -0.16), (0.17, 0.19, 0.3))


def _fluff(p, amp, freq):
    """Soft lumps for fur: a few crossed waves (deterministic)."""
    x, y, z = p[:, 0] * F(freq), p[:, 1] * F(freq), p[:, 2] * F(freq)
    w = np.sin(x * F(1.7) + y * F(0.6)) * np.sin(y * F(1.3) - z * F(0.9)) + np.sin(z * F(1.9) + x * F(0.8)) * F(0.6)
    return F(amp) * w


def _cap(p, fem, band=True):
    """headwear's cap crown without its peak: the cloth over the skull and its rim height."""
    base = _cranium(p, fem)
    y, z = p[:, 1], p[:, 2]
    rim = F(0.022) + F(0.02) * np.clip(y / F(0.09), -1, 1)
    t = F(0.012) + F(0.003) * _smooth(F(0.03), F(0.0), z - rim)
    d = _shell(base, t, 0.003)
    return carve(d, z - rim, 0.0012), base, rim, t


def _cone(p, spine, radii, wrinkle=0.0):
    d = tube(p, spine, radii)
    if wrinkle:
        z = p[:, 2]
        d = d + F(wrinkle) * np.sin(z * F(120) + p[:, 0] * F(60)) * _smooth(F(0.12), F(0.16), z)
    return d


class CapShape:
    def __init__(self, shape, fem):
        self.shape, self.fem = shape, fem

    def parts(self, p):
        s, fem = self.shape, self.fem
        if s == "greathelm":
            return self._greathelm(p)
        if s == "helmet":
            return self._helmet(p)
        if s in ("turban", "hardhat", "beret"):
            return getattr(self, "_" + s)(p, _cranium(p, fem))
        if s == "bandana":
            return self._bandana(p)
        crown, base, rim, t = _cap(p, fem)
        z = p[:, 2]
        if s == "santa":
            spine = [(0, -0.02, 0.06), (0, -0.03, 0.125), (0.006, -0.058, 0.168), (0.022, -0.098, 0.182),
                     (0.042, -0.132, 0.158), (0.056, -0.15, 0.112), (0.062, -0.152, 0.07)]
            cone = _cone(p, spine, [0.072, 0.058, 0.043, 0.031, 0.022, 0.015, 0.011], 0.0012)
            crown = smin(crown, carve(cone, _cranium(p, fem) - F(0.008), 0.002), 0.02)
            fur = _shell(base, t + F(0.016), 0.026)
            fur = carve(carve(fur, rim + F(0.03) - z, 0.008), z - rim + F(0.004), 0.008)
            fur = fur - _fluff(p, 0.0017, 170) - _fluff(p * F(1.7), 0.0006, 170)
            pom = sphere(p, (0.064, -0.153, 0.05), 0.024) - _fluff(p, 0.0022, 210)
            return {"crown": crown, "fur": fur, "pom": pom}
        # elf: a pointed cone and a cuff turned up round the head with a scalloped top edge
        spine = [(0, -0.02, 0.06), (0, -0.026, 0.135), (0.004, -0.042, 0.195), (0.018, -0.07, 0.238),
                 (0.042, -0.1, 0.252), (0.066, -0.118, 0.236), (0.074, -0.12, 0.214)]
        cone = _cone(p, spine, [0.072, 0.054, 0.037, 0.024, 0.014, 0.007, 0.004], 0.0008)
        crown = smin(crown, carve(cone, _cranium(p, fem) - F(0.008), 0.002), 0.018)
        ang = np.arctan2(p[:, 0], p[:, 1])
        edge = rim + F(0.02) + F(0.012) * (F(1) - np.abs(np.sin(ang * F(4))))
        cuff = carve(carve(_shell(base, t + F(0.0045), 0.006), edge - z, 0.0015), z - rim + F(0.002), 0.0015)
        pom = sphere(p, (0.076, -0.121, 0.206), 0.012) - _fluff(p, 0.0012, 360)
        return {"crown": crown, "band": cuff, "pom": pom}

    def _helmet(self, p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        base = _cranium(p, self.fem)
        rim = F(0.03) + F(0.018) * np.clip(y / F(0.09), -1, 1)          # low at the back, over the brow in front
        dome = carve(_shell(base, 0.0085, 0.0042), z - rim + F(0.004), 0.0015)
        crest = carve(np.maximum(np.abs(x) - F(0.0035), base - F(0.0135)), z - rim - F(0.012), 0.003)
        brow = carve(carve(_shell(base, 0.011, 0.005), rim + F(0.012) - z, 0.0012), z - rim + F(0.006), 0.0012)
        cheeks = _shell(ellipsoid(p, (0, 0.0, -0.03), (0.092, 0.105, 0.09)), 0.0, 0.004)
        cheeks = carve(carve(cheeks, np.abs(x) - F(0.05), 0.004), rim - z, 0.002)
        cheeks = carve(carve(carve(cheeks, F(0.052) - y, 0.006), z + F(0.074), 0.006), y + F(0.03), 0.004)
        neck = _shell(ellipsoid(p, (0, -0.03, -0.03), (0.09, 0.1, 0.085)), 0.0, 0.004)
        neck = carve(carve(carve(neck, F(-0.02) - y, 0.006), rim - z, 0.002), z + F(0.06), 0.006)
        nasal = np.maximum(ellipsoid(p, (0, 0.1, 0.016), (0.0065, 0.0085, 0.032)), -(z + F(0.01)))
        # rivets round the brow band, set on its surface
        ang = np.linspace(0, 2 * np.pi, 16, endpoint=False) + np.pi / 16
        heights = 0.03 + 0.018 * np.clip(0.1 * np.cos(ang) / 0.09, -1, 1) + 0.003
        dirs = np.stack([np.sin(ang), np.cos(ang), np.zeros_like(ang)], 1)
        lo, hi = np.zeros(16), np.full(16, 0.15)
        for _ in range(22):
            mid = (lo + hi) / 2
            pts = (np.asarray((0, -0.012, 0)) + dirs * mid[:, None]).astype(F)
            pts[:, 2] = heights
            inside = _cranium(pts, self.fem) < 0.0128
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        rivets = None
        for k in range(16):
            c = (dirs[k, 0] * lo[k], -0.012 + dirs[k, 1] * lo[k], heights[k])
            r = sphere(p, c, 0.0042)
            rivets = r if rivets is None else np.minimum(rivets, r)
        crown = smin(smin(dome, crest, 0.004), smin(cheeks, neck, 0.004), 0.006)
        return {"crown": crown, "band": brow, "peak": nasal, "rivet": rivets}

    def _greathelm(self, p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        yc = 0.006
        m = (np.abs(x / F(0.092)) ** 2.4 + np.abs((y - F(yc)) / F(0.118)) ** 2.4) ** (1 / 2.4)
        side = (m - F(1)) * F(0.092)
        dome = ellipsoid(p, (0, yc, 0.02), (0.092, 0.118, 0.115))
        outer = np.minimum(np.maximum(side, z - F(0.02)), dome)     # straight walls up to the dome's equator
        shell = smax(carve(outer, outer + F(0.0045), 0.001), -(z + F(0.122)), 0.0012)
        cut = np.maximum(np.maximum(np.abs(z + F(0.003)) - F(0.0045), np.abs(x) - F(0.066)), F(0.02) - y)
        cut = np.maximum(cut, F(0.0065) - np.abs(x))          # the bar between the eyes stays
        holes = None
        for k in range(10):
            hx, hz = 0.026 + 0.011 * (k % 5), -0.05 - 0.013 * (k // 5)
            hole = np.maximum(np.hypot(np.abs(x) - F(hx), z - F(hz)) - F(0.0026), F(0.03) - y)
            holes = hole if holes is None else np.minimum(holes, hole)
        shell = carve(carve(shell, cut, 0.0012), holes, 0.0008)
        bands = smax(outer - F(0.0018), np.minimum(np.abs(x) - F(0.0075), np.abs(z - F(0.017)) - F(0.0065)), 0.0014)
        bands = carve(smax(bands, -(z + F(0.122)), 0.0012), cut, 0.0012)
        self._inside = np.abs(outer + F(0.0045))
        return {"crown": shell, "band": bands}

    def _bandana(self, p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        r = np.hypot(x - F(0.008), z - F(0.008))
        near = _smooth(F(-0.02), F(-0.11), y) * _smooth(F(0.1), F(0.04), r) * _smooth(F(0.014), F(0.03), r)
        fold = F(0.0014) * np.sin(np.arctan2(z - F(0.008), x - F(0.008)) * F(7)) * near    # creases drawn to the knot
        out = p - np.asarray((0, -0.012, 0.022), dtype=F)
        crown = _cap(p + out * (fold / np.maximum(np.linalg.norm(out, axis=1), F(1e-6)))[:, None], self.fem)[0]
        knot = ellipsoid(p, (0.008, -0.123, 0.008), (0.018, 0.012, 0.014)) - _fluff(p, 0.0008, 300)
        sq = np.asarray((1.0, 4.0, 1.0), dtype=F)
        for end, slant in (((0.034, -0.104, -0.084), 0.7), ((-0.018, -0.108, -0.07), -0.6)):
            spine = [(0.008, -0.127, 0.0), ((0.008 + end[0]) / 2, -0.121, end[2] / 2), end]
            tail = tube(p * sq, [tuple(np.asarray(c) * sq) for c in spine], [0.01, 0.017, 0.024]) / F(4.0)
            d = np.asarray(end) - np.asarray(spine[1])
            d /= np.linalg.norm(d)
            n = d + np.asarray((slant, 0, 0))     # the cloth's end cut on the slant
            n /= np.linalg.norm(n)
            tail = smax(tail, (p - np.asarray(end, dtype=F) + F(0.01) * d.astype(F)) @ n.astype(F), 0.0015)
            knot = smin(knot, tail, 0.004)
        return {"crown": crown, "knot": knot}

    def _turban(self, p, base):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        rim = F(0.012) + F(0.024) * np.clip(y / F(0.09), -1, 1)
        wrap = smin(ellipsoid(p, (0, -0.008, 0.056), (0.103, 0.121, 0.086)), base - F(0.022), 0.02)
        phase = (z - rim) / F(0.019) + np.abs(np.arctan2(x, y)) / F(np.pi) * F(1.6)
        frac = phase - np.floor(phase)
        wrap = wrap + F(0.0022) * np.exp(-((frac - F(0.5)) / F(0.17)) ** 2) - F(0.0012) * np.sin(np.pi * frac)
        wrap = carve(carve(wrap, z - rim, 0.004), base - F(0.006), 0.002)
        return {"crown": wrap, "badge": ellipsoid(p, (0, 0.111, 0.06), (0.011, 0.0075, 0.014))}

    def _hardhat(self, p, base):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        rim = F(0.03) + F(0.01) * np.clip(y / F(0.09), -1, 1)
        shell = carve(_shell(base, 0.017, 0.0045), z - rim, 0.0015)
        lip = smax(np.abs(z - rim - F(0.002)) - F(0.0028), base - F(0.027) - F(0.02) * _smooth(F(0.0), F(0.09), y), 0.0024)
        lip = carve(lip, base - F(0.0135), 0.002)
        ribs = None
        for cx in (-0.024, 0.0, 0.024):
            r = smax(np.abs(x - F(cx)) - F(0.0042), base - F(0.0215), 0.0016)
            ribs = r if ribs is None else np.minimum(ribs, r)
        ribs = carve(ribs, z - rim - F(0.03), 0.01)
        return {"crown": smin(shell, ribs, 0.003), "brim": lip}

    def _beret(self, p, base):
        y, z = p[:, 1], p[:, 2]
        rim = F(0.03) + F(0.016) * np.clip(y / F(0.09), -1, 1)
        band = carve(carve(_shell(base, 0.0105, 0.0045), rim + F(0.009) - z, 0.0012), z - rim, 0.0012)
        a = np.radians(14)
        rot = ((np.cos(a), 0, np.sin(a)), (0, 1, 0), (-np.sin(a), 0, np.cos(a)))
        disc = ellipsoid(p, (0.014, -0.014, 0.079), (0.108, 0.112, 0.034), rot) - _fluff(p, 0.0005, 90)
        disc = smin(disc, base - F(0.0105), 0.026)
        disc = carve(carve(disc, base - F(0.0065), 0.002), z - rim - F(0.004), 0.003)
        stalk = round_cone(p, (0.03, -0.012, 0.104), (0.035, -0.013, 0.125), 0.0062, 0.004)
        return {"crown": smin(disc, stalk, 0.003), "band": band}

    def field(self, p):
        parts = self.parts(p)
        d = parts["crown"]
        for name, v in parts.items():
            if name != "crown":
                d = smin(d, v, 0.0015) if name in ("fur", "pom", "band", "knot", "brim") else np.minimum(d, v)
        return d


def _cap_regions(co, visor):
    """headwear's plain cap: the turned band and the peak, a shade darker than the crown."""
    y, z = co[:, 1], co[:, 2]
    rim = 0.022 + 0.02 * np.clip(y / 0.09, -1, 1)
    region = np.where(z < rim + 0.0125, "band", "crown").astype(object)
    if visor:
        region = np.where((y > 0.07) & (z < 0.06), "peak", region)
    return region


def mesh(head, kind, shape, visor=True, under=()):
    """A cap (or hood, its cowl draped over ``under`` where headwear takes it) and the region of each face, in world
    coordinates."""
    from .shellmesh import centres, place, refine
    if kind == "hood" or shape not in SHAPES:
        obj = headwear.mesh(head, kind, visor and kind == "cap", *((under,) if under else ()))
        me = obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        fc = centres(obj, (co.reshape(-1, 3) - np.asarray(head.c, dtype=np.float32)) / head.h)
        regions = _cap_regions(fc, visor) if kind == "cap" else np.full(len(fc), "crown", dtype=object)
        return obj, regions
    cs = CapShape(shape, head.shape.fem)
    key = ("capshape", shape, head.shape.fem)
    target = {"santa": 13000, "elf": 11000, "greathelm": 22000}.get(shape, 12000)     # the helm's flats sliver most
    obj = fieldmesh.build(cs.field, BOUNDS, 0.0011, target, f"hat_{shape}", key, ("headwear.py", "capshapes.py"), "headwear")
    refine(obj, cs.field, budget=1.25 if shape == "greathelm" else 1.6)
    fc = centres(obj, place(obj, head))
    parts = cs.parts(fc)
    names = list(parts)
    dist = np.stack([np.abs(parts[n]) for n in names], 1)
    regions = np.asarray(names, dtype=object)[np.argmin(dist, 1)]
    if shape == "greathelm":
        regions = np.where(cs._inside < 0.0012, "slit", regions)
    return obj, regions
