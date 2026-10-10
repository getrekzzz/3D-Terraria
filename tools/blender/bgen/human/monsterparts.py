"""The separate pieces of monster faces (monsterface.py), each bound to the head:

- cyclops: the one great eyeball, bloodshot, with a wide iris and a round pupil;
- void: two glowing almond eyes floating in the dark hollow (no eyeballs);
- ghoul: rows of crooked, yellowed teeth in the stretched mouth, the canines longest;
- brute: thread stitches crossing the scars, and a steel bolt with a hex collar out of each side of the neck.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from .features import paint as paint_sweep, round_section, sweep
from .garments import mesh_from, set_slot
from .monsterface import CYC_C, CYC_R, MOUTH, scars
from .paint import hex3
from .rig import rigid


def own_eyes(p):
    """True when the face makes its own eyes (no person's eyeballs)."""
    return p.get("face") in ("cyclops", "void")


def uv_sphere(bm, u, v):
    """A unit sphere of ``u`` segments round and ``v`` rings from pole to pole (z), built face by face in a fixed
    order (bmesh.ops.create_uvsphere orders its faces differently from run to run). Returns its verts."""
    top, bottom = bm.verts.new((0, 0, 1)), bm.verts.new((0, 0, -1))
    rings = [[bm.verts.new((math.sin(math.pi * i / v) * math.cos(2 * math.pi * k / u),
                            math.sin(math.pi * i / v) * math.sin(2 * math.pi * k / u), math.cos(math.pi * i / v)))
              for k in range(u)] for i in range(1, v)]
    for k in range(u):
        k1 = (k + 1) % u
        bm.faces.new((top, rings[0][k], rings[0][k1]))
        for r0, r1 in zip(rings, rings[1:]):
            bm.faces.new((r0[k], r1[k], r1[k1], r0[k1]))
        bm.faces.new((bottom, rings[-1][k1], rings[-1][k]))
    return [top, bottom] + [x for r in rings for x in r]


def build(head, p, lookup=None):
    """The pieces for ``p``'s face as finished objects (material and weights set)."""
    kind = p.get("face")
    out = []
    if kind == "cyclops":
        out.append(eyeball(head, head.at(CYC_C), CYC_R * head.h, hex3(p.get("eyes", "#ff3a3a"))))
    elif kind == "void":
        out.append(glow_eyes(head, hex3(p.get("eyes", "#90e0ff"))))
    elif kind == "ghoul":
        out.append(teeth(head, hex3(p.get("teeth", "#d8c890"))))
    elif kind == "brute":
        out.append(stitches(head))
        bolts = neck_bolts(head)
        if lookup is not None:
            from .rig import head_weights
            head_weights(bolts, head, *lookup)
        out.append(bolts)
    return out


def eyeball(head, centre, R, iris):
    """A big eyeball looking straight ahead: black pupil, an iris of radial fibres darkening to a black limbus,
    a sclera yellowed and threaded with red veins toward the back."""
    rings = [0.0, 0.1, 0.2, 0.22, 0.3, 0.4, 0.5, 0.56, 0.6, 0.66, 0.8, 1.0, 1.25, 1.55, 1.9, 2.4, 2.9, math.pi]
    seg = 48
    bm = bmesh.new()
    col = bm.loops.layers.color.new("Color")
    verts = []
    for a in rings:
        r = R * (1.0 + (0.05 * (1 - a / 0.62) ** 1.5 if a < 0.62 else 0.0))
        ring = []
        for k in range(seg if 0 < a < math.pi else 1):
            t = 2 * math.pi * k / seg
            ring.append(bm.verts.new((r * math.sin(a) * math.cos(t), r * math.cos(a), r * math.sin(a) * math.sin(t))))
        verts.append(ring)

    def band(a0, a1, t):
        mid = (a0 + a1) / 2
        if mid < 0.21:
            return (0.01, 0.008, 0.008)
        if mid < 0.58:
            k = (mid - 0.21) / 0.37
            spoke = 0.75 + 0.45 * (0.5 + 0.5 * math.sin(t * 31.0)) * (0.5 + 0.5 * math.sin(t * 13.0 + 1.1))
            return tuple(min(1.0, c * spoke * (1.3 - 0.75 * k)) for c in iris)
        if mid < 0.63:
            return tuple(c * 0.22 for c in iris)
        vein = max(0.0, math.sin(t * 7.0 + 1.3 * math.sin(t * 3.0)) ** 24) + max(0.0, math.sin(t * 11.0 + 0.7) ** 30)
        red = min(1.0, (mid - 0.63) / 1.2) * 0.6 + min(1.0, vein) * min(1.0, (mid - 0.63) / 0.5)
        return (0.88, 0.8 - 0.55 * red, 0.66 - 0.5 * red)

    for i in range(len(rings) - 1):
        a, b = verts[i], verts[i + 1]
        for k in range(seg):
            t = 2 * math.pi * (k + 0.5) / seg
            if len(a) == 1:
                face = bm.faces.new((a[0], b[(k + 1) % seg], b[k]))
            elif len(b) == 1:
                face = bm.faces.new((a[k], a[(k + 1) % seg], b[0]))
            else:
                face = bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
            c = band(rings[i], rings[i + 1], t)
            for loop in face.loops:
                loop[col] = (*c, 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.translate(bm, verts=bm.verts, vec=centre)
    obj = mesh_from(bm, "eye_cyclops")
    set_slot(obj, "char:eye")
    rigid(obj, "head")
    return obj


def glow_eyes(head, color):
    """Two glowing almond eyes, slanted, deep in the void of the face."""
    bm = bmesh.new()
    col = bm.loops.layers.color.new("Color")
    h = head.h
    for sx in (1, -1):
        vs = uv_sphere(bm, 16, 10)
        tilt = math.radians(14) * sx
        for v in vs:
            x, y, z = v.co.x * 0.0158, v.co.y * 0.0035, v.co.z * 0.0068
            z *= 1 - 0.35 * max(0.0, -v.co.z) * abs(v.co.x)       # flatter underneath: an almond
            x, z = x * math.cos(tilt) - z * math.sin(tilt), x * math.sin(tilt) + z * math.cos(tilt)
            v.co = head.at((sx * 0.028 + x, 0.07 + y, -0.006 + z))
        for f in {f for v in vs for f in v.link_faces}:
            for loop in f.loops:
                loop[col] = (*color, 1.0)
    obj = mesh_from(bm, "eye_glow")
    set_slot(obj, "char:glow")
    rigid(obj, "head")
    return obj


def teeth(head, color):
    """Upper and lower rows clenched across the mouth's arch, crooked and of uneven length, rooted in the gums."""
    from .monsterface import ARCH
    c, r = MOUTH
    bm = bmesh.new()
    bm.loops.layers.color.new("Color")
    n = 10
    yq = c[1] - r[1] * 0.45                     # (in the arched space the mouth was cut in)
    for row, sz in enumerate((1, -1)):
        for k in range(n):
            x = (k - (n - 1) / 2) / ((n - 1) / 2) * (r[0] - 0.0075)
            y = yq - ARCH * x * x
            hz = r[2] * math.sqrt(max(0.0, 1 - (x / r[0]) ** 2 - 0.2))
            jag = math.sin(k * 2.3 + row * 1.7)
            canine = math.exp(-((abs(x) - 0.019) / 0.004) ** 2)
            root = c[2] + sz * (hz + 0.005)
            tip = c[2] - sz * (0.0009 + 0.0022 * canine) + sz * 0.0012 * (1 - jag) * (1 - canine)
            lean = 0.0011 * math.sin(k * 3.1 + row)
            path = [head.at((x + lean * t, y + 0.0008 * t, root + (tip - root) * t)) for t in np.linspace(0, 1, 6)]
            w = (0.0039 + 0.0004 * math.cos(k * 1.9)) * head.h * (0.85 if canine > 0.5 else 1.0)

            def radius(t, w=w, canine=canine):
                return w * (1.0 - (0.35 + 0.5 * canine) * t ** 1.8)

            def flat(t, a):
                return 1.0 if abs(math.sin(a)) > 0.7 else 0.62
            vs = sweep(bm, path, round_section(radius, flat), 8, up=Vector((0, 1, 0)))
            stain = 0.8 + 0.2 * math.sin(k * 1.3 + row * 2)
            paint_sweep(bm, vs, lambda t, a, stain=stain: tuple(min(1.0, q * stain * (0.72 + 0.35 * t)) for q in color))
    obj = mesh_from(bm, "teeth")
    set_slot(obj, "char:bone")
    rigid(obj, "head")
    return obj


def stitches(head):
    """Dark thread stitches straddling each scar, sunk into the skin at both ends."""
    bm = bmesh.new()
    bm.loops.layers.color.new("Color")
    h = head.h
    for line, normals in scars(head.shape) or ():
        pts = [Vector(q) for q in line]
        for i in range(len(pts) - 1):
            for t in (0.25, 0.75):
                p = pts[i].lerp(pts[i + 1], t)
                tang = (pts[i + 1] - pts[i]).normalized()
                nrm = Vector(normals[i]).normalized()
                nrm = (nrm - tang * nrm.dot(tang)).normalized()
                side = tang.cross(nrm).normalized()
                arc = [p + side * 0.0042 * u + nrm * (0.0012 * (1 - u * u) - 0.0012 * u * u) + tang * 0.0007 * u
                       for u in np.linspace(-1, 1, 7)]
                vs = sweep(bm, [head.at(a[:]) for a in arc], round_section(lambda u: 0.00062 * h), 6)
                paint_sweep(bm, vs, lambda u, a: (0.06, 0.05, 0.045))
    obj = mesh_from(bm, "stitches")
    set_slot(obj, "char:leather")
    rigid(obj, "head")
    return obj


def neck_bolts(head):
    """A steel bolt out of each side of the neck: a rounded shaft standing out from a hex collar on the skin."""
    shape = head.shape
    z, y = -0.098, -0.03
    hits = shape._surface(np.asarray([(0.0, y, z), (0.0, y, z)]), np.asarray([(0.14, y, z), (-0.14, y, z)]))
    bm = bmesh.new()
    bm.loops.layers.color.new("Color")
    h = head.h
    steel, dark = (0.5, 0.52, 0.55), (0.2, 0.19, 0.18)
    for hit, sx in zip(hits, (1, -1)):
        base = Vector(hit[:])
        out = Vector((sx, 0.0, 0.0))
        shaft = [head.at(base - out * 0.008 + out * t) for t in np.linspace(0, 0.048, 8)]
        vs = sweep(bm, shaft, round_section(lambda u: 0.0072 * h * (1.0 - 0.12 * u)), 16, caps=(False, True))
        paint_sweep(bm, vs, lambda u, a: tuple(c * (0.8 + 0.25 * u) * (0.92 + 0.08 * math.cos(a * 2)) for c in steel))
        collar = [head.at(base - out * 0.004 + out * t) for t in np.linspace(0, 0.011, 4)]

        def hexa(u, a):
            k = math.cos(math.pi / 6) / math.cos(((a + math.pi / 6) % (math.pi / 3)) - math.pi / 6)
            return k
        vs = sweep(bm, collar, round_section(lambda u: 0.0125 * h, hexa), 24)
        paint_sweep(bm, vs, lambda u, a: tuple(c * (0.85 + 0.15 * u) for c in dark))
    obj = mesh_from(bm, "neck_bolts")
    set_slot(obj, "char:metal")
    rigid(obj, "head")
    return obj

