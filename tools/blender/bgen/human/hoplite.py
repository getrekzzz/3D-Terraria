"""A hoplite: a skeleton in Greek bronze (``kit: hoplite``, a parameter for ``anatomy: skeleton``; added by
__init__.build_skeleton). A Corinthian helm with a horsehair crest (corinthian.py); a muscled bronze cuirass, its
chest and belly modelled, its foot dipping over the belly and rising over the hips; a skirt of leather pteruges in
two layers hanging from under it, the strips swinging with the thighs; bronze greaves shaped over the knee and calf
and open behind; and a round aspis on the outside of the left forearm, its bronze face painted with a lambda. The
bones the cuirass encloses (ribcage, shoulder blades, collarbones, the back's vertebrae) are left out; the arms,
thighs, knees and feet show bare.

Colours: ``armor`` (the bronze), ``crest``, ``pteruges`` and ``emblem`` (the shield's lambda).
"""
import bmesh
import numpy as np

from . import matter
from .lathe import Lathe
from .matter import F
from .sdf import ellipsoid, smin

SOURCES = ("hoplite.py", "plate.py")
HIDDEN = ("ribcage", "scapula", "clavicle")       # bones the cuirass closes round (and the column's middle)


def equip(fr, head, p, extras, bones):
    """A skeleton's worn objects ``extras`` with its kit added (when ``p`` names one), and the ``bones`` left showing."""
    if p.get("kit") != "hoplite":
        return extras, bones
    kit, bones = build(fr, head, p, bones)
    return extras + kit, bones


def build(fr, head, p, bones):
    """The kit's objects, weighted to the human rig's bones, and the ``bones`` left showing."""
    from . import corinthian
    from .plate import _paint
    from .rig import rigid
    s = fr.s
    bronze = matter.hexrgb(p.get("armor", "#c0903a"))
    key = ("hoplite", matter.frame_key(fr))
    piece = cuirass(fr)
    body = matter.mesh(piece.field, (piece.lo, piece.hi), 0.0017 * s, piece.target, piece.name, key, SOURCES)
    _paint(body, piece, bronze)
    rigid(body, "chest")
    out = [body]
    for side in ("R", "L"):
        g = greave(fr, side)
        obj = g.make(key, s, bronze)
        rigid(obj, g.bone)
        out.append(obj)
    for o in out:
        matter.slot(o, "char:metal")
    out.append(pteruges(fr, matter.hexrgb(p.get("pteruges", "#7a2a20"))))
    out.append(aspis(fr, "L", bronze, matter.hexrgb(p.get("emblem", "#5a1410"))))
    out += corinthian.helm(head, bronze, matter.hexrgb(p.get("crest", "#b02a22")))
    import bpy
    keep = []
    for b in bones:
        if b.name.startswith(HIDDEN):
            me = b.data
            bpy.data.objects.remove(b)
            bpy.data.meshes.remove(me)
            continue
        if b.name.startswith("spine"):           # the back's vertebrae, which would come through the backplate
            z = matter.coords(b)[:, 2]
            inside = np.flatnonzero((z > fr.j["spine"].z + 0.03 * s) & (z < fr.j["neck"].z - 0.035 * s))
            bm = bmesh.new()
            bm.from_mesh(b.data)
            bm.verts.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[bm.verts[i] for i in inside], context="VERTS")
            bm.to_mesh(b.data)
            bm.free()
        keep.append(b)
    return out, keep


def cuirass(fr):
    """A muscle cuirass: a barrel from the hips to over the shoulders with the chest's two plates and the belly's
    grooves standing in the bronze, open at the neck and the arms, its foot dipping in front over the belly."""
    from .plate import Plate, ball_cut, tube_cut
    j, s = fr.j, fr.s
    S = lambda *v: np.asarray(v, dtype=float) * s
    t = 0.006 * s

    def outer(p):
        o = smin(ellipsoid(p, S(0, 0.004, 1.27), (0.15 * s, 0.122 * s, 0.24 * s)),
                 ellipsoid(p, S(0, 0.026, 1.33), (0.138 * s, 0.106 * s, 0.15 * s)), 0.03 * s)
        o = smin(o, ellipsoid(p, S(0, 0.0, 1.06), (0.146 * s, 0.112 * s, 0.13 * s)), 0.05 * s)
        for sx in (1, -1):
            o = smin(o, ellipsoid(p, S(sx * 0.066, 0.112, 1.355), (0.08 * s, 0.022 * s, 0.052 * s)), 0.03 * s)
        x, y, z = p[:, 0] / F(s), p[:, 1] / F(s), p[:, 2] / F(s)
        front = matter.ss(0.02, 0.09, y).astype(F)
        ax = np.abs(x)
        chest = F(1.304) + F(0.042) * (ax / F(0.13)) ** F(1.6)           # the chest's lower edge, rising to the arms
        groove = np.exp(-(x / F(0.0075)) ** 2) * matter.ss(1.43, 1.39, z).astype(F)
        groove = groove + np.exp(-((z - chest) / F(0.0065)) ** 2) * matter.ss(0.135, 0.1, ax).astype(F)
        for zk in (1.215, 1.15, 1.088):
            groove = groove + np.exp(-((z - F(zk)) / F(0.0055)) ** 2) * matter.ss(0.1, 0.06, np.abs(x)).astype(F)
        return o + F(0.0035 * s) * groove * front

    def foot(p):
        x, y = p[:, 0] / F(s), p[:, 1] / F(s)
        dip = F(0.05) * np.exp(-(x / F(0.08)) ** 2) * matter.ss(0.0, 0.08, y).astype(F)
        return F(s) * (F(1.062) - dip) - p[:, 2]
    cuts = [foot, tube_cut(S(0, -0.02, 0), (0, 0, 1), 0.078 * s)]
    for side, sx in (("R", 1), ("L", -1)):
        cuts.append(ball_cut(np.asarray(tuple(j[f"shoulder.{side}"])) + S(-sx * 0.005, 0, -0.035), 0.085 * s))
    c = S(0, 0.004, 1.25)
    return Plate("cuirass", "chest", outer, cuts, t, c - S(0.2, 0.18, 0.3), c + S(0.2, 0.18, 0.3), 8000)


def greave(fr, side):
    """A bronze greave from over the knee to the ankle, open behind: a boss over the kneecap, a ridge down the shin,
    swelling round the calf and flaring a little at the ankle, reaching higher and lower in front than behind."""
    j, s = fr.j, fr.s
    kn, an = (np.asarray(tuple(j[f"{k}.{side}"]), dtype=float) for k in ("knee", "ankle"))

    def radius(u, e):
        fwd = e[..., 1]
        r = 0.05 - 0.02 * u + 0.011 * np.exp(-((u - 0.03) / 0.07) ** 2) * np.clip(fwd, 0, 1) ** 1.5
        r = r + 0.015 * np.exp(-((u - 0.33) / 0.16) ** 2) * np.clip(0.75 - fwd, 0, 1.75) ** 1.1
        return (r + 0.006 * matter.ss(0.86, 1.0, u) + 0.0025 * np.clip(fwd, 0, 1) ** 14) * s
    return Lathe(f"greave_{side}", f"shin.{side}", None, kn + (an - kn) * -0.08, kn + (an - kn) * 0.94, 0.0045 * s, s,
                 (0, 1, 0), 148, 18, 40, radius, ends=0.72)


def pteruges(fr, leather):
    """Two layers of leather strips round the hips under the cuirass, the outer shorter, flaring a little as they
    hang; they ride the hips at the top and swing more and more with the nearer thigh toward their ends."""
    from .garments import mesh_from
    from .rig import write_weights
    s = fr.s
    bm = bmesh.new()
    weights, rgb = [], []
    rows, th = 6, 0.0045 * s
    for layer, (n, top, length, rx0, ry0, phase) in enumerate(((22, 1.055, 0.2, 0.124, 0.094, 0.5),
                                                              (22, 1.07, 0.165, 0.133, 0.101, 0.0))):
        for k in range(n):
            a = 2 * np.pi * (k + phase) / n
            tone = 0.9 + 0.08 * np.sin(k * 2.3 + layer * 1.7)
            grid = []
            for i in range(rows):
                v = i / (rows - 1)
                rx, ry = (rx0 + 0.03 * v ** 1.3) * s, (ry0 + 0.03 * v ** 1.3) * s
                c = np.asarray((rx * np.sin(a), ry * np.cos(a), (top - length * v) * s))
                tg = np.asarray((rx * np.cos(a), -ry * np.sin(a), 0.0))
                tg /= np.linalg.norm(tg)
                nrm = np.asarray((-tg[1], tg[0], 0.0))
                w = 0.021 * s * (0.75 if i == rows - 1 else 1.0)
                row = []
                for off in (1, -1):              # outer face, then inner
                    for q in (-1, 0, 1):
                        co = c + tg * q * w + nrm * off * th / 2
                        row.append(bm.verts.new(tuple(co)))
                        wr = 0.65 * v ** 1.2 * matter.ss(-0.05, 0.07, co[0] / s)
                        wl = 0.65 * v ** 1.2 * matter.ss(-0.05, 0.07, -co[0] / s)
                        weights.append({"hips": 1 - wr - wl, "thigh.R": wr, "thigh.L": wl})
                        edge = 0.8 if q else 1.0
                        rgb.append(leather * tone * edge * (0.75 if i == rows - 1 else 1.0) * (0.6 if off < 0 else 1.0))
                grid.append(row)
            for i in range(rows - 1):
                g0, g1 = grid[i], grid[i + 1]
                for q in range(2):
                    bm.faces.new((g0[q], g1[q], g1[q + 1], g0[q + 1]))                  # outer face
                    bm.faces.new((g0[3 + q + 1], g1[3 + q + 1], g1[3 + q], g0[3 + q]))  # inner face
                bm.faces.new((g0[2], g1[2], g1[5], g0[5]))                              # the two long edges
                bm.faces.new((g0[3], g1[3], g1[0], g0[0]))
            last = grid[-1]
            for q in range(2):                                                          # the end
                bm.faces.new((last[q], last[q + 1], last[3 + q + 1], last[3 + q]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = mesh_from(bm, "pteruges")
    write_weights(obj, weights)
    matter.paint(obj, np.asarray(rgb))
    matter.slot(obj, "char:leather")
    return obj


def aspis(fr, side, bronze, emblem):
    """The round shield on the outside of the forearm, facing out and a little toward the hand: a bronze-faced dish
    with a flat rim, a lambda on its face, dark wood behind."""
    from .garments import mesh_from
    from .rig import rigid
    j, s = fr.j, fr.s
    el, wr = (np.asarray(tuple(j[f"{k}.{side}"]), dtype=float) for k in ("elbow", "wrist"))
    d = (wr - el) / np.linalg.norm(wr - el)
    out = np.asarray((1.0 if side == "R" else -1.0, 0, 0))
    out = out - d * (out @ d)
    out /= np.linalg.norm(out)
    n = out + 0.45 * d
    n /= np.linalg.norm(n)
    up = np.asarray((0, 1.0, 0)) - d * d[1]         # (the forearm's front, which points up when the elbow bends)
    up = up - n * (up @ n)
    up /= np.linalg.norm(up)
    across = np.cross(up, n)
    R, rim, depth, thick = 0.26 * s, 0.03 * s, 0.046 * s, 0.012 * s
    centre = (el + wr) / 2 - n * 0.02 * s
    rings, segs = 20, 72
    ang = np.linspace(0, 2 * np.pi, segs, endpoint=False)
    radii = np.concatenate([np.linspace(0.012, 1, rings) * (R - rim), R - rim + rim * np.asarray((0.35, 0.7, 1.0))])
    height = np.where(radii < R - rim, depth * (1 - (radii / (R - rim)) ** 2), -0.006 * s * ((radii - R + rim) / rim) ** 2)
    bm = bmesh.new()
    cols = []

    def ring(r, h):
        pts = centre + (np.cos(ang)[:, None] * across + np.sin(ang)[:, None] * up) * r + n * h
        return [bm.verts.new(tuple(q)) for q in pts]
    front = [ring(r, h) for r, h in zip(radii, height)]
    back = [ring(r, h - thick) for r, h in zip(radii, height)]
    apex, base = bm.verts.new(tuple(centre + n * depth)), bm.verts.new(tuple(centre + n * (depth - thick)))
    for grid, pole, flip in ((front, apex, False), (back, base, True)):
        for i in range(len(grid) - 1):
            for k in range(segs):
                f = (grid[i][k], grid[i][(k + 1) % segs], grid[i + 1][(k + 1) % segs], grid[i + 1][k])
                bm.faces.new(f[::-1] if flip else f)
        for k in range(segs):
            f = (pole, grid[0][(k + 1) % segs], grid[0][k])
            bm.faces.new(f[::-1] if flip else f)
    for k in range(segs):
        bm.faces.new((front[-1][k], front[-1][(k + 1) % segs], back[-1][(k + 1) % segs], back[-1][k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = mesh_from(bm, "aspis")
    co = matter.coords(obj) - centre
    u, v = co @ across / R, co @ up / R
    lam = np.minimum(_stroke(u, v, (-0.5, -0.5), (0.0, 0.52)), _stroke(u, v, (0.5, -0.5), (0.0, 0.52)))
    grain = matter.fbm(co, 14.0, 3, 77)
    r = np.hypot(u, v)
    face = bronze * (0.86 + 0.2 * grain)[:, None] * (1 - 0.18 * matter.ss(0.86, 0.9, r) * matter.ss(1.0, 0.95, r))[:, None]
    face = np.where((lam < 0.075)[:, None], emblem, face)
    behind = np.zeros(len(co), dtype=bool)                  # (the vertices of the back: the second set of rings, and its pole)
    behind[len(radii) * segs:2 * len(radii) * segs] = True
    behind[-1] = True
    rgb = np.where(behind[:, None], np.asarray((0.3, 0.2, 0.12)) * (0.8 + 0.3 * grain)[:, None], face)
    matter.paint(obj, rgb)
    matter.slot(obj, "char:metal")
    rigid(obj, f"forearm.{side}")
    return obj


def _stroke(u, v, a, b):
    """Distance in the shield's face from the segment a -> b (radius units)."""
    a, b = np.asarray(a), np.asarray(b)
    q = np.stack([u, v], 1) - a
    t = np.clip(q @ (b - a) / ((b - a) @ (b - a)), 0, 1)
    return np.linalg.norm(q - t[:, None] * (b - a), axis=1)
