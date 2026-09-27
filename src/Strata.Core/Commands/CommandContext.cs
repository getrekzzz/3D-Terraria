using System.Globalization;
using System.Numerics;
using Strata.Core.Session;

namespace Strata.Core.Commands;

/// <summary>Engine services commands may need (moving the physical body, aiming the camera).</summary>
public interface ICommandHost
{
    /// <summary>Moves the player's body to a feet position (meters), waiting for terrain as needed.</summary>
    /// <remarks>With <paramref name="settle"/> the body lands on the nearest standable spot once terrain is loaded
    /// (surface targets are estimates: biome layers add turf and mud above the height field).</remarks>
    void Teleport(PlayerEntity player, Vector3 feet, bool settle = false);

    /// <summary>Points the player's view along a direction.</summary>
    void Aim(PlayerEntity player, Vector3 direction);
}

/// <summary>Host used by headless runs: moves the Core entity directly.</summary>
public sealed class HeadlessCommandHost : ICommandHost
{
    public void Teleport(PlayerEntity player, Vector3 feet, bool settle = false) => player.Position = feet;

    public void Aim(PlayerEntity player, Vector3 direction) => player.Aim = Vector3.Normalize(direction);
}

/// <summary>What commands operate on, plus named numeric probes used by <c>assert</c> and <c>probe</c>.</summary>
public sealed class CommandContext
{
    private readonly Dictionary<string, Func<double>> _probes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Prefix, Func<string, double?> Resolve)> _prefixProbes = new();

    public CommandContext(GameWorld world, ICommandHost host)
    {
        World = world;
        Host = host;
        StandardProbes.Register(this);
    }

    public GameWorld World { get; }
    public ICommandHost Host { get; }
    public CommandRegistry? Registry { get; set; }

    /// <summary>Position stored by the <c>mark</c> command (probe <c>player.from_mark</c>).</summary>
    public Vector3? Mark { get; set; }

    public PlayerEntity Player => World.LocalPlayer ?? throw new CommandException("no player in this world");

    /// <summary>Adds a fixed probe, e.g. <c>player.life</c>.</summary>
    public void AddProbe(string name, Func<double> read) => _probes[name] = read;

    /// <summary>Adds a parameterised probe, e.g. prefix <c>inv.</c> resolving <c>inv.torch</c>.</summary>
    public void AddPrefixProbe(string prefix, Func<string, double?> resolve) => _prefixProbes.Add((prefix, resolve));

    public IEnumerable<string> ProbeNames => _probes.Keys.Concat(_prefixProbes.Select(p => p.Prefix + "<id>")).OrderBy(n => n, StringComparer.Ordinal);

    public double ReadProbe(string name)
    {
        if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out double literal)) return literal;
        if (_probes.TryGetValue(name, out var read)) return read();
        foreach (var (prefix, resolve) in _prefixProbes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && resolve(name[prefix.Length..]) is { } v)
                return v;
        throw new CommandException($"unknown probe '{name}'");
    }
}
