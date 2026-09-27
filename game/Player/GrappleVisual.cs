using Godot;

namespace Strata.Game.Player;

/// <summary>Draws the grappling hook: a chain of links from the hand to the hook head, and the hook's claw.</summary>
public sealed partial class GrappleVisual : Node3D
{
    private PlayerController _player = null!;
    private readonly ImmediateMesh _mesh = new();
    private StandardMaterial3D _material = null!;

    public void Initialize(PlayerController player)
    {
        _player = player;
        Name = "GrappleVisual";
        TopLevel = true;
        _material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = 0.8f, Roughness = 0.35f };
        AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.grapple");
        _mesh.ClearSurfaces();
        if (_player.Hook == HookState.None) return;
        var hand = _player.HandPosition;
        var head = _player.HookHead;
        var along = head - hand;
        float length = along.Length();
        if (length < 0.05f) return;
        var dir = along / length;
        var side = dir.Cross(Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        var up = side.Cross(dir).Normalized();
        var color = _player.HookColor;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        // Chain links: alternating flat diamonds a few centimetres long.
        const float link = 0.09f;
        int links = Mathf.Min(400, (int)(length / link));
        for (int i = 0; i < links; i++)
        {
            var c = hand + dir * (i + 0.5f) * link;
            var w = (i % 2 == 0 ? side : up) * 0.022f;
            Diamond(c - dir * link * 0.5f, c + w, c + dir * link * 0.5f, c - w, color);
        }
        // The claw: three prongs around the head.
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.Tau / 3f;
            var spoke = (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.12f;
            Diamond(head - dir * 0.05f, head + spoke * 0.3f + dir * 0.05f, head + spoke - dir * 0.06f, head + spoke * 0.3f - dir * 0.02f, color.Darkened(0.2f));
        }
        _mesh.SurfaceEnd();
    }

    private void Diamond(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        foreach (var p in new[] { a, b, c, a, c, d })
        {
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(p);
        }
    }
}
