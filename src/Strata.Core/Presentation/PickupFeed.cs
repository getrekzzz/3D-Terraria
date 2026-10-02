using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Presentation;

/// <summary>
/// Terraria's pickup popups, engine-neutral: what the player just collected floats up from their head as "Wood (37)".
/// A pickup of the same item and prefix while its popup is still kept (<see cref="MergeWindow"/>) adds to its count,
/// starts it over and pops it, so mining a vein grows one popup instead of flooding the screen; coins of every kind
/// share one popup that sums their value ("1 gold 23 silver"). A popup shows for <see cref="Life"/> seconds after its
/// last pickup, rising <see cref="RisePixels"/> and fading over the last <see cref="FadeTime"/>. At most
/// <see cref="Capacity"/> are kept; a new item takes the oldest one's place. The game samples the motion every frame
/// and rebuilds a popup's text only when its <see cref="Popup.Version"/> changes.
/// </summary>
public sealed class PickupFeed
{
    public const int Capacity = 8;

    /// <summary>Seconds a popup shows after its last pickup.</summary>
    public const double Life = 1.5;

    /// <summary>Seconds of fading at the end of <see cref="Life"/>.</summary>
    public const double FadeTime = 0.5;

    /// <summary>Seconds after its last pickup in which another pickup of the same kind joins a popup.</summary>
    public const double MergeWindow = 2.0;

    /// <summary>Seconds a joined or new popup takes to shrink from <see cref="PopScale"/> back to full size.</summary>
    public const double PopTime = 0.12;

    public const float PopScale = 1.25f;

    /// <summary>Pixels a popup rises over its life.</summary>
    public const float RisePixels = 40;

    /// <summary>Pixels between stacked popups.</summary>
    public const float RowPitch = 22;

    /// <summary>One popup: an item and prefix with a count, or coins with their total value in copper.</summary>
    public sealed class Popup
    {
        public ItemDef? Item { get; internal set; }
        public PrefixDef? Prefix { get; internal set; }
        public bool IsCoins { get; internal set; }

        /// <summary>Items collected, or the copper value for coins.</summary>
        public long Amount { get; internal set; }

        /// <summary>Seconds since the last pickup joined it.</summary>
        public double Age { get; internal set; } = double.MaxValue;

        /// <summary>Changes whenever <see cref="Amount"/> or the item does (the view rebuilds its text).</summary>
        public int Version { get; internal set; }

        public bool Shown => Age < Life;

        internal bool Kept => Age < MergeWindow;

        /// <summary>"Wood (37)", "Legendary Muramasa", or the coins as "1 gold 23 silver".</summary>
        public string Text => IsCoins
            ? Coins.Format(Amount)
            : ItemTooltip.NameLine(new ItemStack(Item!, (int)Math.Min(Amount, int.MaxValue), Prefix));
    }

    private readonly Popup[] _popups = new Popup[Capacity];

    public PickupFeed()
    {
        for (int i = 0; i < Capacity; i++) _popups[i] = new Popup();
    }

    /// <summary>The kept and spent popups (check <see cref="Popup.Shown"/>), in no order.</summary>
    public IReadOnlyList<Popup> Popups => _popups;

    /// <summary>Popups on screen now.</summary>
    public int ShownCount
    {
        get
        {
            int n = 0;
            foreach (var p in _popups)
                if (p.Shown) n++;
            return n;
        }
    }

    /// <summary>Shows a collected stack: joins the kept popup of the same kind, or takes a free (else the oldest) one.</summary>
    public Popup Add(ItemStack stack)
    {
        if (stack.IsEmpty) throw new ArgumentException("an empty stack is not a pickup", nameof(stack));
        bool coins = Coins.IsCoin(stack);
        long amount = coins ? (long)stack.Item!.Value * stack.Count : stack.Count;
        Popup? target = null;
        foreach (var p in _popups)
            if (p.Kept && (coins ? p.IsCoins : !p.IsCoins && p.Item == stack.Item && p.Prefix == stack.Prefix))
            {
                target = p;
                break;
            }
        if (target != null) target.Amount += amount;
        else
        {
            target = _popups[0];
            foreach (var p in _popups)
                if (p.Age > target.Age) target = p; // a spent popup's age keeps growing, so it goes before any kept one
            target.Item = stack.Item;
            target.Prefix = coins ? null : stack.Prefix;
            target.IsCoins = coins;
            target.Amount = amount;
        }
        target.Age = 0;
        target.Version++;
        return target;
    }

    /// <summary>Ages every popup by <paramref name="seconds"/>.</summary>
    public void Tick(double seconds)
    {
        foreach (var p in _popups)
            if (p.Age < double.MaxValue) p.Age += seconds;
    }

    /// <summary>
    /// The row of the popup at <paramref name="index"/> in <see cref="Popups"/> in the stack over the head: 0 for the
    /// latest pickup, one up for each shown popup picked up more recently. Rows follow the age, and so does the rise, so
    /// stacked popups never cross.
    /// </summary>
    public int Row(int index)
    {
        double age = _popups[index].Age;
        int row = 0;
        for (int i = 0; i < _popups.Length; i++)
        {
            var p = _popups[i];
            if (i != index && p.Shown && (p.Age < age || (p.Age == age && i < index))) row++;
        }
        return row;
    }

    /// <summary>Pixels risen <paramref name="age"/> seconds after the last pickup (quick at first, easing out).</summary>
    public static float Rise(double age)
    {
        double t = Math.Clamp(age / Life, 0, 1);
        return (float)(RisePixels * (1 - (1 - t) * (1 - t)));
    }

    /// <summary>Opacity <paramref name="age"/> seconds after the last pickup: full, then fading over the last half second.</summary>
    public static float Alpha(double age) =>
        age <= Life - FadeTime ? 1f : age >= Life ? 0f : (float)((Life - age) / FadeTime);

    /// <summary>Size <paramref name="age"/> seconds after the last pickup: <see cref="PopScale"/> shrinking to 1.</summary>
    public static float Scale(double age) =>
        age >= PopTime ? 1f : (float)(PopScale + (1 - PopScale) * Math.Max(age, 0) / PopTime);
}
