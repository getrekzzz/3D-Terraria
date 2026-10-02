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

    public static TownPanels Create(Node parent, UiContext ui)
    {
        var haircut = new HaircutPanel();
        parent.AddChild(haircut);
        haircut.Initialize(ui);
        return new TownPanels { Haircut = haircut };
    }
}
