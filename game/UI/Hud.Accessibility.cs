using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The HUD's accessibility overlays (X-12): the subtitles above the chat log (<see cref="CaptionsPanel"/>) and the
/// autosave spinner in the bottom-right corner (<see cref="SaveSpinner"/>), both placed clear of every layout region
/// (<see cref="HudOverlays"/>) whenever the layout is applied, and hidden with the HUD (photo mode, <c>hud off</c>).
/// </summary>
public sealed partial class Hud
{
    private CaptionsPanel _captions = null!;
    private SaveSpinner _saveSpinner = null!;

    /// <summary>The subtitles (the <c>ui.captions</c> probes and the <c>access</c> command).</summary>
    public CaptionsPanel Captions => _captions;

    /// <summary>The autosave spinner (the <c>ui.saving</c> probe).</summary>
    public SaveSpinner SaveSpinner => _saveSpinner;

    private void BuildAccessibility(Control root)
    {
        var captions = CaptionsConfig.Load(new Bootstrap.GodotDataSource());
        root.AddChild(_captions = LayoutWidgets.Register(new CaptionsPanel(captions)));
        root.AddChild(_saveSpinner = LayoutWidgets.Register(new SaveSpinner(_ui.Game)));
        LayoutApplied += layout =>
        {
            var rect = HudOverlays.CaptionsRect(layout, captions.MaxLines);
            _captions.Place(rect);
            _saveSpinner.Place(HudOverlays.SaveIconRect(layout, rect));
        };
    }
}
