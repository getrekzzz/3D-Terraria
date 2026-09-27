using Strata.Core.Content;

namespace Strata.Core.Persistence;

/// <summary>
/// Maps content string ids to compact per-save numeric ids. Runtime ids are never persisted; each save
/// carries its own tables so content can be added, removed or reordered without breaking old saves.
/// </summary>
public sealed class SaveIdTable
{
    private readonly List<string> _names = new();
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public SaveIdTable()
    {
    }

    public SaveIdTable(IEnumerable<string> names)
    {
        foreach (var n in names) Intern(n);
    }

    public IReadOnlyList<string> Names
    {
        get { lock (_gate) return _names.ToArray(); }
    }

    public int Count
    {
        get { lock (_gate) return _names.Count; }
    }

    /// <summary>Save id for a content id (appends new ids).</summary>
    public int Intern(string id)
    {
        lock (_gate)
        {
            if (_byName.TryGetValue(id, out var v)) return v;
            v = _names.Count;
            _names.Add(id);
            _byName[id] = v;
            return v;
        }
    }

    public string NameOf(int saveId)
    {
        lock (_gate)
        {
            if ((uint)saveId >= (uint)_names.Count) throw new InvalidDataException($"Save id {saveId} is outside the id table.");
            return _names[saveId];
        }
    }
}

/// <summary>The id tables stored in a world save.</summary>
public sealed class WorldIdTables
{
    public SaveIdTable Blocks { get; init; } = new();
    public SaveIdTable Items { get; init; } = new();
    public SaveIdTable TileObjects { get; init; } = new();
    public SaveIdTable Prefixes { get; init; } = new();

    /// <summary>Resolves save ids to runtime definitions, creating placeholders for unknown ids.</summary>
    public ushort ResolveBlock(ContentDatabase db, int saveId) =>
        (ushort)db.GetOrAddPlaceholderBlock(Blocks.NameOf(saveId)).RuntimeId;
}
