"""A ghost's hem (``wisps``: the colour the cloth fades to): the bottom of a robe's skirt fades from the cloth's own
colour into a pale mist, and below its edge broad tongues of that mist hang on down the skirt's flare, overlapping,
curved round it like the cloth, drifting back (longest behind) and rounding off into soft tips just off the ground,
as if the figure drifted rather than walked (with ``feet: none`` it has no feet: no boots and nothing of the legs
under the robe, which the hips carry instead, so in the walk it glides while the upper body strides). The wisps follow
the hem they hang from.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .features import paint, spline, sweep
from .garments import mesh_from, set_slot, strip_covered
from .head import smooth
from .paint import hex3
from .rig import read_weights, write_weights


def wisps(fr, garments, p, body=None):
    s = fr.s
    mist = hex3(p["wisps"])
    skirts = [o for o in garments if o.type == "MESH" and o.name.startswith("robe_skirt")]
    if not skirts:
        return []
    skirt = skirts[0]
    if p.get("feet") == "none":    # nothing of the legs left under the robe (their cut ends showed under the hem)
        if body is not None:
            strip_covered(body, [lambda co: co.z < fr.j["knee.R"].z + 0.1 * s])      # (up into the gap above)
        for o in skirts:
            _glide(o)
    co = np.array([v.co[:] for v in skirt.data.vertices])
    bottom, top = co[:, 2].min(), co[:, 2].max()
    _fade(skirt, bottom, bottom + 0.5 * (top - bottom), mist)
    cy = fr.j["spine"].y
    ang = np.arctan2(co[:, 0], co[:, 1] - cy)
    weights = read_weights(skirt)
    cloth = hex3(p.get("robe", "#202028"))
    hem_col = {}                                          # the faded cloth's colour at each vertex
    if "Color" in skirt.data.color_attributes:
        data = skirt.data.color_attributes["Color"].data
        for loop in skirt.data.loops:
            hem_col.setdefault(loop.vertex_index, tuple(data[loop.index].color[:3]))
    bm = bmesh.new()
    bm.loops.layers.color.new("Color")
    count = 16
    roots = []
    up, back = Vector((0, 0, 1)), Vector((0, -1, 0))
    floor = 0.015 * s
    edge_r = np.hypot(co[:, 0], co[:, 1] - cy)
    for k in range(count):
        a = 2 * math.pi * k / count + 0.08 * math.sin(k * 2.3)
        near = np.flatnonzero(np.abs((ang - a + np.pi) % (2 * np.pi) - np.pi) < 0.12)
        if not len(near):
            continue
        i = near[np.argmin(co[near, 2])]                  # the lowest point of the edge in that direction
        edge = Vector(co[i])
        out = Vector((math.sin(a), math.cos(a), 0))
        side = up.cross(out)
        r = float(edge_r[i])
        above = near[(co[near, 2] > edge.z + 0.03 * s) & (co[near, 2] < edge.z + 0.07 * s)]
        flare = (r - float(edge_r[above].max())) / (0.05 * s) if len(above) else 0.1   # the skirt's outward slope
        behind = max(0.0, -math.cos(a))
        length = (0.08 + 0.045 * math.sin(k * 1.7) ** 2 + 0.035 * behind) * s
        drop = max(0.0, min(length, edge.z - floor - 0.012 * s))
        trail = 0.5 * math.sqrt(max(0.0, length * length - drop * drop)) + 0.02 * behind * s
        sway = 0.012 * s * math.sin(k * 2.9)
        inset = 0.024 * s                                 # behind the cloth (and its folds) until below the hem
        pts = [edge + up * 0.03 * s - out * (0.03 * flare * s + inset), edge - out * inset]
        for f in (0.35, 0.7, 1.0):                        # on down the skirt's flare, drifting back
            q = (edge + out * (drop * f * min(flare, 0.4) - inset * (1 - f) + 0.004 * s * f) + back * trail * f ** 1.5
                 - up * drop * f + side * sway * f * f)
            pts.append(Vector((q.x, q.y, max(q.z, floor + 0.008 * s * f))))
        path = spline(pts, 20)
        width = 0.62 * 2 * math.pi * r / count * (1 + 0.12 * math.sin(k * 3.1))   # broad: neighbours overlap

        def section(t, a_, width=width, r=r, k=k):
            y = math.sin(a_) * width * math.sqrt(max(0.0, 1 - t * t)) * (1 - 0.4 * t) * (1 + 0.08 * math.sin(t * 7 + k))
            y *= 0.55 + 0.45 * smooth(0.0, 0.2, t)            # (narrower at the top, tucked behind the cloth)
            return math.cos(a_) * 0.0022 * s * (1 - 0.5 * t) - y * y / (2 * r), y   # curved round the skirt
        vs = sweep(bm, path, section, 12, up=out)
        c0 = hem_col.get(int(i), tuple(c + (m - c) * 0.7 for c, m in zip(cloth, mist)))
        paint(bm, vs, lambda t, a_, k=k, c0=c0: tuple(
            (c + (m - c) * smooth(0.0, 0.9, t)) * (0.93 + 0.07 * math.sin(a_ * 3 + k)) for c, m in zip(c0, mist)))
        roots.append((len(bm.verts), weights[i]))
    obj = mesh_from(bm, "wisps")
    set_slot(obj, "char:cloth")
    out, start = [], 0
    for end, w in roots:
        out += [w] * (end - start)
        start = end
    write_weights(obj, out)
    return [obj]


def _glide(o):
    """The robe carried by the hips instead of the legs (their share handed to the hips; the waist's weights at its top
    kept): there are no legs under it, and a stride would swing the hem and the wisps hanging from it out behind and
    to the side, and turn them edge-on under it."""
    legs = [g for g in o.vertex_groups if g.name.split(".")[0] in ("thigh", "shin", "foot", "toe")]
    if not legs:
        return
    hips = o.vertex_groups.get("hips") or o.vertex_groups.new(name="hips")
    index = {g.index for g in legs}
    for v in o.data.vertices:
        w = sum(g.weight for g in v.groups if g.group in index)
        if w > 0:
            hips.add([v.index], w, "ADD")
    for g in legs:
        o.vertex_groups.remove(g)


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
