"""The sculpted hair's surface finish (hairsdf): every face takes its UVs from the clump it belongs to (U runs along
it), so the hair shader's strands and highlights follow the flow, and the colour darkens toward the roots and into the
grooves; then the mesh is moved from head-local units onto the head.
"""
import numpy as np

F = np.float32


def finish(sc, obj):
    """UVs and colours from each face's clump, then the mesh moved from head-local units onto the head."""
    import bmesh
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    corners = np.asarray([[v.co[:] for v in f.verts] for f in bm.faces], dtype=F)       # (faces, 3, 3)
    lock = _nearest_lock(sc, corners.mean(1))
    uvs = _corner_uvs(sc, corners, lock)
    bm.verts.ensure_lookup_table()
    shade = _shade(sc, np.asarray([v.co[:] for v in bm.verts], dtype=F))
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.new("Color")
    base = sc.g.color
    for fi, f in enumerate(bm.faces):
        for j, loop in enumerate(f.loops):
            loop[uv].uv = uvs[fi, j]
            k = shade[loop.vert.index]
            loop[col] = (base[0] * k, base[1] * k, base[2] * k, 1.0)
    head = sc.g.head
    for v in bm.verts:
        v.co = head.at(v.co)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = True


def _nearest_lock(sc, X):
    """The clump each point belongs to (the nearest one; by segment midpoints where no clump reaches)."""
    pt, sg = sc._pairs(X)
    d = sc._seg_dist(X[pt], sg)[0]
    lock = np.full(len(X), -1, dtype=np.int64)
    order = np.argsort(d)[::-1]                    # nearest last, so it wins the scatter
    lock[pt[order]] = sc.s["lock"][sg[order]]
    lost = np.nonzero(lock < 0)[0]
    mid = (sc.s["a"] + sc.s["b"]) * 0.5
    for c in range(0, len(lost), 2000):
        i = lost[c:c + 2000]
        far = np.einsum("ijk,ijk->ij", X[i, None] - mid[None], X[i, None] - mid[None])
        lock[i] = sc.s["lock"][np.argmin(far, axis=1)]
    return lock


def _corner_uvs(sc, corners, lock):
    F3 = len(corners)
    X = corners.reshape(-1, 3)
    L = np.repeat(lock, 3)
    span = np.asarray(sc.span)
    S = int((span[:, 1] - span[:, 0]).max())
    for j in range(S):                             # each corner against every segment of its clump
        sg = np.minimum(span[L, 0] + j, span[L, 1] - 1)
        d, u, across = sc._seg_dist(X, sg)
        if j == 0:
            best = d.copy()
            pick_u, pick_a, pick_s = u, across, sg
        else:
            m = d < best
            best[m] = d[m]
            pick_u, pick_a, pick_s = np.where(m, u, pick_u), np.where(m, across, pick_a), np.where(m, sg, pick_s)
    s = sc.s
    arc = s["arc_a"][pick_s] + (s["arc_b"][pick_s] - s["arc_a"][pick_s]) * pick_u
    uvs = np.stack([arc * 10.0, sc.band[L] + np.clip(pick_a, -1.5, 1.5) * 0.06], 1).astype(F)
    return uvs.reshape(F3, 3, 2)


def _shade(sc, X):
    """A colour factor per point that changes smoothly over the hair: each clump's own tone, darker toward its
    root, blended by nearness, and a little darker down the grooves where clumps meet."""
    out = np.full(len(X), 0.95, dtype=F)              # (the layer under the clumps: their average tone)
    pt, sg = sc._pairs(X)
    if not len(pt):
        return out
    d, u, _ = sc._seg_dist(X[pt], sg)
    key = pt * sc.n_locks + sc.s["lock"][sg]
    order = np.lexsort((d, key))
    first = order[np.r_[0, np.nonzero(np.diff(key[order]))[0] + 1]]          # per (point, clump): nearest
    pl, dl, ll, tl = pt[first], d[first], sc.s["lock"][sg[first]], sc._along(sg[first], u[first])
    near = np.full(len(X), np.inf, dtype=F)
    np.minimum.at(near, pl, dl)
    w = np.exp(-((dl - near[pl]) / 0.0015) ** 2)
    tone = sc.shade[ll] * (0.88 + 0.12 * np.minimum(1.0, tl / 0.3))
    den = np.bincount(pl, w, len(X))
    hit = den > 0
    out[hit] = (np.bincount(pl, w * tone, len(X))[hit] / den[hit]).astype(F)
    groove = np.bincount(pl, w, len(X)) - 1.0                                 # how many clumps meet here
    return (out * (1.0 - 0.1 * np.clip(groove, 0.0, 1.0))).astype(F)
