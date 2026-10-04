"""Linen wraps (``wraps``: the linen's colour): a mummy bound head to toe in overlapping strips of aged cloth.

Strips are wound from the feet and hands upward (wrapstrip.py): each leg from the ankle to the hip and each arm from
the wrist to the shoulder in a helix whose turns overlap, so every edge steps down over the turn below; the feet
heel to toe; the trunk from the hips to the armpits; the neck; the head in bands round the skull from the jaw to the
crown with a dark slit left open over the eyes (which glow); straps cross over each shoulder, a strip runs between
the legs and two bands cross over the crown; the hands are bound in a tight layer with the strips' lines. The turns
waver, so here and there they part and the dark, shrunken body shows between them; a few loose ends hang free. Each
strip is its own shade of old linen, stained and grimier toward the feet.
"""
import math

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import wrapstrip as ws
from .coverings import Skin, arrays, make_mesh, noise, smooth_normals, ss, submesh
from .hands import hand_axes
from .paint import flat, hex3, paint

DARK = np.asarray((0.16, 0.12, 0.09))          # the dried body between the strips


class Binder:
    def __init__(self, fr, head, parts, p):
        self.fr, self.head, self.parts, self.s = fr, head, parts, fr.s
        self.linen = np.asarray(hex3(p["wraps"]))
        self.rng = np.random.default_rng(1913)
        dg = bpy.context.evaluated_depsgraph_get()
        self.body = BVHTree.FromObject(parts["body"], dg)
        self.skull = BVHTree.FromObject(parts["head"], dg)
        self.strips = []          # (arrays, tone)

    def add(self, arr, tone=None, clear=False):
        if arr is not None:
            t = self.rng.uniform(0.84, 1.05)
            grey = self.rng.uniform(0.0, 0.35)
            base = self.linen * t
            base = base * (1 - grey) + np.full(3, base.mean() * 0.95) * grey
            self.strips.append((arr, base if tone is None else tone, clear))

    # ------------------------------------------------------------------ the windings
    def limbs(self):
        j, s = self.fr.j, self.s
        th, off = 0.0032 * s, 0.0035 * s
        for side, sx in (("R", 1), ("L", -1)):
            hip, knee = j[f"hip.{side}"] + Vector((sx * 0.012 * s, 0, 0)), j[f"knee.{side}"]
            # (rays that find no surface near, passing up the inside of a limb or across the pelvis, leave gaps)
            an = j[f"ankle.{side}"]
            leg = ws.Surface(self.body, [hip + (hip - knee).normalized() * 0.04 * s, knee, an], reach=0.13 * s)
            L = leg.length
            self.add(ws.helix(leg, 0.95, 0.5, 0.06 * s / L, 0.044 * s / L, off, th, phase=1.0 + sx, spin=sx, skip=leg.missed))
            self.add(ws.helix(leg, 0.56, 0.0, 0.066 * s / L, 0.05 * s / L, off + 0.6 * th, th, phase=2.3 + sx, spin=-sx,
                              skip=leg.missed))
            ankle = ws.Surface(self.body, [an.lerp(knee, 0.25), an - Vector((0, 0, 0.025 * s))], reach=0.085 * s)
            L = ankle.length
            self.add(ws.helix(ankle, 1.0, 0.45, 0.05 * s / L, 0.036 * s / L, off * 0.7, th, phase=0.6 * sx, spin=-sx,
                              skip=ankle.missed))
            sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
            arm = ws.Surface(self.body, [sh + (sh - el).normalized() * 0.035 * s, el, wr + (wr - el) * 0.05], reach=0.12 * s)
            L = arm.length
            self.add(ws.helix(arm, 0.97, 0.45, 0.045 * s / L, 0.033 * s / L, off * 0.8, th * 0.9, phase=0.4 * sx, spin=sx,
                              skip=arm.missed))
            self.add(ws.helix(arm, 0.5, 0.0, 0.05 * s / L, 0.037 * s / L, off * 0.8 + 0.6 * th, th * 0.9, phase=1.7 * sx, spin=-sx,
                              skip=arm.missed))
            heel, ball, toe = j[f"heel.{side}"], j[f"ball.{side}"], j[f"toe.{side}"]
            foot = ws.Surface(self.body, [heel + Vector((0, 0.035 * s, 0.032 * s)), ball + Vector((0, 0, 0.028 * s)),
                                          toe + Vector((0, -0.015 * s, 0.022 * s))], up=(0, 0, 1), reach=0.08 * s)
            L = foot.length
            self.add(ws.helix(foot, 0.98, 0.0, 0.05 * s / L, 0.036 * s / L, off * 0.8, th, phase=0.0, spin=sx, skip=foot.missed))
            fwd = Vector((toe.x - heel.x, toe.y - heel.y, 0)).normalized()
            out = Vector((fwd.y, -fwd.x, 0))
            for k, x in enumerate((-0.022, 0.022)):       # two strips over the toes, from the instep to under the tip
                o = out * x * s
                cap = [ball + o + Vector((0, 0, 0.06 * s)) - fwd * 0.02 * s, toe + o + Vector((0, 0, 0.035 * s)),
                       toe + o + fwd * 0.03 * s + Vector((0, 0, 0.012 * s)), toe + o + Vector((0, 0, -0.01 * s))]
                self.add(ws.ribbon(self.body, cap, 0.05 * s, off + (1.6 + 0.4 * k) * th, th))
                back = [heel + o * 0.8 + Vector((0, 0, 0.09 * s)) + fwd * 0.01 * s, heel + o * 0.8 - fwd * 0.035 * s + Vector((0, 0, 0.035 * s)),
                        heel + o * 0.8 - fwd * 0.02 * s + Vector((0, 0, -0.008 * s)), heel + o * 0.8 + fwd * 0.03 * s + Vector((0, 0, -0.012 * s))]
                self.add(ws.ribbon(self.body, back, 0.045 * s, off + (1.6 + 0.4 * k) * th, th))

    def trunk(self):
        j, s = self.fr.j, self.s
        th, off = 0.0034 * s, 0.0045 * s
        bottom = Vector((0, j["pelvis"].y, j["hip.R"].z - 0.035 * s))
        top = Vector((0, j["chest"].y - 0.01 * s, j["shoulder.R"].z - 0.085 * s))
        trunk = ws.Surface(self.body, [bottom, (bottom + top) / 2 + Vector((0, 0.01 * s, 0)), top], up=(0, 1, 0), reach=0.22 * s)
        L = trunk.length
        self.add(ws.helix(trunk, 0.0, 1.0, 0.07 * s / L, 0.052 * s / L, off, th, phase=0.3, segs=64, skip=trunk.missed))
        self.add(ws.helix(trunk, 0.0, 0.32, 0.09 * s / L, 0.07 * s / L, off + 1.2 * th, th, phase=2.0, segs=64, spin=-1))
        # each shoulder bound in turns round the collarbone's line, out over the deltoid and under the armpit; straps
        # crossing over it (front to back), and a strip between the legs
        for side, sx in (("R", 1), ("L", -1)):
            sh = j[f"shoulder.{side}"]
            inner = Vector((sx * 0.06 * s, sh.y, sh.z - 0.01 * s))
            yoke = ws.Surface(self.body, [inner, sh + (sh - inner).normalized() * 0.03 * s], up=(0, 0, 1), reach=0.13 * s)
            L = yoke.length
            self.add(ws.helix(yoke, 0.0, 1.0, 0.05 * s / L, 0.036 * s / L, off + 1.0 * th, th, phase=0.8, segs=48, spin=sx,
                              skip=yoke.missed))
        z = j["shoulder.R"].z - 0.045 * s         # a band across the top of the chest and one across the shoulder blades
        for a0 in (0.0, math.pi):
            band = []
            for a in np.linspace(a0 - 1.1, a0 + 1.1, 11):
                hit = self.body.ray_cast(Vector((0, j["chest"].y, z)), Vector((math.sin(a), math.cos(a), 0)), 0.3 * s)
                if hit[0] is not None:
                    band.append(hit[0] + hit[1] * 0.004 * s)
            if len(band) > 4:
                self.add(ws.ribbon(self.body, band, 0.055 * s, off + 1.6 * th, th))
        for sx in (1, -1):
            for k, x in enumerate((0.055, 0.1, 0.145)):
                front = Vector((sx * (x - 0.02) * s, 0.25 * s, j["shoulder.R"].z - (0.12 - 0.02 * k) * s))
                over = Vector((sx * x * s, j["neck"].y, j["shoulder.R"].z + 0.03 * s))
                back = Vector((sx * (x - 0.01) * s, -0.25 * s, j["shoulder.R"].z - (0.13 - 0.02 * k) * s))
                self.add(ws.ribbon(self.body, [front, (front + over) / 2 + Vector((0, 0, 0.02 * s)), over,
                                               (over + back) / 2 + Vector((0, 0, 0.02 * s)), back],
                                   0.05 * s, off + (2.0 + 0.6 * k) * th, th))
        c = Vector((0, j["pelvis"].y, j["hip.R"].z - 0.02 * s))
        crotch = []
        for a in np.linspace(math.radians(55), math.radians(-235), 13):     # front, under, up the back
            d = Vector((0, math.cos(a), math.sin(a)))
            hit = self.body.ray_cast(c, d, 0.5 * s)
            if hit[0] is not None:
                crotch.append(hit[0] + hit[1] * 0.004 * s)
        if len(crotch) > 4:
            self.add(ws.ribbon(self.body, crotch, 0.065 * s, off + 2.2 * th, th))

    def head_wrap(self):
        head, s = self.head, self.s
        h = head.h
        th, off = 0.0028 * s, 0.003 * s
        sphere = ws.Surface(self.skull, centre=head.c, t_range=(-1.2, 1.45), reach=0.3 * s)
        eye_el = math.atan2(head.local(head.eyes[1]).z, head.local(head.eyes[1]).y)

        def slit(t, ph):
            ph = np.mod(ph + math.pi, 2 * math.pi) - math.pi
            return (np.abs(t - eye_el - 0.01) < 0.085) & (np.abs(ph) < 0.75) & (np.abs(ph) > 0.2)

        self.add(ws.helix(sphere, -0.95, 1.2, 0.3, 0.215, off, th, phase=0.5, segs=56, skip=slit), clear=True)
        neck_top = head.at((0, -0.008, -0.075))
        neck = ws.Surface([self.skull, self.body], [head.at((0, -0.03, -0.27)), head.at((0, -0.02, -0.18)), neck_top],
                          up=(0, 1, 0), reach=0.12 * s)
        self.add(ws.helix(neck, 0.15, 1.0, 0.045 * s / neck.length, 0.034 * s / neck.length, off, th, phase=1.1, segs=48))
        for k, curve in enumerate((((0, 0.075, 0.06), (0, 0.04, 0.105), (0, -0.02, 0.125), (0, -0.08, 0.1), (0, -0.11, 0.04)),
                                   ((0.085, 0.0, 0.03), (0.06, -0.005, 0.1), (0, -0.01, 0.13), (-0.06, -0.005, 0.1), (-0.085, 0.0, 0.03)))):
            self.add(ws.ribbon(self.skull, [head.at(c) for c in curve], 0.042 * s, off + (1.6 + 0.5 * k) * th, th), clear=True)

    def loose_ends(self):
        j, s = self.fr.j, self.s
        th = 0.0025 * s
        ends = [(j["elbow.L"].lerp(j["wrist.L"], 0.55), (0.0, -0.3, -1.0), 0.28 * s, 0.04 * s),
                (j["hip.R"] + Vector((0.07 * s, 0.02 * s, -0.08 * s)), (0.3, 0.2, -1.0), 0.22 * s, 0.05 * s),
                (self.head.at((0.03, -0.09, -0.02)), (0.2, -0.6, -1.0), 0.24 * s, 0.035 * s),
                (Vector((-0.1 * s, -0.08 * s, j["hip.R"].z + 0.08 * s)), (-0.4, -0.3, -1.0), 0.2 * s, 0.05 * s)]
        for i, (at, d, length, width) in enumerate(ends):
            tree = self.skull if i == 2 else self.body
            loc, nrm, _, _ = tree.find_nearest(at)
            d = Vector(d).normalized()
            tang = (d - nrm * d.dot(nrm)).normalized()
            start = loc + nrm * 0.008 * s
            self.add(ws.hanging(start, tang, nrm, width, length, th, seed=31 + i))

    def _clear(self, V, S, fixed):
        """Lifts any vertex of the head's strips sunk into the face (where a strip spans the nose or the hollow under
        the brow) back out to its height over its own surface point."""
        h0 = np.linalg.norm(V - S, axis=1)
        for i in np.flatnonzero(fixed):
            v = Vector(V[i])
            loc, nrm, _, _ = self.skull.find_nearest(v)
            if loc is not None and (v - loc).dot(nrm) < 0.6 * h0[i]:
                V[i] = np.asarray(loc + nrm * max(h0[i], 0.002 * self.s))

    # ------------------------------------------------------------------ one mesh
    def mesh(self, skin):
        V, T, C, S = [], [], [], []
        s = self.s
        base = 0
        F = []
        for k, ((verts, tris, across, along, surf), tone, clear) in enumerate(self.strips):
            edge = 1.0 - 0.28 * ss(0.12, 0.0, across) - 0.18 * ss(0.75, 1.0, across)
            stain = ss(0.58, 0.78, noise(surf, 0.07 * s, 101 + k, 2))
            grime = ss(0.55 * s, 0.05 * s, surf[:, 2]) * 0.6 + 0.4 * ss(0.62, 0.8, noise(surf, 0.2 * s, 7))
            weave = 0.95 + 0.05 * np.sin(along / (0.011 * s) * 2 * math.pi + k)
            col = tone[None, :] * (edge * weave)[:, None]
            col = col * (1 - stain[:, None] * 0.45) + col * np.asarray((0.72, 0.6, 0.42))[None, :] * stain[:, None] * 0.45
            col = col * (1 - grime[:, None] * 0.35) + col * np.asarray((0.62, 0.52, 0.4))[None, :] * grime[:, None] * 0.35
            V.append(verts)
            T.append(tris + base)
            C.append(col)
            S.append(surf)
            F.append(np.full(len(verts), clear))
            base += len(verts)
        V, T, C, S = np.concatenate(V), np.concatenate(T), np.concatenate(C), np.concatenate(S)
        self._clear(V, S, np.concatenate(F))
        return make_mesh("wraps", V, T, C, "char:cloth", skin.weights(S))


def _bind_hand(obj, fr, side, linen, s):
    """A tight layer of linen over a hand, ridged and lined like the strips round the fingers and palm."""
    from .rig import read_weights
    V, T, N = arrays(obj)
    W = read_weights(obj)
    along, thumb, dorsal = hand_axes(fr, side)
    w = np.asarray(fr.j[f"wrist.{side}"])
    q = V - w
    a = q @ np.asarray(along)
    ph = np.arctan2(q @ np.asarray(dorsal), q @ np.asarray(thumb))
    u = a / (0.016 * s) + ph / (2 * math.pi) * 0.7
    f = u - np.floor(u)
    Ns = smooth_normals(V, T, N, 2)
    h = 0.0018 * s + 0.0012 * s * ss(0.0, 0.08, f) * (1.0 - ss(0.5, 1.0, f))
    col = linen[None, :] * (1.0 - 0.3 * ss(0.12, 0.0, f) - 0.15 * ss(0.8, 1.0, f))[:, None]
    col *= (0.82 + 0.1 * noise(V, 0.03 * s, 5))[:, None]
    return make_mesh(f"wraps_hand_{side}", V + Ns * h[:, None], T, col, "char:cloth", W)


def grow(fr, head, parts, p, worn):
    s = fr.s
    skin = Skin(parts)
    b = Binder(fr, head, parts, p)
    b.limbs()
    b.trunk()
    b.head_wrap()
    b.loose_ends()
    out = [b.mesh(skin)]
    for side, hand in zip("RL", parts["hands"]):
        out.append(_bind_hand(hand, fr, side, b.linen * 0.92, s))
    dark = "#%02x%02x%02x" % tuple(int(c * 255) for c in DARK)
    for o in [parts["body"], *parts["hands"]]:
        flat(o, dark)
    under, eyes = b.linen * 0.45, [np.asarray(e) for e in head.eyes.values()]

    def face(co):       # under-layers of linen on the face, dark round the eyes
        k = ss(0.024 * s, 0.042 * s, min(float(np.linalg.norm(np.asarray(co) - e)) for e in eyes))
        return tuple(DARK * (1 - k) + under * k)
    paint(parts["head"], face)
    from .garments import set_slot
    for o in parts["eyes"]:
        flat(o, p.get("eyes", "#ff6a2a"))
        set_slot(o, "char:glow")
    return out
