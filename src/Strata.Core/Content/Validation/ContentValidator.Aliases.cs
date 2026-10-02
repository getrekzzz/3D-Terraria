namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private const string AliasFile = "data/config/aliases.json";

    /// <summary>
    /// The alias table is global: <see cref="ContentDatabase.Current"/> resolves item, block, prefix and every other id
    /// through it. An alias key that is still a live id in any category would remap that definition too (an old prefix
    /// id equal to an item id would turn the item into the prefix's new name), so alias keys must not be live ids. (Targets
    /// may be model or ore-tier names, not only content ids.)
    /// </summary>
    private static void ValidateAliases(Ctx ctx)
    {
        var registries = ctx.Db.AllRegistries.ToList();
        foreach (var (from, to) in ctx.Db.Aliases)
        {
            foreach (var r in registries.Where(r => r.Contains(from)))
                ctx.Report.Error(AliasFile, from, $"alias key '{from}' is also a live {r.Category} id; it would remap that {r.Category} to '{to}'.");
        }
    }
}
