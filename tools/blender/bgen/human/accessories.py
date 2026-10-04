"""Hats, a face mask and creature features, fitted to the generated head and body and bound to the bones that
carry them. Hats: sculpted cloth caps and hoods (headwear, capshapes), wide hats (hats), and wreaths, circlets and
perched caps that rest on the finished hair (crowns); creature features: horns (features), wings (wings), tails
(tails)."""
import bmesh
from mathutils import Vector

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
        from . import hats
        bald = p.get("hairStyle") in ("bald", "buzz")
        obj, regions, _ = hats.mesh(head, shape or "brim", 0.004 if bald else 0.012)
    else:
        from . import capshapes
        obj, regions = capshapes.mesh(head, kind, shape, p.get("visor", True), under)
    colors = region_colors(base, trim, shape)
    if shape in ("shako", "helmet"):           # gold bands on a shako, a brass brow band on a helmet
        colors["band"] = colors["badge"]
    elif shape == "beret":
        colors["band"] = tuple(c * 0.78 for c in base)
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
            "knot": tuple(c * 0.88 for c in base)}


def paint_regions(obj, regions, colors):
    """Face colours from each face's region name (shaded per face when regions carry a factor)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    shade = regions[1] if isinstance(regions, tuple) else None
    names = regions[0] if isinstance(regions, tuple) else regions
    for f in bm.faces:
        c = colors.get(names[f.index], colors["crown"])
        k = 1.0 if shade is None else float(shade[f.index])
        for loop in f.loops:
            loop[layer] = (c[0] * k, c[1] * k, c[2] * k, 1.0)
    bm.to_mesh(obj.data)
    bm.free()


def mask(head, head_obj, color):
    """A smooth face mask (the Lunatic Cultist's): the face lifted into a thin shell over the skin, with eye holes."""
    h = head.h
    bm = bmesh.new()
    bm.from_mesh(head_obj.data)
    bm.normal_update()
    eyes = [Vector((sx * 0.031, 0.066, -0.002)) for sx in (1, -1)]
    drop = []
    for f in bm.faces:
        q = head.local(f.calc_center_median())
        if not (q.y > 0.02 and abs(q.x) < 0.065 and -0.11 < q.z < 0.05) or any((q - e).length < 0.016 for e in eyes):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * 0.007 * h
    colorize(bm, color)
    obj = mesh_from(bm, "mask")
    set_slot(obj, "char:bone")
    rigid(obj, "head")
    return obj

