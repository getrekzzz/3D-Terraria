"""The possessed armour's limbs (plate.py): shaped plate, not stacked tubes. Domed pauldrons with two lames fanning
down the outside of the arm under them, rerebraces, couters cupping the point of the elbow with a wing on the outer
side, vambraces tapering to the wrist with a ridge; cuisses with a front ridge, flattened on the inside of the thigh,
tassets over them, poleyns domed over the knee with a soft keel and a side wing and a lame above and below, greaves
swelling at the calf and tapering to the ankle with a shin ridge and a flared foot, and solid sabatons with pointed
toe caps. Tubes are lathed (lathe.py); the small cups are meshed fields, solid so there is no thin shell to crumple.
"""
import numpy as np

from .lathe import Lathe, profile
from .sdf import ellipsoid, round_cone, smin, sphere


def pieces(fr, t):
    from .plate import Plate, ball_cut, plane
    j, s = fr.j, fr.s
    V = lambda v: np.asarray(tuple(v), dtype=float)
    S = lambda *v: np.asarray(v, dtype=float) * s
    fwd = (0, 1, 0)
    out = []
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = V(j[f"shoulder.{side}"]), V(j[f"elbow.{side}"]), V(j[f"wrist.{side}"])
        hip, kn, an = V(j[f"hip.{side}"]), V(j[f"knee.{side}"]), V(j[f"ankle.{side}"])
        up = (el - sh) / np.linalg.norm(el - sh)
        lat, ua, fa, th, sn = (sx, 0, 0), f"upper_arm.{side}", f"forearm.{side}", f"thigh.{side}", f"shin.{side}"
        at = lambda a, b, u: a + (b - a) * u
        # pauldron: a dome over the shoulder, two lames below it fanning down the outside of the arm
        pc = sh + S(sx * 0.012, -0.004, 0.006)
        out.append(Plate(f"pauldron_{side}", ua, (lambda pc: lambda p: ellipsoid(p, pc, (0.094 * s, 0.098 * s, 0.09 * s)))(pc),
                         [plane(pc + up * 0.02 * s, up + np.asarray((-sx * 0.25, 0, 0))), ball_cut(S(0, -0.02, 1.47), 0.1 * s)],
                         t, pc - 0.12 * s, pc + 0.12 * s, 3200))
        off = S(sx * 0.008, 0, 0)
        for k in range(2):
            out.append(Lathe(f"pauldron_lame{k}_{side}", ua, None, at(sh, el, 0.04 + 0.17 * k) + off, at(sh, el, 0.27 + 0.17 * k) + off,
                             t, s, lat, 112, 4, 40, profile(0.095 - 0.008 * k, 0.088 - 0.008 * k, s)))
        out.append(Lathe(f"rerebrace_{side}", ua, None, at(sh, el, 0.36), at(sh, el, 0.9), t, s, lat, None, 10, 32,
                         profile(0.058, 0.052, s, ridge=0.004, toward=lat)))
        # couter: a cup over the point of the elbow, drawn to a point behind, a rounded wing on the outer side
        def couter(p, el=el, sx=sx):
            cup = smin(ellipsoid(p, el + S(0, -0.012, 0), (0.05 * s, 0.042 * s, 0.054 * s)),
                       ellipsoid(p, el + S(0, -0.042, 0), (0.016 * s, 0.022 * s, 0.016 * s)), 0.02 * s)
            return smin(cup, ellipsoid(p, el + S(sx * 0.038, -0.008, 0), (0.018 * s, 0.038 * s, 0.05 * s)), 0.02 * s)
        out.append(Plate(f"couter_{side}", fa, couter, [plane(el + S(0, 0.006, 0), (0, 1, 0))], t, el - 0.1 * s, el + 0.1 * s, 1100,
                         solid=True))
        out.append(Lathe(f"vambrace_{side}", fa, None, at(el, wr, 0.12), at(el, wr, 0.87), t, s, lat, None, 12, 32,
                         profile(0.055, 0.044, s, ridge=0.004, toward=lat, flare=(0.004, 0.8))))
        # legs
        out.append(Lathe(f"cuisse_{side}", th, None, at(hip, kn, 0.04), at(hip, kn, 0.87), t, s, fwd, None, 14, 40,
                         profile(0.088, 0.064, s, ridge=0.008, toward=fwd, swell=(-0.012, 0.4, 0.6, (-sx, 0, 0)))))
        for k in range(2):           # tassets: two lames hanging from the fauld over the front of the thigh
            a0 = hip + S(sx * 0.008, 0, 0)
            out.append(Lathe(f"tasset{k}_{side}", th, None, at(a0, kn, 0.04 + 0.15 * k), at(a0, kn, 0.23 + 0.15 * k), t, s, fwd, 96, 5, 32,
                             profile(0.098 - 0.008 * k, 0.094 - 0.008 * k, s)))
        out.append(Lathe(f"knee_lame0_{side}", th, None, at(hip, kn, 0.82), at(hip, kn, 0.94), t, s, fwd, 100, 3, 28, profile(0.068, 0.064, s)))
        out.append(Lathe(f"knee_lame1_{side}", sn, None, at(kn, an, 0.06), at(kn, an, 0.16), t, s, fwd, 100, 3, 28, profile(0.062, 0.06, s)))

        def poleyn(p, kn=kn, sx=sx):            # a dome over the knee, softly keeled, a rounded wing on the outer side
            kc = kn + S(0, 0.008, 0.002)
            cup = smin(ellipsoid(p, kc, (0.064 * s, 0.06 * s, 0.066 * s)), ellipsoid(p, kc + S(0, 0.012, 0), (0.018 * s, 0.056 * s, 0.06 * s)),
                       0.02 * s)
            return smin(cup, ellipsoid(p, kn + S(sx * 0.054, 0.0, -0.004), (0.02 * s, 0.042 * s, 0.054 * s)), 0.022 * s)
        out.append(Plate(f"poleyn_{side}", sn, poleyn, [plane(kn + S(0, -0.002, 0), (0, -1, 0))], t, kn - 0.11 * s, kn + 0.11 * s, 1500,
                         solid=True))
        out.append(Lathe(f"greave_{side}", sn, None, at(kn, an, 0.13), at(kn, an, 0.93), t, s, fwd, None, 17, 36,
                         profile(0.054, 0.04, s, ridge=0.005, toward=fwd, swell=(0.02, 0.25, 0.2, (0, -1, 0)), flare=(0.007, 0.86))))
        out += sabatons(fr, side, t)
    return out


def sabatons(fr, side, t):
    """Solid foot plates (no open sole to see into from behind) and pointed toe caps."""
    from .plate import Plate, plane
    j, s = fr.j, fr.s
    V = lambda v: np.asarray(tuple(v), dtype=float)
    an, heel, ball, toe = (V(j[f"{k}.{side}"]) for k in ("ankle", "heel", "ball", "toe"))
    fwd = toe - heel
    fwd[2] = 0
    fwd /= np.linalg.norm(fwd)

    def foot(p):
        o = ellipsoid(p, (heel + ball) / 2 + np.asarray((0, 0, 0.035 * s)), (0.05 * s, 0.11 * s, 0.05 * s))
        return smin(o, sphere(p, an + np.asarray((0, -0.005 * s, -0.01 * s)), 0.05 * s), 0.02 * s)

    tip = toe + fwd * 0.05 * s + np.asarray((0, 0, 0.012 * s))

    def cap(p):
        return smin(round_cone(p, ball + np.asarray((0, 0, 0.03 * s)), tip, 0.044 * s, 0.008 * s),
                    ellipsoid(p, ball + np.asarray((0, 0.01 * s, 0.026 * s)), (0.046 * s, 0.04 * s, 0.032 * s)), 0.02 * s)
    ground = plane((0, 0, 0.002 * s), (0, 0, -1))
    return [Plate(f"sabaton_{side}", f"foot.{side}", foot, [ground, plane(an + np.asarray((0, 0, 0.02 * s)), (0, 0.4, 1)), plane(ball + fwd * 0.012 * s, fwd)],
                  t, heel - 0.12 * s, ball + 0.12 * s, 1300, solid=True),
            Plate(f"toecap_{side}", f"toe.{side}", cap, [ground, plane(ball - fwd * 0.004 * s, -fwd)], t, ball - 0.1 * s, tip + 0.06 * s, 800,
                  solid=True)]


def voiders(fr, key, steel):
    """Dark mail behind the knees and inside the elbows, where the plates part over a bending joint: a lathed sleeve
    within the plates, its weights blending from the upper bone to the lower so it bends with the joint, painted in
    alternating dark links."""
    rings, segs = 6, 20
    from . import matter
    from .rig import write_weights
    j, s = fr.j, fr.s
    V = lambda v: np.asarray(tuple(v), dtype=float)
    dark = steel * 0.52
    out = []
    for side in ("R", "L"):
        for up, mid, lo, bones, front, rad in (
                ("hip", "knee", "ankle", ("thigh", "shin"), (0, 1, 0),
                 lambda u, e: (0.05 - 0.006 * u + 0.008 * np.clip(-e[..., 1], 0, 1) ** 1.5) * s),
                ("shoulder", "elbow", "wrist", ("upper_arm", "forearm"), (0, -1, 0), lambda u, e: (0.04 - 0.002 * u) * s + 0 * e[..., 0])):
            a, m, b = V(j[f"{up}.{side}"]), V(j[f"{mid}.{side}"]), V(j[f"{lo}.{side}"])
            p0, p1 = m + (a - m) * 0.22, m + (b - m) * 0.16
            sleeve = Lathe(f"voider_{mid}_{side}", f"{bones[0]}.{side}", None, p0, p1, 0.002 * s, s, front, None, rings, segs, rad)
            obj = sleeve.make(key, s, dark)
            co = matter.coords(obj)
            d = (p1 - p0) / np.linalg.norm(p1 - p0)
            w = matter.ss(0.3, 0.7, (co - p0) @ d / np.linalg.norm(p1 - p0))
            write_weights(obj, [{f"{bones[0]}.{side}": 1 - x, f"{bones[1]}.{side}": x} for x in w])
            row, col = np.divmod(np.arange(len(co)), segs)
            link = np.where((row < rings) & ((row + col) % 2 == 1), 0.78, 1.0)[:, None]
            matter.paint(obj, dark * link * (0.9 + 0.2 * matter.noise(co, 30.0, 61)[:, None]))
            matter.slot(obj, "char:metal")
            out.append(obj)
    return out
