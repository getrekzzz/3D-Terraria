using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>
/// Terraria's quick keys (H, J, B): drink from anywhere in the backpack without changing the held item.
/// <list type="bullet">
/// <item>Quick heal takes the potion that heals the missing life with the least overheal, or the strongest when none
/// covers it; nothing at full life or under Potion Sickness (which it names).</item>
/// <item>Quick mana does the same over mana restored; nothing at full mana.</item>
/// <item>Quick buff eats the best food unless a food buff (any tier) is on, then drinks one of every buff item whose
/// buff is not already on (nor one the player is immune to).</item>
/// </list>
/// Ties go to the first slot (the hotbar first). A press plays one sound through <see cref="Used"/> and, when the hands
/// are free, the first item's drink or eat action (a short use animation of that item; the held item's next use starts
/// its own at once); every item used floats its name through <see cref="QuickUsed"/>.
/// </summary>
public sealed partial class ItemUseSystem
{
    /// <summary>A quick key used one of an item (the HUD floats "Used Healing Potion" over the head).</summary>
    public event Action<PlayerEntity, ItemDef>? QuickUsed;

    /// <summary>Drinks the best healing potion in the backpack (H).</summary>
    public UseOutcome QuickHeal(PlayerEntity p)
    {
        var v = p.State.Vitals;
        int missing = v.MaxLife - v.Life;
        if (missing <= 0 || !QuickReady(p)) return UseOutcome.None;
        int slot = BestFit(p.State.Inventory.Backpack, missing, mana: false, skipHealing: false);
        return slot < 0 ? UseOutcome.None : QuickUse(p, slot, lead: true);
    }

    /// <summary>Drinks the best mana potion in the backpack (J).</summary>
    public UseOutcome QuickMana(PlayerEntity p)
    {
        var v = p.State.Vitals;
        int missing = v.MaxMana - v.Mana;
        if (missing <= 0 || !QuickReady(p)) return UseOutcome.None;
        // A potion that also heals life (Restoration) waits out Potion Sickness; pure mana potions never do.
        int slot = BestFit(p.State.Inventory.Backpack, missing, mana: true, skipHealing: PotionSick(p));
        return slot < 0 ? UseOutcome.None : QuickUse(p, slot, lead: true);
    }

    /// <summary>
    /// Uses one of every buff item in the backpack whose buff is not active (B); returns how many were used. Food
    /// counts once and only while no food buff is on (the tiers conflict): the highest tier, the longest-lasting of it.
    /// </summary>
    public int QuickBuff(PlayerEntity p)
    {
        if (!QuickReady(p)) return 0;
        var inv = p.State.Inventory.Backpack;
        var buffs = p.State.Buffs;
        var content = _world.Session.Content.Buffs;
        bool sick = PotionSick(p);
        bool fed = false;
        foreach (var b in buffs.Active) fed |= b.Def.FoodTier > 0;
        int food = -1, foodTier = 0, foodTicks = 0;
        for (int i = 0; i < inv.Size && !fed; i++)
        {
            if (QuickBuffOf(inv[i]) is not { } buff || buff.FoodTier <= 0) continue;
            int ticks = inv[i].Item!.Buff!.Ticks;
            if (buff.FoodTier > foodTier || (buff.FoodTier == foodTier && ticks > foodTicks))
            {
                food = i;
                foodTier = buff.FoodTier;
                foodTicks = ticks;
            }
        }
        int used = 0;
        if (food >= 0 && QuickUse(p, food, lead: true) == UseOutcome.Consumed) used++;
        for (int i = 0; i < inv.Size && p.Alive; i++)
        {
            if (QuickBuffOf(inv[i]) is not { FoodTier: 0 } buff || buffs.Has(buff.Id) || p.State.Effects.Immunities.Contains(buff.Id)) continue;
            if (QuickUse(p, i, lead: used == 0) == UseOutcome.Consumed) used++;
        }
        return used;

        BuffDef? QuickBuffOf(ItemStack s) =>
            !s.IsEmpty && s.Item!.Buff is { } granted && !(sick && s.Item.HealLife > 0) && _world.Uses[s.Item].Has(ItemAction.Consume)
            && content.TryGet(granted.Id, out var def) ? def : null;
    }

    /// <summary>Quick keys work while alive and allowed to use items (not cursed, frozen or stoned); a refusal says why.</summary>
    private bool QuickReady(PlayerEntity p)
    {
        if (!p.Alive) return false;
        if (!p.State.Effects.NoItemUse) return true;
        Say(p, "You can't use items right now");
        return false;
    }

    /// <summary>
    /// Terraria's pick: the stack whose life (or mana) restored minus <paramref name="missing"/> is the smallest
    /// non-negative overheal, else the largest heal; -1 when the backpack has none.
    /// </summary>
    private int BestFit(Inventory inv, int missing, bool mana, bool skipHealing)
    {
        int best = -1, bestScore = int.MinValue;
        for (int i = 0; i < inv.Size; i++)
        {
            var s = inv[i];
            if (s.IsEmpty) continue;
            int restores = mana ? s.Item!.HealMana : s.Item!.HealLife;
            if (restores <= 0 || (skipHealing && s.Item.HealLife > 0) || !_world.Uses[s.Item].Has(ItemAction.Consume)) continue;
            int score = restores - missing;
            if (bestScore < 0 ? score > bestScore : score >= 0 && score < bestScore)
            {
                best = i;
                bestScore = score;
            }
        }
        return best;
    }

    private bool PotionSick(PlayerEntity p) => _healingCooldown != null && p.State.Buffs.Has(_healingCooldown.Id);

    /// <summary>
    /// Consumes one from <paramref name="slot"/>. The <paramref name="lead"/> use of a key press plays the sound and,
    /// when the hands are free, the drink (a swing or a shot in progress is never cut short); quick buff's others only
    /// float their names.
    /// </summary>
    private UseOutcome QuickUse(PlayerEntity p, int slot, bool lead)
    {
        var inv = p.State.Inventory.Backpack;
        var item = inv[slot].Item!;
        var outcome = ConsumeAt(p, inv, slot);
        if (outcome != UseOutcome.Consumed) return outcome;
        if (lead)
        {
            if (p.UseAnimation <= 0) StartAnimation(p, item, 1f);
            Used?.Invoke(p, _world.Uses[item], outcome);
        }
        QuickUsed?.Invoke(p, item);
        return outcome;
    }
}
