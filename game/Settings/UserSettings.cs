using System;
using Godot;

namespace Strata.Game.Settings;

/// <summary>
/// Player preferences outside the graphics preset (user://settings.cfg): gameplay, camera, controls, audio,
/// interface and display options. Changes apply live through <see cref="Changed"/> and are saved with
/// <see cref="Save"/>. Key bindings are stored by <see cref="KeyBindings"/>.
/// </summary>
public sealed class UserSettings
{
    private const string FilePath = "user://settings.cfg";

    public static UserSettings Current { get; private set; } = new();

    // Gameplay
    /// <summary>Mine and place single 0.5 m cells instead of 1 m grid blocks (Alt swaps while held).</summary>
    public bool SmallBlocks { get; set; }

    /// <summary>Looking steeply down with a pickaxe digs the ground under your feet (a shaft you drop into).</summary>
    public bool SmartDig { get; set; } = true;

    public bool DamageNumbers { get; set; } = true;

    /// <summary>Pause the world while the inventory or a menu is open.</summary>
    public bool AutoPause { get; set; }

    /// <summary>Halloween and Christmas follow the system date (off: no seasonal events).</summary>
    public bool SeasonalEvents { get; set; } = true;

    // Camera and controls
    public float MouseSensitivity { get; set; } = 1f;
    public bool InvertY { get; set; }
    public float FieldOfView { get; set; } = 72f;
    public float CameraDistance { get; set; } = 4.2f;
    public bool FirstPerson { get; set; }
    public bool CameraShake { get; set; } = true;

    /// <summary>Red screen edges when hurt (strongest in first person).</summary>
    public bool HurtFlash { get; set; } = true;

    // Display
    public string WindowMode { get; set; } = "windowed";
    public bool VSync { get; set; } = true;
    public int MaxFps { get; set; }
    public float Brightness { get; set; } = 1f;
    public int ViewDistance { get; set; } = 5;

    // Audio (0..1)
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.6f;
    public float SoundVolume { get; set; } = 0.8f;
    public float AmbientVolume { get; set; } = 0.6f;

    /// <summary>Audio output device by name; "Default" follows the system's default device.</summary>
    public string AudioDevice { get; set; } = "Default";

    // Interface
    public float UiScale { get; set; } = 1f;
    public bool ShowFps { get; set; }
    public bool ShowMinimap { get; set; } = true;
    public bool ShowTargetName { get; set; } = true;
    public string CursorColor { get; set; } = "#ffffff";

    public event Action<UserSettings>? Changed;

    public static UserSettings Load()
    {
        var s = new UserSettings();
        var file = new ConfigFile();
        if (file.Load(FilePath) == Error.Ok)
        {
            foreach (var prop in typeof(UserSettings).GetProperties())
            {
                if (!prop.CanWrite || prop.Name == nameof(Current)) continue;
                var value = file.GetValue("settings", ToKey(prop.Name), default(Variant));
                if (value.VariantType == Variant.Type.Nil) continue;
                try
                {
                    object converted = prop.PropertyType == typeof(bool) ? value.AsBool()
                        : prop.PropertyType == typeof(int) ? value.AsInt32()
                        : prop.PropertyType == typeof(float) ? value.AsSingle()
                        : value.AsString();
                    prop.SetValue(s, converted);
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"settings: ignoring {prop.Name}: {ex.Message}");
                }
            }
        }
        Current = s;
        return s;
    }

    public void Save()
    {
        var file = new ConfigFile();
        foreach (var prop in typeof(UserSettings).GetProperties())
        {
            if (!prop.CanWrite || prop.Name == nameof(Current)) continue;
            var v = prop.GetValue(this);
            Variant value = v switch
            {
                bool b => b,
                int i => i,
                float f => f,
                string str => str,
                _ => default,
            };
            file.SetValue("settings", ToKey(prop.Name), value);
        }
        var error = file.Save(FilePath);
        if (error != Error.Ok) GD.PushError($"could not save {FilePath}: {error}");
    }

    /// <summary>Notifies listeners after one or more properties were changed.</summary>
    public void Apply() => Changed?.Invoke(this);

    private static string ToKey(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);
}
