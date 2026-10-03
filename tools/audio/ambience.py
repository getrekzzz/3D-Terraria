"""The ambience: one seamless stereo loop per biome soundscape (assets/audio/ambient/<id>.ogg), played under the music on
the Ambient bus. Biomes name theirs in data/biomes.json ("ambience"); a "<id>_night" loop replaces it after dark. Each
loop is a bed (wind, air, surf, rumble) with calls scattered over it at random times and places, then a room."""
import numpy as np

import ambience_voices as v
from scipy import signal

from synth import SR, bandpass, circular, hall, highpass, loudness, lowpass, osc, pan, samples, seconds, wander

LOOP = 30.0  # seconds
XF = samples(2.0)  # crossfade folded over the loop point


def _calls(buf, rng, makers, every, distance=(0.2, 0.9), spread=0.9, gain=1.0):
    """Scatters calls over the loop, one roughly every `every` (min, max) seconds; a caller sometimes repeats itself."""
    t = rng.uniform(0, every[1])
    while t < LOOP:
        make = makers[rng.integers(len(makers))]
        p, d = rng.uniform(-spread, spread), rng.uniform(*distance)
        seed = int(rng.integers(1 << 30))
        for _ in range(1 if rng.random() < 0.6 else rng.integers(2, 4)):  # the same bird answering itself
            call = make(np.random.default_rng(seed))
            v.scatter(buf, call * gain, samples(t), p, d)
            t += len(call) / SR + rng.uniform(0.25, 0.7)
        t += rng.uniform(*every)


def _bed(fn, rng, width=0.8):
    return v.seamless(v.stereo(fn, rng, samples(LOOP) + XF, width), XF)


def birds(rng):
    buf = _bed(lambda r, n: v.wind(r, n, 300, 700, 3, 0.08, 0.5, 0.15), rng) * 0.25
    leaves = _bed(lambda r, n: bandpass(r.standard_normal(n), 2500, 7000) * wander(r, n, 0.3) ** 3, rng) * 0.08
    buf += leaves
    _calls(buf, rng, [v.warble, v.whistle, v.chirps], (1.0, 3.0), gain=0.32)
    return hall(buf, rt60=1.4, wet=0.2, loop=True), -26


def birds_night(rng):
    n = samples(LOOP) + XF
    buf = _bed(lambda r, k: v.wind(r, k, 250, 600, 3, 0.06, 0.4, 0.1), rng) * 0.2
    chorus = np.zeros((n, 2))
    for _ in range(5):
        far = rng.uniform(0.1, 0.9)
        bug = v.crickets(rng, n, rng.uniform(3500, 5200), rng.uniform(1.6, 3.2), 0.12 * (1 - 0.7 * far))
        chorus += pan(lowpass(bug, 12000 - 9000 * far), rng.uniform(-0.9, 0.9))
    buf += v.seamless(chorus, XF)
    _calls(buf, rng, [v.owl], (9.0, 16.0), (0.5, 0.9), gain=0.5)
    return hall(buf, rt60=1.8, wet=0.25, loop=True), -27


def insects(rng):
    n = samples(LOOP) + XF
    buf = v.seamless(np.stack([v.cicadas(rng, n, 5200), v.cicadas(rng, n, 5900)], axis=1) * 0.25, XF)
    buf += _bed(lambda r, k: lowpass(v.coloured(r, k, 1.0), 900) * (0.4 + 0.6 * wander(r, k, 0.1)), rng) * 0.06
    _calls(buf, rng, [v.whoop, v.warble, v.chirps], (1.5, 4.0), gain=0.3)
    _calls(buf, rng, [v.frog], (2.0, 5.0), (0.3, 0.8), gain=0.25)
    return hall(buf, rt60=1.6, wet=0.25, loop=True), -25


def insects_night(rng):
    n = samples(LOOP) + XF
    buf = v.seamless(np.stack([v.cicadas(rng, n, 4600), v.cicadas(rng, n, 4300)], axis=1) * 0.15, XF)
    chorus = np.zeros((n, 2))
    for _ in range(5):
        chorus += pan(v.crickets(rng, n, rng.uniform(4200, 5200), rng.uniform(2.0, 3.6), 0.1), rng.uniform(-0.9, 0.9))
    buf += v.seamless(chorus, XF)
    _calls(buf, rng, [v.frog], (0.8, 2.5), (0.2, 0.8), gain=0.3)
    return hall(buf, rt60=1.8, wet=0.25, loop=True), -26


def wind(rng):
    buf = _bed(lambda r, n: v.wind(r, n, 320, 950, 9, 0.14, 0.5, 2.0), rng, 0.9) * 0.3
    return hall(buf, rt60=1.2, wet=0.15, loop=True), -23


def desert_wind(rng):
    buf = _bed(lambda r, n: v.wind(r, n, 240, 600, 5, 0.1, 0.8, 0.6), rng, 0.9) * 0.3
    sand = _bed(lambda r, n: highpass(r.standard_normal(n), 3000) * wander(r, n, 0.2) ** 3, rng) * 0.05
    return hall(buf + sand, rt60=0.8, wet=0.1, loop=True), -25


def high_wind(rng):
    buf = _bed(lambda r, n: v.wind(r, n, 700, 1900, 4, 0.1, 0.7, 0.5), rng, 0.95) * 0.3
    return hall(buf, rt60=2.0, wet=0.2, loop=True), -26


def waves(rng):
    n = samples(LOOP) + XF
    t = seconds(n)
    surf = np.zeros((n, 2))
    at = 0.0
    while at < LOOP + 2.0:  # each wave swells in, breaks, and draws back as a hiss
        period = rng.uniform(6.5, 10.0)
        k = t - at
        env = np.where(k < 0, 0.0, np.where(k < 2.4, (np.clip(k, 0, 2.4) / 2.4) ** 2, np.exp(-np.clip(k - 2.4, 0, None) / 1.8)))
        body = lowpass(v.coloured(rng, n, 1.0), 1400) * env
        broke = np.clip((k - 1.9) / 0.6, 0, 1)
        wash = highpass(rng.standard_normal(n), 2500) * env * broke * broke * (3 - 2 * broke) * 0.2  # the hiss as it breaks
        surf += pan(body + wash, rng.uniform(-0.4, 0.4))
        at += period
    rumble = v.stereo(lambda r, k: lowpass(v.coloured(r, k, 2.0), 160), rng, n) * 0.4
    buf = v.seamless(surf + rumble, XF)
    _calls(buf, rng, [v.gull], (8.0, 15.0), (0.5, 0.9), gain=0.25)
    return hall(buf, rt60=1.2, wet=0.15, loop=True), -22


def lava(rng):
    n = samples(LOOP) + XF
    buf = _bed(lambda r, k: lowpass(v.coloured(r, k, 2.0), 140) * (0.5 + 0.5 * wander(r, k, 0.15)), rng) * 0.9
    buf += _bed(lambda r, k: bandpass(v.coloured(r, k, 1.0), 200, 900) * wander(r, k, 0.2) ** 2, rng) * 0.08
    fire = np.zeros((n - XF, 2))
    density = 3 + 6 * wander(rng, n - XF, 0.3)
    t = 0.0
    while t < LOOP:
        v.scatter(fire, v.crackle(rng) * min(1.0, rng.exponential(0.25)) * 0.3, samples(t), rng.uniform(-0.8, 0.8), rng.uniform(0.3, 0.9))
        t += rng.exponential(1.0 / density[min(samples(t), len(density) - 1)])
    buf += fire
    _calls(buf, rng, [v.bubble], (0.2, 1.2), (0.0, 0.7), gain=0.35)
    return hall(buf, rt60=2.4, wet=0.3, loop=True), -22


def cave(rng, rt60=3.2, drips=(0.6, 2.2), low=1.0):
    buf = _bed(lambda r, k: lowpass(v.coloured(r, k, 2.0), 140) * (0.6 + 0.4 * wander(r, k, 0.1)), rng) * 0.25 * low
    buf += _bed(lambda r, k: v.resonant_sweep(v.coloured(r, k, 1.0), 180 * 1.6 ** wander(r, k, 0.05), 6), rng) * 0.05
    _calls(buf, rng, [v.drip], drips, (0.0, 0.85), gain=0.22)
    return hall(buf, rt60=rt60, damp=0.45, wet=0.45, loop=True), -28


def crypt(rng):
    buf, _ = cave(rng, 4.5, (1.5, 4.0), 1.4)
    howl = v.seamless(v.stereo(lambda r, n: v.wind(r, n, 140, 380, 10, 0.07, 0.2, 0.6), rng, samples(LOOP) + XF), XF) * 0.06
    return buf + hall(howl, rt60=4.0, wet=0.5, loop=True), -27


def eerie(rng):
    n = samples(LOOP) + XF
    t = seconds(n)
    drone = sum(osc("saw", f, n / SR)[:n] for f in (55.0, 55.6, 82.6)) / 3
    cutoff = 140 * 3 ** wander(rng, n, 0.06)
    out, zi = np.zeros(n), np.zeros(2)
    for i in range(0, n, 512):  # a slowly opening and closing filter, its state carried across blocks
        b, a = signal.butter(2, float(cutoff[i]) / (SR / 2))
        out[i:i + 512], zi = signal.lfilter(b, a, drone[i:i + 512], zi=zi)
    drone = out
    breath = v.stereo(lambda r, k: v.wind(r, k, 160, 420, 7, 0.08, 0.4, 0.8), rng, n) * 0.12
    buf = v.seamless(pan(drone * (0.7 + 0.3 * np.sin(2 * np.pi * t / 7.5)), 0.0) * 0.35 + breath, XF)
    return hall(buf, rt60=3.5, wet=0.4, loop=True), -27


def bees(rng):
    n = samples(LOOP) + XF
    swarm = np.zeros((n, 2))
    for _ in range(7):
        buzz, near = v.bee(rng, n)
        side = 2 * wander(rng, n, 0.2) - 1
        swarm += np.stack([buzz * np.cos((side + 1) * np.pi / 4), buzz * np.sin((side + 1) * np.pi / 4)], axis=1)
    hum = v.stereo(lambda r, k: bandpass(v.coloured(r, k, 1.0), 150, 500), rng, n) * 0.15
    buf = v.seamless(swarm * 0.2 + hum, XF)
    return hall(buf, rt60=1.5, wet=0.3, loop=True), -27


def sparkle(rng):
    buf = _bed(lambda r, n: bandpass(r.standard_normal(n), 5000, 10000) * (0.3 + 0.7 * wander(r, n, 0.15) ** 2), rng) * 0.03
    buf += _bed(lambda r, n: v.wind(r, n, 400, 900, 3, 0.07, 0.4, 0.1), rng) * 0.12
    _calls(buf, rng, [v.tinkle], (0.8, 2.6), (0.2, 0.8), gain=0.12)
    _calls(buf, rng, [v.warble, v.whistle], (3.0, 7.0), (0.4, 0.9), gain=0.2)
    return hall(buf, rt60=2.6, wet=0.35, loop=True), -27


BEDS = {f.__name__: f for f in (birds, birds_night, insects, insects_night, wind, desert_wind, high_wind, waves, lava, cave,
                                crypt, eerie, bees, sparkle)}


def build(name, seed):
    """Renders one loop: stereo float32 (n, 2) at SR, levelled to its bed's target loudness."""
    x, rms_db = BEDS[name](np.random.default_rng(seed))
    x = circular(lambda c: highpass(c, 30), x)  # nothing below hearing, without a seam at the loop point
    return loudness(x, rms_db, 0.9).astype(np.float32)
