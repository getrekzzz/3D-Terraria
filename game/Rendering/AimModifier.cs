using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Turns a human's upper body toward where it aims, after the animation has posed it: <see cref="Twist"/> turns the
/// torso and head about the vertical (the legs keep facing where they walk), <see cref="Pitch"/> bends the spine and
/// head up or down, and <see cref="ArmAim"/> lets the arms follow the pitch fully (guns, bows, staffs held at the
/// target). Spread down the spine so no single joint kinks. <see cref="Carry"/> turns the right hand to carry a held item
/// between uses.
/// </summary>
public sealed partial class AimModifier : SkeletonModifier3D
{
    private static readonly (string Bone, float Twist, float Pitch)[] Chain =
    {
        ("spine", 0.25f, 0.08f), ("chest", 0.35f, 0.17f), ("neck", 0.15f, 0.3f), ("head", 0.25f, 0.45f),
    };

    private int[] _chain = System.Array.Empty<int>();
    private int _armR = -1, _armL = -1, _handR = -1;

    /// <summary>Torso turn relative to the legs, radians (+ = to the character's left, as Godot yaw).</summary>
    public float Twist { get; set; }

    /// <summary>Aim pitch, radians (+ = up).</summary>
    public float Pitch { get; set; }

    /// <summary>0..1: how much the arms join the aim pitch beyond what the chest gives them.</summary>
    public float ArmAim { get; set; }

    /// <summary>
    /// Radians the right hand turns from the animated grip: X about the hand's own X axis (+ cocks the wrist toward the
    /// thumb, − toward the little finger), Y about its length (+ fans the item out from the body).
    /// </summary>
    public Vector2 Carry { get; set; }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null) return;
        if (_chain.Length == 0)
        {
            _chain = new int[Chain.Length];
            for (int i = 0; i < Chain.Length; i++) _chain[i] = sk.FindBone(Chain[i].Bone);
            _armR = sk.FindBone("upper_arm.R");
            _armL = sk.FindBone("upper_arm.L");
            _handR = sk.FindBone("hand.R");
        }
        if (_handR >= 0 && Carry.LengthSquared() > 1e-6f)
        {
            var carry = new Quaternion(Vector3.Right, Carry.X) * new Quaternion(Vector3.Up, Carry.Y);
            sk.SetBonePoseRotation(_handR, (sk.GetBonePoseRotation(_handR) * carry).Normalized());
        }
        float twist = Mathf.Clamp(Twist, -1.35f, 1.35f), pitch = Mathf.Clamp(Pitch, -1.1f, 1.1f);
        if (Mathf.Abs(twist) < 1e-3f && Mathf.Abs(pitch) < 1e-3f) return;
        float turned = 0f;
        for (int i = 0; i < _chain.Length; i++)
        {
            if (_chain[i] < 0) continue;
            turned += twist * Chain[i].Twist;
            Rotate(sk, _chain[i], twist * Chain[i].Twist, pitch * Chain[i].Pitch, turned);
        }
        if (ArmAim <= 0f) return;
        // The chest already carries the arms by its share of the pitch; bring them the rest of the way.
        float arm = pitch * (1f - Chain[0].Pitch - Chain[1].Pitch) * ArmAim;
        if (_armR >= 0) Rotate(sk, _armR, 0f, arm, turned);
        if (_armL >= 0) Rotate(sk, _armL, 0f, arm, turned);
    }

    /// <summary>Applies a skeleton-space turn (yaw about up, then pitch about the already-turned side axis) at a bone.</summary>
    private static void Rotate(Skeleton3D sk, int bone, float yaw, float pitch, float turned)
    {
        var side = new Quaternion(Vector3.Up, turned) * Vector3.Right;
        var r = new Quaternion(side, pitch) * new Quaternion(Vector3.Up, yaw);
        int parent = sk.GetBoneParent(bone);
        var p = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.GetRotationQuaternion() : Quaternion.Identity;
        sk.SetBonePoseRotation(bone, (p.Inverse() * r * p * sk.GetBonePoseRotation(bone)).Normalized());
    }
}
