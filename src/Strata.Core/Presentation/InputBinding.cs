namespace Strata.Core.Presentation;

/// <summary>What an <see cref="InputBinding"/> listens to.</summary>
public enum BindingKind
{
    Key,
    Mouse,
    PadButton,
    PadAxis,
}

/// <summary>
/// One input binding as data/config/input.json and the player's keybinds.cfg spell it: <c>key:W</c> (a key by its
/// engine name), <c>mouse:1</c> (a mouse button, 1 = left … 9), <c>pad:a</c> (a controller button by its Xbox name) or
/// <c>axis:left_x-</c> (one direction of a stick, or a trigger: <c>axis:rt+</c>). Parsing, checking and naming live here
/// so the data checks, the engine adapter and the controls menu agree. <see cref="Code"/> is the engine's button or axis
/// index (SDL order, as Godot's JoyButton and JoyAxis) or the mouse button; <see cref="Sign"/> the axis direction.
/// </summary>
public readonly record struct InputBinding(BindingKind Kind, string Name, int Code, int Sign)
{
    /// <summary>Controller buttons by name, in the engine's index order (a = 0 … touchpad = 20).</summary>
    public static readonly string[] PadButtons =
    {
        "a", "b", "x", "y", "back", "guide", "start", "ls", "rs", "lb", "rb",
        "dpad_up", "dpad_down", "dpad_left", "dpad_right", "misc1", "paddle1", "paddle2", "paddle3", "paddle4", "touchpad",
    };

    /// <summary>What the controls menu and the button prompts call each controller button (Xbox layout).</summary>
    private static readonly string[] PadButtonLabels =
    {
        "A", "B", "X", "Y", "View", "Guide", "Menu", "LS", "RS", "LB", "RB",
        "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right", "Share", "P1", "P2", "P3", "P4", "Touchpad",
    };

    /// <summary>Stick and trigger axes by name, in the engine's index order (left_x = 0 … rt = 5).</summary>
    public static readonly string[] PadAxes = { "left_x", "left_y", "right_x", "right_y", "lt", "rt" };

    public bool IsPad => Kind is BindingKind.PadButton or BindingKind.PadAxis;

    /// <summary>A trigger (only ever pulled one way).</summary>
    public bool IsTrigger => Kind == BindingKind.PadAxis && Code >= 4;

    public static InputBinding Key(string name) => new(BindingKind.Key, name, 0, 0);
    public static InputBinding Mouse(int button) => new(BindingKind.Mouse, button.ToString(), button, 0);
    public static InputBinding PadButton(int index) => new(BindingKind.PadButton, PadButtons[index], index, 0);
    public static InputBinding PadAxis(int axis, int sign) => new(BindingKind.PadAxis, PadAxes[axis], axis, sign < 0 ? -1 : 1);

    /// <summary>Reads a binding string; false with the reason in <paramref name="error"/> when it is not one.</summary>
    public static bool TryParse(string? text, out InputBinding binding, out string error)
    {
        binding = default;
        error = "";
        int colon = text?.IndexOf(':') ?? -1;
        if (text == null || colon <= 0)
        {
            error = $"'{text}' is not kind:name (key:W, mouse:1, pad:a, axis:left_x-)";
            return false;
        }
        string kind = text[..colon], name = text[(colon + 1)..];
        switch (kind)
        {
            case "key":
                if (name.Trim().Length == 0 || name.Trim() != name) break; // engine names may hold a space ("Kp Enter")
                binding = Key(name);
                return true;
            case "mouse":
                if (!int.TryParse(name, out int button) || button is < 1 or > 9) break;
                binding = Mouse(button);
                return true;
            case "pad":
                int index = Array.IndexOf(PadButtons, name);
                if (index < 0) break;
                binding = PadButton(index);
                return true;
            case "axis":
                if (name.Length < 2 || name[^1] is not ('+' or '-')) break;
                int axis = Array.IndexOf(PadAxes, name[..^1]);
                int sign = name[^1] == '-' ? -1 : 1;
                if (axis < 0 || (axis >= 4 && sign < 0)) break; // triggers only pull one way
                binding = PadAxis(axis, sign);
                return true;
        }
        error = kind switch
        {
            "key" => $"'{text}': a key needs its name (key:W)",
            "mouse" => $"'{text}': mouse buttons are 1 to 9",
            "pad" => $"'{text}': controller buttons are {string.Join(", ", PadButtons)}",
            "axis" => $"'{text}': axes are {string.Join(", ", PadAxes)} with + or - (triggers +)",
            _ => $"'{text}': unknown kind '{kind}' (key, mouse, pad, axis)",
        };
        return false;
    }

    /// <summary>Parses a binding the data already passed the checks for (throws on a bad one).</summary>
    public static InputBinding Parse(string text) =>
        TryParse(text, out var b, out var error) ? b : throw new FormatException(error);

    /// <summary>The binding string ("pad:a", "axis:left_y-").</summary>
    public override string ToString() => Kind switch
    {
        BindingKind.Key => "key:" + Name,
        BindingKind.Mouse => "mouse:" + Code,
        BindingKind.PadButton => "pad:" + Name,
        _ => "axis:" + Name + (Sign < 0 ? "-" : "+"),
    };

    /// <summary>
    /// How the controls menu and the prompts name a controller binding: "A", "RB", "LT", "Left Stick Up"; null for keys
    /// and mouse buttons (the engine names those).
    /// </summary>
    public string? PadLabel => Kind switch
    {
        BindingKind.PadButton => PadButtonLabels[Code],
        BindingKind.PadAxis when Code == 4 => "LT",
        BindingKind.PadAxis when Code == 5 => "RT",
        // The engine's stick Y axes point down: '-' is up.
        BindingKind.PadAxis => (Code < 2 ? "Left Stick " : "Right Stick ") + ((Code & 1) == 0 ? (Sign < 0 ? "Left" : "Right") : (Sign < 0 ? "Up" : "Down")),
        _ => null,
    };
}
