using Godot;

namespace Strata.Game.Bootstrap;

/// <summary>Composition root placeholder (replaced as the runtime grows).</summary>
public partial class GameRoot : Node
{
    public override void _Ready()
    {
        GD.Print("Strata booted");
    }
}
