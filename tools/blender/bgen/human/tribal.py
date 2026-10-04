"""A jungle shaman's outfit (the Witch Doctor's), each part asked for by its own parameter:

- tribalMask (wood colour): a carved, painted wooden mask over the face (tribalmask.py);
- headdress (band colour): a headband round the skull with a crown of long feathers standing up and fanning out
  behind, red, yellow and green with dark tips;
- beads (colour of the bone pieces): strands of beads hanging on the bare chest, the lowest carrying fangs;
- grassSkirt (colour): two layers of long leaves hanging from under the belt to above the knee;
- bodyPaint (colour): chevrons across the chest and back, bands round the arms and stripes down the shins, painted on
  the bare skin.

They go with ``shirt: none`` (and usually ``pants: none``) for bare skin. The skirt swings with the thighs, the beads
follow the chest under them and the mask and headdress are carried by the head.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .features import blade, paint, sweep
from .garments import mesh_from, set_slot
from .paint import hex3
from .rig import rigid, write_weights

PARAMS = ("tribalMask", "headdress", "beads", "grassSkirt", "bodyPaint")


def wear(fr, head, parts, p, w, lookup=None):
    """The outfit's parts asked for by ``p`` (none if it asks for none), finished and weighted."""
    out = []
    if "bodyPaint" in p:
        body_paint(fr, parts["body"], hex3(p["bodyPaint"]), w)
    if "grassSkirt" in p:
        out.append(grass_skirt(fr, parts["body"], hex3(p["grassSkirt"]), w))
    if "beads" in p:
        out.append(beads(fr, parts["body"], hex3(p["beads"]), w, lookup))
    if "headdress" in p:
        out.append(headdress(head, hex3(p["headdress"])))
    if "tribalMask" in p:
        from . import tribalmask
        mask = tribalmask.mesh(head)
        wood = hex3(p["tribalMask"])
        bm = bmesh.new()
        bm.from_mesh(mask.data)
        layer = bm.loops.layers.color.new("Color")
        for f in bm.faces:
            for loop in f.loops:
                loop[layer] = (*tribalmask.colour(tuple(head.local(loop.vert.co)), wood), 1.0)
        bm.to_mesh(mask.data)
        bm.free()
        set_slot(mask, "char:leather")
        rigid(mask, "head")
        out.append(mask)
    return out


def body_paint(fr, body, color, w):
    """Chevrons on the chest and back, bands round the upper arms and forearms, stripes down the shins."""
    s, j = fr.s, fr.j
    me = body.data
    layer = me.color_attributes["Color"]
    hit = np.zeros(len(me.vertices))
    for v in me.vertices:
        co = v.co
        k = 0.0
        for side in ("R", "L"):
            t = w.arm_t(co, side)
            if t is not None:
                k = max(k, 1.0 if (0.2 < t < 0.27 or 0.31 < t < 0.35 or 0.72 < t < 0.77) else 0.0)
        if k == 0.0 and abs(co.x) < 0.17 * s and j["spine"].z < co.z < j["neck"].z:
            for drop in (0.0, 0.045):         # two chevrons, pointing down at the breastbone and the spine
                line = j["chest"].z + (0.13 - drop) * s - 0.55 * abs(co.x)
                if abs(co.z - line) < 0.011 * s:
                    k = 1.0
        if k == 0.0 and j["ankle.R"].z + 0.18 * s < co.z < j["knee.R"].z - 0.04 * s and co.y > j["knee.R"].y:
            for sx in (1, -1):
                if abs(co.x - sx * (abs(j["knee.R"].x) + 0.012 * s)) < 0.006 * s:
                    k = 1.0
        hit[v.index] = k
    for loop in me.loops:
        if hit[loop.vertex_index]:
            c = layer.data[loop.index].color_srgb
            layer.data[loop.index].color_srgb = (color[0] * 0.9 + c[0] * 0.1, color[1] * 0.9 + c[1] * 0.1,
                                                 color[2] * 0.9 + c[2] * 0.1, 1.0)


def grass_skirt(fr, body, color, w):
    """Two overlapping layers of long leaves round the hips, hanging from under the belt to above the knee and
    leaning out just enough to clear the hips and thighs."""
    s, j = fr.s, fr.j
    top = w.waist + 0.004 * s
    bottom = j["knee.R"].z + 0.07 * s
    zs = np.linspace(top, bottom, 7)
    segs = 72
    clear = np.stack([w.radial([body], z, 0.03 * s, segs) for z in zs])      # (height, angle)
    cy = j["spine"].y
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    straw = tuple(c * 0.55 + q * 0.45 for c, q in zip(color, (0.72, 0.62, 0.32)))
    for layer_i, (count, out0) in enumerate(((30, 0.012), (30, 0.02))):
        for k in range(count):
            a = 2 * math.pi * (k + 0.5 * layer_i) / count
            ai = int(round(a / (2 * math.pi) * segs)) % segs
            r0 = clear[0, ai] + out0 * s
            # lean out so the straight leaf clears every height below its root
            lean = max(((clear[i, ai] + out0 * s) - r0) / (top - zs[i]) for i in range(1, len(zs)))
            length = (top - bottom) * (0.9 + 0.12 * math.sin(k * 2.1 + layer_i))
            r1 = r0 + lean * length + 0.012 * s
            root = Vector((math.sin(a) * r0, cy + math.cos(a) * r0, top))
            tip = Vector((math.sin(a) * r1, cy + math.cos(a) * r1, top - length))
            outward = Vector((math.sin(a), math.cos(a), 0))
            tone = straw if (k + layer_i) % 3 == 0 else color
            dark = 0.7 + 0.15 * layer_i

            def shade(u, v, under, tone=tone, dark=dark):
                k_ = (dark + (1 - dark) * min(1.0, u * 2.5)) * (1.1 if abs(v) < 0.12 else 1.0) * (0.85 if under else 1.0)
                return tuple(min(1.0, c * k_) for c in tone)
            blade(bm, layer, root, tip - root, outward, length, 0.055 * s, shade, fold=0.25, curl=0.02,
                  thick=0.002 * s, rows=7, round_tip=False)
    obj = mesh_from(bm, "skirt_grass")
    set_slot(obj, "char:cloth")
    write_weights(obj, [w.skirt_weights(v.co, bottom) for v in obj.data.vertices])
    return obj


def beads(fr, body, bone, w, lookup):
    """Three strands of beads lying on the chest, longer each, the lowest hung with fangs at the front."""
    import bpy
    s, j = fr.s, fr.j
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    palette = [bone, (0.62, 0.12, 0.08), (0.12, 0.45, 0.42), (0.08, 0.06, 0.05)]
    neck = j["neck"]
    for n, (drop, size) in enumerate(((0.05, 0.0065), (0.1, 0.0075), (0.155, 0.0085))):
        ring = []
        for k in range(64):
            ph = 2 * math.pi * k / 64                       # 0 at the front
            front = 0.5 + 0.5 * math.cos(ph)
            z = w.collar_z - (0.02 + drop * front ** 1.6) * s
            q = Vector((math.sin(ph) * 0.3 * s, neck.y + math.cos(ph) * 0.3 * s, z))
            hit, nrm, _, _ = tree.ray_cast(q, (Vector((neck.x, neck.y, z)) - q).normalized(), 0.4 * s)
            if hit is not None:
                ring.append(hit + nrm * size * s)
        if len(ring) < 8:
            continue
        for c in _even(ring, size * 2.1 * s):
            res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size * s)
            bmesh.ops.translate(bm, vec=c, verts=res["verts"])
            col = palette[(len(bm.verts) // 42 + n) % len(palette)]
            for f in {f for v in res["verts"] for f in v.link_faces}:
                for loop in f.loops:
                    loop[layer] = (*col, 1.0)
        if n == 2:                                          # fangs hanging from the front of the lowest strand
            front = max(ring, key=lambda v: v.y)
            for f_i in range(-3, 4):
                q = front + Vector((f_i * 0.022 * s, 0.1 * s, 0))
                hit, nrm, _, _ = tree.ray_cast(q, Vector((0, -1, 0)), 0.3 * s)
                if hit is None:
                    continue
                root = hit + nrm * size * s
                length = (0.045 - 0.008 * abs(f_i)) * s
                path = [root, root - Vector((0, 0, length * 0.5)) + nrm * 0.004 * s,
                        root - Vector((0, 0, length)) + nrm * 0.002 * s + Vector((f_i * 0.002 * s, 0, 0))]
                vs = sweep(bm, path, lambda t, a: (math.cos(a) * 0.0055 * s * (1 - t) ** 0.8, math.sin(a) * 0.0042 * s * (1 - t) ** 0.8), 8)
                paint(bm, vs, lambda t, a: tuple(c * (0.75 + 0.25 * t) for c in bone))
    obj = mesh_from(bm, "beads")
    set_slot(obj, "char:bone")
    if lookup is not None:
        ktree, body_w = lookup
        weights = []
        for v in obj.data.vertices:
            _, idx, _ = ktree.find(v.co)
            weights.append({b: x for b, x in body_w[idx].items() if b.split(".")[0] not in ("upper_arm", "forearm", "hand")} or {"chest": 1.0})
        write_weights(obj, weights)
    else:
        rigid(obj, "chest")
    return obj


def _even(ring, step):
    """Points every ``step`` along a closed polyline."""
    pts = ring + ring[:1]
    out, carry = [], 0.0
    for a, b in zip(pts, pts[1:]):
        seg = (b - a).length
        t = carry
        while t < seg:
            out.append(a.lerp(b, t / seg))
            t += step
        carry = t - seg
    return out


def headdress(head, band):
    """A band round the skull above the brow and a crown of feathers rising from it, longest at the back."""
    shape = head.shape
    bm = bmesh.new()
    layer = bm.loops.layers.color.new("Color")
    c0 = np.asarray((0.0, -0.012, 0.055))
    ring = []
    for k in range(49):
        ph = 2 * math.pi * k / 48
        d = np.asarray((math.sin(ph), math.cos(ph), 0.12 - 0.1 * math.cos(ph)))
        d = d / np.linalg.norm(d)
        lo, hi = 0.0, 0.2
        for _ in range(22):
            mid = (lo + hi) / 2
            q = (c0 + d * mid).astype(np.float32)[None]
            lo, hi = (mid, hi) if shape.field(q, ears=False, face=False)[0] < 0 else (lo, mid)
        ring.append(Vector((c0 + d * (lo + 0.006)).tolist()))
    path = [head.at(q[:]) for q in ring]
    h = head.h
    vs = sweep(bm, path, lambda t, a: (math.cos(a) * 0.014 * h, math.sin(a) * 0.0045 * h), 8, up=Vector((0, 0, 1)), caps=(False, False))
    paint(bm, vs, lambda t, a: tuple(c * (0.85 + 0.15 * (math.sin(t * 2 * math.pi * 24) > 0)) for c in band))
    colours = [(0.7, 0.13, 0.08), (0.88, 0.68, 0.16), (0.22, 0.48, 0.18), (0.7, 0.13, 0.08), (0.14, 0.3, 0.58), (0.9, 0.86, 0.76)]
    count = 21
    for k in range(count):
        ph = math.radians(-125 + 250 * k / (count - 1)) + math.pi       # round the back, from ear to ear
        i = int(round((ph % (2 * math.pi)) / (2 * math.pi) * 48)) % 48
        root = ring[i]
        back = abs(math.cos((ph - math.pi) / 2.0))
        length = (0.14 + 0.12 * back ** 2) * h * (0.86 if k % 2 else 1.0)
        outward = Vector((math.sin(ph), math.cos(ph), 0))
        d = (Vector((0, 0, 1)) * (1.0 - 0.25 * (1 - back)) + outward * (0.35 + 0.25 * (1 - back))).normalized()
        col = colours[k % len(colours)]
        tip = (0.08, 0.06, 0.05)

        def tone(u, v, under, col=col, tip=tip):
            if abs(v) < 0.1:
                return (0.85, 0.8, 0.68)                          # the pale quill down the middle
            m = max(0.0, (u - 0.8) / 0.2) if u < 0.97 else 0.0    # a dark band near the tip, a pale tip
            c = tuple(x + (t - x) * m for x, t in zip(col, tip))
            bar = 0.8 if 0.5 < u < 0.56 else 1.0                  # a barred stripe across
            return tuple(x * bar * (0.8 if under else 1.0) for x in c)
        blade(bm, layer, head.at(root[:]) - d * 0.01 * h, d, outward, length, 0.031 * h, tone, fold=0.22, curl=-0.08,
              thick=0.0018 * h, rows=9, round_tip=True, skew=0.3)
    obj = mesh_from(bm, "hat_headdress")
    set_slot(obj, "char:cloth")
    rigid(obj, "head")
    return obj
