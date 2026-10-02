using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Seasons;

/// <summary>Seasonal events by calendar date (data/config/seasons.json).</summary>
public sealed class SeasonConfig
{
    public const string Path = "data/config/seasons.json";

    public SeasonWindow[] Seasons { get; init; } =
    {
        new() { Id = "halloween", Name = "Halloween", From = new[] { 10, 10 }, To = new[] { 11, 1 } },
        new() { Id = "christmas", Name = "Christmas", From = new[] { 12, 15 }, To = new[] { 12, 31 } },
    };

    /// <summary>Real seconds between looks at the system date (the season cannot change faster than a day anyway).</summary>
    public float RecheckSeconds { get; init; } = 30f;

    public static SeasonConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<SeasonConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    /// <summary>The season whose window holds <paramref name="date"/> ("" for none). Windows are inclusive and may wrap
    /// past the new year.</summary>
    public string SeasonOn(DateTime date)
    {
        int md = date.Month * 100 + date.Day;
        foreach (var s in Seasons)
        {
            int from = s.From[0] * 100 + s.From[1], to = s.To[0] * 100 + s.To[1];
            if (from <= to ? md >= from && md <= to : md >= from || md <= to) return s.Id;
        }
        return "";
    }

    /// <summary>1-based index of a season (0 for none or unknown), for probes.</summary>
    public int IndexOf(string id)
    {
        for (int i = 0; i < Seasons.Length; i++)
            if (Seasons[i].Id == id) return i + 1;
        return 0;
    }
}

public sealed class SeasonWindow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>First and last day, [month, day], inclusive.</summary>
    public int[] From { get; init; } = { 1, 1 };
    public int[] To { get; init; } = { 1, 1 };
}

/// <summary>
/// The seasonal event running now (Halloween, Christmas or none): from the system date through an injectable clock
/// (tests pin it), unless the player switched seasons off in the settings; the <c>season</c> command can force one or
/// switch them off for the session. Conditions read it as <c>{ "type": "season", "is": "halloween" }</c>.
/// </summary>
public sealed class SeasonState
{
    private Func<DateTime> _clock = () => DateTime.Now;
    private string? _override;
    private string _current = "";
    private long _checkedMs;
    private bool _stale = true;

    private SeasonConfig _config = new();

    public SeasonConfig Config
    {
        get => _config;
        set
        {
            _config = value;
            Invalidate();
        }
    }

    /// <summary>Today's date (the system clock unless a test or tool injects one).</summary>
    public Func<DateTime> Clock
    {
        get => _clock;
        set
        {
            _clock = value;
            Invalidate();
        }
    }

    /// <summary>The player's "seasonal events" setting (read when the date is rechecked).</summary>
    public Func<bool> Enabled { get; set; } = () => true;

    /// <summary>Forced season: null follows the date, "" (or "off") none, otherwise that season id.</summary>
    public string? Override
    {
        get => _override;
        set
        {
            _override = value == "off" ? "" : value;
            Invalidate();
        }
    }

    /// <summary>The running season id, "" for none.</summary>
    public string Current
    {
        get
        {
            if (_override != null) return _override;
            long now = Environment.TickCount64;
            if (_stale || now - _checkedMs >= (long)(Config.RecheckSeconds * 1000f))
            {
                _stale = false;
                _checkedMs = now;
                _current = Enabled() ? Config.SeasonOn(_clock()) : "";
            }
            return _current;
        }
    }

    public bool Is(string id) => Current == id;

    /// <summary>Forgets the cached date check (the clock, setting or config changed).</summary>
    public void Invalidate() => _stale = true;
}
