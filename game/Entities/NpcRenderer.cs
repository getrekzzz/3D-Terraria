using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Presents creatures: an animated model per creature (pooled by model), smoothed between simulation ticks,
/// tinted per definition, flashing when hurt, plus a small health readout. Motion on top of the clips (facing,
/// banking, squash, attacks, deaths): NpcRenderer.Motion.
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
        public readonly List<CreatureVisual> Barrier = new();
        public readonly List<CreatureVisual> Arms = new();
        public readonly List<CreatureVisual> Illusions = new();
        public string? IllusionModel;
        public Vector3 Shown;
        public float Squash = 1f;
        public float LastVy;
        public Quaternion Turn = Quaternion.Identity;
        public float Yaw, Bank, Stretch, Spin, Whirl, Blink = 1f, Punch;
        public int LastAttackTimer, LastHurtFlash, LastSwing = -1000, LastPunchFlash;
    }

    private readonly Dictionary<long, View> _views = new();
    private readonly Dictionary<string, Stack<CreatureVisual>> _pool = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;

    /// <summary>
    /// Light candidates for <see cref="Rendering.DynamicLights"/>: glowing creatures near the camera. Critters (fireflies)
    /// blink faintly over a short range; the pool keeps only the nearest few whatever the swarm size.
    /// </summary>
    public IEnumerable<Rendering.DynamicLights.Source> Glows(Vector3 eye, float radius)
    {
        foreach (var npc in _game.Npcs.All)
        {
            if (npc.Def.Light is not { Length: 3 } l || !npc.Alive) continue;
            var c = npc.Center;
            var at = new Vector3(c.X, c.Y, c.Z);
            if (at.DistanceTo(eye) > radius) continue;
            int max = System.Math.Max(1, System.Math.Max(l[0], System.Math.Max(l[1], l[2])));
            float peak = max / 15f;
            bool critter = npc.Def.HasTag("critter");
            var color = new Color(l[0] / (float)max, l[1] / (float)max, l[2] / (float)max);
            yield return new Rendering.DynamicLights.Source(at, color, critter ? peak * 0.4f : peak,
                critter ? Rendering.DynamicLights.Kind.Blink : Rendering.DynamicLights.Kind.Steady,
                critter ? 2f : 2f + max / 3f, critter ? 0.12f : 0.5f, (npc.Id * 0.618034f) % 1f,
                () => { var p = npc.Center; return new Vector3(p.X, p.Y, p.Z); });
        }
    }

    public void Initialize(GameWorld game, PropLibrary props)
    {
        _game = game;
        _props = props;
        Name = "Creatures";
        game.Npcs.Removed += npc =>
        {
            if (!_views.Remove(npc.Id, out var view)) return;
            if (!KeepCorpse(npc, view)) Release(view.Visual, view.Model);
            foreach (var (seg, model) in view.Segments) Release(seg, model);
            ReleaseArms(npc, view);
            ReleaseIllusions(view);
            if (npc.Def.Boss?.BarrierModel is { } wall) foreach (var tile in view.Barrier) Release(tile, wall);
            view.Health.QueueFree();
            ReleaseTether(view.Stalk);
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
        // A pool miss instantiates the model (and loads it the first time): the creature-side hitch source.
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.npcs.create");
        var visual = CreatureVisual.Create($"assets/models/creatures/{model}.glb", _props);
        if (visual != null) AddChild(visual);
        return visual;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.npcs");
        float dt = (float)delta;
        var world = _game.Session.World;
        UpdateCorpses(dt);
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
            var wanted = npc.ShownModel;
            if (wanted != view.Model && Acquire(wanted) is { } swapped)
            {
                // A phase change spins into the new form; a disguise drops at once (the chest simply bites).
                bool unmasked = view.Model == npc.Def.Disguise?.Model;
                Release(view.Visual, view.Model);
                view.Visual = swapped;
                view.Model = wanted;
                if (!unmasked) view.Spin = 1f;
            }
            var visual = view.Visual;
            visual.Position = npc.Segments.Count == 0 ? view.Shown + Vector3.Up * visual.LiftFor(npc.HalfExtents.Y * 2f, npc.Def.Scale * npc.VisualScale) : view.Shown;
            if (npc.Segments.Count > 0) UpdateSegments(npc, view, world);
            if (npc.Barrier != null) UpdateBarrier(npc, view, world);
            if (npc.Segments.Count > 0 && npc.Def.Boss!.SegmentModels.Length == 0)
            {
                var v = new Vector3(npc.Velocity.X, npc.Velocity.Y, npc.Velocity.Z);
                if (v.LengthSquared() > 0.01f) visual.Basis = Basis.LookingAt(v.Normalized(), Mathf.Abs(v.Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up);
            }
            else Orient(npc, view, dt);
            Animate(npc, view, dt);
            if (npc.Illusions > 0 || view.Illusions.Count > 0) UpdateIllusions(npc, view);
            if (view.Stalk != null) UpdateStalk(npc, view);
            var tint = Color.FromHtml(npc.Def.Tint);
            // The hurt flash fades with the hit's flash timer instead of blinking off.
            if (npc.HurtFlash > 0) tint = tint.Lerp(new Color(2.2f, 0.7f, 0.7f), Mathf.Clamp(npc.HurtFlash / 8f, 0f, 1f));
            visual.SetTint(tint);
            visual.SetLight(OwnLight(npc, LightProbe.SampleSmooth(world, view.Shown + Vector3.Up * 0.5f)));
            view.Health.Visible = npc.Life < npc.MaxLife;
            if (view.Health.Visible)
            {
                view.Health.Text = $"{npc.Life}/{npc.MaxLife}";
                view.Health.Position = view.Shown + new Vector3(0, npc.HalfExtents.Y * 2f + 0.35f, 0);
            }
        }
        // Arms to boss parts once every view has moved this frame (a hand listed after its body would trail a frame).
        foreach (var npc in _game.Npcs.All)
            if (npc.Segments.Count == 0 && npc.Def.Boss is { Parts.Length: > 0, ArmModel: not null } && _views.TryGetValue(npc.Id, out var view))
                UpdatePartArms(npc, view);
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
        if (!freeParts) SyncSegmentViews(npc, view, segModel);
        while (view.Segments.Count < npc.Segments.Count)
        {
            int index = view.Segments.Count;
            var model = index < boss.SegmentModels.Length ? boss.SegmentModels[index] : boss.ChainModel(index, npc.Segments.Count, segModel);
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
            // A plate that launched its probe goes dark (and no longer fires).
            seg.SetTint(npc.HurtFlash > 0 ? new Color(2.2f, 0.7f, 0.7f) : Strata.Core.AI.WormBrain.Spent(npc, i) ? SpentTint : Colors.White);
            seg.SetLight(OwnLight(npc, LightProbe.SampleSmooth(world, p)));
            ahead = p;
        }
    }

    /// <summary>
    /// A worm that lost segments (split, or its head handed on) drops the extra visuals back into the pool, and its
    /// last visual wears the tail model while the others wear the body's.
    /// </summary>
    private static readonly Color SpentTint = new(0.35f, 0.35f, 0.35f);

    private void SyncSegmentViews(Npc npc, View view, string segModel)
    {
        while (view.Segments.Count > npc.Segments.Count)
        {
            var (visual, model) = view.Segments[^1];
            Release(visual, model);
            view.Segments.RemoveAt(view.Segments.Count - 1);
        }
        var boss = npc.Def.Boss!;
        for (int i = 0; i < view.Segments.Count; i++)
        {
            var want = boss.ChainModel(i, npc.Segments.Count, segModel);
            if (view.Segments[i].Model == want) continue;
            if (Acquire(want) is not { } swapped) continue;
            Release(view.Segments[i].Visual, view.Segments[i].Model);
            view.Segments[i] = (swapped, want);
        }
    }

    private View? Create(Npc npc)
    {
        var model = npc.ShownModel;
        var visual = Acquire(model);
        if (visual == null) return null;
        // Glowing creatures get their light from the shared pool (Rendering.DynamicLights, see Glows); visuals made
        // before that carried their own light, which a pooled visual may still hold.
        if (visual.GetNodeOrNull<OmniLight3D>("Glow") is { } oldGlow) oldGlow.QueueFree();
        var stalk = HasTether(npc) ? AcquireTether(npc.Def.TetherColor) : null;
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
        float yaw = npc.Yaw + Mathf.Pi;
        return new View { Visual = visual, Model = model, Health = health, Shown = pos, Stalk = stalk, Yaw = yaw, Turn = new Quaternion(Vector3.Up, yaw) };
    }
}
