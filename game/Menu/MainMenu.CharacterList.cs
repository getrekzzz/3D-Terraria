using Godot;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;

namespace Strata.Game.Menu;

/// <summary>
/// The character list (Select Player): one blue row per saved character, starred ones first, each with its turntable,
/// name, difficulty and stats, and the favourite, play and delete icons.
/// </summary>
public sealed partial class MainMenu
{
    private void ShowCharacters()
    {
        var (panel, box) = MenuKit.Panel(new Vector2(760, 520));
        box.AddChild(MenuKit.Title("Select Player", 40));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 360), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);
        box.AddChild(scroll);
        var characters = SaveCatalog.Characters(PlayersDirectory, (path, ex) => _services.Log.Error("menu", $"Unreadable character {path}", ex));
        if (characters.Count == 0) list.AddChild(MenuKit.Text("No characters yet — create one!", 18, MenuKit.Idle));
        foreach (var c in SaveFavorites.StarredFirst(characters, c => Favorites.IsCharacter(c.Path))) list.AddChild(CharacterRow(c));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", ShowTitle, 26));
        buttons.AddChild(MenuKit.Big("New", ShowCharacterCreate, 26));
        box.AddChild(buttons);
        Show(MenuKit.Centered(panel));
    }

    private Control CharacterRow(CharacterEntry c)
    {
        var (panel, row) = MenuKit.RowPanel();
        var preview = new CharacterPreview();
        preview.Initialize(new Vector2(70, 90));
        row.AddChild(preview);
        preview.Show(c.Look);
        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        info.AddChild(MenuKit.Text(c.Name, 24, MenuKit.Idle));
        var difficultyColor = c.Difficulty switch
        {
            CharacterDifficulty.Mediumcore => new Color(0.7f, 0.6f, 1f),
            CharacterDifficulty.Hardcore => new Color(1f, 0.4f, 0.35f),
            _ => new Color(0.8f, 0.85f, 0.9f),
        };
        info.AddChild(MenuKit.Text($"{c.Difficulty}   ·   {c.MaxLife} life   ·   {c.MaxMana} mana   ·   {SaveCatalog.PlayTime(c.PlayTicks)} played", 15, difficultyColor));
        row.AddChild(info);
        bool starred = Favorites.IsCharacter(c.Path);
        row.AddChild(MenuKit.Icon(starred ? "menu_favourite" : "menu_favourite_off", starred ? "Unfavorite" : "Favorite", () =>
        {
            Favorites.ToggleCharacter(c.Path);
            ShowCharacters();
        }));
        row.AddChild(MenuKit.Icon("menu_play", "Play", () =>
        {
            _character = c;
            ShowWorlds();
        }));
        row.AddChild(MenuKit.Icon("menu_delete", "Delete", () => Confirm($"Delete {c.Name}? This cannot be undone.", () =>
        {
            SaveCatalog.DeleteCharacter(c.Path);
            Favorites.ForgetCharacter(c.Path);
            ShowCharacters();
        })));
        return panel;
    }
}
