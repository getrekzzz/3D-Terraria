"""More pre-hardmode bosses in the reference game's likeness: the Eater of Worlds (a chitin-plated worm with a
mandibled head that chomps as it goes), the Queen Bee (striped and fuzzy, compound eyes, four buzzing wings, a stinger
it curls forward to attack) and Skeletron (a cracked skull with a hanging jaw, big skeletal hands and the arm bones
the game stretches between them). Worm pieces are modelled along +Y (travel direction) about their centre; free
parts (hands) about their centre too. Front faces Blender +Y.
"""
import math

from mathutils import Vector

from .detail import fang, mix, noise3, organic, teeth_row
from .human.rig import Poser, keyframe_action
from .mesh import MeshBuilder

UP, FWD, SIDE = (0, 0, 1), (0, 1, 0), (1, 0, 0)


def clips(specs):
    """A rig description for creatures.finish: ``specs`` = (bones, {clip: (seconds, fn(t) -> {bone: [(axis, angle)]})}),
    rotations in armature space about each bone's head, sampled every frame so loops are seamless."""
    bones, actions = specs

    def act(arm):
        poser = Poser(arm)
        for name, (length, fn) in actions.items():
            n = max(2, round(length * 30))
            keys = {i / n: {b: [("axis", ax, ang) for ax, ang in ops] for b, ops in fn(i / n).items()} for i in range(n + 1)}
            used = {b for pose in keys.values() for b in pose}
            for pose in keys.values():
                for b in used:
                    pose.setdefault(b, [])
            keyframe_action(arm, poser, name, n / 30, keys, fps=30)
    return {"rig": bones, "actions": act}


def wave(t, cycles=1.0, phase=0.0):
    return math.sin(2 * math.pi * (t * cycles + phase))


# ---------------------------------------------------------------------------------------------------- Eater of Worlds
SHELL, SHELL_DARK, FLESH_EOW, MANDIBLE = "#6a5a86", "#3a2e4e", "#8a4a6a", "#b8a078"


def _plates(mb, y0, y1, r, rng, spikes=3):
    """Overlapping chitin rings along Y, each with a raised rim and spikes along the back."""
    rings = 3
    for k in range(rings):
        a, b = y0 + (y1 - y0) * k / rings, y0 + (y1 - y0) * (k + 1.15) / rings
        prof = [(r * 0.86, a), (r * 1.02, a + (b - a) * 0.15), (r * 1.0, b - (b - a) * 0.25), (r * 0.93, b)]
        from mathutils import Matrix
        v = mb.lathe(prof, "prop:stone", SHELL, segments=14, matrix=Matrix.Rotation(-math.pi / 2, 4, "X"))
        _shade(mb, v, lambda q, n: mix(SHELL_DARK, SHELL, 0.5 + 0.5 * n.z + 0.15 * noise3(q, 6)))
    for k in range(spikes):
        a = math.pi / 2 + (k - (spikes - 1) / 2) * 0.55
        base = Vector((math.cos(a) * r * 0.95, (y0 + y1) / 2 + rng.uniform(-0.1, 0.1), math.sin(a) * r * 0.95))
        mb.cylinder(base, base + Vector((math.cos(a) * 0.3, -0.18, math.sin(a) * 0.3)) * r, r * 0.12, 0.0, "prop:plain", "#d8d0c0", segments=5, smooth=False)


def _shade(mb, verts, fn):
    faces = {f for v in verts for f in v.link_faces}
    mb.bm.normal_update()
    for f in faces:
        for loop in f.loops:
            loop[mb.color_layer] = fn(loop.vert.co.copy(), loop.vert.normal.copy())


def eater_segment(mb: MeshBuilder, p, rng):
    """One body ring: a purple flesh core showing between ridged chitin plates, spines along the back."""
    r, length = p.get("radius", 0.72), p.get("length", 1.15)
    mb.cylinder((0, -length * 0.52, 0), (0, length * 0.52, 0), r * 0.8, r * 0.8, "prop:flesh", FLESH_EOW, segments=12)
    _plates(mb, -length * 0.45, length * 0.45, r, rng)
    for sx in (-1, 1):  # little side legs / bristles
        base = Vector((sx * r * 0.85, 0, -r * 0.35))
        mb.tube([base, base + Vector((sx * r * 0.35, -0.05, -r * 0.3)), base + Vector((sx * r * 0.4, -0.2, -r * 0.55))], [r * 0.07, r * 0.05, r * 0.01], "prop:plain", SHELL_DARK, segments=4)


def eater_tail(mb: MeshBuilder, p, rng):
    r, length = p.get("radius", 0.72), p.get("length", 1.15)
    from mathutils import Matrix
    prof = [(0.0, -length * 1.1), (r * 0.3, -length * 0.8), (r * 0.6, -length * 0.3), (r * 0.82, length * 0.2), (r * 0.9, length * 0.5)]
    v = mb.lathe(prof, "prop:stone", SHELL, segments=12, matrix=Matrix.Rotation(-math.pi / 2, 4, "X"))
    _shade(mb, v, lambda q, n: mix(SHELL_DARK, SHELL, 0.5 + 0.5 * n.z))
    for k in range(4):
        y = length * (0.3 - k * 0.35)
        base = Vector((0, y, r * (0.8 - k * 0.15)))
        mb.cylinder(base, base + Vector((0, -0.2, 0.35)) * r, r * 0.1, 0.0, "prop:plain", "#d8d0c0", segments=5, smooth=False)


EOW_BONES = {"head": ((0, -0.5, 0), (0, 0.4, 0), None),
             "mandible.R": ((0.48, 0.54, -0.08), (0.7, 1.1, -0.08), "head"),
             "mandible.L": ((-0.48, 0.54, -0.08), (-0.7, 1.1, -0.08), "head")}


def eater_head(mb: MeshBuilder, p, rng):
    """The head: the plated body continuing into a blunt, ridged snout with a round fanged mouth, glowing eyes on the
    brow, spines on the crown and two big curved mandibles that chomp."""
    r, length = p.get("radius", 0.8), p.get("length", 1.2)
    mb.bone = "head"
    mb.cylinder((0, -length * 0.55, 0), (0, 0, 0), r * 0.8, r * 0.8, "prop:flesh", FLESH_EOW, segments=12)
    _plates(mb, -length * 0.5, -length * 0.05, r, rng, spikes=2)

    def build(b):
        b.ellipsoid((0, length * 0.18, 0), (r * 1.02, length * 0.5, r * 0.95))
        b.ellipsoid((0, length * 0.05, r * 0.35), (r * 0.7, length * 0.35, r * 0.55))      # heavy brow
        b.ball((0, length * 0.72, -r * 0.05), r * 0.55, negative=True)                     # the mouth

    def paint(q, n):
        ridge = 0.5 + 0.5 * math.sin(q.y * 16 + 2 * noise3(q, 3))
        return mix(mix(SHELL_DARK, SHELL, 0.4 + 0.5 * max(0.0, n.z)), "#a898c8", 0.3 * ridge * max(0.0, n.z))

    organic(mb, build, "prop:stone", tris=1800, resolution=r / 14, paint=paint, smooth=1)
    mouth = Vector((0, length * 0.5, -r * 0.05))
    from .detail import teeth_ring
    mb.sphere(mouth - Vector((0, r * 0.12, 0)), r * 0.45, "prop:flesh", "#2a0a14", subdivisions=2, scale=(1, 0.35, 1))
    teeth_ring(mb, mouth, r * 0.5, (0, 1, 0), 12, r * 0.3, r * 0.065, inward=0.72, rng=rng)
    teeth_ring(mb, mouth - Vector((0, r * 0.15, 0)), r * 0.34, (0, 1, 0), 9, r * 0.2, r * 0.05, inward=0.8, rng=rng)
    for sx in (-1, 1):  # glowing eyes under the brow and spines on the crown
        mb.sphere((sx * r * 0.5, length * 0.38, r * 0.5), r * 0.13, "prop:plain:glow", "#f0c040", subdivisions=2, scale=(1, 0.7, 0.8))
        for k in range(2):
            base = Vector((sx * r * 0.32, -length * 0.02 - k * length * 0.2, r * 0.82))
            mb.cylinder(base, base + Vector((sx * 0.15, -0.3, 0.45)) * r, r * 0.12, 0.0, "prop:plain", "#d8d0c0", segments=5, smooth=False)
    for side, sx in (("R", 1), ("L", -1)):
        mb.bone = f"mandible.{side}"
        base = Vector((sx * r * 0.6, length * 0.45, -r * 0.1))
        pts = [base, base + Vector((sx * r * 0.4, length * 0.3, 0)), base + Vector((sx * r * 0.22, length * 0.65, 0.02)), base + Vector((-sx * r * 0.2, length * 0.8, 0.0))]
        mb.tube(pts, [r * 0.22, r * 0.17, r * 0.1, r * 0.02], "prop:plain", MANDIBLE, segments=7)
        for k in range(3):  # serrations on the inner edge
            q = pts[1].lerp(pts[2], k / 3)
            fang(mb, q, q + Vector((-sx * r * 0.2, r * 0.05, 0)), r * 0.045, "prop:plain", "#e8dcc0", segments=4)
    mb.bone = None
    chomp = lambda t: {"mandible.R": [(UP, -0.45 * (0.5 + 0.5 * wave(t)))], "mandible.L": [(UP, 0.45 * (0.5 + 0.5 * wave(t)))]}
    return clips((EOW_BONES, {"fly": (0.6, chomp), "walk": (0.6, chomp), "idle": (1.2, chomp)}))


# ---------------------------------------------------------------------------------------------------- Queen Bee
BEE_BONES = {"body": ((0, -0.05, 0.45), (0, 0.25, 0.45), None),
             "abdomen": ((0, -0.12, 0.42), (0, -0.55, 0.3), "body"),
             "wing.R": ((0.08, -0.02, 0.6), (0.5, -0.12, 0.72), "body"), "wing.L": ((-0.08, -0.02, 0.6), (-0.5, -0.12, 0.72), "body"),
             "hind.R": ((0.07, -0.1, 0.58), (0.42, -0.3, 0.62), "body"), "hind.L": ((-0.07, -0.1, 0.58), (-0.42, -0.3, 0.62), "body")}
YELLOW, BLACK, FUZZ = "#f2b21e", "#1c140c", "#6a4418"


def queen_bee(mb: MeshBuilder, p, rng):
    """About 1 m long at scale 1 (the game draws her at three times that), bottom at z = 0, facing +Y."""
    mb.bone = "body"

    def thorax(b):
        b.ellipsoid((0, 0.08, 0.46), (0.16, 0.17, 0.15))           # thorax
        b.ellipsoid((0, 0.3, 0.46), (0.12, 0.1, 0.12))             # head
    organic(mb, thorax, "prop:cloth", tris=1800, resolution=0.012,
            paint=lambda q, n: mix(FUZZ, YELLOW, 0.35 + 0.25 * noise3(q, 20) + (0.3 if q.y > 0.22 else 0)),
            sculpt=lambda q, n: n * 0.006 * noise3(q, 60))           # fuzz
    for sx in (-1, 1):  # big compound eyes, antennae with clubbed tips, a little crown tuft
        mb.sphere((sx * 0.085, 0.34, 0.49), 0.06, "prop:plain", "#2a1a10", subdivisions=2, scale=(0.8, 1, 1.2))
        mb.sphere((sx * 0.1, 0.36, 0.52), 0.015, "prop:plain:glow", "#fff0c0", subdivisions=1)
        a = Vector((sx * 0.04, 0.38, 0.55))
        mb.tube([a, a + Vector((sx * 0.05, 0.1, 0.12)), a + Vector((sx * 0.12, 0.2, 0.14))], [0.008, 0.007, 0.006], "prop:plain", BLACK, segments=4)
        mb.sphere(a + Vector((sx * 0.12, 0.2, 0.14)), 0.018, "prop:plain", BLACK, subdivisions=1)
    for k in range(5):
        a = -0.6 + 0.3 * k
        mb.cylinder((0, 0.22, 0.58), (math.sin(a) * 0.05, 0.2, 0.66 + 0.02 * math.cos(a)), 0.012, 0.0, "prop:cloth", YELLOW, segments=4)
    for sx in (-1, 1):  # six jointed legs tucked under the thorax
        for k, y in enumerate((0.16, 0.06, -0.04)):
            hip = Vector((sx * 0.08, y, 0.36))
            knee = hip + Vector((sx * 0.12, 0.03 - 0.03 * k, -0.08))
            foot = knee + Vector((sx * 0.02, -0.06 - 0.04 * k, -0.14))
            mb.tube([hip, knee, foot], [0.015, 0.012, 0.006], "prop:plain", BLACK, segments=4)
    mb.bone = "abdomen"

    def abdomen(b):
        b.ellipsoid((0, -0.3, 0.38), (0.19, 0.3, 0.2), stiffness=2.0)

    def stripes(q, n):
        band = math.sin((q.y + 0.1) * 26)
        return mix(YELLOW, BLACK, 1.0 if band > 0.25 else 0.0)

    organic(mb, abdomen, "prop:cloth", tris=1600, resolution=0.012, paint=stripes)
    mb.cylinder((0, -0.58, 0.33), (0, -0.72, 0.28), 0.03, 0.0, "prop:plain", BLACK, segments=6)  # stinger
    for side, sx in (("R", 1), ("L", -1)):
        for wing, (span, rise, lag) in ((f"wing.{side}", (0.5, 0.14, 0.0)), (f"hind.{side}", (0.36, 0.06, -0.1))):
            mb.bone = wing
            root = Vector((sx * 0.07, -0.03 + lag, 0.6))
            pts = [(0, 0), (span * 0.3, span * 0.2), (span * 0.85, span * 0.18), (span, span * 0.02), (span * 0.8, -span * 0.12), (span * 0.25, -span * 0.08)]
            from mathutils import Matrix
            m = Matrix.Translation(root) @ Matrix.Rotation(0.25 * sx, 4, "Z") @ Matrix.Diagonal((sx, 1, 1, 1)) @ Matrix.Rotation(math.pi / 2 - rise, 4, "X")
            mb.extrude_polygon(pts, 0.006, "prop:gel:clear", (0.9, 0.95, 1.0, 0.5), matrix=m)
            for vx, vz in ((span * 0.85, span * 0.15), (span * 0.95, 0.0), (span * 0.6, -span * 0.08)):
                mb.tube([m @ Vector((0, 0.004, 0)), m @ Vector((vx, 0.004, vz))], [0.005, 0.002], "prop:plain", "#3a2a1a", segments=3)
    mb.bone = None

    def fly(t):
        f = wave(t, 1)
        return {"wing.R": [(FWD, -0.7 * f)], "wing.L": [(FWD, 0.7 * f)], "hind.R": [(FWD, -0.6 * wave(t, 1, 0.1))],
                "hind.L": [(FWD, 0.6 * wave(t, 1, 0.1))], "abdomen": [(SIDE, 0.05 * wave(t, 1))]}

    def attack(t):
        pose = fly(t * 5 % 1)
        curl = math.sin(math.pi * min(1.0, t * 1.4))
        pose["abdomen"] = [(SIDE, 0.9 * curl)]        # the stinger swings forward under the body
        return pose

    return clips((BEE_BONES, {"fly": (0.1, fly), "idle": (0.1, fly), "attack": (0.5, attack)}))


# ---------------------------------------------------------------------------------------------------- Skeletron
BONE_COL, BONE_DARK, SOCKET = "#e8dcc2", "#8a7a60", "#140c0a"


def _bone_paint(q, n):
    grime = 0.5 + 0.5 * noise3(q, 7)
    suture = math.exp(-(noise3(q, 2.5, 4.0) / 0.03) ** 2) * max(0.0, n.z)
    return mix(mix(BONE_COL, BONE_DARK, 0.18 * grime + 0.2 * max(0.0, -n.z)), "#3a2a20", 0.75 * suture)


def skeletron_skull(mb: MeshBuilder, p, rng):
    """A huge skull centred at the origin: domed cranium with sutures and cracks, heavy brow, deep sockets with a
    burning point in each, nasal cavity, cheekbones, a toothy upper jaw and a separate lower jaw hanging open."""
    r = p.get("radius", 1.1)
    layer = f"prop:{p.get('layer', 'plain')}"
    glow = p.get("eyes", "#ff5030")

    def build(b):
        b.ellipsoid((0, -r * 0.05, r * 0.18), (r * 0.88, r, r * 0.9))                 # cranium
        b.ellipsoid((0, r * 0.42, -r * 0.35), (r * 0.62, r * 0.45, r * 0.42))          # face and upper jaw
        for sx in (-1, 1):
            b.ellipsoid((sx * r * 0.5, r * 0.5, -r * 0.2), (r * 0.22, r * 0.25, r * 0.16))   # cheekbones
            b.ball((sx * r * 0.33, r * 0.82, r * 0.02), r * 0.27, negative=True)             # eye sockets
        b.ellipsoid((0, r * 0.88, -r * 0.3), (r * 0.09, r * 0.15, r * 0.14), negative=True)  # nasal cavity
        b.ellipsoid((0, r * 0.5, r * 0.28), (r * 0.7, r * 0.2, r * 0.07))              # brow ridge

    paint = _bone_paint if layer == "prop:plain" else (lambda q, n: mix("#c8ccd4", "#4a4e58", 0.25 + 0.2 * noise3(q, 5)))
    organic(mb, build, layer, tris=6000, resolution=r / 30, paint=paint, smooth=1)
    for sx in (-1, 1):
        mb.sphere((sx * r * 0.33, r * 0.62, r * 0.02), r * 0.2, "prop:plain", SOCKET, subdivisions=2, scale=(1, 0.6, 1))
        mb.sphere((sx * r * 0.33, r * 0.7, r * 0.03), r * 0.07, "prop:plain:glow", glow, subdivisions=2)
    teeth_row(mb, (-r * 0.4, r * 0.66, -r * 0.62), (r * 0.4, r * 0.66, -r * 0.62), 10, r * 0.16, (0, 0.15, -1), r * 0.045, rng, color="#f4ecd8", jag=0.15)
    # The lower jaw, hanging open.
    jaw_top = Vector((0, r * 0.2, -r * 0.75))

    def jaw(b):
        b.capsule(jaw_top + Vector((-r * 0.5, r * 0.05, 0.05)), jaw_top + Vector((-r * 0.35, r * 0.45, -r * 0.2)), r * 0.1)
        b.capsule(jaw_top + Vector((r * 0.5, r * 0.05, 0.05)), jaw_top + Vector((r * 0.35, r * 0.45, -r * 0.2)), r * 0.1)
        b.capsule(jaw_top + Vector((-r * 0.35, r * 0.45, -r * 0.2)), jaw_top + Vector((r * 0.35, r * 0.45, -r * 0.2)), r * 0.12)
    organic(mb, jaw, layer, tris=900, resolution=r / 30, paint=paint, smooth=1)
    teeth_row(mb, jaw_top + Vector((-r * 0.36, r * 0.5, -r * 0.12)), jaw_top + Vector((r * 0.36, r * 0.5, -r * 0.12)), 9, r * 0.13, (0, 0.2, 1), r * 0.04, rng, color="#f4ecd8", jag=0.15)
    for _ in range(p.get("cracks", 4)):  # dark cracks over the dome
        a = rng.uniform(0, 2 * math.pi)
        start = Vector((math.cos(a) * r * 0.45, math.sin(a) * r * 0.5 - r * 0.1, r * 0.95))
        pts = [start]
        for _ in range(3):
            pts.append(pts[-1] + Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), -0.18)) * r)
        mb.tube([q.normalized() * r * 0.97 if q.length > 0 else q for q in pts], [r * 0.018, r * 0.014, r * 0.01, r * 0.005], "prop:plain", "#2a1e18", segments=3)


def skeletron_hand(mb: MeshBuilder, p, rng):
    """A giant bone hand centred at the origin, palm facing +Y, fingers of three phalanges curling to grab, knuckles,
    carpal bones and the ends of the two forearm bones at the wrist (-Z)."""
    s = p.get("size", 1.1)
    layer = f"prop:{p.get('layer', 'plain')}"
    paint = _bone_paint if layer == "prop:plain" else (lambda q, n: mix("#c8ccd4", "#4a4e58", 0.25 + 0.2 * noise3(q, 5)))

    def build(b):
        for k in range(4):                                  # metacarpals fanning out
            x = (-0.3 + k * 0.2) * s
            b.capsule((x * 0.6, -0.02 * s, -0.3 * s), (x, 0, 0.2 * s), 0.06 * s)
            b.ball((x, 0, 0.22 * s), 0.075 * s)
        for k in range(5):                                  # carpals
            b.ball(((-0.2 + k * 0.1) * s, -0.03 * s, -0.36 * s + 0.03 * s * (k % 2)), 0.07 * s)
        for sx in (-1, 1):                                  # radius and ulna ends
            b.capsule((sx * 0.1 * s, -0.05 * s, -0.45 * s), (sx * 0.08 * s, -0.08 * s, -0.95 * s), 0.07 * s)
            b.ball((sx * 0.1 * s, -0.05 * s, -0.45 * s), 0.1 * s)
        for k in range(4):                                  # fingers: three phalanges curling toward +Y
            x = (-0.3 + k * 0.2) * s
            j = [Vector((x, 0, 0.22 * s))]
            seg = (0.28, 0.2, 0.15) if k in (1, 2) else (0.24, 0.17, 0.13)
            ang = 0.35
            for L in seg:
                d = Vector((0, math.sin(ang), math.cos(ang)))
                j.append(j[-1] + d * L * s)
                ang += 0.55
            for a0, a1 in zip(j, j[1:]):
                b.capsule(a0, a1, 0.05 * s)
                b.ball(a1, 0.058 * s)
        t = [Vector((0.38 * s, 0.02 * s, -0.22 * s))]          # thumb
        for L, d in ((0.22, Vector((0.5, 0.4, 0.7))), (0.17, Vector((0.2, 0.7, 0.6)))):
            t.append(t[-1] + d.normalized() * L * s)
        for a0, a1 in zip(t, t[1:]):
            b.capsule(a0, a1, 0.055 * s)
            b.ball(a1, 0.06 * s)

    organic(mb, build, layer, tris=4500, resolution=s / 70, paint=paint, smooth=1)


def skeletron_arm(mb: MeshBuilder, p, rng):
    """One arm bone, one unit long along +Y from the origin (the game stretches it between shoulder and hand): a
    shaft flaring into knobbly joint ends."""
    r = p.get("radius", 0.13)
    layer = f"prop:{p.get('layer', 'plain')}"
    paint = _bone_paint if layer == "prop:plain" else (lambda q, n: mix("#c8ccd4", "#4a4e58", 0.25 + 0.2 * noise3(q, 5)))

    def build(b):
        b.capsule((0, 0.12, 0), (0, 0.88, 0), r)
        for y in (0.06, 0.94):
            for sx in (-1, 1):
                b.ball((sx * r * 0.7, y, 0), r * 1.15)
    organic(mb, build, layer, tris=700, resolution=r / 5, paint=paint, smooth=1)


GENERATORS = {
    "eater_head": eater_head, "eater_segment": eater_segment, "eater_tail": eater_tail,
    "queen_bee_detailed": queen_bee,
    "skeletron_skull": skeletron_skull, "skeletron_hand_bones": skeletron_hand, "skeletron_arm_bone": skeletron_arm,
}
