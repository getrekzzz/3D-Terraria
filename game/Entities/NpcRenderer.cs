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
            view.Visual.Visible = false;
            view.Health.QueueFree();
            if (!_pool.TryGetValue(view.Model, out var stack)) _pool[view.Model] = stack = new Stack<CreatureVisual>();
            stack.Push(view.Visual);
        };
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
            var target = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
            view.Shown = view.Shown.DistanceSquaredTo(target) > 4f ? target : view.Shown.Lerp(target, 1f - Mathf.Exp(-dt * 25f));
            var visual = view.Visual;
            visual.Position = view.Shown;
            visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, npc.Yaw + Mathf.Pi, 1f - Mathf.Exp(-dt * 10f)), 0);
            Animate(npc, view, dt);
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

    private View? Create(Npc npc)
    {
        var model = npc.Def.Model ?? npc.Def.Id;
        CreatureVisual? visual = null;
        if (_pool.TryGetValue(model, out var stack) && stack.Count > 0) visual = stack.Pop();
        if (visual == null)
        {
            visual = CreatureVisual.Create(AssetPaths.CreatureModel(npc.Def), _props);
            if (visual == null) return null;
            AddChild(visual);
        }
        visual.Visible = true;
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
        return new View { Visual = visual, Model = model, Health = health, Shown = pos };
    }
}
