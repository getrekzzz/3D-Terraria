"""Tailoring: a garment's raw shell (the body's surface over one region, cut out by garments.Wardrobe.shell) shaped
into cloth. The shell is refined, then

- eased off the skin: closer over the shoulders than over the belly and forearms, cinched under the belt and inside
  the boot tops, bloused out above them and gathered above the cuffs;
- spread across hollows the way cloth spans them (between the shoulder blades, under the chest, at the small of the
  back): a membrane relaxed over the eased surface that never sinks into it, held back at the armpits and crotch
  where the limbs part;
- folded where it bunches: inside the elbows and behind the knees, stacked above the cuffs and the boot tops, fanned
  out from the belt, creased across the instep of boots. Creases are darkened in the vertex colour as brightness,
  which the game's recolouring carries over to the chosen colour.

finishing.py then reduces the mesh to its budget and adds seams, collars, cuffs, hems, boot rims, soles and buttons.
"""
import math

import bmesh
import numpy as np

UP = np.array((0.0, 0.0, 1.0))


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


def membrane(X, N, edges, lo, hi, pinned, iters=160):
    """Heights along N (between ``lo`` and ``hi``) of a membrane resting on X: each point is pulled toward its
    neighbours' average, so cloth fills hollows and lies on the bulges; ``pinned`` points stay at ``lo``."""
    n = len(X)
    a, b = edges[:, 0], edges[:, 1]
    deg = np.maximum(np.bincount(edges.ravel(), minlength=n), 1).astype(float)
    hi = np.where(pinned, lo, np.maximum(hi, lo))
    h = lo.copy()
    for _ in range(iters):
        P = X + N * h[:, None]
        nb = np.stack([np.bincount(a, P[b, k], n) + np.bincount(b, P[a, k], n) for k in range(3)], 1) / deg[:, None]
        h = np.clip(h + 0.9 * np.einsum("ij,ij->i", nb - P, N), lo, hi)
    return h


def rings(edges, start, k):
    """How many edges away from a ``start`` vertex each vertex lies, counted up to ``k``."""
    n = len(start)
    d = np.where(start, 0.0, float(k))
    seen = start.copy()
    for step in range(1, k):
        hit = (np.bincount(edges[:, 0], seen[edges[:, 1]], n) + np.bincount(edges[:, 1], seen[edges[:, 0]], n)) > 0
        new = hit & ~seen
        d[new] = step
        seen |= new
    return d


def _nearest(points, X, values):
    """``values`` (one per point of ``points``) read at the nearest point to each of X."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(points))
    for i, p in enumerate(points):
        kd.insert(p, i)
    kd.balance()
    out = np.empty(len(X))
    for n, x in enumerate(X):                # inverse-distance blend of the four nearest, so steps don't show
        near = kd.find_n(x, 4)
        w = np.array([1.0 / (d + 1e-4) for _, _, d in near])
        out[n] = np.dot(w, [values[i] for _, i, _ in near]) / w.sum()
    return out


def refine(bm):
    """Splits every triangle in four so folds have the vertices to form."""
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.normal_update()
    bm.verts.index_update()


class Cloth:
    """The shape of one garment: ``kind`` is shirt, coat, pants or boots; ``thickness`` in units of the frame scale."""

    def __init__(self, wardrobe, kind, thickness):
        self.w, self.f, self.s = wardrobe, wardrobe.f, wardrobe.f.s
        self.kind = kind
        self.t = thickness * self.s
        self.belted = wardrobe.tucked

    def apply(self, bm):
        """Moves the refined shell's vertices onto the cloth surface; returns each vertex's crease shade (0..1)."""
        refine(bm)
        bm.verts.ensure_lookup_table()
        X = np.array([v.co[:] for v in bm.verts])
        N = np.array([v.normal[:] for v in bm.verts])
        edges = np.array([(e.verts[0].index, e.verts[1].index) for e in bm.edges])
        pinned = np.array([v.is_boundary for v in bm.verts])
        self._regions(X)
        lo, cap, hi = getattr(self, "_" + ("shirt" if self.kind == "coat" else self.kind))(X)
        if self.kind in ("shirt", "coat"):
            lo = np.minimum(np.maximum(lo, np.minimum(self._hang(X, N, lo), lo + cap)), np.maximum(hi, self.t))
        if self.kind == "coat":
            under = self.w.standoff.get("shirt")
            if under is not None:
                lo = np.maximum(lo * 1.15 + 0.002 * self.s, _nearest(under[0], X, under[1]) + 0.0035 * self.s)
            cap, hi = cap * 1.4, np.full(len(X), 1.0)
        h = membrane(X, N, edges, lo, np.minimum(lo + cap, hi), pinned)
        fold, crease = self._folds(X)
        fade = ss(0.0, 4.0, rings(edges, pinned, 4))       # no folds right at a hem: the bands finish the edge there
        h = h + fold * fade
        crease = crease * fade
        self.w.standoff[self.kind] = (X, h)
        P = X + N * h[:, None]
        if self.kind == "boots":                             # a flat tread under the foot
            P[:, 2] = np.maximum(P[:, 2], -0.001 * self.s)
        for v, p in zip(bm.verts, P):
            v.co = p
        bm.normal_update()
        return np.clip(1.0 - crease, 0.6, 1.0)

    def _hang(self, X, N, lo):
        """Cloth falls from the chest and the shoulder blades instead of following the body in under them: below
        the most forward (or backward) point of each column of the torso it hangs nearly plumb (raised ``lo``)."""
        s, z = self.s, self.z
        chest = self.f.j["chest"].z
        cloth = X + N * lo[:, None]
        col = np.round(X[:, 0] / (0.012 * s)).astype(int)
        out = lo.copy()
        for sign in (1.0, -1.0):
            facing = self.torso & (N[:, 1] * sign > 0.3)
            band = facing & (z > chest - 0.04 * s) & (z < chest + 0.13 * s)
            if not band.any():
                continue
            top = {}
            for c, y, zz in zip(col[band], cloth[band, 1] * sign, z[band]):
                if c not in top or y > top[c][0]:
                    top[c] = (y, zz)
            idx = np.flatnonzero(facing & (z > (self.w.waist + 0.016 * s if self.belted else -1.0)))
            peak = np.array([top.get(c, (-1.0, 0.0)) for c in col[idx]])
            below = peak[:, 1] - z[idx]
            want = peak[:, 0] - 0.15 * np.maximum(below, 0.0)                 # a slight slope in toward the belt
            ny = N[idx, 1] * sign
            need = lo[idx] + (want - cloth[idx, 1] * sign) / ny * ss(0.3, 0.6, ny)
            out[idx] = np.where(below > 0, np.maximum(lo[idx], need), lo[idx])
        return out

    # ---------------------------------------------------------------- where each point lies
    def _regions(self, X):
        j, s = self.f.j, self.s
        self.z = X[:, 2]
        self.arms, self.legs = {}, {}
        on_arm = np.zeros(len(X), dtype=bool)
        for side, sx in (("R", 1), ("L", -1)):
            arm = Limb(X, j[f"shoulder.{side}"], j[f"wrist.{side}"])
            arm.on = (X[:, 0] * sx > abs(j[f"shoulder.{side}"].x) * 0.7) & (arm.t > 0.04) & (arm.r < 0.085 * s)
            arm.elbow = (j[f"elbow.{side}"] - j[f"shoulder.{side}"]).length / arm.length
            self.arms[side] = arm
            on_arm |= arm.on
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
            d = np.minimum(d, np.linalg.norm(X - pit, axis=1))
        return ss(0.035 * s, 0.1 * s, d)

    # ---------------------------------------------------------------- ease: (lowest standoff, bridging allowed, highest)
    def _shirt(self, X):
        j, s, w, z = self.f.j, self.s, self.w, self.z
        waist, hem = w.waist, w.waist - 0.07 * s
        ease = 0.0012 * s + 0.0028 * s * ss(j["chest"].z + 0.1 * s, waist + 0.06 * s, z)
        ease *= 0.35 + 0.65 * ss(w.collar_z, w.collar_z - 0.035 * s, z)
        hi = np.full(len(X), 1.0)
        if self.belted:
            zz = (z - (waist + 0.016 * s)) / (0.1 * s)
            ease += 0.0085 * s * ss(0.0, 0.22, zz) * (1.0 - ss(0.22, 1.0, zz)) * (0.75 + 0.25 * np.cos(self.az))
            ease += np.where(z < waist - 0.026 * s, 0.002 * s + 0.0025 * s * ss(waist - 0.026 * s, hem, z), 0.0)
            hi = np.where((z > waist - 0.026 * s) & (z < waist + 0.016 * s), 0.0085 * s, hi)
        for arm in self.arms.values():
            t = arm.t
            sleeve = 0.0015 * s + 0.0025 * s * ss(0.25, 0.65, t) + 0.0045 * s * ss(0.66, 0.8, t) * (1 - ss(0.88, 0.95, t))
            ease = np.where(arm.on, sleeve, ease)
        cap = np.where(self.torso, (0.032 if self.f.female else 0.022) * s, 0.006 * s) * self._armpits(X)
        if self.w.head is not None:          # over the throat, where the head's neck stands proud of the body's
            ease += self.w.head.neck_depth(X)
        return self.t + ease, cap, hi

    def _pants(self, X):
        j, s, w, z = self.f.j, self.s, self.w, self.z
        ease = np.full(len(X), 0.0015 * s)
        top = w.boot_top
        for leg in self.legs.values():
            t = leg.t
            e = 0.0015 * s + 0.0025 * s * ss(0.02, 0.25, t) - 0.0008 * s * ss(0.5, 0.8, t)
            zz = (z - (top + 0.014 * s)) / (0.11 * s)
            e += 0.009 * s * ss(0.0, 0.25, zz) * (1.0 - ss(0.25, 1.0, zz))
            ease = np.where(leg.on, e, ease)
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
        for side, leg in self.legs.items():
            ankle = j[f"ankle.{side}"].z
            k = np.clip((z - ankle - 0.04 * s) / (self.w.boot_top - ankle - 0.04 * s), 0.0, 1.0)
            want = (0.047 + 0.013 * k) * s - leg.r
            ease = np.where(leg.on & (z > ankle + 0.02 * s), np.maximum(ease, np.minimum(want, 0.02 * s)), ease)
        return self.t + ease, np.full(len(X), 0.012 * s), np.full(len(X), 1.0)

    # ---------------------------------------------------------------- folds: (displacement, crease darkness)
    def _folds(self, X):
        s, z = self.s, self.z
        fold = np.zeros(len(X))
        crease = np.zeros(len(X))

        def add(mask, x, amp, dark=0.22):
            nonlocal fold, crease
            r = ridge(x)
            fold = fold + mask * amp * (r - 0.3)
            crease = crease + mask * dark * (1.0 - r) ** 2

        big = 1.3 if self.kind == "coat" else 1.0
        if self.kind in ("shirt", "coat"):
            for k, (side, arm) in enumerate(self.arms.items()):
                seed = 1.3 + 2.9 * k
                inner = np.clip(0.5 + 0.5 * arm.front, 0.0, 1.0) ** 2
                along = arm.t * arm.length
                add(arm.on * bump(arm.t, arm.elbow, 0.09) * (0.25 + 0.75 * inner),
                    along / (0.034 * s * big) + wobble(arm.phi, seed), 0.005 * s * big)
                add(arm.on * ss(0.66, 0.78, arm.t) * (1 - ss(0.88, 0.93, arm.t)),
                    along / (0.027 * s * big) + 1.3 * wobble(arm.phi, seed + 1.0), 0.0042 * s * big)
            torso = self.torso
            if self.belted and self.kind == "shirt":
                waist = self.w.waist
                # Fanned out from the belt, front and back (the fans meet under the side seams).
                z0 = waist - 0.12 * s
                back = np.abs(self.az) > math.pi / 2
                u = np.where(back, (np.mod(self.az, 2 * math.pi) - math.pi) * 0.15 * s, self.arc)
                psi = np.arctan2(u, np.maximum(z - z0, 0.01 * s))
                m = torso * ss(waist + 0.01 * s, waist + 0.03 * s, z) * (1 - ss(waist + 0.05 * s, waist + 0.22 * s, z))
                add(m * np.abs(np.cos(self.az)) ** 0.5, psi / 0.2 + 0.2 * np.sin(z / s * 18 + self.az * 3), 0.003 * s)
                m = torso * ss(waist - 0.03 * s, waist - 0.06 * s, z)
                add(m, self.arc / (0.045 * s) + 0.2 * np.sin(z / s * 30), 0.0015 * s, 0.15)
            elif self.kind == "coat":
                m = torso * ss(self.f.j["chest"].z, self.w.waist, z) * np.clip(-np.cos(self.az), 0.2, 1.0)
                add(m, self.arc / (0.06 * s) + 0.3 * np.sin(z / s * 12), 0.003 * s, 0.22)
        elif self.kind == "pants":
            top = self.w.boot_top
            for k, (side, leg) in enumerate(self.legs.items()):
                seed = 0.7 + 3.1 * k
                along = leg.t * leg.length
                behind = np.clip(0.5 - 0.5 * leg.front, 0.0, 1.0) ** 1.5
                front = np.clip(0.5 + 0.5 * leg.front, 0.0, 1.0) ** 2
                add(leg.on * bump(leg.t, leg.knee, 0.07) * behind, along / (0.023 * s) + wobble(leg.phi, seed), 0.004 * s)
                add(leg.on * bump(leg.t, leg.knee - 0.07, 0.06) * front, along / (0.04 * s) + 0.6 * wobble(leg.phi, seed + 2), 0.0022 * s, 0.2)
                zz = (z - (top + 0.014 * s)) / (0.11 * s)
                add(leg.on * ss(0.0, 0.12, zz) * (1 - ss(0.6, 1.0, zz)), z / (0.02 * s) + 1.4 * wobble(leg.phi, seed + 4), 0.004 * s)
            crotch_z = self.f.j["hip.R"].z - 0.09 * s
            psi = np.arctan2(X[:, 0], np.maximum(crotch_z + 0.03 * s - z, 0.01 * s))
            m = (X[:, 1] > 0) * bump(z, crotch_z - 0.03 * s, 0.06 * s) * ss(0.13 * s, 0.04 * s, np.abs(X[:, 0]))
            add(m, psi / 0.22, 0.0025 * s, 0.22)
        elif self.kind == "boots":
            j = self.f.j
            for k, (side, leg) in enumerate(self.legs.items()):
                ankle = j[f"ankle.{side}"]
                m = leg.on * bump(z, ankle.z + 0.01 * s, 0.03 * s) * ss(ankle.y - 0.01 * s, ankle.y + 0.04 * s, X[:, 1])
                add(m, (z + 0.3 * (X[:, 1] - ankle.y)) / (0.013 * s) + 0.3 * np.sin(leg.phi * 2), 0.0018 * s, 0.25)
                m = leg.on * ss(ankle.z + 0.05 * s, ankle.z + 0.08 * s, z) * (1 - ss(self.w.boot_top - 0.03 * s, self.w.boot_top - 0.005 * s, z))
                add(m, z / (0.034 * s) + wobble(leg.phi, 1.9 + k), 0.0016 * s, 0.2)
        return fold, crease
