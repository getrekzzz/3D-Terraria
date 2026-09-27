namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Flag every world has from its first moment.</summary>
    public const string NewWorldFlag = "new_world";

    /// <summary>Raises the world milestones for items a player carries (checked once a second).</summary>
    private void CheckMilestones(PlayerEntity p)
    {
        if (Session.Content.Flags.Contains(NewWorldFlag) && !Progression.IsSet(NewWorldFlag)) Progression.Set(NewWorldFlag);
        foreach (var stack in p.State.Inventory.Backpack.Stacks)
            if (stack.Item!.Milestone is { } flag && !Progression.IsSet(flag)) Progression.Set(flag);
    }
}
