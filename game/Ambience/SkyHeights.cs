using Godot;
using Strata.Core.Common;
using Strata.Core.Session;

namespace Strata.Game.Ambience;

/// <summary>
/// The sky map around the camera as a small float texture of heights (metres): the top of the first cell in each
/// column that stops falling weather. Weather particles die below it and splashes are born on it, so rain stops at
/// roofs and cave ceilings without raycasts. Refreshed when the camera moves a few cells or twice a second (building
/// changes the sky map); one texture upload each time, no per-frame allocations.
/// </summary>
public sealed class SkyHeights
{
    /// <summary>Columns per side (cells): 96 cells = 48 m around the camera.</summary>
    public const int Size = 96;
    private const int Snap = 4;
    private const float RefreshSeconds = 0.5f;

    private readonly WorldSession _session;
    private readonly float[] _heights = new float[Size * Size];
    private readonly byte[] _bytes = new byte[Size * Size * 4];
    private readonly Image _image;
    private Int3 _origin = new(int.MinValue, 0, int.MinValue);
    private double _lastRefresh = -1;

    public SkyHeights(WorldSession session)
    {
        _session = session;
        _image = Image.CreateEmpty(Size, Size, false, Image.Format.Rf);
        Texture = ImageTexture.CreateFromImage(_image);
    }

    public ImageTexture Texture { get; }

    /// <summary>x, z of the texture's corner (m), 1 / its width (m), and the "no data" height.</summary>
    public Vector4 Rect { get; private set; }

    /// <summary>Bumped on every upload, so materials know to rebind.</summary>
    public int Version { get; private set; }

    public void Update(Vector3 eye)
    {
        int cx = Mathf.FloorToInt(eye.X / Units.CellSize), cz = Mathf.FloorToInt(eye.Z / Units.CellSize);
        var origin = new Int3(Snapped(cx) - Size / 2, 0, Snapped(cz) - Size / 2);
        double now = Time.GetTicksMsec() / 1000.0;
        if (origin == _origin && now - _lastRefresh < RefreshSeconds) return;
        _origin = origin;
        _lastRefresh = now;
        var sky = _session.Sky;
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                _heights[z * Size + x] = sky.SkyTop(origin.X + x, origin.Z + z) * Units.CellSize;
        System.Buffer.BlockCopy(_heights, 0, _bytes, 0, _bytes.Length);
        _image.SetData(Size, Size, false, Image.Format.Rf, _bytes);
        Texture.Update(_image);
        Rect = new Vector4(origin.X * Units.CellSize, origin.Z * Units.CellSize, 1f / (Size * Units.CellSize), -1e6f);
        Version++;
    }

    private static int Snapped(int v) => v - (v & (Snap - 1));
}
