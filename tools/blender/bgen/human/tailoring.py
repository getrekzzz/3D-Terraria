"""Tailoring: a garment's raw shell (the body's surface over one region, cut out by garments.Wardrobe.shell) shaped
into cloth with the solvers in drape.py. The shell is refined, takes the body's skin weights spread a little wider
(so it bends in broader curves than the skin), and is then

- eased off the skin: closer over the shoulders than over the belly and forearms, cinched under the belt and inside
  the boot tops, bloused out above them and gathered above the cuffs; a coat rests on the shirt under it;
- carried across what it can't follow the body into: clefts narrower than a rolling ball (between the pecs, down the
  spine, under the seat), hollows a relaxed membrane fills (under the chest, the small of the back), held back at
  the armpits and crotch where the limbs part; on a shirt or coat front, hung straight down from the chest;
- rounded by its stiffness, a low-pass filter that takes the edge off muscles and the collarbones, so no anatomy
  prints through, and smoothed wherever standing off from a tight groove (in front of the shoulder) would turn the
  surface over on itself;
- folded where it bunches: a few broad folds inside the elbows and behind the knees, above the cuffs and the boot
  tops, standing out of the belt, creased across the instep of boots. Creases are darkened in the vertex colour as
  brightness, which the game's recolouring carries over to the chosen colour.

finishing.py then reduces the mesh to its budget and adds seams, collars, cuffs, hems, boot rims, soles and buttons.
"""
import math

import numpy as np

from .drape import Graph, blur, membrane, nearest, refine, rings, roll, settle, soften, untangle


def ss(a, b, x):
    """Smoothstep from a to b (either order), vectorised."""
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def bump(x, c, w):
    return np.exp(-((x - c) / w) ** 2)


def ridge(x):
    """One fold per unit of x: broad rounded crests with tight creases between them, 0 (crease) .. 1 (crest)."""
    c = 0.5 + 0.5 * np.cos(2.0 * np.pi * x)
    return c * (2.0 - c)


def wobble(phi, seed):
    """Low-frequency phase noise round a limb or the torso, so fold rings wander instead of lying level."""
    return 0.45 * np.sin(phi + seed) + 0.25 * np.sin(2 * phi + seed * 2.3) + 0.06 * np.sin(5 * phi + seed * 0.7)


class Limb:
    """Points measured along a limb (t: 0 at ``a`` .. 1 at ``b``) and round it (phi: 0 facing forward, ±pi behind)."""

    def __init__(self, X, a, b):
        a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
        ab = b - a
        self.length = float(np.linalg.norm(ab))
        axis = ab / self.length
        self.t = (X - a) @ ab / ab.dot(ab)
        r = X - a - np.clip(self.t, 0.0, 1.0)[:, None] * ab
        self.r = np.linalg.norm(r, axis=1)
        fwd = np.array((0.0, 1.0, 0.0)) - axis * axis[1]
        fwd /= np.linalg.norm(fwd)
        self.phi = np.arctan2(r @ np.cross(axis, fwd), r @ fwd)
        self.front = np.cos(self.phi)
        self.axis, self.fwd = axis, fwd


class Cloth:
    """The shape of one garment: ``kind`` is shirt, coat, pants or boots; ``thickness`` in units of the frame scale."""

    def __init__(self, wardrobe, kind, thickness):
        self.w, self.f, self.s = wardrobe, wardrobe.f, wardrobe.f.s
        self.kind = kind
        self.t = thickness * self.s
        self.belted = wardrobe.tucked
        self.suit = wardrobe.suit
        self.heavy = 1.4 if kind == "coat" else 1.0     # how long the cloth settles (heavier cloth rounds more)

    def apply(self, bm):
        """Moves the refined shell's vertices onto the cloth surface; returns each vertex's crease shade (0..1)."""
        coarse = len(bm.verts)          # (the shell's own vertices, which refining puts first)
        refine(bm)
        bm.verts.ensure_lookup_table()
        X = np.array([v.co[:] for v in bm.verts])
        N = np.array([v.normal[:] for v in bm.verts])
        g = Graph(len(X), np.array([(e.verts[0].index, e.verts[1].index) for e in bm.edges]))
        pinned = np.array([v.is_boundary for v in bm.verts])
        for _ in range(6):              # cloth stands off along smoothed normals, which don't cross over a cleft
            N = g.mean(N)
        N /= np.linalg.norm(N, axis=1, keepdims=True)
        self.shell = X, N, g, pinned    # (for a further pass over the result: robes.RobeCloth)
        self._regions(X)
        edge = rings(g, pinned, 8)
        if self.kind != "boots":        # (the skin left two rings in from the edges keeps the cloth's weights there)
            soften(bm, g, ss(2.0, 8.0, edge), 60 if self.kind == "pants" else 100)
        lo, cap, hi = getattr(self, "_" + ("shirt" if self.kind == "coat" else self.kind))(X)
        # The skin under a garment is removed, so inside its edges the cloth may give a little where it rounds over
        # the body's sharper forms; a coat never closer to the shirt than a few millimetres.
        floor = lo * (1.0 - 0.45 * ss(4.0, 8.0, edge))
        if self.kind == "shirt":        # never down onto the trousers' waistband under its tail
            floor = np.where(self.torso & (self.z < self.w.waist + 0.02 * self.s), np.maximum(floor, 0.0085 * self.s), floor)
            lo = np.maximum(lo, floor)
        if self.kind == "coat":         # over the shirt and close to it: a coat rests on what it covers
            lo = floor = lo * 0.75
            under = self.w.standoff.get("shirt")
            if under is not None:
                under = nearest(under[0], X, under[1])
                lo, floor = np.maximum(lo, under + 0.4 * self.t), np.maximum(lo * 0.8, under + 0.15 * self.t)
            cap, hi = cap * 1.4, np.full(len(X), 1.0)
        R = (0.045 if self.kind == "coat" else 0.032) * self.s
        span = roll(X, N, lo, R, coarse) - lo
        span = cap * np.tanh(span / np.maximum(cap, 1e-6))        # (eased into the limit, and blurred: no ledges)
        sh = np.minimum(*[np.linalg.norm(X - np.array(self.f.j[f"shoulder.{k}"]), axis=1) for k in "RL"])
        span *= ss(0.07 * self.s, 0.07 * self.s + 2 * R, sh)     # not where the arm folds in against the chest
        for _ in range(6):
            span = np.where(pinned, 0.0, g.mean(span))
        h = membrane(X, N, g, lo + span, np.minimum(lo + cap, hi), pinned)
        if self.kind in ("shirt", "coat") and not self.suit:
            h = self._hang(X, N, h, lo + 2.0 * cap)
        elif self.kind == "pants" and not self.suit:
            h = self._fall(X, N, h, lo + 0.03 * self.s)
        h = settle(X, N, g, h, floor, pinned, self._stiffness(), int(150 * self.heavy), int(4 * self.heavy + 0.5))
        h = np.minimum(h, np.maximum(hi, floor))
        fold, crease = self._folds(X)
        fade = ss(0.0, 4.0, edge)                           # no folds right at a hem: the bands finish the edge there
        h = h + fold * fade
        crease = crease * fade
        if self.suit and self.kind == "shirt":      # a body suit melts into the skin at the neck and the wrists
            h = h * (0.3 + 0.7 * ss(0.0, 6.0, rings(g, pinned & (self.z > self.w.waist), 6)))
        F = np.array([[v.index for v in f.verts] for f in bm.faces])
        NF = np.array([f.normal[:] for f in bm.faces])
        P = untangle(X, N, h, F, NF, g, pinned) if self.kind in ("shirt", "coat") else X + N * h[:, None]
        self.w.standoff[self.kind] = (X, np.einsum("ij,ij->i", P - X, N))
        if self.kind == "shirt" and "coat" in self.w.p:    # under a coat: held in close, clear of it however it bends
            P = untangle(X, N, floor, F, NF, g, pinned)
        if self.kind == "boots":                             # a flat tread under the foot
            P[:, 2] = np.maximum(P[:, 2], -0.001 * self.s)
        for v, p in zip(bm.verts, P):
            v.co = p
        bm.normal_update()
        return np.clip(1.0 - crease, 0.75, 1.0)

    def _hang(self, X, N, h, most):
        """Cloth spans the chest and falls from it instead of following the body in under it, and is gathered back
        in toward the belt (the waist on a robe). On a depth map of the torso's front each row is filled level between
        its crests and each column carried down from its highest point less a slight slope; the lift that takes
        (blurred, so neighbouring columns agree) raises the cloth."""
        s, z = self.s, self.z
        cell = 0.008 * s
        top = self.f.j['chest'].z + 0.13 * s
        floor = self.w.waist + 0.016 * s if self.belted else self.w.waist - 0.04 * s
        P = X + N * h[:, None]
        ny = N[:, 1]
        sel = np.flatnonzero(self.torso & (ny > 0.05) & (z > floor))
        if not len(sel):
            return h
        u = (P[sel, 0] - P[sel, 0].min()) / cell + 2       # (a margin so the blur doesn't wrap)
        v = (P[sel, 2] - floor) / cell + 2
        iu, iv = np.round(u).astype(int), np.round(v).astype(int)
        D = np.full((iu.max() + 4, iv.max() + 4), -np.inf)
        np.maximum.at(D, (iu, iv), P[sel, 1])
        # Each row spans level from crest to crest (across the bust, the pecs), then each column falls from its top.
        E = np.maximum(D, np.minimum(np.maximum.accumulate(D, 0), np.maximum.accumulate(D[::-1], 0)[::-1]))
        for k in range(min(int((top - floor) / cell) + 2, E.shape[1] - 1) - 1, -1, -1):
            E[:, k] = np.maximum(E[:, k], E[:, k + 1] - 0.08 * cell)
        fin = np.isfinite(D)
        lift = np.where(fin, E - np.where(fin, D, 0.0), 0.0)
        lift = blur(lift * lift / (lift + 0.006 * s), fin.astype(float), 5)       # (eased in where it starts)
        u0, v0 = np.clip(np.floor(u).astype(int), 0, D.shape[0] - 2), np.clip(np.floor(v).astype(int), 0, D.shape[1] - 2)
        fu, fv = np.clip(u - u0, 0, 1), np.clip(v - v0, 0, 1)
        up = (lift[u0, v0] * (1 - fu) * (1 - fv) + lift[u0 + 1, v0] * fu * (1 - fv)
              + lift[u0, v0 + 1] * (1 - fu) * fv + lift[u0 + 1, v0 + 1] * fu * fv)
        # Along the normal, so only where that points forward (the front panel, not the flanks it would push out).
        up = up / np.maximum(ny[sel], 0.3) * ss(0.55, 0.85, ny[sel]) * ss(floor, floor + 0.1 * s, z[sel])
        out = h.copy()
        out[sel] = np.minimum(h[sel] + up, np.maximum(most[sel], h[sel]))
        return out

    def _fall(self, X, N, h, most):
        """Trouser legs fall from the seat and the thigh instead of following the leg in behind the knee and over the
        shin: on a map of each leg (along it, round it) each column of the cloth's distance from the leg's axis is
        carried down from its widest, tapering a little; the lift that takes (blurred) raises the cloth."""
        s, out = self.s, h.copy()
        P = X + N * h[:, None]
        for side, leg in self.legs.items():
            j = self.f.j
            a, b = np.array(j[f"hip.{side}"]), np.array(j[f"ankle.{side}"])
            rv = P - a - np.clip(leg.t, 0, 1)[:, None] * (b - a)
            r = np.linalg.norm(rv, axis=1)
            sel = np.flatnonzero(leg.on & (leg.t > 0.05) & (self.z > self.w.boot_top))
            if not len(sel):
                continue
            du, nv = 0.012 * s / leg.length, 40
            u, v = (leg.t[sel] - 0.05) / du + 1, (leg.phi[sel] + np.pi) / (2 * np.pi) * nv
            iu, iv = np.round(u).astype(int), np.round(v).astype(int) % nv
            D = np.full((iu.max() + 3, nv), -np.inf)
            np.maximum.at(D, (iu, iv), r[sel])
            E = D.copy()
            for k in range(1, len(E)):
                E[k] = np.maximum(E[k], E[k - 1] - 0.1 * 0.012 * s)
            fin = np.isfinite(D)
            lift = np.where(fin, E - np.where(fin, D, 0.0), 0.0)
            lift = blur(lift * lift / (lift + 0.004 * s), fin.astype(float), 3)
            u0, v0 = np.clip(np.floor(u).astype(int), 0, len(D) - 2), np.floor(v).astype(int) % nv
            fu, fv = np.clip(u - np.floor(u), 0, 1), v - np.floor(v)
            up = (lift[u0, v0] * (1 - fu) * (1 - fv) + lift[u0 + 1, v0] * fu * (1 - fv)
                  + lift[u0, (v0 + 1) % nv] * (1 - fu) * fv + lift[u0 + 1, (v0 + 1) % nv] * fu * fv)
            facing = np.einsum("ij,ij->i", N[sel], rv[sel]) / np.maximum(r[sel], 1e-9)
            up *= ss(self.w.boot_top + 0.02 * s, self.w.boot_top + 0.12 * s, self.z[sel]) / np.maximum(facing, 0.4)
            out[sel] = np.minimum(h[sel] + up, np.maximum(most[sel], h[sel]))
        return out

    def _stiffness(self):
        """How much the cloth rounds over the body's smaller forms, per point: most on the torso, less down the
        sleeves and legs, where it follows the limb."""
        if self.kind == "boots":
            return np.full(len(self.z), 0.3)
        if self.kind == "pants":
            return np.full(len(self.z), 0.6)
        return 1.0 - 0.4 * self.sleeve

    # ---------------------------------------------------------------- where each point lies
    def _regions(self, X):
        j, s = self.f.j, self.s
        self.z = X[:, 2]
        self.arms, self.legs = {}, {}
        on_arm, self.sleeve = np.zeros(len(X), dtype=bool), np.zeros(len(X))
        for side, sx in (("R", 1), ("L", -1)):
            arm = Limb(X, j[f"shoulder.{side}"], j[f"wrist.{side}"])
            arm.on = (X[:, 0] * sx > abs(j[f"shoulder.{side}"].x) * 0.7) & (arm.t > 0.04) & (arm.r < 0.085 * s)
            arm.elbow = (j[f"elbow.{side}"] - j[f"shoulder.{side}"]).length / arm.length
            self.arms[side] = arm
            on_arm |= arm.on
            self.sleeve = np.maximum(self.sleeve, arm.on * ss(0.08, 0.3, arm.t))  # (the sleeve head is still torso)
            leg = Limb(X, j[f"hip.{side}"], j[f"ankle.{side}"])
            leg.on = X[:, 0] * sx > 0
            leg.knee = (j[f"knee.{side}"] - j[f"hip.{side}"]).length / leg.length
            self.legs[side] = leg
        self.torso = ~on_arm
        yc = j["chest"].y
        self.az = np.arctan2(X[:, 0], X[:, 1] - yc)          # 0 front, ±pi/2 the sides, ±pi the back
        self.arc = self.az * 0.15 * s                          # roughly the distance round the torso from the front

    def _armpits(self, X):
        j, s = self.f.j, self.s
        d = np.full(len(X), 1.0)
        for side in ("R", "L"):
            sh = j[f"shoulder.{side}"]
            pit = np.array((sh.x * 0.82, sh.y, sh.z - 0.075 * s))
            off = X - pit
            off[:, 2] *= np.where(off[:, 2] > 0, 2.5, 1.0)       # (the hollow opens downward, not up under the collarbone)
            d = np.minimum(d, np.linalg.norm(off, axis=1))
        return ss(0.035 * s, 0.1 * s, d)

    # ---------------------------------------------------------------- ease: (lowest standoff, bridging allowed, highest)
    def _shirt(self, X):
        j, s, w, z = self.f.j, self.s, self.w, self.z
        waist, hem = w.waist, w.waist - 0.07 * s
        hi = np.full(len(X), 1.0)
        if self.suit:                   # skin-tight
            ease = 0.0008 * s + 0.0016 * s * ss(waist + 0.01 * s, waist - 0.01 * s, z)    # (clear of the trousers below)
        else:
            ease = 0.0012 * s + 0.0028 * s * ss(j["chest"].z + 0.1 * s, waist + 0.06 * s, z)
            ease *= 0.35 + 0.65 * ss(w.collar_z, w.collar_z - 0.035 * s, z)
            if self.belted:             # bloused over the belt, the tail hanging over the trousers below it
                zz = (z - (waist + 0.016 * s)) / (0.1 * s)
                ease += 0.0075 * s * ss(0.0, 0.22, zz) * (1.0 - ss(0.22, 1.0, zz)) * (0.75 + 0.25 * np.cos(self.az))
                ease += np.where(z < waist - 0.026 * s, 0.002 * s + 0.0025 * s * ss(waist - 0.026 * s, hem, z), 0.0)
                hi = np.where((z > waist - 0.026 * s) & (z < waist + 0.016 * s), 0.0085 * s, hi)
            for arm in self.arms.values():
                t = arm.t
                sleeve = 0.0012 * s + 0.0022 * s * ss(0.25, 0.65, t) + 0.0018 * s * ss(0.66, 0.8, t) * (1 - ss(0.88, 0.95, t))
                ease = np.where(arm.on, sleeve, ease)
        cap = ((0.034 if self.f.female else 0.024) * s * (1 - self.sleeve) + 0.006 * s * self.sleeve) * self._armpits(X)
        if self.w.head is not None:          # over the throat, where the head's neck stands proud of the body's
            ease += self.w.head.neck_depth(X)
        return self.t + ease, cap * (0.2 if self.suit else 1.0), hi

    def _pants(self, X):
        j, s, w, z = self.f.j, self.s, self.w, self.z
        ease = np.full(len(X), 0.0015 * s)
        top = w.boot_top
        for leg in self.legs.values():
            t = leg.t
            e = 0.0015 * s + 0.0025 * s * ss(0.02, 0.25, t) - 0.0008 * s * ss(0.5, 0.8, t)
            zz = (z - (top + 0.014 * s)) / (0.11 * s)
            e += 0.005 * s * ss(0.0, 0.25, zz) * (1.0 - ss(0.25, 1.0, zz))
            ease = np.where(leg.on, e, ease)
        if self.suit:
            ease = np.full(len(X), 0.0008 * s)
        hi = np.full(len(X), 1.0)
        hi = np.where(z > w.waist - 0.08 * s, 0.0075 * s, hi)                 # under the belt and the shirt's tail
        hi = np.where((z > top - 0.012 * s) & (z < top + 0.014 * s), 0.0055 * s, hi)   # inside the boot top
        crotch = np.array((0.0, 0.0, j["hip.R"].z - 0.09 * s))
        cap = 0.012 * s * ss(0.05 * s, 0.12 * s, np.linalg.norm(X - crotch, axis=1))
        return self.t + ease, cap, hi

    def _boots(self, X):
        j, s, z = self.f.j, self.s, self.z
        ease = 0.001 * s + 0.0035 * s * ss(self.w.sole + 0.002 * s, self.w.sole - 0.004 * s, z)
        ease += 0.0015 * s * ss(j["ball.R"].y - 0.03 * s, j["ball.R"].y + 0.02 * s, X[:, 1])   # roomier toe box
        # The shaft stands round and nearly straight from the ankle to the top instead of following the calf.
        for side, leg in (() if self.suit else self.legs.items()):      # (a creature's feet follow its legs)
            ankle = j[f"ankle.{side}"].z
            k = np.clip((z - ankle - 0.04 * s) / (self.w.boot_top - ankle - 0.04 * s), 0.0, 1.0)
            want = (0.047 + 0.013 * k) * s - leg.r
            ease = np.where(leg.on & (z > ankle + 0.02 * s), np.maximum(ease, np.minimum(want, 0.02 * s)), ease)
        return self.t + ease, np.full(len(X), 0.012 * s), np.full(len(X), 1.0)

    # ---------------------------------------------------------------- folds: (displacement, crease darkness)
    def _folds(self, X):
        """A few broad folds where cloth gathers (the crook of the elbows, above the cuffs, over the belt, behind the
        knees, from the crotch, above the boots), each set fading round the limb so none closes into a ring."""
        s, z = self.s, self.z
        fold = np.zeros(len(X))
        crease = np.zeros(len(X))
        if self.suit:
            return fold, crease

        def add(mask, x, amp, dark=0.1):
            nonlocal fold, crease
            r = ridge(x)
            fold = fold + mask * amp * (r - 0.3)
            crease = crease + mask * dark * (1.0 - r) ** 2

        big = 1.25 if self.kind == "coat" else 1.0
        if self.kind in ("shirt", "coat"):
            for k, (side, arm) in enumerate(self.arms.items()):
                seed = 1.3 + 2.9 * k
                along = arm.t * arm.length
                inner = np.clip(0.5 + 0.5 * arm.front, 0.0, 1.0) ** 2           # the crook of the elbow faces forward
                add(arm.on * bump(arm.t, arm.elbow, 0.07) * inner,
                    along / (0.04 * s * big) + 1.2 * np.abs(np.sin(arm.phi)) + 0.3 * wobble(arm.phi, seed), 0.0032 * s * big)
                one_side = 0.35 + 0.65 * (0.5 + 0.5 * np.sin(arm.phi + seed))
                add(arm.on * ss(0.7, 0.8, arm.t) * (1 - ss(0.88, 0.93, arm.t)) * one_side,
                    along / (0.036 * s * big) + 0.8 * wobble(arm.phi, seed + 1.0), 0.0022 * s * big)
            torso = self.torso
            if self.belted and self.kind == "shirt":
                waist = self.w.waist        # soft folds standing up out of the belt, gone well below the chest
                m = torso * ss(waist + 0.012 * s, waist + 0.03 * s, z) * (1 - ss(waist + 0.04 * s, waist + 0.13 * s, z))
                m = m * (0.675 + 0.325 * np.cos(self.az))                # (most in front, where the tail blouses)
                add(m, self.az * 16 / (2 * math.pi) + 0.25 * np.sin(3 * self.az + 1.0) + 0.15 * np.sin(5 * self.az), 0.0022 * s)
            elif self.kind == "coat":       # a little drape down the back from the shoulder blades
                m = torso * ss(self.f.j["chest"].z + 0.05 * s, self.w.waist + 0.05 * s, z) * ss(-0.3, -0.7, np.cos(self.az))
                add(m, self.arc / (0.075 * s) + 0.3 * np.sin(z / s * 9), 0.0016 * s, 0.06)
        elif self.kind == "pants":
            top = self.w.boot_top
            for k, (side, leg) in enumerate(self.legs.items()):
                seed = 0.7 + 3.1 * k
                along = leg.t * leg.length
                behind = np.clip(0.5 - 0.5 * leg.front, 0.0, 1.0) ** 1.5
                add(leg.on * bump(leg.t, leg.knee, 0.055) * behind,
                    along / (0.036 * s) - 0.9 * np.cos(leg.phi) + 0.2 * wobble(leg.phi, seed), 0.0026 * s)
                zz = (z - (top + 0.014 * s)) / (0.11 * s)
                one_side = 0.4 + 0.6 * (0.5 + 0.5 * np.cos(leg.phi - seed))
                add(leg.on * ss(0.0, 0.15, zz) * (1 - ss(0.45, 0.8, zz)) * one_side,
                    z / (0.032 * s) + 0.7 * wobble(leg.phi, seed + 4), 0.0026 * s)
            crotch_z = self.f.j["hip.R"].z - 0.09 * s
            psi = np.arctan2(X[:, 0], np.maximum(crotch_z + 0.03 * s - z, 0.01 * s))
            m = (X[:, 1] > 0) * bump(z, crotch_z - 0.03 * s, 0.05 * s) * ss(0.11 * s, 0.03 * s, np.abs(X[:, 0]))
            add(m, psi / 0.4 + 0.5, 0.0018 * s, 0.06)
        elif self.kind == "boots":
            j = self.f.j
            for k, (side, leg) in enumerate(self.legs.items()):
                ankle = j[f"ankle.{side}"]
                m = leg.on * bump(z, ankle.z + 0.01 * s, 0.03 * s) * ss(ankle.y - 0.01 * s, ankle.y + 0.04 * s, X[:, 1])
                add(m, (z + 0.3 * (X[:, 1] - ankle.y)) / (0.013 * s) + 0.3 * np.sin(leg.phi * 2), 0.0018 * s, 0.25)
                m = leg.on * ss(ankle.z + 0.05 * s, ankle.z + 0.08 * s, z) * (1 - ss(self.w.boot_top - 0.03 * s, self.w.boot_top - 0.005 * s, z))
                add(m, z / (0.034 * s) + wobble(leg.phi, 1.9 + k), 0.0016 * s, 0.2)
        return fold, crease
