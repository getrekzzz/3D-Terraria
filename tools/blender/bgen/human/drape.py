"""Solvers for draping cloth over a garment's shell (see tailoring.py), working on the shell's vertices X, their
normals N and its edge graph: heights along N found by relaxing a membrane over the body, rolling a ball over it,
low-pass filtering, and skin weights spread over the cloth.
"""
import bmesh
import numpy as np


class Graph:
    """The shell's vertex adjacency, for smoothing per-vertex values over the surface."""

    def __init__(self, n, edges):
        self.n, self.a, self.b = n, edges[:, 0], edges[:, 1]
        self.deg = np.maximum(np.bincount(edges.ravel(), minlength=n), 1).astype(float)

    def mean(self, v):
        """The average of each vertex's neighbours (scalars or rows of vectors)."""
        if v.ndim == 2:
            return np.stack([self.mean(v[:, k]) for k in range(v.shape[1])], 1)
        return (np.bincount(self.a, v[self.b], self.n) + np.bincount(self.b, v[self.a], self.n)) / self.deg

    def most(self, v):
        """The largest value among each vertex and its neighbours."""
        out = v.copy()
        np.maximum.at(out, self.a, v[self.b])
        np.maximum.at(out, self.b, v[self.a])
        return out


def membrane(X, N, g, lo, hi, pinned, iters=300):
    """Heights along N (between ``lo`` and ``hi``) of a membrane resting on X: each point is pulled toward its
    neighbours' average, so cloth fills hollows and lies on the bulges; ``pinned`` points stay at ``lo``."""
    hi = np.where(pinned, lo, np.maximum(hi, lo))
    h = lo.copy()
    for _ in range(iters):
        P = X + N * h[:, None]
        h = np.clip(h + 0.9 * np.einsum("ij,ij->i", g.mean(P) - P, N), lo, hi)
    return h


def _pairs(P, Q, cell):
    """Index pairs (i into P, j into Q) of points in the same or neighbouring cells of a grid of size ``cell``, one
    batch per neighbour offset with i ascending, and where each i's run starts in the batch and how long it is."""
    base = np.minimum(P.min(0), Q.min(0))
    cp, cq = (np.floor((V - base) / cell).astype(np.int64) for V in (P, Q))
    dims = np.maximum(cp.max(0), cq.max(0)) + 3
    key = lambda c: ((c[:, 0] + 1) * dims[1] + c[:, 1] + 1) * dims[2] + c[:, 2] + 1
    order = np.argsort(key(cq))
    ks = key(cq)[order]
    for off in np.stack(np.meshgrid(*[(-1, 0, 1)] * 3), -1).reshape(-1, 3):
        kp = key(cp + off)
        start = np.searchsorted(ks, kp, "left")
        n = np.searchsorted(ks, kp, "right") - start
        first = np.cumsum(n) - n
        yield np.repeat(np.arange(len(P)), n), order[np.arange(n.sum()) - np.repeat(first - start, n)], first, n


def roll(X, N, lo, R, m):
    """The height along each point's normal at which a ball of radius R rolled over the surface X + N*lo (sampled
    at its first ``m`` points) touches that normal line: cloth too stiff to follow the body into clefts narrower than
    the ball (between the pecs, down the spine, under the seat) spans them, however the surface curves along them."""
    O = X + N * lo[:, None]
    A = X + N * R
    t = lo.copy()
    for i, j, first, n in _pairs(O, O[:m], 1.25 * R):
        d = A[i] - O[j]
        b = np.einsum("ij,ij->i", N[i], d)
        disc = b * b - np.einsum("ij,ij->i", d, d) + R * R
        # (not a surface facing back at this one across a gap: an arm's inside and the flank beside it)
        reach = np.where((disc > 0) & (np.einsum("ij,ij->i", N[i], N[j]) > -0.2), np.sqrt(np.maximum(disc, 0)) - b, -1.0)
        if len(reach):
            has = n > 0
            t[has] = np.maximum(t[has], np.maximum.reduceat(reach, first[has]))
    return t


def settle(X, N, g, h, floor, pinned, rate, iters=40, reach=4):
    """Rounds draped cloth the way its stiffness would: a low-pass (Taubin) filter of the surface X + N*h flattens
    the tents a membrane pitches over sharp bumps and ridges (a muscle's corner, the collarbones) and small ripples
    (``rate`` 0..1 per point sets how much), then a smooth lift (spread ``reach`` rings out) puts back whatever the
    filter cut below ``floor``."""
    f = h.copy()
    for _ in range(iters):
        for k in (0.6, -0.63):
            P = X + N * f[:, None]
            f = f + k * rate * np.einsum("ij,ij->i", g.mean(P) - P, N)
            f[pinned] = h[pinned]
    lift = np.maximum(floor - f, 0.0)
    for _ in range(reach):
        lift = g.most(lift)
    for _ in range(3 * reach):
        lift = g.mean(lift)
    return np.maximum(f + lift, floor)


def untangle(X, N, h, F, NF, g, pinned, rounds=10):
    """The cloth surface X + N*h with its folds smoothed out, where it turns over on itself (stood off further than
    a tight groove of the body is wide): the points round every face turned well away from the shell's own face
    (``F`` triangles, ``NF`` their normals on the shell) are drawn toward their neighbours' average, never in below
    their height, until none is (faces at the pinned edges are left to the hems)."""
    P = X + N * h[:, None]
    free = ~pinned[F].any(1)
    for _ in range(rounds):
        n = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
        bad = free & (np.einsum("ij,ij->i", n, NF) < 0.5 * np.linalg.norm(n, axis=1))
        if not bad.any():
            break
        m = np.zeros(len(P))
        m[F[bad].ravel()] = 1.0
        m = (g.most(g.most(m)) > 0) & ~pinned
        for _ in range(4):
            P = np.where(m[:, None], g.mean(P), P)
            P += N * np.maximum(h - np.einsum("ij,ij->i", P - X, N), 0.0)[:, None]
    return P


def soften(bm, g, give, iters):
    """Spreads the copied skin weights over the cloth (by ``give`` 0..1 per vertex), so it bends in broader curves
    than the body under it: no pinch in front of the shoulders or inside the elbows."""
    dl = bm.verts.layers.deform.verify()
    rows = [dict(v[dl]) for v in bm.verts]
    groups = sorted({k for r in rows for k in r})
    if not groups:
        return
    W = np.zeros((len(rows), len(groups)))
    for i, r in enumerate(rows):
        W[i, [groups.index(k) for k in r]] = list(r.values())
    S = W
    for _ in range(iters):
        S = 0.5 * S + 0.5 * g.mean(S)
    W = W + (S - W) * give[:, None]
    if W.shape[1] > 4:                                  # at most four bones each, as the exporter keeps
        W[W < np.sort(W, 1)[:, -4:-3]] = 0.0
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    for v, row in zip(bm.verts, W):
        v[dl].clear()
        for k in np.flatnonzero(row > 0.005):
            v[dl][groups[k]] = float(row[k])


def rings(g, start, k):
    """How many edges away from a ``start`` vertex each vertex lies, counted up to ``k``."""
    d = np.where(start, 0.0, float(k))
    seen = start.copy()
    for step in range(1, k):
        hit = (np.bincount(g.a, seen[g.b], g.n) + np.bincount(g.b, seen[g.a], g.n)) > 0
        new = hit & ~seen
        d[new] = step
        seen |= new
    return d


def blur(grid, mask, n):
    """``n`` passes of a 3x3 box blur over the cells of a 2D grid where ``mask`` is set (wrapping round both axes),
    each pass also spreading the values one cell further out."""
    for _ in range(n):
        grid, mask = [sum(np.roll(np.roll(m, a, 0), b, 1) for a in (-1, 0, 1) for b in (-1, 0, 1)) for m in (grid * mask, mask)]
        grid = np.where(mask > 0, grid / np.maximum(mask, 1e-9), 0.0)
        mask = (mask > 0).astype(float)
    return grid


def nearest(points, X, values):
    """``values`` (one per point of ``points``) read at the nearest point to each of X."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(points))
    for i, p in enumerate(points):
        kd.insert(p, i)
    kd.balance()
    out = np.empty(len(X))
    for n, x in enumerate(X):                # inverse-distance blend of the four nearest, so steps don't show
        near = kd.find_n(x, 4)
        w = np.array([1.0 / (d + 1e-4) for _, _, d in near])
        out[n] = np.dot(w, [values[i] for _, i, _ in near]) / w.sum()
    return out


def refine(bm):
    """Splits every triangle in four so folds have the vertices to form."""
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.normal_update()
    bm.verts.index_update()
