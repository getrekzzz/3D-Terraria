using System;
using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Core.Diagnostics;
using Strata.Core.Persistence;
using Strata.Core.Session;

namespace Strata.Game.Menu;

/// <summary>
/// The character creator: a turntable preview beside a form of labelled rows (name, body, hair, colours, difficulty).
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

    private void ShowCharacterCreate()
    {
        var look = new Appearance();
        string name = "";
        var difficulty = CharacterDifficulty.Classic;
        var (panel, box) = MenuKit.Panel(new Vector2(980, 0), 8);
        box.AddChild(MenuKit.Title("Create Player", 40));
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        box.AddChild(columns);
        var preview = new CharacterPreview();
        preview.Initialize(new Vector2(280, 420));
        columns.AddChild(preview);
        var form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 8);
        columns.AddChild(form);

        void Refresh() => preview.Show(look);
        form.AddChild(MenuKit.Row("Name", MenuKit.Field("", "Enter a name", t => name = t.Trim())));
        OptionButton? styles = null;
        form.AddChild(MenuKit.Row("Body", MenuKit.Choice(new[] { "Male", "Female" }, 0, i =>
        {
            look.Body = Appearance.Bodies[i];
            look.HairStyle = Appearance.StylesFor(look.Body)[0];
            styles!.Clear();
            foreach (var s in Appearance.StylesFor(look.Body)) styles.AddItem(Capital(s));
            styles.Selected = 0;
            Refresh();
        })));
        styles = MenuKit.Choice(Array.ConvertAll(Appearance.MaleStyles, Capital), 0, i =>
        {
            look.HairStyle = Appearance.StylesFor(look.Body)[i];
            Refresh();
        });
        form.AddChild(MenuKit.Row("Hair Style", styles));
        form.AddChild(MenuKit.Row("Hair Color", MenuKit.Swatches(HairColors, look.HairColor, h => { look.HairColor = h; Refresh(); })));
        form.AddChild(MenuKit.Row("Skin", MenuKit.Swatches(SkinColors, look.SkinColor, h => { look.SkinColor = h; Refresh(); })));
        form.AddChild(MenuKit.Row("Shirt", MenuKit.Swatches(ClothColors, look.ShirtColor, h => { look.ShirtColor = h; Refresh(); })));
        form.AddChild(MenuKit.Row("Pants", MenuKit.Swatches(ClothColors, look.PantsColor, h => { look.PantsColor = h; Refresh(); })));
        form.AddChild(MenuKit.Row("Shoes", MenuKit.Swatches(ClothColors, look.ShoeColor, h => { look.ShoeColor = h; Refresh(); })));
        var note = MenuKit.Text(DifficultyNotes[0], 15, new Color(0.8f, 0.85f, 0.95f));
        form.AddChild(MenuKit.Row("Difficulty", MenuKit.Choice(new[] { "Classic", "Mediumcore", "Hardcore" }, 0, i =>
        {
            difficulty = (CharacterDifficulty)i;
            note.Text = DifficultyNotes[i];
        })));
        form.AddChild(note);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", ShowCharacters, 26));
        buttons.AddChild(MenuKit.Big("Create", () =>
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
        }, 26));
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
}
