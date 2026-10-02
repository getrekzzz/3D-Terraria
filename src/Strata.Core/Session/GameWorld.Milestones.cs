namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Flag every world has from its first moment.</summary>
    public const string NewWorldFlag = "new_world";

    private Gameplay.Guide.HintDirector? _hints;

    /// <summary>Onboarding hints, checked with the milestones once a second (built on first use).</summary>
    public Gameplay.Guide.HintDirector Hints => _hints ??= new Gameplay.Guide.HintDirector(this);

    /// <summary>Raises the world milestones for items a player carries (checked once a second).</summary>
    private void CheckMilestones(PlayerEntity p)
    {
        if (Session.Content.Flags.Contains(NewWorldFlag) && !Progression.IsSet(NewWorldFlag)) Progression.Set(NewWorldFlag);
        var inv = p.State.Inventory;
        Check(inv.Backpack);
        Check(inv.Ammo);
        Hints.Check(p);

        void Check(Gameplay.Items.Inventory storage)
        {
            for (int i = 0; i < storage.Size; i++)
                if (storage[i].Item?.Milestone is { } flag && !Progression.IsSet(flag)) Progression.Set(flag);
        }
    }
}
