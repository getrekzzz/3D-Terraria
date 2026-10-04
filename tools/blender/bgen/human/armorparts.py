"""The plate pieces armour styles are laid out from (armor.py): torso plates (front, back, belly bands, a belt), a cap
over a ball (a shoulder, a knee) and plates on a limb segment, each with optional seams, a raised ridge, light
strips and an edge colour. Sizes are in metres at an average man's frame (scaled by Frame.s); outlines are drawn in
the core's surface coordinates (plates.around_torso, plates.along).
"""
import math

import numpy as np

from . import plates as P
from .sdf import F, smax, smin

SOURCES = ("armorparts.py",)


def _seam_lines(u, v, seams, w):
    """Line fields (< 0 on the line) for seams given as ("u", at, lo, hi) or ("v", at, lo, hi)."""
    out = []
    for axis, at, lo, hi in seams:
        a, b = (u, v) if axis == "u" else (v, u)
        out.append(smax(np.abs(a - F(at)) - F(w), np.abs(b - F((lo + hi) / 2)) - F((hi - lo) / 2), 0.0005))
    return out


def _edge_shade(sm, outline, color, edge):
    """Colours shading from ``edge`` at a plate's outline to its own colour a little way in (or None)."""
    if edge is None:
        return None
    base, edge, width = np.asarray(color or sm.color), np.asarray(edge), F(0.014 * sm.s)

    def shade(co):
        t = np.clip(-outline(np.asarray(co, dtype=F)) / width, 0, 1)[:, None] ** 0.8
        return edge + (base - edge) * t
    return shade


def torso(sm, name, top, bottom, half, inner=0.008, thick=0.005, back=False, corner=0.03, notch=None, ridge=None,
          seams=(), lines=(), color=None, slot="char:metal", mirror=False, centre=0.0, rim=0.0025, panel=None, edge=None):
    """A plate on the torso between heights ``bottom`` and ``top`` (frame units), ``half`` its half-width round the
    body; ``notch`` (half-width, depth) cuts the neckline; ``ridge`` (half-width, height) raises a strip down its
    middle; ``panel`` (inset, height) raises an inner panel; seams in (u, z) frame units; ``edge``: the colour it
    shades to at its outline."""
    s, fr = sm.s, sm.fr
    core = sm.torso
    zc, hz = (top + bottom) / 2 * s, (top - bottom) / 2 * s

    def outline(p):
        u, z = P.around_torso(fr, p, back=back)
        d = P.box(u, z, centre * s, zc, half * s, hz, corner * s)
        if notch is not None:
            a, b = notch[0] * s, notch[1] * s
            e = (np.sqrt((u / F(a)) ** 2 + ((z - F(top * s)) / F(b)) ** 2) - F(1)) * F(min(a, b))
            d = smax(d, -e, 0.008 * s)
        return d

    def extra(p, c, d):
        u, z = P.around_torso(fr, p, back=back)
        if ridge is not None:
            band = smax(P.shell(c, inner, thick + ridge[1] * s), smax(np.abs(u) - F(ridge[0] * s), outline(p) + F(0.004 * s), 0.002), 0.003)
            d = smin(d, band, 0.003 * s)
        if panel is not None:
            raised = smax(P.shell(c, inner, thick + panel[1] * s), outline(p) + F(panel[0] * s), 0.004 * s)
            d = smin(d, raised, 0.002 * s)
        for line in _seam_lines(u, z, [(a, at * s, lo * s, hi * s) for a, at, lo, hi in seams], 0.0014):
            d = P.groove(d, c, line, inner + thick + (panel[1] * s if panel else 0.0), 0.0015)
        return d

    field = P.plate(core, inner * s, thick * s, outline, rim * s, extra=extra)
    reach = sm.fitter.torso_reach + (inner + thick + 0.03) * s          # (the plate lies within this of the axis)
    cy, ref = fr.j["spine"].y, 0.13 * s
    lo_a, hi_a = (centre - half) * s / ref - 0.15, (centre + half) * s / ref + 0.15
    xs = [reach * np.sin(np.clip(a, -np.pi / 2, np.pi / 2)) for a in (lo_a, hi_a)]
    ys = cy + min(0.0, reach * np.cos(min(np.pi, max(abs(lo_a), abs(hi_a))))) - 0.02 * s, cy + reach
    if back:
        ys = 2 * cy - ys[1], 2 * cy - ys[0]
    bounds = ((xs[0], ys[0], bottom * s - 0.03 * s), (xs[1], ys[1], top * s + 0.03 * s))
    objs = sm.make(name, core, field, bounds, color, slot, mirror=mirror, stiff=0.7, key=(sm.torso_key,),
                   shade=_edge_shade(sm, outline, color, edge))
    for pts in lines:              # light strips: [(u, z), ...] in frame units
        us, zs = zip(*pts)
        q = P.torso_points(fr, core, np.asarray(us) * s, np.asarray(zs) * s, (inner + thick) * s, back=back)
        sm.line(objs[0], q)
        if mirror:
            sm.line(objs[1], q * np.asarray((-1, 1, 1), dtype=F))
    return objs


def cap(sm, name, centre, radius, axis, top, inner=0.004, thick=0.005, seams=(), lines=(), color=None, slot="char:metal",
        flare=0.0, stiff=0.6, rim=0.0025, rings=(), edge=None):
    """A domed cap over a ball (``centre``, ``radius``) covering directions within ``top`` radians of ``axis``;
    ``flare`` lifts its lower edge away from the body; seams and ``rings`` (raised) at angles from the axis; light
    strips as [(angle from the axis, angle round it), ...]; mirrored onto the left side."""
    s = sm.s
    axis_n = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
    radius = sm.fitter.fit_ball(centre, axis_n, top, guess=radius) or radius        # just round the clothes under it
    base = P.ball(centre, radius)
    cut, angle = _cap_angle(centre, axis_n, radius + inner * s)

    def core(p):
        if not flare:
            return base(p)
        t = np.clip(angle(p) / F(top * (radius + inner * s)), 0, 1)
        return base(p) - F(flare * s) * t ** 3

    def outline(p):
        return cut(p) - F(top * (radius + inner * s))

    def extra(p, c, d):
        a = angle(p)
        for at in rings:
            band = smax(P.shell(c, inner * s, (thick + 0.0025) * s), np.abs(a - F(at * (radius + inner * s))) - F(0.004 * s), 0.002)
            d = smin(d, smax(band, outline(p) + F(0.002), 0.002), 0.002 * s)
        for at in seams:
            d = P.groove(d, c, np.abs(a - F(at * (radius + inner * s))) - F(0.0014), (inner + thick) * s, 0.0015)
        return d

    field = P.plate(core, inner * s, thick * s, outline, rim * s, extra=extra)
    r = radius + (inner + thick + flare + 0.02) * s
    c = np.asarray(centre)
    bounds = (tuple(c - r), tuple(c + r))
    objs = sm.make(name, core, field, bounds, color, slot, stiff=stiff, key=(radius,),
                   shade=_edge_shade(sm, outline, color, edge))
    for pts in lines:
        dirs = [_dir(axis_n, a, b) for a, b in pts]
        q = P.onto(core, np.asarray([c] * len(dirs)), np.asarray(dirs), (inner + thick) * s)
        sm.line(objs[0], q)
        sm.line(objs[1], q * np.asarray((-1, 1, 1), dtype=F))
    return objs


def _cap_angle(centre, axis, r):
    c, axis = np.asarray(centre, dtype=F), np.asarray(axis, dtype=F)

    def angle(p):
        q = p - c
        cos = (q @ axis) / np.maximum(np.linalg.norm(q, axis=1), F(1e-6))
        return np.arccos(np.clip(cos, -1, 1)) * F(r)
    return angle, angle


def _dir(axis, a, b):
    """The direction ``a`` radians from ``axis``, turned ``b`` round it (0 toward the world's up, or forward)."""
    ref = np.asarray((0, 0, 1.0)) if abs(axis[2]) < 0.9 else np.asarray((0, 1.0, 0))
    e1 = ref - axis * ref.dot(axis)
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(axis, e1)
    return axis * math.cos(a) + (e1 * math.cos(b) + e2 * math.sin(b)) * math.sin(a)


def limb(sm, name, seg, v0, v1, half, front, inner=0.004, thick=0.005, corner=0.012, seams=(), lines=(), ridge=None,
         color=None, slot="char:metal", stiff=0.6, rim=0.0025, taper=0.0, keep=None, edge=None):
    """A plate on a limb segment ``seg`` = (a, b, ra, rb) from ``v0`` to ``v1`` along it (fractions), ``half`` its
    half-width round the limb (frame units) centred on the ``front`` direction; ``taper`` narrows it toward v1;
    seams ("u"/"v", at, lo, hi) with v in fractions; light strips as [(u, v), ...]; mirrored onto the left side."""
    s = sm.s
    a, b, ra, rb = seg
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    length = float(np.linalg.norm(b - a))
    ref = 0.045 * s
    fit = sm.fitter.fit_limb(a, b, front, half * s / ref + 0.3, max(0.0, v0 - 0.05), min(1.0, v1 + 0.05), guess=(ra, rb))
    if fit is not None:                    # just round the clothes under it
        ra, rb = max(fit[0], 0.02 * s), max(fit[1], 0.015 * s)
    core = P.limb(a, b, ra, rb)

    def outline(p):
        u, v = P.along(p, a, b, front, ref)
        t = np.clip(v / F(length), 0, 1)
        w = F(half * s) * (F(1) - F(taper) * t)
        return P.box(u / w * F(half * s), v, 0.0, (v0 + v1) / 2 * length, half * s, (v1 - v0) / 2 * length, corner * s)

    def extra(p, c, d):
        u, v = P.along(p, a, b, front, ref)
        if ridge is not None:
            band = smax(P.shell(c, inner * s, (thick + ridge[1]) * s), smax(np.abs(u) - F(ridge[0] * s), outline(p) + F(0.004 * s), 0.002), 0.003)
            d = smin(d, band, 0.003 * s)
        conv = [(k, at * (length if k == "v" else s), lo * (s if k == "v" else length), hi * (s if k == "v" else length))
                for k, at, lo, hi in seams]
        for line in _seam_lines(u, v, conv, 0.0014):
            d = P.groove(d, c, line, (inner + thick) * s, 0.0015)
        return d

    field = P.plate(core, inner * s, thick * s, outline, rim * s, extra=extra)
    r = max(ra, rb) + (inner + thick + 0.02) * s
    lo, hi = np.minimum(a, b) - r, np.maximum(a, b) + r
    objs = sm.make(name, core, field, (tuple(lo), tuple(hi)), color, slot, stiff=stiff, keep=keep, key=(ra, rb),
                   shade=_edge_shade(sm, outline, color, edge))
    for pts in lines:
        us, vs = zip(*pts)
        q = P.limb_points(core, a, b, np.asarray(us) * s, np.asarray(vs) * length, (inner + thick) * s, front, ref)
        sm.line(objs[0], q)
        sm.line(objs[1], q * np.asarray((-1, 1, 1), dtype=F))
    return objs
