using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// Worn armour and body accessories on the avatar (P3-42): each equipped armour item shows its set's pieces for its
/// slot (tools/blender/bgen/armor.py, fitted to the player rig and scaled to this body's bone lengths); a helmet hides
/// the hair; shields ride the left forearm and balloons float behind the shoulders. Attachments are rebuilt only
/// when the equipment changes; per frame they only take the body's voxel light and a balloon's bob.
/// </summary>
public sealed partial class PlayerAvatar
{
    private readonly List<Node3D> _worn = new();
    private readonly List<GeometryInstance3D> _wornGeometry = new();
    private Node3D? _balloon;
    private bool _wornDirty = true;
    private float _bob;

    /// <summary>Armour pieces and worn accessories currently attached (probe <c>avatar.armor_pieces</c>).</summary>
    public int WornCount => _worn.Count;

    private void InitializeArmor()
    {
        var inv = _controller.Entity.State.Inventory;
        inv.Armor.Changed += _ => _wornDirty = true;
        inv.Accessories.Changed += _ => _wornDirty = true;
    }

    private void UpdateArmor(float delta)
    {
        if (_visual == null) return;
        var light = _visual.CurrentLight;
        if (_wornDirty)
        {
            _wornDirty = false;
            RebuildWorn();
        }
        foreach (var g in _wornGeometry) g.SetInstanceShaderParameter("voxel_light", light);
        if (_balloon != null)
        {
            _bob += delta;
            _balloon.Position = new Vector3(0f, 0.32f + Mathf.Sin(_bob * 1.7f) * 0.03f, -0.28f);
        }
    }

    private void RebuildWorn()
    {
        foreach (var node in _worn) node.GetParent()?.QueueFree(); // each sits in its own BoneAttachment3D
        _worn.Clear();
        _wornGeometry.Clear();
        _balloon = null;
        bool hideHair = false;
        var inv = _controller.Entity.State.Inventory;
        var assets = WornAssets.Instance;
        foreach (var stack in inv.Armor.Stacks)
        {
            var item = stack.Item!;
            string slot = item.EquipSlot.ToString().ToLowerInvariant();
            foreach (var piece in assets.Set(item.ArmorSet ?? item.Id))
            {
                if (piece.Slot != slot || _props.Model(piece.Model, multimesh: false) is not { } mesh) continue;
                var node = new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * PieceScale(piece.Bone, piece.RefLength), Name = piece.Name };
                if (Attach(piece.Bone, node)) hideHair |= piece.HidesHair;
            }
        }
        foreach (var stack in inv.Accessories.Stacks)
        {
            var item = stack.Item!;
            var kind = assets.Worn(item.Id);
            if (kind == null || AssetPaths.ItemModel(item, _game.Session.Content) is not { } path || _props.Model(path, multimesh: false) is not { } mesh)
                continue;
            if (kind == "shield")
            {
                // The shield's face (model -Z) turns outward from the left forearm (bone -X), its top up the arm.
                var node = new MeshInstance3D { Mesh = mesh, Position = new Vector3(-0.07f, 0.12f, 0f), RotationDegrees = new Vector3(0f, 90f, 180f) };
                Attach("forearm.L", node);
            }
            else if (_balloon == null)
            {
                var node = new MeshInstance3D { Mesh = mesh };
                if (Attach("chest", node)) _balloon = node;
            }
        }
        _visual!.SetSlotVisible("char:hair", !hideHair);
    }

    private bool Attach(string bone, MeshInstance3D node)
    {
        if (!_visual!.AttachToBone(bone, node))
        {
            node.QueueFree();
            return false;
        }
        _worn.Add(node);
        _wornGeometry.Add(node);
        return true;
    }

    /// <summary>
    /// How much to scale a piece fitted on the reference rig to this body: its bone's length over the reference
    /// length, or, for an end bone (the head), its parent's ratio.
    /// </summary>
    private float PieceScale(string bone, float refLength)
    {
        var assets = WornAssets.Instance;
        for (string? b = bone; b != null; b = _visual!.ParentBone(b))
        {
            float actual = _visual!.BoneLength(b);
            float reference = b == bone ? refLength : assets.RigLength(b);
            if (actual > 0.001f && reference > 0.001f) return Mathf.Clamp(actual / reference, 0.7f, 1.4f);
        }
        return 1f;
    }
}
