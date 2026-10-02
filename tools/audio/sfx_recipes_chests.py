"""Chests: the lid creaking shut when a chest is put away (chest_close), the counterpart of sfx_recipes.py's
chest_open creak. Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _thud, creak


def chest_close(rng):
    """The opening creak pitched lower (180 Hz) as the lid swings down, ending in the soft clap of the lid on the box."""
    swing = creak(180)(rng) * 0.8
    clap = _thud(140, 0.12, 0.03, rng, grit=0.5)
    at = int(0.24 * S)
    out = np.zeros(max(len(swing), at + len(clap)))
    out[:len(swing)] += swing
    out[at:at + len(clap)] += clap
    return out


RECIPES = {"chest_close": (chest_close, 1)}

# A touch softer than the opening creak (which plays at the default 0.8): the chest is being put away.
GAIN = {"chest_close": 0.7}
