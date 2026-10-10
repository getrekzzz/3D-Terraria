"""The hoplite's Corinthian helm (hoplite.py), sculpted as distance fields in head-local units round a bare skull:
a bronze shell closing round the whole head, domed over the vault, its cheek pieces running down past the jaw with
a narrow slot between them, almond eye openings either side of a nasal that hangs between them, a ridge raised
round the brow and arched over the eyes, the nape drawn in and flaring out again over the neck, the foot and the
openings rolled into a bead; the skull's teeth and burning eyes show through the openings. On top a stilt carries
a tall horsehair crest that rises over the brow and sweeps back and down behind the head (its own mesh, painted in
streaks along the hair).
"""
import numpy as np

from . import fieldmesh, matter
from .headwear import _smooth
from .sdf import F, carve, ellipsoid, smax, smin

T = 0.0058                                      # the bronze's thickness
SOURCES = ("corinthian.py",)
BOUNDS = ((-0.115, -0.25, -0.18), (0.115, 0.15, 0.26))
CREST_C = (-0.014, 0.0)                         # (y, z) the crest is swept round


def _outer(p):
    """The helm's outer surface: the dome over the vault, and below it a column round the face and jaw whose plan
    is an egg (fuller behind), the cheeks narrowing toward the jaw, the face guard full over the nose, the nape
    drawn in and flaring out at the foot."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    rx = F(0.086) - F(0.011) * _smooth(F(0.01), F(-0.08), z) - F(0.004) * _smooth(F(-0.08), F(-0.15), z)
    yf = F(0.1) + F(0.007) * _smooth(F(0.01), F(-0.04), z) - F(0.006) * _smooth(F(-0.06), F(-0.15), z)
    yb = F(-0.118) + F(0.014) * _smooth(F(0.0), F(-0.075), z) - F(0.034) * _smooth(F(-0.075), F(-0.16), z)
    yc = (yf + yb) / 2
    front = y > yc
    ry = np.where(front, yf - yc, yc - yb)
    n = np.where(front, F(2.7), F(2.0))                 # (squarer in front: room for the cheekbones)
    m = ((np.abs(x) / rx) ** n + (np.abs(y - yc) / ry) ** n) ** (F(1) / n)
    col = (m - F(1)) * np.minimum(rx, ry)
    dome = ellipsoid(p, (0, -0.009, 0.012), (0.086, 0.109, 0.104))
    o = smin(np.maximum(col, z - F(0.012)), dome, 0.012)
    return o - F(0.0013) * np.exp(-((z - _brow(x, y)) / F(0.0028)) ** 2)        # a ridge round the brow


def _brow(x, y):
    """The height of the ridge round the brow: level behind, arched over each eye in front."""
    return F(0.026) + F(0.009) * np.exp(-((np.abs(x) - F(0.034)) / F(0.026)) ** 2) * _smooth(F(0.04), F(0.08), y)


def _foot(p):
    """Negative above the helm's foot: lowest at the chin and the flared nape, a little higher under the ears."""
    y, z = p[:, 1], p[:, 2]
    bottom = F(-0.128) - F(0.012) * _smooth(F(0.0), F(0.09), y) - F(0.03) * _smooth(F(-0.04), F(-0.13), y)
    return bottom - z


def _openings(p):
    """Negative in the face's openings: two almond eyes, their outer corners lifted, joined under a hanging nasal
    to a slot running down between the cheek pieces."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    tilt = F(0.16) * (ax - F(0.034))
    eye = (np.hypot((ax - F(0.034)) / F(0.025), (z + F(0.003) - tilt) / F(0.0125)) - F(1)) * F(0.0125)
    width = F(0.0115) + F(0.009) * _smooth(F(-0.05), F(-0.15), z)
    slot = np.maximum(ax - width, z + F(0.03))
    hole = smin(eye, slot, 0.011)
    nasal = np.maximum(ax - F(0.0072), F(-0.052) - z)
    hole = smax(hole, -nasal, 0.0015)
    return np.maximum(hole, F(0.03) - y)


def _bead(keep, mid, r):
    return np.sqrt((keep + F(r)) ** 2 + mid * mid) - F(r)


def helm_field(p):
    o = _outer(p)
    mid = o + F(T / 2)
    shell = carve(o, o + F(T), 0.001)
    foot, hole = _foot(p), _openings(p)
    shell = smax(smax(shell, foot, 0.0012), -hole, 0.0012)
    shell = smin(shell, np.maximum(_bead(foot, mid, 0.0026), -hole), 0.001)
    shell = smin(shell, np.maximum(_bead(-hole, mid, 0.0022), foot), 0.001)
    _, phi = _polar(p)
    stilt = np.maximum(np.maximum(np.abs(p[:, 0]) - F(0.0045), o - F(0.009)), np.maximum(phi - F(0.5), F(-1.6) - phi))
    return smin(shell, stilt, 0.004)


def _polar(p):
    y, z = p[:, 1] - F(CREST_C[0]), p[:, 2] - F(CREST_C[1])
    return np.hypot(y, z), np.arctan2(y, z)            # (0 straight up, positive toward the front)


def crest_field(p):
    """The horsehair crest: a fan standing on the stilt from over the brow to behind the nape, tallest over the
    crown, its hair spreading wider toward the top."""
    x = p[:, 0]
    r, phi = _polar(p)
    r_in = F(0.108)
    h = F(0.088) * _smooth(F(0.72), F(0.3), phi) * (F(0.25) + F(0.75) * _smooth(F(-2.15), F(-0.9), phi))
    h = h + F(0.012) * _smooth(F(-2.2), F(-1.6), phi) * _smooth(F(-0.9), F(-1.5), phi)      # (the tail hangs full)
    radial = smax(r_in - r, r - r_in - h, 0.006)
    width = F(0.0065) + F(0.011) * np.clip((r - r_in) / np.maximum(h, F(0.02)), 0, 1)
    ends = np.maximum(phi - F(0.72), F(-2.2) - phi) * r
    return smax(smax(radial, np.abs(x) - width, 0.004), ends, 0.008)


def helm(head, bronze, crest):
    """The helm and its crest as objects carried by the head."""
    from .rig import rigid
    from .shellmesh import place
    out = []
    for name, field, h, target, kind in (("helm_corinthian", helm_field, 0.0012, 12000, "metal"),
                                         ("corinthian_crest", crest_field, 0.0016, 5000, "hair")):
        obj = fieldmesh.build(field, BOUNDS, h, target, name, ("corinthian", name), SOURCES, "headwear")
        q = place(obj, head).astype(F)
        if kind == "metal":
            o = _outer(q)
            line = np.exp(-((q[:, 2] - _brow(q[:, 0], q[:, 1])) / F(0.0026)) ** 2)
            inner = _smooth(F(-T * 0.35), F(-T * 0.75), o)
            edge = _smooth(F(0.006), F(0.0), np.minimum(np.abs(_openings(q)), np.abs(_foot(q))))
            grain = matter.fbm(q, 18.0, 3, 71)
            rgb = bronze * (0.86 + 0.2 * grain)[:, None] * (1 + 0.1 * line - 0.12 * edge)[:, None]
            rgb = rgb * (1 - inner[:, None]) + bronze * 0.3 * inner[:, None]
            matter.slot(obj, "char:metal")
        else:
            r, phi = _polar(q)
            streak = matter.noise(np.stack([q[:, 0] * 3, phi * 0.05, r * 0.4], 1), 160.0, 73)
            tip = _smooth(F(0.05), F(0.09), r - F(0.108))
            rgb = crest * (0.7 + 0.35 * streak)[:, None] * (1 - 0.25 * tip)[:, None]
            matter.slot(obj, "char:cloth")
        matter.paint(obj, rgb)
        rigid(obj, "head")
        out.append(obj)
    return out
