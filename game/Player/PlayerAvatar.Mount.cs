using Godot;
using Strata.Core.Gameplay.Mounts;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// The ridden mount: its creature model (scaled to the mount's height, tinted) under the rider, playing its own clips
/// (running, hopping, flying), and the rider lifted onto its saddle. Models are cached per mount id.
/// </summary>
public sealed partial class PlayerAvatar
{
    private static readonly string[] AirClips = { "fly", "jump", "hop", "walk", "idle" };
    private static readonly string[] RunClips = { "run", "walk", "hop", "fly", "idle" };
    private static readonly string[] IdleClips = { "idle", "fly", "walk" };
    private readonly System.Collections.Generic.Dictionary<string, CreatureVisual?> _mounts = new();
    private CreatureVisual? _mountShown;

    private void UpdateMount(MountRide? ride)
    {
        var shown = ride == null ? null : MountVisual(ride.Config);
        if (shown != _mountShown)
        {
            if (_mountShown != null) _mountShown.Visible = false;
            if (shown != null) shown.Visible = true;
            _mountShown = shown;
        }
        if (_visual != null) _visual.Position = ride == null ? Vector3.Zero : Vector3.Up * ride.Config.Seat;
        if (ride == null || shown == null) return;
        var v = _controller.Velocity;
        float ground = new Vector2(v.X, v.Z).Length();
        string clip = ride.Flying || !_controller.IsOnFloor() ? First(shown, AirClips)
            : ground > 0.4f ? First(shown, RunClips) : First(shown, IdleClips);
        shown.Play(clip, speed: ground > 0.4f ? Mathf.Clamp(ground / 4f, 0.7f, 2.2f) : 1f);
        shown.SetLight(PlayerLight.Brighten(LightProbe.SampleSmooth(_game.Session.World, GlobalPosition + Vector3.Up * 0.5f), _controller.Entity));
    }

    private CreatureVisual? MountVisual(MountParams mount)
    {
        if (_mounts.TryGetValue(mount.Id, out var cached)) return cached;
        var visual = CreatureVisual.Create($"assets/models/creatures/{mount.Model}.glb", _props);
        if (visual != null)
        {
            AddChild(visual);
            float height = Mathf.Max(visual.Top - visual.Bottom, 0.05f);
            float scale = mount.Height / height;
            visual.Scale = Vector3.One * scale;
            visual.Position = Vector3.Up * -visual.Bottom * scale; // feet on the ground
            if (mount.Tint != null) visual.SetTint(Color.FromHtml(mount.Tint));
            visual.Visible = false;
        }
        return _mounts[mount.Id] = visual;
    }

    private static string First(CreatureVisual visual, string[] clips)
    {
        foreach (var c in clips)
            if (visual.HasAnimation(c)) return c;
        return clips[^1];
    }
}
