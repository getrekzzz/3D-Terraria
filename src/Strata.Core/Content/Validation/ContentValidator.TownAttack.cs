using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    /// <summary>
    /// Attack blocks: a known style; launching styles need a friendly projectile that cannot hurt players or break
    /// terrain (residents fight beside the player, so their shots must never turn on them); a ward needs its buff.
    /// </summary>
    private static void ValidateTownAttack(Ctx ctx, TownNpcDef t)
    {
        if (t.Attack is not { } a) return;
        if (Array.IndexOf(TownAttack.Styles, a.Style) < 0) ctx.Error(t, $"attack.style '{a.Style}' must be one of {string.Join(", ", TownAttack.Styles)}.");
        if (a.Range <= 0 || a.Interval <= 0 || a.Count < 1) ctx.Error(t, "attack needs range > 0, interval > 0 and count >= 1.");
        bool launches = a.Style is "throw" or "shoot" or "magic";
        if (launches && a.Projectile == null) ctx.Error(t, $"attack style '{a.Style}' needs a projectile.");
        if ((launches || a.Style == "melee") && a.Damage <= 0) ctx.Error(t, "an attacking style needs damage > 0.");
        if (a.Style == "heal" && a.Heal <= 0) ctx.Error(t, "a heal attack needs heal > 0.");
        if (a.Style == "ward" && a.Buff == null) ctx.Error(t, "a ward attack needs a buff.");
        ctx.Ref(t, ctx.Db.Buffs, a.Buff, "attack.buff");
        ctx.Ref(t, ctx.Db.Projectiles, a.Projectile, "attack.projectile");
        if (a.Projectile == null || !ctx.Db.Projectiles.TryGet(a.Projectile, out var p)) return;
        if (!p.Friendly || p.Hostile) ctx.Error(t, $"attack.projectile '{p.Id}' must be friendly and not hostile.");
        foreach (var hit in p.OnHit)
            if (hit.Type == ProjectileHitSpecs.Explode && hit.Parse<ExplodeParams>() is { } e && (e.HurtsPlayers || e.DestroyTerrain))
                ctx.Error(t, $"attack.projectile '{p.Id}' explodes on players or terrain; residents need a harmless blast.");
    }
}
