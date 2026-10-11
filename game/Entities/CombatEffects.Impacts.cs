using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>Projectile impacts on the terrain (P3-11): a few chips of the struck block fly off where it hit.</summary>
public sealed partial class CombatEffects
{
    /// <summary>Terrain impacts shown since start (probe <c>fx.impacts</c>).</summary>
    public int Impacts { get; private set; }

    private void InitializeImpacts(Strata.Core.Session.GameWorld game)
    {
        game.Projectiles.HitTerrain += (p, at, block) =>
        {
            if (p.Def.Bobber || block == ContentDatabase.Air || block == Strata.Core.Voxels.VoxelWorld.Unknown) return;
            var color = Color.FromHtml(game.Session.Content.Blocks[block].Color);
            var back = new Vector3(-p.Velocity.X, -p.Velocity.Y, -p.Velocity.Z);
            _kit.Burst(EffectRecipe.Chips, ToGodot(at), color, 4, 1.6f, back.LengthSquared() > 1e-4f ? back.Normalized() : Vector3.Up, 0.6f);
            Impacts++;
        };
    }
}
