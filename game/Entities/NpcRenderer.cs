using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Presents creatures: an animated model per creature (pooled by model), smoothed between simulation ticks,
/// tinted per definition, flashing when hurt, gels squashing on landing, plus a small health readout.
/// </summary>
public sealed partial class NpcRenderer : Node3D
{
    private sealed class View
    {
        public required CreatureVisual Visual;
        public required string Model;
        public required Label3D Health;
        public MeshInstance3D? Stalk;
        public readonly List<(CreatureVisual Visual, string Model)> Segments = new();
        public Vector3 Shown;
        public float Squash = 1f;
        public float LastVy;
    }

    private readonly Dictionary<long, View> _views = new();
    private readonly Dictionary<string, Stack<CreatureVisual>> _pool = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;

    public void Initialize(GameWorld game, PropLibrary props)
    {
        _game = game;
        _props = props;
        Name = "Creatures";
        game.Npcs.Removed += npc =>
        {
            if (!_views.Remove(npc.Id, out var view)) return;
            Release(view.Visual, view.Model);
            foreach (var (seg, model) in view.Segments) Release(seg, model);
            view.Health.QueueFree();
            view.Stalk?.QueueFree();
        };
    }

    private void Release(CreatureVisual visual, string model)
    {
        visual.Visible = false;
        if (!_pool.TryGetValue(model, out var stack)) _pool[model] = stack = new Stack<CreatureVisual>();
        stack.Push(visual);
    }

    private CreatureVisual? Acquire(string model)
    {
        if (_pool.TryGetValue(model, out var stack) && stack.Count > 0)
        {
            var pooled = stack.Pop();
            pooled.Visible = true;
            return pooled;
        }
        var visual = CreatureVisual.Create($"assets/models/creatures/{model}.glb", _props);
        if (visual != null) AddChild(visual);
        return visual;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var world = _game.Session.World;
        foreach (var npc in _game.Npcs.All)
        {
            if (!_views.TryGetValue(npc.Id, out var view))
            {
                view = Create(npc);
                if (view == null) continue;
                _views[npc.Id] = view;
            }
            var target = npc.Segments.Count > 0
                ? new Vector3(npc.Center.X, npc.Center.Y, npc.Center.Z)
                : new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
            view.Shown = view.Shown.DistanceSquaredTo(target) > 4f ? target : view.Shown.Lerp(target, 1f - Mathf.Exp(-dt * 25f));
            var wanted = npc.ModelOverride ?? npc.Def.Model ?? npc.Def.Id;
            if (wanted != view.Model && Acquire(wanted) is { } swapped)
            {
                Release(view.Visual, view.Model);
                view.Visual = swapped;
                view.Model = wanted;
            }
            var visual = view.Visual;
            visual.Position = view.Shown;
            if (npc.Segments.Count > 0) UpdateSegments(npc, view, world);
            if (npc.Segments.Count > 0 && npc.Def.Boss!.SegmentModels.Length == 0)
            {
                var v = new Vector3(npc.Velocity.X, npc.Velocity.Y, npc.Velocity.Z);
                if (v.LengthSquared() > 0.01f) visual.Basis = Basis.LookingAt(v.Normalized(), Mathf.Abs(v.Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up);
            }
            else
            {
                visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, npc.Yaw + Mathf.Pi, 1f - Mathf.Exp(-dt * 10f)), 0);
            }
            Animate(npc, view, dt);
            if (view.Stalk != null) UpdateStalk(npc, view);
            var tint = Color.FromHtml(npc.Def.Tint);
            if (npc.HurtFlash > 0) tint = new Color(2.2f, 0.7f, 0.7f);
            visual.SetTint(tint);
            visual.SetLight(LightProbe.Sample(world, view.Shown + Vector3.Up * 0.5f));
            view.Health.Visible = npc.Life < npc.MaxLife;
            if (view.Health.Visible)
            {
                view.Health.Text = $"{npc.Life}/{npc.MaxLife}";
                view.Health.Position = view.Shown + new Vector3(0, npc.HalfExtents.Y * 2f + 0.35f, 0);
            }
        }
    }

    /// <summary>
    /// Body segments: a chain (worms) trails the head with each piece facing the one in front; free parts with their
    /// own models (the warden's hands) face where the head faces, the second of a pair mirrored.
    /// </summary>
    private void UpdateSegments(Npc npc, View view, Strata.Core.Voxels.VoxelWorld world)
    {
        var boss = npc.Def.Boss!;
        var segModel = boss.SegmentModel ?? view.Model;
        bool freeParts = boss.SegmentModels.Length > 0;
        while (view.Segments.Count < npc.Segments.Count)
        {
            int index = view.Segments.Count;
            bool tail = index == npc.Segments.Count - 1;
            var model = index < boss.SegmentModels.Length ? boss.SegmentModels[index] : tail ? boss.TailModel ?? segModel : segModel;
            if (Acquire(model) is not { } seg) return;
            view.Segments.Add((seg, model));
        }
        var ahead = view.Shown;
        for (int i = 0; i < npc.Segments.Count; i++)
        {
            var p = new Vector3(npc.Segments[i].X, npc.Segments[i].Y, npc.Segments[i].Z);
            var seg = view.Segments[i].Visual;
            seg.Position = p;
            if (freeParts)
            {
                var basis = new Basis(Vector3.Up, npc.Yaw + Mathf.Pi);
                seg.Basis = i % 2 == 1 ? basis.Scaled(new Vector3(-1, 1, 1)) : basis;
            }
            var dir = ahead - p;
            if (!freeParts && dir.LengthSquared() > 1e-4f) seg.Basis = Basis.LookingAt(dir.Normalized(), Mathf.Abs(dir.Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up);
            seg.SetTint(npc.HurtFlash > 0 ? new Color(2.2f, 0.7f, 0.7f) : Colors.White);
            seg.SetLight(LightProbe.Sample(world, p));
            ahead = p;
        }
    }

    private void Animate(Npc npc, View view, float dt)
    {
        var visual = view.Visual;
        if (npc.Def.Brain.Type == Strata.Core.Content.Specs.BrainSpecs.SlimeHop)
        {
            // Squash on landing, stretch while rising.
            if (npc.OnGround && view.LastVy < -2f) view.Squash = 0.65f;
            float want = npc.OnGround ? 1f : Mathf.Clamp(1f + npc.Velocity.Y * 0.04f, 0.85f, 1.25f);
            view.Squash = Mathf.Lerp(view.Squash, want, 1f - Mathf.Exp(-dt * 12f));
            float s = npc.Def.Scale;
            visual.Scale = new Vector3(s / Mathf.Sqrt(view.Squash), s * view.Squash, s / Mathf.Sqrt(view.Squash));
            view.LastVy = npc.Velocity.Y;
            return;
        }
        visual.Scale = Vector3.One * npc.Def.Scale;
        string clip = npc.Pose switch
        {
            "move" => visual.HasAnimation("walk") ? "walk" : "fly",
            "attack" => visual.HasAnimation("fly") ? "fly" : "walk",
            "jump" => "jump",
            _ => visual.HasAnimation("idle") ? "idle" : "fly",
        };
        float speed = clip == "walk" ? Mathf.Max(0.5f, new Vector2(npc.Velocity.X, npc.Velocity.Z).Length() / 2.5f) : (clip == "fly" && npc.Pose == "attack" ? 2f : 1f);
        visual.Play(clip, speed: speed);
        if (!visual.HasAnimation("fly") && !visual.HasAnimation("walk") && npc.Def.Gravity == false)
            visual.Position += new Vector3(0, Mathf.Sin(npc.Age * 0.08f) * 0.08f, 0); // bobbing eye
    }

    /// <summary>Rooted creatures (snatchers): a vine from the anchor to the head.</summary>
    private static void UpdateStalk(Npc npc, View view)
    {
        var root = new Vector3(npc.Anchor.X, npc.Anchor.Y, npc.Anchor.Z);
        var head = view.Shown + new Vector3(0, npc.HalfExtents.Y * 0.8f, 0);
        var span = head - root;
        float len = span.Length();
        view.Stalk!.Visible = len > 0.05f;
        if (len <= 0.05f) return;
        var up = span / len;
        var side = Mathf.Abs(up.Y) > 0.95f ? Vector3.Right : up.Cross(Vector3.Up).Normalized();
        var basis = new Basis(side, up, side.Cross(up)).Scaled(new Vector3(1, len, 1));
        view.Stalk.GlobalTransform = new Transform3D(basis, root + span * 0.5f);
    }

    private static MeshInstance3D NewStalk() => new()
    {
        Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.09f, Height = 1f, RadialSegments = 6, Rings = 1 },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.45f, 0.15f), Roughness = 0.9f },
    };

    private View? Create(Npc npc)
    {
        var model = npc.Def.Model ?? npc.Def.Id;
        var visual = Acquire(model);
        if (visual == null) return null;
        // Visuals are pooled per model, so a reused one may carry another creature's glow.
        var glow = visual.GetNodeOrNull<OmniLight3D>("Glow");
        if (npc.Def.Light is { Length: 3 } l)
        {
            if (glow == null)
            {
                glow = new OmniLight3D { Name = "Glow", LightEnergy = 0.8f, ShadowEnabled = false };
                visual.AddChild(glow);
            }
            glow.LightColor = new Color(l[0] / 15f, l[1] / 15f, l[2] / 15f);
            glow.OmniRange = 2f + Mathf.Max(l[0], Mathf.Max(l[1], l[2])) / 3f;
            glow.Position = new Vector3(0, npc.HalfExtents.Y, 0);
            glow.Visible = true;
        }
        else if (glow != null)
        {
            glow.Visible = false;
        }
        MeshInstance3D? stalk = null;
        if (npc.Def.Brain.Type == Strata.Core.Content.Specs.BrainSpecs.Lunger)
        {
            stalk = NewStalk();
            AddChild(stalk);
        }
        var health = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.004f,
            FontSize = 48,
            OutlineSize = 12,
            Modulate = new Color(1f, 0.45f, 0.45f),
            NoDepthTest = false,
            Visible = false,
        };
        AddChild(health);
        var pos = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
        return new View { Visual = visual, Model = model, Health = health, Shown = pos, Stalk = stalk };
    }
}
