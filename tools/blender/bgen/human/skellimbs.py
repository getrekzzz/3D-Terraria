"""The skeleton's right limbs as distance fields in metres (frame coordinates), each built round the rig joints so
that it turns about them without parting from its neighbours: rounded joint surfaces (humeral and femoral heads,
the elbow's spool, the knee's condyles, the talus dome, knuckles) are centred on the joints they turn about.

Humerus, radius and ulna, the hand (carpals, metacarpals, phalanges); femur with the kneecap, tibia and fibula, the
foot (talus, heel, midfoot, metatarsals, toes). Each returns (field, bounds); the left side is mirrored.
"""
import numpy as np

from .bonefield import Body, Bone, ball, basis, capsule, cone, ell, flat_tube, rod, tube
from .hands import hand_axes


def _bounds(points, pad):
    pts = np.asarray(points, dtype=float)
    return tuple(pts.min(0) - pad), tuple(pts.max(0) + pad)


def humerus_field(frame):
    j, s = frame.j, frame.s
    a, e = np.asarray(j["shoulder.R"]), np.asarray(j["elbow.R"])
    B = Bone(a, e)
    L = B.length
    at = lambda u, y=0.0, z=0.0: B.at(u * L, y * s, z * s)

    def field(p):
        b = Body(p, 0.008 * s)
        b.add(ball(a, 0.0215 * s))                                                            # head
        b.add(ell(at(0.05, 0.003, 0.015), (0.022 * s, 0.0125 * s, 0.0105 * s), B.rot), 0.012 * s)  # greater tubercle
        b.add(ell(at(0.065, 0.013, 0.001), (0.012 * s, 0.0068 * s, 0.0068 * s), B.rot), 0.01 * s)   # lesser tubercle
        b.add(cone(at(0.06, 0, 0.005), at(0.22), 0.0165 * s, 0.0108 * s), 0.018 * s)
        b.add(cone(at(0.17), at(0.82, -0.001), 0.0106 * s, 0.0111 * s), 0.006 * s)                  # shaft
        b.add(cone(at(0.3, 0.002, 0.006), at(0.47, 0.002, 0.006), 0.0068 * s, 0.0052 * s), 0.008 * s)  # deltoid tuberosity
        b.add(ell(at(0.925, -0.003), (0.032 * s, 0.0095 * s, 0.022 * s), B.rot), 0.012 * s)        # flare to the condyles
        b.add(ell(at(0.968, -0.003, -0.023), (0.011 * s, 0.0062 * s, 0.0068 * s), B.rot), 0.009 * s)  # medial epicondyle
        b.add(ell(at(0.962, -0.002, 0.019), (0.01 * s, 0.0058 * s, 0.0055 * s), B.rot), 0.009 * s)    # lateral epicondyle
        b.add(ball(B.at(L, 0.002 * s, -0.008 * s), 0.0108 * s), 0.004 * s)                         # trochlea
        b.add(ball(B.at(L, 0.004 * s, 0.011 * s), 0.0094 * s), 0.004 * s)                          # capitulum
        b.cut(ell(at(0.95, -0.013, -0.002), (0.012 * s, 0.0055 * s, 0.0075 * s), B.rot), 0.003 * s)  # olecranon fossa
        return b.d
    return field, _bounds([a, e], 0.04 * s)


def forearm_field(frame):
    j, s = frame.j, frame.s
    e, w = np.asarray(j["elbow.R"]), np.asarray(j["wrist.R"])
    B = Bone(e, w)
    L = B.length
    at = lambda u, y=0.0, z=0.0: B.at(u * s, y * s, z * s)

    def field(p):
        b = Body(p, 0.008 * s)
        # Ulna: the olecranon hooks behind the humerus' trochlea, the shaft thins to a small head on the little
        # finger's side of the wrist.
        b.add(tube([at(-0.016, -0.015, -0.007), at(0.002, -0.017, -0.008), at(0.03, -0.01, -0.008), at(0.06, -0.008, -0.007)],
                   [0.0078 * s, 0.0088 * s, 0.0085 * s, 0.0074 * s]), 0.006 * s)
        b.add(ball(at(0.018, 0.007, -0.008), 0.0058 * s), 0.006 * s)                               # coronoid
        b.add(cone(at(0.06, -0.008, -0.007), B.at(L - 0.012 * s, -0.011 * s, -0.004 * s), 0.0074 * s, 0.0052 * s), 0.005 * s)
        b.add(ball(B.at(L - 0.006 * s, -0.011 * s, -0.004 * s), 0.0066 * s), 0.004 * s)           # head
        b.add(cone(B.at(L - 0.006 * s, -0.013 * s, -0.003 * s), B.at(L + 0.001 * s, -0.014 * s, -0.001 * s), 0.003 * s, 0.002 * s),
              0.002 * s)
        # Radius: a disc-shaped head under the capitulum, a slender neck, then a shaft widening to the broad end
        # that carries the hand.
        b.add(rod(at(0.0165, 0.003, 0.011), B.rot, 0.0092 * s, 0.0048 * s, 0.002 * s), 0.002 * s)
        b.add(cone(at(0.018, 0.003, 0.011), at(0.045, 0.004, 0.0095), 0.0068 * s, 0.0056 * s), 0.004 * s)
        b.add(ball(at(0.05, 0.007, 0.006), 0.0055 * s), 0.005 * s)                                # tuberosity
        b.add(tube([at(0.045, 0.004, 0.0095), at(0.14, 0.007, 0.007), B.at(L - 0.03 * s, 0.008 * s, 0.003 * s)],
                   [0.0056 * s, 0.0065 * s, 0.0078 * s]), 0.006 * s)
        b.add(ell(B.at(L - 0.014 * s, 0.007 * s, 0.002 * s), (0.016 * s, 0.0108 * s, 0.0118 * s), B.rot), 0.008 * s)
        b.add(ball(B.at(L - 0.004 * s, 0.014 * s, 0.006 * s), 0.0042 * s), 0.004 * s)             # styloid
        b.cut(ball(e + B.ax(2) * 0.011 * s + B.ax(1) * 0.004 * s, 0.0098 * s), 0.0015 * s)       # seats for the condyles
        b.cut(ball(e + B.ax(2) * -0.008 * s + B.ax(1) * 0.002 * s, 0.0113 * s), 0.0015 * s)
        return b.d
    return field, _bounds([e, w], 0.035 * s)


FINGER_R = {"thumb": 1.12, "index": 1.0, "middle": 1.04, "ring": 0.96, "pinky": 0.84}


def hand_field(frame):
    """Carpals in two rows, a metacarpal to each knuckle, and the phalanges, each with a head centred on the joint
    it turns about."""
    j = frame.j
    k = frame.s * (0.94 if frame.female else 1.0)
    along, thumb, dorsal = (np.asarray(v) for v in hand_axes(frame, "R"))
    w = np.asarray(j["wrist.R"])
    H = lambda u, t, d: w + along * u * k + thumb * t * k + dorsal * d * k
    rot = np.stack([along, thumb, dorsal], 1)

    def field(p):
        b = Body(p, 0.006 * k)
        for t, d, r in ((0.014, 0.001, 0.0062), (0.002, 0.002, 0.0064), (-0.011, 0.001, 0.0058), (-0.014, -0.006, 0.0042)):
            b.add(ell(H(0.0085, t, d), (r * k, r * 0.95 * k, r * 0.8 * k), rot), 0.0028 * k)       # proximal row
        for t, d, r in ((0.0175, -0.002, 0.0058), (0.0075, 0.0015, 0.0052), (-0.002, 0.002, 0.0064), (-0.0125, 0.0015, 0.0058)):
            b.add(ell(H(0.0215, t, d), (r * k, r * 0.92 * k, r * 0.78 * k), rot), 0.0028 * k)      # distal row
        for f in ("index", "middle", "ring", "pinky"):
            g = FINGER_R[f]
            kn = np.asarray(j[f"{f}1.R"])
            base = H(0.031, (kn - w) @ thumb / k * 0.82, 0.001)
            b.add(ell(base, (0.0062 * g * k, 0.0058 * g * k, 0.005 * g * k), rot), 0.003 * k)
            mid = (base + kn) / 2 + dorsal * 0.0012 * k
            b.add(flat_tube([base, mid, kn], [0.0042 * g * k, 0.003 * g * k, 0.0042 * g * k], thumb, 0.82), 0.0025 * k)
            _finger(b, j, f, g, k, dorsal)                                                          # (above: metacarpal)
        tb = np.asarray(j["thumb1.R"])
        b.add(ell(tb - along * 0.004 * k, (0.0062 * k, 0.0055 * k, 0.005 * k), rot), 0.002 * k)   # trapezium
        _finger(b, j, "thumb", FINGER_R["thumb"], k, dorsal)
        return b.d

    pts = [w] + [j[f"{f}_tip.R"] for f in FINGER_R] + [j["thumb1.R"]]
    return field, _bounds(pts, 0.02 * k)


def _finger(b, j, f, g, k, dorsal):
    """A finger's phalanges (the thumb's metacarpal and two phalanges), each slimmer than the one before: a base
    broad across the finger just past the joint, a slim shaft flattened front to back, waisted and bowed a little
    toward the back of the hand, and a head of two knuckles side by side centred on the next joint; the last
    phalanx ends in a flat spade-shaped tuft."""
    pts = [np.asarray(j[f"{f}{n}.R"]) for n in (1, 2, 3)] + [np.asarray(j[f"{f}_tip.R"])]
    sizes = (1.2, 0.95, 0.8) if f == "thumb" else (1.0, 0.8, 0.68)
    knuckle = 0.0058 * g * k
    b.add(ball(pts[0], knuckle), 0.002 * k)                    # the head of the metacarpal
    prev = knuckle
    for i in range(3):
        a, c = pts[i], pts[i + 1]
        d = (c - a) / np.linalg.norm(c - a)
        rot = basis(d, dorsal)
        back, across = rot[:, 1], rot[:, 2]
        q = sizes[i] * g * k
        rb, rs, rh = 0.0056 * q, 0.0028 * q, 0.0043 * q          # base, shaft (front to back), head
        start = a + d * (prev * 0.8 + rb * 0.45)
        b.add(ell(start, (rb * 0.6, rb * 0.82, rb * 1.08), rot), 0.0012 * k)
        if i < 2:
            for side in (1, -1):                                  # the condyles
                b.add(ball(c + across * side * rh * 0.4 - d * rh * 0.1, rh * 0.66), 0.001 * k)
            end, r_end = c - d * rh * 0.55, rh * 0.62
        else:
            end, r_end = c - d * 0.0032 * q, rs * 0.85
            b.add(ell(end, (0.0033 * q, 0.0016 * q, 0.0033 * q), rot), 0.0015 * k)   # the tuft
        mid = (start + end) / 2 + back * rs * 0.3
        b.add(flat_tube([start, mid, end], [rb * 0.55, rs, r_end], across, 0.78), 0.0015 * k)
        prev = rh * 0.75


def femur_field(frame):
    j, s = frame.j, frame.s
    a, kn = np.asarray(j["hip.R"]), np.asarray(j["knee.R"])
    B = Bone(a, kn)
    L = B.length
    at = lambda u, y=0.0, z=0.0: B.at(u * s, y * s, z * s)

    def field(p):
        b = Body(p, 0.01 * s)
        b.add(ball(a, 0.0235 * s))                                                            # head
        b.add(cone(a, at(0.05, 0.004, 0.042), 0.0155 * s, 0.0165 * s), 0.01 * s)               # neck
        b.add(ell(at(0.032, -0.006, 0.055), (0.022 * s, 0.0145 * s, 0.0125 * s), B.rot), 0.012 * s)   # greater trochanter
        b.add(ball(at(0.078, -0.011, 0.02), 0.0085 * s), 0.008 * s)                             # lesser trochanter
        b.add(tube([at(0.065, 0.0, 0.037), at(0.2, 0.006, 0.02), at(0.36, 0.004, 0.004)],
                   [0.0152 * s, 0.0138 * s, 0.0142 * s]), 0.012 * s)                            # shaft
        b.add(ell(B.at(L - 0.036 * s, -0.002 * s, 0), (0.036 * s, 0.0175 * s, 0.026 * s), B.rot), 0.014 * s)
        for z in (-0.018, 0.0185):                                                           # condyles round the knee
            b.add(ell(B.at(L - 0.002 * s, -0.007 * s, z * s), (0.0195 * s, 0.0235 * s, 0.0165 * s), B.rot), 0.014 * s)
        b.cut(ell(B.at(L + 0.003 * s, -0.019 * s, 0), (0.017 * s, 0.013 * s, 0.0055 * s), B.rot), 0.004 * s)  # notch
        b.add(ell(B.at(L - 0.024 * s, 0.021 * s, 0.002 * s), (0.019 * s, 0.0075 * s, 0.0165 * s), B.rot), 0.007 * s)  # kneecap
        return b.d
    return field, _bounds([a, kn, a + np.asarray((0.07, 0, 0)) * s], 0.04 * s)


def shin_field(frame):
    j, s = frame.j, frame.s
    kn, an = np.asarray(j["knee.R"]), np.asarray(j["ankle.R"])
    B = Bone(kn, an)
    L = B.length
    at = lambda u, y=0.0, z=0.0: B.at(u * s, y * s, z * s)

    def field(p):
        b = Body(p, 0.01 * s)
        b.add(rod(at(0.03), B.rot, 0.023 * s, 0.0085 * s, 0.0065 * s, ry=0.035 * s), 0.006 * s)   # plateau
        b.add(ell(at(0.04, -0.002, 0), (0.02 * s, 0.022 * s, 0.032 * s), B.rot), 0.012 * s)
        b.add(ell(at(0.062, 0.017, 0.003), (0.016 * s, 0.007 * s, 0.009 * s), B.rot), 0.008 * s)  # tuberosity
        b.add(tube([at(0.045, 0.002), at(0.14, 0.004), at(0.3, 0.002, -0.002), B.at(L - 0.04 * s, 0, -0.004 * s)],
                   [0.0165 * s, 0.0125 * s, 0.0112 * s, 0.0118 * s]), 0.012 * s)
        b.add(capsule(at(0.08, 0.009), at(0.32, 0.007, -0.002), 0.0072 * s), 0.008 * s)           # the shin's crest
        b.add(ell(B.at(L - 0.026 * s, 0, -0.004 * s), (0.017 * s, 0.0155 * s, 0.0175 * s), B.rot), 0.012 * s)
        b.add(ell(B.at(L - 0.004 * s, 0.003 * s, -0.019 * s), (0.013 * s, 0.0068 * s, 0.0058 * s), B.rot), 0.005 * s)  # medial malleolus
        # Fibula: head under the outer plateau, a slender shaft, the lateral malleolus reaching lower than the medial.
        b.add(ball(at(0.05, -0.012, 0.029), 0.0082 * s), 0.004 * s)
        b.add(cone(at(0.05, -0.012, 0.029), B.at(L - 0.03 * s, -0.008 * s, 0.022 * s), 0.0055 * s, 0.0054 * s), 0.004 * s)
        b.add(ell(B.at(L + 0.002 * s, -0.006 * s, 0.022 * s), (0.017 * s, 0.0078 * s, 0.0064 * s), B.rot), 0.005 * s)
        b.cut(capsule(an - B.ax(2) * 0.008 * s, an + B.ax(2) * 0.007 * s, 0.0168 * s), 0.002 * s)  # the talus' seat
        return b.d
    return field, _bounds([kn, an], 0.045 * s)


def foot_field(frame):
    """Talus (a dome round the ankle's axis), heel, the midfoot's small bones, five metatarsals and the toes."""
    j, s = frame.j, frame.s
    an, heel, ball_, toe = (np.asarray(j[f"{n}.R"]) for n in ("ankle", "heel", "ball", "toe"))
    fwd = np.asarray((toe[0] - an[0], toe[1] - an[1], 0.0))
    fwd /= np.linalg.norm(fwd)
    lat = np.cross(fwd, (0, 0, 1))
    if lat[0] < 0:
        lat = -lat
    F_ = lambda f, l, z: an + fwd * f * s + lat * l * s + np.asarray((0, 0, z * s))
    rot = np.stack([fwd, lat, np.cross(fwd, lat)], 1)
    heads = metatarsal_heads(frame)

    def field(p):
        b = Body(p, 0.008 * s)
        b.add(capsule(an - lat * 0.012 * s, an + lat * 0.011 * s, 0.0158 * s), 0.004 * s)        # talus dome
        b.add(cone(F_(0.008, -0.001, -0.008), F_(0.036, -0.006, -0.022), 0.014 * s, 0.0105 * s), 0.008 * s)
        b.add(cone(F_(-0.006, 0.002, -0.032), heel + np.asarray((0, 0.01, 0.006)) * s, 0.0128 * s, 0.0135 * s), 0.008 * s)  # heel
        b.add(ell(F_(0.014, 0.006, -0.042), (0.025 * s, 0.0135 * s, 0.012 * s), rot), 0.01 * s)
        b.add(ell(F_(0.046, -0.01, -0.031), (0.0085 * s, 0.014 * s, 0.011 * s), rot), 0.004 * s)  # navicular
        b.add(ell(F_(0.05, 0.014, -0.045), (0.012 * s, 0.011 * s, 0.0095 * s), rot), 0.004 * s)   # cuboid
        for l in (-0.016, -0.005, 0.005):
            b.add(ell(F_(0.064, l, -0.037 - 0.002 * (l > 0)), (0.008 * s, 0.0055 * s, 0.0095 * s), rot), 0.003 * s)  # cuneiforms
        for i, (h, r) in enumerate(heads):
            base = F_(0.074 + 0.003 * (i == 0) - 0.006 * (i > 2), -0.017 + 0.0095 * i, -0.039 + 0.004 * i)
            rb = (0.0085 if i == 0 else 0.0062) * s
            b.add(ball(base, rb), 0.003 * s)
            b.add(cone(base, h, rb * 0.75, r * 0.72), 0.003 * s)
            b.add(ball(h, r), 0.002 * s)
        b.add(ball(F_(0.06, 0.028, -0.048), 0.006 * s), 0.004 * s)                               # fifth's tuberosity
        tdir = toe - ball_
        tdir[2] = 0
        tdir /= np.linalg.norm(tdir)
        for i, (h, r) in enumerate(heads):
            spread = (lat * (i - 1.6) * 0.06 + tdir)
            spread /= np.linalg.norm(spread)
            segs = (0.027, 0.02) if i == 0 else (0.019 - 0.0012 * i, 0.0095, 0.008)
            rr = (0.0074, 0.0066) if i == 0 else (0.0049, 0.0044, 0.004)
            a = h
            for n, (length, rad) in enumerate(zip(segs, rr)):
                d = spread * length * s * (1 - 0.15 * (i == 4)) - np.asarray((0, 0, 0.0018 * s * n))
                start = a + d / np.linalg.norm(d) * (r if n == 0 else rad * s) * 0.85
                c = a + d
                b.add(ball(start, rad * s * 1.12), 0.0015 * s)
                b.add(cone(start, c, rad * s * 0.85, rad * s * 0.8), 0.0018 * s)
                b.add(ball(c, rad * s * (0.95 if n < len(segs) - 1 else 0.9)), 0.0015 * s)
                a = c
        return b.d
    return field, _bounds([an, heel, ball_, toe, an + lat * 0.04 * s, an - lat * 0.03 * s], 0.025 * s)


def metatarsal_heads(frame):
    """The five metatarsal heads (big toe first): centres and radii, along a line slanting back to the little toe."""
    j, s = frame.j, frame.s
    an, ball_ = np.asarray(j["ankle.R"]), np.asarray(j["ball.R"])
    fwd = ball_ - an
    fwd[2] = 0
    fwd /= np.linalg.norm(fwd)
    lat = np.cross(fwd, (0, 0, 1))
    if lat[0] < 0:
        lat = -lat
    out = []
    for i, (l, f, r) in enumerate(((-0.024, 0.004, 0.0085), (-0.008, 0.006, 0.0063), (0.006, 0.0, 0.006), (0.018, -0.008, 0.0058),
                                   (0.029, -0.018, 0.0058))):
        c = ball_ + lat * l * s + fwd * f * s
        c[2] = r * s + 0.002 * s
        out.append((c, r * s))
    return out
