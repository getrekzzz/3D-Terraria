"""The skull as a distance field in head-local units (as headshape: origin at the head's centre, x to the right, y
forward, z up; multiply by Head.h for metres), meshed and cached with fieldmesh.

The vault matches the head's cranium (a little fuller, so caps made for heads sit on it). Carved into the face: deep
orbits under a modest brow, the pear-shaped nasal aperture under the nasal bones, the ear canals; built up: cheekbones
with free-standing zygomatic arches over the temporal hollows, the maxilla with its alveolar ridge and a row of
upper teeth, mastoids and the occipital condyles the atlas carries. The mandible (body, chin, rami, coronoid
processes and lower teeth) hangs from condyles seated under the temporal bones, a little open.
"""
import numpy as np

from . import fieldmesh
from .bonefield import Body, Part, ball, basis, capsule, cone, ell, flat_tube, plate, rot_x, tidy, tube
from .sdf import F, vec

# Upper dental arch (crown centres, right half) and the teeth along it: (width, depth, height) per tooth.
UPPER_ARCH = ((0.0, 0.0915), (0.0125, 0.0876), (0.0205, 0.0798), (0.0256, 0.0696), (0.0287, 0.0566), (0.0306, 0.0428),
              (0.0314, 0.029))
UPPER = ((0.0084, 0.0064, 0.0112), (0.0066, 0.006, 0.0102), (0.0076, 0.0074, 0.0118), (0.0068, 0.0084, 0.0092),
         (0.0066, 0.0086, 0.009), (0.0098, 0.0102, 0.0084), (0.0092, 0.0098, 0.008), (0.0086, 0.0094, 0.0076))
LOWER = ((0.0056, 0.0058, 0.0098), (0.006, 0.006, 0.0098), (0.007, 0.007, 0.011), (0.0068, 0.0078, 0.0088),
         (0.0068, 0.008, 0.0086), (0.0106, 0.0098, 0.0082), (0.0102, 0.0094, 0.0078), (0.0098, 0.009, 0.0074))
HINGE = np.asarray((0.0, -0.006, -0.038))
GAPE = np.radians(6.0)
ORBIT_AXIS = {sx: np.asarray((sx * 0.25, 1.0, 0.05)) / np.linalg.norm((0.25, 1.0, 0.05)) for sx in (1, -1)}


def orbit_centre(sx):
    return np.asarray((sx * 0.0335, 0.083, -0.001))


def glow_points():
    """Where the eye lights float, deep in each orbit (head-local)."""
    return {sx: orbit_centre(sx) - ORBIT_AXIS[sx] * 0.021 for sx in (1, -1)}


def _arch(points, n_out_centre, z, shrink=1.0, dy=0.0):
    """Teeth positions along a dental arch: returns a function (arc length) -> (centre, tangent, outward)."""
    pts = np.asarray([(x * shrink, n_out_centre + (y - n_out_centre) * shrink + dy, z) for x, y in points])
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    s = np.concatenate([[0.0], np.cumsum(seg)])

    def at(d):
        i = min(np.searchsorted(s, d, side="right") - 1, len(seg) - 1)
        t = (d - s[i]) / seg[i]
        c = pts[i] + (pts[i + 1] - pts[i]) * t
        tan = (pts[i + 1] - pts[i]) / seg[i]
        out = np.asarray((tan[1], -tan[0], 0.0))
        return c, tan, out
    return at, pts


def _teeth(body, at, sizes, sx, k=0.0008):
    d = 0.0
    for w, depth, height in sizes:
        c, tan, out = at(d + w / 2)
        c = c * np.asarray((sx, 1, 1))
        tan = tan * np.asarray((sx, 1, 1))
        out = out * np.asarray((sx, 1, 1))
        body.add(_box(c, np.stack([tan, out, (0, 0, 1)], 1), (w * 0.52, depth / 2, height / 2), min(w, depth) * 0.3), k)
        d += w


def _box(c, rot, half, rr):
    c, rotf, half = vec(c), vec(rot), vec(half)

    def fn(p):
        q = np.abs((p - c) @ rotf) - half + F(rr)
        return np.sqrt(np.einsum("ij,ij->i", np.maximum(q, 0), np.maximum(q, 0))) + np.minimum(q.max(1), 0) - F(rr)
    ext = float(np.linalg.norm(half))
    return Part(fn, np.asarray(c) - ext, np.asarray(c) + ext)


class Skull:
    def __init__(self, fem, atlas):
        self.fem = fem
        self.atlas = np.asarray(atlas, dtype=float)       # head-local centre of the atlas (the head bone's pivot)

    def key(self):
        return ("skull", self.fem, tuple(round(float(v), 5) for v in self.atlas))

    def field(self, p):
        fem = self.fem
        b = Body(p, 0.01)
        e = -0.003                          # the head's cranium less the scalp
        b.add(ell((0, -0.012, 0.022), (0.0755 + e, 0.095 + e, 0.086 + e)))
        b.add(ell((0, 0.034, 0.03 + 0.003 * fem), (0.061 + e, 0.05 + e, 0.058 + e)), 0.03)
        b.add(ell((0, -0.056, -0.008), (0.058 + e, 0.054 + e, 0.06 + e)), 0.03)
        for sx in (1, -1):
            b.cut(ell((sx * 0.084, 0.02, -0.006), (0.012, 0.042, 0.03)), 0.02)                 # temporal flattening
        b.add(ell((0, 0.052, -0.012), (0.056, 0.036, 0.034)), 0.02)                            # brow to cheeks
        b.add(ell((0, 0.063, -0.047), (0.037, 0.032, 0.024)), 0.018)                           # maxilla
        b.add(ell((0, 0.084, 0.014), (0.012, 0.007 + 0.002 * (1 - fem), 0.01)), 0.01)            # glabella
        b.add(ell((0, -0.016, -0.058), (0.032, 0.032, 0.014)), 0.016)                           # skull base
        for sx in (1, -1):
            b.add(capsule((sx * 0.012, 0.0825, 0.0195), (sx * 0.05, 0.0715, 0.0185), 0.0048 - 0.001 * fem), 0.008)  # brow
            b.add(ell((sx * 0.05, 0.064, -0.027), (0.0125, 0.0105, 0.0135), basis((sx * 0.8, 0.6, 0), (0, 0, 1))), 0.01)  # cheekbone
            b.add(cone((sx * 0.046, 0.06, -0.03), (sx * 0.031, 0.05, -0.052), 0.0062, 0.0055), 0.008)  # buttress
            b.add(ell((sx * 0.053, -0.03, -0.05), (0.0065, 0.009, 0.0115 - 0.002 * fem)), 0.012)  # mastoid
            b.add(ell((sx * 0.06, 0.0, -0.03), (0.0085, 0.011, 0.0062)), 0.008)               # articular eminence
            b.add(ell((sx * 0.056, -0.012, -0.03), (0.008, 0.016, 0.01)), 0.01)                # temporal bone
        for sx in (1, -1):
            o, n = orbit_centre(sx), ORBIT_AXIS[sx]
            rot = basis(np.cross(n, (0, 0, 1)), (0, 0, 1))
            b.cut(_box(o + n * 0.01, rot, (0.0205, 0.0188, 0.03), 0.0125), 0.005)               # orbit
            b.cut(cone(o - n * 0.008, o - n * 0.042, 0.016, 0.006), 0.004)
            b.cut(ell((sx * 0.071, 0.026, -0.014), (0.011, 0.02, 0.017)), 0.01)                # temporal fossa
            b.cut(ball((sx * 0.0625, -0.017, -0.031), 0.0042), 0.002)                          # ear canal
        b.cut(ell((0, 0.088, -0.0355), (0.0132, 0.03, 0.0135)), 0.003)                         # nasal aperture
        b.cut(ell((0, 0.09, -0.021), (0.0068, 0.03, 0.0125)), 0.003)
        b.add(cone((0, 0.0862, 0.007), (0, 0.0935, -0.0118), 0.0052, 0.0034), 0.004)          # nasal bones
        b.add(ell((0, 0.06, -0.031), (0.0013, 0.016, 0.012)), 0.002)                           # septum
        b.add(cone((0, 0.09, -0.0485), (0, 0.0985, -0.0495), 0.003, 0.0012), 0.003)             # nasal spine
        for sx in (1, -1):
            b.add(cone((sx * 0.0092, 0.0838, 0.004), (sx * 0.0158, 0.0858, -0.0225), 0.0036, 0.003), 0.004)
            b.add(flat_tube([(sx * 0.0565, 0.0635, -0.024), (sx * 0.0565, 0.066, 0.014)], [0.0042, 0.0038], (0, 1, 0), 0.55),
                  0.009)                                                                       # orbital rim
            b.add(flat_tube([(sx * 0.056, 0.056, -0.027), (sx * 0.0655, 0.026, -0.0305), (sx * 0.064, -0.002, -0.031)],
                            [0.0034, 0.0029, 0.0032], (0, 0, 1), 0.5), 0.008)                  # zygomatic arch
            b.add(ell((sx * 0.0135, self.atlas[1], self.atlas[2] + 0.0068), (0.0055, 0.0085, 0.0048)), 0.006)  # condyle
        at, pts = _arch(UPPER_ARCH, 0.045, -0.0525)
        for sx in (1, -1):
            mirror = pts * np.asarray((sx, 1, 1))
            b.add(flat_tube(list(mirror), [0.0048] * len(mirror), (0, 0, 1), 0.55), 0.006)      # alveolar ridge
        b.add(ell((0, 0.05, -0.0585), (0.025, 0.032, 0.0055)), 0.006)                          # palate
        at, _ = _arch(UPPER_ARCH, 0.045, -0.0662)
        for sx in (1, -1):
            _teeth(b, at, UPPER, sx)
        jaw = self.mandible(p)
        return np.minimum(b.d, jaw)

    def mandible(self, p):
        """The lower jaw, built closed and turned open about the hinge through its condyles."""
        fem = self.fem
        q = ((p - vec(HINGE)) @ vec(rot_x(-GAPE)) + vec(HINGE)).astype(F)
        b = Body(q, 0.01)
        jx = 0.051 - 0.004 * fem
        border = [(jx, -0.007, -0.087), (jx - 0.006, 0.02, -0.097), (0.033, 0.053, -0.1025), (0.0165, 0.0775, -0.1055),
                  (0.0, 0.0865, -0.1065)]
        at, crest = _arch(UPPER_ARCH, 0.045, -0.0888, shrink=0.93, dy=-0.0035)
        for sx in (1, -1):
            m = np.asarray((sx, 1, 1))
            bd = [np.asarray(v) * m for v in border]
            b.add(tube(bd, [0.0068, 0.0066, 0.0064, 0.0066, 0.0068]), 0.004)                  # lower border
            cr = crest[::-1] * m
            b.add(flat_tube(list(cr), [0.0046] * len(cr), (0, 0, 1), 0.55), 0.005)             # alveolar crest
            top = [cr[0], cr[2], cr[3], cr[4], cr[6]]
            low = bd
            for i in range(len(top) - 1):                                                      # the body between
                b.add(plate(low[i], low[i + 1], top[i], 0.0046), 0.004)
                b.add(plate(low[i + 1], top[i + 1], top[i], 0.0046), 0.004)
            b.add(ell((sx * jx, -0.008, -0.085), (0.005, 0.009, 0.0075)), 0.006)               # angle
            b.add(flat_tube([(sx * (jx + 0.004), -0.006, -0.0385), (sx * (jx + 0.002), -0.002, -0.06), (sx * jx, -0.006, -0.084)],
                            [0.0038, 0.0052, 0.0054], (0, 1, 0), 0.36), 0.005)                 # ramus
            b.add(flat_tube([(sx * jx, 0.006, -0.074), (sx * (jx - 0.001), 0.017, -0.052), (sx * (jx - 0.002), 0.022, -0.04)],
                            [0.0048, 0.0032, 0.0015], (0, 1, 0), 0.42), 0.005)                 # coronoid
            b.add(ell((sx * (jx + 0.005), -0.006, -0.038), (0.0095, 0.0055, 0.0055)), 0.004)   # condyle
            b.add(ell((sx * 0.0155, 0.085, -0.1025), (0.0065, 0.0055, 0.0055)), 0.005)         # mental tubercle
        b.add(ell((0, 0.0875, -0.1005), (0.0165 - 0.004 * fem, 0.0072, 0.0095)), 0.006)       # chin
        at, _ = _arch(UPPER_ARCH, 0.045, -0.0765, shrink=0.93, dy=-0.0035)
        for sx in (1, -1):
            _teeth(b, at, LOWER, sx)
        return b.d

    def shade(self, q):
        """Darkening (0 dark .. 1 bone) of the orbits' depths and the nasal cavity, per head-local point (N, 3)."""
        q = np.asarray(q, dtype=float)
        k = np.ones(len(q))
        for sx in (1, -1):
            o, n = orbit_centre(sx), ORBIT_AXIS[sx]
            r = q - o
            depth = -(r @ n)
            lat = np.linalg.norm(r - np.outer(r @ n, n), axis=1)
            k *= 1 - 0.78 * np.clip((depth - 0.003) / 0.012, 0, 1) * np.clip((0.026 - lat) / 0.006, 0, 1) * (depth < 0.05)
        r = (q - (0, 0.088, -0.032)) / (0.014, 0.03, 0.02)
        k *= 1 - 0.75 * np.clip((0.086 - q[:, 1]) / 0.008, 0, 1) * np.clip((1.1 - np.linalg.norm(r, axis=1)) / 0.2, 0, 1)
        x, y, z = q[:, 0], q[:, 1], q[:, 2]
        wig = lambda a, f: 0.0014 * np.sin(a * f) + 0.0007 * np.sin(a * f * 2.7 + 1.3)
        lines = [  # (distance from the suture's line, where it runs)
            (np.abs(y - (0.034 - 0.12 * z * z / 0.11) - wig(x, 260)), (z > 0.012) & (np.abs(x) < 0.07)),            # coronal
            (np.abs(x - wig(y, 300)), (z > 0.05) & (y < 0.03) & (y > -0.072)),                                    # sagittal
            (np.abs(y + 0.072 + 0.0 * x - 0.55 * np.abs(x) + 0.32 * np.maximum(0.06 - z, 0) - wig(x, 240)),
             (z > -0.035) & (y < -0.04)),                                                                        # lambdoid
        ]
        for d, m in lines:
            k *= 1 - 0.2 * m * np.clip(1 - d / 0.0042, 0, 1) ** 1.5
        return k

    def teeth(self, q):
        """How much each head-local point belongs to a tooth crown (0..1)."""
        q = np.asarray(q, dtype=float)
        band = lambda z, top, bottom: np.clip((top - z) / 0.0012, 0, 1) * np.clip((z - bottom) / 0.0012, 0, 1)
        upper = band(q[:, 2], -0.0612, -0.0735)
        closed = (q - HINGE) @ rot_x(-GAPE) + HINGE
        lower = band(closed[:, 2], -0.0705, -0.0806)
        return np.maximum(upper, lower) * (q[:, 1] > 0.016) * (np.abs(q[:, 0]) < 0.036)


BOUNDS = ((-0.095, -0.128, -0.13), (0.095, 0.118, 0.125))


def mesh(head, fem):
    """The skull and jaw (one mesh, head-local units scaled to the head in world space)."""
    skull = Skull(fem, head.local(head.frame.j["head"]))
    obj = tidy(fieldmesh.build(skull.field, BOUNDS, 0.0009, 12000, "skull", skull.key(), ("bonefield.py", "skulls.py"),
                               "skeleton", min_piece=200))
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", (co.reshape(-1, 3) * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return obj, skull
