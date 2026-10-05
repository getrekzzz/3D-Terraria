"""Caps with a shape of their own (``hatShape`` for hat = cap), sculpted like headwear's cap in head-local units:

- santa: the cap's crown running up into a long soft cone that flops over to hang behind one ear, a thick fur
  trim for its band and a fur pompom at the tip;
- elf: a pointed cone curling back at the tip, a scalloped cuff turned up round the head and a small pompom;
- helmet: an open-faced metal helmet: a dome with a raised crest, a brow band with rivets, cheek guards, a nasal and
  a flared neck guard;
- greathelm: a closed metal helm round the whole head, with an eye slit, breathing holes and a cross of bands;
- bandana: a cloth pulled over the skull, its edge a little uneven, folds drawn out from the knot at the back, two
  short tails and the square's corner hanging from the knot;
- turban: a long cloth wound round the head in a few soft turns rising to a peak over the brow, where a jewel is
  pinned, under a smooth full top, its end tucked in at one side;
- hardhat: a stiff shell with ribs over the crown and a lip all round, deeper over the brow;
- beret: a soft round disc slumped to one side over a narrow band, with a little stalk on top.

Every other cap shape (and a hood) is headwear's own cap; ``shako`` is built with the wide hats (hats.py), ``party``
and ``nurse`` rest on the hair (crowns.py)."""
import math

import numpy as np

from . import fieldmesh, headwear
from .headwear import _cranium, _rounded, _shell, _smooth
from .sdf import F, carve, ellipsoid, round_cone, smax, smin, sphere, tube

SHAPES = ("santa", "elf", "helmet", "greathelm", "bandana", "turban", "hardhat", "beret")
BOUNDS = ((-0.17, -0.22, -0.25), (0.17, 0.2, 0.3))
MIDDLE = -0.012                                  # the axis the bandana's edge is measured round
BAND_T, BAND_THICK = 0.0085, 0.0026              # the bandana's outer surface off the skull, and its cloth
KNOT = (0.004, -0.12, -0.024)
# the tails: which side, their path from the knot (out, back in units of 0.012, down) and half-widths along it
TAILS = ((1.0, ((0.004, 0.0, 0.0), (0.014, 0.25, 0.012), (0.021, 0.4, 0.032), (0.025, 0.45, 0.054),
                (0.027, 0.42, 0.074)), (0.009, 0.0135, 0.013, 0.0095, 0.003)),
         (-1.0, ((0.004, 0.0, 0.0), (0.013, 0.25, 0.011), (0.018, 0.38, 0.03), (0.021, 0.4, 0.05)),
          (0.009, 0.013, 0.0105, 0.003)))


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


def _ribbon(p, spine, widths, half, back=(0.0, -1.0, 0.0)):
    """A thin strip of cloth along a polyline, ``half`` thick each side, its half-width ``widths`` at each point,
    lying flat toward ``back``: a flattened tube (round-ended in its own plane, so its pieces join without gaps)."""
    out = None
    spine = [np.asarray(c, dtype=F) for c in spine]
    for i in range(len(spine) - 1):
        a, b = spine[i], spine[i + 1]
        d = b - a
        length = float(np.linalg.norm(d))
        d = d / F(length)
        w = np.cross(np.asarray(back, dtype=F), d)
        w = w / np.linalg.norm(w)
        n = np.cross(d, w)
        q = p - a
        t = np.clip(q @ d / F(length), 0, 1)
        q = q - (t * F(length))[:, None] * d
        flat = np.hypot(q @ d, q @ w) - (F(widths[i]) + F(widths[i + 1] - widths[i]) * t)
        dist = headwear._rounded(flat, np.abs(q @ n) - F(half), half * 0.8)
        out = dist if out is None else np.minimum(out, dist)
    return out


def _cone(p, spine, radii, wrinkle=0.0):
    d = tube(p, spine, radii)
    if wrinkle:
        z = p[:, 2]
        d = d + F(wrinkle) * np.sin(z * F(120) + p[:, 0] * F(60)) * _smooth(F(0.12), F(0.16), z)
    return d


class CapShape:
    def __init__(self, shape, fem, under=None, skull=False):
        self.shape, self.fem, self.under = shape, fem, under      # (what a greathelm's skirt rests on, as hood.drape)
        self.skull = skull                                        # (a helmet on a bare skull)
        self._inside, self._paint = None, {}

    def parts(self, p):
        s, fem = self.shape, self.fem
        if s in ("helmet", "greathelm"):
            from . import helms
            if s == "helmet":
                parts, self._inside = helms.helmet(p, fem, self.skull)
            else:
                parts, self._inside, self._paint = helms.greathelm(p, fem, self.under)
            return parts
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

    def _bandana(self, p):
        """Cloth tied tight over the skull: low on the brow, over the tops of the ears and down to the nape, its
        edge turned under; creases run up from the knot at the nape, and its two corners hang from the knot as thin
        tapering tails lying on the back of the neck, over the folded square's corner hanging from under the knot."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        base = _cranium(p, self.fem)
        ang = np.arctan2(x, y - F(MIDDLE))
        edge = F(0.0115) + F(0.031) * np.cos(ang) - F(0.0125) * np.cos(F(2) * ang)
        edge = edge + F(0.0018) * np.sin(F(5) * ang + F(0.8)) + F(0.001) * np.sin(F(11) * ang + F(2.1))   # (cloth, not a rim)
        knot = np.asarray(KNOT, dtype=F)
        kq = p - knot
        kr = np.hypot(kq[:, 0], kq[:, 2])
        near = _smooth(F(0.21), F(0.03), kr) * _smooth(F(0.008), F(0.024), kr) * _smooth(F(0.07), F(-0.01), y)
        fan = np.arctan2(kq[:, 2], kq[:, 0]) * F(5.0) + F(0.4) + F(18.0) * kr
        ridge = (F(0.5) + F(0.5) * np.cos(F(2) * fan)) ** 3                # folds drawn out from the knot
        soft = np.sin(x * F(60) + F(1.3)) * np.sin(y * F(45) - z * F(30)) * _smooth(F(0.0), F(0.06), z - edge)
        loose = _smooth(F(0.04), F(0.1), z) * _smooth(F(-0.06), F(0.04), y)    # (a little slack over the crown)
        t = F(BAND_T) + F(0.004) * ridge * near + F(0.0012) * soft + F(0.0018) * loose
        mid = base - t + F(BAND_THICK / 2)
        below = edge - z
        cloth = carve(_shell(base, t, BAND_THICK, 0.001), -below, 0.001)
        hem = np.sqrt((below + F(0.0016)) ** 2 + mid * mid) - F(0.0019)
        tie = ellipsoid(p, KNOT, (0.0105, 0.0085, 0.0095)) - _fluff(p, 0.0006, 300)
        for s in (-1.0, 1.0):
            lobe = ellipsoid(p, (KNOT[0] + 0.011 * s, KNOT[1] + 0.003, KNOT[2] + 0.002), (0.009, 0.006, 0.0075))
            tie = smin(tie, lobe, 0.003)
        tails = [_ribbon(p, [(KNOT[0] + s * a, KNOT[1] - 0.002 - 0.012 * b, KNOT[2] - c) for a, b, c in path],
                         widths, 0.0017) for s, path, widths in TAILS]
        # the corner of the folded square hanging from under the knot over the nape
        u, v = x - F(KNOT[0]), F(KNOT[2] - 0.002) - z
        sheet = np.abs(y - F(KNOT[1] - 0.007) - F(0.06) * v * v) - F(0.0013)          # (bowing out a little)
        point = _rounded(sheet, np.maximum((np.abs(u) - F(0.02) * (F(1) - v / F(0.058))) * F(0.94), -v), 0.0012)
        knot = smin(smin(smin(tie, tails[0], 0.003), tails[1], 0.003), point, 0.004)
        return {"crown": smin(cloth, hem, 0.0012), "knot": knot}

    def _turban(self, p, base):
        """A long cloth wound round the head in a few thick, soft turns: lying on the head at the bottom, swelling
        above into a full dome; on the front the turns rise from each side to meet in a peak over the brow, where
        the jewel is pinned; behind they run level; the top is a smooth cap of cloth, and the cloth's end is
        tucked in at one side."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        rim = F(0.014) + F(0.022) * np.clip(y / F(0.09), -1, 1)          # low behind, over the brow in front
        up = z - rim
        dome = ellipsoid(p, (0, -0.012, 0.052), (0.104, 0.122, 0.092))
        reach = F(0.012) + F(0.05) * _smooth(F(0.0), F(0.05), up)        # (it lies on the head at the bottom)
        vol = smax(smin(dome, base - F(0.012), 0.02), base - reach, 0.012)
        front = _smooth(F(-0.02), F(0.07), y)
        rise = up + F(0.42) * np.sqrt(x * x + F(0.012) ** 2) * front     # each turn's height: a peak over the brow
        turn = rise / F(0.026)
        k = np.floor(turn)
        frac = turn - k
        amp = F(0.0036) + F(0.0012) * np.sin(k * F(2.3) + F(0.6))         # (each turn its own fullness)
        roll = (F(0.5) - F(0.5) * np.cos(F(2 * np.pi) * frac)) ** F(0.6) * amp       # (soft valleys between the turns)
        wound = _smooth(F(0.1), F(0.07), rise)                            # (the turns end under the smooth top)
        # the end tucked in: a tapering flap lying over the turns on the left side, from behind up toward the top
        t = (np.arctan2(x, y) + F(2.3)) / F(0.8)
        w = F(0.004) + F(0.011) * (F(1) - np.clip(t, 0, 1))
        flap = _smooth(w, w - F(0.003), np.abs(z - F(0.058) - F(0.042) * t)) * _smooth(F(-0.06), F(0.04), t)
        vol = vol - roll * wound - F(0.003) * flap * _smooth(F(1.04), F(0.92), t)
        cloth = carve(carve(vol, up, 0.005), base - F(0.006), 0.002)
        return {"crown": cloth, "badge": ellipsoid(p, (0, 0.111, 0.075), (0.011, 0.0075, 0.014))}

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


def mesh(head, kind, shape, visor=True, under=(), skull=False):
    """A cap (or hood, its cowl draped over ``under`` where headwear takes it) and the region of each face, in world
    coordinates."""
    from .shellmesh import centres, place, refine
    if kind == "cap" and shape not in SHAPES:
        return headwear.cap_mesh(head, visor, 0.0055 if skull else 0.0)
    if kind == "hood":
        from . import hood
        return hood.mesh(head, under, shape)
    field, digest = None, None
    if shape == "greathelm" and under:
        from . import hood
        field, digest = hood.drape(head, under)
    cs = CapShape(shape, head.shape.fem, field, skull)
    key = ("capshape", shape, head.shape.fem, digest, skull)
    target = {"santa": 13000, "elf": 11000, "helmet": 16000, "greathelm": 6500}.get(shape, 11000)
    sources = ("headwear.py", "capshapes.py", "helms.py")

    def seen(c):            # a greathelm's inside only round the eye slit and up from its foot: the head hides the rest
        cs.parts(c)
        near = ((c[:, 1] > F(0.0)) & (np.abs(c[:, 2] + F(0.003)) < F(0.03))) | (c[:, 2] < F(-0.095))
        return (cs._inside > F(0.0015)) | near
    obj = fieldmesh.build(cs.field, BOUNDS, 0.0011, target, f"hat_{shape}", key, sources, "headwear",
                          keep=seen if shape == "greathelm" else None)
    other = {"bandana": "knot", "helmet": "band"}.get(shape)
    cuts = [lambda co: (lambda q: q["crown"] - q[other])(cs.parts(co))] if other else []
    if shape == "greathelm":       # (the bands' edges run straight; the small round holes want a fine mesh round them)
        cuts = [(lambda co, n=n: (cs.parts(co), cs._paint[n])[1], fine) for n, fine in (("band", 0.006), ("slit", 0.0016))]
    refine(obj, cs.field, budget=1.6, cuts=cuts, fine=0.0009 if shape == "greathelm" else 0.0016)
    fc = centres(obj, place(obj, head))
    parts = cs.parts(fc)
    names = list(parts)
    dist = np.stack([np.abs(parts[n]) for n in names], 1)
    regions = np.asarray(names, dtype=object)[np.argmin(dist, 1)]
    for name, inside in cs._paint.items():
        regions = np.where(inside < 0, name, regions)
    if shape == "greathelm":                    # the inside, seen through the eye slit
        regions = np.where(cs._inside < 0.0012, "slit", regions)
    return obj, regions
