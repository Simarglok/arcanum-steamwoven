using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;

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
    public const int Drop = 5;
    /// <summary>ARC-COMBAT-023: the item's throw script (0x4B3C00: point 6, whose skip default refuses the throw).</summary>
    public const int Throw = 6;
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
/// worn slots); <see cref="Quantity"/> exists for gold and ammunition only, and (ARC-WORLD-066) <see cref="AmmoType"/> for
/// ammunition only (0..<see cref="SessionAmmoRecord.AmmoTypes"/> − 1).
/// </summary>
public sealed record SessionItemFacts(SessionItemKind Kind, int Weight, int Complexity, int InventoryLocation, int? Quantity,
    IReadOnlyList<int> ScriptPoints)
{
    /// <summary>ARC-WORLD-066: the ammunition type (field 0x8F) of an ammunition item; null for any other item.</summary>
    public int? AmmoType { get; init; }

    /// <summary>ARC-SCRIPT-2397 (ledger IV15, IV18): the map data's own off bit of the item (object flag 0x2 of field 0x13). The original keeps the flag word through every move, so an item the data stores off that reaches the ground (an NPC's drop, a spill) lies switched off there; a script's toggle (<see cref="GameSessionOwner.QueryItemOff"/>) flips it from this value.</summary>
    public bool OffInData { get; init; }

    /// <summary>ARC-WORLD-123: the key number (field 0xBA) of a key item, which the key ring lists and a lock compares; null for any other item.</summary>
    public int? KeyId { get; init; }

    /// <summary>ARC-WORLD-062: the item flag 0x20000 (field 0x57) that keeps a dropped item from decaying.</summary>
    public bool NoDecay { get; init; }

    /// <summary>ARC-WORLD-1158: the item flag 0x20 (field 0x57) "not droppable": the remove check (0x466DA0) refuses the item's drop (the grid, wear and throw paths). ARC-WORLD-1299: a dead body's inventory walk (0x463860) ignores it and lays the item down like any other.</summary>
    public bool NoDrop { get; init; }

    /// <summary>ARC-WORLD-1299: the item flag 0x10 (field 0x57) "no display": a dead body's inventory walk destroys such an item (0x463860). A seed that does not say reads as clear.</summary>
    public bool NoDisplay { get; init; }

    /// <summary>ARC-WORLD-1299: the spell flag 0x4000 (field 0x14) "summoned": a dead body's inventory walk destroys such an item (0x463860). A seed that does not say reads as clear.</summary>
    public bool Summoned { get; init; }

    /// <summary>ARC-WORLD-085: the magic weight adjustment (field 0x5A), which the holder's power scales into the item's weight.</summary>
    public int MagicWeight { get; init; }

    /// <summary>ARC-WORLD-085: the weight facts the session keeps for the item once it tracks it.</summary>
    public SessionItemWeight WeightFacts => new(Weight, MagicWeight, Complexity);

    /// <summary>ARC-WORLD-063: the item's size in inventory cells (its inventory art's frame in 32-pixel cells, 0x461780), which decides
    /// whether a junk pile has room for it; 1 × 1 when the host does not know it (the original's size when the art does not read).</summary>
    public int InventoryWidth { get; init; } = 1;

    /// <inheritdoc cref="InventoryWidth"/>
    public int InventoryHeight { get; init; } = 1;

    /// <summary>The largest side in cells (0x461780 of a 0-pixel side wraps to 2^27); a seed with a size outside 1..this is refused.</summary>
    public const int MaxInventoryCells = 1 << 27;

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
    /// <summary>ARC-COMBAT-024: the seed covers a window of its map (the streamed main map), not the whole map: an item the session
    /// took from the map elsewhere is not in it (<see cref="GameSessionOwner.AttachMapInventory"/>).</summary>
    public bool Window { get; init; }

    public const int MaxItems = 2048;
    public const int MaxHolders = 2048;
    public const int MaxListLength = 512;
}

/// <summary>An item the PC took from the world, with the map it came from and its saved kind.</summary>
public readonly record struct SessionPcItem(string Entity, SessionMapId Origin, SessionItemKind Kind);

/// <summary>An item of the hero's starting kit (ARC-WORLD-015): entity <c>kit:n</c> and its saved kind. Kit items came from
/// no map; in the PC's inventory they precede every item it took from the world.</summary>
public readonly record struct SessionKitItem(string Entity, SessionItemKind Kind);

/// <summary>A made item (ARC-TECHNOLOGY-004): entity <c>made:n</c> and its saved kind. Made items came from no map: the hero makes
/// them from schematics, and since ARC-WORLD-055 the session adds them to NPCs (one numbering); in the hero's inventory they follow
/// the items it took from the world. ARC-WORLD-083: its magick/tech complexity (field 0x60), which the original's object keeps through
/// every move (ledger MC1): an assembly product's from the host's facts for its prototype, 0 (neutral) where the session was given
/// none.</summary>
public readonly record struct SessionMadeItem(string Entity, SessionItemKind Kind, int Complexity = 0)
{
    internal const string Prefix = "made:";

    /// <summary>The number n of <c>made:n</c> (1 or more), or null for any other entity.</summary>
    internal static int? Number(string entity) =>
        entity.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(entity.AsSpan(Prefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1 && entity == EntityOf(n) ? n : null;

    internal static string EntityOf(int number) => Prefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// ARC-WORLD-055: the items the session added to one NPC's inventory, keyed by the NPC's entity identity (the entity of its
/// home map, so the list follows a companion that visits another map, as the original carries a follower with its items).
/// The items are made items (<c>made:n</c>, numbered with the hero's), in the order they entered the NPC's list; in the NPC's
/// list they follow its seed items. ARC-WORLD-090: <see cref="Container"/> marks the list of a container instead (a merchant's substitute
/// container, which the barter window fills): <see cref="Npc"/> is then the container's entity.
/// </summary>
public sealed record SessionNpcInventory(string Npc, IReadOnlyList<SessionMadeItem> Items)
{
    public bool Container { get; init; }
}

/// <summary>
/// ARC-WORLD-066: the units of an ammunition stack the session tracks: every ammunition item the hero holds or placed, and any
/// other ammunition item whose units the session changed (a seed stack an NPC spent from, a stack an NPC received). Its ammunition
/// type (0..3, the original's tables) and its unit count (0 or more; the original never removes a stack at 0 by itself).
/// </summary>
public readonly record struct SessionAmmoRecord(string Entity, int AmmoType, int Units)
{
    /// <summary>The ammunition types of the original's tables (0x5B32A0, 0x5B32B0): arrows, bullets, charge cells, fuel.</summary>
    public const int AmmoTypes = 4;
    /// <summary>Charge cells (ARC-TECHNOLOGY-007).</summary>
    public const int Cells = 2;
}

/// <summary>
/// ARC-WORLD-071: the coins of an NPC's gold pile the session tracks: a seed pile whose coins it changed, or a pile it created in the
/// NPC's list (a <c>made:n</c> item of the gold prototype). Its coins are 1 or more (a move that empties a pile destroys it, NT1).
/// </summary>
public readonly record struct SessionGoldRecord(string Entity, int Quantity);

/// <summary>
/// ARC-WORLD-071: an item the hero gave an NPC that is not a made item: a kit item (<see cref="Origin"/> null) or an item the PC took
/// from the world, with the map it came from. It stays in the NPC's list (<see cref="SessionNpcInventory"/>) until it leaves it, and an
/// item the hero takes back returns to the list it came from.
/// </summary>
public readonly record struct SessionGivenItem(string Entity, SessionMapId? Origin);

/// <summary>Which of the hero's lists a placed item came from, and goes back to when it is picked up (ARC-WORLD-059). ARC-WORLD-081:
/// <see cref="Created"/> is an item a script created on the ground (create object, action 52); it was in no list and joins the made items
/// when picked up.</summary>
public enum SessionPlacedSource { Kit, Pc, Made, Created }

/// <summary>
/// One of the hero's items the session placed on the ground (ARC-WORLD-059): its entity and kind, the hero's list it came from
/// (<see cref="Origin"/>, the map whose seed lists it, for an item the PC took from the world), and the map and cell it lies on.
/// ARC-WORLD-083: a made item keeps its <see cref="SessionMadeItem.Complexity"/> on the ground (0 for any other source, whose seed
/// answers for a taken item). ARC-WORLD-081: a created item (<see cref="SessionPlacedSource.Created"/>, numbered <c>made:n</c> with the made
/// items) keeps its prototype's complexity; created items have their own budget and no hero is needed for them.
/// </summary>
public sealed record SessionPlacedItem(string Entity, SessionItemKind Kind, SessionPlacedSource Source, SessionMapId? Origin, SessionMapId Map,
    CellCoord Cell, int Complexity = 0);

/// <summary>
/// ARC-SCRIPT-1279 (ledger WO14, WO16): what the session wrote on one item's own fields through the script actions set written start (94, field 0xCB of a written item) and toggle item inventory
/// display (127, bit 0x10 of the item flags, field 0x57): the written start line the item now has (null: the data's), and whether the display bit is flipped against what the data stores (a toggle
/// is an exclusive-or, so the session keeps the parity, not the bit).
/// </summary>
public readonly record struct SessionItemFieldWrites(int? WrittenStart, bool DisplayFlipped)
{
    public bool IsEmpty => WrittenStart is null && !DisplayFlipped;
}

/// <summary>One item's written fields in a save (the <c>inventory</c> section's <c>itemFields</c>, schema 94).</summary>
public sealed record SessionItemFieldRecord(string Entity, SessionItemFieldWrites Writes);

/// <summary>ARC-RES-1441: the slot (a cell of the holder's grid, 0..959) the arrangement at a window's open gave one item of a seed holder's list.</summary>
public readonly record struct SessionHolderSlot(string Item, int Slot);

/// <summary>ARC-RES-1441 (schema 95): the arranged slots of one seed holder (a container or an NPC of a map's seed) in a save, by item entity in ordinal order; never empty.</summary>
public sealed record SessionHolderSlots(string Holder, IReadOnlyList<SessionHolderSlot> Slots);

/// <summary>
/// Saved inventory state: the PC's gold (ARC-WORLD-015: the hero's starting coins; 0 without a hero), the hero's starting
/// kit in kit number order, the items the PC took from the world in the order they entered its list, (ARC-TECHNOLOGY-004) the
/// items the hero made from schematics or took from an NPC that held made items, in the order they entered its list,
/// (ARC-WORLD-055) the made items the session added to NPCs, by NPC entity in ordinal order, and (ARC-WORLD-059) the hero's items
/// the session placed on the ground, in the order they were placed, and (ARC-WORLD-066) the ammunition stacks it tracks, by entity
/// in ordinal order.
/// </summary>
public sealed class SessionInventoryState
{
    internal SessionInventoryState(int gold, SessionKitItem[] kit, SessionPcItem[] pc, SessionMadeItem[] made, SessionNpcInventory[] npcs,
        SessionPlacedItem[] placed, SessionAmmoRecord[]? ammo = null, SessionGivenItem[]? given = null, SessionGoldRecord[]? piles = null,
        SessionItemScriptSlot[]? scriptSlots = null, SessionItemWeightRecord[]? weights = null,
        string[]? identified = null, int lastDiscardedMade = 0, SessionHeroGridItem[]? grid = null, SessionGeneratedContainer[]? generated = null,
        SessionNpcWear[]? npcWorn = null)
    {
        NpcWorn = Array.AsReadOnly(npcWorn ?? []);
        Generated = Array.AsReadOnly(generated ?? []);
        Grid = Array.AsReadOnly(grid ?? []);
        Identified = Array.AsReadOnly(identified ?? []);
        LastDiscardedMade = lastDiscardedMade;
        ScriptSlots = Array.AsReadOnly(scriptSlots ?? []);
        Ammo = Array.AsReadOnly(ammo ?? []);
        Weights = Array.AsReadOnly(weights ?? []);
        Given = Array.AsReadOnly(given ?? []);
        Piles = Array.AsReadOnly(piles ?? []);
        Gold = gold;
        Kit = Array.AsReadOnly(kit);
        Pc = Array.AsReadOnly(pc);
        Made = Array.AsReadOnly(made);
        Npcs = Array.AsReadOnly(npcs);
        Placed = Array.AsReadOnly(placed);
    }

    public int Gold { get; }
    public IReadOnlyList<SessionKitItem> Kit { get; }
    public IReadOnlyList<SessionPcItem> Pc { get; }
    public IReadOnlyList<SessionMadeItem> Made { get; }
    public IReadOnlyList<SessionNpcInventory> Npcs { get; }
    public IReadOnlyList<SessionPlacedItem> Placed { get; }
    public IReadOnlyList<SessionAmmoRecord> Ammo { get; }
    /// <summary>ARC-WORLD-071: the kit and taken items the hero gave NPCs, by entity in ordinal order.</summary>
    public IReadOnlyList<SessionGivenItem> Given { get; }
    /// <summary>ARC-WORLD-071: the gold piles whose coins the session tracks, by entity in ordinal order.</summary>
    public IReadOnlyList<SessionGoldRecord> Piles { get; }
    /// <summary>ARC-SCRIPT-042: the item script slots runs changed, by entity in ordinal order and then by point.</summary>
    public IReadOnlyList<SessionItemScriptSlot> ScriptSlots { get; }
    /// <summary>ARC-WORLD-085: the weight facts of the objects the session tracks, by entity in ordinal order (rows with a non-zero
    /// weight or adjustment only).</summary>
    public IReadOnlyList<SessionItemWeightRecord> Weights { get; }
    /// <summary>ARC-UI-019: the slots of the hero's items in its inventory grid, in slot order.</summary>
    public IReadOnlyList<SessionHeroGridItem> Grid { get; }
    /// <summary>ARC-WORLD-090: the items the session marked identified (item flag 0x1, a barter window's open and sale), by entity in
    /// ordinal order.</summary>
    public IReadOnlyList<string> Identified { get; }
    /// <summary>ARC-WORLD-094: the highest made number of a made item a regeneration discarded without a deleted-item record (0 when
    /// none), so a new made item never takes a discarded one's number.</summary>
    public int LastDiscardedMade { get; }
    /// <summary>ARC-WORLD-100: the containers with flag 0x200 the session generated once (their source and flag cleared, ledger RK10), by
    /// map and then entity in ordinal order.</summary>
    public IReadOnlyList<SessionGeneratedContainer> Generated { get; }
    /// <summary>ARC-WORLD-103: the worn items of the NPCs the session dressed (a regeneration's best-items wield, ledger RK15), by NPC entity
    /// in ordinal order; for such an NPC they replace the inventory locations of its map data.</summary>
    public IReadOnlyList<SessionNpcWear> NpcWorn { get; }

    /// <summary>ARC-SCRIPT-1279 (schema 94): the item fields scripts wrote (written start, inventory display bit), by entity in ordinal order; no row is empty.</summary>
    public IReadOnlyList<SessionItemFieldRecord> ItemFields { get; init; } = [];

    /// <summary>ARC-SCRIPT-1279: how many items carry written fields at once (two full lists, many times over).</summary>
    public const int MaxItemFields = 16384;

    /// <summary>ARC-RES-1441 (schema 95): the slots the arrangement at the open of a loot, steal or See Contents window gave the items of seed holders, by holder entity in ordinal order
    /// (<c>InventoryDomain.HolderSlots</c>); no row is empty.</summary>
    public IReadOnlyList<SessionHolderSlots> HolderSlots { get; init; } = [];

    /// <summary>ARC-RES-1441: how many holders keep arranged slots at once (as many as the dressed NPCs may, many times over).</summary>
    public const int MaxHolderSlots = 4096;

    /// <summary>ARC-WORLD-090: how many identified items a session keeps (two full lists, a merchant's and its container's, many times over).</summary>
    public const int MaxIdentified = 16384;
    /// <summary>ARC-WORLD-100: how many generated-once containers a session keeps (116 installed, many times over).</summary>
    public const int MaxGenerated = 4096;
    /// <summary>ARC-WORLD-103: how many dressed NPCs a session keeps (238 installed NPCs have an inventory source, many times over).</summary>
    public const int MaxNpcWorn = 4096;
}

public sealed record SessionInventoryItem(string Entity, SessionItemFacts Facts);

/// <summary>ARC-WORLD-100: a container with flag 0x200 the session generated once: its map and entity.</summary>
public readonly record struct SessionGeneratedContainer(SessionMapId Map, string Entity);

public sealed record SessionGroundItem(SessionInventoryItem Item, CellCoord Cell);

public sealed record SessionHolderInventory(string Entity, SessionHolderKind Kind, bool Dead, IReadOnlyList<SessionInventoryItem> Items);

/// <summary>Detached current inventory of one attached map: its seed without the items the PC has taken or the session deleted,
/// each NPC holder's list followed by the made items the session added to that NPC (ARC-WORLD-055).</summary>
public sealed class SessionMapInventory
{
    internal SessionMapInventory(SessionMapId map, SessionGroundItem[] ground, SessionHolderInventory[] holders, SessionJunkPileInventory[]? junkPiles = null)
    {
        JunkPiles = Array.AsReadOnly(junkPiles ?? []);
        Map = map;
        Ground = Array.AsReadOnly(ground);
        Holders = Array.AsReadOnly(holders);
    }

    public SessionMapId Map { get; }
    /// <summary>Ground items in seed order.</summary>
    public IReadOnlyList<SessionGroundItem> Ground { get; }
    /// <summary>Holders in seed order, each list in its current order.</summary>
    public IReadOnlyList<SessionHolderInventory> Holders { get; }
    /// <summary>ARC-WORLD-063: the junk piles the session made on this map, in the order they were made, each with its items in the
    /// pile's order; an item in a pile is not in <see cref="Ground"/>.</summary>
    public IReadOnlyList<SessionJunkPileInventory> JunkPiles { get; }
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
    private readonly IReadOnlyList<SessionItemScriptRun>? _scripts;

    /// <summary>The lines the transfer's scripts printed, in order, for the host to show; empty when none.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get => _messages ?? Array.Empty<SessionScriptMessage>(); init => _messages = value; }

    /// <summary>ARC-SCRIPT-041: every item script run of the transfer, in order (also on a refusal); empty without a script host.</summary>
    public IReadOnlyList<SessionItemScriptRun> Scripts { get => _scripts ?? Array.Empty<SessionItemScriptRun>(); init => _scripts = value; }

    /// <summary>ARC-SCRIPT-041: the point whose script returned skip default (<see cref="SessionScriptPoint.Get"/> or
    /// <see cref="SessionScriptPoint.Transfer"/>), so the item did not move; <see cref="Item"/> is null then, and the disposition is
    /// Changed only when that run wrote global flags.</summary>
    public int? DeclinedBy { get; init; }

    /// <summary>ARC-WORLD-1283 (ledger NT2, VP5): the item message number the original shows to the PC when a transfer refuses at the item's remove check
    /// (0x466DA0: the no-drop flag, code 11; the message is item.mes 100 + code, <see cref="GameSessionOwner.NoDropMessage"/>, through 0x4673F0), set on that refusal
    /// whether the hero is the source or the recipient. A dialog <c>IN -name</c> that ends this way is <see cref="GameSessionDisposition.Changed"/> (the item laid at the PC's
    /// tile, as the original does after any transfer that did not end with the PC) and carries the number too; null otherwise.</summary>
    public int? PcMessage { get; init; }

    private readonly IReadOnlyList<SessionItemTriggerResult>? _itemSpells;

    /// <summary>ARC-MAGIC-022: what the item trigger host's executors did after the move (the pickup 0x4CBAA0 of an item the hero got,
    /// the removal's steps of an item it gave away); empty without a host.</summary>
    public IReadOnlyList<SessionItemTriggerResult> ItemSpells { get => _itemSpells ?? Array.Empty<SessionItemTriggerResult>(); init => _itemSpells = value; }
}

/// <summary>ARC-SCRIPT-041: one item script run of a transfer: the attachment point, the attachee's entity (the item, or
/// <see cref="SessionEventKinds.PlayerEntity"/> for the recipient's transfer script) and the dispatcher's result.</summary>
public sealed record SessionItemScriptRun(int Point, string Attachee, ScrRunResult Run);

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

    /// <summary>
    /// ARC-AI-1655 (ledger IS2): the same line for a run whose triggerer is an NPC, not the PC (an NPC's ground pickup runs the item's get and insert-item scripts with the NPC as the triggerer): the original picks the text or the alternate text by
    /// the triggerer's gender when it is a critter. A host that does not know the NPC's gender answers a line whose two texts agree and is otherwise <see cref="SessionPrintedLineStatus.Unsupported"/> (the session then refuses the pickup); the default
    /// reads no line.
    /// </summary>
    SessionPrintedLine PrintedLineForNpc(int script, int line) => new(SessionPrintedLineStatus.Unsupported, string.Empty, "this host does not read a line for an NPC triggerer");

    /// <summary>ARC-SCRIPT-042: the rule of an effect number an add-effect action names (<see cref="SessionEffectDefinition.FromRule"/> of
    /// the module's effect rule), or null when the host has none; the session then refuses the move.</summary>
    SessionEffectDefinition? EffectDefinition(int number) => null;

    /// <summary>ARC-SCRIPT-042: the identified test of an object (<see cref="IScrItemQueries.IsIdentified"/>), or null when the host does
    /// not know it; the session then refuses the move.</summary>
    bool? IsIdentified(ScrObjectRef obj) => null;
}

/// <summary>ARC-SCRIPT-013: how a transfer runs an item's insert-item script; ARC-SCRIPT-042: and an equipment move its item events.</summary>
public static class SessionItemScripts
{
    /// <summary>
    /// The commands an item event's script may use: <see cref="ScrAdmissionSet.S6"/> without the dialog action (no
    /// conversation starts from a transfer) and the story-state actions (the session has no story-state number); ARC-MAGIC-041 (TI8) keeps the
    /// deferred calls, call script in (54) and remove script call (125), which the move schedules and removes in the session's event queue as it
    /// commits (<c>ItemScriptRuns.Commit</c>); use ammunition units (135) stays out, since the only script that uses it runs it from a deferred call
    /// (a host of its own), never from the event; ARC-SCRIPT-042 adds the item-effect commands of <see cref="ScrAdmissionSet.S14"/>:
    /// the identified and rewielding conditions, add and remove effect, call-ex, the magick-tech adjustment, play script eye candy and play
    /// sound (82; the eye candy and the sound stay in the runs' effects for the host to show and play); ARC-DEV-152 adds the value kinds of the
    /// script stores (<see cref="ScrAdmissionSet.StoreValueKinds"/>: global variables, the hero's variables and flags, read and written as the
    /// move's global flags are); ARC-DEV-366 adds get stat (33, ledger GS1), which the session answers for the hero from the effect list the
    /// move's earlier runs left (the tech books' insert script reads the holder's intelligence, XA9); ARC-SCRIPT-2335 (ledger IV1–IV8) adds the loop for, loop end and loop break
    /// (18 to 20), the looped-object and container vicinity focus kinds (10, 17, 18), the name condition (20) and change art number (59), which the display-case scripts of the temple
    /// items use, answered by <see cref="ISessionItemWorldHost"/>; ARC-SCRIPT-2336 (ledger FK8) adds the follower focus kinds every follower and any follower (2, 3: the triggerer's direct followers, answered by
    /// the same world part); ARC-SCRIPT-2346 (ledger IV9–IV11) adds toggle state (56), which flips the off flag of an item of the move (the attachee in the installed scripts).; ARC-SCRIPT-2347 (IV13–IV16) adds assign object (15, with the run's object variables as the focus kind 11), get object type (34), the quest state condition (6) and obj is dead (22), which the same part answers
    /// (as the use host reads them), and monster generator disabled (46), which the session answers from its generator table (IV13–IV15); ARC-SCRIPT-2374 (IV18–IV21) adds set PC quest state (16) and give quest experience (63), which the session applies in the move's own revision
    /// (a request for a PC only; at most one experience award, which may raise a level: the host takes its level-up messages and follower step, ARC-SCRIPT-2394, and a stat or power ratio read after it refuses the move). A script outside it refuses the move.
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S14-item",
        ScrAdmissionSet.S6.Conditions.Concat([ScrCatalogue.Condition.Identified, ScrCatalogue.Condition.ItemsBeingRewielded, ScrCatalogue.Condition.IsNamed,
            ScrCatalogue.Condition.PcQuestState, ScrCatalogue.Condition.IsDead, ScrCatalogue.Condition.MonsterGenDisabled]),
        ScrAdmissionSet.S6.Actions.Where(action => action is not (ScrCatalogue.Action.Dialog or ScrCatalogue.Action.GetStoryState
            or ScrCatalogue.Action.SetStoryState))
            .Concat([ScrCatalogue.Action.AddEffect, ScrCatalogue.Action.RemoveEffect, ScrCatalogue.Action.CallScriptEx,
                ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy, ScrCatalogue.Action.PlaySound, ScrCatalogue.Action.GetStat,
                ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.LoopBreak, ScrCatalogue.Action.ChangeArtNumber, ScrCatalogue.Action.ToggleState,
                ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.GetObjectType, ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.GiveQuestExperience]),
        ScrAdmissionSet.S6.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds),
        ScrAdmissionSet.S6.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EveryContainerInVicinity, ScrFocusKind.AnyContainerInVicinity,
            ScrFocusKind.LocalObject, ScrFocusKind.EveryFollower, ScrFocusKind.AnyFollower]));

    /// <summary>ARC-SCRIPT-042: how many item script slots a session keeps (<see cref="GameSessionLimits.PolicyMaxPcItems"/> items with
    /// all eight item event points each).</summary>
    public const int MaxStoredSlots = GameSessionLimits.PolicyMaxPcItems * 8;

    /// <summary>
    /// ARC-SCRIPT-042, WE1: the cause an add-effect action names, as the original numbers its causes (race 0, bless 3, curse 4 and
    /// gender 9 read in the executable's callers of the effect add; the others in the same order: background 1, class 2, item 5,
    /// spell 6, injury 7, tech 8); null for any other number.
    /// </summary>
    public static EffectCause? Cause(int code) => code switch
    {
        0 => EffectCause.Race, 1 => EffectCause.Background, 2 => EffectCause.Class, 3 => EffectCause.Bless, 4 => EffectCause.Curse,
        5 => EffectCause.Item, 6 => EffectCause.Spell, 7 => EffectCause.Injury, 8 => EffectCause.Tech, 9 => EffectCause.Gender,
        _ => null,
    };
}

/// <summary>Mutable inventory domain of one owner: attached map seeds, the PC's gold and the PC's list, and the made items added to
/// NPCs.</summary>
internal sealed class InventoryDomain
{
    internal sealed class Seed(SessionMapInventorySeed source, Dictionary<string, SessionSeedItem> items,
        Dictionary<string, SessionSeedHolder> holders, Dictionary<string, string> holderOf)
    {
        internal SessionMapInventorySeed Source { get; private set; } = source;
        internal Dictionary<string, SessionSeedItem> Items { get; } = items;
        internal Dictionary<string, SessionSeedHolder> Holders { get; } = holders;
        /// <summary>Item entity to its T0 holder entity.</summary>
        internal Dictionary<string, string> HolderOf { get; } = holderOf;

        /// <summary>
        /// ARC-DEV-386 (E1-c4, OM16): a holder the host's map data does not list: a critter the session created on this map. It has no seed items (its list is what the session adds to it), no
        /// script points (the session reads no game data) and is alive; it joins <see cref="Source"/> after the map's own holders, so a view lists it last. False when the entity is already a holder
        /// or an item of the seed (an entity is never given twice).
        /// </summary>
        internal bool AddCreatedHolder(string entity)
        {
            if (Holders.ContainsKey(entity) || Items.ContainsKey(entity)) return false;
            var holder = new SessionSeedHolder(entity, SessionHolderKind.Npc, false, false, [], []);
            Holders.Add(entity, holder);
            Source = Source with { Holders = Array.AsReadOnly([.. Source.Holders, holder]) };
            return true;
        }

        /// <summary>ARC-DEV-386: takes a created holder out again (its critter was destroyed); false for an entity that is no holder of this seed.</summary>
        internal bool RemoveCreatedHolder(string entity)
        {
            if (!Holders.Remove(entity)) return false;
            Source = Source with { Holders = Array.AsReadOnly(Source.Holders.Where(holder => holder.Entity != entity).ToArray()) };
            return true;
        }
    }

    internal Dictionary<SessionMapId, Seed> Seeds { get; } = [];
    /// <summary>ARC-SCRIPT-042: the item script slots runs changed, by item entity and attachment point.</summary>
    internal Dictionary<(string Entity, int Point), ScrSlot> ScriptSlots { get; } = [];

    internal SessionItemScriptSlot[] CaptureScriptSlots() => [.. ScriptSlots.OrderBy(pair => pair.Key.Entity, StringComparer.Ordinal).ThenBy(pair => pair.Key.Point)
        .Select(pair => new SessionItemScriptSlot(pair.Key.Entity, pair.Key.Point, pair.Value.Script, pair.Value.LocalFlags, pair.Value.Counters))];
    /// <summary>Moves on every seed attach; a seed is not state, so it moves no revision, but a transfer that called the host must
    /// see that the seeds it read are still the same.</summary>
    internal long SeedGeneration { get; set; }
    internal int Gold { get; set; }
    /// <summary>ARC-DEV-090 (MIG6d-2): the hero's kit, taken, made and placed items and the given records, as directory entries found by
    /// entity (<see cref="HeroLists"/>); the only owner of those lists.</summary>
    internal HeroLists Hero { get; } = new();
    internal IReadOnlyList<SessionKitItem> Kit => Hero.Kit;
    internal IReadOnlyList<SessionPcItem> Pc => Hero.Pc;
    internal IReadOnlyList<SessionMadeItem> Made => Hero.Made;
    internal HashSet<string> Taken { get; } = new(StringComparer.Ordinal);
    /// <summary>ARC-WORLD-055: the made items added to each NPC, by NPC entity; a list is never empty. ARC-WORLD-090: and to each
    /// container a barter window filled. ARC-DEV-088 (MIG6d): the lists are directory entries, an item's holder found by its entity.</summary>
    internal HolderLists Lists { get; } = new();

    /// <summary>ARC-WORLD-090: the items the session marked identified (item flag 0x1).</summary>
    internal SortedSet<string> Identified { get; } = new(StringComparer.Ordinal);

    /// <summary>ARC-SCRIPT-1279: the item fields scripts wrote (<see cref="SessionItemFieldWrites"/>), by entity; a row never holds an empty change.</summary>
    internal SortedDictionary<string, SessionItemFieldWrites> ItemFields { get; } = new(StringComparer.Ordinal);

    /// <summary>ARC-WORLD-094: the highest made number a regeneration discarded (<see cref="SessionInventoryState.LastDiscardedMade"/>).</summary>
    internal int LastDiscardedMade { get; set; }

    /// <summary>ARC-DEV-091 (MIG6d-3): the junk piles and pools of blood, as directory records found by entity (<see cref="ObjectRecords"/>);
    /// the only owner of those records and of their number marks.</summary>
    internal ObjectRecords Objects { get; } = new();

    /// <summary>ARC-WORLD-100: the generated-once containers (<see cref="SessionInventoryState.Generated"/>).</summary>
    internal HashSet<SessionGeneratedContainer> Generated { get; } = [];

    /// <summary>ARC-WORLD-103: the worn items of each NPC the session dressed, by NPC entity and slot (an empty record: it wears
    /// nothing). For such an NPC an item is worn only in the slot its record names, whatever its map data's inventory location.</summary>
    internal SortedDictionary<string, SortedDictionary<EquipmentSlot, string>> NpcWorn { get; } = new(StringComparer.Ordinal);

    /// <summary>ARC-RES-1410 (ledger XR16 (g), IW39): the slots the arrangement at the open of a loot, steal or See Contents window gave the items of a
    /// map holder's list (a container or an NPC of the map's seed), by holder entity and item entity (the arranged items only: the worn and quick-slot
    /// items stay out of it). The original writes them into the items' inventory locations (0x4670A0), which are saved; here the holder's list answers with
    /// them in place of the map data's, and they are saved with the inventory section (ARC-RES-1441, schema 95), so a restored session draws the same
    /// slots (the barter window, a list that no longer fits after an add). An entry is dropped when its item enters a list
    /// again (<see cref="ForgetSlot"/>). A junk pile the session made keeps its slots in its record instead.</summary>
    internal SortedDictionary<string, SortedDictionary<string, int>> HolderSlots { get; } = new(StringComparer.Ordinal);

    /// <summary>ARC-WORLD-103: an item that leaves an NPC's list (to the hero, the ground, deletion) is worn no more; an item that comes
    /// back to the list later is not worn either.</summary>
    internal void Unwear(string entity)
    {
        foreach (var worn in NpcWorn.Values)
            if (worn.FirstOrDefault(pair => pair.Value == entity) is { Value: not null } pair) { worn.Remove(pair.Key); return; }
    }

    /// <summary>ARC-WORLD-103: the inventory location of an item in an NPC's list: for an NPC the session dressed, the slot its record
    /// names, or -1 (in the pack) for an item its map data had worn; the map data's location otherwise.</summary>
    internal SessionItemFacts Worn(string holder, string entity, SessionItemFacts facts) =>
        !NpcWorn.TryGetValue(holder, out var worn) ? facts
            : worn.FirstOrDefault(pair => pair.Value == entity) is { Value: not null } pair ? facts with { InventoryLocation = (int)pair.Key }
            : facts.Worn ? facts with { InventoryLocation = -1 } : facts;

    /// <summary>The made items added to NPCs and containers (the bound of <see cref="GameSessionLimits.MaxNpcItems"/>).</summary>
    internal int NpcHeld => Lists.Count;

    /// <summary>The NPC (or container) that holds the added made item, or null.</summary>
    internal string? NpcOf(string entity) => Lists.HolderOf(entity);

    /// <summary>The made items added to an NPC, in order (empty when none).</summary>
    internal IReadOnlyList<SessionMadeItem> AddedTo(string npc) => Lists.Of(npc);

    internal void AddToNpc(string npc, SessionMadeItem item) { ForgetSlot(item.Entity); Lists.Add(npc, item, container: false); }

    /// <summary>ARC-RES-1410: an item that enters a list takes no slot the arrangement of an earlier list gave it.</summary>
    internal void ForgetSlot(string entity)
    {
        foreach (var holder in HolderSlots.Where(pair => pair.Value.Remove(entity) && pair.Value.Count == 0).Select(pair => pair.Key).ToArray()) HolderSlots.Remove(holder);
    }

    /// <summary>ARC-WORLD-090: an item at the end of a container's added list.</summary>
    internal void AddToContainer(string container, SessionMadeItem item) { ForgetSlot(item.Entity); Lists.Add(container, item, container: true); }

    /// <summary>Removes an added made item from whichever NPC holds it; an emptied list is dropped.</summary>
    internal bool RemoveFromNpc(string entity)
    {
        if (!Lists.Remove(entity)) return false;
        Unwear(entity);
        // ARC-AI-1698: the place the insert gave the item leaves with it, so the list's other items keep theirs and the save holds no slot of an item that is no longer there.
        ForgetSlot(entity);
        return true;
    }

    /// <summary>ARC-WORLD-059: the hero's items the session placed on the ground, in the order they were placed; each is also in
    /// <see cref="Taken"/>.</summary>
    internal IReadOnlyList<SessionPlacedItem> Placed => Hero.Placed;

    /// <summary>Items the PC owns: the kit, the taken and the made items, and (ARC-WORLD-059) those it placed on the ground, which
    /// can come back (the bound of <see cref="GameSessionLimits.MaxPcItems"/>).</summary>
    internal int Held => Hero.Held;

    /// <summary>ARC-WORLD-081: the items scripts created on the ground that lie there still.</summary>
    internal int Created => Hero.Created;

    /// <summary>The placed item with this entity, or null.</summary>
    internal SessionPlacedItem? PlacedItem(string entity) => Hero.PlacedItem(entity);

    /// <summary>Puts a placed item back into the list it came from: a kit item at its kit number's place (the kit stays in number
    /// order), a taken or made item at the end of its list.</summary>
    internal void Return(SessionPlacedItem item)
    {
        Hero.Remove(item.Entity);
        switch (item.Source)
        {
            case SessionPlacedSource.Kit:
                if (HeroDomain.KitNumber(item.Entity) is null) throw new InvalidOperationException("PLACED_KIT_ENTITY");
                Hero.InsertKit(new(item.Entity, item.Kind));
                break;
            case SessionPlacedSource.Pc: Hero.AddPc(new(item.Entity, item.Origin!.Value, item.Kind)); break;
            default: Hero.AddMade(new(item.Entity, item.Kind, item.Complexity)); break;
        }
    }

    /// <summary>ARC-WORLD-071: an item leaving an NPC for the hero: a given kit item back at its kit number's place, a given taken item at
    /// the end of the taken items with its origin, any other at the end of the made items with its complexity. It stays taken.</summary>
    internal void ReturnGiven(string entity, SessionItemKind kind, int complexity)
    {
        Unwear(entity);
        if (Hero.TakeGiven(entity) is not { } given) { Add(new SessionMadeItem(entity, kind, complexity)); return; }
        if (given.Origin is { } origin) { Hero.AddPc(new(entity, origin, kind)); return; }
        if (HeroDomain.KitNumber(entity) is null) throw new InvalidOperationException("GIVEN_KIT_ENTITY");
        Hero.InsertKit(new(entity, kind));
    }

    /// <summary>A made item is also recorded as taken, so no seed can list it.</summary>
    internal void Add(SessionMadeItem item)
    {
        Unwear(item.Entity);
        Hero.AddMade(item);
        Taken.Add(item.Entity);
    }

    internal void Add(SessionPcItem item)
    {
        Unwear(item.Entity);
        Hero.AddPc(item);
        Taken.Add(item.Entity);
    }

    /// <summary>A kit entity is also recorded as taken, so no seed can list it.</summary>
    internal void Add(SessionKitItem item)
    {
        Hero.AddKit(item);
        Taken.Add(item.Entity);
    }

    /// <summary>ARC-WORLD-066: the ammunition stacks the session tracks, by entity.</summary>
    internal SortedDictionary<string, SessionAmmoRecord> Ammo { get; } = new(StringComparer.Ordinal);

    /// <summary>ARC-WORLD-071: the gold piles whose coins the session tracks, by entity.</summary>
    internal SortedDictionary<string, int> Piles { get; } = new(StringComparer.Ordinal);

    /// <summary>ARC-WORLD-085: the weight facts of the objects the session tracks (kit, taken and made items wherever they are), by
    /// entity; an object without a row weighs 0.</summary>
    internal SortedDictionary<string, SessionItemWeight> Weights { get; } = new(StringComparer.Ordinal);

    /// <summary>Keeps an object's weight facts: a row only for a non-zero weight or adjustment.</summary>
    internal void SetWeight(string entity, SessionItemWeight facts)
    {
        if (facts.Weight != 0 || facts.MagicWeight != 0) Weights[entity] = facts;
        else Weights.Remove(entity);
    }

    internal SessionInventoryState Capture() => new(Gold, Kit.ToArray(), Pc.ToArray(), Made.ToArray(),
        Lists.Capture(),
        Placed.ToArray(), Ammo.Values.ToArray(), Hero.GivenAll.ToArray(), Piles.Select(pair => new SessionGoldRecord(pair.Key, pair.Value)).ToArray(),
        CaptureScriptSlots(), Weights.Select(pair => new SessionItemWeightRecord(pair.Key, pair.Value)).ToArray(), Identified.ToArray(),
        LastDiscardedMade, Grid.Values.OrderBy(item => item.Slot).ToArray(),
        Generated.OrderBy(row => row.Map.RuntimeMap).ThenBy(row => row.Entity, StringComparer.Ordinal).ToArray(),
        NpcWorn.Select(pair => new SessionNpcWear(pair.Key, Array.AsReadOnly(pair.Value.Select(worn => new SessionNpcWornItem(worn.Key, worn.Value)).ToArray()))).ToArray())
        {
            ItemFields = ItemFields.Select(pair => new SessionItemFieldRecord(pair.Key, pair.Value)).ToArray(),
            HolderSlots = HolderSlots.Where(pair => pair.Value.Count > 0)
                .Select(pair => new SessionHolderSlots(pair.Key, Array.AsReadOnly(pair.Value.Select(slot => new SessionHolderSlot(slot.Key, slot.Value)).ToArray()))).ToArray(),
        };

    /// <summary>ARC-UI-019: the hero's items in its inventory grid, by entity (a worn item and one without room have none).</summary>
    internal Dictionary<string, SessionHeroGridItem> Grid { get; } = new(StringComparer.Ordinal);

    /// <summary>The facts of a made item: its kind, no weight, location (-1, in the bag) or scripts; the host knows the prototype's
    /// weight. ARC-WORLD-083: the complexity the session keeps for it (0 when it keeps none).</summary>
    internal static SessionItemFacts MadeFacts(SessionItemKind kind, int complexity = 0) => new(kind, 0, complexity, -1, null, []);

    /// <summary>ARC-WORLD-083: an item's complexity in an NPC's added list: a given taken item's from its origin seed (0 while that seed
    /// is not attached), any other its own.</summary>
    internal int AddedComplexity(SessionMadeItem item) =>
        Hero.GivenOf(item.Entity) is { Origin: { } origin } ? SeedComplexity(origin, item.Entity) : item.Complexity;

    /// <summary>ARC-WORLD-083: a placed item's complexity: a taken item's from its origin seed (0 while that seed is not attached), a
    /// made item's its own, a kit item's 0.</summary>
    internal int PlacedComplexity(SessionPlacedItem item) => item.Origin is { } origin ? SeedComplexity(origin, item.Entity) : item.Complexity;

    /// <summary>ARC-WORLD-083: the facts of an item in an NPC's added list, with its complexity and tracked units.</summary>
    internal SessionItemFacts AddedFacts(SessionMadeItem item) => WithAmmo(item.Entity, MadeFacts(item.Kind, AddedComplexity(item)));

    private int SeedComplexity(SessionMapId map, string entity) =>
        Seeds.TryGetValue(map, out var seed) && seed.Items.TryGetValue(entity, out var row) ? row.Facts.Complexity : 0;

    /// <summary>ARC-WORLD-066: an item's facts with its tracked ammunition units (a made or placed stack's type and units, a seed
    /// stack's changed units); any other item's facts unchanged.</summary>
    internal SessionItemFacts WithAmmo(string entity, SessionItemFacts facts) =>
        Ammo.TryGetValue(entity, out var stack) ? facts with { AmmoType = stack.AmmoType, Quantity = stack.Units }
        // ARC-WORLD-071: a tracked gold pile's coins too.
        : Piles.TryGetValue(entity, out var coins) ? facts with { Quantity = coins } : facts;

    /// <summary>The map's current inventory; an NPC holder is dead when its seed says so or <paramref name="killed"/> (the session's
    /// life records, ARC-COMBAT-005) does. Ground items in seed order, then each spilled container's items at its cell (a container
    /// an attack broke holds nothing, ARC-COMBAT-008), then the NPC items a critical dropped at their cells (ARC-COMBAT-009,
    /// <paramref name="dropped"/>), which leave their holder's list, then the hero's items placed on this map (ARC-WORLD-059) in
    /// placing order.</summary>
    internal SessionMapInventory View(SessionMapId map, Seed seed, Func<string, bool> killed, Func<string, CellCoord?> spilled,
        Func<string, CellCoord?> dropped, Func<string, CellCoord?>? moved = null)
    {
        // ARC-WORLD-063: an item in a junk pile lies in the pile, not on the ground.
        var inPile = Objects.Piles.Where(pile => pile.Map == map).SelectMany(pile => pile.Items).Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        var inventory = WithPiled(map, seed, killed, spilled, dropped, moved);
        var ground = inventory.Ground.ToDictionary(item => item.Item.Entity, StringComparer.Ordinal);
        return new(map, inventory.Ground.Where(item => !inPile.Contains(item.Item.Entity)).ToArray(), inventory.Holders.ToArray(),
            Objects.Piles.Where(pile => pile.Map == map).Select(pile => new SessionJunkPileInventory(pile.Entity, pile.Cell,
                Array.AsReadOnly(pile.Items.Where(item => ground.TryGetValue(item.Entity, out var lying) && lying.Cell == pile.Cell)
                    .Select(item => ground[item.Entity].Item).ToArray()))).ToArray());
    }

    /// <summary>The map's inventory with every item on its ground, those in junk piles included (ARC-WORLD-063).</summary>
    internal SessionMapInventory WithPiled(SessionMapId map, Seed seed, Func<string, bool> killed, Func<string, CellCoord?> spilled,
        Func<string, CellCoord?> dropped, Func<string, CellCoord?>? moved = null) => new(map,
        // ARC-MAGIC-1230: a pushed item of the map's data lies on the cell the push left it on (the session's record), wherever its data puts it.
        seed.Source.Items.Where(item => item.Cell is not null && !Taken.Contains(item.Entity))
            .Select(item => new SessionGroundItem(new(item.Entity, WithAmmo(item.Entity, item.Facts)), moved?.Invoke(item.Entity) ?? item.Cell!.Value))
            // ARC-WORLD-1306: the items a barter window sold into a container leave its list at the break and lie on the ground as placed items
            // (Placed below); only a save made before that fix can still hold some in a spilled container's list, and they stay hidden.
            .Concat(seed.Source.Holders.Where(holder => holder.Kind == SessionHolderKind.Container).SelectMany(holder =>
                spilled(holder.Entity) is { } cell ? Current(seed, holder, dropped, added: false).Select(item => new SessionGroundItem(item, moved?.Invoke(item.Entity) ?? cell)) : []))
            .Concat(seed.Source.Holders.Where(holder => holder.Kind == SessionHolderKind.Npc).SelectMany(holder => holder.Items
                .Where(entity => !Taken.Contains(entity) && dropped(entity) is not null)
                .Select(entity => new SessionGroundItem(new(entity, WithAmmo(entity, seed.Items[entity].Facts)), moved?.Invoke(entity) ?? dropped(entity)!.Value))))
            .Concat(Placed.Where(item => item.Map == map).Select(item => new SessionGroundItem(new(item.Entity, WithAmmo(item.Entity, MadeFacts(item.Kind, PlacedComplexity(item)))), item.Cell)))
            .ToArray(),
        seed.Source.Holders.Select(holder => new SessionHolderInventory(holder.Entity, holder.Kind,
            holder.Dead || holder.Kind == SessionHolderKind.Npc && killed(holder.Entity),
            holder.Kind == SessionHolderKind.Container && spilled(holder.Entity) is not null ? Array.Empty<SessionInventoryItem>()
                : Array.AsReadOnly(Current(seed, holder, dropped).ToArray()))).ToArray());

    /// <summary>The holder's list in its order without the taken and deleted items and the items a critical dropped
    /// (<paramref name="dropped"/>); an NPC's list is followed by the made items the session added to it (ARC-WORLD-055), a container's
    /// by the items a barter window sold into it (ARC-WORLD-090) unless <paramref name="added"/> is false.</summary>
    internal IEnumerable<SessionInventoryItem> Current(Seed seed, SessionSeedHolder holder, Func<string, CellCoord?> dropped, bool added = true)
    {
        // ARC-WORLD-103: an NPC the session dressed wears what its record names.
        // ARC-RES-1410: an item the arrangement of the holder's list placed lies in the slot it gave.
        var slots = HolderSlots.GetValueOrDefault(holder.Entity);
        SessionInventoryItem Item(string entity, SessionItemFacts facts)
        {
            var shown = holder.Kind == SessionHolderKind.Npc ? Worn(holder.Entity, entity, facts) : facts;
            return new(entity, !shown.Worn && slots is not null && slots.TryGetValue(entity, out var slot) ? shown with { InventoryLocation = slot } : shown);
        }
        return holder.Items
            .Where(entity => !Taken.Contains(entity) && dropped(entity) is null).Select(entity => Item(entity, WithAmmo(entity, seed.Items[entity].Facts)))
            .Concat(added && (holder.Kind == SessionHolderKind.Npc) != Lists.IsContainer(holder.Entity)
                ? AddedTo(holder.Entity).Select(item => Item(item.Entity, AddedFacts(item)))
                : []);
    }

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
        return new(new(Array.AsReadOnly(holderOrder.ToArray()), Array.AsReadOnly(itemOrder.ToArray())) { Window = source.Window }, items, holders, holderOf);
    }

    // ARC-WORLD-066: an ammunition item carries its type in the original's range (a type out of it indexes past the original's tables,
    // crash class: refused in both settings); a negative count is refused too (the original lets it grow the source of a use).
    private static bool Facts(SessionItemFacts? facts) => facts?.ScriptPoints is not null && Enum.IsDefined(facts.Kind.Type) &&
        Points(facts.ScriptPoints) && (facts.Quantity is null) == (facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Ammo)) &&
        facts.Quantity is null or >= 0 && (facts.AmmoType is null) == (facts.Kind.Type != SessionItemType.Ammo) &&
        facts.AmmoType is null or >= 0 and < SessionAmmoRecord.AmmoTypes && (facts.KeyId is null || facts.Kind.Type == SessionItemType.Key) && facts.WeightFacts.IsValid &&
        // ARC-WORLD-063: a size in cells as the original's art read gives it (at least one cell a side, at most 2^27).
        facts.InventoryWidth is >= 1 and <= SessionItemFacts.MaxInventoryCells && facts.InventoryHeight is >= 1 and <= SessionItemFacts.MaxInventoryCells;

    private static bool Points(IReadOnlyList<int> points) =>
        points.All(point => point is >= 0 and <= SessionScriptPoint.Last) && points.Distinct().Count() == points.Count;

    /// <summary>Gold merges into the PC's gold and a key goes onto the PC's key ring when it has one; neither is modelled. Since
    /// ARC-WORLD-066 ammunition is admitted: it merges into a held stack of the same prototype (<see cref="SessionAmmoRecord"/>).</summary>
    internal static bool Admitted(SessionItemType type) => type is not (SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing);

    /// <summary>A starting kit or a save's kit holds no ammunition: a kit item carries no unit count (no pre-made hero starts with
    /// ammunition).</summary>
    internal static bool KitAdmitted(SessionItemType type) => Admitted(type) && type != SessionItemType.Ammo;
}
