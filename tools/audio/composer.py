"""A small procedural composer: turns a track description (key, mode, tempo, chord progression, instruments, drum
style) into a seamlessly looping piece with a phrase-structured melody, arpeggio, bass, pad and drums."""
import numpy as np

from synth import (SR, adsr, decay, hat, kick, lowpass, midi_hz, osc, place, reverb, samples, seconds, snare, tom)

SCALES = {"major": [0, 2, 4, 5, 7, 9, 11], "minor": [0, 2, 3, 5, 7, 8, 10], "dorian": [0, 2, 3, 5, 7, 9, 10], "phrygian": [0, 1, 3, 5, 7, 8, 10],
          "harmonic": [0, 2, 3, 5, 7, 8, 11], "lydian": [0, 2, 4, 6, 7, 9, 11], "mixolydian": [0, 2, 4, 5, 7, 9, 10]}


def degree_note(root, scale, degree):
    """MIDI note of a scale degree (degrees may run past 7 or below 0: octaves)."""
    s = SCALES[scale]
    return root + 12 * (degree // 7) + s[degree % 7]


# ---- voices ------------------------------------------------------------------------------------------------------

def voice(kind, note, dur, vel=1.0, vibrato=0.0):
    n = samples(dur + 0.25)
    f = midi_hz(note)
    if vibrato:
        t = seconds(n)
        f = f * (1 + vibrato * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.15) / 0.3, 0, 1))
    hold = dur
    if kind == "square":
        x = lowpass(osc("square", f, dur + 0.25), 3200) * adsr(n, 0.008, 0.12, 0.55, 0.12, hold=hold) * 0.32
    elif kind == "pulse":
        x = lowpass(osc("pulse", f, dur + 0.25, duty=0.25), 3600) * adsr(n, 0.005, 0.1, 0.5, 0.1, hold=hold) * 0.3
    elif kind == "triangle":
        x = osc("triangle", f, dur + 0.25) * adsr(n, 0.01, 0.1, 0.7, 0.15, hold=hold) * 0.5
    elif kind == "flute":
        x = (osc("sine", f, dur + 0.25) + osc("sine", f * 2, dur + 0.25) * 0.15) * adsr(n, 0.06, 0.1, 0.8, 0.18, hold=hold) * 0.5
        x += lowpass(osc("noise", note, dur + 0.25), 2500) * adsr(n, 0.05, 0.1, 0.1, 0.1, hold=hold) * 0.03
    elif kind == "bell":
        x = sum(osc("sine", f * r, dur + 0.25) * w * decay(n, 0.9 / r ** 0.5) for r, w in ((1, 0.5), (2.0, 0.2), (3.01, 0.12), (4.2, 0.06))) * 0.8
    elif kind == "pluck":
        x = lowpass(osc("saw", f, dur + 0.25), 2400) * decay(n, 0.22) * 0.45
    elif kind == "brass":
        x = lowpass(osc("saw", f, dur + 0.25) + osc("saw", f * 1.003, dur + 0.25), 1800) * adsr(n, 0.05, 0.15, 0.7, 0.15, hold=hold) * 0.22
    elif kind == "organ":
        x = sum(osc("sine", f * r, dur + 0.25) * w for r, w in ((1, 0.5), (2, 0.3), (3, 0.15), (4, 0.1))) * adsr(n, 0.02, 0.05, 0.9, 0.1, hold=hold) * 0.45
    elif kind == "pad":
        x = lowpass(sum(osc("saw", f * d, dur + 0.25) for d in (0.996, 1.0, 1.004)), 1100) * adsr(n, 0.35, 0.3, 0.8, 0.25, hold=hold) * 0.12
    elif kind == "choir":
        x = lowpass(sum(osc("triangle", f * d, dur + 0.25) for d in (0.994, 1.0, 1.006)), 1600) * adsr(n, 0.4, 0.3, 0.85, 0.3, hold=hold) * 0.2
    elif kind == "bass":
        x = lowpass(osc("triangle", f, dur + 0.25) + osc("square", f, dur + 0.25) * 0.25, 700) * adsr(n, 0.005, 0.12, 0.7, 0.08, hold=hold) * 0.55
    elif kind == "bass_saw":
        x = lowpass(osc("saw", f, dur + 0.25), 600) * adsr(n, 0.005, 0.1, 0.7, 0.08, hold=hold) * 0.45
    else:
        raise ValueError(kind)
    return x * vel


# ---- parts ---------------------------------------------------------------------------------------------------------

RHYTHMS = {  # bar rhythms in sixteenths (negative = rest)
    "sparse": [[8, 8], [12, 4], [4, 12], [16], [8, -4, 4]],
    "medium": [[4, 4, 4, 4], [6, 2, 4, 4], [4, 2, 2, 8], [2, 2, 4, 8], [8, 4, 4], [4, 4, 8], [-4, 4, 4, 4]],
    "busy": [[2, 2, 2, 2, 4, 4], [4, 2, 2, 4, 2, 2], [2, 2, 4, 2, 2, 4], [3, 3, 2, 4, 4], [2, 2, 2, 2, 2, 2, 4]],
}


def chord_tones(root, scale, degree):
    return [degree_note(root, scale, degree + k) for k in (0, 2, 4)]


def make_melody(spec, rng, chords):
    """Phrase-structured melody: motif A, A answered on the next chord, contrast B, A again; the second half varies and
    ends on the tonic. Returns (start_16th, length_16ths, note)."""
    root, scale = spec["root"] + 12, spec["scale"]
    lo, hi = root - 3, root + 17
    density = spec.get("density", "medium")

    def fit(target, chord_notes, prefer_chord):
        cands = [degree_note(root - 12, scale, d) for d in range(0, 28)]
        cands = [c for c in cands if lo <= c <= hi]
        if prefer_chord:
            pcs = {c % 12 for c in chord_notes}
            cands = [c for c in cands if c % 12 in pcs] or cands
        return min(cands, key=lambda c: abs(c - target))

    def phrase(bars, start_note, seed_rhythm=None):
        out, note, pos = [], start_note, 0
        rhythms = seed_rhythm or [RHYTHMS[density][rng.integers(len(RHYTHMS[density]))] for _ in bars]
        for bi, bar in enumerate(bars):
            chord = chords[bar % len(chords)]
            t = 0
            for length in rhythms[bi]:
                if length < 0:
                    t += -length
                    continue
                strong = t % 8 == 0
                step = rng.choice([-2, -1, -1, 1, 1, 2, 0, 3, -3])
                target = note + (step * 2 if not strong else rng.integers(-4, 5))
                note = fit(target, chord, strong)
                out.append((bar * 16 + t, length, note))
                t += length
        return out, rhythms, note

    notes = []
    total = spec["bars"]
    start = fit(root + 4, chords[0], True)
    for half in range(total // 8):
        base = half * 8
        a, ra, last = phrase([base, base + 1], start)
        a2, _, last = phrase([base + 2, base + 3], last, ra)  # answer: same rhythm on the next chords
        b, _, last = phrase([base + 4, base + 5], last)
        if half == total // 8 - 1:
            end, _, _ = phrase([base + 6], last)
            tonic = fit(root, chords[(base + 7) % len(chords)], True)
            end.append(((base + 7) * 16, 12, tonic))
        else:
            end, _, last = phrase([base + 6, base + 7], last, ra)
        notes += a + a2 + b + end
        start = last
    return notes


def build(spec, seed=1):
    """Renders a track spec to a looping mono float array at SR."""
    rng = np.random.default_rng(seed)
    bpm, bars = spec["bpm"], spec["bars"]
    six = 60.0 / bpm / 4  # seconds per sixteenth
    bar_len = samples(16 * six)
    total = bars * bar_len
    tail = samples(2.5)
    root, scale = spec["root"], spec["scale"]
    prog = spec["progression"]
    chords = [chord_tones(root, scale, prog[b % len(prog)]) for b in range(bars)]
    swing = spec.get("swing", 0.0)

    def at(sixteenth):
        s = sixteenth * six
        if swing and sixteenth % 2 == 1:
            s += swing * six
        return samples(s)

    lead = np.zeros(total + tail)
    back = np.zeros(total + tail)
    low = np.zeros(total + tail)
    drums = np.zeros(total + tail)

    for start, length, note in make_melody(spec, rng, chords):
        place(lead, voice(spec["lead"], note, length * six * 0.95, 0.9, spec.get("vibrato", 0.004)), at(start))
    if spec.get("double"):  # a second voice an octave down on the melody
        for start, length, note in make_melody(spec, np.random.default_rng(seed), chords):
            place(back, voice(spec["double"], note - 12, length * six * 0.95, 0.4), at(start))

    for b in range(bars):
        chord = chords[b]
        if spec.get("pad"):
            for n in chord:
                place(back, voice(spec["pad"], n, 16 * six), at(b * 16))
        arp = spec.get("arp")
        if arp:
            kind, rate = arp
            order = chord + [chord[1] + 12] if rng.random() < 0.5 else chord + [chord[0] + 12]
            steps = 16 // rate
            for k in range(steps):
                n = order[k % len(order)] if spec.get("arp_dir", "up") == "up" else order[::-1][k % len(order)]
                place(back, voice(kind, n + 12, rate * six * 0.9, 0.35), at(b * 16 + k * rate))
        bass = spec.get("bass", "half")
        r = chord[0] - 12
        pattern = {"half": [(0, 8, r), (8, 8, r)], "pump": [(k * 2, 2, r if k % 4 else r) for k in range(8)],
                   "walk": [(0, 4, r), (4, 4, r + 7), (8, 4, r + 12), (12, 4, r + 7)], "whole": [(0, 16, r)],
                   "gallop": [(0, 3, r), (3, 3, r), (6, 2, r + 12), (8, 3, r), (11, 3, r), (14, 2, r + 7)]}[bass]
        for s, l, n in pattern:
            place(low, voice(spec.get("bass_voice", "bass"), n, l * six * 0.9), at(b * 16 + s))
        drum_bar(spec.get("drums", "none"), drums, b, at, rng)

    mix = reverb(lead, SR, spec.get("room", 0.6), 0.35, spec.get("wet", 0.25), 0.0) * 0.85 + reverb(back, SR, 0.7, 0.4, 0.35, 0.0) + low + drums * 0.5
    loop = mix[:total].copy()
    loop[:tail] += mix[total:total + tail]  # the tail wraps into the start: seamless loop
    loop = lowpass(loop, spec.get("bright", 7000))
    return (loop / (np.max(np.abs(loop)) or 1.0) * 0.8).astype(np.float32)


def drum_bar(style, buf, b, at, rng):
    k, s, h, ho = kick(), snare() * 0.7, hat(), hat(open_=True)
    base = b * 16
    if style == "none":
        return
    if style == "light":
        place(buf, k * 0.6, at(base))
        for i in range(0, 16, 4):
            place(buf, h * 0.5, at(base + i + 2))
        place(buf, s * 0.35, at(base + 12))
    elif style == "rock":
        for i in (0, 6, 8):
            place(buf, k, at(base + i))
        for i in (4, 12):
            place(buf, s * 0.8, at(base + i))
        for i in range(0, 16, 2):
            place(buf, h * 0.6, at(base + i))
    elif style == "drive":
        for i in range(0, 16, 4):
            place(buf, k, at(base + i))
        for i in (4, 12):
            place(buf, s, at(base + i))
        for i in range(16):
            place(buf, h * (0.6 if i % 2 else 0.35), at(base + i))
        if b % 4 == 3:
            for i in (13, 14, 15):
                place(buf, s * 0.6, at(base + i))
    elif style == "march":
        place(buf, k, at(base))
        place(buf, k, at(base + 8))
        for i in (4, 6, 12, 14, 15):
            place(buf, s * (0.5 if i % 2 else 0.8), at(base + i))
    elif style == "tribal":
        for i, p in ((0, 90), (3, 90), (6, 130), (8, 90), (10, 110), (12, 150), (14, 130)):
            place(buf, tom(p) * 0.8, at(base + i))
        for i in range(0, 16, 4):
            place(buf, h * 0.4, at(base + i + 2))
    elif style == "halftime":
        place(buf, k, at(base))
        place(buf, k * 0.7, at(base + 10))
        place(buf, s * 0.8, at(base + 8))
        for i in range(0, 16, 2):
            place(buf, (ho if i == 14 else h) * 0.45, at(base + i))
