"""Item sound sets (P3-27): the sounds an item's soundSet or its use style's default names (data/config/use_styles.json
"sound"): shotguns boom, snipers crack, lasers and staffs zap, crystals chime, food crunches, thrown things, whips, yoyos
and flails whoosh. Merged into sfx_recipes.RECIPES. Later WPs append their own section below, in WP order."""
import numpy as np

from sfx_recipes import S, _noise, _thud
from synth import adsr, bandpass, decay, highpass, lowpass, mix, osc, reverb, samples, seconds, sweep_lowpass


def shotgun(rng):
    """A wide boom: a long noisy blast, a deep thump, and the clack of the pump a beat later."""
    dur = 0.6
    n = samples(dur, S)
    blast = lowpass(_noise(dur, rng), 4200, S) * decay(n, 0.06, S)
    boom = _thud(58 * rng.uniform(0.92, 1.08), dur, 0.14, rng, 0.9) * 1.4
    out = mix(blast, boom)
    at = int(rng.uniform(0.3, 0.36) * S)
    clack = bandpass(_noise(0.05, rng), 1500, 5000, S) * decay(samples(0.05, S), 0.006, S, 0.0003) * 0.35
    out[at:at + len(clack)] += clack[:n - at]
    return reverb(out, S, room=0.5, wet=0.18, tail=0.4)


def sniper(rng):
    """A sharp supersonic crack with a long rolling echo behind it."""
    dur = 0.9
    n = samples(dur, S)
    crack = highpass(_noise(dur, rng), 2200, S) * decay(n, 0.008, S, 0.0002) * 0.9
    body = _thud(95 * rng.uniform(0.95, 1.05), dur, 0.05, rng, 0.6) * 1.1
    roll = lowpass(_noise(dur, rng), 900, S) * adsr(n, 0.05, 0.2, 0.3, 0.5, S) * 0.45
    return reverb(mix(crack, body, roll), S, room=0.8, damp=0.5, wet=0.3, tail=0.8)


def laser(rng):
    """A bright falling zap: a square tone diving down an octave with a buzzy edge."""
    dur = 0.22
    n = samples(dur, S)
    t = seconds(n, S)
    f = 2400 * rng.uniform(0.9, 1.1) * np.exp(-t / 0.07) + 380
    zap = osc("pulse", f, dur, S, duty=0.3) * 0.4 + osc("sine", f * 0.5, dur, S) * 0.4
    return lowpass(zap * decay(n, 0.07, S, 0.001), 7000, S)


def staff_bolt(rng):
    """A crystalline bolt: a quick rising sparkle over a soft whoosh."""
    dur = 0.38
    n = samples(dur, S)
    t = seconds(n, S)
    base = rng.uniform(900, 1200)
    f = base * (1 + 0.6 * (1 - np.exp(-t / 0.04)))
    tone = sum(osc("sine", f * k, dur, S) * w for k, w in ((1, 0.5), (2.01, 0.25), (3.02, 0.12))) * decay(n, 0.1, S, 0.004)
    air = sweep_lowpass(highpass(_noise(dur, rng), 600, S), 2500, 7000, S) * adsr(n, 0.02, 0.1, 0.3, 0.2, S) * 0.3
    return reverb(mix(tone, air), S, room=0.5, wet=0.28, tail=0.3)


def water_bolt(rng):
    """A watery bloop: a bubbling pitch rise with a splashy hiss."""
    dur = 0.4
    n = samples(dur, S)
    t = seconds(n, S)
    f = 260 * rng.uniform(0.9, 1.1) * (1 + 2.5 * (1 - np.exp(-t / 0.05))) * (1 + 0.08 * np.sin(2 * np.pi * 28 * t))
    bloop = osc("sine", f, dur, S) * decay(n, 0.09, S, 0.003) * 0.7
    hiss = bandpass(_noise(dur, rng), 1200, 6000, S) * decay(n, 0.12, S, 0.01) * 0.35
    return reverb(mix(bloop, hiss), S, room=0.6, wet=0.25, tail=0.3)


def throw(rng):
    """A short wrist flick: a quick airy whoosh rising in brightness."""
    dur = 0.22
    n = samples(dur, S)
    env = np.sin(np.linspace(0, np.pi, n)) ** 2
    return sweep_lowpass(highpass(_noise(dur, rng), 400, S) * env, 1200 * rng.uniform(0.9, 1.1), 5500, S)


def whip_swing(rng):
    """A whip lashing out: a rising whoosh that ends in a sharp crack."""
    dur = 0.4
    n = samples(dur, S)
    env = np.linspace(0, 1, n) ** 2 * (np.linspace(1, 0, n) > 0.2)
    swish = sweep_lowpass(_noise(dur, rng) * env, 600, 4500, S) * 0.6
    at = int(rng.uniform(0.27, 0.31) * S)
    crack = highpass(_noise(0.08, rng), 2500, S) * decay(samples(0.08, S), 0.006, S, 0.0002) * 1.2
    out = swish.copy()
    out[at:at + len(crack)] += crack[:n - at]
    return out


def yoyo_throw(rng):
    """The yoyo spun out: a whirring tone that rises as the string unwinds."""
    dur = 0.45
    n = samples(dur, S)
    t = seconds(n, S)
    whirr = osc("triangle", 180 + 220 * t / dur, dur, S) * (0.6 + 0.4 * np.sin(2 * np.pi * 32 * t))
    air = bandpass(_noise(dur, rng), 800, 3000, S) * 0.3
    return mix(whirr * 0.5, air) * adsr(n, 0.03, 0.15, 0.6, 0.15, S)


def flail_spin(rng):
    """A heavy ball whirled round: low, pulsing whooshes and the rattle of the chain."""
    dur = 0.55
    n = samples(dur, S)
    t = seconds(n, S)
    pulse = (0.5 + 0.5 * np.sin(2 * np.pi * 7 * t - np.pi / 2)) ** 2
    whoosh = lowpass(_noise(dur, rng), 900, S) * pulse
    rattle = bandpass(_noise(dur, rng) * (rng.uniform(0, 1, n) < 0.02), 2500, 7000, S) * 1.5
    return mix(whoosh, rattle * 0.4) * adsr(n, 0.04, 0.1, 0.8, 0.12, S)


def eat(rng):
    """Two or three crunchy bites: short bursts of mid noise with a soft thump."""
    dur = 0.5
    n = samples(dur, S)
    out = np.zeros(n)
    at = 0
    for _ in range(int(rng.integers(2, 4))):
        bite = mix(bandpass(_noise(0.07, rng), 700, 3500, S) * decay(samples(0.07, S), 0.02, S, 0.002),
                   _thud(120 * rng.uniform(0.85, 1.15), 0.07, 0.015, rng, 0.2) * 0.4)
        out[at:at + len(bite)] += bite[:n - at]
        at += int(rng.uniform(0.12, 0.17) * S)
        if at >= n:
            break
    return lowpass(out, 4000, S)


def _chime(freqs, dur, rng, tau=0.5):
    n = samples(dur, S)
    ring = sum(osc("sine", f * rng.uniform(0.995, 1.005), dur, S) * w * decay(n, tau / (1 + i * 0.5), S, 0.002)
               for i, (f, w) in enumerate(freqs))
    return reverb(ring, S, room=0.7, wet=0.35, tail=0.7)


def crystal(rng):
    """A warm crystal chime: a rising major arpeggio over a ringing body (Life Crystals and Life Fruit)."""
    dur = 1.2
    n = samples(dur, S)
    out = _chime(((523, 0.4), (1046, 0.2), (1568, 0.1)), dur, rng)[:n] * 0.6
    for k, f in enumerate((659, 784, 1046)):
        at = int((0.06 + k * 0.08) * S)
        note = osc("sine", f, dur, S) * decay(n, 0.3, S, 0.003) * 0.35
        out[at:] += note[:n - at]
    return out


def mana_crystal(rng):
    """A cool, glassy chime with a shimmering high partial (Mana Crystals)."""
    dur = 1.1
    n = samples(dur, S)
    t = seconds(n, S)
    body = _chime(((880, 0.4), (1320, 0.25), (2640, 0.12)), dur, rng)[:n]
    shimmer = osc("sine", 3520 * (1 + 0.004 * np.sin(2 * np.pi * 6 * t)), dur, S) * decay(n, 0.4, S, 0.05) * 0.12
    return mix(body, shimmer)


RECIPES = {
    "shotgun": (shotgun, 2), "sniper": (sniper, 2), "laser": (laser, 3), "staff_bolt": (staff_bolt, 3),
    "water_bolt": (water_bolt, 2), "throw": (throw, 3), "whip_swing": (whip_swing, 2), "yoyo_throw": (yoyo_throw, 2),
    "flail_spin": (flail_spin, 2), "eat": (eat, 3), "crystal": (crystal, 1), "mana_crystal": (mana_crystal, 1),
}

# Peak levels: guns sit with gun/gun_heavy (0.8), whooshes with swing (0.55), chimes under the music.
GAIN = {"laser": 0.6, "staff_bolt": 0.6, "water_bolt": 0.6, "throw": 0.5, "whip_swing": 0.6, "yoyo_throw": 0.45,
        "flail_spin": 0.5, "eat": 0.55, "crystal": 0.6, "mana_crystal": 0.6}
