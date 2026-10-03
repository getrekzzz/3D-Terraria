"""Hair locks: where each lock of hair roots in the scalp and the path it takes in the style's flow (combed out from
the crown, swept over the forehead from a side parting, parted and falling behind the shoulders, gathered back to a
tie), traced over surfaces offset from the bare skull (headshape) so they hug it whatever the face looks like. Long
hair falls free below the head, stays off the head and body and slides off the shoulders; tied hair ends in a
ponytail or a bun. hairsdf sculpts the traced locks into the hair's mesh; a buzz cut is only a tinted cap.
"""
import math
from dataclasses import dataclass

import bmesh
import numpy as np
from mathutils import Vector

from . import sdf
from .hair import coverage, ear_clear, hairline, mesh_from
from .paint import hex3

F = np.float32


@dataclass(frozen=True)
class Style:
    cap: float            # height of the layer of hair under the locks (head-local)
    volume: float         # extra height over the top and front
    length: float         # lock length over the head
    spread: float         # random variation of the length (fraction)
    width: float          # lock width at its widest
    thick: float          # lock thickness (clumps are rounder than flat ribbons)
    count: int            # locks over a full scalp
    sweep: float          # fringe swept across from a side parting (-1..1), 0 for none
    mess: float           # random direction and lift
    fall: float = 0.0     # how far long hair hangs below the head (fraction of the frame scale)
    tie: tuple = None     # where tied hair is gathered (head-local)


STYLES = {
    "short": Style(0.003, 0.008, 0.07, 0.25, 0.02, 0.008, 110, 0.8, 0.05),
    "messy": Style(0.003, 0.01, 0.075, 0.4, 0.019, 0.0085, 120, 0.4, 0.6),
    "long": Style(0.003, 0.005, 0.1, 0.15, 0.026, 0.011, 190, 0.55, 0.08, fall=0.3),
    "ponytail": Style(0.0025, 0.003, 0.0, 0.0, 0.02, 0.006, 110, 0.0, 0.03, tie=(0.0, -0.098, 0.036)),
    "bun": Style(0.0025, 0.003, 0.0, 0.0, 0.02, 0.006, 110, 0.0, 0.03, tie=(0.0, -0.084, 0.074)),
    "buzz": Style(0.0012, 0.0, 0.0, 0.0, 0.0, 0.0, 0, 0.0, 0.0),
}

DOWN = np.asarray((0.0, 0.0, -1.0), dtype=F)


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


class Groom:
    RINGS = 8

    def __init__(self, head, style, color, under_hat=False, body_tree=None, seed=1):
        self.head = head
        self.st = STYLES[style]
        # Hair is laid over the bare skull (short hair grows round the ears, so it ignores them; long hair falls over
        # them); ``full`` adds the ears, which no hair may cut into.
        self.full = lambda p: head.shape.field(p, face=False)
        self.field = self.full if self.st.fall else (lambda p: head.shape.field(p, ears=False, face=False))
        self.name = style
        self.color = hex3(color)
        self.under_hat = under_hat
        self.body = body_tree                      # BVH of the body (world space): long hair rests on the shoulders
        self.rng = np.random.default_rng(seed)
        self.part = self.st.sweep
        self.sideburns = not (head.frame.female or self.st.tie)
        side = 1.0 if self.part >= 0 else -1.0
        self.crown = np.asarray((0.012 * side, -0.045, 0.088), dtype=F)
        self.phase = self.rng.uniform(0, 6.3, 3)

    # ------------------------------------------------------------------ surfaces
    def _normal(self, p):
        return _unit(sdf.gradient(self.field, np.ascontiguousarray(p, dtype=F), F(0.0006)))

    def _onto(self, p, height, steps=3):
        """Points moved onto the surface ``height`` (per point) above the skin."""
        p = np.array(p, dtype=F)
        for _ in range(steps):
            g = sdf.gradient(self.field, p, F(0.0006))
            d = self.field(p) - height
            p -= g * (d / np.maximum(np.einsum("ij,ij->i", g, g), F(1e-8)))[:, None]
        return p

    def height(self, p, layer=0.0, t=1.0):
        """Where hair lies, above the skin: with ``t`` None the layer under the clumps (fuller over the top and
        front); otherwise a clump's middle (``t`` 0 at its root .. 1 at its tip), which rises out of that layer and
        settles back toward it, a little higher for the upper layers, so clumps overlap like combed hair."""
        top = _ss(-0.01, 0.08, p[:, 2]) * _ss(-0.05, 0.04, p[:, 1])
        base = self.st.cap + self.st.volume * top
        if t is None:
            return base.astype(F)
        t = np.asarray(t, dtype=F)
        arch = np.maximum(np.sin(np.pi * np.clip(t, 0, 1) ** 0.8), 0.0) ** 0.6   # (sin of pi in float32 is just below 0)
        return (base + self.st.thick * (arch * (0.1 + layer * 0.12) - 0.6 * (1 - t) ** 3)).astype(F)

    def _grows(self, q, slack=0.0):
        return coverage(Vector(q), self.part, self.sideburns, 0.002) > 0.5 if slack == 0 else (
            q[2] - hairline(Vector(q), self.part, self.sideburns) > -slack and ear_clear(Vector(q)) > 0.3)

    # ------------------------------------------------------------------ roots and flow
    def roots(self):
        """Lock roots spread evenly over the part of the scalp that grows hair (a jittered spiral of directions)."""
        n = int(self.st.count / 0.4)
        i = np.arange(n) + 0.5
        z = 1 - 2 * i / n
        a = i * math.pi * (3 - math.sqrt(5))
        dirs = np.stack([np.sqrt(1 - z * z) * np.cos(a), np.sqrt(1 - z * z) * np.sin(a), z], 1)
        dirs = _unit(dirs + self.rng.normal(0, 0.9 / math.sqrt(n), dirs.shape))
        p = self._onto(np.asarray((0, -0.01, 0.0)) + dirs * 0.13, self.st.cap * 0.5, steps=5)
        keep = np.array([self._grows(q) and not (self.under_hat and q[2] > 0.03) for q in p])
        return p[keep]

    def flow(self, p):
        """The direction hair lies at p (not yet in the surface's plane)."""
        st = self.st
        if st.tie is not None:                     # gathered back to the tie
            return _unit(np.asarray(st.tie, dtype=F) - p)
        if st.fall:
            # Parted: away from the parting and down the sides, the front swept back past the temples (never over the
            # face), the back straight down.
            s = np.where(p[:, 0] >= 0.016 * self.part, 1.0, -1.0)
            front = _ss(-0.02, 0.06, p[:, 1])
            back = _ss(-0.03, -0.08, p[:, 1])[:, None]
            side = np.stack([s, -0.25 - 0.7 * front, np.full(len(p), -0.6)], 1)
            down = np.stack([p[:, 0] * 2, np.full(len(p), -0.3), np.full(len(p), -1.0)], 1)
            return (side * (1 - back) + down * back).astype(F)
        # Combed out from the crown; over the forehead swept across, away from the parting.
        v = _unit(p - self.crown)
        front = _ss(-0.01, 0.05, p[:, 1]) * _ss(0.0, 0.07, p[:, 2])
        sweep = np.asarray((-math.copysign(1.0, self.part), 0.4, -0.15), dtype=F) * abs(self.part)
        noise = np.stack([np.sin(p[:, 0] * 61 + p[:, 2] * 37 + self.phase[0]), np.sin(p[:, 1] * 53 + p[:, 0] * 29 + self.phase[1]),
                          np.sin(p[:, 2] * 47 + p[:, 1] * 31 + self.phase[2])], 1)
        return v + front[:, None] * sweep * 1.3 + noise * st.mess * 0.6

    # ------------------------------------------------------------------ tracing
    def trace(self, start, lengths, layers, turn, hang=None, k_rings=None, hanging=False):
        """Polylines (rings x locks x 3) from each start point, following the flow over the layer's surface; locks
        marked by ``hang`` (a test on points) fall free once they reach it, staying off the head and the body and
        sliding down over the shoulders, in front of them or behind (``hanging`` locks fall from the start, steered
        only by ``turn``)."""
        K = k_rings or self.RINGS
        n = len(start)
        P = np.zeros((K, n, 3), dtype=F)
        P[0] = start if hanging else self._onto(start, self.height(start, layers, 0.0))
        step = (lengths / (K - 1)).astype(F)
        lie = np.minimum(lengths, max(self.st.length, 0.05))        # how much of each lock lies on the head
        lift = np.where(self.rng.random(n) < self.st.mess * 0.5, self.rng.uniform(0.002, 0.009, n), 0.0).astype(F)
        free = np.full(n, hanging)
        rest = np.zeros((n, 3), dtype=F)           # the body's normal under a lock resting on it
        ahead = np.zeros(n)                        # +1 falls in front of the shoulder, -1 behind it
        d = None
        for k in range(1, K):
            prev = P[k - 1]
            nrm = self._normal(prev)
            f = (turn if hanging else self.flow(prev) + turn).copy()
            f[free] = f[free] * ((0.6 if k == 1 else 0.15) if hanging else 0.25) + DOWN
            on = free & (np.abs(rest).sum(1) > 0)
            if on.any():                           # slide down the body: off the top of the shoulder to the front or
                up = np.clip(rest[on, 2] * 2.0, 0.0, 1.0)   # back, and never in toward the neck
                g = DOWN + np.stack([np.zeros(on.sum()), ahead[on] * 0.7 * up, np.zeros(on.sum())], 1)
                g = g - rest[on] * np.einsum("ij,ij->i", g, rest[on])[:, None]
                sx = np.sign(prev[on, 0])
                g[:, 0] = sx * np.maximum(sx * g[:, 0], 0.0)
                f[on] = g
            f[~free] -= nrm[~free] * np.einsum("ij,ij->i", f[~free], nrm[~free])[:, None]
            f = _unit(f)
            d = f if d is None else _unit(d * 0.45 + f * 0.55)
            q = prev + d * step[:, None]
            hgt = self.height(q, layers, np.minimum(k * step / lie, 1.0)) + lift * (k / (K - 1)) ** 2
            if (~free).any():
                q[~free] = self._onto(q[~free], hgt[~free])
            if free.any():
                q[free], rest[free] = self._clear(q[free], hgt[free] + 0.004)
            if hang is not None:
                new = hang(q) & ~free
                ahead[new] = np.where(q[new, 1] > 0.012, 1.0, -1.0)
                free |= new
            P[k] = q
        return P

    def _clear(self, q, height):
        """Hanging hair kept off the head (``height`` above it) and off the body and clothes; returns the points and
        the body's normal where they rest on it (zero elsewhere)."""
        inside = self.field(q) < height
        if inside.any():
            q[inside] = self._onto(q[inside], height[inside], steps=2)
        rest = np.zeros_like(q)
        if self.body is not None:
            h, c = self.head.h, np.asarray(self.head.c)
            gap = 0.026
            for i in range(len(q)):
                w = Vector(c + q[i] * h)
                hit, normal, _, _ = self.body.find_nearest(w, 0.2)
                if hit is not None and (w - hit).dot(normal) < gap:
                    q[i] = (np.asarray(hit + normal * gap) - c) / h
                    rest[i] = normal
        return q, rest

    # ------------------------------------------------------------------ the parts
    def scalp_locks(self):
        """The locks rooted over the scalp, trimmed where short hair would run past the hairline or onto the ears."""
        st = self.st
        roots = self.roots()
        n = len(roots)
        if st.tie is not None:
            lengths = np.linalg.norm(np.asarray(st.tie) - roots, axis=1) * 1.05
        else:
            top = _ss(0.0, 0.08, roots[:, 2]) * _ss(-0.06, 0.03, roots[:, 1])
            lengths = st.length * (0.7 + 0.5 * top) * (1 + self.rng.uniform(-st.spread, st.spread, n))
            if st.fall:
                lengths = lengths + st.fall * self.head.frame.s / self.head.h * self.rng.uniform(0.75, 1.0, n) * (0.7 + 0.3 * _ss(-0.06, 0.04, -roots[:, 1]))
        layers = self.rng.integers(0, 3, n).astype(F)
        turn = self.rng.normal(0, 0.1 + 0.5 * st.mess, (n, 3)).astype(F)
        hang = (lambda q: (q[:, 2] < -0.01) & ((np.abs(q[:, 0]) > 0.05) | (q[:, 1] < -0.02))) if st.fall else None
        rings = 12 if st.fall else self.RINGS
        P = self.trace(roots, lengths, layers, turn, hang, rings)
        if not st.fall and st.tie is None:          # short hair: stop at the hairline (a fringe may overhang it)
            slack = np.where(roots[:, 1] > 0.03, 0.012, 0.006)
            cut = np.full(n, rings - 1)
            for i in range(n):
                for k in range(1, rings):
                    if not self._grows(P[k, i], slack[i]):
                        cut[i] = k
                        break
            short = cut < rings - 1
            if short.any():
                lengths[short] *= np.maximum(cut[short] - 0.6, 1.0) / (rings - 1)
                P[:, short] = self.trace(roots[short], lengths[short], layers[short], turn[short], None, rings)
        widths = st.width * self.rng.uniform(0.8, 1.15, n)
        return P, widths, np.full(n, st.thick) * self.rng.uniform(0.85, 1.15, n)

    def tail_locks(self):
        """A ponytail: thick locks from the tie, out from the head and down the back."""
        tie = self._onto(np.asarray([self.st.tie]), np.asarray([self.st.cap + 0.006]))
        out = self._normal(tie)[0]
        n = 15
        a = self.rng.uniform(0, 2 * math.pi, n)
        r = 0.006 * np.sqrt(self.rng.random(n))
        u = _unit(np.cross(out, (1.0, 0.0, 0.0)))
        v = np.cross(out, u)
        start = tie + (np.cos(a) * r)[:, None] * u + (np.sin(a) * r)[:, None] * v + out * 0.004
        lengths = self.rng.uniform(0.2, 0.26, n)
        turn = (out * 1.6 + (np.cos(a)[:, None] * u + np.sin(a)[:, None] * v) * 0.3).astype(F)
        P = self.trace(start, lengths, np.full(n, 2.0, dtype=F), turn, None, 10, hanging=True)
        return P, self.rng.uniform(0.022, 0.028, n), self.rng.uniform(0.012, 0.016, n)

    def bun_locks(self):
        """A bun: a ball of hair at the back of the crown, its locks coiled round it in one direction."""
        tie = self._onto(np.asarray([self.st.tie]), np.asarray([self.st.cap]))
        out = self._normal(tie)[0]
        centre = tie[0] + out * 0.024
        u = _unit(np.cross(out, (1.0, 0.0, 0.0)))
        v = np.cross(out, u)
        n, K, r = 7, 14, 0.024
        P = np.zeros((K, n + 1, 3), dtype=F)
        for i in range(n):
            a0 = 2 * math.pi * i / n + self.rng.uniform(-0.2, 0.2)
            for k in range(K):
                t = k / (K - 1)
                a, lat = a0 + 1.4 * math.pi * t, -0.9 + 1.5 * t       # round the axis, from the base up over the top
                P[k, i] = centre + (u * math.cos(a) + v * math.sin(a)) * r * math.cos(lat) + out * r * math.sin(lat)
        P[:, n] = centre + out[None] * np.linspace(-0.01, 0.004, K)[:, None]     # the core the locks wrap
        widths = np.r_[self.rng.uniform(0.024, 0.028, n), 0.046]
        thick = np.r_[self.rng.uniform(0.012, 0.014, n), 0.044]
        return P, widths, thick

    def cap(self, head_obj):
        """The hair's base: the scalp lifted a few millimetres, dark (the depth between locks), sinking into the skin
        at the hairline."""
        bm = bmesh.new()
        bm.from_mesh(head_obj.data)
        drop = []
        for f in bm.faces:
            q = self.head.local(f.calc_center_median())
            if coverage(q, self.part, self.sideburns, 0.009) < 0.03 or (self.under_hat and q.z > 0.05):
                drop.append(f)
        bmesh.ops.delete(bm, geom=drop, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.verts.ensure_lookup_table()
        q = np.array([self.head.local(v.co) for v in bm.verts], dtype=F)
        cov = np.array([coverage(Vector(x), self.part, self.sideburns, 0.009) for x in q], dtype=F)
        k = _ss(0.05, 0.95, cov)          # a gentle ramp, so the edge where it leaves the skin is a smooth line
        q = self._onto(q, self.height(q, 0.0, None) * k - 0.001 * (1 - k))
        for v, x in zip(bm.verts, q):
            v.co = self.head.at(x)
        uv = bm.loops.layers.uv.new("UVMap")
        col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
        for f in bm.faces:
            for loop in f.loops:
                x = self.head.local(loop.vert.co)
                d = np.asarray(x) - self.crown
                loop[uv].uv = (float(np.linalg.norm(d)) * 10.0, math.atan2(d[0], -d[1]) * 0.3)
                loop[col] = (*(c * 0.8 for c in self.color), 1.0)
        return mesh_from(bm, "hair_cap")

    def build(self, head_obj):
        """The style's hair: one sculpted mesh of clumps (hairsdf), or only the cap for a buzz cut."""
        if self.st.count == 0:
            return [self.cap(head_obj)]
        parts = [self.scalp_locks()]
        if self.name == "ponytail":
            parts.append(self.tail_locks())
        elif self.name == "bun":
            parts.append(self.bun_locks())
        from .hairsdf import Sculpt
        return [Sculpt(self, parts).build()]
