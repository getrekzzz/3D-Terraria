using Strata.Core.Content;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>Projectiles striking the terrain (P3-11): arrows thunk into it, everything else knocks with the block's own dig sound.</summary>
public sealed partial class SoundEvents
{
    private void WireImpacts(GameWorld game)
    {
        game.Projectiles.HitTerrain += (p, at, block) =>
        {
            if (p.Def.Bobber || block == ContentDatabase.Air || block == Strata.Core.Voxels.VoxelWorld.Unknown) return;
            var where = ToGodot(at);
            if (p.Def.Visual == "arrow")
            {
                Sfx.PlayAt("arrow_hit", where, -2f);
                return;
            }
            string dig = $"dig_{game.Session.Content.Blocks[block].Sound}";
            Sfx.PlayAt(Sfx.Has(dig) ? dig : "dig_stone", where, -6f);
        };
    }
}
