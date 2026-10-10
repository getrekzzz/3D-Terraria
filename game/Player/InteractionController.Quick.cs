using Godot;
using Strata.Core.Gameplay.Interaction;

namespace Strata.Game.Player;

public sealed partial class InteractionController
{
    private static readonly StringName QuickHealAction = "quick_heal";
    private static readonly StringName QuickManaAction = "quick_mana";
    private static readonly StringName QuickBuffAction = "quick_buff";

    private bool _quickHealDown, _quickManaDown, _quickBuffDown;

    /// <summary>
    /// Terraria's quick keys (H heal, J mana, B buff): drink from the backpack without changing the held item, with the
    /// inventory open too. Polled like the hotbar keys so smoke scripts' pressed actions count; once per press.
    /// </summary>
    private void PollQuickKeys()
    {
        bool live = Enabled && !UI.InputGate.HotkeysBlocked && !_player.Frozen && Entity.Alive;
        if (Pressed(QuickHealAction, ref _quickHealDown) && live) Report(Game.ItemUse.QuickHeal(Entity));
        if (Pressed(QuickManaAction, ref _quickManaDown) && live) Report(Game.ItemUse.QuickMana(Entity));
        if (Pressed(QuickBuffAction, ref _quickBuffDown) && live && Game.ItemUse.QuickBuff(Entity) > 0) Used?.Invoke(UseOutcome.Consumed);
    }

    private void Report(UseOutcome outcome)
    {
        if (outcome is not (UseOutcome.None or UseOutcome.Cooldown)) Used?.Invoke(outcome);
    }

    /// <summary>True on the tick an action goes down (a press already released by this tick still counts once).</summary>
    private static bool Pressed(StringName action, ref bool wasDown)
    {
        bool down = Input.IsActionPressed(action);
        bool pressed = (down && !wasDown) || Input.IsActionJustPressed(action);
        wasDown = down;
        return pressed;
    }
}
