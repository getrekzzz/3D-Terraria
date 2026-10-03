using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Town;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>
/// Emote bubbles over residents' and players' heads (X-05): a fixed pool of billboard sprites (budgeted by graphics
/// preset) showing assets/icons/emotes/&lt;id&gt;.png. A bubble pops in, bobs, follows its owner and fades; a new emote
/// from the same owner replaces theirs. Bubbles beyond the camera's hearing range are skipped.
/// </summary>
public sealed partial class TownEmoteBubbles : Node3D
{
    private const float HeadHeight = 2.35f, PlayerHeadHeight = 2.2f, PetHeadHeight = 1.0f, MaxDistance = 48f;

    private sealed class Bubble
    {
        public required Sprite3D Sprite;
        public TownNpc? Npc;
        public PlayerEntity? Player;
        public float Age, Life;
    }

    private readonly List<Bubble> _pool = new();
    private readonly Dictionary<string, Texture2D?> _icons = new();
    private GameWorld _game = null!;
    private float _life;

    public void Initialize(GameWorld game, bool lowPreset)
    {
        _game = game;
        Name = "EmoteBubbles";
        _life = game.Town.Config.Emotes.BubbleTicks / 60f;
        int size = lowPreset ? 4 : 12;
        for (int i = 0; i < size; i++)
        {
            var sprite = new Sprite3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                PixelSize = 0.009f,
                Shaded = false,
                Transparent = true,
                NoDepthTest = false,
                Visible = false,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
            AddChild(sprite);
            _pool.Add(new Bubble { Sprite = sprite });
        }
        game.Town.Emoted += (npc, emote) => Show(emote, npc, null);
        game.Town.PlayerEmoted += (p, emote) => Show(emote, null, p);
        game.Town.Removed += npc =>
        {
            foreach (var b in _pool)
                if (b.Npc == npc) Hide(b);
        };
    }

    private void Show(string emote, TownNpc? npc, PlayerEntity? player)
    {
        if (Icon(emote) is not { } icon) return;
        var camera = GetViewport().GetCamera3D();
        var at = npc?.Position ?? player!.Position;
        if (camera != null && camera.GlobalPosition.DistanceTo(new Vector3(at.X, at.Y, at.Z)) > MaxDistance) return;
        Bubble? slot = null;
        foreach (var b in _pool)
            if (b.Sprite.Visible && (npc != null && b.Npc == npc || player != null && b.Player == player)) slot = b;
        if (slot == null)
            foreach (var b in _pool)
                if (!b.Sprite.Visible && (slot == null)) slot = b;
        if (slot == null)
        {
            // Over budget: the oldest bubble makes room.
            foreach (var b in _pool)
                if (slot == null || b.Age > slot.Age) slot = b;
        }
        slot!.Npc = npc;
        slot.Player = player;
        slot.Age = 0f;
        slot.Life = _life;
        slot.Sprite.Texture = icon;
        slot.Sprite.Visible = true;
        Place(slot);
    }

    private Texture2D? Icon(string id)
    {
        if (_icons.TryGetValue(id, out var t)) return t;
        string path = $"res://assets/icons/emotes/{id}.png";
        t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        _icons[id] = t;
        return t;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (var b in _pool)
        {
            if (!b.Sprite.Visible) continue;
            b.Age += dt;
            if (b.Age >= b.Life || b.Npc is { Alive: false }) { Hide(b); continue; }
            Place(b);
        }
    }

    private void Place(Bubble b)
    {
        float lift;
        System.Numerics.Vector3 at;
        if (b.Npc != null)
        {
            at = b.Npc.Position;
            lift = b.Npc.Def.Kind == Strata.Core.Content.Defs.TownNpcKind.Pet ? PetHeadHeight : HeadHeight;
        }
        else
        {
            at = b.Player!.Position;
            lift = PlayerHeadHeight;
        }
        float t = b.Age;
        float pop = Mathf.Min(1f, t / 0.12f);
        float scale = pop < 1f ? Mathf.Lerp(0.3f, 1.15f, pop) : Mathf.Lerp(1.15f, 1f, Mathf.Min(1f, (t - 0.12f) / 0.1f));
        float fade = Mathf.Clamp((b.Life - t) / 0.3f, 0f, 1f);
        b.Sprite.Position = new Vector3(at.X, at.Y + lift + 0.05f * Mathf.Sin(t * 5f), at.Z);
        b.Sprite.Scale = Vector3.One * scale;
        b.Sprite.Modulate = new Color(1f, 1f, 1f, fade);
    }

    private static void Hide(Bubble b)
    {
        b.Sprite.Visible = false;
        b.Npc = null;
        b.Player = null;
    }
}
