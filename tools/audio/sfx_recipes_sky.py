"""Night sky one-shots (P3-30): a Fallen Star streaking down (star_fall: a bright rising-then-falling whoosh with a
shimmer) and landing (star_land: a soft thump under a glassy chime). Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, highpass, lowpass, mix, osc, reverb, samples, seconds


def star_fall(rng):
    """A long airy whoosh that swells and passes, with a high twinkling shimmer riding on it."""
    dur = 1.6
    n = samples(dur, S)
    t = seconds(n, S)
    swell = np.exp(-((t - rng.uniform(0.7, 0.9)) / 0.42) ** 2)
    air = bandpass(_noise(dur, rng), 500, 4500, S) * swell
    pitch = rng.uniform(2400, 3000) * (1 - 0.35 * t / dur)
    shimmer = osc("sine", pitch, dur, S) * (0.5 + 0.5 * np.sin(2 * np.pi * 23 * t)) * swell * 0.18
    sparkle = highpass((rng.uniform(0, 1, n) < 0.004) * rng.uniform(-1, 1, n), 5000, S) * swell * 0.6
    return reverb(mix(air * 0.9, shimmer, sparkle), S, room=0.7, damp=0.4, wet=0.3, tail=0.6)


def star_land(rng):
    """A muffled thump into the ground and a bright, ringing two-note chime."""
    dur = 1.4
    n = samples(dur, S)
    thump = lowpass(_noise(dur, rng), 220, S, 3) * decay(n, 0.05, S, 0.002) * 1.4
    base = rng.uniform(1300, 1500)
    chime = mix(osc("sine", base, dur, S) * decay(n, 0.5, S, 0.002),
                osc("sine", base * 1.5, dur, S) * decay(n, 0.35, S, 0.002) * 0.6,
                osc("sine", base * 2.76, dur, S) * decay(n, 0.12, S, 0.001) * 0.3)
    return reverb(mix(thump, chime * 0.5), S, room=0.6, damp=0.3, wet=0.3, tail=0.8)


RECIPES = {"star_fall": (star_fall, 2), "star_land": (star_land, 2)}

GAIN = {"star_fall": 0.55, "star_land": 0.6}
