"""Linen wraps (``wraps``: the linen's colour): a mummy bound head to toe in overlapping strips of aged cloth.

Strips are wound from the hands and feet upward (wrapstrip.py): each leg from the ankle to the hip and each arm from
the wrist to the shoulder in a helix whose turns overlap, so every edge steps down over the turn below; the trunk
from the hips to the armpits over long straps laid first over each shoulder (their ends tucked under the trunk's
turns); the neck; the head in bands round the skull from the jaw up to a swirl on the crown with a dark slit left
open over the eyes (which glow), two straps crossing over the top; a strip between the legs. The hands and feet are
bound in a tight layer with the strips' lines. The turns waver, so here and there they part and the darker, older
layers show between them; a few loose ends lie down the body and hang off it (drapes.py), moving with the limb they
lie on. Each strip is its own shade of old linen, stained and grimier toward the feet.
"""
import math

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import drapes
from . import wrapstrip as ws
from .coverings import Skin, arrays, make_mesh, noise, smooth_normals, ss, submesh
from .hands import hand_axes
from .paint import flat, hex3, paint

DARK = np.asarray((0.16, 0.12, 0.09))          # the dried flesh round the eyes
UNDER = 0.5                                    # the older layers under the strips (times the linen)


class Binder:
    def __init__(self, fr, head, parts, p):
        self.fr, self.head, self.parts, self.s = fr, head, parts, fr.s
        self.linen = np.asarray(hex3(p["wraps"]))
        self.rng = np.random.default_rng(1913)
        dg = bpy.context.evaluated_depsgraph_get()
        self.body = BVHTree.FromObject(parts["body"], dg)
        self.skull = BVHTree.FromObject(parts["head"], dg)
        self.strips = []          # (arrays, tone, clear)

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
            self.add(ws.helix(leg, 0.97, 0.5, 0.06 * s / L, 0.044 * s / L, off, th, phase=1.0 + sx, spin=sx, skip=leg.missed))
            self.add(ws.helix(leg, 0.56, 0.0, 0.066 * s / L, 0.05 * s / L, off + 0.6 * th, th, phase=2.3 + sx, spin=-sx,
                              skip=leg.missed))
            sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
            arm = ws.Surface(self.body, [sh + (sh - el).normalized() * 0.035 * s, el, wr + (wr - el) * 0.05], reach=0.12 * s)
            L = arm.length
            self.add(ws.helix(arm, 0.97, 0.45, 0.045 * s / L, 0.033 * s / L, off * 0.8, th * 0.9, phase=0.4 * sx, spin=sx,
                              skip=arm.missed))
            self.add(ws.helix(arm, 0.5, 0.0, 0.05 * s / L, 0.037 * s / L, off * 0.8 + 0.6 * th, th * 0.9, phase=1.7 * sx, spin=-sx,
                              skip=arm.missed))

    def _arc(self, centre, a0, a1, n=15):
        """Points on the body where rays from ``centre`` in the plane x = const (angles from +y toward +z) leave it."""
        out = []
        for a in np.linspace(a0, a1, n):
            hit = self.body.ray_cast(centre, Vector((0, math.cos(a), math.sin(a))), 0.5 * self.s)
            if hit[0] is not None:
                out.append(hit[0] + hit[1] * 0.003 * self.s)
        return out

    def trunk(self):
        j, s = self.fr.j, self.s
        th = 0.0034 * s
        # long straps over each shoulder first, from low on the chest to low on the back, overlapping side by side
        for sx in (1, -1):
            for k, x in enumerate((0.05, 0.095, 0.14, 0.18)):
                c = Vector((sx * x * s, j["chest"].y, j["shoulder.R"].z - (0.12 + 0.03 * (k == 3)) * s))
                pts = self._arc(c, math.radians(-50), math.radians(230))
                if len(pts) > 5:
                    self.add(ws.ribbon(self.body, pts, 0.052 * s, 0.0025 * s + 0.15 * k * th, th))
        # the trunk's turns over the straps' ends, from the hips to the armpits
        off = 0.0075 * s
        bottom = Vector((0, j["pelvis"].y, j["hip.R"].z - 0.035 * s))
        top = Vector((0, j["chest"].y - 0.01 * s, j["shoulder.R"].z - 0.08 * s))
        trunk = ws.Surface(self.body, [bottom, (bottom + top) / 2 + Vector((0, 0.01 * s, 0)), top], up=(0, 1, 0), reach=0.22 * s)
        L = trunk.length
        self.add(ws.helix(trunk, 0.0, 1.0, 0.07 * s / L, 0.052 * s / L, off, th, phase=0.3, segs=64, skip=trunk.missed))
        self.add(ws.helix(trunk, 0.0, 0.32, 0.09 * s / L, 0.07 * s / L, off + 1.2 * th, th, phase=2.0, segs=64, spin=-1))
        # turns round the base of the neck over the straps' tops, closing the chest and back between them
        base = ws.Surface(self.body, [Vector((0, j["chest"].y, j["shoulder.R"].z - 0.09 * s)),
                                      Vector((0, j["neck"].y, j["shoulder.R"].z + 0.02 * s))], up=(0, 1, 0), reach=0.12 * s)
        L = base.length
        self.add(ws.helix(base, 0.0, 1.0, 0.055 * s / L, 0.04 * s / L, off + 0.6 * th, th, phase=1.3, segs=56, skip=base.missed))
        crotch = self._arc(Vector((0, j["pelvis"].y, j["hip.R"].z - 0.02 * s)), math.radians(55), math.radians(-235), 13)
        if len(crotch) > 4:                              # a strip between the legs, front to back
            self.add(ws.ribbon(self.body, crotch, 0.065 * s, off + 2.2 * th, th, tuck=0.04 * s))

    def head_wrap(self):
        head, s = self.head, self.s
        th, off = 0.0028 * s, 0.003 * s
        sphere = ws.Surface(self.skull, centre=head.c, t_range=(-1.2, 1.57), reach=0.3 * s, bridge=6)
        eye_el = math.atan2(head.local(head.eyes[1]).z, head.local(head.eyes[1]).y)

        def slit(ph):       # a narrow, uneven gap between two turns across the eyes, pinched over the nose
            ph = np.mod(ph + math.pi, 2 * math.pi) - math.pi
            mid = eye_el + 0.004 + 0.012 * np.sin(2.3 * ph + 0.7)
            front = ss(0.85, 0.5, np.abs(ph))
            half = 0.058 * front * (1.0 - 0.55 * ss(0.16, 0.02, np.abs(ph)))
            return mid, half * (0.8 + 0.2 * np.sin(5.1 * ph + 0.4)), front

        # bands from the jaw up, closing in a swirl on the crown
        self.add(ws.helix(sphere, -0.95, 1.42, 0.3, 0.2, off, th, phase=0.5, segs=56, part=slit), clear=True)
        neck_top = head.at((0, -0.008, -0.075))
        neck = ws.Surface([self.skull, self.body], [head.at((0, -0.03, -0.27)), head.at((0, -0.02, -0.18)), neck_top],
                          up=(0, 1, 0), reach=0.12 * s)
        neck.R -= (ss(0.7, 0.95, neck.ts) * (off + 1.3 * th))[:, None]     # its top tucked under the head's lowest turns
        self.add(ws.helix(neck, 0.15, 1.0, 0.045 * s / neck.length, 0.034 * s / neck.length, off, th, phase=1.1, segs=48))
        for k, curve in enumerate((((0, 0.075, 0.06), (0, 0.04, 0.105), (0, -0.02, 0.125), (0, -0.08, 0.1), (0, -0.11, 0.04)),
                                   ((0.085, 0.0, 0.03), (0.06, -0.005, 0.1), (0, -0.01, 0.13), (-0.06, -0.005, 0.1), (-0.085, 0.0, 0.03)))):
            self.add(ws.ribbon(self.skull, [head.at(c) for c in curve], 0.04 * s, off + (1.05 + 0.35 * k) * th, th * 0.8,
                               tuck=0.035 * s), clear=True)

    def loose_ends(self):
        """A few ends come loose: they lie down the body over the wraps and hang off where it turns away."""
        j, s = self.fr.j, self.s
        verts, polys, base = [], [], 0
        for o in (self.parts["body"], self.parts["head"]):
            V, T, _ = arrays(o)
            verts += [Vector(v) for v in V]
            polys += (T + base).tolist()
            base += len(V)
        tree = BVHTree.FromPolygons(verts, polys)
        ends = [(j["hip.R"] + Vector((0.075 * s, 0.03 * s, -0.06 * s)), 0.22 * s, 0.048 * s, 0.009),     # over a leg's turns
                (Vector((-0.11 * s, -0.07 * s, j["hip.R"].z + 0.06 * s)), 0.19 * s, 0.048 * s, 0.0125)]    # the trunk's
        for i, (at, length, width, gap) in enumerate(ends):
            loc, nrm, _, _ = tree.find_nearest(at)
            rng = np.random.default_rng(32 + i)
            pts, nrms, skin = drapes.trace(tree, np.asarray(loc), np.asarray(nrm), length, 12, gap * s, rng, sway=0.08)
            self.add(drapes.ribbon(pts, nrms, skin, width, 0.0025 * s, rng, taper=0.15, ragged=0.35))

    def _clear(self, V, S, fixed):
        """Lifts any vertex of the head's strips sunk into the face (where a strip spans the nose or the hollow under
        the brow) back out to its height over its own surface point; not the lips of the eye slit, which lie over the
        lids' edges (lifting them off those would draw spikes)."""
        h0 = np.linalg.norm(V - S, axis=1)
        eyes = [np.asarray(e) for e in self.head.eyes.values()]
        near_eye = np.min([np.linalg.norm(V - e, axis=1) for e in eyes], axis=0) < 0.03 * self.s
        for i in np.flatnonzero(fixed & ~near_eye):
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


def _bound(name, V, T, N, W, u, linen, s, lift=0.0018, ridge=0.0012):
    """A tight layer of linen over a skin part (V, T, N; weights W), ridged and lined like strips wound along ``u``
    (turns per vertex: each whole number a strip's edge)."""
    f = u - np.floor(u)
    Ns = smooth_normals(V, T, N, 2)
    h = lift * s + ridge * s * ss(0.0, 0.08, f) * (1.0 - ss(0.5, 1.0, f))
    col = linen[None, :] * (1.0 - 0.3 * ss(0.12, 0.0, f) - 0.15 * ss(0.8, 1.0, f))[:, None]
    col *= (0.82 + 0.1 * noise(V, 0.03 * s, 5))[:, None]
    return make_mesh(name, V + Ns * h[:, None], T, col, "char:cloth", W)


def _bind_hand(obj, fr, side, linen, s):
    """The hand bound in strips round the fingers and palm."""
    from .rig import read_weights
    V, T, N = arrays(obj)
    along, thumb, dorsal = hand_axes(fr, side)
    q = V - np.asarray(fr.j[f"wrist.{side}"])
    ph = np.arctan2(q @ np.asarray(dorsal), q @ np.asarray(thumb))
    return _bound(f"wraps_hand_{side}", V, T, N, read_weights(obj), q @ np.asarray(along) / (0.016 * s) + ph / (2 * math.pi) * 0.7,
                  linen, s)


def _bind_feet(body, fr, linen, s):
    """Both feet bound in turns round the foot from the toes back over the heel and up to the ankle (a tight layer
    over the body's feet, its top edge under the legs' turns)."""
    from .rig import read_weights
    j = fr.j
    V, T, N = arrays(body)
    keep = V[:, 2] < j["ankle.R"].z + 0.03 * s
    Vs, Ts, idx = submesh(V, T, keep)
    W = read_weights(body)
    u = np.zeros(len(Vs))
    for side, sx in (("R", 1), ("L", -1)):
        heel, toe = np.asarray(j[f"heel.{side}"]), np.asarray(j[f"toe.{side}"])
        fwd = (toe - heel) * np.asarray((1.0, 1.0, 0.0))
        fwd /= np.linalg.norm(fwd)
        mine = np.sign(Vs[:, 0]) == sx
        q = Vs[mine] - heel
        a = q @ fwd
        out = np.asarray((fwd[1], -fwd[0], 0.0)) * sx
        ph = np.arctan2(q[:, 2] - 0.03 * s, q @ out)
        u[mine] = (a + 0.6 * np.maximum(q[:, 2] - 0.05 * s, 0.0)) / (0.022 * s) + ph / (2 * math.pi) * 0.8
    grime = linen * 0.8
    return _bound("wraps_feet", Vs, Ts, N[idx], [W[i] for i in idx], u, grime, s, lift=0.003, ridge=0.0022)


def grow(fr, head, parts, p, worn):
    s = fr.s
    skin = Skin(parts)
    b = Binder(fr, head, parts, p)
    b.limbs()
    b.trunk()
    b.head_wrap()
    b.loose_ends()
    out = [b.mesh(skin), _bind_feet(parts["body"], fr, b.linen, s)]
    for side, hand in zip("RL", parts["hands"]):
        out.append(_bind_hand(hand, fr, side, b.linen * 0.92, s))
    under = b.linen * UNDER
    for o in [parts["body"], *parts["hands"]]:
        flat(o, "#%02x%02x%02x" % tuple(int(c * 255) for c in under))
    eyes = [np.asarray(e) for e in head.eyes.values()]

    def face(co):       # the older layers on the face, dark round the eyes
        k = ss(0.024 * s, 0.042 * s, min(float(np.linalg.norm(np.asarray(co) - e)) for e in eyes))
        return tuple(DARK * (1 - k) + under * k)
    paint(parts["head"], face)
    from .garments import set_slot
    for o in parts["eyes"]:
        flat(o, p.get("eyes", "#ff6a2a"))
        set_slot(o, "char:glow")
    return out
