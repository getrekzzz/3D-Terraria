"""Generates every model and item icon from tools/blender/assets.json (+ the drop-in files tools/blender/assets.d/*.json,
merged by asset_spec.py) and the game content. Generators come from bgen's @generator registry and the per-module
GENERATORS dicts listed in build().

Run with Blender:   blender --background --python tools/blender/build_all.py -- [options]
or the bpy module:  python3.13 tools/blender/build_all.py [options]      (pip install bpy==5.2.2)

Options:
  --check          only verify that every tile object and item is covered (no Blender needed)
  --only=GLOB      rebuild matching asset ids (fnmatch), keep the rest of the manifest
  --no-icons       skip icon rendering
  --samples=N      Cycles samples per icon (default 24)
  --portraits      render only the town NPC head shots and bestiary thumbnails (assets/icons/{npcs,creatures})
  --hair           build only the hair models (assets/models/hair; --only matches <m|f>_<style>)
  --boss-icons     render only the boss-bar head icons (assets/icons/bosses; --only matches boss ids)
  --anims          build only the human animation sets (assets/models/creatures/anim; --only matches characters or sets)
  --shard=I/N      with --anims: build every Nth set starting at I (to run several builds side by side)
Held items export their sockets (grip, muzzle, tip, nock, string, chain; bgen/sockets.py) as empties next to the mesh
and list them in the manifest. The "projectiles" section builds projectile models (yoyo heads, flail balls) for
content projectiles (an entry's "projectile", default its id).
Outputs: assets/models/{objects,items,creatures,projectiles}/*.glb, assets/icons/items/*.png, assets/manifest.json
"""
import fnmatch
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
from asset_plan import boss_models, load_content, plan, plan_armor, plan_projectiles, portrait_models  # noqa: E402
from asset_spec import SpecError, load_spec  # noqa: E402

GENERATOR_VERSION = 1

# Every metal tints the iron layer read as neutral (prop:iron:metal, see bgen/items._metal), so its colour is exactly
# the "metals" hex. A metal's entry is a hex, or {"tint": hex, ...accent params} passed to every generator of its
# patterns (e.g. "veins" for glowing ingot cracks, "edge" for darker chamfers).


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    opts = {"check": False, "only": None, "icons": True, "samples": 24, "anims": False, "shard": None, "boss_icons": False, "portraits": False, "hair": False}
    for a in argv:
        if a == "--check":
            opts["check"] = True
        elif a.startswith("--only="):
            opts["only"] = a.split("=", 1)[1]
        elif a == "--no-icons":
            opts["icons"] = False
        elif a.startswith("--samples="):
            opts["samples"] = int(a.split("=", 1)[1])
        elif a == "--hair":
            opts["hair"] = True
        elif a == "--portraits":
            opts["portraits"] = True
        elif a == "--boss-icons":
            opts["boss_icons"] = True
        elif a == "--anims":
            opts["anims"] = True
        elif a.startswith("--shard="):
            i, n = a.split("=", 1)[1].split("/")
            opts["shard"] = (int(i), int(n))
        else:
            raise SystemExit(f"unknown option {a}")
    return opts


def build(opts, spec, object_jobs, item_jobs, creature_jobs, projectile_jobs, items):
    import bpy  # noqa: F401  (must load before mathutils/bmesh when running as the standalone bpy module)
    import bgen
    from bgen import icon_views, sockets as socket_gen
    from bgen import beasts, bosses, bosses_hard, bosses_lunar, bosses_pre, bosses_pre2, bosses_pre3, bosses_temple, creatures, critters, export, fauna, mechanisms, furniture, gear, horrors, human, items as item_gen, festive, lunar, mechs, nature
    from bgen.materials import PropMaterials
    from bgen.mesh import MeshBuilder, bounds as mesh_bounds, reset_scene, rng_for

    generators = {**nature.GENERATORS, **furniture.GENERATORS, **item_gen.GENERATORS, **bosses.GENERATORS, **mechs.GENERATORS,
                  **gear.GENERATORS, **horrors.GENERATORS, **lunar.GENERATORS, **festive.GENERATORS, **critters.ITEM_GENERATORS, **mechanisms.GENERATORS}
    # Creature generators, first module wins (creatures, fauna, beasts, …, bosses_lunar, then bosses).
    creature_generators = {}
    for module in (bosses, bosses_temple, bosses_lunar, bosses_hard, bosses_pre3, bosses_pre2, bosses_pre, critters, festive, lunar, horrors, mechs,
                   beasts, fauna, creatures):
        creature_generators.update(module.GENERATORS)
    # Generators registered with @bgen.generator (load_all imports every bgen module) join both tables; reusing a name
    # from the dicts above is an error rather than a silent override.
    registry = bgen.load_all()
    clash = sorted(set(registry) & (set(generators) | set(creature_generators)))
    if clash:
        raise SystemExit(f"ERROR: generator name(s) {', '.join(clash)} registered with @generator are already used by a module's GENERATORS dict")
    generators.update(registry)
    creature_generators.update(registry)
    manifest_path = REPO / "assets" / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() and opts["only"] else {"objects": {}, "items": {}}
    if not opts["only"] and manifest_path.exists():  # hair models are built by --hair alone; a full build keeps them
        manifest["hair"] = json.loads(manifest_path.read_text(encoding="utf-8")).get("hair", {})
    wanted = (lambda i: any(fnmatch.fnmatch(i, pat) for pat in opts["only"].split(","))) if opts["only"] else (lambda i: True)

    def make(asset_id, job, variant, held=False):
        reset_scene()
        props = PropMaterials(REPO)
        mb = MeshBuilder(f"{asset_id}_v{variant}")
        generators[job["generator"]](mb, job["params"], rng_for(asset_id, variant))
        for slot in mb.materials:
            props.validate(slot)
        obj = mb.build(export.simple_material)
        parts = mb.build_parts(export.simple_material, obj)  # moving parts (a chest lid) are children of obj
        return obj, props, socket_gen.resolve(job["generator"], mb, job["params"], *mesh_bounds([obj, *parts]), held=held)

    for oid, job in sorted(object_jobs.items()):
        if not wanted(oid):
            continue
        models = []
        for v in range(job["variants"]):
            obj, props, _ = make(oid, job, v)
            rel = f"assets/models/objects/{oid}_v{v}.glb"
            export.export_glb([obj, *obj.children], REPO / rel)
            models.append(rel)
            tris = sum(len(p.vertices) - 2 for o in (obj, *obj.children) for p in o.data.polygons)
            print(f"object {oid} v{v}: {tris} tris")
        manifest["objects"][oid] = {"models": models, "generator": job["generator"]}

    from bgen import armor_build
    worn = {i for slots in armor_build.armor_items(spec.get("armor", {}), items).values() for i in slots.values()}
    for iid, job in sorted(item_jobs.items()):
        if iid in worn:  # armour items get their model and icon from the worn pieces (below)
            continue
        if not wanted(iid) and not (job.get("object") and wanted(job["object"])):
            continue
        entry = {k: v for k, v in manifest["items"].get(iid, {}).items() if k == "icon" and not opts["icons"]}
        if "object" in job:
            obj, props, _ = make(job["object"], object_jobs[job["object"]], 0)
            entry["object"] = job["object"]
        else:
            obj, props, sockets = make(iid, job, 0, held=True)
            rel = f"assets/models/items/{iid}.glb"
            export.export_glb([obj, *socket_gen.empties(obj, sockets)], REPO / rel)
            entry["model"] = rel
            if sockets:
                entry["sockets"] = socket_gen.y_up(sockets)
            if job["generator"] in ("shield", "balloon"):  # accessories the avatar wears (game/Player/PlayerAvatar.Armor.cs)
                entry["worn"] = job["generator"]
            if "icon" in entry:  # icon last, whether or not icons are re-rendered (stable manifest key order)
                entry["icon"] = entry.pop("icon")
        if opts["icons"]:
            icon = f"assets/icons/items/{iid}.png"
            view, spot = "object", None
            if "object" not in job:
                gen = job["generator"]
                view = icon_views.view_for(gen, bgen.VIEWS)
                if gen in icon_views.ICON_POSES:  # the icon shows another pose of the item (a whip's lash laid out)
                    obj, props, sockets = make(iid, {**job, "params": {**job["params"], **icon_views.ICON_POSES[gen]}}, 0, held=True)
                if gen in icon_views.SPOTLIT and icon_views.SPOTLIT[gen] in sockets:
                    spot = sockets[icon_views.SPOTLIT[gen]][0]
            shown = [obj, *(c for c in obj.children if c.type == "MESH")]  # with its moving parts, not socket empties
            export.apply_textured_materials(shown, props)
            export.render_icon(shown, REPO / icon, view, samples=opts["samples"], sprite=True, spot=spot)
            entry["icon"] = icon
        manifest["items"][iid] = entry
        print(f"item {iid}: {entry}")

    armor_build.build_armor(REPO, spec.get("armor", {}), items, wanted, manifest, icons=opts["icons"], samples=opts["samples"])

    manifest.setdefault("projectiles", {})
    for mid, job in sorted(projectile_jobs.items()):
        if not wanted(mid):
            continue
        obj, _props, sockets = make(mid, job, 0)
        rel = f"assets/models/projectiles/{mid}.glb"
        export.export_glb([obj, *socket_gen.empties(obj, sockets)], REPO / rel)
        manifest["projectiles"][mid] = {"model": rel, "generator": job["generator"], "sockets": socket_gen.y_up(sockets)}
        print(f"projectile {mid}: {rel}")

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
            gen = creature_generators[job["generator"]]
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
    manifest["projectiles"] = dict(sorted(manifest["projectiles"].items()))
    manifest_path.write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {manifest_path.relative_to(REPO)}")


def build_anims(opts, creature_jobs):
    """Human animation sets: one per distinct skeleton (body, height, gait), shared by every character built on it.
    index.json maps each character model to its set; the game plays a character's actions from its set."""
    import bpy  # noqa: F401
    from bgen import export, human
    from bgen.mesh import reset_scene

    out = REPO / "assets" / "models" / "creatures" / "anim"
    out.mkdir(parents=True, exist_ok=True)
    index, params = {}, {}
    for name, job in sorted(creature_jobs.items()):
        if job["generator"] == "humanoid":
            key = human.motion_key(job.get("params", {}))
            index[name] = key
            params.setdefault(key, job.get("params", {}))
    (out / "index.json").write_text(json.dumps(index, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    pats = opts["only"].split(",") if opts["only"] else ["*"]
    hit = lambda i: any(fnmatch.fnmatch(i, pat) for pat in pats)
    keys = sorted(k for k in params if hit(k) or any(hit(n) for n, v in index.items() if v == k))
    if opts["shard"]:
        keys = keys[opts["shard"][0]::opts["shard"][1]]
    for key in keys:
        reset_scene()
        export.export_glb(human.build_motion(params[key]), out / f"{key}.glb")
        print(f"anim {key}: {sorted(a.name for a in bpy.data.actions)}")


def main() -> int:
    opts = args()
    try:
        spec = load_spec()
    except SpecError as e:
        print("ERROR:", e)
        return 1
    blocks, items, objects, enemies, projectiles = load_content()
    object_jobs, item_jobs, block_icons, creature_jobs, errors = plan(spec, blocks, items, objects, enemies)
    projectile_jobs, projectile_errors = plan_projectiles(spec, projectiles)
    errors += projectile_errors + plan_armor(spec, items)
    for e in errors:
        print("ERROR:", e)
    if errors:
        return 1
    print(f"{len(object_jobs)} objects, {len(item_jobs)} item models/icons, {len(creature_jobs)} creatures, {len(projectile_jobs)} projectiles, {len(block_icons)} block icons (tools/textures/build_icons.py)")
    bosses = boss_models(enemies)
    missing = sorted(b for b in bosses if not (REPO / "assets" / "icons" / "bosses" / f"{b}.png").exists())
    if missing and not opts["boss_icons"]:
        print("ERROR: bosses without a head icon (build_all.py --boss-icons):", ", ".join(missing))
        return 1
    npcs, creatures = portrait_models(enemies)
    missing = sorted(f"{d}/{i}" for d, ids in (("npcs", npcs), ("creatures", creatures)) for i in ids
                     if not (REPO / "assets" / "icons" / d / f"{i}.png").exists())
    if missing and not opts["portraits"]:
        print("ERROR: missing portraits (build_all.py --portraits):", ", ".join(missing))
        return 1
    if opts["check"]:
        return 0
    if opts["hair"]:
        import bpy  # noqa: F401
        from bgen import hair_build
        pats = opts["only"].split(",") if opts["only"] else ["*"]
        manifest_path = REPO / "assets" / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        hair_build.build_hair(REPO, manifest, lambda i: any(fnmatch.fnmatch(i, p) for p in pats))
        manifest_path.write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8")
        return 0
    if opts["portraits"]:
        import bpy  # noqa: F401
        from bgen import portraits
        pats = opts["only"].split(",") if opts["only"] else ["*"]
        tints = {i: e["tint"] for i, e in enemies.items() if e.get("tint")}
        portraits.render_portraits(REPO, npcs, creatures, lambda i: any(fnmatch.fnmatch(i, p) for p in pats), tints=tints)
        return 0
    if opts["boss_icons"]:
        import bpy  # noqa: F401
        from bgen import boss_icons
        pats = opts["only"].split(",") if opts["only"] else ["*"]
        boss_icons.render_boss_icons(REPO, bosses, lambda i: any(fnmatch.fnmatch(i, p) for p in pats), samples=16)
        return 0
    if opts["anims"]:
        build_anims(opts, creature_jobs)
        return 0
    build(opts, spec, object_jobs, item_jobs, creature_jobs, projectile_jobs, items)
    return 0


if __name__ == "__main__":
    sys.exit(main())
