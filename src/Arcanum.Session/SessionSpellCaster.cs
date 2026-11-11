using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// An NPC on the shown map as a cast reads it (ARC-MAGIC-014): its combat values (life target, facts, combatant, stats; ARC-COMBAT-005)
/// and the stored fields the magic rules read besides — its stored magic resistance (resistance 4, no armour or effects: O9) and its
/// object, spell and critter flags. ARC-WORLD-102: <see cref="Book"/> is the spell book its fields store (college ranks and mastery,
/// <see cref="SpellBookRules.FromFields"/>), which a session record of the NPC (<see cref="GameSessionOwner.QueryNpcSpellBook"/>) replaces.
/// </summary>
public sealed record SpellCastCritter(SessionCritterCombat Combat, int StoredMagicResistance, int ObjectFlags, int SpellFlags, int CritterFlags,
    int CritterFlags2)
{
    public SpellBook? Book { get; init; }
}

/// <summary>
/// The map a cast happens on (ARC-MAGIC-014), for any map: the target functions' questions (<see cref="ISpellTargetWorld"/>, MT8; the
/// hero is the object <see cref="SessionSpellCaster.Hero"/>), the line of fire from a parent to a tile (the I2 port, 0x4ADE00), the NPCs'
/// combat values and stored fields, the weapon an NPC wields, the items a critter wears (the acid) and an object's damage step (the
/// host's object rules; an object that cannot take damage answers null).
/// </summary>
public interface ISpellCastMap : ISpellTargetWorld
{
    SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile);

    SpellCastCritter? Critter(string entity);

    CombatWeapon WieldedWeapon(string entity);

    IReadOnlyList<SpellAcidItem> WornItems(string entity);

    /// <summary>A non-critter object's combat values and kind for the damage step, or null when it takes none.</summary>
    (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id);

    /// <summary>Applies a step's damage to a non-critter object (its object rules); false when that is not ready.</summary>
    bool DamageObject(string id, AttackDamage damage);

    /// <summary>ARC-MAGIC-037 (MA-6): a portal's or container's lock facts for the ObjFlag component's lock fields and the Dispel component's held bit, or null when the object is none
    /// (or the host has no lock domain): the session's object, its T0 baseline and its lock level.</summary>
    SpellOpenable? Openable(string id) => null;
}

/// <summary>ARC-MAGIC-037 (MA-6): a portal or container a spell reaches: its session identity, its T0 lock baseline (the session's state is read over it) and its lock level (portal field 47, container field 57),
/// which the unlock gate (0x4551C0) compares with the caster's aptitude.</summary>
public sealed record SpellOpenable(SessionOpenable Openable, SessionOpenableBaseline Baseline, int LockLevel);

/// <summary>What a cast left for the host to show or do: the run note's kind and run, the local player's spell.mes message, the
/// component types that are not ready yet, and the objects a component touched.</summary>
/// <summary>One entry of the cast host's log; <see cref="Detail"/> carries an eye candy's <c>Play:</c> flags (ARC-MAGIC-017).</summary>
public sealed record SpellCastEvent(SpellCastEventKind Kind, int Run, int Value = 0, string? Object = null, int Detail = 0);

/// <summary>How a casting goal's start went (<see cref="SessionSpellCaster.StartGoal"/>): it started, or it ends because the parent's
/// action points refused, because the source cannot afford the cast, because the target is destroyed or off, because the caster
/// cannot turn to its target (ARC-MAGIC-018: stoned, stunned or paralysed), or because there is no such run.</summary>
public enum SpellCastGoalOutcome { Started, NoActionPoints, CannotAfford, TargetGone, CannotTurn, NoRun }

/// <summary>A casting goal's start and its action-point charge (null when the parent is not a combatant). ARC-MAGIC-018:
/// <see cref="Facing"/> is the facing turn's answer for a started goal (null when the run has no parent).</summary>
public sealed record SpellCastGoalStart(SpellCastGoalOutcome Outcome, SessionCombatResult? ActionPoints)
{
    public SpellFacing? Facing { get; init; }
}

/// <summary>How a cast while the caster's casting goal plays went (<see cref="SessionSpellCaster.Queue"/>, SU13): queued beneath the
/// playing goal, refused (the run freed), or there is no such run.</summary>
public enum SpellCastQueueOutcome { Queued, Refused, NoRun }

/// <summary>How the spell UI's cast at a target went (<see cref="SessionSpellCaster.CastAt"/>): nothing (a missing, dead or
/// unconscious caster), the caster cannot afford it (spell.mes 600), no line of fire (604), the run was refused (the invocation
/// check or the session: <see cref="SpellCastAttempt.Run"/>), or the run started and waits for its casting goal.</summary>
public enum SpellCastAttemptOutcome { Nothing, CannotAfford, NoLineOfFire, Refused, Started }

/// <summary>The spell UI's cast: its outcome, the spell.mes message the UI showed, the run start and the started run's slot.</summary>
public sealed record SpellCastAttempt(SpellCastAttemptOutcome Outcome, int? Message = null, SessionSpellRunResult? Run = null, int? RunId = null);

public enum SpellCastEventKind
{
    /// <summary>spell.mes <see cref="SpellCastEvent.Value"/> for the local player (601 maintenance, 602 checks or a shelled source,
    /// 603 a shelled target, 643 a resisting target).</summary>
    Message,
    /// <summary>A payment charged its payer this much fatigue.</summary>
    FatiguePaid,
    /// <summary>A Damage component's step on <see cref="SpellCastEvent.Object"/> dealt this many hit points.</summary>
    Damaged,
    /// <summary>A Heal component healed <see cref="SpellCastEvent.Object"/> (the value is the hit points regained).</summary>
    Healed,
    /// <summary>A component of a type no slice implements yet (the value is its <see cref="SpellComponentType"/>) did nothing.</summary>
    ComponentNotReady,
    /// <summary>A target resisted.</summary>
    Resisted,
    /// <summary>The run was freed.</summary>
    Freed,
    /// <summary>ARC-MAGIC-017: an EyeCandy component's Add (0x451FE0) played eye-candy entry <see cref="SpellCastEvent.Value"/> (the
    /// table index, 6 · spell + the component's number) on <see cref="SpellCastEvent.Object"/>, with the <c>Play:</c> flags in
    /// <see cref="SpellCastEvent.Detail"/>: a presentation event the host draws. Its callbacks (play flags 0x8, 0x10) are not
    /// ready: the run's CALLBACK actions do not follow the animation.</summary>
    EyeCandyAdded,
    /// <summary>ARC-MAGIC-017: an EyeCandy component's Remove (0x4CD940) took entry <see cref="SpellCastEvent.Value"/> off
    /// <see cref="SpellCastEvent.Object"/>: only this run's, or any run's with the <c>no_id</c> play flag (0x100).</summary>
    EyeCandyRemoved,
    /// <summary>ARC-MAGIC-022: a payment spent a charge of the item <see cref="SpellCastEvent.Object"/>; the value is the charges left.</summary>
    ChargeSpent,
    /// <summary>ARC-MAGIC-022: the session refused to save a payment's spent charge of the item <see cref="SpellCastEvent.Object"/>
    /// (the value is the <see cref="GameSessionFailure"/>): the spell was paid without it.</summary>
    ChargeNotSaved,
    /// <summary>ARC-MAGIC-024: this many item events were dropped unfired (<see cref="SessionSpellCaster.FireItemEvents"/>): beyond
    /// <see cref="SessionSpellCaster.MaxItemEventsPerFiring"/> in one firing.</summary>
    ItemEventsDropped,
    /// <summary>ARC-DEV-141 (MIG13 S8c): an AGoal component for the knockback goal 43 (0x4514E0 → 0x434E80) targeted <see cref="SpellCastEvent.Object"/>; the push comes from the source tile, whose x is
    /// <see cref="SpellCastEvent.Value"/> and y <see cref="SpellCastEvent.Detail"/> (the source object's location, else the run's start location, GT59): the host starts the run on the table.</summary>
    Knockback,
    /// <summary>ARC-DEV-143 (MIG13 S9b): an AGoal component for the floating goal 44 (0x4514E0 → 0x451646, GT69) targeted <see cref="SpellCastEvent.Object"/>: the host starts the hover run (no interrupt) on it.</summary>
    Hover,
    /// <summary>ARC-DEV-143: an AGoalTerminate component for the floating goal 44 (0x4516D0 → 0x44E4D0) targeted <see cref="SpellCastEvent.Object"/>: the host ends the object's hover runs.</summary>
    HoverEnded,
    /// <summary>ARC-DEV-261 (GT198–GT199): a Damage component's step on <see cref="SpellCastEvent.Object"/> was cancelled by that object's taking-damage script (point 0x17 skipped the default): no damage reaches it; the other targets of the run go on.</summary>
    DamageVetoed,
    /// <summary>ARC-DEV-295 (B39): a damage step on <see cref="SpellCastEvent.Object"/> gave the hero experience: the pool share is the value, the kill award the detail (XP10, XP9). The awards themselves (level-up messages, followers) are <see cref="SessionSpellCaster.TakeExperienceAwards"/>.</summary>
    ExperienceGiven,
    /// <summary>ARC-DEV-295: the session refused the experience step of a damage step on <see cref="SpellCastEvent.Object"/> (the value is the <see cref="GameSessionFailure"/>): the damage stands.</summary>
    ExperienceNotSaved,
    /// <summary>ARC-DEV-302: an ObjFlag component wrote the flag <see cref="SpellCastEvent.Value"/> on the critter <see cref="SpellCastEvent.Object"/>; <see cref="SpellCastEvent.Detail"/>
    /// bit 0x1 is on (else off), bit 0x2 the critter flags, 0x4 the object flags (else the spell flags), 0x8 a side effect's write (ARC-DEV-358).</summary>
    ObjectFlagWritten,
    /// <summary>ARC-DEV-302: an ObjFlag component left the flag <see cref="SpellCastEvent.Value"/> as it is, because other runs provide it (the reference count 0x455100); the detail as
    /// <see cref="ObjectFlagWritten"/>.</summary>
    ObjectFlagKept,
    /// <summary>ARC-DEV-302: the session refused an ObjFlag component's write on <see cref="SpellCastEvent.Object"/> (the value is the <see cref="GameSessionFailure"/>).</summary>
    ObjectFlagNotSaved,
    /// <summary>ARC-DEV-302 (GT146, GT147): an ObjFlag component set entangled or paralysed (the value is the flag) on <see cref="SpellCastEvent.Object"/>: the host interrupts, at level 6,
    /// that critter's runs whose bottom or current goal is 3 (the walk) or 61 (the melee approach), as 0x44E4D0 does with the author −1.</summary>
    HoldStarted,
    /// <summary>ARC-DEV-303 (EF9): an Effect component added the effect <see cref="SpellCastEvent.Value"/> to the hero <see cref="SpellCastEvent.Object"/>, <see cref="SpellCastEvent.Detail"/> times.</summary>
    EffectAdded,
    /// <summary>ARC-DEV-303 (EF9): an Effect component took the effect <see cref="SpellCastEvent.Value"/> off the hero <see cref="SpellCastEvent.Object"/> (the typed removal, the first entry of the number), <see cref="SpellCastEvent.Detail"/> times asked.</summary>
    EffectRemoved,
    /// <summary>ARC-DEV-303: the session refused an Effect component's add or removal on the hero <see cref="SpellCastEvent.Object"/> (the value is the <see cref="GameSessionFailure"/>, the detail the effect number); the spell goes on.</summary>
    EffectNotSaved,
    /// <summary>ARC-DEV-303 (RS10): the entangled critter <see cref="SpellCastEvent.Object"/> was hit and held (no save asked, or one not made; the value is 0).</summary>
    EntangleHeld,
    /// <summary>ARC-DEV-303 (RS10): the entangled critter <see cref="SpellCastEvent.Object"/> was hit and resisted again: "resisted" plays (0x45A520) and the hold's run ends by event (0x4571E0).</summary>
    EntangleBroken,
    /// <summary>ARC-DEV-352 (E1-b, SUM1–SUM4): a Summon component created the scenery <see cref="SpellCastEvent.Object"/> of prototype file <see cref="SpellCastEvent.Value"/> at its tile.</summary>
    Summoned,
    /// <summary>ARC-DEV-352: a Summon component's creation was refused (the session's failure is <see cref="SpellCastEvent.Detail"/>; the original exits on a failed creation, SUM2, an unreachable bug kept as a typed refusal).</summary>
    SummonRefused,
    /// <summary>ARC-DEV-352 (DS2, SUM8): a Destroy component destroyed the summoned object <see cref="SpellCastEvent.Object"/>.</summary>
    SummonDestroyed,
    /// <summary>ARC-MAGIC-032 (XR1, XR2): the hero's Identify component targeted the critter <see cref="SpellCastEvent.Object"/> (the value is its object type): the original opens the read-only NPC sheet for the casting player; the window is E8's, the viewer's answer is a line.</summary>
    IdentifyCritter,
    /// <summary>ARC-MAGIC-032 (XR1, XR3): the hero's Identify component targeted the container <see cref="SpellCastEvent.Object"/>: the original opens its inventory in mode 4 with no lock test; the window is E8's, the viewer's answer is a line.</summary>
    IdentifyContainer,
    /// <summary>ARC-MAGIC-032 (XR1, XR4): the hero's Identify component targeted another object, an item <see cref="SpellCastEvent.Object"/> (the value is its object type): the original shows its item description, the true one when it is identified.</summary>
    IdentifyItem,
    /// <summary>ARC-MAGIC-032 (XR4): an ObjFlag component set the identified item flag (0x1) on the item <see cref="SpellCastEvent.Object"/>; the session keeps it as an identification mark (<see cref="GameSessionOwner.QueryItemIdentified"/>).</summary>
    ItemIdentified,
    /// <summary>ARC-MAGIC-032: the session refused the identification mark of the item <see cref="SpellCastEvent.Object"/> (the value is the <see cref="GameSessionFailure"/>): the spell goes on.</summary>
    ItemIdentifyNotSaved,
    /// <summary>ARC-MAGIC-034 (MA-3, DP1–DP2): a Dispel component acted on <see cref="SpellCastEvent.Object"/>; <see cref="SpellCastEvent.Detail"/> is the sum of <see cref="SessionSpellCaster.DispelStonedCleared"/>
    /// (the object's stoned spell flag was cleared) and <see cref="SessionSpellCaster.DispelDestroyed"/> (a summoned created critter is destroyed once the runs' endings are told; <see cref="SummonDestroyed"/> follows). The runs it names are told after the outer action (their endings follow).</summary>
    Dispelled,
    /// <summary>ARC-MAGIC-034 (DP2): a Dispel component met a summoned critter (spell flag 0x4000) this host cannot destroy (not a created critter of the session): the object stays; the value is its object type.</summary>
    DispelDestroyNotReady,
    /// <summary>ARC-MAGIC-035 (MA-4, SO1–SO8): a social or AI flag of an ObjFlag component did a step besides its own write on <see cref="SpellCastEvent.Object"/>: <see cref="SpellCastEvent.Value"/> is one of the
    /// <see cref="SessionSpellCaster.SocialReaction"/> family of codes and <see cref="SpellCastEvent.Detail"/> its amount or reason.</summary>
    SocialEffect,
    /// <summary>ARC-MAGIC-037 (MA-6, LM1–LM3): an ObjFlag component wrote the lock flag bit <see cref="SpellCastEvent.Value"/> (a plain write, no Lock event) of the portal or container <see cref="SpellCastEvent.Object"/>;
    /// <see cref="SpellCastEvent.Detail"/> bit 0x1 is on (else off), bit 0x2 a container (else a portal).</summary>
    OpenableFlagWritten,
    /// <summary>ARC-MAGIC-037: the session refused a lock flag write on <see cref="SpellCastEvent.Object"/> (the value is the <see cref="GameSessionFailure"/>): the spell goes on.</summary>
    OpenableFlagNotSaved,
    /// <summary>ARC-MAGIC-037 (LM2): the unlock gate (0x4551C0) refused the caster's unlock of the portal or container <see cref="SpellCastEvent.Object"/>: its lock level (the value) is above the caster's
    /// aptitude; the lock stays and the "resisted" feedback (<see cref="Resisted"/>) follows.</summary>
    UnlockRefused,
    /// <summary>ARC-MAGIC-037 (LM2): the guard alert (0x4AEE50 with loudness 0 and kind 2) the lock-clearing branch always calls, after the gate and whether or not the lock opened, on the portal or container
    /// <see cref="SpellCastEvent.Object"/> by the run's parent; <see cref="SpellCastEvent.Detail"/> is 1 when the unlock was refused. The guards who see or hear it (PL6) are not modelled: the host acts on this line.</summary>
    LockGuardAlert,
    /// <summary>ARC-MAGIC-036 (MA-5, MV1–MV3): a Movement component of type 0 (the target tile, 0x4524C0 → the immediate relocation 0x455350) asks the host to relocate <see cref="SpellCastEvent.Object"/> along the path request to the run's
    /// target tile, whose x is <see cref="SpellCastEvent.Value"/> and y <see cref="SpellCastEvent.Detail"/>: the host asks the path, runs the tiles' scripts on the way and places the object on the last tile. It needs the map's paths, so this host logs the request.</summary>
    Movement,
    /// <summary>ARC-MAGIC-038 (MA-5 second half, TP1–TP5): a Movement component of type 3 (Teleport_Area_Portal, the helper 0x452650) sends <see cref="SpellCastEvent.Object"/> to the start map at the area's location, x in
    /// <see cref="SpellCastEvent.Value"/> and y in <see cref="SpellCastEvent.Detail"/>, with the change of place's flags 0x120: the host makes the change of place.</summary>
    TeleportPortal,
    /// <summary>ARC-MAGIC-038: a Movement component of type 2 (Teleport_Tile, Teleportation) that the anti-teleport test allowed calls the world map's teleport mode (0x4608B0 → 0x560790) with the run's source
    /// (<see cref="SpellCastEvent.Object"/>) and spell (<see cref="SpellCastEvent.Value"/>): the host opens the world map for the player to pick an area, and pays with <see cref="SessionSpellCaster.PayTeleportCost"/>.</summary>
    TeleportMap,
    /// <summary>ARC-MAGIC-038: a Movement component of type 1 (Random_Tile_In_Radius, 0x4524C0 → the placement 0x4F4E40) asks the host to relocate <see cref="SpellCastEvent.Object"/> to a tile
    /// <see cref="SpellCastEvent.Value"/> steps (the component's <c>Tile_Radius:</c>) away in the direction <see cref="SpellCastEvent.Detail"/> (0..8, the one draw): the host asks the path, walks and places as for type 0.</summary>
    MovementRandom,
}

/// <summary>
/// ARC-MAGIC-014 (magic plan MT20a, the cast host): the world the run rules ask, over the session and a map — the run host
/// (<see cref="ISpellRunHost"/>), the components' damage world (<see cref="ISpellDamageWorld"/>) and the event pass's magic host
/// (<see cref="ISessionSpellWorld"/>) — for any caster, critter and map. The hero is the PC (<see cref="Hero"/>); NPCs and objects
/// come from the map. Damage and Heal (MT12) are applied to the session's life; the other component types are not ready and report
/// <see cref="SpellCastEventKind.ComponentNotReady"/>. Payments the rules return are applied through <see cref="Apply"/>.
/// </summary>
public sealed partial class SessionSpellCaster : ISpellRunHost, ISpellDamageWorld, ISessionSpellWorld, ISpellMissileCaster
{
    /// <summary>The PC's entity id in the run rules and the map (the session's player entity).</summary>
    public const string Hero = SessionEventKinds.PlayerEntity;

    private readonly GameSessionOwner _owner;
    private readonly Func<int, SessionSpellRecord?> _records;
    private readonly ISpellCastMap _map;
    // ARC-DEV-302: the same map without the session's written flags (what the critters' data stores), which the reference count and the writes start from.
    private readonly ISpellCastMap _dataMap;
    private readonly ISessionEventWorld _events;
    private readonly List<SpellCastEvent> _log = [];
    // The runs whose goal reached its release frame (the goal's frame flag 0x8): their goal's end frees nothing. A freed run's id
    // leaves the set, since the slot is reused. ARC-DEV-289: a run whose missile flies is rebuilt from the session's missiles at the host's build.
    private readonly HashSet<int> _released = [];
    // ARC-MAGIC-018: the runs whose missile flies (goal 28): their release comes when it arrives.
    private readonly HashSet<int> _missiles = [];
    // Whose charge cells a payment spends: the payer the rules asked for last (only the hero's are modelled). The rules ask the payer
    // before the cells of every payment; a run without a source asks neither and pays nothing.
    private string? _cellsPayer;
    // ARC-MAGIC-024: the item events this host's life steps called for, not fired yet, and whether a firing is under way (the runs its
    // executor calls start add theirs to the end).
    private readonly List<SessionItemEvent> _itemEvents = [];
    private bool _firingItems;
    // ARC-DEV-259: the objects a Destroy component destroyed whose runs have not heard of it yet (the original's 0x459740 call inside the
    // destroy); the session refuses a run action from inside the run rules, so they are told after the outer action or pass, in order.
    // ARC-DEV-301: the same list holds the endings a critter's life step calls for (death: object gone, going unconscious: demaintain) and those the AI gate and the object destroy ask for.
    private readonly List<(SessionSpellRunActionKind Kind, string Id, int Processing)> _goneObjects = [];
    private bool _tellingGone;
    // ARC-MAGIC-034: the summoned created critters a Dispel destroys once its run endings are told (the dispelling run, the entity).
    private readonly List<(int Run, string Entity)> _dispelDestroys = [];
    private const int MaxPendingRunEnds = 256;
    // ARC-MAGIC-025, MG46: the chain of the item event being fired (null outside a firing) and, per run an item event started, the
    // chain it was started in with its source (a slot reused by any other run is dropped); not saved: a restored item run (schema 73) restarts with an empty chain.
    private IReadOnlyCollection<string>? _firingChain;
    private readonly Dictionary<int, (string? Source, IReadOnlyCollection<string> Chain)> _itemChains = [];

    /// <summary>A host over <paramref name="map"/>; with <paramref name="items"/> (ARC-MAGIC-020) the items a critter of the map holds
    /// are objects too (<see cref="Item"/>), so their spells can run with the item as source.</summary>
    public SessionSpellCaster(GameSessionOwner owner, Func<int, SessionSpellRecord?> records, ISpellCastMap map, ISessionEventWorld events,
        Func<string, SessionItemSpells?>? items = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        ArgumentNullException.ThrowIfNull(map);
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _items = items;
        // ARC-DEV-352: the created objects of the summon map are map objects too (a Tgt_Summoned re-aim, the empty-tile tests, a Destroy).
        _dataMap = new CreatedCastMap(items is null ? map : new ItemCastMap(map, Item), this);
        _map = new FlagCastMap(_dataMap, _owner);
        // ARC-DEV-301: the session tells the host of the shown map when a critter's life step ends its runs (death, loss of consciousness).
        _owner.CritterRunEnd = QueueRunEnd;
        // ARC-DEV-289 (MIG14 S8): the session's missiles carry the run each releases (SessionMissileFlight.Run): a host built while they fly (after a load, or a host rebuilt for
        // the map) knows their release and missile marks as the one that launched them did, so the casting goal's end frees nothing and the arrival begins the run.
        foreach (var missile in _owner.QueryMissiles())
            if (missile.Flight is { } flight && _owner.MissileHoldsRun(flight)) { _released.Add(flight.Run); _missiles.Add(flight.Run); }
    }

    /// <summary>The session this host casts in.</summary>
    public GameSessionOwner Owner => _owner;

    /// <summary>What the casts so far left for the host, in order.</summary>
    public IReadOnlyList<SpellCastEvent> Log => _log;

    /// <summary>
    /// ARC-MAGIC-024: the item spell executors this host fires the item events of its own life steps through: a spell's damage step
    /// (DH1: the run's parent the attacker, its other object or else its parent the damage source), a payment's fatigue (0x450420) and
    /// the life steps of its event passes (the fatigue setter's going unconscious, IC4). They fire after the run's pass, once the
    /// notes are applied (<see cref="Act"/>, <see cref="ProcessEvents"/>), since the session refuses a run start while the run rules
    /// act. None: nothing fires and the events are dropped.
    /// </summary>
    public ISessionItemTriggers? ItemTriggers { get; set; }

    /// <summary>ARC-MAGIC-024: the item events one firing walks at most, those the runs it starts call for included (a technical bound:
    /// the goals of those runs play at once, so a chain the original spreads over frames would not end here, MG46).</summary>
    public const int MaxItemEventsPerFiring = 64;

    private MagicAnomalyProfile Profile => _owner.Rules.Magic;

    // ---- the objects ----

    /// <summary>The run rules' object for a map object (the hero with its aptitude), or null.</summary>
    public SpellRunObject? RunObject(string id) =>
        id == Hero ? _owner.QueryHero() is null ? null : new(Hero, SpellObjectFacts.PcType, HeroStat(CharacterStat.MagickTechAptitude) ?? 0)
        : _map.Critter(id) is { } npc ? new(id, SpellObjectFacts.NpcType, NpcStat(npc, CharacterStat.MagickTechAptitude) ?? 0)
        : _map.Object(id) is { } o ? new(id, o.Type) : null;

    /// <summary>The invocation check's facts of a map object (the hero's from the session), or null.</summary>
    public SpellObjectFacts? Facts(string id)
    {
        if (_map.Object(id) is not { } o) return null;
        var aptitude = id == Hero ? HeroStat(CharacterStat.MagickTechAptitude) ?? 0 : _map.Critter(id) is { } npc ? NpcStat(npc, CharacterStat.MagickTechAptitude) ?? 0 : 0;
        var intelligence = id == Hero ? HeroStat(CharacterStat.Intelligence) ?? 0 : _map.Critter(id) is { } c ? NpcStat(c, CharacterStat.Intelligence) ?? 0 : 0;
        return new(id, o.Type, o.Location, o.ObjectFlags, o.SpellFlags, o.CritterFlags, aptitude, intelligence);
    }

    /// <summary>The invocation world over the map (MT7): the line of fire and the BEGIN area tests (MT8's areas).</summary>
    public ISpellInvocationWorld InvocationWorld() =>
        new SpellInvocationAreas(_map, spell => _records(spell) is { } r && r.Areas.Length > 0 ? r.Areas[0].Targets : 0, _map.LineOfFire, Profile);

    // ---- ISpellRunHost ----

    public SpellRunRecord Record(int spell) => _records(spell)?.Run ?? new();

    /// <summary>ARC-MAGIC-022: the whole record of <paramref name="spell"/> the host knows, or null.</summary>
    public SessionSpellRecord? SpellRecord(int spell) => _records(spell);

    /// <summary>ARC-MAGIC-022: the hero's life status, or null without a hero.</summary>
    public LifeStatus? HeroLife => Life(Hero)?.Status;

    public SpellStageFacts StageFacts(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var source = run.Source;
        var life = source is null ? null : Life(source.Id);
        var parent = run.Parent;
        return new(life?.Status.IsDead == true, life?.Status.IsUnconscious == true, parent is null ? 0 : Stat(parent, 4) ?? 0,
            parent is null ? 0 : Stat(parent, 6) ?? 0, parent?.Id == Hero, source is null ? null : Payer(source), Caster(source));
    }

    /// <summary>The charge cells of the payer asked for last (<see cref="Payer"/>): the hero's, also for an item the hero holds (its
    /// parent's stack, ARC-MAGIC-020); any other payer's are not modelled yet and refuse.</summary>
    public ISpellCellSource Cells => _cellsPayer == Hero ? _owner.HeroCellSource() ?? NoCells.Instance
        : _cellsPayer is not null && Item(_cellsPayer) is { Holder: Hero } item ? _owner.HeroCellSource(item.Complexity) ?? NoCells.Instance
        : NoCells.Instance;

    public bool FlagsAllow(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var record = Record(run.Spell).Invocation;
        return SpellInvocationRules.FlagsAllow(run.Parent is { } p ? Facts(p.Id) : null, run.Target is { } t ? Facts(t.Id) : null, record);
    }

    public int SpellFlags(SpellRunObject who) => _map.Object(who.Id)?.SpellFlags ?? 0;

    public int? PlayerSpellFlags => _map.Object(Hero)?.SpellFlags;

    public bool PlayerPerceptionExtraordinary => HeroStat(CharacterStat.Perception) >= CharacterStatRules.ExtraordinaryThreshold;

    public SpellPayer? Payer(SpellRunObject who)
    {
        ArgumentNullException.ThrowIfNull(who);
        _cellsPayer = who.Id;
        if (!who.IsCritter) return Item(who.Id) is { } item ? new(SpellPayerKind.Item, ItemCharges: item.Charges) : new(SpellPayerKind.Other);
        if (Life(who.Id) is not { } life) return null;
        var limiting = who.Id != Hero && _map.Critter(who.Id) is { } npc && (npc.CritterFlags & unchecked((int)0x80000000)) != 0;
        // Worn mana stores are not modelled yet (no item store state): the fatigue pays all.
        return new(SpellPayerKind.Critter, limiting, null, life.Status.Fatigue, life.Record.State.FatigueDamage);
    }

    public SpellResistRecord ResistRecord(int spell) => _records(spell)?.Resist ?? new(spell, false, false, false, 0, SpellResistanceRules.NoSave, 0);

    public SpellResistTarget ResistTarget(SpellRun run, SpellRunTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ResistTarget(run.Spell, run.Parent?.Id, target.Object);
    }

    // ARC-DEV-303 (MG48): the run processing resets the cached target type at each action (0x451070) and sets it for each target object (0x45132D); the entangle's re-resistance reads it.
    // ARC-DEV-352 (SUM1): and the context's summoned slot once per action.
    void ISpellRunHost.ActionStarted(SpellRun run) { _cachedTargetKind = null; _lastSummoned = null; }

    void ISpellRunHost.TargetStarted(SpellRun run, SpellRunTarget target)
    {
        if (target.Object is { } obj) _cachedTargetKind = KindOf(obj.Id);
    }

    // The resistance's facts of a target object for a spell cast by the parent (ARC-WORLD-102: also the AI's fail-chance predictor).
    private SpellResistTarget ResistTarget(int spell, string? parent, SpellRunObject? target)
    {
        var record = ResistRecord(spell);
        if (target is not { } obj) return new(false, SpellTargetKind.Other, false, 0, 0, 0, false);
        var kind = KindOf(obj.Id);
        var npc = obj.Id == Hero ? null : _map.Critter(obj.Id);
        var statValue = record.HasSave ? Stat(obj, record.ResistStat) ?? 0 : 0;
        // The hero stores no magic resistance of its own in the session (its pre-made fields hold none). ARC-MAGIC-033 (D6c, both modes): the save reads the resistance under the target's
        // resistmagic effect changes (Resist Magick works), which the original's save does not (RE2).
        var resistance = obj.Id == Hero ? _owner.HeroSaveMagicResistance() : npc is null ? 0 : _owner.WithNpcEffects(npc.Combat).SaveMagicResistance(npc.StoredMagicResistance);
        return new(obj.Id == parent, kind, ((npc?.CritterFlags2 ?? 0) & 0x2000) != 0, resistance,
            obj.Id == Hero ? HeroStat(CharacterStat.MagickTechAptitude) ?? 0 : npc is null ? 0 : NpcStat(npc, CharacterStat.MagickTechAptitude) ?? 0,
            statValue, record.ResistStat is >= 0 and <= 7 && statValue >= CharacterStatRules.ExtraordinaryThreshold);
    }

    public ISpellRollSource Rolls => _owner.SpellRolls;

    public ISpellFateSave FateSave => _owner.HeroMagickSave;

    public bool IsLocalPlayer(SpellRunObject? who) => who?.Id == Hero;

    public IReadOnlyList<SpellRunTarget> Collect(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        // 0x4510F0 sets the flag at 0x5E75A8 for each pass (0 unless the PC's armed fate was used), after the target list is set up; the run system asks the fate right after this call.
        _spellAtMaximum = false;
        if (_records(run.Spell) is not { } record || (int)run.Action >= record.Areas.Length) return [];
        var context = Context(run, record.Areas[(int)run.Action]);
        return SpellTargetRules.Collect(context, _map, Profile).Entries.Select(e => new SpellRunTarget(e.Object is { } o ? RunObject(o.Id) ?? new(o.Id, o.Type) : null, e.Tile))
            .ToArray();
    }

    public bool UseSpellAtMaximum(SpellRunObject pc) => _spellAtMaximum =
        pc.Id == Hero && _owner.QueryHeroFate() is { } fate && FateRules.IsArmed(fate, FateKind.SpellAtMaximum)
        && _owner.UseHeroFate(_owner.Version, FateKind.SpellAtMaximum).Disposition == GameSessionDisposition.Changed;

    // ARC-MAGIC-037: the original's per-pass flag 0x5E75A8 (the PC's armed fate "spell at maximum" was used for this pass); the unlock gate (0x4551C0) reads it.
    private bool _spellAtMaximum;

    public SpellComponentGate Gate(SpellRun run, int component, SpellRunTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell)?.Component(run.Action, component) is not { } c) return new(false, target);
        var context = Context(run, c.Area) with { Candidate = target.Object is { } o ? _map.Object(o.Id) : null, CandidateTile = target.Tile };
        if (!SpellTargetRules.Test(context, _map, Profile).Passes) return new(false, target, c.ItemTriggers);
        var (aimed, tile) = SpellTargetRules.ReAim(c.ApplyTargets, context.Candidate, target.Tile, context.Self, context.Source, context.Summoned, run.TargetTile,
            context.Target);
        CacheReAimedKind(c.ApplyTargets, context.Self, context.Source);
        return new(true, new(aimed is null ? null : RunObject(aimed.Id) ?? new(aimed.Id, aimed.Type), tile), c.ItemTriggers);
    }

    /// <summary>
    /// ARC-DEV-281 (B08, TA16): the host's AI, told when an aggressive spell reaches a target: the attacker (the run's parent, or its
    /// other object; <see cref="Hero"/> for the player) and the victim's object id. The viewer runs the attack event (0x4A9650) on its AI
    /// host. Null: no AI, nothing happens.
    /// </summary>
    public Action<string?, string>? AiNotice { get; set; }

    void ISpellRunHost.AiNotice(string? attacker, string victim) => AiNotice?.Invoke(attacker, victim);

    /// <summary>
    /// ARC-DEV-292: the host's AI, told after a damage step of this caster (a spell's damage component, a script's damage, a burn, a trap) took effect on a critter (the object id of the victim): the original's damage step (0x4B4390) runs
    /// the AI pass once at the end for a victim that survives (0x4B52FC). The viewer runs it on its AI host (an NPC the host runs and that is alive; the hero's is none). Called after the life took the damage (the item events of the step are kept, and fire when the host asks for them, in
    /// each channel's own order), so the victim's hit reaction (a stun, a fall) has started; not called for a damage the victim's script vetoed or a gate prevented. Null: no AI, nothing happens.
    /// </summary>
    public Action<string>? AiStruck { get; set; }

    private void Struck(string victim, SessionLifeResult applied)
    {
        if (applied.Disposition == GameSessionDisposition.Changed) AiStruck?.Invoke(victim);
    }

    [OriginalFunction(0x452300, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4516D0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4514E0, OriginalFunctionScope.Partial)]
    // ARC-DEV-358 (MA-1): the dispatch is a table by component type (ComponentHandlers, SessionSpellCasterComponents.cs); a type without a handler, or one whose handler leaves the
    // target undone, is logged as ComponentNotReady.
    public SpellComponentOutcome Apply(SpellRun run, int component, SpellRunTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell)?.Component(run.Action, component) is not { } c) return SpellComponentOutcome.None;
        if (ComponentHandlers.TryGetValue(c.Type, out var handler) && handler(this, run, c, target) is { } outcome) return outcome;
        if (c.Damage is null) _log.Add(new(SpellCastEventKind.ComponentNotReady, run.Id, (int)c.Type, target.Object?.Id));
        return SpellComponentOutcome.None;
    }

    /// <summary>
    /// ARC-DEV-259 (B14): the Destroy component (0x451AF0; ledger DS1–DS3) on its target object. A target the host no longer finds does
    /// nothing (the original's destroy leaves a destroyed object as it is). A run with a resistance margin (+0x144) destroys only a
    /// summoned object (spell flag 0x4000); any other run destroys what it names. The destruction (0x43CCA0) is done here for an item lying on
    /// the ground (<see cref="GameSessionOwner.DestroyLyingItem"/>: a thrown grenade destroys itself after its explosion), and the runs hear of
    /// it after the outer action (<see cref="SessionSpellRunActionKind.ObjectGone"/>, 0x459740). Another kind of object (a critter, a
    /// container, a door, a held item) is not ready: false, and the caller logs it as a component left undone.
    /// </summary>
    [OriginalFunction(0x451AF0, OriginalFunctionScope.Partial)]
    private bool DestroyComponent(SpellRun run, SpellRunTarget target)
    {
        if (target.Object is not { } who) return true;
        // An object no longer found is left as it is when the session destroyed it; a carried or worn item is not a map object here: not ready.
        if (_map.Object(who.Id) is not { } victim) return _owner.IsItemDestroyed(who.Id) || Item(who.Id) is null;
        if (run.Margin != 0 && (victim.SpellFlags & SpellTargetObject.Summoned) == 0) return true;
        if (SessionCreatedObject.KindOf(who.Id) is not null) return DestroyCreated(run, who.Id);
        if (!victim.IsItem || _owner.DestroyLyingItem(_owner.Version, who.Id).Disposition != GameSessionDisposition.Changed) return false;
        // The original tells the runs inside the destroy, while this run is processed (guard 0x5B0BA4): that run is left alone.
        _goneObjects.Add((SessionSpellRunActionKind.ObjectGone, who.Id, run.Id));
        return true;
    }

    // ARC-DEV-301: an ending of a critter's (or any object's) runs, told once the run rules are idle (the ending of a critter inside a spell's damage is deferred to the end of the outer action; a run being processed is then not left alone).
    private void QueueRunEnd(SessionSpellRunActionKind kind, string id)
    {
        if (_goneObjects.Count < MaxPendingRunEnds) _goneObjects.Add((kind, id, -1));
    }

    /// <summary>ARC-DEV-301 (0x4A8570's step 15, 0x457450): the AI gate ends the spells the NPC maintains once its leader is gone or out of combat mode: every run it is the source of, but those of technology or with item triggers and no maintenance period, ends by event.
    /// The endings fire at once unless the run rules or an item firing are under way, which tell them when they are done.</summary>
    public void EndMaintainedSpells(string critter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        QueueRunEnd(SessionSpellRunActionKind.Demaintain, critter);
        FirePendingItems();
    }

    /// <summary>ARC-DEV-301 (0x43CCA0 → 0x459740): the destruction of an object that is not a lying item (a critter a script destroys): the runs it is the parent, the source or the other object of hear of it, as <see cref="EndMaintainedSpells"/>.</summary>
    public void ObjectGone(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        QueueRunEnd(SessionSpellRunActionKind.ObjectGone, obj);
        FirePendingItems();
    }

    // ARC-DEV-259: tells the runs of the objects a Destroy component destroyed (0x459740) once the run rules are idle; a call the session
    // refuses (from inside the run rules or an event pass) leaves them for the next one.
    private void TellGoneObjects()
    {
        if (_tellingGone) return;
        _tellingGone = true;
        try
        {
            while (_goneObjects.Count > 0 || _dispelDestroys.Count > 0)
            {
                // ARC-MAGIC-034 (DP2): a Dispel destroys its summoned critter only after the runs' half (loop 2 reads the runs' summoned lists before 0x43CCA0); the endings the destroy queues follow in this loop.
                if (_goneObjects.Count == 0)
                {
                    var (run, entity) = _dispelDestroys[0];
                    _dispelDestroys.RemoveAt(0);
                    DestroyCreated(run, entity);
                    continue;
                }
                var (kind, id, processing) = _goneObjects[0];
                // An id the session would refuse for good is dropped, so it cannot hold up the endings behind it.
                if (!SessionValidation.Entity(id)) { _goneObjects.RemoveAt(0); continue; }
                var before = _owner.QuerySpellRuns();
                var result = _owner.ActOnSpellRun(_owner.Version, this, new(kind, kind is SessionSpellRunActionKind.ObjectGone or SessionSpellRunActionKind.EndByEvent or SessionSpellRunActionKind.Dispel ? processing : -1, id));
                if (result.Failure is GameSessionFailure.InvalidSpellRun or GameSessionFailure.StaleVersion) break;
                _goneObjects.RemoveAt(0);
                Apply(result.Notes, runId => _owner.QuerySpellRuns()[runId] ?? before[runId]);
            }
        }
        catch
        {
            _goneObjects.Clear();
            _dispelDestroys.Clear();
            throw;
        }
        finally { _tellingGone = false; }
    }

    /// <summary>ARC-MAGIC-017: the EyeCandy component (0x451FE0, ledger EC rows) on its target object (none: nothing): an Add plays
    /// entry 6 · spell + number unless the run is forced to end, a Remove takes that entry off. Presentation only: the host draws it
    /// from the log. ARC-DEV-352 (EC11): an Add with a callback play flag makes the run wait for the animation when the entry plays and calls back at once when it does not
    /// (<see cref="EyeCandyPlays"/>; without it the callbacks are not ready).</summary>
    [OriginalFunction(0x451FE0, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome EyeCandy(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (target.Object is not { } obj || component.EyeCandy is not { } data) return SpellComponentOutcome.None;
        var index = SpellEyeCandyRules.EntryIndex(run.Spell, data.Number);
        if (!data.Add) { _log.Add(new(SpellCastEventKind.EyeCandyRemoved, run.Id, index, obj.Id, (int)data.Play)); return SpellComponentOutcome.None; }
        if ((run.Flags & SpellRunFlags.ForcedEnd) != 0) return SpellComponentOutcome.None;
        _log.Add(new(SpellCastEventKind.EyeCandyAdded, run.Id, index, obj.Id, (int)data.Play));
        return EyeCandyOutcome(index, data.Play);
    }

    /// <summary>The animation goal number of the knockback (<c>anim_goal_knockback</c>, GoalType 43).</summary>
    public const int KnockbackGoal = 43;

    /// <summary>ARC-DEV-141 (GT59): the AGoal component with goal 43 (0x4514E0): the target object is pushed away from the run's source object's location, else the run's source location. The start itself (the table's run) is the
    /// host's: it needs the map's bounds and the pushed object's place, so this host logs the request. A component without a target object does nothing.</summary>
    private void Knockback(SpellRun run, SpellRunTarget target)
    {
        if (target.Object is not { } obj) return;
        var source = run.Source is { } s && _map.Object(s.Id) is { } located ? located.Location : run.StartLocation;
        _log.Add(new(SpellCastEventKind.Knockback, run.Id, unchecked((int)source.X), obj.Id, unchecked((int)source.Y)));
    }

    /// <summary>The animation goal number of the hover (<c>anim_goal_floating</c>, GoalType 44).</summary>
    public const int HoverGoal = 44;

    /// <summary>ARC-DEV-143 (GT69): the AGoal component with goal 44 starts the hover of the target object, an AGoalTerminate with goal 44 ends it. Like the knockback, the run itself is the host's (it needs the owner on the map); this host logs the request.
    /// A component without a target object does nothing.</summary>
    private void Hover(SpellRun run, SpellRunTarget target, bool start)
    {
        if (target.Object is not { } obj) return;
        _log.Add(new(start ? SpellCastEventKind.Hover : SpellCastEventKind.HoverEnded, run.Id, 0, obj.Id));
    }

    public SpellTimeFacts TimeFacts(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(run.Parent?.IsCritter == true, run.Parent is { IsCritter: true } parent ? Caster(parent).MasteredCollege : SpellBookRules.NoMastery,
            run.Target?.IsCritter == true, run.Parent?.Aptitude ?? 0, run.Target?.Aptitude ?? 0);
    }

    /// <summary>A stat by the original's number (0..7 the primary stats, 8..16 the derived ones; 17 the level), or null.</summary>
    public int? Stat(SpellRunObject who, int stat)
    {
        ArgumentNullException.ThrowIfNull(who);
        if (stat == 17) return who.Id == Hero ? _owner.QueryHeroExperience()?.Level : _map.Critter(who.Id)?.Combat.Level;
        if (CharacterStatOf(stat) is not { } s) return null;
        return who.Id == Hero ? HeroStat(s) : _map.Critter(who.Id) is { } npc ? NpcStat(npc, s) : null;
    }

    /// <summary>ARC-MAGIC-026: the session's maintain bar answers (<see cref="GameSessionOwner.MaintainBarTakes"/>).</summary>
    public bool MaintainBarHasSlot(SpellRun run) => _owner.MaintainBarTakes(run);

    /// <summary>The charge of a run freed before its BEGIN (0x456FA0 through 0x4507D0): the run cost with mastery, applied, by its
    /// source; in the original whatever the run (MG40), in Steamwoven only when its BEGIN would have paid.</summary>
    public IReadOnlyList<SpellPayment> ChargeBegin(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Source is not { } source || Payer(source) is not { } payer) return [];
        var record = Record(run.Spell);
        if (!Profile.GoalEndChargesAnyRun && !SpellCostRules.BeginPays(run.Free, run.Parent?.IsCritter == true, record.Technology, record.SkipsBeginCost))
            return [];
        var cost = SpellCostRules.RunCost(record.Cost, run.Spell, Caster(source), false);
        return [SpellPaymentRules.Pay(payer, cost, true, run.Spell, record.Payment, Cells)];
    }

    // ---- ISpellDamageWorld ----

    public Combatant Combatant(SpellRunObject who)
    {
        ArgumentNullException.ThrowIfNull(who);
        if (who.Id == Hero) return _owner.QueryHeroAttack()?.Combatant ?? new Combatant { IsPc = true };
        if (_map.Critter(who.Id) is { } npc && _owner.QueryLife(npc.Combat.Target).View is { } life)
            return _owner.WithNpcEffects(npc.Combat).Combatant(life.Status, life.Record.State, _owner.Rules.Combat);
        return _map.ObjectCombat(who.Id)?.Combatant ?? new Combatant();
    }

    public AttackTargetKind Kind(SpellRunObject who) =>
        who.Id == Hero || _map.Critter(who.Id) is not null ? AttackTargetKind.Critter : _map.ObjectCombat(who.Id)?.Kind ?? AttackTargetKind.OtherObject;

    public CombatWeapon WieldedWeapon(SpellRunObject who) =>
        who.Id == Hero ? _owner.QueryHeroAttack()?.Weapon ?? CombatWeapon.Unarmed() : _map.WieldedWeapon(who.Id);

    public GameDifficulty Difficulty => _owner.Difficulty;

    public int CurrentHitPoints(SpellRunObject who) => Life(who.Id)?.Status.HitPoints ?? 0;

    public int CurrentFatigue(SpellRunObject who) => Life(who.Id)?.Status.Fatigue ?? 0;

    public IReadOnlyList<SpellAcidItem> WornItems(SpellRunObject critter) => _map.WornItems(critter.Id);

    // ---- ISessionSpellWorld ----

    public ISpellRunHost SpellRuns => this;

    public LifeFacts? CritterFacts(SessionMapId map, string entity) => _events.CritterFacts(map, entity);

    // ---- the casting goal (0x429B50..0x429CD0) ----

    /// <summary>
    /// The casting goal's start (0x429B50): the run's parent pays <see cref="TurnBasedRules.CastSpellCost"/> action points
    /// (0x4B7CD0: only in a turn-based round, in its own subturn; a refusal ends the goal), then the pre-animation check (0x450940,
    /// PC3): unless the run is free or has no source, its source must afford the BEGIN cost (the maintenance for another action) —
    /// no mastery, doubled for a dwarf — asked without applying. A goal that ends here frees the run with its BEGIN charged
    /// (<see cref="EndGoal"/>, 0x429CD0). Then a target destroyed or switched off ends the goal too (0x429BC0).
    /// </summary>
    [OriginalFunction(0x429B50, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x429BC0, OriginalFunctionScope.Partial)]
    public SpellCastGoalStart StartGoal(int run, ISessionCombatWorld combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (_owner.QuerySpellRuns()[run] is not { } r) return new(SpellCastGoalOutcome.NoRun, null);
        // A new goal for the slot: a release this host remembers belongs to an earlier run, freed elsewhere.
        _released.Remove(run);
        _missiles.Remove(run);
        SessionCombatResult? points = null;
        if (r.Parent is { } parent && Combatant(parent.Id) is { } combatant)
        {
            points = _owner.ConsumeActionPoints(_owner.Version, combatant, TurnBasedRules.CastSpellCost, combat);
            if (points.Charge == SessionActionPointUse.Refused || points.Disposition == GameSessionDisposition.Rejected)
                return new(SpellCastGoalOutcome.NoActionPoints, points);
        }
        if (SpellCostRules.PreAnimationChecked(r.Free, r.Source is not null))
        {
            var source = r.Source!;
            // PC3: a failed record lookup refuses.
            if (_records(r.Spell)?.Run is not { } record) return new(SpellCastGoalOutcome.CannotAfford, points);
            var cost = SpellCostRules.PreAnimationCost(r.Action == SpellRunAction.Begin, record.Cost, record.MaintainCost, Caster(source), false);
            if (Payer(source) is { } payer && !SpellPaymentRules.Pay(payer, cost, false, r.Spell, record.Payment, Cells).Succeeded)
                return new(SpellCastGoalOutcome.CannotAfford, points);
        }
        // 0x429BC0: a target destroyed or switched off ends the goal before its animation.
        if (r.Target is { } target && ((_map.Object(target.Id)?.ObjectFlags ?? 0) & (SpellTargetObject.Destroyed | SpellTargetObject.Off)) != 0)
            return new(SpellCastGoalOutcome.TargetGone, points);
        // ARC-MAGIC-018: the facing turn (0x42C850) after the range node; a refusal ends the goal through its end node, as above.
        var facing = Facing(r);
        if (facing?.Kind == SpellFacingKind.Refused) return new(SpellCastGoalOutcome.CannotTurn, points) { Facing = facing };
        return new(SpellCastGoalOutcome.Started, points) { Facing = facing };
    }

    /// <summary>ARC-MAGIC-018: the casting goal's facing turn (0x42C850, <see cref="SpellFacingRules.Turn"/>) of the run's parent toward
    /// its target object; null without a parent on the map.</summary>
    private SpellFacing? Facing(SpellRun run)
    {
        if (run.Parent is not { } parent || _map.Object(parent.Id) is not { } caster) return null;
        var target = run.Target is { } t ? _map.Object(t.Id) : null;
        return SpellFacingRules.Turn(caster.SpellFlags, caster.IsCritter, caster.CritterFlags, Cell(caster.Location), run.Target?.Id == parent.Id,
            target is null ? null : Cell(target.Location));
    }

    private static CellCoord Cell(SpellLocation location) => new(unchecked((int)location.X), unchecked((int)location.Y));

    /// <summary>
    /// The casting goal's release frame (0x429C80): the target is checked again (0x456A90, <see cref="TargetStillValid"/>); then the
    /// run's BEGIN is processed (0x456F70) and its notes applied. A failed re-check begins nothing and frees the run unpaid in both settings
    /// (MG41: the original keeps it in its slot until the game ends, a slot leak; owner decision 2026-10-28, ARC-WORLD-108); null answers then.
    /// </summary>
    [OriginalFunction(0x429C80, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? Release(int run)
    {
        // The release gate fires once per goal (0x42CAA0, the frame flag 0x8): a run already released or begun answers null.
        if (_released.Contains(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
        // The release gate marks the goal's frame handled before the re-check (0x42CAA0): the goal's end then frees nothing.
        _released.Add(run);
        var begun = ReleaseRun(r);
        // A BEGIN the session rejected left the run untouched: its goal's end still frees it.
        if (begun?.Disposition == GameSessionDisposition.Rejected) _released.Remove(run);
        return begun;
    }

    // The release's re-check and BEGIN (0x429C80), for the casting goal and the missile's arrival.
    private SessionSpellRunResult? ReleaseRun(SpellRun r)
    {
        if (TargetStillValid(r)) return Act(new(SessionSpellRunActionKind.Begin, r.Id));
        Act(new(SessionSpellRunActionKind.Free, r.Id));
        return null;
    }

    /// <summary>
    /// ARC-MAGIC-018: the casting goal's release frame for a spell whose projectile eye candy has art (0x429C40, the host's test): the
    /// release gate marks the frame handled (0x42CAA0), so the goal's end frees nothing, and a missile is launched (0x4248A0, goal 28)
    /// instead of the release: no re-check and no BEGIN now; they come when it arrives (<see cref="ArriveMissile"/>). False, and
    /// nothing done, for a run already released or begun, or none.
    /// </summary>
    [OriginalFunction(0x429C40, OriginalFunctionScope.Partial)]
    public bool LaunchMissile(int run)
    {
        if (_released.Contains(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return false;
        _released.Add(run);
        _missiles.Add(run);
        return true;
    }

    /// <summary>
    /// ARC-MAGIC-018: the missile arrived (goal 28's release, 0x429C80): the target re-check and the run's BEGIN, as the casting goal's
    /// release does; a failed re-check frees the run unpaid in both settings (MG41). A BEGIN the session
    /// rejected leaves no goal to free the run: it is freed as a lost missile's is. Null when no missile of the run flies or the run
    /// no longer waits.
    /// </summary>
    public SessionSpellRunResult? ArriveMissile(int run)
    {
        if (!_missiles.Remove(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
        var begun = ReleaseRun(r);
        if (begun?.Disposition == GameSessionDisposition.Rejected) Act(new(SessionSpellRunActionKind.Free, run));
        return begun;
    }

    /// <summary>
    /// ARC-MAGIC-018: the missile's goal ended without its release (goal 28's end, 0x429E70: a leg whose line is refused, the map left,
    /// a point off the map): the original destroys the missile and frees nothing, so the run keeps its slot, never begun (MG44, owner
    /// decision 2026-10-28: fixed in both rule modes, no switch): the run is freed unpaid. Null when no missile of the run flies.
    /// </summary>
    [OriginalFunction(0x429E70, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? LoseMissile(int run)
    {
        if (!_missiles.Remove(run) || _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r)) return null;
        return Act(new(SessionSpellRunActionKind.Free, run));
    }

    /// <summary>ARC-MAGIC-018: whether a missile of the run flies.</summary>
    public bool MissileFlies(int run) => _missiles.Contains(run);

    /// <summary>
    /// The casting goal ended (0x429CD0). After its release frame (the goal's frame flag 0x8), or for a run whose BEGIN ran, nothing
    /// is freed and null answers; before it the run is freed with its BEGIN cost charged (0x456FA0, see <see cref="ChargeBegin"/>).
    /// A goal refused before its animation (<paramref name="refused"/>: action points, the pre-animation check or the target) ends
    /// the same way in the original (MG39); Steamwoven frees that run unpaid.
    /// </summary>
    [OriginalFunction(0x429CD0, OriginalFunctionScope.Partial)]
    public SessionSpellRunResult? EndGoal(int run, bool refused = false)
    {
        if (_released.Contains(run) || _owner.QuerySpellRuns()[run] is not { } r || (r.Flags & SpellRunFlags.Processed) != 0) return null;
        return Act(new(refused && !Profile.GoalRefusalChargesBegin ? SessionSpellRunActionKind.Free : SessionSpellRunActionKind.FreeCharged, run));
    }

    /// <summary>
    /// ARC-MAGIC-019 (SU13): a run the spell UI started (<see cref="CastAt"/>) while the caster's casting goal plays is queued beneath
    /// that goal (0x455C30's queue branch, 0x44D9B0): the new goal goes to the bottom of the caster's goal stack, so queued casts play
    /// in the order they were cast, each once the goals above it are done, and start then through <see cref="StartGoal"/>.
    /// <paramref name="depth"/> is the playing goal's stack index: the casts already queued plus its own subgoals. Refused
    /// (<see cref="SpellUiRules.QueueRefused"/>: depth 4 or more, or a turn-based round) the run is freed unpaid (0x4558D0), silently.
    /// </summary>
    [OriginalFunction(0x44D9B0, OriginalFunctionScope.Partial)]
    public SpellCastQueueOutcome Queue(int run, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        if (_owner.QuerySpellRuns()[run] is null) return SpellCastQueueOutcome.NoRun;
        if (!SpellUiRules.QueueRefused(depth, _owner.QueryCombatRound() is not null)) return SpellCastQueueOutcome.Queued;
        Act(new(SessionSpellRunActionKind.Free, run));
        return SpellCastQueueOutcome.Refused;
    }

    /// <summary>
    /// ARC-MAGIC-019: a queued cast whose goal never plays, because its caster's goal stack ended through the end step or was
    /// interrupted. Its only end node, the casting goal 26's animation reset (0x42B090), frees nothing, so in the original the run
    /// keeps its slot (MG43); fixed in both modes (owner decision 2026-10-28): a run still waiting for its BEGIN is freed, unpaid.
    /// Null when nothing was freed.
    /// </summary>
    public SessionSpellRunResult? DropQueued(int run) =>
        _owner.QuerySpellRuns()[run] is not { } r || !AwaitsBegin(r) ? null : Act(new(SessionSpellRunActionKind.Free, run));

    /// <summary>
    /// The release frame's target re-check (0x456A90): the record's BEGIN area passes at once when it is 0, exactly 0x10, or has
    /// the self bit without the tile bit (high word 0x8000); otherwise the picker's wrapper (0x4F2680) over the area test (MT8):
    /// a target object is the candidate and the target with no tiles (refused for object flag 0x800), retried with its location as
    /// both tiles; a target tile is both tiles. The wrapper's further global gate (0x603D20) is the picker's and not modelled.
    /// </summary>
    [OriginalFunction(0x456A90, OriginalFunctionScope.Partial)]
    public bool TargetStillValid(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_records(run.Spell) is not { } record || record.Areas.Length == 0) return true;
        var mask = record.Areas[0].Targets;
        if (mask == 0 || mask == 0x10 || ((mask & 0x1) != 0 && ((mask >> 32) & 0x8000) == 0)) return true;
        var basis = Context(run, record.Areas[0]);
        if (run.Target is { } t)
        {
            if (_map.Object(t.Id) is not { } o || (o.ObjectFlags & PickerRefused) != 0) return false;
            var asObject = basis with { Candidate = o, Target = o, CandidateTile = SpellLocation.None, TargetTile = SpellLocation.None };
            return SpellTargetRules.Test(asObject, _map, Profile).Passes
                || SpellTargetRules.Test(asObject with { CandidateTile = o.Location, TargetTile = o.Location }, _map, Profile).Passes;
        }
        return SpellTargetRules.Test(basis with { Candidate = null, Target = null, CandidateTile = run.TargetTile, TargetTile = run.TargetTile }, _map,
            Profile).Passes;
    }

    /// <summary>
    /// The spell UI's cast at a picked target (0x57C110, ARC-MAGIC-015) by <paramref name="caster"/>, which is the run's parent and,
    /// without <paramref name="item"/>, its source: nothing when the caster is missing, dead or unconscious; a critter caster must afford the spell's <c>Cost:</c> (0x4508A0:
    /// <see cref="SpellCostRules.CheckCost"/>, the payment asked without applying), else spell.mes 600; the invocation (0x455A20) takes
    /// the target object, or else the tile; no line of fire (0x456BC0) shows spell.mes 604; then the run starts (0x455AC0: the
    /// invocation check, a slot and the run), whose casting goal is the host's (<see cref="StartGoal"/>).
    /// ARC-MAGIC-022: with <paramref name="item"/> (an item the host knows that <paramref name="caster"/> holds) the item is the
    /// invocation's source and the caster its parent, with the power the caster draws from it, as the item use's slot-0 cast sets the
    /// activation's source (0x57BC70: an item's holder is the caster); an item source skips the cost check.
    /// </summary>
    [OriginalFunction(0x57C110, OriginalFunctionScope.Partial)]
    public SpellCastAttempt CastAt(string caster, int spell, string? targetObject, SpellLocation targetTile, string? item = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (Life(caster) is not { } life || life.Status.IsDead || life.Status.IsUnconscious) return new(SpellCastAttemptOutcome.Nothing);
        if (item is not null && Item(item)?.Holder != caster) return new(SpellCastAttemptOutcome.Nothing);
        if (_records(spell) is not { } record || RunObject(item ?? caster) is not { } source || Facts(caster) is not { } facts ||
            (item is null ? facts : Facts(item)) is not { } sourceFacts)
            return new(SpellCastAttemptOutcome.Nothing);
        if (source.IsCritter)
        {
            var cost = SpellCostRules.CheckCost(record.Run.Cost, Caster(source), false);
            if (Payer(source) is not { } payer || !SpellPaymentRules.Pay(payer, cost, false, spell, record.Run.Payment, Cells).Succeeded)
                return Refused(SpellCastAttemptOutcome.CannotAfford, SpellUiRules.CannotAffordMessage);
        }
        var target = targetObject is null ? null : Facts(targetObject);
        if (targetObject is not null && target is null) return new(SpellCastAttemptOutcome.Nothing);
        var invocation = new SpellInvocation(spell, facts, sourceFacts, target, target is null ? targetTile : SpellLocation.None,
            item is null ? null : ItemPower(Item(item)!, facts), TeleportAllowed: TeleportAllowedFor(spell, caster), ParentIsLocalPlayer: caster == Hero);
        var world = InvocationWorld();
        if (!SpellInvocationRules.HasLineOfFire(invocation, world)) return Refused(SpellCastAttemptOutcome.NoLineOfFire, SpellUiRules.NoLineOfFireMessage);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation), record.Run, world, _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run)
        {
            // ARC-MAGIC-038 (0x4564E0): the anti-teleport refusal of Teleportation shows message 10000 to the local player only.
            if (started.Creation?.Check is { ShowsTeleportMessage: true }) _log.Add(new(SpellCastEventKind.Message, -1, TeleportRefusedMessage));
            return new(SpellCastAttemptOutcome.Refused, null, started);
        }
        _itemChains.Remove(run.Id);
        return new(SpellCastAttemptOutcome.Started, null, started, run.Id);
    }

    private SpellCastAttempt Refused(SpellCastAttemptOutcome outcome, int message)
    {
        _log.Add(new(SpellCastEventKind.Message, -1, message));
        return new(outcome, message);
    }

    /// <summary>
    /// The targeting cursor's test of a candidate (0x4F2830's area test, ARC-MAGIC-015): the BEGIN area with <paramref name="mask"/>
    /// (the cursor's) for <paramref name="caster"/> as self and source, the candidate object (if any) as candidate and target and the
    /// tile (none with an object, except for the picker's second test, ARC-MAGIC-021) as both tiles. The answer carries the area
    /// test's failure reason for the cursor's message.
    /// </summary>
    public SpellTargetTest TestTarget(string caster, int spell, ulong mask, string? candidate, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (_records(spell) is not { } record || record.Areas.Length == 0 || _map.Object(caster) is not { } self) return new(false);
        var basis = new SpellTargetContext(record.Areas[0] with { Targets = mask }, self, self, self.Location, CandidateTile: tile, TargetTile: tile);
        if (candidate is not null)
            return _map.Object(candidate) is { } o && (o.ObjectFlags & PickerRefused) == 0
                ? SpellTargetRules.Test(basis with { Candidate = o, Target = o }, _map, Profile) : new(false);
        return SpellTargetRules.Test(basis, _map, Profile);
    }

    /// <summary>
    /// ARC-MAGIC-021: the targeting cursor's pick (0x4F28A0, <see cref="SpellUiRules.Pick"/>) for <paramref name="caster"/> as the
    /// local PC: the object the screen pick found under the point (<paramref name="atPoint"/>, a map id, found without the objects
    /// <see cref="PickSpares"/> names), else the objects on <paramref name="tile"/> (null: no tile under the point) in the map's order,
    /// else the tile, each through <see cref="TestTarget"/>. The caster's followers are the map's party of the caster.
    /// </summary>
    public SpellPick PickTarget(string caster, int spell, ulong mask, string? atPoint, SpellLocation? tile)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var followers = Followers(caster);
        return SpellUiRules.Pick(mask, caster, followers, atPoint is null ? null : _map.Object(atPoint), tile, _map,
            (o, at) => TestTarget(caster, spell, mask, o?.Id, at));
    }

    /// <summary>ARC-MAGIC-021: which map objects the pick sees through for this mask (SU15, <see cref="SpellUiRules.PickSpares"/>), the
    /// screen pick's extra skip while the cursor is open; the followers are read once, when it is made.</summary>
    public Func<string, bool> PickSpared(string caster, ulong mask)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var followers = Followers(caster);
        return id => _map.Object(id ?? throw new ArgumentNullException(nameof(id))) is { } o && SpellUiRules.PickSpares(mask, o, caster, followers);
    }

    // The follower list of the pick (0x441260): the map's party of the caster, the caster left out.
    private HashSet<string> Followers(string caster) =>
        _map.Object(caster) is { } self ? _map.Party(self).Select(o => o.Id).Where(id => id != caster).ToHashSet(StringComparer.Ordinal) : [];

    // A run whose BEGIN has not run yet: the only one a release frame begins.
    private static bool AwaitsBegin(SpellRun run) => run.Action == SpellRunAction.Begin && (run.Flags & SpellRunFlags.Processed) == 0;

    /// <summary>The object flag the picker's wrapper refuses an object target for (0x4F2680).</summary>
    public const int PickerRefused = SpellUiRules.ClickThrough;

    /// <summary>One action on the session's runs through this host, with its notes applied.</summary>
    public SessionSpellRunResult Act(SessionSpellRunAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = _owner.QuerySpellRuns();
        var result = _owner.ActOnSpellRun(_owner.Version, this, action);
        Apply(result.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        FirePendingItems();
        return result;
    }

    /// <summary>ARC-MAGIC-026: a click on maintain-bar slot <paramref name="slot"/> through this host
    /// (<see cref="GameSessionOwner.StopMaintainedSpell"/>), with its notes applied.</summary>
    public SessionSpellRunResult StopMaintainedSpell(int slot)
    {
        var before = _owner.QuerySpellRuns();
        var result = _owner.StopMaintainedSpell(_owner.Version, this, slot);
        Apply(result.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        FirePendingItems();
        return result;
    }

    /// <summary>
    /// An event pass with this host as the magic world (the magic-tech events run through it) and, when the host's event world is a
    /// script world, that world for deferred SCR calls. The notes of the firings that ran are applied after the pass, so several
    /// ticks of one run in a pass are each checked against the fatigue before the pass.
    /// </summary>
    public SessionEventPassResult ProcessEvents(int budget = 500)
    {
        var before = _owner.QuerySpellRuns();
        ISessionEventWorld world = _events switch
        {
            ISessionAiWorld ai => new AiPassWorld(this, ai),
            ISessionScriptWorld scripts => new ScriptPassWorld(this, scripts),
            _ => new PassWorld(this, _events),
        };
        var pass = _owner.ProcessEvents(world, budget);
        // ARC-MAGIC-024: the pass's life steps call for theirs too (the encumbrance drain, a poison death fires nothing without a killer).
        foreach (var firing in pass.Life) _itemEvents.AddRange(firing.Life.ItemEvents);
        foreach (var firing in pass.SpellRuns.Where(f => f.Ran)) Apply(firing.Notes, id => _owner.QuerySpellRuns()[id] ?? before[id]);
        FirePendingItems();
        return pass;
    }

    /// <summary>
    /// ARC-MAGIC-024: fires <paramref name="events"/> (a life step's <see cref="SessionLifeResult.ItemEvents"/> the host made itself:
    /// a run step's fatigue, the overexertion, a script's damage or kill) after any this host has not fired yet, through
    /// <see cref="ItemTriggers"/> (<see cref="GameSessionOwner.FireItemEvents"/> with the map's worn items of an NPC), in order; the
    /// events the started runs' life steps call for follow, up to <see cref="MaxItemEventsPerFiring"/> in all (the rest are dropped
    /// and logged). During a firing the events only join it; a call the session refuses (from inside the run rules or an event pass)
    /// leaves them for the next firing. Nothing without <see cref="ItemTriggers"/>: the events are dropped.
    /// </summary>
    public IReadOnlyList<SessionItemTriggerFiring> FireItemEvents(IEnumerable<SessionItemEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var list = events.ToArray();
        if (list.Any(e => e is null)) throw new ArgumentException("Null item event.", nameof(events));
        _itemEvents.AddRange(list);
        return FirePendingItems();
    }

    /// <summary>ARC-MAGIC-024: the dying animation of <paramref name="critter"/> ended (0x42FF40, IC5): its dying with no other object
    /// when it is dead (<see cref="GameSessionOwner.QueryDyingEndItemEvents"/>), fired as <see cref="FireItemEvents"/> does.</summary>
    public IReadOnlyList<SessionItemTriggerFiring> DyingAnimationEnded(string critter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        return FireItemEvents(LifeTarget(critter) is { } target ? _owner.QueryDyingEndItemEvents(target) : []);
    }

    private IReadOnlyList<SessionItemTriggerFiring> FirePendingItems()
    {
        if (_firingItems) return [];
        TellGoneObjects();
        var fired = FireItemLoop();
        // A firing's runs may have destroyed objects (a grenade's item event): their runs hear of it once the firing is over.
        TellGoneObjects();
        return fired;
    }

    private IReadOnlyList<SessionItemTriggerFiring> FireItemLoop()
    {
        if (ItemTriggers is not { } triggers)
        {
            _itemEvents.Clear();
            return [];
        }
        var fired = new List<SessionItemTriggerFiring>();
        _firingItems = true;
        try
        {
            for (var walked = 0; _itemEvents.Count > 0; walked++)
            {
                if (walked == MaxItemEventsPerFiring)
                {
                    _log.Add(new(SpellCastEventKind.ItemEventsDropped, -1, _itemEvents.Count));
                    _itemEvents.Clear();
                    break;
                }
                // A call from inside the run rules or an event pass (a host's callback) is refused: the events wait for the outer
                // action's or pass's firing.
                _firingChain = _itemEvents[0].Chain;
                var result = _owner.FireItemEvents(triggers, [_itemEvents[0]], WornItemIds, IsItem);
                _firingChain = null;
                if (result.Failure != GameSessionFailure.None) break;
                _itemEvents.RemoveAt(0);
                fired.AddRange(result.Firings);
            }
        }
        catch
        {
            // A host's executor that throws ends the firing: nothing left over fires on a later, unrelated action.
            _itemEvents.Clear();
            throw;
        }
        finally
        {
            _firingItems = false;
            _firingChain = null;
        }
        return fired;
    }

    // ARC-MAGIC-025: an object of the map (or an item a critter holds) of an item type, 5..14 (0x4CBE70's item branch).
    private bool IsItem(string id) => _map.Object(id) is { Type: >= SpellObjectFacts.FirstItemType and <= SpellObjectFacts.LastItemType };

    // MG46: a run an item event started remembers the chain of that event (none outside a firing).
    private void Chained(int run, IReadOnlyCollection<string>? chain) => _itemChains[run] = (_owner.QuerySpellRuns()[run]?.Source?.Id, chain ?? []);

    // MG46: the chain of the events a run's damage step calls for: for a run an item event started, the chain it was started in and its
    // own source; null for any other run.
    private IReadOnlyCollection<string>? ChainOf(SpellRun run)
    {
        if (run.Triggers == 0) return null;
        var known = _itemChains.TryGetValue(run.Id, out var entry) && entry.Source == run.Source?.Id ? entry.Chain : [];
        return run.Source?.Id is { } source && !known.Contains(source) ? [.. known, source] : known;
    }

    // An NPC's worn items in slot order (the map's), or null for an entity that is no NPC of the map.
    private IReadOnlyList<string>? WornItemIds(string id) =>
        id != Hero && _map.Critter(id) is not null ? [.. _map.WornItems(id).OrderBy(item => item.Slot).Select(item => item.Id)] : null;

    // The pass world: this host for the magic-tech events, the host's event world for the rest (its party facts when it has them; a
    // world without them answers none, as the pass then does; ARC-WORLD-096: the session asks the wrapped world for the loot window
    // and the restock host).
    private class PassWorld(SessionSpellCaster caster, ISessionEventWorld events) : ISessionSpellWorld, ISessionPartyEventWorld, ISessionWrappingEventWorld
    {
        public ISpellRunHost SpellRuns => caster;
        public ISessionEventWorld Wrapped => events;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => events.CritterFacts(map, entity);
        public ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => (events as ISessionPartyEventWorld)?.ReactionNpcFacts(map, entity);
        public ReactionPcFacts? ReactionPcFacts() => (events as ISessionPartyEventWorld)?.ReactionPcFacts();
    }

    // The pass world of a host whose event world also runs deferred script calls.
    private class ScriptPassWorld(SessionSpellCaster caster, ISessionScriptWorld scripts) : PassWorld(caster, scripts), ISessionScriptWorld
    {
        public bool ObjectExists(SessionMapId map, string entity) => scripts.ObjectExists(map, entity);
        public bool RunScriptCall(SessionScriptCall call) => scripts.RunScriptCall(call);
    }

    // ARC-WORLD-092: the pass world of a host whose event world is also the AI scheduler's host, so the AI ticks run through it.
    private sealed class AiPassWorld(SessionSpellCaster caster, ISessionAiWorld ai) : ScriptPassWorld(caster, ai), ISessionAiWorld
    {
        public SessionLifeTarget? AiLifeTarget(string entity) => ai.AiLifeTarget(entity);
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => ai.ScanCombatants(map);
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => ai.CombatantFacts(map, entity);
        public long DistanceToPc(string npc) => ai.DistanceToPc(npc);
        public bool RunScript(AiScriptPoint point, string npc, string? triggerer) => ai.RunScript(point, npc, triggerer);
        public bool RunWaypoints(string npc) => ai.RunWaypoints(npc);
        public void RunStandpoints(string npc) => ai.RunStandpoints(npc);
        public bool IsGenerator(string npc) => ai.IsGenerator(npc);
        public long? Spawn(string npc) => ai.Spawn(npc);
        public bool IsEncounterCreature(string npc) => ai.IsEncounterCreature(npc);
        public void Destroy(string npc) => ai.Destroy(npc);
        public void InterruptAnimations(string npc) => ai.InterruptAnimations(npc);
        public bool SectorLoaded(string npc) => ai.SectorLoaded(npc);
        public AiCatchUpFacts CatchUp(string npc) => ai.CatchUp(npc);
        public void MoveToLeader(string npc) => ai.MoveToLeader(npc);
        public bool MapClearing => ai.MapClearing;
        public bool Sleeping(string npc) => ai.Sleeping(npc);
        public bool Hidden(string npc) => ai.Hidden(npc);
        public bool PcHidden => ai.PcHidden;
        public AiConversation Conversation(string npc) => ai.Conversation(npc);
        public bool ConversationCallback => ai.ConversationCallback;
        public void EndConversation(string npc) => ai.EndConversation(npc);
        public int AnimationPriority(string npc) => ai.AnimationPriority(npc);
        public bool StunAnimationRunning(string npc) => ai.StunAnimationRunning(npc);
        public bool AnimationRunning(string npc) => ai.AnimationRunning(npc);
        public void ReWield(string npc, bool allSlots) => ai.ReWield(npc, allSlots);
        public string? Leader(string npc) => ai.Leader(npc);
        public void EndMaintainedSpells(string npc) => ai.EndMaintainedSpells(npc);
        public bool Heal(string npc) => ai.Heal(npc);
        public bool SearchItems(string npc) => ai.SearchItems(npc);
        public void UpdateDanger(string npc) => ai.UpdateDanger(npc);
        public void Perform(string npc) => ai.Perform(npc);
        public bool Loading => ai.Loading;
    }

    private SessionCombatant? Combatant(string id) =>
        id == Hero ? SessionCombatant.Hero : _map.Critter(id) is not null ? SessionCombatant.Critter(id) : null;

    // ---- notes ----

    /// <summary>
    /// Applies what the run rules left for the host (<see cref="SpellRunNote"/>): a payment's fatigue (the stage's from the run's
    /// source, a reflection's from its payer; worn mana stores are not modelled yet), and records the local player's messages,
    /// resisting targets and freed runs in <see cref="Log"/>.
    /// </summary>
    public void Apply(IReadOnlyList<SpellRunNote> notes, Func<int, SpellRun?> runs)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(runs);
        foreach (var note in notes)
        {
            switch (note.Kind)
            {
                case SpellRunNoteKind.Payments when runs(note.Run)?.Source is { } source:
                    foreach (var payment in note.Payments ?? []) Pay(note.Run, source.Id, payment);
                    break;
                case SpellRunNoteKind.ReflectionPayment when note.Target?.Object is { } payer:
                    foreach (var payment in note.Payments ?? []) Pay(note.Run, payer.Id, payment);
                    break;
                case SpellRunNoteKind.Message: _log.Add(new(SpellCastEventKind.Message, note.Run, note.Value)); break;
                case SpellRunNoteKind.Resisted:
                    _log.Add(new(SpellCastEventKind.Resisted, note.Run, 0, note.Target?.Object?.Id));
                    if (IsLocalPlayer(runs(note.Run)?.Parent)) _log.Add(new(SpellCastEventKind.Message, note.Run, ResistedMessage));
                    break;
                case SpellRunNoteKind.Freed: _released.Remove(note.Run); _missiles.Remove(note.Run); _log.Add(new(SpellCastEventKind.Freed, note.Run)); break;
            }
        }
    }

    /// <summary>spell.mes: a resisting target (the host's feedback, 0x45A520).</summary>
    public const int ResistedMessage = 643;

    private void Pay(int run, string payer, SpellPayment payment)
    {
        // ARC-MAGIC-022: an item source's spent charge (and with it the magical flag) is saved. The notes of an event pass are applied
        // after the pass, so two payments of one pass were both made against the count before it: one that finds no charge left
        // spends nothing (the original refuses the second, NoCharges). A spend the session refuses (a full record budget) is logged.
        if (payment.ChargeSpent)
        {
            if (_items?.Invoke(payer) is not { } raw || raw.Entity != payer) return;
            var left = _owner.QueryItemCharges(payer) ?? raw.Charges;
            if (left <= 0) return;
            var spent = _owner.SpendItemCharge(_owner.Version, payer, Math.Max(1, raw.Charges));
            _log.Add(spent.Disposition == GameSessionDisposition.Changed ? new(SpellCastEventKind.ChargeSpent, run, left - 1, payer)
                : new(SpellCastEventKind.ChargeNotSaved, run, (int)spent.Failure, payer));
            return;
        }
        if (payment.FatigueDamageAdded <= 0 || LifeTarget(payer) is not { } target) return;
        var result = _owner.PaySpellFatigue(_owner.Version, target, payment.FatigueDamageAdded);
        if (result.Disposition == GameSessionDisposition.Changed) _log.Add(new(SpellCastEventKind.FatiguePaid, run, payment.FatigueDamageAdded, payer));
        _itemEvents.AddRange(result.ItemEvents);
    }

    private void ApplyDamage(SpellRun run, SpellDamageComponent data, SpellRunObject target)
    {
        var result = SpellLifeComponents.Damage(data, run, target, this, Rolls, Profile);
        foreach (var step in result.Steps)
        {
            // ARC-DEV-261 (GT198, DH1): the victim's taking-damage script comes first for each damaged object, with the run's parent as the attacker; a veto cancels that victim's damage step only.
            if (TakingDamage is { } asks && !asks(step.Object, run.Parent?.Id)) { _log.Add(new(SpellCastEventKind.DamageVetoed, run.Id, 0, step.Object)); continue; }
            if (step.Life is { } life && LifeTarget(step.Object) is { } who)
            {
                // DH1: the run's parent is the attacker, its other object (else its parent) the damage source.
                var maxHitPoints = Life(step.Object)?.Status.MaxHitPoints ?? 1;
                var source = (run.Other ?? run.Parent)?.Id;
                var applied = _owner.ApplyDamage(_owner.Version, who, life, new(run.Parent?.Id, source));
                if (applied.Outcome is { } outcome)
                {
                    _log.Add(new(SpellCastEventKind.Damaged, run.Id, outcome.HitPointDamage, step.Object));
                    QueueDamageStep(step.Object, step.Damage, outcome, run.Parent?.Id);
                    // ARC-DEV-295 (XP9, XP10): the same damage function pays the pool share and the kill award, with the source as the killer.
                    RecordDamageExperience(step.Object, outcome, maxHitPoints, source, run.Id, applied.DeathStopped);
                }
                // MG46: the damage step of a run an item event started carries its chain.
                var chain = ChainOf(run);
                _itemEvents.AddRange(chain is null ? applied.ItemEvents : applied.ItemEvents.Select(e => e with { Chain = chain }));
                Struck(step.Object, applied);
            }
            else if (step.Life is null && step.Damage.Prevented == DamagePrevention.None && _map.DamageObject(step.Object, step.Damage))
                _log.Add(new(SpellCastEventKind.Damaged, run.Id, step.Damage.HitPointDamage, step.Object));
        }
    }

    private void ApplyHeal(SpellRun run, SpellDamageComponent data, SpellRunObject target)
    {
        if (LifeTarget(target.Id) is not { } who) return;
        var before = _owner.QueryLife(who).View?.Status.HitPoints ?? 0;
        // The raise script (point 0x12) runs no SCR yet: it allows the raise.
        var healed = _owner.Heal(_owner.Version, who, SpellLifeComponents.Heal(data, run, Rolls), () => true);
        _itemEvents.AddRange(healed.ItemEvents);
        if (healed.Disposition == GameSessionDisposition.Changed)
            _log.Add(new(SpellCastEventKind.Healed, run.Id, (_owner.QueryLife(who).View?.Status.HitPoints ?? 0) - before, target.Id));
    }

    // ---- helpers ----

    private SpellTargetContext Context(SpellRun run, SpellTargetArea area)
    {
        var self = run.Parent is { } p ? _map.Object(p.Id) : null;
        var source = run.Source is { } s ? _map.Object(s.Id) : null;
        var origin = source?.Location ?? run.StartLocation;
        return new(area, self, source, origin, Target: run.Target is { } t ? _map.Object(t.Id) : null, TargetTile: run.TargetTile,
            Other: run.Other is { } o ? _map.Object(o.Id) : null, Summoned: LastSummoned, SummonedList: SummonedOf(run));
    }

    /// <summary>The session's life target of a critter (the hero, or an NPC of the map), or null.</summary>
    public SessionLifeTarget? LifeTarget(string id) => id == Hero ? _owner.QueryHero() is null ? null : SessionLifeTarget.Hero : _map.Critter(id)?.Combat.Target;

    private SessionLifeView? Life(string id) => LifeTarget(id) is { } target ? _owner.QueryLife(target).View : null;

    private SpellCaster Caster(SpellRunObject? source)
    {
        if (source is null || !source.IsCritter) return default;
        if (source.Id == Hero && _owner.QueryHero() is { } hero)
            return new(true, (int)hero.Race, _owner.QueryHeroSpellBook()?.MasteredCollege ?? SpellBookRules.NoMastery);
        // ARC-WORLD-102: the session's record of the NPC, else the book its fields store.
        return _map.Critter(source.Id) is { } npc ? new(true, (int)npc.Combat.Race, (_owner.QueryNpcSpellBook(source.Id) ?? npc.Book)?.MasteredCollege ?? SpellBookRules.NoMastery)
            : new(true, 0, SpellBookRules.NoMastery);
    }

    private int? HeroStat(CharacterStat stat) => _owner.HeroEffectiveStat(stat);

    private int? NpcStat(SpellCastCritter npc, CharacterStat stat) =>
        _owner.QueryLife(npc.Combat.Target).View is { } life ? _owner.WithNpcEffects(npc.Combat).EffectiveStat(stat, life.Record.State) : null;

    // The original's stat numbers the character owner models (0..7 primary, 8..16 derived).
    private static CharacterStat? CharacterStatOf(int stat) => stat switch
    {
        >= 0 and <= 7 => (CharacterStat)stat,
        8 => CharacterStat.CarryWeight, 9 => CharacterStat.DamageBonus, 10 => CharacterStat.AcAdjustment, 11 => CharacterStat.Speed,
        12 => CharacterStat.HealRate, 13 => CharacterStat.PoisonRecovery, 14 => CharacterStat.ReactionModifier,
        15 => CharacterStat.MaxFollowers, 16 => CharacterStat.MagickTechAptitude,
        _ => null,
    };

    private sealed class NoCells : ISpellCellSource
    {
        public static readonly NoCells Instance = new();

        public SpellCellPayment Pay(SpellCellRequest request) => new(false, 0);
    }
}
