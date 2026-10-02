using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>
    /// HUD, inventory, world map and minimap, town dialog, the performance overlay, and above every screen the held
    /// stack at the pointer and the item tooltip that follows it. Under the inventory and the town dialog, the empty
    /// screen catches the clicks their panels do not take (throw, use, camera drag).
    /// </summary>
    private static (UI.Hud Hud, UI.InventoryScreen Inventory, UI.WorldMapScreen WorldMap, UI.TownDialog TownDialog, UI.TooltipFollower Tooltip, UI.CursorItemLayer CursorItem) AddUi(
        WorldRuntime runtime, GameWorld game, PlayerController player, InteractionController interaction, TileObjectRenderer objectRenderer)
    {
        var ui = new UI.UiContext(game, player, interaction);
        var hud = new UI.Hud();
        runtime.AddChild(hud);
        hud.Initialize(ui);
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
        var perf = new UI.PerfOverlay();
        runtime.AddChild(perf);
        perf.Initialize(runtime, objectRenderer);
        hud.Connect(inventory, townDialog, minimap, perf); // the minimap and F3 overlay take their regions of the HUD layout
        var cursorItem = new UI.CursorItemLayer();
        cursorItem.Initialize(ui);
        runtime.AddChild(cursorItem);
        var tooltip = new UI.TooltipFollower();
        tooltip.Initialize(cursorItem);
        runtime.AddChild(tooltip);
        return (hud, inventory, worldMap, townDialog, tooltip, cursorItem);
    }
}
