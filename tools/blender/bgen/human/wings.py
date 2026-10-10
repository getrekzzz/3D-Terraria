"""Wings growing from the shoulder blades, half spread and swept back, carried by the chest. ``wingShape``:

- bat: an arm (upper arm, forearm) and four long finger bones fanning from the wrist, with a hooked thumb claw; a
  membrane with thickness spans the fingers and runs down to the waist, bellying between the bones, its trailing
  edge scalloped between the finger tips;
- feather: a bird's wing, the arm shingled with small coverts and carrying overlapping rows of flight feathers
  (primaries from the hand, secondaries along the forearm) under rows of coverts;
- ice: faceted crystal shards fanning from a crystal spar like flight feathers, pale cyan with white ridges
  (icewings.py).
"""
import math

import bmesh
from mathutils import Vector

from . import icewings, wingroot
from .features import blade, paint, round_section, spline, sweep
from .garments import mesh_from, set_slot
from .paint import hex3
from .rig import rigid


class Plane:
    """The wing's plane: u out along the span, v up, w out of the plane away from the back (frame units)."""

    def __init__(self, origin, sx, s, sweep_deg=32, tilt_deg=14, bend=0.07):
        a, t = math.radians(sweep_deg), math.radians(tilt_deg)
        self.X = Vector((sx * math.cos(a), -math.sin(a), 0))
        z = Vector((0, -math.sin(t), math.cos(t)))
        self.Z = (z - self.X * z.dot(self.X)).normalized()
        self.N = self.X.cross(self.Z) * sx
        if self.N.y > 0:
            self.N = -self.N
        self.o, self.s, self.bend = origin, s, bend

    def at(self, u, v, w=0.0):
        """A point of the wing; the plane curls back toward the tips."""
        return self.o + (self.X * u + self.Z * v + self.N * (w + self.bend * u * u)) * self.s


def _bezier(a, k, b, t):
    return a * (1 - t) ** 2 + k * 2 * t * (1 - t) + b * t * t


def _curve(a, b, bow):
    """A finger from a to b bowed sideways by ``bow`` (2D points)."""
    d = b - a
    k = (a + b) / 2 + Vector((-d.y, d.x)) * bow
    return lambda t: _bezier(a, k, b, t)


def _membrane(bm, plane, P, nr, ns, th, belly, verts, top=1.0):
    """A panel of membrane with thickness from a 2D patch P(r, s) (s = 0 on its free edge, up to ``top``), bellying
    out between its sides; collects (vert, r, s) for colouring."""
    sheets = []
    for side in (1, -1):
        grid = []
        for i in range(ns + 1):
            s_ = top * i / ns
            row = []
            for j in range(nr + 1):
                r = j / nr
                q = P(r, s_)
                w = belly * math.sin(math.pi * r) * math.sin(math.pi * min(1.0, s_ * 1.4)) ** 0.8
                v = bm.verts.new(plane.at(q.x, q.y, w + side * th * 0.5))
                verts.append((v, r, s_))
                row.append(v)
            grid.append(row)
        sheets.append(grid)
    a, b = sheets
    for i in range(ns):
        for j in range(nr):
            bm.faces.new((a[i][j], a[i][j + 1], a[i + 1][j + 1], a[i + 1][j]))
            bm.faces.new((b[i + 1][j], b[i + 1][j + 1], b[i][j + 1], b[i][j]))
    rim = [(i, 0) for i in range(ns + 1)] + [(ns, j) for j in range(1, nr + 1)] + \
          [(i, nr) for i in range(ns - 1, -1, -1)] + [(0, j) for j in range(nr - 1, 0, -1)]
    for (i0, j0), (i1, j1) in zip(rim, rim[1:] + rim[:1]):
        if a[i0][j0] is a[i1][j1]:
            continue
        try:
            bm.faces.new((a[i0][j0], b[i0][j0], b[i1][j1], a[i1][j1]))
        except ValueError:
            pass


def _bat(bm, claws, plane, s, color):
    V = lambda u, v: Vector((u, v))
    S, E, W = V(0, 0), V(0.15, 0.14), V(0.31, 0.21)
    tips = [V(0.64, 0.18), V(0.62, -0.07), V(0.5, -0.29), V(0.31, -0.39)]
    B = V(0.0, -0.3)
    fingers = [_curve(W, t, 0.06 + 0.02 * i) for i, t in enumerate(tips)]
    mverts = []

    def scallop(a, b, depth):
        mid = (a + b) / 2
        inward = (W - mid).normalized()
        return lambda r: a.lerp(b, r) + inward * depth * (a - b).length * math.sin(math.pi * r)

    for i in range(3):        # fans between the fingers
        f0, f1 = fingers[i], fingers[i + 1]
        edge = scallop(tips[i], tips[i + 1], 0.2)
        P = lambda r, q, f0=f0, f1=f1, edge=edge: ((1 - r) * f0(1 - q) + r * f1(1 - q) + (1 - q) * edge(r) + q * W
                                                    - ((1 - r) * (1 - q) * f0(1) + r * (1 - q) * f1(1) + q * W))
        _membrane(bm, plane, P, 10, 8, 0.0032, -0.02, mverts, top=0.97)
    arm = lambda r: S.lerp(E, r * 2) if r < 0.5 else E.lerp(W, r * 2 - 1)
    edge = scallop(B, tips[3], 0.16)
    f3 = fingers[3]
    P = lambda r, q: ((1 - r) * B.lerp(S, q) + r * f3(1 - q) + (1 - q) * edge(r) + q * arm(r)
                      - ((1 - r) * (1 - q) * B + r * (1 - q) * tips[3] + (1 - r) * q * S + r * q * W))
    _membrane(bm, plane, P, 12, 8, 0.0032, -0.022, mverts)
    paint(bm, mverts, lambda r, q, a=0: tuple(c * (1.05 - 0.22 * q) * (0.94 + 0.06 * math.cos(r * 18)) for c in color))
    bone = tuple(c * 0.72 for c in color)
    for f, t in zip(fingers, tips):
        path = [plane.at(*f(k / 20)) for k in range(21)]
        knuckle = lambda u: 1 + 0.3 * math.exp(-((u - 0.42) / 0.06) ** 2)
        vs = sweep(bm, path, round_section(lambda u: s * (0.0085 - 0.0062 * u) * knuckle(u)), 8)
        paint(bm, vs, lambda u, a: bone)
    return S, E, W, bone


def _claw(bm, plane, base, d, s):
    path = spline([plane.at(base.x, base.y), plane.at(base.x + d.x * 0.5, base.y + d.y * 0.5, 0.004),
                   plane.at(base.x + d.x, base.y + d.y * 0.8, 0.012)], 10)
    return sweep(bm, path, round_section(lambda u: s * 0.0075 * (1 - u) ** 0.9 + 0.0004 * s), 8)


def _arm_coverts(bm, path, radius, plane, color, first):
    """Small coverts shingled over the back and top of a bird wing's arm from its root out, pointing along it and
    down a little, in three staggered rows (from path index ``first``, where the root leaves the back)."""
    from .features import frames
    layer = bm.loops.layers.color.get("Color")
    s, n = plane.s, len(path)
    for row, theta in enumerate((1.2, 0.45, -0.3)):
        for i in range(max(first, 1), n - 3, 2):
            j = i + row % 2          # the middle row staggered half a step
            t = j / (n - 1)
            p, (tg, _, _) = path[j], frames(path[j - 1:j + 2])[1]
            up = (plane.Z - tg * plane.Z.dot(tg)).normalized()
            back = tg.cross(up)
            back = back if back.dot(plane.N) > 0 else -back
            out = back * math.cos(theta) + up * math.sin(theta)
            down = back * math.sin(theta) - up * math.cos(theta)
            length = s * (0.06 - 0.032 * t)
            r = radius(t)
            d = tg * math.cos(0.6) + down * math.sin(0.6)

            def tone(u, v, under, k=0.92 + 0.1 * row):
                return tuple(min(1.0, c * k * (1.08 if abs(v) < 0.1 else 1.0) * (0.92 + 0.12 * u)) for c in color)
            blade(bm, layer, p + out * (r * 0.8), d, out, length, length * 0.34, tone, fold=0.06,
                  curl=-0.14, thick=0.003 * s, rows=4, round_tip=True)


def _feathers(bm, plane, s, color):
    """Rows of flight feathers and coverts along the arm, each layer a little further out of the plane."""
    V = lambda u, v: Vector((u, v))
    S, E, W, T = V(0, 0), V(0.14, 0.11), V(0.3, 0.18), V(0.47, 0.2)
    rows = [  # (count, root start, root end, angle start, angle end (deg, from the span), length start, end, layer)
        (9, W, T, -100, -12, 0.27, 0.34, 0),       # primaries, the outermost nearly along the hand
        (10, S + (E - S) * 0.3, W, -104, -100, 0.2, 0.25, 0),
        (12, S + (E - S) * 0.4, T, -100, -40, 0.12, 0.15, 1),     # greater coverts
        (14, S + (E - S) * 0.5, T, -100, -60, 0.065, 0.08, 2),
    ]
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    for count, a, b, ang0, ang1, l0, l1, depth in rows:
        for k in range(count):
            t = k / (count - 1)
            root2 = a.lerp(b, t) + V(0, 0.012 * depth)
            ang = math.radians(ang0 + (ang1 - ang0) * t ** 1.4)
            length = l0 + (l1 - l0) * t
            lift = 0.012 * depth + 0.0009 * k            # each feather lies over the one before it
            root = plane.at(root2.x, root2.y, lift)
            tip = plane.at(root2.x + math.cos(ang) * length, root2.y + math.sin(ang) * length, lift - 0.01)
            n = plane.N
            shade = 0.82 + 0.18 * depth / 2

            def tone(u, v, under, shade=shade, depth=depth):
                k_ = shade * (1.0 - 0.28 * u ** 2 * (depth == 0)) * (1.12 if abs(v) < 0.1 else 1.0) * (1.06 if under else 1.0)
                return tuple(min(1.0, c * k_) for c in color)
            blade(bm, layer, root, tip - root, n, (tip - root).length, length * s * (0.11 if depth == 0 else 0.2), tone,
                  fold=0.08, curl=0.05, thick=0.0035 * s, rows=6, round_tip=True, skew=0.35)
    return S, E, W, T


def wings(fr, body, color, shape="bat", garments=()):
    """A pair of wings: membrane (or feathers, or crystal) and bones as one part, claws as another. Each grows out of
    a slit in the garment over the shoulder blade (wingroot.py), its root broad and flattened where it leaves a bare
    back (a fleshy stem through cloth) and tapering into the arm."""
    s = fr.s
    base = hex3(color)
    skin = wingroot.skin_tone(body)
    bm, claws = bmesh.new(), bmesh.new()
    bm.loops.layers.color.new("Color")
    hems = []
    for sx in (1, -1):
        root, normal, hit = wingroot.find(fr, body, garments, sx)
        start = root - normal * 0.035 * s
        shoulder = root + normal * 0.045 * s + Vector((sx * 0.03 * s, 0, 0.03 * s))
        plane = Plane(shoulder, sx, s, bend=0.07 if shape == "bat" else 0.05)
        if shape == "bat":
            S, E, W, tone = _bat(bm, claws, plane, s, base)
            radii = (0.026, 0.019, 0.016, 0.0115, 0.009)
            vs = _claw(claws, plane, W, Vector((0.03, 0.065)), s)
            paint(claws, vs, lambda u, a: (0.12, 0.1, 0.09))
        elif shape == "ice":
            S, E, W, T = icewings.wing(bm, plane, s)
            tone = (0.85, 0.95, 1.0)
            radii = (0.022, 0.018, 0.015, 0.011, 0.006)
        else:
            S, E, W, T = _feathers(bm, plane, s, base)
            tone = base
            radii = (0.026, 0.022, 0.019, 0.014, 0.008)
        arm = [start, plane.at(S.x, S.y), plane.at(E.x, E.y), plane.at(W.x, W.y)]
        if shape != "bat":
            arm.append(plane.at(T.x, T.y))
        path = spline(arm, 44)
        rs = list(radii[:len(arm)])
        u_skin = min(range(len(path)), key=lambda i: (path[i] - root).length) / (len(path) - 1)
        u_joint = min(range(len(path)), key=lambda i: (path[i] - arm[1]).length) / (len(path) - 1)

        def radius(u, rs=rs):
            x = u * (len(rs) - 1)
            i = min(int(x), len(rs) - 2)
            return s * (rs[i] + (rs[i + 1] - rs[i]) * (x - i))
        if shape == "ice":
            icewings.arm(bm, arm, plane.N, s)
        else:
            bare = hit is None or hit.name.split(".")[0] not in wingroot.GARMENTS
            vs = sweep(bm, path, wingroot.section(s, radius, u_skin, u_joint, broad=bare), 14, up=Vector((0, 0, 1)))

            def shade(u, a, tone=tone, a0=u_skin, a1=u_joint):
                if shape != "bat":
                    return tone
                k = min(1.0, max(0.0, (u - a0) / (a1 - a0)))      # the skin of the back runs out along a bat wing's root
                return tuple(c + (t - c) * k for c, t in zip(skin, tone))
            paint(bm, vs, shade)
            if shape != "bat":
                _arm_coverts(bm, path, radius, plane, base, round(u_skin * (len(path) - 1)) + 1)
        ring = None if shape == "ice" or hit is None else wingroot.crossing(hit, vs, s)
        if ring:                         # the slit parted round the root where it actually comes through the cloth
            hem = wingroot.lens(hit, ring, normal, s)
        else:
            hem = wingroot.slit(hit, root, normal, s, 0.022 * s, 0.032 * s)
        if hem is not None:
            hems.append(hem)
    obj = mesh_from(bm, "wings")
    if shape == "ice":
        for poly in obj.data.polygons:
            poly.use_smooth = False
    set_slot(obj, {"bat": "char:leather", "ice": "char:eye"}.get(shape, "char:cloth"))
    rigid(obj, "chest")
    out = [obj]
    for i, hem in enumerate(hems):
        h = mesh_from(hem, f"wing_slit{i}")
        set_slot(h, "char:cloth")
        rigid(h, "chest")
        out.append(h)
    if claws.verts:
        cl = mesh_from(claws, "wing_claws")
        set_slot(cl, "char:nail")
        rigid(cl, "chest")
        out.append(cl)
    else:
        claws.free()
    return out
