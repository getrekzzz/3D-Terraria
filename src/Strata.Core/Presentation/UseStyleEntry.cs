using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strata.Core.Presentation;

/// <summary>
/// How one use style looks (data/config/use_styles.json "styles"): the body clip it plays, how far the arms follow
/// the aim, and the optional grip, hold-out and blade curve the held-item, shot-origin and swing presentation read.
/// </summary>
public sealed class UseStyleEntry
{
    /// <summary>Easing names a curve key may use (as in tools/blender/bgen/human/motion.py's tl).</summary>
    public static readonly string[] Eases = { "in", "out", "io", "lin" };

    /// <summary>The upper-body clip (animation name) a use plays.</summary>
    public string Clip { get; init; } = "use";

    /// <summary>The clip a tool of this style plays instead (pickaxes and axes chop with "mine"); null = <see cref="Clip"/>.</summary>
    public string? ToolClip { get; init; }

    /// <summary>How much of the aim pitch the arms take while the clip plays (0 = none, 1 = the arm follows the aim).</summary>
    public float ArmAim { get; init; } = 0.5f;

    /// <summary>The held item's grip in the hand bone; null = the default grip (the item along the fingers' thumb side).</summary>
    public HandGrip? Hand { get; init; }

    /// <summary>Where the business end is held relative to the eye (shot origin); null = from the eye.</summary>
    public HoldOut? Holdout { get; init; }

    /// <summary>The blade's pitch over a swing (swing only): keys [t, degrees, ease into the key].</summary>
    public CurveKey[]? BladeCurve { get; init; }

    /// <summary>How the style is held and moved in first person (P3-07); null = a plain lift from the default rest.</summary>
    public FpStyle? Fp { get; init; }

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Clip)) yield return "clip must not be empty";
        if (ToolClip is { } tool && string.IsNullOrWhiteSpace(tool)) yield return "toolClip must not be empty";
        if (ArmAim is < 0f or > 1f) yield return "armAim must be within 0–1";
        if (Hand is { } hand)
        {
            if (hand.Offset.Length != 3) yield return "hand.offset needs [x, y, z]";
            if (hand.Euler.Length != 3) yield return "hand.euler needs [x, y, z] degrees";
        }
        if (Holdout is { } h && (Math.Abs(h.Forward) > 2f || Math.Abs(h.Right) > 2f || Math.Abs(h.Down) > 2f))
            yield return "holdout offsets must be within ±2 m";
        if (BladeCurve is { } curve)
        {
            if (curve.Length < 2) yield return "bladeCurve needs at least two keys";
            for (int k = 0; k < curve.Length; k++)
            {
                var key = curve[k];
                if (key.T is < 0f or > 1f) yield return $"bladeCurve key {k}: t must be within 0–1";
                if (k > 0 && key.T <= curve[k - 1].T) yield return $"bladeCurve key {k}: t must increase";
                if (Array.IndexOf(Eases, key.Ease) < 0) yield return $"bladeCurve key {k}: ease must be one of {string.Join(", ", Eases)}";
            }
        }
        if (Fp != null)
            foreach (var problem in Fp.Validate()) yield return $"fp {problem}";
    }
}

/// <summary>A held item's grip: its offset (m) and rotation (degrees, XYZ Euler) in the hand bone.</summary>
public sealed class HandGrip
{
    public float[] Offset { get; init; } = { 0f, 0.08f, 0f };
    public float[] Euler { get; init; } = { 90f, 0f, 0f };
}

/// <summary>Where a held weapon's business end sits relative to the eye, in metres along the aim.</summary>
public sealed class HoldOut
{
    public float Forward { get; init; }
    public float Right { get; init; }
    public float Down { get; init; }
}

/// <summary>One key of a curve over a clip: at phase <see cref="T"/> the value is <see cref="Degrees"/>, eased in by <see cref="Ease"/>.</summary>
[JsonConverter(typeof(CurveKeyConverter))]
public readonly record struct CurveKey(float T, float Degrees, string Ease);

/// <summary>Reads and writes a <see cref="CurveKey"/> as the compact array [t, degrees, ease].</summary>
public sealed class CurveKeyConverter : JsonConverter<CurveKey>
{
    public override CurveKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("a curve key is [t, degrees, ease]");
        reader.Read();
        float t = reader.GetSingle();
        reader.Read();
        float degrees = reader.GetSingle();
        reader.Read();
        string ease = reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException("a curve key's ease is a name");
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("a curve key is [t, degrees, ease]");
        return new CurveKey(t, degrees, ease);
    }

    public override void Write(Utf8JsonWriter writer, CurveKey value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.T);
        writer.WriteNumberValue(value.Degrees);
        writer.WriteStringValue(value.Ease);
        writer.WriteEndArray();
    }
}
