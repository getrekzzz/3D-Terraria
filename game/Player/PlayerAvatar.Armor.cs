using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// Worn armour and body accessories on the avatar (P3-42): each shown armour item (the vanity piece when one is worn,
/// else the armour) shows its set's pieces for its slot (tools/blender/bgen/armor.py, fitted to the player rig and
/// scaled to this body's bone lengths); a helmet hides the hair; shields ride the left forearm and balloons float
/// behind the shoulders (vanity accessories over functional ones; hidden accessory slots are not drawn). Attachments
/// are rebuilt only when the equipment changes; per frame they only take the body's voxel light and a balloon's bob.
/// </summary>
public sealed partial class PlayerAvatar
{
    private static readonly StringName VoxelLight = "voxel_light";
    private readonly List<Node3D> _worn = new();
    private readonly List<GeometryInstance3D> _wornGeometry = new();
    private Node3D? _balloon;
    private bool _wornDirty = true;
    private float _bob;

    /// <summary>Armour pieces and worn accessories currently attached (probe <c>avatar.armor_pieces</c>).</summary>
    public int WornCount => _worn.Count;

    private void InitializeArmor()
    {
        // Armour, accessories, vanity, unlocked slots and the hide toggles all raise it.
        _controller.Entity.State.Inventory.EquipmentChanged += () => _wornDirty = true;
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
        foreach (var g in _wornGeometry) g.SetInstanceShaderParameter(VoxelLight, light);
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
        for (int i = 0; i < PlayerInventory.ArmorSlots; i++)
        {
            if (inv.ShownArmor(i) is not { } item) continue;
            string slot = item.EquipSlot.ToString().ToLowerInvariant();
            var set = item.ArmorSet != null && assets.Set(item.ArmorSet).Count > 0 ? item.ArmorSet : item.Id; // as armor_build.armor_items
            foreach (var piece in assets.Set(set))
            {
                if (piece.Slot != slot || _props.Model(piece.Model, multimesh: false) is not { } mesh) continue;
                var node = new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * PieceScale(piece.Bone, piece.RefLength), Name = piece.Name };
                if (Attach(piece.Bone, node)) hideHair |= piece.HidesHair;
            }
        }
        // One shield and one balloon; a later shown accessory replaces an earlier one, so vanity wins.
        ItemDef? shield = null, balloon = null;
        foreach (var item in inv.ShownAccessories)
        {
            var kind = assets.Worn(item.Id);
            if (kind == "shield") shield = item;
            else if (kind != null) balloon = item;
        }
        if (WornModel(shield) is { } shieldMesh)
        {
            // The shield's face (model -Z) turns outward from the left forearm (bone -X), its height along the arm.
            Attach("forearm.L", new MeshInstance3D
            {
                Mesh = shieldMesh, Position = new Vector3(-0.07f, -0.02f, 0f), RotationDegrees = new Vector3(0f, 90f, 0f),
            });
        }
        if (WornModel(balloon) is { } balloonMesh)
        {
            var node = new MeshInstance3D { Mesh = balloonMesh };
            if (Attach("chest", node)) _balloon = node;
        }
        _visual!.SetSlotVisible("char:hair", !hideHair);
    }

    private Mesh? WornModel(ItemDef? item) =>
        item != null && AssetPaths.ItemModel(item, _game.Session.Content) is { } path ? _props.Model(path, multimesh: false) : null;

    private bool Attach(string bone, MeshInstance3D node)
    {
        if (!_visual!.AttachToBone(bone, node))
        {
            node.QueueFree();
            return false;
        }
        node.LodBias = 8f; // like the body (PlayerAvatar.Initialize): simplified LODs would let the body poke through
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
