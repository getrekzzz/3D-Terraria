"""A ghost's hem (``wisps``: the colour the cloth fades to): the bottom of a robe's skirt fades from the cloth's own
colour into a pale mist, and from its torn edge wisps of it stream out and back, thinning to nothing and curling up
just off the ground, as if the figure drifted rather than walked (it has no feet: dress() leaves the boots off and
the legs end under the robe). The wisps follow the hem they hang from.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .features import paint, spline, sweep
from .garments import mesh_from, set_slot
from .head import smooth
from .paint import hex3
from .rig import read_weights, write_weights


def wisps(fr, garments, p):
    s = fr.s
    mist = hex3(p["wisps"])
    skirts = [o for o in garments if o.type == "MESH" and o.name.startswith("robe_skirt")]
    if not skirts:
        return []
    skirt = skirts[0]
    co = np.array([v.co[:] for v in skirt.data.vertices])
    bottom, top = co[:, 2].min(), co[:, 2].max()
    _fade(skirt, bottom, bottom + 0.5 * (top - bottom), mist)
    cy = fr.j["spine"].y
    ang = np.arctan2(co[:, 0], co[:, 1] - cy)
    weights = read_weights(skirt)
    cloth = hex3(p.get("robe", "#202028"))
    bm = bmesh.new()
    bm.loops.layers.color.new("Color")
    count = 22
    roots = []
    up = Vector((0, 0, 1))
    for k in range(count):
        a = 2 * math.pi * k / count + 0.09 * math.sin(k * 2.3)
        near = np.flatnonzero(np.abs((ang - a + np.pi) % (2 * np.pi) - np.pi) < 0.12)
        if not len(near):
            continue
        i = near[np.argmin(co[near, 2])]                  # the lowest point of the edge in that direction
        out = Vector((math.sin(a), math.cos(a), 0))
        root = Vector(co[i]) + out * 0.004 * s
        side = up.cross(out)
        behind = max(0.0, -math.cos(a))
        length = (0.16 + 0.06 * math.sin(k * 1.7) ** 2 + 0.2 * behind) * s
        trail = out * (0.8 - 0.4 * behind) + Vector((0, -1, 0)) * (0.25 + 0.75 * behind)
        trail.normalize()
        sway = (0.035 + 0.02 * math.sin(k * 2.9)) * s * (1 if k % 2 else -1)
        floor = 0.02 * s
        low = Vector((root.x, root.y, floor + 0.025 * s))
        pts = [root + up * 0.03 * s, root - up * 0.02 * s + out * 0.008 * s,
               low + trail * length * 0.3 - side * sway * 0.4, Vector((0, 0, floor)) + low.to_2d().to_3d()
               + trail * length * 0.65 + side * sway * 0.5, Vector((0, 0, floor + 0.05 * s)) + low.to_2d().to_3d()
               + trail * length + side * sway]
        pts = [Vector((q.x, q.y, max(q.z, floor))) for q in pts]
        path = spline(pts, 24)
        width = (0.032 + 0.006 * math.sin(k * 3.1)) * s

        def section(t, a_, width=width, k=k):
            ripple = 1 + 0.22 * math.sin(t * 9 + k)                # smoke thinning and swelling as it streams
            return math.cos(a_) * 0.002 * s * (1 - 0.6 * t), math.sin(a_) * width * (1 - t) ** 0.6 * ripple
        vs = sweep(bm, path, section, 6, up=out)
        paint(bm, vs, lambda t, a_: tuple(c + (m - c) * (0.5 + 0.5 * smooth(0.0, 0.6, t)) for c, m in zip(cloth, mist)))
        roots.append((len(bm.verts), weights[i]))
    obj = mesh_from(bm, "wisps")
    set_slot(obj, "char:cloth")
    out, start = [], 0
    for end, w in roots:
        out += [w] * (end - start)
        start = end
    write_weights(obj, out)
    return [obj]


def _fade(skirt, z0, z1, mist):
    """The skirt's colour from ``z1`` down to the hem at ``z0`` fades toward ``mist``, streaked."""
    me = skirt.data
    if "Color" not in me.color_attributes:
        return
    layer = me.color_attributes["Color"]
    for loop in me.loops:
        v = me.vertices[loop.vertex_index].co
        k = 0.85 * smooth(z1, z0, v.z) ** 1.3 * (0.75 + 0.25 * math.sin(math.atan2(v.x, v.y) * 11 + v.z * 40))
        c = layer.data[loop.index].color_srgb
        layer.data[loop.index].color_srgb = (c[0] + (mist[0] - c[0]) * k, c[1] + (mist[1] - c[1]) * k,
                                             c[2] + (mist[2] - c[2]) * k, 1.0)
