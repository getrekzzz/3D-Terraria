using System.Text.Json;
using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Character looks: old saves keep loading, models resolve, Randomize stays within the creator's choices.</summary>
public class AppearanceTests
{
    [Fact]
    public void AnOldSaveLooksLikeItDidWithDefaultsForTheNewFields()
    {
        const string old = """{"body":"male","hairStyle":"messy","hairColor":"#c89050","skinColor":"#a8704a","shirtColor":"#c04040","pantsColor":"#303038","shoeColor":"#1a1a1a"}""";
        var look = JsonSerializer.Deserialize<Appearance>(old, SaveJson.Options)!;
        Assert.Equal("messy", look.HairStyle);
        Assert.Equal("#c89050", look.HairColor);
        Assert.Equal("shirt", look.ClothingStyle);
        Assert.Equal("player_m_shirt", look.ModelId);
        Assert.Equal("m_messy", look.HairModelId);
        Assert.Equal(new Appearance().EyeColor, look.EyeColor);
        Assert.Equal(new Appearance().UndershirtColor, look.UndershirtColor);
    }

    [Fact]
    public void EveryFormerStyleIsStillOffered()
    {
        foreach (var style in new[] { "short", "messy", "long", "buzz", "bald", "ponytail", "bun" })
            Assert.Contains(style, Appearance.HairStyles);
        Assert.True(Appearance.HairStyles.Length >= 20);
    }

    [Fact]
    public void BaldAndUnknownStylesWearNoHairAndUnknownClothesFallBack()
    {
        Assert.Null(new Appearance { HairStyle = "bald" }.HairModelId);
        Assert.Null(new Appearance { HairStyle = "no_such_style" }.HairModelId);
        Assert.Equal("player_f_shirt", new Appearance { Body = "female", ClothingStyle = "chainmail" }.ModelId);
        Assert.Equal("player_f_coat", new Appearance { Body = "female", ClothingStyle = "coat" }.ModelId);
    }

    [Fact]
    public void RandomLooksUseOnlyOfferedChoicesAndAreDeterministicPerSeed()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var look = Appearance.Random(new Random(seed));
            Assert.Contains(look.Body, Appearance.Bodies);
            Assert.Contains(look.HairStyle, Appearance.HairStyles);
            Assert.Contains(look.ClothingStyle, Appearance.ClothingStyles);
            Assert.Contains(look.EyeColor, Appearance.EyePalette);
            Assert.Equal(look.ModelId, Appearance.Random(new Random(seed)).ModelId);
        }
    }
}
