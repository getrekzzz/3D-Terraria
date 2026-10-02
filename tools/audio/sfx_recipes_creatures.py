"""Creature materials (P3-19): hit and death sounds for metal, stone, plant, chitin, spirit and bone creatures
(data/config/creature_materials.json). Flesh and gel reuse hit/npc_death and hit_squish/slime_death."""
import numpy as np

from sfx_recipes import S, _noise, _thud
from synth import bandpass, decay, highpass, lowpass, mix, osc, reverb, samples, seconds


def _ring(freqs, dur, tau, rng, jitter=0.03):
    n = samples(dur, S)
    return sum(osc("sine", f * rng.uniform(1 - jitter, 1 + jitter), dur, S) * w * decay(n, tau * (1.0 / (1 + i * 0.4)), S, 0.0005)
               for i, (f, w) in enumerate(freqs))


def hit_metal(rng):
    """A clang: a hard tick and inharmonic ringing partials."""
    dur = 0.4
    n = samples(dur, S)
    tick = highpass(_noise(dur, rng), 3000, S) * decay(n, 0.004, S) * 0.6
    return mix(tick, _ring(((620, 0.5), (1430, 0.3), (2310, 0.2), (3480, 0.12)), dur, 0.16, rng) * 0.8)


def death_metal(rng):
    """A machine breaking apart: a heavy clang, rattling debris and a falling electric whine."""
    dur = 1.0
    n = samples(dur, S)
    t = seconds(n, S)
    clang = _ring(((310, 0.6), (740, 0.4), (1210, 0.25)), dur, 0.35, rng)
    whine = osc("saw", 900 * np.exp(-t / 0.4) + 60, dur, S) * decay(n, 0.5, S) * 0.12
    rattle = bandpass(_noise(dur, rng) * (rng.uniform(0, 1, n) < 0.03), 1500, 6000, S) * decay(n, 0.5, S) * 2.0
    return reverb(mix(clang, lowpass(whine, 3000, S), rattle), S, room=0.5, wet=0.2, tail=0.3)


def hit_stone(rng):
    """A dull knock with grit."""
    return mix(_thud(140 * rng.uniform(0.9, 1.1), 0.25, 0.05, rng, 0.9), bandpass(_noise(0.25, rng), 800, 3500, S) * decay(samples(0.25, S), 0.03, S) * 0.5)


def death_stone(rng):
    """A rumble of falling rocks."""
    dur = 0.9
    out = np.zeros(samples(dur, S))
    for k in range(6):
        s = hit_stone(rng) * (1 - k * 0.12)
        at = int(rng.uniform(0, 0.5) * S)
        out[at:at + len(s)] += s[:len(out) - at]
    return out + lowpass(_noise(dur, rng), 200, S) * decay(len(out), 0.4, S) * 0.8


def hit_plant(rng):
    """A wet, leafy thwack."""
    dur = 0.22
    n = samples(dur, S)
    return mix(_thud(220, dur, 0.04, rng, 0.4) * 0.7, bandpass(_noise(dur, rng), 1500, 7000, S) * decay(n, 0.05, S) * 0.6)


def death_plant(rng):
    """Snapping stems and a rustle of leaves."""
    dur = 0.8
    n = samples(dur, S)
    snaps = bandpass((rng.uniform(0, 1, n) < 0.004) * rng.uniform(-1, 1, n), 1500, 6000, S) * 3.0 * decay(n, 0.4, S)
    rustle = bandpass(_noise(dur, rng), 2000, 8000, S) * decay(n, 0.3, S, 0.02) * 0.5
    return mix(snaps, rustle, _thud(120, dur, 0.15, rng, 0.3) * 0.6)


def hit_chitin(rng):
    """A crisp crunch of shell."""
    dur = 0.2
    n = samples(dur, S)
    crack = bandpass(_noise(dur, rng), 2500, 9000, S) * decay(n, 0.02, S, 0.0005)
    return mix(crack, _thud(300, dur, 0.03, rng, 0.2) * 0.4, _ring(((2900, 0.2), (4300, 0.1)), dur, 0.03, rng))


def death_chitin(rng):
    """A shell cracking open, with a wet pop."""
    dur = 0.6
    n = samples(dur, S)
    t = seconds(n, S)
    pop = osc("sine", 180 * (1 + 2 * np.exp(-t / 0.03)), dur, S) * decay(n, 0.08, S) * 0.6
    return mix(hit_chitin(rng), pop, bandpass(_noise(dur, rng), 1000, 5000, S) * decay(n, 0.15, S, 0.01) * 0.4)


def hit_spirit(rng):
    """A ghostly chime: shimmering partials with a breathy edge."""
    dur = 0.45
    n = samples(dur, S)
    return mix(_ring(((880, 0.4), (1320, 0.3), (1760, 0.2)), dur, 0.2, rng), highpass(_noise(dur, rng), 4000, S) * decay(n, 0.1, S) * 0.15)


def death_spirit(rng):
    """A fading wail that rises and dissolves."""
    dur = 1.1
    n = samples(dur, S)
    t = seconds(n, S)
    f = 420 + 380 * t / dur
    wail = (osc("sine", f, dur, S) + osc("sine", f * 1.5, dur, S) * 0.3) * np.sin(np.pi * t / dur) ** 2
    return reverb(mix(wail * 0.6, highpass(_noise(dur, rng), 3000, S) * decay(n, 0.6, S) * 0.15), S, room=0.8, wet=0.4, tail=0.4)


def death_bone(rng):
    """A skeleton clattering apart."""
    dur = 0.8
    n = samples(dur, S)
    out = np.zeros(n)
    for _ in range(7):
        m = samples(0.12, S)
        clack = bandpass(_noise(0.12, rng), 1200, 5000, S) * decay(m, 0.015, S, 0.0005) + _ring(((1700, 0.3), (2600, 0.15)), 0.12, 0.03, rng)
        at = int(rng.uniform(0, 0.6) * S)
        out[at:at + m] += clack[:n - at]
    return out


RECIPES = {"hit_metal": (hit_metal, 3), "death_metal": (death_metal, 2), "hit_stone": (hit_stone, 3), "death_stone": (death_stone, 2),
           "hit_plant": (hit_plant, 3), "death_plant": (death_plant, 2), "hit_chitin": (hit_chitin, 3), "death_chitin": (death_chitin, 2),
           "hit_spirit": (hit_spirit, 3), "death_spirit": (death_spirit, 2), "death_bone": (death_bone, 2)}

GAIN = {"hit_metal": 0.6, "death_metal": 0.75, "hit_stone": 0.6, "death_stone": 0.7, "hit_plant": 0.55, "death_plant": 0.6,
        "hit_chitin": 0.55, "death_chitin": 0.6, "hit_spirit": 0.45, "death_spirit": 0.55, "death_bone": 0.65}
