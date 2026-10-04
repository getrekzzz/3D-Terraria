"""Distance fields to game meshes: the field is sampled on a narrow band, meshed with surface nets, snapped onto the
surface, reduced to a triangle budget (keeping detail where the surface bends) and evened out. Results are kept on
disk (tools/blender/_cache, not committed) under a name made from the caller's key and the source of the modules
that define the field, so any change to them meshes it afresh, and in memory for characters sharing a shape.
"""
import hashlib
import os
import pathlib

import numpy as np

from . import sdf

HERE = pathlib.Path(__file__).parent
_MESHES = {}


def _beautify(me, flat=0.2):
    """Flips edges between nearly coplanar triangles where that makes them closer to equilateral."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(me)
    edges = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(1.0) < flat]
    bmesh.ops.beautify_fill(bm, faces=bm.faces[:], edges=edges, method="ANGLE")
    bm.to_mesh(me)
    bm.free()


def cache_file(kind, key, sources):
    src = b"".join((HERE / name).read_bytes() for name in sorted(set(sources) | {"sdf.py", "fieldmesh.py"}))
    digest = hashlib.sha1(repr(key).encode() + src).hexdigest()[:20]
    return HERE.parents[1] / "_cache" / kind / f"{digest}.npz"


def _arrays(me):
    verts = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", verts)
    faces = np.empty(len(me.polygons) * 3, dtype=np.int32)
    me.polygons.foreach_get("vertices", faces)
    return verts.reshape(-1, 3), faces.reshape(-1, 3)


def _compact(verts, faces):
    used = np.zeros(len(verts), dtype=bool)
    used[faces.ravel()] = True
    remap = np.cumsum(used) - 1
    return verts[used], remap[faces]


def grid_field(f, lo, h):
    """A field read back from its samples by trilinear interpolation: cheap to evaluate again and again while the
    mesh is snapped and evened out (exact enough within the sampled band)."""
    lo = np.asarray(lo, dtype=np.float32)
    top = np.asarray(f.shape) - 2

    def field(p):
        g = (p - lo) / np.float32(h)
        i = np.clip(np.floor(g).astype(np.int64), 0, top)
        t = np.clip(g - i, 0.0, 1.0).astype(np.float32)
        out = np.zeros(len(p), dtype=np.float32)
        for dx in (0, 1):
            wx = t[:, 0] if dx else 1 - t[:, 0]
            for dy in (0, 1):
                wy = t[:, 1] if dy else 1 - t[:, 1]
                for dz in (0, 1):
                    wz = t[:, 2] if dz else 1 - t[:, 2]
                    out += wx * wy * wz * f[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
        return out
    return field


def build(field, bounds, h, target, name, key, sources, kind, min_piece=None, keep=None, resample=False):
    """The zero set of ``field`` within ``bounds`` as a triangle mesh object of about ``target`` triangles (in the
    field's own coordinates). Only the largest connected piece is kept, or with ``min_piece`` every piece of at least
    that many faces; ``keep(face_centres) -> mask`` drops faces after meshing (hidden parts). ``resample`` snaps and
    evens out the mesh on the sampled grid instead of the field itself (for fields that are slow to evaluate)."""
    key = (key, h, target, bounds, min_piece)
    path = cache_file(kind, key, sources)
    if key not in _MESHES and path.exists():
        with np.load(path) as data:
            _MESHES[key] = (data["verts"], data["faces"])
    if key in _MESHES:
        return sdf.to_mesh(name, *_MESHES[key])
    f, lo = sdf.sample(field, bounds[0], bounds[1], h)
    verts, quads = sdf.surface_nets(f, lo, h)
    if resample:
        field = grid_field(f, lo, h)
    del f
    verts, quads = sdf.largest_piece(verts, quads) if min_piece is None else sdf.pieces(verts, quads, min_piece)
    verts = sdf.project(field, verts, h * 0.5, steps=1)
    if keep is not None:
        verts, quads = _compact(verts, quads[keep(verts[quads].mean(1))])
    obj = sdf.to_mesh(name, verts, quads)
    tris = len(quads) * 2
    if tris > target:
        dec = obj.modifiers.new("decimate", "DECIMATE")
        dec.ratio = target / tris
        dec.use_collapse_triangulate = True
        from .blobs import apply_modifiers
        apply_modifiers(obj)
    me = obj.data
    if any(len(poly.vertices) != 3 for poly in me.polygons):
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
    verts, faces = _arrays(me)
    for _ in range(3):        # even out the triangles: flip edges toward better shapes, then relax the vertices
        verts = sdf.relax(field, verts, faces, h * 0.5, iters=3)
        me.vertices.foreach_set("co", verts.ravel())
        me.update()
        _beautify(me)
        verts, faces = _arrays(me)
    if me.validate():          # (decimating can leave duplicate faces; drop them so the cache and this mesh agree)
        verts, faces = _arrays(me)
    _MESHES[key] = (verts, faces)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f"{path.stem}.{os.getpid()}.npz")
    np.savez(tmp, verts=verts, faces=faces)
    os.replace(tmp, path)
    for poly in me.polygons:
        poly.use_smooth = True
    return obj
