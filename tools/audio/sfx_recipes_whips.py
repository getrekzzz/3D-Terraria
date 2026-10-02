"""Whips (P3-15): the lash swinging up (whip_swing: a rising airy swish) and its tip breaking the sound barrier
(whip_crack: a sharp, bright snap with a short slap of air behind it). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, highpass, mix, samples, seconds


def whip_swing(rng):
    """A rising swish: band-passed air whose band climbs as the lash speeds up."""
    dur = 0.32
    n = samples(dur, S)
    t = seconds(n, S)
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.5
    lo = bandpass(_noise(dur, rng), 400, 1600, S)
    hi = bandpass(_noise(dur, rng), 1500, 5000, S)
    k = t / dur
    return (lo * (1 - k) + hi * k) * env * 0.9


def whip_crack(rng):
    """A crack: a near-instant broadband snap, a bright ring, and a quick slap of air."""
    dur = 0.25
    n = samples(dur, S)
    snap = highpass(_noise(dur, rng), 2500, S) * decay(n, 0.006, S, 0.0002) * 1.6
    ring = bandpass(_noise(dur, rng), 3000, 7000, S) * decay(n, 0.03, S, 0.0005) * 0.5
    slap = bandpass(_noise(dur, rng), 300, 1500, S) * decay(n, 0.05, S, 0.002) * 0.4
    return mix(snap, ring, slap)


RECIPES = {"whip_swing": (whip_swing, 2), "whip_crack": (whip_crack, 3)}

GAIN = {"whip_swing": 0.45, "whip_crack": 0.7}
