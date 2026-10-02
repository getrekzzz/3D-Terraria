using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Terraria use styles: each item's style, the actions it implies, inference for items without one, and the config.</summary>
public class UseStyleTests
{
    private static readonly ItemUseTable Uses = new(TestPaths.Content);

    private static ItemUseInfo Info(string id) => Uses[TestPaths.Content.Items.Get(id)];

    [Theory]
    [InlineData("slime_crown", UseStyle.HoldUp, ItemAction.SummonBoss)]
    [InlineData("life_crystal", UseStyle.HoldUp, ItemAction.Consume)]
    [InlineData("magic_mirror", UseStyle.HoldUp, ItemAction.Consume)]
    [InlineData("lesser_healing_potion", UseStyle.Drink, ItemAction.Consume)]
    [InlineData("cooked_fish", UseStyle.Eat, ItemAction.Consume)]
    [InlineData("wooden_boomerang", UseStyle.Throw, ItemAction.Shoot)]
    [InlineData("bomb", UseStyle.Throw, ItemAction.Shoot)]
    [InlineData("wood_yoyo", UseStyle.Yoyo, ItemAction.Shoot)]
    [InlineData("ball_o_hurt", UseStyle.Flail, ItemAction.Shoot)]
    [InlineData("leather_whip", UseStyle.Whip, ItemAction.Melee)]
    [InlineData("minishark", UseStyle.Gun, ItemAction.Shoot)]
    [InlineData("laser_rifle", UseStyle.Gun, ItemAction.Shoot)]
    [InlineData("wooden_bow", UseStyle.Bow, ItemAction.Shoot)]
    [InlineData("amethyst_staff", UseStyle.Staff, ItemAction.Shoot)]
    [InlineData("water_bolt", UseStyle.Tome, ItemAction.Shoot)]
    [InlineData("copper_shortsword", UseStyle.Rapier, ItemAction.Thrust)]
    [InlineData("spear", UseStyle.Spear, ItemAction.Thrust)]
    [InlineData("copper_pickaxe", UseStyle.Swing, ItemAction.Mine | ItemAction.Melee)]
    [InlineData("copper_broadsword", UseStyle.Swing, ItemAction.Melee)]
    [InlineData("finch_staff", UseStyle.Swing, ItemAction.Shoot)]
    [InlineData("wooden_fishing_pole", UseStyle.Swing, ItemAction.Fish | ItemAction.Shoot)]
    public void ItemsUseTheirTerrariaStyle(string id, UseStyle style, ItemAction actions)
    {
        var info = Info(id);
        Assert.Equal(style, info.Style);
        Assert.True(info.Has(actions) && (info.Actions & actions) == actions, $"{id}: {info.Actions} lacks {actions}");
    }

    [Fact]
    public void HarmlessSwingsAndProjectileSpearsDoNotMelee()
    {
        // Summon staves and fishing rods swing without a hitbox; magic and projectile spears hit with their projectile only.
        foreach (var id in new[] { "finch_staff", "wooden_fishing_pole", "unholy_trident", "mushroom_spear", "north_pole", "daybreak" })
            Assert.False(Info(id).Has(ItemAction.Melee | ItemAction.Thrust), $"{id} should not melee");
        Assert.True(Info("unholy_trident").Has(ItemAction.Shoot));
        Assert.Equal(UseStyle.Spear, Info("north_pole").Style);
    }

    [Fact]
    public void ItemsWithoutAStyleAreInferredFromTheirStats()
    {
        Assert.Equal(UseStyle.Place, Info("dirt").Style); // derived placers
        Assert.Equal(UseStyle.Place, Info("daybloom_seeds").Style);
        Assert.Equal(UseStyle.Shoot, Info("grappling_hook").Style); // flung out along the aim
        Assert.Equal(UseStyle.None, Info("gel").Style);

        Assert.Equal(UseStyle.Swing, UseStyleRules.Infer(new ItemDef { Id = "p", PickPower = 35 }));
        Assert.Equal(UseStyle.Drink, UseStyleRules.Infer(new ItemDef { Id = "h", HealLife = 50 }));
        Assert.Equal(UseStyle.Eat, UseStyleRules.Infer(new ItemDef { Id = "f", Buff = new BuffApplication { Id = UseStyleRules.WellFed } }));
        Assert.Equal(UseStyle.Bow, UseStyleRules.Infer(new ItemDef { Id = "b", UseAmmo = "arrow" }));
        Assert.Equal(UseStyle.Gun, UseStyleRules.Infer(new ItemDef { Id = "g", UseAmmo = "bullet" }));
        Assert.Equal(UseStyle.Staff, UseStyleRules.Infer(new ItemDef { Id = "s", Damage = 5, DamageType = DamageClass.Magic, Projectile = "bolt" }));
        Assert.Equal(UseStyle.Gun, UseStyleRules.Resolve(new ItemDef { Id = "x", UseStyle = UseStyle.Gun, UseAmmo = "arrow" })); // data wins
    }

    [Fact]
    public void EveryUsableItemHasAStyle()
    {
        foreach (var item in TestPaths.Content.Items.All)
        {
            var info = Uses[item];
            bool usable = (item.IsWeapon && item.AmmoClass == null) || item.IsTool || info.Has(ItemAction.Consume | ItemAction.SummonBoss | ItemAction.PlaceBlock | ItemAction.PlaceObject);
            if (usable) Assert.True(info.Style != UseStyle.None, $"{item.Id} has no use style");
        }
    }

    [Fact]
    public void StylesPickTheirClips()
    {
        var styles = TestPaths.Timing.Config;
        Assert.Equal("mine", styles.ClipFor(Info("copper_pickaxe"))); // tools chop
        Assert.Equal("swing", styles.ClipFor(Info("copper_broadsword")));
        Assert.Equal("rapier", styles.ClipFor(Info("copper_shortsword")));
        Assert.Equal("spear", styles.ClipFor(Info("spear")));
        Assert.Equal("bow", styles.ClipFor(Info("wooden_bow")));
        Assert.Equal("gun", styles.ClipFor(Info("laser_rifle"))); // a magic gun is held like a gun
        Assert.Equal("staff", styles.ClipFor(Info("amethyst_staff")));
        Assert.Equal("tome", styles.ClipFor(Info("water_bolt")));
        Assert.Equal("throw", styles.ClipFor(Info("shuriken")));
        Assert.Equal("whip", styles.ClipFor(Info("leather_whip")));
        Assert.Equal("drink", styles.ClipFor(Info("lesser_healing_potion")));
        Assert.Equal("eat", styles.ClipFor(Info("cooked_fish")));
        Assert.Equal("hold_up", styles.ClipFor(Info("slime_crown"))); // held up, not drunk
        Assert.Equal(1f, styles.Style(UseStyle.Gun).ArmAim);
    }

    [Fact]
    public void ShippedStylesValidateAndMatchTheFallback()
    {
        var shipped = UseStyleConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        var fallback = UseStyleConfig.Fallback();
        Assert.Empty(shipped.Validate());
        Assert.Equal(fallback.Styles.Keys.Order(), shipped.Styles.Keys.Order());
        foreach (var (name, entry) in shipped.Styles)
        {
            var d = fallback.Styles[name];
            Assert.Equal((d.Clip, d.ToolClip, d.ArmAim), (entry.Clip, entry.ToolClip, entry.ArmAim));
            Assert.Equal(d.BladeCurve ?? Array.Empty<CurveKey>(), entry.BladeCurve ?? Array.Empty<CurveKey>());
        }
    }

    [Fact]
    public void EveryShippedStyleHasAFirstPersonPose()
    {
        var shipped = UseStyleConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        foreach (var (name, entry) in shipped.Styles) Assert.True(entry.Fp != null, $"style {name} has no fp");
        Assert.Equal("blade", shipped.Styles["swing"].Fp!.Curve);
        Assert.NotNull(shipped.Styles["swing"].Fp!.ToolKeys);
        Assert.True(shipped.Styles["gun"].Fp!.AimAtCrosshair);
        Assert.NotNull(shipped.Styles["gun"].Fp!.Recoil);
        Assert.True(shipped.Styles["drink"].Fp!.RaiseToFace);
    }

    [Fact]
    public void FirstPersonKeysMustIncrease()
    {
        var good = UseStyleConfig.Fallback();
        var styles = new Dictionary<string, UseStyleEntry>(good.Styles)
        {
            ["gun"] = new UseStyleEntry
            {
                Clip = "gun", ArmAim = 1f,
                Fp = new FpStyle
                {
                    Curve = "wiggle",
                    Keys = new[] { new FpKey { T = 0.5f }, new FpKey { T = 0.2f, Ease = "bounce" } },
                    Recoil = new FpRecoil { Decay = 0f, Pos = new[] { 0f, 0f } },
                },
            },
        };
        var problems = new UseStyleConfig { Clips = good.Clips, Styles = styles }.Validate();
        Assert.Contains("style gun: fp keys 1: t must increase", problems);
        Assert.Contains(problems, p => p.StartsWith("style gun: fp keys 1: ease must be one of", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("style gun: fp curve must be one of", StringComparison.Ordinal));
        Assert.Contains("style gun: fp recoil: decay must be within (0, 2] s", problems);
        Assert.Contains("style gun: fp recoil: pos needs [x, y, z]", problems);
    }

    [Fact]
    public void ConfigReportsMissingAndBadStyles()
    {
        var good = UseStyleConfig.Fallback();
        var styles = new Dictionary<string, UseStyleEntry>(good.Styles);
        styles.Remove("holdUp");
        styles["swing"] = new UseStyleEntry { Clip = "", ArmAim = 2f, BladeCurve = new CurveKey[] { new(0.5f, 0f, "lin"), new(0.2f, 10f, "bounce") } };
        styles["juggle"] = new UseStyleEntry();
        var problems = new UseStyleConfig { Clips = good.Clips, Styles = styles }.Validate();
        Assert.Contains("style 'holdUp' is missing", problems);
        Assert.Contains("style 'juggle' is not a use style", problems);
        Assert.Contains("style swing: clip must not be empty", problems);
        Assert.Contains("style swing: armAim must be within 0–1", problems);
        Assert.Contains("style swing: bladeCurve key 1: t must increase", problems);
        Assert.Contains(problems, p => p.StartsWith("style swing: bladeCurve key 1: ease must be one of", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorChecksStylesAndStacks()
    {
        var db = ContentLoader.LoadFromTexts(new[] { ("test.json", """
        { "items": [
            { "id": "dud_gun", "useStyle": "gun", "damage": 5, "damageType": "ranged" },
            { "id": "limp_whip", "useStyle": "whip" },
            { "id": "old_potion", "category": "potion", "maxStack": 30, "useStyle": "drink", "healLife": 5, "consumable": true },
            { "id": "copper_coin", "category": "coin", "maxStack": 100 },
            { "id": "mystery_blade", "damage": 5, "damageType": "melee" }
          ] }
        """) });
        Assert.Contains(db.Report.Errors, e => e.Subject.EndsWith(":dud_gun", StringComparison.Ordinal) && e.Message.Contains("gun items need a projectile or useAmmo"));
        Assert.Contains(db.Report.Errors, e => e.Subject.EndsWith(":limp_whip", StringComparison.Ordinal) && e.Message.Contains("whip items need damage"));
        Assert.Contains(db.Report.Errors, e => e.Subject.EndsWith(":old_potion", StringComparison.Ordinal) && e.Message.Contains("stack to 9999"));
        Assert.DoesNotContain(db.Report.Errors, e => e.Subject.EndsWith(":copper_coin", StringComparison.Ordinal));
        Assert.Contains(db.Report.Warnings, e => e.Subject.EndsWith(":mystery_blade", StringComparison.Ordinal) && e.Message.Contains("no useStyle"));
    }

    [Fact]
    public void StackablesStackTo9999()
    {
        foreach (var item in TestPaths.Content.Items.All)
            if (item.IsStackable && item.Category != ItemCategory.Coin) Assert.True(item.MaxStack == ItemDef.CommonMaxStack, $"{item.Id} stacks to {item.MaxStack}");
        Assert.Equal(ItemDef.CommonMaxStack, TestPaths.Content.Items.Get("lesser_healing_potion").MaxStack);
        Assert.Equal(ItemDef.CommonMaxStack, TestPaths.Content.Items.Get("work_bench").MaxStack); // derived furniture too
    }
}
