"""The head's surface as a signed distance field in head-local units (origin at the skull's centre, x to the
character's right, y forward, z up; multiply by Head.h for metres).

Bone and fat masses (cranium, brow ridge, cheekbones, jaw, chin, muzzle, cheeks) blend softly; the features cut
and add with tight blends so their edges stay crisp: eyelids are a shell round each eyeball with an almond opening
(margins, a crease above, a roll below, the caruncle in the inner corner), the nose has a bridge, a tip with two
lobules, wings set off by an alar groove, nostrils and a columella; the lips have a cupid's bow, a tubercle, a mouth
line turning down slightly at the corners, the philtrum above and the hollow below. The neck meets the jaw with a
clean angle. Women get a smaller brow ridge, a narrower jaw and smaller chin, fuller lips, a finer nose and a slimmer
neck; age deepens folds and thins lips; weight fills cheeks and the throat.
"""
import math

import numpy as np

from . import sdf
from .earshape import Ear
from .sdf import F, capsule, carve, ellipsoid, round_cone, smin, sphere, tube


NOSTRIL_NORMAL = np.asarray((0.0, 0.4, -0.92)) / math.hypot(0.4, 0.92)   # nostrils open down, a little forward


def _basis(x_dir, up=(0.0, 0.0, 1.0)):
    """Local-to-world rotation whose local x runs along ``x_dir`` and local z stays near ``up``."""
    x = np.asarray(x_dir, dtype=float)
    x /= np.linalg.norm(x)
    z = np.asarray(up, dtype=float) - x * np.dot(up, x)
    z /= np.linalg.norm(z)
    y = np.cross(z, x)
    return np.stack([x, y, z], 1)


def _bump(t, peak):
    """0 at t = 0 and 1, 1 at t = peak (a skewed sine arch)."""
    g = math.log(0.5) / math.log(peak)
    return np.sin(np.pi * np.clip(t, 0, 1) ** g) ** 0.85


class HeadShape:
    def __init__(self, frame, eyes, eye_r):
        self.fem = 1.0 if frame.female else 0.0
        self.male = 1.0 - self.fem
        self.old = 1.0 if frame.old else 0.0
        self.fat = frame.weight
        self.eyes = eyes            # sx -> head-local eye centre (numpy)
        self.R = eye_r              # eyeball radius (head-local)
        self.Rs = eye_r + 0.0021    # outer surface of the lids
        # Eye opening in the eye's frontal plane: u toward the outer corner, v up (head-local units).
        self.u_in, self.u_out = -0.0128, 0.0116
        self.v_in, self.v_out = -0.0006, 0.0011
        self.lid_up, self.lid_down = 0.0037, 0.0046
        self.neck_r = (0.053 - 0.008 * self.fem) * (0.82 + 0.36 * frame.build) ** 0.4
        self.n = 0.9 if self.fem else 1.0            # nose scale: finer and shorter on women
        self.ears = {sx: Ear(sx, self.fem) for sx in (1, -1)}
        self.anchors = None
        self.anchors = self._find_anchors()

    def key(self):
        """Everything the shape depends on (heads with equal keys are identical)."""
        return (self.fem, self.old, round(float(self.fat), 4), round(float(self.neck_r), 5), round(float(self.R), 5),
                tuple(round(float(v), 5) for v in self.eyes[1]))

    def nose_at(self, x, y, z):
        """A nose point (designed for the male nose) scaled for this face; women's tips turn up slightly."""
        n = self.n
        return (x * n, 0.084 + (y - 0.084) * n, z * n + 0.0018 * self.fem * (1 if z < -0.03 else 0))

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

        for x in np.linspace(-0.0205, 0.0205, 11):              # the junction of the lips, down a little at the corners
            z = -0.0699 - 0.0018 * (x / 0.024) ** 2
            seg("mouth", (x, 0.07, z), (x, 0.11, z))
        for z in np.linspace(-0.0603, -0.0562, 4):
            seg("philtrum", (0, 0.08, z), (0, 0.11, z))
        for sx in (1, -1):        # across the underside of the tip, between the columella and the wing
            c = np.asarray(self.nose_at(sx * 0.0062, 0.0985, -0.0465))
            seg("nostril", c - NOSTRIL_NORMAL * 0.004, c + NOSTRIL_NORMAL * 0.006)
            out = np.asarray((sx, 0.25, 0.0)) / math.hypot(1, 0.25)
            for q in ((0.0156, 0.0928, -0.0378), (0.018, 0.0888, -0.0428), (0.0168, 0.0876, -0.0474)):
                c = np.asarray(self.nose_at(sx * q[0], q[1], q[2]))
                seg("alar", c - out * 0.005, c + out * 0.007)
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
        fem, male, old, fat = self.fem, self.male, self.old, self.fat
        E = ellipsoid
        # Cranium, forehead, occiput.
        d = E(p, (0, -0.012, 0.022), (0.0755, 0.095, 0.086))
        d = smin(d, E(p, (0, 0.034, 0.03 + 0.003 * fem), (0.061, 0.05, 0.058)), 0.03)
        d = smin(d, E(p, (0, -0.056, -0.008), (0.058, 0.054, 0.06)), 0.03)
        # Face mass: maxilla (muzzle), lower face, between the jaw lines.
        d = smin(d, E(p, (0, 0.056, -0.046), (0.037, 0.036, 0.031)), 0.022)
        d = smin(d, E(p, (0, 0.046, -0.074), (0.04 - 0.004 * fem, 0.04, 0.033)), 0.02)
        d = smin(d, E(p, (0, 0.064, -0.066), (0.03, 0.028, 0.022)), 0.024)          # mouth (orbicularis)
        jaw_x = 0.054 - 0.007 * fem
        for sx in (1, -1):
            # Brow ridge over each eye, lower and further back toward the temple.
            d = smin(d, E(p, (sx * 0.029, 0.0775 + 0.0015 * male, 0.0165), (0.021, 0.0095 + 0.0025 * male, 0.0085 - 0.001 * fem),
                          _basis((sx, -0.28, -0.12))), 0.012)
            # Cheekbone body and arch.
            d = smin(d, E(p, (sx * 0.049, 0.056, -0.012 + 0.002 * fem), (0.0175, 0.016, 0.012), _basis((sx, 0.5, 0.1))), 0.02)
            d = smin(d, capsule(p, (sx * 0.054, 0.048, -0.009), (sx * 0.064, 0.004, -0.012), 0.006), 0.02)
            # Soft cheeks under the cheekbones (fuller with weight and on women).
            d = smin(d, E(p, (sx * 0.039, 0.052, -0.043), ((0.023 + 0.002 * fem) * (0.9 + 0.35 * fat), 0.021, 0.026)), 0.026)
            # Masseter: from the cheekbone's arch down to the jaw angle (fills the side of the face), and the buccal
            # pad in front of it, so no hollow is left between the cheek, the masseter and the jaw.
            d = smin(d, round_cone(p, (sx * 0.053, 0.03, -0.02), (sx * (jaw_x - 0.003), 0.006, -0.062 + 0.004 * fem), 0.0125, 0.011), 0.02)
            d = smin(d, E(p, (sx * 0.0425, 0.018, -0.066), (0.011, 0.012, 0.014)), 0.015)
            # Jaw: ramus up to the ear, the body to the chin.
            ang = (sx * jaw_x, -0.004, -0.071 + 0.006 * fem)
            d = smin(d, round_cone(p, (sx * (jaw_x + 0.002), -0.01, -0.03), ang, 0.009, 0.0105 + 0.001 * male), 0.014 + 0.006 * fem)
            d = smin(d, tube(p, [ang, (sx * 0.03, 0.05, -0.096 + 0.002 * fem), (sx * 0.012, 0.079, -0.104 + 0.003 * fem)],
                             [0.0105 + 0.001 * male, 0.0098, 0.0115 - 0.002 * fem]), 0.016 + 0.006 * fem)
            # Side of the head round the ear (temporal bone, mastoid), then the temple hollow behind the brow.
            d = smin(d, E(p, (sx * 0.05, -0.012, -0.012), (0.025, 0.038, 0.034)), 0.03)
            d = carve(d, E(p, (sx * 0.076, 0.043, 0.012), (0.011, 0.026, 0.022)), 0.02)
        # Chin and glabella.
        d = smin(d, E(p, (0, 0.081 - 0.002 * fem, -0.1 + 0.004 * fem), (0.017 - 0.004 * fem, 0.0125 - 0.001 * fem, 0.016 - 0.002 * fem)), 0.012 + 0.004 * fem)
        d = smin(d, E(p, (0, 0.0825, 0.0125), (0.012, 0.0075 + 0.001 * male, 0.0095)), 0.01)
        d = self._neck(p, d)
        for sx in (1, -1):
            if face:
                d = sdf.region(p, d, self.eyes[sx], 0.047, lambda q, dd, sx=sx: self._eye(q, dd, sx))
            if ears:
                d = sdf.region(p, d, self.ears[sx].centre, 0.046, self.ears[sx].field)
        if face:
            d = sdf.region(p, d, (0, 0.09, -0.03), 0.058, self._nose)
            d = sdf.region(p, d, (0, 0.082, -0.071), 0.046, self._mouth)
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
        # The jaw line stays crisp in front; the nape blends wider into the skull.
        k = (F(0.009) + F(0.011) * np.clip((F(0.015) - p[:, 1]) / F(0.03), 0, 1)).astype(F) * F(1.25)
        h = np.maximum(k - np.abs(d - neck), F(0)) / k
        return np.minimum(d, neck) - h * h * h * k / F(6.0)

    def _eye(self, p, d, sx):
        R, Rs = self.R, self.Rs
        e = self.eyes[sx]
        # Socket under the brow, then room for the eyeball.
        d = carve(d, ellipsoid(p, e + np.asarray((sx * 0.0005, 0.0145, 0.001)), (0.0168, 0.0115, 0.0128)), 0.01)
        d = carve(d, sphere(p, e, R + 0.0004), 0.0006)
        # Lids: a shell over the eyeball blending into brow and cheek, opened in an almond.
        d = smin(d, sphere(p, e, Rs), 0.0065)
        d = smin(d, self._lid_tube(p, -0.002, Rs - 0.0003, 0.0009, sx, upper=False, span=(0.12, 0.95)), 0.0025)  # lower roll
        d = carve(d, self.opening(p, sx), 0.0006)
        d = carve(d, self._lid_tube(p, 0.0048 + 0.0008 * self.fem, Rs + 0.0006, 0.00085, sx, span=(0.12, 0.9)), 0.0014)  # crease
        return smin(d, self.caruncle(p, sx), 0.0008)

    def caruncle(self, p, sx):
        e = self.eyes[sx]
        c = e + np.asarray((sx * (self.u_in + 0.0014), 0.0042, -0.0003))
        return ellipsoid(p, c, (0.0017, 0.0016, 0.0014))

    def _nose(self, p, d):
        n, at = self.n, self.nose_at
        nose = round_cone(p, at(0, 0.085, 0.003), at(0, 0.0985, -0.0275), 0.0046 * n, 0.0056 * n)         # dorsum
        nose = smin(nose, round_cone(p, at(0, 0.077, 0.0), at(0, 0.0875, -0.037), 0.0075 * n, 0.0102 * n), 0.006)  # sidewalls
        nose = smin(nose, ellipsoid(p, at(0, 0.1007, -0.0352), (0.0069 * n, 0.0078 * n, 0.0074 * n)), 0.008)  # tip
        for sx in (1, -1):
            nose = smin(nose, sphere(p, at(sx * 0.0036, 0.1028, -0.0366), 0.0052 * n), 0.004)            # lobules
            nose = smin(nose, ellipsoid(p, at(sx * 0.0122, 0.0928, -0.0442), (0.0095 * n, 0.0041 * n, 0.0048 * n),
                                        _basis((0.42 * sx, 1.0, -0.12), (0, 0, 1))), 0.01)               # wings
        nose = smin(nose, capsule(p, at(0, 0.0985, -0.0432), at(0, 0.0928, -0.0495), 0.0027 * n), 0.003)    # columella
        d = smin(d, nose, 0.009)
        a = self.anchors
        for i, sx in enumerate((1, -1)):
            if a is not None:
                # Alar crease round the back of each wing (sunk into the surface), then the nostril, opening below.
                out = np.asarray((sx, 0.25, 0.0)) / math.hypot(1, 0.25)
                radii = [0.0003, 0.0006, 0.0004]
                d = carve(d, tube(p, [c - out * r * 0.3 for c, r in zip(a["alar"][3 * i:3 * i + 3], radii)], radii), 0.0018)
                d = carve(d, ellipsoid(p, a["nostril"][i] - NOSTRIL_NORMAL * 0.0007, (0.0037 * n, 0.0018 * n, 0.0019 * n),
                                       _basis((-0.3 * sx, 0.92, 0.4), NOSTRIL_NORMAL)), 0.0012)
            if self.old:           # nasolabial folds deepen with age
                d = carve(d, tube(p, [at(sx * 0.019, 0.088, -0.04), (sx * 0.026, 0.083, -0.056), (sx * 0.0305, 0.0765, -0.072)],
                                  [0.0008, 0.0016, 0.0008]), 0.005)
        return d

    def lips(self, p):
        """The lips' own masses (also where the painter puts their colour): the upper lip with its tubercle and the
        cupid's bow (two peaks under the philtrum columns, a notch between them) and the fuller lower lip, both
        following the dental arch and thinning toward the corners of the mouth."""
        fem = self.fem
        full = 1.0 + 0.18 * fem - 0.22 * self.old
        q = np.asarray(p, dtype=F).copy()
        q[:, 1] += F(14.0) * q[:, 0] * q[:, 0]
        thin = F(1) + F(0.45) * (q[:, 0] / F(0.026)) ** 2
        q[:, 1] = F(0.09) + (q[:, 1] - F(0.09)) * thin
        q[:, 2] = F(-0.07) + (q[:, 2] + F(0.07)) * thin
        upper = ellipsoid(q, (0, 0.0912, -0.0652), (0.0275 - 0.0015 * fem, 0.0068, 0.005 * full))
        upper = smin(upper, ellipsoid(q, (0, 0.0952, -0.0665), (0.0062, 0.004, 0.0038 * full)), 0.003)
        for sx in (1, -1):
            upper = smin(upper, ellipsoid(q, (sx * 0.0055, 0.0932, -0.0624), (0.0048, 0.004, 0.0026 * full)), 0.0025)
        upper = carve(upper, ellipsoid(q, (0, 0.094, -0.0578), (0.0024, 0.007, 0.0018)), 0.0012)
        lower = ellipsoid(q, (0, 0.089, -0.0745), (0.024 - 0.0015 * fem, 0.0078, 0.006 * full))
        return smin(upper, lower, 0.002)

    def _mouth(self, p, d):
        d = smin(d, self.lips(p), 0.0045)
        for sx in (1, -1):    # philtrum columns
            d = smin(d, round_cone(p, (sx * 0.0046, 0.0937, -0.0598), (sx * 0.0037, 0.0926, -0.0535), 0.0013, 0.0008), 0.0028)
        a = self.anchors
        if a is not None:
            # A rounded groove between the lips, the dimples at the corners and the philtrum's groove, each sunk just
            # into the surface found for it.
            xs = a["mouth"][:, 0]
            radii = 0.00075 * (0.45 + 0.55 * np.cos(xs / 0.0205 * np.pi / 2))
            d = carve(d, tube(p, list(a["mouth"] - np.stack([0 * radii, radii * 0.35, 0 * radii], 1)), list(radii)), 0.0014)
            pr = np.linspace(0.001, 0.0005, len(a["philtrum"]))
            d = carve(d, tube(p, list(a["philtrum"] - np.stack([0 * pr, pr * 0.5, 0 * pr], 1)), list(pr)), 0.0018)
        d = carve(d, ellipsoid(p, (0, 0.0965, -0.0852), (0.015, 0.0055, 0.0038)), 0.006)                     # under the lower lip
        return d


def E_adam(p, neck_r):
    """The larynx (Adam's apple) a third of the way down the front of a man's neck."""
    return ellipsoid(p, (0, neck_r * 0.62, -0.135), (0.0065, 0.0062, 0.0105))


def mesh(shape, h_local=0.001, target=14000, name="head", bounds=((-0.1, -0.13, -0.3), (0.1, 0.14, 0.13))):
    """The head mesh (head-local coordinates): sampled, meshed, snapped onto the field and reduced to about
    ``target`` triangles, keeping detail where the surface bends."""
    from . import fieldmesh
    return fieldmesh.build(shape.field, bounds, h_local, target, name, shape.key(), ("headshape.py", "earshape.py"), "heads")
