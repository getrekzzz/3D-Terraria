"""Armour over a body suit, laid out from plates (plates.py) by ``armor`` style:

- cyborg: gunmetal panels on a dark suit: a chest plate, segmented belly plates, a back plate, a belt, shoulder caps,
  upper arm, forearm and shin plates and knee caps, with panel seams and teal light strips (``glow``) set into them,
  and a metal collar round the neck (helmets.collar, when there is no helmet);
- martian: a space trooper's rounded white armour on a grey suit: a deep chest plate, a back plate, broad
  pauldrons, gauntlet cuffs, knee pads and shin guards, accent bands in ``glow``;
- diver: a pressure suit's sleek plates (chest, back, shoulders, forearms, shins) with seams and lit strips;
- carapace: an insect's chitin: overlapping shoulder plates, a segmented chest and back, plates down the forearms
  and shins, glossy and dark with paler edges;
- solar: a Selenian's heavy orange plate: a breastplate with a glowing sun, faulds, layered flared pauldrons, bracers
  and greaves, with flame blades (solarblades.py).

Colours: ``armorColor`` (the plates), ``glow`` (light strips, accent bands), ``trim`` (a second plate colour, or the
colour plates shade to at their edges).
"""
import math

import bmesh
import numpy as np
from mathutils import Vector

from . import plates as P
from .garments import mesh_from, set_slot
from .paint import _ensure_layer, hex3
from .sdf import F

SOURCES = ("armor.py", "armorparts.py", "armorsets.py")


class Smith:
    """Makes a character's plates: meshes them (cached by the frame and style), keeps them off the clothes, binds
    them, colours them and sets light strips into them."""

    def __init__(self, fr, p, fitter, style):
        self.fr, self.p, self.fitter, self.style = fr, p, fitter, style
        s = self.s = fr.s
        self.key = (style, round(fr.height, 4), round(fr.build, 4), round(fr.weight, 4), fr.female, fr.old)
        self.torso, self.torso_key = fitter.fit_torso(fr)
        self.color = hex3(p.get("armorColor", "#8a96a4"))
        self.trim = hex3(p.get("trim", p.get("armorColor", "#8a96a4")))
        self.glow = hex3(p.get("glow", "#3affc0"))
        self.out, self.lines = [], []
        j = fr.j
        m, f = 0.82 + 0.36 * fr.build, 0.84 + 0.4 * fr.weight
        bulk, fat = max(0.0, fr.build - 0.5) * 2.0, max(0.0, fr.weight - 0.45) / 0.55
        arm = 0.82 if fr.female else 1.0
        self.limbs = {}
        for side, sx in (("R", 1), ("L", -1)):
            sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
            up, fw = (el - sh).normalized(), (wr - el).normalized()
            dl = m * arm * (1 + 0.14 * bulk + 0.08 * fat)
            hip, kn, an = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"]
            self.limbs[side] = {
                "deltoid": (sh + up * 0.03 * s + Vector((sx * 0.006 * s, 0, 0)), 0.058 * dl * s + 0.01 * s),
                "fore": (el + fw * 0.02 * s, wr - fw * 0.012 * s,
                         0.039 * s * arm * m ** 0.5 * (1 + 0.14 * bulk + 0.15 * fat) + 0.009 * s, 0.025 * s * arm + 0.008 * s),
                "upper": (sh + up * 0.06 * s, el, 0.04 * s * arm * f ** 0.4 * (1 + 0.12 * bulk + 0.3 * fat) + 0.016 * s, 0.033 * s * arm + 0.01 * s),
                "thigh": (hip + Vector((sx * 0.01 * s, 0.01 * s, -0.04 * s)), kn, 0.08 * s * f ** 0.6 * (1 + 0.06 * bulk + 0.2 * fat) + 0.012 * s,
                          0.05 * s + 0.01 * s),
                "shin": (kn + Vector((0, 0.008 * s, -0.01 * s)), an + Vector((0, 0, 0.02 * s)), 0.049 * s * (1 + 0.06 * bulk + 0.08 * fat) + 0.008 * s,
                         0.031 * s + 0.008 * s),
                "knee": (kn + Vector((0, 0.03 * s, 0.004 * s)), 0.05 * s + 0.004 * s),
                "lateral": Vector((sx * math.cos(math.radians(40)), 0.3, math.sin(math.radians(40)))),
            }

    # ------------------------------------------------------------------ making one plate
    def make(self, name, core, field, bounds, color=None, slot="char:metal", mirror=True, gap=0.0035, stiff=0.6, keep=None,
             target=None, h=0.001, shade=None, key=()):
        """Meshes, fits and binds a plate (and its mirror image on the left); returns the objects. ``key``: whatever
        else its shape depends on (radii fitted to the body). Triangles by the plate's size unless ``target``."""
        key = tuple(k if isinstance(k, str) else round(float(k), 4) for k in key)
        if target is None:             # (about 900 for a knee cap to 2000 for a breastplate)
            size = sorted(float(b - a) for a, b in zip(*bounds))
            target = int(np.clip(900 + 9000 * size[1] * size[2], 900, 2100))
        obj = P.mesh(field, bounds, name, (self.key, name) + key, SOURCES, h=h, target=target)
        objs = [(obj, core)]
        if mirror:
            objs.append((P.mirror(obj, name + "_L"), lambda q, core=core: core(q * np.asarray((-1, 1, 1), dtype=F))))
        base = color or self.color
        for o, c in objs:
            moved = self.fitter.push(o, c, gap)
            print(f"plate {o.name}: {len(o.data.polygons)} tris, pushed out up to {moved * 1000:.1f} mm")
            flip = shade is not None and o is not obj
            self._paint(o, c, base, (lambda q: shade(q * np.asarray((-1, 1, 1), dtype=F))) if flip else shade)
            set_slot(o, slot)
            self.fitter.bind(o, stiff, keep)
            self.out.append(o)
        return [o for o, _ in objs]

    def _paint(self, obj, core, base, shade):
        """The plate's colour, a little lighter toward the top and darker on its underside facing the body."""
        from .sdf import gradient
        me = obj.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        g = gradient(core, co, 0.001)
        g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
        n = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertex_normals.foreach_get("vector", n)
        under = np.clip(-np.einsum("ij,ij->i", g, n.reshape(-1, 3)), 0, 1)
        k = (0.94 + 0.08 * np.clip(g[:, 2], -1, 1)) * (1 - 0.35 * under)
        cols = np.asarray(shade(co) if shade is not None else [base] * len(co), dtype=float)
        tone = np.minimum(cols * k[:, None], 1.0).tolist()
        layer = _ensure_layer(me)
        for loop in me.loops:
            layer.data[loop.index].color_srgb = (*tone[loop.vertex_index], 1.0)

    def line(self, plate, pts, width=0.0042, height=0.0021, color=None):
        """A light strip along points near a plate's face (moved onto its outer face as it was finally fitted)."""
        from mathutils.bvhtree import BVHTree
        import bpy
        tree = BVHTree.FromObject(plate, bpy.context.evaluated_depsgraph_get())
        hits = []
        for p in pts:
            p = Vector(p)
            loc, n, _, _ = tree.find_nearest(p)
            if loc is None:
                continue
            hit = tree.ray_cast(loc + n * 0.02, -n, 0.04)
            hits.append(hit[0] if hit[0] is not None else loc)
        if len(hits) < 2:
            return
        self.lines.append((plate, np.asarray(hits, dtype=F), width, height, color or self.glow))

    def finish_lines(self):
        """All light strips as one glowing object, each strip bound like the plate it lies on."""
        if not self.lines:
            return []
        from mathutils.kdtree import KDTree
        from .rig import read_weights, write_weights
        bm = bmesh.new()
        layer = bm.loops.layers.color.new("Color")
        owner = []
        for plate, pts, width, height, color in self.lines:
            core = _surface_field(plate)
            verts = P.strip(bm, core, pts, width, height, sink=height * 0.35)
            for v, _, _ in verts:
                for loop in v.link_loops:
                    loop[layer] = (*color, 1.0)
            owner += [(v, plate) for v, _, _ in verts]
        bm.verts.index_update()
        index = {v.index: plate for v, plate in owner}
        obj = mesh_from(bm, "glow_lines")
        set_slot(obj, "char:glow")
        trees = {}
        out = []
        for v in obj.data.vertices:
            plate = index[v.index]
            if plate.name not in trees:
                kd = KDTree(len(plate.data.vertices))
                for i, pv in enumerate(plate.data.vertices):
                    kd.insert(pv.co, i)
                kd.balance()
                trees[plate.name] = (kd, read_weights(plate))
            kd, w = trees[plate.name]
            out.append(dict(w[kd.find(v.co)[1]]))
        write_weights(obj, out)
        return [obj]


def _surface_field(obj):
    """An approximate outward field near a mesh (distance along its nearest face's normal), for a strip's frame."""
    from mathutils.bvhtree import BVHTree
    import bpy
    tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())

    def field(p):
        out = np.empty(len(p), dtype=F)
        for i, q in enumerate(p):
            loc, n, _, _ = tree.find_nearest(Vector(q))
            out[i] = (Vector(q) - loc).dot(n) if loc is not None else 1.0
        return out
    return field


def build(style, fr, p, fitter):
    """The plates (and their light strips) of an armour ``style`` for this character (none for an unknown style:
    ``armor`` is a colour on an ``anatomy: armor`` body, matter.py)."""
    from . import armorsets
    if style not in ("cyborg", "martian", "diver", "carapace", "solar"):
        return []
    sm = Smith(fr, p, fitter, style)
    getattr(armorsets, style)(sm)
    return sm.out + sm.finish_lines()
