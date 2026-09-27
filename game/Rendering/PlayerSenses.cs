using System.Collections.Generic;
using Godot;
using Strata.Core.Session;

namespace Strata.Game.Rendering;

/// <summary>
/// What the local player's buffs let them sense: night vision (Night Owl) lifts a soft floor of light into the dark,
/// and Spelunker makes every ore within reach glow through the rock.
/// </summary>
public sealed partial class PlayerSenses : Node3D
{
    public const int OreRadius = 16;
    public const int MaxGlows = 600;

    private GameWorld _game = null!;
    private PlayerEntity _player = null!;
    private MultiMeshInstance3D _glows = null!;
    private double _scan;
    private float _owl;

    public void Initialize(GameWorld game, PlayerEntity player)
    {
        Name = "PlayerSenses";
        _game = game;
        _player = player;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 10,
        };
        var mesh = new BoxMesh { Size = Vector3.One * 0.18f, Material = material };
        _glows = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = MaxGlows, VisibleInstanceCount = 0 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_glows);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.senses");
        var info = _player.State.Effects.Info;
        float want = info.Contains("night_vision") ? 1f : 0f;
        _owl = Mathf.MoveToward(_owl, want, (float)delta * 2f);
        RenderingServer.GlobalShaderParameterSet("strata_night_vision", _owl);

        bool spelunk = info.Contains("ores");
        if (!spelunk)
        {
            _glows.Multimesh.VisibleInstanceCount = 0;
            return;
        }
        _scan -= delta;
        if (_scan > 0) return;
        _scan = 0.5;
        Scan();
    }

    private void Scan()
    {
        var session = _game.Session;
        var world = session.World;
        var blocks = session.Content.Blocks;
        var c = _player.FeetCell;
        int count = 0;
        var mm = _glows.Multimesh;
        for (int dy = -OreRadius; dy <= OreRadius && count < MaxGlows; dy++)
            for (int dz = -OreRadius; dz <= OreRadius && count < MaxGlows; dz++)
                for (int dx = -OreRadius; dx <= OreRadius && count < MaxGlows; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > OreRadius * OreRadius) continue;
                    ushort id = world.GetBlock(c.X + dx, c.Y + dy, c.Z + dz);
                    if (id == 0 || id >= blocks.Count) continue;
                    var def = blocks[id];
                    if (!def.HasTag("ore")) continue;
                    var at = (new Vector3(c.X + dx, c.Y + dy, c.Z + dz) + Vector3.One * 0.5f) * Strata.Core.Common.Units.CellSize;
                    mm.SetInstanceTransform(count, new Transform3D(Basis.Identity, at));
                    var color = Color.FromHtml(def.Color);
                    mm.SetInstanceColor(count, new Color(color.R * 1.4f, color.G * 1.4f, color.B * 1.4f, 0.85f));
                    count++;
                }
        mm.VisibleInstanceCount = count;
    }
}
