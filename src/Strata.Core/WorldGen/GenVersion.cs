namespace Strata.Core.WorldGen;

/// <summary>
/// Bump whenever generator output changes for the same seed. Stored in saves; golden-hash tests pin it.
/// </summary>
public static class GenVersion
{
    public const int Current = 1;
}
