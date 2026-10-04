"""Meshing helpers for the sculpted headwear (hats, capshapes): evening out the slivers a reduction leaves on a
field's mesh, cutting its triangles along the lines where one coloured part meets the next (so a colour ends on a
clean edge, not a sawtooth of whole triangles), shading it from the field, and placing a mesh made in head-local units
onto the head."""
import numpy as np

from .sdf import F


def refine(obj, field, longest=0.008, rounds=3, budget=1.6, cuts=(), normals=True):
    """Splits the long slivers a reduction leaves across broad smooth areas (which smear the shading of a crease at
    one end along their whole length), the longest first and up to ``budget`` times the faces it starts with, and
    puts the new vertices back on the surface; then cuts the triangles along the zero set of each of ``cuts``
    (functions of position, see ``cut``; the edges near each are split finer first) and shades the mesh from the
    field."""
    import bmesh
    from . import sdf
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    some = list(bm.verts)[::max(1, len(bm.verts) // 400)]
    co = np.asarray([v.co for v in some], dtype=F)
    out = 1.0 if np.einsum("ij,ij->", sdf.gradient(field, co, 0.0006), np.asarray([v.normal for v in some], dtype=F)) > 0 else -1.0

    def sliver(f):
        ls = [e.calc_length() for e in f.edges]
        return max(ls) > 2.6 * min(ls)

    cap = int(len(bm.faces) * budget)
    for _ in range(rounds):
        long = [e for e in bm.edges if e.calc_length() > longest and any(sliver(f) for f in e.link_faces)]
        long = sorted(long, key=lambda e: -e.calc_length())[:max(0, (cap - len(bm.faces)) // 2)]
        if not long:
            break
        res = bmesh.ops.subdivide_edges(bm, edges=long, cuts=1, use_grid_fill=False)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
        new = [v for v in res["geom_inner"] if isinstance(v, bmesh.types.BMVert)]
        if new:
            bm.normal_update()
            pts = _onto(field, np.asarray([v.co for v in new], dtype=F), np.asarray([v.normal for v in new], dtype=F) * F(out))
            for v, q in zip(new, pts):
                v.co = q
    bmesh.ops.beautify_fill(bm, faces=bm.faces[:], edges=[e for e in bm.edges if e.is_manifold and e.calc_face_angle(1.0) < 0.12],
                            method="ANGLE")
    _clean(bm)
    bm.to_mesh(obj.data)
    bm.free()
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    tri = np.empty(len(me.polygons) * 3, dtype=np.int32)
    me.polygons.foreach_get("vertices", tri)
    co = sdf.relax(field, co.reshape(-1, 3), tri.reshape(-1, 3), 0.0006, iters=2)
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    for poly in me.polygons:
        poly.use_smooth = True
    for fn in cuts:
        _densify(obj, field, fn, out)
        cut(obj, fn)
    if cuts:
        _drop_copies(obj)
    if normals:
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        _field_normals(me, field, co.reshape(-1, 3), out)


def _field_normals(me, field, co, out, lo=0.75, hi=0.93):
    """Normals taken from the field wherever it agrees with the mesh (off its creases), so the long triangles left
    on broad faces shade evenly, fading to the mesh's own toward a crease (a hard switch would draw a sawtooth of
    light along the line where the two part)."""
    from . import sdf
    vn = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", vn)
    vn = vn.reshape(-1, 3)
    g = sdf.gradient(field, co, 0.0006) * F(out)
    g /= np.maximum(np.linalg.norm(g, axis=1), F(1e-9))[:, None]
    w = np.clip((np.einsum("ij,ij->i", g, vn) - F(lo)) / F(hi - lo), 0, 1)
    w = (w * w * (3 - 2 * w))[:, None]
    n = g * w + vn * (1 - w)
    n /= np.maximum(np.linalg.norm(n, axis=1), F(1e-9))[:, None]
    me.normals_split_custom_set_from_vertices(n.tolist())


def _densify(obj, field, fn, out, fine=0.002, rounds=4, most=24000):
    """Splits the edges the zero set of ``fn`` crosses (sampled along each edge, and at each face's middle for a dot
    lying inside one) down to ``fine``, putting the new vertices back on the surface, so a narrow band or a small dot
    of colour crosses edges rather than slipping inside one long triangle."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    ts = np.asarray((0.0, 0.2, 0.4, 0.6, 0.8, 1.0), dtype=F)
    for _ in range(rounds):
        bm.edges.index_update()
        edges = [e for e in bm.edges if e.calc_length() > fine and e.is_manifold and e.calc_face_angle(1.0) < 0.35]
        if not edges:
            break
        a = np.asarray([e.verts[0].co for e in edges], dtype=F)
        b = np.asarray([e.verts[1].co for e in edges], dtype=F)
        pts = a[:, None, :] + (b - a)[:, None, :] * ts[None, :, None]
        sign = fn(pts.reshape(-1, 3)).reshape(len(edges), len(ts)) > 0
        split = {e for e, row in zip(edges, sign) if row.any() and not row.all()}
        faces = [f for f in bm.faces if len(f.verts) == 3]
        mid = np.asarray([f.calc_center_median() for f in faces], dtype=F)
        corner = np.asarray([[v.co for v in f.verts] for f in faces], dtype=F).reshape(-1, 3)
        inside, at = fn(mid) > 0, (fn(corner) > 0).reshape(-1, 3)
        long = set(edges)
        for f, m, c in zip(faces, inside, at):
            if (c != m).all():
                split.update(e for e in f.edges if e in long)
        if not split:
            break
        split = sorted(split, key=lambda e: e.index)[:most]
        res = bmesh.ops.subdivide_edges(bm, edges=split, cuts=1, use_grid_fill=False)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
        new = [v for v in res["geom_inner"] if isinstance(v, bmesh.types.BMVert)]
        if new:
            bm.normal_update()
            pts = _onto(field, np.asarray([v.co for v in new], dtype=F), np.asarray([v.normal for v in new], dtype=F) * F(out))
            for v, q in zip(new, pts):
                v.co = q
    bm.to_mesh(obj.data)
    bm.free()


def cut(obj, fn):
    """Splits the mesh's triangles along the zero set of ``fn`` (a function of the vertex positions, e.g. the
    difference of two parts' distances), so the faces either side of it can take different colours along a clean
    edge instead of a sawtooth of whole triangles."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.index_update()
    bm.faces.index_update()
    s = fn(np.asarray([v.co for v in bm.verts], dtype=F)).astype(np.float64)
    s = np.where(np.abs(s) < 1e-9, 1e-9, s)
    val = {v: s[v.index] for v in bm.verts}
    new = set()
    for e in [e for e in bm.edges if val[e.verts[0]] * val[e.verts[1]] < 0]:
        a, b = e.verts
        fac = min(max(val[a] / (val[a] - val[b]), 0.1), 0.9)
        new.add(bmesh.utils.edge_split(e, a, fac)[1])
    for f in sorted({f for v in new for f in v.link_faces}, key=lambda f: f.index):     # (a fixed order)
        on = [v for v in f.verts if v in new]
        if len(on) == 2 and bm.edges.get(on) is None:
            bmesh.utils.face_split(f, on[0], on[1])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bm.to_mesh(obj.data)
    bm.free()


def _clean(bm):
    """Drops zero-area faces and doubled faces (both copies: a split or a flip can leave a fin folded onto its
    neighbour) with the loose bits they leave, closing any small hole that opens."""
    import bmesh
    bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    copies = _copies(bm)
    if copies:
        bmesh.ops.delete(bm, geom=[f for group in copies for f in group], context="FACES_ONLY")
        bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bmesh.ops.holes_fill(bm, edges=bm.edges[:], sides=3)
        bmesh.ops.delete(bm, geom=[f for group in _copies(bm) for f in group[1:]], context="FACES_ONLY")


def _drop_copies(obj):
    """Keeps one face of each set on the same vertices (a split along a thin fold can double one)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    extra = [f for group in _copies(bm) for f in group[1:]]
    if extra:
        bmesh.ops.delete(bm, geom=extra, context="FACES_ONLY")
        bm.to_mesh(obj.data)
    bm.free()


def _copies(bm):
    """Groups of faces on the same vertices."""
    bm.verts.index_update()
    groups = {}
    for f in bm.faces:
        groups.setdefault(frozenset(v.index for v in f.verts), []).append(f)
    return [g for g in groups.values() if len(g) > 1]


def _onto(field, pts, nrm, reach=0.008, n=41):
    """Moves points onto the surface along their mesh normals, to the crossing where the field turns from inside to
    outside nearest each point: a chord across a thin curved sheet may sag past its middle, and a move along the
    field's gradient would then throw it onto the sheet's far side."""
    ts = np.linspace(-reach, reach, n).astype(F)
    f = np.stack([field(pts + nrm * t) for t in ts])
    cross = (f[:-1] < 0) & (f[1:] >= 0)
    near = np.where(cross, np.abs(ts[:-1, None] + ts[1:, None]), np.inf)
    k = np.argmin(near, 0)
    ok = np.isfinite(near[k, np.arange(len(pts))])
    f0, f1 = f[k, np.arange(len(pts))], f[k + 1, np.arange(len(pts))]
    t = ts[k] - (ts[k + 1] - ts[k]) * f0 / np.where(f1 - f0 > 0, f1 - f0, 1)
    return np.where(ok[:, None], pts + nrm * t[:, None], pts)


def place(obj, head):
    """Moves a mesh made in head-local units onto the head; returns its head-local vertex positions."""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    me.vertices.foreach_set("co", (co * head.h + np.asarray(head.c, dtype=np.float32)).ravel())
    me.update()
    return co


def centres(obj, co):
    """Each (triangle) face's centre, from vertex positions ``co``."""
    me = obj.data
    tri = np.empty(len(me.polygons) * 3, dtype=np.int32)
    me.polygons.foreach_get("vertices", tri)
    return co[tri.reshape(-1, 3)].mean(1)
