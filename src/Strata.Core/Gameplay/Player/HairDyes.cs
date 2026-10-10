using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Player;

/// <summary>An RGB colour, each channel 0..1.</summary>
public readonly record struct HairRgb(float R, float G, float B)
{
    public static HairRgb Lerp(HairRgb a, HairRgb b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new HairRgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    public static HairRgb Bytes(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    public string Hex => $"#{Channel(R):x2}{Channel(G):x2}{Channel(B):x2}";

    private static int Channel(float v) => (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
}

/// <summary>What a dynamic hair dye reads about its wearer.</summary>
public readonly record struct HairDyeInputs(float Life, float Mana, WorldLayer Layer, float Hour, long Coins, float Speed, long Tick);

/// <summary>
/// The reference game's hair dyes as pure functions: a dye's colour from its mode and the wearer's life, mana, depth,
/// the time of day, coins carried and speed (no transcendental maths, so the colour is the same everywhere).
/// </summary>
public static class HairDyes
{
    /// <summary>Running speed (m/s) at which the Speed Hair Dye is fully red.</summary>
    public const float FullSpeed = 12f;

    /// <summary>Ticks per full Rainbow Hair Dye hue cycle.</summary>
    public const int RainbowTicks = 360;

    public static HairRgb Color(HairDyeParams dye, HairRgb natural, in HairDyeInputs i) => dye.Mode switch
    {
        HairDyeMode.Static => TryParseHex(dye.Color, out var c) ? c : natural,
        HairDyeMode.Life => HairRgb.Lerp(HairRgb.Bytes(95, 70, 70), HairRgb.Bytes(235, 20, 20), i.Life),
        HairDyeMode.Mana => HairRgb.Lerp(HairRgb.Bytes(70, 70, 95), HairRgb.Bytes(30, 90, 255), i.Mana),
        HairDyeMode.Depth => i.Layer switch
        {
            WorldLayer.Space => HairRgb.Bytes(215, 230, 255),
            WorldLayer.Sky => HairRgb.Bytes(120, 185, 255),
            WorldLayer.Surface => HairRgb.Bytes(70, 175, 60),
            WorldLayer.Underground => HairRgb.Bytes(140, 100, 55),
            WorldLayer.Cavern => HairRgb.Bytes(85, 85, 105),
            _ => HairRgb.Bytes(225, 60, 20),
        },
        HairDyeMode.Time => HairRgb.Lerp(HairRgb.Bytes(30, 40, 125), HairRgb.Bytes(255, 215, 80), 1f - MathF.Abs(i.Hour - 12f) / 12f),
        HairDyeMode.Money => Coins.TopTier(i.Coins) switch
        {
            CoinTier.Platinum => HairRgb.Bytes(220, 230, 245),
            CoinTier.Gold => HairRgb.Bytes(235, 190, 40),
            CoinTier.Silver => HairRgb.Bytes(185, 190, 200),
            _ => HairRgb.Bytes(185, 95, 35),
        },
        HairDyeMode.Speed => HairRgb.Lerp(HairRgb.Bytes(255, 255, 255), HairRgb.Bytes(255, 30, 30), i.Speed / FullSpeed),
        HairDyeMode.Rainbow => Hue((float)(i.Tick % RainbowTicks) / RainbowTicks),
        _ => natural,
    };

    /// <summary>A fully saturated colour around the hue wheel (0..1), piecewise linear.</summary>
    public static HairRgb Hue(float h)
    {
        float x = (h - MathF.Floor(h)) * 6f;
        float Ramp(float offset)
        {
            float d = MathF.Abs((x + offset) % 6f - 3f);
            return Math.Clamp(d - 1f, 0f, 1f);
        }
        return new HairRgb(Ramp(0f), Ramp(4f), Ramp(2f));
    }

    /// <summary>Whether a dye's colour changes as the wearer plays (the avatar re-tints it twice a second).</summary>
    public static bool IsDynamic(HairDyeParams dye) => dye.Mode is not (HairDyeMode.Static or HairDyeMode.Remove);

    /// <summary>The dye the player wears, or null.</summary>
    public static HairDyeParams? Worn(PlayerEntity p, GameWorld world) =>
        p.State.Look.HairDye is { } id && world.Session.Content.Items.TryGet(id, out var item) ? world.Uses[item].HairDye : null;

    /// <summary>The player's hair colour now: natural, or their dye's colour for their state.</summary>
    public static HairRgb ColorFor(PlayerEntity p, GameWorld world)
    {
        var look = p.State.Look;
        var natural = TryParseHex(look.HairColor, out var n) ? n : new HairRgb(0.35f, 0.23f, 0.13f);
        if (Worn(p, world) is not { } dye) return natural;
        var v = p.State.Vitals;
        var speed = p.Velocity;
        var inputs = new HairDyeInputs(
            v.MaxLife > 0 ? v.Life / (float)v.MaxLife : 0f, v.MaxMana > 0 ? v.Mana / (float)v.MaxMana : 0f,
            world.Session.Generator.Plan.Bands.LayerAt(p.FeetCell.Y), world.Session.Clock.Hour,
            Coins.Total(p.State.Inventory), MathF.Sqrt(speed.X * speed.X + speed.Z * speed.Z), world.Session.Clock.Tick);
        return Color(dye, natural, inputs);
    }

    public static bool TryParseHex(string? hex, out HairRgb color)
    {
        color = default;
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
        color = HairRgb.Bytes((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff);
        return true;
    }
}
