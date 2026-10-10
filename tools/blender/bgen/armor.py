"""Wearable armour pieces (P3-41), fitted to the player's body by bgen/armor_fit.py and built in their bone's local
space. A set (assets.d armour entry) names a style preset and colours; the preset picks a style per piece:

  head      dome | full (visor slit) | hood | wizard | mask | horned | space (glass visor) | skull | none
  chest     plate | chain | scale | carapace | cloth | ribs | none      (+ belt on the hips)
  pauldron  plate | small | spiky | round | none                        (upper_arm.L/R)
  bracer    plate | wrap | none                                         (forearm.L/R)
  legs      plate | chain | cloth | carapace | none                     (cuisse on thigh.L/R, greave on shin.L/R)
  boot      plate | leather | none                                      (foot.L/R)

Colour params: ``metal`` (main), ``trim`` (rims, crests), ``dark`` (visors, mail gaps), ``glow`` (emissive accents),
``layer`` (metal | cloth | leather | plain | wood | bone) for the main surfaces.
"""
import math

from mathutils import Vector

from .armor_fit import Shell
from .mesh import MeshBuilder

FRONT = math.pi / 2  # bone-local +Z faces forward
EYE_T = 0.41  # eye height along the head bone (bgen/human/frame.py: head centre 0.078 m above the joint)
LAYERS = {"metal": "prop:iron:metal", "cloth": "prop:cloth", "leather": "prop:leather", "plain": "prop:plain",
          "wood": "prop:planks", "bone": "prop:plain", "gold": "prop:gold"}

PRESETS = {
    "simple": {"head": "dome", "chest": "chain", "pauldron": "small", "bracer": "none", "legs": "chain", "boot": "plate"},
    "knight": {"head": "full", "chest": "chain", "pauldron": "plate", "bracer": "plate", "legs": "chain", "boot": "plate"},
    "fluted": {"head": "full", "chest": "plate", "pauldron": "plate", "bracer": "plate", "legs": "plate", "boot": "plate", "crest": True},
    "ornate": {"head": "full", "chest": "plate", "pauldron": "plate", "bracer": "plate", "legs": "plate", "boot": "plate", "crest": True, "trimmed": True},
    "scale": {"head": "horned", "chest": "scale", "pauldron": "spiky", "bracer": "plate", "legs": "plate", "boot": "plate"},
    "molten": {"head": "horned", "chest": "plate", "pauldron": "spiky", "bracer": "plate", "legs": "plate", "boot": "plate", "veins": True},
    "space": {"head": "space", "chest": "plate", "pauldron": "round", "bracer": "plate", "legs": "plate", "boot": "plate", "veins": True},
    "wood": {"head": "dome", "chest": "plate", "pauldron": "small", "bracer": "none", "legs": "plate", "boot": "leather"},
    "jungle": {"head": "hood", "chest": "cloth", "pauldron": "none", "bracer": "wrap", "legs": "cloth", "boot": "leather"},
    "bone": {"head": "skull", "chest": "ribs", "pauldron": "small", "bracer": "wrap", "legs": "chain", "boot": "leather"},
    "carapace": {"head": "dome", "chest": "carapace", "pauldron": "round", "bracer": "plate", "legs": "carapace", "boot": "plate"},
    "hood": {"head": "hood", "chest": "cloth", "pauldron": "none", "bracer": "wrap", "legs": "cloth", "boot": "leather"},
    "wizard": {"head": "wizard"},
    "mining": {"head": "dome", "lamp": True},
}
PIECES = ("head", "chest", "belt", "pauldron.L", "pauldron.R", "bracer.L", "bracer.R", "cuisse.L", "cuisse.R",
          "greave.L", "greave.R", "boot.L", "boot.R")
BONE = {"head": "head", "chest": "chest", "belt": "hips", "pauldron": "upper_arm", "bracer": "forearm",
        "cuisse": "thigh", "greave": "shin", "boot": "foot"}
# The equipment slot each piece belongs to (its item's icon and dropped model show those pieces).
SLOT = {"head": "head", "chest": "body", "belt": "body", "pauldron": "body", "bracer": "body", "cuisse": "legs",
        "greave": "legs", "boot": "legs"}


def pieces_of(entry):
    """(piece, style) for every piece a set wears."""
    p = {**PRESETS[entry.get("preset", "knight")], **entry.get("styles", {})}
    out = []
    for piece in PIECES:
        kind = piece.split(".")[0]
        style = {"belt": p.get("chest", "none"), "cuisse": p.get("legs", "none"), "greave": p.get("legs", "none")}.get(kind, p.get(kind, "none"))
        if style != "none":
            out.append((piece, style))
    return out


def build_piece(mb: MeshBuilder, fit, piece, style, entry):
    """Adds one piece in its bone's local space; returns True when it hides the hair (helmets)."""
    kind, _, side = piece.partition(".")
    bone = BONE[kind] + (f".{side}" if side else "")
    p = {**PRESETS[entry.get("preset", "knight")], **entry}
    layer = LAYERS[p.get("layer", "metal")]
    metal, trim = p.get("metal", "#a8acb4"), p.get("trim") or p.get("metal", "#a8acb4")
    dark, glow = p.get("dark", "#22242a"), p.get("glow")
    rim = trim if p.get("trimmed") or kind in ("head", "chest") else None
    length = fit.length[bone]
    return globals()[f"_{kind}"](mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p)


def _head(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    cloud = fit.local(["head", "neck"], bone, skip=("char:hair", "char:eye"))
    if style == "wizard":
        brim = Shell(cloud, length, 0.45, 0.5, rings=2, segments=20, clearance=0.03, smooth=1)
        ring = [brim.point(0, brim.angle(k), 0.06) for k in range(20)]
        mb.tube(ring + [ring[0]], [0.018] * 21, "prop:cloth", metal, segments=5)
        apex = Vector((0.0, length * 1.9, -0.06))
        mb.cylinder((0, length * 0.5, 0), tuple(apex), 0.105, 0.012, "prop:cloth", metal, segments=14)
        if glow:
            for k in range(3):
                a = FRONT + (k - 1) * 0.55
                c = Vector((math.cos(a) * 0.09, length * (0.75 + 0.18 * k), math.sin(a) * 0.09))
                mb.sphere(tuple(c), 0.012, "prop:plain:glow", glow, subdivisions=1)
        return False
    spans = {"dome": (0.2, 1.0), "full": (-0.55, 1.0), "horned": (-0.55, 1.0), "space": (-0.6, 1.0), "hood": (-0.7, 1.05),
             "mask": (-0.35, 0.75), "skull": (-0.45, 1.0)}
    t0, t1 = spans[style]
    arc = None
    if style == "hood":
        arc = (FRONT + 0.75, FRONT - 0.75 + 2 * math.pi)
    elif style == "mask":
        arc = (FRONT - 1.1, FRONT + 1.1)
    top = (lambda t: 1.0 - 0.75 * max(0.0, (t - 0.72) / 0.28) ** 2) if style not in ("mask",) else None
    shell = Shell(cloud, length, t0, t1, rings=8, segments=16, clearance=0.025 if style != "space" else 0.05,
                  arc=arc, profile=top)
    material = "prop:cloth" if style == "hood" else ("prop:plain" if style == "skull" else layer)
    shell.build(mb, material, metal, cap=style not in ("mask",), rim=rim if style not in ("hood", "mask") else None)
    eye = (EYE_T - t0) / (t1 - t0) * (shell.rings - 1)  # ring index at eye height
    if style in ("full", "horned", "skull"):  # a visor slit (or eye sockets) across the face
        for sx in ((-1, 1) if style == "skull" else (0,)):
            a0, a1 = (FRONT - 0.55, FRONT + 0.55) if style != "skull" else (FRONT + sx * 0.45 - 0.22, FRONT + sx * 0.45 + 0.22)
            pts = [tuple(shell.point(eye, a0 + (a1 - a0) * k / 6, 0.004)) for k in range(7)]
            mb.tube(pts, [0.012] * 7, "prop:plain:glow" if glow else "prop:plain", glow or dark, segments=4)
    if style == "space":  # a big glass visor
        for i in range(3):
            pts = [tuple(shell.point(eye - 0.9 + i * 0.7, FRONT - 0.8 + 1.6 * k / 8, 0.006)) for k in range(9)]
            mb.tube(pts, [0.02] * 9, "prop:plain:glow" if glow else "prop:crystal", glow or "#8ac8ff", segments=4)
    if style == "horned":
        for sx in (-1, 1):
            base = shell.point(shell.rings * 0.8, FRONT + sx * 1.2, -0.01)
            tip = base + Vector((sx * 0.12, 0.13, 0.05))
            mb.tube([tuple(base), tuple(base.lerp(tip, 0.5) + Vector((sx * 0.03, -0.01, 0))), tuple(tip)], [0.03, 0.018, 0.003],
                    "prop:plain" if not glow else "prop:plain", trim, segments=6)
    if p.get("crest"):
        ridge = [tuple(Vector((0, shell.ys[-1] + 0.02, 0)) + Vector((0, 0, z))) for z in (-0.11, -0.05, 0.0, 0.05, 0.1)]
        mb.tube(ridge, [0.012, 0.022, 0.026, 0.022, 0.012], layer, trim, segments=6)
    if p.get("lamp"):
        c = shell.point(shell.rings * 0.75, FRONT, 0.02)
        mb.cylinder(tuple(c - Vector((0, 0, 0.01))), tuple(c + Vector((0, 0, 0.03))), 0.03, 0.035, layer, dark, segments=12)
        mb.sphere(tuple(c + Vector((0, 0, 0.032))), 0.024, "prop:plain:glow", glow or "#fff2b0", subdivisions=1)
    return style not in ("mask",)


def _chest(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    cloud = fit.local(["chest", "spine", "clavicle.L", "clavicle.R"], bone)
    shell = Shell(cloud, length, -0.8, 0.92, rings=8, segments=18, clearance=0.022 if style != "cloth" else 0.012,
                  profile=lambda t: 1.0 - 0.18 * max(0.0, (t - 0.8) / 0.2))
    material = "prop:cloth" if style == "cloth" else ("prop:plain" if style == "ribs" else layer)
    colors = None
    if style == "chain":
        colors = lambda i, k: metal if (i + k) % 2 else _shade(metal, 0.72)
    elif style in ("carapace", "scale", "ribs"):
        colors = lambda i, k: trim if i % 2 else metal
    shell.build(mb, material, metal, ring_colors=colors, rim=rim)
    if style in ("plate", "carapace"):  # a raised ridge down the breastbone
        mb.tube([tuple(shell.point(i, FRONT, 0.006)) for i in range(1, shell.rings - 1)], [0.012] * (shell.rings - 2), layer, trim, segments=5)
    if style == "scale":
        for i in range(1, shell.rings - 1):
            for k in range(0, 18, 2):
                a = 2 * math.pi * (k + (i % 2)) / 18
                mb.sphere(tuple(shell.point(i, a, 0.004)), 0.022, layer, trim if (i + k) % 4 else metal, subdivisions=1, scale=(1, 0.6, 0.5))
    if glow and p.get("veins"):
        for a in (FRONT - 0.5, FRONT + 0.5):
            mb.tube([tuple(shell.point(i, a + 0.06 * (i % 2), 0.006)) for i in range(1, shell.rings - 1)], [0.007] * (shell.rings - 2),
                    "prop:plain:glow", glow, segments=4)
    return False


def _belt(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    cloud = fit.local(["hips", "spine"], bone)
    shell = Shell(cloud, length, -0.4, 0.9, rings=3, segments=18, clearance=0.03)
    shell.build(mb, "prop:leather" if style in ("cloth", "chain") else layer, trim if style != "cloth" else _shade(metal, 0.8), rim=None)
    c = shell.point(1.5, FRONT, 0.008)
    mb.box(tuple(c - Vector((0.03, 0.025, 0.006))), tuple(c + Vector((0.03, 0.025, 0.012))), "prop:gold" if p.get("trimmed") else layer, trim)
    return False


def _pauldron(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    side = bone.split(".")[1]
    cloud = fit.local([bone, f"clavicle.{side}"], bone)
    big = {"plate": 1.3, "small": 1.12, "spiky": 1.3, "round": 1.45}[style]
    shell = Shell(cloud, length, -0.12, 0.38, rings=4, segments=12, clearance=0.02,
                  profile=lambda t: big - (big - 1.0) * t)
    shell.build(mb, layer, metal, cap=False, rim=trim if style in ("plate", "round") else None)
    if style == "spiky":
        for k in range(3):
            a = math.pi * (0.25 + k * 0.25) + (0 if side == "R" else math.pi)
            base = shell.point(1.0, a, -0.005)
            out = (base - Vector((0, base.y, 0))).normalized()
            mb.cylinder(tuple(base), tuple(base + out * 0.08 + Vector((0, -0.03, 0))), 0.02, 0.0, layer, trim, segments=6)
    return False


def _bracer(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    cloud = fit.local([bone], bone)
    shell = Shell(cloud, length, 0.35, 0.97, rings=4, segments=10, clearance=0.015,
                  profile=lambda t: 1.0 + 0.12 * t)
    shell.build(mb, "prop:leather" if style == "wrap" else layer, metal if style != "wrap" else _shade(metal, 0.8),
                rim=trim if style == "plate" else None)
    return False


def _legs(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p, t0, t1):
    cloud = fit.local([bone], bone)
    shell = Shell(cloud, length, t0, t1, rings=5, segments=12, clearance=0.016 if style != "cloth" else 0.01)
    material = "prop:cloth" if style == "cloth" else layer
    colors = None
    if style == "chain":
        colors = lambda i, k: metal if (i + k) % 2 else _shade(metal, 0.72)
    elif style == "carapace":
        colors = lambda i, k: trim if i % 2 else metal
    shell.build(mb, material, metal, ring_colors=colors, rim=trim if style == "plate" else None)
    return shell


def _cuisse(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    _legs(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p, 0.04, 0.9)
    return False


def _greave(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    shell = _legs(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p, 0.02, 0.9)
    if style == "plate":  # a knee cop
        mb.sphere(tuple(shell.point(0.15, FRONT, 0.005)), 0.05, layer, trim, subdivisions=1, scale=(1, 0.8, 0.55))
    if glow and p.get("veins"):
        mb.tube([tuple(shell.point(i, FRONT + 0.3, 0.006)) for i in range(1, 5)], [0.006] * 4, "prop:plain:glow", glow, segments=4)
    return False


def _boot(mb, fit, bone, length, style, layer, metal, trim, dark, glow, rim, p):
    side = bone.split(".")[1]
    cloud = fit.local([bone, f"toe.{side}"], bone, min_weight=0.5)
    shell = Shell(cloud, length, -0.55, 1.15, rings=6, segments=12, clearance=0.014)
    shell.build(mb, "prop:leather" if style == "leather" else layer, metal if style != "leather" else _shade(metal, 0.85),
                cap=True, rim=trim if style == "plate" else None)
    return False


def _shade(hexv, k):
    h = hexv.lstrip("#")
    return "#" + "".join(f"{min(255, int(int(h[i:i + 2], 16) * k)):02x}" for i in (0, 2, 4))
