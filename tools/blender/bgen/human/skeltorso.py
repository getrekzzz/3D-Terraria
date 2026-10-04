"""The skeleton's trunk as distance fields in metres (frame coordinates): the vertebral column, the ribcage with its
costal cartilages and sternum, the pelvis (sacrum, coccyx and the two hip bones), and each shoulder's clavicle and
scapula. Each ``*_field(frame)`` returns (field, bounds) for fieldmesh; right-side parts are mirrored for the left.
"""
import math

import numpy as np

from .bonefield import Body, Part, ball, basis, capsule, catmull, cone, ell, flat_tube, plate, rod, tube
from .sdf import F

RIB_RX = (0.058, 0.08, 0.095, 0.105, 0.112, 0.117, 0.119, 0.119, 0.116, 0.111)
RIB_END = tuple(math.pi * a for a in (0.86, 0.83, 0.81, 0.8, 0.79, 0.78, 0.77, 0.72, 0.67, 0.62))
RIB_DROP = (0.014, 0.03, 0.044, 0.055, 0.063, 0.069, 0.073, 0.074, 0.071, 0.066)
STERNAL_Z = (1.395, 1.371, 1.349, 1.328, 1.308, 1.288, 1.268)


def _ss(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


class Column:
    """The vertebral column: one level per vertebra from the fifth lumbar up to the atlas, on a curve bowed forward
    in the lumbar and cervical spine and back in the thoracic spine. Levels are (kind, centre, rot, (rx, ry, h))
    with rot's columns: up the column, backward, to the right."""

    def __init__(self, frame):
        j, s = frame.j, frame.s
        self.s = s
        head = j["head"]
        ctrl = [(1.0 * s, j["pelvis"].y - 0.04 * s), (1.09 * s, j["spine"].y - 0.018 * s), (1.18 * s, j["chest"].y - 0.028 * s),
                (1.3 * s, j["chest"].y - 0.05 * s), (1.425 * s, j["neck"].y - 0.026 * s),
                (1.505 * s, j["neck"].y * 0.55 + head.y * 0.45 - 0.013 * s), (head.z - 0.002 * s, head.y)]
        curve = catmull([(0.0, y, z) for z, y in ctrl], 8)
        self._z, self._y = curve[:, 2], curve[:, 1]
        zs = [("lumbar", z) for z in np.linspace(1.03, 1.17, 5)] + [("thoracic", z) for z in np.linspace(1.2, 1.425, 12)]
        zs += [("cervical", z) for z in np.linspace(1.447, 1.553, 6)]
        self.levels = []
        for i, (kind, z) in enumerate(zs):
            if kind == "lumbar":
                size = (0.023 + 0.001 * (i == 0), 0.0165, 0.0115)
            elif kind == "thoracic":
                k = (i - 5) / 11
                size = (0.019 - 0.0055 * k, 0.0155 - 0.0045 * k, 0.0085 - 0.0017 * k)
            else:
                size = (0.0128, 0.0095, 0.0058 + 0.002 * (z < 1.45))
            self.levels.append((kind, self.at(z * s), self.frame_at(z * s), tuple(v * s for v in size)))
        self.atlas = self.at(head.z - 0.002 * s)

    def y(self, z):
        return float(np.interp(z, self._z, self._y))

    def at(self, z):
        return np.asarray((0.0, self.y(z), z))

    def frame_at(self, z):
        e = 0.01 * self.s
        up = self.at(z + e) - self.at(z - e)
        return basis(up, (0, -1, 0))

    def thoracic(self, k):
        """The k-th thoracic vertebra (1 at the top)."""
        return self.levels[5 + 12 - k]


def _local(c, rot):
    return lambda u, v=0.0, w=0.0: c + rot @ np.asarray((u, v, w))


def column_field(frame):
    col = Column(frame)
    s = frame.s

    def field(p):
        b = Body(p, 0.01 * s)
        prev = None
        for kind, c, rot, (rx, ry, h) in col.levels:
            L = _local(c, rot)
            b.add(rod(c, rot, rx, h, 0.0035 * s, ry=ry), 0.002 * s)
            if prev is not None:                                  # the disc below
                pc, ph, prx, pry = prev
                gap_c = (pc + c) / 2
                half = np.linalg.norm(c - pc) / 2 - (h + ph) / 2 + 0.0012 * s
                b.add(rod(gap_c, rot, min(rx, prx) * 0.86, max(half + (h + ph) / 2 * 0.3, 0.002 * s), 0.0015 * s,
                          ry=min(ry, pry) * 0.86), 0.002 * s)
            else:                                                  # down onto the sacrum
                b.add(rod(L(-h - 0.006 * s), rot, rx * 0.86, 0.007 * s, 0.0015 * s, ry=ry * 0.86), 0.002 * s)
            prev = (c, h, rx, ry)
            _arch(b, kind, L, rx, ry, h, s, top=kind == "cervical" and c[2] > 1.55 * s, c7=kind == "cervical" and c[2] < 1.45 * s)
        L = _local(col.atlas, col.frame_at(col.atlas[2]))
        for sx in (1, -1):                                         # the atlas: a ring on two lateral masses
            b.add(ell(L(0, 0, sx * 0.014 * s), (0.0065 * s, 0.009 * s, 0.0055 * s), col.frame_at(col.atlas[2])), 0.003 * s)
            b.add(tube([L(0, 0, sx * 0.014 * s), L(0, -0.009 * s, sx * 0.007 * s), L(0, -0.0115 * s, 0)], [0.0034 * s] * 3), 0.003 * s)
            b.add(tube([L(0, 0.002 * s, sx * 0.014 * s), L(0, 0.014 * s, sx * 0.011 * s), L(0.001 * s, 0.021 * s, 0)],
                       [0.0034 * s, 0.003 * s, 0.0034 * s]), 0.003 * s)
            b.add(cone(L(0, 0, sx * 0.016 * s), L(0.002 * s, 0.003 * s, sx * 0.032 * s), 0.0042 * s, 0.003 * s), 0.003 * s)
        b.add(cone(L(-0.026 * s), L(-0.004 * s, -0.004 * s), 0.0048 * s, 0.0036 * s), 0.003 * s)   # the dens of the axis
        return b.d

    lo = (-0.07 * s, min(col._y) - 0.09 * s, 0.98 * s)
    hi = (0.07 * s, max(col._y) + 0.04 * s, frame.j["head"].z + 0.02 * s)
    return field, (lo, hi), col


def _arch(b, kind, L, rx, ry, h, s, top=False, c7=False):
    """A vertebra's neural arch round the canal behind its body: pedicles and laminae, the spinous process, the
    transverse processes and the articular processes that link it to its neighbours."""
    for sx in (1, -1):
        w = lambda x: sx * x
        if kind == "lumbar":
            b.add(tube([L(0, ry * 0.7, w(rx * 0.55)), L(-0.001 * s, ry + 0.011 * s, w(0.0085 * s)), L(-0.002 * s, ry + 0.017 * s, 0)],
                       [0.0058 * s, 0.0056 * s, 0.0055 * s]), 0.004 * s)
            b.add(cone(L(0.001 * s, ry + 0.006 * s, w(0.011 * s)), L(0.003 * s, ry + 0.009 * s, w(0.036 * s)), 0.0045 * s, 0.0026 * s), 0.003 * s)
            b.add(capsule(L(-0.0165 * s, ry + 0.012 * s, w(0.0115 * s)), L(0.0165 * s, ry + 0.012 * s, w(0.0115 * s)), 0.0036 * s), 0.003 * s)
        elif kind == "thoracic":
            b.add(tube([L(0, ry * 0.7, w(rx * 0.55)), L(0, ry + 0.009 * s, w(0.007 * s)), L(-0.001 * s, ry + 0.014 * s, 0)],
                       [0.0036 * s, 0.0034 * s, 0.0034 * s]), 0.003 * s)
            b.add(cone(L(0.001 * s, ry + 0.004 * s, w(0.008 * s)), L(0.003 * s, ry + 0.016 * s, w(0.027 * s)), 0.004 * s, 0.0033 * s), 0.003 * s)
            b.add(capsule(L(-0.0105 * s, ry + 0.008 * s, w(0.009 * s)), L(0.0105 * s, ry + 0.008 * s, w(0.009 * s)), 0.003 * s), 0.003 * s)
        else:
            b.add(tube([L(0, ry * 0.6, w(rx * 0.7)), L(0, ry + 0.007 * s, w(0.007 * s)), L(0, ry + 0.011 * s, 0)],
                       [0.0029 * s] * 3), 0.0025 * s)
            b.add(cone(L(0, ry * 0.1, w(rx * 0.9)), L(0.001 * s, ry * 0.1, w(rx + 0.0105 * s)), 0.0034 * s, 0.0028 * s), 0.0025 * s)
            b.add(capsule(L(-0.0105 * s, ry + 0.004 * s, w(0.0128 * s)), L(0.0105 * s, ry + 0.004 * s, w(0.0128 * s)), 0.0046 * s), 0.004 * s)
    if kind == "lumbar":
        rot = basis(L(0, 1, 0) - L(0), L(1, 0, 0) - L(0))
        b.add(ell(L(-0.004 * s, ry + 0.027 * s, 0), (0.0145 * s, 0.0098 * s, 0.004 * s), rot), 0.006 * s)
    elif kind == "thoracic":
        b.add(cone(L(0, ry + 0.012 * s), L(-0.024 * s, ry + 0.034 * s), 0.0042 * s, 0.0028 * s), 0.003 * s)
    else:
        tip = (-0.013 * s, ry + 0.033 * s) if c7 else (-0.006 * s, ry + 0.02 * s)
        b.add(cone(L(0, ry + 0.009 * s), L(tip[0], tip[1]), 0.0032 * s, 0.0034 * s if c7 else 0.0026 * s), 0.0025 * s)


# ---------------------------------------------------------------------------------------------- ribcage
def sternum_y(frame, z):
    s = frame.s
    return frame.j["chest"].y + 0.062 * s + (1.4 * s - z) * 0.11


def ribcage_field(frame, col):
    s = frame.s
    j = frame.j
    paths = _rib_paths(frame, col)

    def field(p):
        b = Body(p, 0.01 * s)
        for sx in (1, -1):
            m = np.asarray((sx, 1, 1))
            for rib, cart in paths:
                pts = rib * m
                n = len(pts)
                radii = [0.0034 * s, 0.0031 * s] + [0.0028 * s + 0.0004 * s * i / (n - 3) for i in range(n - 2)]
                b.add(flat_tube(list(pts), radii, (0, 0, 1), 0.5), 0.002 * s)
                c = [q * m for q in cart]
                b.add(flat_tube(c, [0.003 * s, 0.0028 * s, 0.0027 * s], (0, 0, 1), 0.62), 0.003 * s)
        zt, zb = 1.405 * s, 1.255 * s
        b.add(ell((0, sternum_y(frame, 1.385 * s), 1.383 * s), (0.026 * s, 0.0068 * s, 0.019 * s)), 0.004 * s)   # manubrium
        b.cut(ball((0, sternum_y(frame, zt), zt + 0.002 * s), 0.0075 * s), 0.003 * s)                                  # jugular notch
        body = [(0, sternum_y(frame, z), z) for z in (1.366 * s, 1.31 * s, zb)]
        b.add(flat_tube(body, [0.0135 * s, 0.0158 * s, 0.0138 * s], (0, 1, 0), 2.1), 0.003 * s)
        b.add(cone((0, sternum_y(frame, zb), zb), (0, sternum_y(frame, zb) + 0.003 * s, zb - 0.027 * s), 0.0062 * s, 0.0032 * s),
              0.003 * s)                                                                                               # xiphoid
        return b.d

    lo = (-0.14 * s, j["chest"].y - 0.12 * s, 1.17 * s)
    hi = (0.14 * s, j["chest"].y + 0.105 * s, 1.45 * s)
    return field, (lo, hi)


def _rib_paths(frame, col):
    """Right-side ribs: [(centre line points (head, tubercle, shaft), cartilage points)]."""
    s = frame.s
    out, ends, rys = [], [], []
    for k in range(1, 11):
        _, c, _, (rbx, _, _) = col.thoracic(k)
        yk, zk = c[1], c[2]
        rx, a1, drop = RIB_RX[k - 1] * s, RIB_END[k - 1], RIB_DROP[k - 1] * s
        y_back = yk - 0.04 * s
        if k <= 7:
            zst = STERNAL_Z[k - 1] * s
            ry = (sternum_y(frame, zst) - 0.011 * s - y_back) / (1 - math.cos(a1))
        else:
            ry = rys[6] * (0.99, 0.97, 0.94)[k - 8]
        rys.append(ry)
        a0 = math.asin(min(0.045 * s / rx, 0.9))
        yc = y_back + ry
        back = lambda a: math.copysign(abs(math.cos(a)) ** (0.6 if a < math.pi / 2 else 1.0), math.cos(a))   # a flat back
        shaft = [(math.sin(a) * rx, yc - ry * back(a), zk + 0.007 * s - drop * ((1 - math.cos(a)) / 2) ** 1.3)
                 for a in np.linspace(a0, a1, 10)]
        rib = np.asarray([(rbx * 1.02, yk - 0.006 * s, zk + 0.003 * s), (0.028 * s, yk - 0.03 * s, zk + 0.005 * s)] + shaft)
        end = rib[-1]
        if k <= 7:
            st = np.asarray((0.012 * s, sternum_y(frame, zst) - 0.003 * s, zst))
        else:
            pe, ps = ends[k - 2]
            st = pe + (ps - pe) * 0.42
        ends.append((end, st))
        mid = (end + st) / 2 + np.asarray((0.004 * s, 0.006 * s, 0.0))
        out.append((rib, [end, mid, st]))
    return out
