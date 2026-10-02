namespace Strata.Core.Gameplay.Player;

/// <summary>How a character is played: what death costs.</summary>
public enum CharacterDifficulty
{
    /// <summary>Death drops half the coins carried.</summary>
    Classic,

    /// <summary>Death drops everything in the inventory.</summary>
    Mediumcore,

    /// <summary>Death is permanent: the character is lost.</summary>
    Hardcore,
}

/// <summary>
/// What a character looks like, chosen at creation: the body and clothing style pick the body model, the hair style a
/// hair model worn on the head, and the colours tint them (hair, skin, eyes, shirt, undershirt, trousers, shoes).
/// </summary>
public sealed class Appearance
{
    public static readonly string[] Bodies = { "male", "female" };

    /// <summary>Hair styles (any body; a hair model per body and style, "bald" has none).</summary>
    public static readonly string[] HairStyles =
    {
        "short", "messy", "long", "buzz", "bald", "ponytail", "bun", "mohawk", "spiky", "afro", "curly", "bob", "pixie",
        "side_part", "slicked_back", "undercut", "mullet", "braid", "pigtails", "twin_buns", "topknot", "dreadlocks",
        "wavy_long",
    };

    /// <summary>The styles offered to each body before every style suited both (kept for callers of the old lists).</summary>
    public static readonly string[] MaleStyles = HairStyles;

    public static readonly string[] FemaleStyles = HairStyles;

    /// <summary>Clothing styles: a shirt; a vest over the undershirt; a long coat over the undershirt.</summary>
    public static readonly string[] ClothingStyles = { "shirt", "vest", "coat" };

    /// <summary>Starting hair per body (the first style the creator offers).</summary>
    public static string DefaultStyle(string body) => body == "female" ? "long" : "short";

    public string Body { get; set; } = "male";
    public string HairStyle { get; set; } = "short";
    public string ClothingStyle { get; set; } = "shirt";
    public string HairColor { get; set; } = "#5a3a20";
    public string SkinColor { get; set; } = "#e0b090";
    public string EyeColor { get; set; } = "#5a7890";
    public string ShirtColor { get; set; } = "#4a7ac0";
    public string UndershirtColor { get; set; } = "#d8d0c0";
    public string PantsColor { get; set; } = "#3a4a6a";
    public string ShoeColor { get; set; } = "#5a3c26";

    /// <summary>The hair styles offered for a body (every style suits either).</summary>
    public static string[] StylesFor(string body) => HairStyles;

    private string BodyKey => Body == "female" ? "f" : "m";

    /// <summary>The generated body model for this body and clothing style (assets/models/creatures/&lt;id&gt;.glb).</summary>
    public string ModelId => $"player_{BodyKey}_{(Array.IndexOf(ClothingStyles, ClothingStyle) >= 0 ? ClothingStyle : ClothingStyles[0])}";

    /// <summary>The hair model worn on the head (assets/models/hair/&lt;id&gt;.glb), or null when bald or unknown.</summary>
    public string? HairModelId => HairStyle == "bald" || Array.IndexOf(HairStyles, HairStyle) < 0 ? null : $"{BodyKey}_{HairStyle}";

    /// <summary>A random look (the creator's Randomize button): body, styles and natural or bold colours.</summary>
    public static Appearance Random(Random rng)
    {
        string Pick(string[] from) => from[rng.Next(from.Length)];
        return new Appearance
        {
            Body = Pick(Bodies), HairStyle = Pick(HairStyles), ClothingStyle = Pick(ClothingStyles),
            HairColor = Pick(HairPalette), SkinColor = Pick(SkinPalette), EyeColor = Pick(EyePalette),
            ShirtColor = Pick(ClothPalette), UndershirtColor = Pick(ClothPalette), PantsColor = Pick(ClothPalette),
            ShoeColor = Pick(ShoePalette),
        };
    }

    public static readonly string[] HairPalette = { "#1a1410", "#3a2414", "#5a3a20", "#8a5a30", "#c89050", "#e8d090", "#b03020", "#e8e8e8", "#6a6a70", "#3a6ab0", "#c04080", "#40a060" };
    public static readonly string[] SkinPalette = { "#f4d0b0", "#e0b090", "#c8946a", "#a8704a", "#7a4a2a", "#4a2a1a" };
    public static readonly string[] EyePalette = { "#5a7890", "#3a6ab0", "#4a8a4a", "#6a4a2a", "#2a1a10", "#8a8a9a", "#a050c0", "#c03030" };
    public static readonly string[] ClothPalette = { "#4a7ac0", "#c04040", "#40a050", "#e0c040", "#8a4ac0", "#e8e0d0", "#2a2a30", "#a06030", "#30a0a0", "#e07030" };
    public static readonly string[] ShoePalette = { "#5a3c26", "#2a2420", "#6a6a70", "#8a5a30", "#1a1a1a" };

    public Appearance Clone() => (Appearance)MemberwiseClone();
}
