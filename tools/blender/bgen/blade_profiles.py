"""Named blade profiles, guards and pommels for held weapons (swords, unique blades). Everything adds geometry to a
MeshBuilder in item space: Blender Z up, metres, grip at the origin, blade along +Z, flat in the XZ plane, thin along Y.

Blades are lofted with a bevelled cross-section (a knife edge, a lighter ``edge`` band, a raised central ridge or a
dark fuller groove) and flat-shaded, so each facet catches the icon light and the blade reads as metal at 128 px.
A profile is two piecewise-linear sides, ``[(t, x)]`` with t 0 (guard) .. 1 (tip) and x in blade widths; both sides
meet at the tip. Exported helpers: ``blade``, ``guard``, ``pommel``, ``grip``, ``bevel_slab``, ``star_points``,
``lighten``, ``darken``.
"""
import math

import bmesh
from mathutils import Matrix, Vector

from .mesh import MeshBuilder, hex_color

HANDLE = "prop:dark_planks"


# ---------------------------------------------------------------- colours
def lighten(color, a=0.45):
    c = hex_color(color)
    return "#%02x%02x%02x" % tuple(round(255 * (c[i] + (1 - c[i]) * a)) for i in range(3))


def darken(color, a=0.35):
    c = hex_color(color)
    return "#%02x%02x%02x" % tuple(round(255 * c[i] * (1 - a)) for i in range(3))


def _paint(mb: MeshBuilder, faces, material, color):
    index, rgba = mb._material_index(material), hex_color(color)
    for f in faces:
        f.material_index = index
        for loop in f.loops:
            loop[mb.color_layer] = rgba


# ---------------------------------------------------------------- profiles
def _mirror(side):
    return [(t, -x) for t, x in side], side


def _saw(t0, t1, teeth, base, amp, tip=(0.86, 0.42)):
    """One side with ``teeth`` barbs between t0 and t1 (ramp out toward the tip, then a sharp drop)."""
    pts, period = [(0.0, base), (t0, base)], (t1 - t0) / teeth
    for k in range(teeth):
        end = t0 + (k + 1) * period
        pts += [(end - 0.006, base + amp), (end, base)]
    return pts + [tip, (1.0, 0.0)]


def _flame(phase, amp=0.16, waves=3.5):
    pts = []
    for i in range(12):
        t = i / 12
        taper = 1.0 - t ** 2.2
        pts.append((t, (0.46 + amp * math.sin((t * waves + phase) * 2 * math.pi)) * taper + 0.04))
    return pts + [(1.0, 0.06)]


PROFILES = {
    "straight": lambda: _mirror([(0, 0.5), (0.4, 0.5), (0.8, 0.5), (0.92, 0.28), (1, 0)]),
    "leaf": lambda: _mirror([(0, 0.3), (0.2, 0.5), (0.52, 0.64), (0.8, 0.46), (1, 0)]),
    "broad": lambda: _mirror([(0, 0.72), (0.08, 0.62), (0.72, 0.62), (0.87, 0.46), (1, 0)]),
    "rapier": lambda: _mirror([(0, 0.3), (0.45, 0.25), (0.86, 0.2), (1, 0)]),
    "katana": lambda: ([(0, -0.5), (0.85, -0.46), (0.95, -0.12), (1, 0.5)], [(0, 0.5), (1, 0.5)]),
    "curved": lambda: ([(0, -0.42), (0.45, -0.56), (0.8, -0.78), (0.93, -0.5), (1, 0.38)], [(0, 0.42), (0.6, 0.42), (1, 0.38)]),
    "cleaver": lambda: ([(0, -0.55), (1, -0.64)], [(0, 0.55), (0.1, 0.64), (0.8, 0.64), (1, -0.64)]),
    "hooked": lambda: ([(0, -0.42), (0.84, -0.38), (1, -0.05)],
                       [(0, 0.42), (0.48, 0.46), (0.7, 0.9), (0.706, 0.4), (0.88, 0.3), (1, -0.05)]),
    "serrated": lambda: _mirror(_saw(0.12, 0.8, 4, 0.5, 0.14)),
    "jagged": lambda: ([(t, -x) for t, x in _saw(0.04, 0.72, 3, 0.42, 0.3, (0.84, 0.4))],
                       _saw(0.12, 0.8, 3, 0.42, 0.28, (0.88, 0.36))),
    "flame": lambda: ([(t, -x) for t, x in _flame(0.0)], [(t, x if t < 1 else -0.06) for t, x in _flame(0.5)]),
}
# Shortsword stand-ins: a short blade of a long profile reads better as a rapier or leaf (gladius).
SHORT = {"straight": "rapier", "broad": "leaf", "katana": "rapier", "curved": "leaf", "cleaver": "leaf",
         "hooked": "leaf", "serrated": "leaf", "jagged": "leaf", "flame": "leaf"}


def _interp(side, t):
    for (t0, x0), (t1, x1) in zip(side, side[1:]):
        if t0 <= t <= t1:
            return x0 if t1 - t0 < 1e-9 else x0 + (x1 - x0) * (t - t0) / (t1 - t0)
    return side[-1][1]


def stations(profile, z0, length, width, curve=0.0):
    """[(z, xl, xr)] along the blade; ``curve`` bends it (tip offset in blade lengths, quadratic)."""
    left, right = PROFILES[profile]() if isinstance(profile, str) else profile
    ts = sorted({round(t, 4) for t, _ in left + right})
    out = []
    for t in ts:
        bend = curve * length * t * t
        out.append((z0 + t * length, _interp(left, t) * width + bend, _interp(right, t) * width + bend))
    return out


# ---------------------------------------------------------------- blade loft
def blade(mb: MeshBuilder, profile="straight", z0=0.07, length=0.8, width=0.09, material="prop:iron:metal",
          color="#c0c4cc", edge=None, edge_material=None, thick=0.022, fuller=False, fuller_color=None, curve=0.0):
    """Lofts a bevelled blade; returns the tip position (x, 0, z). Edge bands use ``edge_material``/``edge``
    (default: the blade's material, lightened colour); the fuller groove is darkened unless ``fuller_color``."""
    rows = stations(profile, z0, length, width, curve)
    edge = edge or lighten(color)
    edge_material = edge_material or material
    groove = fuller_color or darken(color, 0.4)
    t, bev = thick / 2, width * 0.24
    rings, tip, flags = [], None, []
    for z, xl, xr in rows:
        w = xr - xl
        if w < 1e-4:
            tip = mb.bm.verts.new(((xl + xr) / 2, 0.0, z))
            break
        b, fw, xc = min(bev, w * 0.3), w * 0.13, (xl + xr) / 2
        rel = (z - z0) / length
        grooved = fuller and 0.04 < rel < 0.72
        cy = t * 0.35 if grooved else t * 1.3
        front = [(xl, 0), (xl + b, -t), (xc - fw, -t), (xc, -cy), (xc + fw, -t), (xr - b, -t), (xr, 0)]
        back = [(x, -y) for x, y in reversed(front[1:-1])]
        rings.append([mb.bm.verts.new((x, y, z)) for x, y in front + back])
        flags.append(grooved)
    n = 12
    edge_seg, mid_seg = {0, 5, 6, 11}, {2, 3, 8, 9}
    body, edges, mids = [], [], []

    def bucket(i, grooved):
        return edges if i in edge_seg else (mids if i in mid_seg and grooved else body)
    for (a, ga), (b, gb) in zip(zip(rings, flags), zip(rings[1:], flags[1:])):
        for i in range(n):
            j = (i + 1) % n
            bucket(i, ga and gb).append(mb.bm.faces.new((a[i], a[j], b[j], b[i])))
    last = rings[-1]
    if tip is not None:
        for i in range(n):
            bucket(i, False).append(mb.bm.faces.new((last[i], last[(i + 1) % n], tip)))
    else:
        body.append(mb.bm.faces.new(last))
    body.append(mb.bm.faces.new(list(reversed(rings[0]))))
    bmesh.ops.recalc_face_normals(mb.bm, faces=body + edges + mids)
    _paint(mb, body, material, color)
    _paint(mb, mids, material, groove)
    _paint(mb, edges, edge_material, edge)
    z, xl, xr = rows[-1]
    return ((xl + xr) / 2, 0.0, z)


# ---------------------------------------------------------------- slabs
def _signed_area(pts):
    return sum(x0 * z1 - x1 * z0 for (x0, z0), (x1, z1) in zip(pts, pts[1:] + pts[:1])) / 2


def bevel_slab(mb: MeshBuilder, pts, depth, bevel, material, color, rim=None, rim_material=None, matrix=None, edge=0.45):
    """A flat polygon (XZ) with chamfered faces: the inner face at y = ±depth/2, the outline at ±depth*edge/2, the
    chamfer ring in ``rim`` colour (default lightened). Concave outlines are fine for small bevels."""
    rim = rim or lighten(color, 0.3)
    rim_material = rim_material or material
    ccw = _signed_area(pts) > 0
    inner = []
    m = len(pts)
    for i in range(m):
        p, a, b = Vector(pts[i]), Vector(pts[i - 1]), Vector(pts[(i + 1) % m])
        e1, e2 = (p - a).normalized(), (b - p).normalized()
        n1 = Vector((-e1.y, e1.x)) if ccw else Vector((e1.y, -e1.x))
        n2 = Vector((-e2.y, e2.x)) if ccw else Vector((e2.y, -e2.x))
        d = n1 + n2
        d = d.normalized() if d.length > 1e-6 else n1
        k = min(bevel / max(d.dot(n1), 0.35), bevel * 2.2, 0.6 * (p - a).length, 0.6 * (b - p).length)
        # Never past the middle of a thin part (crescent tips, spikes): a crossing inset breaks the face fill.
        for j in range(m):
            if j in (i, (i - 1) % m):
                continue
            q0, q1 = Vector(pts[j]), Vector(pts[(j + 1) % m])
            seg = q1 - q0
            u = max(0.0, min(1.0, (p - q0).dot(seg) / max(seg.length_squared, 1e-12)))
            k = min(k, 0.4 * (p - (q0 + seg * u)).length)
        inner.append(p + d * k)
    h, e = depth / 2, depth * edge / 2
    of = [mb.bm.verts.new((x, -e, z)) for x, z in pts]
    ob = [mb.bm.verts.new((x, e, z)) for x, z in pts]
    inf = [mb.bm.verts.new((q.x, -h, q.y)) for q in inner]
    inb = [mb.bm.verts.new((q.x, h, q.y)) for q in inner]
    faces = [mb.bm.faces.new(inf), mb.bm.faces.new(list(reversed(inb)))]
    rims = []
    for i in range(m):
        j = (i + 1) % m
        rims += [mb.bm.faces.new((inf[i], inf[j], of[j], of[i])), mb.bm.faces.new((of[i], of[j], ob[j], ob[i])),
                 mb.bm.faces.new((ob[i], ob[j], inb[j], inb[i]))]
    verts = of + ob + inf + inb
    if matrix is not None:
        bmesh.ops.transform(mb.bm, matrix=matrix, verts=verts)
    bmesh.ops.recalc_face_normals(mb.bm, faces=faces + rims)
    _paint(mb, faces, material, color)
    _paint(mb, rims, rim_material, rim)
    return verts


def star_points(cx, cz, r_out, r_in, points=5, rot=math.pi / 2):
    return [(cx + math.cos(rot + k * math.pi / points) * (r_out if k % 2 == 0 else r_in),
             cz + math.sin(rot + k * math.pi / points) * (r_out if k % 2 == 0 else r_in)) for k in range(points * 2)]


def _arc(gw, z, rise, thick, n=9, tip=0.012):
    """A bar bent so its ends rise by ``rise`` (a crescent guard); thinner toward the ends."""
    xs = [-gw + 2 * gw * i / (n - 1) for i in range(n)]
    low = [(x, z + rise * (x / gw) ** 2) for x in xs]
    high = [(x, z + rise * (x / gw) ** 2 + thick - (thick - tip) * abs(x / gw)) for x in reversed(xs)]
    return low + high


# ---------------------------------------------------------------- hilt parts
def guard(mb: MeshBuilder, kind, z, width, material, color, gem=None):
    """Crossguard at height z (its bottom), sized from the blade width. Kinds: bar, crescent, winged, disc, spiky,
    none. ``gem`` (hex) sets a glowing stone in its centre."""
    gw = max(width * 1.5, 0.085)
    if kind == "bar":
        pts = [(-gw, z), (gw, z), (gw + 0.022, z + 0.025), (gw, z + 0.05), (-gw, z + 0.05), (-gw - 0.022, z + 0.025)]
        bevel_slab(mb, pts, 0.05, 0.01, material, color)
    elif kind == "crescent":
        bevel_slab(mb, _arc(gw * 1.15, z, 0.06, 0.05), 0.045, 0.006, material, color, edge=0.6)
    elif kind == "winged":
        right = [(0.0, z - 0.01), (gw * 0.6, z), (gw * 1.25, z + 0.05), (gw * 1.6, z + 0.15), (gw * 1.2, z + 0.1),
                 (gw * 1.25, z + 0.13), (gw * 0.85, z + 0.075), (gw * 0.4, z + 0.06), (0.0, z + 0.06)]
        pts = right[:-1] + [(-x, zz) for x, zz in reversed(right[1:])]
        bevel_slab(mb, pts, 0.045, 0.009, material, color)
    elif kind == "disc":
        r = max(width * 1.0, 0.06)
        mb.cylinder((0, 0, z), (0, 0, z + 0.022), r, None, material, color, segments=12, smooth=False)
    elif kind == "spiky":
        right = [(0.0, z), (gw * 0.5, z), (gw * 1.3, z - 0.04), (gw * 0.85, z + 0.02), (gw * 1.4, z + 0.09),
                 (gw * 0.5, z + 0.05), (0.0, z + 0.05)]
        pts = right[:-1] + [(-x, zz) for x, zz in reversed(right[1:])]
        bevel_slab(mb, pts, 0.05, 0.008, material, color)
    if gem and kind != "none":
        mb.sphere((0, -0.02, z + 0.026), 0.022, "prop:crystal:glow", gem, subdivisions=1, scale=(1, 0.6, 1), smooth=False)


def grip(mb: MeshBuilder, bottom=-0.18, top=0.02, wrap=None, radius=0.021):
    """The handle: dark wood, or leather in ``wrap`` colour with two binding rings."""
    if not wrap:
        mb.cylinder((0, 0, bottom), (0, 0, top), radius, None, HANDLE, segments=8)
        return
    mb.cylinder((0, 0, bottom), (0, 0, top), radius, None, "prop:leather", wrap, segments=8)
    for zz in (bottom + (top - bottom) * 0.3, bottom + (top - bottom) * 0.7):
        mb.cylinder((0, 0, zz - 0.006), (0, 0, zz + 0.006), radius * 1.18, None, "prop:leather", darken(wrap, 0.4), segments=8, caps=False)


def pommel(mb: MeshBuilder, kind, z, material, color, gem=None):
    """Pommel centred at height z (below the grip). Kinds: ball, gem, ring, spike, star, none."""
    if kind == "ball":
        mb.sphere((0, 0, z), 0.034, material, color, subdivisions=1)
    elif kind == "gem":
        mb.cylinder((0, 0, z + 0.02), (0, 0, z - 0.012), 0.026, 0.034, material, color, segments=8, smooth=False)
        mb.sphere((0, 0, z - 0.03), 0.028, "prop:crystal:glow", gem or "#ff3a5a", subdivisions=1, smooth=False)
    elif kind == "ring":
        pts = [Vector((math.sin(a) * 0.032, 0, z - 0.012 + math.cos(a) * 0.032)) for a in (i * 2 * math.pi / 10 for i in range(11))]
        mb.tube(pts, [0.009] * len(pts), material, color, segments=5)
    elif kind == "spike":
        mb.cylinder((0, 0, z + 0.02), (0, 0, z - 0.08), 0.03, 0.0, material, color, segments=6, smooth=False)
    elif kind == "star":
        bevel_slab(mb, star_points(0, z - 0.03, 0.06, 0.026), 0.03, 0.008, "prop:plain:glow", gem or "#ffe060",
                   rim=lighten(gem or "#ffe060", 0.5))
