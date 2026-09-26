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

/// <summary>What a character looks like, chosen at creation (body and hair style pick the model, colours tint it).</summary>
public sealed class Appearance
{
    public static readonly string[] Bodies = { "male", "female" };

    /// <summary>Hair styles available for each body (a generated model per combination).</summary>
    public static readonly string[] MaleStyles = { "short", "messy", "long", "buzz", "bald" };

    public static readonly string[] FemaleStyles = { "long", "ponytail", "bun", "short" };

    public string Body { get; set; } = "male";
    public string HairStyle { get; set; } = "short";
    public string HairColor { get; set; } = "#5a3a20";
    public string SkinColor { get; set; } = "#e0b090";
    public string ShirtColor { get; set; } = "#4a7ac0";
    public string PantsColor { get; set; } = "#3a4a6a";
    public string ShoeColor { get; set; } = "#5a3c26";

    public static string[] StylesFor(string body) => body == "female" ? FemaleStyles : MaleStyles;

    /// <summary>The generated player model for this body and hair style (assets/models/creatures/&lt;id&gt;.glb).</summary>
    public string ModelId
    {
        get
        {
            var styles = StylesFor(Body);
            var style = Array.IndexOf(styles, HairStyle) >= 0 ? HairStyle : styles[0];
            return $"player_{(Body == "female" ? "f" : "m")}_{style}";
        }
    }

    public Appearance Clone() => (Appearance)MemberwiseClone();
}
