using Godot;

namespace Strata.Game.Ambience;

public sealed partial class WeatherRenderer
{
    private static readonly StringName WindParam = "wind", FallParam = "fall_speed", HeightsParam = "sky_heights", RectParam = "sky_rect";

    /// <summary>
    /// One GPU particle system (rain, snow, sand or splashes): its amount is the preset budget, and only the share the
    /// weather's strength asks for is alive (<c>amount_ratio</c>, which never restarts the system). It stops emitting
    /// entirely when nothing of its kind falls.
    /// </summary>
    private sealed class System
    {
        private readonly GpuParticles3D _node;
        private readonly ShaderMaterial _process;
        private int _budget;
        private float _ratio;

        public System(Node3D parent, int kind, Vector2 size, Color tint, float opacity, float lifetime, Vector3 box)
        {
            _process = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/weather_particles.gdshader") };
            _process.SetShaderParameter("kind", kind);
            _process.SetShaderParameter("box_radius", box.X);
            _process.SetShaderParameter("box_top", box.Y);
            _process.SetShaderParameter("box_height", box.Z);
            var draw = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/weather_drop.gdshader") };
            draw.SetShaderParameter("kind", kind);
            draw.SetShaderParameter("size", size);
            draw.SetShaderParameter("tint", tint);
            draw.SetShaderParameter("opacity", opacity);
            var quad = new QuadMesh { Size = new Vector2(2f, 2f), Material = draw };
            _node = new GpuParticles3D
            {
                Name = kind switch { 0 => "Rain", 1 => "Snow", 2 => "Sand", _ => "Splashes" },
                ProcessMaterial = _process,
                DrawPass1 = quad,
                Lifetime = lifetime,
                Randomness = 0.4f,
                Explosiveness = 0f,
                LocalCoords = false,
                Emitting = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // The box follows the camera, so it is always in view: skip culling work on it.
                VisibilityAabb = new Aabb(new Vector3(-40f, -40f, -40f), new Vector3(80f, 80f, 80f)),
                FixedFps = 0,
                Interpolate = false,
                DrawOrder = GpuParticles3D.DrawOrderEnum.Index,
            };
            parent.AddChild(_node);
        }

        /// <summary>Particles alive at most now.</summary>
        public int Alive => _node.Emitting ? Mathf.RoundToInt(_budget * _ratio) : 0;

        public void SetBudget(int budget)
        {
            _budget = Mathf.Max(1, budget);
            if (_node.Amount != _budget) _node.Amount = _budget;
        }

        /// <summary>Eases the live share towards <paramref name="strength"/> and passes the wind and fall speed on.</summary>
        public void Drive(float strength, Vector3 wind, float fallSpeed, SkyHeights sky, float dt)
        {
            _ratio = Mathf.MoveToward(_ratio, Mathf.Clamp(strength, 0f, 1f), dt * 0.5f);
            bool on = _ratio > 0.01f;
            if (_node.Emitting != on) _node.Emitting = on;
            if (!on) return;
            _node.AmountRatio = _ratio;
            _process.SetShaderParameter(WindParam, wind);
            _process.SetShaderParameter(FallParam, fallSpeed);
            if (sky.Version != _skyVersion)
            {
                _skyVersion = sky.Version;
                _process.SetShaderParameter(HeightsParam, sky.Texture);
                _process.SetShaderParameter(RectParam, sky.Rect);
            }
        }

        private int _skyVersion = -1;
    }
}
