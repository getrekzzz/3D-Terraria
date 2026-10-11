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

    /// <summary>Name tags fade in from <see cref="TagNear"/> to <see cref="TagNear"/> + <see cref="TagFade"/> metres from the
    /// camera (up close a tag would sit over the screen edge, under any translucent panel there) and out again toward
    /// <see cref="TagFar"/>.</summary>
    private const float TagNear = 1.5f, TagFade = 1.5f, TagFar = 24f;

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
        // While a screen is open over the world (inventory, chest, chat box, menus) the tags step aside, as the HUD's
        // hover names do.
        bool tags = !UI.InputGate.OverlayOpen && !UI.InputGate.GameplayBlocked;
        var camera = GetViewport()?.GetCamera3D();
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
            ShowTag(view.Tag, tags, camera);
        }
    }

    /// <summary>Shows a tag at its alpha for the camera's distance (hidden while a screen is open or out of range).</summary>
    private static void ShowTag(Label3D tag, bool allowed, Camera3D? camera)
    {
        float alpha = 0f;
        if (allowed && camera != null)
        {
            float d = camera.GlobalPosition.DistanceTo(tag.GlobalPosition);
            alpha = Mathf.Clamp((d - TagNear) / TagFade, 0f, 1f) * Mathf.Clamp((TagFar - d) / (TagFade * 2f), 0f, 1f);
        }
        tag.Visible = alpha > 0.01f;
        if (!tag.Visible || Mathf.IsEqualApprox(tag.Modulate.A, alpha)) return;
        tag.Modulate = tag.Modulate with { A = alpha };
        tag.OutlineModulate = tag.OutlineModulate with { A = alpha };
    }

    /// <summary>A resident's name tag alpha as shown now (probe <c>town.&lt;id&gt;.tag</c>): 0 hidden, -1 not shown at all.</summary>
    public float TagAlpha(string id)
    {
        if (_game.Town.Find(id) is not { } npc || !_views.TryGetValue(npc.Id, out var view)) return -1;
        return view.Tag.Visible ? view.Tag.Modulate.A : 0f;
    }

    /// <summary>
    /// Index in <see cref="HumanoidAnimator.States"/> of the clip a resident's body is playing (probe
    /// <c>town.&lt;id&gt;.clip</c>): -1 when not shown, -2 for a body without the humanoid set.
    /// </summary>
    public int PlayingClip(string id)
    {
        if (_game.Town.Find(id) is not { } npc || !_views.TryGetValue(npc.Id, out var view)) return -1;
        if (view.Visual.Humanoid is not { } h) return -2;
        return System.Array.IndexOf(HumanoidAnimator.States, h.PlayingState);
    }

    /// <summary>Height (m) of a resident's hips bone above their feet as posed now (probe <c>town.&lt;id&gt;.hips</c>), or -1.</summary>
    public float HipsHeight(string id)
    {
        if (_game.Town.Find(id) is not { } npc || !_views.TryGetValue(npc.Id, out var view) || view.Visual.Skeleton is not { } sk) return -1;
        int bone = sk.FindBone("hips");
        return bone < 0 ? -1 : (sk.GlobalTransform * sk.GetBoneGlobalPose(bone)).Origin.Y - view.Visual.GlobalPosition.Y;
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
        // A small name at one size on screen (about 16 px on a 900 px view, Label3D.FixedSize), only near the resident:
        // up close a world-sized tag grew into a huge label over the screen, where Terraria shows no name at all.
        // ShowTag fades it right beside the camera and far away.
        var tag = new Label3D
        {
            Text = npc.Title,
            Font = UI.UiFonts.Bold,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            PixelSize = 0.00065f,
            FontSize = 40,
            OutlineSize = 10,
            Modulate = new Color(0.85f, 0.95f, 1f),
            NoDepthTest = false,
            VisibilityRangeEnd = 24f,
        };
        AddChild(tag);
        var pos = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
        var view = new View { Visual = visual, Tag = tag, Npc = npc, Shown = pos };
        ShowHat(view, npc);
        return view;
    }
}
