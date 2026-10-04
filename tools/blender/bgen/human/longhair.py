"""Long hair (head-local units): a curtain of locks set out round the head.

Each lock of the outer layer roots at the parting (the locks of the back at the crown), arcs down over the scalp to
where the hair leaves the head (a ring over the temples, above the ears and round the occiput) and hangs from there,
draped over the shoulders and down the back (hairdrape.py); the locks nearest the face fall in front of the shoulders,
framing it, the rest behind them, steered off the shoulders before they reach them. A shorter inner layer fills the
curtain out at the back and sides. Locks narrow toward the parting, keep above the hairline over the forehead, lie
close on the scalp and turn fuller where they hang; every other one is a little shorter, so the ends make a V of a few
big tips. Under a hat the locks start just inside its lower edge.
"""
import math

import numpy as np

from .hair import hairlines
from .hairsdf import Strands

F = np.float32
RING_A = np.asarray((0.85, 1.15, 1.55, 2.0, 2.6, math.pi))          # azimuth from the front (+ toward the right)
RING_Z = np.asarray((0.03, 0.026, 0.03, 0.012, -0.012, -0.022))      # where the hair leaves the head at it
MIDDLE = np.asarray((0.0, -0.012, 0.01), dtype=F)                     # the skull's middle, for arcs over it
FRONT = 1.3           # locks nearer the face than this (azimuth) fall in front of the shoulders


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
    """Where the outer locks root: along the parting from the forehead back to the crown, round the crown behind it;
    under a hat just inside its lower edge (a cap's rim runs low at the nape, high over the brow)."""
    if g.under_hat:
        rim = g.under_hat.edge_at(np.stack([np.sin(phi) * 0.13, np.cos(phi) * 0.13 - 0.012, np.zeros(len(phi))], 1))
        return _at(g, phi, np.maximum(rim + 0.008, ring_z + 0.01))
    s = np.sign(phi)[:, None]
    a = np.clip((np.abs(phi) - RING_A[0]) / (math.pi / 2 - RING_A[0]), 0.0, 1.0)[:, None] ** 0.8
    front = np.asarray((g.part_x, 0.094, 0.045)) + s * (0.008, 0.0, 0.0)     # at the hairline, beside the parting
    crown = g.crown + s * np.asarray((0.006, 0.0, 0.0)) - MIDDLE
    d = _unit(front - MIDDLE) * (1 - a) + _unit(crown) * a
    back = np.abs(phi) > math.pi / 2                 # round the crown, radiating from the whorl
    whorl = g.crown + 0.014 * np.stack([np.sin(phi), np.cos(phi), np.zeros(len(phi))], 1) - MIDDLE
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
        floor = hairlines(p, g.part, False) + half[j] * 0.7 * _ss(1.05, 0.5, az)
        p[:, 2] += np.maximum(floor - p[:, 2], 0.0) * _ss(1.25, 0.95, az) * (1 - t ** 4)
        P[j] = g._onto(p, g.height(p, 0.0, None) + lift[j])
    return P


def _normals(g, P, rest, K1):
    """The way each lock's flat side faces: out from the head on it, out from the neck below it (and wherever the
    skull's own normal turns away, as it does by the face), flat on the body where it rests."""
    N = g._normal(P.reshape(-1, 3), skull=True).reshape(P.shape)
    out = _unit(P * np.asarray((1.0, 1.0, 0.0), dtype=F) + np.asarray((0.0, 0.02, 0.0), dtype=F))
    off = np.clip((0.9 - np.einsum("kni,kni->kn", N, out)) / 0.3, 0.0, 1.0) * (np.arange(len(P)) >= K1)[:, None]
    a = np.maximum(_ss(-0.04, -0.14, P[:, :, 2]), off)[..., None]
    N = _unit(N * (1 - a) + out * a)
    on = (np.abs(rest).sum(2) > 0)[..., None]
    N = np.where(on, rest, N)
    N[1:-1] = N[1:-1] * 0.5 + (N[:-2] + N[2:]) * 0.25
    return _unit(N)


def _layer(g, phi, roots, rings, K1, K2, width, thick, length, lift, taper, root):
    """One layer of locks: over the scalp from their roots to ``rings`` (K1 rings), then hanging (K2 rings)."""
    n = len(phi)
    t1 = np.linspace(0.0, 1.0, K1)[:, None]
    t = np.r_[np.linspace(0.0, 1.0, K1), np.ones(K2)][:, None]
    hang = np.r_[np.zeros(K1), np.linspace(0.0, 1.0, K2)][:, None]
    lo = 1.0 if g.under_hat else 0.6                    # narrower toward the parting
    W = width[None, :] * (lo + (1 - lo) * t) * (1 + 0.2 * hang)
    T = thick[None, :] * (0.55 + 0.45 * _ss(0.0, 0.25, hang))
    P = np.zeros((K1 + K2, n, 3), dtype=F)
    P[:K1] = _arcs(g, roots, rings, K1, lift + thick[None, :] * 0.28 * (0.3 + 0.7 * _ss(0.0, 0.3, t1)), W[:K1] * 0.5)
    radius = (thick * 0.55).astype(F)
    ahead = np.clip((FRONT - np.abs(phi)) / 0.15, -1.0, 1.0)
    bias = np.stack([np.zeros(n), ahead * 0.9, np.zeros(n)], 1).astype(F)
    # Steered in front of the shoulders or behind them as they reach them (the locks at the back hang straight).
    lean = bias * np.where(ahead > 0, 0.3, 0.55 * _ss(2.5, 1.9, np.abs(phi)))[:, None]
    P = g.drape.hang(P, np.full(n, K1), length / K2, radius, bias, lean=lean)
    stiff = np.r_[np.zeros(K1), np.full(K2, 0.25)].astype(F)
    P, rest = g.drape.settle(P, np.full(n, K1), radius, bias, rounds=14, stiff=stiff, lean=lean, fade=True, smooth=5)
    return Strands(P, W, T, taper=taper, root=root, N=_normals(g, P, rest, K1), round=0.32)


def long_locks(g):
    """The outer and inner layers of long hair."""
    rng = g.rng
    st = g.st
    fall = st.fall * g.head.frame.s / g.head.h
    hat = bool(g.under_hat)             # under a brim: from the temples, a little fuller to close the curtain
    side = 9 if hat else 8
    wide = st.width * (1.1 if hat else 1.0)
    a0 = 1.0 if hat else RING_A[0]
    a = a0 + (math.pi - 0.14 - a0) * np.linspace(0.0, 1.0, side)
    phi = np.r_[a, -a] + rng.uniform(-0.04, 0.04, 2 * side)
    ring_z = np.interp(np.abs(phi), RING_A, RING_Z)
    rings = _at(g, phi, ring_z)
    n = len(phi)
    alt = np.where(np.arange(n) % 2, 0.93, 1.03)
    length = fall * (0.7 + 0.3 * _ss(1.0, 3.0, np.abs(phi))) * alt * rng.uniform(0.97, 1.03, n)
    outer = _layer(g, phi, _roots(g, phi, ring_z), rings, 7, 16, wide * rng.uniform(0.9, 1.1, n),
                   st.thick * rng.uniform(0.9, 1.1, n), length, np.zeros((7, 1)), 0.72, 0.35)
    # The inner layer: under the outer one, from a little lower round the back and sides.
    b = np.linspace(1.35, math.pi - 0.3, 5)
    phi = np.r_[b, -b] + rng.uniform(-0.05, 0.05, 10)
    ring_z = np.interp(np.abs(phi), RING_A, RING_Z) - 0.02
    rings = _at(g, phi, ring_z)
    tops = _at(g, phi, ring_z + 0.016)
    m = len(phi)
    length = fall * (0.6 + 0.15 * _ss(1.0, 2.6, np.abs(phi))) * rng.uniform(0.95, 1.0, m)       # ends hidden under it
    inner = _layer(g, phi, tops, rings, 2, 16, st.width * rng.uniform(0.9, 1.05, m),
                   st.thick * rng.uniform(0.9, 1.05, m), length, np.full((2, 1), -0.004), 0.75, 0.3)
    return [outer, inner]
