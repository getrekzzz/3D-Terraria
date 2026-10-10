"""Alien heads in the head's distance field (head-local units, see headshape), chosen by ``alien``:

- martian: a grey's head (the Martian invaders, the Storm Diver): a big domed cranium swelling over the temples and
  the brow, a narrow face tapering to a small pointed chin, big dark almond eyes slanting up toward the temples, a
  low nose ridge with two slit nostrils, a small lipless mouth and no outer ears (only small openings);
- insect: a Vortexian's: the skull drawn back into a long crest with a keel along the top and a fin down each side,
  a narrow face with big eyes wrapping round toward the temples, a short beak-like muzzle and a mandible at each
  corner of the mouth.

The eyes are separate glossy lenses (``eyes``) set into sockets the field cuts for them, under a raised lid rim;
``paint`` colours the skin (darker round the eyes, a paler muzzle and throat), ``shade`` shades the head from its
field and ``paint_neck`` makes the body's neck under it the same colour and light. No brows, lashes, lips or ears.
"""
import math

import numpy as np

from .sdf import F, capsule, carve, ellipsoid, round_cone, smin, tube

# Eye lenses (head-local): centre, semi-axes (across to the outer corner, up, out of the face), yaw out from the
# face's front, tilt of the outer corner up, pitch of the lens down; how pointed the outer corner is.
EYES = {
    "martian": ((0.034, 0.0715, 0.006), (0.0285, 0.0152, 0.0115), 0.56, 0.42, 0.1, 0.46),
    "insect": ((0.04, 0.064, 0.01), (0.031, 0.0185, 0.0135), 0.8, 0.45, 0.08, 0.4),
}


def bounds(shape):
    """The box the head is meshed in."""
    if shape.alien == "insect":
        return (-0.1, -0.235, -0.3), (0.1, 0.14, 0.2)
    return (-0.105, -0.13, -0.3), (0.105, 0.135, 0.145)


def _frame(spec, sx):
    """The lens's axes: across toward the outer corner, up, out of the face."""
    _, _, yaw, tilt, pitch, _ = spec
    w = np.asarray((sx * math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), -math.sin(pitch)))
    u = np.asarray((sx * math.cos(yaw), -math.sin(yaw), 0.0))
    v = np.cross(u, w)
    v *= np.sign(v[2]) / np.linalg.norm(v)
    u, v = u * math.cos(tilt) + v * math.sin(tilt), v * math.cos(tilt) - u * math.sin(tilt)
    return u, v, w


def _taper(t, point):
    """The lens's height across it: rounder at the inner corner, drawn to a point at the outer one."""
    return 1.0 - point * np.clip(t, 0.0, 1.0) ** 2 + 0.06 * np.clip(-t, 0.0, 1.0)


def lens(p, kind, sx, grow=0.0, back=0.0):
    """Distance to an eye lens (grown by ``grow``, moved ``back`` into the head)."""
    spec = EYES[kind]
    c, r = np.asarray(spec[0]) * (sx, 1, 1), spec[1]
    u, v, w = _frame(spec, sx)
    q = p - (c - w * back).astype(F)
    lu, lv, lw = q @ u.astype(F), q @ v.astype(F), q @ w.astype(F)
    k = _taper(lu / F(r[0]), spec[5]).astype(F)
    return ellipsoid(np.stack([lu, lv / k, lw], 1), (0, 0, 0), (r[0] + grow, r[1] + grow, r[2] + grow))


def _neck(shape, p, d, top):
    """The person's neck, narrower under the skull (``top`` of its radius), with the same crisp jaw line; never
    closer than a few millimetres to the body's own slim neck core inside it (body.py), so that core stays hidden
    instead of showing through in patches."""
    r = shape.neck_r
    q = p * np.asarray((1.0, 1.08, 1.0), dtype=F)
    neck = round_cone(q, (0, -0.012 * 1.08, -0.06), (0, -0.03 * 1.08, -0.3), r * top, r)
    neck = smin(neck, capsule(p, (0, -0.024, -0.215), (0, -0.0055, -0.066), 0.68 * r + 0.0035), 0.01)
    k = (F(0.009) + F(0.011) * np.clip((F(0.015) - p[:, 1]) / F(0.03), 0, 1)).astype(F) * F(1.25)
    h = np.maximum(k - np.abs(d - neck), F(0)) / k
    return np.minimum(d, neck) - h * h * h * k / F(6.0)


def _martian(p):
    E = ellipsoid
    d = E(p, (0, -0.016, 0.04), (0.083, 0.1, 0.094))                             # the domed cranium
    d = smin(d, E(p, (0, 0.028, 0.052), (0.066, 0.064, 0.072)), 0.03)            # the high, bulging brow
    d = smin(d, E(p, (0, -0.064, 0.004), (0.058, 0.052, 0.06)), 0.03)            # occiput
    for sx in (1, -1):
        d = smin(d, E(p, (sx * 0.048, -0.004, 0.056), (0.036, 0.062, 0.052)), 0.03)   # swelling temples
        d = smin(d, E(p, (sx * 0.037, 0.054, -0.026), (0.019, 0.017, 0.014)), 0.022)  # cheekbones
        d = smin(d, capsule(p, (sx * 0.04, 0.03, -0.035), (sx * 0.016, 0.06, -0.082), 0.011), 0.02)  # jaw line
    d = smin(d, E(p, (0, 0.044, -0.046), (0.038, 0.04, 0.043)), 0.024)           # the narrow face
    d = smin(d, round_cone(p, (0, 0.05, -0.05), (0, 0.062, -0.086), 0.026, 0.012), 0.02)
    d = smin(d, E(p, (0, 0.064, -0.09), (0.013, 0.014, 0.012)), 0.016)           # the small pointed chin
    return smin(d, round_cone(p, (0, 0.082, -0.016), (0, 0.086, -0.038), 0.003, 0.0042), 0.01)   # a low nose


def _insect(p):
    E = ellipsoid
    d = E(p, (0, -0.01, 0.03), (0.072, 0.092, 0.084))                           # cranium
    crest = tube(p, [(0, -0.02, 0.05), (0, -0.09, 0.095), (0, -0.15, 0.132), (0, -0.2, 0.158)],
                 [0.068, 0.054, 0.034, 0.011])
    d = smin(d, crest, 0.04)                                                      # drawn back into a long crest
    d = smin(d, tube(p, [(0, 0.05, 0.085), (0, -0.04, 0.13), (0, -0.13, 0.158), (0, -0.2, 0.172)],
                     [0.007, 0.011, 0.008, 0.004]), 0.02)                          # the keel along the top
    for sx in (1, -1):
        d = smin(d, E(p, (sx * 0.052, -0.085, 0.085), (0.006, 0.07, 0.02), _tilt(sx, 0.55)), 0.02)    # side fins
        d = smin(d, E(p, (sx * 0.041, 0.05, -0.022), (0.02, 0.019, 0.016)), 0.02)  # cheek plates
        d = smin(d, capsule(p, (sx * 0.04, 0.025, -0.04), (sx * 0.015, 0.064, -0.084), 0.0105), 0.018)
    d = smin(d, E(p, (0, 0.046, -0.046), (0.038, 0.042, 0.042)), 0.024)          # the narrow face
    d = smin(d, round_cone(p, (0, 0.06, -0.04), (0, 0.094, -0.068), 0.024, 0.011), 0.016)   # the beak-like muzzle
    d = smin(d, E(p, (0, 0.07, -0.09), (0.012, 0.012, 0.012)), 0.01)
    return smin(d, round_cone(p, (0, 0.08, 0.01), (0, 0.098, -0.05), 0.005, 0.007), 0.012)


def _tilt(sx, pitch):
    """A rotation pitching an ellipsoid's long (y) axis up toward the back."""
    c, s = math.cos(pitch), math.sin(pitch)
    return np.asarray(((1, 0, 0), (0, c, s), (0, -s, c)), dtype=float)


def _mouth_line(shape, kind):
    """Points along the mouth (head-local), sunk just into the surface (found once per shape)."""
    line = getattr(shape, "_alien_mouth", None)
    if line is None:
        xs = np.linspace(-0.0135, 0.0135, 7) if kind == "insect" else np.linspace(-0.012, 0.012, 7)
        zs = (-0.0745 - 2.0 * xs * xs) if kind == "insect" else (-0.0725 + 3.0 * xs * xs)
        body = _insect if kind == "insect" else _martian
        a, b = np.stack([xs, xs * 0 + 0.02, zs], 1).astype(F), np.stack([xs, xs * 0 + 0.13, zs], 1).astype(F)
        lo, hi = np.zeros(len(xs), dtype=F), np.ones(len(xs), dtype=F)
        for _ in range(24):
            mid = (lo + hi) * F(0.5)
            inside = body(a + (b - a) * mid[:, None]) < 0
            lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
        line = shape._alien_mouth = [tuple(q) for q in a + (b - a) * lo[:, None] - np.asarray((0, 0.0004, 0), dtype=F)]
    return line


def field(shape, p, ears=True, face=True):
    """The alien head's distance at each point (``face``: eyes, nostrils and mouth; ``ears``: their openings)."""
    kind = shape.alien
    d = _insect(p) if kind == "insect" else _martian(p)
    d = _neck(shape, p, d, 0.72 if kind == "insect" else 0.78)
    if face:
        for sx in (1, -1):
            d = smin(d, lens(p, kind, sx, grow=0.0032, back=0.0034), 0.006)      # the lids' raised rim
            d = carve(d, lens(p, kind, sx, grow=0.0007), 0.003)                   # the socket the lens sits in
            if kind == "insect":         # a mandible at each corner of the mouth
                d = smin(d, round_cone(p, (sx * 0.02, 0.076, -0.07), (sx * 0.011, 0.093, -0.086), 0.0052, 0.0028), 0.004)
            else:                        # slit nostrils
                d = carve(d, ellipsoid(p, (sx * 0.0048, 0.0925, -0.0415), (0.0013, 0.0026, 0.0022)), 0.0012)
        line = _mouth_line(shape, kind)
        d = carve(d, tube(p, line, [0.0005, 0.0009, 0.0011, 0.0012, 0.0011, 0.0009, 0.0005]), 0.0016)
    if ears:
        for sx in (1, -1):
            d = carve(d, ellipsoid(p, (sx * 0.075, -0.016, -0.02), (0.006, 0.0045, 0.0055)), 0.003)
    return d


def eyes(head, kind):
    """The two lenses as Blender objects (world space), glossy and near-black with a sheen toward the top."""
    import bmesh
    from mathutils import Vector
    from .garments import mesh_from
    spec = EYES[kind]
    out = []
    for sx in (1, -1):
        bm = bmesh.new()
        col = bm.loops.layers.color.new("Color")
        c, r = np.asarray(spec[0]) * (sx, 1, 1), [v - 0.0003 for v in spec[1]]   # (a clear dark gap round it in the socket:
        u, v, w = _frame(spec, sx)                      # no two surfaces near enough to show through each other)
        rows, segs = 18, 48
        grid, tone = [], {}
        for i in range(rows + 1):
            th = math.pi * i / rows                     # from the lens's front pole back round its rim
            ring = []
            for k in range(segs if 0 < i < rows else 1):
                ph = 2 * math.pi * k / segs
                x, y, z = math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)
                lu = r[0] * x
                lv = r[1] * y * float(_taper(np.asarray(x), spec[5]))
                lw = r[2] * z
                vert = bm.verts.new(head.at(tuple(c + u * lu + v * lv + w * lw)))
                g = 0.5 + 0.5 * y
                tone[vert] = (0.03 + 0.05 * g * g, 0.045 + 0.07 * g * g, 0.06 + 0.08 * g * g)
                ring.append(vert)
            grid.append(ring)
        for a, b in zip(grid, grid[1:]):
            for k in range(segs):
                if len(a) == 1:
                    face = (a[0], b[k], b[(k + 1) % segs])
                elif len(b) == 1:
                    face = (a[k], b[0], a[(k + 1) % segs])
                else:
                    face = (a[k], b[k], b[(k + 1) % segs], a[(k + 1) % segs])
                f = bm.faces.new(face)
                for loop in f.loops:
                    loop[col] = (*tone[loop.vert], 1.0)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.normal_update()                  # shaded as if turned toward the front at its rim, where it meets the
        front = (Vector(head.at(tuple(c + w * 0.01))) - Vector(head.at(tuple(c)))).normalized()   # socket (no glint)
        bent = [tuple((v.normal + front * 0.9).normalized()) for v in bm.verts]
        obj = mesh_from(bm, f"eye_{sx}")
        obj.data.normals_split_custom_set_from_vertices(bent)
        out.append(obj)
    return out


def shade(obj, head):
    """Shades the head from its field over its broad smooth faces (as the surface it is, not the triangles a
    reduction left: no jagged glints round the eye sockets), keeping the mesh's own normals at its creases."""
    from .platefit import field_normals, set_normals
    c, h = np.asarray(head.c, dtype=F), float(head.h)
    set_normals(obj, field_normals(obj, lambda P: head.shape.field(((P - c) / F(h)).astype(F)) * F(h)))


def paint_neck(obj, head, fn):
    """Paints the body round the neck as the head is painted (``fn``) and shades it there from the head's field, so
    where the body's neck shows between the head's neck and the collar the two read as one surface (one colour, one
    light) instead of meeting at a jagged seam; fading back to the body's own paint and shading lower down."""
    from .paint import _ensure_layer
    from .platefit import set_normals
    from .sdf import gradient
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    vn = np.empty(len(co) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", vn)
    vn = vn.reshape(-1, 3)
    c, h = np.asarray(head.c, dtype=F), F(head.h)
    q = ((co - c) / h).astype(F)
    w = np.clip((q[:, 2] + F(0.34)) / F(0.08), 0, 1)
    w = w * w * (3 - 2 * w)
    near = np.flatnonzero(w > 0)
    if not len(near):
        return
    g = gradient(lambda P: head.shape.field(((P - c) / h).astype(F)) * h, co[near], 0.0005)
    g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
    t = np.clip(1 - np.abs(head.shape.field(q[near])) / F(0.012), 0, 1)
    t = (t * t * (3 - 2 * t) * w[near])[:, None]
    n = vn.copy()
    n[near] = vn[near] * (1 - t) + g * t
    set_normals(obj, n / np.maximum(np.linalg.norm(n, axis=1), 1e-9)[:, None])
    paint = {int(i): (float(w[i]), fn(me.vertices[int(i)].co)) for i in near}
    layer = _ensure_layer(me)
    for loop in me.loops:
        if loop.vertex_index in paint:
            k, col = paint[loop.vertex_index]
            old = layer.data[loop.index].color_srgb
            layer.data[loop.index].color_srgb = (*(a * (1 - k) + b * k for a, b in zip(old[:3], col)), old[3])


def _ramp(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


CREST = ((0.02, 0.09, 0.15, 0.2), (0.05, 0.095, 0.132, 0.158), (0.068, 0.054, 0.034, 0.011))   # -y, centre z, radius


def paint(head, kind, skin):
    """A painter for the alien skin: ``fn(world position) -> rgb``: dark round the eye lenses (right up to them, so
    no pale socket shows), a paler muzzle and throat fading out down the neck, a darker mottled crown, and on an
    insect dark bands across the top of the crest (not down its fins or the back of the skull) and darker fins."""
    from .paint import hex3, mix, mul
    base = hex3(skin)

    def fn(co):
        q = head.local(co)
        c = base
        pt = np.asarray([q[:]], dtype=F)
        for sx in (1, -1):                              # shadowed round the eyes, darkest at the lens
            near = float(lens(pt, kind, sx, grow=0.0007)[0])
            c = mix(c, mul(c, (0.62, 0.66, 0.66)), (1 - _ramp(near / 0.016)) * 0.75)
            c = mix(c, mul(c, (0.35, 0.37, 0.39)), 1 - _ramp((near - 0.0025) / 0.0035))
        muzzle = _ramp((q.y - 0.02) / 0.05) * _ramp((-0.02 - q.z) / 0.04)
        throat = _ramp((-0.1 - q.z) / 0.04) * _ramp((q.z + 0.21) / 0.05) * _ramp((q.y + 0.03) / 0.04)
        c = mix(c, mul(c, (1.12, 1.1, 1.04)), muzzle * 0.6)
        c = mix(c, mul(c, (1.08, 1.08, 1.02)), throat * 0.5)
        top = max(0.0, q.z - 0.06) / 0.08               # the crown a little darker, mottled
        mottle = 0.5 + 0.5 * math.sin(q.x * 160 + math.sin(q.y * 90) * 2) * math.sin(q.y * 140 + q.z * 70)
        c = mix(c, mul(c, (0.85, 0.88, 0.86)), min(1.0, top) * (0.4 + 0.3 * mottle))
        if kind == "insect":
            back = -q.y
            zc, rc = np.interp(back, CREST[0], CREST[1]), np.interp(back, CREST[0], CREST[2])
            crest = _ramp(((q.z - zc) / rc - 0.2) / 0.4) * _ramp((back - 0.01) / 0.06)
            stripe = min(1.0, max(0.0, math.cos(q.y * 110) * 1.6 + 0.2))      # bands across its top
            c = mix(c, mul(c, (0.36, 0.46, 0.46)), crest * stripe * 0.8)
            sx = 1 if q.x > 0 else -1                   # the fins a shade darker, fading out where they blend in
            fin = float(ellipsoid(pt, (sx * 0.052, -0.085, 0.085), (0.012, 0.074, 0.024), _tilt(sx, 0.55))[0])
            c = mix(c, mul(c, (0.74, 0.8, 0.8)), (1 - _ramp(fin / 0.008)) * 0.7)
        return tuple(min(1.0, x) for x in c)
    return fn


# antennae: control points (head-local, right side), root and tip radius
ANTENNAE = [(0.018, 0.083, 0.05), (0.024, 0.092, 0.085), (0.036, 0.082, 0.13), (0.056, 0.04, 0.172), (0.074, -0.02, 0.196),
            (0.086, -0.075, 0.2)]


def antennae(head, color):
    """Two jointed antennae rising from the brow and sweeping back over the crest, each ending in a small bulb;
    darker at the root, ringed along their length. One object, carried by the head."""
    import bmesh
    from .features import paint as tint, spline, sweep
    from .garments import mesh_from
    from .paint import hex3
    base = hex3(color)
    bm = bmesh.new()
    h = head.h
    for sx in (1, -1):
        path = spline([head.at((sx * x, y, z)) for x, y, z in ANTENNAE], 40)

        def section(t, a):
            r = h * (0.0042 - 0.0026 * t) * (1.0 + 0.12 * max(0.0, math.sin(t * math.pi * 9)) ** 4)
            return math.cos(a) * r, math.sin(a) * r
        verts = sweep(bm, path, section, 10, caps=(False, True))
        tint(bm, verts, lambda t, a: tuple(c * (0.55 + 0.5 * t) for c in base))
        res = bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.0062 * h)
        bmesh.ops.translate(bm, vec=path[-1], verts=res["verts"])
        layer = bm.loops.layers.color.get("Color")
        for f in {f for v in res["verts"] for f in v.link_faces}:
            for loop in f.loops:
                loop[layer] = (*(min(1.0, c * 1.25) for c in base), 1.0)
    return mesh_from(bm, "antennae")
