using Godot;
using Strata.Core.Gameplay.Guide;

namespace Strata.Game.UI.GuideBook;

/// <summary>What every Guide book tab shares: the UI context, the (background-built) index and navigation.</summary>
public sealed class GuideBookContext
{
    public required UiContext Ui { get; init; }
    public required GuideBookScreen Book { get; init; }

    /// <summary>The shared model library the bestiary's 3D previews instance creatures from (null: portraits only).</summary>
    public Strata.Game.Rendering.PropLibrary? Props { get; init; }

    /// <summary>The Items index (null until its background build completes).</summary>
    public GuideIndex? Index { get; set; }
}

/// <summary>
/// A bookmark tab of the Guide book (Items, Bestiary, Guide). <see cref="Build"/> runs on the tab's first selection,
/// so a closed book costs nothing; <see cref="Shown"/> refreshes it each time it is brought up.
/// </summary>
public interface IGuideTab
{
    /// <summary>Stable id used by commands and probes ("items", "bestiary", "guide").</summary>
    string Key { get; }

    string Title { get; }

    Control Build(GuideBookContext ctx);

    void Shown();

    /// <summary>Selects an entry of this tab by content id; false when the tab has no such entry.</summary>
    bool Show(string id);
}
