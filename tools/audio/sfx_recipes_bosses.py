"""Boss cues: a heavy slam on the ground (boss_slam), Skeletron's hand striking (hand_slap) and its homing skull leaving
the head (skull_shot). Cue ids raised by boss brains are sound ids. Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise, _thud
from synth import bandpass, decay, highpass, lowpass, mix, osc, reverb, samples, seconds, sweep_lowpass


def boss_slam(rng):
    """A sub-bass thud with a rumble of falling rubble after it."""
    dur = 1.1
    n = samples(dur, S)
    t = seconds(n, S)
    sub = osc("sine", 38 * rng.uniform(0.95, 1.05) * (1 + 1.2 * np.exp(-t / 0.03)), dur, S) * decay(n, 0.28, S, 0.001)
    crack = lowpass(_noise(dur, rng), 2500, S) * decay(n, 0.03, S, 0.0005)
    rubble = np.zeros(n)
    for _ in range(14):  # stones settling after the hit
        at = int(rng.uniform(0.04, 0.7) * S)
        bit = bandpass(_noise(0.06, rng), 300, 1500, S) * decay(samples(0.06, S), 0.015, S, 0.0005) * rng.uniform(0.15, 0.4)
        rubble[at:at + len(bit)] += bit[:n - at]
    return mix(sub * 1.4, crack * 0.6, rubble, _thud(60, dur, 0.18, rng, 0.4))


def hand_slap(rng):
    """A bone hand striking: a dry clack of knuckles over a short body thud."""
    dur = 0.35
    n = samples(dur, S)
    clack = sum(bandpass(_noise(dur, rng), f, f * 1.5, S) for f in (1100 * rng.uniform(0.9, 1.1), 2600)) * decay(n, 0.018, S, 0.0004)
    return mix(clack * 1.3, _thud(85 * rng.uniform(0.9, 1.1), dur, 0.08, rng, 0.5))


def skull_shot(rng):
    """A hollow whoosh as a cursed skull leaves the head, with a bony rattle in it."""
    dur = 0.55
    n = samples(dur, S)
    t = seconds(n, S)
    env = np.sin(np.linspace(0, np.pi, n)) ** 1.2
    air = sweep_lowpass(highpass(_noise(dur, rng), 200, S) * env, 2400, 500, S)
    hollow = osc("sine", 330 * np.exp(-t / 0.5), dur, S) * env * 0.35 + osc("sine", 495 * np.exp(-t / 0.5), dur, S) * env * 0.15
    rattle = bandpass((rng.uniform(0, 1, n) < 0.02) * rng.uniform(-1, 1, n), 1500, 4000, S) * env
    return reverb(mix(air, hollow, rattle * 1.2), S, 0.4, 0.4, 0.2, 0.4)


RECIPES = {"boss_slam": (boss_slam, 2), "hand_slap": (hand_slap, 3), "skull_shot": (skull_shot, 2)}

GAIN = {"hand_slap": 0.7, "skull_shot": 0.6}
