"""Sculpted beards: the mass over the face (beardlocks' layer, combed into clumps with grooves between its strands,
sinking under the skin where the beard thins out, with the moustache rising out of it over the upper lip) and the
hanging part below the jaw (beardbib) melted into it, meshed from their distance field (fieldmesh). UVs run along the
hair (U) for the hair shader's strands and highlights; the colour darkens into the grooves, and where the beard thins out
it fades toward the skin, or up the sideburns toward the head hair.
"""
import hashlib

import numpy as np

from . import fieldmesh, sdf

F = np.float32
FACE = ((-0.095, -0.035, -0.16), (0.095, 0.118, 0.0))      # where the layer over the skin can be
INNER = 0.003         # how far under the skin the layer reaches, so it leaves the skin cleanly where it grows out
HANG_BLEND = 0.004    # how softly the hanging part melts into the face's layer


def _ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


class Sculpt:
    def __init__(self, beard, bib=None):
        """``beard`` the layer over the face (beardlocks.Beard), ``bib`` the hanging part's mass (beardbib), if any."""
        self.b = beard
        self.bib = bib
        if bib is not None:
            self.bib_box = tuple(np.asarray(v, dtype=F) for v in bib.bounds())

    # ------------------------------------------------------------------ the distance field
    def layer(self, X):
        """Distance to the beard's mass over the face (a shell on the skin, not into the ear canals), and to the head,
        for the points near the face (elsewhere 1)."""
        layer, head = np.ones(len(X), dtype=F), np.ones(len(X), dtype=F)
        lo, hi = FACE
        near = np.all((X > np.asarray(lo, dtype=F)) & (X < np.asarray(hi, dtype=F)), axis=1)
        if near.any():
            x = X[near]
            skin = self.b.skin(x)
            base, slope = self.b.base(x, slope=True)
            steep = np.maximum(F(1.7), np.sqrt(1 + slope * slope))       # (its edges, grooves, the moustache)
            layer[near] = np.maximum(skin - base, -(skin + F(INNER))) / steep
            head[near] = self.b.full(x)
        return layer, head

    def hanging(self, X):
        """Distance to the hanging part (beardbib), for the points near it (elsewhere 1)."""
        out = np.ones(len(X), dtype=F)
        if self.bib is not None:
            lo, hi = self.bib_box
            near = np.all((X > lo) & (X < hi), axis=1)
            if near.any():
                out[near] = self.bib.field(X[near])
        return out

    def field(self, X):
        X = np.ascontiguousarray(X, dtype=F)
        layer, head = self.layer(X)
        d = sdf.smin(layer, self.hanging(X), HANG_BLEND)
        return np.maximum(d, -(head + F(INNER + 0.0005)))     # nothing deep in the head (what is inside is dropped)

    # ------------------------------------------------------------------ the mesh
    def build(self, name="beard"):
        b = self.b
        lo, hi = np.asarray(FACE[0], dtype=F), np.asarray(FACE[1], dtype=F)
        parts = [np.ascontiguousarray(v).tobytes() for v in b._path]
        if self.bib is not None:
            lo, hi = np.minimum(lo, self.bib_box[0] - 0.004), np.maximum(hi, self.bib_box[1] + 0.004)
            bib = self.bib
            parts += [np.ascontiguousarray(v).tobytes() for v in (bib.R, bib.lt, bib.ll, bib.lw, bib.gt, bib.gw,
                                                                  bib.gd, bib.g0, bib.g1, bib.gs, bib.gp)]
        long = b.st.length > 0.1
        h, target = (0.0011, 14000) if long else (0.0009, 10000 if self.bib is not None else 8000)
        shape = hashlib.sha1(b"".join(parts)).hexdigest()
        key = ("beard", b.head.shape.key(), b.st, shape)
        sources = ("beardsdf.py", "beardlocks.py", "beardbib.py", "beard.py", "headshape.py", "earshape.py")
        bounds = (tuple(float(v) for v in lo), tuple(float(v) for v in hi))
        return fieldmesh.build(self.field, bounds, h, target, name, key, sources, "beard",
                               min_piece=150, keep=lambda c: b.full(np.ascontiguousarray(c, dtype=F)) > -0.0002,
                               resample=True)

    def finish(self, obj, color, skin, hair=None):
        """UVs along the hair and colours from the clumps and grooves, then the mesh moved from head-local units onto
        the head."""
        import bmesh
        me = obj.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        corners = np.asarray([[v.co[:] for v in f.verts] for f in bm.faces], dtype=F)
        X = np.ascontiguousarray(corners.reshape(-1, 3))
        b = self.b
        # Over the face: combed down the cheeks; the moustache along its own sweep.
        _, clump, strand = b.comb(X)
        uvs = np.stack([-X[:, 2] * 10.0, b.across(X) * 8.0], 1)
        shade = (0.84 + 0.2 * clump) * (1.0 - 0.14 * strand * (0.4 + 0.6 * (1 - clump)))
        h, s, off, grooves, _ = b.whiskers(X)
        whisk = _ss(0.0018, 0.0032, h)
        uvs = uvs + (np.stack([s * 0.5, off * 0.05 + 0.3], 1) - uvs) * (whisk > 0.5)[:, None]
        shade = shade + ((0.8 + 0.22 * (1 - np.abs(off))) * (1 - 0.16 * grooves) - shade) * whisk
        # The hanging part: down its locks, darker in the grooves between them.
        bib = self.hanging(X) + F(0.0003) < self.layer(X)[0]
        if bib.any():
            across, crest = self.bib.across(X[bib])
            uvs[bib] = np.stack([-X[bib, 2] * 10.0, across * 8.0], 1)
            shade[bib] = 0.8 + 0.24 * crest
        col = np.asarray(color, dtype=F)[None] * shade[:, None]
        cov = np.maximum(b.coverage(X), whisk)
        near_skin = ~bib * (1.0 - _ss(0.05, 0.6, cov))           # thinning out: the skin shows through
        col = col + (np.asarray(skin, dtype=F)[None] * 0.92 - col) * (near_skin * 0.75)[:, None]
        if hair is not None:          # up the sideburns into the head hair's colour
            up = _ss(-0.04, -0.016, X[:, 2]) * _ss(0.9, 1.2, np.arctan2(np.abs(X[:, 0]), X[:, 1]))
            col = col + (np.asarray(hair, dtype=F)[None] * shade[:, None] - col) * up[:, None]
        uv = bm.loops.layers.uv.new("UVMap")
        layer = bm.loops.layers.color.new("Color")
        for fi, f in enumerate(bm.faces):
            for j, loop in enumerate(f.loops):
                i = fi * 3 + j
                loop[uv].uv = uvs[i]
                loop[layer] = (*np.minimum(col[i], 1.0), 1.0)
        head = b.head
        for v in bm.verts:
            v.co = head.at(v.co)
        bm.to_mesh(me)
        bm.free()
        for poly in me.polygons:
            poly.use_smooth = True
        return obj
