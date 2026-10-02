using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Crafting;

/// <summary>
/// A recipe's ingredients and surroundings as players read them, shared by the crafting panel and the Guide: a group
/// ingredient by its group's name ("Any Wood"), and the conditions as short phrases ("Near Water"). The stations read
/// through <see cref="Content.Index.StationNames"/>.
/// </summary>
public static class RecipeText
{
    /// <summary>The ingredient's name: its group's ("Any Iron Bar") or its one item's.</summary>
    public static string Ingredient(Ingredient ing, ContentDatabase content) =>
        ing.Group != null
            ? content.RecipeGroups.Find(ing.Group)?.DisplayName ?? Humanize(ing.Group)
            : ing.Accepts.First().DisplayName;

    /// <summary>The recipe's conditions joined with ", " ("Near Water"); "" when it has none.</summary>
    public static string Conditions(RecipeDef recipe, ContentDatabase content)
    {
        if (recipe.Conditions.Length == 0) return "";
        var parts = new string[recipe.Conditions.Length];
        for (int i = 0; i < parts.Length; i++) parts[i] = Condition(recipe.Conditions[i], content);
        return string.Join(", ", parts);
    }

    private static string Condition(TypedSpec spec, ContentDatabase content)
    {
        switch (spec.Type)
        {
            case "nearLiquid":
                var liquid = spec.Parse<NearLiquidParams>().Liquid;
                return "Near " + (content.Liquids.Find(liquid)?.DisplayName ?? Humanize(liquid));
            case "time" when spec.Parse<TimeConditionParams>().Is is { } part:
                return part == "day" ? "During the day" : "At night";
            default:
                return Humanize(spec.Type);
        }
    }

    private static string Humanize(string id)
    {
        var words = id.Replace('_', ' ').Trim();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
