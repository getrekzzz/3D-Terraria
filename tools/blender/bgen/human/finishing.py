"""Finishing a tailored garment (tailoring.py): the shaped cloth reduced to its triangle budget (which keeps detail
where the cloth folds), then stitched seams pressed into it, bands swept along its open edges (ribbed collars and
cuffs, slim stand collars that lean in to the neck, hems, turned-down boot rims), soles on boots, a coat's front
facing and buttons down the plackets. A body suit (the creature's own hide, see garments.Wardrobe.suit) gets none
of this: it melts into the skin at its edges instead.

Seams and soles are cut into the mesh along planes and coloured per face corner, so their edges stay crisp at any
triangle size. Bands belong to the garment's mesh and carry the skin weights of the edge they finish; buttons are
separate parts (their own material) weighted like the cloth under them.
"""
import math

import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import tatters

BUDGET = {"shirt": 10000, "coat": 10000, "pants": 8000, "boots": 3200, "robe": 15000}

# Band profiles: (u, w) in units of the frame scale, u along the surface past the garment's edge, w out from it;
# each runs from on top of the cloth, over the edge, back underneath. Collars and cuffs are swept radially round
# the neck or the limb, so they stay round however the decimated cloth at their edge lies.
COLLAR = ((-0.012, 0.0005), (-0.007, 0.0024), (0.0, 0.003), (0.008, 0.0026), (0.0125, 0.0008), (0.012, -0.0016),
          (0.006, -0.0026), (-0.002, -0.0024))
STAND_COLLAR = ((-0.01, 0.0005), (-0.006, 0.0024), (0.0, 0.0032), (0.008, 0.0024), (0.016, 0.0006), (0.0215, -0.0016),
                (0.023, -0.0034), (0.0205, -0.0048), (0.011, -0.0055), (0.001, -0.0064), (-0.005, -0.0064))
CUFF = ((-0.026, 0.0004), (-0.023, 0.0024), (-0.012, 0.0028), (-0.002, 0.0027), (0.0015, 0.0015), (0.002, -0.0008),
        (-0.005, -0.0016))
HEM = ((-0.016, 0.0004), (-0.013, 0.0024), (-0.003, 0.0027), (0.0005, 0.0015), (0.001, -0.0008), (-0.005, -0.0014))
RIM = ((-0.038, 0.0005), (-0.035, 0.006), (-0.016, 0.0072), (-0.002, 0.008), (0.003, 0.006), (0.0038, 0.0008),
       (0.001, -0.002))


def reduce(obj, budget):
    """Decimates the cloth to ``budget`` triangles, then smooths out any triangle the collapse folded over or
    pinched (which would show as a dark pit)."""
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
        _tidy(obj)
    if obj.vertex_groups.get("_keep") is not None:
        obj.vertex_groups.remove(obj.vertex_groups["_keep"])


def _tidy(obj):
    """Smooths triangles the collapse folded over and turns the long edge of slivers."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for _ in range(4):
        bm.normal_update()
        bad = {v for f in bm.faces if f.normal.dot(sum((g.normal for e in f.edges for g in e.link_faces if g is not f),
                                                        Vector()).normalized()) < 0.5 for v in f.verts}
        if not bad:
            break
        bmesh.ops.smooth_vert(bm, verts=list(bad), factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    # Slivers (flat triangles the collapse leaves along a long edge) fold over once skinned: turn that edge.
    long = set()
    for f in bm.faces:
        L = [e.calc_length() for e in f.edges]
        e = f.edges[L.index(max(L))]
        if len(L) == 3 and 2.0 * f.calc_area() < 0.2 * max(L) ** 2 and not e.is_boundary:
            long.add(e)
    if long:
        bmesh.ops.beautify_fill(bm, faces=list({g for e in long for g in e.link_faces}), edges=list(long), method="ANGLE")
    bm.to_mesh(obj.data)
    bm.free()


def shade_corners(faces, layer, k):
    for f in faces:
        for loop in f.loops:
            c = loop[layer]
            loop[layer] = (c[0] * k, c[1] * k, c[2] * k, c[3])


def tint(faces, layer, base, color):
    """Recolours faces from ``base`` to ``color``, keeping the crease shading they carry."""
    ref = max(max(base), 1e-4)
    for f in faces:
        for loop in f.loops:
            k = max(loop[layer][:3]) / ref
            loop[layer] = (color[0] * k, color[1] * k, color[2] * k, 1.0)


def cut(bm, point, normal, keep):
    """Cuts the faces whose centres pass ``keep`` along the plane (point, normal)."""
    faces = [f for f in bm.faces if keep(f.calc_center_median())]
    geom = list({e for f in faces for e in f.edges}) + list({v for f in faces for v in f.verts}) + faces
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=point, plane_no=normal)


def stitch(bm, layer, point, normal, keep, width, shade=0.8, depth=0.0):
    """A seam along the plane (point, normal) over the faces whose centres pass ``keep``: the mesh is cut along two
    planes ``width`` apart and the strip between them darkened (and pressed in by ``depth``)."""
    point, normal = Vector(point), Vector(normal).normalized()
    for off in (-0.5 * width, 0.5 * width):
        cut(bm, point + normal * off, normal, keep)
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


def band(bm, layer, deform, loop, closed, profile, s, prefer, axis, color, step, ribs=False, edge_shade=1.0, radial=False):
    """Sweeps ``profile`` along the loop of edge vertices, resampled every ``step``; u runs along ``prefer`` laid
    onto the cloth, w along the cloth's normal (straight out from the line ``axis`` (point, direction) the garment
    wraps round when ``radial``: the neck, a limb), turned out from that line. Ribbed bands alternate their shade."""
    if len(loop) < 3:          # (a stray edge or two the decimation left open: nothing to finish)
        return
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
        nn = r.copy() if radial else nn - axis[1] * nn.dot(axis[1]) * 0.5
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


def buttons(src_bm, deform, spots, s, radius, color, name, slot, groups):
    """Small domed discs sitting on the cloth at ``spots`` (point, normal, face), weighted like the face's nearest
    corner."""
    from .garments import colorize, mesh_from, set_slot
    if not spots:
        return []
    bm = bmesh.new()
    dl = bm.verts.layers.deform.verify()
    for co, n, face in spots:
        src = min(face.verts, key=lambda v: (v.co - co).length)
        a = n.orthogonal().normalized()
        b = n.cross(a)
        rim = [a * math.cos(k * math.pi / 4) + b * math.sin(k * math.pi / 4) for k in range(8)]
        rings = [[bm.verts.new(co + d * radius * rr * s + n * h * s) for d in rim]
                 for rr, h in ((0.9, -0.0006), (1.0, 0.0004), (0.8, 0.0014))]
        top = bm.verts.new(co + n * 0.0017 * s)
        faces = [bm.faces.new((r0[k], r0[(k + 1) % 8], r1[(k + 1) % 8], r1[k])) for r0, r1 in zip(rings, rings[1:]) for k in range(8)]
        faces += [bm.faces.new((rings[-1][k], rings[-1][(k + 1) % 8], top)) for k in range(8)]
        faces[-1].normal_update()
        if faces[-1].normal.dot(n) < 0:
            for f in faces:
                f.normal_flip()
        for nv in [x for r in rings for x in r] + [top]:
            for g, wt in src[deform].items():
                nv[dl][g] = wt
    colorize(bm, color)
    obj = mesh_from(bm, name)
    for g in groups:
        obj.vertex_groups.new(name=g)
    set_slot(obj, slot)
    return [obj]


def _tree(bm):
    bm.faces.index_update()
    bm.faces.ensure_lookup_table()
    return BVHTree.FromBMesh(bm)


def _front(bm, tree, x, z, s):
    """Where a ray from straight ahead meets the cloth at (x, z): (point, normal, face), or None."""
    loc, nrm, idx, _ = tree.ray_cast(Vector((x, 1.0 * s, z)), Vector((0, -1, 0)), 2.0 * s)
    if loc is None or loc.y < 0:
        return None
    face = bm.faces[idx]
    n = sum((v.normal for v in face.verts), Vector()).normalized()
    return loc, (n if n.y > 0 else nrm), face


def finish(w, obj, kind, color):
    """Reduces the garment, then adds its seams, bands, soles and buttons; returns the extra parts (buttons)."""
    from .paint import hex3
    j, s = w.f.j, w.f.s
    hidden = kind == "shirt" and "coat" in w.p          # a shirt under a coat: only its lining might show
    robe = bool(w.p.get("robe"))                        # a robe's bodice, long and gathered by the sash
    reduce(obj, BUDGET[kind] // 2 if hidden else BUDGET["robe" if robe and kind == "shirt" else kind])
    torn = w.tatters is not None and kind in w.tatters.kinds
    if (hidden and not torn) or (w.suit and kind != "boots"):     # (a torn coat's shirt: lips where its holes show)
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
    if kind in ("shirt", "coat") and not hidden:
        stitch(bm, layer, (0, j["chest"].y, 0), fwd, lambda c: not w._on_arm(c) and c.z < w.collar_z - 0.006 * s, width, depth=press)
        arms = {}
        for side, sx in (("R", 1), ("L", -1)):
            sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
            axis = (wr - sh).normalized()
            ahead = (fwd - axis * axis.dot(fwd)).normalized()
            out = axis.cross(ahead) * (1 if axis.cross(ahead).z > 0 else -1)
            arms[side] = axis
            # (drawn, not pressed: pressing along a cut across the shoulder's tight curves leaves facets)
            stitch(bm, layer, sh + (wr - sh) * 0.06, axis,
                   lambda c, sh=sh, sx=sx: (c - sh).length < 0.13 * s and c.x * sx > abs(sh.x) * 0.55, width)
            stitch(bm, layer, sh, ahead,
                   lambda c, side=side, sh=sh, out=out: (w.arm_t(c, side) or 0) > 0.08 and (c - sh).dot(out) < 0, width)
        top = w.collar_z
        if kind == "shirt" and not robe:          # a short buttoned placket down from the collar
            keep = lambda c: c.y > 0 and top - 0.11 * s < c.z < top + 0.02 * s and abs(c.x) < 0.05 * s
            for x in (-0.011 * s, 0.011 * s):
                stitch(bm, layer, (x, 0, 0), (1, 0, 0), keep, width * 0.8)
            shade_corners([f for f in bm.faces if keep(f.calc_center_median()) and abs(f.calc_center_median().x) < 0.011 * s], layer, 0.93)
            tree = _tree(bm)
            spots = [_front(bm, tree, 0.0, top - d * s, s) for d in (0.03, 0.058, 0.086)]
            extras += buttons(bm, deform, [p for p in spots if p], s, 0.0048, (0.17, 0.12, 0.09), "buttons", "char:bone",
                              [g.name for g in obj.vertex_groups])
        elif kind == "coat":                      # closed down the front: a faced overlap and a row of brass buttons
            front = lambda c: c.y > 0 and c.z < top
            lip, inner = 0.004 * s, -0.028 * s
            stitch(bm, layer, (lip, 0, 0), (1, 0, 0), front, 0.004 * s, shade=0.55)
            stitch(bm, layer, (inner, 0, 0), (1, 0, 0), front, width * 0.7, shade=0.78)
            if trim:
                tint([f for f in bm.faces if front(c := f.calc_center_median()) and inner + width * 0.35 < c.x < lip - 0.002 * s],
                     layer, base, trim)
            tree = _tree(bm)
            spots = [_front(bm, tree, -0.012 * s, top - d * s, s) for d in (0.05, 0.12, 0.19, 0.26)]
            extras += buttons(bm, deform, [p for p in spots if p and p[0].z > w.waist], s, 0.0065, (0.69, 0.54, 0.25),
                              "buttons", "char:metal", [g.name for g in obj.vertex_groups])
    elif kind == "pants":
        crotch = j["hip.R"].z - 0.09 * s
        stitch(bm, layer, (0, -0.003 * s, 0), fwd, lambda c: True, width, depth=press)
        stitch(bm, layer, (0, 0, 0), (1, 0, 0), lambda c: c.z > crotch - 0.03 * s, width, depth=press)
    elif kind == "boots" and not w.suit:
        stitch(bm, layer, (0, 0, w.sole + 0.0045 * s), up, lambda c: True, 0.0018 * s, shade=0.7)
        cut(bm, (0, 0, w.sole), up, lambda c: c.z < w.sole)
        shade_corners([f for f in bm.faces if f.calc_center_median().z < w.sole], layer, 0.42)
        for side, sx in (("R", 1), ("L", -1)):
            ankle = j[f"ankle.{side}"]
            stitch(bm, layer, ankle, (1, 0, 0), lambda c, a=ankle, sx=sx: c.y < a.y - 0.02 * s and c.z > a.z and c.x * sx > 0, width)
    bm.normal_update()
    neck = (j["neck"], (j["head"] - j["neck"]).normalized())
    for loop, closed in open_loops(bm):
        c = sum((v.co for v in loop), Vector()) / len(loop)
        if kind in ("shirt", "coat"):
            if w.neck_radial(c) < 0.09 * s and abs(c.z - w.collar_z) < 0.05 * s:
                if not hidden:
                    band(bm, layer, deform, loop, closed, STAND_COLLAR if kind == "coat" else COLLAR, s, neck[1], neck,
                         edge, 0.0045 * s, ribs=kind == "shirt" and not robe, radial=True)
                continue
            if torn:                              # torn edges and ragged hems are turned under, not banded
                tatters.lip(bm, layer, deform, loop, closed, 0.004 * s)
                continue
            for side, axis in arms.items():
                t = w.arm_t(c, side)
                if t is not None and abs(t - w.sleeve_limit()) < 0.08:
                    band(bm, layer, deform, loop, closed, CUFF, s, axis, (j[f"shoulder.{side}"], axis), edge, 0.0045 * s,
                         ribs=kind == "shirt" and not robe, radial=True)
            if kind == "shirt" and w.tucked and abs(c.z - (w.waist - 0.07 * s)) < 0.03 * s and abs(c.x) < 0.06 * s:
                band(bm, layer, deform, loop, closed, HEM, s, -up, (Vector((0, j["chest"].y, 0)), up), tone(0.94), 0.008 * s)
        elif kind == "pants" and torn and c.z < w.waist - 0.04 * s:
            tatters.lip(bm, layer, deform, loop, closed, 0.004 * s)
        elif kind == "boots" and abs(c.z - w.boot_top) < 0.04 * s and not w.suit:
            side = "R" if c.x > 0 else "L"
            leg = (j[f"knee.{side}"], (j[f"knee.{side}"] - j[f"ankle.{side}"]).normalized())
            band(bm, layer, deform, loop, closed, RIM, s, up, leg, tone(0.86), 0.008 * s, edge_shade=0.7)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(obj.data)
    bm.free()
    return extras
