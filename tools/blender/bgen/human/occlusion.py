"""Ambient occlusion baked per vertex into the colour attribute's alpha (1 open .. 0 enclosed), in the rest pose.

The game's character shader darkens sky and block light by it (and direct light a little), so the eye sockets, the
eyes under their lids, the gap under the jaw, the insides of ears and nostrils, the layers of hair and the folds of
clothing read at any distance without textures. Rays are cast over a cosine-weighted hemisphere (a fixed spiral,
turned per vertex to avoid banding); each hit counts by how close it is, then two smoothing passes remove speckle.
"""
import math

from mathutils.bvhtree import BVHTree


def _hemisphere(count):
    golden = math.pi * (3 - math.sqrt(5))
    out = []
    for i in range(count):
        r = math.sqrt((i + 0.5) / count)
        a = i * golden
        out.append((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1 - r * r))))
    return out


def vertex_occlusion(obj, reach, rays=48):
    """Per-vertex openness (0..1) of a mesh in its own space."""
    me = obj.data
    verts = [v.co.copy() for v in me.vertices]
    clear = {i for i, m in enumerate(me.materials) if m is not None and m.name == "char:glass"}   # (light passes it)
    bvh = BVHTree.FromPolygons(verts, [tuple(p.vertices) for p in me.polygons if p.material_index not in clear], epsilon=0.0)
    dirs = _hemisphere(rays)
    eps = reach * 0.004
    open_ = []
    for i, v in enumerate(me.vertices):
        n = v.normal
        if n.length < 0.5:
            open_.append(1.0)
            continue
        t = n.orthogonal().normalized()
        b = n.cross(t)
        spin = (i * 2.399963) % (2 * math.pi)       # turn the spiral per vertex (golden angle steps)
        cs, sn = math.cos(spin), math.sin(spin)
        tt, bb = t * cs + b * sn, b * cs - t * sn
        origin = v.co + n * eps
        hit = 0.0
        for dx, dy, dz in dirs:
            d = tt * dx + bb * dy + n * dz
            loc, _, _, dist = bvh.ray_cast(origin, d, reach)
            if loc is not None:
                hit += 1.0 - (dist / reach) ** 2
        open_.append(1.0 - hit / rays)
    # Two passes of neighbour averaging (edges) to remove sampling noise.
    for _ in range(2):
        total = list(open_)
        count = [1] * len(open_)
        for e in me.edges:
            a, b = e.vertices
            total[a] += open_[b]
            total[b] += open_[a]
            count[a] += 1
            count[b] += 1
        open_ = [t / c for t, c in zip(total, count)]
    return open_


def bake(obj, reach, rays=48, floor=0.0):
    """Writes the occlusion into the alpha of the mesh's "Color" corner attribute (created white if missing)."""
    me = obj.data
    if "Color" not in me.color_attributes:
        me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    layer = me.color_attributes["Color"]
    ao = vertex_occlusion(obj, reach, rays)
    for loop in me.loops:
        c = layer.data[loop.index].color_srgb
        layer.data[loop.index].color_srgb = (c[0], c[1], c[2], max(floor, ao[loop.vertex_index]))
    return ao
