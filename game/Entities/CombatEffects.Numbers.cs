using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>
/// Terraria's floating combat text: orange hits, bigger red-orange crits, pink-red damage to players and residents, green
/// life and blue mana gains (<see cref="CombatText"/> holds the colours and motion). Numbers pop in, rise and fade, and
/// keep one size on screen at any distance (Label3D.FixedSize). Labels are pooled up to the graphics preset's
/// combatNumbers; past it the oldest number is reused. Each frame samples the motion curves, so a number costs no tween.
/// </summary>
public sealed partial class CombatEffects
{
    /// <summary>Font size the digits are rasterised at; with <see cref="NumberPixelSize"/> about 38 px tall on a 900 px view.</summary>
    private const int NumberFontSize = 56;

    private const int NumberOutline = 12;

    /// <summary>Metres per font pixel as seen from 1 m (FixedSize draws every label as if it were 1 m from the camera).</summary>
    private const float NumberPixelSize = 0.0011f;

    /// <summary>Damage numbers scatter this far (metres either way) so a flurry of hits stays readable.</summary>
    private const float NumberScatter = 0.3f;

    /// <summary>A heal floats this far above the top of the head in third person.</summary>
    private const float HealAboveHead = 0.4f;

    /// <summary>
    /// In first person, numbers about the player float in front of the eye: this far ahead, this far up, and rising less
    /// (at 1.2 m the full rise would carry them off the top of the screen before they fade).
    /// </summary>
    private const float EyeAhead = 1.2f;

    private const float EyeUp = 0.3f;
    private const float EyeRise = 0.4f;

    private static readonly Color NumberOutlineColor = new(0f, 0f, 0f, 0.85f);

    private sealed class FloatingNumber
    {
        public FloatingNumber(Label3D label) => Label = label;

        public Label3D Label { get; }
        public Vector3 Start;
        public Color Color;
        public float Age;
        public float Size;
        public float Rise;
        public bool Heal;
    }

    /// <summary>Numbers on screen, oldest first (all live equally long, so expired ones are always at the front).</summary>
    private readonly Queue<FloatingNumber> _live = new();

    private readonly Stack<FloatingNumber> _idle = new();
    private int _numbersMade;
    private Rendering.GraphicsSettings _graphics = null!;
    private PlayerEntity? _local;
    private Player.PlayerController? _localView;

    /// <summary>Numbers on screen now (probe fx.numbers).</summary>
    public int NumbersShown => _live.Count;

    /// <summary>Heal and mana numbers on screen now (probe fx.heal_numbers).</summary>
    public int HealNumbersShown
    {
        get
        {
            int n = 0;
            foreach (var number in _live) if (number.Heal) n++;
            return n;
        }
    }

    /// <summary>Labels made so far; never above the largest combatNumbers preset in use (probe fx.number_labels).</summary>
    public int NumberLabels => _numbersMade;

    private void InitializeNumbers(GameWorld game, Rendering.GraphicsSettings graphics)
    {
        _graphics = graphics;
        game.Npcs.Damaged += OnDamaged;
        game.Town.Hurt += (t, dmg) => ShowNumber(ToGodot(t.Center) + new Vector3(0, 1.1f, 0), dmg, CombatTextKind.ToPlayer, scatter: true);
        game.PlayerHealed += OnHealed;
        // The digits and the plus sign, filled and outlined, so the first hit of a session does not rasterise glyphs.
        var font = UI.UiFonts.Bold;
        font.RenderRange(0, new Vector2I(NumberFontSize, 0), '+', '9');
        font.RenderRange(0, new Vector2I(NumberFontSize, NumberOutline), '+', '9');
        SetProcess(false);
    }

    /// <summary>The local player: in first person their hurt and heal numbers float in front of the eye.</summary>
    public void Follow(PlayerEntity entity, Player.PlayerController view)
    {
        _local = entity;
        _localView = view;
    }

    private void OnDamaged(DamageEvent e)
    {
        var kind = CombatText.KindOf(e.Crit, e.ToPlayer);
        if (e.Player != null && InFirstPerson(e.Player)) ShowNumber(InFrontOfEye(), e.Amount, kind, scatter: false, rise: EyeRise);
        else ShowNumber(ToGodot(e.Position), e.Amount, kind, scatter: true);
    }

    private void OnHealed(PlayerEntity p, int amount, bool mana)
    {
        var kind = CombatText.HealKind(mana);
        if (InFirstPerson(p)) ShowNumber(InFrontOfEye(), amount, kind, scatter: false, rise: EyeRise);
        else ShowNumber(ToGodot(p.Position) + new Vector3(0, PlayerEntity.HalfExtents.Y * 2f + HealAboveHead, 0), amount, kind, scatter: false);
    }

    private bool InFirstPerson(PlayerEntity p) => p == _local && _localView != null && _localView.Rig.FirstPerson;

    private Vector3 InFrontOfEye()
    {
        var camera = _localView!.Rig.Camera.GlobalTransform;
        return camera.Origin - camera.Basis.Z * EyeAhead + new Vector3(0, EyeUp, 0);
    }

    private void ShowNumber(Vector3 at, int amount, CombatTextKind kind, bool scatter, float rise = 1f)
    {
        if (!Settings.UserSettings.Current.DamageNumbers) return;
        if (scatter) at += new Vector3((float)GD.RandRange(-NumberScatter, NumberScatter), 0, (float)GD.RandRange(-NumberScatter, NumberScatter));
        if (kind == CombatTextKind.Crit) at.X += (float)GD.RandRange(-CombatText.CritJitter, CombatText.CritJitter);
        var number = Take();
        var rgb = CombatText.Color(kind);
        number.Start = at;
        number.Color = Color.Color8(rgb.R, rgb.G, rgb.B);
        number.Age = 0f;
        number.Size = CombatText.Scale(kind);
        number.Rise = rise;
        number.Heal = kind is CombatTextKind.HealLife or CombatTextKind.HealMana;
        number.Label.Text = CombatText.Text(amount, kind);
        Place(number);
        number.Label.Visible = true;
        _live.Enqueue(number);
        SetProcess(true);
    }

    /// <summary>An idle label, a new one while under the preset's cap, or else the oldest number on screen.</summary>
    private FloatingNumber Take()
    {
        if (_idle.Count > 0) return _idle.Pop();
        if (_numbersMade < _graphics.Preset.CombatNumbers || _live.Count == 0)
        {
            _numbersMade++;
            return new FloatingNumber(NewLabel());
        }
        return _live.Dequeue();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (var number in _live)
        {
            number.Age += dt;
            Place(number);
        }
        while (_live.Count > 0 && _live.Peek().Age >= CombatText.Lifetime)
        {
            var done = _live.Dequeue();
            done.Label.Visible = false;
            _idle.Push(done);
        }
        if (_live.Count == 0) SetProcess(false);
    }

    /// <summary>Samples the pop, rise and fade at the number's age.</summary>
    private static void Place(FloatingNumber number)
    {
        var label = number.Label;
        float age = number.Age;
        float alpha = CombatText.Alpha(age);
        label.Position = number.Start + new Vector3(0, CombatText.Rise(age) * number.Rise, 0);
        label.Scale = Vector3.One * (CombatText.Pop(age) * number.Size);
        label.Modulate = new Color(number.Color, alpha);
        label.OutlineModulate = new Color(NumberOutlineColor, NumberOutlineColor.A * alpha);
    }

    private Label3D NewLabel()
    {
        var label = new Label3D
        {
            Font = UI.UiFonts.Bold,
            FontSize = NumberFontSize,
            OutlineSize = NumberOutline,
            OutlineModulate = NumberOutlineColor,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            PixelSize = NumberPixelSize,
            NoDepthTest = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(label);
        return label;
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
