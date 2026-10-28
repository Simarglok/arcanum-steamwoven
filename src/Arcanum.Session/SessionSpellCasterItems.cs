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

/// <summary>Whether the item use took its spell path (ARC-MAGIC-022): the host does not know the item as the hero's
/// (<see cref="Unknown"/>), the item has no charges and no magical flag (<see cref="NoMagic"/>: the use's other branches are the
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

    SessionItemTriggerResult Fire(uint eventMask, string item, string holder);

    SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder);
}

public sealed partial class SessionSpellCaster
{
    private readonly Func<string, SessionItemSpells?>? _items;

    /// <summary>An item the host's item lookup knows (ARC-MAGIC-020), or null; since ARC-MAGIC-022 with the charges the session left
    /// on it and, once it spent one, the magical flag (<see cref="GameSessionOwner.QueryItemCharges"/>).</summary>
    public SessionItemSpells? Item(string id)
    {
        if (_items?.Invoke(id) is not { } item || item.Entity != id) return null;
        return _owner.QueryItemCharges(id) is { } left ? item with { Charges = left, Flags = item.Flags | SessionItemSpells.MagicalFlag } : item;
    }

    /// <summary>
    /// The executor (0x4CB830, ARC-MAGIC-020) for <paramref name="item"/> and an event: the spells <see cref="ItemSpellTriggerRules.Fire"/>
    /// fires, each a run (0x455A20, 0x455AC0) with the item as source, its holder as parent, aimed at the holder, the event's
    /// <paramref name="other"/> object or <paramref name="tile"/>, with the event's mask as the run's triggers and without the
    /// casting goal (the invocation's flag 0x1: goal 73). Nothing for an item the host does not know or that
    /// <paramref name="holder"/> does not hold. The aims at an attacker's worn weapon or armour are not ready.
    /// </summary>
    public IReadOnlyList<SessionItemSpellStart> StartItemSpells(uint eventMask, string item, string holder, string? other = null,
        SpellLocation tile = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(holder);
        if (Item(item) is not { } spells || spells.Holder != holder) return [];
        return ItemSpellTriggerRules.Fire(spells.Spells, RecordTriggers, eventMask, Rolls)
            .Select(firing => StartItemSpell(firing, spells, eventMask, firing.Aim switch
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
            started.Add(StartItemSpell(firing, spells, eventMask, (holder, SpellLocation.None, false)));
        }
        return (stopped, started);
    }

    // The record's trigger mask (+0x38) of a slot's whole value (0x4CB760): 0 for a value outside the table, which the original reads
    // past it (no installed slot holds one).
    private uint RecordTriggers(int value) => value is >= 0 and < SpellPaymentRules.Spells ? _records(value)?.Data.ItemTriggers ?? 0 : 0;

    private SessionItemSpellStart StartItemSpell(ItemSpellFiring firing, SessionItemSpells item, uint eventMask, (string? Object, SpellLocation Tile, bool NotReady) aim)
    {
        if (aim.NotReady) return new(firing, SessionItemSpellOutcome.NotReady);
        // No target object and no tile: the executor calls no dispatcher.
        if (aim.Object is null && aim.Tile.IsNone) return new(firing, SessionItemSpellOutcome.NoTarget);
        if (_records(firing.Spell) is not { } record || Facts(item.Entity) is not { } source || Facts(item.Holder) is not { } parent)
            return new(firing, SessionItemSpellOutcome.Refused);
        var target = aim.Object is null ? null : Facts(aim.Object);
        if (aim.Object is not null && target is null) return new(firing, SessionItemSpellOutcome.NoTarget);
        var invocation = new SpellInvocation(firing.Spell, parent, source, target, target is null ? aim.Tile : SpellLocation.None, ItemPower(item, parent),
            ParentIsLocalPlayer: item.Holder == Hero);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation, SkipCastGoal: !firing.CastGoal, Triggers: unchecked((int)eventMask)),
            record.Run, InvocationWorld(), _owner.SpellRolls);
        return started.Disposition == GameSessionDisposition.Changed && started.Creation?.Run is { } run
            ? new(firing, SessionItemSpellOutcome.Started, run.Id, started)
            : new(firing, SessionItemSpellOutcome.Refused, null, started);
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
    /// (<see cref="GameSessionOwner.DestroyUsedItem"/>), then the source of every run whose parent or source is the item is cleared
    /// (the original clears first; nothing reads the runs in between). Nothing is cleared when the deletion is refused. False when
    /// nothing was deleted.
    /// </summary>
    public bool SpendUsedItem(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_owner.DestroyUsedItem(_owner.Version, item).Disposition != GameSessionDisposition.Changed) return false;
        Act(new(SessionSpellRunActionKind.ClearSources, Object: item));
        return true;
    }

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

    public SessionItemTriggerResult Fire(uint eventMask, string item, string holder) =>
        Played([], _caster.StartItemSpells(eventMask, item, holder));

    public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder)
    {
        var (stopped, spells) = _caster.WithdrawItemSpells(eventMask, item, holder);
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
    /// </summary>
    public SessionItemUseResult UseItem(string item, string? target)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_caster.Item(item) is not { Holder: SessionSpellCaster.Hero } spells) return new(SessionItemUseOutcome.Unknown);
        if (spells.Charges == 0 && !spells.Magical) return new(SessionItemUseOutcome.NoMagic);
        var (cast, attempt, goal) = CastSlotZero(spells, target);
        var used = Played([], _caster.StartItemSpells(ItemSpellTriggerRules.ItemUsed, item, SessionSpellCaster.Hero, target));
        var spent = spells.Type is SessionItemSpells.FoodType or SessionItemSpells.ScrollType && _caster.SpendUsedItem(item);
        return new(SessionItemUseOutcome.Used, cast, attempt, goal, used, spent);
    }

    // 0x4605E0's callback 0x57CE30: the slot-0 value (0x4CB7A0, −1 for none), a record without item triggers (0x45A030), the
    // activation for the item (0x57BC70), the cast at the use's target when it answered 0 (0x57C110), then the cancel (0x57BFF0).
    private (SessionItemUseCast, SpellCastAttempt?, SpellCastGoalStart?) CastSlotZero(SessionItemSpells item, string? target)
    {
        // An empty slot 0 reads the record before the table in the original (no installed item on the spell path has one): nothing.
        // The callback looks the value up unmasked (0x45A030), as the executor's trigger read does.
        if (item.Spells.Count == 0 || item.Spells[0] is var value && (value == ItemSpellTriggerRules.NoSpell || value is < 0 or >= SpellPaymentRules.Spells))
            return (SessionItemUseCast.NoSpell, null, null);
        var spell = value;
        if (_caster.SpellRecord(spell) is not { } record) return (SessionItemUseCast.NoSpell, null, null);
        if (record.Data.ItemTriggers != 0) return (SessionItemUseCast.Triggered, null, null);
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
