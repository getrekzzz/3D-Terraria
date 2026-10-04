"""Armour layouts by style (see armor.py): which plates each style wears, their sizes, seams and light strips.
Heights are frame units (an average man's metres); a right-side piece is mirrored onto the left."""
import numpy as np

from . import armorparts as A


def cyborg(sm):
    L = sm.limbs["R"]
    A.torso(sm, "plate_chest", top=1.435, bottom=1.235, half=0.14, inner=0.005, thick=0.005, notch=(0.065, 0.04),
            corner=0.045, panel=(0.016, 0.0022), seams=[("u", 0.0, 1.25, 1.4)],
            lines=[[(-0.12, 1.268), (-0.06, 1.252), (-0.012, 1.247)], [(0.012, 1.247), (0.06, 1.252), (0.12, 1.268)]])
    for k, (top, bottom, half) in enumerate(((1.222, 1.168, 0.1), (1.152, 1.1, 0.09))):
        A.torso(sm, f"plate_belly{k}", top=top, bottom=bottom, half=half / 2 - 0.004, centre=half / 2 + 0.002, inner=0.004,
                thick=0.004, corner=0.012, mirror=True)
    A.torso(sm, "plate_back", top=1.44, bottom=1.19, half=0.14, back=True, inner=0.005, thick=0.005, notch=(0.06, 0.03),
            corner=0.045, panel=(0.016, 0.0022), seams=[("u", 0.0, 1.2, 1.41)],
            lines=[[(0.024, 1.215), (0.024, 1.39)], [(-0.024, 1.215), (-0.024, 1.39)]])
    A.torso(sm, "plate_belt", top=1.054, bottom=1.012, half=0.42, inner=0.003, thick=0.006, corner=0.004,
            lines=[[(u, 1.033) for u in np.linspace(-0.08, 0.08, 9)]])
    c, r = L["deltoid"]
    A.cap(sm, "plate_shoulder", c, r, (0.55, -0.05, 0.83), top=1.25, inner=0.007, seams=[0.55], rings=[1.08],
          lines=[[(0.8, a) for a in np.linspace(-1.5, 1.5, 11)]])
    A.limb(sm, "plate_forearm", L["fore"], 0.12, 0.92, 0.052, L["lateral"], seams=[("v", 0.52, -0.04, 0.04)],
           lines=[[(0.0, 0.18), (0.0, 0.46)], [(0.0, 0.58), (0.0, 0.86)]])
    A.limb(sm, "plate_upperarm", L["upper"], 0.3, 0.78, 0.042, L["lateral"], corner=0.01)
    A.limb(sm, "plate_shin", L["shin"], 0.12, 0.74, 0.05, (0.12, 1.0, 0.0), taper=0.25, seams=[("u", 0.0, 0.14, 0.7)],
           lines=[[(-0.026, 0.16), (-0.022, 0.66)], [(0.026, 0.16), (0.022, 0.66)]])
    c, r = L["knee"]
    A.cap(sm, "plate_knee", c, r, (0.08, 1.0, 0.15), top=0.8, seams=[0.42], stiff=0.4)


def martian(sm):
    """Rounded, chunky trooper armour: glossy plates (``armorColor``) with ``trim`` pauldrons and belt."""
    L, trim = sm.limbs["R"], sm.trim
    A.torso(sm, "plate_chest", top=1.44, bottom=1.16, half=0.15, inner=0.006, thick=0.007, notch=(0.07, 0.035),
            corner=0.06, panel=(0.022, 0.003), slot="char:nail", seams=[("v", 1.235, -0.1, 0.1)],
            lines=[[(u, 1.212) for u in np.linspace(-0.1, 0.1, 11)]])
    A.torso(sm, "plate_back", top=1.445, bottom=1.17, half=0.15, back=True, inner=0.006, thick=0.007, notch=(0.065, 0.03),
            corner=0.06, panel=(0.022, 0.003), slot="char:nail")
    A.torso(sm, "plate_belt", top=1.062, bottom=1.006, half=0.42, inner=0.003, thick=0.008, corner=0.004, color=trim,
            slot="char:metal", seams=[("u", u, 1.006, 1.062) for u in (-0.3, -0.18, 0.18, 0.3)])
    A.torso(sm, "plate_buckle", top=1.07, bottom=0.998, half=0.035, inner=0.011, thick=0.007, corner=0.012, slot="char:metal",
            lines=[[(-0.018, 1.034), (0.018, 1.034)]])
    c, r = L["deltoid"]
    A.cap(sm, "plate_shoulder", c, r, (0.6, -0.05, 0.8), top=1.32, inner=0.006, thick=0.007, flare=0.012, rings=[1.12],
          color=trim, slot="char:metal", lines=[[(0.62, a) for a in np.linspace(-1.6, 1.6, 13)]])
    A.limb(sm, "plate_cuff", L["fore"], 0.5, 0.97, 0.13, L["lateral"], thick=0.007, corner=0.02, slot="char:nail",
           seams=[("v", 0.62, -0.13, 0.13)], lines=[[(u, 0.8) for u in np.linspace(-0.09, 0.09, 9)]])
    A.limb(sm, "plate_shin", L["shin"], 0.16, 0.82, 0.072, (0.1, 1.0, 0.0), thick=0.007, corner=0.025, slot="char:nail",
           seams=[("v", 0.5, -0.08, 0.08)], lines=[[(u, 0.32) for u in np.linspace(-0.05, 0.05, 7)]])
    c, r = L["knee"]
    A.cap(sm, "plate_knee", c, r, (0.08, 1.0, 0.15), top=1.0, thick=0.007, color=trim, rings=[0.62], stiff=0.4)


def diver(sm):
    """A pressure suit's sleek plates with lit seams, a glowing core on the chest."""
    L = sm.limbs["R"]
    A.torso(sm, "plate_chest", top=1.435, bottom=1.2, half=0.13, inner=0.005, thick=0.006, notch=(0.06, 0.035),
            corner=0.05, panel=(0.018, 0.0025), seams=[("u", 0.0, 1.21, 1.3)],
            lines=[[(-0.1, 1.38), (-0.05, 1.33), (-0.02, 1.32)], [(0.02, 1.32), (0.05, 1.33), (0.1, 1.38)]])
    A.torso(sm, "plate_back", top=1.445, bottom=1.15, half=0.15, back=True, inner=0.006, thick=0.006, notch=(0.06, 0.03),
            corner=0.05)
    A.torso(sm, "plate_belt", top=1.055, bottom=1.012, half=0.42, inner=0.003, thick=0.007, corner=0.004, color=sm.trim,
            lines=[[(u, 1.034) for u in np.linspace(-0.06, 0.06, 7)]])
    c, r = L["deltoid"]
    A.cap(sm, "plate_shoulder", c, r, (0.55, -0.05, 0.83), top=1.2, seams=[0.6], lines=[[(0.85, a) for a in np.linspace(-1.2, 1.2, 9)]])
    A.limb(sm, "plate_forearm", L["fore"], 0.15, 0.95, 0.07, L["lateral"], corner=0.015, seams=[("v", 0.55, -0.06, 0.06)],
           lines=[[(0.0, 0.2), (0.0, 0.5)]])
    A.limb(sm, "plate_shin", L["shin"], 0.1, 0.9, 0.065, (0.1, 1.0, 0.0), taper=0.2, ridge=(0.006, 0.0025),
           lines=[[(-0.034, 0.15), (-0.03, 0.75)], [(0.034, 0.15), (0.03, 0.75)]])
    c, r = L["knee"]
    A.cap(sm, "plate_knee", c, r, (0.08, 1.0, 0.15), top=0.72, seams=[0.4], stiff=0.4)


def carapace(sm):
    """Chitin: overlapping plates, glossy and dark (``armorColor``), paling to ``trim`` at their edges."""
    L, trim = sm.limbs["R"], sm.trim
    for k, (top, bottom, half) in enumerate(((1.44, 1.33, 0.15), (1.345, 1.255, 0.14), (1.27, 1.19, 0.12))):
        A.torso(sm, f"shell_chest{k}", top=top, bottom=bottom, half=half, inner=0.004 + 0.004 * (2 - k), thick=0.006,
                notch=(0.07, 0.04) if k == 0 else None, corner=0.05, ridge=(0.006, 0.003), slot="char:nail", edge=trim)
        A.torso(sm, f"shell_back{k}", top=top + 0.005, bottom=bottom - 0.01, half=half + 0.01, back=True,
                inner=0.004 + 0.004 * (2 - k), thick=0.006, notch=(0.065, 0.03) if k == 0 else None, corner=0.05,
                ridge=(0.008, 0.004), slot="char:nail", edge=trim)
    c, r = L["deltoid"]
    for k, (tilt, top) in enumerate(((0.0, 1.25), (0.35, 0.9), (0.7, 0.7))):
        axis = np.asarray((0.55 + 0.4 * tilt, -0.05, 0.83 - 0.9 * tilt))
        A.cap(sm, f"shell_shoulder{k}", c, r, axis, top=top, inner=0.005 + 0.007 * (2 - k) / 2, thick=0.006, flare=0.01,
              slot="char:nail", stiff=0.6, edge=trim)
    for k, (v0, v1) in enumerate(((0.1, 0.43), (0.34, 0.69), (0.6, 0.94))):      # (overlapping: no lower rim shows)
        A.limb(sm, f"shell_forearm{k}", L["fore"], v0, v1, 0.075, L["lateral"], inner=0.004 + 0.0025 * (2 - k),
               thick=0.006, corner=0.02, ridge=(0.005, 0.003), taper=0.25, slot="char:nail", edge=trim)
        A.limb(sm, f"shell_shin{k}", L["shin"], 0.06 + v0 * 0.88, 0.06 + v1 * 0.88, 0.07, (0.1, 1.0, 0.0),
               inner=0.004 + 0.0025 * (2 - k), thick=0.006, corner=0.02, ridge=(0.005, 0.003), taper=0.25, slot="char:nail",
               edge=trim)


def solar(sm):
    """A Selenian's heavy plate: an orange breastplate with a glowing sun, a ridged back plate, faulds over the belly,
    a red belt with a sun buckle, layered flared pauldrons, bracers, greaves and knee caps, with flame blades
    (solarblades.py) rising from the pauldrons and sweeping back from the elbows and the knees."""
    from mathutils import Vector
    from .solarblades import Forge
    L, trim, s = sm.limbs["R"], sm.trim, sm.s
    sun = [[(0.03 * np.cos(a), 1.31 + 0.03 * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 21)]]
    sun += [[(r * np.cos(a), 1.31 + r * np.sin(a)) for r in np.linspace(0.043, 0.082, 5)]
            for a in np.radians((0, 180, -35, -90, -145, 40, 140))]
    A.torso(sm, "plate_chest", top=1.445, bottom=1.155, half=0.155, inner=0.006, thick=0.008, notch=(0.07, 0.04),
            corner=0.05, rim=0.003, lines=sun, edge=trim)
    A.torso(sm, "plate_back", top=1.45, bottom=1.16, half=0.155, back=True, inner=0.006, thick=0.008, notch=(0.065, 0.03),
            corner=0.05, rim=0.003, ridge=(0.008, 0.004), edge=trim)
    for k, (top, bottom, half) in enumerate(((1.145, 1.108, 0.125), (1.1, 1.066, 0.118))):
        A.torso(sm, f"plate_fauld{k}", top=top, bottom=bottom, half=half, inner=0.004, thick=0.006, corner=0.012)
    A.torso(sm, "plate_belt", top=1.062, bottom=1.006, half=0.42, inner=0.003, thick=0.008, corner=0.004, color=trim,
            seams=[("u", u, 1.006, 1.062) for u in (-0.3, -0.16, 0.16, 0.3)])
    A.torso(sm, "plate_buckle", top=1.078, bottom=0.99, half=0.044, inner=0.011, thick=0.008, corner=0.04,
            lines=[[(0.024 * np.cos(a), 1.034 + 0.024 * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 17)]])
    c, r = L["deltoid"]
    A.cap(sm, "plate_lame", c, r, (0.92, -0.05, 0.38), top=0.85, inner=0.006, thick=0.007, flare=0.01, color=trim)
    pauldron = A.cap(sm, "plate_pauldron", c, r, (0.6, -0.05, 0.8), top=1.28, inner=0.011, thick=0.008, flare=0.02,
                     rings=[1.08], lines=[[(0.75, a) for a in np.linspace(-1.4, 1.4, 11)]], edge=trim)
    bracer = A.limb(sm, "plate_bracer", L["fore"], 0.08, 0.95, 0.13, L["lateral"], thick=0.007, corner=0.02,
                    ridge=(0.006, 0.003), seams=[("v", 0.5, -0.12, 0.12)], edge=trim)
    greave = A.limb(sm, "plate_greave", L["shin"], 0.08, 0.8, 0.095, (0.1, 1.0, 0.0), thick=0.007, corner=0.025,
                    ridge=(0.007, 0.003), taper=0.15, lines=[[(-0.028, 0.18), (-0.025, 0.7)], [(0.028, 0.18), (0.025, 0.7)]],
                    edge=trim)
    kc, kr = L["knee"]
    A.cap(sm, "plate_knee", kc, kr, (0.08, 1.0, 0.15), top=0.7, thick=0.007, seams=[0.4], stiff=0.4)
    forge = Forge(trim, sm.color, tuple(min(1.0, x * 0.4 + 0.62) for x in (1.0, 0.85, 0.3)), sm.glow)
    fa, fb = (Vector(v) for v in L["fore"][:2])
    sa, sb = (Vector(v) for v in L["shin"][:2])
    for k, sx in enumerate((1, -1)):
        m = Vector((sx, 1, 1))

        def mir(v):
            return Vector((v[0] * m[0], v[1], v[2]))
        for d, out, length, width in (((0.45, -0.45, 0.77), (0.3, -0.35, 0.89), 0.19, 0.075),
                                      ((0.72, -0.4, 0.56), (0.62, -0.32, 0.72), 0.16, 0.065),
                                      ((0.9, -0.3, 0.3), (0.88, -0.3, 0.38), 0.13, 0.055)):
            root, _ = forge.surface(pauldron[k], mir(c), mir(d))              # (flat to the front, curling outward)
            forge.blade(pauldron[k], root, mir(out), mir((0.85, -0.45, -0.1)), length * s, width * s, curl=0.9,
                        thick=0.007 * s)
        lat = mir(L["lateral"])
        root, _ = forge.surface(bracer[k], mir(fa.lerp(fb, 0.22)), lat)
        back = (mir(fa) - mir(fb)).normalized()
        forge.blade(bracer[k], root, back * 0.85 + lat * 0.5, lat, 0.11 * s, 0.034 * s, curl=0.8, thick=0.006 * s)
        root, _ = forge.surface(greave[k], mir(sa.lerp(sb, 0.18)), Vector((sx, 0.25, 0.0)))
        forge.blade(greave[k], root, Vector((sx * 0.5, -0.2, 0.84)), Vector((sx, -0.3, 0)), 0.09 * s, 0.032 * s, curl=0.9,
                    thick=0.006 * s)
    sm.out += forge.finish()
