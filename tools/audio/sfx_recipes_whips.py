"""Whips (P3-15): the lash's tip breaking the sound barrier (whip_crack: a sharp, bright snap with a short slap of
air behind it). The swing-up whoosh, whip_swing, is sfx_recipes_items' (P3-27). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, highpass, mix, samples


def whip_crack(rng):
    """A crack: a near-instant broadband snap, a bright ring, and a quick slap of air."""
    dur = 0.25
    n = samples(dur, S)
    snap = highpass(_noise(dur, rng), 2500, S) * decay(n, 0.006, S, 0.0002) * 1.6
    ring = bandpass(_noise(dur, rng), 3000, 7000, S) * decay(n, 0.03, S, 0.0005) * 0.5
    slap = bandpass(_noise(dur, rng), 300, 1500, S) * decay(n, 0.05, S, 0.002) * 0.4
    return mix(snap, ring, slap)


RECIPES = {"whip_crack": (whip_crack, 3)}

GAIN = {"whip_crack": 0.7}
