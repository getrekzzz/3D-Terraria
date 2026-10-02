"""Town: petting a town pet (a soft two-note chirp with a little sparkle) and the residents' voice blips (X-05): one short
syllable per variant, a pitched buzz shaped by two vowel formants, Animal Crossing / Terraria-style babble that the game
replays per group of dialogue characters. Merged into sfx_recipes.RECIPES."""
import numpy as np

from sfx_recipes import S
from synth import bandpass, decay, fade, highpass, lowpass, osc, reverb, samples


def pet(rng):
    """Two warm sine chirps rising a fifth, then a faint shimmer: a happy little pat."""
    dur = 0.5
    n = samples(dur, S)
    out = np.zeros(n)
    base = 660 * rng.uniform(0.97, 1.03)
    for k, f in enumerate((base, base * 1.5)):
        i = int(0.09 * k * S)
        tone = osc("sine", f, dur, S) + osc("sine", f * 2, dur, S) * 0.2
        out[i:] += (tone * decay(n, 0.09, S, 0.004))[:n - i] * 0.3
    i = int(0.18 * S)
    out[i:] += (osc("sine", base * 4, dur, S) * decay(n, 0.05, S, 0.002))[:n - i] * 0.06
    return reverb(out, S)


# Vowel formants (F1, F2) in Hz: a, e, i, o, u.
VOWELS = ((800, 1200), (500, 1800), (300, 2300), (500, 900), (330, 800))


def _syllable(rng, f0, dur, kind="saw", glide=0.0, breath=0.0, bright=1.0):
    """A sung syllable: a glottal buzz at f0 (gliding by `glide` octaves), filtered through a random vowel."""
    n = samples(dur, S)
    t = np.arange(n) / S
    f = f0 * rng.uniform(0.95, 1.05) * 2 ** (glide * t / dur) * (1 + 0.02 * np.sin(2 * np.pi * 6 * t))
    src = osc(kind, f, dur, S)
    if breath:
        src = src + osc("noise", f0, dur, S) * breath
    f1, f2 = VOWELS[int(rng.integers(len(VOWELS)))]
    voiced = bandpass(src, f1 * 0.7, f1 * 1.3, S) * 1.0 + bandpass(src, f2 * 0.8, min(f2 * 1.2 * bright, S / 2 - 100), S) * 0.6
    env = np.minimum(1.0, t / 0.008) * np.exp(-t / (dur * 0.45))
    return fade(voiced * env * 0.9, 0.002, 0.012, S)


def voice(f0, dur=0.09, kind="saw", glide=0.0, breath=0.0, bright=1.0, post=None):
    def make(rng):
        x = _syllable(rng, f0, dur, kind, glide * rng.uniform(0.6, 1.4), breath, bright)
        return post(x) if post else x
    return make


def _crunch(x):
    return np.clip(x * 3.0, -0.7, 0.7)


RECIPES = {
    "pet": (pet, 2),
    "voice_low": (voice(105, 0.1), 3),
    "voice_mid": (voice(140, 0.09), 3),
    "voice_high": (voice(230, 0.085, glide=0.15), 3),
    "voice_child": (voice(310, 0.075, glide=0.2), 3),
    "voice_old": (voice(125, 0.11, breath=0.25, glide=-0.1), 3),
    "voice_gruff": (voice(95, 0.1, kind="square", breath=0.35, post=lambda x: lowpass(x, 2200, S)), 3),
    "voice_squeak": (voice(420, 0.06, kind="square", glide=0.35), 3),
    "voice_robot": (voice(160, 0.08, kind="square", post=_crunch), 3),
    "voice_bones": (voice(180, 0.06, kind="triangle", breath=0.6, post=lambda x: highpass(x, 900, S)), 3),
    "voice_fungus": (voice(200, 0.1, kind="sine", glide=-0.3), 3),
    "voice_cat": (voice(520, 0.16, kind="saw", glide=0.6, bright=1.3), 3),
    "voice_dog": (voice(240, 0.07, kind="saw", glide=-0.5, breath=0.3), 3),
    "voice_bunny": (voice(900, 0.04, kind="sine", glide=0.4), 3),
}
