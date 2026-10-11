using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Player;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Strings and chains (P3-12/13): a tethered projectile hangs on a line in its tether's colour from the hand (the held
/// model's <c>string</c> socket in the active view for the local player, else just ahead of the owner's chest) to
/// where it is drawn. One shared line mesh for all of them.
/// </summary>
public sealed partial class ProjectileRenderer
{
    private ImmediateMesh? _strings, _chains;
    private readonly List<(Vector3 From, Vector3 To, Color Color, bool Chain)> _stringQueue = new();

    /// <summary>Strings drawn this frame (probe <c>projectiles.strings</c>).</summary>
    public int StringsDrawn { get; private set; }

    /// <summary>Queues the string of a tethered projectile drawn at <paramref name="at"/>.</summary>
    private void QueueString(Projectile p, Vector3 at)
    {
        if (p.Def.Tether is not { } tether || p.Owner is not { } owner) return;
        Vector3 hand;
        bool chain = tether.Kind == "flail";
        if (owner == _game.LocalPlayer && (HeldSockets.TryNamed(chain ? "chain" : "string", out hand) || HeldSockets.TryNamed("grip", out hand))) { }
        else
        {
            var aim = new Vector3(owner.Aim.X, 0f, owner.Aim.Z);
            aim = aim.LengthSquared() > 1e-4f ? aim.Normalized() : Vector3.Forward;
            hand = new Vector3(owner.Position.X, owner.Position.Y + 1.2f, owner.Position.Z) + aim * 0.45f;
        }
        _stringQueue.Add((hand, at, Color.FromHtml(tether.String), chain));
    }

    private void DrawStrings()
    {
        StringsDrawn = _stringQueue.Count;
        if (_strings == null)
        {
            if (_stringQueue.Count == 0) return;
            _strings = new ImmediateMesh();
            _chains = new ImmediateMesh();
            AddChild(new MeshInstance3D
            {
                Mesh = _strings, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true },
            });
            AddChild(new MeshInstance3D
            {
                Mesh = _chains, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = 0.7f, Roughness = 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            });
        }
        _strings.ClearSurfaces();
        _chains!.ClearSurfaces();
        bool lines = false, links = false;
        foreach (var (from, to, color, chain) in _stringQueue)
        {
            if (chain)
            {
                if (!links) _chains.SurfaceBegin(Mesh.PrimitiveType.Triangles);
                links = true;
                ChainStrip.Links(_chains, from, to, color, 0.1f, 0.03f);
                continue;
            }
            if (!lines) _strings.SurfaceBegin(Mesh.PrimitiveType.Lines);
            lines = true;
            _strings.SurfaceSetColor(color);
            _strings.SurfaceAddVertex(from);
            _strings.SurfaceSetColor(color);
            _strings.SurfaceAddVertex(to);
        }
        if (lines) _strings.SurfaceEnd();
        if (links) _chains.SurfaceEnd();
        _stringQueue.Clear();
    }
}
