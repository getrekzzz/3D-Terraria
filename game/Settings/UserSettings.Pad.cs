namespace Strata.Game.Settings;

/// <summary>Controller preferences (Settings → Controls → Controller); the button bindings are in <see cref="KeyBindings"/>.</summary>
public sealed partial class UserSettings
{
    /// <summary>Right-stick look speed (1: the shipped speed of data/config/input.json, "pad.lookSpeed").</summary>
    public float PadLookSensitivity { get; set; } = 1f;

    /// <summary>Pushing the look stick up looks down.</summary>
    public bool PadInvertY { get; set; }

    /// <summary>Stick travel ignored around the centre (0–0.5), so a worn stick does not drift the view or the player.</summary>
    public float PadDeadzone { get; set; } = 0.15f;

    /// <summary>
    /// With a controller the crosshair slows over enemies and is drawn gently toward them while you aim or move
    /// (<see cref="Strata.Core.Gameplay.Interaction.AimAssist"/>). The mouse never gets it.
    /// </summary>
    public bool AimAssist { get; set; } = true;
}
