"""The head's surface as a signed distance field in head-local units (origin at the skull's centre, x to the
character's right, y forward, z up; multiply by Head.h for metres).

The skull and face masses (cranium, brow, cheekbones, cheeks, the lofted lower face and jaw, chin, muzzle) and what
age does to them are in faceforms.py; the nose and lips in nosemouth.py; creature faces (snout, tusks, ear openings) in
creature.py; the ears in earshape.py. This module puts them together with the eyes and the neck: eyelids are a shell
round each eyeball with an almond opening (margins, a crease above, a roll below, the caruncle in the inner corner),
and the neck meets the jaw with a clean angle in front and blends wide into the skull behind. Fine cuts (mouth line,
nostrils, grooves) are placed on the surface the masses make, through anchors found by bisection. Women get larger
eyes and a slimmer neck; age adds bags under the eyes, hooded lids, a longer nose and ears and a heavier brow.

Creature parameters: noseWidth (1) spreads the nose wider and flatter, an ogre's; mouthWidth (1) widens the mouth, a
goblin's; underbite (0..1) carries the lower lip and chin forward; tusks (a colour) rise from the lower jaw; snout
(0..1) slopes a low skull into a reptile's snout over a thick neck; headSize above 1 sinks the head onto a shorter
neck (head.py). Monster faces (``face``) replace the features from monsterface.py, borrowing _eye, _nose and _mouth.
"""
import math

import numpy as np

from . import creature, faceforms, monsterface, nosemouth, sdf
from .earshape import Ear
from .sdf import F, capsule, carve, ellipsoid, round_cone, smin, sphere, tube


def _bump(t, peak):
    """0 at t = 0 and 1, 1 at t = peak (a skewed sine arch)."""
    g = math.log(0.5) / math.log(peak)
    return np.maximum(np.sin(np.pi * np.clip(t, 0, 1) ** g), 0.0) ** 0.85     # (sin(pi) can come out just below 0)


class HeadShape:
    def __init__(self, frame, eyes, eye_r, p=None):
        p = p or {}
        self.fem = 1.0 if frame.female else 0.0
        self.male = 1.0 - self.fem
        self.old = 1.0 if frame.old else 0.0
        self.fat = frame.weight
        self.full = max(0.0, frame.weight - 0.45) / 0.55       # weight above average (0 .. 1) and below it
        self.lean = max(0.0, 0.45 - frame.weight) / 0.45
        self.eyes = eyes            # sx -> head-local eye centre (numpy)
        self.R = eye_r              # eyeball radius (head-local)
        self.Rs = eye_r + 0.0021    # outer surface of the lids
        # Eye opening in the eye's frontal plane: u toward the outer corner, v up (head-local units), sized with the
        # eyeball; a woman's outer corner lifts a little.
        k = eye_r / 0.0118
        self.u_in, self.u_out = -0.0128 * k, 0.0116 * k
        self.v_in, self.v_out = -0.0006 * k, (0.0011 + 0.0004 * self.fem) * k
        self.lid_up, self.lid_down = 0.0037 * k, 0.0046 * k
        bulk, fat = max(0.0, frame.build - 0.5) * 2.0, max(0.0, frame.weight - 0.45) / 0.55
        self.neck_r = ((0.0545 - 0.008 * self.fem) * (0.82 + 0.36 * frame.build) ** 0.4 * (1.0 + 0.16 * fat + 0.08 * bulk)
                       * (1.0 + 0.25 * float(p.get("snout", 0.0))) / float(p.get("headSize", 1.0)) ** 0.7)
        self.n = 0.9 if self.fem else 1.0            # nose scale: finer and shorter on women
        self.mouth_up = 0.0045 * self.fem            # a woman's mouth sits higher, under her shorter nose
        # Creature features (all 0, or 1 for the scales, on people): see human/__init__.py.
        self.brow, self.jaw, self.snout = (float(p.get(k, 0.0)) for k in ("brow", "jaw", "snout"))
        self.nose_len = float(p.get("noseLength", 1.0))
        self.mouth_w = float(p.get("mouthWidth", 1.0))
        self.nose_w = float(p.get("noseWidth", 1.0))
        self.underbite = float(p.get("underbite", 0.0))
        self.tusks = bool(p.get("tusks"))
        self.ear_spec = tuple(float(p.get(k, v)) for k, v in (("earSize", 1.0), ("earPoint", 0.0), ("earFlare", 0.0)))
        if self.old:      # age: the nose and ears keep growing and the brow grows heavier
            self.nose_len += 0.1
            self.brow += 0.25 * self.male
            self.ear_spec = (self.ear_spec[0] * 1.08,) + self.ear_spec[1:]
        self.ears = {sx: Ear(sx, self.fem, *self.ear_spec) for sx in (1, -1)} if self.ear_spec[0] > 0 else {}
        self.monster = p.get("face")             # a monster's face (monsterface.py), or None for a person's
        self.anchors = None
        self.anchors = self._find_anchors()

    def key(self):
        """Everything the shape depends on (heads with equal keys are identical)."""
        key = (self.fem, self.old, round(float(self.fat), 4), round(float(self.neck_r), 5), round(float(self.R), 5),
               tuple(round(float(v), 5) for v in self.eyes[1]), self.brow, self.jaw, self.snout, self.nose_len, self.ear_spec,
               self.mouth_w, self.nose_w, self.underbite, self.tusks)
        return key + ((self.monster, monsterface.digest()),) if self.monster else key

    def bounds(self):
        """The box the head is meshed in: a person's, grown round big ears, a long nose or a snout."""
        lo, hi = [-0.1, -0.13, -0.3], [0.1, 0.14, 0.13]
        if self.ear_spec != (1.0, 0.0, 0.0):
            for ear in self.ears.values():
                lo = [min(a, float(c) - ear.radius) for a, c in zip(lo, ear.centre)]
                hi = [max(a, float(c) + ear.radius) for a, c in zip(hi, ear.centre)]
        hi[1] = max(hi[1], 0.084 + 0.045 * self.nose_len, (creature.tip_y(self) + 0.025) if self.snout > 0 else 0.0)
        return tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi)

    def ear_room(self, p, gap=0.0045):
        """The space a creature's ears take, grown by ``gap``: a hood cuts it out so they come through it."""
        out = np.ones(len(p), dtype=F)
        for ear in self.ears.values():
            out = np.minimum(out, sdf.region(p, out, ear.centre, ear.radius, lambda q, dd, ear=ear: ear.field(q, dd)))
        return out - F(gap)

    def _surface(self, a, b, iters=26):
        """Where segments a -> b (a inside, b outside) first cross the surface, for many segments at once."""
        a, b = np.asarray(a, dtype=F), np.asarray(b, dtype=F)
        lo, hi = np.zeros(len(a), dtype=F), np.ones(len(a), dtype=F)
        for _ in range(iters):
            mid = (lo + hi) * F(0.5)
            inside = self.field(a + (b - a) * mid[:, None]) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        return a + (b - a) * lo[:, None]

    def _find_anchors(self):
        """Fine cuts (mouth line, philtrum groove, nostrils, alar creases) placed on the surface the
        other forms make, so each one opens through the skin instead of leaving a bubble inside it."""
        segs, keys = [], []

        def seg(key, a, b):
            keys.append(key)
            segs.append((a, b))

        for x in np.linspace(-0.0218, 0.0218, 11) * self.mouth_w:   # the junction of the lips, down a little at the corners
            z = -0.0699 - 0.0018 * (x / (0.0255 * self.mouth_w)) ** 2 + self.mouth_up
            seg("mouth", (x, 0.07, z), (x, 0.11, z))
        for key, a, b in nosemouth.anchor_segments(self):
            seg(key, a, b)
        if self.snout > 0:
            for key, a, b in creature.anchor_segments(self):
                seg(key, a, b)
        if not self.ears:         # where an earless head's ear openings go
            for sx in (1, -1):
                seg("tympanum", (sx * 0.03, -0.012, -0.014), (sx * 0.12, -0.012, -0.014))
        if self.old:              # two shallow lines across the forehead
            for k, z in enumerate((0.047, 0.059)):
                for x in np.linspace(-0.026 + 0.004 * k, 0.026 - 0.004 * k, 9):
                    zz = z + 0.004 * (x / 0.026) ** 2
                    seg(f"forehead{k}", (x, 0.03, zz), (x, 0.13, zz))
        hits = self._surface([a for a, _ in segs], [b for _, b in segs])
        out = {}
        for k, h in zip(keys, hits):
            out.setdefault(k, []).append(h)
        return {k: np.asarray(v) for k, v in out.items()}

    # ------------------------------------------------------------------ eye opening
    def lid_curves(self, u):
        """Heights (v) of the upper and lower lid margins at u."""
        t = (u - self.u_in) / (self.u_out - self.u_in)
        base = self.v_in + (self.v_out - self.v_in) * np.clip(t, 0, 1)
        return base + self.lid_up * _bump(t, 0.43), base - self.lid_down * _bump(t, 0.58)

    def _eye_uv(self, p, sx):
        e = self.eyes[sx]
        return (p[:, 0] - e[0]) * sx, p[:, 2] - e[2], p[:, 1] - e[1]

    def opening(self, p, sx):
        u, v, w = self._eye_uv(p, sx)
        upper, lower = self.lid_curves(u)
        d2 = np.maximum.reduce([v - upper, lower - v, self.u_in - u, u - self.u_out])
        return np.maximum.reduce([d2, F(0.0018) - w, w - F(self.Rs + 0.002)])   # only through the lids

    def _on_lid_sphere(self, u, v, r):
        """Head-local points at (u, v) in the eye's frontal plane, pushed onto a sphere of radius r round the eye."""
        out = {}
        for sx in (1, -1):
            e = self.eyes[sx]
            w = np.sqrt(np.maximum(r * r - u * u - v * v, 0.0))
            out[sx] = np.stack([e[0] + sx * u, e[1] + w, e[2] + v], 1)
        return out

    def _lid_tube(self, p, offset, r_sphere, radius, sx, upper=True, span=(0.08, 0.92)):
        u = np.linspace(self.u_in + span[0] * (self.u_out - self.u_in), self.u_in + span[1] * (self.u_out - self.u_in), 9)
        up, lo = self.lid_curves(u)
        v = (up if upper else lo) + offset * _bump((u - self.u_in) / (self.u_out - self.u_in), 0.5)
        pts = self._on_lid_sphere(u, v, r_sphere)[sx]
        n = len(pts)
        radii = [radius * (0.35 + 0.65 * math.sin(math.pi * (i + 0.5) / n)) for i in range(n)]
        return tube(p, list(pts), radii)

    # ------------------------------------------------------------------ the field
    def field(self, p, ears=True, face=True):
        """The head's distance at each point; without ``face`` (eyes, nose and mouth) and ``ears`` it is the bare
        skull and neck, which hair is laid over."""
        sn = self.snout
        d = faceforms.masses(self, p)
        if sn > 0:
            d = creature.jaws(self, p, d)
        if self.monster:
            d = monsterface.skull(self, p, d)
        if self.old:
            d = faceforms.age(self, p, d)
        d = self._neck(p, d)
        for sx in (1, -1):
            if face and not self.monster:
                d = sdf.region(p, d, self.eyes[sx], 0.047, lambda q, dd, sx=sx: self._eye(q, dd, sx))
            if ears and sx in self.ears:
                d = sdf.region(p, d, self.ears[sx].centre, self.ears[sx].radius, self.ears[sx].field)
        if ears and not self.ears:
            d = creature.earless(self, p, d)
        if face and sn > 0:
            d = sdf.region(p, d, (0, 0.11, -0.05), 0.07, lambda q, dd: creature.field(self, q, dd))
        elif face and self.monster:
            d = monsterface.face(self, p, d)
        elif face:
            d = sdf.region(p, d, (0, 0.09, -0.03), 0.058, lambda q, dd: nosemouth.nose(self, q, dd))
            d = sdf.region(p, d, (0, 0.082, -0.071), 0.046 + 0.025 * max(0.0, self.mouth_w - 1.0),
                           lambda q, dd: nosemouth.mouth(self, q, dd))
            if self.tusks:
                d = sdf.region(p, d, (0, 0.085, -0.06), 0.06, lambda q, dd: creature.with_tusks(self, q, dd))
        return d

    def _neck(self, p, d):
        fem, male, fat = self.fem, self.male, self.fat
        r = self.neck_r
        q = p * np.asarray((1.0, 1.08, 1.0), dtype=F)
        # Down into the chest; the generator trims what the torso covers.
        neck = capsule(q, (0, -0.006 * 1.08, -0.05), (0, -0.03 * 1.08, -0.3), r)
        for sx in (1, -1):    # sternocleidomastoids: from behind the ears down the front of the neck to the collarbones
            neck = smin(neck, round_cone(p, (sx * 0.046, -0.018, -0.045), (sx * 0.016, r * 0.42, -0.25),
                                         0.0066 - 0.0018 * fem, 0.0055 - 0.0012 * fem), 0.02)
        if male:
            neck = smin(neck, E_adam(p, r), 0.012)
        if fat > 0.55:          # a fuller throat under the chin
            neck = smin(neck, ellipsoid(p, (0, 0.036, -0.104), (0.03, 0.034, 0.016 + 0.02 * (fat - 0.55))), 0.014)
        # The jaw line stays crisp in front (a reptile's throat runs smoothly up under its jaws); the nape blends wider
        # into the skull.
        k = (F(0.013 + 0.003 * fem + 0.022 * self.snout) + F(0.01) * np.clip((F(0.015) - p[:, 1]) / F(0.03), 0, 1)).astype(F) * F(1.25)
        h = np.maximum(k - np.abs(d - neck), F(0)) / k
        return np.minimum(d, neck) - h * h * h * k / F(6.0)

    def _eye(self, p, d, sx):
        R, Rs = self.R, self.Rs
        e = self.eyes[sx]
        # Socket under the brow, then room for the eyeball.
        d = carve(d, ellipsoid(p, e + np.asarray((sx * 0.0005, 0.0145, 0.002 + 0.001 * self.fem)),
                               (0.0168, 0.0115, 0.0122 - 0.0006 * self.fem)), 0.01)
        d = carve(d, sphere(p, e, R + 0.0004), 0.0006)
        # Lids: a shell over the eyeball blending into brow and cheek, opened in an almond.
        d = smin(d, sphere(p, e, Rs), 0.0065)
        d = smin(d, self._lid_tube(p, -0.002, Rs - 0.0003, 0.0009, sx, upper=False, span=(0.12, 0.95)), 0.0025)  # lower roll
        if self.old:   # bags under the eyes: a second, softer roll below the lid and the crease under it
            d = smin(d, self._lid_tube(p, -0.0052, Rs + 0.0004, 0.0013, sx, upper=False, span=(0.15, 0.95)), 0.0035)
            d = carve(d, self._lid_tube(p, -0.0078, Rs + 0.0012, 0.0005, sx, upper=False, span=(0.2, 0.92)), 0.002)
            # and the skin above the lid hangs over its crease, heaviest toward the outer corner
            d = smin(d, self._lid_tube(p, 0.0062, Rs + 0.0011, 0.0016, sx, span=(0.3, 1.0)), 0.003)
        d = carve(d, self.opening(p, sx), 0.0006)
        d = carve(d, self._lid_tube(p, 0.0048 + 0.0008 * self.fem, Rs + 0.0006, 0.00085, sx, span=(0.12, 0.9)), 0.0014)  # crease
        return smin(d, self.caruncle(p, sx), 0.0008)

    def caruncle(self, p, sx):
        e = self.eyes[sx]
        c = e + np.asarray((sx * (self.u_in + 0.0014), 0.0042, -0.0003))
        return ellipsoid(p, c, (0.0017, 0.0016, 0.0014))

    def lips(self, p):
        """The lips' own masses (where the painter puts their colour)."""
        return nosemouth.lips(self, p)

    def _nose(self, p, d):          # the person's nose and mouth, which monster faces borrow
        return nosemouth.nose(self, p, d)

    def _mouth(self, p, d):
        return nosemouth.mouth(self, p, d)


def E_adam(p, neck_r):
    """The larynx (Adam's apple) a third of the way down the front of a man's neck."""
    return ellipsoid(p, (0, neck_r * 0.62, -0.135), (0.0065, 0.0062, 0.0105))


def mesh(shape, h_local=0.001, target=14000, name="head"):
    """The head mesh (head-local coordinates): sampled, meshed, snapped onto the field and reduced to about
    ``target`` triangles, keeping detail where the surface bends."""
    from . import fieldmesh
    return fieldmesh.build(shape.field, shape.bounds(), h_local, target, name, shape.key(), ("headshape.py", "earshape.py", "creature.py"), "heads")
