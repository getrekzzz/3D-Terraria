"""Meshing helpers for the sculpted headwear (hats, capshapes): evening out the slivers a reduction leaves on a
field's mesh, and placing a mesh made in head-local units onto the head."""
import numpy as np

from .sdf import F


def refine(obj, field, longest=0.008, rounds=3, budget=1.6):
    """Splits the long slivers a reduction leaves across broad smooth areas (which smear the shading of a crease at
    one end along their whole length), the longest first and up to ``budget`` times the faces it starts with, and
    puts the new vertices back on the surface."""
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
    _field_normals(me, field, co, out)


def _field_normals(me, field, co, out, agree=0.9):
    """Normals taken from the field wherever it agrees with the mesh (off its creases), so the long triangles left
    on broad faces shade evenly."""
    from . import sdf
    vn = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", vn)
    vn = vn.reshape(-1, 3)
    g = sdf.gradient(field, co, 0.0006) * F(out)
    g /= np.maximum(np.linalg.norm(g, axis=1), F(1e-9))[:, None]
    use = np.einsum("ij,ij->i", g, vn) > agree
    me.normals_split_custom_set_from_vertices(np.where(use[:, None], g, vn).tolist())


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
