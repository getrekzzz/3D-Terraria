"""Seamless stereo ambience loops (assets/audio/ambience/<id>.ogg): the biome, cave and underworld beds the ambience
director picks by place and time of day (data/config/ambience.json), and the weather beds (light and heavy rain, wind,
blizzard, sandstorm; game/Ambience/WeatherAudio.cs). Each loop is a bed (wind, air, surf, rumble, rain) with calls
scattered over it at random times and places, folded into a seamless loop, set in a room and levelled.

The biome beds are #13's soundscapes under the overhaul's loop ids, seeded by their #13 names, so each is the very loop
#13 made (forest_day is #13's "birds"), lifted by the volume the director plays it at so it is heard where #13 put it.
The weather beds are the overhaul's recipes, now one take per ear at the pipeline's rate (band-limited where the
overhaul's renders ended) and levelled to the overhaul's loudness; the deep cavern and the mushroom hum are the
overhaul's beds rebuilt from #13's cave air and voices. #13 also gave the underground jungle its insects and the ice
caves its snow wind: those are takes of their own of the same soundscapes (so they never phase against the surface
beds while one fades into the other), a little quieter, as they layer over the cave air. Built by
``build_audio.py --ambience``."""
from typing import Callable, NamedTuple, Optional

import numpy as np
from scipy import signal

import ambience_voices as v
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


# ---- #13's soundscapes -------------------------------------------------------------------------------------------------

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


def snow_wind(rng):
    """#13's "wind": the snow's gusting rush and wandering whistle."""
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


def cave(rng, rt60=3.2, drips=(0.6, 2.2), low=1.0, rumble=140):
    buf = _bed(lambda r, k: lowpass(v.coloured(r, k, 2.0), rumble) * (0.6 + 0.4 * wander(r, k, 0.1)), rng) * 0.25 * low
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


# ---- the overhaul's own beds -----------------------------------------------------------------------------------------

def cavern(rng):
    """The deep caverns: #13's cave air with a lower, heavier rumble, sparser drips and a bigger, darker room."""
    buf, _ = cave(rng, 4.6, (1.2, 3.6), 1.8, rumble=95)
    return buf, -27


def _plink(rng):
    """A bright drop settling in pitch: the glowing mushroom caves' sparkle."""
    n = samples(0.35)
    t = seconds(n)
    tone = np.sin(2 * np.pi * np.cumsum(rng.uniform(1300, 2800) * (1 + 0.6 * np.exp(-t / 0.01))) / SR)
    return tone * np.minimum(t / 0.001, 1) * np.exp(-t / 0.05)


def mushroom(rng):
    """The glowing mushroom caves: a soft A major hum swelling slowly and differently in each ear (whole cycles over
    the loop, so it needs no seam), sparkling drips, and a cave's room."""
    n = samples(LOOP)
    t = seconds(n)
    hum = np.zeros((n, 2))
    for ch in range(2):
        for i, f in enumerate((220.0, 277.2, 329.6, 440.0)):
            sway = rng.integers(2, 7) / LOOP
            hum[:, ch] += np.sin(2 * np.pi * f * t) * (0.6 + 0.4 * np.sin(2 * np.pi * sway * t + i + 2 * ch))
    buf = hum * 0.12
    _calls(buf, rng, [_plink], (0.5, 2.0), (0.0, 0.8), gain=0.3)
    return hall(buf, rt60=2.8, damp=0.5, wet=0.35, loop=True), -26


# ---- weather: one independent take per ear, so the rain falls and the wind gusts all around ---------------------------

def _noise(n, rng):
    return rng.uniform(-1, 1, n)


def _smooth(n, rng, rate, lo=0.0, hi=1.0):
    """A slow random curve (control signal) between lo and hi, about `rate` changes per second."""
    k = max(4, int(n / SR * rate) + 2)
    pts = rng.uniform(lo, hi, k)
    return np.interp(np.linspace(0, k - 1, n), np.arange(k), pts)


def _swept(x, cutoff, kind="low"):
    """Filters x with a cutoff that moves every ~80 ms (cutoff: array the length of x, Hz), its state carried along."""
    out = np.zeros_like(x)
    blocks = max(200, int(len(x) / SR * 12))
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
    return bandpass(signal.oaconvolve(imp, kernel)[:n], lo, hi, SR)


def _rain_light(n, rng):
    hiss = bandpass(_noise(n, rng), 900, 7000, SR) * 0.35
    patter = _drops(n, rng, 900, 1500, 7500) * 2.2
    near = _drops(n, rng, 60, 600, 3500, 0.01) * 1.6
    return (hiss + patter + near) * _smooth(n, rng, 0.3, 0.8, 1.0)


def _rain_heavy(n, rng):
    roar = lowpass(_noise(n, rng), 6500, SR) * 0.6
    body = bandpass(_noise(n, rng), 120, 600, SR) * 0.7
    patter = _drops(n, rng, 4000, 1200, 8000) * 1.5
    return (roar + body + patter) * _smooth(n, rng, 0.4, 0.75, 1.0)


def _gusts(n, rng):
    gust = _smooth(n, rng, 0.35, 0.15, 1.0) ** 1.6
    cutoff = 220 + 900 * gust
    bed = _swept(_noise(n, rng), cutoff) * (0.35 + gust)
    howl = bandpass(_noise(n, rng), 300, 900, SR) * (gust ** 3) * 0.6
    return bed + howl


def _blizzard(n, rng):
    base = _gusts(n, rng) * 1.2
    hiss = highpass(_noise(n, rng), 3000, SR) * _smooth(n, rng, 0.8, 0.25, 0.6)
    return base + hiss * 0.5


def _sandstorm(n, rng):
    base = _gusts(n, rng)
    grit = _drops(n, rng, 6000, 2500, 9000, 0.0015) * _smooth(n, rng, 0.7, 0.4, 1.0) * 1.4
    rasp = bandpass(_noise(n, rng), 1200, 5000, SR) * 0.3
    return base + grit + rasp


def _weather(fn, rng, width=0.9, room=None):
    """A weather bed: one take per ear, band-limited where the overhaul's 22 kHz renders of it ended (11 kHz)."""
    buf = _bed(lambda r, n: lowpass(fn(n, r), 11000, SR, 4), rng, width)
    return hall(buf, rt60=room[0], wet=room[1], loop=True) if room else buf


def rain_light(rng):
    return _weather(_rain_light, rng), -23.5


def rain_heavy(rng):
    return _weather(_rain_heavy, rng), -18.5


def wind(rng):
    return _weather(_gusts, rng, room=(1.2, 0.12)), -21.5


def blizzard(rng):
    return _weather(_blizzard, rng, room=(1.2, 0.12)), -21


def sandstorm(rng):
    return _weather(_sandstorm, rng, room=(0.8, 0.1)), -20


# ---- the loops ---------------------------------------------------------------------------------------------------------

class Loop(NamedTuple):
    recipe: Callable  # rng -> (stereo bed, its RMS level in dB as heard at full presence)
    seed: str  # build_audio.py seeds the render with this name: #13's own, so its loops come out unchanged
    played_at: float = 1.0  # the volume data/config/ambience.json gave the bed when it was levelled; the file is lifted by it
    heard: Optional[float] = None  # where the bed is heard (dB RMS at full presence) when not where its recipe puts it


# Loop id (data/config/ambience.json and WeatherAudio name these) -> recipe. #13 mixed its beds to be heard at full
# volume under the music (-17 dB RMS); the director plays each at the volume beside it here, so the file is that much
# louder and the bed lands where #13 put it (as far as its peaks allow, see render). A later change of a volume in the
# config turns that bed up or down from there, as a volume should; these numbers are the calibration, not a copy to sync.
LOOPS = {
    "forest_day": Loop(birds, "birds", 0.55),
    "forest_night": Loop(birds_night, "birds_night", 0.5),
    "jungle_day": Loop(insects, "insects", 0.6),
    "jungle_night": Loop(insects_night, "insects_night", 0.6),
    "ocean": Loop(waves, "waves", 0.7),
    "desert": Loop(desert_wind, "desert_wind", 0.45),
    "tundra": Loop(snow_wind, "wind", 0.45),
    "evil": Loop(eerie, "eerie", 0.5),
    "hallow": Loop(sparkle, "sparkle", 0.45),
    "sky": Loop(high_wind, "high_wind", 0.4),
    "cave": Loop(cave, "cave", 0.55),
    "cavern": Loop(cavern, "cavern", 0.6),
    "mushroom": Loop(mushroom, "mushroom", 0.55),
    "hive": Loop(bees, "bees", 0.6),
    "dungeon": Loop(crypt, "crypt", 0.55),
    # Over the cave or cavern air (-28 / -27 dB), each 2 dB under #13's level for its soundscape (insects -25, snow wind
    # -23), so the two together land about where #13's bed alone was (#13 played it instead of the cave's).
    "jungle_underground": Loop(insects, "jungle_underground", 0.6, heard=-27),
    "ice_cave": Loop(snow_wind, "ice_cave", 0.6, heard=-25),
    "underworld": Loop(lava, "lava", 0.75),
    "rain_light": Loop(rain_light, "rain_light"),
    "rain_heavy": Loop(rain_heavy, "rain_heavy"),
    "wind": Loop(wind, "wind_gusts"),
    "blizzard": Loop(blizzard, "blizzard"),
    "sandstorm": Loop(sandstorm, "sandstorm"),
}

PEAK = 10 ** (-1 / 20)  # a loop lifted by its volume never peaks above -1 dBFS (just under loudness()'s 0.9 ceiling)


def render(loop_id, seed):
    """One loop: stereo float32 (n, 2) at SR. Levelled to where it is heard (loudness: RMS with a soft ceiling, exactly as
    #13 levelled its loops), then lifted by the volume the game plays it at, as far as -1 dBFS peaks allow."""
    loop = LOOPS[loop_id]
    x, heard_db = loop.recipe(np.random.default_rng(seed))
    heard_db = heard_db if loop.heard is None else loop.heard
    x = circular(lambda c: highpass(c, 30), x)  # nothing below hearing, without a seam at the loop point
    x = loudness(x, heard_db, 0.9)
    lift = min(-20 * np.log10(loop.played_at), 20 * np.log10(PEAK / (np.max(np.abs(x)) or 1.0)))
    return (x * 10 ** (max(lift, 0.0) / 20)).astype(np.float32)
