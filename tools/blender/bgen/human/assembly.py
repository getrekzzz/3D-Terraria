"""Assembling a character's parts into the mesh it is exported as: one BVH over several parts (what long hair and
beards rest on), the joined mesh (faces of five or more sides triangulated), and the UV maps the game's character
shader reads besides the strands' own (rest_uvs: the rest pose; sway_uvs: how freely loose cloth swings).
"""
import bpy


def tree(objs):
    """One BVH (world space) over several meshes, or None."""
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in objs:
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [tuple(base + i for i in poly.vertices) for poly in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys) if polys else None


def rest_uvs(obj):
    """UVMap (unless the part already has strand UVs) = rest (x, z); UVRest = rest (y, z): the game shader
    rebuilds the rest position from them so surface detail stays attached to the skin and cloth."""
    me = obj.data
    has_strands = "UVMap" in me.uv_layers and len(me.uv_layers) > 0
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    rest = me.uv_layers.get("UVRest") or me.uv_layers.new(name="UVRest")
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        if not has_strands:
            uv.data[loop.index].uv = (co.x, co.z)
        rest.data[loop.index].uv = (co.y, co.z)


def sway_uvs(mesh):
    """The loose cloth's sway weights (skirts.mark_sway, a point attribute the parts brought into the join; 0 on every
    other part) as the mesh's third UV map, UVSway (u = weight), which the game imports as CUSTOM0 for the character
    shader's sway (X-17); a mesh with no loose cloth gets no map."""
    import numpy as np
    me = mesh.data
    attr = me.attributes.get("sway")
    if attr is None:
        return
    w = np.zeros(len(me.vertices), dtype=np.float32)
    attr.data.foreach_get("value", w)
    me.attributes.remove(me.attributes["sway"])
    if not np.any(w > 1e-4):
        return
    idx = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", idx)
    uv = np.zeros((len(me.loops), 2), dtype=np.float32)
    uv[:, 0] = w[idx]
    for name in ("UVMap", "UVRest"):          # (the first two maps keep their places: TEXCOORD_0 and _1)
        if name not in me.uv_layers:
            me.uv_layers.new(name=name)
    me.uv_layers.new(name="UVSway").data.foreach_set("uv", uv.ravel())


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.name = name
    # Faces of five or more sides (button caps, buckle plates) stop the exporter computing tangents for the whole
    # mesh, which the hair highlights need.
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
        bm.to_mesh(obj.data)
    bm.free()
    return obj
