using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>Loads (or creates) the character, adds its body and moves it on knockback, respawn and recall.</summary>
    private static (PlayerEntity Entity, PlayerController Player) AddPlayer(WorldRuntime runtime, GameServices services, GameWorld game, LaunchOptions o, string playerDir)
    {
        var playerName = o.PlayerName;
        if (o.FreshPlayer) GameWorld.DeletePlayer(playerDir, playerName);
        var state = GameWorld.LoadOrCreatePlayer(playerDir, playerName, services.Content);
        var entity = game.AddPlayer(state, game.SpawnPositionFor(state));
        var player = new PlayerController();
        player.Initialize(runtime, entity, MovementConfig.Load(services.Data));
        runtime.AddChild(player);
        player.Rig.Camera.Current = true;
        runtime.Focus = player;
        game.Npcs.PlayerHurt += (p, _, knock) =>
        {
            if (p == entity) player.ApplyKnockback(new Vector3(knock.X, knock.Y, knock.Z));
        };
        game.PlayerRespawned += p =>
        {
            if (p == entity) player.Teleport(p.Position, resolveSpawn: true);
        };
        game.ItemUse.Recalled += (p, home) =>
        {
            if (p == entity) player.Teleport(home, resolveSpawn: true);
        };
        return (entity, player);
    }

    /// <summary>Third-person avatar, first-person view model, grapple chain, swing trail and carried light.</summary>
    private static void AddPlayerVisuals(WorldRuntime runtime, GameServices services, GameWorld game, Rendering.PropLibrary props, PlayerEntity entity, PlayerController player)
    {
        var avatar = new PlayerAvatar();
        player.AddChild(avatar);
        avatar.Initialize(player, game, props);
        var viewModel = new ViewModel();
        player.Rig.Camera.AddChild(viewModel);
        viewModel.Initialize(player, game, props);
        var grapple = new GrappleVisual();
        runtime.AddChild(grapple);
        grapple.Initialize(player);
        var trail = new Entities.SwingTrail();
        runtime.AddChild(trail);
        trail.Initialize(game);
        var carried = new PlayerLight();
        player.AddChild(carried);
        carried.Initialize(entity, services.Graphics, () => viewModel.LightAnchor, () => avatar.LightAnchor);
    }

    /// <summary>Targeting and use input, and the highlight on the targeted cell.</summary>
    private static InteractionController AddInteraction(WorldRuntime runtime, GameWorld game, PlayerEntity entity, PlayerController player)
    {
        var interaction = new InteractionController();
        interaction.Initialize(runtime, player);
        runtime.AddChild(interaction);
        var highlight = new TargetHighlight();
        runtime.AddChild(highlight);
        highlight.Initialize(game, interaction, entity);
        return interaction;
    }
}
