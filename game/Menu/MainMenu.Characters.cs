using System;
using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Core.Diagnostics;
using Strata.Core.Persistence;
using Strata.Core.Session;

namespace Strata.Game.Menu;

/// <summary>
/// The character creator: a turntable preview beside a form of labelled rows (name, body, hair and clothing styles, colours, difficulty) with a Randomize button.
/// Each row is one <see cref="MenuKit.Row"/> added to the form, so more options slot in as further rows; every change
/// calls Refresh to redraw the preview. The character list is MainMenu.CharacterList.cs.
/// </summary>
public sealed partial class MainMenu
{
    private static readonly string[] HairColors = { "#1a1410", "#3b2616", "#5a3a20", "#8a5a30", "#c89050", "#e8d090", "#a0302a", "#d8d8d8", "#6040a0", "#3050c0" };
    private static readonly string[] SkinColors = { "#f6d8c0", "#e8b898", "#e0b090", "#c08860", "#a06840", "#7a4a2a", "#5a3420", "#80a070" };
    private static readonly string[] ClothColors = { "#4a7ac0", "#c04040", "#40a050", "#e0c040", "#8050b0", "#303038", "#e8e8e8", "#8a5a30", "#3a4a6a", "#e07830" };
    private static readonly string[] DifficultyNotes =
    {
        "Classic: drops half your coins on death.",
        "Mediumcore: drops all items on death.",
        "Hardcore: death is permanent — the character is lost.",
    };

    private void ShowCharacterCreate() => ShowCharacterCreate(new Appearance(), "", CharacterDifficulty.Classic);

    /// <summary>Builds the creator around a look; Randomize rebuilds it around a random one so every row shows it.</summary>
    private void ShowCharacterCreate(Appearance look, string name, CharacterDifficulty difficulty)
    {
        var (panel, box) = MenuKit.Panel(new Vector2(1000, 0), 6);
        box.AddChild(MenuKit.Title("Create Player", 38));
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        box.AddChild(columns);
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 10);
        columns.AddChild(left);
        var preview = new CharacterPreview();
        preview.Initialize(new Vector2(280, 420));
        left.AddChild(preview);
        var randomize = MenuKit.Big("Randomize", () => ShowCharacterCreate(Appearance.Random(new Random()), name, difficulty), 24);
        randomize.Name = "Randomize";
        left.AddChild(randomize);
        var form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 4);
        columns.AddChild(form);

        void Refresh() => preview.Show(look);
        void Colour(string label, string[] presets, string current, Action<string> set) =>
            form.AddChild(MenuKit.Row(label, MenuKit.Swatches(presets, current, h => { set(h); Refresh(); })));
        OptionButton Pick(string label, string nodeName, string[] options, string current, Action<string> set)
        {
            var choice = MenuKit.Choice(Array.ConvertAll(options, Pretty), Math.Max(0, Array.IndexOf(options, current)), i => { set(options[i]); Refresh(); });
            choice.Name = nodeName;
            form.AddChild(MenuKit.Row(label, choice));
            return choice;
        }

        var nameField = MenuKit.Field(name, "Enter a name", t => name = t.Trim());
        nameField.Name = "NameField";
        form.AddChild(MenuKit.Row("Name", nameField));
        Pick("Body", "Body", Appearance.Bodies, look.Body, v => look.Body = v);
        Pick("Hair Style", "HairStyle", Appearance.HairStyles, look.HairStyle, v => look.HairStyle = v);
        Pick("Clothing", "ClothingStyle", Appearance.ClothingStyles, look.ClothingStyle, v => look.ClothingStyle = v);
        Colour("Hair Color", HairColors, look.HairColor, h => look.HairColor = h);
        Colour("Eyes", Appearance.EyePalette, look.EyeColor, h => look.EyeColor = h);
        Colour("Skin", SkinColors, look.SkinColor, h => look.SkinColor = h);
        Colour("Shirt", ClothColors, look.ShirtColor, h => look.ShirtColor = h);
        Colour("Undershirt", ClothColors, look.UndershirtColor, h => look.UndershirtColor = h);
        Colour("Pants", ClothColors, look.PantsColor, h => look.PantsColor = h);
        Colour("Shoes", ClothColors, look.ShoeColor, h => look.ShoeColor = h);
        var note = MenuKit.Text(DifficultyNotes[(int)difficulty], 15, new Color(0.8f, 0.85f, 0.95f));
        form.AddChild(MenuKit.Row("Difficulty", MenuKit.Choice(new[] { "Classic", "Mediumcore", "Hardcore" }, (int)difficulty, i =>
        {
            difficulty = (CharacterDifficulty)i;
            note.Text = DifficultyNotes[i];
        })));
        form.AddChild(note);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", ShowCharacters, 26));
        var create = MenuKit.Big("Create", () =>
        {
            if (name.Length == 0) name = "Player";
            try
            {
                CreateCharacter(name, look, difficulty);
                ShowCharacters();
            }
            catch (Exception ex)
            {
                Error("Could not create the character.", ex);
            }
        }, 26);
        create.Name = "Create";
        buttons.AddChild(create);
        box.AddChild(buttons);
        Show(MenuKit.Centered(panel));
        Refresh();
    }

    private void CreateCharacter(string name, Appearance look, CharacterDifficulty difficulty)
    {
        // Names are unique per save file; add a number when taken.
        string unique = name;
        for (int i = 2; System.IO.File.Exists(GameWorld.PlayerPath(PlayersDirectory, unique)); i++) unique = $"{name} {i}";
        var state = PlayerState.CreateNew(unique, _services.Content);
        state.Look = look.Clone();
        state.Difficulty = difficulty;
        GameWorld.WritePlayer(PlayersDirectory, state);
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>"side_part" → "Side part".</summary>
    private static string Pretty(string s) => Capital(s.Replace('_', ' '));
}
