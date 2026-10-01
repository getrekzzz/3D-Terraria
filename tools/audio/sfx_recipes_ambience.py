"""Ambience one-shots: a water drop plinking on a cave floor (cave_drip) and a fish taking the bait (fish_bite: the
bobber's quick double plop and a splash). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, lowpass, mix, osc, samples, seconds


def cave_drip(rng):
    """A drop meeting water or stone: a bright falling-pitch plink with a tiny splash."""
    dur = 0.4
    n = samples(dur, S)
    t = seconds(n, S)
    f = rng.uniform(1300, 2600) * (1 + 0.7 * np.exp(-t / 0.008))
    plink = osc("sine", f, dur, S) * decay(n, 0.045, S, 0.0006)
    splash = bandpass(_noise(dur, rng), 2000, 7000, S) * decay(n, 0.01, S, 0.0005) * 0.25
    return mix(plink, splash)


def fish_bite(rng):
    """The bobber yanked under: two quick plops (rising pitch bubbles) and a splash of water."""
    dur = 0.55
    n = samples(dur, S)
    out = np.zeros(n)
    for k, at in enumerate((0.0, rng.uniform(0.09, 0.14))):
        m = samples(0.2, S)
        t = seconds(m, S)
        f = rng.uniform(280, 380) * (1 + 1.6 * (1 - np.exp(-t / 0.03)))
        plop = osc("sine", f, 0.2, S) * decay(m, 0.05, S, 0.002) * (1.0 - 0.3 * k)
        i = int(at * S)
        out[i:i + m] += plop[:n - i]
    splash = lowpass(bandpass(_noise(dur, rng), 600, 6000, S), 4000, S) * decay(n, 0.12, S, 0.004) * 0.5
    return mix(out, splash)


RECIPES = {"cave_drip": (cave_drip, 4), "fish_bite": (fish_bite, 2)}

GAIN = {"cave_drip": 0.45, "fish_bite": 0.7}
