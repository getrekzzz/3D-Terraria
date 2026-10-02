using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Specs;
using Strata.Core.Presentation;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// The info panel under the minimap (the Info region), as in Terraria: one row per readout of a worn information
/// accessory (Depth Meter, Compass, GPS, Cell Phone), each with the 16 px icon of the accessory giving it and 14 px
/// outlined text, and the FPS counter (Settings → Show FPS) last. Rebuilt four times a second and drawn directly, so
/// no row can outgrow the region: rows past its height are left out.
/// </summary>
public sealed partial class InfoPanel : Control
{
    private const double Interval = 0.25;
    private const int Icon = 16;
    private const int FontSize = 14;
    private static readonly string[] Readouts = { "depth", "position", "time" };
    private static readonly Dictionary<TypedSpec, string[]> Shows = new();

    private readonly UiContext _ui;
    private readonly List<(Texture2D? Icon, string Text)> _rows = new();
    private double _wait;

    public InfoPanel(UiContext ui)
    {
        _ui = ui;
        Name = "InfoPanel";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps; // item icons drawn small
    }

    /// <summary>Rows the panel wants (the layout may give it fewer).</summary>
    public int Lines => _rows.Count;

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0) return;
        _wait = Interval;
        Rebuild();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = UiFonts.Bold;
        int fit = Mathf.Min(_rows.Count, (int)((Size.Y + 0.5f) / UiLayout.InfoLine));
        for (int i = 0; i < fit; i++)
        {
            var (icon, text) = _rows[i];
            float y = i * UiLayout.InfoLine;
            if (icon != null) DrawTextureRect(icon, new Rect2(0, y + 1, Icon, Icon), false);
            var baseline = new Vector2(Icon + 4, y + 14);
            float width = Mathf.Max(0, Size.X - baseline.X);
            DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, width, FontSize, UiFonts.Outline, UiFonts.OutlineColor);
            DrawString(font, baseline, text, HorizontalAlignment.Left, width, FontSize, UiTheme.Text);
        }
    }

    private void Rebuild()
    {
        _rows.Clear();
        var info = _ui.Entity.State.Effects.Info;
        if (info.Count > 0)
            foreach (var key in Readouts)
                if (info.Contains(key)) _rows.Add((IconFor(key), Readout(key)));
        if (Settings.UserSettings.Current.ShowFps) _rows.Add((null, $"{Engine.GetFramesPerSecond():0} FPS"));
    }

    /// <summary>The icon of the first worn accessory that gives <paramref name="key"/>.</summary>
    private Texture2D? IconFor(string key)
    {
        var inv = _ui.Inventory;
        for (int i = 0; i < inv.AccessorySlotsUnlocked; i++)
        {
            var item = inv.Accessories[i].Item;
            if (item == null) continue;
            foreach (var spec in item.EquipEffects)
                if (spec.Type == EffectSpecs.Info && System.Array.IndexOf(Show(spec), key) >= 0) return ItemIcons.Get(item);
        }
        return null;
    }

    private static string[] Show(TypedSpec spec)
    {
        if (!Shows.TryGetValue(spec, out var show)) Shows[spec] = show = spec.Parse<InfoParams>().Show;
        return show;
    }

    /// <summary>"Depth: 32 m below", "120 m east, 45 m north", "7:30 PM".</summary>
    private string Readout(string key)
    {
        var session = _ui.Game.Session;
        var cell = _ui.Entity.FeetCell;
        switch (key)
        {
            case "depth":
                float depth = session.ContextAt(cell).Depth * Units.CellSize;
                return Mathf.Abs(depth) < 2f ? "Depth: surface" : depth > 0 ? $"Depth: {depth:0} m below" : $"Height: {-depth:0} m above";
            case "position":
                var size = session.Settings.Dimensions;
                float east = (cell.X - size.X * 0.5f) * Units.CellSize, north = (size.Z * 0.5f - cell.Z) * Units.CellSize;
                return $"{Mathf.Abs(east):0} m {(east >= 0 ? "east" : "west")}, {Mathf.Abs(north):0} m {(north >= 0 ? "north" : "south")}";
            default:
                float hour = session.Clock.Hour;
                int h = (int)hour, m = (int)((hour - h) * 60);
                return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
        }
    }
}
