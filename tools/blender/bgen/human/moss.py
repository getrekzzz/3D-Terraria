"""Swamp growth (``moss``: the light moss's colour; ``mud``: the mud's): a body mouldering under the swamp.

The whole body is overgrown (char:hair): a continuous dark wet layer of slime and mud over the skin, and on it moss
in rounded cushions, crowded into a lumpy mat over the shoulders, the crown and the tops of the arms and thinning to
scattered clumps down the legs, where the layer is caked brown with mud. Each cushion is light on top and dark and
wet at its foot, and they vary from green to olive to yellow, drifting slowly across the body. On the head the growth
thins out raggedly round the face, which is left bare so it reads but stained in the same wet greens and muds
(``skin``: the darkest green, under it all), so there is no edge between them. The hands and the bare feet
(``feet: ape``) are stained and caked the same way. Weed hangs off the shoulders, arms, back of the head and jaw in
bunches of thin flat strands that fall past the body, lying over it where it bulges (drapes.py), olive and green,
browner and torn toward their ends. The eyes glow (``eyes``).
"""
import math

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

from . import drapes
from .coverings import Skin, arrays, decimate, edges_of, make_mesh, noise, smooth_normals, ss, strip, subdivide, submesh
from .paint import hex3
from .rig import read_weights

WEED = ((0.36, 0.42, 0.15), (0.46, 0.48, 0.19), (0.42, 0.35, 0.16), (0.28, 0.38, 0.13))   # olive, pale, brown, green
# how readily weed hangs from each part of the body (by bone)
HANG = {"clavicle": 1.0, "upper_arm": 0.5, "forearm": 0.25, "neck": 0.5, "head": 0.6, "chest": 0.35, "spine": 0.2,
        "hips": 0.45, "thigh": 0.15}
FILM = 0.003       # the wet layer's thickness (times the height scale)


def _blend(a, b):
    """Two vertices' bone weights averaged."""
    return {k: 0.5 * (a.get(k, 0.0) + b.get(k, 0.0)) for k in sorted(set(a) | set(b))}


def _domes(V, N, kd, area, rng, dens, size, rise, cover):
    """Low domes raised over a surface (points V, normals N, area per point, in KDTree ``kd``), about ``size``
    across and ``rise`` (range) times as high, crowded by ``dens`` so ``cover`` overlap where it is 1; the highest
    over a point wins. Returns (height, height in its own dome 0..1, dome per point, dome centres)."""
    rad = size * (0.6 + 0.8 * dens)
    seeds = np.flatnonzero(rng.random(len(V)) < dens * cover * area / (math.pi * rad * rad))
    dome, top, owner = np.zeros(len(V)), np.zeros(len(V)), np.zeros(len(V), dtype=np.int64)
    for k, i in enumerate(seeds):
        r = rad[i] * rng.uniform(0.7, 1.3)
        hgt = r * rng.uniform(*rise)
        near = np.asarray([h[1] for h in kd.find_range(Vector(V[i]), r)], dtype=np.int64)
        q = V[near] - V[i]
        d = np.linalg.norm(q - np.outer(q @ N[i], N[i]), axis=1) / r
        z = hgt * np.sqrt(np.clip(1.0 - d * d, 0.0, 1.0))
        win = z > dome[near]
        dome[near[win]], top[near[win]], owner[near[win]] = z[win], z[win] / hgt, k
    return dome, top, owner, seeds


def _mix(a, b, t):
    t = np.asarray(t, dtype=float)[:, None]
    return a * (1 - t) + b * t


class Swamp:
    def __init__(self, fr, head, p):
        self.fr, self.head, self.s = fr, head, fr.s
        self.skin = np.asarray(hex3(p["skin"]))
        self.moss = np.asarray(hex3(p["moss"]))
        self.mud = np.asarray(hex3(p.get("mud", "#2e2416")))

    def local(self, X):
        return np.array([self.head.local(Vector(tuple(x)))[:] for x in X])

    def face(self, X):
        """1 over the face (brow to chin, cheek to cheek), fading out raggedly round it; for head points."""
        q = self.local(X)
        f = (q[:, 0] / 0.054) ** 2 + ((q[:, 2] + 0.022) / 0.074) ** 2
        f = f + 0.9 * (noise(X, 0.022 * self.s, 71, 2) - 0.5)
        return ss(1.5, 0.75, f) * ss(-0.005, 0.04, q[:, 1])

    # ------------------------------------------------------------------ where it grows
    def muddy(self, X):
        """How much mud is caked in (0..1): toward the feet, and in broad patches."""
        j, s = self.fr.j, self.s
        low = ss(j["knee.R"].z + 0.2 * s, j["ankle.R"].z - 0.02 * s, X[:, 2])
        return np.clip(0.85 * low + 0.45 * ss(0.55, 0.78, noise(X, 0.16 * s, 33, 2)), 0.0, 1.0)

    def density(self, X, N):
        """How crowded the cushions are (0..1): over the tops of things high on the body, and in clumps."""
        j, s = self.fr.j, self.s
        high = ss(j["knee.R"].z, j["shoulder.R"].z, X[:, 2])
        up = ss(-0.2, 0.7, N[:, 2])
        clump = ss(0.3, 0.75, noise(X, 0.09 * s, 21, 2))
        mud = self.muddy(X) * (0.4 + 0.6 * ss(0.3, 0.7, noise(X, 0.05 * s, 23)))      # lumps of mud down the legs
        return ss(0.12, 0.45, np.maximum(np.maximum(up * (0.25 + 0.75 * high), clump * (0.3 + 0.7 * high)), mud * 0.7))

    def wet(self, X, N):
        """The wet layer's colour: dark green going to brown, caked with mud toward the feet, catching a little
        moss where it faces up."""
        s = self.s
        col = _mix(self.skin * 0.75, self.skin * 0.45 + self.mud * 0.45, ss(0.35, 0.75, noise(X, 0.12 * s, 9)))
        col = _mix(col, self.moss * 0.5, ss(0.2, 0.9, N[:, 2]) * 0.35)
        mud = self.mud * (0.8 + 0.3 * noise(X, 0.03 * s, 13))[:, None]
        streak = ss(0.55, 0.8, noise(X * np.asarray((1.0, 1.0, 0.25)), 0.018 * s, 77))      # run down from above
        col = _mix(col, mud, np.maximum(self.muddy(X) * 0.8, streak * 0.55))
        return col * (0.9 + 0.2 * noise(X, 0.012 * s, 44))[:, None]

    def tint(self, X, rng):
        """Each cushion's moss: drifting from green to olive across the body, each a little different."""
        olive = self.moss * np.asarray((1.08, 0.92, 0.6))
        col = _mix(self.moss, olive, ss(0.3, 0.7, noise(X, 0.24 * self.s, 61)) * 0.8 + rng.uniform(0, 0.3, len(X)))
        col = _mix(col, self.skin * 1.2, rng.uniform(0, 0.4, len(X)) ** 2)
        col = _mix(col, self.mud * 1.2, self.muddy(X) * 0.8)
        return col * rng.uniform(0.8, 1.12, len(X))[:, None]

    # ------------------------------------------------------------------ the growth
    def layer(self, name, obj, fade, rng, size, fine=True, cover=1.8):
        """The growth over a skin part where ``fade`` > 0: the wet layer, thinning with it, and moss cushions
        (``size`` across, times the height scale) raised out of it, crowded where the moss grows thickly so they
        merge into a lumpy mat (``cover``: how many overlap there), light on their tops and dark and wet in the clefts
        between them. ``fine`` splits each face in four first, so the cushions come out round. The skin faces under
        it are stripped."""
        V, T, N = arrays(obj)
        Nn = smooth_normals(V, T, N, 3)
        s = self.s
        W = read_weights(obj)
        Vs, Ts, idx = submesh(V, T, fade > 0.03)
        Ns, Ws, fs = Nn[idx], [W[i] for i in idx], fade[idx]
        if fine:
            E = edges_of(Ts)
            Vs, Ts, Ns = subdivide(Vs, Ts, Ns)
            Ws = Ws + [_blend(Ws[a], Ws[b]) for a, b in E]
            fs = np.concatenate([fs, (fs[E[:, 0]] + fs[E[:, 1]]) / 2])
        area = np.zeros(len(Vs))
        tri = 0.5 * np.linalg.norm(np.cross(Vs[Ts[:, 1]] - Vs[Ts[:, 0]], Vs[Ts[:, 2]] - Vs[Ts[:, 0]]), axis=1)
        for k in range(3):
            np.add.at(area, Ts[:, k], tri / 3)
        kd = KDTree(len(Vs))
        for i, co in enumerate(Vs):
            kd.insert(co, i)
        kd.balance()
        dens = self.density(Vs, Ns) * ss(0.5, 0.9, fs)
        mound = _domes(Vs, Ns, kd, area, rng, (0.45 + 0.55 * dens) * ss(0.5, 0.9, fs), size * 2.4 * s, (0.12, 0.22), 1.2)[0]
        cush, top, owner, seeds = _domes(Vs, Ns, kd, area, rng, dens, size * s, (0.3, 0.5), cover)
        h = s * (FILM + 0.002 * self.muddy(Vs)) * fs + mound + cush
        tints = self.tint(Vs[seeds], rng) if len(seeds) else np.zeros((1, 3))
        moss = tints[owner] * (0.5 + 0.35 * top + 0.2 * ss(0.0, 0.01 * s, mound))[:, None]
        col = _mix(self.wet(Vs, Ns), moss, ss(0.0, 0.35, top))
        smear = ss(0.4, 0.75, noise(Vs, 0.025 * s, 81)) * ss(0.6, 0.2, fs)       # moss smeared over the bare face
        col = _mix(col, self.moss * (0.42 + 0.2 * noise(Vs, 0.01 * s, 82))[:, None], smear * 0.6)
        uv = np.stack([Vs[:, 2] * 8.0, np.arctan2(Vs[:, 0], Vs[:, 1] + 0.02) / np.pi], 1)
        out = make_mesh(name, Vs + Ns * h[:, None], Ts, col, "char:hair", Ws, uv)
        strip(obj, fade > 0.1)
        return out

    def stain(self, obj):
        """A bare skin part stained to match the wet layer."""
        V, T, N = arrays(obj)
        _paint(obj, self.wet(V, smooth_normals(V, T, N, 3)))

    # ------------------------------------------------------------------ hanging weed
    def roots(self, parts, rng):
        """(points, normals, kind) where weed hangs from: shoulders, arms, the back of the head and the jaw."""
        s = self.s
        P, Nr, K, prob = [], [], [], []
        for part in ("body", "head"):
            V, T, N = arrays(parts[part])
            N = smooth_normals(V, T, N, 3)
            W = read_weights(parts[part])
            w = np.array([sum(HANG.get(b.split(".")[0], 0.0) * x for b, x in wt.items()) for wt in W])
            arm = np.array([sum(x for b, x in wt.items() if b.split(".")[0] in ("upper_arm", "forearm", "hand")) for wt in W])
            kind = np.where(arm > 0.5, 3, 0)
            if part == "head":
                q = self.local(V)
                jaw = ss(-0.04, -0.075, q[:, 2]) * ss(0.0, 0.03, q[:, 1]) * ss(0.06, 0.03, np.abs(q[:, 0]))
                w = w * ss(0.03, -0.01, q[:, 1]) * ss(-0.06, 0.0, q[:, 2]) + 0.9 * jaw    # not over the face
                kind = np.where(jaw > 0.3, 2, 1)
            w *= ss(0.35, 0.65, noise(V, 0.09 * s, 51)) * (N[:, 2] > -0.6)
            P.append(V)
            Nr.append(N)
            K.append(kind)
            prob.append(w)
        P, Nr, K, prob = np.concatenate(P), np.concatenate(Nr), np.concatenate(K), np.concatenate(prob)
        pick = rng.choice(len(P), size=900, p=prob / prob.sum())
        keep, min_d = [], 0.045 * s
        for i in pick:                                   # spaced apart
            if all(np.sum((P[i] - P[k]) ** 2) > min_d * min_d for k in keep):
                keep.append(i)
            if len(keep) >= 75:
                break
        keep = np.asarray(keep, dtype=np.int64)
        return P[keep], Nr[keep], K[keep]

    def weed(self, roots, skin, over, rng):
        """Bunches of thin flat strands from ``roots``, falling past the growth (``over``: its outer surfaces) and
        lying over it where it bulges."""
        s = self.s
        verts, polys, base = [], [], 0
        for o in over:
            V, T, _ = arrays(o)
            verts += [Vector(v) for v in V]
            polys += (T + base).tolist()
            base += len(V)
        tree = BVHTree.FromPolygons(verts, polys)
        out = []
        for root, n, kind in zip(*roots):
            lo, hi = ((0.12, 0.3), (0.07, 0.17), (0.1, 0.22), (0.05, 0.12))[kind]
            tang = np.cross(n, rng.normal(size=3))
            tang /= max(np.linalg.norm(tang), 1e-9)
            tone0 = np.asarray(WEED[int(rng.integers(0, len(WEED)))])
            for _ in range(int(rng.integers(3, 6))):
                at = root + n * 0.04 * s + tang * rng.uniform(-0.016, 0.016) * s + np.cross(n, tang) * rng.uniform(-0.01, 0.01) * s
                loc, nrm, _, _ = tree.find_nearest(Vector(at))
                if loc is None:
                    continue
                length = rng.uniform(lo, hi) * s
                pts, nrms, sk = drapes.trace(tree, np.asarray(loc), np.asarray(nrm), length, 9, 0.003 * s, rng, sway=0.3,
                                             cling=0.45)
                V, T, A, L, S = drapes.ribbon(pts, nrms, sk, rng.uniform(0.006, 0.011) * s, 0.0016 * s, rng,
                                              taper=0.35, ragged=0.5, section=((1, 0), (0, 1), (-1, 0), (0, -1)))
                tone = tone0 * rng.uniform(0.85, 1.15) if rng.random() < 0.85 else self.moss * 0.75
                t = (L / length)[:, None]
                col = tone * (1.0 - 0.3 * t) * (0.88 + 0.12 * np.sin(A * np.pi))[:, None]
                col = col * (1 - 0.4 * t) + self.mud * 0.4 * t
                out.append((V, T, col, np.stack([L * 6.0, A * 0.6], 1), S))
        V = np.concatenate([o[0] for o in out])
        offs = np.cumsum([0] + [len(o[0]) for o in out[:-1]])
        T = np.concatenate([o[1] + k for o, k in zip(out, offs)])
        S = np.concatenate([o[4] for o in out])
        return make_mesh("weed", V, T, np.concatenate([o[2] for o in out]), "char:hair", skin.weights([Vector(x) for x in S]),
                         np.concatenate([o[3] for o in out]))


def _paint(obj, rgb):
    me = obj.data
    layer = me.color_attributes.get("Color") or me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    idx = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", idx)
    rgba = np.ones((len(idx), 4), dtype=np.float32)
    rgba[:, :3] = np.clip(rgb[idx], 0, 1)
    layer.data.foreach_set("color_srgb", rgba.ravel())


def grow(fr, head, parts, p, worn):
    from .garments import set_slot
    from .paint import flat
    sw = Swamp(fr, head, p)
    skin = Skin(parts)
    rng = np.random.default_rng(2024)
    roots = sw.roots(parts, rng)
    body, hd = parts["body"], parts["head"]
    for o in [body, hd, *parts["hands"], *parts.get("feet", [])]:
        sw.stain(o)                                  # before the layer strips the skin under it
    Vb, Vh = arrays(body)[0], arrays(hd)[0]
    out = [sw.layer("moss_body", body, np.ones(len(Vb)), rng, 0.016, cover=2.4),
           sw.layer("moss_head", hd, np.maximum(1.0 - sw.face(Vh), 0.2), rng, 0.012, fine=False, cover=2.4)]
    decimate(out[0], 0.6)
    decimate(out[1], 0.5)
    for i, foot in enumerate(parts.get("feet", [])):
        out.append(sw.layer(f"moss_foot_{i}", foot, np.ones(len(foot.data.vertices)), rng, 0.01, fine=False, cover=1.0))
    out.append(sw.weed(roots, skin, out + [hd, *parts["hands"]], rng))
    for o in parts["eyes"]:
        flat(o, p.get("eyes", "#e8e040"))
        set_slot(o, "char:glow")
    return out
