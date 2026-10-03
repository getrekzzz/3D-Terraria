"""The building blocks of the ambience loops: noise colours, wind, and the short calls scattered over a bed (birds,
crickets, owls, frogs, water drips, lava bubbles, fire crackle, chimes). Calls are mono float arrays at SR; beds are
stereo (n, 2)."""
import numpy as np
from scipy import signal

from synth import SR, bandpass, decay, highpass, lowpass, midi_hz, osc, pan, samples, seconds, wander


# ---- noise and beds --------------------------------------------------------------------------------------------------

def coloured(rng, n, slope):
    """Noise with a 1/f^slope spectrum (0 white, 1 pink, 2 brown), unit RMS."""
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.maximum(np.fft.rfftfreq(n, 1 / SR), 1.0)
    x = np.fft.irfft(spec * np.where(f < 25, 0.0, f ** (-slope / 2)), n)  # nothing below hearing: no rumble eating the level
    return x / (np.sqrt(np.mean(x ** 2)) or 1.0)


def seamless(x, xf):
    """Folds the last xf samples of a (len n + xf) bed over its start with an equal-power fade: a click-free loop."""
    n = len(x) - xf
    out = x[:n].copy()
    k = np.linspace(0, np.pi / 2, xf)
    k = k[:, None] if x.ndim == 2 else k
    out[:xf] = x[:xf] * np.sin(k) + x[n:] * np.cos(k)
    return out


def scatter(buf, sound, at, p=0.0, distance=0.0):
    """Adds a call into a stereo loop at sample `at`, wrapping past the end; far calls are quieter and duller."""
    if distance > 0:
        sound = lowpass(sound, 14000 * (1 - distance) + 1200)
    s = pan(sound * (1.0 - 0.75 * distance), p)
    n = len(buf)
    at %= n
    first = min(len(s), n - at)
    buf[at:at + first] += s[:first]
    if first < len(s):
        buf[:len(s) - first] += s[first:]


def resonant_sweep(x, centre, q, block=512):
    """Noise through a narrow resonance whose centre (Hz, per sample) moves: whistling, howling wind."""
    out = np.zeros_like(x)
    zi = np.zeros(2)
    for i in range(0, len(x), block):
        b, a = signal.iirpeak(min(float(centre[i]), SR * 0.45) / (SR / 2), q)
        out[i:i + block], zi = signal.lfilter(b, a, x[i:i + block], zi=zi)
    return out


def wind(rng, n, lo, hi, q, rate=0.12, hiss=0.5, whistle=1.0):
    """One ear of wind: a gusting low rush plus a whistle that wanders between lo and hi Hz."""
    gust = 0.25 + 0.75 * wander(rng, n, rate) ** 1.6
    rush = lowpass(coloured(rng, n, 1.0), hi * 0.9) * gust * hiss
    centre = lo * (hi / lo) ** wander(rng, n, rate * 1.4)
    howl = resonant_sweep(coloured(rng, n, 1.0), centre, q) * gust ** 1.5 * whistle * 3.0
    return rush + howl


def stereo(fn, rng, n, width=0.8):
    """Builds a bed twice with independent randomness, one per ear, partly shared so it sits in the middle too."""
    left, right = fn(rng, n), fn(rng, n)
    mid = (left + right) * 0.5
    return np.stack([mid + (left - mid) * width, mid + (right - mid) * width], axis=1)


# ---- birds --------------------------------------------------------------------------------------------------------

def warble(rng):
    """A songbird's trill: a fast vibrato around 3-4 kHz that rises or falls a little."""
    dur = rng.uniform(0.3, 0.7)
    n = samples(dur)
    t = seconds(n)
    base = rng.uniform(2800, 4300)
    f = base * (1 + 0.09 * np.sin(2 * np.pi * rng.uniform(16, 30) * t)) * (1 + rng.uniform(-0.18, 0.18) * t / dur)
    env = np.sin(np.pi * t / dur) ** 0.7 * (0.55 + 0.45 * np.abs(np.sin(2 * np.pi * rng.uniform(7, 13) * t)))
    return osc("sine", f, dur) * env


def whistle(rng):
    """Two clear notes, the second falling ('tee-yoo')."""
    base = rng.uniform(2100, 3200)
    out = []
    for f0, f1, dur in ((base, base * 1.12, 0.16), (base * 1.06, base * 0.78, 0.26)):
        n = samples(dur)
        f = f0 + (f1 - f0) * seconds(n) / dur
        out.append(osc("sine", f, dur) * np.sin(np.pi * seconds(n) / dur) ** 0.8)
        out.append(np.zeros(samples(rng.uniform(0.04, 0.08))))
    return np.concatenate(out)


def chirps(rng):
    """A run of short falling chirps (a sparrow's chatter)."""
    out = []
    top = rng.uniform(4800, 6200)
    for _ in range(rng.integers(3, 7)):
        dur = rng.uniform(0.035, 0.06)
        n = samples(dur)
        f = top * (1 - 0.4 * seconds(n) / dur)
        out.append(osc("sine", f, dur) * np.sin(np.pi * seconds(n) / dur))
        out.append(np.zeros(samples(rng.uniform(0.05, 0.09))))
    return np.concatenate(out)


def whoop(rng):
    """A tropical bird's rising call, repeated."""
    out = []
    lo = rng.uniform(800, 1300)
    for _ in range(rng.integers(2, 4)):
        dur = 0.28
        n = samples(dur)
        t = seconds(n)
        f = lo * (1 + 0.9 * (t / dur) ** 1.5)
        tone = osc("sine", f, dur) + osc("sine", f * 2, dur) * 0.25
        out.append(tone * np.sin(np.pi * t / dur) ** 1.2)
        out.append(np.zeros(samples(0.12)))
    return np.concatenate(out)


def owl(rng):
    """'Hoo, hoo-hoo': soft low hoots."""
    out = []
    f0 = rng.uniform(330, 400)
    for dur, gap in ((0.42, 0.45), (0.22, 0.12), (0.5, 0.0)):
        n = samples(dur)
        t = seconds(n)
        f = f0 * (1.04 - 0.08 * t / dur)
        hoot = osc("sine", f, dur) + osc("sine", f * 2, dur) * 0.12
        out.append(hoot * np.sin(np.pi * t / dur) ** 1.8)
        out.append(np.zeros(samples(gap)))
    return lowpass(np.concatenate(out), 1400)


def gull(rng):
    """A distant gull's cry: a nasal falling 'kyow'."""
    dur = rng.uniform(0.35, 0.5)
    n = samples(dur)
    t = seconds(n)
    f = rng.uniform(1100, 1400) * (1.15 - 0.35 * (t / dur) ** 0.7)
    tone = bandpass(osc("saw", f, dur), 900, 3800)
    return tone * np.sin(np.pi * t / dur) ** 0.6 * 0.6


# ---- insects and frogs ------------------------------------------------------------------------------------------------

def crickets(rng, n, pitch, rate, level):
    """One cricket: a ~4-5 kHz tone gated into chirps of three quick pulses, a few chirps a second."""
    t = seconds(n)
    r = rate * (1 + 0.06 * (wander(rng, n, 0.3) - 0.5))
    phase = np.cumsum(r) / SR
    gate = ((phase % 1.0) < 0.094 * rate).astype(float)  # three 32 Hz pulses per chirp
    pulses = np.clip(np.sin(2 * np.pi * 32 * t), 0, 1) ** 2
    env = lowpass(gate * pulses, 400) * (0.5 + 0.5 * wander(rng, n, 0.08))
    return osc("sine", pitch, n / SR)[:n] * env * level


def cicadas(rng, n, centre):
    """A swelling cicada buzz: a high noise band shaken ~150 times a second, rising and fading over seconds."""
    t = seconds(n)
    buzz = bandpass(rng.standard_normal(n), centre * 0.85, centre * 1.15)
    shake = 0.45 + 0.55 * np.abs(np.sin(2 * np.pi * rng.uniform(120, 170) * t))
    swell = wander(rng, n, 0.12) ** 2.5
    return buzz * shake * swell


def frog(rng):
    """A croak: a buzzy low tone chopped ~25 times a second, often twice."""
    out = []
    f = rng.uniform(220, 420)
    for _ in range(rng.integers(1, 3)):
        dur = rng.uniform(0.18, 0.3)
        n = samples(dur)
        t = seconds(n)
        chop = np.clip(np.sin(2 * np.pi * rng.uniform(20, 30) * t), 0, 1)
        out.append(bandpass(osc("saw", f, dur), 180, 1200) * chop * np.sin(np.pi * t / dur) ** 0.5)
        out.append(np.zeros(samples(rng.uniform(0.1, 0.2))))
    return np.concatenate(out)


def bee(rng, n):
    """One bee's buzz drifting near and far (a mono track and its loudness curve)."""
    t = seconds(n)
    f = rng.uniform(180, 260) * (1 + 0.04 * (wander(rng, n, 0.8) - 0.5) + 0.012 * np.sin(2 * np.pi * rng.uniform(5, 9) * t))
    near = wander(rng, n, 0.25) ** 2
    return bandpass(osc("saw", f, n / SR)[:n], 250, 2600) * near, near


# ---- water, fire, glass -----------------------------------------------------------------------------------------------

def drip(rng):
    """A water drop into a pool: a short 'plink' whose pitch jumps up."""
    dur = 0.12
    n = samples(dur)
    t = seconds(n)
    f0 = rng.uniform(900, 2100)
    f = f0 * (1 + 12.0 * t)  # the pitch keeps rising as the ring dies, like a drop into a pool
    return osc("sine", f, dur) * decay(n, rng.uniform(0.01, 0.018), attack=0.001)


def bubble(rng):
    """A lava bubble: a low rising blorp that ends in a soft pop."""
    dur = rng.uniform(0.12, 0.22)
    n = samples(dur)
    t = seconds(n)
    f = rng.uniform(70, 150) * (1 + 1.4 * t / dur)
    blorp = osc("sine", f, dur) * np.sin(np.pi * t / dur) ** 0.8
    pop = np.zeros(n)
    k = samples(0.012)
    pop[-k:] = highpass(rng.standard_normal(k), 900) * decay(k, 0.003)
    return blorp + pop * 0.35


def crackle(rng):
    """One spark of a fire: a tiny bright click."""
    n = samples(rng.uniform(0.003, 0.008))
    return highpass(rng.standard_normal(n), rng.uniform(1500, 4000)) * decay(n, 0.0015, attack=0.0002)


def tinkle(rng):
    """A glassy chime on a high pentatonic note."""
    note = 84 + [0, 2, 4, 7, 9, 12, 14, 16][rng.integers(8)]
    dur = 1.2
    n = samples(dur)
    f = midi_hz(note)
    return sum(osc("sine", f * r, dur) * w * decay(n, 0.5 / r ** 0.6) for r, w in ((1, 0.6), (2.76, 0.2), (5.4, 0.08)))
