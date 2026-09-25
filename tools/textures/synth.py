"""Tileable procedural texture primitives (numpy). Every function returns arrays periodic in both axes."""
import numpy as np

SIZE = 256


def rng(seed: int) -> np.random.Generator:
    return np.random.default_rng(seed)


def spectral_noise(seed: int, size: int = SIZE, falloff: float = 2.0, low_cut: float = 1.0) -> np.ndarray:
    """Periodic fractal noise via filtered white noise in the frequency domain; returns [0,1]."""
    r = rng(seed)
    white = r.standard_normal((size, size))
    f = np.fft.fftfreq(size)[:, None] ** 2 + np.fft.fftfreq(size)[None, :] ** 2
    f = np.sqrt(f) * size
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1e-6) ** (falloff / 2.0))
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * amp))
    out -= out.min()
    return out / max(out.max(), 1e-9)


def band_noise(seed: int, freq: float, size: int = SIZE, width: float = 0.5) -> np.ndarray:
    """Periodic noise concentrated around one spatial frequency (cells/texture); [0,1]."""
    r = rng(seed)
    white = r.standard_normal((size, size))
    f = np.sqrt(np.fft.fftfreq(size)[:, None] ** 2 + np.fft.fftfreq(size)[None, :] ** 2) * size
    amp = np.exp(-((f - freq) / (freq * width + 1e-6)) ** 2)
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * amp))
    out -= out.min()
    return out / max(out.max(), 1e-9)


def voronoi(seed: int, cells: int, size: int = SIZE):
    """Periodic Worley noise: returns (F1 distance, F2-F1 edge, cell id) normalized."""
    r = rng(seed)
    pts = r.random((cells, 2)) * size
    ids = np.arange(cells)
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    f1 = np.full((size, size), 1e9, np.float32)
    f2 = np.full((size, size), 1e9, np.float32)
    cid = np.zeros((size, size), np.int32)
    for (px, py), i in zip(pts, ids):
        dx = np.abs(x - px)
        dx = np.minimum(dx, size - dx)
        dy = np.abs(y - py)
        dy = np.minimum(dy, size - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        cid = np.where(closer, i, cid)
        f1 = np.where(closer, d, f1)
    scale = size / np.sqrt(cells)
    return np.clip(f1 / scale, 0, 1), np.clip((f2 - f1) / scale, 0, 1), cid


def lerp_color(t: np.ndarray, a, b) -> np.ndarray:
    a = np.asarray(a, np.float32)[None, None, :]
    b = np.asarray(b, np.float32)[None, None, :]
    return a + (b - a) * t[..., None]


def gradient_map(t: np.ndarray, stops) -> np.ndarray:
    """Maps [0,1] to colours through stops [(pos, (r,g,b)), ...]."""
    out = np.zeros(t.shape + (3,), np.float32)
    stops = sorted(stops)
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        mask = (t >= p0) & (t <= p1)
        local = np.clip((t - p0) / max(p1 - p0, 1e-6), 0, 1)
        out[mask] = lerp_color(local, c0, c1)[mask]
    out[t < stops[0][0]] = stops[0][1]
    out[t > stops[-1][0]] = stops[-1][1]
    return out


def hex_rgb(h: str):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def shade(color, amount: float):
    return tuple(max(0.0, min(1.0, c * amount)) for c in color)


def normal_from_height(height: np.ndarray, strength: float) -> np.ndarray:
    """Tangent-space normal (x, y in [0,1]) from a periodic heightmap."""
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * strength
    n = np.stack([-dx, -dy, np.ones_like(height)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n[..., :2] * 0.5 + 0.5


def bricks(rows: int, cols: int, mortar: float, size: int = SIZE, stagger: bool = True):
    """Returns (mask 1=brick 0=mortar, brick id, local u, local v)."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    row = np.floor(y * rows)
    xs = x * cols + (0.5 * (row % 2) if stagger else 0)
    col = np.floor(xs) % cols
    u = xs - np.floor(xs)
    v = y * rows - row
    edge = np.minimum(np.minimum(u, 1 - u) / cols * rows, np.minimum(v, 1 - v))
    mask = (edge > mortar).astype(np.float32)
    return mask, (row * cols + col).astype(np.int32), u, v, edge


def planks(count: int, size: int = SIZE):
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    idx = np.floor(y * count)
    v = y * count - idx
    gap = np.minimum(v, 1 - v)
    return idx.astype(np.int32), x, v, gap
