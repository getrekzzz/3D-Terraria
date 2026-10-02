using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Town;

namespace Strata.Game.Entities;

/// <summary>Residents fighting back (P5-10): each attack plays its upper-body clip over the walk/idle/rest state.</summary>
public sealed partial class TownRenderer
{
    private const float AttackClipSeconds = 0.4f;

    private void OnResidentAttacked(TownNpc npc, TownAttack attack)
    {
        if (!_views.TryGetValue(npc.Id, out var view) || view.Visual.Humanoid is not { } body) return;
        string clip = AttackClip(attack);
        if (body.Has(clip)) body.Play(clip, AttackClipSeconds);
    }

    /// <summary>throw → throw, shoot → bow (arrows) or gun, magic → tome, heal/ward → hold_up, melee → swing.</summary>
    private static string AttackClip(TownAttack a) => a.Style switch
    {
        "throw" => "throw",
        "shoot" => a.Projectile is { } p && p.Contains("arrow") ? "bow" : "gun",
        "magic" => "tome",
        "heal" or "ward" => "hold_up",
        _ => "swing",
    };
}
