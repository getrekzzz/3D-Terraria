using System.Text.RegularExpressions;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

/// <summary>
/// Cross-reference and sanity validation for all content. Every broken reference is an error so the
/// game never ships with dangling ids; stylistic concerns are warnings.
/// </summary>
public static partial class ContentValidator
{
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex CoverPattern = new("^terraria:[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static void Validate(ContentDatabase db, ContentValidationOptions options)
    {
        var ctx = new Ctx(db, options);
        foreach (var registry in db.AllRegistries)
            foreach (var def in registry.Definitions)
                ValidateCommon(ctx, def);

        ValidateBlocks(ctx);
        ValidateOreVeins(ctx);
        ValidateTileObjects(ctx);
        ValidateItems(ctx);
        ValidateRecipes(ctx);
        ValidateEnemies(ctx);
        ValidateProjectilesAndBuffs(ctx);
        ValidateLoot(ctx);
        ValidateFlags(ctx);
        if (options.CheckObtainability) ValidateObtainability(ctx);
    }

    private static void ValidateCommon(Ctx ctx, ContentDef def)
    {
        if (!IdPattern.IsMatch(def.Id)) ctx.Error(def, "id must be snake_case ([a-z][a-z0-9_]*).");
        foreach (var c in def.Covers)
            if (!CoverPattern.IsMatch(c)) ctx.Error(def, $"covers entry '{c}' must look like 'terraria:<concept>'.");
    }

    private sealed class Ctx
    {
        public Ctx(ContentDatabase db, ContentValidationOptions options)
        {
            Db = db;
            Options = options;
        }

        public ContentDatabase Db { get; }
        public ContentValidationOptions Options { get; }
        public ContentReport Report => Db.Report;

        public void Error(ContentDef def, string message) => Report.Error(def, message);

        public void Warn(ContentDef def, string message) => Report.Warn(def, message);

        public void Ref<T>(ContentDef owner, Registry<T> registry, string? id, string field) where T : ContentDef
        {
            if (id != null && !registry.Contains(id))
                Error(owner, $"{field} references unknown {registry.Category} '{id}'.");
        }

        public void Spec(ContentDef owner, SpecKind kind, TypedSpec spec, string field)
        {
            var err = Options.Specs.Validate(kind, spec);
            if (err != null) Error(owner, $"{field}: {err}");
        }

        public void Specs(ContentDef owner, SpecKind kind, IEnumerable<TypedSpec> specs, string field)
        {
            int i = 0;
            foreach (var s in specs) Spec(owner, kind, s, $"{field}[{i++}]");
        }

        public void Light(ContentDef owner, int[]? light, string field)
        {
            if (light == null) return;
            if (light.Length != 3 || light.Any(v => v is < 0 or > 15))
                Error(owner, $"{field} must be [r, g, b] with values 0–15.");
        }

        public void Color(ContentDef owner, string? color, string field)
        {
            if (color != null && !HexColor.IsMatch(color)) Error(owner, $"{field} '{color}' must be #RRGGBB.");
        }

        public void Asset(ContentDef owner, string? path, string field)
        {
            if (path == null || Options.AssetExists == null) return;
            if (!Options.AssetExists(path)) Error(owner, $"{field} asset '{path}' does not exist.");
        }
    }
}
