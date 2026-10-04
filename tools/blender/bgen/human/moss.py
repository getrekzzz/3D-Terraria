"""Swamp growth (``moss``: the light moss's colour; ``mud``: the mud's): a body mouldering under the swamp.

The skin (``skin``: a dark, wet green) is mottled with browner rot and caked with mud in patches, thickest on the
legs and feet. Moss grows over it in lumpy cushions where water would leave it (over the shoulders, the crown, the
tops of the arms) and in scattered patches elsewhere: light on top, darker and wetter in its hollows and toward its
edges. Weed hangs off the shoulders, arms, head and jaw in strands and clumps, falling past the body under its own
weight, dark green, olive and brown, its ends dead and darker. The face is left mostly bare so it reads, and the
eyes glow (``eyes``).
"""
import math

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .coverings import Skin, arrays, make_mesh, noise, smooth_normals, ss, submesh
from .paint import hex3

WEED = ((0.17, 0.27, 0.09), (0.36, 0.38, 0.13), (0.32, 0.23, 0.11), (0.22, 0.3, 0.1))   # dark green, olive, brown, deep
# how readily weed hangs from each part of the body (by bone)
HANG = {"clavicle": 1.0, "upper_arm": 0.5, "forearm": 0.25, "neck": 0.5, "head": 0.6, "chest": 0.35, "spine": 0.2,
        "hips": 0.45, "thigh": 0.15}
SIDES = 3          # a strand's section: a flat ribbon with a ridge along it
POINTS = 7         # points along a strand


def _mix(a, b, t):
    t = np.asarray(t, dtype=float)[:, None]
    return a * (1 - t) + b * t


def _col(c, k):
    """Colour ``c`` (3,) scaled per point by ``k`` (n,)."""
    return np.asarray(c)[None, :] * np.asarray(k, dtype=float)[:, None]


class Swamp:
    def __init__(self, fr, head, p):
        self.fr, self.head, self.s = fr, head, fr.s
        self.skin = np.asarray(hex3(p["skin"]))
        self.moss = np.asarray(hex3(p["moss"]))
        self.mud = np.asarray(hex3(p.get("mud", "#2e2416")))

    def local(self, X):
        return np.array([self.head.local(Vector(tuple(x)))[:] for x in X])

    def face(self, X):
        """1 over the face (brow to chin, cheek to cheek), fading out round it; for head points."""
        q = self.local(X)
        f = (q[:, 0] / 0.052) ** 2 + ((q[:, 2] + 0.025) / 0.075) ** 2
        return ss(1.5, 0.8, f) * ss(0.0, 0.045, q[:, 1])

    # ------------------------------------------------------------------ where things grow
    def growth(self, X, N, face=None):
        """How thickly moss grows at each point (0..1): over the tops of things, and in ragged patches."""
        j, s = self.fr.j, self.s
        up = ss(-0.3, 0.6, N[:, 2])
        high = ss(j["hip.R"].z, j["shoulder.R"].z, X[:, 2])
        patch = ss(0.42, 0.58, noise(X, 0.11 * s, 21, 2))
        g = np.maximum(np.maximum(up * (0.2 + 0.8 * high), patch * 0.9), 0.55 * ss(j["spine"].z, j["chest"].z, X[:, 2]))
        g = g * ss(0.2, 0.4, noise(X, 0.045 * s, 22))
        return g if face is None else g * (1.0 - face)

    def caked(self, X):
        """How thickly mud cakes the skin: patches, thicker toward the feet, and the feet themselves."""
        j, s = self.fr.j, self.s
        low = ss(j["knee.R"].z + 0.25 * s, j["ankle.R"].z, X[:, 2])
        feet = ss(j["ankle.R"].z + 0.09 * s, j["ankle.R"].z - 0.01 * s, X[:, 2])
        return np.clip(ss(0.44, 0.58, noise(X, 0.09 * s, 33, 2)) * (0.5 + 0.5 * low) + feet, 0.0, 1.0)

    # ------------------------------------------------------------------ the skin
    def paint(self, obj, face=False):
        """Wet dark green mottled with rot, mud and the dark moss beneath the cushions."""
        V, T, N = arrays(obj)
        s = self.s
        fa = self.face(V) if face else np.zeros(len(V))
        g = self.growth(V, smooth_normals(V, T, N, 3), fa)
        col = _col(self.skin, 0.72 + 0.36 * noise(V, 0.035 * s, 5, 2))
        rot = ss(0.45, 0.7, noise(V, 0.08 * s, 9)) * 0.75
        col = _mix(col, (self.skin * 0.3 + self.mud * 0.7) * 1.1, rot)
        col = _mix(col, _col(self.mud, 0.75 + 0.35 * noise(V, 0.02 * s, 13)), self.caked(V) * 0.9 * (1.0 - 0.7 * fa))
        col = _mix(col, self.moss * 0.36, ss(0.1, 0.5, g) * 0.75)
        _paint(obj, col)

    # ------------------------------------------------------------------ moss cushions
    def cushions(self, name, obj, skin, face=False):
        """A lumpy shell of moss where it grows (char:hair), thinner on the head, or None."""
        V, T, N = arrays(obj)
        s = self.s
        Nn = smooth_normals(V, T, N, 3)
        keep = self.growth(V, Nn, self.face(V) if face else None) > 0.12
        if keep.sum() < 3:
            return None
        Vs, Ts, idx = submesh(V, T, keep)
        Ns = Nn[idx]
        g = self.growth(Vs, Ns, self.face(Vs) if face else None)
        lump = noise(Vs, 0.016 * s, 41, 2)
        cushion = noise(Vs, 0.06 * s, 42)
        h = (0.0022 + 0.012 * ss(0.12, 0.6, g) * (0.3 + 0.7 * lump) + 0.02 * ss(0.4, 0.85, g) * ss(0.3, 0.8, cushion)) * s
        h *= 0.55 if face else 1.0
        top = ss(0.0, 0.7, Ns[:, 2])
        light = ss(0.4, 0.75, lump) * (0.45 + 0.55 * top) * ss(0.2, 0.5, g)
        col = _mix(self.moss * 0.46, _col(self.moss, 0.92 + 0.22 * noise(Vs, 0.01 * s, 44)), light)
        col = _mix(col, (self.moss * 0.5 + self.mud * 0.5) * 0.8, ss(0.6, 0.8, noise(Vs, 0.05 * s, 45)) * 0.5)
        uv = np.stack([Vs[:, 2] * 8.0, np.arctan2(Vs[:, 0], Vs[:, 1] + 0.02) / np.pi], 1)
        return make_mesh(name, Vs + Ns * h[:, None], Ts, col, "char:hair", skin.weights(Vs), uv)

    # ------------------------------------------------------------------ hanging weed
    def roots(self, parts, rng):
        """(points, normals, kind) where weed hangs from: shoulders, arms, the head and jaw, clumped."""
        from .rig import read_weights
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
                w = w * (1.0 - self.face(V)) + 0.9 * jaw
                kind = np.where(jaw > 0.3, 2, 1)
            w *= ss(0.35, 0.65, noise(V, 0.09 * s, 51)) * (N[:, 2] > -0.6)
            P.append(V)
            Nr.append(N)
            K.append(kind)
            prob.append(w)
        P, Nr, K, prob = np.concatenate(P), np.concatenate(Nr), np.concatenate(K), np.concatenate(prob)
        pick = rng.choice(len(P), size=900, p=prob / prob.sum())
        keep, min_d = [], 0.034 * s
        for i in pick:                                   # spaced apart
            if all(np.sum((P[i] - P[k]) ** 2) > min_d * min_d for k in keep):
                keep.append(i)
            if len(keep) >= 150:
                break
        keep = np.asarray(keep, dtype=np.int64)
        return P[keep], Nr[keep], K[keep]

    def weed(self, parts, skin, shells):
        s = self.s
        rng = np.random.default_rng(2024)
        verts, polys, base = [], [], 0
        for o in [parts["body"], parts["head"], *parts["hands"]] + shells:
            V, T, _ = arrays(o)
            verts += [Vector(v) for v in V]
            polys += (T + base).tolist()
            base += len(V)
        tree = BVHTree.FromPolygons(verts, polys)
        roots, normals, kinds = self.roots(parts, rng)
        out = []                                         # (verts, tris, colours, uvs, weight)
        for root, n, kind in zip(roots, normals, kinds):
            w = skin.weights([Vector(root)])[0]
            lo, hi = ((0.12, 0.34), (0.05, 0.16), (0.1, 0.26), (0.05, 0.13))[kind]
            tang0 = np.cross(n, rng.normal(size=3))
            tang0 /= max(np.linalg.norm(tang0), 1e-9)
            for _ in range(int(rng.integers(2, 5))):
                at = root + tang0 * rng.uniform(-0.012, 0.012) * s + np.cross(n, tang0) * rng.uniform(-0.01, 0.01) * s
                out.append(self._strand(tree, at, n, rng.uniform(lo, hi) * s, rng.uniform(0.009, 0.019) * s, rng, w))
        V = np.concatenate([o[0] for o in out])
        offs = np.cumsum([0] + [len(o[0]) for o in out[:-1]])
        T = np.concatenate([o[1] + k for o, k in zip(out, offs)])
        C = np.concatenate([o[2] for o in out])
        U = np.concatenate([o[3] for o in out])
        W = [o[4] for o in out for _ in range(len(o[0]))]
        return make_mesh("weed", V, T, C, "char:hair", W, U)

    def _strand(self, tree, root, n, length, width, rng, weights):
        """One strand of weed: leaving the skin, falling under its weight and swaying, kept off the body."""
        s = self.s
        down = np.asarray((0.0, 0.0, -1.0))
        tang = np.cross(n, rng.normal(size=3))
        tang /= max(np.linalg.norm(tang), 1e-9)
        d = n * 0.55 + down * 0.45 + tang * 0.25
        d /= np.linalg.norm(d)
        pts = [root + n * 0.004 * s]
        seg = length / (POINTS - 1)
        phase, sway = rng.uniform(0, 2 * math.pi), rng.uniform(0.1, 0.35)
        for i in range(POINTS - 1):
            d = d + down * 0.45 + tang * sway * math.sin(phase + i * 1.3)
            d /= np.linalg.norm(d)
            q = pts[-1] + d * seg
            loc, nrm, _, _ = tree.find_nearest(Vector(q), 0.05 * s)
            if loc is not None and (q - np.asarray(loc)).dot(np.asarray(nrm)) < 0.005 * s:
                q = np.asarray(loc) + np.asarray(nrm) * 0.005 * s
            pts.append(q)
        pts = np.asarray(pts)
        tg = np.gradient(pts, axis=0)
        tg /= np.maximum(np.linalg.norm(tg, axis=1, keepdims=True), 1e-9)
        side = np.cross(tg[0], n)
        side = side / np.linalg.norm(side) if np.linalg.norm(side) > 1e-6 else tang
        twist = rng.uniform(-1.2, 1.2)
        base = np.asarray(WEED[int(rng.integers(0, len(WEED)))] if rng.random() < 0.8 else self.moss * 0.7)
        base = base * rng.uniform(0.8, 1.15)
        verts, cols, uvs = [], [], []
        for i in range(POINTS - 1):
            t = i / (POINTS - 1)
            side = side - tg[i] * side.dot(tg[i])
            side /= np.linalg.norm(side)
            a = twist * t
            up = np.cross(side, tg[i])
            sd = side * math.cos(a) + up * math.sin(a)
            up = np.cross(sd, tg[i])
            hw = width * (0.75 + 0.25 * math.sin(min(t / 0.2, 1.0) * math.pi / 2)) * (1.0 - 0.8 * t ** 1.3)
            for k, (x, y) in enumerate(((1.0, 0.0), (0.0, 0.18), (-1.0, 0.0))):
                verts.append(pts[i] + sd * x * hw + up * y * hw)
                c = base * (1.0 - 0.3 * t) * 0.85 + self.mud * 0.15 * t
                cols.append(c * (0.8 if k != 1 else 1.0))
                uvs.append((length * t * 6.0, k / SIDES * 0.6))
        verts.append(pts[-1])
        cols.append(base * 0.6)
        uvs.append((length * 6.0, 0.0))
        tris = []
        for i in range(POINTS - 2):
            for k in range(SIDES):
                a, b = i * SIDES + k, i * SIDES + (k + 1) % SIDES
                tris += [(a, b, b + SIDES), (a, b + SIDES, a + SIDES)]
        tip = (POINTS - 1) * SIDES
        for k in range(SIDES):
            tris.append(((POINTS - 2) * SIDES + k, (POINTS - 2) * SIDES + (k + 1) % SIDES, tip))
        return np.asarray(verts), np.asarray(tris, dtype=np.int64), np.asarray(cols), np.asarray(uvs), weights


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
    shells = [o for o in (sw.cushions("moss_body", parts["body"], skin), sw.cushions("moss_head", parts["head"], skin, True))
              if o is not None]
    out = shells + [sw.weed(parts, skin, shells)]
    sw.paint(parts["body"])
    sw.paint(parts["head"], face=True)
    for hand in parts["hands"]:
        sw.paint(hand)
    for o in parts["eyes"]:
        flat(o, p.get("eyes", "#e8e040"))
        set_slot(o, "char:glow")
    return out
