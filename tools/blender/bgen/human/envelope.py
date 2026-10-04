"""Envelopes for draped cloth (see tailoring.py): the shape a garment takes over the body from hoop tension and its
own weight, worked out on the cloth's points P and normals N in a frame (origin, axis, u, v) laid along the torso
or a limb. Both return how far each point must move along its normal (0 where it stays).

- hoop: each section across the axis is pulled taut into its convex hull, so the cloth spans the breastbone, the
  hollows beside the chest and in front of the armpits, the spine and the dip between the shoulder blades (on a
  sleeve or a trouser leg the grooves between the muscles) instead of following the body into them;
- fall: on a map of the cloth round the axis (along it, round it) each column of its distance from the axis is
  carried down from its widest less a slope, so it falls from the chest and the shoulder blades, from the shoulder
  down a sleeve, from the seat and the thigh down a trouser leg, instead of following the body in below them.
"""
import numpy as np


def frame(P, N, origin, axis, u):
    """Points and normals in a frame: (along the axis, across it in 2D) for each."""
    axis = np.asarray(axis, dtype=float)
    axis /= np.linalg.norm(axis)
    u = np.asarray(u, dtype=float) - axis * np.dot(u, axis)
    u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    Q = P - np.asarray(origin, dtype=float)
    return Q @ axis, np.stack([Q @ u, Q @ v], 1), np.stack([N @ u, N @ v], 1)


def hull(Q):
    """Indices of the convex hull of 2D points Q, counter-clockwise (monotone chain)."""
    order = np.lexsort((Q[:, 1], Q[:, 0]))

    def half(idx):
        out = []
        for i in idx:
            while len(out) >= 2:
                a, b = Q[out[-2]], Q[out[-1]]
                if (b[0] - a[0]) * (Q[i][1] - a[1]) - (b[1] - a[1]) * (Q[i][0] - a[0]) > 0:
                    break
                out.pop()
            out.append(i)
        return out
    return np.array(half(order)[:-1] + half(order[::-1])[:-1])


def exit_distance(p, d, poly):
    """How far each 2D point ``p`` (inside the convex polygon ``poly``) lies from its edge along direction ``d``."""
    a = poly
    e = np.roll(poly, -1, 0) - a
    r = a[None, :, :] - p[:, None, :]
    det = -d[:, None, 0] * e[None, :, 1] + e[None, :, 0] * d[:, None, 1]
    with np.errstate(divide="ignore", invalid="ignore"):
        t = (-r[..., 0] * e[None, :, 1] + e[None, :, 0] * r[..., 1]) / det
        s = (d[:, None, 0] * r[..., 1] - d[:, None, 1] * r[..., 0]) / det
    ok = (np.abs(det) > 1e-12) & (s > -1e-6) & (s < 1 + 1e-6) & (t > -1e-4)
    t = np.where(ok, t, np.inf).min(1)
    return np.where(np.isfinite(t), np.maximum(t, 0.0), 0.0)


def facing(N2):
    """How much of each normal lies across the axis (1 square to it .. 0 along it), and that direction."""
    m = np.linalg.norm(N2, axis=1)
    return m, N2 / np.maximum(m, 1e-9)[:, None]


def hoop(P, N, sel, origin, axis, u, step):
    """The move along N that pulls each section (``step`` thick, of the points ``sel``) taut into its convex hull."""
    t, Q, N2 = frame(P, N, origin, axis, u)
    m, d = facing(N2)
    out = np.zeros(len(P))
    idx = np.flatnonzero(sel)
    if len(idx) < 3:
        return out
    k = np.floor(t[idx] / step).astype(int)
    for level in np.unique(k):
        pts = idx[k == level]
        if len(pts) < 3:
            continue
        ring = Q[pts]
        if np.ptp(ring[:, 0]) < 1e-6 or np.ptp(ring[:, 1]) < 1e-6:
            continue
        poly = ring[hull(ring)]
        out[pts] = exit_distance(ring, d[pts], poly) / np.maximum(m[pts], 0.3)
    return out * np.clip((m - 0.35) / 0.35, 0.0, 1.0)


def fall(P, N, sel, origin, axis, u, top, step, slope, cells=72, blur=2):
    """The move along N that carries each column of the map (along the axis from ``top``, round it) down from its
    widest, less ``slope`` (per unit along; a function of the angle round the axis, 0 toward u) as it goes."""
    t, Q, N2 = frame(P, N, origin, axis, u)
    out = np.zeros(len(P))
    idx = np.flatnonzero(sel & (t > top))
    if not len(idx):
        return out
    r = np.linalg.norm(Q, axis=1)
    ang = np.arctan2(Q[:, 1], Q[:, 0])
    a = (ang[idx] + np.pi) / (2 * np.pi) * cells
    z = (t[idx] - top) / step
    ia, iz = np.floor(a).astype(int) % cells, np.round(z).astype(int)
    D = np.full((cells, iz.max() + 2), -np.inf)
    np.maximum.at(D, (ia, iz), r[idx])
    drop = np.broadcast_to(np.asarray(slope(np.arange(cells) * 2 * np.pi / cells - np.pi), dtype=float), (cells,)) * step
    for k in range(1, D.shape[1]):
        D[:, k] = np.maximum(D[:, k], D[:, k - 1] - drop)
    E = np.where(np.isfinite(D), D, 0.0)
    for _ in range(blur):                   # (neighbouring columns agree: no grooves between them)
        E = np.maximum(E, (np.roll(E, 1, 0) + E + np.roll(E, -1, 0)) / 3.0 - 0.5 * drop[:, None])
    F = np.isfinite(D).astype(float)
    ac = a - 0.5                            # (cells centred on their middle round the axis, on their row along it)
    i0 = np.floor(ac).astype(int)
    fa = ac - i0
    i0, i1 = i0 % cells, (i0 + 1) % cells
    k0 = np.clip(np.floor(z).astype(int), 0, D.shape[1] - 2)
    fz = np.clip(z - k0, 0.0, 1.0)
    num = den = 0.0
    for ii, wa in ((i0, 1 - fa), (i1, fa)):
        for kk, wz in ((k0, 1 - fz), (k0 + 1, fz)):
            w = wa * wz * F[ii, kk]
            num, den = num + w * E[ii, kk], den + w
    want = num / np.maximum(den, 1e-9)
    m = np.einsum("ij,ij->i", N2[idx], Q[idx]) / np.maximum(r[idx], 1e-9)       # the normal's share facing out
    out[idx] = np.maximum(want - r[idx], 0.0) / np.maximum(m, 0.3) * np.clip((m - 0.3) / 0.35, 0.0, 1.0)
    return out
