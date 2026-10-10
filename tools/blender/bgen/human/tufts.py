"""Tufts of fur: clumps scattered over a skin surface (spaced by their width, so they overlap like shingles along
the flow), each rooted in the undercoat, laid along the flow following the surface under it and lifting toward
its tips, and split into three thin, rounded strands, so the coat reads as shaggy hair rather than scales or
feathers. Two styles: ``tuft`` (short dense fur) tapers each strand to a point and fans them apart from the root
(the middle one the longest, lying on top); ``lock`` (long shaggy hair) keeps the strands together down the clump as
one lock, each rounding off at its tip, the side ones splaying a little apart there, and shades them soft (their
normals leant toward the surface's, so their edges do not darken like blades). Built as one mesh with strand UVs
(U along each tuft); each ring of a strand takes the bone weights of the skin under it, so a tuft bends with the
skin it lies on (a lock over a shoulder follows the arm down instead of standing off where the arm swung away).
"""
import math

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .coverings import make_mesh, ss

TC = np.linspace(0.0, 1.0, 6)                     # where a tuft's centre line is sampled (0 root .. 1 tip)
STRANDS = 3                                       # strands per tuft
ARM = ("upper_arm", "forearm", "hand", "thumb", "index", "middle", "ring", "pinky")  # (posed down from the rest A)
# per style: a tuft's width / length, ring positions along the middle strand and the side ones (the tip closes
# beyond), a strand's section (rounded over, flatter below) and thickness / width, the strands' widths (middle, side)
# and the side ones' lengths, how far a tuft turns off the flow (radians), how far its normals lean to the
# surface's (0: its own; their undersides then face down) and how far it bends down under its weight (over a
# shoulder's top and edge)
STYLES = {
    "tuft": dict(aspect=0.55, ts=(0.0, 0.45, 0.8), side=(0.0, 0.55), section=((1.0, 0.0), (0.0, 1.0), (-1.0, 0.0), (0.0, -0.6)),
                 flat=0.5, widths=(0.66, 0.56), side_len=(0.6, 0.92), turn=0.4, soft=0.0, hang=0.0),
    "lock": dict(aspect=0.42, ts=(0.0, 0.4, 0.7, 0.88), side=(0.0, 0.5, 0.84), section=((1.0, 0.0), (0.0, 1.0), (-1.0, 0.0)),
                 flat=0.6, widths=(0.55, 0.45), side_len=(0.75, 0.95), turn=0.2, soft=2.0, hang=0.6),
}


def _sample(V, T, N, L, rng, min_len):
    """Root candidates: area-weighted points (more where tufts are small), with interpolated normal and length."""
    a, b, c = V[T[:, 0]], V[T[:, 1]], V[T[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    Lt = L[T].mean(1)
    ok = Lt > min_len
    if not ok.any():
        return None
    width = 0.34 * np.maximum(Lt, min_len)
    w = np.where(ok, area / (width * width), 0.0)
    count = int(w.sum() * 12) + 8
    tri = rng.choice(len(T), size=count, p=w / w.sum())
    u, v = rng.random(count), rng.random(count)
    flip = u + v > 1
    u[flip], v[flip] = 1 - u[flip], 1 - v[flip]
    bary = np.stack([1 - u - v, u, v], 1)
    return tri, bary


def _bary(p, a, b, c):
    """Barycentric coordinates of points p in triangles (a, b, c) (arrays (..., 3)), clamped into the triangle."""
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = (v0 * v0).sum(-1), (v0 * v1).sum(-1), (v1 * v1).sum(-1)
    d20, d21 = (v2 * v0).sum(-1), (v2 * v1).sum(-1)
    den = d00 * d11 - d01 * d01
    den = np.where(np.abs(den) < 1e-20, 1e-20, den)
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    out = np.clip(np.stack([1.0 - v - w, v, w], -1), 0.0, None)
    return out / np.maximum(out.sum(-1, keepdims=True), 1e-9)


def _weights(W, T, corners, bary, faces, locs, V):
    """Bone weights (bones, matrix (tufts, samples, bones)) of the skin under each sample of each centre line: the
    root's where a sample found no surface (``faces`` -1)."""
    bones = sorted({b for w in W for b in w})
    col = {b: j for j, b in enumerate(bones)}
    Wm = np.zeros((len(W), len(bones)))
    for v, w in enumerate(W):
        for b, y in w.items():
            Wm[v, col[b]] = y
    root = np.einsum("ij,ijk->ik", bary, Wm[corners])
    S = np.repeat(root[:, None], faces.shape[1], 1)
    hit = faces >= 0
    if hit.any():
        tri = T[faces[hit]]
        bc = _bary(locs[hit], V[tri[:, 0]], V[tri[:, 1]], V[tri[:, 2]])
        S[hit] = np.einsum("ij,ijk->ik", bc, Wm[tri])
    return bones, S


def _space(P, R, order):
    """Greedy dart throwing: keeps points no closer than the mean of their radii, in ``order``."""
    cell = float(R.max())
    grid = {}
    keep = []
    for i in order:
        key = tuple((P[i] // cell).astype(int))
        ok = True
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for j in grid.get((key[0] + dx, key[1] + dy, key[2] + dz), ()):
                        if np.sum((P[i] - P[j]) ** 2) < (0.5 * (R[i] + R[j])) ** 2:
                            ok = False
                            break
                    if not ok:
                        break
                if not ok:
                    break
            if not ok:
                break
        if ok:
            grid.setdefault(key, []).append(i)
            keep.append(i)
    return np.asarray(keep, dtype=np.int64)


def build(name, V, T, N, W, flow, L, shell, tone, lift, seed=1, min_len=None, slot="char:hair", spacing=0.62,
          max_width=None, style="tuft"):
    """Tufts over the surface (V, T) with normals N, per-vertex weights W, fur direction ``flow``, tuft length L,
    undercoat height ``shell`` and colour ``tone`` (per vertex); ``lift`` raises the tips; long tufts are no wider than
    ``max_width`` (locks, not cards); ``style`` is a key of STYLES. Returns an object or None."""
    out = arrays(V, T, N, W, flow, L, shell, tone, lift, seed, min_len, spacing, max_width, style)
    return None if out is None else make_mesh(name, *out[:2], out[2], slot, out[4], out[3], normals=out[5])


def arrays(V, T, N, W, flow, L, shell, tone, lift, seed=1, min_len=None, spacing=0.62, max_width=None, style="tuft"):
    """The tufts as (verts, triangles, colours, uvs and weights per vertex, normals per face corner or None), or None
    (see build)."""
    st = STYLES[style]
    rng = np.random.default_rng(seed)
    min_len = min_len if min_len is not None else 0.25 * float(np.max(L)) if len(L) else 0.0
    if not len(L) or float(np.max(L)) <= 0:
        return None
    min_len = max(min_len, 1e-4)
    found = _sample(V, T, N, L, rng, min_len)
    if found is None:
        return None
    tri, bary = found
    corners = T[tri]
    P = np.einsum("ij,ijk->ik", bary, V[corners])
    n = np.einsum("ij,ijk->ik", bary, N[corners])
    n /= np.linalg.norm(n, axis=1, keepdims=True)
    length = np.einsum("ij,ij->i", bary, L[corners])
    good = length > min_len * 0.8
    P, n, length, tri, bary, corners = P[good], n[good], length[good], tri[good], bary[good], corners[good]
    width = st["aspect"] * length
    width = width if max_width is None else np.minimum(width, max_width)
    keep = _space(P, spacing * width, rng.permutation(len(P)))
    P, n, length, width, bary, corners = P[keep], n[keep], length[keep], width[keep], bary[keep], corners[keep]
    m = len(P)
    if not m:
        return None
    f = np.einsum("ij,ijk->ik", bary, flow[corners])
    f -= n * np.einsum("ij,ij->i", f, n)[:, None]
    weak = np.linalg.norm(f, axis=1) < 1e-3
    f[weak] = np.cross(n[weak], (1.0, 0.0, 0.0))
    f /= np.linalg.norm(f, axis=1, keepdims=True)
    sh = np.einsum("ij,ij->i", bary, shell[corners])
    col = np.einsum("ij,ijk->ik", bary, tone[corners])
    length *= rng.uniform(0.72, 1.25, m)
    width *= rng.uniform(0.85, 1.15, m)
    bend = rng.uniform(-0.3, 0.3, m)
    rise = lift * rng.uniform(0.55, 1.45, m)
    shade = rng.uniform(0.88, 1.1, m)
    turn = rng.uniform(-st["turn"], st["turn"], m)
    f = f * np.cos(turn)[:, None] + np.cross(n, f) * np.sin(turn)[:, None]
    tree = BVHTree.FromPolygons([Vector(v) for v in V], [tuple(t) for t in T.tolist()])

    # the clump's centre line, sampled along its length, following the surface (and riding over the undercoat) under it
    # (not turning back on itself where it lies up a slope, nor on an arm: the rest pose holds the arms out, so there
    # down is along the arm, the way its flow already runs)
    arm = np.asarray([sum(y for b, y in W[c].items() if b.startswith(ARM)) for c in range(len(W))])
    sag = st["hang"] * ss(-0.6, 0.1, -f[:, 2]) * (1.0 - np.clip(np.einsum("ij,ij->i", bary, arm[corners]), 0.0, 1.0))
    C = np.zeros((m, len(TC), 3))
    Nn = np.zeros((m, len(TC), 3))
    reach = np.ones(m)                               # how much of its length lies over the surface
    faces = np.full((m, len(TC)), -1, dtype=np.int64)  # the skin face under each sample (for its weights)
    locs = np.zeros((m, len(TC), 3))
    cur, cn, head = P.copy(), n.copy(), f.copy()     # a hanging lock walks over the surface step by step, turning
    dt = float(TC[1] - TC[0])                        # downhill as it goes (so it drapes over an edge, never jumping
    down = np.asarray((0.0, 0.0, -1.0))              # across a gap to another surface below)
    for k, t in enumerate(TC):
        rise_k = math.sin(min(t / 0.25, 1.0) * math.pi / 2)
        if not st["hang"]:                           # a short tuft: straight along its flow, onto the surface under it
            q = P + f * (length * t)[:, None] + np.cross(n, f) * (bend * length * t * t)[:, None]
        elif k:
            g = down - cn * (cn @ down)[:, None]
            head = head + g * (2 * sag * dt)[:, None] + np.cross(cn, head) * (2 * bend * dt)[:, None]
            head -= cn * np.einsum("ij,ij->i", head, cn)[:, None]
            head /= np.maximum(np.linalg.norm(head, axis=1, keepdims=True), 1e-9)
            q = cur + head * (length * dt)[:, None]
        else:
            q = P
        for i in range(m):
            base, nn, sl = q[i], n[i], sh[i]
            if t > 0:
                loc, normal, fi, dist = tree.find_nearest(Vector(q[i]), float(length[i]) * 0.6)
                if loc is not None:                  # on the surface under it, over edges too (a shoulder's)
                    base, nn = np.asarray(loc), np.asarray(normal)
                    faces[i, k], locs[i, k] = fi, base
                    sl = max(sh[i], float(shell[T[fi]].mean()))
                    d = q[i] - base
                    if np.linalg.norm(d) > 0.15 * width[i] and abs(d @ nn) < 0.7 * np.linalg.norm(d):
                        reach[i] = min(reach[i], TC[k - 1])   # past the edge of the part's surface: cut short
                    elif st["hang"]:                 # on from there, heading the way it moved
                        mv = base - cur[i]
                        mv -= nn * (mv @ nn)
                        if mv @ mv > 1e-12:
                            head[i] = mv / np.linalg.norm(mv)
                        cur[i], cn[i] = base, nn
                else:
                    reach[i] = min(reach[i], TC[k - 1])
            C[i, k] = base + nn * (sl * 0.7 + (sl * 0.45 + 0.22 * width[i]) * rise_k + rise[i] * length[i] * t * t)
            Nn[i, k] = nn
    if style == "lock":                              # a lock cut short narrows too (not a round knob)
        width = width * np.clip(reach / 0.45, 0.35, 1.0)
    tang = np.gradient(C, axis=1)
    tang /= np.maximum(np.linalg.norm(tang, axis=2, keepdims=True), 1e-9)
    side = np.cross(Nn, tang)
    side /= np.maximum(np.linalg.norm(side, axis=2, keepdims=True), 1e-9)
    up = np.cross(tang, side)

    def at(arr, t):                                  # (m, 3) along each centre line at per-clump t (m,)
        x = np.clip(t, 0.0, 1.0) * (len(TC) - 1)
        i0 = np.minimum(np.floor(x).astype(int), len(TC) - 2)
        a = (x - i0)[:, None]
        rows = np.arange(m)
        return arr[rows, i0] * (1 - a) + arr[rows, i0 + 1] * a

    # each clump splits into strands: pointed ones fanning a little apart toward their tips (the middle one longest,
    # on top; the shorter side strands with a ring fewer), or a lock's lying together and rounding off at their tips
    lock = style == "lock"
    section, flat = st["section"], st["flat"]
    sides = len(section)
    rings = [np.asarray(st["ts"] if q == (STRANDS - 1) // 2 else st["side"]) for q in range(STRANDS)]
    starts = np.cumsum([0] + [len(r) * sides + 1 for r in rings])
    per = int(starts[-1])
    verts = np.zeros((m, per, 3))
    bones, SW = _weights(W, T, corners, bary, faces, locs, V)
    gstart = np.cumsum([0] + [len(r) + 1 for r in rings])         # a strand's rings, then its tip
    ring_w = np.zeros((m, int(gstart[-1]), len(bones)))           # the skin's weights under each ring
    grp = np.zeros(per, dtype=np.int64)                            # each vertex's ring
    colors = np.zeros((m, per, 3))
    uvs = np.zeros((m, per, 2))
    norms = np.zeros((m, per, 3))
    unders = np.zeros((m, per, 3))                   # (the normals of a strand's underside)
    faces, below = [], []
    for q in range(STRANDS):
        lane = q - (STRANDS - 1) / 2
        lf = (np.ones(m) if lane == 0 else rng.uniform(*st["side_len"], m)) * np.maximum(reach, 0.15)
        fan = lane * rng.uniform(0.15, 0.3, m) * (0.55 if lock else 1.0)
        sw = width * st["widths"][0 if lane == 0 else 1] * rng.uniform(0.85, 1.15, m)
        tone_q = col * (shade * rng.uniform(0.9, 1.1, m))[:, None]
        drop = (0.0 if lane == 0 else -0.12) * width
        o, ts = int(starts[q]), rings[q]
        tip = o + len(ts) * sides
        for k, t in enumerate(ts):
            tt = t * lf
            spread = ss(0.45, 1.0, t) if lock else t           # a lock's strands part only toward their tips
            c = at(C, tt) + at(side, tt) * (width * lane * (0.26 if lock else 0.1) + width * fan * spread)[:, None]
            c = c + at(up, tt) * (drop * t)[:, None]
            sd, u = at(side, tt), at(up, tt)
            ring_w[:, gstart[q] + k] = at(SW, tt)
            grp[o + k * sides:o + (k + 1) * sides] = gstart[q] + k
            if lock:                                           # full down the lock, rounding off toward the tip
                prof = (0.75 + 0.25 * math.sin(min(t / 0.4, 1.0) * math.pi / 2)) * (1.0 - 0.45 * ss(0.55, 0.95, t))
            else:
                prof = (0.85 + 0.15 * math.sin(min(t / 0.3, 1.0) * math.pi / 2)) * (1 - 0.8 * t ** 1.5)
            half = 0.5 * sw * prof
            for j, (sx, sy) in enumerate(section):
                idx = o + k * sides + j
                verts[:, idx] = c + sd * (sx * half)[:, None] + u * (sy * half * flat)[:, None]
                colors[:, idx] = tone_q * (0.86 + 0.2 * t ** 0.8)
                uvs[:, idx] = np.stack([length * tt * 6.0, np.full(m, j / sides * 0.6)], 1)
                norms[:, idx] = sd * sx + u * sy + u * st["soft"]
                unders[:, idx] = sd * (0.4 * sx) - u
            if k == len(ts) - 1:                               # the tip: a round cap on the last ring, or a point
                if lock:
                    tg = at(tang, tt)
                    verts[:, tip] = c + tg * (half * 1.1)[:, None]
                    norms[:, tip] = tg + u * st["soft"]
                    unders[:, tip] = tg - u
                else:
                    verts[:, tip] = at(C, lf) + at(side, lf) * (width * lane * 0.1 + width * fan)[:, None] + at(up, lf) * drop[:, None]
                colors[:, tip] = tone_q * 1.08
                ring_w[:, gstart[q + 1] - 1] = at(SW, lf)
                grp[tip] = gstart[q + 1] - 1
                uvs[:, tip] = np.stack([length * lf * 6.0, np.zeros(m)], 1)
        under = [section[j][1] + section[(j + 1) % sides][1] <= 0 for j in range(sides)]    # the flat underside
        for k in range(len(ts) - 1):
            for j in range(sides):
                a, b = o + k * sides + j, o + k * sides + (j + 1) % sides
                faces += [(a, b, b + sides), (a, b + sides, a + sides)]
                below += [under[j]] * 2
        last = o + (len(ts) - 1) * sides
        for j in range(sides):
            faces.append((last + j, last + (j + 1) % sides, tip))
            below.append(under[j])
    faces = np.asarray(faces, dtype=np.int64)
    F = (faces[None, :, :] + (np.arange(m) * per)[:, None, None]).reshape(-1, 3)
    weights = []                                     # one dict per ring (its four strongest bones), shared
    top4 = np.argsort(-ring_w, axis=2, kind="stable")[:, :, :4]
    for i in range(m):
        ws = [{bones[b]: float(ring_w[i, g, b]) for b in top4[i, g] if ring_w[i, g, b] > 1e-4}
              for g in range(ring_w.shape[1])]
        weights += [ws[g] for g in grp]
    normals = None
    if st["soft"] > 0:                               # per face corner: soft over the top, facing down underneath
        top, bottom = norms.reshape(-1, 3), unders.reshape(-1, 3)
        normals = np.where(np.tile(below, m)[:, None, None], bottom[F], top[F])
        normals /= np.maximum(np.linalg.norm(normals, axis=2, keepdims=True), 1e-9)
    return verts.reshape(-1, 3), F, colors.reshape(-1, 3), uvs.reshape(-1, 2), weights, normals
