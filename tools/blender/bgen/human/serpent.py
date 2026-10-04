"""A serpent's body for ``tailShape: snake`` (Medusa): rooted high between the legs under the robe, it comes out
from under the back of the hem as thick as a thigh and lies on the ground in a long S beside and behind her, tapering
to a tip that curls up off the floor. Its section is flattened with a broad pale belly crossed by scutes; the back
carries a dark diamond chain. The robe's hem is lifted where the body passes under it, so the cloth drapes over it
instead of cutting through, and that part of the skirt is carried by the hips with the tail (rigid to the hips).
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .features import paint, spline, sweep
from .garments import mesh_from, set_slot
from .head import smooth
from .paint import hex3
from .rig import rigid

# The path (frame units: x right, y forward, z up; z is replaced by the lying height outside the robe).
INSIDE = [(0.0, 0.03, 0.44), (0.0, -0.04, 0.33), (0.0, -0.12, 0.2)]
OUTSIDE = [(0.07, -0.42), (0.24, -0.58), (0.36, -0.8), (0.3, -1.04), (0.1, -1.18), (-0.14, -1.2), (-0.32, -1.08),
           (-0.4, -0.92)]
CURL = [(-0.4, -0.84, 0.08), (-0.37, -0.79, 0.15)]


def _radius(t):
    return 0.09 * (1 - t) ** 0.8 + 0.009


def _skirts(garments):
    return [o for o in garments if o.type == "MESH" and o.name.startswith("robe_skirt")]


def _hem(fr, skirts):
    """(height of the hem, how far behind the body it hangs) from the skirt's lowest back vertices."""
    s = fr.s
    pts = [v.co for o in skirts for v in o.data.vertices if abs(v.co.x) < 0.06 * s]
    if not pts:
        return 0.1 * s, -0.25 * s
    z = min(p.z for p in pts)
    back = min(p.y for p in pts if p.z < z + 0.04 * s)
    return z, back


def tail(fr, p, garments=()):
    s = fr.s
    base = hex3(p["tail"])
    pale = tuple(c + (1 - c) * 0.6 for c in base)
    dark = tuple(c * 0.4 for c in base)
    skirts = _skirts(garments)
    hem_z, hem_y = _hem(fr, skirts)
    flat = 0.82
    n = 150
    # Lay the outside part on the ground: each point at the height of its own (flattened) radius.
    rough = [Vector(c) * s for c in INSIDE] + [Vector((x, y, 0.0)) * s for x, y in OUTSIDE] + [Vector(c) * s for c in CURL]
    path = spline(rough, n)
    path = [Vector((q.x, q.y, max(q.z, s * _radius(i / (n - 1)) * flat))) if q.z < 0.2 * s else q
            for i, q in enumerate(path)]
    path = spline(path[::6] + [path[-1]], n)

    def section(t, a):
        r = s * _radius(t)
        y = math.sin(a) * r * (1.12 if t > 0.12 else 1.0)
        x = math.cos(a) * r * (flat if math.cos(a) < 0 else 0.95)        # the flat belly underneath
        return x, y

    bm = bmesh.new()
    verts = sweep(bm, path, section, 22, up=Vector((0, 0, 1)))

    def tone(t, a):
        belly = smooth(0.15, 0.6, -math.cos(a))
        scute = 0.86 + 0.14 * (math.sin(t * 260) > -0.6)
        side = math.sin(a)
        back = smooth(0.2, 0.7, math.cos(a))
        ph = t * 38
        diamond = abs((ph % 1.0) - 0.5) * 2              # 0 at the middle of a diamond .. 1 at its ends
        on = back * (1.0 if abs(side) < 0.55 * (1 - diamond) + 0.05 else 0.0)
        c = tuple(x + (d - x) * on * 0.8 for x, d in zip(base, dark))
        c = tuple(x * (0.92 + 0.08 * math.sin(t * 520 + a * 3)) for x in c)
        return tuple(x + (q * scute - x) * belly for x, q in zip(c, pale))
    paint(bm, verts, tone)
    obj = mesh_from(bm, "tail")
    set_slot(obj, "char:skin")
    rigid(obj, "hips")
    if skirts:
        i_exit = min(range(n), key=lambda i: abs(path[i].y - hem_y))
        top = path[i_exit].z + s * _radius(i_exit / (n - 1)) * flat + 0.012 * s
        _lift(fr, skirts, hem_z, top - hem_z, path[i_exit].x)
    return obj


def _lift(fr, skirts, hem_z, lift, x0):
    """Raises the back of the hem by ``lift`` round where the serpent passes under it (at x ``x0``), tenting the
    cloth out over it, and hands that cloth over to the hips."""
    if lift <= 0:
        return
    s, j = fr.s, fr.j
    spine = j["spine"]
    height = max(0.3 * s, lift * 2.6)
    for o in skirts:
        me = o.data
        names = {g.index: g.name for g in o.vertex_groups}
        hips = o.vertex_groups.get("hips") or o.vertex_groups.new(name="hips")
        for v in me.vertices:
            dx, dy = v.co.x - x0, v.co.y - spine.y
            ang = math.atan2(dx, -dy)                 # 0 straight behind
            g = smooth(0.85, 0.35, abs(ang))
            k = g * smooth(hem_z + height, hem_z, v.co.z)
            if k <= 0:
                continue
            r = math.hypot(dx, dy)
            push = lift * 0.45 * k
            v.co.z += lift * k
            if r > 1e-6:
                v.co.x += dx / r * push
                v.co.y += dy / r * push
            # (groups named with a leading underscore are not bones: they are left alone)
            bones = [(names[g.group], g.weight) for g in v.groups if not names[g.group].startswith("_")]
            total = sum(x for _, x in bones) or 1.0
            for name, x in bones:
                o.vertex_groups[name].add([v.index], x / total * (1 - k), "REPLACE")
            hip = dict(bones).get("hips", 0.0) / total * (1 - k)
            hips.add([v.index], hip + k, "REPLACE")
        me.update()
