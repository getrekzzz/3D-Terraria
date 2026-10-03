using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>
    /// HUD, inventory, world map and minimap, town dialog, the performance overlay, the emote wheel and NPC voices, photo mode, the town panels, and above every screen
    /// the held stack at the pointer and the item tooltip that follows it. Under the inventory and the town dialog, the
    /// empty screen catches the clicks their panels do not take (throw, use, camera drag).
    /// </summary>
    private static (UI.Hud Hud, UI.InventoryScreen Inventory, UI.WorldMapScreen WorldMap, UI.TownDialog TownDialog, UI.PhotoMode.PhotoModeController Photo, UI.TooltipFollower Tooltip, UI.CursorItemLayer CursorItem, UI.TownPanels Town) AddUi(
        WorldRuntime runtime, GameWorld game, PlayerController player, InteractionController interaction, TileObjectRenderer objectRenderer)
    {
        var ui = new UI.UiContext(game, player, interaction);
        var hud = new UI.Hud();
        runtime.AddChild(hud);
        hud.Initialize(ui);
        var hurt = new UI.HurtOverlay();
        runtime.AddChild(hurt);
        hurt.Initialize(game, player.Entity, player.Rig);
        UI.WorldClickCatcher.Create(ui, runtime);
        var inventory = new UI.InventoryScreen();
        runtime.AddChild(inventory);
        inventory.Initialize(ui);
        inventory.ContainerShown += objectRenderer.SetLidOpen; // the chest's lid swings open while it is shown
        var worldMap = new UI.WorldMapScreen();
        runtime.AddChild(worldMap);
        worldMap.Initialize(ui);
        var minimap = new UI.Minimap();
        runtime.AddChild(minimap);
        minimap.Initialize(ui, worldMap);
        var townDialog = new UI.TownDialog();
        runtime.AddChild(townDialog);
        townDialog.Initialize(ui);
        var signs = new UI.SignDialog();
        runtime.AddChild(signs);
        signs.Initialize(ui);
        townDialog.ShopRequested += inventory.OpenShop; // the chat box's Shop opens the inventory with the grid shop
        townDialog.ServiceRequested += inventory.OpenService; // and Reforge with the reforge slot
        var town = UI.TownPanels.Create(runtime, ui);
        townDialog.PanelRequested += town.Open; // the Stylist's Haircut
        inventory.Equipment.HousingPressed += town.Housing.Toggle; // the equipment column's Housing tab
        town.Housing.SetAnchor(inventory.Equipment);
        var perf = new UI.PerfOverlay();
        runtime.AddChild(perf);
        perf.Initialize(runtime, objectRenderer);
        var toast = new UI.AchievementToast();
        runtime.AddChild(toast);
        toast.Initialize(game, player.Entity);
        var book = new UI.GuideBook.GuideBookScreen();
        runtime.AddChild(book);
        book.Initialize(ui, objectRenderer.Props);
        var hints = new UI.HintToast();
        runtime.AddChild(hints);
        hints.Initialize(game, player.Entity);
        // The open book covers the screen: hints wait, and the top-right minimap and info panel step out of the way.
        book.OpenChanged += open =>
        {
            hints.Suppressed = open;
            minimap.Visible = !open;
            hud.Info.Modulate = open ? Godot.Colors.Transparent : Godot.Colors.White;
        };
        var loading = new UI.LoadingTips();
        runtime.AddChild(loading);
        loading.Initialize(game, player);
        var emotes = new UI.EmoteWheel();
        runtime.AddChild(emotes);
        emotes.Initialize(ui);
        var voice = new Audio.NpcVoice();
        runtime.AddChild(voice);
        voice.Initialize(game, interaction);
        var photo = new UI.PhotoMode.PhotoModeController();
        runtime.AddChild(photo);
        photo.Initialize(runtime, player);
        hud.Connect(inventory, townDialog, minimap, perf); // the minimap and F3 overlay take their regions of the HUD layout
        var cursorItem = new UI.CursorItemLayer();
        cursorItem.Initialize(ui);
        runtime.AddChild(cursorItem);
        var tooltip = new UI.TooltipFollower();
        tooltip.Initialize(cursorItem);
        runtime.AddChild(tooltip);
        return (hud, inventory, worldMap, townDialog, photo, tooltip, cursorItem, town);
    }
}
