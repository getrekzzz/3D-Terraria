using Godot;
using Strata.Core.Gameplay.Town;

namespace Strata.Game.Entities;

/// <summary>
/// Rescue presentation (P5-08): the name tag follows the bound title ("Bound Goblin"), a webbed resident (the Stylist)
/// hangs in a translucent silk cocoon, and the Tortured Soul is a pale, shadowless ghost. Cocoons are made once per
/// view and only shown or hidden afterwards; the state is checked when the bound flag changes, not every frame.
/// </summary>
public sealed partial class TownRenderer
{
    private static readonly Color Ghostly = new(0.6f, 0.8f, 1.3f);
    private static Mesh? _cocoonMesh;

    /// <summary>Re-titles and re-dresses a view when its resident is bound or freed.</summary>
    private void UpdateRescueLook(View view, TownNpc npc)
    {
        if (view.ShownBound == npc.Bound) return;
        view.ShownBound = npc.Bound;
        view.Tag.Text = npc.Title;
        string? pose = npc.Bound ? npc.Def.Rescue?.Pose : null;
        bool webbed = pose == "webbed";
        if (webbed && view.Cocoon == null)
        {
            view.Cocoon = new MeshInstance3D { Name = "Cocoon", Mesh = _cocoonMesh ??= BuildCocoon(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            view.Visual.AddChild(view.Cocoon);
            view.Cocoon.Position = new Vector3(0, 0.62f, 0); // around the kneeling body
        }
        if (view.Cocoon != null) view.Cocoon.Visible = webbed;
        view.Ghost = pose == "tortured";
        view.Visual.SetCastShadows(!view.Ghost);
    }

    /// <summary>The body tint: a red hit flash, the ghostly blue of the Tortured Soul, or none.</summary>
    private static Color TintOf(View view, TownNpc npc) =>
        npc.ImmuneTicks > TownSystem.HitImmunity - 8 ? new Color(2.2f, 0.7f, 0.7f) : view.Ghost ? Ghostly : Colors.White;

    /// <summary>A stretched, translucent off-white ellipsoid of silk.</summary>
    private static Mesh BuildCocoon()
    {
        var mesh = new SphereMesh { Radius = 0.42f, Height = 1.35f, RadialSegments = 16, Rings = 10 };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.92f, 0.88f, 0.62f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return mesh;
    }
}
