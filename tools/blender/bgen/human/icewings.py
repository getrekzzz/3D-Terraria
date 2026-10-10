"""Ice wings: a faceted crystal spar for the arm with long crystal shards fanning from it like flight feathers (the
longest from the hand, shorter along the forearm, a row of small ones over the arm) and frost spikes along the
leading edge. Each shard is a pointed prism of four facets that swells a little past its root; its ridges are
white, its faces pale cyan deepening toward the tip, and it is flat shaded so every facet catches the light."""
import math

from mathutils import Vector


WHITE, CYAN, DEEP = (0.95, 0.99, 1.0), (0.62, 0.86, 1.0), (0.38, 0.68, 0.95)


def _mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def shard(bm, layer, root, tip, normal, width, depth=0.55, twist=0.0):
    """A crystal shard from ``root`` to ``tip``: a four-ridged prism (eight vertices a ring: ridges and the middles
    of the flat faces between them), widest a third of the way out, closing to a point at both ends."""
    d = tip - root
    length = d.length
    d.normalize()
    n = (normal - d * normal.dot(d)).normalized()
    b = d.cross(n)
    stations = ((0.0, 0.35), (0.08, 0.8), (0.33, 1.0), (0.7, 0.62), (0.92, 0.22))
    rings = []
    for t, k in stations:
        c = root + d * (length * t)
        ring = []
        for j in range(8):
            a = math.pi / 4 * j + twist
            ridge = j % 2 == 0
            r = width * k * (1.0 if ridge else math.cos(math.pi / 4))
            off = n * (math.cos(a) * r * depth) + b * (math.sin(a) * r)
            v = bm.verts.new(c + off)
            ring.append((v, ridge, t))
        rings.append(ring)
    ends = [(bm.verts.new(root - d * width * 0.25), True, 0.0), (bm.verts.new(tip), True, 1.0)]
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for j in range(8):
            faces.append(bm.faces.new((r0[j][0], r0[(j + 1) % 8][0], r1[(j + 1) % 8][0], r1[j][0])))
    for j in range(8):
        faces.append(bm.faces.new((rings[0][(j + 1) % 8][0], rings[0][j][0], ends[0][0])))
        faces.append(bm.faces.new((rings[-1][j][0], rings[-1][(j + 1) % 8][0], ends[1][0])))
    meta = {v: (ridge, t) for ring in rings for v, ridge, t in ring}
    meta.update({v: (ridge, t) for v, ridge, t in ends})
    for f in faces:
        f.smooth = False
        for loop in f.loops:
            ridge, t = meta[loop.vert]
            face_tone = _mix(CYAN, DEEP, t ** 1.3)
            loop[layer] = (*(_mix(face_tone, WHITE, 0.85) if ridge else face_tone), 1.0)
    return faces


def wing(bm, plane, s):
    """The shards and spikes of one wing on ``plane`` (wings.Plane); returns the arm's 2D joints S, E, W, T."""
    V = lambda u, v: Vector((u, v))
    S, E, W, T = V(0, 0), V(0.14, 0.12), V(0.3, 0.2), V(0.5, 0.23)
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    rows = [  # count, root start, root end, angle start, angle end (deg from the span), length start, end, width, lift
        (8, W, T, -96, -6, 0.3, 0.46, 0.04, 0.0),          # primaries, the outermost nearly along the hand
        (8, S + (E - S) * 0.35, W, -100, -96, 0.2, 0.28, 0.036, 0.004),
        (12, S + (E - S) * 0.2, T, -106, -40, 0.11, 0.17, 0.03, 0.018),     # coverts over them
        (12, S + (E - S) * 0.1, T, -120, -60, 0.06, 0.085, 0.022, 0.03),    # and small ones over the arm
    ]
    for count, a, b, ang0, ang1, l0, l1, w, lift in rows:
        for k in range(count):
            t = k / (count - 1)
            jag = 0.5 + 0.5 * math.sin(k * 2.7 + count)
            root2 = a.lerp(b, t) + V(0, 0.01 * (lift > 0.01))
            ang = math.radians(ang0 + (ang1 - ang0) * t ** 1.3 + 6 * (jag - 0.5))
            length = (l0 + (l1 - l0) * t) * (0.78 + 0.44 * jag)
            z = lift + 0.0016 * k
            root = plane.at(root2.x, root2.y, z)
            tip = plane.at(root2.x + math.cos(ang) * length, root2.y + math.sin(ang) * length, z - 0.012)
            shard(bm, layer, root, tip, plane.N, w * s * (0.8 + 0.4 * jag), twist=0.3 * (jag - 0.5))
    for k in range(6):         # frost spikes standing up off the leading edge
        t = 0.12 + 0.8 * k / 5
        base = S.lerp(E, t * 2) if t < 0.5 else E.lerp(T, (t - 0.5) * 2)
        ang = math.radians(70 - 55 * t)
        length = 0.05 + 0.06 * math.sin(math.pi * t) + 0.02 * math.sin(k * 3.1)
        root = plane.at(base.x, base.y, 0.006)
        tip = plane.at(base.x + math.cos(ang) * length, base.y + math.sin(ang) * length, 0.0)
        shard(bm, layer, root, tip, plane.N, 0.014 * s, twist=0.4)
    return S, E, W, T


def arm(bm, joints, normal, s):
    """The crystal spar: a chain of thick shards from joint to joint (root, shoulder, elbow, wrist, hand), each
    overlapping the next."""
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    widths = (0.05, 0.042, 0.034, 0.026)
    for i, (a, b) in enumerate(zip(joints, joints[1:])):
        d = b - a
        shard(bm, layer, a - d * 0.12, b + d * 0.18, normal, widths[min(i, 3)] * s, depth=0.7, twist=0.4 * i)
