using Godot;

namespace Strata.Game.UI.GuideBook;

/// <summary>A plain control that calls back every frame while in the tree (lazy work for a tab).</summary>
public partial class Ticker : Control
{
    public System.Action? Tick { get; set; }

    public override void _Ready() => SetAnchorsPreset(LayoutPreset.FullRect);

    public override void _Process(double delta) => Tick?.Invoke();
}
