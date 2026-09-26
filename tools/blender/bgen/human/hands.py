"""Hands: palm with thenar and hypothenar pads, knuckle row, three phalanges per finger with knuckle bulges and
fingertip pads, a two-joint thumb, and fingernails. Built per side at a fine blob resolution.
"""
import bmesh
import bpy
from mathutils import Matrix, Vector

from .blobs import Blob

RADII = {
    "thumb": (0.0112, 0.0099, 0.0089),
    "index": (0.0087, 0.0078, 0.0071),
    "middle": (0.009, 0.008, 0.0073),
    "ring": (0.0085, 0.0076, 0.0069),
    "pinky": (0.0076, 0.0068, 0.0062),
}
ORDER = ("index", "middle", "ring", "pinky")


def hand_axes(frame, side):
    sx = 1 if side == "R" else -1
    along = frame.arm_dir[side]
    thumb = Vector((0, 1, 0))
    thumb = (thumb - along * thumb.dot(along)).normalized()
    dorsal = along.cross(thumb) * sx
    return along, thumb, dorsal.normalized()


def _rot(x, y, z):
    return Matrix((x, y, z)).transposed().to_quaternion()


def build_hand(frame, side):
    """Returns (hand mesh object, nail mesh object) for one side."""
    j = frame.j
    k = frame.s * (0.9 if frame.female else 1.0)
    along, thumb, dorsal = hand_axes(frame, side)
    rot = _rot(along, thumb, dorsal)
    wrist = j[f"wrist.{side}"]
    B = Blob(f"hand_{side}", 0.0021 * frame.s)
    B.ellipsoid(wrist - along * 0.018 * k, (0.034 * k, 0.027 * k, 0.019 * k), rot)             # wrist
    B.ellipsoid(wrist + along * 0.052 * k - dorsal * 0.002 * k, (0.044 * k, 0.041 * k, 0.0145 * k), rot)  # palm
    B.ellipsoid(j[f"thumb1.{side}"] + along * 0.018 * k - dorsal * 0.007 * k + thumb * 0.002 * k,
                (0.026 * k, 0.015 * k, 0.012 * k), rot)                                            # thenar pad
    B.ellipsoid(wrist + along * 0.05 * k - thumb * 0.029 * k - dorsal * 0.005 * k, (0.032 * k, 0.011 * k, 0.011 * k), rot)  # hypothenar
    B.capsule(j[f"index1.{side}"] - along * 0.004 * k, j[f"pinky1.{side}"] - along * 0.004 * k, 0.0105 * k)  # knuckle row
    for f, radii in RADII.items():
        pts = [j[f"{f}{n}.{side}"] for n in (1, 2, 3)] + [j[f"{f}_tip.{side}"]]
        for n in range(3):
            a, b = pts[n], pts[n + 1]
            d = (b - a).normalized()
            B.capsule(a, b - d * radii[n] * k * 0.4, radii[n] * k)
            B.ball(a + dorsal * radii[n] * k * 0.25, radii[n] * k * 1.08)           # knuckle
        d = (pts[3] - pts[2]).normalized()
        B.ball(pts[3] - d * radii[2] * k * 0.9 - dorsal * radii[2] * k * 0.2, radii[2] * k * 0.98)  # fingertip pad
    # Carve the gaps between neighbouring fingers (from just past the web to the tips) so they stay separate.
    for a, b in zip(ORDER, ORDER[1:]):
        for n in (2, 3):
            pa = (j[f"{a}{n}.{side}"] + j[f"{b}{n}.{side}"]) / 2
            nxt = f"{a}_tip.{side}" if n == 3 else f"{a}{n + 1}.{side}"
            nxt_b = f"{b}_tip.{side}" if n == 3 else f"{b}{n + 1}.{side}"
            pb = (j[nxt] + j[nxt_b]) / 2
            B.capsule(pa - (pb - pa) * 0.35, pb + (pb - pa) * 0.3, 0.0021 * k, negative=True)
    hand = B.to_mesh(f"hand_{side}", 3600, smooth_iterations=1)
    nails = _nails(frame, side, along, dorsal, k)
    return hand, nails


def _nails(frame, side, along, dorsal, k):
    """Slightly domed nail plates on the back of each fingertip."""
    j = frame.j
    bm = bmesh.new()
    for f, radii in RADII.items():
        tip = j[f"{f}_tip.{side}"]
        base = j[f"{f}3.{side}"]
        d = (tip - base).normalized()
        across = d.cross(dorsal).normalized()
        r = radii[2] * k
        length, width = r * 1.6, r * 1.4
        # The nail's dorsal direction follows the finger (the thumb is rotated about its own axis).
        up = (dorsal - d * dorsal.dot(d)).normalized()
        if f == "thumb":
            up = (up * 0.6 + across * (0.8 if side == "R" else -0.8)).normalized()
            across = d.cross(up).normalized()
        centre = tip - d * (r * 0.75 + length * 0.5) + up * r * 1.0
        grid = []
        n = 5
        for a in range(n):
            row = []
            for b in range(n):
                u = a / (n - 1) - 0.5
                v = b / (n - 1) - 0.5
                dome = (1 - (2 * v) ** 2) * r * 0.12
                row.append(bm.verts.new(centre + d * u * length + across * v * width * (1 - 0.25 * max(0, -u) ** 2) + up * dome))
            grid.append(row)
        for a in range(n - 1):
            for b in range(n - 1):
                face = bm.faces.new((grid[a][b], grid[a + 1][b], grid[a + 1][b + 1], grid[a][b + 1]))
                face.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for face in bm.faces:
        if face.normal.dot(dorsal) < 0 and face.normal.length > 0:
            face.normal_flip()
    me = bpy.data.meshes.new(f"nails_{side}")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(f"nails_{side}", me)
    bpy.context.scene.collection.objects.link(obj)
    return obj
