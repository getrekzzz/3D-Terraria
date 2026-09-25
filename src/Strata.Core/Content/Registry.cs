namespace Strata.Core.Content;

/// <summary>Non-generic view of a registry (for validators, coverage and debug tools).</summary>
public interface IRegistry
{
    string Category { get; }
    int Count { get; }
    IEnumerable<ContentDef> Definitions { get; }
    bool Contains(string id);
}

internal interface IFreezable
{
    void Freeze();
}

/// <summary>
/// Ordered collection of definitions of one category with string→def and runtime-id→def lookup.
/// Runtime ids are assigned in insertion (manifest) order when the registry is frozen.
/// </summary>
public sealed class Registry<T> : IRegistry, IFreezable where T : ContentDef
{
    private readonly List<T> _items = new();
    private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);

    public Registry(string category) => Category = category;

    public string Category { get; }
    public bool IsFrozen { get; private set; }
    public int Count => _items.Count;
    public IReadOnlyList<T> All => _items;
    public IEnumerable<ContentDef> Definitions => _items;

    public T this[int runtimeId] => _items[runtimeId];

    public T this[string id] => Get(id);

    public bool Contains(string id) => _byId.ContainsKey(id);

    public T Get(string id) =>
        _byId.TryGetValue(id, out var def)
            ? def
            : throw new KeyNotFoundException($"Unknown {Category} id '{id}'.");

    public bool TryGet(string id, out T def) => _byId.TryGetValue(id, out def!);

    public T? Find(string? id) => id != null && _byId.TryGetValue(id, out var def) ? def : null;

    /// <summary>Adds a definition. Returns false if the id already exists.</summary>
    internal bool TryAdd(T def)
    {
        if (IsFrozen) throw new InvalidOperationException($"Registry '{Category}' is frozen.");
        if (!_byId.TryAdd(def.Id, def)) return false;
        _items.Add(def);
        return true;
    }

    /// <summary>Appends a definition after freezing (save placeholders). Assigns the next runtime id.</summary>
    internal T AddLate(T def)
    {
        lock (_items)
        {
            if (_byId.TryGetValue(def.Id, out var existing)) return existing;
            def.RuntimeId = _items.Count;
            _items.Add(def);
            _byId[def.Id] = def;
            return def;
        }
    }

    void IFreezable.Freeze()
    {
        for (int i = 0; i < _items.Count; i++) _items[i].RuntimeId = i;
        IsFrozen = true;
    }
}
