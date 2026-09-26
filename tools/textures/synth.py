"""Tileable procedural texture primitives (numpy). Every function returns arrays periodic in both axes."""
import numpy as np

SIZE = 256


def rng(seed: int) -> np.random.Generator:
    return np.random.default_rng(seed)


def normalize(a: np.ndarray) -> np.ndarray:
    lo, hi = a.min(), a.max()
    return (a - lo) / max(hi - lo, 1e-9)


def _freq(size: int) -> np.ndarray:
    return np.sqrt(np.fft.fftfreq(size)[:, None] ** 2 + np.fft.fftfreq(size)[None, :] ** 2) * size


def spectral_noise(seed: int, size: int = SIZE, falloff: float = 2.0, low_cut: float = 1.0) -> np.ndarray:
    """Periodic fractal noise via filtered white noise in the frequency domain; returns [0,1]."""
    white = rng(seed).standard_normal((size, size))
    f = _freq(size)
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1e-6) ** (falloff / 2.0))
    return normalize(np.real(np.fft.ifft2(np.fft.fft2(white) * amp)))


def band_noise(seed: int, freq: float, size: int = SIZE, width: float = 0.5) -> np.ndarray:
    """Periodic noise concentrated around one spatial frequency (cells/texture); [0,1]."""
    white = rng(seed).standard_normal((size, size))
    f = _freq(size)
    amp = np.exp(-((f - freq) / (freq * width + 1e-6)) ** 2)
    return normalize(np.real(np.fft.ifft2(np.fft.fft2(white) * amp)))


def blur(a: np.ndarray, sigma: float) -> np.ndarray:
    """Periodic Gaussian blur (sigma in pixels) of a 2D field."""
    if sigma <= 0:
        return a
    size = a.shape[0]
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(a.shape[1])[None, :]
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * kernel))


def cavity(height: np.ndarray, sigma: float = 3.0, strength: float = 4.0) -> np.ndarray:
    """Ambient occlusion from a heightmap: 1 on ridges and flats, darker in pits and grooves."""
    return np.clip(1.0 - np.maximum(blur(height, sigma) - height, 0) * strength, 0, 1)


def convexity(height: np.ndarray, sigma: float = 2.0) -> np.ndarray:
    """Positive on ridges and edges (weathering catches them), negative in hollows."""
    return height - blur(height, sigma)


def sample(field: np.ndarray, x: np.ndarray, y: np.ndarray) -> np.ndarray:
    """Periodic bilinear sampling of a 2D field at fractional pixel coordinates."""
    h, w = field.shape
    x0 = np.floor(x).astype(np.int64)
    y0 = np.floor(y).astype(np.int64)
    fx, fy = x - x0, y - y0
    x0 %= w
    y0 %= h
    x1, y1 = (x0 + 1) % w, (y0 + 1) % h
    a = field[y0, x0] * (1 - fx) + field[y0, x1] * fx
    b = field[y1, x0] * (1 - fx) + field[y1, x1] * fx
    return a * (1 - fy) + b * fy


def warp(field: np.ndarray, seed: int, amount: float, falloff: float = 2.4) -> np.ndarray:
    """Domain-warps a periodic field by smooth noise (amount in pixels)."""
    size = field.shape[0]
    wx = (spectral_noise(seed, size, falloff) - 0.5) * 2 * amount
    wy = (spectral_noise(seed + 1, size, falloff) - 0.5) * 2 * amount
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    return sample(field, x + wx, y + wy)


def voronoi(seed: int, cells: int, size: int = SIZE, warp_amount: float = 0.0):
    """Periodic Worley noise: (F1 distance, F2-F1 edge, cell id) with distances normalized by the cell spacing."""
    f1, edge, cid, _, _ = voronoi_full(seed, cells, size, warp_amount)
    return f1, edge, cid


def voronoi_full(seed: int, cells: int, size: int = SIZE, warp_amount: float = 0.0):
    """Worley noise plus the offset (dx, dy, in cell spacings) from each pixel to its cell's site."""
    r = rng(seed)
    pts = r.random((cells, 2)) * size
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    if warp_amount > 0:
        x = x + (spectral_noise(seed + 101, size, 2.2) - 0.5) * 2 * warp_amount
        y = y + (spectral_noise(seed + 102, size, 2.2) - 0.5) * 2 * warp_amount
    f1 = np.full((size, size), 1e9, np.float32)
    f2 = np.full((size, size), 1e9, np.float32)
    cid = np.zeros((size, size), np.int32)
    ox = np.zeros((size, size), np.float32)
    oy = np.zeros((size, size), np.float32)
    for i, (px, py) in enumerate(pts):
        dx = (x - px + size / 2) % size - size / 2
        dy = (y - py + size / 2) % size - size / 2
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        cid = np.where(closer, i, cid)
        ox = np.where(closer, dx, ox)
        oy = np.where(closer, dy, oy)
        f1 = np.where(closer, d, f1)
    scale = size / np.sqrt(cells)
    return np.clip(f1 / scale, 0, 2), np.clip((f2 - f1) / scale, 0, 2), cid, ox / scale, oy / scale


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


def palette(r: dict):
    """(base, dark, light) colours of a recipe."""
    return tuple(hex_rgb(r[k]) for k in ("base", "dark", "light"))


def tone_map(t: np.ndarray, r: dict, mid: float = 0.5) -> np.ndarray:
    base, dark, light = palette(r)
    return gradient_map(np.clip(t, 0, 1), [(0.0, shade(dark, 0.8)), (0.25, dark), (mid, base), (0.9, light), (1.0, shade(light, 1.08))])


def hex_rgb(h: str):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def shade(color, amount: float):
    return tuple(max(0.0, min(1.0, c * amount)) for c in color)


def hue_jitter(rgb: np.ndarray, seed: int, amount: float = 0.06, falloff: float = 2.6) -> np.ndarray:
    """Low-frequency warm/cool and brightness drift so large areas never look flat."""
    size = rgb.shape[0]
    warm = spectral_noise(seed, size, falloff) - 0.5
    bright = spectral_noise(seed + 1, size, falloff) - 0.5
    shift = np.stack([warm, warm * 0.2, -warm], -1) * amount
    return np.clip(rgb * (1 + bright[..., None] * amount * 2) + shift, 0, 1)


def normal_from_height(height: np.ndarray, strength: float) -> np.ndarray:
    """Tangent-space normal (x, y in [0,1]) from a periodic heightmap (Sobel, sharper than central differences)."""
    h = height
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 2 + (np.roll(np.roll(h, -1, 1), 1, 0) - np.roll(np.roll(h, 1, 1), 1, 0)) \
        + (np.roll(np.roll(h, -1, 1), -1, 0) - np.roll(np.roll(h, 1, 1), -1, 0))
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 2 + (np.roll(np.roll(h, -1, 0), 1, 1) - np.roll(np.roll(h, 1, 0), 1, 1)) \
        + (np.roll(np.roll(h, -1, 0), -1, 1) - np.roll(np.roll(h, 1, 0), -1, 1))
    n = np.stack([-dx * strength / 4, -dy * strength / 4, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n[..., :2] * 0.5 + 0.5


def stamp_window(size: int, cx: float, cy: float, half: int):
    """Periodic index window around (cx, cy) plus local pixel-centre offsets from the stamp centre."""
    ix = (np.arange(-half, half + 1) + int(cx)) % size
    iy = (np.arange(-half, half + 1) + int(cy)) % size
    oy, ox = np.mgrid[-half:half + 1, -half:half + 1].astype(np.float32)
    return np.ix_(iy, ix), ox + (int(cx) - cx), oy + (int(cy) - cy)


def bricks(rows: int, cols: int, mortar: float, size: int = SIZE, stagger: bool = True):
    """Returns (mask 1=brick 0=mortar, brick id, local u, local v, distance to the brick edge)."""
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
