"""Small DSP kit for generating the game's sounds and music offline (numpy + scipy): band-limited oscillators, envelopes,
filters, a plate-ish reverb, drum voices and note helpers. Everything returns float32 mono arrays in [-1, 1]."""
import numpy as np
from scipy import signal

SR = 32000


def seconds(n, sr=SR):
    return np.arange(n, dtype=np.float64) / sr


def samples(dur, sr=SR):
    return max(1, int(round(dur * sr)))


def midi_hz(note):
    return 440.0 * 2.0 ** ((np.asarray(note, dtype=np.float64) - 69.0) / 12.0)


def phase_of(freq, n, sr=SR):
    """Phase (cycles) for a constant or per-sample frequency."""
    f = np.broadcast_to(np.asarray(freq, dtype=np.float64), (n,))
    return np.cumsum(f) / sr


def _blep(t, dt):
    """PolyBLEP residual for smoothing discontinuities (t = phase fraction, dt = phase increment)."""
    r = np.zeros_like(t)
    a = t < dt
    x = t[a] / dt[a]
    r[a] = x + x - x * x - 1.0
    b = t > 1.0 - dt
    x = (t[b] - 1.0) / dt[b]
    r[b] = x * x + x + x + 1.0
    return r


def osc(kind, freq, dur, sr=SR, duty=0.5):
    """sine | triangle | saw | square | pulse | noise, with polyBLEP anti-aliasing for the edged waves."""
    n = samples(dur, sr)
    if kind == "noise":
        return np.random.default_rng(int(np.sum(np.atleast_1d(freq)) * 1000) % (2 ** 31)).uniform(-1, 1, n)
    f = np.broadcast_to(np.asarray(freq, dtype=np.float64), (n,))
    ph = np.cumsum(f) / sr
    t = ph % 1.0
    dt = np.clip(f / sr, 1e-6, 0.5)
    if kind == "sine":
        return np.sin(2 * np.pi * ph)
    if kind == "triangle":
        return 2.0 * np.abs(2.0 * t - 1.0) - 1.0
    if kind == "saw":
        return (2.0 * t - 1.0) - _blep(t, dt)
    if kind in ("square", "pulse"):
        d = duty if kind == "pulse" else 0.5
        s = np.where(t < d, 1.0, -1.0)
        s += _blep(t, dt)
        s -= _blep((t + 1.0 - d) % 1.0, dt)
        return s
    raise ValueError(kind)


def adsr(n, a=0.005, d=0.1, s=0.6, r=0.1, sr=SR, hold=None):
    """Attack/decay/sustain/release over n samples; the release starts at `hold` seconds (default: n minus release)."""
    t = seconds(n, sr)
    total = n / sr
    hold = total - r if hold is None else hold
    e = np.where(t < a, t / max(a, 1e-6), 0.0)
    dec = (t >= a) & (t < a + d)
    e[dec] = 1.0 - (1.0 - s) * (t[dec] - a) / max(d, 1e-6)
    sus = (t >= a + d) & (t < hold)
    e[sus] = s
    rel = t >= hold
    level = s if hold >= a + d else max(0.0, 1.0 - (1.0 - s) * (hold - a) / max(d, 1e-6))
    e[rel] = level * np.clip(1.0 - (t[rel] - hold) / max(r, 1e-6), 0.0, 1.0)
    return e


def decay(n, tau, sr=SR, attack=0.002):
    t = seconds(n, sr)
    return np.minimum(t / attack, 1.0) * np.exp(-t / tau)


def lowpass(x, cutoff, sr=SR, order=2):
    b, a = signal.butter(order, min(cutoff, sr * 0.45) / (sr / 2), "low")
    return signal.lfilter(b, a, x)


def highpass(x, cutoff, sr=SR, order=2):
    b, a = signal.butter(order, min(cutoff, sr * 0.45) / (sr / 2), "high")
    return signal.lfilter(b, a, x)


def bandpass(x, lo, hi, sr=SR, order=2):
    b, a = signal.butter(order, [lo / (sr / 2), min(hi, sr * 0.45) / (sr / 2)], "band")
    return signal.lfilter(b, a, x)


def sweep_lowpass(x, start, end, sr=SR, steps=24):
    """A lowpass whose cutoff glides from start to end (Hz) across the sound, in blocks."""
    out = np.zeros_like(x)
    edges = np.linspace(0, len(x), steps + 1).astype(int)
    zi = None
    for i in range(steps):
        c = start * (end / start) ** (i / max(1, steps - 1))
        b, a = signal.butter(2, min(c, sr * 0.45) / (sr / 2), "low")
        if zi is None:
            zi = signal.lfilter_zi(b, a) * 0.0
        out[edges[i]:edges[i + 1]], zi = signal.lfilter(b, a, x[edges[i]:edges[i + 1]], zi=zi)
    return out


def reverb(x, sr=SR, room=0.6, damp=0.35, wet=0.25, tail=1.2):
    """Schroeder reverb (parallel combs into series allpasses); appends a tail of `tail` seconds."""
    if tail > 0:
        x = np.concatenate([x, np.zeros(samples(tail, sr))])
    out = np.zeros_like(x)
    for ms in (29.7, 37.1, 41.1, 43.7):
        d = int(ms * sr / 1000)
        g = 0.7 + 0.28 * room
        b = [1.0 - damp]
        a = np.zeros(d + 1)
        a[0] = 1.0
        a[d] = -g * (1.0 - damp)
        comb = signal.lfilter([0.0] * d + [1.0], a, x)
        out += lowpass(comb, 5000 - 3000 * damp, sr, 1) * 0.25
    for ms, g in ((5.0, 0.7), (1.7, 0.7)):
        d = int(ms * sr / 1000)
        b = np.zeros(d + 1)
        b[0], b[d] = -g, 1.0
        a = np.zeros(d + 1)
        a[0], a[d] = 1.0, -g
        out = signal.lfilter(b, a, out)
    return x * (1.0 - wet) + out * wet


def normalize(x, peak=0.89):
    m = np.max(np.abs(x)) if len(x) else 0.0
    return x * (peak / m) if m > 1e-9 else x


def fade(x, fin=0.002, fout=0.01, sr=SR):
    n1, n2 = min(len(x), samples(fin, sr)), min(len(x), samples(fout, sr))
    y = x.copy()
    y[:n1] *= np.linspace(0, 1, n1)
    y[len(y) - n2:] *= np.linspace(1, 0, n2)
    return y


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


def place(buf, sound, at):
    """Adds `sound` into `buf` starting at sample `at` (clipped to the buffer)."""
    if at >= len(buf):
        return
    end = min(len(buf), at + len(sound))
    buf[at:end] += sound[:end - at]


# ---- drum voices ------------------------------------------------------------------------------------------------

def kick(sr=SR, punch=1.0):
    n = samples(0.35, sr)
    f = 45 + 110 * np.exp(-seconds(n, sr) / 0.04)
    body = osc("sine", f, 0.35, sr) * decay(n, 0.12, sr)
    click = lowpass(osc("noise", 1, 0.35, sr), 3000, sr) * decay(n, 0.004, sr) * 0.4
    return (body + click) * punch


def snare(sr=SR, tone=190):
    n = samples(0.25, sr)
    noise = bandpass(osc("noise", 2, 0.25, sr), 1500, 9000, sr) * decay(n, 0.07, sr)
    body = osc("triangle", tone, 0.25, sr) * decay(n, 0.05, sr) * 0.6
    return noise * 0.8 + body


def hat(sr=SR, open_=False):
    dur = 0.3 if open_ else 0.06
    n = samples(dur, sr)
    return highpass(osc("noise", 3, dur, sr), 7000, sr) * decay(n, 0.12 if open_ else 0.018, sr) * 0.5


def tom(pitch=110, sr=SR):
    n = samples(0.3, sr)
    f = pitch * (1 + 0.5 * np.exp(-seconds(n, sr) / 0.03))
    return osc("sine", f, 0.3, sr) * decay(n, 0.12, sr)
