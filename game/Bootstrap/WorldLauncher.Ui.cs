using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>HUD, inventory, world map and minimap, town dialog, the performance overlay, achievement popups, hints, the loading screen and photo mode.</summary>
    private static (UI.Hud Hud, UI.InventoryScreen Inventory, UI.WorldMapScreen WorldMap, UI.TownDialog TownDialog, UI.PhotoMode.PhotoModeController Photo) AddUi(
        WorldRuntime runtime, GameWorld game, PlayerController player, InteractionController interaction, TileObjectRenderer objectRenderer)
    {
        var ui = new UI.UiContext(game, player, interaction);
        var hud = new UI.Hud();
        runtime.AddChild(hud);
        hud.Initialize(ui);
        var hurt = new UI.HurtOverlay();
        runtime.AddChild(hurt);
        hurt.Initialize(game, player.Entity, player.Rig);
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
        var perf = new UI.PerfOverlay();
        runtime.AddChild(perf);
        perf.Initialize(runtime, objectRenderer);
        var toast = new UI.AchievementToast();
        runtime.AddChild(toast);
        toast.Initialize(game, player.Entity);
        var hints = new UI.HintToast();
        runtime.AddChild(hints);
        hints.Initialize(game, player.Entity);
        var loading = new UI.LoadingTips();
        runtime.AddChild(loading);
        loading.Initialize(game, player);
        var photo = new UI.PhotoMode.PhotoModeController();
        runtime.AddChild(photo);
        photo.Initialize(runtime, player);
        return (hud, inventory, worldMap, townDialog, photo);
    }
}
