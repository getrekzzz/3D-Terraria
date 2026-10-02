using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>
/// The local player's item use: swings and shots by weapon kind, a dry click or a mana fizzle for a press that could not
/// be used, drinks, wiring and nets.
/// </summary>
public sealed partial class SoundEvents
{
    private void WireWeapons(GameWorld game)
    {
        game.ItemUse.Used += OnUsed;
        game.ItemUse.Failed += (p, _, outcome) =>
        {
            if (p != _local) return; // once per press, never while held
            if (outcome == UseOutcome.NoMana) Sfx.Play("no_mana", -4f);
            else Sfx.Play("use_fail", -4f);
        };
    }

    private void OnUsed(PlayerEntity p, Strata.Core.Gameplay.Items.ItemUseInfo info, UseOutcome outcome)
    {
        if (p != _local) return;
        var item = info.Item;
        // Every swing of a tool whooshes once, whether or not it also bites a block (that sound comes from the hit).
        bool newSwing = p.UseSerial != _lastSerial;
        _lastSerial = p.UseSerial;
        switch (outcome)
        {
            case UseOutcome.Swung:
                Sfx.Play("swing", item.UseTime < 18 ? -3f : 0f);
                break;
            case UseOutcome.Mined or UseOutcome.Damaged or UseOutcome.HitObject or UseOutcome.TooHard or UseOutcome.WrongTool:
                if (newSwing && info.IsTool) Sfx.Play("swing", item.UseTime < 18 ? -3f : 0f);
                // An object too tough for the tool (an altar before its time) tinks too; blocks already did where struck.
                if (outcome == UseOutcome.TooHard && Engine.GetProcessFrames() != _tinkFrame) Sfx.Play("tink", -6f);
                break;
            case UseOutcome.Shot:
                Sfx.Play(ShotSound(item), item.UseTime <= 6 ? -5f : 0f);
                break;
            case UseOutcome.Consumed:
                if (item.HealLife > 0) Sfx.Play("potion");
                else if (item.HealMana > 0) Sfx.Play("mana");
                else if (item.Category == ItemCategory.BossSummon) Sfx.Play("summon");
                else Sfx.Play("heal", -4f);
                break;
            case UseOutcome.PouredLiquid:
            case UseOutcome.CollectedLiquid:
                Sfx.Play("splash", -4f);
                break;
            case UseOutcome.Wired:
                Sfx.Play("wire", -2f);
                break;
            case UseOutcome.Caught:
                Sfx.Play("net");
                Sfx.Play("pickup");
                break;
            case UseOutcome.Mounted:
                Sfx.Play("minecart");
                break;
        }
    }

    private static string ShotSound(ItemDef item)
    {
        if (item.UseAmmo == "arrow") return "bow";
        if (item.UseAmmo is "bullet" or "rocket") return item.UseTime >= 30 || item.UseAmmo == "rocket" ? "gun_heavy" : "gun";
        if (item.DamageType == DamageClass.Magic) return item.ManaCost >= 12 ? "magic_heavy" : "magic";
        if (item.DamageType == DamageClass.Summon) return "summon";
        return "swing";
    }
}
