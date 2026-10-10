"""A close crop (locks.Groom, a buzz cut): the head's own mesh lifted a fraction of a millimetre over the scalp,
stippled like cropped hair and thinning into the skin's own colour over a soft hairline (head-local units,
headshape).
"""
import math

import bmesh
import numpy as np

from .hair import coverages, mesh_from

F = np.float32


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def crop(g, head_obj):
    """The crop of groom ``g`` over head mesh ``head_obj``: everywhere hair grows, lifted by the style's layer."""
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.faces.ensure_lookup_table()
    X = np.array([g.head.local(f.calc_center_median()) for f in bm.faces], dtype=F)
    keep = (coverages(X, g.part, g.sideburns, 0.012) >= 0.02) & (X[:, 2] <= g.top(X) + 0.005)
    bmesh.ops.delete(bm, geom=[f for f, k in zip(bm.faces, keep) if not k], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.verts.ensure_lookup_table()
    q = np.array([g.head.local(v.co) for v in bm.verts], dtype=F)
    cov = coverages(q, g.part, g.sideburns, 0.012).astype(F)
    k = _ss(0.05, 0.95, cov)
    grain = (np.sin(q @ np.asarray((12.9898, 78.233, 37.719), dtype=F) * 1000.0) * 43758.5453) % 1.0
    q = g._onto(q, (g.st.cap + 0.00025 * (grain - 0.5)) * k - 0.0008 * (1 - k))
    for v, x in zip(bm.verts, q):
        v.co = g.head.at(x)
    dens = dict(zip((v.index for v in bm.verts), _ss(0.0, 0.85, cov) * (0.86 + 0.14 * grain)))
    shade = dict(zip((v.index for v in bm.verts), 0.8 + 0.2 * grain))
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    for f in bm.faces:
        for loop in f.loops:
            x = g.head.local(loop.vert.co)
            d = np.asarray(x) - g.crown
            loop[uv].uv = (float(np.linalg.norm(d)) * 10.0, math.atan2(d[0], -d[1]) * 0.3)
            skin = loop[col]
            a, s = float(dens[loop.vert.index]), float(shade[loop.vert.index])
            loop[col] = (*(c * s * 0.85 * a + sk * (1 - a) for c, sk in zip(g.color, skin[:3])), 1.0)
    return mesh_from(bm, "hair_cap")
