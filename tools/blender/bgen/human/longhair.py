"""Long hair (head-local units): one curtain of locks set out round the head.

Each lock roots at the parting (the locks of the back round the crown), arcs down over the scalp to where the hair
leaves the head (a ring over the temples, above the ears and round the occiput) and hangs from there over the ears
(hair.ear_cover: one smooth dome over each ear, so the curtain never parts round it), draped over the shoulders and
down the back (hairdrape.py). The locks nearest the face fall in front of the shoulders, framing it, the rest behind
them. Neighbouring locks are held together down to the shoulders, so the curtain stays closed over the ears and the
neck, and gather toward their ends (in pairs behind the shoulders, those in front of each into one lock framing the
face), which makes a few big tapered locks with soft grooves between them; the ends make a shallow V, longest down
the middle of the back. Locks lie thin along the hairline and narrow toward the
parting. Under a hat the locks start just inside its lower edge.
"""
import math

import numpy as np

from .hair import hairlines
from .hairsdf import Strands

F = np.float32
RING_A = np.asarray((0.85, 1.15, 1.55, 2.0, 2.6, math.pi))          # azimuth from the front (+ toward the right)
RING_Z = np.asarray((0.03, 0.026, 0.028, 0.012, -0.01, -0.02))       # where the hair leaves the head at it
MIDDLE = np.asarray((0.0, -0.012, 0.01), dtype=F)                     # the skull's middle, for arcs over it
FRONT = 1.3           # locks nearer the face than this (azimuth) fall in front of the shoulders
SIDE = 11             # locks a side


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


def _at(g, phi, z):
    """Points on the scalp at azimuths ``phi`` and heights ``z``."""
    p = np.stack([np.sin(phi) * 0.13, np.cos(phi) * 0.13 - 0.012, z], 1).astype(F)
    return g._onto(p, np.full(len(p), g.st.cap, dtype=F), steps=8)


def _roots(g, phi, ring_z):
    """Where the locks root: along the parting from the forehead back to the crown, round the crown behind it;
    under a hat just inside its lower edge (a cap's rim runs low at the nape, high over the brow)."""
    if g.under_hat:
        rim = g.under_hat.edge_at(np.stack([np.sin(phi) * 0.13, np.cos(phi) * 0.13 - 0.012, np.zeros(len(phi))], 1))
        return _at(g, phi, np.maximum(rim + 0.008, ring_z + 0.01))
    s = np.sign(phi)[:, None]
    a = np.clip((np.abs(phi) - RING_A[0]) / (math.pi / 2 - RING_A[0]), 0.0, 1.0)[:, None] ** 0.8
    front = np.asarray((g.part_x, 0.094, 0.045)) + s * (0.006, 0.0, 0.0)     # at the hairline, beside the parting
    crown = g.crown + s * np.asarray((0.005, 0.0, 0.0)) - MIDDLE
    d = _unit(front - MIDDLE) * (1 - a) + _unit(crown) * a
    back = np.abs(phi) > math.pi / 2                 # round the crown, radiating from the whorl
    whorl = g.crown + 0.012 * np.stack([np.sin(phi), np.cos(phi), np.zeros(len(phi))], 1) - MIDDLE
    d[back] = whorl[back]
    return g._onto((MIDDLE + _unit(d) * 0.14).astype(F), np.full(len(phi), g.st.cap, dtype=F), steps=10)


def _arcs(g, a, b, K, lift, half):
    """Paths (K x n x 3) over the scalp from a to b, lying ``lift`` (rings x locks) above the layer of hair; over the
    forehead the locks (``half`` their width, rings x locks) keep above the hairline instead of covering the brow."""
    da, db = a - MIDDLE, b - MIDDLE
    ra, rb = np.linalg.norm(da, axis=1), np.linalg.norm(db, axis=1)
    ua, ub = da / ra[:, None], db / rb[:, None]
    om = np.arccos(np.clip(np.einsum("ij,ij->i", ua, ub), -1.0, 1.0))[:, None]
    P = np.zeros((K, len(a), 3), dtype=F)
    for j, t in enumerate(np.linspace(0.0, 1.0, K)):
        w0 = np.where(om > 1e-4, np.sin((1 - t) * om) / np.maximum(np.sin(om), 1e-6), 1 - t)
        w1 = np.where(om > 1e-4, np.sin(t * om) / np.maximum(np.sin(om), 1e-6), t)
        p = MIDDLE + _unit(ua * w0 + ub * w1) * (ra * (1 - t) + rb * t)[:, None]
        az = np.abs(np.arctan2(p[:, 0], p[:, 1]))
        floor = hairlines(p, g.part, False) + half[j] * 0.6 * _ss(1.05, 0.5, az)
        p[:, 2] += np.maximum(floor - p[:, 2], 0.0) * _ss(1.25, 0.95, az) * (1 - t ** 4)
        P[j] = g._onto(p, g.height(p, 0.0, None) + lift[j])
    return P


def _normals(g, P, rest, K1, front):
    """The way each lock's flat side faces: out from the head on it, out from the neck below it (and wherever the
    head's own normal turns away, as it does by the face), flat on the body where it rests; the locks in front of
    the shoulders (``front``) turn to face forward below the jaw, lying flat over the chest."""
    N = g._normal(P.reshape(-1, 3)).reshape(P.shape)
    out = _unit(P * np.asarray((1.0, 1.0, 0.0), dtype=F) + np.asarray((0.0, 0.02, 0.0), dtype=F))
    q = P[:, front]
    fwd = _unit(np.stack([np.sign(q[..., 0]) * 0.3, np.ones(q.shape[:2]), np.zeros(q.shape[:2])], -1))
    f = _ss(-0.05, -0.15, q[..., 2])[..., None]
    out[:, front] = _unit(out[:, front] * (1 - f) + fwd * f)
    off = np.clip((0.9 - np.einsum("kni,kni->kn", N, out)) / 0.3, 0.0, 1.0) * (np.arange(len(P)) >= K1)[:, None]
    a = np.maximum(_ss(-0.04, -0.14, P[:, :, 2]), off)[..., None]
    N = _unit(N * (1 - a) + out * a)
    on = (np.abs(rest).sum(2) > 0)[..., None]
    N = np.where(on, rest, N)
    for _ in range(2):
        N[1:-1] = N[1:-1] * 0.5 + (N[:-2] + N[2:]) * 0.25
    return _unit(N)


def _pairs(phi):
    """Neighbouring locks round the curtain (index pairs, and whether the pair straddles the front of a shoulder:
    one lock falling in front of it, the other behind)."""
    order = np.argsort(np.where(phi < 0, phi + 2 * math.pi, phi))        # right front .. back .. left front
    a, b = order[:-1], order[1:]
    split = (np.abs(phi[a]) < FRONT) != (np.abs(phi[b]) < FRONT)
    return np.stack([a, b], 1), split


def _cohesion(K1, pairs, split, gap, front, curl):
    """A constraint for the drape: neighbouring chains further apart than ``gap`` (per pair) are drawn together, down
    to the top of the shoulders where one falls in front of a shoulder and the other behind it, down the back
    otherwise; the chains by the face (``front``) are drawn in against the cheeks and jaw; with ``curl`` the ends of
    every chain turn in under toward the neck (a bob)."""
    a, b = pairs[:, 0], pairs[:, 1]

    def pull(P):
        q = P[K1:, front]
        q[..., 0] -= np.sign(q[..., 0]) * 0.0015 * _ss(0.02, -0.02, q[..., 2]) * _ss(-0.12, -0.07, q[..., 2])
        P[K1:, front] = q
        Q = P[K1:]
        if curl:
            h = _ss(0.5, 1.0, np.linspace(0.0, 1.0, len(Q)))[:, None, None]
            axis = Q * np.asarray((1.0, 1.0, 0.0), dtype=F) - np.asarray((0.0, -0.015, 0.0), dtype=F)
            Q -= _unit(axis) * F(0.003) * h
        d = Q[:, b] - Q[:, a]
        L = np.linalg.norm(d, axis=2)
        z = 0.5 * (Q[:, a, 2] + Q[:, b, 2])
        w = np.where(split[None], _ss(-0.17, -0.1, z), 0.8 * _ss(-0.34, -0.2, z))
        move = d * (np.maximum(L - gap[None], 0.0) / np.maximum(L, 1e-6) * w * 0.35)[..., None]
        np.add.at(Q, (slice(None), a), move)
        np.add.at(Q, (slice(None), b), -move)
        P[K1:] = Q
        return P
    return pull


def _gather(g, P, K1, groups, radius):
    """The ends of the locks of each group drawn together into one tapering lock."""
    K = len(P)
    h = np.r_[np.zeros(K1), np.linspace(0.0, 1.0, K - K1)]
    c = (0.8 * _ss(0.45, 1.0, h)).astype(F)[:, None, None]
    mean = np.zeros_like(P)
    for grp in groups:
        mean[:, grp] = P[:, grp].mean(1, keepdims=True)
    P = P * (1 - c) + mean * c
    q, rest = g.drape.collide(P[K1:].reshape(-1, 3), np.broadcast_to(radius, (K - K1, P.shape[1])).reshape(-1))
    P[K1:] = q.reshape(K - K1, -1, 3)
    out = np.zeros_like(P)
    out[K1:] = rest.reshape(K - K1, -1, 3)
    return P, out


def _groups(phi):
    """The locks in front of each shoulder make one group framing the face; behind them they pair off, and the last
    lock of each side pairs with its mirror down the middle of the back."""
    out, mid = [], []
    for s in (1, -1):
        side = np.flatnonzero(np.sign(phi) == s)
        side = side[np.argsort(np.abs(phi[side]))]
        front = np.abs(phi[side]) < FRONT
        if front.any():
            out.append(side[front])
        side = side[~front]
        n = len(side) - (len(side) % 2)
        out += [side[i:i + 2] for i in range(0, n, 2)]
        mid += list(side[n:])
    if mid:
        out.append(np.asarray(mid))
    return out


def _layer(g, phi, roots, rings, K1, K2, width, thick, length, taper, root):
    """The curtain: over the scalp from the roots to ``rings`` (K1 rings), then hanging (K2 rings). A bob's ends are
    cut blunt in one line and turn under; long hair's gather into a few tapering locks."""
    bob = g.name == "bob"
    n = len(phi)
    t1 = np.linspace(0.0, 1.0, K1)[:, None]
    t = np.r_[np.linspace(0.0, 1.0, K1), np.ones(K2)][:, None]
    hang = np.r_[np.zeros(K1), np.linspace(0.0, 1.0, K2)][:, None]
    lo = 1.0 if g.under_hat else 0.6                    # narrower toward the parting
    W = width[None, :] * (lo + (1 - lo) * t) * (1 + 0.25 * hang)
    T = thick[None, :] * (0.5 + 0.5 * _ss(0.0, 0.25, hang)) * np.ones((K1 + K2, 1))
    P = np.zeros((K1 + K2, n, 3), dtype=F)
    P[:K1] = _arcs(g, roots, rings, K1, thick[None, :] * 0.2 * (0.2 + 0.8 * _ss(0.0, 0.3, t1)), W[:K1] * 0.5)
    radius = (thick * 0.5).astype(F)
    ahead = np.clip((FRONT - np.abs(phi)) / 0.15, -1.0, 1.0)
    bias = np.stack([np.zeros(n), ahead * 0.9, np.zeros(n)], 1).astype(F)
    # Steered in front of the shoulders or behind them as they reach them (the locks at the back hang straight).
    lean = bias * np.where(ahead > 0, 0.35, 0.9 * _ss(2.6, 2.0, np.abs(phi)))[:, None]
    P = g.drape.hang(P, np.full(n, K1), length / K2, radius, bias, lean=lean)
    pairs, split = _pairs(phi)
    gap = np.where(split, 0.4, 0.55) * np.minimum(width[pairs[:, 0]], width[pairs[:, 1]])
    stiff = np.r_[np.zeros(K1), np.full(K2, 0.25)].astype(F)
    P, rest = g.drape.settle(P, np.full(n, K1), radius, bias, rounds=16, stiff=stiff, lean=lean, fade=True, smooth=5,
                             constrain=_cohesion(K1, pairs, split, gap, np.flatnonzero(np.abs(phi) < FRONT), bob))
    if not bob:
        P, gathered = _gather(g, P, K1, _groups(phi), radius)
        rest = np.where(np.abs(gathered).sum(2, keepdims=True) > 0, gathered, rest)
    # Thin along the hairline, so the hair's edge there is soft rather than a ledge.
    edge = P[:K1, :, 2] - hairlines(P[:K1].reshape(-1, 3), g.part, False).reshape(K1, n)
    T[:K1] *= 0.35 + 0.65 * _ss(0.004, 0.03, edge)
    front = np.flatnonzero((np.abs(phi) < FRONT) & (not bob))
    return Strands(P, W, T, taper=taper, root=root, N=_normals(g, P, rest, K1, front), round=0.45,
                   blunt=0.55 if bob else 0.0, merge=0.8 if bob else 0.25, cut=bob)


def _fringe(g, n=6):
    """A side-swept fringe (a bob's): locks from the front of the parting swept down across the forehead to the far
    temple, the front ones lowest, each a little shorter than the one behind it."""
    sx = 1.0 if g.part >= 0 else -1.0
    K = 12
    k = np.linspace(0.0, 1.0, n)                     # front .. back
    a = np.stack([np.full(n, g.part_x - sx * 0.004), 0.088 - 0.032 * k, 0.05 + 0.034 * k], 1) - MIDDLE
    b = np.stack([-sx * (0.058 + 0.01 * k), 0.06 - 0.022 * k, 0.03 + 0.014 * k], 1) - MIDDLE
    ra, rb = np.linalg.norm(a, axis=1), np.linalg.norm(b, axis=1)
    sag = 0.011 * (1 - k) + 0.003
    P = np.zeros((K, n, 3), dtype=F)
    for j, t in enumerate(np.linspace(0.0, 1.0, K)):
        p = MIDDLE + _unit(a / ra[:, None] * (1 - t) + b / rb[:, None] * t) * (ra * (1 - t) + rb * t)[:, None]
        p[:, 2] -= sag * math.sin(math.pi * t)
        P[j] = g._onto(p, g.height(p, 0.0, None) + g.st.thick * 0.3)
    width = g.st.width * 0.85 * (1.0 - 0.15 * k)
    return Strands(P, width, np.full(n, g.st.thick * 0.7), taper=0.5, root=0.5, round=0.4, blunt=0.15, merge=0.5)


def long_locks(g):
    """The curtain of long hair."""
    rng = g.rng
    st = g.st
    fall = st.fall * g.head.frame.s / g.head.h
    hat = bool(g.under_hat)             # under a brim: from the temples, a little fuller to close the curtain
    wide = st.width * (1.1 if hat else 1.0)
    a0 = 1.0 if hat else RING_A[0]
    a = a0 + (math.pi - 0.11 - a0) * np.linspace(0.0, 1.0, SIDE)
    phi = np.r_[a, -a] + rng.uniform(-0.03, 0.03, 2 * SIDE)
    ring_z = np.interp(np.abs(phi), RING_A, RING_Z)
    rings = _at(g, phi, ring_z)
    n = len(phi)
    face = np.where(np.abs(phi) < FRONT, 1.0, 0.0)          # the locks by the face finer
    if g.name == "bob":                 # down to the chin, a little longer by the face, short at the nape
        length = fall * (1.0 - 0.22 * _ss(1.2, 2.8, np.abs(phi))) * rng.uniform(0.98, 1.02, n)
        return [_layer(g, phi, _roots(g, phi, ring_z), rings, 7, 10, wide * rng.uniform(0.95, 1.05, n),
                       st.thick * rng.uniform(0.95, 1.05, n), length, 0.75, 0.35), _fringe(g)]
    # Long enough at the sides to fall past the shoulders (behind them, or in front for the locks by the face).
    length = fall * np.where(np.abs(phi) < FRONT, 0.76, 0.86 + 0.14 * _ss(1.6, 3.0, np.abs(phi)))
    for grp in _groups(phi):            # a group's locks end together, every other group a little shorter
        length[grp] = length[grp].mean() * rng.uniform(0.95, 1.03)
    return [_layer(g, phi, _roots(g, phi, ring_z), rings, 7, 16, wide * rng.uniform(0.92, 1.08, n) * (1 - 0.1 * face),
                   st.thick * rng.uniform(0.92, 1.08, n) * (1 - 0.25 * face), length, 0.6, 0.35)]
