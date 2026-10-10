"""Hair locks: where each lock of hair roots in the scalp and the path it takes in the style's flow, traced over
surfaces offset from the bare skull (headshape) so they hug it whatever the face looks like.

- Short and messy hair is combed out from the crown, the fringe swept across from a side parting, the sideburns and
  the nape lying down; locks that would run past the hairline or onto the ears stop there and lie close to the skin.
  A neat cut is fuller on top and close down the sides and back, ending in an even edge that thins into the skin
  at the sides and nape; messy hair ends unevenly, flat locks lying over each other with some flicking up.
- Long hair falls from a parting down the sides and back of the head (swept back from the forehead, never over the
  face), over the ears, hangs free below it framing the face, and drapes over the shoulders and down the back
  (longhair.py, hairdrape.py). A woman's short hair is a chin-length bob in the same way, its ends cut blunt and
  turned under, with a fringe swept across the forehead.
- Tied hair is pulled back flat to a tie and ends in a ponytail or a bun (hairtails.py).
- A buzz cut is a close crop: a tinted layer on the scalp that thins into the skin at the hairline (crop.py).

hairsdf sculpts the traced locks into the hair's mesh.
"""
import math
from dataclasses import dataclass

import numpy as np
from mathutils import Vector

from . import sdf
from .hairdrape import Drape
from .hair import EAR_ZONE, coverage, coverages, ear_clear, ear_cover, hairline, hairlines
from .hairsdf import Strands
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
    "short": Style(0.003, 0.006, 0.075, 0.2, 0.023, 0.0085, 100, 0.8, 0.04),
    "messy": Style(0.003, 0.009, 0.08, 0.3, 0.022, 0.0095, 105, 0.4, 0.45),
    "long": Style(0.003, 0.004, 0.11, 0.1, 0.036, 0.013, 150, 0.55, 0.05, fall=0.36),
    "bob": Style(0.003, 0.005, 0.0, 0.0, 0.034, 0.012, 150, 0.8, 0.03, fall=0.13),
    "ponytail": Style(0.0025, 0.002, 0.0, 0.0, 0.026, 0.0065, 90, 0.0, 0.02, tie=(0.0, -0.096, 0.048)),
    "bun": Style(0.0025, 0.002, 0.0, 0.0, 0.026, 0.0065, 90, 0.0, 0.02, tie=(0.0, -0.092, 0.058)),
    "buzz": Style(0.0007, 0.0, 0.0, 0.0, 0.0, 0.0, 0, 0.0, 0.0),
}

DOWN = np.asarray((0.0, 0.0, -1.0), dtype=F)
BEARD_BURN = -0.036          # how low (head-local z) a cut's sideburns run with a beard, under its top edge


def _bearded(p):
    from .beard import STYLES as BEARDS
    st = BEARDS.get(p.get("beardStyle", "full")) if p.get("beard") else None
    return st is not None and st.cheek > 0


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-9)


class Groom:
    RINGS = 8

    def __init__(self, head, style, color, under_hat=None, body_tree=None, seed=1, worn_tree=None, tie_color=None):
        self.head = head
        if style == "short" and head.frame.female:      # a woman's short hair is a chin-length bob
            style = "bob"
        self.st = STYLES[style]
        # Hair is laid over the bare skull (short hair grows round the ears, so it ignores them; long hair falls over
        # them, lying on a smooth dome over each); ``full`` adds the ears, which no hair may cut into, and ``hang`` is
        # what hanging hair is kept off.
        self.full = lambda p: head.shape.field(p, face=False)
        self.field = (ear_cover(head.shape, 0.4 if style == "bob" else 0.6) if self.st.fall else
                      (lambda p: head.shape.field(p, ears=False, face=False)))
        self.hang = self.field if self.st.fall else self.full
        self.name = style
        self.color = hex3(color)
        self.tie_color = hex3(tie_color) if tie_color else tuple(c * 0.45 for c in self.color)
        self.under_hat = under_hat                          # the hat it is cut back under (underhat), or None
        self.drape = Drape(self, body_tree, worn_tree)     # long hair and tails rest on the body and its clothes
        self.rng = np.random.default_rng(seed)
        self.part = self.st.sweep
        self.part_x = 0.018 * self.part                     # where long hair parts (head-local x)
        self.sideburns = not (head.frame.female or self.st.tie)
        if self.sideburns and not self.st.fall and _bearded(head.p):     # a cut's sideburns run down into the beard
            self.sideburns = BEARD_BURN
        # Under a hat tied hair is gathered low at the nape, below the band.
        nape = min(-0.03, float(under_hat.edge_at((0.0, -0.1, 0.0))[0]) - 0.035) if under_hat else 0.0
        self.tie = (0.0, -0.09, nape) if under_hat and self.st.tie else self.st.tie
        side = 1.0 if self.part >= 0 else -1.0
        self.crown = np.asarray((0.012 * side, -0.045, 0.088), dtype=F)
        self.phase = self.rng.uniform(0, 6.3, 3)

    # ------------------------------------------------------------------ surfaces
    def top(self, p):
        """How high hair grows at each point's azimuth: all over, or only a little way up under a hat."""
        p = np.asarray(p, dtype=float).reshape(-1, 3)
        return self.under_hat.top(p) if self.under_hat else np.full(len(p), 1.0)

    def _normal(self, p, skull=False):
        """Out from the surface hair is laid over at p (``skull``: from the bare skull, ignoring the ears)."""
        f = (lambda q: self.head.shape.field(q, ears=False, face=False)) if skull else self.field
        return _unit(sdf.gradient(f, np.ascontiguousarray(p, dtype=F), F(0.0006)))

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
        settles back toward it, a little higher for the upper layers, so clumps overlap like combed hair. Past the
        hairline hair lies down close to the skin."""
        top = _ss(-0.01, 0.08, p[:, 2]) * _ss(-0.05, 0.04, p[:, 1])
        cap = self.st.cap
        if not (self.st.fall or self.tie):          # a cut lies closer down the sides and back than over the top
            cap = cap * (0.55 + 0.45 * _ss(0.015, 0.06, p[:, 2]))
        base = cap + self.st.volume * top
        if t is None:
            return base.astype(F)
        lie = 0.3 + 0.7 * coverages(p, self.part, self.sideburns, 0.012)
        t = np.asarray(t, dtype=F)
        arch = np.maximum(np.sin(np.pi * np.clip(t, 0, 1) ** 0.8), 0.0) ** 0.6   # (sin of pi in float32 is just below 0)
        rise = 0.35 if self.tie is not None else 1.0 if self.st.mess > 0.2 else 0.6   # flat pulled back, close cut neat
        return (base * lie + self.st.thick * lie * (arch * (0.1 + layer * 0.12) * rise - 0.6 * (1 - t) ** 3)).astype(F)

    def _grows(self, q, slack=0.0):
        return coverage(Vector(q), self.part, self.sideburns, 0.002) > 0.5 if slack == 0 else (
            q[2] - hairline(Vector(q), self.part, self.sideburns) > -slack and ear_clear(Vector(q)) > 0.3)

    # ------------------------------------------------------------------ roots and flow
    def roots(self):
        """Lock roots spread evenly over the part of the scalp that grows hair (a jittered spiral of directions); for
        hair pulled back to a tie, rows just inside the hairline all round, so the locks sweep back from it unbroken."""
        if self.tie is not None:
            return self._rows()
        n = int(self.st.count / 0.4)
        i = np.arange(n) + 0.5
        z = 1 - 2 * i / n
        a = i * math.pi * (3 - math.sqrt(5))
        dirs = np.stack([np.sqrt(1 - z * z) * np.cos(a), np.sqrt(1 - z * z) * np.sin(a), z], 1)
        dirs = _unit(dirs + self.rng.normal(0, 0.9 / math.sqrt(n), dirs.shape))
        p = self._onto(np.asarray((0, -0.01, 0.0)) + dirs * 0.13, self.st.cap * 0.5, steps=5)
        # Under a hat from well up inside it, so the locks come out from under its edge combed, not as stubs.
        keep = np.array([self._grows(q) for q in p]) & (p[:, 2] < self.top(p) + (0.025 if self.under_hat else 0.0))
        return p[keep]

    def _rows(self):
        out = []
        for dz, n in ((0.005, 40), (0.024, 30), (0.048, 22)):
            phi = (np.arange(n) + self.rng.uniform(-0.35, 0.35, n)) * (2 * math.pi / n) - math.pi
            ring = np.stack([np.sin(phi) * 0.13, np.cos(phi) * 0.13, np.zeros(n)], 1)
            z = hairlines(ring, self.part, self.sideburns) + dz
            p = self._onto(np.stack([ring[:, 0], ring[:, 1] - 0.012, z], 1).astype(F), np.full(n, self.st.cap * 0.5), 8)
            out += [q for q, t in zip(p, self.top(p)) if self._grows(q) and q[2] < t]
        return np.asarray(out, dtype=F)

    def flow(self, p):
        """The direction hair lies at p (not yet in the surface's plane)."""
        st = self.st
        if self.tie is not None:                   # gathered back to the tie
            to = np.asarray(self.tie, dtype=F) - p
            if self.under_hat:                     # low at the nape: round the sides from the front, not down the face
                to[:, 0] += np.where(p[:, 0] < 0, F(-0.12), F(0.12)) * _ss(-0.03, 0.05, p[:, 1])
            return _unit(to)
        # Combed out from the crown; over the forehead swept across, away from the parting; the sideburns, the hair
        # round the ears and the nape lie down.
        v = _unit(p - self.crown)
        front = _ss(-0.01, 0.05, p[:, 1]) * _ss(0.0, 0.07, p[:, 2])
        sweep = np.asarray((-math.copysign(1.0, self.part), 0.4, -0.15), dtype=F) * abs(self.part)
        low = (_ss(0.025, -0.015, p[:, 2]) * _ss(-0.06, -0.02, p[:, 1]))[:, None]
        noise = np.stack([np.sin(p[:, 0] * 61 + p[:, 2] * 37 + self.phase[0]),
                          np.sin(p[:, 1] * 53 + p[:, 0] * 29 + self.phase[1]),
                          np.sin(p[:, 2] * 47 + p[:, 1] * 31 + self.phase[2])], 1)
        f = v + front[:, None] * sweep * 1.3
        return f * (1 - low) + (DOWN + np.asarray((0, -0.2, 0), dtype=F)) * low * 1.5 + noise * st.mess * 0.6

    # ------------------------------------------------------------------ tracing
    def trace(self, start, lengths, layers, turn, k_rings=None, lift=0.0):
        """Polylines (rings x locks x 3) from each start point, following the flow over the layer's surface, their
        ends rising ``lift`` (per lock) off it."""
        K = k_rings or self.RINGS
        n = len(start)
        P = np.zeros((K, n, 3), dtype=F)
        P[0] = self._onto(start, self.height(start, layers, 0.0))
        step = (lengths / (K - 1)).astype(F)
        lie = np.maximum(lengths, 0.02)            # the whole lock arches a little off the head
        lift = np.broadcast_to(np.asarray(lift, dtype=F), (n,))
        d = None
        for k in range(1, K):
            prev = P[k - 1]
            nrm = self._normal(prev)
            f = self.flow(prev) + turn
            f = _unit(f - nrm * np.einsum("ij,ij->i", f, nrm)[:, None])
            d = f if d is None else _unit(d * 0.45 + f * 0.55)
            q = prev + d * step[:, None]
            P[k] = self._onto(q, self.height(q, layers, np.minimum(k * step / lie, 1.0)) + lift * (k / (K - 1)) ** 2)
        return P

    # ------------------------------------------------------------------ the parts
    def scalp_locks(self):
        """The locks rooted over the scalp: short hair trimmed where it would run past the hairline or onto the ears,
        tied hair ending at the tie."""
        st = self.st
        roots = self.roots()
        n = len(roots)
        top = _ss(0.0, 0.08, roots[:, 2]) * _ss(-0.06, 0.03, roots[:, 1])
        lengths = st.length * (0.7 + 0.5 * top) * (1 + self.rng.uniform(-st.spread, st.spread, n))
        crown = np.linalg.norm(roots - self.crown, axis=1)
        layers = (2.0 * (1 - _ss(0.02, 0.12, crown)) + self.rng.uniform(0, 0.6, n)).astype(F)   # upstream locks on top
        turn = self.rng.normal(0, 0.05 + 0.5 * min(st.mess, 0.28), (n, 3)).astype(F)   # (messy: tousled, not crossed)
        widths = st.width * self.rng.uniform(0.85, 1.15, n)
        thick = st.thick * self.rng.uniform(0.85, 1.15, n) * (0.7 if self.under_hat else 1.0)
        rings = self.RINGS
        if self.tie is not None:                   # over the skull to the tie: as long as the way there
            rings = 10
            far = np.linalg.norm(np.asarray(self.tie) - roots, axis=1) * 1.6
            P = self.trace(roots, far, layers, turn, 16)
            arc = np.concatenate([np.zeros((1, n)), np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=2), axis=0)])
            near = np.argmin(np.linalg.norm(P - np.asarray(self.tie, dtype=F), axis=2), axis=0)
            P = self.trace(roots, np.maximum(arc[near, np.arange(n)], 0.01), layers, turn, rings)
            # Thin along the hairline, so the hair's edge there is the smooth layer under the locks.
            T = thick[None] * (0.3 + 0.7 * _ss(0.003, 0.025, P[..., 2] - hairlines(P.reshape(-1, 3), self.part,
                                                                                 self.sideburns).reshape(P.shape[:2])))
            return Strands(P, widths, T, taper=0.6, root=0.35, merge=0.6, cut=True)
        # Every lock runs on to where the hair ends (a fringe may overhang the hairline), so the ends of the locks
        # make the edge of the hair and the scalp shows long combed locks rather than rows of short ones.
        slack = np.where(roots[:, 1] > 0.03, 0.012, 0.005)
        K = 16
        P = self.trace(roots, np.full(n, st.length * 2.4), layers, turn, K)
        arc = np.concatenate([np.zeros((1, n)), np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=2), axis=0)])
        edge, end = arc[-1].copy(), P[-1].copy()
        for i in range(n):
            for k in range(1, K):
                if not self._grows(P[k, i], slack[i]):
                    edge[i] = arc[k - 1, i] + 0.5 * (arc[k, i] - arc[k - 1, i])
                    end[i] = P[k - 1, i]
                    break
        mess = st.mess > 0.2
        # A neat cut ends in an even edge; messy hair ends unevenly over the top and the fringe, some of its locks
        # flicking up off the rest, but evenly round the ears and over the nape (no ragged edge there).
        rough = self.rng.uniform(0.82, 1.0, n) if mess else self.rng.uniform(0.95, 1.0, n)
        even = self.rng.uniform(0.95, 1.0, n) if mess else self.rng.uniform(0.9, 1.0, n)
        lengths = np.minimum(lengths * 2.0, edge * np.where((end[:, 1] > 0.03) | (mess & (end[:, 2] > 0.0)), rough, even))
        tuft = self.rng.random(n) < st.mess * 0.6
        lift = np.where(tuft, self.rng.uniform(0.002, 0.0055, n) * st.mess / 0.45, 0.0) * _ss(0.0, 0.04, roots[:, 2])
        keep = lengths > 0.02                       # nubs at the hairline: the locks from above cover it
        rings = 10
        P = self.trace(roots[keep], lengths[keep], layers[keep], turn[keep], rings, lift[keep])
        # Narrower and closer to the skin by the ears, down the sideburns and at the nape.
        (cy, cz), (ry, rz) = EAR_ZONE
        r = np.hypot((P[..., 1] - cy) / ry, (P[..., 2] - cz) / rz)
        W = widths[keep][None] * (0.5 + 0.5 * _ss(1.0, 1.7, r))
        # Fuller over the top, shorter and closer down the sides and back (a neat cut most of all).
        T = thick[keep][None] * ((0.6 + 0.4 * _ss(-0.03, 0.02, P[..., 2])) if mess else
                                 (0.38 + 0.62 * _ss(-0.04, 0.06, P[..., 2])))
        # Tapered to the skin over the nape (the sideburns lie flat and even down to their edge).
        T *= self.taper(P.reshape(-1, 3), 0.6).reshape(P.shape[:2])
        return Strands(P, W, T, taper=0.55 if mess else 0.65, root=0.5, round=0.0 if mess else 0.2,
                       blunt=0.0 if mess else 0.15, merge=0.3 if mess else 0.85, cut=True, tip=0.35 if mess else 0.5)

    # ------------------------------------------------------------------ a cut's edge (hairsdf)
    def nape(self, x):
        """1 behind the ears and down the nape, 0 at the sideburns and over the front."""
        return _ss(-0.012, -0.045, x[:, 1])

    def reach(self, x):
        """How far past the hairline the scalp's locks may reach: a little further for a messy cut's ends, but evenly
        over the nape; under long hair a little short of the nape, where its hanging locks cover it."""
        nape = self.nape(x)
        return (0.004 * (1 - nape) if self.st.mess > 0.2 else 0.0) + 0.002 - (0.007 * nape if self.st.fall else 0.0)

    def taper(self, x, depth):
        """A cut's thickness factor: thinning by ``depth`` over its last few centimetres above the nape."""
        above = x[:, 2] - hairlines(x, self.part, self.sideburns) + self.reach(x)
        return (1.0 - depth * _ss(0.035, 0.0, above) * self.nape(x)).astype(F)

    def envelope(self, x):
        """How far off the skull a cut may stand: close down the sides, round the ears and down the sideburns,
        thinning to the skin toward the hairline over the nape (a taper, not a ledge); the fringe free."""
        above = x[:, 2] - hairlines(x, self.part, self.sideburns) + self.reach(x)
        env = 0.45 * np.maximum(above, 0.0) - 0.0006
        side = 0.0032 + 0.35 * np.maximum(x[:, 2] - 0.008, 0.0) + 0.05 * _ss(-0.035, -0.05, x[:, 1])
        front = 0.0 if self.under_hat else _ss(0.04, 0.065, x[:, 1])
        return (np.minimum(env, side) + 0.05 * front).astype(F)

    def build(self, head_obj):
        """The style's hair: one sculpted mesh of clumps (hairsdf) and, for tied hair, the band that ties it; or only
        the crop for a buzz cut (crop.py)."""
        from .crop import crop
        if self.st.count == 0:
            return [crop(self, head_obj)]
        from . import hairtails
        from .hairsdf import Sculpt
        from .longhair import long_locks
        parts, solids, extra = long_locks(self) if self.st.fall else [self.scalp_locks()], [], []
        if self.name == "ponytail":
            tail, gather, band = hairtails.ponytail(self)
            parts.append(tail)
            solids.append(gather)
            extra.append(band)
        elif self.name == "bun":
            bun, core = hairtails.bun(self)
            parts.append(bun)
            solids += core
        if self.name == "bob":         # shallower grooves: a bob is a smooth, glossy mass
            sculpt = Sculpt(self, parts, solids, 0.0035, (0.0008, 0.0009, 0.006), 0.0035)
        elif self.st.fall:
            sculpt = Sculpt(self, parts, solids, 0.0035, (0.0024, 0.0022, 0.006), 0.0035)
        elif self.tie:
            sculpt = Sculpt(self, parts, solids, 0.0026, (0.0009, 0.0016, 0.0045), 0.0028)
        else:
            sculpt = Sculpt(self, parts, solids, 0.0026, (0.0008, 0.0008, 0.0045), 0.0005)
        return [sculpt.build()] + extra
