"""The metal caps (capshapes' helmet and greathelm), sculpted as distance fields in head-local units:

- helmet: an open-faced helmet: a dome with a raised crest and a nasal, a riveted brow band, and below it one
  curved piece that wraps the cheeks, swells over the ears and flares out over the nape, its front corners rounded
  and every edge rolled into a bead;
- greathelm: a closed helm round the whole head, straight-sided with a domed top, an eye slit with a bar between the eyes,
  breathing holes and a cross of bands, its foot flared a little over a gorget (plate.py's armour), or, given what is
  under it (``under``, a field like hood.drape's), flaring into a skirt that rests on the shoulders and chest; the
  edge beaded. Its bands and holes are painted on, not cut or raised
  (relief there streaks the shading of the long flat walls), their edges cut clean.

Each returns its parts (name -> distance) and the distance to its inside surface (the greathelm's is painted dark: it
is seen only through the eye slit).
"""
import numpy as np

from .headwear import _cranium, _rounded, _shell, _smooth
from .sdf import F, carve, ellipsoid, smax, smin, sphere

EARS = ((0.068, -0.014, -0.012), (0.02, 0.022, 0.028))         # the room a person's ear needs under a helmet


def _bead(keep, mid, r):
    """A rolled edge of radius ``r`` along the line where ``keep`` (negative on the metal) ends, centred on the
    plate's mid-surface ``mid``."""
    return np.sqrt((keep + F(r)) ** 2 + mid * mid) - F(r)


def _rivets(fem, heights, n, offset, radius, phase=0.5):
    """``n`` rivet centres round the vault at ``heights`` (a function of azimuth) and ``offset`` off it."""
    ang = (np.arange(n) + phase) * (2 * np.pi / n)
    dirs = np.stack([np.sin(ang), np.cos(ang), np.zeros_like(ang)], 1)
    z = heights(ang)
    lo, hi = np.zeros(n), np.full(n, 0.15)
    for _ in range(22):
        mid = (lo + hi) / 2
        pts = (np.asarray((0, -0.012, 0)) + dirs * mid[:, None]).astype(F)
        pts[:, 2] = z
        inside = _cranium(pts, fem) < offset
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    return [(dirs[k, 0] * lo[k], -0.012 + dirs[k, 1] * lo[k], z[k], radius) for k in range(n)]


def _wrap(p, skull=False):
    """What the helmet's guard wraps: the cheeks and jaw, the ears and the nape, which it flares out over (a bare
    skull's narrower cheeks, and no ears)."""
    y, z = p[:, 1], p[:, 2]
    q = (p - np.asarray((0, -0.02, -0.02), dtype=F)) / np.asarray((0.085 if skull else 0.09, 0.118, 0.1), dtype=F)
    q[:, 1] *= np.where(q[:, 1] < 0, F(0.118 / 0.096), F(1))             # an egg in plan: narrower at the face
    wrap = (np.linalg.norm(q, axis=1) - F(1)) * F(0.09)
    (ex, ey, ez), er = EARS
    for sx in (() if skull else (-1.0, 1.0)):
        wrap = smin(wrap, ellipsoid(p, (ex * sx, ey, ez), er), 0.025)
    wrap = smin(wrap, _cranium(p, 0.0) - F(0.0065 if skull else 0.0), 0.012)      # (up under the dome it lies just inside)
    return wrap - F(0.011) * _smooth(F(-0.03), F(-0.08), z) * _smooth(F(-0.02), F(-0.09), y)


def _on_guard(y, z, sx, off=0.0045, skull=False):
    """A point on the guard's outer surface at height ``z``, ``y`` along, on side ``sx``."""
    lo, hi = 0.0, 0.15
    for _ in range(24):
        mid = (lo + hi) / 2
        q = np.asarray([[sx * mid, y, z]], dtype=F)
        lo, hi = (mid, hi) if _wrap(q, skull)[0] < off else (lo, mid)
    return (sx * lo, y, z)


def helmet(p, fem, skull=False):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    base = _cranium(p, fem)
    rim = F(0.03) + F(0.018) * np.clip(y / F(0.09), -1, 1)          # low at the back, over the brow in front
    dome = carve(_shell(base, 0.0085, 0.0042), z - rim + F(0.004), 0.0015)
    crest = carve(np.maximum(np.abs(x) - F(0.0035), base - F(0.0135)), z - rim - F(0.012), 0.003)
    brow = carve(carve(_shell(base, 0.011, 0.005), rim + F(0.012) - z, 0.0012), z - rim + F(0.006), 0.0012)
    nasal = np.maximum(ellipsoid(p, (0, 0.1, 0.016), (0.0065, 0.0085, 0.032)), -(z + F(0.01)))
    # The guard: one curved plate round the cheeks, ears and nape, a little off them, tucked under the brow band.
    wrap = _wrap(p, skull)
    front = F(0.052) - F(0.017) * _smooth(F(-0.02), F(-0.09), z)
    bottom = F(-0.09) + F(0.018) * _smooth(F(0.0), F(-0.06), y)
    edge = _rounded(y - front, bottom - z, 0.017)
    plate = np.maximum(_shell(wrap, 0.0045, 0.0034), np.maximum(edge, z - rim - F(0.006)))
    bead = np.maximum(_bead(edge, wrap - F(0.0028), 0.0022), z - rim)
    crown = smin(smin(dome, crest, 0.004), smin(plate, bead, 0.0012), 0.004)
    heights = lambda a: 0.03 + 0.018 * np.clip(0.1 * np.cos(a) / 0.09, -1, 1) + 0.003
    rivets = _rivets(fem, heights, 16, 0.0128, 0.0042, 0.5)
    rivets += [_on_guard(yy, zz, sx, 0.0035, skull) + (0.0032,) for sx in (-1, 1) for yy, zz in ((0.034, -0.03), (0.024, -0.064))]
    studs = None
    for cx, cy, cz, r in rivets:
        d = sphere(p, (cx, cy, cz), r)
        studs = d if studs is None else np.minimum(studs, d)
    inside = np.minimum(np.abs(base - F(0.0043)), np.abs(wrap - F(0.0011)))
    return {"crown": crown, "band": brow, "peak": nasal, "rivet": studs}, inside


def greathelm(p, fem, under=None):
    """Its parts, the distance to its inside surface, and the painted regions (name -> negative where painted): the
    cross of bands and the breathing holes."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    yc = 0.006
    if under is not None:          # a skirt flaring onto the shoulders and chest
        grow = F(1) + F(0.2) * _smooth(F(-0.14), F(-0.225), z) ** F(1.6)
    else:                          # its foot flared a little, over a gorget
        grow = F(1) + F(0.07) * _smooth(F(-0.095), F(-0.128), z)
    rx, ry = F(0.1) * grow, F(0.124) * grow
    m = np.hypot(x / rx, (y - F(yc)) / ry)            # (an ellipse, so the walls run smoothly into the dome)
    side = (m - F(1)) * F(0.1)
    dome = ellipsoid(p, (0, yc, 0.02), (0.1, 0.124, 0.115))
    outer = np.minimum(np.maximum(side, z - F(0.02)), dome)     # straight walls up to the dome's equator
    cross = np.maximum(np.minimum(np.abs(x) - F(0.0075), np.abs(z - F(0.017)) - F(0.0065)), -(z + F(0.12)))
    holes = None
    for k in range(10):
        hx, hz = 0.026 + 0.011 * (k % 5), -0.05 - 0.013 * (k // 5)
        hole = np.maximum(np.hypot(np.abs(x) - F(hx), z - F(hz)) - F(0.0024), F(0.03) - y)
        holes = hole if holes is None else np.minimum(holes, hole)
    keep = -(z + F(0.235)) if under is not None else -(z + F(0.128))
    if under is not None:
        keep = np.maximum(keep, F(0.003) - under(p))           # down to the shoulders and chest
    shell = smax(carve(outer, outer + F(0.0045), 0.001), keep, 0.0012)
    shell = smin(shell, _bead(keep, outer + F(0.00225), 0.0026), 0.001)
    slit = np.maximum(np.maximum(np.abs(z + F(0.003)) - F(0.0045), np.abs(x) - F(0.068)), F(0.02) - y)
    slit = np.maximum(slit, F(0.0065) - np.abs(x))          # the bar between the eyes stays
    shell = carve(shell, slit, 0.0012)
    return {"crown": shell}, np.abs(outer + F(0.0045)), {"band": cross, "slit": holes}
