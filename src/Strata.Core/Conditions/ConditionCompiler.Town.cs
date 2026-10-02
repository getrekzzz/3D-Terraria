using Strata.Core.Content;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Town;

namespace Strata.Core.Conditions;

public static partial class ConditionCompiler
{
    /// <summary>Town and player conditions: wealth, life, residents, carried items.</summary>
    private static void AddTown(Dictionary<string, Func<TypedSpec, ICondition>> f)
    {
        f["coins"] = s =>
        {
            var p = s.Parse<CoinsConditionParams>();
            return new DelegateCondition(c => c.PlayerCoins >= p.Min);
        };
        f["maxLife"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.PlayerMaxLife >= p.Min && c.PlayerMaxLife <= p.Max);
        };
        f["resident"] = s =>
        {
            var p = s.Parse<ResidentConditionParams>();
            return new DelegateCondition(c => c.Residents.Contains(p.Id) == p.Present);
        };
        f["residents"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.ResidentCount >= p.Min && c.ResidentCount <= p.Max);
        };
        f["hasItem"] = s =>
        {
            var p = s.Parse<HasItemConditionParams>();
            string key = p.Item ?? (p.Tag != null ? "#" + p.Tag : "@" + p.Category);
            return new DelegateCondition(c => (c.ItemCount?.Invoke(key) ?? 0) >= p.Count);
        };

        // --- P5-03: rescues, journal facts (bestiary, kills, pickups, achievements), angler quests ---
        f["rescued"] = s =>
        {
            string flag = TownRescue.FlagFor(s.Parse<RescuedConditionParams>().Id);
            return new DelegateCondition(c => c.Flags.Contains(flag));
        };
        f["bestiary"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Records is { } r && r.BestiaryPercent >= p.Min && r.BestiaryPercent <= p.Max);
        };
        f["anglerQuests"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.AnglerQuests >= p.Min && c.AnglerQuests <= p.Max);
        };
        f["killed"] = s =>
        {
            var p = s.Parse<KilledConditionParams>();
            return new DelegateCondition(c => (c.Records?.Kills(p.Enemy) ?? 0) >= p.Min);
        };
        f["obtained"] = s =>
        {
            var p = s.Parse<ObtainedConditionParams>();
            return new DelegateCondition(c => c.Records?.Obtained(p.Item) ?? false);
        };
        f["achieved"] = s =>
        {
            var p = s.Parse<AchievedConditionParams>();
            return new DelegateCondition(c => c.Records?.Achieved(p.Id) ?? false);
        };
    }

    private static void RegisterTownSpecs(SpecCatalog c)
    {
        c.Register<CoinsConditionParams>(SpecKind.Condition, "coins");
        c.Register<RangeConditionParams>(SpecKind.Condition, "maxLife");
        c.Register<ResidentConditionParams>(SpecKind.Condition, "resident");
        c.Register<RangeConditionParams>(SpecKind.Condition, "residents");
        c.Register<HasItemConditionParams>(SpecKind.Condition, "hasItem");

        // --- P5-03 ---
        c.Register<RescuedConditionParams>(SpecKind.Condition, "rescued");
        c.Register<RangeConditionParams>(SpecKind.Condition, "bestiary");
        c.Register<RangeConditionParams>(SpecKind.Condition, "anglerQuests");
        c.Register<KilledConditionParams>(SpecKind.Condition, "killed");
        c.Register<ObtainedConditionParams>(SpecKind.Condition, "obtained");
        c.Register<AchievedConditionParams>(SpecKind.Condition, "achieved");
    }
}
