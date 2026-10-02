namespace Strata.Core.Presentation;

/// <summary>An axis-aligned screen rectangle in logical UI pixels (origin top-left, y down).</summary>
public readonly record struct UiRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool IsEmpty => W <= 0 || H <= 0;

    /// <summary>True when the two rectangles share area; rectangles that only touch along an edge do not intersect.</summary>
    public bool Intersects(UiRect o) => !IsEmpty && !o.IsEmpty && X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    /// <summary>True when this rectangle lies entirely within <paramref name="outer"/> (to a hundredth of a pixel).</summary>
    public bool Inside(UiRect outer) =>
        X >= outer.X - 0.01f && Y >= outer.Y - 0.01f && Right <= outer.Right + 0.01f && Bottom <= outer.Bottom + 0.01f;

    public override string ToString() => $"({X:0.#}, {Y:0.#}, {W:0.#}×{H:0.#})";
}

/// <summary>The panel shown under the inventory grid: a chest, an NPC shop or an NPC service (reforge, help).</summary>
public enum UiPanel { None, Chest, Shop, Service }

/// <summary>
/// What the interface is showing, which decides where everything goes. <see cref="BuffCount"/> is the number of buff
/// icons, <see cref="AccessoryRows"/> the accessory rows of the equipment column (5–7), <see cref="InfoLines"/> the
/// rows of the info panel and <see cref="DebugOverlay"/> whether the F3 performance overlay is open.
/// </summary>
public sealed record UiState(
    bool InventoryOpen = false,
    UiPanel Panel = UiPanel.None,
    bool BossBar = false,
    bool EventBar = false,
    bool NpcChat = false,
    int BuffCount = 0,
    int AccessoryRows = 5,
    bool ShowMinimap = true,
    int InfoLines = 0,
    bool DebugOverlay = false);

/// <summary>The named screen regions of <see cref="UiLayout"/>; each widget reads the rectangle of its own region.</summary>
public static class UiRegion
{
    public const string ItemName = "item_name";
    public const string Hotbar = "hotbar";
    public const string Buffs = "buffs";
    public const string LifeText = "life_text";
    public const string Hearts = "hearts";
    public const string Mana = "mana";
    public const string Minimap = "minimap";
    public const string Info = "info";
    public const string Crosshair = "crosshair";
    public const string Breath = "breath";
    public const string StatusLine = "status_line";
    public const string Chat = "chat";
    public const string BossBar = "boss_bar";
    public const string EventBar = "event_bar";
    public const string NpcChat = "npc_chat";
    public const string Debug = "debug";
    public const string Inventory = "inventory";
    public const string CoinAmmo = "coin_ammo";
    public const string Trash = "trash";
    public const string SortButtons = "sort_buttons";
    public const string Panel = "panel";
    public const string Crafting = "crafting";
    public const string Equipment = "equipment";

    public static readonly string[] All =
    {
        ItemName, Hotbar, Buffs, LifeText, Hearts, Mana, Minimap, Info, Crosshair, Breath, StatusLine, Chat, BossBar,
        EventBar, NpcChat, Debug, Inventory, CoinAmmo, Trash, SortButtons, Panel, Crafting, Equipment,
    };
}

/// <summary>
/// The rectangles of the regions shown in a <see cref="UiState"/>. A region that is not shown (the hotbar while the
/// inventory is open, a closed boss bar, the minimap when <see cref="MinimapHidden"/>) has no rectangle.
/// </summary>
public sealed class UiLayoutResult
{
    internal UiLayoutResult(float width, float height, Dictionary<string, UiRect> rects, float slotScale, bool minimapHidden)
    {
        Width = width;
        Height = height;
        Rects = rects;
        SlotScale = slotScale;
        MinimapHidden = minimapHidden;
    }

    public float Width { get; }
    public float Height { get; }
    public IReadOnlyDictionary<string, UiRect> Rects { get; }

    /// <summary>The size multiplier of the inventory-screen slots (1, or 0.85 / 0.75 when the screen is too small).</summary>
    public float SlotScale { get; }

    /// <summary>True when the inventory is open and the equipment column only fits with the minimap hidden.</summary>
    public bool MinimapHidden { get; }

    public bool Has(string region) => Rects.ContainsKey(region);

    public bool TryGet(string region, out UiRect rect) => Rects.TryGetValue(region, out rect);

    /// <summary>The region's rectangle, or an empty one when the region is not shown.</summary>
    public UiRect Get(string region) => Rects.TryGetValue(region, out var r) ? r : default;
}

/// <summary>
/// The interface layout as pure logic: named screen regions placed by Terraria-like rules (hotbar and buffs top-left,
/// hearts, mana and minimap top-right, chat bottom-left, boss bar bottom-centre, the inventory over the hotbar with
/// the crafting column under it and the equipment column on the right) so that no region ever covers another or leaves
/// the screen, at any logical viewport from <see cref="UiScale.MinLogicalWidth"/> × <see cref="UiScale.MinLogicalHeight"/>
/// up. Sizes are logical pixels at UI scale 1; the engine applies the UI scale through the viewport, so callers pass the
/// logical viewport size. Fixed regions keep their place; flexible ones (chat, info, debug overlay, crafting) shrink
/// around them and floating ones (breath, status line, bars) move to the nearest free spot. When the inventory's
/// regions do not fit, the slots shrink to 0.85 and then 0.75 of their size. The engine applies the result on viewport
/// resize and on state changes only, never per frame.
/// </summary>
public static partial class UiLayout
{
    public const float Margin = 20;
    public const float Gap = 4;
    public const float Slot = 52;
    public const float SmallSlot = 34;
    public const float PanelSlot = 42;
    public const float EquipSlot = 44;
    public const float TextLine = 20;
    public const float Caption = 18;
    public const float HotbarY = 24;
    public const float HotbarWidth = 10 * Slot + 9 * Gap;
    public const float BuffIcon = 32;
    public const float BuffCellHeight = 46; // icon plus timer
    public const float BuffPitchX = 38;
    public const float BuffPitchY = 50;
    public const int BuffsPerRow = 11;
    public const int MaxBuffs = 44;
    public const float HeartPitch = 26;
    public const float HeartsWidth = 10 * HeartPitch;
    public const float HeartsHeight = 2 * HeartPitch;
    public const float ManaWidth = 28;
    public const float ManaHeight = Caption + 10 * HeartPitch;
    public const float MinimapSize = 210;
    public const float InfoWidth = MinimapSize;
    public const float InfoMinWidth = 160;
    public const float InfoLine = 18;
    public const float CrosshairWidth = 240; // reticle, the interact key hint to its right and the target label below
    public const float CrosshairHeight = 56;
    public const float CrosshairTop = 16; // the reticle sits this far below the region's top
    public const float BreathWidth = 200;
    public const float BreathHeight = 22;
    public const float StatusWidth = 400;
    public const float StatusHeight = 24;
    public const float ChatWidth = 560;
    public const float ChatMinWidth = 260;
    public const float ChatLine = 20;
    public const int ChatLines = 10;
    public const int ChatMinLines = 3;
    public const float BossBarWidth = 500;
    public const float BossBarMinWidth = 400;
    public const float BossBarHeight = 64;
    public const float EventBarWidth = 300;
    public const float EventBarHeight = 80;
    public const float NpcChatWidth = 520;
    public const float NpcChatMinWidth = 400;
    public const float NpcChatHeight = 180;
    public const float NpcChatMinHeight = 140;
    public const float NpcChatClearance = 16;
    public const float DebugWidth = 520;
    public const float DebugHeight = 460; // about 25 lines of the F3 overlay
    public const float DebugMinWidth = 220;
    public const float DebugMinHeight = 80;
    public const float SortButton = 26;
    public const float PanelButtonsWidth = 120;
    public const float ServiceWidth = 260;
    public const float CraftDetailWidth = 228; // output slot, a row of six 32 px materials, 'Required Objects'
    public const float CraftDetailHeight = 72;
    public const float CraftSelectedScale = 1.25f;
    public const float DefenseBadge = 36;
    public const float EyeToggle = 18;

    /// <summary>The inventory slot scales tried in order until everything fits.</summary>
    public static readonly float[] SlotScales = { 1f, 0.85f, 0.75f };

    /// <summary>Lays out <paramref name="s"/> on a logical viewport of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static UiLayoutResult Compute(float width, float height, UiState s)
    {
        if (!s.InventoryOpen)
        {
            var hud = new UiLayoutPass(width, height);
            PlaceHud(hud, s);
            return hud.Result(1f, false);
        }

        UiLayoutPass? pass = null;
        foreach (float scale in SlotScales)
        {
            pass = new UiLayoutPass(width, height);
            if (PlaceInventory(pass, s, scale)) return pass.Result(scale, pass.MinimapHidden);
        }
        return pass!.Result(SlotScales[^1], pass.MinimapHidden); // the smallest scale, with whatever could be placed
    }

    /// <summary>Buff icon rows needed for <paramref name="count"/> buffs at <paramref name="perRow"/> per row.</summary>
    public static int BuffRows(int count, int perRow) =>
        count <= 0 ? 0 : (Math.Min(count, MaxBuffs) + Math.Max(1, perRow) - 1) / Math.Max(1, perRow);

    /// <summary>Width of a row of <paramref name="icons"/> buff icons.</summary>
    public static float BuffRowWidth(int icons) => icons <= 0 ? 0 : icons * BuffPitchX - (BuffPitchX - BuffIcon);

    /// <summary>Height of <paramref name="rows"/> rows of buff icons with their timers.</summary>
    public static float BuffRowsHeight(int rows) => rows <= 0 ? 0 : rows * BuffPitchY - (BuffPitchY - BuffCellHeight);

    /// <summary>Height of the equipment column: the tab row plus three armour rows and the accessory rows.</summary>
    public static float EquipmentHeight(int accessoryRows, float scale)
    {
        int rows = 3 + Math.Clamp(accessoryRows, 0, 7);
        return SortButton + Gap + rows * EquipSlot * scale + (rows - 1) * Gap;
    }

    /// <summary>Width of the equipment column: the defense badge, Dye | Vanity | Equip and the visibility toggles.</summary>
    public static float EquipmentWidth(float scale) => DefenseBadge + Gap + 3 * EquipSlot * scale + 2 * Gap + Gap + EyeToggle;
}
