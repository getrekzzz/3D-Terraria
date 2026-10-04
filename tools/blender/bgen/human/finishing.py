"""Finishing a tailored garment (tailoring.py): the shaped cloth reduced to its triangle budget (which keeps detail
where the cloth folds), then stitched seams pressed into it, bands swept along its open edges (ribbed collars and
cuffs, hems, turned-down boot rims), soles on boots and buttons down the plackets.

Seams and soles are cut into the mesh along planes and coloured per face corner, so their edges stay crisp at any
triangle size. Bands belong to the garment's mesh and carry the skin weights of the edge they finish; buttons are
separate parts (their own material) weighted like the cloth under them.
"""
import math

import bmesh
from mathutils import Vector
from mathutils.kdtree import KDTree

from . import tatters

BUDGET = {"shirt": 10000, "coat": 10000, "pants": 8000, "boots": 3200, "robe": 15000}

# Band profiles: (u, w) in units of the frame scale, u along the surface past the garment's edge, w out from it;
# each runs from on top of the cloth, over the edge, back underneath.
COLLAR = ((-0.012, 0.0006), (-0.007, 0.003), (0.001, 0.0042), (0.009, 0.0038), (0.0135, 0.0018), (0.0125, -0.0008),
          (0.005, -0.002), (-0.003, -0.0016))
STAND_COLLAR = ((-0.014, 0.0008), (-0.008, 0.004), (0.004, 0.0062), (0.018, 0.0085), (0.026, 0.0095), (0.0275, 0.007),
                (0.02, 0.0035), (0.006, 0.001))
CUFF = ((-0.028, 0.0004), (-0.025, 0.0032), (-0.013, 0.0038), (-0.002, 0.0036), (0.0015, 0.002), (0.002, -0.0008),
        (-0.005, -0.0018))
HEM = ((-0.016, 0.0004), (-0.013, 0.0024), (-0.003, 0.0027), (0.0005, 0.0015), (0.001, -0.0008), (-0.005, -0.0014))
RIM = ((-0.038, 0.0005), (-0.035, 0.006), (-0.016, 0.0072), (-0.002, 0.008), (0.003, 0.006), (0.0038, 0.0008),
       (0.001, -0.002))


def reduce(obj, budget):
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    keep = obj.vertex_groups.get("_keep")          # less weight, less reduced (torn edges, see tatters)
    if tris > budget:
        dec = obj.modifiers.new("decimate", "DECIMATE")
        dec.ratio = budget / tris
        dec.use_collapse_triangulate = True
        if keep is not None:
            dec.vertex_group, dec.vertex_group_factor = keep.name, 30.0
        from .blobs import apply_modifiers
        apply_modifiers(obj)
    if obj.vertex_groups.get("_keep") is not None:
        obj.vertex_groups.remove(obj.vertex_groups["_keep"])


def shade_corners(faces, layer, k):
    for f in faces:
        for loop in f.loops:
            c = loop[layer]
            loop[layer] = (c[0] * k, c[1] * k, c[2] * k, c[3])


def stitch(bm, layer, point, normal, keep, width, shade=0.8, depth=0.0):
    """A seam along the plane (point, normal) over the faces whose centres pass ``keep``: the mesh is cut along two
    planes ``width`` apart and the strip between them darkened (and pressed in by ``depth``)."""
    point, normal = Vector(point), Vector(normal).normalized()
    for off in (-0.5 * width, 0.5 * width):
        faces = [f for f in bm.faces if keep(f.calc_center_median())]
        geom = list({e for f in faces for e in f.edges}) + list({v for f in faces for v in f.verts}) + faces
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=point + normal * off, plane_no=normal)
    strip = [f for f in bm.faces if abs((f.calc_center_median() - point).dot(normal)) < 0.5 * width
             and keep(f.calc_center_median())]
    shade_corners(strip, layer, shade)
    if depth:
        bm.normal_update()
        for v in {v for f in strip for v in f.verts}:
            v.co -= v.normal * depth


def open_loops(bm):
    """The garment's open edges chained into loops: [(verts, closed)]."""
    links = {}
    for e in bm.edges:
        if e.is_boundary:
            a, b = e.verts
            links.setdefault(a, []).append(b)
            links.setdefault(b, []).append(a)
    seen, out = set(), []
    for start in links:
        if start in seen:
            continue
        loop, prev, cur = [start], None, start
        seen.add(start)
        while True:
            nxt = [v for v in links[cur] if v is not prev and v not in seen]
            if not nxt:
                break
            prev, cur = cur, nxt[0]
            loop.append(cur)
            seen.add(cur)
        out.append((loop, len(loop) > 2 and start in links[cur]))
    return out


def band(bm, layer, deform, loop, closed, profile, s, prefer, axis, color, step, ribs=False, edge_shade=1.0):
    """Sweeps ``profile`` along the loop of edge vertices, resampled every ``step``; u runs along ``prefer`` laid
    onto the cloth, w along the cloth's normal, turned out from the line ``axis`` (point, direction) the garment
    wraps round (a limb, the neck, the torso). Ribbed bands alternate their shade."""
    pts = [v.co.copy() for v in loop]
    n = len(pts)
    # Normals averaged along the edge and turned outward, so a crumpled corner of the decimated cloth can't twist
    # the band.
    raw = [v.normal.copy() for v in loop]
    nrm = []
    for i in range(n):
        nn = sum((raw[(i + d) % n] for d in range(-4, 5)), Vector()) if closed else raw[i].copy()
        r = pts[i] - axis[0]
        r -= axis[1] * r.dot(axis[1])
        nn -= axis[1] * nn.dot(axis[1]) * 0.5
        nrm.append((nn if nn.dot(r) > 0 else -nn).normalized() if nn.length > 1e-6 else r.normalized())
    for _ in range(3):         # even out the decimated edge
        pts = [pts[i] if not closed and i in (0, n - 1) else (pts[i - 1] + pts[i] * 2 + pts[(i + 1) % n]) * 0.25 for i in range(n)]
    segs = n if closed else n - 1
    length = [0.0]
    for i in range(segs):
        length.append(length[-1] + (pts[(i + 1) % n] - pts[i]).length)
    count = max(int(length[-1] / step), 3)
    rings, normals, i = [], [], 0
    for k in range(count + (0 if closed else 1)):
        d = length[-1] * k / count
        while i < segs - 1 and length[i + 1] < d:
            i += 1
        a = (d - length[i]) / max(length[i + 1] - length[i], 1e-9)
        v0, v1 = loop[i], loop[(i + 1) % n]
        p = pts[i].lerp(pts[(i + 1) % n], a)
        nn = nrm[i].lerp(nrm[(i + 1) % n], a).normalized()
        u = (prefer - nn * prefer.dot(nn)).normalized()
        src = v0 if a < 0.5 else v1
        ring = []
        for pu, pw in profile:
            nv = bm.verts.new(p + u * (pu * s) + nn * (pw * s))
            for g, wt in src[deform].items():
                nv[deform][g] = wt
            ring.append(nv)
        rings.append(ring)
        normals.append(nn)
    faces = []
    for k in range(len(rings) - (0 if closed else 1)):
        r0, r1 = rings[k], rings[(k + 1) % len(rings)]
        row = []
        for m in range(len(profile) - 1):
            f = bm.faces.new((r0[m], r1[m], r1[m + 1], r0[m + 1]))
            k_shade = (0.84 if ribs and k % 2 else 1.0) * (edge_shade if m == 0 else 1.0)
            for lp in f.loops:
                lp[layer] = (color[0] * k_shade, color[1] * k_shade, color[2] * k_shade, 1.0)
            row.append(f)
        faces.append(row)
    # Outward on the cloth side: the band's top faces must mostly face along the cloth's normal.
    agree = 0
    for k, row in enumerate(faces):
        row[1].normal_update()
        agree += 1 if row[1].normal.dot(normals[k]) >= 0 else -1
    if agree < 0:
        for row in faces:
            for f in row:
                f.normal_flip()


def buttons(src_bm, deform, points, s, radius, color, name, slot, groups):
    """Small domed discs sitting on the cloth at the vertices nearest ``points``, weighted like them."""
    from .garments import colorize, mesh_from, set_slot
    src_bm.verts.ensure_lookup_table()
    kd = KDTree(len(src_bm.verts))
    for v in src_bm.verts:
        kd.insert(v.co, v.index)
    kd.balance()
    bm = bmesh.new()
    dl = bm.verts.layers.deform.verify()
    for p in points:
        _, idx, _ = kd.find(Vector(p))
        v = src_bm.verts[idx]
        n = v.normal.normalized()
        a = n.orthogonal().normalized()
        b = n.cross(a)
        rim = [a * math.cos(k * math.pi / 4) + b * math.sin(k * math.pi / 4) for k in range(8)]
        rings = [[bm.verts.new(v.co + d * radius * rr * s + n * h * s) for d in rim]
                 for rr, h in ((0.9, -0.0004), (1.0, 0.0006), (0.8, 0.0016))]
        top = bm.verts.new(v.co + n * 0.0019 * s)
        faces = [bm.faces.new((r0[k], r0[(k + 1) % 8], r1[(k + 1) % 8], r1[k])) for r0, r1 in zip(rings, rings[1:]) for k in range(8)]
        faces += [bm.faces.new((rings[-1][k], rings[-1][(k + 1) % 8], top)) for k in range(8)]
        faces[-1].normal_update()
        if faces[-1].normal.dot(n) < 0:
            for f in faces:
                f.normal_flip()
        for nv in [x for r in rings for x in r] + [top]:
            for g, wt in v[deform].items():
                nv[dl][g] = wt
    colorize(bm, color)
    obj = mesh_from(bm, name)
    for g in groups:
        obj.vertex_groups.new(name=g)
    set_slot(obj, slot)
    return obj


def _front(bm, x, z, s):
    """The cloth's front-most point near (x, z)."""
    near = [v.co for v in bm.verts if abs(v.co.x - x) < 0.008 * s and abs(v.co.z - z) < 0.008 * s and v.co.y > 0]
    return max(near, key=lambda c: c.y, default=None)


def finish(w, obj, kind, color):
    """Reduces the garment, then adds its seams, bands, soles and buttons; returns the extra parts (buttons)."""
    from .paint import hex3
    j, s = w.f.j, w.f.s
    hidden = kind == "shirt" and "coat" in w.p          # a shirt under a coat shows only at the collar
    robe = bool(w.p.get("robe"))                        # a robe's bodice, long and gathered by the sash
    reduce(obj, BUDGET[kind] // 2 if hidden else BUDGET["robe" if robe and kind == "shirt" else kind])
    if hidden:
        return []
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.loops.layers.color.get("Color")
    deform = bm.verts.layers.deform.verify()
    base = hex3(color) if isinstance(color, str) else tuple(color)
    tone = lambda k: tuple(c * k for c in base)
    trim = hex3(w.p["trim"]) if w.p.get("trim") and (robe or kind == "coat") else None
    edge = trim or tone(0.9)             # collars and cuffs: the trim colour on robes and coats that have one
    width, press = 0.0032 * s, 0.0006 * s
    up, fwd = Vector((0, 0, 1)), Vector((0, 1, 0))
    extras = []
    if kind in ("shirt", "coat"):
        stitch(bm, layer, (0, j["chest"].y, 0), fwd, lambda c: not w._on_arm(c) and c.z < w.collar_z - 0.006 * s, width, depth=press)
        arms = {}
        for side, sx in (("R", 1), ("L", -1)):
            sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
            axis = (wr - sh).normalized()
            ahead = (fwd - axis * axis.dot(fwd)).normalized()
            out = axis.cross(ahead) * (1 if axis.cross(ahead).z > 0 else -1)
            arms[side] = axis
            stitch(bm, layer, sh + (wr - sh) * 0.06, axis,
                   lambda c, sh=sh, sx=sx: (c - sh).length < 0.13 * s and c.x * sx > abs(sh.x) * 0.55, width, depth=press)
            stitch(bm, layer, sh, ahead,
                   lambda c, side=side, sh=sh, out=out: (w.arm_t(c, side) or 0) > 0.08 and (c - sh).dot(out) < 0, width, depth=press)
        top = w.collar_z
        if kind == "shirt" and not robe:          # a short buttoned placket down from the collar
            keep = lambda c: c.y > 0 and top - 0.11 * s < c.z < top + 0.02 * s and abs(c.x) < 0.05 * s
            for x in (-0.011 * s, 0.011 * s):
                stitch(bm, layer, (x, 0, 0), (1, 0, 0), keep, width * 0.8)
            shade_corners([f for f in bm.faces if keep(f.calc_center_median()) and abs(f.calc_center_median().x) < 0.011 * s], layer, 0.93)
            spots = [_front(bm, 0.0, top - d * s, s) for d in (0.03, 0.058, 0.086)]
            extras.append(buttons(bm, deform, [p for p in spots if p], s, 0.0048, (0.17, 0.12, 0.09), "buttons", "char:bone",
                                  [g.name for g in obj.vertex_groups]))
        elif kind == "coat":                      # closed down the front: the overlap's edge and a row of brass buttons
            stitch(bm, layer, (0.004 * s, 0, 0), (1, 0, 0), lambda c: c.y > 0 and c.z < top, 0.004 * s, shade=0.55)
            spots = [_front(bm, -0.012 * s, top - d * s, s) for d in (0.05, 0.12, 0.19, 0.26)]
            extras.append(buttons(bm, deform, [p for p in spots if p and p.z > w.waist], s, 0.0065, (0.69, 0.54, 0.25), "buttons",
                                  "char:metal", [g.name for g in obj.vertex_groups]))
    elif kind == "pants":
        crotch = j["hip.R"].z - 0.09 * s
        stitch(bm, layer, (0, -0.003 * s, 0), fwd, lambda c: True, width, depth=press)
        stitch(bm, layer, (0, 0, 0), (1, 0, 0), lambda c: c.z > crotch - 0.03 * s, width, depth=press)
    elif kind == "boots":
        stitch(bm, layer, (0, 0, w.sole + 0.0045 * s), up, lambda c: True, 0.0018 * s, shade=0.7)
        sole = [f for f in bm.faces if f.calc_center_median().z < w.sole]
        bmesh.ops.bisect_plane(bm, geom=list({e for f in sole for e in f.edges}) + list({v for f in sole for v in f.verts}) + sole,
                               dist=1e-6, plane_co=(0, 0, w.sole), plane_no=up)
        shade_corners([f for f in bm.faces if f.calc_center_median().z < w.sole], layer, 0.42)
        for side, sx in (("R", 1), ("L", -1)):
            ankle = j[f"ankle.{side}"]
            stitch(bm, layer, ankle, (1, 0, 0), lambda c, a=ankle, sx=sx: c.y < a.y - 0.02 * s and c.z > a.z and c.x * sx > 0, width)
    bm.normal_update()
    torn = w.tatters is not None and kind in w.tatters.kinds
    for loop, closed in open_loops(bm):
        c = sum((v.co for v in loop), Vector()) / len(loop)
        if kind in ("shirt", "coat"):
            if w.neck_radial(c) < 0.09 * s and abs(c.z - w.collar_z) < 0.05 * s:
                band(bm, layer, deform, loop, closed, STAND_COLLAR if kind == "coat" else COLLAR, s, up, (j["neck"], up),
                     edge, 0.0045 * s, ribs=kind == "shirt" and not robe)
                continue
            if torn:                              # torn edges and ragged hems are turned under, not banded
                tatters.lip(bm, layer, deform, loop, closed, 0.004 * s)
                continue
            for side, axis in arms.items():
                t = w.arm_t(c, side)
                if t is not None and abs(t - w.sleeve_limit()) < 0.08:
                    band(bm, layer, deform, loop, closed, CUFF, s, axis, (j[f"shoulder.{side}"], axis), edge, 0.0045 * s,
                         ribs=kind == "shirt" and not robe)
            if kind == "shirt" and w.tucked and abs(c.z - (w.waist - 0.07 * s)) < 0.03 * s and abs(c.x) < 0.06 * s:
                band(bm, layer, deform, loop, closed, HEM, s, -up, (Vector((0, j["chest"].y, 0)), up), tone(0.94), 0.008 * s)
        elif kind == "pants" and torn and c.z < w.waist - 0.04 * s:
            tatters.lip(bm, layer, deform, loop, closed, 0.004 * s)
        elif kind == "boots" and abs(c.z - w.boot_top) < 0.04 * s:
            side = "R" if c.x > 0 else "L"
            leg = (j[f"knee.{side}"], (j[f"knee.{side}"] - j[f"ankle.{side}"]).normalized())
            band(bm, layer, deform, loop, closed, RIM, s, up, leg, tone(0.86), 0.008 * s, edge_shade=0.7)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(obj.data)
    bm.free()
    return extras
