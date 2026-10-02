using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Pooled projectile lights (P1b-15): the graphics preset's <c>projectileLights</c> lights go each frame to the glowing
/// shots (and glowing minions) nearest the camera; every other shot only glows. No light is made per projectile.
/// </summary>
public sealed partial class ProjectileRenderer
{
    private readonly List<OmniLight3D> _lights = new();
    private readonly List<(float Distance, Projectile Shot)> _lit = new();

    /// <summary>Pooled lights in use this frame (probe <c>projectiles.lights</c>).</summary>
    public int LightsUsed { get; private set; }

    /// <summary>Sizes the light pool to the preset and follows preset changes.</summary>
    public void UseLights(GraphicsSettings graphics)
    {
        SetLightCount(graphics.Preset.ProjectileLights);
        graphics.Changed += preset => SetLightCount(preset.ProjectileLights);
    }

    private void SetLightCount(int count)
    {
        while (_lights.Count > count)
        {
            _lights[^1].QueueFree();
            _lights.RemoveAt(_lights.Count - 1);
        }
        while (_lights.Count < count)
        {
            var light = new OmniLight3D { OmniRange = 4f, LightEnergy = 1.5f, ShadowEnabled = false, Visible = false };
            AddChild(light);
            _lights.Add(light);
        }
    }

    private void PlaceLights()
    {
        LightsUsed = 0;
        if (_lights.Count == 0) return;
        var camera = GetViewport()?.GetCamera3D();
        var eye = camera?.GlobalPosition ?? Vector3.Zero;
        _lit.Clear();
        foreach (var p in _game.Projectiles.All)
        {
            if (p.PetSlot != null || p.Def.Light is not { Length: 3 } || !_views.ContainsKey(p.Id)) continue;
            _lit.Add((eye.DistanceSquaredTo(new Vector3(p.Position.X, p.Position.Y, p.Position.Z)), p));
        }
        if (_lit.Count > _lights.Count) _lit.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        for (int i = 0; i < _lights.Count; i++)
        {
            var light = _lights[i];
            if (i >= _lit.Count)
            {
                light.Visible = false;
                continue;
            }
            var p = _lit[i].Shot;
            var l = p.Def.Light!;
            light.LightColor = new Color(l[0] / 15f, l[1] / 15f, l[2] / 15f);
            light.GlobalPosition = _views[p.Id].GlobalPosition;
            light.Visible = true;
            LightsUsed++;
        }
    }
}
