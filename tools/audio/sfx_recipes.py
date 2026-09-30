"""Recipes for every sound effect the game plays (assets/audio/sfx/<id>.wav, variants <id>_<n>.wav). Each recipe takes a
numpy Generator (for per-variant variation) and returns a mono float array at SFX_SR.

New sounds go in their own module, tools/audio/sfx_recipes_<topic>.py, which defines RECIPES (and optionally GAIN) in
the same form as below; this module merges them in file-name order (a sound id defined twice is an error). Extension
modules may import helpers from here (``from sfx_recipes import whoosh``); always import this module, not an extension."""
import importlib
import pathlib

import numpy as np

from synth import (adsr, bandpass, decay, fade, highpass, kick, lowpass, midi_hz, mix, osc, reverb, samples, seconds, snare,
                   sweep_lowpass)

SFX_SR = 22050
S = SFX_SR


def _noise(dur, rng):
    return rng.uniform(-1, 1, samples(dur, S))


def _thud(freq, dur, tau, rng, grit=0.3):
    n = samples(dur, S)
    f = freq * (1 + 0.6 * np.exp(-seconds(n, S) / 0.02))
    body = osc("sine", f, dur, S) * decay(n, tau, S)
    return body + lowpass(_noise(dur, rng), 1800, S) * decay(n, tau * 0.5, S) * grit


def dig(material):
    """A pick biting into a block: a short tick plus the material's own texture."""
    def make(rng):
        dur = 0.18
        n = samples(dur, S)
        tick = highpass(_noise(dur, rng), 2500, S) * decay(n, 0.006, S) * 0.5
        if material == "stone" or material == "ore":
            ring = sum(osc("sine", f * rng.uniform(0.97, 1.03), dur, S) for f in (1850, 2710, 3900)) * decay(n, 0.05 if material == "stone" else 0.09, S) * 0.18
            body = bandpass(_noise(dur, rng), 400, 2500, S) * decay(n, 0.03, S) * 0.7
            sparkle = osc("sine", 3200 * rng.uniform(0.9, 1.1), dur, S) * decay(n, 0.12, S) * (0.2 if material == "ore" else 0.0)
            return mix(tick, ring, body, sparkle)
        if material == "wood":
            return mix(tick, _thud(260 * rng.uniform(0.9, 1.1), dur, 0.05, rng, 0.5) * 0.9)
        if material in ("sand", "snow"):
            return mix(tick * 0.4, lowpass(_noise(dur, rng), 1400 if material == "sand" else 2400, S) * decay(n, 0.05, S) * 0.9)
        if material in ("glass", "ice"):
            return mix(tick, sum(osc("sine", f * rng.uniform(0.95, 1.05), dur, S) for f in (2400, 3700, 5100)) * decay(n, 0.07, S) * 0.25)
        if material == "metal":
            return mix(tick, sum(osc("sine", f, dur, S) for f in (900, 1460, 2210)) * decay(n, 0.12, S) * 0.25)
        # dirt, grass, cloth
        return mix(tick * 0.5, lowpass(_noise(dur, rng), 900, S) * decay(n, 0.04, S), _thud(120, dur, 0.04, rng, 0.2) * 0.6)
    return make


def broke(material):
    """The block giving way: a crumble built from several dig hits."""
    def make(rng):
        out = np.zeros(samples(0.45, S))
        for k in range(4):
            s = dig(material)(rng) * (1.0 - k * 0.18)
            at = int(k * rng.uniform(0.03, 0.07) * S)
            out[at:at + len(s)] += s[:len(out) - at]
        n = len(out)
        return out + lowpass(_noise(0.45, rng), 700, S) * decay(n, 0.12, S) * 0.5
    return make


def swing(rng):
    dur = 0.28
    n = samples(dur, S)
    whoosh = bandpass(_noise(dur, rng), 500, 3500, S)
    env = np.sin(np.linspace(0, np.pi, n)) ** 2
    return sweep_lowpass(whoosh * env, 900 * rng.uniform(0.8, 1.2), 3200, S) * 1.2


def hit(rng):
    return _thud(95 * rng.uniform(0.85, 1.15), 0.2, 0.06, rng, 0.6)


def squish(rng):
    dur = 0.25
    n = samples(dur, S)
    f = 300 * rng.uniform(0.8, 1.2) * (1 + 0.8 * np.sin(np.linspace(0, 3.5, n)))
    return osc("sine", f, dur, S) * decay(n, 0.08, S) * 0.6 + lowpass(_noise(dur, rng), 1200, S) * decay(n, 0.05, S) * 0.5


def clack(rng):
    dur = 0.15
    n = samples(dur, S)
    return sum(bandpass(_noise(dur, rng), f, f * 1.4, S) for f in (900, 2100)) * decay(n, 0.02, S) * 1.2


def npc_death(rng):
    dur = 0.45
    n = samples(dur, S)
    f = 220 * rng.uniform(0.85, 1.15) * np.exp(-seconds(n, S) / 0.2)
    voice = lowpass(osc("saw", f, dur, S), 1200, S) * adsr(n, 0.01, 0.1, 0.5, 0.2, S) * 0.5
    return mix(voice, _thud(80, dur, 0.1, rng) * 0.8)


def player_hurt(rng):
    dur = 0.3
    n = samples(dur, S)
    f = 180 * rng.uniform(0.9, 1.1) * (1 - 0.3 * seconds(n, S) / dur)
    grunt = bandpass(osc("saw", f, dur, S), 200, 1500, S) * adsr(n, 0.01, 0.08, 0.4, 0.15, S)
    return mix(grunt * 0.9, hit(rng) * 0.6)


def player_death(rng):
    dur = 1.2
    n = samples(dur, S)
    f = 300 * np.exp(-seconds(n, S) / 0.5)
    tone = lowpass(osc("square", f, dur, S), 1500, S) * adsr(n, 0.01, 0.3, 0.4, 0.6, S) * 0.4
    return reverb(tone, S, 0.6, 0.4, 0.3, 0.5)


def bow(rng):
    dur = 0.25
    n = samples(dur, S)
    twang = osc("triangle", 180 * rng.uniform(0.95, 1.05), dur, S) * decay(n, 0.06, S) * 0.7
    return mix(twang, swing(rng)[:n] * 0.35)


def gun(rng, heavy=False):
    dur = 0.35 if heavy else 0.2
    n = samples(dur, S)
    crack = highpass(_noise(dur, rng), 800, S) * decay(n, 0.02, S)
    boom = _thud(70 if heavy else 110, dur, 0.09 if heavy else 0.04, rng, 0.8)
    return mix(crack * (1.0 if heavy else 0.8), boom * (1.2 if heavy else 0.7))


def magic(rng, heavy=False):
    dur = 0.45 if heavy else 0.3
    n = samples(dur, S)
    base = rng.uniform(600, 900) * (0.6 if heavy else 1.0)
    f = base * (1 + 1.5 * np.exp(-seconds(n, S) / 0.05))
    zap = osc("saw", f, dur, S) * 0.3 + osc("sine", f * 1.5, dur, S) * 0.3
    sparkle = sum(osc("sine", base * k, dur, S) for k in (2.0, 3.01, 4.02)) * 0.08
    return reverb(sweep_lowpass(zap + sparkle, 6000, 800, S) * decay(n, 0.12 if heavy else 0.08, S), S, 0.5, 0.3, 0.25, 0.3)


def explosion(rng):
    dur = 1.2
    n = samples(dur, S)
    rumble = lowpass(_noise(dur, rng), 400, S, 3) * decay(n, 0.35, S) * 2.0
    blast = lowpass(_noise(dur, rng), 3000, S) * decay(n, 0.06, S)
    return mix(rumble, blast, _thud(45, dur, 0.3, rng, 0.3))


def boss_roar(rng):
    dur = 1.6
    n = samples(dur, S)
    t = seconds(n, S)
    f = 70 + 25 * np.sin(2 * np.pi * 7 * t) * (t / dur)
    growl = lowpass(osc("saw", f, dur, S) + osc("saw", f * 1.51, dur, S) * 0.6, 900, S) * adsr(n, 0.15, 0.3, 0.8, 0.6, S)
    return reverb(growl + lowpass(_noise(dur, rng), 600, S) * adsr(n, 0.2, 0.3, 0.5, 0.6, S) * 0.5, S, 0.8, 0.3, 0.35, 0.8)


def fanfare(rng):
    out = np.zeros(samples(1.6, S))
    for k, note in enumerate((67, 71, 74, 79)):
        dur = 0.6 if k == 3 else 0.18
        n = samples(dur, S)
        tone = (osc("square", midi_hz(note), dur, S) * 0.25 + osc("triangle", midi_hz(note - 12), dur, S) * 0.3) * adsr(n, 0.01, 0.1, 0.6, 0.2, S)
        at = int(k * 0.17 * S)
        out[at:at + n] += tone[:len(out) - at]
    return reverb(lowpass(out, 5000, S), S, 0.6, 0.3, 0.25, 0.5)


def gong(rng):
    dur = 2.0
    n = samples(dur, S)
    partials = sum(osc("sine", 110 * r * rng.uniform(0.99, 1.01), dur, S) * w for r, w in ((1, 1), (2.4, 0.5), (3.9, 0.35), (5.3, 0.2)))
    return reverb(partials * decay(n, 0.7, S, 0.01) * 0.5, S, 0.8, 0.2, 0.35, 1.0)


def jingle(rng):
    out = np.zeros(samples(1.0, S))
    for k, note in enumerate((72, 76, 79, 84)):
        n = samples(0.35, S)
        tone = osc("sine", midi_hz(note), 0.35, S) * decay(n, 0.12, S) * 0.5 + osc("sine", midi_hz(note + 12), 0.35, S) * decay(n, 0.06, S) * 0.15
        at = int(k * 0.1 * S)
        out[at:at + n] += tone[:len(out) - at]
    return reverb(out, S, 0.5, 0.3, 0.2, 0.4)


def blip(freq, dur=0.08, kind="sine", tau=0.03):
    def make(rng):
        n = samples(dur, S)
        return osc(kind, freq * rng.uniform(0.98, 1.02), dur, S) * decay(n, tau, S) * 0.6
    return make


def pickup(rng):
    dur = 0.12
    n = samples(dur, S)
    f = 600 + 900 * seconds(n, S) / dur
    return osc("sine", f, dur, S) * decay(n, 0.05, S) * 0.5


def coins(rng):
    out = np.zeros(samples(0.35, S))
    for k in range(3):
        n = samples(0.2, S)
        tone = sum(osc("sine", f * rng.uniform(0.97, 1.03), 0.2, S) for f in (2600, 3900, 5300)) * decay(n, 0.04, S) * 0.2
        at = int(k * rng.uniform(0.04, 0.07) * S)
        out[at:at + n] += tone[:len(out) - at]
    return out


def gulp(rng):
    dur = 0.35
    n = samples(dur, S)
    f = 250 + 200 * np.sin(np.linspace(0, 2 * np.pi, n)) ** 2
    return lowpass(osc("sine", f, dur, S), 800, S) * adsr(n, 0.02, 0.1, 0.6, 0.15, S) * 0.8


def chime(freq):
    def make(rng):
        dur = 0.6
        n = samples(dur, S)
        tone = sum(osc("sine", freq * r, dur, S) * w for r, w in ((1, 0.5), (2, 0.2), (3.01, 0.1)))
        return reverb(tone * decay(n, 0.2, S), S, 0.6, 0.3, 0.3, 0.3)
    return make


def shimmer(rng):
    dur = 0.9
    n = samples(dur, S)
    f = 500 * 2 ** (seconds(n, S) * 2.5)
    tone = (osc("sine", f, dur, S) + osc("sine", f * 1.5, dur, S) * 0.5) * adsr(n, 0.1, 0.3, 0.5, 0.3, S) * 0.4
    return reverb(tone, S, 0.7, 0.3, 0.35, 0.5)


def whoosh(lo, hi, dur=0.35, gain=1.0):
    def make(rng):
        n = samples(dur, S)
        env = np.sin(np.linspace(0, np.pi, n)) ** 1.5
        return sweep_lowpass(_noise(dur, rng) * env, lo, hi, S) * gain
    return make


def splash(rng):
    dur = 0.6
    n = samples(dur, S)
    return bandpass(_noise(dur, rng), 400, 4000, S) * decay(n, 0.15, S) * 0.9 + osc("sine", 300 * np.exp(-seconds(n, S) / 0.1), dur, S) * decay(n, 0.05, S) * 0.4


def bloop(rng):
    dur = 0.2
    n = samples(dur, S)
    f = 400 * (1 + 1.2 * seconds(n, S) / dur)
    return osc("sine", f, dur, S) * decay(n, 0.06, S) * 0.6


def creak(pitch):
    def make(rng):
        dur = 0.35
        n = samples(dur, S)
        f = pitch * (1 + 0.15 * np.sin(np.linspace(0, 9, n)))
        return bandpass(osc("saw", f, dur, S), 200, 2000, S) * adsr(n, 0.02, 0.1, 0.5, 0.1, S) * 0.5
    return make


def step(material):
    def make(rng):
        dur = 0.09
        n = samples(dur, S)
        cutoff = {"stone": 2500, "grass": 900, "sand": 1300, "wood": 1100, "snow": 1800}.get(material, 1000)
        return lowpass(_noise(dur, rng), cutoff, S) * decay(n, 0.02, S) * 0.5
    return make


def land(rng):
    return _thud(70, 0.2, 0.05, rng, 0.5)


def grapple(rng):
    dur = 0.25
    n = samples(dur, S)
    links = np.zeros(n)
    for k in range(6):
        at = int(k * 0.035 * S)
        s = bandpass(_noise(0.02, rng), 2000, 6000, S) * decay(samples(0.02, S), 0.004, S)
        links[at:at + len(s)] += s[:n - at]
    return links


def craft(rng):
    dur = 0.3
    n = samples(dur, S)
    return mix(sum(osc("sine", f, dur, S) for f in (1200, 1830, 2650)) * decay(n, 0.06, S) * 0.3, hit(rng) * 0.4)


def summon(rng):
    dur = 1.4
    n = samples(dur, S)
    f = 90 * 2 ** (seconds(n, S) * 0.8)
    drone = lowpass(osc("saw", f, dur, S) + osc("saw", f * 1.498, dur, S), 700, S) * adsr(n, 0.4, 0.3, 0.7, 0.5, S) * 0.4
    return reverb(drone, S, 0.8, 0.3, 0.4, 0.8)


RECIPES = {
    "ui_click": (blip(1400, 0.05, "sine", 0.015), 1), "ui_hover": (blip(2000, 0.03, "sine", 0.008), 1),
    "ui_open": (blip(700, 0.12, "triangle", 0.05), 1), "ui_close": (blip(500, 0.12, "triangle", 0.05), 1),
    "swing": (swing, 3), "hit": (hit, 3), "hit_squish": (squish, 3), "hit_bone": (clack, 3),
    "npc_death": (npc_death, 3), "slime_death": (squish, 2), "player_hurt": (player_hurt, 3), "player_death": (player_death, 1),
    "bow": (bow, 2), "gun": (lambda r: gun(r), 3), "gun_heavy": (lambda r: gun(r, True), 2), "magic": (lambda r: magic(r), 3),
    "magic_heavy": (lambda r: magic(r, True), 2), "explosion": (explosion, 2), "boss_roar": (boss_roar, 1), "boss_defeated": (fanfare, 1),
    "event_start": (gong, 1), "npc_arrive": (jingle, 1), "pickup": (pickup, 2), "coins": (coins, 2), "potion": (gulp, 1),
    "mana": (chime(880), 1), "heal": (chime(660), 1), "teleport": (shimmer, 1), "jump": (whoosh(600, 1800, 0.18, 0.5), 2),
    "land": (land, 1), "double_jump": (whoosh(900, 3000, 0.25, 0.7), 1), "wings": (whoosh(300, 900, 0.3, 0.6), 2),
    "rocket": (whoosh(1500, 5000, 0.35, 0.6), 1), "dash": (whoosh(700, 4000, 0.3, 0.9), 1), "grapple": (grapple, 1),
    "splash": (splash, 2), "fish_bite": (bloop, 1), "door_open": (creak(140), 1), "door_close": (creak(110), 1),
    "chest_open": (creak(220), 1), "craft": (craft, 2), "summon": (summon, 1), "place": (lambda r: dig("wood")(r) * 0.8, 2),
    "lever": (clack, 1), "wire": (blip(2400, 0.05, "square", 0.012), 1), "net": (whoosh(1200, 2600, 0.2, 0.6), 1),
    "minecart": (creak(300), 1), "dart_trap": (bow, 1),
}
for _m in ("dirt", "grass", "stone", "ore", "wood", "sand", "snow", "glass", "ice", "metal", "cloth"):
    RECIPES[f"dig_{_m}"] = (dig(_m), 3)
    RECIPES[f"break_{_m}"] = (broke(_m), 2)
for _m in ("grass", "stone", "sand", "wood", "snow"):
    RECIPES[f"step_{_m}"] = (step(_m), 3)


# Peak level per sound (quiet cues stay quiet; the rest land around -2 dBFS).
GAIN = {"ui_hover": 0.25, "ui_click": 0.4, "ui_open": 0.4, "ui_close": 0.4, "pickup": 0.45, "coins": 0.55, "jump": 0.4, "land": 0.5,
        "swing": 0.55, "grapple": 0.6, "fish_bite": 0.6, "place": 0.6, "mana": 0.5, "heal": 0.5,
        "wire": 0.3, "net": 0.5, "lever": 0.6}
for _m in ("grass", "stone", "sand", "wood", "snow"):
    GAIN[f"step_{_m}"] = 0.3
for _m in ("dirt", "grass", "stone", "ore", "wood", "sand", "snow", "glass", "ice", "metal", "cloth"):
    GAIN[f"dig_{_m}"] = 0.6


def _merge_extensions():
    """Adds RECIPES and GAIN from every sfx_recipes_*.py next to this file, sorted by file name."""
    owners = {"RECIPES": dict.fromkeys(RECIPES, "sfx_recipes.py"), "GAIN": dict.fromkeys(GAIN, "sfx_recipes.py")}
    for path in sorted(pathlib.Path(__file__).resolve().parent.glob("sfx_recipes_*.py")):
        module = importlib.import_module(path.stem)
        if not isinstance(getattr(module, "RECIPES", None), dict):
            raise ValueError(f"tools/audio/{path.name} must define a RECIPES dict")
        for name, table in (("RECIPES", RECIPES), ("GAIN", GAIN)):
            for sid, value in getattr(module, name, {}).items():
                if sid in owners[name]:
                    raise ValueError(f"sound '{sid}' in {name} of tools/audio/{path.name} is already defined in {owners[name][sid]}")
                owners[name][sid] = path.name
                table[sid] = value


_merge_extensions()


def render(sound_id, variant, seed):
    fn, _ = RECIPES[sound_id]
    rng = np.random.default_rng(seed)
    x = np.asarray(fn(rng), dtype=np.float64)
    x = fade(x, 0.001, 0.02, S)
    peak = np.max(np.abs(x)) or 1.0
    return (x / peak * GAIN.get(sound_id, 0.8)).astype(np.float32)
