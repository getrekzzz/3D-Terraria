"""Character parameters as the generator reads them: the names other branches of it used (vocabulary), the
defaults every human is dressed in unless told otherwise, and the parameters a skeleton and its actions are built
from (motion_params; characters whose parameters give the same skeleton share an animation set, see motion_key).
"""


def vocabulary(p: dict) -> dict:
    """Parameters in the names other branches of the generator used, as this one names them: hat bandana (a cap shaped
    as a bandana, no peak), tricorne (a wide hat shaped as a tricorn), helm (a cap shaped as a metal helmet), ears
    pointed (an elf's tips, unless the ear is shaped already) and coatOpen (lapels)."""
    q = dict(p)
    hat = q.get("hat")
    if hat == "bandana":
        q.update(hat="cap", hatShape=q.get("hatShape", "bandana"), visor=q.get("visor", False))
    elif hat == "tricorne":
        q.update(hat="wide", hatShape=q.get("hatShape", "tricorn"))
    elif hat == "helm":
        q.update(hat="cap", hatShape=q.get("hatShape", "helmet"))
    if q.pop("ears", None) == "pointed" and not any(k in q for k in ("earPoint", "earSize", "earFlare")):
        q.update(earSize=1.05, earPoint=1.1, earFlare=0.5)
    if "coatOpen" in q:
        q.setdefault("lapels", bool(q.pop("coatOpen")))
    return q


DEFAULTS = {"shirt": "#4a7ac0", "pants": "#3a4a6a", "boots": "#5a3c26", "hair": "#5a3a20", "eyes": "#5a7890"}


def motion_params(p: dict) -> dict:
    """The parameters a character's skeleton and actions are built from (as build() and build_skeleton() see them)."""
    p = {**DEFAULTS, **p}
    if p.get("undead"):
        p = {"build": 0.25, "weight": 0.15, **p}
    if p.get("anatomy") == "skeleton":
        p = {**p, "undead": True}
    return p
