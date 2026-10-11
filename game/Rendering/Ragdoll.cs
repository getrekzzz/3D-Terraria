using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// A physics ragdoll on a human skeleton (X-16): fifteen capsule bodies (hips, spine, chest, head, arms, legs) under a
/// <see cref="PhysicalBoneSimulator3D"/>, built once per pooled visual and idle until <see cref="Start"/>. The bodies
/// collide with terrain and placed objects only (not each other, the player or creatures). <see cref="Drop"/> lets them
/// fall through the ground (the corpse sinks out of sight); <see cref="Stop"/> hands the bones back to animation.
/// </summary>
public sealed class Ragdoll
{
    /// <summary>Physics layer of ragdoll bodies (nothing else collides with it).</summary>
    public const uint Layer = 1u << 9;

    private static readonly uint Ground = World.ColliderManager.TerrainLayer | World.ColliderManager.ObjectLayer;

    /// <summary>(bone, the bone its capsule reaches to (null: a ball), radius, mass, hinge joint).</summary>
    private static readonly (string Bone, string? To, float Radius, float Mass, bool Hinge)[] Parts =
    {
        ("hips", "spine", 0.13f, 8f, false), ("spine", "chest", 0.12f, 6f, false), ("chest", "neck", 0.14f, 8f, false),
        ("head", null, 0.11f, 4f, false),
        ("upper_arm.L", "forearm.L", 0.05f, 2f, false), ("forearm.L", "hand.L", 0.045f, 1.5f, true),
        ("upper_arm.R", "forearm.R", 0.05f, 2f, false), ("forearm.R", "hand.R", 0.045f, 1.5f, true),
        ("thigh.L", "shin.L", 0.07f, 5f, false), ("shin.L", "foot.L", 0.055f, 3.5f, true), ("foot.L", "toe.L", 0.045f, 1f, true),
        ("thigh.R", "shin.R", 0.07f, 5f, false), ("shin.R", "foot.R", 0.055f, 3.5f, true), ("foot.R", "toe.R", 0.045f, 1f, true),
        ("neck", "head", 0.06f, 1.5f, false),
    };

    private static readonly StringName BoneNameProperty = "bone_name";
    private static readonly StringName SwingSpan = "joint_constraints/swing_span", TwistSpan = "joint_constraints/twist_span";
    private static readonly StringName AngularLimitEnabled = "joint_constraints/angular_limit_enabled";
    private static readonly StringName AngularLimitLower = "joint_constraints/angular_limit_lower", AngularLimitUpper = "joint_constraints/angular_limit_upper";

    private readonly PhysicalBoneSimulator3D _sim;
    private readonly List<PhysicalBone3D> _bodies = new();

    private Ragdoll(PhysicalBoneSimulator3D sim) => _sim = sim;

    public bool Running { get; private set; }

    private float _startHips;

    /// <summary>How far the hips have dropped since <see cref="Start"/> (metres; probe <c>fx.ragdoll_drop</c>).</summary>
    public float Dropped => _bodies.Count > 0 ? _startHips - _bodies[0].GlobalPosition.Y : 0f;

    /// <summary>The ragdoll for a human skeleton, or null when it lacks the bones.</summary>
    public static Ragdoll? Build(Skeleton3D skeleton)
    {
        if (skeleton.FindBone("hips") < 0 || skeleton.FindBone("head") < 0) return null;
        var sim = new PhysicalBoneSimulator3D { Name = "Ragdoll", Active = false };
        skeleton.AddChild(sim);
        var ragdoll = new Ragdoll(sim);
        foreach (var (bone, to, radius, mass, hinge) in Parts)
        {
            int index = skeleton.FindBone(bone);
            if (index < 0) continue;
            int child = to == null ? -1 : skeleton.FindBone(to);
            float length = child >= 0 ? skeleton.GetBoneRest(child).Origin.Length() : radius * 2f;
            var body = new PhysicalBone3D
            {
                Name = bone,
                Mass = mass,
                Friction = 0.9f,
                LinearDamp = 0.2f,
                AngularDamp = 2f,
                CollisionLayer = Layer,
                CollisionMask = Ground,
                JointType = hinge ? PhysicalBone3D.JointTypeEnum.Hinge : PhysicalBone3D.JointTypeEnum.Cone,
            };
            body.Set(BoneNameProperty, bone);
            // Loose enough to crumple: cones swing 75° (twist 40°), hinges (elbows, knees, ankles) bend 140° one way.
            if (hinge)
            {
                body.Set(AngularLimitEnabled, true);
                body.Set(AngularLimitLower, -140f);
                body.Set(AngularLimitUpper, 5f);
            }
            else
            {
                body.Set(SwingSpan, 75f);
                body.Set(TwistSpan, 40f);
            }
            // Bones point along their local +Y: the capsule spans the bone from its joint to its child.
            Shape3D shape = to == null
                ? new SphereShape3D { Radius = radius }
                : new CapsuleShape3D { Radius = radius, Height = Mathf.Max(length, radius * 2.05f) };
            body.AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, to == null ? radius : length * 0.5f, 0) });
            sim.AddChild(body);
            ragdoll._bodies.Add(body);
        }
        return ragdoll;
    }

    /// <summary>
    /// Takes over the bones from the current pose and throws the body along <paramref name="impulse"/> (m/s); the
    /// head and chest also get <paramref name="topple"/> so even a still body folds over.
    /// </summary>
    public void Start(Vector3 impulse, Vector3 topple)
    {
        Running = true;
        _startHips = _bodies.Count > 0 ? _bodies[0].GlobalPosition.Y : 0f;
        foreach (var b in _bodies) b.CollisionMask = Ground;
        _sim.Active = true;
        _sim.Influence = 1f;
        _sim.PhysicalBonesStartSimulation();
        foreach (var b in _bodies)
        {
            var push = impulse;
            if (b.Name == "head" || b.Name == "chest") push += topple;
            b.ApplyCentralImpulse(push * b.Mass);
        }
    }

    /// <summary>Lets the body fall through the ground (the end of a corpse).</summary>
    public void Drop()
    {
        foreach (var b in _bodies) b.CollisionMask = 0;
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _sim.PhysicalBonesStopSimulation();
        _sim.Active = false;
    }
}
