using System.Collections.Generic;
using System.Text;
using Godot;

namespace Strata.Game.UI;

public static partial class UiTheme
{
    /// <summary>
    /// The project theme (project.godot [gui] theme/custom), exported from <see cref="Theme"/>: the code builder stays the
    /// source of truth, the file gives every control (and the editor) the same look from the first frame.
    /// </summary>
    public const string ThemePath = "res://assets/ui/strata_theme.tres";

    /// <summary>Milliseconds the last <see cref="Install"/> spent rasterising glyphs.</summary>
    public static double WarmupMs { get; private set; }

    /// <summary>
    /// Makes the theme global, before any UI node exists: the UI font becomes ThemeDB's fallback (Label3D, custom
    /// drawing and CanvasLayers that stop theme propagation all use it), the code-built theme is merged into the project
    /// theme (so code edits apply even before the file is re-exported), and the glyphs are pre-rasterised.
    /// </summary>
    public static void Install()
    {
        ThemeDB.FallbackFont = UiFonts.Bold;
        ThemeDB.FallbackFontSize = UiFonts.Body;
        (ThemeDB.GetProjectTheme() ?? ThemeDB.GetDefaultTheme()).MergeWith(Theme);
        ulong start = Time.GetTicksUsec();
        UiFonts.Warmup();
        WarmupMs = (Time.GetTicksUsec() - start) / 1000.0;
    }

    /// <summary>Saves <see cref="Theme"/> to <see cref="ThemePath"/> with stable sub-resource ids (diff-friendly).</summary>
    public static Error Export()
    {
        foreach (var type in Theme.GetStyleboxTypeList())
            foreach (var name in Theme.GetStyleboxList(type))
            {
                var style = Theme.GetStylebox(name, type);
                if (string.IsNullOrEmpty(style.ResourceSceneUniqueId)) style.ResourceSceneUniqueId = $"{type}_{name}";
            }
        DirAccess.MakeDirRecursiveAbsolute(ThemePath.GetBaseDir());
        return ResourceSaver.Save(Theme, ThemePath);
    }

    /// <summary>
    /// True when the exported file (read fresh from disk) holds exactly what the code builds; a stale export fails the
    /// <c>ui.theme_synced</c> probe so it cannot drift unnoticed.
    /// </summary>
    public static bool ExportInSync(out string firstDifference)
    {
        firstDifference = "";
        if (!ResourceLoader.Exists(ThemePath))
        {
            firstDifference = $"{ThemePath} is missing";
            return false;
        }
        var saved = ResourceLoader.Load<Theme>(ThemePath, null, ResourceLoader.CacheMode.Ignore);
        var want = Describe(Theme);
        var have = Describe(saved);
        for (int i = 0; i < want.Count || i < have.Count; i++)
        {
            string a = i < want.Count ? want[i] : "(nothing)", b = i < have.Count ? have[i] : "(nothing)";
            if (a == b) continue;
            firstDifference = $"code {a} | file {b}";
            return false;
        }
        return true;
    }

    /// <summary>Every item of a theme as sorted "kind type/name = value" lines; styleboxes list their stored properties.</summary>
    private static List<string> Describe(Theme theme)
    {
        var lines = new List<string>
        {
            $"default_font = {theme.DefaultFont?.ResourcePath}",
            $"default_font_size = {theme.DefaultFontSize}",
        };
        foreach (var variation in theme.GetTypeVariationList("Button")) lines.Add($"variation {variation} < Button");
        for (var kind = Theme.DataType.Color; kind < Theme.DataType.Max; kind++)
            foreach (var type in theme.GetThemeItemTypeList(kind))
                foreach (var name in theme.GetThemeItemList(kind, type))
                    lines.Add($"{kind} {type}/{name} = {Value(theme.GetThemeItem(kind, name, type))}");
        lines.Sort(System.StringComparer.Ordinal);
        return lines;
    }

    private static string Value(Variant value)
    {
        if (value.Obj is not Resource resource) return GD.VarToStr(value);
        if (!string.IsNullOrEmpty(resource.ResourcePath) && !resource.ResourcePath.Contains("::")) return resource.ResourcePath;
        var text = new StringBuilder(resource.GetClass());
        foreach (var property in resource.GetPropertyList())
        {
            if (((int)property["usage"] & (int)PropertyUsageFlags.Storage) == 0) continue;
            var name = (string)property["name"];
            if (name is "resource_path" or "resource_scene_unique_id" or "script") continue;
            text.Append(' ').Append(name).Append('=').Append(GD.VarToStr(resource.Get(name)));
        }
        return text.ToString();
    }
}
