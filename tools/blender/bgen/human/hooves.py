"""Cloven hooves (``feet: hooves``) for goat-legged creatures (the Krampus): in place of the body's shod foot, a
thick pastern drops steeply from the ankle onto a hoof set just ahead of it, the heel raised off the ground with a
fetlock knob behind; the hoof is a dark horn wedge split into two toes, its front wall sloping back from the toe to
the coronet. The pastern is skin (painted ``faceSkin`` or ``skin``, and furred over above the coronet by a fur coat,
fur.py), the hoof horn is coloured ``nails`` (char:nail); both are skinned like the talons (talons.skin).
"""
from mathutils import Matrix, Vector

from .blobs import Blob

G = 1.35          # the hoof's size against an average foot's scale (a big, heavy hoof)


def _rot(along, across):
    """The rotation taking an ellipsoid's x axis to ``along`` and y to ``across`` (made square to it)."""
    d = along.normalized()
    a = (across - d * across.dot(d)).normalized()
    return Matrix((d, a, d.cross(a))).transposed().to_quaternion()


def _frame(fr, side):
    j, s = fr.j, fr.s
    sx = 1 if side == "R" else -1
    an, heel, toe = j[f"ankle.{side}"], j[f"heel.{side}"], j[f"toe.{side}"]
    fwd = Vector((toe.x - heel.x, toe.y - heel.y, 0)).normalized()
    out = Vector((fwd.y, -fwd.x, 0)) * sx            # toward the outside of the foot
    centre = Vector((an.x, an.y, 0.0)) + fwd * 0.055 * s * G
    return an, fwd, out, centre


def coronet(fr, side):
    """The height (z) of the hoof's top edge, where the pastern's fur stops."""
    return 0.052 * fr.s * G


def build_hoof(fr, side):
    """(pastern mesh, hoof mesh) for one side, unpainted."""
    s = fr.s * G
    an, fwd, out, c = _frame(fr, side)
    up = Vector((0, 0, 1))
    B = Blob(f"foot_{side}", 0.0028 * s)
    top = c + up * 0.046 * s - fwd * 0.004 * s
    B.capsule(an + up * 0.07 * s, an, 0.034 * fr.s)                      # into the leg's end
    B.capsule(an + up * 0.01 * s, top, 0.031 * s)                        # the pastern, steeply down and forward
    B.ball(an - fwd * 0.026 * s + up * 0.006 * s, 0.03 * s)              # the fetlock knob behind
    B.ellipsoid(top - up * 0.004 * s, (0.038 * s, 0.04 * s, 0.016 * s), _rot(fwd, out))       # the coronet's swell
    foot = B.to_mesh(f"foot_{side}", 2400, smooth_iterations=1)

    H = Blob(f"claws_{side}", 0.0022 * s)
    slope = (fwd - up * 0.55).normalized()                               # the front wall leaning back from the toe
    for k in (1, -1):                                                    # the two toes
        toe = c + out * k * 0.019 * s + up * 0.024 * s + fwd * 0.004 * s
        H.ellipsoid(toe, (0.04 * s, 0.021 * s, 0.032 * s), _rot(slope, out))
    H.ellipsoid(c - fwd * 0.012 * s + up * 0.03 * s, (0.03 * s, 0.035 * s, 0.024 * s), _rot(fwd, out))   # the heel bulbs
    H.ellipsoid(c + fwd * 0.03 * s + up * 0.02 * s, (0.04 * s, 0.0035 * s, 0.04 * s), _rot(fwd, out), negative=True)  # the cleft
    H.ellipsoid(c + up * -0.05 * s, (0.2 * s, 0.2 * s, 0.05 * s), negative=True)                     # a flat sole
    hoof = H.to_mesh(f"claws_{side}", 1600, smooth_iterations=1)
    return foot, hoof
