"""Clothing shaped from the body itself: a garment is the body's surface over a region (shirt, trousers, boots,
coat, a vest (cut as a sleeveless coat), a robe's bodice), tailored into cloth (tailoring.py, robes.py: ease, draping
across hollows, folds), torn on the undead (tatters.py) and finished (finishing.py: seams, collars, cuffs, hems, boot
rims and soles, buttons), skinned with the body's own weights. Skin that ends up under clothing is removed so nothing
pokes through when limbs bend (but kept under tears). Skirts (coats, robes; skirts.py) are lofted from under the belt
or sash, which ties it together; their loose cloth is marked for the game's sway (skirts.mark_sway).
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

from . import finishing
from .head import smooth
from .paint import hex3
from .robes import RobeCloth
from .skirts import Skirts
from .tailoring import Cloth


class Wardrobe(Skirts):
    def __init__(self, frame, p, head=None):
        self.f = frame
        self.p = p
        self.head = head
        j, s = frame.j, frame.s
        self.waist = j["hip.R"].z + (0.125 if p.get("robe") else 0.1) * s     # (a robe is girt at the natural waist)
        self.boot_top = j["ankle.R"].z + (0.16 if "boots" in p else 0.05) * s
        self.cuff = p.get("sleeves", "long")
        self.tatters = None   # where the clothes are torn (tatters.Tatters, the undead)
        self.collar_z = frame.j["neck"].z
        self.belted = not p.get("robe")                    # a belt at the waist (over the coat if there is one)
        self.suit = bool(p.get("suit"))                    # shirt and trousers are the creature's own body
        self.girded = bool(p.get("robe") or "coat" in p or "shirt" in p) and not self.suit    # (see ``girth``)
        self.hidden = []                # tests for skin hidden under what isn't shaped from it (a long skirt)
        self.tucked = self.belted and "coat" not in p and not self.suit   # the shirt bloused over the belt
        # A coat (or a vest) worn open (the default, ``lapels``): a V down to the chest with lapels, showing the shirt
        # under it.
        self.lapels = ("coat" in p or "vest" in p) and p.get("lapels", True) and not self.suit
        self.front = None               # the open front's V (lapels.Front), once the collar is fitted
        self.sole = 0.022 * s           # top of the boot soles
        self.standoff = {}              # each garment's height off the skin per vertex (a coat sits on the shirt)
        self.extras = []                # parts the garments brought along (buttons)

    # ------------------------------------------------------------------ regions (on rest-pose body positions)
    def arm_t(self, co, side):
        """How far along the arm (0 shoulder .. 1 wrist) a point lies, or None if it isn't on that arm."""
        j, s = self.f.j, self.f.s
        sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
        ab = wr - sh
        t = (co - sh).dot(ab) / ab.length_squared
        near = (co - (sh + ab * max(0.0, min(1.0, t)))).length
        sx = 1 if side == "R" else -1
        return t if co.x * sx > abs(sh.x) * 0.7 and t > -0.05 and near < 0.085 * s else None

    def neck_radial(self, co):
        n = self.f.j["neck"]
        return ((co.x - n.x) ** 2 + (co.y - n.y) ** 2) ** 0.5

    def fit_collar(self, body):
        """The collar height: just above the highest point of the shoulder girdle around the neck, so one level cut
        crosses the (vertical) neck cleanly and everything outside the neck lies below it."""
        s = self.f.s
        top = self.f.j["neck"].z + 0.05 * s
        ring = [v.co.z for v in body.data.vertices
                if 0.088 * s < self.neck_radial(v.co) < 0.14 * s and v.co.z < top and not self._on_arm(v.co)]
        self.collar_z = max(ring, default=self.f.j["neck"].z) + 0.004 * s
        if self.lapels:
            from .lapels import Front
            self.front = Front(self)

    def sleeve_limit(self):
        if isinstance(self.cuff, (int, float)):     # a sleeve ending that far down the arm (0..1)
            return float(self.cuff)
        return 0.97 if self.cuff == "long" else 0.38

    def in_shirt(self, co):
        j, s = self.f.j, self.f.s
        for side in ("R", "L"):
            t = self.arm_t(co, side)
            if t is not None and t > 0.05:
                return t < self.sleeve_limit()
        if co.z < self.waist - 0.07 * s:
            return False
        return co.z < self.collar_z

    def in_robe(self, co):
        """A robe's bodice: the shirt's region down to under the sash, where the skirt takes over."""
        return self.in_shirt(co) and (self._on_arm(co) or co.z > self.waist - 0.035 * self.f.s)

    def in_coat(self, co):
        """The shirt's region down to just under the belt, where the coat's tails take over."""
        return self.in_shirt(co) and (self._on_arm(co) or co.z > self.waist - 0.015 * self.f.s)

    def in_vest(self, co):
        """A sleeveless vest: the coat's region off the arms (the shirt shows as sleeves and collar)."""
        return self.in_coat(co) and not self._on_arm(co)

    def in_pants(self, co):
        return self.boot_top - 0.01 * self.f.s < co.z < self.waist + 0.004 * self.f.s and not self._on_arm(co)

    def in_boots(self, co):
        return co.z < self.boot_top + 0.01 * self.f.s

    def _on_arm(self, co):
        return any((t := self.arm_t(co, side)) is not None and t > 0.05 for side in ("R", "L"))

    # ------------------------------------------------------------------ clean edges: planes cut before selection
    def shirt_cuts(self, bottom=-0.07):
        j, s = self.f.j, self.f.s
        up = Vector((0, 0, 1))
        cuts = [(Vector((0, 0, self.waist + bottom * s)), up, lambda c: not self._on_arm(c))]
        cuts.append((Vector((0, 0, self.collar_z)), up, lambda c: self.neck_radial(c) < 0.14 * s))
        for side in ("R", "L"):
            sh, wr = j[f"shoulder.{side}"], j[f"wrist.{side}"]
            cuts.append((sh + (wr - sh) * self.sleeve_limit(), (wr - sh).normalized(), lambda c, side=side: self.arm_t(c, side) is not None))
        return cuts

    def pants_cuts(self):
        s, up = self.f.s, Vector((0, 0, 1))
        knee = self.f.j["knee.R"].z
        return [(Vector((0, 0, self.waist + 0.004 * s)), up, lambda c: not self._on_arm(c)),   # (well under the belt)
                (Vector((0, 0, self.boot_top - 0.01 * s)), up, lambda c: c.z < knee)]

    def boot_cuts(self):
        return [(Vector((0, 0, self.boot_top + 0.01 * self.f.s)), Vector((0, 0, 1)), lambda c: True)]

    def shell(self, body, test, name, thickness, color, material, kind, cuts=()):
        """A garment (``kind``: shirt, coat, pants, boots or robe, a robe's bodice) over the body faces whose centres
        pass ``test``, after the body copy is cut cleanly along ``cuts`` (plane point, plane normal, face filter);
        returns the object (or None)."""
        if kind == "coat" and self.front is not None:       # open down to the chest
            test = (lambda base: lambda co: base(co) and not self.front.inside(co))(test)
            cuts = list(cuts) + self.front.cuts()
        bm = bmesh.new()
        bm.from_mesh(body.data)
        bm.verts.layers.deform.verify()
        for point, normal, filt in cuts:
            faces = [f for f in bm.faces if filt(f.calc_center_median())]
            geom = list({e for f in faces for e in f.edges}) + list({v for f in faces for v in f.verts}) + faces
            bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=point, plane_no=normal)
        bm.normal_update()
        drop = [f for f in bm.faces if not test(f.calc_center_median())]
        bmesh.ops.delete(bm, geom=drop, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        if not bm.faces:
            bm.free()
            return None
        cloth = RobeCloth(self, thickness) if kind == "robe" else Cloth(self, kind, thickness)
        kind = cloth.kind
        shade = cloth.apply(bm)
        if kind != "boots":
            self._loosen(bm, body, kind == "shirt" and self.tucked)
        if self.tatters is not None:
            shade = self.tatters.tear(bm, kind, self.standoff[kind][0], shade)
        keep = [cloth.keep(bm)] if isinstance(cloth, RobeCloth) else []
        if self.tatters is not None:
            keep.append(self.tatters.keep(len(bm.verts)))
        colorize(bm, color, shade)
        obj = mesh_from(bm, name)
        for g in body.vertex_groups:            # the copied faces keep the body's weights (same group order)
            obj.vertex_groups.new(name=g.name)
        keep = [k for k in keep if k is not None]
        if keep:                                # how much each part may be reduced (see finishing.reduce)
            g = obj.vertex_groups.new(name="_keep")
            w = np.round(np.minimum.reduce(keep), 2)
            for level in np.unique(w):
                g.add(np.flatnonzero(w == level).tolist(), float(level), "REPLACE")
        set_slot(obj, material)
        self.extras += finishing.finish(self, obj, kind, color)
        return obj

    def _loosen(self, bm, body, tail):
        """The garment girded about the waist like the belt or sash over it (``girdle``); and a shirt's ``tail``
        hangs from the hips: most of the thigh's pull the skin under it has goes to the hips, so it doesn't swing
        with each stride and kink at the hem (just enough left to clear the trousers under it)."""
        names = [g.name for g in body.vertex_groups]
        hips = names.index("hips") if "hips" in names else None
        legs = {i for i, n in enumerate(names) if n.startswith("thigh.")}
        if hips is None:
            return
        s, dl = self.f.s, bm.verts.layers.deform.verify()
        for v in bm.verts:
            k = 0.6 * min(max((self.waist - 0.02 * s - v.co.z) / (0.05 * s), 0.0), 1.0) if tail else 0.0
            k = 1 - (1 - k) * (1 - self.girth(v.co.z))
            for i in [i for i in v[dl].keys() if i in legs] if k > 0 else ():
                v[dl][hips] = v[dl].get(hips, 0.0) + v[dl][i] * k
                v[dl][i] *= 1 - k

    def girth(self, z):
        """How far something worn at height ``z`` is girded: 1 in a band about the waist (under and round the belt
        or sash), fading out above and below. Whatever is girded (garments, belt, sash, a skirt's top) gives the
        thighs' share of its weights to the hips, so all of it moves alike and a stride can't push one through
        another; the skin under it is gone. Nothing is girded where skin can show at the waist (a bare torso, torn
        clothes): there it all keeps the body's weights."""
        s, w = self.f.s, self.waist
        if not self.girded or self.tatters is not None:
            return 0.0
        return smooth(w - 0.075 * s, w - 0.035 * s, z) * smooth(w + 0.065 * s, w + 0.025 * s, z)

    def waist_weights(self, body):
        """co -> weights for something worn round the waist at co (a belt, a skirt's top): the body's under it (its
        nearest skin off the arms), girded (``girth``)."""
        from mathutils.kdtree import KDTree
        from .rig import read_weights
        bw = read_weights(body)
        arms = ("clavicle", "upper_arm", "forearm", "hand")
        keep = [i for i, w in enumerate(bw) if sum(x for k, x in w.items() if k.startswith(arms)) < 0.1]
        tree = KDTree(len(keep))
        for i in keep:
            tree.insert(body.data.vertices[i].co, i)
        tree.balance()

        def at(co):
            out = {}
            for _, i, d in tree.find_n(co, 6):
                for b, x in bw[i].items():
                    out[b] = out.get(b, 0.0) + x / (d + 0.004 * self.f.s)
            g, legs = self.girth(co.z), sum(x for b, x in out.items() if b.startswith("thigh."))
            out = {b: x * (1 - g) if b.startswith("thigh.") else x for b, x in out.items()}
            out["hips"] = out.get("hips", 0.0) + g * legs
            total = sum(out.values()) or 1.0
            return {b: x / total for b, x in out.items() if x > 1e-3 * total}
        return at

    def belt(self, body, color, buckle="#c0a060", over=()):
        """A leather band round the waist (following the body's girth and lying over the garments ``over``) with
        rounded, stitched edges, a buckle frame and prong in front, and the strap's tongue running on past it."""
        from .rig import write_weights
        s = self.f.s
        z = self.waist - 0.005 * s
        segs = 64
        radius = self.radial([body], z, 0.017 * s, segs) + 0.011 * s
        if over:
            radius = np.maximum(radius, self.radial(over, z, 0.017 * s, segs) + 0.004 * s)
        radius = taut(radius)           # (pulled tight it spans the hollow of the spine instead of dipping into it)
        # Rows across the band: (height, out, shade); the dark rows are the stitching along each edge.
        rows = ((-0.017, -0.0015, 0.8), (-0.0155, 0.0, 1.0), (-0.0125, 0.0003, 0.62), (-0.011, 0.0003, 1.0), (0.011, 0.0003, 0.62),
                (0.0125, 0.0003, 1.0), (0.0155, 0.0, 0.8), (0.017, -0.0015, 0.8))
        band = self._strap(rows, [(2 * math.pi * k / segs, radius[k]) for k in range(segs)], z, color, "belt", closed=True)
        # The tongue: the strap's end passing through the buckle and on round the right side, with a pointed tip.
        r0 = radius[0]
        tip = [(a, r0 + 0.0035 * s) for a in [i * 0.012 * s / r0 for i in range(-1, 7)]]
        tongue = self._strap(((-0.0145, 0.0, 0.85), (-0.012, 0.0005, 1.0), (0.012, 0.0005, 0.85), (0.0145, 0.0, 0.85)), tip, z,
                             tuple(c * 0.95 for c in hex3(color)), "belt_tongue", closed=False, point=True)
        front = r0 + self.f.j["spine"].y
        bm = bmesh.new()
        y0, y1 = front + 0.001 * s, front + 0.0055 * s
        ring = lambda w, h, y: [bm.verts.new((x * w * s, y, z + zz * h * s)) for x, zz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        of, inf, ob, ib = ring(0.026, 0.022, y1), ring(0.017, 0.014, y1), ring(0.026, 0.022, y0), ring(0.017, 0.014, y0)
        for k in range(4):
            n = (k + 1) % 4
            for quad in ((of[k], of[n], inf[n], inf[k]), (ob[n], ob[k], ib[k], ib[n]), (ob[k], ob[n], of[n], of[k]), (inf[k], inf[n], ib[n], ib[k])):
                bm.faces.new(quad)
        res = bmesh.ops.create_cube(bm, size=1.0)       # the prong, from the frame's left side to the middle
        bmesh.ops.scale(bm, vec=(0.02 * s, 0.003 * s, 0.003 * s), verts=res["verts"])
        bmesh.ops.translate(bm, vec=(-0.008 * s, front + 0.005 * s, z), verts=res["verts"])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        colorize(bm, buckle)
        clasp = mesh_from(bm, "buckle")
        set_slot(clasp, "char:metal")
        at = self.waist_weights(body)       # skinned like the waist under it; the buckle rigid, as at its middle
        for o in (band, tongue):
            write_weights(o, [at(v.co) for v in o.data.vertices])
        mid = sum((v.co for v in clasp.data.vertices), Vector()) / len(clasp.data.vertices)
        write_weights(clasp, [at(mid)] * len(clasp.data.vertices))
        return [band, tongue, clasp]

    def _strap(self, rows, path, z, color, name, closed, point=False, slot="char:leather"):
        """A leather strip following ``path`` [(angle round the waist, radius from the body's axis)], ``rows`` across
        it: (height, out, shade of the strip from this row to the next)."""
        s, cy = self.f.s, self.f.j["spine"].y
        base = hex3(color) if isinstance(color, str) else color
        bm = bmesh.new()
        grid = []
        for i, (a, r) in enumerate(path):
            taper = 0.35 if point and i == len(path) - 1 else 1.0
            d = Vector((math.sin(a), math.cos(a), 0))
            grid.append([bm.verts.new((d.x * (r + out * s), cy + d.y * (r + out * s), z + h * s * taper)) for h, out, _ in rows])
        layer = bm.loops.layers.color.new("Color")
        n = len(path)
        for i in range(n if closed else n - 1):
            a, b = grid[i], grid[(i + 1) % n]
            for m in range(len(rows) - 1):
                f = bm.faces.new((a[m], b[m], b[m + 1], a[m + 1]))
                k = rows[m][2]          # each row's shade colours the strip above it
                for loop in f.loops:
                    loop[layer] = (base[0] * k, base[1] * k, base[2] * k, 1.0)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        obj = mesh_from(bm, name)
        set_slot(obj, slot)
        return obj


def taut(radius):
    """A closed band's radii round the waist (``radius[k]`` at angle 2*pi*k/n, 0 in front) pulled taut: the convex
    hull of the outline they make."""
    from .envelope import exit_distance, hull
    a = 2 * np.pi * np.arange(len(radius)) / len(radius)
    d = np.stack([np.sin(a), np.cos(a)], 1)
    ring = d * radius[:, None]
    return exit_distance(np.zeros_like(ring), d, ring[hull(ring)])


def strip_covered(body, tests):
    """Deletes body faces lying well inside clothing (every vertex covered, two rings in from the garment's edge).
    A test is a function of a rest position, or a mask over the body's vertices (a torn garment's, see tatters)."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    inner = {v.index for v in bm.verts if any(t[v.index] if isinstance(t, np.ndarray) else t(v.co) for t in tests)}
    for _ in range(2):
        inner = {v.index for v in bm.verts if v.index in inner and all(n.index in inner for e in v.link_edges for n in e.verts)}
    drop = [f for f in bm.faces if all(v.index in inner for v in f.verts)]
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(body.data)
    bm.free()


def colorize(bm, color, shade=None):
    """Every face corner in ``color``, darkened by ``shade`` (per vertex index: a factor, or one per channel) when
    given."""
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    n = len(bm.verts)
    c = np.asarray(hex3(color) if isinstance(color, str) else color, dtype=float)[:3]
    k = np.ones((n, 1)) if shade is None else np.asarray(shade, dtype=float).reshape(n, -1)
    rgb = (k * c).tolist()
    bm.verts.index_update()
    for f in bm.faces:
        for loop in f.loops:
            r, g, b = rgb[loop.vert.index]
            loop[layer] = (r, g, b, 1.0)


def mesh_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def set_slot(obj, slot):
    from ..export import simple_material
    obj.data.materials.clear()
    obj.data.materials.append(simple_material(slot))
    for poly in obj.data.polygons:
        poly.material_index = 0
