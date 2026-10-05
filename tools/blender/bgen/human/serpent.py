"""A serpent's body for ``tailShape: snake`` (Medusa, who has ``feet: none``): her legs end at the hips and a serpent's
trunk as wide as her hips comes down inside the robe to the floor, turns back and comes out from under the back of
the hem, lying on the ground in a long S behind her and tapering to a tip that curls up off the floor. Its section is
flattened with a broad pale belly crossed by scutes (facing forward on the trunk); the back carries a dark diamond
chain. The robe's skirt hangs from her hips like a bell over the trunk (carried by the hips, as the trunk is, so in
the walk her upper body strides while she glides and nothing passes through the cloth), and its hem is lifted where
the body passes under it so the cloth drapes over it.
"""
import math

import bmesh
from mathutils import Vector

from .features import paint, spline, sweep
from .garments import mesh_from, set_slot, strip_covered
from .head import smooth
from .paint import hex3
from .rig import rigid

# The path (frame units: x right, y forward, z up; z is replaced by the lying height on the ground): the trunk inside
# the robe, the turn on the floor and the part lying outside.
TRUNK = [(0.0, 0.0, 0.95), (0.0, 0.0, 0.64), (0.0, 0.012, 0.38), (0.0, 0.006, 0.17), (0.0, -0.06, 0.0)]
OUTSIDE = [(0.02, -0.2), (0.05, -0.32), (0.07, -0.42), (0.24, -0.58), (0.36, -0.8), (0.3, -1.04), (0.1, -1.18),
           (-0.14, -1.2), (-0.32, -1.08), (-0.4, -0.92)]
CURL = [(-0.4, -0.84, 0.08), (-0.37, -0.79, 0.15)]
TOP, FLOOR = 0.112, 0.095          # the trunk's radius under the hips and where it reaches the floor


def _radius(t, t_floor):
    if t < t_floor:
        return TOP + (FLOOR - TOP) * t / t_floor
    u = (t - t_floor) / (1 - t_floor)
    return (FLOOR - 0.009) * (1 - u) ** 0.85 + 0.009


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


def tail(fr, body, p, garments=()):
    s = fr.s
    base = hex3(p["tail"])
    pale = tuple(c + (1 - c) * 0.6 for c in base)
    dark = tuple(c * 0.4 for c in base)
    skirts = _skirts(garments)
    hem_z, hem_y = _hem(fr, skirts)
    cut = fr.j["hip.R"].z + 0.06 * s
    strip_covered(body, [lambda co: co.z < cut])          # no legs under the robe (nor hips that move with them)
    for o in [body] + [o for o in garments if o.type == "MESH" and o not in skirts]:
        _hips_carry(o, cut + 0.14 * s, 0.06 * s)           # (the body left at the waist, the bodice and sash on it)
    for o in skirts:
        _hang_from_hips(o)
    flat = 0.82
    n = 160
    rough = ([Vector(c) * s for c in TRUNK] + [Vector((x, y, 0.0)) * s for x, y in OUTSIDE]
             + [Vector(c) * s for c in CURL])
    path = spline(rough, n)
    lengths = [0.0]
    for a, b in zip(path, path[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    t_of = [x / lengths[-1] for x in lengths]
    i_floor = min(range(n), key=lambda i: path[i].z)
    t_floor = t_of[i_floor] * 0.9
    # On the ground each point lies at the height of its own (flattened) radius.
    path = [Vector((q.x, q.y, max(q.z, s * _radius(t_of[i], t_floor) * flat))) if q.z < 0.2 * s else q
            for i, q in enumerate(path)]
    path = spline(path[::6] + [path[-1]], n)
    lengths = [0.0]
    for a, b in zip(path, path[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    t_of = [x / lengths[-1] for x in lengths]

    def section(t, a):
        r = s * _radius(t, t_floor)
        y = math.sin(a) * r * 1.08
        x = math.cos(a) * r * (flat if math.cos(a) < 0 else 0.95)        # the flat belly underneath
        return x, y

    bm = bmesh.new()
    verts = sweep(bm, path, section, 24, up=Vector((0, -1, 0)))

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
        ground = [i for i in range(n) if path[i].z < 0.2 * s and path[i].y < 0]
        i_exit = min(ground, key=lambda i: abs(path[i].y - hem_y))
        top = path[i_exit].z + s * _radius(t_of[i_exit], t_floor) * flat + 0.014 * s
        _lift(fr, skirts, hem_z, top - hem_z, path[i_exit].x)
    return obj


def _hang_from_hips(o):
    """The skirt carried by the hips alone (bone groups other than hips dropped; '_' groups are not bones)."""
    for g in list(o.vertex_groups):
        if g.name != "hips" and not g.name.startswith("_"):
            o.vertex_groups.remove(g)
    hips = o.vertex_groups.get("hips") or o.vertex_groups.new(name="hips")
    hips.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")


def _hips_carry(body, top, blend):
    """What is left of the body (or a garment on it) below ``top`` moves with the hips, not with the hidden legs
    (fading in over ``blend``), so nothing under the robe swings out through it in walk."""
    legs = {g.index: g for g in body.vertex_groups if g.name.split(".")[0] in ("thigh", "shin", "foot", "toe")}
    if not legs:
        return
    hips = body.vertex_groups.get("hips") or body.vertex_groups.new(name="hips")
    for v in body.data.vertices:
        k = smooth(top, top - blend, v.co.z)
        own = [(g.group, g.weight) for g in v.groups if g.group in legs]
        if k <= 0 or not own:
            continue
        for gi, wt in own:
            legs[gi].add([v.index], wt * (1 - k), "REPLACE")
        hips.add([v.index], sum(wt for _, wt in own) * k, "ADD")


def _lift(fr, skirts, hem_z, lift, x0):
    """Raises the back of the hem by ``lift`` round where the serpent passes under it (at x ``x0``), tenting the
    cloth out over it."""
    if lift <= 0:
        return
    s, j = fr.s, fr.j
    spine = j["spine"]
    height = max(0.3 * s, lift * 2.6)
    for o in skirts:
        me = o.data
        for v in me.vertices:
            dx, dy = v.co.x - x0, v.co.y - spine.y
            ang = math.atan2(dx, -dy)                 # 0 straight behind
            k = smooth(0.85, 0.35, abs(ang)) * smooth(hem_z + height, hem_z, v.co.z)
            if k <= 0:
                continue
            r = math.hypot(dx, dy)
            push = lift * 0.45 * k
            v.co.z += lift * k
            if r > 1e-6:
                v.co.x += dx / r * push
                v.co.y += dy / r * push
        me.update()
