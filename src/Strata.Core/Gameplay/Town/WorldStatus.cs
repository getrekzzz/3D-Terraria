using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Session;
using Strata.Core.Simulation;

namespace Strata.Core.Gameplay.Town;

/// <summary>How much of the world is pure, evil and hallowed, in whole percent (summing to 100).</summary>
public sealed record WorldStatusReport(int Pure, int Evil, int Hallow, long Tick);

/// <summary>
/// The Dryad's world status: an incremental sampler over a <see cref="Step"/>-cell XZ grid at the surface and
/// underground, <see cref="SamplesPerTick"/> samples per tick so it never causes a spike, using the planned biome
/// and zones (spread and purification zones included) rather than counting blocks, so it is approximate. A finished
/// report stays fresh for one in-game hour.
/// </summary>
public sealed class WorldStatus
{
    public const int Step = 32;
    public const int SamplesPerTick = 256;
    public const int UndergroundDepth = 40;

    private readonly WorldSession _session;
    private WorldStatusParams _params = new();
    private int _next = -1, _evil, _hallow, _total;

    public WorldStatus(WorldSession session) => _session = session;

    public WorldStatusReport? Latest { get; private set; }

    public bool Sampling => _next >= 0;

    private int Columns => Math.Max(1, _session.Generator.Plan.Size.X / Step);
    private int Rows => Math.Max(1, _session.Generator.Plan.Size.Z / Step);
    private int Samples => Columns * Rows * 2;

    /// <summary>The latest fresh report, starting a new sampling pass when there is none (null while sampling).</summary>
    public WorldStatusReport? Request(WorldStatusParams p)
    {
        if (Latest is { } r && _session.Clock.Tick - r.Tick < GameClock.TicksPerHour) return r;
        if (!Sampling)
        {
            _params = p;
            _next = 0;
            _evil = _hallow = _total = 0;
        }
        return null;
    }

    public void Tick()
    {
        if (!Sampling) return;
        int end = Math.Min(Samples, _next + SamplesPerTick);
        for (; _next < end; _next++) Sample(_next);
        if (_next < Samples) return;
        _next = -1;
        int evil = _total == 0 ? 0 : (int)MathF.Round(_evil * 100f / _total);
        int hallow = _total == 0 ? 0 : Math.Min(100 - evil, (int)MathF.Round(_hallow * 100f / _total));
        Latest = new WorldStatusReport(100 - evil - hallow, evil, hallow, _session.Clock.Tick);
    }

    /// <summary>Finishes the current pass at once (console and tests).</summary>
    public void CompleteNow()
    {
        while (Sampling) Tick();
    }

    private void Sample(int i)
    {
        int layer = i & 1, cell = i >> 1;
        int x = (cell % Columns) * Step + Step / 2, z = (cell / Columns) * Step + Step / 2;
        var gen = _session.Generator;
        float surface = gen.Terrain.SurfaceHeight(x, z);
        int y = (int)surface - (layer == 0 ? 2 : UndergroundDepth);
        if (y < 1) return;
        bool evil = false, hallow = false;
        Classify(gen.Biomes.IdOf(gen.Biomes.BiomeAt(x, y, z, surface)), ref evil, ref hallow);
        foreach (var zone in _session.ZonesAt(new Int3(x, y, z))) Classify(zone, ref evil, ref hallow);
        _total++;
        if (evil) _evil++;
        else if (hallow) _hallow++;
    }

    private void Classify(string id, ref bool evil, ref bool hallow)
    {
        if (Array.IndexOf(_params.EvilBiomes, id) >= 0) evil = true;
        if (Array.IndexOf(_params.HallowBiomes, id) >= 0) hallow = true;
    }

    /// <summary>The report as the service's text.</summary>
    public static string Describe(WorldStatusReport r, WorldStatusParams p, string world, string evilName) =>
        p.Text.Replace("{world}", world).Replace("{pure}", r.Pure.ToString()).Replace("{evil}", r.Evil.ToString())
            .Replace("{evilName}", evilName).Replace("{hallow}", r.Hallow.ToString());
}
