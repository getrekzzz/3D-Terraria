"""The Goblin Tinkerer's reforge: a hammer striking an anvil (reforge), played on each press of the reforge panel's
hammer. Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S, _noise, _thud
from synth import bandpass, decay, highpass, mix, osc, reverb, samples


def reforge(rng):
    """A bright metallic clang: the hard strike, inharmonic anvil partials ringing out (the higher ones die first) over
    the dull thump of the anvil's body, in a small room."""
    dur = 0.9
    n = samples(dur, S)
    base = 980 * rng.uniform(0.97, 1.03)
    ring = sum(osc("sine", base * r * rng.uniform(0.995, 1.005), dur, S) * w * decay(n, tau, S, 0.0005)
               for r, w, tau in ((1.0, 0.55, 0.32), (2.32, 0.35, 0.2), (3.87, 0.25, 0.12), (5.41, 0.15, 0.07), (7.03, 0.08, 0.04)))
    strike = bandpass(_noise(dur, rng), 2500, 9000, S) * decay(n, 0.004, S, 0.0002)
    body = np.zeros(n)
    thump = _thud(160, 0.18, 0.04, rng, grit=0.4)
    body[:len(thump)] += thump[:n]
    return reverb(mix(ring, highpass(strike, 1500, S) * 0.7, body * 0.35), S, 0.35, 0.3, 0.18, 0.25)


RECIPES = {"reforge": (reforge, 2)}

# Loud and clear over the coins it costs: the event of the panel.
GAIN = {"reforge": 0.75}
