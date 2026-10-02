"""Hair models (X-09): every hair style of bgen/human/hair_styles.py for each player body, built on that body's head
exactly as a character's hair would be, then moved into the head bone's local space (like the armour pieces, see
bgen/armor_fit.py) so the game wears it on the head bone with an identity transform. Strands are generated in the
grey hair marker colour; the character shader recolours them (hair_color). Outputs assets/models/hair/<b>_<style>.glb
(b = m | f) and the manifest's "hair" section."""
import pathlib

from . import armor_fit, export
from .mesh import reset_scene

BODIES = {"m": {"body": "male", "age": "young"}, "f": {"body": "female", "age": "young"}}
MARKER = "#808080"


def build_hair(repo: pathlib.Path, manifest: dict, wanted):
    from . import human
    from .human import frame as frame_mod, hair_styles
    from .human.head import Head
    manifest.setdefault("hair", {})
    for key, base in BODIES.items():
        p = human.motion_params({**base, "hairStyle": "bald"})
        for style in hair_styles.STYLES:
            hid = f"{key}_{style}"
            if style == "bald" or not wanted(hid):
                continue
            reset_scene()
            fr = frame_mod.Frame(p)
            head = Head(fr, p)
            head_obj = head.build()
            parts = hair_styles.build(head, head_obj, style, MARKER)
            for o in parts:
                human.set_material(o, "char:hair")
            obj = human.join(parts, hid)
            human.rest_uvs(obj)
            arm = frame_mod.build_armature(fr, hid)
            obj.data.transform(arm.data.bones["head"].matrix_local.inverted())
            armor_fit.export_piece(obj)
            rel = f"assets/models/hair/{hid}.glb"
            export.export_glb([obj], repo / rel, texcoords=True, tangents=True)
            tris = sum(len(f.vertices) - 2 for f in obj.data.polygons)
            manifest["hair"][hid] = {"model": rel, "bone": "head", "tris": tris}
            print(f"hair {hid}: {tris} tris")
    manifest["hair"] = dict(sorted(manifest["hair"].items()))
