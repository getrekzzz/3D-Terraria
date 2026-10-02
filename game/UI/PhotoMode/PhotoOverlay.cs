using System;
using System.Linq;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Menu;

namespace Strata.Game.UI.PhotoMode;

/// <summary>
/// Photo mode's lens panel (top right): focus distance and blur (depth of field), exposure in stops and the colour
/// filter, with the controls hint. The controller hides it while a picture is taken.
/// </summary>
public sealed partial class PhotoOverlay : CanvasLayer
{
    private PhotoModeController _photo = null!;
    private HSlider _focus = null!, _blur = null!, _exposure = null!;
    private Label _focusValue = null!, _blurValue = null!, _exposureValue = null!, _hint = null!;
    private OptionButton _filter = null!;
    private bool _syncing;

    public void Initialize(PhotoModeController photo)
    {
        _photo = photo;
        Name = "PhotoOverlay";
        Layer = 46;
        var (panel, box) = MenuKit.Panel(new Vector2(300, 0), 6);
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.OffsetLeft = -316;
        panel.OffsetRight = -16;
        panel.OffsetTop = 16;
        AddChild(panel);
        box.AddChild(MenuKit.Text("Photo mode", 22, MenuKit.Gold));
        _focus = AddSlider(box, "Focus distance", PhotoLens.MinFocus, 60f, 0.5f, v => _photo.SetLens(focus: v), out _focusValue);
        _blur = AddSlider(box, "Blur", 0f, 1f, 0.05f, v => _photo.SetLens(blur: v), out _blurValue);
        _exposure = AddSlider(box, "Exposure", -PhotoLens.MaxStops, PhotoLens.MaxStops, 0.1f, v => _photo.SetLens(stops: v), out _exposureValue);
        box.AddChild(MenuKit.Text("Filter", 15));
        var names = PhotoLens.Filters.Select(f => char.ToUpperInvariant(f[0]) + f[1..]).ToArray();
        _filter = MenuKit.Choice(names, 0, i => { if (!_syncing) _photo.SetLens(filter: i); });
        _filter.FocusMode = Control.FocusModeEnum.None;
        box.AddChild(_filter);
        _hint = MenuKit.Text("", 13, new Color(0.8f, 0.85f, 0.95f));
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_hint);
    }

    private HSlider AddSlider(VBoxContainer box, string label, float min, float max, float step, Action<float> changed, out Label value)
    {
        var row = new HBoxContainer();
        var name = MenuKit.Text(label, 15);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);
        value = MenuKit.Text("", 15, MenuKit.Gold);
        row.AddChild(value);
        box.AddChild(row);
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 22) };
        slider.ValueChanged += v =>
        {
            if (!_syncing) changed((float)v);
            Refresh();
        };
        box.AddChild(slider);
        return slider;
    }

    /// <summary>Shows the controller's current lens and the bound keys.</summary>
    public void Sync()
    {
        _syncing = true;
        _focus.Value = _photo.Focus;
        _blur.Value = _photo.Blur;
        _exposure.Value = _photo.Stops;
        _filter.Selected = _photo.Filter;
        _syncing = false;
        Refresh();
        _hint.Text = $"Hold right mouse to look, WASD/Space/Ctrl to fly.\n{Key("photo_shot")}: take picture   {Key("photo_mode")}/Esc: leave";
    }

    private void Refresh()
    {
        if (_exposureValue == null) return;
        _focusValue.Text = $"{_focus.Value:0.0} m";
        _blurValue.Text = _blur.Value <= 0.001 ? "Off" : $"{_blur.Value * 100:0}%";
        _exposureValue.Text = $"{_exposure.Value:+0.0;-0.0;0.0} EV";
    }

    private static string Key(string action)
    {
        foreach (var e in InputMap.ActionGetEvents(action))
            if (e is InputEventKey k) return OS.GetKeycodeString(k.PhysicalKeycode != Godot.Key.None ? k.PhysicalKeycode : k.Keycode);
        return action;
    }
}
