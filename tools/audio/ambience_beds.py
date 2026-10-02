"""Biome, cave and underworld beds for tools/audio/ambience.py: birdsong, crickets and frogs, surf, desert and tundra
wind, cave air and drips, the deep cavern rumble, lava, the mushroom hum, the evil drone, hallowed chimes, the
dungeon's halls, the sky's thin wind and the hive's buzz. Each takes (sample count, numpy Generator)."""
import numpy as np

from synth import bandpass, highpass, lowpass, reverb

SR = 22050


def _noise(n, rng):
    return rng.uniform(-1, 1, n)


def _smooth(n, rng, rate, lo=0.0, hi=1.0):
    k = max(4, int(n / SR * rate) + 2)
    return np.interp(np.linspace(0, k - 1, n), np.arange(k), rng.uniform(lo, hi, k))


def _place(buf, x, at):
    at = int(at) % len(buf)
    end = min(len(buf), at + len(x))
    buf[at:end] += x[:end - at]


def _sweep_tone(dur, f0, f1, tau):
    n = int(dur * SR)
    t = np.arange(n) / SR
    f = f0 * (f1 / f0) ** (t / dur)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.minimum(t / 0.004, 1) * np.exp(-t / tau)


def _bird(rng):
    """A short song: two to six chirps sweeping in a few kHz, with a little trill."""
    out = np.zeros(int(SR * 1.4))
    at = 0.0
    base = rng.uniform(2200, 4200)
    for _ in range(rng.integers(2, 7)):
        d = rng.uniform(0.05, 0.14)
        f0 = base * rng.uniform(0.8, 1.25)
        chirp = _sweep_tone(d, f0, f0 * rng.uniform(0.7, 1.5), d * 0.6)
        chirp *= 1 + 0.4 * np.sin(2 * np.pi * rng.uniform(25, 45) * np.arange(len(chirp)) / SR)
        _place(out, chirp * rng.uniform(0.5, 1.0), at * SR)
        at += d + rng.uniform(0.02, 0.12)
    return out


def _calls(n, rng, per_second, make, gain):
    out = np.zeros(n)
    for _ in range(int(n / SR * per_second)):
        _place(out, make(rng) * gain * rng.uniform(0.3, 1.0), rng.uniform(0, n))
    return out


def _breeze(n, rng, lo, hi, level):
    return bandpass(_noise(n, rng), lo, hi, SR) * _smooth(n, rng, 0.25, 0.3, 1.0) * level


def _crickets(n, rng, voices=4):
    t = np.arange(n) / SR
    out = np.zeros(n)
    for _ in range(voices):
        f = rng.uniform(3800, 5200)
        rate, chirp = rng.uniform(22, 34), rng.uniform(1.5, 3.2)
        pulse = (np.sin(2 * np.pi * rate * t + rng.uniform(0, 6)) > 0.2).astype(float)
        gate = (np.sin(2 * np.pi * (1 / chirp) * t + rng.uniform(0, 6)) > -0.1).astype(float)
        out += lowpass(pulse * gate, 400, SR) * np.sin(2 * np.pi * f * t) * rng.uniform(0.15, 0.35)
    return out


def _frog(rng):
    d = rng.uniform(0.18, 0.4)
    n = int(d * SR)
    t = np.arange(n) / SR
    f = rng.uniform(280, 620)
    body = np.sign(np.sin(2 * np.pi * f * t)) * (np.sin(2 * np.pi * rng.uniform(18, 30) * t) > 0)
    return lowpass(body, 1400, SR) * np.sin(np.pi * t / d) * 0.6


def _drip(rng):
    d = 0.35
    n = int(d * SR)
    t = np.arange(n) / SR
    f = rng.uniform(1300, 2800)
    tone = np.sin(2 * np.pi * np.cumsum(f * (1 + 0.6 * np.exp(-t / 0.01))) / SR)
    return tone * np.minimum(t / 0.001, 1) * np.exp(-t / 0.05)


def _pop(rng):
    d = 0.25
    n = int(d * SR)
    t = np.arange(n) / SR
    f = rng.uniform(60, 140)
    return np.sin(2 * np.pi * np.cumsum(f * (1 + 2 * np.exp(-t / 0.02))) / SR) * np.exp(-t / 0.06)


def _chime(rng):
    d = 2.5
    n = int(d * SR)
    t = np.arange(n) / SR
    f = rng.choice([1046.5, 1174.7, 1318.5, 1568.0, 1760.0, 2093.0])
    return sum(np.sin(2 * np.pi * f * r * t) * w * np.exp(-t / (1.2 / r)) for r, w in ((1, 0.6), (2.76, 0.2), (5.4, 0.1)))


def forest_day(n, rng):
    return _breeze(n, rng, 200, 2000, 0.25) + _calls(n, rng, 0.9, _bird, 0.55)


def forest_night(n, rng):
    return _breeze(n, rng, 150, 1200, 0.12) + _crickets(n, rng) + _calls(n, rng, 0.15, _frog, 0.25)


def jungle_day(n, rng):
    t = np.arange(n) / SR
    insects = sum(bandpass(_noise(n, rng), f, f * 1.3, SR) * (0.6 + 0.4 * np.sin(2 * np.pi * rng.uniform(0.1, 0.3) * t)) for f in (2600, 5200))
    return insects * 0.5 + _calls(n, rng, 1.4, _bird, 0.6) + _breeze(n, rng, 150, 1500, 0.15)


def jungle_night(n, rng):
    return _crickets(n, rng, 6) * 0.8 + _calls(n, rng, 1.2, _frog, 0.5) + _breeze(n, rng, 120, 900, 0.1)


def ocean(n, rng):
    t = np.arange(n) / SR
    swell = 0.5 + 0.5 * np.sin(2 * np.pi * t / 7.0 + np.sin(2 * np.pi * t / 23.0))
    noise = _noise(n, rng)
    dull, bright = lowpass(noise, 900, SR), lowpass(noise, 3400, SR)
    surf = (dull + (bright - dull) * swell ** 2) * (0.3 + 0.9 * swell ** 3)
    return surf + highpass(_noise(n, rng), 4000, SR) * swell ** 4 * 0.2


def desert(n, rng):
    return _breeze(n, rng, 120, 900, 0.5) + bandpass(_noise(n, rng), 3000, 7000, SR) * _smooth(n, rng, 0.5, 0, 0.15)


def tundra(n, rng):
    whistle = bandpass(_noise(n, rng), 900, 1400, SR, 3) * _smooth(n, rng, 0.3, 0.0, 1.0) ** 2 * 0.8
    return _breeze(n, rng, 100, 700, 0.5) + whistle


def evil(n, rng):
    t = np.arange(n) / SR
    drone = sum(np.sin(2 * np.pi * f * t) * w for f, w in ((55, 0.5), (58.3, 0.4), (82.4, 0.25), (116.5, 0.12)))
    whisper = bandpass(_noise(n, rng), 1500, 4000, SR) * _smooth(n, rng, 0.4, 0, 0.5) ** 2
    return drone * (0.7 + 0.3 * _smooth(n, rng, 0.2)) + whisper * 0.5


def hallow(n, rng):
    return _calls(n, rng, 0.6, _chime, 0.4) + _breeze(n, rng, 2000, 7000, 0.06)


def sky(n, rng):
    return _breeze(n, rng, 400, 3000, 0.4) + highpass(_noise(n, rng), 5000, SR) * 0.03


def cave(n, rng):
    air = bandpass(_noise(n, rng), 50, 260, SR) * 0.6 * _smooth(n, rng, 0.15, 0.6, 1.0)
    drips = reverb(_calls(n, rng, 0.5, _drip, 0.35), SR, room=0.8, damp=0.5, wet=0.45, tail=0)[:n]
    return air + drips


def cavern(n, rng):
    rumble = lowpass(_noise(n, rng), 90, SR, 3) * 2.2 * _smooth(n, rng, 0.1, 0.5, 1.0)
    drips = reverb(_calls(n, rng, 0.3, _drip, 0.3), SR, room=0.95, damp=0.4, wet=0.6, tail=0)[:n]
    return rumble + drips + bandpass(_noise(n, rng), 60, 200, SR) * 0.3


def underworld(n, rng):
    rumble = lowpass(_noise(n, rng), 120, SR, 3) * 3.0 * _smooth(n, rng, 0.2, 0.6, 1.0)
    bubbles = _calls(n, rng, 2.5, _pop, 0.5)
    crackle = bandpass((rng.uniform(0, 1, n) < 0.002) * rng.uniform(-1, 1, n), 1500, 6000, SR) * 1.5
    return rumble + bubbles + crackle


def mushroom(n, rng):
    t = np.arange(n) / SR
    hum = sum(np.sin(2 * np.pi * f * t) * (0.6 + 0.4 * np.sin(2 * np.pi * rng.uniform(0.05, 0.2) * t + i)) for i, f in enumerate((220, 277.2, 329.6, 440)))
    sparkle = _calls(n, rng, 0.8, _drip, 0.15)
    return hum * 0.12 + sparkle


def dungeon(n, rng):
    t = np.arange(n) / SR
    drone = sum(np.sin(2 * np.pi * f * t) * w for f, w in ((43.7, 0.5), (65.4, 0.3), (87.3, 0.15)))
    halls = bandpass(_noise(n, rng), 200, 700, SR) * _smooth(n, rng, 0.2, 0.1, 0.6)
    return reverb(drone * 0.5 + halls * 0.6, SR, room=0.95, damp=0.5, wet=0.5, tail=0)[:n]


def hive(n, rng):
    t = np.arange(n) / SR
    out = np.zeros(n)
    for _ in range(6):
        f = rng.uniform(170, 240) * (1 + 0.02 * np.sin(2 * np.pi * rng.uniform(2, 6) * t))
        saw = 2 * ((np.cumsum(f) / SR) % 1.0) - 1
        out += lowpass(saw, 1800, SR) * _smooth(n, rng, 0.6, 0.0, 1.0) ** 2
    return out * 0.4
