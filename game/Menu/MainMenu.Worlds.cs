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
        foreach (var w in worlds) list.AddChild(WorldRow(w));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", ShowCharacters, 26));
        buttons.AddChild(MenuKit.Big("New", ShowWorldCreate, 26));
        box.AddChild(buttons);
        Show(MenuKit.Centered(panel));
    }

    private Control WorldRow(WorldEntry w)
    {
        var (panel, box) = MenuKit.Panel(new Vector2(0, 0), 2);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        box.AddChild(row);
        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddChild(MenuKit.Text(w.Name, 24, MenuKit.Idle));
        string evil = w.Evil == EvilKind.Corruption ? "Corruption" : "Crimson";
        string difficulty = w.Difficulty switch { Difficulty.Expert => "Expert", Difficulty.Master => "Master", _ => "Classic" };
        var detail = $"{w.Size} · {difficulty} · {evil}{(w.Hardmode ? " · Hardmode" : "")} · {SaveCatalog.PlayTime(w.PlayTicks)} played · seed {w.Seed}{(w.OldGenerator ? " · old terrain" : "")}";
        info.AddChild(MenuKit.Text(detail, 15, w.Difficulty == Difficulty.Master ? new Color(1f, 0.45f, 0.4f) : w.Difficulty == Difficulty.Expert ? new Color(1f, 0.75f, 0.3f) : new Color(0.8f, 0.85f, 0.9f)));
        row.AddChild(info);
        void Play() => PlayRequested?.Invoke(new LaunchRequest(w.Directory, _character!.Name));
        row.AddChild(MenuKit.Small("Play", () =>
        {
            if (w.OldGenerator) Confirm(OldGeneratorWarning, Play);
            else Play();
        }, 100));
        row.AddChild(MenuKit.Small("Delete", () => Confirm($"Delete the world {w.Name}? This cannot be undone.", () =>
        {
            SaveCatalog.DeleteWorld(w.Directory);
            ShowWorlds();
        }), 100));
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
        box.AddChild(MenuKit.Row("Seed", MenuKit.Field("", "Random", t => seedText = t.Trim(), 32), "Leave empty for a random world. Numbers or any text."));
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
            }), 26));
        box.AddChild(buttons);
        Show(MenuKit.Centered(panel));
    }

    /// <summary>Generates the world plan and saves the new world (off the main thread), then returns to the list.</summary>
    private void CreateWorld(string name, long seed, WorldSizePreset size, Difficulty difficulty, EvilKind evil)
    {
        var done = Busy("Generating world...");
        var settings = new WorldSettings { Name = name, Seed = seed, Size = size, Difficulty = difficulty, Evil = evil };
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

    /// <summary>A number seed as typed, or a stable hash of any other text; random when empty.</summary>
    private static long ParseSeed(string text)
    {
        if (text.Length == 0) return (long)(GD.Randi() & 0x7fffffff);
        if (long.TryParse(text, out long n)) return n;
        return (long)(Strata.Core.Determinism.Hash.String(text) & 0x7fffffff);
    }

    private static string RandomWorldName()
    {
        string[] a = { "Verdant", "Ancient", "Hidden", "Shattered", "Endless", "Golden", "Whispering", "Crimson", "Frozen", "Sunken" };
        string[] b = { "Vale", "Frontier", "Lands", "Isles", "Hollow", "Reach", "Expanse", "Wilds", "Kingdom", "Realm" };
        return $"{a[GD.Randi() % a.Length]} {b[GD.Randi() % b.Length]}";
    }
}
