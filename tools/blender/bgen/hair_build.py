"""Hair models (X-09): every style of the hair catalogue (bgen/human/hair_styles.py) for each player body, built on
that body's head exactly as a character's own hair is (most styles are sculpted locks, locks.Groom), then moved into
the head bone's local space (like the armour pieces, see bgen/armor_fit.py) so the game wears it on the head bone
with an identity transform.

Each body is built once, dressed as its coat variant (the bulkiest of assets.d/x-09-player.json): long hair and
ponytails rest on that body and its clothes, and the hair's ambient occlusion (the vertex colour's alpha, as on every
character: occlusion.py) is baked with the head, body and clothes under it shading it. Hair is generated in the grey
hair marker colour, a ponytail's tie in the red shirt marker darkened as a character's tie is (human.groom); the
character shader recolours them (hair_color, shirt_color). Outputs assets/models/hair/<b>_<style>.glb (b = m | f)
and the manifest's "hair" section."""
import pathlib

from . import armor_fit, export
from .mesh import reset_scene

BODIES = {"m": "player_m_coat", "f": "player_f_coat"}      # the dressed body per hair set (assets.d/x-09-player.json)
MARKER = "#808080"
TIE = "#602c2c"              # the shirt marker (#b05050) x 0.55, as human.groom darkens a shirt for a tie
DRAPED = {"long", "wavy_long", "ponytail"}       # styles that hang onto the shoulders and back


def _player_params(name):
    from asset_spec import load_spec          # (tools/blender, on the path when run from build_all.py)
    return load_spec()["creatures"][name]["params"]


def _mesh_data(objs):
    """Vertices and polygons (world space) of several meshes, as occlusion.bake takes its extra occluders."""
    verts, polys = [], []
    for o in objs:
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [tuple(base + i for i in p.vertices) for p in o.data.polygons]
    return verts, polys


def _body(name):
    """The player body ``name`` built bare and dressed (no hair): (frame, head, head mesh, bare-body BVH, worn BVH,
    occluders, head bone rest matrix)."""
    import bpy
    from mathutils.bvhtree import BVHTree

    from . import human
    from .human import coverings, frame as frame_mod
    p = human.vocabulary(_player_params(name))
    q = coverings.undress(p, {**human.DEFAULTS, **p})
    q = {**q, "hairStyle": "bald"}
    reset_scene()
    fr, head, parts = human._parts(q)
    bare = BVHTree.FromObject(parts["body"], bpy.context.evaluated_depsgraph_get())     # before the clothes trim it
    worn = human.dress(fr, head, parts, q)
    worn_tree = human.tree([o for o in worn if o.type == "MESH" and not o.name.startswith(human.LOOSE)])
    occluders = _mesh_data([parts["head"], parts["body"], *parts["eyes"], *(o for o in worn if o.type == "MESH")])
    arm = frame_mod.build_armature(fr, name)
    rest = arm.data.bones["head"].matrix_local.copy()
    return fr, head, parts["head"], bare, worn_tree, occluders, rest


def build_hair(repo: pathlib.Path, manifest: dict, wanted):
    import bpy

    from . import human
    from .human import hair_styles, occlusion
    manifest.setdefault("hair", {})
    for key, body in BODIES.items():
        styles = [s for s in hair_styles.STYLES if s != "bald" and wanted(f"{key}_{s}")]
        if not styles:
            continue
        fr, head, head_obj, bare, worn_tree, occluders, rest = _body(body)
        for style in styles:
            hid = f"{key}_{style}"
            draped = style in DRAPED
            seed = sum(ord(ch) for ch in style)          # (as human.groom seeds a character with no skin given)
            parts = hair_styles.build(head, head_obj, style, MARKER, bare if draped else None,
                                      worn_tree if draped else None, TIE, seed)
            for o in parts:
                human.set_material(o, "char:cloth" if o.name.startswith("hair_tie") else "char:hair")
            obj = human.join(parts, hid)
            human.rest_uvs(obj)
            occlusion.bake(obj, 0.1 * fr.s, occluders=occluders)
            colors = obj.data.color_attributes        # (the exporter writes only the active one: marker colour and AO)
            colors.active_color = colors["Color"]
            colors.render_color_index = colors.find("Color")
            obj.data.transform(rest.inverted())
            armor_fit.export_piece(obj)
            rel = f"assets/models/hair/{hid}.glb"
            export.export_glb([obj], repo / rel, texcoords=True, tangents=True)
            tris = sum(len(f.vertices) - 2 for f in obj.data.polygons)
            manifest["hair"][hid] = {"model": rel, "bone": "head", "tris": tris}
            print(f"hair {hid}: {tris} tris")
            mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            bpy.data.meshes.remove(mesh)
    manifest["hair"] = dict(sorted(manifest["hair"].items()))
