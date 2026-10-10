"""Signed-distance modelling in numpy: primitives and smooth blends evaluated on arrays of points, a narrow-band
grid sampler and a surface-nets mesher, so parts with crisp detail (eyelids, lips, nostrils, the jaw line) can be
modelled in code with exact control over how sharply each form blends into the next.

A field is a callable ``f(points)`` taking an (N, 3) float32 array and returning (N,) distances (negative inside).
Distances only need to be accurate near the surface; the blends here keep fields close to distance-like, which the
narrow band relies on. Rotations are 3x3 local-to-world matrices (columns are the local axes).
"""
import numpy as np

F = np.float32


def vec(x):
    return np.asarray(x, dtype=F)


def norm(v):
    return np.sqrt(np.einsum("ij,ij->i", v, v))


# ---------------------------------------------------------------------------------------------- primitives
def sphere(p, c, r):
    return norm(p - vec(c)) - F(r)


def ellipsoid(p, c, r, rot=None):
    """Ellipsoid with semi-axes r (a close distance bound, exact on the surface)."""
    q = p - vec(c)
    if rot is not None:
        q = q @ vec(rot)
    rr = vec(r)
    k0 = norm(q / rr)
    k1 = norm(q / (rr * rr))
    return k0 * (k0 - F(1)) / np.maximum(k1, F(1e-12))


def round_cone(p, a, b, ra, rb):
    """A cone between spheres (a, ra) and (b, rb), exact (after Inigo Quilez)."""
    a, b = vec(a), vec(b)
    ba = b - a
    l2 = F(ba @ ba)
    rr = F(ra - rb)
    a2 = l2 - rr * rr
    il2 = F(1) / l2
    pa = p - a
    y = pa @ ba
    z = y - l2
    x = pa * l2 - y[:, None] * ba
    x2 = np.einsum("ij,ij->i", x, x)
    y2 = y * y * l2
    z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    side = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - ra
    out = np.where(np.sign(y) * a2 * y2 < k, np.sqrt(x2 + y2) * il2 - ra, side)
    return np.where(np.sign(z) * a2 * z2 > k, np.sqrt(x2 + z2) * il2 - rb, out)


def capsule(p, a, b, r):
    return round_cone(p, a, b, r, r)


def tube(p, points, radii):
    """A smooth tube through a polyline (round cones joined end to end)."""
    d = None
    for i in range(len(points) - 1):
        seg = round_cone(p, points[i], points[i + 1], radii[i], radii[i + 1])
        d = seg if d is None else np.minimum(d, seg)
    return d


# ---------------------------------------------------------------------------------------------- blends
def smin(a, b, k):
    """Union blending over a band of width k: a cubic smooth minimum, so the blend's curvature is continuous too
    (a quadratic one leaves faint ridges where highlights catch at the edges of every fillet)."""
    if k <= 0:
        return np.minimum(a, b)
    k = k * 1.25
    h = np.maximum(F(k) - np.abs(a - b), F(0)) / F(k)
    return np.minimum(a, b) - h * h * h * F(k / 6.0)


def smax(a, b, k):
    return -smin(-a, -b, k)


def carve(a, b, k):
    """``a`` with ``b`` cut away, the cut's edge rounded over width k."""
    return smax(a, -b, k)


def region(p, d, centre, radius, fn):
    """Applies ``fn(points, distances) -> distances`` only to points within ``radius`` of ``centre`` (a feature
    whose shapes and blends all lie inside that ball); elsewhere the distances pass through unchanged."""
    q = p - vec(centre)
    m = np.einsum("ij,ij->i", q, q) < F(radius * radius)
    if m.any():
        d = d.copy()
        d[m] = fn(p[m], d[m])
    return d


# ---------------------------------------------------------------------------------------------- sampling
def _evaluate(field, pts, chunk=1 << 20):
    out = np.empty(len(pts), dtype=F)
    for i in range(0, len(pts), chunk):
        out[i:i + chunk] = field(pts[i:i + chunk])
    return out


def sample(field, lo, hi, h, coarse=4):
    """The field on a grid of spacing h over [lo, hi]: exact within a band around the surface (found on a coarse
    grid first), elsewhere only its sign is kept. Returns (values, origin)."""
    lo, hi = vec(lo), vec(hi)
    H = F(h * coarse)
    n = (np.ceil((hi - lo) / H).astype(int) + 1)
    axes = [lo[i] + H * np.arange(n[i], dtype=F) for i in range(3)]
    grid = np.stack(np.meshgrid(*axes, indexing="ij"), -1).reshape(-1, 3)
    fc = _evaluate(field, grid).reshape(n)
    m = n - 1
    corners = [np.abs(fc[i:i + m[0], j:j + m[1], k:k + m[2]]) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    active = np.argwhere(np.minimum.reduce(corners) < H * 1.25)
    nf = m * coarse + 1
    fine = np.repeat(np.repeat(np.repeat(fc, coarse, 0), coarse, 1), coarse, 2)[:nf[0], :nf[1], :nf[2]].copy()
    mask = np.zeros(nf, dtype=bool)
    base = active * coarse
    for di in range(coarse + 1):
        for dj in range(coarse + 1):
            for dk in range(coarse + 1):
                mask[base[:, 0] + di, base[:, 1] + dj, base[:, 2] + dk] = True
    idx = np.argwhere(mask)
    fine[mask] = _evaluate(field, lo + idx.astype(F) * F(h))
    return fine, lo


def surface_nets(f, lo, h):
    """Quads on the zero set of a sampled field: one vertex per cell the surface crosses (at the mean of its edge
    crossings), one quad per crossed grid edge, wound so normals point out of the negative region."""
    s = f < 0
    nx, ny, nz = f.shape
    shape = (nx - 1, ny - 1, nz - 1)
    offs = [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    signs = [s[i:i + shape[0], j:j + shape[1], k:k + shape[2]] for i, j, k in offs]
    mixed = np.logical_or.reduce(signs) & ~np.logical_and.reduce(signs)
    cells = np.argwhere(mixed)
    vals = np.stack([f[cells[:, 0] + i, cells[:, 1] + j, cells[:, 2] + k] for i, j, k in offs], 1)
    acc = np.zeros((len(cells), 3), dtype=F)
    cnt = np.zeros(len(cells), dtype=F)
    for a in range(8):
        for b in range(a + 1, 8):
            if sum(abs(offs[a][t] - offs[b][t]) for t in range(3)) != 1:
                continue
            fa, fb = vals[:, a], vals[:, b]
            x = (fa < 0) != (fb < 0)
            t = np.where(x, fa / np.where(x, fa - fb, F(1)), F(0))
            pa, pb = vec(offs[a]), vec(offs[b])
            acc[x] += pa + t[x, None] * (pb - pa)
            cnt[x] += 1
    verts = lo + (cells.astype(F) + acc / np.maximum(cnt, 1)[:, None]) * F(h)
    lin = np.ravel_multi_index(cells.T, shape)

    def vid(i, j, k):
        return np.searchsorted(lin, np.ravel_multi_index((i, j, k), shape))

    quads = []
    e = np.argwhere(s[:-1, 1:-1, 1:-1] != s[1:, 1:-1, 1:-1])
    i, j, k = e[:, 0], e[:, 1] + 1, e[:, 2] + 1
    quads.append((np.stack([vid(i, j - 1, k - 1), vid(i, j, k - 1), vid(i, j, k), vid(i, j - 1, k)], 1), s[i, j, k]))
    e = np.argwhere(s[1:-1, :-1, 1:-1] != s[1:-1, 1:, 1:-1])
    i, j, k = e[:, 0] + 1, e[:, 1], e[:, 2] + 1
    quads.append((np.stack([vid(i - 1, j, k - 1), vid(i - 1, j, k), vid(i, j, k), vid(i, j, k - 1)], 1), s[i, j, k]))
    e = np.argwhere(s[1:-1, 1:-1, :-1] != s[1:-1, 1:-1, 1:])
    i, j, k = e[:, 0] + 1, e[:, 1] + 1, e[:, 2]
    quads.append((np.stack([vid(i - 1, j - 1, k), vid(i, j - 1, k), vid(i, j, k), vid(i - 1, j, k)], 1), s[i, j, k]))
    faces = []
    for q, inside_first in quads:
        q = q.copy()
        q[~inside_first] = q[~inside_first][:, ::-1]
        faces.append(q)
    return verts, np.concatenate(faces)


def _labels(n, faces):
    """Connected pieces of a mesh: one label per vertex (label propagation with pointer jumping)."""
    lab = np.arange(n)
    a, b = faces.ravel(), np.roll(faces, -1, axis=1).ravel()
    while True:
        la, lb = lab[a], lab[b]
        m = np.minimum(la, lb)
        new = lab.copy()
        np.minimum.at(new, la, m)
        np.minimum.at(new, lb, m)
        while True:                      # pointer jumping: every label points straight at its root
            nxt = new[new]
            if np.array_equal(nxt, new):
                break
            new = nxt
        if np.array_equal(new, lab):
            return lab
        lab = new


def _keep(verts, faces, keep):
    remap = np.cumsum(keep) - 1
    return verts[keep], remap[faces[keep[faces].all(1)]]


def largest_piece(verts, faces):
    """Keeps the largest connected piece of a mesh, dropping closed bubbles a cut leaves inside the solid."""
    lab = _labels(len(verts), faces)
    roots, counts = np.unique(lab, return_counts=True)
    return _keep(verts, faces, lab == roots[np.argmax(counts)])


def pieces(verts, faces, min_faces):
    """Keeps every connected piece of at least ``min_faces`` faces (drops specks and bubbles)."""
    lab = _labels(len(verts), faces)
    roots, inv = np.unique(lab, return_inverse=True)
    counts = np.bincount(inv[faces[:, 0]], minlength=len(roots))
    return _keep(verts, faces, counts[inv] >= min_faces)


def gradient(field, pts, e):
    out = np.empty_like(pts)
    for axis in range(3):
        d = np.zeros(3, dtype=F)
        d[axis] = e
        out[:, axis] = (field(pts + d) - field(pts - d)) / F(2 * e)
    return out


def project(field, pts, e, steps=3):
    """Moves points onto the zero set along the field's gradient (Newton steps of at most 4e, so a point where the
    field is flat cannot be thrown far)."""
    pts = vec(pts).copy()
    for _ in range(steps):
        d = field(pts)
        g = gradient(field, pts, e)
        step = g * (d / np.maximum(np.einsum("ij,ij->i", g, g), F(1e-8)))[:, None]
        n = norm(step)
        pts -= step * np.minimum(F(1), F(4 * e) / np.maximum(n, F(1e-12)))[:, None]
    return pts


def relax(field, verts, faces, e, iters=4, crease=0.9):
    """Evens out the long, thin triangles a reduction leaves in smooth areas: each vertex away from creases moves
    toward the middle of its neighbours within the surface's tangent plane, then back onto the surface. Vertices
    on creases (lid margins, the mouth line, nostril rims) stay where the reduction put them."""
    n = len(verts)
    verts = vec(verts).copy()
    for _ in range(iters):
        g = gradient(field, verts, e)
        nrm = g / np.maximum(norm(g), F(1e-12))[:, None]
        acc = np.zeros_like(verts)
        cnt = np.zeros(n, dtype=F)
        bend = np.ones(n, dtype=F)
        for i in range(3):
            a, b, c = faces[:, i], faces[:, (i + 1) % 3], faces[:, (i + 2) % 3]
            np.add.at(acc, a, verts[b] + verts[c])
            np.add.at(cnt, a, F(2))
            dots = np.einsum("ij,ij->i", nrm[a], nrm[b])
            np.minimum.at(bend, a, dots)
            np.minimum.at(bend, b, dots)
        move = acc / np.maximum(cnt, F(1))[:, None] - verts
        move -= nrm * np.einsum("ij,ij->i", move, nrm)[:, None]
        free = bend > crease
        verts[free] += move[free] * F(0.5)
        verts[free] = project(field, verts[free], e, steps=2)
    return verts


def to_mesh(name, verts, faces):
    """A Blender mesh object from vertex and quad arrays (linked into the scene, smooth shaded)."""
    import bpy
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(verts))
    me.vertices.foreach_set("co", verts.astype(np.float32).ravel())
    n = faces.shape[1]
    me.loops.add(faces.size)
    me.loops.foreach_set("vertex_index", faces.astype(np.int32).ravel())
    me.polygons.add(len(faces))
    me.polygons.foreach_set("loop_start", (np.arange(len(faces), dtype=np.int32) * n))
    me.polygons.foreach_set("loop_total", np.full(len(faces), n, dtype=np.int32))
    me.update(calc_edges=True)
    me.validate()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for poly in me.polygons:
        poly.use_smooth = True
    return obj
