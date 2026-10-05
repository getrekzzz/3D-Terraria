"""A golem of granite blocks (``anatomy: stone``): chipped slabs and boulders, each riding one bone of the human rig
so the body grinds and shifts block against block as it moves, over a lumpy core of dark packed gravel that shows
only in the cracks between them. Broad boulder shoulders with stones between them and the neck, a tapering chest of
two front slabs, a hump behind the neck and a block under the jaw, a broad belly block on a wide pelvis, thick arms
swelling to heavy forearms and massive fists of jointed stone fingers, pillar legs with a stone behind each knee on
flat block feet, and a blocky head sunk low under a heavy brow, its eyes glowing deep in the sockets.

Colours from ``skin`` (the granite: paler speckle, dark flecks and darker cracks, each block a shade apart) and
``eyes`` (the glow).
"""
import numpy as np

from . import matter
from .matter import F, chips, crack, local, round_box
from .rig import rigid

SOURCES = ("golem.py",)
FIST = 1.45          # the hands' blocks against the hand's joints


class Piece:
    """A block: ``shape(p)`` (chipped) and ``field(p)`` (with its cracks), world metres; ``bone`` carries it."""

    def __init__(self, name, bone, c, rot, half, r, seed, cracks=(), nchips=6, taper=0.0, flare=0.0):
        self.name, self.bone, self.flare = name, bone, flare
        self.c = np.asarray(tuple(c), dtype=float)
        self.rot = np.asarray(rot, dtype=float)
        self.half = np.asarray(half, dtype=float)
        self.r, self.seed, self.cracks, self.nchips, self.taper = r, seed, cracks, nchips, taper

    def _q(self, p):
        q = local(p, self.c, self.rot)
        if self.taper:          # narrower toward local -z (a chest wide at the shoulders)
            t = np.clip((q[:, 2] / F(self.half[2]) + 1) * F(0.5), 0, 1)
            q = q.copy()
            q[:, 0] /= F(1 - self.taper) + F(self.taper) * t
        if self.flare:          # thicker toward local +x (a forearm swelling to the wrist)
            t = np.clip(q[:, 0] / F(self.half[0]), -1, 1)
            q = q.copy()
            q[:, 1:] /= (F(1) + F(self.flare) * t)[:, None]
        return q

    def shape(self, p):
        q = self._q(p)
        d = round_box(q, self.half, self.r)
        d = chips(d, q, self.half, self.seed, self.nchips, depth=(0.2, 0.55), soft=self.r * 0.3)
        lump = F(0.011 * self.half.min() / 0.05) * (matter.noise(q, 6.0, self.seed + 5) - F(0.5))
        return d + lump + F(0.0016) * (matter.noise(q, 38.0, self.seed) - F(0.5)) + F(0.0012) * (matter.noise(q, 90.0, self.seed + 3) - F(0.5))

    def field(self, p):
        d = self.shape(p)
        q = self._q(p)
        for k, (n, off, along, reach) in enumerate(self.cracks):
            d = crack(d, q, n, off * self.half @ np.abs(n), 0.002, 0.009, along, reach * float(np.abs(along) @ self.half) if along else None,
                      wiggle=0.004, seed=self.seed + k)
        return d

    def bounds(self, pad):
        ext = np.abs(self.rot) @ (self.half * (1 + abs(self.flare))) / (1 - abs(self.taper)) + pad
        return tuple(self.c - ext), tuple(self.c + ext)


def _limb(name, bone, a, b, u0, u1, wy, wz, r, seed, front=(0, 1, 0), cracks=(), flare=0.0):
    """A block along a bone from fraction u0 to u1 of the way from ``a`` to ``b``, ``wy`` deep and ``wz`` wide."""
    a, b = np.asarray(tuple(a)), np.asarray(tuple(b))
    rot = matter.basis(b - a, front)
    L = np.linalg.norm(b - a)
    c = a + (b - a) * (u0 + u1) / 2
    return Piece(name, bone, c, rot, (L * (u1 - u0) / 2, wy, wz), r, seed, cracks, flare=flare)


def pieces(fr):
    j, s = fr.j, fr.s
    S = lambda *v: np.asarray(v) * s
    ident = np.eye(3)
    out = [
        Piece("pelvis", "hips", S(0, -0.01, 0.95), ident, S(0.15, 0.108, 0.086), 0.032 * s, 11,
              cracks=(((0, 1, 0.3), 0.2, (1, 0, 0), 0.6),)),
        Piece("belly", "spine", S(0, 0.002, 1.09), ident, S(0.134, 0.1, 0.088), 0.03 * s, 12, nchips=5),
        Piece("chest", "chest", S(0, -0.014, 1.285), ident, S(0.16, 0.122, 0.13), 0.034 * s, 13, taper=0.3,
              cracks=(((1, 0, 0.4), 0.35, (0, 0, -1), 0.4),)),
        Piece("back", "chest", S(0, -0.1, 1.41), matter.basis((1, 0, 0), (0, 1, 0.5)), S(0.15, 0.064, 0.09), 0.028 * s, 14),
        Piece("nape", "neck", S(0, -0.035, 1.47), matter.basis((1, 0, 0), (0, 1, 0.3)), S(0.075, 0.06, 0.055), 0.022 * s, 15, nchips=7),
        Piece("throat", "neck", S(0, 0.026, 1.452), matter.basis((1, 0, 0), (0, 1, -0.2)), S(0.084, 0.058, 0.056), 0.022 * s, 16, nchips=6),
    ]
    for side, sx in (("R", 1), ("L", -1)):
        sh, el, wr = j[f"shoulder.{side}"], j[f"elbow.{side}"], j[f"wrist.{side}"]
        hip, kn, an = j[f"hip.{side}"], j[f"knee.{side}"], j[f"ankle.{side}"]
        k = 0 if side == "R" else 50
        tilt = matter.basis((sx, 0, -0.32 * sx * sx), (0, 1, 0))
        out += [
            Piece(f"trap_{side}", "chest", S(sx * 0.1, -0.034, 1.425), matter.basis((sx, 0, -0.45 * sx * sx), (0, 1, 0)),
                  S(0.072, 0.07, 0.048), 0.02 * s, 31 + k, nchips=6),             # between the neck and the shoulder
            Piece(f"pec_{side}", "chest", S(sx * 0.072, 0.088, 1.31), matter.basis((sx, 0.25 * sx * sx, -0.1), (0, 1, 0.15)),
                  S(0.076, 0.04, 0.08), 0.018 * s, 20 + k),
            Piece(f"shoulder_{side}", f"upper_arm.{side}", np.asarray(tuple(sh)) + S(sx * 0.03, -0.006, 0.036), tilt,
                  S(0.1, 0.1, 0.085), 0.03 * s, 21 + k, nchips=9,
                  cracks=(((0, 1, 0.2), -0.1, (0, 0, 1), 0.5),)),
            _limb(f"upperarm_{side}", f"upper_arm.{side}", sh, el, 0.25, 0.92, 0.062 * s, 0.064 * s, 0.022 * s, 22 + k),
            Piece(f"elbow_{side}", f"forearm.{side}", np.asarray(tuple(el)) + S(0, -0.008, 0), matter.basis(wr - el, (0, 1, 0)),
                  S(0.058, 0.058, 0.06), 0.024 * s, 23 + k, nchips=8),
            _limb(f"forearm_{side}", f"forearm.{side}", el, wr, 0.15, 0.95, 0.068 * s, 0.072 * s, 0.024 * s, 24 + k, flare=0.12,
                  cracks=(((0, 0.3, 1), 0.1, (1, 0, 0), 0.3),)),
            _limb(f"thigh_{side}", f"thigh.{side}", hip, kn, 0.06, 0.9, 0.09 * s, 0.086 * s, 0.028 * s, 25 + k,
                  cracks=(((0, 1, 0.2), 0.0, (1, 0, 0), 0.2),)),
            Piece(f"hock_{side}", f"shin.{side}", np.asarray(tuple(kn)) + S(0, -0.042, 0.012), matter.basis(an - kn, (0, 1, 0)),
                  S(0.06, 0.042, 0.064), 0.02 * s, 30 + k, nchips=6),             # fills the back of the knee
            Piece(f"knee_{side}", f"shin.{side}", np.asarray(tuple(kn)) + S(0, 0.02, 0.0), matter.basis(an - kn, (0, 1, 0)),
                  S(0.064, 0.058, 0.066), 0.024 * s, 26 + k, nchips=8),
            _limb(f"shin_{side}", f"shin.{side}", kn, an, 0.09, 0.92, 0.076 * s, 0.08 * s, 0.026 * s, 27 + k, flare=0.14),
        ]
        heel, ball, toe = j[f"heel.{side}"], j[f"ball.{side}"], j[f"toe.{side}"]
        fwd = np.asarray(tuple(ball - heel))
        fwd[2] = 0
        rot = matter.basis(fwd, (0, 0, 1))      # x forward, y up, z sideways
        mid = (np.asarray(tuple(heel)) + np.asarray(tuple(ball))) / 2
        out += [
            Piece(f"foot_{side}", f"foot.{side}", mid + S(0, -0.006, 0.046), rot, S(0.11, 0.05, 0.076), 0.02 * s, 28 + k),
            Piece(f"toe_{side}", f"toe.{side}", np.asarray(tuple(ball)) + S(sx * 0.003, 0.036, 0.03), rot,
                  S(0.05, 0.034, 0.07), 0.014 * s, 29 + k, nchips=7),
        ]
    return out


def fist(fr, side):
    """A hand of stone: a palm block and jointed fingers, every segment its own block on its own bone."""
    from .hands import hand_axes
    j, s = fr.j, fr.s
    along, thumb, dorsal = (np.asarray(tuple(v)) for v in hand_axes(fr, side))
    wr = np.asarray(tuple(j[f"wrist.{side}"]))
    k = 0 if side == "R" else 300
    big = lambda c: wr + (c - wr) * FIST          # a massive fist: the hand's blocks scaled up about the wrist
    out = [Piece(f"palm_{side}", f"hand.{side}", big(wr + along * 0.04 * s - dorsal * 0.003 * s), np.stack([along, thumb, dorsal], 1),
                 FIST * np.asarray((0.056 * s, 0.062 * s, 0.04 * s)), 0.016 * s * FIST, 200 + k, nchips=5)]
    for n, f in enumerate(("thumb", "index", "middle", "ring", "pinky")):
        w = (0.021 if f == "thumb" else 0.017 if f != "pinky" else 0.015) * s
        pts = [np.asarray(tuple(j[f"{f}{i}.{side}"])) for i in (1, 2, 3)] + [np.asarray(tuple(j[f"{f}_tip.{side}"]))]
        if f == "thumb":
            pts[0] = pts[0] + (pts[1] - pts[0]) * 0.35
        for i in range(3):
            a, b = pts[i], pts[i + 1]
            L = np.linalg.norm(b - a)
            ext = 0.004 * s if i < 2 else 0.0
            c = (a + b) / 2 + (b - a) / L * ext / 2
            rot = matter.basis(b - a, dorsal)
            out.append(Piece(f"{f}{i}_{side}", f"{f}.0{i + 1}.{side}", big(c), rot,
                             FIST * np.asarray((L / 2 + ext / 2 - 0.0012 * s, w * 0.95, w)), 0.0055 * s * FIST, 210 + k + 10 * n + i, nchips=3))
    return out


def head_pieces(head):
    """The head in head-local units: a block with a flat top and chipped corners grown into a heavy brow ledge over
    deep sockets and a blunt wedge of a nose, and a jutting jaw block under it."""
    h = head.h
    at = lambda *v: np.asarray(tuple(head.at(v)))
    skull = Piece("skull", "head", at(0, -0.006, -0.06), np.eye(3), (0.096 * h, 0.098 * h, 0.082 * h), 0.024 * h, 400, nchips=7,
                  cracks=(((1, 0, 0.6), 0.25, (0, 0, 1), 0.4),))
    brow = Piece("brow", "head", at(0, 0.072, -0.02), matter.basis((1, 0, 0), (0, 1, 0.35)), (0.1 * h, 0.045 * h, 0.03 * h),
                 0.014 * h, 401, nchips=6)
    nose = Piece("nose", "head", at(0, 0.098, -0.072), matter.basis((0, 0, -1), (0, 1, 0.25)), (0.032 * h, 0.018 * h, 0.021 * h),
                 0.008 * h, 402, nchips=4, taper=-0.4)
    jaw = Piece("jaw", "head", at(0, 0.03, -0.138), matter.basis((1, 0, 0), (0, 1, -0.12)), (0.092 * h, 0.086 * h, 0.042 * h),
                0.018 * h, 403, nchips=6, cracks=(((1, 0, 0.2), -0.3, (0, 1, 0), 0.2),))
    return [Skull(head, skull, brow, nose), jaw]


class Skull:
    """Skull, brow and nose blocks grown together, the eye sockets cut deep under the brow and a mouth slot."""

    def __init__(self, head, skull, brow, nose):
        from .sdf import smin
        self.head, self.parts, self.smin = head, (skull, brow, nose), smin
        self.name, self.bone, self.seed = "skull", "head", skull.seed

    def _join(self, fn, p):
        a, b, c = (fn(x, p) for x in self.parts)
        return self.smin(self.smin(a, b, 0.012 * self.head.h), c, 0.008 * self.head.h)

    def shape(self, p):
        return self._join(Piece.shape, p)

    def field(self, p):
        from .sdf import carve, ellipsoid
        h = self.head.h
        d = self._join(Piece.field, p)
        q = (p - np.asarray(tuple(self.head.c), dtype=F)) / F(h)
        for sx in (1, -1):
            d = carve(d, ellipsoid(q, (sx * 0.04, 0.104, -0.047), (0.028, 0.045, 0.019)) * F(h), 0.003 * h)
        mouth = matter.round_box(q - np.asarray((0, 0.11, -0.102), dtype=F), (0.052, 0.03, 0.004), 0.003) * F(h)
        return carve(d, mouth, 0.002 * h)

    def bounds(self, pad):
        b = [x.bounds(pad) for x in self.parts]
        return tuple(np.min([x[0] for x in b], 0)), tuple(np.max([x[1] for x in b], 0))


def _paint(obj, piece, base, tone):
    co = matter.coords(obj).astype(F)
    deep = np.clip(-piece.shape(co) / F(0.003 * 0.8), 0, 1)[:, None]            # in a crack
    blotch = matter.fbm(co, 9.0, 3, piece.seed)[:, None]
    grain = matter.hash3(np.floor(co * 190.0), piece.seed)[:, None]
    rgb = base * tone * (0.84 + 0.32 * blotch)
    rgb = np.where(grain > 0.86, rgb * 0.62 + np.asarray((0.74, 0.78, 0.86)) * 0.38, rgb)      # pale feldspar
    rgb = np.where(grain < 0.1, rgb * 0.62, rgb)                                               # dark flecks
    rgb = rgb * (1 - deep) + base * 0.25 * deep
    matter.paint(obj, rgb)


def _block(piece, fr, h, target, field=None):
    lo, hi = piece.bounds(0.01 * fr.s)
    obj = matter.mesh(field or piece.field, (lo, hi), h, target, piece.name, ("golem", matter.frame_key(fr)), SOURCES)
    rigid(obj, piece.bone)
    return obj


def build(fr, head, arm, p):
    s = fr.s
    base = matter.hexrgb(p.get("skin", "#4e5a72"))
    rng = np.random.default_rng(7)
    objs = []
    for piece in pieces(fr):
        big = np.prod(piece.half) > (0.05 * s) ** 3
        obj = _block(piece, fr, 0.0028 * s if big else 0.0022 * s, 2600 if big else 1400)
        _paint(obj, piece, base, 0.9 + 0.2 * rng.random())
        objs.append(obj)
    for side in ("R", "L"):
        for piece in fist(fr, side):
            obj = _block(piece, fr, 0.0011 * s if "palm" not in piece.name else 0.0018 * s, 260 if "palm" not in piece.name else 900)
            _paint(obj, piece, base, 0.9 + 0.2 * rng.random())
            objs.append(obj)
    hp = head_pieces(head)
    for piece in hp:
        obj = _block(piece, fr, 0.0016 * s, 4200 if piece.name == "skull" else 1400)
        _paint(obj, piece, base, 0.92 + 0.16 * rng.random())
        objs.append(obj)
    objs.append(_eyes(head, p.get("eyes", "#8ae0ff")))
    objs.append(_core(fr, arm, base))
    for o in objs[:-2]:
        matter.slot(o, "char:bone")
    return objs


def _eyes(head, color):
    """Two glowing coals deep in the sockets."""
    import bmesh
    from .garments import mesh_from
    bm = bmesh.new()
    for sx in (1, -1):
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
        bmesh.ops.scale(bm, vec=(0.022 * head.h, 0.01 * head.h, 0.012 * head.h), verts=res["verts"])
        bmesh.ops.translate(bm, vec=head.at((sx * 0.04, 0.094, -0.048)), verts=res["verts"])
    obj = mesh_from(bm, "eye_glow")
    matter.paint(obj, np.tile(matter.hexrgb(color), (len(obj.data.vertices), 1)))
    matter.slot(obj, "char:glow")
    rigid(obj, "head")
    return obj


def _core(fr, arm, base):
    """A slim dark body inside the blocks: what shows in the cracks between them as they move."""
    from . import frame as frame_mod
    from . import rig
    from .blobs import Blob
    from .body import build_body
    slim = frame_mod.Frame({"height": fr.height, "body": "female" if fr.female else "male", "build": 0.5, "weight": 0.3})
    blob = Blob("core_blob", 0.012 * fr.s)
    build_body(slim, blob)
    core = blob.to_mesh("core", 6000, smooth_iterations=1)
    fingers = [b.name for b in arm.data.bones if b.name.split(".")[0] in frame_mod.FINGERS]
    rig.heat_weights(core, arm, drop=fingers + ["root"])
    rig.clean_limb_weights(core, arm, fr)
    co, n = matter.coords(core), matter.normals(core)
    lumps = 0.022 * fr.s * (matter.fbm(co.astype(F), 16.0, 2, 41)[:, None] - 0.5)          # rubble, not a smooth rod
    core.data.vertices.foreach_set("co", (co - n * 0.018 * fr.s + n * lumps).astype(np.float32).ravel())   # inside the blocks
    core.data.update()
    grit = matter.fbm(co.astype(F), 40.0, 2, 43)[:, None]
    matter.paint(core, base * (0.36 + 0.26 * grit))                  # dark packed gravel between the blocks
    matter.slot(core, "char:bone")
    return core
