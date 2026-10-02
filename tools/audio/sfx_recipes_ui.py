"""Interface sounds: Terraria's grab, the soft pick-up click of an item moving between inventory slots (grab), and the
hotbar switch tick. Merged into sfx_recipes.RECIPES."""
from sfx_recipes import S, _noise, blip
from synth import bandpass, decay, mix, samples


def grab(rng):
    """A short triangle blip with a tiny papery tick on top: quiet enough to repeat while items are taken one by one."""
    dur = 0.06
    n = samples(dur, S)
    tone = blip(900 * rng.uniform(0.96, 1.04), dur, "triangle", 0.02)(rng)[:n]
    tick = bandpass(_noise(dur, rng), 2000, 6000, S) * decay(n, 0.003, S, 0.0003) * 0.35
    return mix(tone, tick)


def hotbar_tick(rng):
    """Terraria's menu tick on a hotbar switch: a bright 30 ms click that stays out of the way when the wheel spins."""
    dur = 0.03
    n = samples(dur, S)
    tone = blip(2600 * rng.uniform(0.97, 1.03), dur, "sine", 0.006)(rng)[:n]
    tick = bandpass(_noise(dur, rng), 3000, 8000, S) * decay(n, 0.002, S, 0.0002) * 0.3
    return mix(tone, tick)


RECIPES = {"grab": (grab, 2), "hotbar_tick": (hotbar_tick, 1)}

# Cues, not events: as quiet as the menu click (the tick quieter still, it repeats as the wheel turns).
GAIN = {"grab": 0.4, "hotbar_tick": 0.25}
