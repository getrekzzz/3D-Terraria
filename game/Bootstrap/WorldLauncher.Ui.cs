using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>HUD, inventory, world map and minimap, town dialog, the performance overlay and achievement popups.</summary>
    private static (UI.Hud Hud, UI.InventoryScreen Inventory, UI.WorldMapScreen WorldMap, UI.TownDialog TownDialog) AddUi(
        WorldRuntime runtime, GameWorld game, PlayerController player, InteractionController interaction, TileObjectRenderer objectRenderer)
    {
        var ui = new UI.UiContext(game, player, interaction);
        var hud = new UI.Hud();
        runtime.AddChild(hud);
        hud.Initialize(ui);
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
        return (hud, inventory, worldMap, townDialog);
    }
}
