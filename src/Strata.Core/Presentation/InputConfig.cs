using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Interaction;

namespace Strata.Core.Presentation;

/// <summary>
/// The default controls (data/config/input.json): every input action with its bindings (<see cref="InputBinding"/>:
/// keys, mouse buttons, controller buttons and stick or trigger directions), and how a controller feels
/// (<see cref="Pad"/>). The engine registers the actions from it at start and the player's rebinds go over it. The
/// checks keep a controller able to play the whole game: every action the game reads exists, the ones a controller
/// needs have a controller binding, and no controller button does two things at once.
/// </summary>
public sealed class InputConfig
{
    public const string Path = "data/config/input.json";

    /// <summary>Actions the game reads; each must be in the file.</summary>
    public static readonly string[] Required =
    {
        "move_forward", "move_back", "move_left", "move_right", "look_left", "look_right", "look_up", "look_down",
        "jump", "sprint", "crouch", "use_primary", "use_secondary", "interact", "inventory", "map", "grapple", "pause",
        "hotbar_next", "hotbar_prev", "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_cancel", "ui_split",
        "ui_quick_move", "ui_favorite",
    };

    /// <summary>Actions a controller player cannot do without: each needs a controller binding.</summary>
    public static readonly string[] PadRequired =
    {
        "move_forward", "move_back", "move_left", "move_right", "look_left", "look_right", "look_up", "look_down",
        "jump", "crouch", "use_primary", "interact", "inventory", "map", "grapple", "pause", "hotbar_next", "hotbar_prev",
        "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_cancel", "ui_split", "ui_quick_move",
    };

    /// <summary>Menu and screen actions ("ui_…") are read only while a screen has the controller, never with gameplay.</summary>
    public static bool IsScreenAction(string action) => action.StartsWith("ui_", StringComparison.Ordinal);

    public string Comment { get; init; } = "";

    /// <summary>Bindings per action, as binding strings.</summary>
    public Dictionary<string, string[]> Actions { get; init; } = new();

    public PadConfig Pad { get; init; } = new();

    public static InputConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<InputConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    /// <summary>The parsed bindings of an action (none when it is unknown); call after <see cref="Validate"/> passed.</summary>
    public IEnumerable<InputBinding> Bindings(string action)
    {
        if (!Actions.TryGetValue(action, out var list)) yield break;
        foreach (var text in list)
            if (InputBinding.TryParse(text, out var b, out _)) yield return b;
    }

    public List<string> Validate()
    {
        var problems = new List<string>();
        // Which action each controller binding belongs to, separately for gameplay and for screens (a screen takes the
        // controller over, so the same button may do one thing in each).
        var gameplayPad = new Dictionary<string, string>();
        var screenPad = new Dictionary<string, string>();
        foreach (var (action, list) in Actions)
        {
            if (action.Length == 0 || action.Any(ch => !(char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '_')))
                problems.Add($"action '{action}' must be snake_case");
            if (list.Length == 0) problems.Add($"{action} has no bindings");
            var seen = new HashSet<string>();
            foreach (var text in list)
            {
                if (!InputBinding.TryParse(text, out var binding, out var error))
                {
                    problems.Add($"{action}: {error}");
                    continue;
                }
                if (!seen.Add(binding.ToString())) problems.Add($"{action} lists {binding} twice");
                if (!binding.IsPad) continue;
                var owners = IsScreenAction(action) ? screenPad : gameplayPad;
                if (owners.TryGetValue(binding.ToString(), out var other))
                    problems.Add($"{binding} is bound to both {other} and {action}");
                else owners[binding.ToString()] = action;
            }
        }
        foreach (var action in Required)
            if (!Actions.ContainsKey(action)) problems.Add($"missing action {action}");
        foreach (var action in PadRequired)
            if (Actions.ContainsKey(action) && !Bindings(action).Any(b => b.IsPad)) problems.Add($"{action} has no controller binding");
        Pad.Validate(problems);
        return problems;
    }
}

/// <summary>How a controller feels: look speed and curve, the default dead zone, the screen cursor's repeat and aim assist.</summary>
public sealed class PadConfig
{
    /// <summary>Turning speed (degrees per second) at full tilt of the look stick and sensitivity 1.</summary>
    public float LookSpeed { get; init; } = 220f;

    /// <summary>Pitch speed as a share of <see cref="LookSpeed"/> (looking up and down is slower than turning).</summary>
    public float PitchRatio { get; init; } = 0.7f;

    /// <summary>The look stick's response curve exponent (1 linear; 2 fine near the centre, fast at the rim).</summary>
    public float Exponent { get; init; } = 2f;

    /// <summary>The default stick dead zone (the Controls setting starts here).</summary>
    public float Deadzone { get; init; } = 0.15f;

    /// <summary>Stick length treated as full tilt.</summary>
    public float OuterDeadzone { get; init; } = 0.95f;

    /// <summary>Seconds a direction is held on a screen before the cursor starts repeating.</summary>
    public float RepeatDelay { get; init; } = 0.35f;

    /// <summary>Seconds between repeated cursor steps while the direction stays held.</summary>
    public float RepeatInterval { get; init; } = 0.09f;

    public AimAssistConfig AimAssist { get; init; } = new();

    public void Validate(List<string> problems)
    {
        if (LookSpeed is < 30 or > 720) problems.Add("pad.lookSpeed must be within [30, 720] degrees per second");
        if (PitchRatio is <= 0 or > 1.5f) problems.Add("pad.pitchRatio must be within (0, 1.5]");
        if (Exponent is < 1 or > 4) problems.Add("pad.exponent must be within [1, 4]");
        if (Deadzone is < 0 or > 0.5f) problems.Add("pad.deadzone must be within [0, 0.5]");
        if (OuterDeadzone <= Deadzone || OuterDeadzone > 1) problems.Add("pad.outerDeadzone must be above the dead zone and at most 1");
        if (RepeatDelay is < 0.05f or > 2) problems.Add("pad.repeatDelay must be within [0.05, 2] seconds");
        if (RepeatInterval is < 0.02f or > 1) problems.Add("pad.repeatInterval must be within [0.02, 1] seconds");
        AimAssist.Validate(problems);
    }
}
