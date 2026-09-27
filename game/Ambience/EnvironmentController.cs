using Godot;
using Strata.Core.Presentation;
using Strata.Game.Rendering;

namespace Strata.Game.Ambience;

/// <summary>
/// The sky, sun, moon, clouds, fog and post-processing of the world. Ambient light and reflections come from the
/// physically based sky (radiance cubemap); every lit surface gates them with its baked voxel sky exposure, so
/// caves and sealed rooms stay dark while open land gets real sky light. Quality follows the graphics preset.
/// </summary>
public sealed partial class EnvironmentController : Node3D
{
    /// <summary>Sun radiance scale of the atmosphere (sky shader units).</summary>
    private const float SkyIntensity = 33f;
    private const float SunEnergy = 3.1f;
    private const float MoonEnergy = 0.45f; // a bright moonlit night reads like the reference game's: dark blue, not black
    private const float CloudAltitude = 1600f;

    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private Godot.Environment _env = null!;
    private Sky _skyResource = null!;
    private ShaderMaterial _sky = null!;
    private CameraAttributesPractical _camera = null!;
    private GraphicsPreset _preset = null!;
    private int _seed;

    public float Daylight { get; private set; } = 1f;

    /// <summary>Radius of the streamed world in metres: distance fog reaches the sky colour there.</summary>
    public float ViewDistance { get; set; } = 96f;

    /// <summary>Forces the cloud cover (0 clear … 1 overcast) instead of the drifting weather; null restores it.</summary>
    public float? CloudCoverOverride { get; set; }

    /// <summary>Tint of the active world event (Blood Moon…), or null.</summary>
    public Color? EventTint { get; set; }

    /// <summary>The look of the biome around the camera (fog colour and density, sky and ambient tint), from data.</summary>
    public Strata.Core.Content.Defs.BiomeDef? Biome { get; set; }

    public void Initialize(GraphicsSettings graphics, long seed)
    {
        _seed = (int)(seed & 0x7fffffff);
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        _skyResource = new Sky { SkyMaterial = _sky, ProcessMode = Sky.ProcessModeEnum.Incremental };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = _skyResource,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.0f,
            // Screen blending lets bright emitters (flames, lava, glowing ores, magic) bloom into a warm halo without
            // hazing the whole image; only HDR light above the threshold glows.
            GlowIntensity = 0.7f,
            GlowStrength = 1.0f,
            GlowBloom = 0.0f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogDepthCurve = 1.5f,
            FogSkyAffect = 0f,
            FogSunScatter = 0.08f,
            SsaoRadius = 1.2f,
            SsaoIntensity = 2.2f,
            SsaoPower = 1.6f,
            SsaoDetail = 0.6f,
            SsaoLightAffect = 0.15f,
            SsilRadius = 6f,
            SsilIntensity = 1.2f,
            SsilSharpness = 0.9f,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2.0f,
            SsrDepthTolerance = 0.3f,
            VolumetricFogAlbedo = new Color(0.92f, 0.94f, 0.97f),
            VolumetricFogAnisotropy = 0.6f,
            VolumetricFogDetailSpread = 2f,
            VolumetricFogTemporalReprojectionEnabled = true,
            VolumetricFogTemporalReprojectionAmount = 0.9f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.22f,
            AdjustmentContrast = 1.06f,
        };
        for (int level = 0; level < 7; level++) _env.SetGlowLevel(level, level is >= 2 and <= 5 ? 1f : level == 6 ? 0.5f : 0f);
        _camera = new CameraAttributesPractical
        {
            // Gentle, slow adaptation: turning between sky and ground must not pump the brightness.
            AutoExposureScale = 0.35f,
            AutoExposureSpeed = 0.3f,
            AutoExposureMinSensitivity = 85f,
            AutoExposureMaxSensitivity = 170f,
        };
        AddChild(new WorldEnvironment { Environment = _env, CameraAttributes = _camera });

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowBlendSplits = true,
            ShadowNormalBias = 1.2f,
            ShadowBias = 0.04f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        _moon = new DirectionalLight3D
        {
            Name = "Moon",
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            ShadowNormalBias = 1.2f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        // Sealed chunks (caves with no opening to the sky) live on their own layer: the sun and moon cannot reach them,
        // so they never draw into the big sun and moon shadow maps; torches still see them.
        _sun.ShadowCasterMask = ~World.ChunkLayers.Sealed;
        _moon.ShadowCasterMask = ~World.ChunkLayers.Sealed;
        AddChild(_sun);
        AddChild(_moon);

        RenderingServer.GlobalShaderParameterSet("strata_cloud_noise", SkyNoise.Build(_seed));
        graphics.Changed += ApplyPreset;
        ApplyPreset(graphics.Preset);
    }

    private void ApplyPreset(GraphicsPreset p)
    {
        _preset = p;
        _env.SsaoEnabled = p.Ssao;
        _env.SsilEnabled = p.Ssil;
        _env.SsrEnabled = p.Ssr;
        _env.SsrMaxSteps = p.SsrSteps;
        _env.VolumetricFogEnabled = p.VolumetricFog;
        _env.GlowEnabled = p.Glow;
        _skyResource.RadianceSize = (Sky.RadianceSizeEnum)Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(p.SkyRadiance) / Mathf.Log(2f)) - 5, 0, 6);
        _sky.SetShaderParameter("cloud_steps", p.CloudSteps);
        _camera.AutoExposureEnabled = p.AutoExposure;
        _sun.DirectionalShadowMaxDistance = p.ShadowDistance;
        _sun.DirectionalShadowMode = p.ShadowSplits switch
        {
            1 => DirectionalLight3D.ShadowMode.Orthogonal,
            2 => DirectionalLight3D.ShadowMode.Parallel2Splits,
            _ => DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        _sun.LightAngularDistance = p.SunSize;
        _moon.DirectionalShadowMaxDistance = Mathf.Min(p.ShadowDistance, 80f);
        _moon.LightAngularDistance = p.SunSize;
    }
}
