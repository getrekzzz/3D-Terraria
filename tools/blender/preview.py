"""Contact sheets of generated creatures for checking models and motion without the game.

    python3.13 tools/blender/preview.py -- --creature=player --clips=walk,swing --frames=6 --out=DIR
    python3.13 tools/blender/preview.py -- --creature=king_slime --out=DIR
    python3.13 tools/blender/preview.py -- '--creature=king_slime_v2:{"width": 3.5}' --out=DIR   (generator:params)

Builds the creature from tools/blender/assets.json (nothing is written to assets/) and renders it with Blender's
Workbench engine in vertex colours: for every clip a row of evenly spaced frames seen from the front-right, or for a
static model a row of views (front, three-quarter, side, back, top). --view=X,Y,Z sets the clip camera direction
(the creature faces +Y; 1,0,0 is its right side). --hold=1 puts a 0.8 m rod in a human's right hand, attached as the
game attaches held items (out of the fist along the hand bone's Z axis). Writes DIR/<creature>.png.
"""
import json
import math
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

CELL = 256


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    opts = {"creature": "player", "clips": None, "frames": 6, "out": ".", "view": "0.75,1,0.3", "hold": ""}
    for a in argv:
        key, _, value = a.lstrip("-").partition("=")
        opts[key] = value
    opts["frames"] = int(opts["frames"])
    return opts


def build(name):
    import bpy  # noqa: F401
    from bgen import beasts, bosses, creatures, critters, export, fauna, festive, horrors, human, lunar, mechs
    from bgen.mesh import MeshBuilder, reset_scene, rng_for
    spec = json.loads((HERE / "assets.json").read_text(encoding="utf-8"))["creatures"]
    job = spec[name] if ":" not in name else {"generator": name.split(":")[0], "params": json.loads(name.split(":", 1)[1] or "{}")}
    reset_scene()
    if job["generator"] == "humanoid":
        return human.build(name, job.get("params", {}))
    mb = MeshBuilder(name.split(":")[0])
    gen = None
    for module in (creatures, fauna, beasts, mechs, horrors, lunar, festive, critters, bosses, *_extra_modules()):
        gen = gen or getattr(module, "GENERATORS", {}).get(job["generator"])
    rig = gen(mb, job.get("params", {}), rng_for(name.split(":")[0], 0))
    return creatures.finish(mb.build(export.simple_material), rig, name)


def _extra_modules():
    import importlib
    out = []
    for mod in ("bosses_pre", "bosses_pre2", "bosses_hard", "bosses_lunar"):
        try:
            out.append(importlib.import_module(f"bgen.{mod}"))
        except ModuleNotFoundError:
            pass
    return out


def hold_rod(arm):
    """A stand-in held item: a rod from the grip (0.08 m along the hand) along the hand bone's +Z, tipped red."""
    import bmesh
    import bpy
    from mathutils import Matrix
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.02, radius2=0.012, depth=0.8,
                          matrix=Matrix.Translation((0, 0, 0.4)))
    me = bpy.data.meshes.new("rod")
    bm.to_mesh(me)
    colors = me.color_attributes.new("Color", "FLOAT_COLOR", "POINT")
    for i, v in enumerate(me.vertices):
        colors.data[i].color = (0.9, 0.1, 0.1, 1) if v.co.z > 0.6 else (0.85, 0.75, 0.3, 1)
    rod = bpy.data.objects.new("rod", me)
    bpy.context.scene.collection.objects.link(rod)
    bone = arm.data.bones["hand.R"]
    rod.parent = arm
    rod.parent_type = "BONE"
    rod.parent_bone = "hand.R"
    rod.matrix_parent_inverse = Matrix.Identity(4)
    rod.location = (0, 0.08 - bone.length, 0)      # bone parenting starts at the tail


def setup_scene(objs):
    import bpy
    from mathutils import Vector
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "VERTEX"
    shading.show_cavity = True
    shading.show_shadows = False
    shading.background_type = "VIEWPORT"
    shading.background_color = (0.42, 0.45, 0.5)
    scene.render.resolution_x = scene.render.resolution_y = CELL
    scene.render.film_transparent = False
    scene.display_settings.display_device = "sRGB"
    for o in objs:
        if o.type == "MESH":
            me = o.data
            if me.color_attributes and me.color_attributes.active_color is None:
                me.color_attributes.active_color = me.color_attributes[0]
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    scene.collection.objects.link(cam)
    scene.camera = cam
    from bgen.mesh import bounds
    lo, hi = bounds(objs)
    centre, extent = (lo + hi) / 2, hi - lo
    return cam, centre, max(extent.length, 0.2), Vector


def shoot(cam, centre, size, direction, path, span=1.25):
    import bpy
    from mathutils import Vector
    d = Vector(direction).normalized()
    cam.location = centre + d * size * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = size * span
    cam.data.clip_end = size * 8
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def sheet(tiles, cols, path):
    """Stitches equal-size PNG tiles (rows of ``cols``) into one image."""
    import bpy
    import numpy as np
    rows = (len(tiles) + cols - 1) // cols
    canvas = np.zeros((rows * CELL, cols * CELL, 4), dtype=np.float32)
    for i, tile in enumerate(tiles):
        img = bpy.data.images.load(str(tile))
        px = np.array(img.pixels[:], dtype=np.float32).reshape(CELL, CELL, 4)
        r, c = rows - 1 - i // cols, i % cols           # image rows run bottom-up
        canvas[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = px
        bpy.data.images.remove(img)
    out = bpy.data.images.new("sheet", cols * CELL, rows * CELL, alpha=True)
    out.pixels = canvas.ravel().tolist()
    out.filepath_raw = str(path)
    out.file_format = "PNG"
    out.save()


def main():
    import bpy
    opts = args()
    out = pathlib.Path(opts["out"]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    objs = build(opts["creature"])
    arm = next((o for o in objs if o.type == "ARMATURE"), None)
    cam, centre, size, _ = setup_scene(objs)
    tiles = []
    tmp = out / f"_tiles_{opts['creature'].split(':')[0]}"      # per creature, so previews can run side by side
    tmp.mkdir(exist_ok=True)
    actions = sorted(a.name for a in bpy.data.actions)
    clips = opts["clips"].split(",") if opts["clips"] else actions
    if arm is None or not actions:
        views = [(0, 1, 0.15), (0.7, 1, 0.45), (1, 0, 0.1), (0, -1, 0.2), (0.01, 0.2, 1)]
        for i, v in enumerate(views):
            shoot(cam, centre, size, v, tmp / f"{i}.png")
            tiles.append(tmp / f"{i}.png")
        sheet(tiles, len(views), out / f"{opts['creature'].split(':')[0]}.png")
        return
    if opts["hold"] and "hand.R" in arm.data.bones:
        hold_rod(arm)
    arm.animation_data_create()
    for track in arm.animation_data.nla_tracks:
        track.mute = True
    n = opts["frames"]
    for clip in clips:
        action = bpy.data.actions.get(clip)
        if action is None:
            continue
        arm.animation_data.action = action
        if hasattr(arm.animation_data, "action_slot") and action.slots:
            arm.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        for k in range(n):
            frame = start + (end - start) * k / max(1, n - 1)
            bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
            path = tmp / f"{clip}_{k}.png"
            shoot(cam, centre, size, tuple(float(v) for v in opts["view"].split(",")), path, span=1.15)
            tiles.append(path)
    sheet(tiles, n, out / f"{opts['creature'].split(':')[0]}.png")
    print("clips:", ", ".join(c for c in clips if bpy.data.actions.get(c)))


if __name__ == "__main__":
    main()
