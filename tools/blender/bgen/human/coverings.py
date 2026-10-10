"""Creature coverings grown over the skin in place of clothes: a fur coat (fur.py), linen wraps (wraps.py) and swamp
growth (moss.py), with the creature heads, hands and feet that go with them (muzzles.py, paws.py,
hooves.py).

A covering is built from copies of the skinned body, head and hands, so it fits them exactly and follows the
animation with their weights; the skin it hides is removed. With a covering a character wears only the garments its
own parameters name (``pants`` alone gives the werewolf his trousers), torn when ``torn`` is set.

Parameters (listed with the others in human/__init__.py): fur (a shaggy coat's colour, fur.py) with furBelly (paler
chest and belly), furLength (1) and furKind (wolf|ape|goat: a goat-demon's shaggy hide and legs, fur.py); wraps (a
mummy's linen, wraps.py); moss (light moss, moss.py) with mud; muzzle (wolf|ape: a wolf's head with snout and tall
ears, or an ape's face, muzzles.py); faceSkin (bare face, hands and feet under fur); hands (claws); handSize (1,
paws.py); feet (also paws|ape, paws.py; hooves: cloven hooves, hooves.py); nails (claws', nails' and hooves'
colour); torn (tears the clothes worn, as undead's are).
"""
import math

import bpy
import numpy as np
from mathutils.kdtree import KDTree

from .rig import read_weights, write_weights

COVERINGS = ("fur", "wraps", "moss")
GARMENTS = ("shirt", "pants", "boots")


def covering(p):
    return next((k for k in COVERINGS if k in p), None)


def undress(p, merged):
    """The parameters a covered character is dressed with: only the garments it names (``merged`` has the defaults)."""
    if covering(p) is None:
        return merged
    return {k: v for k, v in merged.items() if k not in GARMENTS or k in p}


def wears(p, garment):
    """Whether a character wears ``garment`` (pants, boots): everyone does unless covered and not naming it."""
    return covering(p) is None or garment in p


def head_shape(head, p):
    """A creature head (``muzzle``) is sculpted into the head's field before it is meshed."""
    if p.get("muzzle"):
        from .muzzles import Muzzle
        head._shape = Muzzle(head.shape, p["muzzle"])


def grow(fr, head, parts, p, worn):
    """The covering's objects (skinned), after the clothes; removes the skin it hides."""
    kind = covering(p)
    if kind is None:
        return []
    from importlib import import_module
    out = import_module(f".{kind}", __package__).grow(fr, head, parts, p, worn)
    if p.get("muzzle") == "wolf":
        from .muzzles import fangs
        out.append(fangs(head))
    return out


def reshape(fr, p):
    from .paws import reshape
    reshape(fr, p)


def shod(p):
    """Whether the body grows its own (shoe-like) feet."""
    from .paws import KINDS
    return p.get("feet") not in ("talons", "hooves") + KINDS


def nail(fr, p, side, obj):
    """A fingertip's nails, or the claws that replace them (``hands: claws``)."""
    if p.get("hands") != "claws":
        return obj
    from .paws import hand_claws
    me = obj.data
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(me)
    return hand_claws(fr, side)


def feet(fr, p):
    """([foot per side], [claws per side]) for paws, ape feet and hooves, painted; ([], []) otherwise."""
    from .paws import KINDS, build_feet
    if p.get("feet") not in KINDS + ("hooves",):
        return [], []
    from .hooves import build_hoof
    from .paint import flat
    out = ([], [])
    for side in ("R", "L"):
        foot, claw = build_hoof(fr, side) if p["feet"] == "hooves" else build_feet(fr, side, p["feet"])
        flat(foot, p.get("faceSkin", p.get("skin", "#8a7a6a")))
        flat(claw, p.get("nails", "#2a2420"))
        out[0].append(foot)
        out[1].append(claw)
    return out


# ------------------------------------------------------------------------------------------------ noise
def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFFFF).astype(np.float64) / float(0xFFFFFF)


def noise(X, scale, seed=0, octaves=1):
    """Smooth value noise (0..1) at points X (N, 3) with features about ``scale`` metres across; octaves add finer
    detail at half the strength each."""
    X = np.asarray(X, dtype=np.float64)
    total, amp, norm = np.zeros(len(X)), 1.0, 0.0
    for o in range(octaves):
        g = X / (scale / 2 ** o) + 17.3 * o
        i = np.floor(g).astype(np.int64)
        t = g - i
        t = t * t * t * (t * (t * 6 - 15) + 10)
        v = np.zeros(len(X))
        for dx in (0, 1):
            wx = t[:, 0] if dx else 1 - t[:, 0]
            for dy in (0, 1):
                wy = t[:, 1] if dy else 1 - t[:, 1]
                for dz in (0, 1):
                    wz = t[:, 2] if dz else 1 - t[:, 2]
                    v += wx * wy * wz * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed + 31 * o)
        total += v * amp
        norm += amp
        amp *= 0.5
    return total / norm


def ss(a, b, x):
    t = np.clip((np.asarray(x, dtype=float) - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------------------------------------ the skin
class Skin:
    """The skinned surface a covering grows on (body, head, hands, feet), as one point set with each point's bone
    weights, so anything built over it can take the weights of the skin under it."""

    def __init__(self, parts):
        objs = [parts["body"], parts["head"], *parts["hands"], *parts.get("feet", [])]
        X, W = [], []
        for o in objs:
            X += [v.co.copy() for v in o.data.vertices]
            W += read_weights(o)
        self.X, self.W = np.array([v[:] for v in X]), W
        self.tree = KDTree(len(X))
        for i, co in enumerate(X):
            self.tree.insert(co, i)
        self.tree.balance()

    def weights(self, points, k=4):
        """Weights for each point: its nearest skin points' weights, blended by inverse distance."""
        out = []
        for co in points:
            near = self.tree.find_n(co, k)
            acc, total = {}, 0.0
            for _, i, d in near:
                f = 1.0 / (d + 1e-4)
                total += f
                for b, x in self.W[i].items():
                    acc[b] = acc.get(b, 0.0) + x * f
            w = {b: x / total for b, x in acc.items() if x / total > 0.02}
            out.append(w)
        return out


def arrays(obj):
    """(verts, triangles, vertex normals) of a mesh object (triangulated copy)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bm.verts.index_update()
    bm.normal_update()
    V = np.array([v.co[:] for v in bm.verts])
    N = np.array([v.normal[:] for v in bm.verts])
    T = np.array([[v.index for v in f.verts] for f in bm.faces], dtype=np.int64).reshape(-1, 3)
    bm.free()
    return V, T, N


def submesh(V, T, keep):
    """The faces whose corners are all kept (``keep`` per vertex), compacted; returns (verts, tris, old indices)."""
    tk = T[keep[T].all(1)]
    used = np.zeros(len(V), dtype=bool)
    used[tk.ravel()] = True
    remap = np.cumsum(used) - 1
    return V[used], remap[tk], np.flatnonzero(used)


def edges_of(T):
    e = np.sort(np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]]), 1)
    return np.unique(e, axis=0)


def neighbour_mean(values, E, n, iters=1):
    """Each vertex's value averaged with its neighbours' (``iters`` times)."""
    v = np.asarray(values, dtype=float)
    for _ in range(iters):
        acc = v.copy()
        cnt = np.ones(n)
        np.add.at(acc, E[:, 0], v[E[:, 1]])
        np.add.at(acc, E[:, 1], v[E[:, 0]])
        np.add.at(cnt, E[:, 0], 1)
        np.add.at(cnt, E[:, 1], 1)
        v = acc / (cnt[:, None] if v.ndim > 1 else cnt)
    return v


def vertex_normals(V, T):
    fn = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    N = np.zeros_like(V)
    for i in range(3):
        np.add.at(N, T[:, i], fn)
    return N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)


def subdivide(V, T, N):
    """Each triangle split in four; new edge points lie on the smooth surface the vertex normals describe (a cubic
    through both ends), so the copy is rounder than the facets it came from. Returns (verts, tris, normals)."""
    E = edges_of(T)
    n = len(V)
    a, b = V[E[:, 0]], V[E[:, 1]]
    na, nb = N[E[:, 0]], N[E[:, 1]]
    d = b - a
    mid = (a + b) / 2 + (np.einsum("ij,ij->i", d, nb)[:, None] * nb - np.einsum("ij,ij->i", d, na)[:, None] * na) / 8
    nm = na + nb
    nm /= np.maximum(np.linalg.norm(nm, axis=1, keepdims=True), 1e-12)
    key = E[:, 0] * n + E[:, 1]
    order = np.argsort(key)
    skey = key[order]

    def mid_index(i, j):
        lo, hi = np.minimum(i, j), np.maximum(i, j)
        return n + order[np.searchsorted(skey, lo * n + hi)]

    m01, m12, m20 = mid_index(T[:, 0], T[:, 1]), mid_index(T[:, 1], T[:, 2]), mid_index(T[:, 2], T[:, 0])
    T2 = np.concatenate([np.stack([T[:, 0], m01, m20], 1), np.stack([m01, T[:, 1], m12], 1),
                         np.stack([m20, m12, T[:, 2]], 1), np.stack([m01, m12, m20], 1)])
    return np.concatenate([V, mid]), T2, np.concatenate([N, nm])


def smooth_normals(V, T, N, iters=6):
    """Normals averaged over neighbours, so offsets along them don't cross over in clefts."""
    E = edges_of(T)
    M = neighbour_mean(N, E, len(V), iters)
    return M / np.maximum(np.linalg.norm(M, axis=1, keepdims=True), 1e-12)


def make_mesh(name, V, T, colors, slot, weights=None, uv=None, alpha=None, normals=None):
    """A mesh object from vertex positions and triangles, coloured per vertex (sRGB), with strand UVs per vertex
    (``uv``), bone weights and (unit) normals per vertex (or per face corner: shape (triangles, 3, 3)) when given."""
    from ..export import simple_material
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(V))
    me.vertices.foreach_set("co", np.asarray(V, dtype=np.float32).ravel())
    me.loops.add(T.size)
    me.loops.foreach_set("vertex_index", T.astype(np.int32).ravel())
    me.polygons.add(len(T))
    me.polygons.foreach_set("loop_start", np.arange(len(T), dtype=np.int32) * 3)
    me.polygons.foreach_set("loop_total", np.full(len(T), 3, dtype=np.int32))
    me.update(calc_edges=True)
    me.validate()
    corner = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", corner)
    layer = me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    rgba = np.ones((len(corner), 4), dtype=np.float32)
    rgba[:, :3] = np.clip(np.asarray(colors)[corner], 0.0, 1.0)
    layer.data.foreach_set("color_srgb", rgba.ravel())
    me.color_attributes.active_color = layer
    if uv is not None:
        uvl = me.uv_layers.new(name="UVMap")
        uvl.data.foreach_set("uv", np.asarray(uv, dtype=np.float32)[corner].ravel())
    me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), dtype=bool))
    if normals is not None:                     # per vertex, or per face corner (3 a triangle)
        n = np.asarray(normals, dtype=np.float32)
        if n.ndim == 3:
            me.normals_split_custom_set(n.reshape(-1, 3).tolist())
        else:
            me.normals_split_custom_set_from_vertices(n.tolist())
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(simple_material(slot))
    if weights is not None:
        write_weights(obj, weights)
    return obj


def strip(obj, covered):
    """Removes the faces of a skin part lying well inside a covering (``covered``: a mask over its vertices)."""
    from .garments import strip_covered
    strip_covered(obj, [np.asarray(covered, dtype=bool)])


def angle(v):
    return math.atan2(v[0], v[1])


def decimate(obj, ratio):
    """Reduces a mesh to ``ratio`` of its faces (bone weights and colours interpolated)."""
    from .blobs import apply_modifiers
    dec = obj.modifiers.new("decimate", "DECIMATE")
    dec.ratio = ratio
    dec.use_collapse_triangulate = True
    apply_modifiers(obj)
