"""Projectile impacts (P3-11): an arrow thunking into wood, earth or stone (arrow_hit: a short woody knock with a
quick shaft quiver). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, lowpass, mix, osc, samples, seconds


def arrow_hit(rng):
    """A dull thunk (low knock and a burst of filtered noise) and the shaft's brief buzzing quiver."""
    dur = 0.35
    n = samples(dur, S)
    t = seconds(n, S)
    knock = osc("sine", rng.uniform(150, 190) * (1 + 0.6 * np.exp(-t / 0.01)), dur, S) * decay(n, 0.04, S, 0.001)
    crack = bandpass(_noise(dur, rng), 600, 3500, S) * decay(n, 0.015, S, 0.0005) * 0.6
    quiver = lowpass(osc("saw", rng.uniform(70, 95), dur, S), 900, S) * (0.5 + 0.5 * np.sin(2 * np.pi * 34 * t)) * decay(n, 0.12, S, 0.01) * 0.25
    return mix(knock, crack, quiver)


RECIPES = {"arrow_hit": (arrow_hit, 2)}

GAIN = {"arrow_hit": 0.6}
