"""Soft headwear sculpted as distance fields over the bare skull (headshape), in head-local units like the head:

- a cap (a baseball cap): a shallow crown of six panels that puff a little between their seams, close over the scalp
  and looser over the hair left below its rim, a button on top, a hemmed rim, and, unless asked not to, a stiff bill
  that curves down at its sides and dips a little toward its edge;
- a hood (hood.py): cloth resting on the crown, standing off the face round a turned opening and falling in folds onto
  the shoulders.

Both are meshed and cached with fieldmesh like the head they are fitted to. The cap's crown is a shell whose inside
is only kept near the rim (higher up the head fills it, so it is never seen)."""
import math

import numpy as np

from . import fieldmesh
from .sdf import F, carve, ellipsoid, smin


def _shell(base, t, thickness, k=0.0015):
    """The cloth between ``t - thickness`` and ``t`` off the surface ``base``."""
    return carve(base - t, base - (t - F(thickness)), k)


def _smooth(a, b, x):
    s = np.clip((x - a) / (b - a), 0.0, 1.0)
    return s * s * (3 - 2 * s)


def _rounded(a, b, k):
    """The intersection of two solids with the edge where they meet rounded to radius k."""
    ax, bx = a + F(k), b + F(k)
    out = np.sqrt(np.maximum(ax, 0) ** 2 + np.maximum(bx, 0) ** 2) + np.minimum(np.maximum(ax, bx), 0)
    return out - F(k)


def _cranium(p, fem):
    """The skull's vault alone (headshape's cranium, forehead and occiput, without the temples' hollows): cloth
    bridges those."""
    d = ellipsoid(p, (0, -0.012, 0.022), (0.0755, 0.095, 0.086))
    d = smin(d, ellipsoid(p, (0, 0.034, 0.03 + 0.003 * fem), (0.061, 0.05, 0.058)), 0.03)
    return smin(d, ellipsoid(p, (0, -0.056, -0.008), (0.058, 0.054, 0.06)), 0.03)


class Cap:
    """The baseball cap's parts (head-local): crown, hem, bill and button."""
    THICK = 0.0028
    PITCH, CURVE, REACH = math.radians(12), 0.02, 0.064     # the bill's dip, the fall of its sides, its reach

    def __init__(self, fem, visor=True, extra=0.0):
        self.fem, self.visor, self.extra = fem, visor, extra
        self.front = 0.037                                    # the rim's height over the brow
        y = np.linspace(0.05, 0.15, 400).astype(F)
        p = np.stack([np.zeros_like(y), y, np.full_like(y, self.front + 0.002)], 1)
        self.ya = float(y[np.argmax(self.outer(p) > 0)])      # where the bill leaves the crown

    def rim(self, y):
        return F(0.02) + F(self.front - 0.02) * np.clip(y / F(0.09), -1, 1)    # 0.003 at the nape .. over the brow

    def offset(self, p):
        """How far the crown stands off the skull: room for the hair near the rim, close over the bare scalp higher
        up, the front panels standing up off the forehead a little, each panel puffing out between its seams."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        up = z - self.rim(y)
        t = F(0.0055 + self.extra) + F(0.0048) * _smooth(F(0.03), F(0.0), up)
        t = t + F(0.0035) * _smooth(F(0.0), F(0.07), y) * _smooth(F(0.0), F(0.025), up) * _smooth(F(0.115), F(0.085), z)
        t = t + F(0.0009) * _smooth(F(0.006), F(0.002), up)                         # the hem
        ang = np.arctan2(x, y + F(0.012))
        r = np.hypot(x, y + F(0.012))
        return t + F(0.0016) * np.sin(F(3) * ang) ** 2 * _smooth(F(0.0), F(0.02), up) * _smooth(F(0.004), F(0.035), r)

    def outer(self, p):
        return _cranium(p, self.fem) - self.offset(p)

    def parts(self, p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        base = _cranium(p, self.fem)
        t = self.offset(p)
        rim = self.rim(y)
        crown = carve(carve(base - t, base - (t - F(self.THICK)), 0.0012), z - rim, 0.0012)
        hem = np.maximum(crown, z - rim - F(0.005))
        out = {"crown": crown, "hem": hem, "button": ellipsoid(p, (0, -0.012, 0.1128), (0.0064, 0.0064, 0.0034))}
        if self.visor:
            out["bill"] = self.bill(p, base, t)[0]
        return out

    def bill(self, p, base, t):
        """The stiff bill and its side (< 0 underneath): a sheet dipping toward its edge and curving down at its
        sides, round-edged, an oval in plan reaching ``REACH`` past the brow."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ya, za = F(self.ya), F(self.front + 0.0045)
        gy, cx = F(math.tan(self.PITCH)), F(0.082)
        surf = za - gy * (y - ya) - F(self.CURVE) * (x / cx) ** 2
        gx = F(2 * self.CURVE) * x / (cx * cx)
        side = (z - surf) / np.sqrt(F(1) + gy * gy + gx * gx)
        ay = F(self.REACH + 0.05)
        yc = ya + F(self.REACH) - ay
        plan = (np.sqrt((x / F(0.077)) ** 2 + ((y - yc) / ay) ** 2) - F(1)) * F(0.06)
        half = F(0.0021) - F(0.0004) * _smooth(F(0.0), F(1.0), (y - ya) / F(self.REACH))
        d = _rounded(np.abs(side) - half, plan, 0.0016)
        d = carve(d, base - (t - F(self.THICK)), 0.002)          # nothing inside the crown
        return np.maximum(d, ya - F(0.07) - y), side

    def field(self, p):
        parts = self.parts(p)
        d = np.minimum(parts["crown"], parts["hem"])
        if "bill" in parts:
            d = smin(d, parts["bill"], 0.0035)
        return smin(d, parts["button"], 0.0016)

    def regions(self, co):
        """Each point's part (the bill's underside is "under") and its shade: the seams between the panels a
        little darker."""
        parts = self.parts(co)
        names = [n for n in parts if n != "hem"]
        region = np.asarray(names, dtype=object)[np.argmin(np.stack([np.abs(parts[n]) for n in names], 1), 1)]
        region = np.where((region == "crown") & (co[:, 2] < self.rim(co[:, 1]) + 0.005), "hem", region)
        if self.visor:
            side = self.bill(co, _cranium(co, self.fem), self.offset(co))[1]
            region = np.where((region == "bill") & (side < 0), "under", region)
        x, y, z = co[:, 0], co[:, 1], co[:, 2]
        ang = np.arctan2(x, y + 0.012)
        r = np.hypot(x, y + 0.012)
        seam = r * np.abs(np.arcsin(np.clip(np.sin(3 * ang), -1, 1))) / 3        # distance to the nearest seam
        shade = 1 - 0.2 * np.exp(-(seam / 0.0022) ** 2) * (region == "crown") * _smooth(0.006, 0.012, r)
        return region, shade

    def cuts(self):
        """The lines the colours change along: the bill's join and its edge, the hem's top."""
        def bill_join(co):
            parts = self.parts(co)
            return parts["bill"] - np.minimum(parts["crown"], parts["hem"])

        def bill_edge(co):
            return self.bill(co, _cranium(co, self.fem), self.offset(co))[1]

        def hem_top(co):
            return co[:, 2] - self.rim(co[:, 1]) - 0.005
        return [hem_top] + ([bill_join, bill_edge] if self.visor else [])

    def hidden(self, centres):
        """Faces kept: all but the crown's inside above the hair under its rim."""
        base = _cranium(centres, self.fem)
        inside = np.abs(base - (self.offset(centres) - F(self.THICK))) < np.abs(base - self.offset(centres))
        high = centres[:, 2] - self.rim(centres[:, 1]) > F(0.016)
        return ~(inside & high & (centres[:, 1] < F(self.ya - 0.01)))


def cap_field(shape, visor=True):
    return Cap(shape.fem, visor).field


BOUNDS = ((-0.12, -0.14, -0.04), (0.12, 0.19, 0.13))


def cap_mesh(head, visor=True, extra=0.0):
    """The cap's mesh (world coordinates) and each face's region with a shade per vertex; ``extra`` stands it further
    off the head (a skeleton's, which is settled onto the smaller skull)."""
    from .shellmesh import centres, place, refine
    cap = Cap(head.shape.fem, visor, extra)
    obj = fieldmesh.build(cap.field, BOUNDS, 0.001, 13000, "hat_cap", ("cap", head.shape.fem, visor, extra), ("headwear.py",),
                          "headwear", keep=cap.hidden)
    refine(obj, cap.field, cuts=cap.cuts())
    co = place(obj, head)
    region = cap.regions(centres(obj, co))[0]
    return obj, (region, cap.regions(co)[1])
