using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>
/// Failed-use feedback: a click that cannot do its thing says why (a sound from <see cref="Failed"/>, a message from
/// <see cref="Refused"/>) once per press. Out of ammo names the cheapest ammo of the weapon's class; out of mana says so;
/// a refused use speaks only when it recorded a reason (potion sickness, a curse, a summon's conditions). Placement,
/// a boomerang already out and a crystal at its cap stay silent, as in Terraria.
/// </summary>
public sealed partial class ItemUseSystem
{
    /// <summary>A refusal message is not repeated to the same player within this many ticks (rapid clicks).</summary>
    private const int RepeatMessageTicks = 120;

    private readonly Dictionary<string, string> _ammoMessages = new();
    private string? _refusal;
    private PlayerEntity? _messaged;
    private string? _lastMessage;
    private long _lastMessageTick;

    /// <summary>
    /// A fresh press that could not be used: <see cref="UseOutcome.NoAmmo"/>, <see cref="UseOutcome.NoMana"/> or
    /// <see cref="UseOutcome.Blocked"/> with a reason. Holding the button never raises it again.
    /// </summary>
    public event Action<PlayerEntity, ItemUseInfo, UseOutcome>? Failed;

    /// <summary>Why a use failed, for the player (the HUD shows it). Not repeated while the same text is still up.</summary>
    public event Action<PlayerEntity, string>? Refused;

    /// <summary>How many failed presses were reported (probe <c>use.failures</c>).</summary>
    public int Failures { get; private set; }

    /// <summary>Returns <see cref="UseOutcome.Blocked"/> and records why, so the press is reported.</summary>
    private UseOutcome Refuse(string reason)
    {
        _refusal = reason;
        return UseOutcome.Blocked;
    }

    /// <summary>
    /// Reports a failed outcome on a fresh press (event, count and message) and passes it through. A held button, and a
    /// Blocked outcome without a recorded reason, stay silent.
    /// </summary>
    private UseOutcome Report(PlayerEntity p, ItemUseInfo info, UseOutcome outcome, bool freshPress)
    {
        string? message = outcome switch
        {
            UseOutcome.NoAmmo => AmmoMessage(info.Item.UseAmmo),
            UseOutcome.NoMana => "Not enough mana",
            UseOutcome.Blocked => _refusal,
            _ => null,
        };
        _refusal = null;
        if (!freshPress || message == null) return outcome;
        Failures++;
        Failed?.Invoke(p, info, outcome);
        Say(p, message);
        return outcome;
    }

    /// <summary>Raises <see cref="Refused"/> unless the same player just heard the same message.</summary>
    private void Say(PlayerEntity p, string message)
    {
        long now = _world.Session.Clock.Tick;
        bool repeat = p == _messaged && message == _lastMessage && now >= _lastMessageTick && now - _lastMessageTick < RepeatMessageTicks;
        if (repeat) return;
        _messaged = p;
        _lastMessage = message;
        _lastMessageTick = now;
        Refused?.Invoke(p, message);
    }

    /// <summary>
    /// "Needs Wooden Arrow": the cheapest obtainable item of the ammo class (placeholders with no value or the
    /// <c>hidden</c> tag are skipped; ties go to the lower id). Cached per class.
    /// </summary>
    private string AmmoMessage(string? ammoClass)
    {
        if (ammoClass == null) return "Out of ammo";
        if (_ammoMessages.TryGetValue(ammoClass, out var cached)) return cached;
        ItemDef? best = null;
        foreach (var item in _world.Session.Content.Items.All)
        {
            if (item.AmmoClass != ammoClass || item.Value <= 0 || item.HasTag("hidden")) continue;
            if (best == null || item.Value < best.Value || item.Value == best.Value && string.CompareOrdinal(item.Id, best.Id) < 0) best = item;
        }
        return _ammoMessages[ammoClass] = best != null ? $"Needs {best.DisplayName}" : "Out of ammo";
    }
}
