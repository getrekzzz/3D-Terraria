"""A bib apron worn over the clothes: a cloth panel of real thickness hung from a strap round the neck, tied round the
waist (the ties knotted in a bow at the back) and falling to above the knees.

The panel lies over the shirt, belt and trousers as a membrane: the clothes' outermost surface is sampled round the
torso (rays toward the body's axis), the cloth spans its hollows and the shirt's folds and rests on their crests, is
pulled in against the belt by the waistband and hangs from the belly and hips below it with a slight flare and a few
soft folds deepening toward the hem. Its pattern (a bib narrower than the skirt) is laid onto that surface by arc
length; every edge is a turned hem (a thicker band with a stitch line) rounded over from the front face to the back,
and a stitched patch pocket sits on the skirt. The bib follows the chest and spine like the shirt under it; the skirt
follows the hips at the waist and each thigh lower down, so a striding leg carries its side of the apron with it.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

from .garments import mesh_from, set_slot
from .paint import hex3
from .rig import ARM_BONES, read_weights, write_weights
from .straps import bvh, neck_strap, waist_ties
from .tailoring import ridge, ss

STEP = 0.006            # spacing of the grid the panel's surface is solved on (frame scale)
STITCH = (0.0085, 0.0101)   # a hem's stitch line, inset from the edge (frame scale)


def _lines(features, step):
    """Feature heights (descending) with evenly spaced lines filled in between, none further apart than ``step``."""
    out = [features[0]]
    for a, b in zip(features, features[1:]):
        n = max(1, math.ceil((a - b) / step - 1e-6))
        out += [a + (b - a) * q / n for q in range(1, n + 1)]
    return np.array(out)


def build(w, body, head_obj, under, color):
    """The apron over the clothes ``under`` (garments.Wardrobe ``w``'s shirt, trousers, belt and buttons): the panel,
    the neck strap and the waistband with its bow, each a skinned object."""
    return Apron(w, body, head_obj, under, color).build()


class Apron:
    def __init__(self, w, body, head_obj, under, color):
        f, s = w.f, w.f.s
        self.w, self.f, self.s, self.j = w, f, s, f.j
        self.head_obj = head_obj
        self.base = hex3(color)
        shirt = w.p.get("shirt")
        if shirt and max(abs(a - b) for a, b in zip(self.base, hex3(shirt))) < 0.06:
            self.base = tuple(c * 0.84 for c in self.base)      # cut from the shirt's cloth: a shade deeper, to read apart
        self.T = 0.0038 * s                  # the cloth's thickness
        self.gap = 0.0025 * s                # its clearance over the clothes
        self.hem_w = 0.012 * s               # the turned hems' width (their stitch line just inside it)
        self.tie_z = w.waist - 0.005 * s     # the waistband and ties ride on the belt
        self.band_w = 0.024 * s
        self.top = f.j["chest"].z + 0.1 * s
        self.hem = f.j["knee.R"].z + 0.07 * s
        self.clothes = [body, *under]
        self.tree = bvh(self.clothes, lambda c: not w._on_arm(c))
        self.kd = KDTree(len(body.data.vertices))
        for i, v in enumerate(body.data.vertices):
            self.kd.insert(v.co, i)
        self.kd.balance()
        self.body_w = read_weights(body)
        self.bm = bmesh.new()
        self.layer = self.bm.loops.layers.color.new("Color")
        self.vk, self.fk, self.wt = {}, {}, {}

    def build(self):
        self._surface()
        self._panel()
        panel = self._finish("apron")
        neck_strap(self, panel)
        strap = self._finish("apron_strap")
        waist_ties(self, panel)
        return [panel, strap, self._finish("apron_ties")]

    # ------------------------------------------------------------------ the panel's surface
    def _surface(self):
        """Radii of the panel's base surface on a grid of heights (top down) and angles round the body's axis."""
        s = self.s
        zs = np.arange(self.top + 0.03 * s, self.hem - 0.05 * s, -STEP * s)
        ths = np.linspace(-1.75, 1.75, 2 * round(1.75 * 0.15 / STEP) + 1)
        far = 0.5 * s
        sup = np.zeros((len(zs), len(ths)))
        for i, z in enumerate(zs):
            for k, th in enumerate(ths):
                d = Vector((math.sin(th), math.cos(th), 0.0))
                loc, _, _, dist = self.tree.ray_cast(Vector((0.0, 0.0, z)) + d * far, -d, far)
                if loc is not None:
                    sup[i, k] = far - dist
        p = np.pad(sup, 1, mode="edge")             # widened by a cell, so nothing thin slips between the rays
        sup = np.max([p[a:a + len(zs), b:b + len(ths)] for a in range(3) for b in range(3)], axis=0)
        bloused = ss(self.tie_z + 0.01 * s, self.tie_z + 0.03 * s, zs) * ss(self.tie_z + 0.15 * s, self.tie_z + 0.08 * s, zs)
        lo = sup + self.gap + self.T + 0.0012 * s * bloused[:, None]      # (more where the shirt moves under it)
        band = int(np.argmin(np.abs(zs - self.tie_z)))
        for i in range(band + 1, len(zs)):          # below the waistband the cloth hangs, flaring a little
            lo[i] = np.maximum(lo[i], lo[i - 1] + 0.05 * STEP * s)
        r = lo.copy()
        for _ in range(800):                        # a membrane over the clothes, pulled in at the waistband
            p = np.pad(r, 1, mode="edge")
            r = np.maximum((p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:]) * 0.25, lo)
            r[band] = lo[band]
        self.zs, self.ths, self.r, self.lo = zs, ths, r, lo
        below = self.tie_z - self.band_w / 2 - 0.01 * s
        self.skirt_w = float(np.interp(0.92, self.ths, self._arc(below)))      # round to 53 degrees either side
        self.pocket = (0.58 * self.skirt_w, self.tie_z - self.band_w / 2 - 0.05 * s)
        self.pocket += (self.pocket[1] - 0.11 * s,)

    def _at(self, grid, z, th):
        """Bilinear read of a surface grid at heights ``z`` and angles ``th``."""
        fi = np.clip((self.zs[0] - z) / (self.zs[0] - self.zs[1]), 0, len(self.zs) - 1.001)
        fk = np.clip((th - self.ths[0]) / (self.ths[1] - self.ths[0]), 0, len(self.ths) - 1.001)
        i, k = fi.astype(int), fk.astype(int)
        a, b = fi - i, fk - k
        return (grid[i, k] * (1 - a) + grid[i + 1, k] * a) * (1 - b) + (grid[i, k + 1] * (1 - a) + grid[i + 1, k + 1] * a) * b

    def _arc(self, z):
        """Arc length along the surface from the centre front to each grid angle, at height z."""
        r = self._at(self.r, np.full(len(self.ths), z), self.ths)
        arc = np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(np.sin(self.ths) * r), np.diff(np.cos(self.ths) * r)))])
        return arc - arc[len(self.ths) // 2]

    def half_width(self, z):
        """The pattern's half width at height z: the bib widening a little downward, the skirt below the waistband."""
        s = self.s
        bib = 0.088 * s + 0.012 * s * min(max((self.top - z) / (self.top - self.tie_z), 0.0), 1.0)
        k = ss(self.tie_z + self.band_w / 2 - 0.004 * s, self.tie_z - self.band_w / 2 + 0.004 * s, z)
        return bib + (self.skirt_w - bib) * k

    def _pocket_mask(self, x, z):
        x0, z0, z1 = self.pocket
        m = 0.025 * self.s
        return ss(x0 + m, x0, np.abs(x)) * ss(z0 + m, z0, z) * ss(z1 - m, z1, z)

    def place(self, X, Z):
        """Pattern points (arc length X from the centre front, height Z; rows of equal height) on the panel's surface
        with the skirt's folds: their positions, normals and the folds' crease shade."""
        s = self.s
        P = np.zeros(X.shape + (3,))
        shade = np.ones(X.shape)
        top = self.tie_z - self.band_w / 2
        for i in range(X.shape[0]):
            x, z = X[i], Z[i]
            # Below the hips nothing holds the skirt round the body: it hangs flatter, its sides coming forward.
            flat = np.clip((self.j["hip.R"].z - z) / (self.j["hip.R"].z - self.hem), 0.0, 1.0)
            th = np.interp(x, self._arc(z[0]), self.ths) * (1.0 - 0.2 * flat)
            r, lo = self._at(self.r, z, th), self._at(self.lo, z, th)
            t = np.clip((top - z) / (top - self.hem), 0.0, 1.0)
            crest = ridge(x / (0.085 * s) + 0.2 + 0.12 * np.sin(z / s * 7 + x / s * 3) + 0.25 * t)
            calm = 1.0 - 0.85 * self._pocket_mask(x, z)
            r = np.maximum(r + 0.0075 * s * t ** 1.2 * (crest - 0.45) * calm, lo)
            zz = z - 0.005 * s * t ** 4 * (crest - 0.5) * calm          # the folds' crests hang a little lower
            P[i] = np.stack([np.sin(th) * r, np.cos(th) * r + 0.5 * flat * (1.0 - np.cos(th)) * r, zz], 1)
            shade[i] = 1.0 - 0.25 * t ** 1.2 * (1.0 - crest) ** 2 * calm
        N = np.cross(np.gradient(P, axis=1), np.gradient(P, axis=0))
        return P, N / np.linalg.norm(N, axis=2)[..., None], shade

    # ------------------------------------------------------------------ cloth pieces
    def _shell(self, F, B, N, wts, vert_k, face_k, rim_k=0.9, top_rim_k=None):
        """A closed piece of cloth from its front and back grids (rows x columns x 3): both faces, and its edge rounded
        over between them. ``wts`` (per grid point) are copied to the back and the edge."""
        bm, m, n = self.bm, F.shape[0] - 1, F.shape[1] - 1
        fv = [[bm.verts.new(F[i, k]) for k in range(n + 1)] for i in range(m + 1)]
        bv = [[bm.verts.new(B[i, k]) for k in range(n + 1)] for i in range(m + 1)]
        for i in range(m + 1):
            for k in range(n + 1):
                self.vk[fv[i][k]], self.vk[bv[i][k]] = vert_k[i, k], 0.8
                self.wt[fv[i][k]] = self.wt[bv[i][k]] = wts[i][k]
        for i in range(m):
            for k in range(n):
                self.fk[bm.faces.new((fv[i][k], fv[i][k + 1], fv[i + 1][k + 1], fv[i + 1][k]))] = face_k[i, k]
                self.fk[bm.faces.new((bv[i][k], bv[i + 1][k], bv[i + 1][k + 1], bv[i][k + 1]))] = 1.0
        ring = ([(0, k) for k in range(n + 1)] + [(i, n) for i in range(1, m + 1)] + [(m, k) for k in range(n - 1, -1, -1)]
                + [(i, 0) for i in range(m - 1, 0, -1)])
        mids = []
        for i, k in ring:
            e = np.zeros(3)
            e += (F[0, k] - F[1, k]) if i == 0 else (F[m, k] - F[m - 1, k]) if i == m else 0.0
            e += (F[i, 0] - F[i, 1]) if k == 0 else (F[i, n] - F[i, n - 1]) if k == n else 0.0
            e -= N[i, k] * e.dot(N[i, k])
            mid = bm.verts.new((F[i, k] + B[i, k]) / 2 + e / np.linalg.norm(e) * np.linalg.norm(F[i, k] - B[i, k]) / 2)
            self.vk[mid], self.wt[mid] = vert_k[i, k], wts[i][k]
            mids.append(mid)
        for a, (i0, k0) in enumerate(ring):
            b = (a + 1) % len(ring)
            i1, k1 = ring[b]
            k = top_rim_k if top_rim_k is not None and i0 == i1 == 0 else rim_k
            self.fk[bm.faces.new((fv[i0][k0], mids[a], mids[b], fv[i1][k1]))] = k
            self.fk[bm.faces.new((mids[a], bv[i0][k0], bv[i1][k1], mids[b]))] = k * 0.9

    def sweep(self, pts, ups, profile, wts, widths=None, closed=False, cut=0.0, shade=1.0):
        """A cloth strip of ``profile`` swept along ``pts``, its outer face toward ``ups``; open ends are capped (the
        last one cut on the slant by ``cut``, running on past the path's end). ``wts``: the skin weights at each point."""
        bm, n = self.bm, len(pts)
        hw = max(abs(x) for x, _, _ in profile) * (widths[-1] if widths is not None else 1.0)
        rings = []
        for i, p in enumerate(pts):
            t = pts[(i + 1) % n if closed else min(i + 1, n - 1)] - pts[(i - 1) % n if closed else max(i - 1, 0)]
            t /= np.linalg.norm(t)
            u = ups[i] - t * np.dot(ups[i], t)
            u /= np.linalg.norm(u)
            side = np.cross(t, u)
            f = widths[i] if widths is not None else 1.0
            ring = []
            for x, y, _ in profile:
                q = p + side * x * f + u * y + (t * (x * f * cut + abs(cut) * hw) if i == n - 1 else 0.0)
                v = bm.verts.new(q)
                self.vk[v], self.wt[v] = shade, wts[i]
                ring.append(v)
            rings.append(ring)
        m = len(profile)
        for i in range(n if closed else n - 1):
            a, b = rings[i], rings[(i + 1) % n]
            for k in range(m):
                self.fk[bm.faces.new((a[k], b[k], b[(k + 1) % m], a[(k + 1) % m]))] = profile[k][2]
        if not closed:
            for ring, i in ((rings[0], 0), (rings[-1], n - 1)):
                c = bm.verts.new(sum((v.co for v in ring), Vector()) / m)
                self.vk[c], self.wt[c] = shade, wts[i]
                for k in range(m):
                    self.fk[bm.faces.new((ring[k], ring[(k + 1) % m], c))] = 0.85

    def _finish(self, name):
        """Turns the pieces gathered so far into a skinned, coloured object and starts a new one."""
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        for f in bm.faces:
            k0 = self.fk.get(f, 1.0)
            for loop in f.loops:
                k = k0 * self.vk.get(loop.vert, 1.0)
                loop[self.layer] = (self.base[0] * k, self.base[1] * k, self.base[2] * k, 1.0)
        weights = [self.wt[v] for v in bm.verts]
        obj = mesh_from(bm, name)
        set_slot(obj, "char:cloth")
        write_weights(obj, [{b: x for b, x in d.items() if x > 0.01} for d in weights])
        self.bm = bmesh.new()
        self.layer = self.bm.loops.layers.color.new("Color")
        self.vk, self.fk, self.wt = {}, {}, {}
        return obj

    # ------------------------------------------------------------------ skin weights
    def torso_w(self, p):
        """The skin weights of the body nearest ``p``, without the arms'."""
        _, idx, _ = self.kd.find(Vector(p))
        w = {b: x for b, x in self.body_w[idx].items() if b.split(".")[0] not in ARM_BONES} or {"chest": 1.0}
        total = sum(w.values())
        return {b: x / total for b, x in w.items()}

    def _skirt_w(self, p):
        """Hips at the waist, each thigh taking its side of the skirt lower down."""
        j, s = self.j, self.s
        t = ss(j["hip.R"].z + 0.04 * s, j["hip.R"].z - 0.12 * s, p[2])
        side = ss(-0.11 * s, 0.11 * s, p[0])        # spread over the width, so a stride shears it evenly
        return {"hips": 1.0 - t, "thigh.R": t * side, "thigh.L": t * (1.0 - side)}

    def _panel_w(self, P, Z):
        """The bib takes the body's weights under it (smoothed over the panel), the skirt the hips' and thighs'."""
        flat = P.reshape(-1, 3)
        near = [self.torso_w(p) for p in flat]
        skirt = [self._skirt_w(p) for p in flat]
        bones = sorted({b for d in near + skirt for b in d})
        W = np.array([[d.get(b, 0.0) for b in bones] for d in near]).reshape(P.shape[:2] + (len(bones),))
        for _ in range(8):
            p = np.pad(W, ((1, 1), (1, 1), (0, 0)), mode="edge")
            W = (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] + 4 * W) / 8
        S = np.array([[d.get(b, 0.0) for b in bones] for d in skirt]).reshape(W.shape)
        k = ss(self.tie_z + 0.02 * self.s, self.tie_z + 0.07 * self.s, Z)[..., None]      # all hips over the belt
        W = k * W + (1 - k) * S
        W /= W.sum(axis=2, keepdims=True)
        return [[dict(zip(bones, W[i, k])) for k in range(W.shape[1])] for i in range(W.shape[0])]

    # ------------------------------------------------------------------ the parts
    def _panel(self):
        s, hw_, (sa, sb) = self.s, self.hem_w, (STITCH[0] * self.s, STITCH[1] * self.s)
        bw = self.band_w
        zs = _lines([self.top, self.top - sa, self.top - sb, self.top - hw_, self.tie_z + bw / 2 - 0.004 * s, self.tie_z,
                     self.tie_z - bw / 2 + 0.004 * s, self.hem + hw_, self.hem + sb, self.hem + sa, self.hem], 0.02 * s)
        inner = self.skirt_w - hw_
        u = np.linspace(-1.0, 1.0, max(4, math.ceil(2 * inner / (0.018 * s))) + 1)[1:-1]
        edge = np.array([0.0, sa, sb, hw_])
        HW = np.array([self.half_width(z) for z in zs])[:, None]
        X = np.array([np.concatenate([-h + edge, u * (h - hw_), (h - edge)[::-1]]) for h in HW[:, 0]])
        Z = np.repeat(zs[:, None], X.shape[1], 1)
        P, N, crease = self.place(X, Z)
        d = np.minimum(HW - np.abs(X), np.where(Z >= self.tie_z, self.top - Z, Z - self.hem))
        on_stitch = (d > sa - 1e-7) & (d < sb + 1e-7)
        F = P + N * (0.0012 * s * ss(hw_, sb, d) - 0.0004 * s * on_stitch)[..., None]
        C = lambda A: (A[:-1, :-1] + A[1:, :-1] + A[:-1, 1:] + A[1:, 1:]) / 4
        dc = np.minimum(C(HW - np.abs(X)), np.where(C(Z) >= self.tie_z, self.top - C(Z), C(Z) - self.hem))
        self._shell(F, P - N * self.T, N, self._panel_w(P, Z), crease, np.where((dc > sa) & (dc < sb), 0.72, 1.0))
        self._patch_pocket()

    def _patch_pocket(self):
        """A patch pocket on the skirt, stitched round its sides and bottom and down the middle, its mouth open."""
        s, (x0, z0, z1) = self.s, self.pocket
        a, b, top = 0.003 * s, 0.0043 * s, 0.009 * s
        mid = 0.0008 * s
        xs = _lines([x0, x0 - a, x0 - b, mid, -mid, -x0 + b, -x0 + a, -x0], 0.018 * s)[::-1]
        zs = _lines([z0, z0 - top, z0 - top - 0.0013 * s, z1 + b, z1 + a, z1], 0.016 * s)
        X, Z = np.meshgrid(xs, zs)
        P, N, crease = self.place(X, Z)
        gape = 0.0015 * s * ss(z0 - 0.03 * s, z0, Z)          # the mouth stands a little open
        Tp = 0.003 * s
        F = P + N * (0.0012 * s + Tp + gape)[..., None]
        C = lambda A: (A[:-1, :-1] + A[1:, :-1] + A[:-1, 1:] + A[1:, 1:]) / 4
        xc, zc = np.abs(C(X)), C(Z)
        d = np.minimum(x0 - xc, zc - z1)
        stitch = ((d > a) & (d < b)) | (xc < mid) | ((zc < z0 - top) & (zc > z0 - top - 0.0013 * s))
        wts = [[self._skirt_w(p) for p in row] for row in P]
        self._shell(F, F - N * Tp, N, wts, crease, np.where(stitch, 0.72, 1.0), top_rim_k=0.45)

