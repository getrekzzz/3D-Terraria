using System;
using System.Threading.Tasks;
using Godot;
using Strata.Core.Diagnostics;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Game.Menu;

public sealed partial class MainMenu
{
    private static readonly string[] Sizes = { "Small", "Medium", "Large" };
    private static readonly string[] Difficulties = { "Classic", "Expert", "Master" };
    private static readonly string[] Evils = { "Random", "Corruption", "Crimson" };
    private static readonly string[] DifficultyInfo =
    {
        "Classic: the standard experience.",
        "Expert: tougher enemies and bosses, better loot.",
        "Master: the ultimate challenge.",
    };

    private const string OldGeneratorWarning =
        "Made with an older world generator - new terrain will not match.\nPlaces you have not explored yet will not line up with the saved ones. Play anyway?";

    private void ShowWorlds()
    {
        if (_character == null)
        {
            ShowCharacters();
            return;
        }
        var (panel, box) = MenuKit.Panel(new Vector2(760, 520));
        box.AddChild(MenuKit.Title("Select World", 40));
        box.AddChild(MenuKit.Text($"Playing as {_character.Name}", 16, new Color(0.8f, 0.85f, 0.95f)));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 340), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);
        box.AddChild(scroll);
        var worlds = SaveCatalog.Worlds(WorldsDirectory, (dir, ex) => _services.Log.Error("menu", $"Unreadable world {dir}", ex));
        if (worlds.Count == 0) list.AddChild(MenuKit.Text("No worlds yet — create one!", 18, MenuKit.Idle));
        foreach (var w in SaveFavorites.StarredFirst(worlds, w => Favorites.IsWorld(w.Directory))) list.AddChild(WorldRow(w));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", ShowCharacters, 26));
        buttons.AddChild(MenuKit.Big("New", ShowWorldCreate, 26));
        box.AddChild(buttons);
        Show(MenuKit.Centered(panel));
    }

    /// <summary>A world's blue row: name, size, difficulty, evil, play time and seed, then favourite, play and delete.</summary>
    private Control WorldRow(WorldEntry w)
    {
        var (panel, row) = MenuKit.RowPanel();
        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        info.AddChild(MenuKit.Text(w.Name, 24, MenuKit.Idle));
        string evil = w.Evil == EvilKind.Corruption ? "Corruption" : "Crimson";
        string difficulty = w.Difficulty switch { Difficulty.Expert => "Expert", Difficulty.Master => "Master", _ => "Classic" };
        var detail = $"{w.Size} · {difficulty} · {evil}{(w.Hardmode ? " · Hardmode" : "")} · {SaveCatalog.PlayTime(w.PlayTicks)} played · seed {w.Seed}{(w.OldGenerator ? " · old terrain" : "")}";
        info.AddChild(MenuKit.Text(detail, 15, w.Difficulty == Difficulty.Master ? new Color(1f, 0.45f, 0.4f) : w.Difficulty == Difficulty.Expert ? new Color(1f, 0.75f, 0.3f) : new Color(0.8f, 0.85f, 0.9f)));
        row.AddChild(info);
        void Play() => PlayRequested?.Invoke(new LaunchRequest(w.Directory, _character!.Name));
        bool starred = Favorites.IsWorld(w.Directory);
        row.AddChild(MenuKit.Icon(starred ? "menu_favourite" : "menu_favourite_off", starred ? "Unfavorite" : "Favorite", () =>
        {
            Favorites.ToggleWorld(w.Directory);
            ShowWorlds();
        }));
        row.AddChild(MenuKit.Icon("menu_play", "Play", () =>
        {
            if (w.OldGenerator) Confirm(OldGeneratorWarning, Play);
            else Play();
        }));
        row.AddChild(MenuKit.Icon("menu_delete", "Delete", () => Confirm($"Delete the world {w.Name}? This cannot be undone.", () =>
        {
            SaveCatalog.DeleteWorld(w.Directory);
            Favorites.ForgetWorld(w.Directory);
            ShowWorlds();
        })));
        return panel;
    }

    private void ShowWorldCreate()
    {
        string name = "";
        string seedText = "";
        int size = 0, difficulty = 0, evil = 0;
        var (panel, box) = MenuKit.Panel(new Vector2(720, 0), 10);
        box.AddChild(MenuKit.Title("Create World", 40));
        box.AddChild(MenuKit.Row("World Name", MenuKit.Field("", "Enter a world name", t => name = t.Trim())));
        var found = Settings.UserSettings.Current.DiscoveredSeeds;
        box.AddChild(MenuKit.Row("Seed", MenuKit.Field("", "Random", t => seedText = t.Trim(), 32), "Leave empty for a random world. Numbers or any text."
            + (found.Length > 0 ? $"\nSecret seeds found: {found.Replace("|", ", ")}." : "")));
        box.AddChild(MenuKit.Row("Size", MenuKit.Choice(Sizes, 0, i => size = i),
            "Small is about 1 km across, Medium 2 km, Large 3 km (streamed as you explore)."));
        var note = MenuKit.Text(DifficultyInfo[0], 15, new Color(0.8f, 0.85f, 0.95f));
        box.AddChild(MenuKit.Row("Difficulty", MenuKit.Choice(Difficulties, 0, i =>
        {
            difficulty = i;
            note.Text = DifficultyInfo[i];
        })));
        box.AddChild(note);
        box.AddChild(MenuKit.Row("World Evil", MenuKit.Choice(Evils, 0, i => evil = i)));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", ShowWorlds, 26));
        buttons.AddChild(MenuKit.Big("Create", () => CreateWorld(name.Length == 0 ? RandomWorldName() : name, ParseSeed(seedText),
            (WorldSizePreset)(size + 1), (Difficulty)difficulty, evil switch
            {
                1 => EvilKind.Corruption,
                2 => EvilKind.Crimson,
                _ => GD.Randi() % 2 == 0 ? EvilKind.Corruption : EvilKind.Crimson,
            }, SecretSeeds.Parse(seedText)), 26));
        box.AddChild(buttons);
        Show(MenuKit.Centered(panel));
    }

    /// <summary>Generates the world plan and saves the new world (off the main thread), then returns to the list.</summary>
    private void CreateWorld(string name, long seed, WorldSizePreset size, Difficulty difficulty, EvilKind evil, SecretSeed secret = SecretSeed.None)
    {
        var done = Busy("Generating world...");
        var settings = new WorldSettings { Name = name, Seed = seed, Size = size, Difficulty = difficulty, Evil = evil, Secret = secret };
        RememberSecret(secret);
        var dir = SaveCatalog.NewWorldDirectory(WorldsDirectory, name);
        var services = _services;
        Task.Run(() => WorldSession.Create(services.Content, settings, dir, services.Render, services.Log)).ContinueWith(t =>
        {
            Callable.From(() =>
            {
                done();
                if (t.Exception != null) Error("Could not create the world.", t.Exception.GetBaseException());
                else
                {
                    services.Log.Info("menu", $"Created world '{name}' ({size}, {difficulty}, {evil}, seed {seed}).");
                    ShowWorlds();
                }
            }).CallDeferred();
        });
    }

    /// <summary>
    /// A number seed as typed, or a stable hash of any other text; random when empty or a secret seed name (secret
    /// worlds differ every time, as in Terraria).
    /// </summary>
    private static long ParseSeed(string text)
    {
        if (text.Length == 0 || SecretSeeds.Parse(text) != SecretSeed.None) return (long)(GD.Randi() & 0x7fffffff);
        if (long.TryParse(text, out long n)) return n;
        return (long)(Strata.Core.Determinism.Hash.String(text) & 0x7fffffff);
    }

    /// <summary>Secret seeds stay secret until found: once used, they are listed under the seed field.</summary>
    private static void RememberSecret(SecretSeed secret)
    {
        if (secret == SecretSeed.None) return;
        var s = Settings.UserSettings.Current;
        var found = new System.Collections.Generic.List<string>(s.DiscoveredSeeds.Split('|', StringSplitOptions.RemoveEmptyEntries));
        foreach (var title in SecretSeeds.Titles(secret))
            if (!found.Contains(title)) found.Add(title);
        s.DiscoveredSeeds = string.Join("|", found);
        s.Save();
    }

    private static string RandomWorldName()
    {
        string[] a = { "Verdant", "Ancient", "Hidden", "Shattered", "Endless", "Golden", "Whispering", "Crimson", "Frozen", "Sunken" };
        string[] b = { "Vale", "Frontier", "Lands", "Isles", "Hollow", "Reach", "Expanse", "Wilds", "Kingdom", "Realm" };
        return $"{a[GD.Randi() % a.Length]} {b[GD.Randi() % b.Length]}";
    }
}
