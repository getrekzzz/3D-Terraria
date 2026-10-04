"""Creature outfits beyond clothes, each part asked for by its own parameter: a jungle shaman's mask, headdress,
beads, leaf skirt and body paint (tribal.py) and a ghost's misty, wisping hem (spectre.py)."""
from . import spectre, tribal


def wear(fr, head, parts, p, w, garments, lookup=None):
    """The parts ``p`` asks for, finished and weighted (``garments``: what the character already wears)."""
    out = tribal.wear(fr, head, parts, p, w, lookup)
    if "wisps" in p:
        out += spectre.wisps(fr, garments, p)
    return out
