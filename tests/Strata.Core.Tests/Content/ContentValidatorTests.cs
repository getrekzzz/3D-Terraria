using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Content.Validation;

namespace Strata.Core.Tests.Content;

public class ContentValidatorTests
{
    private static ContentDatabase Load(string json) => ContentLoader.LoadFromTexts(new[] { ("test.json", json) });

    private static void AssertError(ContentDatabase db, string fragment) =>
        Assert.Contains(db.Report.Errors, e => e.Message.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public void DetectsUnknownReferences()
    {
        var db = Load("""{ "items": [ { "id": "sword", "damage": 5, "damageType": "melee", "projectile": "nope" } ] }""");
        AssertError(db, "unknown projectile 'nope'");
    }

    [Fact]
    public void DetectsDuplicateIds()
    {
        var db = Load("""{ "items": [ { "id": "a" }, { "id": "a" } ] }""");
        Assert.Contains(db.Report.Errors, e => e.Message.Contains("Duplicate"));
    }

    [Fact]
    public void RejectsUnknownFieldsStrictly()
    {
        var db = Load("""{ "items": [ { "id": "a", "damge": 5 } ] }""");
        Assert.True(db.Report.HasErrors);
    }

    [Fact]
    public void RejectsUnknownSpecTypesAndBadParams()
    {
        var db = Load("""
        { "enemies": [
            { "id": "x", "brain": { "type": "teleporter" } },
            { "id": "y", "brain": { "type": "slimeHop", "jumpHight": 3 } }
        ] }
        """);
        AssertError(db, "Unknown Brain type 'teleporter'");
        AssertError(db, "Invalid Brain 'slimeHop'");
    }

    [Fact]
    public void BossCompanionsAndEnragedAttacksAreChecked()
    {
        var db = Load("""
        { "flags": [ { "id": "pair_down", "name": "Pair", "kind": "boss" }, { "id": "other_down", "name": "Other", "kind": "boss" } ],
          "projectiles": [ { "id": "zap", "friendly": false } ],
          "enemies": [
            { "id": "left", "boss": { "title": "Pair", "flag": "pair_down", "companions": ["right", "stranger", "ghost"] },
              "ranged": { "projectile": "zap", "parts": [3], "enraged": { "projectile": "zap", "enraged": { "projectile": "zap" } } } },
            { "id": "right", "boss": { "title": "Pair", "flag": "pair_down", "companions": ["left"] } },
            { "id": "stranger", "boss": { "title": "Other", "flag": "other_down" } }
          ] }
        """);
        AssertError(db, "'right' may not bring companions of its own");
        AssertError(db, "'stranger' must be a boss with the same flag");
        AssertError(db, "unknown enemy 'ghost'");
        AssertError(db, "ranged.parts must index body parts");
        AssertError(db, "ranged.enraged may not nest another enraged attack");
    }

    [Fact]
    public void ValidatesLiquids()
    {
        var db = Load("""
        { "liquids": [
            { "id": "water", "code": 1, "bucket": "water_bucket", "mixes": [ { "with": "lava", "block": "nope" } ] },
            { "id": "lava", "code": 1, "contactBuff": "missing" }
          ],
          "items": [
            { "id": "water_bucket", "behaviors": [ { "type": "bucket", "liquid": "lava", "empty": "pail" } ] }
          ] }
        """);
        AssertError(db, "code 1 is already used by 'water'");
        AssertError(db, "unknown block 'nope'");
        AssertError(db, "unknown buff 'missing'");
        AssertError(db, "unknown item 'pail'");
        AssertError(db, "bucket holds 'lava' but that liquid's bucket is ''");
        AssertError(db, "places liquid code 3");
    }

    [Fact]
    public void DetectsLootCycles()
    {
        var db = Load("""
        { "lootTables": [
            { "id": "a", "entries": [ { "table": "b" } ] },
            { "id": "b", "entries": [ { "table": "a" } ] }
        ] }
        """);
        AssertError(db, "dependency cycle");
    }

    [Fact]
    public void DetectsProgressionCycles()
    {
        var db = Load("""{ "flags": [ { "id": "a", "requires": ["b"] }, { "id": "b", "requires": ["a"] } ] }""");
        AssertError(db, "dependency cycle");
    }

    [Fact]
    public void RecipesNeedExistingStations()
    {
        var db = Load("""
        { "items": [ { "id": "a", "tags": ["starter"] }, { "id": "b" } ],
          "recipes": [ { "id": "b", "output": "b", "inputs": [ { "item": "a" } ], "stations": ["forge_of_dreams"] } ] }
        """);
        AssertError(db, "station 'forge_of_dreams'");
    }

    [Fact]
    public void NoMeleeBladesCannotSwingAndShotLimitsMustAllowAShot()
    {
        var db = Load("""
        { "projectiles": [ { "id": "blade", "returns": 30 }, { "id": "beam" } ],
          "items": [
            { "id": "fake_zenith", "useStyle": "swing", "noMelee": true, "damage": 5, "damageType": "melee", "projectile": "beam",
              "behaviors": [ { "type": "meleeSwing" }, { "type": "shootProjectile" } ] },
            { "id": "triple_rang", "useStyle": "shoot", "damage": 5, "damageType": "melee", "projectile": "blade",
              "behaviors": [ { "type": "shootProjectile", "count": 3 } ] },
            { "id": "bad_limit", "useStyle": "shoot", "damage": 5, "damageType": "melee", "projectile": "beam",
              "behaviors": [ { "type": "shootProjectile", "maxActive": -1 } ] },
            { "id": "real_zenith", "useStyle": "swing", "noMelee": true, "damage": 5, "damageType": "melee", "projectile": "blade",
              "behaviors": [ { "type": "shootProjectile", "count": 3, "maxActive": 0 } ] }
          ] }
        """);
        AssertError(db, "noMelee items cannot have a meleeSwing behaviour");
        AssertError(db, "fires 3 'blade' per use but only 1 may be out at once");
        AssertError(db, "maxActive must be >= 0");
        Assert.DoesNotContain(db.Report.Errors, e => e.Subject.EndsWith(":real_zenith", StringComparison.Ordinal));
    }

    [Fact]
    public void BiomeReliefIsRangeChecked()
    {
        var db = Load("""
        { "biomes": [
            { "id": "peaks", "relief": { "base": 5, "ruggedness": 2, "ridges": 70, "mountains": { "height": 10, "peak": 90 } } },
            { "id": "plains", "relief": { "base": 5, "rolling": 20, "ruggedness": 0.2, "cliffs": 0.1, "mountains": { "height": 20, "peak": 30 } } }
        ] }
        """);
        AssertError(db, "relief.ruggedness must be within 0–1");
        AssertError(db, "relief.ridges must be within 0–60");
        AssertError(db, "relief.mountains.peak must be within 0–80");
        Assert.DoesNotContain(db.Report.Errors, e => e.Subject.EndsWith(":plains", StringComparison.Ordinal));
    }

    [Fact]
    public void BuffsAndInvasionsNeedIcons()
    {
        var db = ContentLoader.LoadFromTexts(new[] { ("test.json", """
        { "buffs": [ { "id": "glowing", "name": "Glowing" }, { "id": "drawn", "name": "Drawn", "icon": "drawn_icon" } ],
          "events": [
            { "id": "raid", "phase": "manual", "killGoal": 5, "killTag": "raider" },
            { "id": "drizzle", "chance": 0.1 }
          ] }
        """) }, new ContentValidationOptions { AssetExists = p => p == "assets/icons/buffs/drawn_icon.png" });
        AssertError(db, "icon asset 'assets/icons/buffs/glowing.png' does not exist");
        AssertError(db, "icon asset 'assets/icons/events/raid.png' does not exist");
        Assert.DoesNotContain(db.Report.Errors, e => e.Subject.EndsWith(":drawn", StringComparison.Ordinal));
        Assert.DoesNotContain(db.Report.Errors, e => e.Subject.EndsWith(":drizzle", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsBadIdsAndCovers()
    {
        var db = Load("""{ "items": [ { "id": "BadId", "covers": ["copper"] } ] }""");
        AssertError(db, "snake_case");
        AssertError(db, "terraria:<concept>");
    }

    [Fact]
    public void RarityOutsideTerrariaTiersIsAnErrorAndSilentBuffsWarn()
    {
        var db = Load("""
        { "buffs": [ { "id": "mystery" } ],
          "items": [ { "id": "relic", "rarity": 20 }, { "id": "odd_potion", "rarity": -12, "buff": { "id": "mystery", "ticks": 60 } } ] }
        """);
        AssertError(db, "rarity must be -13–13");
        Assert.DoesNotContain(db.Report.Errors, e => e.Message.Contains("rarity") && e.Message.Contains("odd_potion"));
        Assert.Contains(db.Report.Warnings, w => w.Message.Contains("buff 'mystery' has no description"));
    }
}
