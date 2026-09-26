"""Vertex colour painting: skin tone with anatomical variation (lips, flushed cheeks, nose and ears, darker lids,
painted brows, stubble shadow, redder knuckles, elbows and knees) and flat colours for other parts.

Colours are written as sRGB (``color_srgb``), matching the bytes bgen.mesh.MeshBuilder stores through bmesh, so the
glTF export (linear COLOR_0) and the game shader treat every model the same way.
"""
import math

import bpy
from mathutils import Vector


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
        self.undead = bool(p.get("undead"))

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
        q = self.head.local(co)
        c = self.skin
        # Lips: redder and a little darker, sharpest at the vermilion border.
        lip = gauss(q.x / 0.024, 1.0) ** 3 * gauss((q.z + 0.07) / 0.0105, 1.0) ** 2 * (1.0 if q.y > 0.078 else 0.0)
        c = mix(c, mul(c, (0.93, 0.6, 0.6)), min(lip * 1.3, 0.85))
        blush = sum(gauss((q - Vector((sx * 0.042, 0.07, -0.03))).length, 0.022) for sx in (1, -1))
        blush += 0.7 * gauss((q - Vector((0, 0.11, -0.04))).length, 0.012)
        blush += 0.6 * (gauss(abs(q.x) - 0.075, 0.012) if abs(q.x) > 0.06 else 0.0)
        c = mix(c, mul(c, (1.0, 0.86, 0.85)), min(blush, 1.0) * 0.55)
        for sx in (1, -1):
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
        if self.stubble and q.y > 0.0:
            jaw = (1.0 if q.z < -0.052 else gauss((q.z + 0.052) / 0.01, 1.0)) * (1.0 if q.z > -0.13 else 0.0)
            upper = gauss(q.x / 0.022, 1.0) * gauss((q.z + 0.057) / 0.005, 1.0)
            amount = max(jaw * (1.0 - lip), upper) * 0.22
            c = mix(c, (0.25, 0.22, 0.2), amount)
        return c
