"""Builds the wearable armour (P3-41): every piece of every set in the spec's "armor" section as its own GLB in its
bone's local space (assets/models/armor/<set>_<piece>.glb), listed in assets/manifest.json "armor" with the bone, the
bone's rest length (the game scales a piece by actual / reference length) and whether it hides the hair. The set's
items get their dropped model and inventory icon from the same pieces placed on the rest-pose body: a helmet shows the
head piece, a breastplate the chest, belt and pauldrons, leggings the cuisses, greaves and boots."""
import pathlib

from mathutils import Matrix

from . import armor, armor_fit, export
from .materials import PropMaterials
from .mesh import MeshBuilder, reset_scene


RIG_BONES = {"hips", "spine", "chest", "neck", "head", "upper_arm", "forearm", "thigh", "shin", "foot"}


def armor_items(spec_armor: dict, items: dict) -> dict:
    """Armour set key -> {equip slot: item id}: set items by armorSet, single pieces (mining_helmet) by their own id."""
    out = {}
    for iid, item in items.items():
        slot = item.get("equipSlot")
        if slot not in ("head", "body", "legs"):
            continue
        key = item.get("armorSet") if item.get("armorSet") in spec_armor else (iid if iid in spec_armor else None)
        if key:
            out.setdefault(key, {})[slot] = iid
    return out


def build_armor(repo: pathlib.Path, spec_armor: dict, items: dict, wanted, manifest: dict, icons=True, samples=24):
    fit = armor_fit.BodyFit(repo)
    sets = armor_items(spec_armor, items)
    manifest.setdefault("armor", {})
    for key, entry in sorted(spec_armor.items()):
        slots = sets.get(key, {})
        if not (wanted(key) or any(wanted(i) for i in slots.values())):
            continue
        pieces = {}
        for piece, style in armor.pieces_of(entry):
            obj, bone, hides = _piece(fit, key, piece, style, entry)
            rel = f"assets/models/armor/{key}_{piece}.glb"
            armor_fit.export_piece(obj)
            export.export_glb([obj], repo / rel)
            tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
            pieces[piece] = {"model": rel, "bone": bone, "slot": armor.SLOT[piece.split(".")[0]], "refLength": round(fit.length[bone], 4),
                             "hidesHair": hides, "tris": tris}
        manifest["armor"][key] = pieces
        print(f"armor {key}: {len(pieces)} pieces, {sum(p['tris'] for p in pieces.values())} tris")
        for slot, iid in sorted(slots.items()):
            _item(repo, fit, key, entry, slot, iid, manifest, icons, samples)
    manifest["armor"] = dict(sorted(manifest["armor"].items()))
    # Bone lengths of the rig the pieces were fitted to: the game scales a piece by its wearer's / these.
    manifest["armorRig"] = {b: round(fit.length[b], 4) for b in sorted(fit.length) if b.split(".")[0] in RIG_BONES}


def _piece(fit, key, piece, style, entry):
    reset_scene()
    mb = MeshBuilder(f"{key}_{piece}")
    hides = armor.build_piece(mb, fit, piece, style, entry)
    kind, _, side = piece.partition(".")
    return mb.build(export.simple_material), armor.BONE[kind] + (f".{side}" if side else ""), bool(hides)


def _item(repo, fit, key, entry, slot, iid, manifest, icons, samples):
    """The item's dropped model and icon: its slot's pieces on the rest-pose body, recentred on the origin."""
    reset_scene()
    objs = []
    for piece, style in armor.pieces_of(entry):
        if armor.SLOT[piece.split(".")[0]] != slot or piece.startswith("bracer"):  # bracers float off the A-pose body
            continue
        mb = MeshBuilder(f"{iid}_{piece}")
        armor.build_piece(mb, fit, piece, style, entry)
        obj = mb.build(export.simple_material)
        kind, _, side = piece.partition(".")
        armor_fit.to_body(obj, fit.rest[armor.BONE[kind] + (f".{side}" if side else "")])
        objs.append(obj)
    if not objs:
        return
    import bpy
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = objs[0]
    lo, hi = export.bounds([obj])
    centre = (lo + hi) / 2
    centre.z = lo.z
    obj.data.transform(Matrix.Translation(-centre))
    rel = f"assets/models/items/{iid}.glb"
    export.export_glb([obj], repo / rel)
    entry_out = {"model": rel, "armor": key}
    if icons:
        icon = f"assets/icons/items/{iid}.png"
        export.apply_textured_materials([obj], PropMaterials(repo))
        export.render_icon([obj], repo / icon, "object", samples=samples, sprite=True)
        entry_out["icon"] = icon
    elif "icon" in manifest["items"].get(iid, {}):
        entry_out["icon"] = manifest["items"][iid]["icon"]
    manifest["items"][iid] = entry_out
    print(f"item {iid}: {entry_out}")
