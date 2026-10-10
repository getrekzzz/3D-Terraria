"""Hats, a face mask, an eye patch and creature features, fitted to the generated head and body and bound to the
bones that carry them. Hats: sculpted cloth caps and hoods (headwear, capshapes), wide hats (hats), and wreaths,
circlets and perched caps that rest on the finished hair (crowns); creature features: horns (features), wings
(wings), tails (tails)."""
import bmesh

from .garments import colorize, mesh_from, set_slot
from .rig import rigid

ON_HAIR = {"party", "nurse"}       # cap shapes perched on the hair rather than pulled over the skull


def rests_on_hair(p):
    """Headwear placed after the hair is groomed, on top of it (the hair under it is left whole)."""
    return p.get("hat") == "wreath" or (p.get("hat") == "cap" and p.get("hatShape") in ON_HAIR)


def trims_hair(p):
    """Headwear the hair is cut back under (it grows only below the band)."""
    return p.get("hat") in ("cap", "wide") and not rests_on_hair(p)


def wear(fr, head, parts, p, garments=()):
    """The mask and the creature features, as a list of objects (hats: hat and on_hair)."""
    out = []
    if "mask" in p:
        out.append(mask(head, parts["head"], p["mask"]))
    if "horns" in p:
        from . import features
        out.append(features.horns(head, p["horns"], p.get("hornShape", "imp")))
    if "wings" in p:
        from . import wings
        out.extend(wings.wings(fr, parts["body"], p["wings"], p.get("wingShape", "bat"), garments))
    if "tail" in p:
        from . import tails
        out.append(tails.tail(fr, parts["body"], p, garments))
    from . import scifi
    if any(k in p for k in scifi.KEYS):        # machines and aliens: armour, helmets, alien heads (scifi.py)
        out.extend(scifi.wear(fr, head, parts, p, list(garments) + out))
    return out


def on_hair(head, parts, p, hair):
    """Headwear resting on the groomed hair (wreaths, circlets, a party cone, a nurse's cap)."""
    if not rests_on_hair(p):
        return []
    from . import crowns
    return crowns.build(head, [parts["head"]] + list(hair), p)


def hat(head, p, under=()):
    """A hat pulled onto the head: a sculpted cloth cap or hood (its cowl draped over ``under``), or a wide hat,
    coloured by region; one object."""
    from .paint import hex3
    kind, shape = p["hat"], p.get("hatShape")
    base = hex3(p.get("hatColor", "#5a3a1a"))
    trim = hex3(p["trim"]) if p.get("trim") else None
    if kind == "wide" or (kind == "cap" and shape == "shako"):
        from . import hatmesh
        bald = p.get("hairStyle") in ("bald", "buzz")
        obj, regions, _ = hatmesh.mesh(head, shape or "brim", 0.004 if bald else 0.012)
    else:
        from . import capshapes
        obj, regions = capshapes.mesh(head, kind, shape, p.get("visor", True), under, p.get("anatomy") == "skeleton")
    colors = region_colors(base, trim, shape)
    if shape in ("shako", "helmet"):           # gold bands on a shako, a brass brow band on a helmet
        colors["band"] = colors["badge"]
    elif shape == "beret":
        colors["band"] = tuple(c * 0.78 for c in base)
    if kind == "hood":                         # the lining, seen only in the shadow of the opening
        colors["under"] = tuple(c * 0.5 for c in base)
    paint_regions(obj, regions, colors)
    set_slot(obj, {"mushroom": "char:leather", "hardhat": "char:leather", "helmet": "char:metal",
                   "greathelm": "char:metal"}.get(shape, "char:cloth"))
    rigid(obj, "head")
    return obj


def region_colors(base, trim, shape):
    """The colour of each region of a hat: its cloth, a darker underside, a band (the trim colour when there is
    one), fur and pompoms, a plume, metal fittings."""
    mix = lambda a, b, t: tuple(x + (y - x) * t for x, y in zip(a, b))
    dark = tuple(c * 0.42 for c in base)
    gold = trim or (0.78, 0.6, 0.25)
    return {"crown": base, "brim": base, "under": tuple(c * 0.8 for c in base), "band": trim or dark,
            "trim": trim or mix(base, (0.55, 0.52, 0.48), 0.35), "fur": (0.95, 0.94, 0.9), "pom": (0.97, 0.96, 0.92),
            "plume": (0.8, 0.12, 0.1), "badge": gold, "rivet": gold, "peak": tuple(c * 0.75 for c in base),
            "gill": mix(base, (0.9, 0.95, 1.0), 0.55), "spot": mix(base, (0.85, 0.95, 1.0), 0.6),
            "stripe": mix(base, (1.0, 1.0, 1.0), 0.75), "cross": (0.8, 0.12, 0.14), "slit": (0.05, 0.05, 0.06),
            "knot": tuple(c * 0.88 for c in base), "hem": tuple(c * 0.86 for c in base), "button": base,
            "bill": tuple(c * 0.94 for c in base)}


def paint_regions(obj, regions, colors):
    """Face colours from each face's region name (shaded per face when regions carry a factor)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    shade = regions[1] if isinstance(regions, tuple) else None         # per face, or per vertex
    names = regions[0] if isinstance(regions, tuple) else regions
    per_vert = shade is not None and len(shade) == len(bm.verts) and len(shade) != len(bm.faces)
    for f in bm.faces:
        c = colors.get(names[f.index], colors["crown"])
        k = 1.0 if shade is None or per_vert else float(shade[f.index])
        for loop in f.loops:
            kk = float(shade[loop.vert.index]) if per_vert else k
            loop[layer] = (c[0] * kk, c[1] * kk, c[2] * kk, 1.0)
    bm.to_mesh(obj.data)
    bm.free()


def bpy_data_remove(obj):
    import bpy
    me = obj.data
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(me)


def mask(head, head_obj, color):
    """A smooth, stylised face mask (the Lunatic Cultist's): a rigid shell over the face with almond eye holes and a
    nose ridge, sculpted as a distance field (facemask.py)."""
    from . import facemask
    obj = facemask.mesh(head)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bpy_data_remove(obj)
    colorize(bm, color)
    obj = mesh_from(bm, "mask")
    set_slot(obj, "char:bone")
    rigid(obj, "head")
    return obj



def eyepatch(head, parts, color="#141414"):
    """A pirate's eye patch over the left eye with its strap round the head. The patch is a padded oval cast onto the
    face over the eye socket (onto the brow, cheek and nose round it, and the eyeball inside the lids), rounded out
    over the eye and sunk at its edge; the strap is a flat band lying on the skin (under hair and headwear) from the
    patch's outer edge back over the left ear, round the back of the head, over the right ear and diagonally down
    across the forehead to the patch's inner edge. One object, rigid on the head."""
    import math

    import bpy
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    deps = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(o, deps) for o in [parts["head"], *parts.get("eyes", [])]]
    h = head.h

    def cast(origin, direction):
        """The nearest surface (world point, world normal) along a ray from a head-local point, or (None, None)."""
        o, d = head.at(origin), Vector(direction).normalized()
        best = (None, None, math.inf)
        for tree in trees:
            hit, n, _, dist = tree.ray_cast(o, d, h)
            if hit is not None and dist < best[2]:
                best = (hit, n, dist)
        return best[0], best[1]

    bm = bmesh.new()
    e = head.local(head.eyes[-1])
    cx, cz, ax, az = e.x - 0.002, e.z + 0.002, 0.024, 0.0195
    rings, segs = 6, 24
    grid = []                    # the patch: ring 0 is its centre, the last ring its sunk edge
    for i in range(rings + 2):
        r = min(i, rings) / rings
        row = []
        for k in range(segs if i else 1):
            a = 2 * math.pi * k / segs
            x, z = cx + ax * r * math.cos(a), cz + az * r * math.sin(a)
            hit, _ = cast((x, 0.3, z), (0, -1, 0))
            y = head.local(hit).y if hit is not None else e.y + 0.012
            lift = 0.0024 + 0.0042 * (1 - r * r) if i <= rings else -0.0008      # padded over the eye, sunk at the edge
            row.append(bm.verts.new(head.at((x, y + lift, z))))
        grid.append(row)
    for k in range(segs):
        bm.faces.new((grid[0][0], grid[1][k], grid[1][(k + 1) % segs]))
    for i in range(1, rings + 1):
        for k in range(segs):
            bm.faces.new((grid[i][k], grid[i + 1][k], grid[i + 1][(k + 1) % segs], grid[i][(k + 1) % segs]))
    # The strap's path: unwrapped azimuth (0 at the face, + to the character's right) and height, head-local.
    out_a = math.atan2(cx - ax * 0.96, e.y)
    in_a = math.atan2(cx + ax * 0.7, e.y) - 2 * math.pi
    keys = [(out_a, cz + 0.004), (-1.25, 0.022), (-1.57, 0.05), (-2.2, 0.056), (-math.pi, 0.052), (-4.1, 0.06),
            (-4.71, 0.07), (-5.4, 0.074), (-5.9, 0.055), (in_a, cz + az * 0.7)]
    n = 96
    psi = np.linspace(keys[0][0], keys[-1][0], n)
    zs = np.interp(-psi, [-k[0] for k in keys], [k[1] for k in keys])
    for _ in range(6):                    # rounded where the path turns (the ends stay where the patch takes them)
        zs[1:-1] = (zs[:-2] + 2 * zs[1:-1] + zs[2:]) / 4
    pts, nrm = [], []
    for a, z in zip(psi, zs):
        d = Vector((math.sin(a), math.cos(a), 0.0))
        hit, normal = cast(tuple(Vector((0, -0.012, z)) + d * 0.3), -d)
        if hit is None:
            continue
        pts.append(hit + normal * 0.0016 * h)
        nrm.append(normal)
    half, thick = 0.0034 * h, 0.0012 * h
    loops = []
    for i, (p, normal) in enumerate(zip(pts, nrm)):
        tan = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = tan.cross(normal).normalized()
        loops.append([bm.verts.new(p + side * sx * half + normal * nz * thick) for sx, nz in ((-1, 0), (1, 0), (1, 1), (-1, 1))])
    for a_, b_ in zip(loops, loops[1:]):
        for k in range(4):
            bm.faces.new((a_[k], a_[(k + 1) % 4], b_[(k + 1) % 4], b_[k]))
    for cap in (loops[0], loops[-1][::-1]):
        bm.faces.new(cap)
    colorize(bm, color)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = mesh_from(bm, "eyepatch")
    set_slot(obj, "char:leather")
    rigid(obj, "head")
    return obj
