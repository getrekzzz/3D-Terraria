"""Hair under a hat that trims it (head-local units): the hat's own solid, which the hair is carved round so none of
it shows through, and the height of the hat's lower edge all round the head (its band, a cap's rim, a helmet's
cheek guards), above which hair grows only a little way, out of sight under it. Both come from the same fields the
hats are sculpted from (hats, capshapes, headwear), so the hair follows whatever shape the hat has.
"""
import math

import numpy as np

F = np.float32
BINS = 72
MIDDLE_Y = -0.012          # the axis the azimuths are measured round
ROOM = 0.012               # how far above the lower edge hair still grows, out of sight


def _crown(parts):
    """The union of a hat's parts that fit round the head."""
    fit = [parts[k][0] if isinstance(parts[k], tuple) else parts[k] for k in ("crown", "band", "fur") if k in parts]
    return np.min(fit, 0)


class UnderHat:
    def __init__(self, head, p):
        kind, shape = p["hat"], p.get("hatShape")
        fem = head.shape.fem
        bald = p.get("hairStyle") in ("bald", "buzz")
        if kind == "wide" or shape == "shako":
            from . import hats
            hat = hats.Hat(shape or "brim", fem, 0.004 if bald else 0.012)       # as accessories.hat fits it
        else:
            from . import capshapes, headwear
            hat = capshapes.CapShape(shape, fem) if shape in capshapes.SHAPES else None
        self.solid = hat.field if hat else headwear.cap_field(head.shape, p.get("visor", True))
        if kind == "wide" or shape == "shako":       # (a wide hat's crown filled: no lock pokes out through it)
            self.solid = hat.filled
        # Where it fits round the head: its crown and band, not a brim, peak, knot or pompom standing off it.
        self.fit = (lambda q: _crown(hat.parts(q))) if hat else self.solid
        self.key = (kind, shape, bool(p.get("visor", True)), bald)
        self.angles = (np.arange(BINS) + 0.5) * (2 * math.pi / BINS) - math.pi
        self.edge, self.rim = self._edge(head)
        self.low = float(self.edge.min())

    def _edge(self, head):
        """Per azimuth, the lowest point at which the hat lies close round the skull, and how far out from the skull
        its surface stands there (its rim's thickness), a little smoothed."""
        z = np.arange(-0.14, 0.16, 0.003)
        r = np.arange(0.04, 0.17, 0.003)
        A, Z, R = np.meshgrid(self.angles, z, r, indexing="ij")
        p = np.stack([np.sin(A) * R, np.cos(A) * R + MIDDLE_Y, Z], -1).reshape(-1, 3).astype(F)
        skull = head.shape.field(p, ears=False, face=False)
        near = (skull > 0) & (skull < 0.035)
        hat = np.zeros(len(p), dtype=bool)
        hat[near] = self.fit(np.ascontiguousarray(p[near])) < 0
        low = np.where(hat, p[:, 2], np.inf).reshape(BINS, -1).min(1)
        top = low[np.isfinite(low)].max() if np.isfinite(low).any() else 1.0
        low = np.where(np.isfinite(low), low, top)
        at = hat.reshape(BINS, -1) & (p[:, 2].reshape(BINS, -1) <= low[:, None] + 0.0065)
        rim = np.where(at.any(1), np.where(at, skull.reshape(BINS, -1), 0.0).max(1), 0.01)
        ring = np.r_[rim[-2:], rim, rim[:2]]
        rim = np.mean([ring[k:k + BINS] for k in range(5)], 0)
        ring = np.r_[low[-2:], low, low[:2]]
        low = np.min([ring[k:k + BINS] for k in range(5)], 0)          # the lowest of its neighbours, then their mean
        ring = np.r_[low[-2:], low, low[:2]]
        return np.mean([ring[k:k + BINS] for k in range(5)], 0), rim

    def edge_at(self, p):
        """The height of the hat's lower edge at the azimuth of each point."""
        p = np.asarray(p, dtype=float).reshape(-1, 3)
        phi = np.arctan2(p[:, 0], p[:, 1] - MIDDLE_Y)
        return np.interp(phi, self.angles, self.edge, period=2 * math.pi)

    def rim_at(self, p):
        """How far the hat's surface stands out from the skull at its lower edge, at the azimuth of each point."""
        p = np.asarray(p, dtype=float).reshape(-1, 3)
        phi = np.arctan2(p[:, 0], p[:, 1] - MIDDLE_Y)
        return np.interp(phi, self.angles, self.rim, period=2 * math.pi)

    def top(self, p):
        """How high hair grows at the azimuth of each point."""
        return self.edge_at(p) + ROOM

    def carve(self, X, d):
        """The hair's distances ``d`` at points X with the hat (and a hair's breadth round it) cut away."""
        m = X[:, 2] > F(self.low - 0.01)
        if m.any():
            from .sdf import smax
            d = d.copy()
            d[m] = smax(d[m], -(self.solid(np.ascontiguousarray(X[m])) - F(0.0012)), 0.0015)
        return d
