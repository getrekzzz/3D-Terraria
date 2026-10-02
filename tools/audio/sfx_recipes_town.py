"""Town: petting a town pet (a soft two-note chirp with a little sparkle). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S
from synth import decay, osc, reverb, samples


def pet(rng):
    """Two warm sine chirps rising a fifth, then a faint shimmer: a happy little pat."""
    dur = 0.5
    n = samples(dur, S)
    out = np.zeros(n)
    base = 660 * rng.uniform(0.97, 1.03)
    for k, f in enumerate((base, base * 1.5)):
        i = int(0.09 * k * S)
        tone = osc("sine", f, dur, S) + osc("sine", f * 2, dur, S) * 0.2
        out[i:] += (tone * decay(n, 0.09, S, 0.004))[:n - i] * 0.3
    i = int(0.18 * S)
    out[i:] += (osc("sine", base * 4, dur, S) * decay(n, 0.05, S, 0.002))[:n - i] * 0.06
    return reverb(out, S)


RECIPES = {"pet": (pet, 2)}
