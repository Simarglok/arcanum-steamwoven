using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>Item object types of the v119 schema (5..14). The session keeps its own copy so it needs no data reader.</summary>
public enum SessionItemType { Weapon = 5, Ammo = 6, Armor = 7, Gold = 8, Food = 9, Scroll = 10, Key = 11, KeyRing = 12, Written = 13, Generic = 14 }

/// <summary>Inventory holders the session admits in a map seed. A PC record is not a seed holder: the session owns the PC's list.</summary>
public enum SessionHolderKind { Container = 2, Npc = 16 }

/// <summary>
/// Script attachment points the inventory rules look at, numbered as the source's script attachment
/// points (docs/research/scr-event-dispatcher.md), which are also the key of an object's script array element.
/// </summary>
public static class SessionScriptPoint
{
    public const int Examine = 0;
    public const int Use = 1;
    public const int Get = 4;
    public const int InsertItem = 21;
    public const int WieldOn = 24;
    public const int WieldOff = 25;
    public const int RemoveItem = 28;
    public const int Transfer = 32;
    /// <summary>The last point, <c>SAP_CRITICAL_MISS</c>; script arrays have no other keys.</summary>
    public const int Last = 35;
}

/// <summary>What identifies an item to a player and to a save: type, prototype, numeric name class (<c>OBJ_F_NAME</c>, the
/// key negative IN compares) and description number (the display name source).</summary>
public readonly record struct SessionItemKind(SessionItemType Type, int Prototype, int Name, int Description);

/// <summary>
/// T0 facts of one item, taken by the host from the map's inventory view. <see cref="ScriptPoints"/> lists the attachment
/// points with a script (any order, no duplicates). <see cref="InventoryLocation"/> is the serialized slot (1000..1008 are
/// worn slots); <see cref="Quantity"/> exists for gold and ammunition only.
/// </summary>
public sealed record SessionItemFacts(SessionItemKind Kind, int Weight, int Complexity, int InventoryLocation, int? Quantity,
    IReadOnlyList<int> ScriptPoints)
{
    public bool HasScript(int point) => ScriptPoints.Contains(point);

    /// <summary>Worn slots, helmet 1000 to boots 1008 (the source's wear locations).</summary>
    public bool Worn => InventoryLocation is >= 1000 and <= 1008;
}

/// <summary>One item of a map seed: in exactly one holder's list (no cell) or on the ground at a cell.</summary>
public sealed record SessionSeedItem(string Entity, SessionItemFacts Facts, CellCoord? Cell);

/// <summary>
/// One holder of a map seed with its list in serialized order. <see cref="Dead"/> is the host's life fact (true only
/// when it knows the NPC is dead); <see cref="HasSubstituteInventory"/> is a non-null substitute-inventory handle.
/// </summary>
public sealed record SessionSeedHolder(string Entity, SessionHolderKind Kind, bool Dead, bool HasSubstituteInventory,
    IReadOnlyList<int> ScriptPoints, IReadOnlyList<string> Items);

/// <summary>
/// The loaded T0 inventory of one map, built by the host from its data (for the Crash Site, the ARC-DATA-012 Area view
/// plus life from the step rules). It is a baseline, not session state: it is never saved and is attached again after a
/// restore.
/// </summary>
public sealed record SessionMapInventorySeed(IReadOnlyList<SessionSeedHolder> Holders, IReadOnlyList<SessionSeedItem> Items)
{
    public const int MaxItems = 2048;
    public const int MaxHolders = 2048;
    public const int MaxListLength = 512;
}

/// <summary>An item the PC took from the world, with the map it came from and its saved kind.</summary>
public readonly record struct SessionPcItem(string Entity, SessionMapId Origin, SessionItemKind Kind);

/// <summary>An item of the hero's starting kit (ARC-WORLD-015): entity <c>kit:n</c> and its saved kind. Kit items came from
/// no map; in the PC's inventory they precede every item it took from the world.</summary>
public readonly record struct SessionKitItem(string Entity, SessionItemKind Kind);

/// <summary>
/// Saved inventory state: the PC's gold (ARC-WORLD-015: the hero's starting coins; 0 without a hero), the hero's starting
/// kit in kit order, and the items the PC took from the world in the order they entered its list.
/// </summary>
public sealed class SessionInventoryState
{
    internal SessionInventoryState(int gold, SessionKitItem[] kit, SessionPcItem[] pc)
    {
        Gold = gold;
        Kit = Array.AsReadOnly(kit);
        Pc = Array.AsReadOnly(pc);
    }

    public int Gold { get; }
    public IReadOnlyList<SessionKitItem> Kit { get; }
    public IReadOnlyList<SessionPcItem> Pc { get; }
}

public sealed record SessionInventoryItem(string Entity, SessionItemFacts Facts);

public sealed record SessionGroundItem(SessionInventoryItem Item, CellCoord Cell);

public sealed record SessionHolderInventory(string Entity, SessionHolderKind Kind, bool Dead, IReadOnlyList<SessionInventoryItem> Items);

/// <summary>Detached current inventory of one attached map: its seed without the items the PC has taken.</summary>
public sealed class SessionMapInventory
{
    internal SessionMapInventory(SessionMapId map, SessionGroundItem[] ground, SessionHolderInventory[] holders)
    {
        Map = map;
        Ground = Array.AsReadOnly(ground);
        Holders = Array.AsReadOnly(holders);
    }

    public SessionMapId Map { get; }
    /// <summary>Ground items in seed order.</summary>
    public IReadOnlyList<SessionGroundItem> Ground { get; }
    /// <summary>Holders in seed order, each list in its current order.</summary>
    public IReadOnlyList<SessionHolderInventory> Holders { get; }
}

/// <summary>
/// The PC's insert check (0x466510: carry weight, the Technophobia background against technology items,
/// free inventory room). It depends on the created character and its own items, which the session does not own, so the
/// host answers it per item.
/// </summary>
public enum SessionInsertVerdict { Accepted, TooHeavy, TechnologyRefused, NoRoom }

public delegate SessionInsertVerdict SessionInsertCheck(SessionInventoryItem item);

/// <summary>
/// Outcome of an item transfer. <see cref="Item"/> names the moved item (for negative IN, the one found); <see cref="Verdict"/>
/// is the insert check's answer when it ran (also on an <see cref="GameSessionFailure.InsertRefused"/> refusal).
/// </summary>
public readonly record struct InventoryMutationResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    string? Item, SessionInsertVerdict? Verdict, GameSessionVersion Version)
{
    /// <summary>ARC-SCRIPT-013: the item's insert-item script run, when one ran (also on a refusal of its effects).</summary>
    public ScrRunResult? InsertScript { get; init; }
    private readonly IReadOnlyList<SessionScriptMessage>? _messages;

    /// <summary>The lines that script printed, in order, for the host to show; empty when none.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get => _messages ?? Array.Empty<SessionScriptMessage>(); init => _messages = value; }
}

/// <summary>A line a script printed (ARC-SCRIPT-013): the DLG line of <paramref name="Script"/>, the print action's second
/// number and the text the host built.</summary>
public sealed record SessionScriptMessage(int Script, int Line, int Kind, string Text);

public enum SessionPrintedLineStatus
{
    /// <summary>Show <see cref="SessionPrintedLine.Text"/>.</summary>
    Shown,
    /// <summary>The script's DLG is missing or keeps no record: the original's load fails and nothing is printed (ledger IS2).</summary>
    NoDialog,
    /// <summary>The line would do more than show text (an action field, generated text), cannot be decoded, or its DLG
    /// cannot be read here (a file the original might still load); the transfer is refused before any effect.</summary>
    Unsupported,
}

public sealed record SessionPrintedLine(SessionPrintedLineStatus Status, string Text, string Detail);

/// <summary>
/// ARC-SCRIPT-013: what the session needs from the host to run an item's insert-item script on a transfer. The dispatcher
/// reads the game through the <see cref="IScrHost"/> part (the item's slot, the script files, the session's global flags,
/// the player); <see cref="PrintedLine"/> reads the DLG line a print-line action shows (see
/// <see cref="Arcanum.Scripting.Scr.ScrPrintedLine"/>). Every call only reads; the session applies the run.
/// </summary>
public interface ISessionItemScriptHost : IScrHost
{
    SessionPrintedLine PrintedLine(int script, int line);
}

/// <summary>ARC-SCRIPT-013: how a transfer runs an item's insert-item script.</summary>
public static class SessionItemScripts
{
    /// <summary>
    /// The commands an insert-item script may use: <see cref="ScrAdmissionSet.S6"/> without the dialog action (no
    /// conversation starts from a transfer), the story-state actions (the session has no story-state number) and the
    /// deferred calls (not applied from a transfer yet). A script outside it refuses the transfer.
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S6-item", ScrAdmissionSet.S6.Conditions,
        ScrAdmissionSet.S6.Actions.Where(action => action is not (ScrCatalogue.Action.Dialog or ScrCatalogue.Action.GetStoryState
            or ScrCatalogue.Action.SetStoryState or ScrCatalogue.Action.CallScriptIn or ScrCatalogue.Action.RemoveScriptCall)),
        ScrAdmissionSet.S6.ValueKinds, ScrAdmissionSet.S6.FocusKinds);
}

/// <summary>Mutable inventory domain of one owner: attached map seeds, the PC's gold and the PC's list.</summary>
internal sealed class InventoryDomain
{
    internal sealed class Seed(SessionMapInventorySeed source, Dictionary<string, SessionSeedItem> items,
        Dictionary<string, SessionSeedHolder> holders, Dictionary<string, string> holderOf)
    {
        internal SessionMapInventorySeed Source { get; } = source;
        internal Dictionary<string, SessionSeedItem> Items { get; } = items;
        internal Dictionary<string, SessionSeedHolder> Holders { get; } = holders;
        /// <summary>Item entity to its T0 holder entity.</summary>
        internal Dictionary<string, string> HolderOf { get; } = holderOf;
    }

    internal Dictionary<SessionMapId, Seed> Seeds { get; } = [];
    internal int Gold { get; set; }
    internal List<SessionKitItem> Kit { get; } = [];
    internal List<SessionPcItem> Pc { get; } = [];
    internal HashSet<string> Taken { get; } = new(StringComparer.Ordinal);

    /// <summary>Items the PC holds: the kit and the taken items (the bound of <see cref="GameSessionLimits.MaxPcItems"/>).</summary>
    internal int Held => Kit.Count + Pc.Count;

    internal void Add(SessionPcItem item)
    {
        Pc.Add(item);
        Taken.Add(item.Entity);
    }

    /// <summary>A kit entity is also recorded as taken, so no seed can list it.</summary>
    internal void Add(SessionKitItem item)
    {
        Kit.Add(item);
        Taken.Add(item.Entity);
    }

    internal SessionInventoryState Capture() => new(Gold, Kit.ToArray(), Pc.ToArray());

    /// <summary>The map's current inventory; an NPC holder is dead when its seed says so or <paramref name="killed"/> (the session's
    /// life records, ARC-COMBAT-005) does.</summary>
    internal SessionMapInventory View(SessionMapId map, Seed seed, Func<string, bool> killed) => new(map,
        seed.Source.Items.Where(item => item.Cell is not null && !Taken.Contains(item.Entity))
            .Select(item => new SessionGroundItem(new(item.Entity, item.Facts), item.Cell!.Value)).ToArray(),
        seed.Source.Holders.Select(holder => new SessionHolderInventory(holder.Entity, holder.Kind,
            holder.Dead || holder.Kind == SessionHolderKind.Npc && killed(holder.Entity),
            Array.AsReadOnly(Current(seed, holder).ToArray()))).ToArray());

    /// <summary>The holder's list in its order without the taken items.</summary>
    internal IEnumerable<SessionInventoryItem> Current(Seed seed, SessionSeedHolder holder) => holder.Items
        .Where(entity => !Taken.Contains(entity)).Select(entity => new SessionInventoryItem(entity, seed.Items[entity].Facts));

    /// <summary>Checks a seed's structure and copies it, so later changes to the caller's lists cannot reach the
    /// session; null when it is not admissible.</summary>
    internal static Seed? Validate(SessionMapInventorySeed? source)
    {
        if (source?.Holders is null || source.Items is null) return null;
        if (source.Items.Count > SessionMapInventorySeed.MaxItems || source.Holders.Count > SessionMapInventorySeed.MaxHolders) return null;
        var items = new Dictionary<string, SessionSeedItem>(StringComparer.Ordinal);
        var itemOrder = new List<SessionSeedItem>(source.Items.Count);
        foreach (var raw in source.Items)
        {
            if (raw is null || !SessionValidation.Entity(raw.Entity) || !Facts(raw.Facts)) return null;
            if (raw.Cell is { } cell && (cell.X < 0 || cell.Y < 0)) return null;
            var item = raw with { Facts = raw.Facts with { ScriptPoints = Array.AsReadOnly(raw.Facts.ScriptPoints.ToArray()) } };
            if (!items.TryAdd(item.Entity, item)) return null;
            itemOrder.Add(item);
        }
        var holders = new Dictionary<string, SessionSeedHolder>(StringComparer.Ordinal);
        var holderOrder = new List<SessionSeedHolder>(source.Holders.Count);
        var holderOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in source.Holders)
        {
            if (raw?.Items is null || raw.ScriptPoints is null || !SessionValidation.Entity(raw.Entity) ||
                !Enum.IsDefined(raw.Kind) || !Points(raw.ScriptPoints) || raw.Items.Count > SessionMapInventorySeed.MaxListLength) return null;
            // A container has no life and no substitute inventory.
            if (raw.Kind == SessionHolderKind.Container && (raw.Dead || raw.HasSubstituteInventory)) return null;
            var holder = raw with { ScriptPoints = Array.AsReadOnly(raw.ScriptPoints.ToArray()), Items = Array.AsReadOnly(raw.Items.ToArray()) };
            if (items.ContainsKey(holder.Entity) || !holders.TryAdd(holder.Entity, holder)) return null;
            foreach (var entity in holder.Items)
                if (entity is null || !items.TryGetValue(entity, out var listed) || listed.Cell is not null || !holderOf.TryAdd(entity, holder.Entity))
                    return null;
            holderOrder.Add(holder);
        }
        // Every item is either listed once or on the ground.
        if (items.Values.Any(item => item.Cell is null && !holderOf.ContainsKey(item.Entity))) return null;
        return new(new(Array.AsReadOnly(holderOrder.ToArray()), Array.AsReadOnly(itemOrder.ToArray())), items, holders, holderOf);
    }

    private static bool Facts(SessionItemFacts? facts) => facts?.ScriptPoints is not null && Enum.IsDefined(facts.Kind.Type) &&
        Points(facts.ScriptPoints) && (facts.Quantity is null) == (facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Ammo)) &&
        facts.Quantity is null or >= 0;

    private static bool Points(IReadOnlyList<int> points) =>
        points.All(point => point is >= 0 and <= SessionScriptPoint.Last) && points.Distinct().Count() == points.Count;

    /// <summary>Gold and ammunition merge into a matching stack on insert, and a key goes onto the PC's key ring when it has
    /// one; neither is modelled.</summary>
    internal static bool Admitted(SessionItemType type) => type is not (SessionItemType.Gold or SessionItemType.Ammo or SessionItemType.Key or SessionItemType.KeyRing);
}
