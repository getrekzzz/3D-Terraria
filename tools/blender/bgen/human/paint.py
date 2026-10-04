"""Vertex colour painting: skin tone with anatomical variation (lips, flushed cheeks, nose and ears, darker lids,
painted brows, stubble shadow, redder knuckles, elbows and knees) and flat colours for other parts.

Colours are written as sRGB (``color_srgb``), matching the bytes bgen.mesh.MeshBuilder stores through bmesh, so the
glTF export (linear COLOR_0) and the game shader treat every model the same way.
"""
import math

import bpy
import numpy as np
from mathutils import Vector

from . import monsterface
from .monsterface import EYELESS


def hex3(value: str):
    h = value.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def gauss(d: float, r: float) -> float:
    return math.exp(-(d / r) ** 2)


def _ensure_layer(mesh):
    if "Color" not in mesh.color_attributes:
        mesh.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    layer = mesh.color_attributes["Color"]
    mesh.color_attributes.active_color = layer
    mesh.color_attributes.render_color_index = mesh.color_attributes.find("Color")
    return layer


def paint(obj: bpy.types.Object, fn):
    """Colours every face corner with fn(vertex_position) -> (r, g, b)."""
    mesh = obj.data
    layer = _ensure_layer(mesh)
    cache = {}
    for loop in mesh.loops:
        vi = loop.vertex_index
        if vi not in cache:
            cache[vi] = fn(mesh.vertices[vi].co)
        r, g, b = cache[vi]
        layer.data[loop.index].color_srgb = (min(r, 1.0), min(g, 1.0), min(b, 1.0), 1.0)


def flat(obj: bpy.types.Object, color: str):
    c = hex3(color)
    paint(obj, lambda co: c)


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def mul(a, b):
    return tuple(x * y for x, y in zip(a, b))


class SkinPainter:
    def __init__(self, frame, head, p):
        self.frame = frame
        self.head = head
        self.p = p
        self.skin = hex3(p.get("skin", "#e0b090"))
        self.hair = hex3(p.get("hair", "#4a3020"))
        self.stubble = not frame.female and "beard" not in p and p.get("stubble", True) and not p.get("undead")
        from .beard import STYLES as BEARDS
        self.beard = (hex3(p["beard"]), BEARDS[p.get("beardStyle", "full")]) if "beard" in p else None
        self.undead = bool(p.get("undead"))
        # Skin under the hair takes the hair colour, so the scalp never reads as a bald patch where the hair shell
        # thins (distance LOD simplification, extreme head poses).
        style = p.get("hairStyle", "long" if frame.female else "short")
        self.scalp = style != "bald" and p.get("hat") != "hood"
        from .locks import STYLES
        self.part = STYLES[style].sweep if style in STYLES else 0.0
        self.sideburns = not (frame.female or style in ("ponytail", "bun"))

    def body(self, co: Vector):
        j = self.frame.j
        c = self.skin
        red = 0.0
        for side in ("R", "L"):
            red += 0.5 * gauss((co - j[f"elbow.{side}"]).length, 0.04)
            red += 0.45 * gauss((co - j[f"knee.{side}"] - Vector((0, 0.04, 0))).length, 0.05)
        c = mix(c, mul(c, (1.02, 0.84, 0.82)), min(red, 1.0))
        return c

    def face(self, co: Vector):
        from .head import smooth
        q = self.head.local(co)
        c = self.skin
        # Lips: the sculpted lips' own surface, redder and a little darker, with a crisp vermilion border.
        lip_d = float(self.head.shape.lips(np.asarray([q], dtype=np.float32))[0]) if q.y > 0.075 and q.z < -0.05 else 1.0
        lip = 1.0 - smooth(0.0002, 0.001, lip_d)
        tone = (0.9, 0.62, 0.64) if self.frame.female else (0.92, 0.72, 0.72)
        c = mix(c, mul(c, tone), lip * (0.75 if self.frame.female else 0.6))
        blush = sum(gauss((q - Vector((sx * 0.042, 0.07, -0.03))).length, 0.022) for sx in (1, -1))
        blush += 0.7 * gauss((q - Vector((0, 0.11, -0.04))).length, 0.012)
        blush += 0.6 * (gauss(abs(q.x) - 0.075, 0.012) if abs(q.x) > 0.06 else 0.0)
        c = mix(c, mul(c, (1.0, 0.88, 0.87)), min(blush, 1.0) * 0.4)
        for sx in (1, -1) if self.p.get("face") not in EYELESS else ():
            e = self.head.local(self.head.eyes[sx])
            d = q - e
            lid = gauss(math.hypot(d.x, d.z), 0.016) * (1.0 if d.y > -0.004 else 0.0)
            c = mix(c, mul(c, (0.86, 0.8, 0.82)), lid * (0.85 if self.undead else 0.5))
            if self.undead:
                c = mix(c, mul(c, (0.45, 0.4, 0.42)), gauss(math.hypot(d.x, d.z), 0.02) * 0.7)   # sunken, bruised sockets
            # Painted brow (strands are added on top).
            x = d.x * sx
            arch = 0.0165 + 0.0035 * math.sin((x + 0.012) / 0.03 * math.pi)
            if -0.018 < x < 0.024 and q.y > 0.06:
                brow = gauss((d.z - arch) / 0.0042, 1.0) * (1.0 - max(0.0, (x - 0.014) / 0.012))
                c = mix(c, self.hair, max(0.0, min(brow, 1.0)) * 0.7)
        if self.stubble or self.beard:
            from .beard import STYLES, coverages
            qa = np.asarray([q], dtype=np.float32)
        if self.stubble:
            # A shaved beard's shadow: cool, over where a beard grows, fading out at its edges.
            shadow = float(coverages(qa, STYLES["short"])[0]) * (1.0 - lip)
            c = mix(c, mul(c, (0.74, 0.76, 0.8)), shadow * 0.4)
        if self.beard:
            # The beard's roots on the skin under it, thinning out with it at its edges.
            color, style = self.beard
            roots = min(1.0, float(coverages(qa, style)[0]) * 1.3) * (1.0 - lip)
            c = mix(c, mul(color, (0.72, 0.72, 0.72)), roots * 0.85)
        if self.scalp:
            # Hair roots under the hair, thinning out softly at the hairline (the locks cover the edge).
            from .hair import coverage
            c = mix(c, mul(self.hair, (0.82, 0.82, 0.82)), coverage(q, self.part, self.sideburns, 0.008) * 0.9)
        if self.p.get("face"):          # a monster's sockets, scars, veins and gums
            c = monsterface.tint(self.head, self.p["face"], q, c)
        return c
