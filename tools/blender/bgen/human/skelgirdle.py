"""The skeleton's girdles as distance fields in metres (frame coordinates): the pelvis (sacrum and coccyx between two
hip bones, each an iliac blade flaring to its crest, the acetabulum cupping the femoral head, and the pubic and
ischial rami round the obturator opening), and the right shoulder's clavicle and scapula (blade, spine and acromion
over the joint, coracoid, and the glenoid cupping the humeral head). Each returns (field, bounds).
"""
import math

import numpy as np

from .bonefield import Body, Part, ball, basis, cone, ell, flat_tube, plate, tube
from .sdf import F, smax
from .skeltorso import sternum_y

CREST = (1.12, 2.62)          # the iliac crest's span round the pelvis (radians from the front)


def _ilium_sections(frame, z):
    s = frame.s
    wide = frame.j["hip.R"].x - 0.088 * s
    dz = z - 0.95 * s
    return 0.093 * s + wide + dz * 0.26, 0.058 * s + dz * 0.2, -0.006 * s


def _crest_z(s, phi):
    t = np.clip((phi - CREST[0]) / (CREST[1] - CREST[0]), 0, 1)
    return 1.032 * s + 0.034 * s * np.maximum(np.sin(np.pi * t), 0) ** 0.8


def _on_blade(frame, phi, z):
    rx, ry, yc = _ilium_sections(frame, z)
    r = 1 / math.sqrt((math.sin(phi) / rx) ** 2 + (math.cos(phi) / ry) ** 2)
    return np.asarray((r * math.sin(phi), yc + r * math.cos(phi), z))


def _ilium(frame, sx):
    """The iliac blade: a thin shell round an elliptic cone widening upward, cut to the crest, its front and back
    borders and the line where it narrows into the acetabulum."""
    s = frame.s

    def fn(p):
        x, y, z = p[:, 0] * F(sx), p[:, 1], p[:, 2]
        rx, ry, yc = _ilium_sections(frame, z)
        qy = y - F(yc)
        k0 = np.sqrt((x / rx) ** 2 + (qy / ry) ** 2)
        k1 = np.sqrt((x / (rx * rx)) ** 2 + (qy / (ry * ry)) ** 2)
        shell = np.abs(k0 * (k0 - 1) / np.maximum(k1, F(1e-9))) - F(0.0032 * s)
        phi = np.arctan2(x, qy)
        rr = np.sqrt(x * x + qy * qy)
        bottom = 0.948 * s + 0.03 * s * np.clip((phi - 1.75) / 0.75, -1, 1) ** 2
        cut = np.maximum.reduce([z - _crest_z(s, phi), F(bottom) - z, (phi - F(CREST[1])) * rr, (F(CREST[0]) - phi) * rr])
        return smax(shell, cut.astype(F), 0.003 * s)
    lo, hi = (0.02 * s, -0.11 * s, 0.93 * s), (0.15 * s, 0.08 * s, 1.08 * s)
    if sx < 0:
        lo, hi = (-hi[0], lo[1], lo[2]), (-lo[0], hi[1], hi[2])
    return Part(fn, lo, hi)


def pelvis_field(frame, col):
    j, s = frame.j, frame.s
    ys = col.y(1.0 * s)

    def field(p):
        b = Body(p, 0.01 * s)
        for sx in (1, -1):
            m = np.asarray((sx, 1, 1))
            H = np.asarray(j["hip.R" if sx > 0 else "hip.L"])
            b.add(_ilium(frame, sx), 0.004 * s)
            crest = [_on_blade(frame, phi, float(_crest_z(s, phi)) - 0.002 * s) * m for phi in np.linspace(*CREST, 12)]
            b.add(tube(crest, [0.0046 * s] * 12), 0.005 * s)                                         # iliac crest
            asis, psis = crest[0], crest[-1]
            aiis = _on_blade(frame, CREST[0], 0.988 * s) * m
            b.add(ball(asis, 0.0058 * s), 0.004 * s)
            b.add(ball(psis, 0.006 * s), 0.004 * s)
            b.add(tube([asis, aiis, H + np.asarray((-0.008 * sx, 0.022, 0.014)) * s], [0.0045 * s, 0.005 * s, 0.006 * s]), 0.005 * s)
            b.add(tube([psis, np.asarray((sx * 0.044, -0.074, 0.982)) * s, np.asarray((sx * 0.055, -0.064, 0.958)) * s,
                        np.asarray((sx * 0.06, -0.046, 0.928)) * s], [0.0055 * s, 0.005 * s, 0.0045 * s, 0.004 * s]), 0.005 * s)
            n = np.asarray((sx, 0.35, -0.45)) / np.linalg.norm((1, 0.35, -0.45))
            b.add(ball(H - n * 0.004 * s, 0.033 * s), 0.008 * s)                                     # acetabulum
            b.add(tube([H + np.asarray((-0.012 * sx, 0.022, -0.006)) * s, np.asarray((sx * 0.04, 0.05, 0.915)) * s,
                        np.asarray((sx * 0.012, 0.058, 0.902)) * s], [0.0085 * s, 0.007 * s, 0.0075 * s]), 0.006 * s)  # pubis
            b.add(tube([np.asarray((sx * 0.012, 0.055, 0.882)) * s, np.asarray((sx * 0.03, 0.035, 0.868)) * s,
                        np.asarray((sx * 0.05, 0.005, 0.858)) * s], [0.0065 * s, 0.0055 * s, 0.0065 * s]), 0.005 * s)
            b.add(ell(np.asarray((sx * 0.058, -0.012, 0.862)) * s, (0.011 * s, 0.016 * s, 0.012 * s)), 0.006 * s)  # tuberosity
            b.add(tube([np.asarray((sx * 0.058, -0.016, 0.866)) * s, np.asarray((sx * 0.068, -0.022, 0.895)) * s,
                        H + np.asarray((-0.016 * sx, -0.022, -0.012)) * s], [0.009 * s, 0.0085 * s, 0.01 * s]), 0.006 * s)  # ischium
            b.add(cone(np.asarray((sx * 0.06, -0.04, 0.925)) * s, np.asarray((sx * 0.046, -0.052, 0.919)) * s, 0.004 * s, 0.0024 * s),
                  0.003 * s)
        b.add(ell((0, 0.058 * s, 0.893 * s), (0.016 * s, 0.009 * s, 0.016 * s)), 0.006 * s)              # symphysis
        for sx in (1, -1):
            H = np.asarray(j["hip.R" if sx > 0 else "hip.L"])
            n = np.asarray((sx, 0.35, -0.45)) / np.linalg.norm((1, 0.35, -0.45))
            b.cut(ball(H, 0.0247 * s), 0.0015 * s)
            b.cut(Part(lambda q, H=H, n=n: F(0.001 * s) - (q - H) @ n.astype(F), H - 0.04 * s, H + 0.04 * s), 0.004 * s)
        _sacrum(b, s, ys)
        return b.d

    lo, hi = (-0.155 * s, -0.115 * s, 0.84 * s), (0.155 * s, 0.085 * s, 1.085 * s)
    return field, (lo, hi)


def _sacrum(b, s, ys):
    """The sacrum, wedged between the iliac blades and leaning back down to the coccyx; its promontory carries the
    lumbar spine. Four pairs of foramina open on its front, a crest of bumps runs down its back."""
    S = lambda x, y, z: np.asarray((x * s, ys + y * s, z * s))
    al, ar, ml, mr, apex = S(-0.05, -0.016, 0.991), S(0.05, -0.016, 0.991), S(-0.031, -0.027, 0.952), S(0.031, -0.027, 0.952), S(0, -0.04, 0.912)
    b.add(plate(ar, al, ml, 0.0085 * s), 0.004 * s)
    b.add(plate(ar, ml, mr, 0.0085 * s), 0.004 * s)
    b.add(plate(mr, ml, apex, 0.0065 * s), 0.004 * s)
    b.add(ell(S(0, -0.004, 0.996), (0.021 * s, 0.0155 * s, 0.0115 * s), basis((0, 0.35, 1), (0, -1, 0))), 0.012 * s)  # promontory
    for sx in (1, -1):
        b.add(ell(S(sx * 0.033, -0.014, 0.99), (0.017 * s, 0.0105 * s, 0.011 * s)), 0.012 * s)                  # alae
    for k, (dy, dz, r) in enumerate(((0.001, -0.007, 0.0052), (0.004, -0.017, 0.0042), (0.008, -0.025, 0.0033))):
        b.add(ball(apex + np.asarray((0, dy, dz)) * s, r * s), 0.003 * s)                            # coccyx
    for z in (0.985, 0.967, 0.949, 0.931):
        t = (0.993 - z) / 0.081
        y = -0.014 - 0.026 * t
        b.add(ball(S(0, y - 0.0075, z), 0.0036 * s), 0.003 * s)                                     # median crest
        for sx in (1, -1):
            b.cut(ball(S(sx * (0.017 - 0.004 * t), y + 0.0075, z), 0.0033 * s), 0.0015 * s)          # foramina


# ---------------------------------------------------------------------------------------------- shoulder girdle
def clavicle_field(frame):
    """The right clavicle: an S-curved strut from the manubrium out to the acromion."""
    j, s = frame.j, frame.s
    p0 = np.asarray((0.02 * s, sternum_y(frame, 1.405 * s) - 0.002 * s, 1.402 * s))
    p4 = np.asarray(j["shoulder.R"]) + np.asarray((-0.013, 0.002, 0.028)) * s
    pts, radii = [], []
    for t in np.linspace(0, 1, 7):
        bow = 0.014 * math.sin(math.pi * t / 0.68) if t < 0.68 else -0.005 * math.sin(math.pi * (t - 0.68) / 0.32)
        pts.append(p0 + (p4 - p0) * t + np.asarray((0, bow * s, 0)))
        radii.append((0.0068 - 0.0045 * t + 0.0032 * t * t) * s)

    def field(p):
        b = Body(p, 0.008 * s)
        b.add(tube(pts, radii), 0.003 * s)
        b.add(ell(p0 + np.asarray((0.002, 0, 0)) * s, (0.0085 * s, 0.0075 * s, 0.008 * s)), 0.006 * s)
        return b.d
    return field, (tuple(np.minimum(p0, p4) - 0.025 * s), tuple(np.maximum(p0, p4) + 0.025 * s))


def scapula_field(frame):
    """The right scapula: a thin, slightly bent blade on the back of the ribs with thickened borders, the spine
    rising across it to the acromion over the shoulder joint, the coracoid hooking forward, the glenoid cup."""
    j, s = frame.j, frame.s
    sh = np.asarray(j["shoulder.R"])
    cy = j["chest"].y + 0.02 * s
    B = lambda x, y, z: np.asarray((x * s, cy + y * s, z * s))
    L = lambda x, y, z: sh + np.asarray((x, y, z)) * s
    sa, ia, mt, mb = B(0.064, -0.103, 1.432), B(0.086, -0.106, 1.272), B(0.1, -0.088, 1.418), B(0.1, -0.086, 1.33)
    la, g = L(-0.044, -0.038, -0.026), L(-0.0215, -0.006, 0.0)
    n = np.asarray((1, 0.25, 0)) / np.linalg.norm((1, 0.25, 0))

    def field(p):
        b = Body(p, 0.008 * s)
        for tri in ((sa, ia, mb), (sa, mb, mt), (mt, mb, la), (mb, ia, la)):
            b.add(plate(*tri, 0.0021 * s), 0.003 * s)
        b.add(tube([sa, ia], [0.003 * s, 0.0032 * s]), 0.003 * s)                                     # medial border
        b.add(tube([ia, (ia + la) / 2 + np.asarray((0.004, 0.004, 0)) * s, la], [0.0036 * s, 0.0044 * s, 0.0058 * s]), 0.004 * s)
        b.add(tube([sa, mt, L(-0.04, -0.03, 0.012)], [0.0028 * s, 0.0026 * s, 0.0034 * s]), 0.003 * s)   # superior border
        b.add(tube([B(0.066, -0.108, 1.398), B(0.1, -0.098, 1.41), L(-0.047, -0.064, 0.02), L(-0.024, -0.038, 0.032),
                    L(-0.012, -0.014, 0.034)], [0.0028 * s, 0.0042 * s, 0.0055 * s, 0.0058 * s, 0.0056 * s]), 0.004 * s)  # spine
        b.add(flat_tube([L(-0.03, -0.05, 0.03), L(-0.016, -0.024, 0.035), L(-0.007, -0.006, 0.0355), L(-0.004, 0.006, 0.034)],
                        [0.0045 * s, 0.0052 * s, 0.0052 * s, 0.0042 * s], (0, 0, 1), 1.7), 0.006 * s)            # acromion
        b.add(tube([L(-0.038, -0.03, 0.014), L(-0.032, 0.002, 0.024), L(-0.024, 0.018, 0.016)],
                   [0.0048 * s, 0.004 * s, 0.0034 * s]), 0.004 * s)                                  # coracoid
        b.add(cone(la, g - n * 0.004 * s, 0.0065 * s, 0.008 * s), 0.005 * s)                         # neck
        b.add(ell(g - n * 0.003 * s, (0.0055 * s, 0.0175 * s, 0.0115 * s), basis(n, (0, 0, 1))), 0.004 * s)  # glenoid
        b.cut(ball(sh, 0.0223 * s), 0.0015 * s)
        return b.d
    pts = np.asarray([sa, ia, mt, mb, la, g, L(-0.009, -0.004, 0.034), L(-0.024, 0.018, 0.016)])
    return field, (tuple(pts.min(0) - 0.02 * s), tuple(pts.max(0) + 0.02 * s))
