"""A robe's bodice: the robe's own cloth over the shoulders, chest and arms (cut from the body like a shirt by
garments.Wardrobe.shell), continued under the sash by its skirt (skirts.py), so the two read as one garment.

Unlike a shirt it is cut loose: draped like one (tailoring.Cloth), it rests on the shoulders and falls from them and
the chest nearly plumb to the sash, which gathers it into soft, uneven folds (it blouses a little over it). Round the
torso it hangs from a map of the body (angle x height): each row spanned by its convex hull, each column falling from
the widest point above it, so it bridges the chest, the shoulder blades and the small of the back and no muscle,
spine or crease shows through. At the sash it is cinched to just over the body's girth there (the robe is girt at the
natural waist), so the sash lies snug. The draped cloth is then relaxed over the body like heavy fabric and hangs in
long folds down the sleeves, which widen toward the wrist.
"""
import numpy as np

from .drape import blur, rings
from .skirts import hull
from .tailoring import Cloth, ridge, ss, wobble


CINCH = 0.006           # the bodice's height over the girth of the body under the sash (frame scale)
RING = 72               # angle cells round the torso


class RobeCloth(Cloth):
    def __init__(self, wardrobe, thickness):
        super().__init__(wardrobe, "shirt", thickness)
        self.belted = True              # gathered at the waist by the sash
        self.draped = False

    def apply(self, bm):
        shade = super().apply(bm)
        s = self.s
        X, N, g, edge = self.shell
        P = np.array([v.co[:] for v in bm.verts])
        # Easing in from the sleeves (which keep their shape) over a few rings, so no crease marks the armhole.
        move = 0.6 * ((rings(g, edge, 3) >= 2) * ss(0.0, 6.0, rings(g, ~self.torso, 7)))[:, None]
        floor = self.t + 0.002 * s
        ceil = np.where(self.band, self.cinch, np.inf)          # (held in under the sash)
        for _ in range(40):             # heavy cloth: relaxed over the body's smaller shapes, never into it
            P = P + move * (g.mean(P) - P)
            d = np.einsum("ij,ij->i", P - X, N)
            P = P + N * (np.clip(d, floor, ceil) - d)[:, None]
        self.draped = True
        for v, p in zip(bm.verts, P):
            v.co = p
        bm.normal_update()
        # The folds are laid out over the draped cloth and stand out from it: where it bridges a hollow (the small
        # of the back) the body's points crowd together under it and its normals tilt steeply, which would crumple them.
        self._regions(P)
        fold, crease = self._folds(P)
        fade = ss(0.0, 4.0, rings(g, edge, 4))
        P = P + np.array([v.normal[:] for v in bm.verts]) * (fold * fade)[:, None]
        self.w.standoff["shirt"] = (X, np.einsum("ij,ij->i", P - X, N))
        for v, p in zip(bm.verts, P):
            v.co = p
        bm.normal_update()
        return np.minimum(shade, np.clip(1.0 - crease * fade, 0.6, 1.0))

    def keep(self, bm):
        """Weights for reducing the finished mesh (see finishing.reduce): low over the gathers above the sash, so
        they keep their shape instead of breaking up into facets."""
        s, top = self.s, self.w.waist + 0.016 * self.s
        z = np.array([v.co.z for v in bm.verts])
        torso = np.array([not self.w._on_arm(v.co) for v in bm.verts])
        return 1.0 - 0.9 * torso * ss(top - 0.02 * s, top, z) * (1 - ss(top + 0.1 * s, top + 0.16 * s, z))

    def _shirt(self, X):
        """The shirt's ease (with the sash's gathering, as under a belt) let out: loose from the chest down, the
        sleeves widening toward the wrist and sagging below the arm, free to span far across the torso's hollows."""
        lo, cap, hi = super()._shirt(X)
        j, s, w, z = self.f.j, self.s, self.w, self.z
        extra = 0.0065 * s * ss(w.collar_z - 0.04 * s, j["chest"].z, z) * ss(w.waist - 0.03 * s, w.waist + 0.03 * s, z)
        for side, arm in self.arms.items():
            sh = np.asarray(j[f"shoulder.{side}"])
            R = X - sh - np.clip(arm.t, 0, 1)[:, None] * (np.asarray(j[f"wrist.{side}"]) - sh)
            under = np.clip(-R[:, 2] / np.maximum(np.linalg.norm(R, axis=1), 1e-6), 0.0, 1.0)
            bell = 0.009 * s * ss(0.45, 0.97, arm.t) + 0.005 * s * ss(0.35, 0.97, arm.t) * under
            extra = np.where(arm.on, bell, extra)
        cap = np.where(self.torso, np.maximum(cap, 0.05 * s * self._armpits(X)), np.maximum(cap, 0.012 * s * self._armpits(X)))
        self._cinch(X, lo + extra)
        return lo + extra, cap, np.where(self.band, self.cinch, hi)

    def _polar(self, P):
        """Angle round the body's axis (0 in front, as the skirt and sash measure it), radius from it, and the bin of
        the angle on a ring of ``RING`` cells."""
        cy = self.f.j["spine"].y
        az = np.arctan2(P[:, 0], P[:, 1] - cy)
        return az, np.hypot(P[:, 0], P[:, 1] - cy), np.round((az + np.pi) / (2 * np.pi) * RING).astype(int) % RING

    def _cinch(self, X, lo):
        """Where the sash holds the bodice in (``band``, the torso under it) and how far off the skin (``cinch``): out
        to the hull of the body under the whole sash (the girth the band takes, spanning the small of the back), so
        the cloth meets the sash instead of following the body in under it, and never closer than its ease ``lo``."""
        s, w, N = self.s, self.w, self.shell[1]
        z = X[:, 2]
        self.band = self.torso & (z < w.waist + 0.016 * s)
        az, r, k = self._polar(X)
        sel = self.torso & (z > w.waist - 0.036 * s) & (z < w.waist + 0.019 * s)      # (and the hem under it)
        prof = np.full(RING, -1.0)
        np.maximum.at(prof, k[sel], r[sel])
        have = np.flatnonzero(prof > 0)
        prof = np.interp(np.arange(RING), np.concatenate([have - RING, have, have + RING]), np.tile(prof[have], 3))
        self.ring = hull(prof) + CINCH * s
        out = np.stack([np.sin(az), np.cos(az), np.zeros(len(az))], 1)
        facing = np.maximum(np.einsum("ij,ij->i", N, out), 0.35)
        self.cinch = np.maximum((self.ring[k] - r) / facing, lo)

    def _hang(self, X, N, h, most):
        """The robe's fall, all round the torso: on a map of the cloth round the body's axis (angle, height) each row
        is spanned by its convex hull (across the cleavage, the spine's groove, the hollows of the waist) and each
        column carried down from its widest less a slight taper, so the cloth hangs from the chest and the shoulder
        blades instead of following the body in; over the last span above the sash it is drawn in to the girth the
        sash holds it at (``_cinch``), where it gathers. The lift (blurred) is smaller on the flanks the lowered arms
        rest against."""
        s, z, j = self.s, self.z, self.f.j
        floor, top = self.w.waist + 0.016 * s, j["shoulder.R"].z - 0.08 * s      # (the map ends under the armpits)
        cell = 0.01 * s
        P = X + N * h[:, None]
        sel = np.flatnonzero(self.torso & (z > self.w.waist - 0.04 * s) & (z < top))
        if not len(sel):
            return h
        az, r, iu = self._polar(P[sel])
        v = (z[sel] - floor) / cell + 7                 # (margins: the blur wraps)
        iv = np.round(v).astype(int)
        D = np.full((RING, iv.max() + 8), -np.inf)
        np.maximum.at(D, (iu, iv), r)
        th = 2 * np.pi * np.arange(RING) / RING - np.pi
        c = np.cos(th[:, None] - th[None, :])           # (row direction, hull support direction)
        E = D.copy()
        for k in range(D.shape[1]):
            fin = np.isfinite(D[:, k])
            if np.diff(np.flatnonzero(np.concatenate([fin, fin]))).max(initial=RING) > RING // 6:
                continue                                # (only rows all round the body)
            H = (np.stack([np.sin(th[fin]), np.cos(th[fin])], 1) * D[fin, k][:, None] @ np.stack([np.sin(th), np.cos(th)])).max(0)
            E[:, k] = np.where(fin, np.maximum(D[:, k], np.where(c > 0.2, H[None, :] / np.maximum(c, 1e-3), np.inf).min(1)), -np.inf)
        for k in range(E.shape[1] - 2, -1, -1):
            E[:, k] = np.maximum(E[:, k], E[:, k + 1] - 0.12 * cell)
        for k in range(E.shape[1]):                     # (cells no point fell in: from their row's neighbours)
            fin = np.flatnonzero(np.isfinite(E[:, k]))
            if len(fin):
                E[:, k] = np.interp(np.arange(RING), np.concatenate([fin - RING, fin, fin + RING]), np.tile(E[fin, k], 3))
        full = np.isfinite(E).all(0)
        E = np.where(full[None, :], E, 0.0)
        E = blur(E, np.broadcast_to(full[None, :], E.shape).astype(float), 2)
        # The hull's radius read at each point: the cloth is lifted out to it (so a groove fills up to the crests
        # beside it), drawn in to the sash's girth near the sash, less on the flanks.
        u = (az + np.pi) / (2 * np.pi) * RING
        u0, v0 = np.floor(u).astype(int), np.clip(np.floor(v).astype(int), 0, D.shape[1] - 2)
        fu, fv = u - np.floor(u), np.clip(v - v0, 0, 1)
        lerp = lambda G: (G[u0 % RING, v0] * (1 - fu) * (1 - fv) + G[(u0 + 1) % RING, v0] * fu * (1 - fv)
                          + G[u0 % RING, v0 + 1] * (1 - fu) * fv + G[(u0 + 1) % RING, v0 + 1] * fu * fv)
        held = 1.0 - ss(floor, floor + 0.14 * s, z[sel])
        side = (0.35 + 0.65 * np.cos(az) ** 2) * ss(top, top - 0.06 * s, z[sel])
        ring = np.interp(u, np.arange(RING + 1), np.append(self.ring, self.ring[0]))
        up = np.maximum((lerp(E) - r) * side * (1 - held) + (ring - r) * held, 0.0)
        facing = np.einsum("ij,ij->i", N[sel], np.stack([np.sin(az), np.cos(az), np.zeros(len(sel))], 1))
        res = h.copy()
        res[sel] = np.minimum(h[sel] + up / np.maximum(facing, 0.35), np.maximum(most[sel], h[sel]))
        return res

    def _folds(self, X):
        """The shirt's folds on the sleeves, long soft ones down them from the shoulder, and the gathers the sash
        draws the cloth into, fading out above it (none until the cloth has been draped, so relaxing it doesn't
        smooth them away)."""
        if not self.draped:
            return np.zeros(len(X)), np.zeros(len(X))
        self.belted = False             # not the shirt's fans above a belt: too fine for cloth bridging the back
        fold, crease = super()._folds(X)
        self.belted = True
        s, z, az = self.s, self.z, self.az
        top = self.w.waist + 0.016 * s
        # Many narrow, uneven gathers standing out of the sash, some running higher than others (broad, even ones
        # would read as the back's muscles).
        reach = (0.05 + 0.03 * (1 + np.sin(az * 5 + 0.8)) + 0.015 * (1 + np.sin(az * 11 + 2.0))) * s
        m = self.torso * ss(top - 0.012 * s, top + 0.004 * s, z) * (1 - ss(top, top + reach, z)) ** 1.5
        c = ridge(az * 22 / (2 * np.pi) + 0.3 * wobble(az * 3, 1.3) + 0.25)
        fold = fold + m * 0.0036 * s * (c - 0.45)
        crease = crease + m * 0.14 * (1.0 - c) ** 2
        for k, arm in enumerate(self.arms.values()):
            r = ridge(arm.phi * 5 / (2 * np.pi) + 0.35 * wobble(arm.t * 6.0, 0.9 + 2.3 * k))
            m = arm.on * ss(0.2, 0.75, arm.t) * (1 - ss(0.9, 0.97, arm.t))
            fold = fold + m * 0.003 * s * (r - 0.3)
            crease = crease + m * 0.18 * (1.0 - r) ** 2
        return fold, crease
