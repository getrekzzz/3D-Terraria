using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Pets and light pets (Core ProjectileSystem.Pets.cs): they stay upright and turn to face where they go; creature
/// pets play walk, fly or idle by their speed; item and glow pets turn slowly as they hover. A light pet's glow is a
/// light from the shared <see cref="DynamicLights"/> pool, at most the graphics preset's <c>petLights</c> of them.
/// </summary>
public sealed partial class ProjectileRenderer
{
    private readonly Dictionary<long, string> _petAnims = new();

    /// <summary>Projectiles drawn with the glow fallback because their model was still loading.</summary>
    private readonly HashSet<long> _awaiting = new();
    private float _retryClock;
    private bool _retryNow;

    /// <summary>
    /// A few times a second, rebuilds a projectile drawn with the glow fallback once its model has loaded, so long-lived
    /// ones (pets, minions) do not stay a glow. Returns true with the new node (not yet added) when it was swapped.
    /// </summary>
    private bool RetryModel(Projectile p, ref Node3D? node)
    {
        if (!_retryNow) return false;
        _awaiting.Remove(p.Id);
        var fresh = Create(p);
        if (_awaiting.Contains(p.Id))
        {
            fresh.Free(); // still loading: keep the current glow
            return false;
        }
        node?.QueueFree();
        _petAnims.Remove(p.Id);
        node = fresh;
        return true;
    }

    /// <summary>Light pets offered to the light pool at its last refresh (probe <c>lights.pets</c>).</summary>
    public int PetLightsOffered { get; private set; }

    /// <summary>The node of a player's pet (or light pet), or null when none is out.</summary>
    public Node3D? PetNode(PlayerEntity owner, bool light)
    {
        var slot = light ? MiscSlot.LightPet : MiscSlot.Pet;
        return _game.Projectiles.PetIn(owner, slot) is { } pet && _views.TryGetValue(pet.Id, out var node) ? node : null;
    }

    private void AnimatePet(Projectile p, Node3D node, Vector3 vel)
    {
        float flat = new Vector2(vel.X, vel.Z).Length();
        if (flat > 0.3f) node.Rotation = new Vector3(0, Mathf.LerpAngle(node.Rotation.Y, Mathf.Atan2(vel.X, vel.Z), 0.2f), 0);
        if (node.GetChildCount() > 0 && node.GetChild(0) is CreatureVisual creature)
        {
            string anim = p.Def.Pet!.Flies ? (creature.HasAnimation("fly") ? "fly" : "idle") : flat > 0.5f || !p.Grounded ? "walk" : "idle";
            if (!_petAnims.TryGetValue(p.Id, out var playing) || playing != anim)
            {
                _petAnims[p.Id] = anim;
                creature.Play(anim);
            }
            return;
        }
        if (flat <= 0.3f) node.Rotation = new Vector3(0, node.Rotation.Y + 0.6f * (float)GetProcessDeltaTime(), 0);
    }

    /// <summary>A <see cref="DynamicLights"/> provider: the light pets nearest the camera, capped by the preset's petLights.</summary>
    public Func<Vector3, float, IEnumerable<DynamicLights.Source>> PetLights(GraphicsSettings graphics) =>
        (eye, radius) => PetLightSources(graphics, eye, radius);

    private IEnumerable<DynamicLights.Source> PetLightSources(GraphicsSettings graphics, Vector3 eye, float radius)
    {
        var torch = graphics.Lighting.Torch;
        int budget = graphics.Preset.PetLights;
        PetLightsOffered = 0;
        foreach (var p in _game.Projectiles.All)
        {
            if (budget <= 0) yield break;
            if (p.PetSlot != MiscSlot.LightPet || p.Def.Light is not { Length: 3 } l) continue;
            var pet = p;
            var at = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            if (at.DistanceTo(eye) > radius) continue;
            budget--;
            PetLightsOffered++;
            int max = Math.Max(1, Math.Max(l[0], Math.Max(l[1], l[2])));
            float peak = max / 15f;
            yield return new DynamicLights.Source(at, new Color(l[0] / (float)max, l[1] / (float)max, l[2] / (float)max), peak,
                DynamicLights.Kind.Steady, torch.PlacedRangeBase + peak * torch.PlacedRangePerPeak, torch.PlacedEnergy * peak,
                (p.Id * 0.618034f) % 1f, () => new Vector3(pet.Position.X, pet.Position.Y, pet.Position.Z), torch.PlacedAttenuation);
        }
    }
}
