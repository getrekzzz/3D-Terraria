using Godot;
using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on controller support (X-04): the device in use, the screen cursor (whether it drives a screen, what it is
/// on, the clicks it made), aim assist, the hotbar slot the shoulder buttons step and the crosshair's button prompt.
/// </summary>
[ProbeSource]
public static class PadProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("pad.active", () => InputDevice.UsingPad ? 1 : 0);
        ctx.AddProbe("pad.navigating", () => InputGate.PadNavigating ? 1 : 0);
        // A menu control has the engine's focus (the pause menu, settings): the controller moves it.
        var viewport = world.Runtime.GetViewport();
        ctx.AddProbe("pad.menu_focus", () => viewport.GuiGetFocusOwner() is { } owner && owner.IsVisibleInTree() ? 1 : 0);
        // pad.menu_focus.<words>: 1 when the focused menu button's text, lower case with spaces as underscores, contains them.
        ctx.AddPrefixProbe("pad.menu_focus.", words => viewport.GuiGetFocusOwner() is Button { Visible: true } b
            && b.Text.ToLowerInvariant().Replace(' ', '_').Contains(words.ToLowerInvariant(), System.StringComparison.Ordinal) ? 1 : 0);
        if (world.Player is { } player)
        {
            ctx.AddProbe("hotbar.selected", () => player.Entity.State.Inventory.SelectedHotbar);
            var rig = player.Rig;
            ctx.AddProbe("pad.assist_target", () => rig.AssistTarget != null ? 1 : 0);
            ctx.AddProbe("pad.assist_friction", () => rig.AssistFriction);
            ctx.AddProbe("pad.assist_pull_deg", () => Mathf.RadToDeg(rig.AssistPulled));
            // The angle (degrees) between the view and the creature aim assist is helping toward; -1 for none.
            ctx.AddProbe("pad.assist_angle", () => rig.AssistTarget is { } t ? Mathf.RadToDeg(t.Angle) : -1);
        }
        if (world.Hud is { } hud) ctx.AddProbe("hud.prompt_pad", () => hud.PromptOnPad ? 1 : 0);
        if (world.Inventory is not { } inventory) return;
        PadCursor? cursor = null;
        PadCursor? Cursor() => cursor ??= world.Runtime.GetNodeOrNull<PadCursor>(PadCursor.NodeName);
        ctx.AddProbe("pad.cursor", () => Cursor()?.Active == true ? 1 : 0);
        ctx.AddProbe("pad.targets", () => Cursor()?.TargetCount ?? 0);
        ctx.AddProbe("pad.presses", () => Cursor()?.Presses ?? 0);
        ctx.AddProbe("pad.pointer", () => PadCursor.Pointer != null ? 1 : 0);
        ctx.AddProbe("pad.focus_button", () => Cursor()?.Focus is BaseButton ? 1 : 0);
        ctx.AddProbe("pad.focus_recipe", () => Cursor()?.Focus is ItemSlotView view && InCrafting(view) ? 1 : 0);
        // pad.focus.<area>: the index of the slot of that area (backpack, chest, shop, coins, armor…) under the cursor; -1 elsewhere.
        ctx.AddPrefixProbe("pad.focus.", name =>
        {
            Strata.Core.Gameplay.Items.SlotArea area;
            try { area = Strata.Core.Commands.InventoryCommands.Area(name); }
            catch (CommandException) { return null; }
            return Cursor()?.Focus is ItemSlotView view && inventory.SlotOf(view) is { } slot && slot.Area == area ? slot.Index : -1;
        });
    }

    private static bool InCrafting(Node view)
    {
        for (var n = view.GetParent(); n != null; n = n.GetParent())
            if (n is CraftingPanel) return true;
        return false;
    }
}
