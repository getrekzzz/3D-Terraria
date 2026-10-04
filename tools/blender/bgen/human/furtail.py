"""A full, bushy brush of a tail (``tailShape: brush``), grown like the fur coat (fur.py): a tapering core from the
tailbone (where it leaves the body or the trousers over it), swept out and down behind the legs, wrapped in an
undercoat and long tufts that all flow toward the tip, longest round the middle, so the brush swells and closes to a
point; it darkens toward the tip. One object, carried by the hips."""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import tufts
from .coverings import make_mesh, smooth_normals, ss
from .features import round_section, spline, sweep
from .paint import hex3

PATH = [(0, 0.04, 0.02), (0, -0.04, -0.02), (0, -0.12, -0.09), (0.01, -0.2, -0.2), (0.022, -0.24, -0.34),
        (0.03, -0.235, -0.48), (0.03, -0.215, -0.56)]


def brush(fr, body, p, garments=()):
    from .tails import _exit
    s = fr.s
    base = np.asarray(hex3(p["tail"]))
    start = _exit(fr, body, garments)
    path = spline([start + Vector(c) * s for c in PATH], 44)
    length = sum((b - a).length for a, b in zip(path, path[1:]))
    bm = bmesh.new()
    radius = lambda t: s * (0.012 + 0.034 * math.sin(math.pi * min(t / 0.62, 1.0) ** 0.7 * 0.5) * (1 - t) ** 0.5 + 0.008 * (1 - t))
    info = sweep(bm, path, round_section(radius), 12, up=Vector((0, 0, 1)))
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.verts.index_update()
    tv = {v.index: (t, a) for v, t, a in info}
    V = np.array([v.co[:] for v in bm.verts])
    T = np.array([[v.index for v in f.verts] for f in bm.faces], dtype=np.int64)
    bm.free()
    t = np.array([tv[i][0] for i in range(len(V))])
    a = np.array([tv[i][1] for i in range(len(V))])
    from .coverings import vertex_normals
    N = smooth_normals(V, T, vertex_normals(V, T), 2)
    P = np.array([q[:] for q in path])
    tang = np.gradient(P, axis=0)
    tang /= np.linalg.norm(tang, axis=1, keepdims=True)
    flow = tang[np.clip(np.round(t * (len(P) - 1)).astype(int), 0, len(P) - 1)]
    L = s * (0.06 + 0.09 * np.sin(np.pi * np.clip(t, 0, 1)) ** 0.8) * (1 - ss(0.9, 1.0, t) * 0.5)
    L *= ss(0.0, 0.1, t)
    tip = base * 0.42
    k = ss(0.55, 0.9, t)[:, None]
    tone = base[None, :] * (1 - k) + tip[None, :] * k
    h = np.full(len(V), 0.012 * s) * (0.4 + 0.6 * ss(0.0, 0.15, t))
    W = [{"hips": 1.0}] * len(V)
    core_rgb = tone * 0.7
    uv = np.stack([t * length * 6.0, a / math.pi * 0.3], 1)
    fur = tufts.arrays(V, T, N, W, flow, L, h, tone, 0.14, seed=4242, min_len=0.02 * s)
    verts, tris, rgb, uvs, weights = V + N * h[:, None], T, core_rgb, uv, list(W)
    if fur is not None:
        fv, ft, fc, fu, fw = fur
        tris = np.concatenate([tris, ft + len(verts)])
        verts = np.concatenate([verts, fv])
        rgb = np.concatenate([rgb, fc])
        uvs = np.concatenate([uvs, fu])
        weights += fw
    return make_mesh("tail", verts, tris, rgb, "char:hair", weights, uvs)
