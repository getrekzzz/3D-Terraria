using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Rendering;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The bestiary's live 3D creature: a transparent <see cref="SubViewport"/> with its own world, a key and a fill light
/// and the creature's model (the head's, for worms) in its tint, playing its idle clip, framed to its bounds, turning
/// slowly and dragged round with the mouse. The still portrait shows until the model has loaded through the shared
/// asset cache (and stays when there is no model). Built per shown entry; <see cref="Release"/> (or freeing it) drops
/// the viewport and the model.
/// </summary>
public sealed partial class CreaturePreview : Control
{
    private const float Side = 160f, Spin = 0.45f, DragTurn = 0.012f, DragHold = 2f, FovDegrees = 30f;
    private static readonly Color NeutralLight = new(0.3f, 0.3f, 0.3f, 1f);

    private readonly EnemyDef _def;
    private readonly PropLibrary? _props;
    private readonly TextureRect _portrait;
    private SubViewportContainer? _frame;
    private Node3D? _pivot;
    private CreatureVisual? _visual;
    private float _yaw = 0.6f, _hold;
    private bool _released;
    private readonly string _path, _resPath;

    public CreaturePreview(EnemyDef def, PropLibrary? props, Texture2D? portrait)
    {
        _def = def;
        _props = props;
        CustomMinimumSize = new Vector2(Side, Side);
        MouseFilter = MouseFilterEnum.Stop;
        _portrait = new TextureRect
        {
            Texture = portrait, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _portrait.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_portrait);
        _path = $"assets/models/creatures/{def.ModelNames()[0]}.glb";
        _resPath = "res://" + _path;
        _released = props == null || !ResourceLoader.Exists(_resPath);
    }

    /// <summary>True once the 3D model shows in place of the portrait (probe ui.book_preview).</summary>
    public bool Active => _visual != null;

    public override void _Process(double delta)
    {
        if (_released) return;
        if (_visual == null)
        {
            // Retried each frame while the model loads on a loader thread.
            var visual = CreatureVisual.Create(_path, _props!);
            if (visual == null)
            {
                if (AssetCache.TryGet<PackedScene>(_resPath, out _) == AssetState.Missing) _released = true;
                return;
            }
            Build(visual);
            return;
        }
        if (_hold > 0f) _hold -= (float)delta;
        else _yaw += Spin * (float)delta;
        _pivot!.Rotation = new Vector3(0f, _yaw, 0f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_visual == null || e is not InputEventMouseMotion m || (m.ButtonMask & MouseButtonMask.Left) == 0) return;
        _yaw += m.Relative.X * DragTurn;
        _hold = DragHold;
        AcceptEvent();
    }

    /// <summary>Frees the viewport and model and goes back to the portrait for good (the entry changed or the book closed).</summary>
    public void Release()
    {
        _released = true;
        _visual = null;
        _pivot = null;
        _frame?.QueueFree();
        _frame = null;
        _portrait.Visible = true;
    }

    private void Build(CreatureVisual visual)
    {
        var viewport = new SubViewport
        {
            OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, Size = new Vector2I((int)Side * 2, (int)Side * 2),
        };
        _frame = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        _frame.AddChild(viewport);
        _pivot = new Node3D { Name = "Pivot" };
        viewport.AddChild(_pivot);
        var holder = new Node3D { Name = "Holder", Scale = Vector3.One * _def.Scale };
        _pivot.AddChild(holder);
        holder.AddChild(visual);
        visual.SetTint(Color.FromHtml(_def.Tint));
        visual.SetLight(NeutralLight, immediate: true);
        if (visual.HasAnimation("idle")) visual.Play("idle", 0f);

        // Frame the model's bounds: centred on the pivot, the camera far enough back to fit its largest extent.
        var box = Bounds(visual, Transform3D.Identity, new Aabb(), first: true, out _);
        box = new Aabb(box.Position * _def.Scale, box.Size * _def.Scale);
        holder.Position = -box.GetCenter();
        float radius = Mathf.Max(0.25f, box.Size.Length() * 0.5f);
        float distance = radius / Mathf.Tan(Mathf.DegToRad(FovDegrees) * 0.5f) * 1.05f;
        var camera = new Camera3D { Fov = FovDegrees, Near = 0.05f, Far = distance + radius * 4f, Current = true };
        viewport.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0f, radius * 0.35f, distance), Vector3.Zero, Vector3.Up);
        var key = new DirectionalLight3D { LightEnergy = 1.3f, LightColor = new Color(1f, 0.97f, 0.9f) };
        viewport.AddChild(key);
        key.LookAtFromPosition(new Vector3(1.5f, 2.5f, 2f), Vector3.Zero, Vector3.Up);
        var fill = new DirectionalLight3D { LightEnergy = 0.55f, LightColor = new Color(0.75f, 0.82f, 1f) };
        viewport.AddChild(fill);
        fill.LookAtFromPosition(new Vector3(-2f, 0.5f, 1f), Vector3.Zero, Vector3.Up);

        AddChild(_frame);
        _visual = visual;
        _portrait.Visible = false;
        _pivot.Rotation = new Vector3(0f, _yaw, 0f);
    }

    private static Aabb Bounds(Node node, Transform3D xf, Aabb box, bool first, out bool empty)
    {
        empty = first;
        if (node is MeshInstance3D { Mesh: { } mesh })
        {
            var b = xf * mesh.GetAabb();
            box = empty ? b : box.Merge(b);
            empty = false;
        }
        foreach (var child in node.GetChildren())
        {
            box = Bounds(child, child is Node3D n ? xf * n.Transform : xf, box, empty, out bool stillEmpty);
            empty = stillEmpty;
        }
        return box;
    }
}
