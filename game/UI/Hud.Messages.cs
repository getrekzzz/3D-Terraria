using Godot;
using Strata.Core.Gameplay.Messages;

namespace Strata.Game.UI;

/// <summary>
/// Where the game's messages go, in Terraria's colours. World messages scroll up the <see cref="ChatLog"/>:
/// - boss messages ("has awoken!", "has been defeated!", phases and rages, "has left!") and invasions in purple;
/// - residents arriving or leaving in blue, and their deaths ("was slain...") and the player's own in red;
/// - other events (the Blood Moon, an eclipse) and world changes (hardmode, a meteorite) in green;
/// - a catch ("Caught Bass!") in the item's rarity colour.
/// Refusals and notices take the status line under the crosshair; pickups float over the player's head
/// (<see cref="PickupPopups"/>). The big centred banner is optional (the BigAnnouncements setting, off by default),
/// and only for "has awoken!".
/// </summary>
public sealed partial class Hud
{
    private const int BannerSize = 24;

    private Label _banner = null!;
    private Tween? _bannerTween;

    private void WireMessages(UiContext ui)
    {
        var npcs = ui.Game.Npcs;
        // Every creature announcement once, coloured by its kind (NpcSystem raises the awoken and defeated lines too).
        npcs.Messaged += (text, kind) =>
        {
            var color = Color.FromHtml(MessageColors.Hex(kind));
            Announce(text, color);
            if (kind == MessageKind.BossAwoken) Banner(text, color);
        };
        npcs.BossLeft += b => Announce($"{b.Def.Boss!.Title} has left!", UiTheme.BossPurple);
        var town = ui.Game.Town;
        town.Arrived += t => Announce($"{t.Title} has arrived!", UiTheme.NpcArrived);
        town.Departed += t => Announce($"{t.Title} has left!", UiTheme.NpcArrived);
        town.Died += t => Announce($"{t.Title} was slain...", UiTheme.NpcSlain);
        // Invasions (fought to the end, or with a completion flag) speak in the boss purple; other events in green.
        ui.Game.Events.Announced += (text, def) =>
            Announce(text, def.UntilDefeated || def.CompletionFlag != null ? UiTheme.BossPurple : UiTheme.EventGreen);
        ui.Game.Progression.Raised += f =>
        {
            if (f.Announce != null) Announce(f.Announce, UiTheme.EventGreen);
        };
        ui.Game.Objectives.Announced += (text, color) => Announce(text, Color.FromHtml(color));
        // "Wanderer was slain by a Zombie." in red, and under the death screen's title.
        ui.Game.PlayerSlain += (p, text) =>
        {
            Announce(text, UiTheme.Death);
            if (p == ui.Entity) _death.Cause = text;
        };
        ui.Game.ItemUse.Caught += (p, stack) =>
        {
            if (p == ui.Entity) Announce($"Caught {stack.Item!.DisplayName}!", UiTheme.Rarity(stack.Item.Rarity));
        };

        // Refusals and notices: one line under the crosshair.
        ui.Game.ItemUse.Refused += (p, message) =>
        {
            if (p == ui.Entity) _status.Show(message, UiTheme.Muted);
        };
        ui.Game.Interaction.Refused += (p, message) =>
        {
            if (p == ui.Entity) _status.Show(message, UiTheme.Muted);
        };
        ui.Notified += _status.Show;
        ui.Interaction.SmartCursorToggled += on => _status.Show(on ? "Smart Cursor On" : "Smart Cursor Off", on ? UiTheme.SmartCursor : UiTheme.Muted);
        ui.Game.Town.HomeLost += t => _status.Show($"{t.Title}'s home is no longer suitable.", UiTheme.Muted);
    }

    /// <summary>A world message: a line in the chat log in <paramref name="color"/>.</summary>
    public void Announce(string text, Color color) => _chat.Add(text, color);

    private void BuildBanner(Control root)
    {
        _banner = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _banner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _banner.GrowHorizontal = Control.GrowDirection.Both;
        _banner.OffsetTop = 120;
        _banner.AddThemeFontSizeOverride("font_size", BannerSize);
        _banner.AddThemeColorOverride("font_color", UiTheme.Text);
        _banner.AddThemeColorOverride("font_outline_color", UiFonts.OutlineColor);
        _banner.AddThemeConstantOverride("outline_size", UiFonts.Outline);
        root.AddChild(_banner);
    }

    /// <summary>The optional centred banner (BigAnnouncements): shows a few seconds, then fades; a newer one replaces it.</summary>
    private void Banner(string text, Color color)
    {
        if (!Settings.UserSettings.Current.BigAnnouncements) return;
        _banner.Text = text;
        _banner.SelfModulate = UiTheme.Vision(color);
        _banner.Modulate = Colors.White;
        _banner.Visible = true;
        _bannerTween?.Kill();
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenInterval(4.0);
        _bannerTween.TweenProperty(_banner, "modulate:a", 0.0, 1.5);
        _bannerTween.TweenCallback(Callable.From(() => _banner.Visible = false));
    }
}
