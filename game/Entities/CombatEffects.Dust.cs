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
    private GoreSystem _gore = null!;
    private readonly RandomNumberGenerator _rng = new();

    public CreatureMaterialsConfig Materials => _materials;

    private void InitializeDust(GameWorld game)
    {
        _game = game;
        _materials = CreatureMaterialsConfig.Load(new Bootstrap.GodotDataSource());
        game.Npcs.Damaged += HitDust;
        game.Npcs.Killed += DeathBurst;
    }

    /// <summary>
    /// A death: a burst of the material's dust (25 before the preset scale) and gore chunks sized to the creature (2–4,
    /// bosses 8); critters only puff.
    /// </summary>
    private void DeathBurst(Npc npc)
    {
        var center = new Vector3(npc.Center.X, npc.Center.Y, npc.Center.Z);
        var half = new Vector3(npc.HalfExtents.X, npc.HalfExtents.Y, npc.HalfExtents.Z);
        if (npc.Def.HasTag("critter"))
        {
            _kit.Burst(EffectRecipe.Puff, center, new Color(0.85f, 0.85f, 0.82f), 6, 1.2f);
            return;
        }
        var m = _materials.Resolve(npc.Def);
        _kit.Burst(Recipe(m.Dust.Recipe), center, DustColor(m.Dust.UseTint, m.Dust.Color, npc), npc.IsBoss ? 40 : 25, 3.5f, null, 0.2f);
        int pieces = npc.IsBoss ? 8 : m.Gore.Pieces;
        if (pieces <= 0) return;
        float size = Mathf.Clamp(Mathf.Max(half.X, half.Y) * 0.22f, 0.06f, npc.IsBoss ? 0.45f : 0.18f);
        _gore.Spawn(center, half, pieces, size, DustColor(m.Gore.UseTint, m.Gore.Color, npc), m.Gore.Shape == "sphere", _rng);
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
