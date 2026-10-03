namespace Strata.Core.Presentation;

/// <summary>
/// How a use style is held in first person (data/config/use_styles.json style <c>fp</c>, P3-07). <see cref="Rest"/>
/// is the pose between uses: an offset (m) from the view model's anchor at the lower right of the view and a rotation
/// (degrees, Euler in Godot's default YXZ order, as Node3D.Rotation). <see cref="Keys"/> move the item over a use, each a
/// pose relative to the rest (added to it) at use phase t, eased in from the previous key; <see cref="ToolKeys"/> do
/// the same for the style's tools (a pickaxe chops instead of swinging). <see cref="Curve"/> makes the item follow a
/// shared gameplay curve instead (<c>blade</c>: the swing's blade angle; <c>extend</c>: a thrust's extension), so what
/// is seen is what hits. <see cref="AimAtCrosshair"/> turns the pose so its barrel or arrow converges on the crosshair
/// point; <see cref="RaiseToFace"/> marks uses lifted to the mouth (kept clear of the near plane);
/// <see cref="Recoil"/> is the kick each shot gives, decaying over its time.
/// </summary>
public sealed class FpStyle
{
    /// <summary>Shared gameplay curves a first-person pose can follow.</summary>
    public static readonly string[] Curves = { "blade", "extend" };

    public FpPose Rest { get; init; } = new();
    public FpKey[]? Keys { get; init; }
    public FpKey[]? ToolKeys { get; init; }
    public string? Curve { get; init; }
    public bool AimAtCrosshair { get; init; }
    public bool RaiseToFace { get; init; }
    public FpRecoil? Recoil { get; init; }

    public IEnumerable<string> Validate()
    {
        foreach (var p in Rest.Validate("rest")) yield return p;
        if (Curve != null && Array.IndexOf(Curves, Curve) < 0) yield return $"curve must be one of {string.Join(", ", Curves)}";
        foreach (var p in ValidateKeys("keys", Keys)) yield return p;
        foreach (var p in ValidateKeys("toolKeys", ToolKeys)) yield return p;
        if (Recoil is { } r)
        {
            foreach (var p in r.Validate()) yield return p;
        }
    }

    private static IEnumerable<string> ValidateKeys(string name, FpKey[]? keys)
    {
        if (keys == null) yield break;
        if (keys.Length == 0) yield return $"{name} must not be empty";
        for (int k = 0; k < keys.Length; k++)
        {
            var key = keys[k];
            if (key.T is < 0f or > 1f) yield return $"{name} {k}: t must be within 0–1";
            if (k > 0 && key.T <= keys[k - 1].T) yield return $"{name} {k}: t must increase";
            if (Array.IndexOf(UseStyleEntry.Eases, key.Ease) < 0) yield return $"{name} {k}: ease must be one of {string.Join(", ", UseStyleEntry.Eases)}";
            foreach (var p in key.Validate($"{name} {k}")) yield return p;
        }
    }
}

/// <summary>A first-person pose: offset (m) and rotation (degrees).</summary>
public class FpPose
{
    public float[] Pos { get; init; } = { 0f, 0f, 0f };
    public float[] Rot { get; init; } = { 0f, 0f, 0f };

    public IEnumerable<string> Validate(string where)
    {
        if (Pos.Length != 3) yield return $"{where}: pos needs [x, y, z]";
        else if (Pos.Any(v => MathF.Abs(v) > 1f)) yield return $"{where}: pos must be within ±1 m";
        if (Rot.Length != 3) yield return $"{where}: rot needs [x, y, z] degrees";
        else if (Rot.Any(v => MathF.Abs(v) > 360f)) yield return $"{where}: rot must be within ±360°";
    }
}

/// <summary>A key of a first-person use: at phase <see cref="T"/> the pose (relative to rest), eased in by <see cref="Ease"/>.</summary>
public sealed class FpKey : FpPose
{
    public float T { get; init; }
    public string Ease { get; init; } = "io";
}

/// <summary>A shot's kick: added to the pose at full strength when the shot leaves, decaying to ~5 % over <see cref="Decay"/> seconds.</summary>
public sealed class FpRecoil : FpPose
{
    public float Decay { get; init; } = 0.12f;

    public IEnumerable<string> Validate()
    {
        foreach (var p in Validate("recoil")) yield return p;
        if (Decay is <= 0f or > 2f) yield return "recoil: decay must be within (0, 2] s";
    }
}
