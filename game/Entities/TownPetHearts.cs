using Godot;
using Strata.Core.Gameplay.Town;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>
/// Petting a town pet (P5-20): a burst of small hearts rises from the pet and fades, as in Terraria, with a soft chirp.
/// The hearts are a fixed pool of billboard sprites sharing one generated texture (no allocations per burst).
/// </summary>
public sealed partial class TownPetHearts : Node3D
{
    private const int PoolSize = 18, PerBurst = 6;
    private const float Life = 1.1f;

    private readonly Sprite3D[] _hearts = new Sprite3D[PoolSize];
    private readonly Vector3[] _drift = new Vector3[PoolSize];
    private readonly float[] _age = new float[PoolSize];
    private int _next;

    public void Initialize(GameWorld game)
    {
        Name = "PetHearts";
        var texture = HeartTexture();
        for (int i = 0; i < PoolSize; i++)
        {
            _hearts[i] = new Sprite3D
            {
                Texture = texture,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                PixelSize = 0.006f,
                Shaded = false,
                Transparent = true,
                Visible = false,
                Modulate = new Color(1f, 0.45f, 0.6f),
            };
            AddChild(_hearts[i]);
            _age[i] = Life;
        }
        game.Town.Petted += (pet, _) => Burst(pet);
    }

    private void Burst(TownNpc pet)
    {
        var at = new Vector3(pet.Position.X, pet.Position.Y + 0.7f, pet.Position.Z);
        for (int k = 0; k < PerBurst; k++)
        {
            int i = _next;
            _next = (_next + 1) % PoolSize;
            float a = k * Mathf.Tau / PerBurst + (float)GD.RandRange(-0.3, 0.3);
            _drift[i] = new Vector3(Mathf.Cos(a) * 0.35f, 0.9f + (float)GD.RandRange(0, 0.4), Mathf.Sin(a) * 0.35f);
            _age[i] = -k * 0.05f;
            _hearts[i].Position = at;
            _hearts[i].Visible = false;
        }
        Audio.Sfx.PlayAt("pet", at);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < PoolSize; i++)
        {
            if (_age[i] >= Life) continue;
            _age[i] += dt;
            var h = _hearts[i];
            if (_age[i] < 0f) continue;
            if (_age[i] >= Life)
            {
                h.Visible = false;
                continue;
            }
            float t = _age[i] / Life;
            h.Visible = true;
            h.Position += _drift[i] * dt;
            h.Scale = Vector3.One * (0.6f + 0.6f * Mathf.Min(1f, t * 4f));
            h.Modulate = new Color(1f, 0.45f, 0.6f, 1f - t * t);
        }
    }

    /// <summary>A 32 px heart: the union of two circles and a triangle, with a soft edge.</summary>
    private static ImageTexture HeartTexture()
    {
        const int size = 32;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2.4f - 1.2f, v = 1.1f - (y + 0.5f) / size * 2.4f;
                // Implicit heart curve: (u² + v² − 1)³ − u²v³ ≤ 0.
                float q = u * u + v * v - 1f;
                float f = q * q * q - u * u * v * v * v;
                float alpha = Mathf.Clamp(-f * 18f, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        return ImageTexture.CreateFromImage(img);
    }
}
