using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Town;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>Presents town residents: their rigged model (walk/idle/jump), smoothed motion, a name tag and seasonal hats.</summary>
public sealed partial class TownRenderer : Node3D
{
    private sealed class View
    {
        public required CreatureVisual Visual;
        public required Label3D Tag;
        public required TownNpc Npc;
        public MeshInstance3D? Hat;
        public MeshInstance3D? Cocoon;
        public bool? ShownBound;
        public bool Ghost;
        public Vector3 Shown;
    }

    private readonly Dictionary<long, View> _views = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;

    public void Initialize(GameWorld game, PropLibrary props)
    {
        _game = game;
        _props = props;
        Name = "Residents";
        game.Town.ResidentAttacked += OnResidentAttacked;
        game.Town.Removed += npc =>
        {
            if (!_views.Remove(npc.Id, out var view)) return;
            view.Visual.QueueFree();
            view.Tag.QueueFree();
        };
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.town");
        float dt = (float)delta;
        var world = _game.Session.World;
        UpdateSeason();
        foreach (var npc in _game.Town.Residents)
        {
            if (!_views.TryGetValue(npc.Id, out var view))
            {
                view = Create(npc);
                if (view == null) continue;
                _views[npc.Id] = view;
            }
            view.Visual.Visible = npc.Alive;
            view.Tag.Visible = npc.Alive;
            if (!npc.Alive) continue;
            var target = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
            view.Shown = view.Shown.DistanceSquaredTo(target) > 4f ? target : view.Shown.Lerp(target, 1f - Mathf.Exp(-dt * 20f));
            var visual = view.Visual;
            visual.Position = view.Shown;
            visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, npc.Yaw + Mathf.Pi, 1f - Mathf.Exp(-dt * 10f)), 0);
            string clip = Clip(npc, visual);
            float speed = clip == "walk" ? Mathf.Max(0.6f, new Vector2(npc.Velocity.X, npc.Velocity.Z).Length() / 1.6f) : 1f;
            visual.Play(clip, speed: speed);
            UpdateRescueLook(view, npc);
            visual.SetTint(TintOf(view, npc));
            visual.SetLight(LightProbe.SampleSmooth(world, view.Shown + Vector3.Up));
            view.Tag.Position = view.Shown + new Vector3(0, 2.15f, 0);
        }
    }

    /// <summary>The locomotion state for a resident: a rest pose (sit, sleep, bound, lie) when resting, else walk/jump/idle.</summary>
    private static string Clip(TownNpc npc, CreatureVisual visual)
    {
        string? rest = npc.Posture switch
        {
            ResidentPosture.Sit => "sit",
            ResidentPosture.Sleep => "sleep",
            ResidentPosture.Bound => "bound",
            ResidentPosture.Lie => "lie",
            _ => null,
        };
        if (rest != null && visual.HasAnimation(rest)) return rest;
        return npc.Pose == "walk" ? "walk" : npc.Pose == "jump" && visual.HasAnimation("jump") ? "jump" : "idle";
    }

    private View? Create(TownNpc npc)
    {
        var visual = CreatureVisual.Create(AssetPaths.TownNpcModel(npc.Def), _props);
        if (visual == null) return null;
        AddChild(visual);
        visual.SetTint(Colors.White);
        var tag = new Label3D
        {
            Text = npc.Title,
            Font = UI.UiFonts.Bold,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.003f,
            FontSize = 40,
            OutlineSize = 10,
            Modulate = new Color(0.85f, 0.95f, 1f),
            NoDepthTest = false,
        };
        AddChild(tag);
        var pos = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
        var view = new View { Visual = visual, Tag = tag, Npc = npc, Shown = pos };
        ShowHat(view, npc);
        return view;
    }
}
