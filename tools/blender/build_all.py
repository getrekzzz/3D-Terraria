"""Generates every model and item icon from tools/blender/assets.json and the game content.

Run with Blender:   blender --background --python tools/blender/build_all.py -- [options]
or the bpy module:  python3.13 tools/blender/build_all.py [options]      (pip install bpy==5.2.2)

Options:
  --check          only verify that every tile object and item is covered (no Blender needed)
  --only=GLOB      rebuild matching asset ids (fnmatch), keep the rest of the manifest
  --no-icons       skip icon rendering
  --samples=N      Cycles samples per icon (default 24)
Outputs: assets/models/{objects,items,creatures}/*.glb, assets/icons/items/*.png, assets/manifest.json
"""
import fnmatch
import json
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))

GENERATOR_VERSION = 1
CELL = 0.5

# Metals with their own prop texture layer; the rest tint the iron layer.
METAL_LAYERS = {"copper": "copper", "gold": "gold"}


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    opts = {"check": False, "only": None, "icons": True, "samples": 24}
    for a in argv:
        if a == "--check":
            opts["check"] = True
        elif a.startswith("--only="):
            opts["only"] = a.split("=", 1)[1]
        elif a == "--no-icons":
            opts["icons"] = False
        elif a.startswith("--samples="):
            opts["samples"] = int(a.split("=", 1)[1])
        else:
            raise SystemExit(f"unknown option {a}")
    return opts


def load_json(path: pathlib.Path):
    text = re.sub(r"^\s*//.*$", "", path.read_text(encoding="utf-8"), flags=re.M)
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", text))


def load_content():
    manifest = load_json(REPO / "data" / "manifest.json")
    blocks, items, objects, enemies = {}, {}, {}, {}
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
            enemies[t["id"]] = t
    return blocks, items, objects, enemies


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
        for metal, tint in spec["metals"].items():
            patterns.append((pattern.replace("{metal}", metal), metal, tint, s))
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
            metal, tint, s = match
            params = {"metal": tint, "metalLayer": METAL_LAYERS.get(metal, "iron"), **s.get("params", {})}
            item_jobs[iid] = {"generator": s["generator"], "params": params}
            continue
        placed = items.get(iid, {}).get("placeObject") or placer_of_object.get(iid)
        if placed and placed in object_jobs:
            item_jobs[iid] = {"object": placed}
            continue
        errors.append(f"item '{iid}' has no model/icon source (add it to assets.json items)")
    return object_jobs, item_jobs, block_icons, creature_jobs, errors


def build(opts, spec, object_jobs, item_jobs, creature_jobs):
    import bpy  # noqa: F401  (must load before mathutils/bmesh when running as the standalone bpy module)
    from bgen import beasts, bosses, creatures, export, fauna, furniture, gear, horrors, human, items as item_gen, lunar, mechs, nature
    from bgen.materials import PropMaterials
    from bgen.mesh import MeshBuilder, reset_scene, rng_for

    generators = {**nature.GENERATORS, **furniture.GENERATORS, **item_gen.GENERATORS, **bosses.GENERATORS, **mechs.GENERATORS,
                  **gear.GENERATORS, **horrors.GENERATORS, **lunar.GENERATORS}
    manifest_path = REPO / "assets" / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() and opts["only"] else {"objects": {}, "items": {}}
    wanted = (lambda i: any(fnmatch.fnmatch(i, pat) for pat in opts["only"].split(","))) if opts["only"] else (lambda i: True)

    def make(asset_id, job, variant):
        reset_scene()
        props = PropMaterials(REPO)
        mb = MeshBuilder(f"{asset_id}_v{variant}")
        generators[job["generator"]](mb, job["params"], rng_for(asset_id, variant))
        for slot in mb.materials:
            props.validate(slot)
        return mb.build(export.simple_material), props

    for oid, job in sorted(object_jobs.items()):
        if not wanted(oid):
            continue
        models = []
        for v in range(job["variants"]):
            obj, props = make(oid, job, v)
            rel = f"assets/models/objects/{oid}_v{v}.glb"
            export.export_glb([obj], REPO / rel)
            models.append(rel)
            tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
            print(f"object {oid} v{v}: {tris} tris")
        manifest["objects"][oid] = {"models": models, "generator": job["generator"]}

    for iid, job in sorted(item_jobs.items()):
        if not wanted(iid) and not (job.get("object") and wanted(job["object"])):
            continue
        entry = {k: v for k, v in manifest["items"].get(iid, {}).items() if k == "icon" and not opts["icons"]}
        if "object" in job:
            obj, props = make(job["object"], object_jobs[job["object"]], 0)
            entry["object"] = job["object"]
        else:
            obj, props = make(iid, job, 0)
            rel = f"assets/models/items/{iid}.glb"
            export.export_glb([obj], REPO / rel)
            entry["model"] = rel
        if opts["icons"]:
            icon = f"assets/icons/items/{iid}.png"
            export.apply_textured_materials([obj], props)
            export.render_icon([obj], REPO / icon, "object" if "object" in job else _view_for(job["generator"]), samples=opts["samples"])
            entry["icon"] = icon
        manifest["items"][iid] = entry
        print(f"item {iid}: {entry}")

    manifest.setdefault("creatures", {})
    for name, job in sorted(creature_jobs.items()):
        if not wanted(name):
            continue
        reset_scene()
        rel = f"assets/models/creatures/{name}.glb"
        if job["generator"] == "humanoid":
            # Detailed people (bgen/human): own meshes, UVs along hair strands and rest-pose UVs for the shader.
            objs = human.build(name, job.get("params", {}))
            human.validate_slots(objs)
            export.export_glb(objs, REPO / rel, texcoords=True, tangents=True)
            tris = sum(len(p.vertices) - 2 for p in objs[1].data.polygons)
            print(f"human {name}: {tris} tris, {len(objs[0].data.bones)} bones")
        else:
            props = PropMaterials(REPO)
            mb = MeshBuilder(name)
            gen = (creatures.GENERATORS.get(job["generator"]) or fauna.GENERATORS.get(job["generator"])
                   or beasts.GENERATORS.get(job["generator"]) or mechs.GENERATORS.get(job["generator"]) or horrors.GENERATORS.get(job["generator"])
                   or lunar.GENERATORS.get(job["generator"])
                   or bosses.GENERATORS[job["generator"]])
            rig = gen(mb, job.get("params", {}), rng_for(name, 0))
            for slot in mb.materials:
                props.validate(slot)
            objs = creatures.finish(mb.build(export.simple_material), rig, name)
            export.export_glb(objs, REPO / rel)
        actions = sorted(a.name for a in __import__("bpy").data.actions)
        manifest["creatures"][name] = {"model": rel, "generator": job["generator"], "animations": actions}
        print(f"creature {name}: {actions}")

    manifest["generatorVersion"] = GENERATOR_VERSION
    manifest["objects"] = dict(sorted(manifest["objects"].items()))
    manifest["items"] = dict(sorted(manifest["items"].items()))
    manifest["creatures"] = dict(sorted(manifest["creatures"].items()))
    manifest_path.write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {manifest_path.relative_to(REPO)}")


def _view_for(generator: str) -> str:
    long_items = {"sword", "shortsword", "pickaxe", "axe", "hammer", "bow", "wand", "staff", "arrow", "gun", "rod", "whip"}
    return "item" if generator in long_items else "object"


def main() -> int:
    opts = args()
    spec = json.loads((HERE / "assets.json").read_text(encoding="utf-8"))
    blocks, items, objects, enemies = load_content()
    object_jobs, item_jobs, block_icons, creature_jobs, errors = plan(spec, blocks, items, objects, enemies)
    for e in errors:
        print("ERROR:", e)
    if errors:
        return 1
    print(f"{len(object_jobs)} objects, {len(item_jobs)} item models/icons, {len(creature_jobs)} creatures, {len(block_icons)} block icons (tools/textures/build_icons.py)")
    if opts["check"]:
        return 0
    build(opts, spec, object_jobs, item_jobs, creature_jobs)
    return 0


if __name__ == "__main__":
    sys.exit(main())
