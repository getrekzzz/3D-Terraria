using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Presentation;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Hit dust by creature material (data/config/creature_materials.json): blood from flesh, gel droplets from slimes,
/// bone chips from skeletons, sparks from metal, leaves from plants, thrown out of the creature away from the player
/// and lit where it flies. More damage, more dust (up to 12 before the preset's scale).
/// </summary>
public sealed partial class CombatEffects
{
    private CreatureMaterialsConfig _materials = null!;
    private GameWorld _game = null!;

    public CreatureMaterialsConfig Materials => _materials;

    private void InitializeDust(GameWorld game)
    {
        _game = game;
        _materials = CreatureMaterialsConfig.Load(new Bootstrap.GodotDataSource());
        game.Npcs.Damaged += HitDust;
    }

    private void HitDust(DamageEvent e)
    {
        if (e.ToPlayer || e.Kind != DamageKind.Hit || _game.Npcs.Find(e.NpcId) is not { } npc) return;
        var material = _materials.Resolve(npc.Def);
        var center = new Vector3(npc.Center.X, npc.Center.Y, npc.Center.Z);
        var away = Vector3.Up;
        if (_game.LocalPlayer is { } p)
        {
            var d = center - new Vector3(p.Center.X, p.Center.Y, p.Center.Z);
            d.Y = Mathf.Max(d.Y, 0f) + 0.6f;
            away = d.Normalized();
        }
        _kit.Burst(Recipe(material.Dust.Recipe), center + away * npc.HalfExtents.X * 0.6f, DustColor(material.Dust.UseTint, material.Dust.Color, npc),
            Mathf.Min(12, 4 + e.Amount / 10), 3.2f, away, 0.18f);
    }

    private static Color DustColor(bool useTint, string color, Npc npc) => Color.FromHtml(useTint ? npc.Def.Tint : color);

    private static EffectRecipe Recipe(string name) => name switch
    {
        "Chips" => EffectRecipe.Chips,
        "Sparks" => EffectRecipe.Sparks,
        "Puff" => EffectRecipe.Puff,
        "Splash" => EffectRecipe.Splash,
        "Leaves" => EffectRecipe.Leaves,
        _ => EffectRecipe.Dust,
    };
}
