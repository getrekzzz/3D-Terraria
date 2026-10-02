using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;

namespace Strata.Game.Entities;

/// <summary>
/// Mini health bars: a small camera-facing bar over every hurt creature (shaders/ui_healthbar.gdshader), its fill
/// sliding from green through yellow to red. Bosses show the HUD's boss bar instead. All bars share one unit quad and
/// one material; the fill is a per-instance shader parameter, sent only when the creature's life changes, and the bar
/// nodes are pooled with the creature views.
/// </summary>
public sealed partial class NpcRenderer
{
    private const float BarGap = 0.25f, BarMinWidth = 0.6f, BarMaxWidth = 2f;

    private QuadMesh _barMesh = null!;
    private ShaderMaterial _barMaterial = null!;
    private readonly Stack<MeshInstance3D> _barPool = new();

    /// <summary>Health bars visible this frame (probe fx.health_bars).</summary>
    public int HealthBarsShown { get; private set; }

    private void InitializeHealthBars()
    {
        _barMesh = new QuadMesh { Size = Vector2.One };
        _barMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ui_healthbar.gdshader") };
    }

    private MeshInstance3D AcquireBar()
    {
        if (_barPool.Count > 0) return _barPool.Pop();
        var bar = new MeshInstance3D
        {
            Name = "HealthBar",
            Mesh = _barMesh,
            MaterialOverride = _barMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            // The shader turns the quad to face the camera and grows it with distance: keep it from being culled.
            ExtraCullMargin = 2f,
            Visible = false,
        };
        AddChild(bar);
        return bar;
    }

    private void ReleaseBar(View view)
    {
        view.Health.Visible = false;
        view.BarFraction = -1f;
        _barPool.Push(view.Health);
    }

    /// <summary>Shows the bar of a hurt, living, non-boss creature above its head, sized to its width.</summary>
    private void UpdateHealthBar(Npc npc, View view)
    {
        var bar = view.Health;
        bool show = npc.Alive && npc.Life > 0 && npc.Life < npc.MaxLife && !npc.IsBoss;
        if (bar.Visible != show) bar.Visible = show;
        if (!show) return;
        HealthBarsShown++;
        float width = Mathf.Clamp(System.Math.Max(npc.HalfExtents.X, npc.HalfExtents.Z) * 2f, BarMinWidth, BarMaxWidth);
        float height = Mathf.Clamp(width * 0.125f, 0.08f, 0.16f);
        bar.Transform = new Transform3D(Basis.FromScale(new Vector3(width, height, 1f)),
            view.Shown + new Vector3(0, npc.HalfExtents.Y * 2f + BarGap + height * 0.5f, 0));
        float fraction = npc.MaxLife > 0 ? (float)npc.Life / npc.MaxLife : 0f;
        if (fraction == view.BarFraction) return;
        view.BarFraction = fraction;
        bar.SetInstanceShaderParameter("fraction", fraction);
    }
}
