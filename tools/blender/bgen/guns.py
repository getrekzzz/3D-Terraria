"""Firearms (P3-38): side profiles that read in an inventory slot. The profile lies in the item's YZ plane (+Z the
barrel, +Y the top of the gun, the grip hangs toward -Y), with depth along X; icons view it side-on ('side' view).
Styles: pistol, musket, rifle, shotgun, minigun, launcher, raygun. Silhouette params: ``shark`` (minishark and
megashark: dorsal fin, jaw and eye), ``star`` (a star at the muzzle), ``scope``, ``double`` (twin barrels),
``flintlock`` (hammer and pan), ``drum`` (a round magazine); colours ``metal``, ``wood``, ``accent`` (+ ``glow``)."""
import math

from mathutils import Matrix, Vector

from .mesh import MeshBuilder

# Profile polygons are given as (up, along) points: rotate the XZ-plane polygon onto the YZ plane.
_YZ = Matrix.Rotation(math.pi / 2, 4, "Z")


def _metal(p, default):
    return f"prop:{p.get('metalLayer', 'iron')}" + (":metal" if p.get("metalLayer", "iron") == "iron" else ""), p.get("metal", default)


def _profile(mb, points, depth, material, color, x=0.0):
    return mb.extrude_polygon(points, depth, material, color, matrix=Matrix.Translation((x, 0, 0)) @ _YZ)


def _barrel(mb, u, v0, v1, r, material, color, segments=10):
    mb.cylinder((0, u, v0), (0, u, v1), r, None, material, color, segments=segments)


def gun(mb: MeshBuilder, p, rng):
    style = p.get("style", "pistol")
    layer, tint = _metal(p, "#6a6e78")
    wood = p.get("wood", "#7a4a2a")
    dark = "#2a2c32"
    glow = ":glow" if p.get("glow") else ""
    accent = p.get("accent")
    length = {"pistol": 0.3, "musket": 0.95, "rifle": 0.85, "shotgun": 0.75, "minigun": 0.8, "launcher": 0.8,
              "raygun": 0.34}.get(style, 0.5)
    length = p.get("length", length)
    bore = 0.045
    if style in ("pistol", "raygun"):
        _profile(mb, [(0.012, -0.05), (0.012, 0.0), (-0.10, -0.035), (-0.115, -0.085), (-0.095, -0.1)], 0.045,
                 "prop:dark_planks" if style == "pistol" else layer, wood if style == "pistol" else tint)  # grip
        _profile(mb, [(0.0, -0.07), (0.07, -0.07), (0.07, 0.16), (0.0, 0.16)], 0.05, layer, tint)  # slide
        if style == "raygun":
            mb.sphere((0, 0.04, -0.07), 0.055, "prop:plain" + glow, accent or "#4aff6a", subdivisions=2)
            for v in (0.18, 0.23, 0.28):
                _barrel(mb, bore, v, v + 0.018, 0.034, layer, accent or tint, segments=12)
            mb.cylinder((0, bore, length - 0.03), (0, bore, length + 0.02), 0.02, 0.045, layer, tint, segments=12)
        else:
            _barrel(mb, bore, 0.16, length, 0.016, layer, tint)
        mb.box((-0.006, 0.07, length - 0.03), (0.006, 0.088, length - 0.012), layer, tint)  # front sight
        mb.tube([Vector((0, 0.0, -0.01)), Vector((0, -0.035, 0.0)), Vector((0, -0.035, 0.05)), Vector((0, 0.0, 0.06))],
                [0.006] * 4, layer, dark, segments=5)  # trigger guard
        if p.get("flintlock"):
            mb.box((-0.01, 0.06, -0.09), (0.01, 0.1, -0.06), layer, dark)  # hammer
    elif style in ("musket", "rifle", "shotgun"):
        stock = [(0.05, -0.36), (0.06, 0.0), (0.0, 0.14), (-0.03, 0.12), (-0.035, 0.0), (-0.1, -0.33), (-0.08, -0.38)]
        _profile(mb, stock, 0.05, "prop:planks", wood)
        _profile(mb, [(0.0, -0.02), (0.07, -0.02), (0.07, 0.2), (0.0, 0.2)], 0.055, layer, tint)  # receiver
        r = 0.022 if style == "shotgun" else 0.015
        _barrel(mb, bore, 0.2, length, r, layer, tint)
        if p.get("double"):
            _barrel(mb, bore + 2.1 * r, 0.2, length, r, layer, tint)
        if style == "shotgun":
            _profile(mb, [(0.0, 0.32), (0.035, 0.32), (0.035, 0.5), (0.0, 0.5)], 0.06, "prop:planks", wood)  # pump
        else:
            _profile(mb, [(-0.005, 0.2), (0.03, 0.2), (0.03, length - 0.08), (-0.005, length - 0.1)], 0.045, "prop:planks", wood)  # forend
        if style == "rifle":
            _profile(mb, [(0.0, 0.05), (0.0, 0.14), (-0.12, 0.12), (-0.12, 0.07)], 0.04, layer, dark)  # magazine
        if p.get("flintlock"):
            mb.box((-0.01, 0.06, -0.04), (0.01, 0.11, -0.01), layer, dark)
        if p.get("scope"):
            mb.cylinder((0, 0.11, 0.0), (0, 0.11, 0.26), 0.026, None, layer, dark, segments=12)
            for v in (0.02, 0.24):
                mb.cylinder((0, 0.11, v - 0.02), (0, 0.11, v + 0.02), 0.034, None, layer, dark, segments=12)
    elif style == "minigun":
        _profile(mb, [(0.012, -0.1), (0.012, -0.05), (-0.1, -0.08), (-0.11, -0.13), (-0.09, -0.15)], 0.05,
                 "prop:dark_planks", wood)  # rear grip
        mb.cylinder((0, bore, -0.16), (0, bore, 0.16), 0.06, 0.055, layer, tint, segments=14)  # body
        for k in range(6):
            a = k * math.pi / 3
            off = (math.cos(a) * 0.024, bore + math.sin(a) * 0.024)
            mb.cylinder((off[0], off[1], 0.16), (off[0], off[1], length), 0.0095, None, layer, tint, segments=6)
        for v in (0.3, length - 0.04):
            _barrel(mb, bore, v, v + 0.03, 0.04, layer, dark, segments=12)
        mb.box((-0.012, 0.11, -0.1), (0.012, 0.125, 0.1), layer, dark)  # carry handle
        for v in (-0.08, 0.08):
            mb.box((-0.01, 0.09, v - 0.01), (0.01, 0.12, v + 0.01), layer, dark)
        if p.get("drum", True) and not p.get("shark"):
            mb.cylinder((-0.03, -0.04, 0.02), (0.03, -0.04, 0.02), 0.06, None, layer, dark, segments=14)  # drum
    elif style == "launcher":
        mb.cylinder((0, 0.06, -0.24), (0, 0.06, length), 0.065, 0.07, layer, tint, segments=14)
        mb.cylinder((0, 0.06, length - 0.05), (0, 0.06, length + 0.02), 0.08, 0.085, layer, dark, segments=14)
        mb.cylinder((0, 0.06, -0.28), (0, 0.06, -0.22), 0.075, 0.068, layer, dark, segments=14)
        _profile(mb, [(0.0, -0.02), (0.0, 0.04), (-0.12, 0.01), (-0.12, -0.04)], 0.045, "prop:dark_planks", wood)  # grip
        _profile(mb, [(0.0, 0.18), (0.0, 0.24), (-0.1, 0.22), (-0.1, 0.18)], 0.04, "prop:dark_planks", wood)  # fore grip
        mb.box((-0.01, 0.125, 0.0), (0.01, 0.16, 0.06), layer, dark)  # sight
    if p.get("shark"):
        fin = p.get("fin", tint)
        _profile(mb, [(0.08, -0.08), (0.24, -0.02), (0.2, 0.04), (0.08, 0.16)], 0.025, layer, fin)  # dorsal fin
        _profile(mb, [(0.05, -0.15), (0.17, -0.27), (0.1, -0.16), (-0.08, -0.25), (0.0, -0.13)], 0.025, layer, fin)  # tail
        _profile(mb, [(0.0, length - 0.12), (0.1, length - 0.05), (0.09, length + 0.03), (-0.0, length + 0.04),
                      (-0.05, length - 0.02)], 0.09, layer, fin)  # snout over the muzzle
        for v in (length - 0.03, length, length + 0.025):
            mb.box((-0.046, 0.0, v), (0.046, 0.022, v + 0.012), "prop:plain", "#f2f2ea")  # teeth
        for sx in (-1, 1):
            mb.sphere((sx * 0.046, 0.06, length - 0.06), 0.011, "prop:plain", "#101010", subdivisions=1)
    if p.get("star"):
        pts = []
        for k in range(10):
            a = math.pi / 2 + k * math.pi / 5
            r = 0.11 if k % 2 == 0 else 0.045
            pts.append((0.06 + math.sin(a) * r, length + 0.08 + math.cos(a) * r))
        _profile(mb, pts, 0.03, "prop:plain" + glow, accent or "#ffe860")
    elif accent and not p.get("shark") and style != "raygun":
        top = 0.135 if style == "launcher" else 0.07
        _profile(mb, [(top, 0.0), (top + 0.014, 0.0), (top + 0.014, length * 0.55), (top, length * 0.55)], 0.03,
                 "prop:plain" + glow, accent)  # accent strip along the top
    mb.socket("muzzle", (0, 0.06 if style == "launcher" else bore, length + (0.08 if p.get("star") else 0.0)),
              (0, 0, 1), (0, 1, 0))
