namespace Strata.Core.Presentation;

/// <summary>
/// Terraria's chat log, engine-neutral: the newest <see cref="Capacity"/> world messages, each with its colour and the
/// time it arrived. The log shows the last <see cref="Shown"/> lines; each stays <see cref="Hold"/> seconds and then fades
/// out over <see cref="Fade"/>. The history view (Enter) shows every kept line at full strength.
/// </summary>
public sealed class ChatHistory
{
    /// <summary>Lines kept for the history view; older ones are dropped.</summary>
    public const int Capacity = 50;

    /// <summary>Lines the log shows at most while the history view is closed.</summary>
    public const int Shown = 10;

    /// <summary>Seconds a line shows at full strength.</summary>
    public const double Hold = 10;

    /// <summary>Seconds a line takes to fade out after <see cref="Hold"/>.</summary>
    public const double Fade = 1;

    /// <summary>One message: its text, colour and the time (seconds) it arrived.</summary>
    public readonly record struct Line(string Text, Rgb Color, double Time);

    private readonly Line[] _lines = new Line[Capacity];
    private int _first;

    /// <summary>Lines kept (at most <see cref="Capacity"/>).</summary>
    public int Count { get; private set; }

    /// <summary>Lines ever added; changes with every message, even once the oldest are being dropped.</summary>
    public int Added { get; private set; }

    /// <summary>The kept line at <paramref name="index"/>, 0 being the oldest.</summary>
    public Line this[int index] =>
        (uint)index < (uint)Count ? _lines[(_first + index) % Capacity] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Adds a message at <paramref name="time"/>; the oldest line goes once <see cref="Capacity"/> are kept.</summary>
    public void Add(string text, Rgb color, double time)
    {
        var line = new Line(text, color, time);
        if (Count < Capacity) _lines[(_first + Count++) % Capacity] = line;
        else
        {
            _lines[_first] = line;
            _first = (_first + 1) % Capacity;
        }
        Added++;
    }

    /// <summary>A line's strength <paramref name="age"/> seconds after it arrived: 1 while held, 0 once faded.</summary>
    public static float Alpha(double age) =>
        age <= Hold ? 1f : age >= Hold + Fade ? 0f : (float)(1 - (age - Hold) / Fade);

    /// <summary>
    /// The index of the oldest line the log shows at <paramref name="now"/> (<see cref="Count"/> for none): among the
    /// last <see cref="Shown"/>, the first not yet faded out. Lines arrive in order, so every later one shows too.
    /// </summary>
    public int FirstShown(double now)
    {
        for (int i = Math.Max(0, Count - Shown); i < Count; i++)
            if (Alpha(now - this[i].Time) > 0) return i;
        return Count;
    }
}
