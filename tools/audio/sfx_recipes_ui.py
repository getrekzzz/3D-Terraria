"""Interface sounds: Terraria's grab, the soft pick-up click of an item moving between inventory slots (grab). Merged
into sfx_recipes.RECIPES."""
from sfx_recipes import S, _noise, blip
from synth import bandpass, decay, mix, samples


def grab(rng):
    """A short triangle blip with a tiny papery tick on top: quiet enough to repeat while items are taken one by one."""
    dur = 0.06
    n = samples(dur, S)
    tone = blip(900 * rng.uniform(0.96, 1.04), dur, "triangle", 0.02)(rng)[:n]
    tick = bandpass(_noise(dur, rng), 2000, 6000, S) * decay(n, 0.003, S, 0.0003) * 0.35
    return mix(tone, tick)


RECIPES = {"grab": (grab, 2)}

# A cue, not an event: as quiet as the menu click.
GAIN = {"grab": 0.4}
