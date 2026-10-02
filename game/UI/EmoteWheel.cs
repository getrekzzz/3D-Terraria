using Godot;

namespace Strata.Game.UI;

/// <summary>
/// The player's emote wheel (X-05): hold the emote key (T) and move the mouse toward an emote; releasing shows it over
/// the player's head (<see cref="Strata.Core.Gameplay.Town.TownSystem.PlayerEmote"/>), and residents nearby answer.
/// Works with a captured mouse: the pick follows the accumulated mouse motion. Esc or no movement cancels.
/// </summary>
public sealed partial class EmoteWheel : CanvasLayer
{
    private const string Gate = "emote_wheel";
    private const float Radius = 120f, IconSize = 52f, DeadZone = 24f;

    private UiContext _ui = null!;
    private Control _canvas = null!;
    private string[] _emotes = System.Array.Empty<string>();
    private Texture2D?[] _icons = System.Array.Empty<Texture2D?>();
    private Vector2 _aim;
    private int _selected = -1;

    public bool IsOpen => _canvas.Visible;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "EmoteWheel";
        Layer = 25;
        _emotes = ui.Game.Town.Config.Emotes.Wheel;
        _icons = new Texture2D?[_emotes.Length];
        for (int i = 0; i < _emotes.Length; i++)
        {
            string path = $"res://assets/icons/emotes/{_emotes[i]}.png";
            _icons[i] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }
        _canvas = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.Draw += DrawWheel;
        AddChild(_canvas);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputGate.TextEntry) return;
        if (!IsOpen)
        {
            if (e.IsActionPressed("emote_wheel") && !InputGate.GameplayBlocked && _ui.Entity.Alive) Open();
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion:
                _aim = (_aim + motion.Relative).LimitLength(Radius);
                Pick();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                Close(emit: true);
                break;
            default:
                if (e.IsActionReleased("emote_wheel")) Close(emit: true);
                else if (e.IsActionPressed("pause")) Close(emit: false);
                else return;
                break;
        }
        GetViewport().SetInputAsHandled();
    }

    private void Open()
    {
        _aim = Vector2.Zero;
        _selected = -1;
        _canvas.Visible = true;
        _canvas.QueueRedraw();
        InputGate.SetOpen(Gate, true);
        Audio.Sfx.Play("ui_open", -6f);
        GetViewport().SetInputAsHandled();
    }

    private void Close(bool emit)
    {
        _canvas.Visible = false;
        InputGate.SetOpen(Gate, false);
        if (emit && _selected >= 0 && _ui.Game.Town.PlayerEmote(_ui.Entity, _emotes[_selected])) Audio.Sfx.Play("ui_click");
    }

    private void Pick()
    {
        int before = _selected;
        if (_aim.Length() < DeadZone || _emotes.Length == 0) _selected = -1;
        else
        {
            // Clockwise from the top: angle 0 is up the screen.
            float a = Mathf.PosMod(Mathf.Atan2(_aim.X, -_aim.Y), Mathf.Tau);
            _selected = (int)Mathf.Round(a / (Mathf.Tau / _emotes.Length)) % _emotes.Length;
        }
        if (before != _selected) Audio.Sfx.Play("ui_hover", -10f);
        _canvas.QueueRedraw();
    }

    private void DrawWheel()
    {
        var centre = _canvas.Size * 0.5f;
        _canvas.DrawCircle(centre, Radius + IconSize * 0.75f, new Color(0.05f, 0.06f, 0.1f, 0.55f));
        _canvas.DrawArc(centre, Radius + IconSize * 0.75f, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.25f), 2f);
        for (int i = 0; i < _emotes.Length; i++)
        {
            float a = i * Mathf.Tau / _emotes.Length;
            var at = centre + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * Radius;
            float size = i == _selected ? IconSize * 1.3f : IconSize;
            if (i == _selected) _canvas.DrawCircle(at, size * 0.62f, new Color(1f, 0.85f, 0.35f, 0.45f));
            if (_icons[i] is { } icon) _canvas.DrawTextureRect(icon, new Rect2(at - Vector2.One * size * 0.5f, Vector2.One * size), false);
        }
        if (_aim.Length() >= DeadZone) _canvas.DrawLine(centre, centre + _aim, new Color(1, 1, 1, 0.5f), 2f);
        _canvas.DrawCircle(centre, 5f, new Color(1, 1, 1, 0.8f));
    }
}
