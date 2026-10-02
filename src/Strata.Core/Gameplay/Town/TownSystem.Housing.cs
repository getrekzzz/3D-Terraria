using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Housing;

namespace Strata.Core.Gameplay.Town;

/// <summary>The housing menu's API (Terraria): query a room, move a resident into it (swapping homes), or evict them.</summary>
public sealed partial class TownSystem
{
    /// <summary>A resident was evicted (homeless until a move-in check finds them a room).</summary>
    public event Action<TownNpc>? Evicted;

    /// <summary>Whether the room around a cell is a suitable home, and who lives there.</summary>
    public HousingQuery QueryHousing(Int3 cell)
    {
        var room = _world.Rooms.Get(cell)!;
        var occupant = room.Valid ? Occupant(room) : null;
        string message = occupant != null ? Format(HousingMessage("occupied"), occupant) : room.Message;
        return new HousingQuery(room, room.Valid && room.HomeNode != null, message, occupant);
    }

    /// <summary>Moves a resident into the room around a cell; its occupant takes the resident's old home.</summary>
    public AssignResult AssignHome(TownNpc npc, Int3 cell, out string message)
    {
        if (npc.Bound || npc.Def.Kind is not (TownNpcKind.Resident or TownNpcKind.Pet))
        {
            message = Format(HousingMessage("notResident"), npc);
            return AssignResult.NotAResident;
        }
        var q = QueryHousing(cell);
        message = q.Message;
        if (!q.Suitable) return AssignResult.NotSuitable;
        if (q.Occupant == npc) return AssignResult.AlreadyHome;
        var result = AssignResult.Assigned;
        if (q.Occupant is { } other)
        {
            var old = npc.Home is { } h ? _world.Rooms.Get(HomeCell(h)) : null;
            if (old is { Valid: true, HomeNode: not null } && old.Key == npc.HomeRoom) SetHome(other, old);
            else ClearHome(other);
            result = AssignResult.Swapped;
        }
        SetHome(npc, q.Room);
        message = Format(HousingMessage("occupied"), npc);
        return result;
    }

    /// <summary>Turns a resident out of their home. As in Terraria, the next move-in check may house them anywhere.</summary>
    public bool Evict(TownNpc npc)
    {
        if (npc.Home == null) return false;
        ClearHome(npc);
        Evicted?.Invoke(npc);
        return true;
    }

    private TownNpc? Occupant(Room room)
    {
        foreach (var r in _residents)
            if (r.HomeRoom == room.Key) return r;
        return null;
    }

    private static void ClearHome(TownNpc npc)
    {
        npc.Home = null;
        npc.HomeRoom = null;
        npc.BannerCell = null;
    }

    /// <summary>The highest room cell in the column above the home node (the banner's place).</summary>
    private static Int3? BannerOf(Room room)
    {
        if (room.HomeNode is not { } node) return null;
        var c = HomeCell(node);
        if (!room.Interior.Contains(c)) return null;
        while (room.Interior.Contains(c + Int3.Up)) c += Int3.Up;
        return c;
    }

    private string HousingMessage(string key) => _world.Housing.Config.Message(key);

    private static string Format(string text, TownNpc npc) => text.Replace("{name}", npc.Title);
}
