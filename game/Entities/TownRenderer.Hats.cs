using Godot;
using Strata.Core.Gameplay.Town;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Seasonal hats on town residents: a witch's hat at Halloween, a Santa hat at Christmas. One small holder per resident
/// on the head bone (made the first time a season runs), sharing two procedural hat meshes; a season change only flips
/// visibility. Residents tagged "seasonal" (Santa) keep their own look.
/// </summary>
public sealed partial class TownRenderer
{
    private static Mesh? _witchHat, _santaHat;
    private string _season = "";

    private void UpdateSeason()
    {
        string season = _game.Session.Seasons.Current;
        if (season == _season) return;
        _season = season;
        foreach (var view in _views.Values) ShowHat(view, view.Npc);
    }

    private void ShowHat(View view, TownNpc npc)
    {
        bool want = _season is "halloween" or "christmas" && !npc.Def.HasTag("seasonal");
        if (!want)
        {
            if (view.Hat != null) view.Hat.Visible = false;
            return;
        }
        if (view.Hat == null)
        {
            view.Hat = new MeshInstance3D { Name = "SeasonHat", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            if (view.Visual.AttachToBone("head", view.Hat)) view.Hat.Position = new Vector3(0, 0.19f, 0);
            else
            {
                view.Visual.AddChild(view.Hat);
                view.Hat.Position = new Vector3(0, view.Visual.Top - 0.06f, 0);
            }
        }
        view.Hat.Mesh = _season == "halloween" ? _witchHat ??= BuildWitchHat() : _santaHat ??= BuildSantaHat();
        view.Hat.Visible = true;
    }

    private static StandardMaterial3D Cloth(Color c) => new() { AlbedoColor = c, Roughness = 0.95f };

    /// <summary>A wide brim and a tall cone in dark purple with a violet band.</summary>
    private static Mesh BuildWitchHat()
    {
        var mesh = new ArrayMesh();
        AddCylinder(mesh, 0.2f, 0.2f, 0.016f, 0f, Cloth(new Color(0.22f, 0.15f, 0.3f)));
        AddCylinder(mesh, 0.105f, 0.0f, 0.3f, 0.016f, Cloth(new Color(0.22f, 0.15f, 0.3f)));
        AddCylinder(mesh, 0.107f, 0.098f, 0.035f, 0.016f, Cloth(new Color(0.55f, 0.22f, 0.8f)));
        return mesh;
    }

    /// <summary>A red cone with a white fur band and pompom.</summary>
    private static Mesh BuildSantaHat()
    {
        var mesh = new ArrayMesh();
        AddCylinder(mesh, 0.125f, 0.125f, 0.05f, 0f, Cloth(new Color(0.96f, 0.96f, 0.96f)));
        AddCylinder(mesh, 0.115f, 0.015f, 0.24f, 0.05f, Cloth(new Color(0.8f, 0.08f, 0.08f)));
        var pom = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 10, Rings = 6 };
        var arrays = pom.GetMeshArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for (int i = 0; i < verts.Length; i++) verts[i] += new Vector3(0, 0.3f, 0);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Cloth(new Color(0.96f, 0.96f, 0.96f)));
        return mesh;
    }

    /// <summary>Appends a (truncated) cone from <paramref name="y0"/> up by <paramref name="height"/> as one surface.</summary>
    private static void AddCylinder(ArrayMesh mesh, float bottom, float top, float height, float y0, Material material)
    {
        var cyl = new CylinderMesh { BottomRadius = bottom, TopRadius = top, Height = height, RadialSegments = 18, Rings = 1 };
        var arrays = cyl.GetMeshArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for (int i = 0; i < verts.Length; i++) verts[i] += new Vector3(0, y0 + height * 0.5f, 0);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }
}
