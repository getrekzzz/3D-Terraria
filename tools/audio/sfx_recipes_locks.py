"""Locks: the click and little jingle of a key turning in a chest (unlock) and the dull clack of a lock that holds
(locked). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, lowpass, mix, osc, samples, seconds


def unlock(rng):
    """Two metallic clicks of the bolt sliding back, then a short bright jingle of the key ring."""
    dur = 0.5
    n = samples(dur, S)
    t = seconds(n, S)
    out = np.zeros(n)
    for at, f in ((0.0, 2600), (0.07, 1900)):
        i = int(at * S)
        click = bandpass(_noise(dur, rng), f, f * 1.6, S) * decay(n, 0.006, S, 0.0003)
        out[i:] += click[:n - i] * 0.9
    ring = np.zeros(n)
    for k in range(4):
        i = int((0.14 + 0.045 * k + rng.uniform(0, 0.01)) * S)
        f = rng.uniform(3200, 5200)
        tone = (osc("sine", f, dur, S) + osc("sine", f * 1.47, dur, S) * 0.4) * decay(n, 0.05, S, 0.0005)
        ring[i:] += tone[:n - i] * 0.22
    thunk = osc("sine", 220 * (1 + 0.4 * np.exp(-t / 0.01)), dur, S) * decay(n, 0.03, S) * 0.4
    return mix(out, ring, thunk)


def locked(rng):
    """A dull wooden-metal clack: the lid jerks against its latch and stops."""
    dur = 0.22
    n = samples(dur, S)
    t = seconds(n, S)
    knock = osc("sine", 180 * rng.uniform(0.92, 1.08) * (1 + 0.6 * np.exp(-t / 0.006)), dur, S) * decay(n, 0.03, S, 0.0006)
    rattle = bandpass(_noise(dur, rng), 700, 1800, S) * decay(n, 0.018, S, 0.0004)
    return lowpass(mix(knock, rattle * 0.6), 2600, S)


RECIPES = {"unlock": (unlock, 1), "locked": (locked, 2)}

GAIN = {"unlock": 0.6, "locked": 0.55}
