"""Seamless ambience loops (assets/audio/ambience/<id>.ogg): weather beds (light and heavy rain, wind, blizzard,
sandstorm) and the biome, cave and underworld beds. Each recipe renders a little more than the loop length and the
overrun is cross-faded into the head, so the loop has no seam. Built by ``build_audio.py --ambience``."""
import numpy as np
from scipy import signal

import ambience_beds as beds
from synth import bandpass, highpass, lowpass, samples

SR = 22050
CROSS = 1.5  # seconds cross-faded at the seam


def _noise(n, rng):
    return rng.uniform(-1, 1, n)


def _smooth(n, rng, rate, lo=0.0, hi=1.0):
    """A slow random curve (control signal) between lo and hi, about `rate` changes per second."""
    k = max(4, int(n / SR * rate) + 2)
    pts = rng.uniform(lo, hi, k)
    return np.interp(np.linspace(0, k - 1, n), np.arange(k), pts)


def _swept(x, cutoff, kind="low", blocks=200):
    """Filters x with a cutoff that moves per sample block (cutoff: array the length of x, Hz)."""
    out = np.zeros_like(x)
    edges = np.linspace(0, len(x), blocks + 1).astype(int)
    zi = None
    for i in range(blocks):
        a0, a1 = edges[i], edges[i + 1]
        c = float(np.clip(np.mean(cutoff[a0:a1]), 40, SR * 0.45))
        b, a = signal.butter(2, c / (SR / 2), kind)
        if zi is None:
            zi = signal.lfilter_zi(b, a) * 0.0
        out[a0:a1], zi = signal.lfilter(b, a, x[a0:a1], zi=zi)
    return out


def _drops(n, rng, rate, lo, hi, tau=0.004):
    """Raindrop ticks: sparse impulses rung through a band, each a tiny decaying click."""
    imp = (rng.uniform(0, 1, n) < rate / SR) * rng.uniform(0.2, 1.0, n) * rng.choice([-1, 1], n)
    kernel = np.exp(-np.arange(int(SR * tau * 6)) / (SR * tau))
    return bandpass(np.convolve(imp, kernel)[:n], lo, hi, SR)


def rain_light(n, rng):
    hiss = bandpass(_noise(n, rng), 900, 7000, SR) * 0.35
    patter = _drops(n, rng, 900, 1500, 7500) * 2.2
    near = _drops(n, rng, 60, 600, 3500, 0.01) * 1.6
    return (hiss + patter + near) * _smooth(n, rng, 0.3, 0.8, 1.0)


def rain_heavy(n, rng):
    roar = lowpass(_noise(n, rng), 6500, SR) * 0.6
    body = bandpass(_noise(n, rng), 120, 600, SR) * 0.7
    patter = _drops(n, rng, 4000, 1200, 8000) * 1.5
    return (roar + body + patter) * _smooth(n, rng, 0.4, 0.75, 1.0)


def wind(n, rng):
    gust = _smooth(n, rng, 0.35, 0.15, 1.0) ** 1.6
    cutoff = 220 + 900 * gust
    bed = _swept(_noise(n, rng), cutoff) * (0.35 + gust)
    howl = bandpass(_noise(n, rng), 300, 900, SR) * (gust ** 3) * 0.6
    return bed + howl


def blizzard(n, rng):
    base = wind(n, rng) * 1.2
    hiss = highpass(_noise(n, rng), 3000, SR) * _smooth(n, rng, 0.8, 0.25, 0.6)
    return base + hiss * 0.5


def sandstorm(n, rng):
    base = wind(n, rng)
    grit = _drops(n, rng, 6000, 2500, 9000, 0.0015) * _smooth(n, rng, 0.7, 0.4, 1.0) * 1.4
    rasp = bandpass(_noise(n, rng), 1200, 5000, SR) * 0.3
    return base + grit + rasp


LOOPS = {
    "rain_light": (rain_light, 12.0),
    "rain_heavy": (rain_heavy, 12.0),
    "wind": (wind, 16.0),
    "blizzard": (blizzard, 14.0),
    "sandstorm": (sandstorm, 14.0),
    "forest_day": (beds.forest_day, 20.0),
    "forest_night": (beds.forest_night, 16.0),
    "jungle_day": (beds.jungle_day, 18.0),
    "jungle_night": (beds.jungle_night, 16.0),
    "ocean": (beds.ocean, 21.0),
    "desert": (beds.desert, 14.0),
    "tundra": (beds.tundra, 14.0),
    "evil": (beds.evil, 16.0),
    "hallow": (beds.hallow, 18.0),
    "sky": (beds.sky, 14.0),
    "cave": (beds.cave, 18.0),
    "cavern": (beds.cavern, 18.0),
    "underworld": (beds.underworld, 14.0),
    "mushroom": (beds.mushroom, 16.0),
    "dungeon": (beds.dungeon, 18.0),
    "hive": (beds.hive, 12.0),
}

# Peak levels of the beds (they sit under music and sound effects).
GAIN = {"rain_light": 0.5, "rain_heavy": 0.6, "wind": 0.5, "blizzard": 0.55, "sandstorm": 0.55, "forest_day": 0.45, "forest_night": 0.4,
        "jungle_day": 0.45, "jungle_night": 0.45, "ocean": 0.55, "desert": 0.4, "tundra": 0.4, "evil": 0.45, "hallow": 0.4, "sky": 0.35,
        "cave": 0.5, "cavern": 0.55, "underworld": 0.6, "mushroom": 0.4, "dungeon": 0.5, "hive": 0.45}


def render(loop_id, seed):
    """The loop as float32 at SR: rendered long, the tail cross-faded into the head (equal power), normalised."""
    fn, length = LOOPS[loop_id]
    rng = np.random.default_rng(seed)
    n, x = samples(length, SR), None
    c = samples(CROSS, SR)
    x = np.asarray(fn(n + c, rng), dtype=np.float64)
    head, tail = x[:c], x[n:n + c]
    t = np.linspace(0, np.pi / 2, c)
    out = x[:n].copy()
    out[:c] = head * np.sin(t) + tail * np.cos(t)
    peak = np.max(np.abs(out)) or 1.0
    return (out / peak * GAIN.get(loop_id, 0.5)).astype(np.float32)
