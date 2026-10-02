using Godot;

namespace Strata.Game.UI;

/// <summary>
/// The town's own screens outside the W4 chat box: the Stylist's haircut (P5-21) and the housing menu (P5-29). The W4
/// TownDialog's service buttons and the inventory's housing button open them through this holder.
/// </summary>
public sealed class TownPanels
{
    public required HaircutPanel Haircut { get; init; }

    /// <summary>Closes every town panel (the console's <c>ui close</c>).</summary>
    public void CloseAll()
    {
        Haircut.Close();
    }

    /// <summary>Opens the screen for a chat-box service (<see cref="TownDialog.PanelRequested"/>).</summary>
    public void Open(Strata.Core.Gameplay.Town.TownNpc npc, string service)
    {
        if (service == Strata.Core.Content.Specs.TownServiceSpecs.Haircut) Haircut.Open(npc);
    }

    public static TownPanels Create(Node parent, UiContext ui)
    {
        var haircut = new HaircutPanel();
        parent.AddChild(haircut);
        haircut.Initialize(ui);
        return new TownPanels { Haircut = haircut };
    }
}
