"""Failed-use feedback: the dry click of a weapon with nothing to fire (use_fail), the fizzle of a spell without mana
(no_mana) and the tink of a pickaxe too weak for the block it hits (tink). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import adsr, bandpass, decay, highpass, lowpass, mix, osc, samples, seconds, sweep_lowpass


def use_fail(rng):
    """A short, dull wooden click: a knock with a little tick on top, and the softer catch of the mechanism after it."""
    dur = 0.14
    n = samples(dur, S)
    t = seconds(n, S)
    knock = osc("sine", 330 * rng.uniform(0.9, 1.1) * (1 + 0.5 * np.exp(-t / 0.004)), dur, S) * decay(n, 0.016, S, 0.0005)
    tick = bandpass(_noise(dur, rng), 1100, 3000, S) * decay(n, 0.004, S, 0.0003)
    click = mix(knock * 0.8, tick * 0.5)
    out = click.copy()
    at = int(rng.uniform(0.026, 0.038) * S)
    out[at:] += click[:n - at] * 0.35
    return lowpass(out, 3200, S)


def no_mana(rng):
    """A descending, airy fizzle: hiss whose brightness falls away, a sinking tone under it and a few sparks."""
    dur = 0.42
    n = samples(dur, S)
    t = seconds(n, S)
    env = adsr(n, 0.008, 0.12, 0.45, 0.22, S)
    hiss = sweep_lowpass(highpass(_noise(dur, rng), 500, S), 6500, 600, S)
    sparks = bandpass((rng.uniform(0, 1, n) < 0.004) * rng.uniform(-1, 1, n), 1800, 6000, S)
    f = 840 * rng.uniform(0.95, 1.05) * np.exp(-t / 0.22)
    tone = (osc("sine", f, dur, S) + osc("sine", f * 1.5, dur, S) * 0.3) * decay(n, 0.12, S)
    return mix(hiss * env * 0.8, tone * 0.25, sparks * env * 1.5)


def tink(rng):
    """A bright metallic ping: a hard tick and inharmonic partials that ring briefly (the higher ones die first)."""
    dur = 0.32
    n = samples(dur, S)
    base = 2450 * rng.uniform(0.94, 1.06)
    ring = sum(osc("sine", base * r, dur, S) * w * decay(n, tau, S, 0.0004)
               for r, w, tau in ((1.0, 0.5, 0.1), (1.59, 0.25, 0.07), (2.14, 0.2, 0.05), (2.76, 0.12, 0.03)))
    tick = highpass(_noise(dur, rng), 3000, S) * decay(n, 0.002, S, 0.0002) * 0.6
    return mix(ring, tick)


RECIPES = {"use_fail": (use_fail, 2), "no_mana": (no_mana, 2), "tink": (tink, 2)}

# Peak levels: cues, not events; the tink stays clear of the dig and break sounds around it.
GAIN = {"use_fail": 0.55, "no_mana": 0.5, "tink": 0.55}
