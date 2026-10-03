"""Combat sounds: striking a creature (flesh, gel, bone), a creature dying, and the player getting hurt. Every hit is built
the same way so they read as one family: a bright contact click, a mid-range smack that says what was struck, a short
pitched body and a little low weight, lightly saturated so it punches through the music."""
import numpy as np

from synth import adsr, bandpass, decay, fade, highpass, lowpass, mix, osc, peaking, samples, seconds

S = 22050


def _noise(dur, rng):
    return rng.uniform(-1, 1, samples(dur, S))


def _punch(x, drive=1.8):
    return np.tanh(x * drive) / np.tanh(drive)


def impact(rng, weight=1.0, dur=0.22):
    """The shared core of a hit: click, smack, body, weight. Heavier hits sit lower and ring a little longer."""
    n = samples(dur, S)
    t = seconds(n, S)
    click = bandpass(_noise(dur, rng), 2500, 7500, S) * decay(n, 0.0025, S, attack=0.0003) * 0.8
    smack = bandpass(_noise(dur, rng), 700, 2400, S) * decay(n, 0.02 * weight ** 0.5, S, attack=0.0008)
    f = 175 * rng.uniform(0.9, 1.1) / weight ** 0.3 * (1 + 0.9 * np.exp(-t / 0.012))
    body = osc("sine", f, dur, S) * decay(n, 0.04 * weight ** 0.5, S, attack=0.001) * 0.9
    low = osc("sine", 70 * (1 + 0.6 * np.exp(-t / 0.02)), dur, S) * decay(n, 0.05, S) * 0.45 * weight
    return fade(mix(click, smack, body, low), 0.0005, 0.04, S)


def hit(rng):
    """Striking flesh: a tight thwack with a soft fleshy tail."""
    dur = 0.22
    n = samples(dur, S)
    flesh = lowpass(_noise(dur, rng), 1300, S) * decay(n, 0.035, S) * 0.45
    return _punch(mix(impact(rng, 1.0, dur), flesh))


def hit_squish(rng):
    """Striking gel: a wet slap and a wobbling blorp that drops in pitch."""
    dur = 0.28
    n = samples(dur, S)
    t = seconds(n, S)
    f = rng.uniform(420, 560) * np.exp(-t / 0.09) * (1 + 0.12 * np.sin(2 * np.pi * rng.uniform(22, 30) * t)) + 140
    blorp = osc("sine", f, dur, S) * decay(n, 0.07, S, attack=0.002) * 0.8
    wet = lowpass(_noise(dur, rng), 1800, S) * (0.75 + 0.25 * np.sin(2 * np.pi * 37 * t)) * decay(n, 0.03, S) * 0.4
    slap = impact(rng, 0.8, dur) * 0.6
    return fade(_punch(mix(slap, blorp, wet), 1.4), 0.0005, 0.04, S)


def hit_bone(rng):
    """Striking bone: a dry knock and a short clatter of hollow ticks."""
    dur = 0.24
    n = samples(dur, S)
    out = impact(rng, 0.7, dur) * 0.6
    at = 0
    for k in range(rng.integers(2, 4)):
        m = samples(0.06, S)
        tick = sum(peaking(_noise(0.06, rng), f * rng.uniform(0.92, 1.08), 14, S) for f in (1150, 1750, 2700))
        tick *= decay(m, 0.012, S, attack=0.0004) * (1.6 - 0.4 * k)
        out[at:at + m] += tick[:n - at]
        at += samples(rng.uniform(0.018, 0.04), S)
    return _punch(out, 1.6)


def npc_death(rng):
    """A creature going down: a heavy hit, a body slumping (a falling thud) and a soft puff."""
    dur = 0.6
    n = samples(dur, S)
    t = seconds(n, S)
    heavy = mix(impact(rng, 1.5, dur), lowpass(_noise(dur, rng), 1100, S) * decay(n, 0.09, S) * 0.5)
    slump = osc("sine", 150 * rng.uniform(0.9, 1.1) * np.exp(-t / 0.25) + 45, dur, S) * adsr(n, 0.04, 0.12, 0.4, 0.3, S) * 0.5
    puff = bandpass(_noise(dur, rng), 300, 1800, S) * decay(n, 0.07, S, attack=0.03) * 0.2
    return _punch(mix(heavy, slump, puff), 1.5)


VOWELS = {"uh": (640, 1190, 2390), "oh": (500, 880, 2450), "eh": (560, 1700, 2500), "ah": (750, 1250, 2600)}


def grunt(rng, vowel, f0, dur):
    """A short voiced grunt: a buzzing glottal source with a little pitch wobble through three vowel formants, opening
    with a breath."""
    n = samples(dur, S)
    t = seconds(n, S)
    contour = (1 + 0.06 * np.minimum(t / 0.02, 1)) * (1 - 0.22 * t / dur)
    jitter = 1 + 0.012 * lowpass(rng.standard_normal(n), 30, S) / 0.1
    source = lowpass(osc("saw", f0 * contour * jitter, dur, S), 3500, S) + _noise(dur, rng) * 0.08
    breath = highpass(_noise(dur, rng), 600, S) * decay(n, 0.02, S) * 0.5
    f1, f2, f3 = VOWELS[vowel]
    voiced = peaking(source, f1, 7, S) + peaking(source, f2, 11, S) * 0.55 + peaking(source, f3, 16, S) * 0.25
    env = adsr(n, 0.012, 0.05, 0.7, dur * 0.45, S) * (1 - t / dur) ** 0.6
    return lowpass(voiced * env * 2.2 + breath, 4200, S)


def player_hurt(rng):
    """The player taking a hit: the blow lands, and a short 'ugh' follows a beat later."""
    dur = rng.uniform(0.2, 0.26)
    vowel = ["uh", "oh", "eh", "ah"][rng.integers(4)]
    voice = grunt(rng, vowel, rng.uniform(165, 205), dur)
    blow = impact(rng, 1.1, 0.2)
    out = np.zeros(samples(0.02, S) + len(voice))
    out[:len(blow)] += blow * 0.75
    out[samples(0.02, S):] += voice
    return _punch(out, 1.3)


def slime_death(rng):
    """A slime bursting: a wet splat, then the gel collapsing in two falling blorps."""
    out = np.zeros(samples(0.55, S))
    splat = mix(impact(rng, 0.9, 0.3) * 0.5, lowpass(_noise(0.3, rng), 2500, S) * decay(samples(0.3, S), 0.06, S) * 0.6)
    out[:len(splat)] += splat
    for k, at in enumerate((0.04, 0.17)):
        b = hit_squish(rng) * (0.8 - 0.25 * k)
        i = samples(at, S)
        out[i:i + len(b)] += b[:len(out) - i]
    return _punch(out, 1.3)


def chop(rng):
    """An axe or pick biting into wood: a sharp contact, a hollow woody 'tok' from a few short trunk resonances, and a
    couple of splinters cracking."""
    dur = 0.25
    n = samples(dur, S)
    t = seconds(n, S)
    click = bandpass(_noise(dur, rng), 2000, 6000, S) * decay(n, 0.002, S, attack=0.0002) * 0.6
    strike = _noise(dur, rng) * decay(n, 0.004, S, attack=0.0003)
    tok = sum(peaking(strike, f * rng.uniform(0.92, 1.08), q, S) * w for f, q, w in ((185, 18, 1.0), (420, 22, 0.7), (960, 24, 0.35), (1900, 26, 0.15)))
    thump = osc("sine", 110 * (1 + 0.5 * np.exp(-t / 0.01)), dur, S) * decay(n, 0.03, S) * 0.35
    splinters = np.zeros(n)
    for _ in range(rng.integers(2, 5)):
        at, m = samples(rng.uniform(0.008, 0.05), S), samples(0.004, S)
        splinters[at:at + m] += highpass(_noise(0.004, rng), 3000, S) * decay(m, 0.001, S, attack=0.0002) * rng.uniform(0.05, 0.12)
    return fade(_punch(mix(click, tok * 5.0, thump, splinters), 1.5), 0.0005, 0.04, S)
