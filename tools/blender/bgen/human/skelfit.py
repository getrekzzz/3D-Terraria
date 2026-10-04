"""Fitting a skeleton's clothes and bones together (skeldress.py). Bone that the clothes cover deeply is cut away, as
``dress`` strips the skin it covers: it never shows, and in a deep bend (a knee in a sprint, a shoulder in an
overhead swing) the rigid bone would only poke through the cloth that bends around it. The bone that stays, near the
skin the clothes leave bare (neck, wrists), gets the cloth lifted over it wherever it comes too close."""
import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


def world(obj, attr):
    """Vertex positions ("co") or normals ("normal") of ``obj`` in world space, (N, 3)."""
    me = obj.data
    v = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get(attr, v)
    m = np.asarray(obj.matrix_world, dtype=float)
    return v.reshape(-1, 3) @ m[:3, :3].T + (m[:3, 3] if attr == "co" else 0)


def islands(me):
    """Per-vertex label of the connected piece each vertex belongs to."""
    parent = np.arange(len(me.vertices))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in me.edges:
        a, b = root(e.vertices[0]), root(e.vertices[1])
        parent[max(a, b)] = min(a, b)
    return np.array([root(i) for i in range(len(parent))])


def sheet(me):
    """Indices of the faces of a garment's sheet: its large islands, not the bands round its edges (collar, cuffs,
    hems), which are separate pieces and turn their inner faces toward the body."""
    label = islands(me)
    first = np.array([f.vertices[0] for f in me.polygons], dtype=int)
    ids, size = np.unique(label[first], return_counts=True)
    return np.flatnonzero(np.isin(label[first], ids[size >= size.max() * 0.2]))


def sheet_tree(objs):
    """One tree of the sheets of ``objs`` (world space)."""
    verts, polys = [], []
    for o in objs:
        faces = o.data.polygons
        polys += [[len(verts) + v for v in faces[f].vertices] for f in sheet(o.data)]
        verts += world(o, "co").tolist()
    return BVHTree.FromPolygons(verts, polys)


def hide(bones, skin, depth):
    """Deletes the bone faces farther than ``depth`` from all of ``skin`` (the points of the body the clothes leave
    bare: neck, wrists, any gap); returns the pieces that keep faces."""
    kd = KDTree(max(len(skin), 1))
    for i, p in enumerate(skin):
        kd.insert(p, i)
    kd.balance()
    out = []
    for b in bones:
        hidden = [len(skin) == 0 or kd.find(v)[2] > depth for v in world(b, "co")]
        bm = bmesh.new()
        bm.from_mesh(b.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(hidden[v.index] for v in f.verts)], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(b.data)
        bm.free()
        if len(b.data.polygons):
            out.append(b)
        else:
            me = b.data
            bpy.data.objects.remove(b)
            bpy.data.meshes.remove(me)
    return out


def lift(objs, sheets, bones, margin, reach):
    """Hangs the cloth on the bones: wherever a bone comes within ``margin`` of the outside of one of the ``sheets``,
    everything worn there (``objs``) is lifted together over it, so layers keep their order, and the lift eases out
    across ``reach``: the bone shows as a soft ridge under the cloth instead of poking through it."""
    if not bones:
        return
    tips = np.concatenate([world(b, "co") for b in bones])
    ways = np.concatenate([world(b, "normal") for b in bones])
    for _ in range(3):
        tree = sheet_tree(sheets)
        found = {}   # cell -> (lift, where, which way): the cloth surface over bone, how far it must move out
        for v, w in zip(tips, ways):
            loc, n, _f, dist = tree.find_nearest(Vector(v), reach)
            if loc is None:
                continue
            out = (Vector(v) - loc).dot(n)
            # under the cloth's face (not past its edge), facing the same way (not inside a collar or a cuff)
            if out > -margin and abs(out) > 0.8 * dist and Vector(w).dot(n) > 0.25:
                cell = tuple(np.floor(np.asarray(loc) / (0.3 * margin)).astype(int))
                if out + margin > found.get(cell, (0,))[0]:
                    found[cell] = (out + margin, loc, n)
        if not found:
            return
        push = list(found.values())
        kd = KDTree(len(push))
        for i, (_t, loc, _n) in enumerate(push):
            kd.insert(loc, i)
        kd.balance()
        for o in objs:
            co = world(o, "co")
            d = np.zeros_like(co)
            for i, v in enumerate(co):
                best, way = 0.0, Vector()
                for _c, j, dist in kd.find_range(v, reach):
                    t = push[j][0] * (1 - (dist / reach) ** 2) ** 2
                    best, way = max(best, t), way + push[j][2] * t * t
                if best > 0:
                    d[i] = way.normalized() * best
            m = np.linalg.inv(np.asarray(o.matrix_world, dtype=float)[:3, :3])
            local = np.empty(len(o.data.vertices) * 3, dtype=np.float32)
            o.data.vertices.foreach_get("co", local)
            o.data.vertices.foreach_set("co", (local.reshape(-1, 3) + d @ m.T).astype(np.float32).ravel())
            o.data.update()
