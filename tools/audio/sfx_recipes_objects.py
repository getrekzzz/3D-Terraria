"""Object sounds (P3-21, P3-22): a clay pot shattering (pot_shatter) and a felled tree crashing down (tree_fall)."""
import numpy as np

from sfx_recipes import S, _noise, _thud
from synth import bandpass, decay, lowpass, mix, osc, samples


def pot_shatter(rng):
    """Clay bursting: a hollow knock and a spray of ceramic shards tinkling down."""
    dur = 0.6
    n = samples(dur, S)
    knock = _thud(320 * rng.uniform(0.9, 1.1), dur, 0.04, rng, 0.8)
    shards = np.zeros(n)
    for _ in range(9):
        m = samples(0.08, S)
        f = rng.uniform(1800, 4200)
        tink = osc("sine", f, 0.08, S) * decay(m, 0.012, S, 0.0003) * rng.uniform(0.2, 0.6)
        at = int(rng.uniform(0.01, 0.45) * S)
        shards[at:at + m] += tink[:n - at]
    crack = bandpass(_noise(dur, rng), 1500, 7000, S) * decay(n, 0.03, S, 0.0005) * 0.7
    return mix(knock, shards, crack)


def tree_fall(rng):
    """A trunk crashing to the ground: creaking split, whoosh of the crown, a heavy thud and rustling leaves."""
    dur = 1.6
    n = samples(dur, S)
    t = np.arange(n) / S
    creak = bandpass(osc("saw", 140 * (1 + 0.1 * np.sin(t * 9)), dur, S), 300, 1500, S) * np.exp(-t / 0.35) * 0.25
    impact_at = int(0.55 * S)
    thud = _thud(70, 1.0, 0.25, rng, 1.0) * 1.6
    rustle = bandpass(_noise(dur, rng), 2000, 8000, S) * np.clip((t - 0.5) / 0.05, 0, 1) * np.exp(-np.clip(t - 0.55, 0, None) / 0.35) * 0.4
    out = mix(creak, rustle)
    out[impact_at:impact_at + len(thud)] += thud[:n - impact_at]
    return lowpass(out, 6000, S)


RECIPES = {"pot_shatter": (pot_shatter, 2), "tree_fall": (tree_fall, 2)}

GAIN = {"pot_shatter": 0.65, "tree_fall": 0.8}
