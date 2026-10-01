"""Weather one-shots: a close thunder crack that rolls away (thunder_near) and a distant rumble (thunder_far).
Merged into sfx_recipes.RECIPES; played by game/Ambience/WeatherAudio.cs on the Weather bus."""
import numpy as np

from sfx_recipes import S, _noise
from synth import bandpass, decay, highpass, lowpass, mix, reverb, samples, seconds


def _roll(dur, rng, lo, hi, tau, bumps):
    """A rumble: low noise under a slow, lumpy envelope of a few rolling swells."""
    n = samples(dur, S)
    t = seconds(n, S)
    body = lowpass(bandpass(_noise(dur, rng), lo, hi, S), hi * 0.8, S, 3)
    env = np.exp(-t / tau)
    for _ in range(bumps):
        at, width = rng.uniform(0.1, dur * 0.7), rng.uniform(0.2, 0.7)
        env += rng.uniform(0.3, 0.8) * np.exp(-((t - at) / width) ** 2) * np.exp(-t / (tau * 1.5))
    return body * env * np.minimum(t / 0.05, 1.0)


def thunder_near(rng):
    """A splitting crack (bright crackle over a sharp low hit) that turns into a long roll echoing off the land."""
    dur = 4.5
    n = samples(dur, S)
    crack = highpass(_noise(dur, rng), 1500, S) * decay(n, 0.06, S, 0.001)
    crackle = bandpass((rng.uniform(0, 1, n) < 0.02) * rng.uniform(-1, 1, n), 800, 6000, S) * decay(n, 0.35, S, 0.001)
    hit = lowpass(_noise(dur, rng), 260, S, 3) * decay(n, 0.22, S, 0.002) * 3.0
    roll = _roll(dur, rng, 30, 420, 1.3, 4)
    return reverb(mix(crack * 0.6, crackle * 0.9, hit, roll * 2.2), S, room=0.85, damp=0.6, wet=0.35, tail=0.6)


def thunder_far(rng):
    """Distant thunder: no crack, a soft low swell rolling for several seconds."""
    dur = 5.0
    return lowpass(_roll(dur, rng, 25, 260, 1.8, 5), 300, S, 2)


RECIPES = {"thunder_near": (thunder_near, 3), "thunder_far": (thunder_far, 3)}

GAIN = {"thunder_near": 0.9, "thunder_far": 0.75}
