"""A robe's bodice: the robe's own cloth over the shoulders, chest and arms (cut from the body like a shirt by
garments.Wardrobe.shell), continued under the sash by its skirt (skirts.py), so the two read as one garment.

Unlike a shirt it is cut loose: it rests on the shoulders and falls from them, the chest and the shoulder blades
nearly plumb to the sash, which gathers it into soft folds (it blouses a little over it). The draped cloth is then
relaxed over the body like heavy fabric, so the chest's and belly's shapes don't show through, and hangs in long folds
down the sleeves, which widen toward the wrist.
"""
import numpy as np

from .tailoring import Cloth, ridge, rings, ss, wobble


class RobeCloth(Cloth):
    def __init__(self, wardrobe, thickness):
        super().__init__(wardrobe, "shirt", thickness)
        self.belted = True              # gathered at the waist by the sash
        self.draped = False

    def apply(self, bm):
        shade = super().apply(bm)
        s = self.s
        X, h = self.w.standoff["shirt"]
        P = np.array([v.co[:] for v in bm.verts])
        N = (P - X) / np.maximum(h, 1e-6)[:, None]
        edges = np.array([(e.verts[0].index, e.verts[1].index) for e in bm.edges])
        edge = np.array([v.is_boundary for v in bm.verts])
        n, a, b = len(X), edges[:, 0], edges[:, 1]
        deg = np.maximum(np.bincount(edges.ravel(), minlength=n), 1).astype(float)[:, None]
        # Easing in from the sleeves (which keep their shape) over a few rings, so no crease marks the armhole.
        move = 0.6 * ((rings(edges, edge, 3) >= 2) * ss(0.0, 6.0, rings(edges, ~self.torso, 7)))[:, None]
        floor = self.t + 0.002 * s
        for _ in range(40):             # heavy cloth: relaxed over the body's smaller shapes, never into it
            avg = np.stack([np.bincount(a, P[b, k], n) + np.bincount(b, P[a, k], n) for k in range(3)], 1) / deg
            P = P + move * (avg - P)
            hh = np.einsum("ij,ij->i", P - X, N)
            P = P + N * np.maximum(floor - hh, 0.0)[:, None]
        self.draped = True
        for v, p in zip(bm.verts, P):
            v.co = p
        bm.normal_update()
        # The folds are laid out over the draped cloth and stand out from it: where it bridges a hollow (the small
        # of the back) the body's points crowd together under it and its normals tilt steeply, which would crumple them.
        self._regions(P)
        fold, crease = self._folds(P)
        fade = ss(0.0, 4.0, rings(edges, edge, 4))
        Nc = np.array([v.normal[:] for v in bm.verts])
        P = P + Nc * (fold * fade)[:, None]
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
        sleeves widening toward the wrist and sagging below the arm."""
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
        return lo + extra, cap, hi

    def _hang(self, X, N, lo):
        """The shirt's fall from the chest and the shoulder blades, drawn in gradually to the sash instead of
        stepping in just above it (which leaves a shelf of crumpled cloth over the small of the back)."""
        top = self.w.waist + 0.016 * self.s
        return lo + (super()._hang(X, N, lo) - lo) * ss(top, top + 0.14 * self.s, self.z)

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
        m = self.torso * ss(top - 0.012 * s, top + 0.004 * s, z) * (1 - ss(top + 0.01 * s, top + 0.14 * s, z))
        m = m * (0.65 + 0.35 * np.sin(az * 7 + 0.4))
        c = 0.5 + 0.5 * np.cos(az * 13 + 0.5 * np.sin(az * 4 + 1.1) + 0.3 * np.sin(z / s * 16 + az * 3))
        fold = fold + m * 0.0045 * s * (c - 0.4)
        crease = crease + m * 0.12 * (1.0 - c) ** 2
        for k, arm in enumerate(self.arms.values()):
            r = ridge(arm.phi * 5 / (2 * np.pi) + 0.35 * wobble(arm.t * 6.0, 0.9 + 2.3 * k))
            m = arm.on * ss(0.2, 0.75, arm.t) * (1 - ss(0.9, 0.97, arm.t))
            fold = fold + m * 0.003 * s * (r - 0.3)
            crease = crease + m * 0.18 * (1.0 - r) ** 2
        return fold, crease
