"""The Storm Diver's kit (helmets.py ``helmet: diver`` and scifi.py ``jetpack``):

- a big round metal helmet over the whole head with a wide window across the front, a gasket round the window, a hub
  at each side, a fin along the crown and a lamp on the brow; a glass visor domes over the window; it follows the
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
from .sdf import F, capsule, carve, round_cone, smax, smin, sphere

SOURCES = ("diverhelm.py",)
WINDOW = (1.08, -0.62, 0.52)        # the window: half its width (radians of yaw), its bottom and top (pitch)


def _window(d):
    """> 0 for directions (head frame: x right, y forward, z up) inside the window, about radians from its edge."""
    yaw = np.arctan2(d[..., 0], d[..., 1])
    pitch = np.arcsin(np.clip(d[..., 2], -1, 1))
    w, lo, hi = WINDOW
    mid, half = (lo + hi) / 2, (hi - lo) / 2
    return 1.0 - np.sqrt((yaw / w) ** 2 + ((pitch - mid) / half) ** 2)


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

    def field(p):
        q = p - c
        r = np.linalg.norm(q, axis=1)
        d = smax(r - F(radius + t), F(radius) - r, 0.0)
        d = smax(d, -(q @ n) - F(cut + 0.004 * s), 0.003)                       # open at the neck
        win = _window(q / np.maximum(r, F(1e-6))[:, None]) * F(radius)
        d = smax(d, win - F(0.004 * s), 0.004)                                   # the window
        gasket = smax(smax(r - F(radius + t + 0.006 * s), F(radius + 0.002 * s) - r, 0.0), np.abs(win + F(0.0)) - F(0.011 * s), 0.003)
        d = smin(d, gasket, 0.004)
        for sx in (1, -1):                                                       # the side hubs
            axis = np.asarray((sx, 0.0, 0.0), dtype=F)
            hub = smax(np.abs(q @ axis - F(radius + 0.008 * s)) - F(0.008 * s),
                       np.linalg.norm(q - (q @ axis)[:, None] * axis, axis=1) - F(0.04 * s), 0.003)
            d = smin(d, hub, 0.006)
            d = carve(d, smax(np.abs(np.linalg.norm(q - (q @ axis)[:, None] * axis, axis=1) - F(0.026 * s)) - F(0.0018),
                              F(radius + 0.012 * s) - q @ axis, 0.001), 0.001)
        fin = smax(smax(np.abs(q[:, 0]) - F(0.006 * s), r - F(radius + t + 0.014 * s), 0.004), F(0.25) * F(radius) - q[:, 2], 0.01)
        fin = smax(fin, F(radius + 0.002 * s) - r, 0.0)
        fin = smax(fin, q[:, 1] - F(0.02 * s), 0.01)
        return smin(d, fin, 0.006)

    R = radius + 0.04 * s
    bounds = (tuple(c - R), tuple(c + R))
    obj = P.mesh(field, bounds, "helmet", (shop.key, "diver", round(radius, 4), round(cut, 4)), SOURCES, h=0.0016, target=7000)
    _shade(obj, shop.metal)
    set_slot(obj, "char:metal")
    rigid(obj, "head")
    visor = shop.glass(centre, radius + 0.002 * s, cut, "visor",
                       window=lambda d: _window(np.asarray([d.x, d.y, d.z])) * radius + 0.006 * s)
    return [ring, obj, visor, _lamp(centre, radius + t, s, shop.glow)]


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
    _shade(obj, color)
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
