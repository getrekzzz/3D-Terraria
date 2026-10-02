using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Debug;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.UI.PhotoMode;

/// <summary>
/// Photo mode (X-12): <c>photo_mode</c> (P) pauses the world simulation, swaps to a tethered <see cref="FreeCamera"/>
/// starting at the player's view, hides the world's 2D layers (HUD, minimap, screens) from outside and shows the lens
/// overlay (depth of field, exposure, colour filter). <c>photo_shot</c> (F12/Enter) saves the frame without the overlay
/// to user://screenshots/&lt;timestamp&gt;.png; P or Esc leaves and restores the gameplay camera and simulation.
/// Actions are polled (edge-detected) so scripted <c>press</c> drives it like a key.
/// </summary>
public sealed partial class PhotoModeController : Node
{
    private const string Gate = "photo";
    public const string ShotDirectory = "user://screenshots";

    private WorldRuntime _runtime = null!;
    private PlayerController _player = null!;
    private FreeCamera _camera = null!;
    private PhotoFilterLayer _filterLayer = null!;
    private PhotoOverlay _overlay = null!;
    private CameraAttributesPractical? _base;
    private CameraAttributesPractical _attributes = new();
    private readonly List<CanvasLayer> _hidden = new();
    private readonly Dictionary<string, bool> _held = new();
    private bool _wasSimulating, _wasFirstPerson, _capturing;

    public bool Active { get; private set; }

    /// <summary>Pictures requested (also counted when the headless renderer cannot capture).</summary>
    public int Shots { get; private set; }

    public int Saved { get; private set; }
    public string LastPath { get; private set; } = "";
    public bool HudHidden => Active && _hidden.Count > 0 && _hidden.TrueForAll(l => !l.Visible);
    public float Focus { get; private set; } = 8f;
    public float Blur { get; private set; }
    public float Stops { get; private set; }
    public int Filter { get; private set; }
    public bool DofEnabled => PhotoLens.Dof(Focus, Blur).Enabled;

    public void Initialize(WorldRuntime runtime, PlayerController player)
    {
        _runtime = runtime;
        _player = player;
        Name = "PhotoMode";
        _camera = new FreeCamera { Name = "PhotoCamera", Speed = 6f };
        AddChild(_camera);
        _filterLayer = new PhotoFilterLayer { Visible = false };
        AddChild(_filterLayer);
        _overlay = new PhotoOverlay { Visible = false };
        AddChild(_overlay);
        _overlay.Initialize(this);
    }

    public override void _Process(double delta)
    {
        if (InputGate.TextEntry) return;
        bool toggle = Edge("photo_mode"), leave = Edge("pause"), shot = Edge("photo_shot");
        if (!Active)
        {
            if (toggle && !InputGate.GameplayBlocked) SetActive(true);
            return;
        }
        if (toggle || leave)
        {
            SetActive(false);
            return;
        }
        if (shot) TakeShot();
        // Hold the right mouse button to look around; release it to work the sliders.
        var want = Input.IsActionPressed("use_secondary") ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != want) Input.MouseMode = want;
    }

    private bool Edge(string action)
    {
        bool now = InputMap.HasAction(action) && Input.IsActionPressed(action);
        bool was = _held.GetValueOrDefault(action);
        _held[action] = now;
        return now && !was;
    }

    public void SetActive(bool on)
    {
        if (on == Active) return;
        Active = on;
        var rig = _player.Rig;
        if (on)
        {
            _wasSimulating = _runtime.Simulating;
            _runtime.Simulating = false;
            InputGate.SetOpen(Gate, true);
            var view = rig.Camera.GlobalTransform;
            _wasFirstPerson = rig.FirstPerson;
            rig.FirstPerson = false; // the body shows in the picture; no floating first-person hand
            _camera.Fov = rig.Camera.Fov;
            _camera.LookFrom(view.Origin, view.Origin - view.Basis.Z);
            _camera.Tether = _player.GlobalPosition;
            _base = GetViewport().World3D?.CameraAttributes as CameraAttributesPractical;
            _attributes = _base?.Duplicate() as CameraAttributesPractical ?? new CameraAttributesPractical();
            _camera.Attributes = _attributes;
            _camera.Current = true;
            HideWorldLayers();
            ApplyLens();
            _filterLayer.Visible = true;
            _overlay.Visible = true;
            _overlay.Sync();
        }
        else
        {
            _overlay.Visible = false;
            _filterLayer.Visible = false;
            foreach (var layer in _hidden)
                if (IsInstanceValid(layer)) layer.Visible = true;
            _hidden.Clear();
            rig.FirstPerson = _wasFirstPerson;
            rig.Camera.MakeCurrent();
            _runtime.Simulating = _wasSimulating;
            InputGate.SetOpen(Gate, false);
        }
    }

    /// <summary>Hides every visible 2D layer the world owns (HUD, minimap, closed screens) and remembers them.</summary>
    private void HideWorldLayers()
    {
        _hidden.Clear();
        foreach (var child in _runtime.GetChildren())
            if (child is CanvasLayer { Visible: true } layer)
            {
                layer.Visible = false;
                _hidden.Add(layer);
            }
    }

    public void SetLens(float? focus = null, float? blur = null, float? stops = null, int? filter = null)
    {
        Focus = Mathf.Clamp(focus ?? Focus, PhotoLens.MinFocus, PhotoLens.MaxFocus);
        Blur = Mathf.Clamp(blur ?? Blur, 0f, 1f);
        Stops = Mathf.Clamp(stops ?? Stops, -PhotoLens.MaxStops, PhotoLens.MaxStops);
        Filter = Mathf.Clamp(filter ?? Filter, 0, PhotoLens.Filters.Length - 1);
        if (!Active) return;
        ApplyLens();
        _overlay.Sync();
    }

    private void ApplyLens()
    {
        var a = _attributes;
        var dof = PhotoLens.Dof(Focus, Blur);
        a.DofBlurFarEnabled = dof.Enabled;
        a.DofBlurNearEnabled = dof.Enabled;
        a.DofBlurNearDistance = dof.NearDistance;
        a.DofBlurNearTransition = dof.NearTransition;
        a.DofBlurFarDistance = dof.FarDistance;
        a.DofBlurFarTransition = dof.FarTransition;
        a.DofBlurAmount = dof.Amount;
        // Scale the metering with the multiplier (as EnvironmentController.WriteExposure does) so auto exposure keeps it.
        float m = PhotoLens.ExposureMultiplier(Stops);
        float baseMultiplier = _base?.ExposureMultiplier ?? 1f;
        a.ExposureMultiplier = baseMultiplier * m;
        if (_base != null)
        {
            a.AutoExposureScale = _base.AutoExposureScale * m;
            a.AutoExposureMinSensitivity = _base.AutoExposureMinSensitivity * m;
            a.AutoExposureMaxSensitivity = _base.AutoExposureMaxSensitivity * m;
        }
        _filterLayer.Mode = Filter;
    }

    /// <summary>Saves the next frame without the overlay; without a renderer (headless) it only counts the request.</summary>
    public async void TakeShot()
    {
        Shots++;
        if (!Active || _capturing) return;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("PHOTO skipped (headless renderer)");
            return;
        }
        _capturing = true;
        _overlay.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture()?.GetImage();
        if (image != null)
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ShotDirectory));
            var path = ProjectSettings.GlobalizePath($"{ShotDirectory}/{PhotoLens.ScreenshotName(System.DateTime.Now)}");
            var err = image.SavePng(path);
            if (err == Error.Ok)
            {
                Saved++;
                LastPath = path;
            }
            GD.Print(err == Error.Ok ? $"PHOTO {path}" : $"PHOTO failed: {err}");
        }
        _capturing = false;
        if (Active) _overlay.Visible = true;
    }
}
