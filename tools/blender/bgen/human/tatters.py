"""Tatters: the rotted clothing of the undead. Each garment gets a few large ragged holes, placed on the body and the
same every time for a character, and torn hems: sleeve ends, the shirt's loose tail and trouser legs ripped off above
the boots (and the bottoms of skirts) hang in uneven pointed tongues.

The cloth is cut along the tear's outline rather than along its faces (``cut``), so an edge is jagged only where the
tear is; the cloth darkens with grime toward a tear and lifts a little at the torn edge, and finishing turns every
torn edge under (``lip``) so it shows the cloth's thickness. The skin stays under the holes (``mask``).
"""
import math
import zlib

import bmesh
import numpy as np
from mathutils import Vector

from .tailoring import Limb, ss

GRIME = np.array((0.74, 0.68, 0.58))      # the colour a torn edge is darkened toward (as a multiplier)


def tri(x):
    """A triangle wave: 0 at whole numbers, 1 half way between."""
    return 1.0 - np.abs(2.0 * (x - np.floor(x)) - 1.0)


class Hem:
    """A torn edge round a garment opening: uneven tongues hanging between notches torn up into the cloth."""

    def __init__(self, rng, count, depth):
        self.n, self.depth = count, depth
        self.lengths = rng.uniform(0.25, 1.0, count)
        self.phase = rng.uniform(0, 2 * math.pi, 4)

    def cut(self, d_end, u, frayed=True):
        """Field (> 0 keeps the cloth) for points ``d_end`` in from the opening, at ``u`` (0..1) round it."""
        p = self.phase
        q = u * self.n + self.n * (0.032 * np.sin(2 * np.pi * u * 2 + p[0]) + 0.014 * np.sin(2 * np.pi * u * 3 + p[1])) + p[2]
        i = np.floor(q).astype(int) % self.n
        tongue = tri(q - 0.5) ** 0.8                                   # 1 at a tongue's tip, 0 in a notch
        cut = self.depth * (1.0 - self.lengths[i] * tongue)
        if frayed:
            cut = cut + self.depth * 0.09 * (tri(u * self.n * 3.3 + p[3]) - 0.5)     # small teeth along it
        return d_end - cut


class Tatters:
    """Where one undead character's clothes are torn (``amount``: 0..1, how badly)."""

    def __init__(self, wardrobe, body, amount):
        self.w, self.f, self.s = wardrobe, wardrobe.f, wardrobe.f.s
        p, j, s = wardrobe.p, self.f.j, self.s
        rng = np.random.RandomState(zlib.crc32(repr(sorted(p.items())).encode()))
        X = np.array([v.co[:] for v in body.data.vertices])
        N = np.array([v.normal[:] for v in body.data.vertices])
        self.X, self.N = X, N
        robe, coat = bool(p.get("robe")), "coat" in p
        self.kinds = {"coat"} if coat else {"shirt"}
        if not robe:
            self.kinds.add("pants")
        arms = {side: self._arm(X, side) for side in ("R", "L")}
        on_arm = arms["R"][0] | arms["L"][0]
        z = X[:, 2]
        self.holes, self.hems = {k: [] for k in self.kinds}, {k: [] for k in self.kinds}
        top = (wardrobe.waist + 0.07 * s) if robe else (wardrobe.waist - 0.07 * s if not wardrobe.tucked else wardrobe.waist)
        top += 0.07 * s
        n = max(1, round(amount * 6))
        for kind in self.kinds & {"shirt", "coat"}:
            torso = ~on_arm & (z > top) & (z < wardrobe.collar_z - 0.09 * s) & (np.abs(X[:, 0]) < 0.8 * j["shoulder.R"].x)
            for facing in (1, -1, 1 if rng.rand() < 0.5 else -1)[:max(2, n // 2 + 1)]:
                self._place(rng, kind, torso & (N[:, 1] * facing > 0.35), (0.032, 0.05))
            for side in ("R", "L") if n > 2 else (("R", "L")[rng.randint(2)],):
                on, t, _ = arms[side]
                self._place(rng, kind, on & (t > 0.2) & (t < wardrobe.sleeve_limit() - 0.16), (0.022, 0.032))
            sleeve = wardrobe.sleeve_limit()
            short = rng.randint(2) if wardrobe.cuff == "long" and rng.rand() < 0.5 and not coat else -1
            for k, side in enumerate(("R", "L")):
                end = rng.uniform(0.58, 0.7) if k == short else sleeve
                self.hems[kind].append(("arm", side, end, Hem(rng, rng.randint(6, 8), 0.05 * s)))
            if kind == "shirt" and not robe and not wardrobe.tucked:
                self.hems[kind].append(("tail", None, wardrobe.waist - 0.07 * s, Hem(rng, rng.randint(12, 15), 0.065 * s)))
        if coat:            # the shirt under a torn coat is torn with it, a little less (its edge shows in the hole)
            self.kinds.add("shirt")
            self.holes["shirt"] = [(c, nn, t1, t2, r * 0.7, *rest) for c, nn, t1, t2, r, *rest in self.holes["coat"]]
            self.hems["shirt"] = []
        if "pants" in self.kinds:
            for side, sx in (("R", 1), ("L", -1)):
                end = wardrobe.boot_top + rng.uniform(0.06, 0.13) * s
                self.hems["pants"].append(("leg", side, end, Hem(rng, rng.randint(6, 8), 0.055 * s)))
            legs = (X[:, 0] * np.sign(X[:, 0]) > 0.035 * s) & (z > wardrobe.boot_top + 0.24 * s) & (z < j["hip.R"].z - 0.07 * s)
            for facing in (1, -1, 1)[:max(2, n // 2 + 1)]:
                self._place(rng, "pants", legs & (N[:, 1] * facing > 0.2), (0.026, 0.04))
        self.skirt_hem = Hem(rng, 15, 0.09 * s)

    def _arm(self, X, side):
        j, s = self.f.j, self.s
        sx = 1 if side == "R" else -1
        limb = Limb(X, j[f"shoulder.{side}"], j[f"wrist.{side}"])
        on = (X[:, 0] * sx > abs(j[f"shoulder.{side}"].x) * 0.7) & (limb.t > 0.05) & (limb.r < 0.085 * self.s)
        return on, limb.t, limb

    def _place(self, rng, kind, mask, radii):
        """A hole of radius in ``radii`` centred on a body vertex where ``mask`` holds, clear of the other holes; now
        and then a smaller one torn beside it."""
        idx = np.flatnonzero(mask)
        if not len(idx):
            return
        s = self.s
        r = rng.uniform(*radii) * s
        for _ in range(40):
            c = self.X[idx[rng.randint(len(idx))]]
            if all(np.linalg.norm(c - h[0]) > (r + h[4]) * 1.6 for k in self.holes for h in self.holes[k]):
                break
        else:
            return
        self._hole(rng, kind, c, r)
        if rng.rand() < 0.5:
            near = idx[np.abs(np.linalg.norm(self.X[idx] - c, axis=1) - r * 2.1) < 0.006 * s]
            if len(near):
                self._hole(rng, kind, self.X[near[rng.randint(len(near))]], r * rng.uniform(0.35, 0.5))

    def _hole(self, rng, kind, c, r):
        n = self.N[np.argmin(np.linalg.norm(self.X - c, axis=1))]
        up = np.array((0.0, 0.0, 1.0)) - n * n[2]
        up = up if np.linalg.norm(up) > 1e-3 else np.cross(n, (1.0, 0.0, 0.0))
        up = up / np.linalg.norm(up)
        a = rng.uniform(-0.7, 0.7)
        t1 = up * math.cos(a) + np.cross(n, up) * math.sin(a)          # rips run mostly downward
        teeth = max(8, round(2 * math.pi * r / (0.017 * self.s)))
        self.holes[kind].append((c, n, t1, np.cross(n, t1), r, rng.uniform(1.15, 1.6), rng.uniform(0, 2 * math.pi, 5), teeth))

    # ------------------------------------------------------------------ the tear field (> 0 cloth, < 0 torn away)
    def field(self, X, kind, frayed=True):
        """The tear field at rest positions ``X``; without ``frayed`` the holes' small teeth are left out (for the
        grime and lift along the edge, which would otherwise streak out from every tooth)."""
        X = np.asarray(X, dtype=float).reshape(-1, 3)
        f = np.full(len(X), 1.0)
        if kind not in self.kinds:
            return f
        for c, n, t1, t2, r, e, ph, teeth in self.holes[kind]:
            d = X - c
            a, b, h = d @ t1 / e, d @ t2, d @ n
            th = np.arctan2(b, a)
            R = r * (1 + 0.2 * np.sin(2 * th + ph[0]) + 0.12 * np.sin(3 * th + ph[1]) + 0.07 * np.sin(5 * th + ph[2]))
            if frayed:
                R = R + r * 0.26 * (tri(th * teeth / (2 * np.pi) + ph[3]) - 0.5) * (0.55 + 0.45 * np.sin(3 * th + ph[4]))
            f = np.minimum(f, np.sqrt(a * a + b * b + 2.0 * h * h) - R)
        for where, side, end, hem in self.hems[kind]:
            if where == "tail":
                arms = self._arm(X, "R")[0] | self._arm(X, "L")[0]
                az = np.arctan2(X[:, 0], X[:, 1] - self.f.j["chest"].y)
                f = np.where(arms, f, np.minimum(f, hem.cut(X[:, 2] - end, az / (2 * np.pi) + 0.5, frayed)))
            elif where == "arm":
                on, t, limb = self._arm(X, side)
                f = np.where(on, np.minimum(f, hem.cut((end - t) * limb.length, limb.phi / (2 * np.pi) + 0.5, frayed)), f)
            else:
                j = self.f.j
                limb = Limb(X, j[f"hip.{side}"], j[f"ankle.{side}"])
                on = (X[:, 0] * (1 if side == "R" else -1) > 0) & (X[:, 2] < j["hip.R"].z)
                f = np.where(on, np.minimum(f, hem.cut(X[:, 2] - end, limb.phi / (2 * np.pi) + 0.5, frayed)), f)
        return f

    def mask(self, body, test, kind):
        """The body vertices garment ``kind`` covers (``test``) where it isn't torn away (for strip_covered)."""
        inside = np.array([bool(test(v.co)) for v in body.data.vertices])
        return inside & (self.field(self.X, kind) > 0.003 * self.s)

    # ------------------------------------------------------------------ cutting the cloth
    def tear(self, bm, kind, X, shade):
        """Cuts the tailored shell ``bm`` (vertices at rest positions ``X``) along its tears, darkens and lifts the cloth
        at the torn edges; returns the per-vertex colour multipliers (n, 3) for the new vertex order."""
        if kind not in self.kinds:
            return shade
        f = self.field(X, kind)
        out = cut(bm, f, {"shade": shade, "soft": self.field(X, kind, frayed=False)})
        s = self.s
        k, f = out["shade"], np.maximum(out["soft"], 0.0)
        self.edge = out["field"]
        grime = 1.0 - ss(0.0, 0.028 * s, f)
        rgb = k[:, None] * (1.0 - grime[:, None] ** 1.5 * (1.0 - GRIME * 0.7))
        bm.normal_update()
        lift = 0.0012 * s * (1.0 - ss(0.0, 0.015 * s, f)) ** 2
        for v, d in zip(bm.verts, lift):
            if d > 1e-6:
                v.co += v.normal * d
        bm.normal_update()
        return rgb

    def keep(self, n):
        """Weights for the ``n`` vertices of the cloth just torn (low near the tears; see finishing.reduce), so
        reducing the mesh keeps their outline and doesn't leave long slivers fanning out from them; or None."""
        edge, self.edge = getattr(self, "edge", None), None
        return None if edge is None or len(edge) != n else ss(0.004 * self.s, 0.04 * self.s, edge)


def cut(bm, f, layers=None):
    """Cuts the triangulated mesh along ``f`` == 0 (one value per vertex) and deletes the side below zero, along with
    scraps left hanging; vertex layers (weights) are carried onto the new edge. ``layers`` {name: per-vertex values}
    are interpolated the same way; returns them (and "field") for the vertices that remain, in their new order."""
    bm.verts.ensure_lookup_table()
    names = ["field"] + list(layers or {})
    lay = {name: bm.verts.layers.float.new("_cut_" + name) for name in names}
    values = {"field": np.where(np.abs(f) < 1e-6, 1e-6, f), **(layers or {})}
    for name in names:
        for v, x in zip(bm.verts, values[name]):
            v[lay[name]] = float(x)
    fl = lay["field"]
    on = []
    for e in [e for e in bm.edges if (e.verts[0][fl] > 0) != (e.verts[1][fl] > 0)]:
        a, b = e.verts
        _, nv = bmesh.utils.edge_split(e, a, a[fl] / (a[fl] - b[fl]))
        nv[fl] = 0.0
        on.append(nv)
    if on:
        bmesh.ops.connect_verts(bm, verts=on)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(v[fl] < 0 for v in f.verts)], context="FACES")
    _drop_scraps(bm, 0.02 * 0.02)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    out = {name: np.array([v[lay[name]] for v in bm.verts]) for name in names}
    for name in names:
        bm.verts.layers.float.remove(lay[name])
    return out


def _drop_scraps(bm, min_area):
    """Deletes pieces of cloth smaller than ``min_area`` cut off from the rest."""
    seen, scraps = set(), []
    for start in bm.faces:
        if start in seen:
            continue
        piece, stack = [], [start]
        seen.add(start)
        while stack:
            face = stack.pop()
            piece.append(face)
            for e in face.edges:
                for g in e.link_faces:
                    if g not in seen:
                        seen.add(g)
                        stack.append(g)
        if sum(face.calc_area() for face in piece) < min_area:
            scraps += piece
    if scraps:
        bmesh.ops.delete(bm, geom=scraps, context="FACES")


def lip(bm, layer, deform, loop, closed, depth, shade=0.5):
    """Turns a torn edge under: a narrow strip from the edge in toward the skin, as dark as the cloth's shadowed back,
    so the edge shows the cloth's thickness and the gap under it never shows. The strip has its own vertices, so the
    edge stays crisp and the cloth's shading beside it is undisturbed."""
    n = len(loop)
    if n < 3:
        return
    raw = [v.normal.copy() for v in loop]
    nrm = []
    for i in range(n):
        nn = sum((raw[(i + d) % n] if closed else raw[min(max(i + d, 0), n - 1)] for d in range(-2, 3)), Vector())
        nrm.append(nn.normalized() if nn.length > 1e-6 else raw[i])

    def copy(v, co):
        nv = bm.verts.new(co)
        for g, wt in v[deform].items():
            nv[deform][g] = wt
        return nv

    top = [copy(v, v.co) for v in loop]
    inner = [copy(v, v.co - nn * depth) for v, nn in zip(loop, nrm)]
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        edge = bm.edges.get((loop[i], loop[j]))
        if edge is None or not edge.link_faces:
            continue
        face = edge.link_faces[0]
        src = {lp.vert: lp[layer] for lp in face.loops}
        forward = any(lp.vert is loop[i] and lp.link_loop_next.vert is loop[j] for lp in face.loops)
        quad = (top[j], top[i], inner[i], inner[j]) if forward else (top[i], top[j], inner[j], inner[i])
        for lp in bm.faces.new(quad).loops:
            c = src[loop[i]] if lp.vert in (top[i], inner[i]) else src[loop[j]]
            k = 0.8 if lp.vert in (top[i], top[j]) else 0.8 * shade
            lp[layer] = (c[0] * k, c[1] * k, c[2] * k, 1.0)
