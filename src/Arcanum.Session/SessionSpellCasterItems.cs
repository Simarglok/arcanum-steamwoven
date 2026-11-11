using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// An item a critter holds as the item spell executors read it (ARC-MAGIC-020): its entity and holder (the parent, field 0x58),
/// object type, the five item spell fields (0x64..0x68, 10000 none), the spell charges (0x69; negative unlimited, 0 none) and the
/// magick/tech complexity (the charge-cell need of an item source). The host builds it from the item's data. Since ARC-MAGIC-022 also
/// the item flags (field 0x57: 0x4 magical, 0x100 needs a target); the cast host reads the charges and the magical flag through the
/// session's spent charges (<see cref="GameSessionOwner.QueryItemCharges"/>).
/// </summary>
public sealed record SessionItemSpells(string Entity, string Holder, int Type, IReadOnlyList<int> Spells, int Charges, int Complexity, uint Flags = 0)
{
    private const int NoSpell = 10000;

    /// <summary>The magical item flag (0x4) the payment sets when it spends a charge.</summary>
    public const uint MagicalFlag = 0x4;

    /// <summary>Whether any of the five slots holds a spell.</summary>
    public bool HasSpells => Spells.Any(spell => spell != NoSpell);

    /// <summary>Whether the item carries the magical flag (0x4).</summary>
    public bool Magical => (Flags & MagicalFlag) != 0;

    /// <summary>The item types the use spends after its spell path (food 9, scroll 10).</summary>
    public const int FoodType = 9, ScrollType = 10;

    /// <summary>The facts of an item from its stored fields; null for an object that is not an item.</summary>
    public static SessionItemSpells? FromCombatFields(string entity, string holder, Arcanum.Data.Combat.CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.Item is { } item
            ? new(entity, holder, fields.Object.Type, Array.AsReadOnly(item.Spells.ToArray()), item.SpellManaStore, item.MagickTechComplexity, item.Flags)
            : null;
    }
}

/// <summary>ARC-UI-016: an item's spell pane (<see cref="SessionSpellCaster.ItemPane"/>): the item, whether the bar's weapon button
/// shows for it, the pane's buttons and its gauge.</summary>
public sealed record SessionItemSpellPane(string Item, bool WeaponButton, IReadOnlyList<ItemSpellButton> Buttons, ItemSpellGauge Gauge);

/// <summary>What became of one item spell an event fired (<see cref="SessionSpellCaster.StartItemSpells"/>): its run started, its
/// aim found no target (no run, as in the original), its aim needs what this slice does not model (an attacker's worn items), or the
/// run start was refused (<see cref="SessionItemSpellStart.Result"/>).</summary>
public enum SessionItemSpellOutcome { Started, NoTarget, NotReady, Refused }

/// <summary>One item spell an event fired, its outcome, the run's slot and the run start's answer.</summary>
public sealed record SessionItemSpellStart(ItemSpellFiring Firing, SessionItemSpellOutcome Outcome, int? Run = null, SessionSpellRunResult? Result = null);

/// <summary>What an item event did through its host: the runs the withdrawal stopped (0x4573D0), the spells it fired, and for each
/// started run its casting goal's start (null when the goal was not played).</summary>
public sealed record SessionItemTriggerResult(IReadOnlyList<int> Stopped, IReadOnlyList<SessionItemSpellStart> Spells,
    IReadOnlyList<SpellCastGoalStart?> Goals)
{
    public static SessionItemTriggerResult None { get; } = new([], [], []);
}

/// <summary>What the slot-0 cast of an item use did (ARC-MAGIC-022, 0x4605E0 → the activation callback 0x57CE30): nothing for an
/// empty slot 0 (<see cref="NoSpell"/>) or a record with item triggers (<see cref="Triggered"/>: its <c>Item_Used</c> fires instead);
/// the activation did nothing (<see cref="Nothing"/>: the holder cannot act or the spell is disabled); a spell aimed at an inventory
/// item is not ready (<see cref="InventoryTarget"/>); otherwise the cast at the holder's tile (<see cref="AtCaster"/>) or at the use's
/// target (<see cref="AtTarget"/>).</summary>
public enum SessionItemUseCast { NoSpell, Triggered, Nothing, InventoryTarget, AtCaster, AtTarget }

/// <summary>Whether the item use took its spell path (ARC-MAGIC-022): the host does not know the item (<see cref="Unknown"/>; before
/// ARC-WORLD-108 also any item that was not the hero's), the item has no charges and no magical flag (<see cref="NoMagic"/>: the use's other branches are the
/// host's), or it did (<see cref="Used"/>).</summary>
public enum SessionItemUseOutcome { Unknown, NoMagic, Used }

/// <summary>An item use's spell path: the outcome, the slot-0 cast with its attempt and its casting goal's start, the item used
/// executor's spells (<c>Item_Used</c>) and whether a food or scroll was spent.</summary>
public sealed record SessionItemUseResult(SessionItemUseOutcome Outcome, SessionItemUseCast Cast = SessionItemUseCast.NoSpell,
    SpellCastAttempt? Attempt = null, SpellCastGoalStart? Goal = null, SessionItemTriggerResult? ItemUsed = null, bool Spent = false);

/// <summary>
/// ARC-MAGIC-020: the item spell executors a session move calls (the wear and unwear of <see cref="GameSessionOwner.Equip"/> and
/// <see cref="GameSessionOwner.Unequip"/>): whether an item's spells can run here, an event's firing (0x4CB830) and an unwear's or
/// drop's withdrawal (0x4CBFF0). The host plays the casting goals of the runs they start.
/// </summary>
public interface ISessionItemTriggers
{
    /// <summary>Whether <paramref name="item"/>'s spells can run through this host for the hero (the host knows it as the hero's; since
    /// ARC-MAGIC-022 the session saves the charges its spells spend). The host must cast in the same session as the move that asks.</summary>
    bool Supports(string item);

    /// <summary>The executor for one item of <paramref name="holder"/>; <paramref name="other"/> is the event's other object (since
    /// ARC-MAGIC-023: an attack's other party), the aim of <c>Target_Attacker</c>. ARC-MAGIC-025: <paramref name="tile"/> is the aim
    /// of <c>Parent_Atks_Location</c> (0x4CBDB0's tile) and <paramref name="runOther"/> the attacker global 0x5FF618 the executor
    /// copies into its runs' other object (IC6, IC10); none for the other events.</summary>
    SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null, SpellLocation tile = default, string? runOther = null);

    SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder);

    /// <summary>ARC-MAGIC-022: the spell steps of the inventory removal (0x467860, IU9) of <paramref name="item"/>, which has just left
    /// <paramref name="holder"/>'s inventory: its runs as source end (0x457450), then the drop's withdrawal (0x4CC130) with the holder
    /// as parent.</summary>
    SessionItemTriggerResult Remove(string item, string holder);
}

public sealed partial class SessionSpellCaster
{
    private readonly Func<string, SessionItemSpells?>? _items;

    /// <summary>An item the host's item lookup knows (ARC-MAGIC-020), or null; since ARC-MAGIC-022 with the charges the session left
    /// on it and, once it spent one, the magical flag (<see cref="GameSessionOwner.QueryItemCharges"/>).</summary>
    public SessionItemSpells? Item(string id)
    {
        if (_items?.Invoke(id) is not { } item || item.Entity != id) return null;
        if (_heldBy is { } held && held.Item == id) item = item with { Holder = held.Holder };
        // ARC-MAGIC-032: the session's identification mark (a barter window's, Divine Magick's) counts as the item flag 0x1 the pane reads (0x464370, 0x5506C0).
        if (SessionValidation.Entity(id) && _owner.QueryItemIdentified(id)) item = item with { Flags = item.Flags | ItemSpellPaneRules.IdentifiedFlag };
        return _owner.QueryItemCharges(id) is { } left ? item with { Charges = left, Flags = item.Flags | SessionItemSpells.MagicalFlag } : item;
    }

    /// <summary>
    /// ARC-UI-016: the item spell pane for an item the hero holds (interface bar mode 8, ledger IP rows): whether the bar's weapon
    /// button shows for it (<see cref="ItemSpellPaneRules.WeaponButtonShown"/>, read for the wielded weapon), the pane's buttons
    /// (<see cref="ItemSpellPaneRules.PaneButtons"/>, technological and item-trigger tests from the spell records) and its gauge, from
    /// the item's current charges and flags (<see cref="Item"/>). Null for an item the host does not know as the hero's.
    /// </summary>
    public SessionItemSpellPane? ItemPane(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Item(item) is not { Holder: Hero } spells) return null;
        return new(item, ItemSpellPaneRules.WeaponButtonShown(spells.Charges, spells.Flags, spells.Complexity),
            ItemSpellPaneRules.PaneButtons(spells.Spells, spells.Flags, spells.Complexity, spell => SpellRecord(spell)?.Data.IsTechnology,
                ItemTriggered), ItemSpellPaneRules.Gauge(spells.Charges, spells.Flags));
    }

    /// <summary>Whether a spell's record has item triggers (0x45A030), null for a value outside the record table.</summary>
    public bool? ItemTriggered(int spell) => SpellRecord(spell) is { } record ? record.Data.ItemTriggers != 0 : null;

    /// <summary>
    /// The executor (0x4CB830, ARC-MAGIC-020) for <paramref name="item"/> and an event: the spells <see cref="ItemSpellTriggerRules.Fire"/>
    /// fires, each a run (0x455A20, 0x455AC0) with the item as source, its holder as parent, aimed at the holder, the event's
    /// <paramref name="other"/> object or <paramref name="tile"/>, with the event's mask as the run's triggers and without the
    /// casting goal (the invocation's flag 0x1: goal 73). Nothing for an item the host does not know or that
    /// <paramref name="holder"/> does not hold. The aims at an attacker's worn weapon or armour are not ready.
    /// ARC-MAGIC-025: the run's parent is the item's (0x455A20 → 0x450A50): its holder in an inventory, else the item itself, which the
    /// item lookup reports as its own holder (a thrown item on the map); such an item fires for any <paramref name="holder"/>, the
    /// executor's parent, which only the parent aim reads. <paramref name="runOther"/> (the attacker global) is each run's other
    /// object (+0xA0): the damage source its kill credits (DH1) and the other party of its target areas. MG46: a run started while the
    /// host fires an item event records that event's chain (<see cref="FireItemEvents"/>).
    /// </summary>
    public IReadOnlyList<SessionItemSpellStart> StartItemSpells(uint eventMask, string item, string holder, string? other = null,
        SpellLocation tile = default, string? runOther = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holder);
        if (Item(item) is not { } spells || spells.Holder != holder && spells.Holder != item) return [];
        return ItemSpellTriggerRules.Fire(spells.Spells, RecordTriggers, eventMask, Rolls)
            .Select(firing => StartItemSpell(firing, spells, eventMask, runOther, firing.Aim switch
            {
                ItemSpellAim.Parent => (holder, SpellLocation.None, false),
                ItemSpellAim.Other => (other, SpellLocation.None, false),
                ItemSpellAim.Location => (null, tile, false),
                ItemSpellAim.None => (null, SpellLocation.None, false),
                _ => (null, SpellLocation.None, true),
            })).ToArray();
    }

    /// <summary>
    /// The unwear and drop executor (0x4CBFF0, ARC-MAGIC-020) for <paramref name="item"/>: for each spell
    /// <see cref="ItemSpellTriggerRules.Withdraw"/> lists, the first run (slot order) whose parent is the item's holder, whose target
    /// object is the holder and whose spell is the slot's is interrupted (0x4573D0, which compares no source), then the spell runs
    /// again at the holder with the event's mask and the casting goal. Nothing for an item the host does not know or that
    /// <paramref name="holder"/> does not hold.
    /// </summary>
    public (IReadOnlyList<int> Stopped, IReadOnlyList<SessionItemSpellStart> Spells) WithdrawItemSpells(uint eventMask, string item, string holder)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holder);
        if (Item(item) is not { } spells || spells.Holder != holder) return ([], []);
        var stopped = new List<int>();
        var started = new List<SessionItemSpellStart>();
        foreach (var firing in ItemSpellTriggerRules.Withdraw(spells.Spells, RecordTriggers, eventMask))
        {
            if (_owner.QuerySpellRuns().Runs.FirstOrDefault(r => r.Parent?.Id == holder && r.Target?.Id == holder && r.Spell == firing.Spell) is { } run
                && Act(new(SessionSpellRunActionKind.Interrupt, run.Id)).Disposition != GameSessionDisposition.Rejected)
                stopped.Add(run.Id);
            started.Add(StartItemSpell(firing, spells, eventMask, null, (holder, SpellLocation.None, false)));
        }
        return (stopped, started);
    }

    /// <summary>
    /// ARC-MAGIC-022: the spell steps of the inventory removal (0x467860, IU9) of <paramref name="item"/> from
    /// <paramref name="holder"/>, called after the session's move: every run whose source is the item ends by event unless its record
    /// is technological or has item triggers without a maintenance period (0x457450, <see cref="SessionSpellRunActionKind.Demaintain"/>);
    /// then the drop's withdrawal (0x4CC130, <see cref="WithdrawItemSpells"/> with <see cref="ItemSpellTriggerRules.Drop"/>) with the
    /// holder passed, as the original passes it and leaves the item's parent field unwritten: the item counts as the holder's while it
    /// runs (<see cref="HeldAs"/>), so the lookup need only still know it. The runs the first step ends come first in the stopped list
    /// (none when the session refused the step), each once.
    /// </summary>
    public (IReadOnlyList<int> Stopped, IReadOnlyList<SessionItemSpellStart> Spells) RemoveItemSpells(string item, string holder)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holder);
        // The runs 0x457450 ends (by event: an interrupt event, or freed before their BEGIN), as SpellRunSystem.Demaintain picks them.
        var ended = _owner.QuerySpellRuns().Runs.Where(run => run.Source?.Id == item && Record(run.Spell) is var r && !r.Technology &&
            (!r.ItemTriggers || r.MaintainPeriod > 0)).Select(run => run.Id).ToList();
        using var held = HeldAs(item, holder);
        if (Act(new(SessionSpellRunActionKind.Demaintain, Object: item)).Disposition == GameSessionDisposition.Rejected) ended.Clear();
        var (stopped, spells) = WithdrawItemSpells(ItemSpellTriggerRules.Drop, item, holder);
        return ([.. ended.Concat(stopped).Distinct()], spells);
    }

    /// <summary>ARC-MAGIC-022: until the scope ends, <paramref name="item"/> counts as <paramref name="holder"/>'s for this host (the item
    /// lookup's holder is replaced), as an item the removal withdraws keeps its parent field (IU9). Scopes nest; each restores the one
    /// before it.</summary>
    public IDisposable HeldAs(string item, string holder)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holder);
        var previous = _heldBy;
        _heldBy = (item, holder);
        return new HeldScope(this, previous);
    }

    private sealed class HeldScope(SessionSpellCaster caster, (string Item, string Holder)? previous) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            caster._heldBy = previous;
        }
    }

    // ARC-MAGIC-022: an item a removal is withdrawing counts as its former holder's (IU9: the parent field is not rewritten).
    private (string Item, string Holder)? _heldBy;

    // The record's trigger mask (+0x38) of a slot's whole value (0x4CB760): 0 for a value outside the table, which the original reads
    // past it (no installed slot holds one).
    private uint RecordTriggers(int value) => value is >= 0 and < SpellPaymentRules.Spells ? _records(value)?.Data.ItemTriggers ?? 0 : 0;

    private SessionItemSpellStart StartItemSpell(ItemSpellFiring firing, SessionItemSpells item, uint eventMask, string? runOther,
        (string? Object, SpellLocation Tile, bool NotReady) aim)
    {
        if (aim.NotReady) return new(firing, SessionItemSpellOutcome.NotReady);
        // No target object and no tile: the executor calls no dispatcher.
        if (aim.Object is null && aim.Tile.IsNone) return new(firing, SessionItemSpellOutcome.NoTarget);
        if (_records(firing.Spell) is not { } record || Facts(item.Entity) is not { } source || Facts(item.Holder) is not { } parent)
            return new(firing, SessionItemSpellOutcome.Refused);
        var target = aim.Object is null ? null : Facts(aim.Object);
        if (aim.Object is not null && target is null) return new(firing, SessionItemSpellOutcome.NoTarget);
        var invocation = new SpellInvocation(firing.Spell, parent, source, target, target is null ? aim.Tile : SpellLocation.None, ItemPower(item, parent),
            TeleportAllowed: TeleportAllowedFor(firing.Spell, item.Holder), ParentIsLocalPlayer: item.Holder == Hero) { Other = runOther is null ? null : Facts(runOther) };
        var started = _owner.StartSpellRun(_owner.Version, new(invocation, SkipCastGoal: !firing.CastGoal, Triggers: unchecked((int)eventMask)),
            record.Run, InvocationWorld(), _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run)
        {
            // ARC-MAGIC-038 (0x4564E0): the anti-teleport refusal shows message 10000 whenever the parent is the local player.
            if (started.Creation?.Check is { ShowsTeleportMessage: true }) _log.Add(new(SpellCastEventKind.Message, -1, TeleportRefusedMessage));
            return new(firing, SessionItemSpellOutcome.Refused, null, started);
        }
        Chained(run.Id, _firingChain);
        return new(firing, SessionItemSpellOutcome.Started, run.Id, started);
    }

    // The power a critter parent draws from an item source (0x4614A0, the equipment rules); another parent takes the complexity.
    private static int ItemPower(SessionItemSpells item, SpellObjectFacts parent) => parent.Type is SpellObjectFacts.PcType or SpellObjectFacts.NpcType
        ? EquipmentRules.EffectivePower(Math.Clamp(item.Complexity, -EquipmentLimits.MaxMagnitude, EquipmentLimits.MaxMagnitude),
            Math.Clamp(parent.Aptitude, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude)) : item.Complexity;

    /// <summary>
    /// ARC-MAGIC-022: whether the item use stops at a scroll the hero cannot read (0x462CC0's first test): a scroll (type 10) whose
    /// slot-0 spell needs more intelligence (0x4B1750: the constant <see cref="SpellBookRules.MinimumIntelligence"/> for every
    /// spell) than the hero has (stat 4). The original tells a PC message 13 (the host's).
    /// </summary>
    public bool ScrollTooHard(string item) => Item(item) is { Type: SessionItemSpells.ScrollType, Holder: Hero } &&
        (HeroStat(CharacterStat.Intelligence) ?? 0) < SpellBookRules.MinimumIntelligence;

    /// <summary>
    /// ARC-MAGIC-022: the use's spending of a used food or scroll (0x4574D0, then the destruction 0x43CCA0): the session deletes it
    /// (<see cref="GameSessionOwner.DestroyUsedItem"/> for the hero's; ARC-WORLD-108: <see cref="GameSessionOwner.RemoveItemFromNpc"/>
    /// for an NPC's, refused for food or a scroll it wears), then the source of every run whose parent or source is the item is cleared
    /// (the original clears first; nothing reads the runs in between). Nothing is cleared when the deletion is refused. False when
    /// nothing was deleted, also for an item the host does not know or that is not food or a scroll.
    /// </summary>
    public bool SpendUsedItem(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Item(item) is not { Type: SessionItemSpells.FoodType or SessionItemSpells.ScrollType } spells || !DestroyHeldItem(spells)) return false;
        Act(new(SessionSpellRunActionKind.ClearSources, Object: item));
        return true;
    }

    // The destruction of a used food or scroll (0x43CCA0) out of its holder's lists: the hero's, or an NPC's (ARC-WORLD-108).
    private bool DestroyHeldItem(SessionItemSpells item) => (item.Holder == Hero
        ? _owner.DestroyUsedItem(_owner.Version, item.Entity).Disposition
        : _owner.RemoveItemFromNpc(_owner.Version, item.Holder, item.Entity).Disposition) == GameSessionDisposition.Changed;

    // The map with the items a critter of it holds (ARC-MAGIC-020): an item is an object in its holder's inventory, at its holder's
    // location (the inventory flag 0x1000, owner the holder). Everything else is the map's.
    private sealed class ItemCastMap(ISpellCastMap map, Func<string, SessionItemSpells?> items) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) =>
            map.Object(id) ?? (items(id) is { } item && item.Entity == id && map.Object(item.Holder) is { } holder
                ? new SpellTargetObject(id, item.Type, holder.Location, SpellTargetObject.Inventory, Owner: item.Holder) : null);

        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => map.ObjectsInSquare(centre, radius, typeMask);
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => map.ObjectsAt(tile, typeMask);
        public bool TileBlocked(SpellLocation tile) => map.TileBlocked(tile);
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => map.PathExists(source, tile);
        public int SectorKind(SpellLocation location) => map.SectorKind(location);
        public int Direction(SpellLocation from, SpellLocation to) => map.Direction(from, to);
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => map.Party(source);
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => map.LineOfFire(parent, tile);
        public SpellCastCritter? Critter(string entity) => map.Critter(entity);
        public CombatWeapon WieldedWeapon(string entity) => map.WieldedWeapon(entity);
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => map.WornItems(entity);
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => map.ObjectCombat(id);
        public bool DamageObject(string id, AttackDamage damage) => map.DamageObject(id, damage);
    }
}

/// <summary>
/// ARC-MAGIC-020: the item spell executors of one cast host with its combat world (<see cref="ISessionItemTriggers"/>). The casting
/// goals of the runs they start play at once — no eye-candy art is drawn, so each release comes on the goal's start — through the
/// host's goal (<see cref="SessionSpellCaster.StartGoal"/>, its action points in a turn-based round, then
/// <see cref="SessionSpellCaster.Release"/>). A fired run's goal 73 has no end node: when its start refuses, the original keeps the
/// run in its slot, never begun (IT10); both rule modes free it unpaid (owner decision 2026-10-28: a slot leak, fixed without a
/// switch). A withdrawal's recast plays the casting goal with its end
/// (<see cref="SessionSpellCaster.EndGoal"/>).
/// </summary>
public sealed class SessionItemTriggers(SessionSpellCaster caster, ISessionCombatWorld combat) : ISessionItemTriggers
{
    private readonly SessionSpellCaster _caster = caster ?? throw new ArgumentNullException(nameof(caster));
    private readonly ISessionCombatWorld _combat = combat ?? throw new ArgumentNullException(nameof(combat));

    public bool Supports(string item) => _caster.Item(item) is { Holder: SessionSpellCaster.Hero };

    public SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null, SpellLocation tile = default,
        string? runOther = null) =>
        Played([], _caster.StartItemSpells(eventMask, item, holder, other, tile, runOther));

    public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder)
    {
        var (stopped, spells) = _caster.WithdrawItemSpells(eventMask, item, holder);
        return Played(stopped, spells);
    }

    public SessionItemTriggerResult Remove(string item, string holder)
    {
        // The recast's goal plays while the item still counts as the holder's.
        using var held = _caster.HeldAs(item, holder);
        var (stopped, spells) = _caster.RemoveItemSpells(item, holder);
        return Played(stopped, spells);
    }

    /// <summary>
    /// ARC-MAGIC-022: the spell path of the hero's use of <paramref name="item"/> on <paramref name="target"/> (0x462CC0, single
    /// player). The host runs the use's earlier steps first: the scroll test (<see cref="SessionSpellCaster.ScrollTooHard"/>), the
    /// targeting cursor of an item that needs a target and has none, a trap device's own use, and the use script, which may stop
    /// the use. Taken when the item's charges are not 0 or it has the magical flag; the use charges no action points (each casting
    /// goal charges its own 4 in a turn-based round, <see cref="SessionSpellCaster.StartGoal"/>). In order:
    /// the slot-0 cast (0x4605E0 → 0x57CE30): nothing for an empty slot or a record with item triggers; otherwise the activation
    /// (0x57BC70, <see cref="SpellUiRules.Activate"/> with the holder as caster, no Shift or Alt) casts at the holder's tile, or,
    /// when it opens the cursor or its action points refuse, the cast goes to the use's target (0x57C110,
    /// <see cref="SessionSpellCaster.CastAt"/> with the item as source); its casting goal plays at once with its end. Then the item
    /// used executor (0x4CBF70: <see cref="ItemSpellTriggerRules.ItemUsed"/>, the target as the other object) and, for food or a
    /// scroll, the spend (<see cref="SessionSpellCaster.SpendUsedItem"/>).
    /// ARC-WORLD-108: an NPC's use of an item it holds (the AI's item-use goal 39 and the chooser's use at once) takes the same path
    /// with the NPC as holder; the activation casts nothing for a caster that is not a PC (0x40DA20 in 0x57BC70, SU1), so its slot-0
    /// spell is never cast (<see cref="SessionItemUseCast.Nothing"/>) and only the item used executor's spells and the spend follow.
    /// The callback's cancel (0x57BFF0) still returns the player's interface to its normal mode, closing an open targeting cursor
    /// (NU3): the host's.
    /// </summary>
    public SessionItemUseResult UseItem(string item, string? target)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_caster.Item(item) is not { } spells) return new(SessionItemUseOutcome.Unknown);
        if (spells.Charges == 0 && !spells.Magical) return new(SessionItemUseOutcome.NoMagic);
        var (cast, attempt, goal) = CastSlotZero(spells, target);
        var used = Played([], _caster.StartItemSpells(ItemSpellTriggerRules.ItemUsed, item, spells.Holder, target));
        var spent = spells.Type is SessionItemSpells.FoodType or SessionItemSpells.ScrollType && _caster.SpendUsedItem(item);
        return new(SessionItemUseOutcome.Used, cast, attempt, goal, used, spent);
    }

    // 0x4605E0's callback 0x57CE30: the slot-0 value (0x4CB7A0, −1 for none), a record without item triggers (0x45A030), the
    // activation for the item (0x57BC70), the cast at the use's target when it answered 0 (0x57C110), then the cancel (0x57BFF0).
    [OriginalFunction(0x57CE30, OriginalFunctionScope.Partial)]
    private (SessionItemUseCast, SpellCastAttempt?, SpellCastGoalStart?) CastSlotZero(SessionItemSpells item, string? target)
    {
        // An empty slot 0 reads the record before the table in the original (no installed item on the spell path has one): nothing.
        // The callback looks the value up unmasked (0x45A030), as the executor's trigger read does.
        if (item.Spells.Count == 0 || item.Spells[0] is var value && (value == ItemSpellTriggerRules.NoSpell || value is < 0 or >= SpellPaymentRules.Spells))
            return (SessionItemUseCast.NoSpell, null, null);
        var spell = value;
        if (_caster.SpellRecord(spell) is not { } record) return (SessionItemUseCast.NoSpell, null, null);
        if (record.Data.ItemTriggers != 0) return (SessionItemUseCast.Triggered, null, null);
        // ARC-WORLD-108: the activation's caster is the item's holder, and nothing happens for one that is not a PC (NU2).
        if (item.Holder != SessionSpellCaster.Hero) return (SessionItemUseCast.Nothing, null, null);
        var begin = record.Areas.Length > 0 ? record.Areas[0].Targets : 0;
        var life = _caster.HeroLife;
        var active = life is { IsDead: false, IsUnconscious: false };
        // The activation's early refusals come before its inventory branch (IU4).
        if (active && record.Data.Enabled && SpellUiRules.TargetsInventory(begin)) return (SessionItemUseCast.InventoryTarget, null, null);
        var activation = SpellUiRules.Activate(active, record.Data.Enabled, begin, record.Resist.Aggressive,
            altHeld: false, shiftHeld: false, actionPointsAllow: true);
        SessionItemUseCast cast;
        SpellCastAttempt attempt;
        switch (activation.Kind)
        {
            case SpellActivationKind.Nothing: return (SessionItemUseCast.Nothing, null, null);
            case SpellActivationKind.CastAtCaster:
                cast = SessionItemUseCast.AtCaster;
                attempt = _caster.CastAt(SessionSpellCaster.Hero, spell, null, _caster.Facts(SessionSpellCaster.Hero)?.Location ?? SpellLocation.None, item.Entity);
                break;
            default:
                cast = SessionItemUseCast.AtTarget;
                attempt = _caster.CastAt(SessionSpellCaster.Hero, spell, target, SpellLocation.None, item.Entity);
                break;
        }
        return (cast, attempt, attempt.RunId is { } run ? Play(run, castGoal: true) : null);
    }

    private SessionItemTriggerResult Played(IReadOnlyList<int> stopped, IReadOnlyList<SessionItemSpellStart> spells) =>
        new(stopped, spells, spells.Select(spell => spell.Run is { } run ? Play(run, spell.Firing.CastGoal) : null).ToArray());

    private SpellCastGoalStart Play(int run, bool castGoal)
    {
        var start = _caster.StartGoal(run, _combat);
        if (start.Outcome == SpellCastGoalOutcome.Started) _caster.Release(run);
        if (castGoal) _caster.EndGoal(run, refused: start.Outcome != SpellCastGoalOutcome.Started);
        else if (start.Outcome != SpellCastGoalOutcome.Started) _caster.Act(new(SessionSpellRunActionKind.Free, run));
        return start;
    }
}
