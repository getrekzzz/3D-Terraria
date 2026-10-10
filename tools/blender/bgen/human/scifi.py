"""Machines and aliens (the Cyborg, the Martian invaders, the Lunar pillars' Storm Diver, Vortexian and Selenian):
the parts the generator adds for them, worn after the clothes (accessories.wear calls ``wear``).

Parameters (listed in human/__init__.py, described here):

- alien: martian (a grey alien's big cranium, large dark almond eyes, small mouth, slit nostrils and ear holes) or
  insect (a tall swept crest, keel and side fins, huge eyes, mandibles): the head, eyes and skin paint of alienhead.py;
- antennae: their colour (two antennae with bulb tips on the crown);
- armor: a plate style over the body suit (cyborg, martian, diver, carapace, solar; armor.py), with armorColor (the
  plates), trim (a second plate colour, or the colour plates shade to at their edges) and glow (light strips); on an
  ``anatomy: armor`` body (matter.py) ``armor`` is instead that body's colour and none of this applies;
- helmet: bubble (a glass dome on a neck ring), diver (a round metal helmet with a glass visor) or solar (a closed
  helm with lit eye slits and flame blades) (helmets.py), with helmetColor (default armorColor) and glass (a dome's
  or visor's tint);
- implant: a cyborg's skull plate and eye lens, the lens's colour (implant.py);
- jetpack: its colour (diverhelm.py).
"""
import bpy

from .garments import set_slot
from .rig import rigid

KEYS = ("alien", "armor", "helmet", "implant", "jetpack", "antennae")
WORN = ("shirt", "pants", "boots", "coat", "robe")       # the clothes plates and helmets are fitted over


def wear(fr, head, parts, p, garments):
    """The creature parts this character's parameters ask for, as a list of objects (eyes and skin are changed in
    place)."""
    out = []
    kind = p.get("alien")
    if kind:
        from . import alienhead
        from .paint import paint
        for o, new in zip(parts["eyes"], alienhead.eyes(head, kind)):
            old = o.data
            o.data = new.data
            bpy.data.objects.remove(new)
            bpy.data.meshes.remove(old)
            set_slot(o, "char:eye")
            rigid(o, "head")
        skin = alienhead.paint(head, kind, p.get("skin", "#8ab08a"))
        paint(parts["head"], skin)
        alienhead.paint_neck(parts["body"], head, skin)
        alienhead.shade(parts["head"], head)
    if "antennae" in p:
        from . import alienhead
        obj = alienhead.antennae(head, p["antennae"])
        set_slot(obj, "char:nail")
        rigid(obj, "head")
        out.append(obj)
    if not any(k in p for k in ("armor", "helmet", "implant", "jetpack")):
        return out
    from .platefit import Fitter
    worn = [o for o in garments if o.type == "MESH" and o.name.split(".")[0] in WORN]
    fitter = Fitter(worn + [parts["body"], parts["head"]], clothes=worn)
    if "armor" in p:
        from . import armor
        out += armor.build(p["armor"], fr, p, fitter)
    if "implant" in p:
        from . import implant
        out += implant.build(head, p)
    if "helmet" in p:
        from . import helmets
        out += helmets.build(p["helmet"], fr, head, p, fitter, worn, parts["head"])
    elif p.get("armor") == "cyborg":
        from . import helmets
        out += helmets.collar(fr, head, p, fitter, worn, parts["head"])
    if "jetpack" in p:
        from . import diverhelm
        out += diverhelm.jetpack(fr, p, fitter)
    return out
