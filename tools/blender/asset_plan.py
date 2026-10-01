"""Build planning for tools/blender/build_all.py (plain Python, no Blender needed): reads the game content and the
asset spec and decides how every model and icon is produced."""
import json
import pathlib
import re

REPO = pathlib.Path(__file__).resolve().parents[2]
CELL = 0.5
# Which entry of a metal's "metalStyles" shapes each pattern generator (P3-39 per-tier silhouettes).
STYLE_OF = {"sword": "sword", "shortsword": "sword", "pickaxe": "pick", "axe": "axe", "hammer": "hammer", "bow": "bow"}


def load_json(path: pathlib.Path):
    text = re.sub(r"^\s*//.*$", "", path.read_text(encoding="utf-8"), flags=re.M)
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", text))


def load_content():
    manifest = load_json(REPO / "data" / "manifest.json")
    blocks, items, objects, enemies, projectiles = {}, {}, {}, {}, {}
    for rel in manifest["files"]:
        doc = load_json(REPO / "data" / rel)
        for b in doc.get("blocks", []):
            blocks[b["id"]] = b
        for i in doc.get("items", []):
            items[i["id"]] = i
        for o in doc.get("tileObjects", []):
            objects[o["id"]] = o
        for e in doc.get("enemies", []):
            enemies[e["id"]] = e
        for t in doc.get("townNpcs", []):  # town residents use rigged creature models too
            enemies[t["id"]] = {**t, "_town": True}
        for p in doc.get("projectiles", []):
            projectiles[p["id"]] = p
    return blocks, items, objects, enemies, projectiles


def plan_projectiles(spec, projectiles):
    """Projectile model jobs (model id -> job) and errors: every entry must name a content projectile."""
    jobs, errors = {}, []
    for mid, s in spec.get("projectiles", {}).items():
        target = s.get("projectile", mid)
        if target not in projectiles:
            errors.append(f"assets.json projectile model '{mid}' is for '{target}', which is not a content projectile")
            continue
        jobs[mid] = {"generator": s["generator"], "params": dict(s.get("params", {}))}
    return jobs, errors


def plan(spec, blocks, items, objects, enemies):
    """Decides how every model and icon is produced. Returns (object_jobs, item_jobs, block_icons, creature_jobs, errors)."""
    errors = []
    creature_jobs = dict(spec.get("creatures", {}))
    for eid, e in enemies.items():
        if e.get("model", eid) not in creature_jobs:
            errors.append(f"creature '{eid}' uses model '{e.get('model', eid)}' missing from assets.json creatures")
    if "player" not in creature_jobs:
        errors.append("assets.json creatures needs a 'player' model")
    object_jobs = {}
    for oid, o in objects.items():
        s = spec["objects"].get(oid)
        if s is None:
            errors.append(f"tile object '{oid}' has no entry in assets.json objects")
            continue
        size = o.get("size", [1, 1, 1])
        object_jobs[oid] = {"generator": s["generator"], "params": {**s.get("params", {}), "size": (size[0] * CELL, size[1] * CELL, size[2] * CELL)},
                            "variants": o.get("variants", 1)}
    for extra in sorted(set(spec["objects"]) - set(objects)):
        errors.append(f"assets.json object '{extra}' does not exist in content")

    # Derived items: placeable blocks (block icons) and placeable objects (object icons).
    placer_of_object = {}
    for oid, o in objects.items():
        if o.get("natural") or o.get("autoItem") is False:
            continue
        placer_of_object[o.get("item") or oid] = oid
    block_icons = sorted(bid for bid, b in blocks.items() if b.get("autoItem", True) and "drop" not in b and bid not in items)

    patterns = []
    for pattern, s in spec["items"]["patterns"].items():
        for metal, entry in spec["metals"].items():
            tint, accents = (entry, {}) if isinstance(entry, str) else (entry["tint"], {k: v for k, v in entry.items() if k != "tint"})
            patterns.append((pattern.replace("{metal}", metal), metal, (tint, accents), s))
    item_jobs = {}
    all_items = set(items) | set(placer_of_object) | set(block_icons)
    for iid in sorted(all_items):
        if iid in block_icons:
            continue
        if iid in spec["items"]["explicit"]:
            s = spec["items"]["explicit"][iid]
            item_jobs[iid] = {"generator": s["generator"], "params": dict(s.get("params", {}))}
            continue
        match = next(((m, t, s) for name, m, t, s in patterns if name == iid), None)
        if match:
            metal, (tint, accents), s = match
            style = spec.get("metalStyles", {}).get(metal, {}).get(STYLE_OF.get(s["generator"], ""), {})
            params = {"metal": tint, "metalLayer": "iron", **accents, **style, **s.get("params", {})}
            item_jobs[iid] = {"generator": s["generator"], "params": params}
            continue
        placed = items.get(iid, {}).get("placeObject") or placer_of_object.get(iid)
        if placed and placed in object_jobs:
            item_jobs[iid] = {"object": placed}
            continue
        errors.append(f"item '{iid}' has no model/icon source (add it to assets.json items)")
    return object_jobs, item_jobs, block_icons, creature_jobs, errors


def boss_models(enemies):
    """Non-minor bosses (the boss bar shows them) -> the creature model their head icon is rendered from."""
    return {eid: e.get("model", eid) for eid, e in enemies.items() if e.get("boss") and not e["boss"].get("minor")}


def portrait_models(enemies):
    """Town NPC head shots and bestiary thumbnails: (town npc id -> model, enemy id -> model)."""
    npcs = {i: e.get("model", i) for i, e in enemies.items() if e.get("_town")}
    return npcs, {i: e.get("model", i) for i, e in enemies.items() if not e.get("_town")}


def plan_armor(spec, items):
    """Every wearable armour item needs a worn-armour entry (its set id, or its own id for single pieces)."""
    armor = spec.get("armor", {})
    return [f"armour item '{iid}' has no entry in the asset spec's armor section ('{i.get('armorSet') or iid}')"
            for iid, i in sorted(items.items()) if i.get("equipSlot") in ("head", "body", "legs")
            and (i.get("armorSet") or iid) not in armor]
