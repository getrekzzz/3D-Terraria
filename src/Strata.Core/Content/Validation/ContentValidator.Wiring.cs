using Strata.Core.Content.Defs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static readonly HashSet<string> LogicGates = new(StringComparer.Ordinal) { "and", "or", "nand", "nor", "xor", "xnor" };

    /// <summary>Timers, teleporters, statues and logic gates (wiring devices).</summary>
    private static void ValidateWiringObject(Ctx ctx, TileObjectDef t)
    {
        if (t.Timer < 0) ctx.Error(t, "timer must be >= 0 ticks.");
        if (t.LogicGate != null && !LogicGates.Contains(t.LogicGate)) ctx.Error(t, $"unknown logicGate '{t.LogicGate}' (and, or, nand, nor, xor, xnor).");
        if (t.Statue is not { } s) return;
        if ((s.Enemy == null) == (s.Item == null)) ctx.Error(t, "statue needs exactly one of 'enemy' or 'item'.");
        ctx.Ref(t, ctx.Db.Enemies, s.Enemy, "statue.enemy");
        ctx.Ref(t, ctx.Db.Items, s.Item, "statue.item");
        if (s.Count < 1 || s.Cap < 1 || s.Radius < 1 || s.Cooldown < 0) ctx.Error(t, "statue count, cap and radius must be >= 1 and cooldown >= 0.");
    }
}
