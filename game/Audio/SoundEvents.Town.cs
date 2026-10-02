using Strata.Core.Content.Defs;

namespace Strata.Game.Audio;

public sealed partial class SoundEvents
{
    /// <summary>Residents fighting back (P5-10): a throw whooshes, shots crack or twang, spells and heals chime.</summary>
    private void WireTown(Strata.Core.Session.GameWorld game)
    {
        game.Town.ResidentAttacked += (r, a) => Sfx.PlayAt(AttackSound(a), ToGodot(r.Eye), -4f);
    }

    private static string AttackSound(TownAttack a) => a.Style switch
    {
        "shoot" => a.Projectile is { } p && p.Contains("arrow") ? "bow" : a.Projectile is { } r && r.StartsWith("rocket") ? "rocket" : "gun",
        "magic" => "magic",
        "heal" or "ward" => "heal",
        _ => "swing",
    };
}
