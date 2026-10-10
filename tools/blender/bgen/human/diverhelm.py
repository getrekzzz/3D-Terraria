"""The Storm Diver's kit (helmets.py ``helmet: diver`` and scifi.py ``jetpack``):

- a big round metal helmet over the whole head with a wide window across the front, a round gasket tube round the
  window, a hub at each side, a fin along the crown rising from the shell and sinking back into it, and a lamp on the
  brow; a glass visor domes over the window; it follows the
  head, sitting in the neck ring on the shoulders;
- a jetpack on the back: a rounded housing between two tall tanks, each ending in a thruster bell with a glowing
  throat.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import plates as P
from .garments import mesh_from, set_slot
from .paint import _ensure_layer, hex3
from .rig import rigid
from .platefit import field_normals, set_normals
from .sdf import F, capsule, carve, round_cone, smax, smin, sphere, tube

SOURCES = ("diverhelm.py",)
WINDOW = (1.08, -0.62, 0.52)        # the window: half its width (radians of yaw), its bottom and top (pitch)


def _window(d):
    """> 0 for directions (head frame: x right, y forward, z up) inside the window, about radians from its edge."""
    yaw = np.arctan2(d[..., 0], d[..., 1])
    pitch = np.arcsin(np.clip(d[..., 2], -1, 1))
    w, lo, hi = WINDOW
    mid, half = (lo + hi) / 2, (hi - lo) / 2
    return 1.0 - np.sqrt((yaw / w) ** 2 + ((pitch - mid) / half) ** 2)


def _rim(centre, radius, n=72):
    """Points round the window's edge (a closed loop) on a sphere of ``radius``."""
    w, lo, hi = WINDOW
    th = np.linspace(0, 2 * np.pi, n + 1)
    yaw, pitch = w * np.cos(th), (lo + hi) / 2 + (hi - lo) / 2 * np.sin(th)
    d = np.stack([np.sin(yaw) * np.cos(pitch), np.cos(yaw) * np.cos(pitch), np.sin(pitch)], 1)
    return [tuple(v) for v in np.asarray(centre) + d * radius]


def build(shop):
    """[neck ring, helmet shell, visor (glass), lamp (glow)]."""
    from .helmets import _shade
    head, s = shop.head, shop.s
    centre = head.c + Vector(shop.axis) * 0.01 * head.h + Vector((0, 0.006 * head.h, 0))
    neck_top = shop.collar_z + 0.02 * s
    radius = shop.reach(centre, neck_top) + 0.02 * s
    inner = 0.09 * s
    cut = math.sqrt(max(radius * radius - inner * inner, 0.0))
    top = float(np.dot(np.asarray(centre - Vector(shop.axis) * cut - shop.fr.j["neck"]), np.asarray(shop.axis)))
    ring = shop.ring(inner - 0.004 * s, top + 0.006 * s)
    c = np.asarray(centre, dtype=F)
    n = np.asarray(shop.axis, dtype=F)
    t = 0.008 * s
    rim = _rim(c, radius + 0.5 * t)
    a0, a1 = -math.asin(0.02 * s / radius), math.acos(0.25)                    # the fin's ends (from the top)

    def gasket(p, r, win):
        """A round tube along the window's edge, the same thickness all the way round."""
        out = np.full(len(p), 1.0, dtype=F)
        m = (np.abs(win) < F(0.03 * s)) & (np.abs(r - F(radius)) < F(0.03 * s))
        if m.any():
            out[m] = tube(p[m], rim, [0.0085 * s] * len(rim))
        return out

    def field(p):
        q = p - c
        r = np.linalg.norm(q, axis=1)
        d = smax(r - F(radius + t), F(radius) - r, 0.0)
        d = smax(d, -(q @ n) - F(cut + 0.004 * s), 0.003)                       # open at the neck
        win = _window(q / np.maximum(r, F(1e-6))[:, None]) * F(radius)
        d = smax(d, win - F(0.004 * s), 0.004)                                   # the window
        d = smin(d, gasket(p, r, win), 0.003)
        a = np.arctan2(-q[:, 1], q[:, 2])                  # the fin rises from the shell and sinks back into it
        rise = np.clip(np.minimum((a - F(a0)) / F(0.4), (F(a1) - a) / F(0.5)), 0, 1)
        fin = smax(np.abs(q[:, 0]) - F(0.006 * s), r - F(radius + t * 0.5) - F(0.0185 * s) * rise * rise * (3 - 2 * rise), 0.004)
        fin = smax(smax(fin, F(radius + 0.002 * s) - r, 0.0), F(radius) * np.maximum(F(a0) - a, a - F(a1)), 0.0)
        return smin(d, fin, 0.006)

    R = radius + 0.04 * s
    bounds = (tuple(c - R), tuple(c + R))
    obj = P.mesh(field, bounds, "helmet", (shop.key, "diver", round(radius, 4), round(cut, 4)), SOURCES, h=0.0016, target=6400)
    normals = field_normals(obj, field)
    set_normals(obj, normals)
    _shade(obj, shop.metal, normals=normals)
    set_slot(obj, "char:metal")
    rigid(obj, "head")
    visor = shop.glass(centre, radius + 0.002 * s, cut, "visor",
                       window=lambda d: _window(np.asarray([d.x, d.y, d.z])) * radius + 0.006 * s)
    return [ring, obj] + [_hub(shop, c, sx, radius, s) for sx in (1, -1)] + [visor, _lamp(centre, radius + t, s, shop.glow)]


def _hub(shop, c, sx, radius, s):
    """A round hub on the side of the helmet, swept round its axis (true and even all the way round): a short drum
    with rounded edges and a groove ringing its face, its foot sunk into the shell."""
    from .helmets import _outline, _revolve, _shade

    def section(rho, t):                            # (a slice through the axis: rho runs either side of it)
        drum = P.box(rho, t, 0.0, radius + 0.0085 * s, 0.04 * s, 0.0075 * s, 0.003 * s)     # (its foot within the shell)
        return carve(drum, P.box(np.abs(rho), t, 0.026 * s, radius + 0.016 * s, 0.0024, 0.004 * s, 0.0012), 0.0014)

    loop, normal = _outline(section, (-0.05 * s, radius - 0.012 * s), (0.05 * s, radius + 0.024 * s), 0.006 * s, 0.007 * s)
    k, mid = len(loop), (loop[:, 1].max() + loop[:, 1].min()) / 2
    on_axis = np.abs(loop[:, 0]) + 1.0 * (loop[:, 1] < mid)
    i0 = int(np.argmin(on_axis))                    # from the middle of the face round the off-axis half to the foot
    i1 = int(np.argmin(np.abs(loop[:, 0]) + 1.0 * (loop[:, 1] > mid)))
    order = [(i0 + j) % k for j in range(k)]
    if loop[order[1], 0] < loop[order[-1], 0]:
        order = [order[0]] + order[:0:-1]
    half = [i for i in order if loop[i, 0] > 1e-5]
    t_top, t_foot = float(loop[i0, 1]), float(loop[i1, 1])
    outline = np.concatenate([[(0.0, t_top)], loop[half], [(0.0, t_foot)]])
    normals = np.concatenate([[(0.0, 1.0)], normal[half], [(0.0, -1.0)]])
    obj, n = _revolve(outline, normals, np.asarray(c, dtype=float), (sx, 0.0, 0.0), "hub", segs=36, closed=False)
    set_normals(obj, n)
    _shade(obj, shop.metal, normals=n)
    set_slot(obj, "char:metal")
    rigid(obj, "head")
    return obj


def _lamp(centre, r, s, color):
    """A small glowing lamp on the brow, above the window."""
    d = Vector((0, math.cos(0.72), math.sin(0.72)))
    bm = bmesh.new()
    res = bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=0.012 * s)
    bmesh.ops.scale(bm, vec=(1.0, 0.5, 1.0), verts=res["verts"])
    rot = Vector((0, 1, 0)).rotation_difference(d).to_matrix()
    bmesh.ops.rotate(bm, verts=res["verts"], cent=(0, 0, 0), matrix=rot)
    bmesh.ops.translate(bm, vec=centre + d * (r + 0.002 * s), verts=res["verts"])
    layer = bm.loops.layers.color.new("Color")
    for f in bm.faces:
        for loop in f.loops:
            loop[layer] = (*(min(1.0, x + 0.3) for x in color), 1.0)
    obj = mesh_from(bm, "lamp")
    set_slot(obj, "char:glow")
    rigid(obj, "head")
    return obj


def jetpack(fr, p, fitter, torso_key=None):
    """The jetpack on the back, fitted onto whatever is worn there; [housing and tanks (metal), thruster glow]."""
    s = fr.s
    color = hex3(p.get("jetpack", "#4a5866"))
    glow = hex3(p.get("glow", "#3affc0"))
    core, key = fitter.fit_torso(fr)
    back = P.torso_points(fr, core, [0.0], [1.3 * s], 0.0, back=True)[0]
    y0 = float(back[1]) - 0.016 * s                        # the housing's front face, clear of a back plate
    cy, dz = y0 - 0.045 * s, 0.0
    tanks = [(sx * 0.082 * s, cy - 0.026 * s) for sx in (1, -1)]

    def field(p_):
        x, y, z = p_[:, 0], p_[:, 1], p_[:, 2]
        body = P.box(x, z, 0.0, 1.29 * s, 0.1 * s, 0.14 * s, 0.03 * s)
        body = smax(body, np.abs(y - F(cy + 0.004 * s)) - F(0.044 * s), 0.012 * s)
        d = body
        for tx, ty in tanks:
            tank = capsule(p_, (tx, ty, 1.15 * s), (tx, ty, 1.43 * s), 0.056 * s)
            d = smin(d, tank, 0.01 * s)
            bell = round_cone(p_, (tx, ty, 1.12 * s), (tx, ty, 1.05 * s), 0.036 * s, 0.048 * s)
            bell = smax(bell, -round_cone(p_, (tx, ty, 1.12 * s), (tx, ty, 1.03 * s), 0.025 * s, 0.04 * s), 0.002)
            d = smin(d, bell, 0.006 * s)
            for zz in (1.2, 1.36):                              # bands round the tanks
                d = smin(d, smax(np.abs(z - F(zz * s)) - F(0.007 * s),
                                 np.sqrt((x - F(tx)) ** 2 + (y - F(ty)) ** 2) - F(0.061 * s), 0.002), 0.002)
        d = carve(d, smax(np.abs(z - F(1.29 * s)) - F(0.0015), F(cy - 0.034 * s) - y, 0.001), 0.001)
        return d

    lo = (-0.18 * s, cy - 0.12 * s, 0.98 * s)
    hi = (0.18 * s, y0 + 0.02 * s, 1.5 * s)
    obj = P.mesh(field, (lo, hi), "jetpack", (round(fr.height, 4), round(fr.build, 4), round(fr.weight, 4), key, round(y0, 4)),
                 SOURCES, h=0.0014, target=6000)
    from .helmets import _shade
    normals = field_normals(obj, field)
    set_normals(obj, normals)
    _shade(obj, color, normals=normals)
    set_slot(obj, "char:metal")
    fitter.bind(obj, stiff=1.0, keep=("chest", "spine"))
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    for tx, ty in tanks:
        res = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=0.039 * s, radius2=0.025 * s, depth=0.06 * s)
        bmesh.ops.translate(bm, vec=(tx, ty, 1.075 * s), verts=res["verts"])
    for f in bm.faces:
        for loop in f.loops:
            loop[layer] = (*(min(1.0, x + 0.25) for x in glow), 1.0)
    flame = mesh_from(bm, "thrusters")
    set_slot(flame, "char:glow")
    from .rig import read_weights, write_weights
    w = read_weights(obj)[0]
    write_weights(flame, [dict(w) for _ in flame.data.vertices])
    return [obj, flame]
