"""Hanging hair: the parts of long locks and tails that fall free of the scalp under their own weight. They are kept
off the head and off the body and the clothes over it, rest on the shoulders and back and slide off the shoulders to
the front or behind them (never outward along them), and are relaxed into smooth curves (rounds of position
constraints: fixed segment lengths from the root out, a little bending stiffness, gravity and the collisions).

Head-local units (headshape); the body and its clothes are BVHs in world space.
"""
import numpy as np
from mathutils import Vector

from . import sdf

F = np.float32
DOWN = np.asarray((0.0, 0.0, -1.0), dtype=F)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def shoulders(z):
    """1 at the height of the shoulders (head-local z), fading out above and below them."""
    return (_ss(-0.05, -0.12, z) * _ss(-0.28, -0.18, z)).astype(F)


def slide(p, normal, bias):
    """Which way hair resting on the body at p (normal ``normal``) slides: down and to the front or back (``bias``),
    along the surface, and never outward along the shoulder; off the top of the shoulder it slides quickly."""
    up = np.clip((normal[:, 2] - 0.3) / 0.4, 0.0, 1.0)[:, None]      # off the top of the shoulders quickly
    g = DOWN + bias * (1 + up)
    g = g - normal * np.einsum("ij,ij->i", g, normal)[:, None]
    sx = np.sign(p[:, 0])
    g[:, 0] = sx * np.minimum(sx * g[:, 0], 0.0)
    return g


class Drape:
    def __init__(self, groom, body=None, worn=None):
        self.g = groom
        self.body = body                 # BVH of the bare body (world)
        self.worn = worn                 # BVH of the clothes over it (world), or None
        self.c = np.asarray(groom.head.c, dtype=float)
        self.h = groom.head.h

    # ------------------------------------------------------------------ collisions
    def off_head(self, q, height):
        """Points kept ``height`` (per point) above the head and neck (ears included, or covered over for long hair)."""
        full = self.g.hang
        q = np.array(q, dtype=F)
        for _ in range(3):
            d = full(q) - height
            inside = d < 0
            if not inside.any():
                break
            grad = _unit(sdf.gradient(full, np.ascontiguousarray(q[inside]), F(0.0006)))
            q[inside] -= grad * d[inside, None]
        return q

    def off_body(self, q, gap, cloth=0.006):
        """Points kept ``gap`` (per point, head-local) off the body (``cloth`` more, for a shirt over it) and off the
        clothes; returns the points and the surface normal where each one rests (zero where it hangs free)."""
        rest = np.zeros_like(q)
        c, h = self.c, self.h
        low = np.flatnonzero(q[:, 2] < -0.06)            # nothing above the jaw reaches the body
        for tree, extra in ((self.body, cloth), (self.worn, 0.0)):
            if tree is None:
                continue
            for i in low:
                w = Vector(c + q[i] * h)
                hit, normal, _, dist = tree.find_nearest(w, 0.25 * h)
                if hit is None:
                    continue
                off = (gap[i] + extra) * h
                side = (w - hit).dot(normal)
                # Too near, or sunk in behind the surface; not merely beside a rim (a neckline, a hem) facing away.
                if side < off and (dist < off or side < -0.5 * dist):
                    q[i] = (np.asarray(hit + normal * off) - c) / h
                    rest[i] = normal
        return q, rest

    def collide(self, q, radius):
        q = self.off_head(q, radius * 0.85 + self.g.st.cap + 0.002)
        return self.off_body(q, radius + 0.0015)

    # ------------------------------------------------------------------ chains
    def hang(self, P, start, step, radius, bias, momentum=0.25, lean=None):
        """Continues chains P (rings x chains x 3) from ring ``start`` (per chain, at least 2) on in segments of
        ``step`` (per chain): they fall from where they leave the head, carrying on a little the way they were going
        and leaning ``lean`` (chains x 3) round the shoulders, and slide down over whatever they land on."""
        K, n = P.shape[:2]
        P = np.array(P, dtype=F)
        start = np.asarray(start)
        idx = np.arange(n)
        d = _unit(P[start - 1, idx] - P[start - 2, idx])
        step = np.asarray(step, dtype=F)
        rest = np.zeros((n, 3), dtype=F)
        for k in range(1, K):
            m = k >= start
            if not m.any():
                continue
            prev = P[k - 1, m]
            f = d[m] * momentum + DOWN + (0 if lean is None else lean[m] * shoulders(prev[:, 2])[:, None])
            on = np.abs(rest[m]).sum(1) > 0
            if on.any():
                f[on] = slide(prev[on], rest[m][on], bias[m][on])
            dm = _unit(d[m] * 0.4 + _unit(f) * 0.6)
            q, r = self.collide(prev + dm * step[m, None], radius[m])
            P[k, m] = q
            d[m] = dm
            rest[m] = r
        return P

    def settle(self, P, fixed, radius, bias=None, rounds=30, stiff=None, pull=0.3, lean=(0.0, 0.0, 0.0), fade=False,
               smooth=0, constrain=None):
        """Relaxes chains P (rings x chains x 3) whose first ``fixed`` rings (per chain) are held: gravity pulls the
        rest down (and ``lean`` sideways, against the head and back; with ``fade`` only round the shoulders, steering
        long hair in front of them or behind), bending stiffness (``stiff`` per ring, 0..1)
        keeps them smooth, segment lengths stay as they are, and collisions keep them off the head, body and clothes
        (``radius``, per chain, or per ring and chain: the lock's half thickness). Chains resting on the body slide
        (``bias``, chains x 3: to the front or behind). Returns the chains and the body's normal under every point
        resting on it. ``smooth`` rounds of smoothing at the end round off kinks. ``constrain(P) -> P`` is applied every
        round before the segment lengths are restored (to hold chains together, say)."""
        K, n = P.shape[:2]
        P = np.array(P, dtype=F)
        seg = np.linalg.norm(np.diff(P, axis=0), axis=2)                # (K-1, n)
        free = np.arange(K)[:, None] >= np.asarray(fixed)[None, :]     # (K, n)
        stiff = np.full(K, 0.3, dtype=F) if stiff is None else np.asarray(stiff, dtype=F)
        r = np.asarray(radius, dtype=F)
        rad = np.broadcast_to(r if r.ndim == 2 else r[None] if r.ndim == 1 else r, (K, n))
        rest = np.zeros((K, n, 3), dtype=F)
        step = F(pull) * seg.mean()
        lean = np.broadcast_to(np.asarray(lean, dtype=F), (n, 3))      # (3,) or per chain
        lean = np.broadcast_to(lean[None], P.shape)[free]
        for _ in range(rounds):
            P[free] += (DOWN + lean * (shoulders(P[free][:, 2])[:, None] if fade else 1.0)) * step
            if bias is not None:
                on = (np.abs(rest).sum(2) > 0) & free
                if on.any():
                    b = np.broadcast_to(bias[None], P.shape)[on]
                    P[on] += (slide(P[on], rest[on], b) - DOWN) * step
            for k in range(2, K):         # bending: toward the straight continuation of the two rings before
                m = free[k]
                P[k, m] += (2 * P[k - 1, m] - P[k - 2, m] - P[k, m]) * stiff[k]
            if constrain is not None:
                P = np.where(free[..., None], constrain(P.copy()), P)
            for k in range(1, K):         # follow the leader: every segment keeps its length
                m = free[k]
                P[k, m] = P[k - 1, m] + _unit(P[k, m] - P[k - 1, m]) * seg[k - 1, m, None]
            q, rr = self.collide(P[free], rad[free])
            P[free] = q
            rest[free] = rr
        for _ in range(smooth):           # kinks where the chains met the body rounded off
            Q = P.copy()
            Q[1:-1] = P[1:-1] * 0.5 + (P[:-2] + P[2:]) * 0.25
            inner = free.copy()
            inner[-1] = False
            P[inner] = Q[inner]
            for k in range(1, K):
                m = free[k]
                P[k, m] = P[k - 1, m] + _unit(P[k, m] - P[k - 1, m]) * seg[k - 1, m, None]
            q, rr = self.collide(P[free], rad[free])
            P[free] = q
            rest[free] = rr
        return P, rest
