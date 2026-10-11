"""Guide: the achievement popup's bright rising arpeggio with a shimmer tail. Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S
from synth import decay, mix, osc, reverb, samples


def achievement(rng):
    """Four bell tones climbing a major chord (C6 E6 G6 C7), then a soft sparkle."""
    dur = 1.1
    n = samples(dur, S)
    out = np.zeros(n)
    for k, f in enumerate((1046.5, 1318.5, 1568.0, 2093.0)):
        i = int(0.075 * k * S)
        bell = (osc("sine", f, dur, S) + osc("sine", f * 2.0, dur, S) * 0.25 + osc("triangle", f * 0.5, dur, S) * 0.2)
        out[i:] += (bell * decay(n, 0.35 if k == 3 else 0.16, S, 0.002))[:n - i] * (0.32 if k == 3 else 0.24)
    sparkle = np.zeros(n)
    for _ in range(6):
        i = int(rng.uniform(0.3, 0.7) * S)
        sparkle[i:] += (osc("sine", rng.uniform(4000, 6500), dur, S) * decay(n, 0.04, S, 0.001))[:n - i] * 0.05
    return reverb(mix(out, sparkle), S)


RECIPES = {"achievement": (achievement, 1)}
