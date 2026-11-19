using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>A session object a script event names: the runtime map (positive) and the stable entity code (1..128
/// ASCII letters, digits and <c>. _ : -</c>). Never a live handle.</summary>
public readonly record struct ScrObjectRef
{
    public ScrObjectRef(int map, string code)
    {
        if (map <= 0) throw new ArgumentOutOfRangeException(nameof(map), "A runtime map is positive.");
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length is 0 or > 128 || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')))
            throw new ArgumentException("An entity code is 1..128 ASCII letters, digits and . _ : -.", nameof(code));
        Map = map; Code = code;
    }

    public int Map { get; }
    public string Code { get; }
}

/// <summary>One script attachment as an object, tile or sector stores it: the script number (0 = none), the 32 local
/// flags and the four byte counters (counter 0 in the low byte).</summary>
public readonly record struct ScrSlot(int Script, uint LocalFlags, uint Counters);

/// <summary>Where the attachment of a run lives, which decides how it is written back.</summary>
public enum ScrSource { Object, Tile, Sector }

/// <summary>The attachment points (0..35) the dispatcher treats by name.</summary>
public static class ScrPoint
{
    public const int Examine = 0, Destroy = 2, Use = 1, Dialog = 9, FirstHeartbeat = 10, Dying = 12, EnterCombat = 13, ExitCombat = 14, Raise = 18, Heartbeat = 19, TakingDamage = 23, NewSector = 27, StartCombat = 15, EndCombat = 16, LeaderSleeping = 29, Bust = 30, DialogOverride = 31, Count = 36,
        // ARC-MAGIC-042 (TI4): the weapon's hit (7) and miss (8) points, run for a thrown item's strike or landing.
        Hit = 7, Miss = 8,
        // ARC-SCRIPT-1483: the attacker's critter-hits point (26) and the weapon's critical-hit point (34), run from the hit step with the hit point (7).
        CritterHits = 26, CriticalHit = 34,
        // ARC-COMBAT-1719: the weapon's critical-miss point (35), run after the miss point (8) at the range end of a critical shot (0x4B2870).
        CriticalMiss = 35;
}

/// <summary>
/// An event on an object's attachment point: the attachee whose slot runs, the triggerer and the extra object (either
/// may be absent), and the line to start at (0 for every engine event; a dialog resumption starts elsewhere).
/// </summary>
public sealed record ScrObjectEvent(int Point, ScrObjectRef Attachee, ScrObjectRef? Triggerer, ScrObjectRef? Extra, int Line = 0);

/// <summary>A critter stepping onto a tile that holds <paramref name="Slot"/>: point use, line 0, no attachee.</summary>
public sealed record ScrTileEvent(ScrSlot Slot, ScrObjectRef Triggerer);

/// <summary>The player entering a sector whose script is <paramref name="Slot"/>: point use, line 0, no attachee.</summary>
public sealed record ScrSectorEvent(ScrSlot Slot, ScrObjectRef Player);

public enum ScrScriptStatus { Found, Missing, Unreadable }

/// <summary>A script the host found by number (a framed program), or why not.</summary>
public sealed record ScrScriptLookup(ScrScriptStatus Status, ScrProgram? Program)
{
    public static ScrScriptLookup Missing { get; } = new(ScrScriptStatus.Missing, null);
    public static ScrScriptLookup Unreadable { get; } = new(ScrScriptStatus.Unreadable, null);
    public static ScrScriptLookup Of(ScrProgram program) => new(ScrScriptStatus.Found, program ?? throw new ArgumentNullException(nameof(program)));
}

/// <summary>What the dialog gates of an NPC attachee see (ARC-RES-042 E6); any combination. The original tests dead,
/// then surrendered, then mind-controlled, each on its own.</summary>
[Flags]
public enum ScrDialogState { Normal = 0, Dead = 1, Surrendered = 2, MindControlled = 4 }

/// <summary>The trap spot attempt before a use event (E2): no unspotted trap on the attachee, the trap stayed hidden,
/// or the triggerer spotted it (the use is cancelled).</summary>
public enum ScrTrapCheck { NoTrap, NotSpotted, Spotted }

/// <summary>
/// What the dispatcher reads from the game while it decides and runs. It never writes: every change of a run comes
/// back in <see cref="ScrEffects"/>, which the caller applies as one step. Calls are synchronous; a host must answer
/// from one consistent state for the whole call.
/// </summary>
public interface IScrHost
{
    /// <summary>The attachee's slot for the point, or null when it has none.</summary>
    ScrSlot? Slot(ScrObjectRef attachee, int point);
    /// <summary>The script with this number; any number may be asked (a call script's operand is a value), and one with no file
    /// is <see cref="ScrScriptLookup.Missing"/>.</summary>
    ScrScriptLookup Script(int number);
    /// <summary>Global flag 0..3199.</summary>
    bool GlobalFlag(int index);
    int StoryState();
    /// <summary>An NPC that is a monster generator (its slots never run, E5).</summary>
    bool IsMonsterGenerator(ScrObjectRef attachee);
    /// <summary>Asked only for a dialog event; <see cref="ScrDialogState.Normal"/> for anything that is not an NPC; a
    /// combination of the three states otherwise.</summary>
    ScrDialogState DialogState(ScrObjectRef attachee);
    /// <summary>Asked only for a use event with a triggerer, before the script; the host owns the roll.</summary>
    ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee);
    /// <summary>A trap object (destroyed instead of written back when its script becomes 0, E3).</summary>
    bool IsTrapObject(ScrObjectRef attachee);
    /// <summary>The player character the player focus names, or null when there is none.</summary>
    ScrObjectRef? Player();
    /// <summary>The critter's leader, or null when it follows no one.</summary>
    ScrObjectRef? Leader(ScrObjectRef critter);
    /// <summary>
    /// ARC-SCRIPT-1899: whether two references name one object. The original keeps one critter wherever it stands, so a carried follower named by its home map and the same one named by the map that shows it are one object; a host that
    /// can tell them apart answers by identity. The default is the plain reference test (the following test uses it to compare a leader with the second object of the condition).
    /// </summary>
    bool SameObject(ScrObjectRef a, ScrObjectRef b) => a == b;
    /// <summary>An NPC with the jilted flag; false for anything that is not an NPC.</summary>
    bool IsJilted(ScrObjectRef critter);
    /// <summary>A player character (the dialog action starts a conversation only for one).</summary>
    bool IsPc(ScrObjectRef critter);
    /// <summary>ARC-SCRIPT-2440 (ledger TI5, F11): whether the host applies a transform (40) of the run's attachee so that the later actions of the same run name the new object (the original writes the new handle into the run's own record). When true the
    /// interpreter hands the later actions the placeholder <see cref="ScrTransformedObject.Placeholder"/> instead of the old attachee, and the host rebinds it to the made object (<see cref="ScrEffects.Rebind"/>) when it applies the request; a host that
    /// returns false (the default) keeps the old attachee, and reads the object after the transform itself.</summary>
    bool RebindsTransformedAttachee => false;
}

/// <summary>ARC-SCRIPT-2440: the stand-in a run names for the object its transform (40) makes, before any host has made it.</summary>
public static class ScrTransformedObject
{
    private const string Prefix = "transformed:";

    /// <summary>The placeholder of the <paramref name="number"/>th transform (from 1) of a run, on the map of the object that was transformed.</summary>
    public static ScrObjectRef Placeholder(int map, int number) => new(map, Prefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Whether the object is the placeholder of a transform's result.</summary>
    public static bool Is(ScrObjectRef obj) => obj.Code.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>
/// ARC-SCRIPT-014: the object questions of the guard conditions of set <see cref="ScrAdmissionSet.S4"/> (ledger SG1). A host
/// whose admission set holds them implements this interface; the dispatcher asks only for the objects the conditions name.
/// </summary>
public interface IScrObjectQueries
{
    /// <summary>The state of the quest the condition compares (SG1): a PC's own state of it (6 when its entry is marked
    /// botched); for any other object the quest's global state, whatever the object.</summary>
    int QuestState(ScrObjectRef obj, int quest);
    /// <summary>Whether a direct inventory item of the object has this name number (SG2: a container's or a critter's
    /// inventory, no nested search); false for an object without an inventory.</summary>
    bool HoldsItemNamed(ScrObjectRef obj, int name);
    /// <summary>The object's name number (its name field).</summary>
    int NameOf(ScrObjectRef obj);
    /// <summary>Whether the object is mind-controlled (its spell flags); such an object is never "named".</summary>
    bool IsMindControlled(ScrObjectRef obj);
    /// <summary>The name number of the item the object wields in inventory location 1000..1008, or null for an empty one.</summary>
    int? WieldedItemName(ScrObjectRef obj, int location);
    /// <summary>Whether the object has the reputation (SG5: only a PC has reputations).</summary>
    bool HasReputation(ScrObjectRef obj, int reputation);
    /// <summary>ARC-SCRIPT-018: the object's type (its type field, OV3: 15 for a PC, 16 for an NPC), or null when the host
    /// does not know the object (the run then stops as for an absent object).</summary>
    int? ObjectType(ScrObjectRef obj);
}

/// <summary>
/// ARC-SCRIPT-050: the area question of get area (action 84, ledger GA1–GA3). A host whose admission set holds the action implements this
/// interface; the interpreter asks only for the object the action names.
/// </summary>
public interface IScrAreaQueries
{
    /// <summary>The area get area stores for the object (0x4CB6A0): the current map's map-list area (0x40FFE0, 0 for a map without a row)
    /// whatever the object's map, and only when that is 0 and the current map is the start map, the area at the object's location
    /// (0x4CB630); null when the host cannot answer (the dispatch then ends with <see cref="ScrStop.NotModelled"/> and applies nothing).</summary>
    int? AreaOf(ScrObjectRef obj);
}

/// <summary>
/// ARC-DEV-178 (bug B19): the reaction question of get reaction (action 29, ledger GR1). A host whose admission set holds the action implements
/// this interface; the interpreter asks only for the first object of each of the action's two object operands.
/// </summary>
public interface IScrReactionQueries
{
    /// <summary>The standard effective reaction (0x4C0CC0) of <paramref name="npc"/> toward <paramref name="pc"/>: 0 while the second object is on the
    /// NPC's enemy list, 50 when the second is not a PC or the first not an NPC, else the NPC's stored reaction with the race, modifier and
    /// reputation terms and its reaction effects, raised to 50 when the PC mind-controls the NPC; null when the host cannot answer (the dispatch then ends with <see cref="ScrStop.NotModelled"/> and applies
    /// nothing).</summary>
    int? ReactionOf(ScrObjectRef npc, ScrObjectRef pc);
}

/// <summary>
/// ARC-DEV-157: the stat question of get stat (action 33, ledger GS1–GS4). A host whose admission set holds the action implements this
/// interface; the interpreter asks only for the object the action names.
/// </summary>
public interface IScrStatQueries
{
    /// <summary>The object's effective stat <paramref name="stat"/> as the original's stat getter (0x4B0490) gives it: the base value plus the
    /// effects' changes, clamped, with the situational penalties of the getter. Null when the host cannot answer (the object is not a PC or an
    /// NPC the host knows, or the stat number is one it does not model); the dispatch then ends with <see cref="ScrStop.NotModelled"/> and
    /// applies nothing.</summary>
    int? EffectiveStat(ScrObjectRef obj, int stat);

    /// <summary>Whether the object counts as dumb because of its background (55, 74 or 75, the dumb test 0x45FC00, ledger BG5); the
    /// interpreter asks it only for intelligence (stat 4) above 4, where get stat then stores 1. False for an object the host does not know.</summary>
    bool IsDumbByBackground(ScrObjectRef obj);
}

/// <summary>
/// ARC-SCRIPT-1481 (ledger VT1–VT4): the vitals questions of get hit points (action 107) and get fatigue points (action 108). A host whose admission set holds either action implements this interface; the interpreter asks only for the
/// object the action names.
/// </summary>
public interface IScrVitalQueries
{
    /// <summary>The object's current and maximum hit points as the original's pair reads them (0x43D600 and 0x43D5A0: the maximum minus the damage, and the maximum); null when the host cannot answer (the object is not a
    /// PC or an NPC the host knows); the dispatch then ends with <see cref="ScrStop.NotModelled"/> and applies nothing.</summary>
    (int Current, int Maximum)? HitPoints(ScrObjectRef obj);

    /// <summary>The object's current and maximum fatigue points (0x45D700 and 0x45D670: the maximum minus the damage, and the maximum); null as <see cref="HitPoints"/>.</summary>
    (int Current, int Maximum)? FatiguePoints(ScrObjectRef obj);
}

/// <summary>
/// ARC-SKILLS-009: the skill question of get skill (action 47, ledger SX1, SX2). A host whose admission set holds the action implements
/// this interface; the interpreter asks only for skill numbers 0..15 and stores 0 for any other without asking.
/// </summary>
public interface IScrSkillQueries
{
    /// <summary>The object's effective skill level, as the original's level functions give it (0x4C5EB0 for a basic skill 0..11,
    /// 0x4C65E0 for a tech skill 12..15 as tech index <paramref name="skill"/> − 12): the base level from the points (a monstrous
    /// critter's melee 20) plus the effects' changes, clamped to 0..20 and capped by the key stat; never the points or the training.
    /// 0 for an object that is not a PC or an NPC.</summary>
    int SkillLevel(ScrObjectRef obj, int skill);
}

/// <summary>
/// ARC-SCRIPT-2410 (ledger IV20): optional for a host that applies a run's quest requests as the run goes, as the original does (set PC quest state, 0x4C4D20, and give quest experience, 0x45F110, act at once): the interpreter
/// tells the host each such request as the action makes it (a call-ex callee's too, in the order the actions run), so the run's later quest reads (<see cref="IScrObjectQueries.QuestState"/>) answer what the earlier writes left. The request
/// is still collected in <see cref="ScrEffects.States"/>; a host that implements this does not apply it a second time.
/// </summary>
public interface IScrQuestWrites
{
    void QuestRequested(ScrStateRequest request);
}

/// <summary>
/// ARC-SCRIPT-042: the item questions of set <see cref="ScrAdmissionSet.S14"/> (ledger WE rows). A host whose admission set holds the
/// identified or rewielding condition or the magick-tech adjustment implements this interface; the interpreter asks only for the
/// objects the clauses name.
/// </summary>
public interface IScrItemQueries
{
    /// <summary>Whether the object is identified as the original's test (0x464370) reads it: an item whose type has a complexity of 0 or
    /// less, or an item with the identified item flag (0x1); null when the host does not know the object (the run then stops as for an
    /// absent object).</summary>
    bool? IsIdentified(ScrObjectRef obj);
    /// <summary>The item's effective power ratio for its owner (0x461540, ledger EQ9): 100 for a neutral item or an owner that is not a
    /// critter, else <c>100 × power / complexity</c>; null when the host does not know the item or the owner (the run stops).</summary>
    int? PowerRatio(ScrObjectRef item, ScrObjectRef owner);
    /// <summary>Whether the worn items are being rewielded (0x4655C0, the global the rewield 0x465530 sets around its runs).</summary>
    bool ItemsBeingRewielded();
}

/// <summary>
/// ARC-SCRIPT-1562 (ledger CP9): the question of obj is animal (condition 37, 0x4451B0 case 0x25), which the weapon scripts of the hunting weapons ask of the victim. A host whose admission set holds condition 37 implements this interface.
/// </summary>
public interface IScrCritterQueries
{
    /// <summary>Whether the object is an NPC whose critter flags (0xDA) hold the animal bit 0x8 as the session has them; false for any other object (the original tests the object type 0x10 first); null when the host does not know the object (the run stops).</summary>
    bool? IsAnimal(ScrObjectRef obj);

    /// <summary>
    /// ARC-SCRIPT-1661 (CP11, exe read 2026-11-13: condition 31 at 0x4451B0 case 0x1f, 0x4B3D50): whether the object is a PC or an NPC whose critter flags (0xDA) hold the combat-mode bit 0x40000 as the session has it; false for any other object type; null when the host does not
    /// know the object (the run stops). A surrendered NPC keeps the bit, so it still answers true (CL4).
    /// </summary>
    bool? IsInCombat(ScrObjectRef obj);
}

/// <summary>
/// ARC-SCRIPT-1733 (ledger CS1–CS12): the critter-state and social conditions of <see cref="ScrAdmissionSet.CritterStateConditions"/>, which the original answers from the object's own fields (0x406CA0 over the field numbers named below) or from the
/// reaction, danger, sight and hearing records. A host whose admission set holds one of them implements this interface. Each answer is null when the host does not know the object (the run stops, not modelled); an absent object never asks.
/// </summary>
public interface IScrCritterStateQueries
{
    /// <summary>Obj met PC before (c10, 0x4451B0 case 10 → 0x4C0C40): true for the same object twice; false unless the second object is a PC and the first an NPC; else whether the PC is in the NPC's remembered reaction slots (1..9), so false during the first conversation (the conversation slot 0 is not one).</summary>
    bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc);

    /// <summary>Obj is invisible (c15, case 15): bit 0x1 of the spell flags (field 0x14) of any object.</summary>
    bool? IsInvisible(ScrObjectRef obj);

    /// <summary>Obj has surrendered (c25, case 25 → 0x4AF210 → 0x4AAB00): an NPC whose danger state reads surrender (not fleeing, critter flag 0x4000); false for any other object type.</summary>
    bool? IsSurrendered(ScrObjectRef obj);

    /// <summary>Obj is in dialog (c26, case 26 → 0x4C1110): an NPC with a conversation partner (the reaction-PC array's entry 0, field 0x129); every other object type goes to a registered callback (0x567400 reading the PC's dialog slot) which is not modelled and answers null.</summary>
    bool? IsInDialog(ScrObjectRef obj);

    /// <summary>Obj can see obj (c28, case 28 → 0x4AF260): the sight check of the observer toward the target, true when it needs nothing more (excess 0); null for an observer that is not an NPC this host holds sight facts of.</summary>
    bool? CanSee(ScrObjectRef observer, ScrObjectRef target);

    /// <summary>Obj can hear obj (c29, case 29 → 0x4AF470 with loudness 1, normal): the hearing check of the listener toward the source; null for a listener that is not an NPC this host holds hearing facts of.</summary>
    bool? CanHear(ScrObjectRef listener, ScrObjectRef source);

    /// <summary>Obj is invulnerable (c30, case 30): object flag 0x400000 (field 0x13) of any object, the flags as the session has them.</summary>
    bool? IsInvulnerable(ScrObjectRef obj);

    /// <summary>Obj is undead (c38, case 38): an NPC whose critter flags (0xDA) hold the undead bit 0x4; false for the PC and any other object type (the original tests the object type 0x10 first).</summary>
    bool? IsUndead(ScrObjectRef obj);

    /// <summary>Prowling (c51, case 51 → 0x45EDE0): a PC or NPC whose critter flags hold the prowling bit 0x1 (the session's concealment of the hero and of a follower); false for any other object type.</summary>
    bool? IsProwling(ScrObjectRef obj);

    /// <summary>
    /// Obj has bad associates (c11, ARC-SCRIPT-1771, case 11 → 0x45F460, ledger CS12): for a PC the object itself, else its leader (none: false); true when one of the leader's followers is summoned (spell flag 0x4000), has no NPC flag 0x8000 and no animal critter flag
    /// and has neither object flag 0x102. Null when the host cannot tell for an object it holds or for a follower whose flags it cannot read while no other follower decides it.
    /// </summary>
    bool? HasBadAssociates(ScrObjectRef obj);

    /// <summary>Obj is monster of type (c19, ARC-SCRIPT-1771, case 19, ledger CS13): the art id stored in object field 0x18 is a monster art (type 0xC, the top four bits) whose monster number (bits 23..27, 0x503F20) equals the number; false for any other art; null for an object the host does not know or whose art field it cannot read.</summary>
    bool? IsMonsterOfType(ScrObjectRef obj, int type);
}

/// <summary>
/// ARC-SCRIPT-1772 (ledger CQ1–CQ3): the getters of <see cref="ScrAdmissionSet.CritterFieldActions"/> and, since ARC-SCRIPT-2326, get armor (<see cref="ScrAdmissionSet.CritterArtActions"/>). The original answers each from the object's own field when the object is an NPC (type 0x10) and 0 for every other object type (0x45D010 social class field 0x128,
/// 0x45D290 origin field 0x123, 0x45D080 faction field 0x124). A host whose admission set holds one of them implements this interface. An answer is null when the host does not know the object (the dispatch ends not modelled, nothing applied).
/// </summary>
public interface IScrCritterFieldQueries
{
    /// <summary>Get social class (38): the NPC's social class (field 0x128), 0 for any other object type.</summary>
    int? SocialClassOf(ScrObjectRef obj);

    /// <summary>Get origin (39): the NPC's origin (field 0x123) as it is now (a dialog action's set origin over the map data's), 0 for any other object type.</summary>
    int? OriginOf(ScrObjectRef obj);

    /// <summary>Get faction (89): the NPC's faction (field 0x124) as it is now (a script's set faction over the map data's), 0 for any other object type.</summary>
    int? FactionOf(ScrObjectRef obj);

    /// <summary>Get armor (action 32, ARC-SCRIPT-2326, ledger AR1–AR3): the armour part of the object's current art id (object field 1 through 0x406CA0, then 0x504030: bits 20..23 of a critter art id, bits 20..22 of a monster's, 0 for any other art type), the look of the
    /// body armour a critter wears (3 chain, 4 and 6 plate), not an armour class; null when the host does not know the object (the dispatch ends not modelled, nothing applied).</summary>
    int? ArmorOf(ScrObjectRef obj);
}

/// <summary>
/// ARC-SCRIPT-1661 (ledger CP12): the question of adjust stat (action 137, 0x447220 case 0x89), which the alignment weapon script 30252 runs at the wielder. A host whose admission set holds action 137 implements this interface.
/// </summary>
public interface IScrStatAdjustQueries
{
    /// <summary>
    /// Whether the host applies an adjustment of base stat <paramref name="stat"/> of <paramref name="obj"/> (the original writes <c>base + value</c> through the stat setter 0x4B0980 for any PC or NPC and stat 0..27; this host's session holds the hero's alignment (stat 19) only). False ends the dispatch with
    /// <see cref="ScrStop.NotModelled"/> and applies nothing; an absent object never asks.
    /// </summary>
    bool CanAdjustStat(ScrObjectRef obj, int stat);
}

/// <summary>
/// ARC-SCRIPT-1562 (ledger CP7): the question of get magick-tech adjustment (action 80, 0x44933F → 0x461620), which the combat-point scripts of the installed poison weapons ask before their damage: the chance, in percent, that the opposing
/// aptitude of the target cancels the item's magick or technology in an attack by the attacker. A host whose admission set holds action 80 implements this interface.
/// </summary>
public interface IScrMagickTechQueries
{
    /// <summary>The percent chance of 0x461620 for <paramref name="item"/> (its magick-tech complexity), <paramref name="attacker"/> and <paramref name="target"/> (their aptitudes): 0 unless both are a PC or an NPC; null when the host does not know the
    /// item or a critter's aptitude (the run stops, as for an object the host does not know).</summary>
    int? MagickCancelPercent(ScrObjectRef item, ScrObjectRef attacker, ScrObjectRef target);
}

/// <summary>
/// ARC-SCRIPT-2041 (ledger PV1–PV4): the portals (doors) in the vicinity of an object, for the focus kind every portal in the vicinity (19, the loop for of the four ground rune tile scripts). A host
/// whose set admits the focus kind implements it besides <see cref="IScrWorldQueries"/> (the loop for itself and the cap).
/// </summary>
public interface IScrPortalQueries
{
    /// <summary>
    /// The portals in the vicinity of <paramref name="centre"/> (focus kinds 19 and 20, 0x44B4E0 with 0x440FC0 and the type mask 2, ledger PV1): every portal object of the centre's map whose tile is within
    /// <see cref="ScrWorld.VicinityReach"/> tiles of the centre's tile on both axes and that is not destroyed or switched off, open, closed or locked alike, in the host's list order (the original's order, the
    /// scan of the window's sectors, is not read); empty when the host does not know the centre or its place. The interpreter keeps the first <see cref="ScrWorld.VicinityListCap"/> under
    /// <see cref="ScrRuleProfile.VicinityListCapped"/>.
    /// </summary>
    IReadOnlyList<ScrObjectRef> PortalsInVicinity(ScrObjectRef centre);
}

/// <summary>
/// ARC-SCRIPT-2319 (ledger GF1–GF7): the group of an object, for the focus kinds every one in the group (13) and anyone in the group (14), which the object-list builder (0x44B4E0) answers as the triggerer's direct followers
/// (object field 0xF1 through 0x4411C0, in array order) with the triggerer itself appended last. A host whose set admits the kinds implements this interface; the interpreter adds the triggerer and applies the list's cap.
/// </summary>
public interface IScrGroupQueries
{
    /// <summary>The direct followers of <paramref name="leader"/> in the order the original's follower array holds them (a waiting NPC is outside it, ledger OL16; a forced or charmed follower stays in): the hero's followers for the PC,
    /// an NPC's own followers for an NPC, none for any other object or one the host does not know.</summary>
    IReadOnlyList<ScrObjectRef> FollowersOf(ScrObjectRef leader);
}

/// <summary>
/// ARC-SCRIPT-2322 (ledger CV1–CV5): the containers in the vicinity of an object, for the focus kinds every container in the vicinity (17) and any container in the vicinity (18; one list, as the portals' 19 and 20). A
/// host whose set admits either kind implements it besides <see cref="IScrWorldQueries"/>.
/// </summary>
public interface IScrContainerQueries
{
    /// <summary>
    /// The containers in the vicinity of <paramref name="centre"/> (type mask bit 2 of the same scan as the portals, ledger CV1, CV2): every container object of the centre's map whose tile is within
    /// <see cref="ScrWorld.VicinityReach"/> tiles of the centre's tile on both axes and that is not destroyed or switched off, in the host's list order; empty when the host does not know the centre or its place.
    /// The interpreter keeps the first <see cref="ScrWorld.VicinityListCap"/> under <see cref="ScrRuleProfile.VicinityListCapped"/>.
    /// </summary>
    IReadOnlyList<ScrObjectRef> ContainersInVicinity(ScrObjectRef centre);
}

/// <summary>
/// ARC-WORLD-081: what a host that runs <see cref="ScrAdmissionSet.S15"/> answers about the world around a run (ledger PG3, PG5). The
/// interpreter asks only while a clause needs it.
/// </summary>
public interface IScrWorldQueries
{
    /// <summary>
    /// The scenery in the vicinity of <paramref name="centre"/> (focus kind 15, 0x44B4E0 with 0x440FC0): every scenery object of the
    /// centre's map whose tile is within <see cref="ScrWorld.VicinityReach"/> tiles of the centre's tile on both axes and that is not
    /// destroyed or switched off, in the host's list order (the original's order is not read); empty when the host does not know the
    /// centre or its place. The interpreter keeps the first <see cref="ScrWorld.VicinityListCap"/> under
    /// <see cref="ScrRuleProfile.VicinityListCapped"/>.
    /// </summary>
    IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre);

    /// <summary>
    /// ARC-SCRIPT-047: everyone in the vicinity of <paramref name="centre"/> (focus kinds 8 and 9, 0x44B4E0 with 0x440FC0 and the type
    /// mask 0x18000, ledger TG4): every PC and NPC of the centre's map whose tile is within <see cref="ScrWorld.VicinityReach"/> tiles of
    /// the centre's tile on both axes and that is not destroyed or switched off (the list's flag mask), the centre itself and dead critters
    /// included, in the host's list order (the original's order is not read); empty when the host does not know the centre or its place.
    /// The interpreter keeps the first <see cref="ScrWorld.VicinityListCap"/> under <see cref="ScrRuleProfile.VicinityListCapped"/>.
    /// </summary>
    IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre);

    /// <summary>A number from <paramref name="low"/> to <paramref name="high"/>, both included (<paramref name="low"/> ≤
    /// <paramref name="high"/>), from the game's generator (random between, 0x4CEF00; PG5).</summary>
    int Random(int low, int high);

    /// <summary>ARC-SCRIPT-049: the object's tile (its location field, which distance between reads, HW3), or null when the host does not
    /// know the object or its place (the run then stops as for an absent object).</summary>
    (long X, long Y)? Location(ScrObjectRef obj);
}

/// <summary>
/// ARC-SCRIPT-049: the object questions of the conditions obj is dead (22) and obj is switched off (27) of set
/// <see cref="ScrAdmissionSet.S18"/> (ledger HW1, HW2). A host whose admission set holds them implements this interface; the interpreter
/// asks only for the objects the conditions name. Null is an object the host does not know: the run stops as for an absent object.
/// </summary>
public interface IScrObjectStateQueries
{
    /// <summary>Whether the object is dead as the original's test (0x45D8D0) reads it: its current hit points (0x43D600) are 0 or less.</summary>
    bool? IsDead(ScrObjectRef obj);
    /// <summary>Whether the object is switched off: its object flag "off" (field 0x13, bit 0x2), as a script's toggle state left it.</summary>
    bool? IsSwitchedOff(ScrObjectRef obj);
}

/// <summary>
/// ARC-MAGIC-029 (MT21a, ledger SC1–SC4): the spell questions of the conditions obj has body spell (14), obj is influenced by spell (35), knows spell (48) and mastered spell college (49) of set
/// <see cref="ScrAdmissionSet.S31"/>. A host whose admission set holds them implements this interface; the interpreter asks only for the objects the conditions name (the list rule of SG6 then decides).
/// Null is an object the host does not know: the run stops as for an absent object.
/// </summary>
public interface IScrSpellQueries
{
    /// <summary>The object's spell flags (object field 20, <c>obj_f_spell_flags</c>, 0x14) as the original's field getter (0x406CA0) reads them; every object type has them. Condition 14 tests the four body bits 0x3C
    /// (body of air 0x4, earth 0x8, fire 0x10, water 0x20) over this word.</summary>
    int? SpellFlags(ScrObjectRef obj);
    /// <summary>Whether the object is under the influence of the spell as 0x459380 answers it (AC1): some active spell run (of the 512 slots, in slot order) whose target is the object, or whose two object lists hold it, casts
    /// that spell. The spell is passed as read (any number).</summary>
    bool? IsInfluencedBy(ScrObjectRef obj, int spell);
    /// <summary>Whether the object knows the spell as 0x4B1950 answers it (SB5): false for a spell outside 0..79; otherwise whether the spell's rank (spell % 5 + 1) is at most the rank the object has in the college
    /// spell / 5 (0x4B1AB0: a PC or NPC's field, any other object has rank 0).</summary>
    bool? KnowsSpell(ScrObjectRef obj, int spell);
    /// <summary>The college the object mastered (0x4B1CB0, SB1): the PC's or NPC's stored college, −1 for none; −1 also for every object that is neither a PC nor an NPC.</summary>
    int? MasteredCollege(ScrObjectRef obj);
}

/// <summary>The game clock as the story commands read it (ledger SQ1, SQ5): the day index (0 on the first day) and the hour and minute of the day.</summary>
public readonly record struct ScrGameTime(long DayIndex, int Hour, int Minute)
{
    /// <summary>Whether it is day (0x45ACF0): the hour is 6 to 17.</summary>
    public bool IsDay => Hour is >= 6 and <= 17;

    /// <summary>Get day since startup (0x45A8D0): the day index plus one.</summary>
    [OriginalFunction(0x45A8D0, OriginalFunctionScope.Full)]
    public long DaySinceStartup() => DayIndex + 1;

    /// <summary>Get hours since startup (0x45A890): whole hours of the game's age, the day index times 24 plus the hour.</summary>
    [OriginalFunction(0x45A890, OriginalFunctionScope.Full)]
    public long HoursSinceStartup() => DayIndex * 24 + Hour;
}

/// <summary>
/// ARC-SCRIPT-1165 (E5-c, ledger SQ1–SQ12): the story and time questions of set <see cref="ScrAdmissionSet.S34"/>: the daytime condition and the clock
/// actions (<see cref="Time"/>), the global quest state, the PC's rumor knowledge and the game-wide quelled set, and the PC's blessings and curses. A host whose
/// admission set holds one implements this interface; the interpreter asks only for what a clause names.
/// </summary>
public interface IScrStoryQueries
{
    /// <summary>The game clock, or null when the host holds none (the dispatch then ends with <see cref="ScrStop.NotModelled"/> and applies nothing).</summary>
    ScrGameTime? Time();
    /// <summary>The quest's game-wide state (0x4C51A0, 2 until a PC completes or botches it); 0 for a number the host's quest table does not hold.</summary>
    int GlobalQuestState(int quest);
    /// <summary>Whether the object is a PC that has learned the rumor (0x4C58D0); false for any other object and for a rumor number the host does not hold; null for an object the
    /// host does not know (the run then stops as for an absent object).</summary>
    bool? RumorKnown(ScrObjectRef obj, int rumor);
    /// <summary>Whether the rumor is quelled (0x4C5650, game-wide); false for a rumor number the host does not hold.</summary>
    bool RumorQuelled(int rumor);
    /// <summary>Whether the object is a PC that holds the blessing (<paramref name="curse"/> false, 0x4C4280) or the curse (true, 0x4C3DD0) of this number; false for any other object;
    /// null for an object the host does not know.</summary>
    bool? HasBlessing(ScrObjectRef obj, int number, bool curse);
}

/// <summary>
/// ARC-DEV-100 (MIG8, GR29, ledger GV1–GV4): the script stores of the value kinds global variable (1), PC variable (5) and PC flag (6),
/// which a set holding <see cref="ScrAdmissionSet.StoreValueKinds"/> reads through a host that implements this interface. The session
/// owns them (<c>ScriptStores</c>, SA13, SA14). The PC arrays are fields 272 and 273 of each PC object (SA14), so since ARC-DEV-265 they are
/// asked for and written back per PC: the run asks only for an object the host's <see cref="IScrHost.IsPc"/> calls a PC, and two PCs keep
/// apart arrays (a network game's; a single-player host has the hero alone). A run reads them as the dispatch left them (its own writes first)
/// and hands every changed value back in <see cref="ScrEffects"/>.
/// </summary>
public interface IScrStoreQueries
{
    /// <summary>Global variable 0..1999 (zero until a script writes it).</summary>
    int GlobalVariable(int index);
    /// <summary>PC variable 0..65535 of <paramref name="pc"/>, an object the host calls a PC; 0 for an index never written.</summary>
    int PcVariable(ScrObjectRef pc, int index);
    /// <summary>PC flag 0..65535 of <paramref name="pc"/> (bit <c>index mod 32</c> of word <c>index / 32</c>), an object the host calls a PC;
    /// false when never set.</summary>
    bool PcFlag(ScrObjectRef pc, int index);
}

/// <summary>ARC-WORLD-081: the vicinity a script's object list reads (ledger PG3).</summary>
public static class ScrWorld
{
    /// <summary>
    /// The vicinity's reach in tiles on both axes from its centre. The original takes the bounding box, in tiles, of a screen rectangle
    /// the size of the game window centred on the attachee (0x440FF0); that window is the fixed 800 × 400 play view, and with tiles of
    /// 80 × 40 pixels the box reaches exactly 10 tiles on both axes (the reading of OL29, the same rectangle). Both rule modes use it: it
    /// never depends on the viewer's window or zoom (owner decision 2026-10-28: a fixed reach).
    /// </summary>
    public const int VicinityReach = 10;

    /// <summary>The objects the original's list copies at most (0x44B4E0, PG3); the rest are dropped silently.</summary>
    public const int VicinityListCap = 100;
}

/// <summary>
/// ARC-WORLD-081: a create-object action (52, ledger PG1): the prototype number as the script names it (the basic number; the host adds
/// 20 to find the prototype, PG8) and the first object of operand 1, next to which the host places the new object.
/// </summary>
public readonly record struct ScrCreateRequest(int Prototype, ScrObjectRef Near);

/// <summary>ARC-WORLD-114: what a toggle-state (56) or kill (58) action asks of one object (ledger SU6, PM1).</summary>
public enum ScrObjectStateChange
{
    /// <summary>Toggle state (56): the object flag "off" (0x2) is flipped by the raw field writes (0x4EFEE0, 0x4EFF50), not the
    /// object flag setter (0x43D0E0), so the object's lights are left as they are.</summary>
    ToggleOff,
    /// <summary>Kill (58): a death with no killer (0x45D900).</summary>
    Kill,
    /// <summary>ARC-SCRIPT-049: set faction (134) to <see cref="ScrObjectStateRequest.Value"/> (0x45D0C0, HW4): an NPC's faction field
    /// (0x124) is written; any other object is left as it is.</summary>
    SetFaction,
    /// <summary>ARC-SCRIPT-052: destroy (68) of the first object of its operand (0x43CCA0, TD1, TD2): an object already destroyed is left as it
    /// is; otherwise its destroy-point (2) script runs first and a skip default keeps it; else it is taken out of the world (object flag 0x1).</summary>
    Destroy,
    /// <summary>ARC-SCRIPT-052: stop attacking (109) of the first object of its operand (0x4AA4A0, TD3): for an NPC peace with its focus,
    /// for the PC the same for each of its followers.</summary>
    StopAttacking,
    /// <summary>ARC-DEV-231 (S14a, GT158–GT161): walk to (69) of the first object of its operand to the tile (<see cref="ScrObjectStateRequest.Value"/>,
    /// <see cref="ScrObjectStateRequest.Value2"/>): the walk goal 7 through the generic starter, or the found walk retargeted in place.</summary>
    WalkTo,
    /// <summary>ARC-DEV-231 (S14a, GT161): run to (74), the same with goal 55 and the run flag by encumbrance.</summary>
    RunTo,
    /// <summary>ARC-DEV-233 (S14b, GT162): heal fatigue (76) of each object of its operand by <see cref="ScrObjectStateRequest.Value"/>: the fatigue damage falls by the value to 0 at most; a critter whose
    /// fatigue was 0 or less and is above 0 now gets up (the life step's get-up). A negative value raises the damage in the original and is not modelled.</summary>
    HealFatigue,
    /// <summary>ARC-DEV-233 (S14b, GT155): perform animation (62) of the first object of its operand, the animation number in <see cref="ScrObjectStateRequest.Value"/> as read (the host refuses one outside 0..25).</summary>
    PerformAnimation,
    /// <summary>ARC-DEV-234 (S14c, GT164): try to steal 100 coins (115): the first object of operand 0 (<see cref="ScrObjectStateRequest.Object"/>, the thief) tries to take the whole gold stack of the first object of
    /// operand 1 (<see cref="ScrObjectStateRequest.Other"/>) with the pickpocket skill; the host starts the skill goal only when the victim is a critter with at least 100 coins.</summary>
    StealCoins,
    /// <summary>ARC-DEV-236 (S14e, GT187): become prone (93) of each object of its operand: the object's runs are interrupted at level 4 (a refusal does nothing) and its art becomes the fall (animation 7) at the last frame;
    /// no goal starts and no flag changes.</summary>
    BecomeProne,
    /// <summary>ARC-DEV-241 (S14f, GT183/GT184): one of the six cast actions. <see cref="ScrObjectStateRequest.Object"/> is the target (the first object of the target operand), <see cref="ScrObjectStateRequest.Other"/> the caster
    /// (the first object of 48, 118, 121 and 122's operand 0; none for 61 and 136, which have no caster), <see cref="ScrObjectStateRequest.Value"/> the spell and <see cref="ScrObjectStateRequest.Value2"/> the invocation
    /// flags (<see cref="ScrCastFlags"/>). The host runs the spell dispatcher without the spell UI's cost, line-of-fire and reach checks.</summary>
    CastSpell,
    /// <summary>ARC-SCRIPT-1294 (RM6, 0x4593F0): stop spell on object (130): <see cref="ScrObjectStateRequest.Object"/> is the first object of operand 1 and <see cref="ScrObjectStateRequest.Value"/> the spell of operand 0. The host interrupts the first
    /// active run of that spell that holds the object as its target or in one of its object lists (0x459290, 0x457110); the run goes on whether it found one or not.</summary>
    StopSpell,
    /// <summary>ARC-DEV-243 (S14g, GT194): damage (60) of one object of its operand: <see cref="ScrObjectStateRequest.Value"/> the amount, <see cref="ScrObjectStateRequest.Value2"/> the damage type (0 normal, 1 poison, 2 electrical, 3 fire, 4 fatigue);
    /// no attacker, resistances apply.</summary>
    Damage,
    /// <summary>ARC-DEV-243 (S14g, GT194): apply unresistable damage (138): as <see cref="Damage"/> with the resistance step skipped.</summary>
    DamageUnresistable,
    /// <summary>ARC-DEV-243 (S14g, GT196): heal hit points (75) of one object of its operand by <see cref="ScrObjectStateRequest.Value"/>: a living critter only, no resurrection, no get-up.</summary>
    HealHitPoints,
    /// <summary>ARC-SCRIPT-1481 (VT3): heal poison (128) of each object of its operand: the poison level becomes the level minus <see cref="ScrObjectStateRequest.Value"/>, floored at 0, through the stat setter (which keeps the poison timer event right).</summary>
    HealPoison,
    /// <summary>ARC-MAGIC-029 (MT21a, SC5): give spell mastery in college (112) of the first object of its operand: <see cref="ScrObjectStateRequest.Value"/> the college as read (not range-checked in the original). The host
    /// writes it for a PC or an NPC that has no mastered college and at least five ranks of the college (0x4B1CF0, SB7), and does nothing else.</summary>
    GiveSpellMastery,
    /// <summary>ARC-MAGIC-042 (T-m1, ledger TI5): transform into a basic prototype (40): the run's attachee (<see cref="ScrObjectStateRequest.Object"/>) is replaced by a new object of the basic prototype <see cref="ScrObjectStateRequest.Value"/> (the file
    /// is that number + 20) at its place, carrying its hit-point percentage; the old object is destroyed and the new one is the attachee of every later request of the run. The host decides what the object is and when the creation fails (the action stops).</summary>
    Transform,
    /// <summary>ARC-MAGIC-042 (T-m1, ledger TI6): force follow (21): the first object of operand 0 (<see cref="ScrObjectStateRequest.Object"/>) follows the first object of operand 1 (<see cref="ScrObjectStateRequest.Other"/>) with the follow check skipped (0x45DE90, force 1).</summary>
    MakeFollow,
    /// <summary>ARC-MAGIC-042 (T-m1, ledger TI6): attack (36): <see cref="ScrObjectStateRequest.Other"/> (the first object of operand 1) attacks <see cref="ScrObjectStateRequest.Object"/>, one request per object of operand 0 in list order (the attack event 0x4A9650, loudness 1, flags 2).</summary>
    Attack,
    /// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS1): critter disband (22) of the first object of its operand, the follower check skipped (0x45DFC0 with force 1): an NPC that follows or waits for a leader is let go (unwaited, taken out of the leader's array,
    /// its leader cleared); any other object does nothing.</summary>
    Disband,
    /// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS2): set reaction (30): the standard reaction of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 0, an NPC) toward <see cref="ScrObjectStateRequest.Other"/> (the first object of operand 1, a PC)
    /// is changed by <see cref="ScrObjectStateRequest.Value"/> minus its present standard value, through the reaction change (0x4C0DE0).</summary>
    SetReaction,
    /// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS2): adjust reaction (31): the reaction change (0x4C0DE0) of <see cref="ScrObjectStateRequest.Object"/> toward <see cref="ScrObjectStateRequest.Other"/> by <see cref="ScrObjectStateRequest.Value"/>.</summary>
    AdjustReaction,
    /// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS3): set day standpoint (45) of the first object of its operand (an NPC; any other object does nothing): the location field 0x121 takes <see cref="ScrObjectStateRequest.Value"/> as its low word and
    /// <see cref="ScrObjectStateRequest.Value2"/> (already the original's second word) as its high word. <see cref="ScrObjectStateRequest.Map"/> is set by the extended action (140), which also writes the NPC's teleport map (field 0xF3).</summary>
    SetDayStandpoint,
    /// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS3): set night standpoint (46, extended 141): as <see cref="SetDayStandpoint"/> for the field 0x122.</summary>
    SetNightStandpoint,
    /// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS5): change art number (59) of each object of its operand: the number field of the object's art id becomes <see cref="ScrObjectStateRequest.Value"/> (0x502780, then frame 0, then kept only when the art exists,
    /// 0x502220); no host applies it yet (the viewer has no per-object art overlay), so the host sets refuse it.</summary>
    ChangeArtNumber,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO1): create item (66): <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 1, the holder) gets a new object of the basic prototype <see cref="ScrObjectStateRequest.Value"/> (the file is that number + 20)
    /// made at its location and, when it is an item (type 5..14), put into its inventory (0x4617F0, slot -1: the first free place); a prototype of another type leaves the new object where it was made.</summary>
    CreateItem,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO2): transfer item (41): the first direct inventory item of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 1) whose name number is <see cref="ScrObjectStateRequest.Value"/> is put into the
    /// inventory of <see cref="ScrObjectStateRequest.Other"/> (the first object of operand 2) by the same insert (0x4617F0); nothing when the holder is no critter or container or holds no such item.</summary>
    TransferItem,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO3): set lock state (53) of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 0): a portal or container (not the junk pile, description 3023) has its locked bit cleared and, for the value 1, set again (0x441DD0);
    /// clearing a locked object schedules the Lock event an hour later, as a key does.</summary>
    SetLockState,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO4): toggle open or closed (88) of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 0): a portal is opened or closed at once to the end of its steps (0x4EEF80, 0x4F08F0), a container opens (the ART's next
    /// frame) or closes (the previous), a destroyed object does nothing, any other object type nothing.</summary>
    ToggleOpenClosed,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO6): set written start (94) of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 0, a written item, type 13): its written-start field (0xCB) takes <see cref="ScrObjectStateRequest.Value"/>.</summary>
    SetWrittenStart,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO7): destroy item named (126): the first direct inventory item of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 1) whose name number is <see cref="ScrObjectStateRequest.Value"/> is destroyed (0x43CCA0).</summary>
    DestroyItemNamed,
    /// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO8): toggle item inventory display (127) of <see cref="ScrObjectStateRequest.Object"/> (the first object of operand 0, an item of type 5..14): bit 0x10 of its item flags (field 0x57) is flipped.</summary>
    ToggleItemInventoryDisplay,
    /// <summary>ARC-SCRIPT-1772 (ledger CQ4, exe 0x447220 case 0x39): toggle invulnerability (57) of each object of its operand in list order: object flag 0x400000 (field 0x13) is set when clear and cleared when set, by the plain flag write (0x4EFF50, 0x4EFEE0 over 0x43D0E0, 0x43D280).</summary>
    ToggleInvulnerability,
    /// <summary>ARC-SCRIPT-1772 (ledger CQ5, case 0x5C): rename (92) of <see cref="ScrObjectStateRequest.Object"/>, the first object of operand 0: its name number (field 0x16, the class the name conditions compare, not display text) becomes <see cref="ScrObjectStateRequest.Value"/> as read, with no type test.</summary>
    Rename,
}

/// <summary>ARC-SCRIPT-1164 (E5-b): which game state a <see cref="ScrWorldRequest"/> changes.</summary>
public enum ScrWorldChange
{
    /// <summary>Toggle sector blocked (106, ledger WO5): the sector (<see cref="ScrWorldRequest.A"/>, <see cref="ScrWorldRequest.B"/> = x, y as the action's second word reads) is added to or taken off the current map's blocked-sector list (0x4EF010 → 0x4D0F00);
    /// <see cref="ScrWorldRequest.Blocked"/> is the state it ends in.</summary>
    SectorBlocked,
    /// <summary>Toggle monster generator (110, ledger WO9): bit 0x80 of the slot <see cref="ScrWorldRequest.A"/> of the generators' counter table (0x4BABF0, 0x4BABD0), game-wide; <see cref="ScrWorldRequest.Blocked"/> is the state it ends in.</summary>
    GeneratorBlocked,
}

/// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO5, WO9): a toggle of a game-wide table the action reads and writes without an object: the sector blocked list of the current map or the monster generators' block bits. The request carries the state the toggle
/// ends in, so a host applies the absolute state whatever it held (the run read the same state through <see cref="IScrWorldObjectQueries"/>).</summary>
public readonly record struct ScrWorldRequest(ScrWorldChange Change, int A, int B, bool Blocked);

/// <summary>
/// ARC-SCRIPT-1164 (E5-b, ledger WO4, WO5, WO9): the world-object questions of set <see cref="ScrAdmissionSet.S37"/>: whether a portal or container is open (condition 36, and the open toggle's present state), whether a sector is on the current map's
/// blocked list (condition 45, and the sector toggle), whether a generator slot is blocked (condition 46, and the generator toggle). A host whose admission set holds them implements this interface; the interpreter asks only for what a clause names.
/// </summary>
public interface IScrWorldObjectQueries
{
    /// <summary>Whether the object is open as the original's test reads it (a portal's or container's ART frame is not 0, 0x4F08C0, 0x502B50); false for any other object; null for an object the host does not know (the run then stops as for an absent object).</summary>
    bool? IsOpen(ScrObjectRef obj);
    /// <summary>Whether a toggle of a closed object ends open: its ART has another frame to open to (a portal of one frame and a container of one frame stay closed); false for any other object; null when the host does not know the object.</summary>
    bool? CanOpen(ScrObjectRef obj);
    /// <summary>Whether the sector (x, y) is on the current map's blocked-sector list (0x4D0EE0).</summary>
    bool SectorBlocked(int x, int y);
    /// <summary>Whether bit 0x80 of the generator counter table's slot is set (0x4BABA0); false for a slot outside 0..255.</summary>
    bool GeneratorBlocked(int slot);
}

/// <summary>ARC-DEV-241: the invocation flags of a script cast (GT183): 0x2 casts free (the run's Free flag), 0x4 casts unresistable (the run's Unresistable flag).</summary>
[Flags]
public enum ScrCastFlags { None = 0, Free = 0x2, Unresistable = 0x4 }

/// <summary>ARC-WORLD-114: a toggle-state (56) or kill (58) action for one object of its object operand, in action order (each object
/// of the operand's list in turn, 0x44B4E0), and (ARC-SCRIPT-049) a set faction (134) for the first object of its operand with the
/// faction as <paramref name="Value"/> (0 for the others), and (ARC-SCRIPT-052) a destroy (68) or stop attacking (109) for the first object of
/// its operand. The host decides what the object is.
/// ARC-SCRIPT-1163: the extended standpoint actions (140, 141) carry the catalogue map number they also write as the NPC's teleport map in <paramref name="Map"/>.</summary>
public readonly record struct ScrObjectStateRequest(ScrObjectStateChange Change, ScrObjectRef Object, int Value = 0, int Value2 = 0, ScrObjectRef? Other = null, int? Map = null);

/// <summary>ARC-SCRIPT-042: whether an effect action adds or removes.</summary>
public enum ScrEffectChange { Add, Remove }

/// <summary>
/// ARC-SCRIPT-042: an add-effect (77) or remove-effect (78) action for one object of its object operand (ledger WE1): add appends
/// <paramref name="Effect"/> with <paramref name="Cause"/> (0x4E9F70), remove takes away the first entry of the number whatever its
/// cause (0x4EA100, <paramref name="Cause"/> 0). Both values are as read; the host decides what the object is.
/// </summary>
public readonly record struct ScrEffectRequest(ScrEffectChange Change, ScrObjectRef Object, int Effect, int Cause = 0);

/// <summary>ARC-SCRIPT-1734: which eye candy table an eye candy request names and what it does: <see cref="PlayScript"/> (action 120, entry of the script eye candy table), <see cref="PlaySpell"/> (action 104, 0x456E60: the spell eye candy
/// entry number % 10 + number / 10 × 6 of the spell table) and <see cref="StopScript"/> (action 124, 0x44C800 → 0x4CD940: take the script eye candy of the number off the object).</summary>
public enum ScrEyeCandyKind { PlayScript, PlaySpell, StopScript }

/// <summary>ARC-SCRIPT-042: a play-script-eye-candy action (120, ledger WE4): the entry of the script eye-candy table and the object it
/// plays on; ARC-SCRIPT-1734 adds play spell eye candy (104) and stop script eye candy (124, ledger WE11, WE12) in order with it (<see cref="Kind"/>), the number as read. Presentation only; the host shows the play (120, #1592) and the stop (124, #1828); the spell play (104) is not shown yet.</summary>
public readonly record struct ScrEyeCandy(int Number, ScrObjectRef Object, ScrEyeCandyKind Kind = ScrEyeCandyKind.PlayScript);

/// <summary>
/// ARC-SKILLS-009: a use-item-with-skill action (79, ledger SX3, SX4) as the run asks for it: the source, the item (absent for a use
/// without one), the target, the skill number and the modifier, as read. The original only queues the source's use-item-with-skill goal
/// (0x4352C0), which runs the shared skill check later with the modifier as the invocation modifier (counted twice, SC12) and no flags;
/// the script never sees the result. The run asks for nothing without a source or a target. Everything else (the gate, holding the item,
/// the check, the skill's handler) is the host's.
/// </summary>
public readonly record struct ScrSkillUse(ScrObjectRef Source, ScrObjectRef? Item, ScrObjectRef Target, int Skill, int Modifier);

/// <summary>A fade-and-teleport's extras (action 102, ledger TR2): the game seconds the move takes, the sound and the movie
/// numbers, as the action reads them. The original uses the seconds and the sound only when positive and the movie when it
/// is not zero (an unsigned test); the fade itself, the time advance, the sound and the movie are the host's.</summary>
public sealed record ScrTeleportFade(int Seconds, int Sound, int Movie);

/// <summary>
/// ARC-SCRIPT-014: a teleport the run asks for (actions 44 and 102, ledger TR1, TR2): the object, the catalogue map number as the
/// script gives it, and the tile. <see cref="RuntimeMap"/> is that number less 4,999, as the original computes it (32-bit).
/// The original keeps one pending teleport: a request while a teleport of the local PC is pending is refused, any other
/// pending request is replaced (ledger TR4); the host owns that rule.
/// </summary>
public sealed record ScrTeleport(ScrObjectRef Object, int CatalogueMap, int X, int Y, ScrTeleportFade? Fade = null)
{
    public int RuntimeMap => unchecked(CatalogueMap - 4999);
}

/// <summary>What the caller does after the event: its normal behaviour, not, or nothing ran because the script is
/// outside the admitted set (the caller states its own reaction to a refusal).</summary>
public enum ScrOutcome { RunDefault, SkipDefault, Refused }

/// <summary>Why a dispatch ended.</summary>
public enum ScrStop
{
    /// <summary>The slot holds script 0 or the attachee has no slot for the point (run default, E2).</summary>
    NoScript,
    /// <summary>The triggerer spotted a trap on the attachee before a use (skip default, E2).</summary>
    TrapSpotted,
    /// <summary>The script is a trap script (number 30000..30007 with a trap file flag), run by trap code, not
    /// interpreted (skip default, E4).</summary>
    TrapScript,
    /// <summary>The script file is missing (skip default, E4; run default for the mind-control script, X5).</summary>
    MissingScript,
    /// <summary>The script file could not be read or framed (as missing).</summary>
    UnreadableScript,
    /// <summary>The attachee is a monster generator (run default, E5).</summary>
    MonsterGenerator,
    /// <summary>A dead NPC's dialog script without the death-speech flag (run default, E6).</summary>
    DeadWithoutSpeech,
    /// <summary>A surrendered NPC's dialog script without the surrender-speech flag (run default, E6).</summary>
    SurrenderedWithoutSpeech,
    /// <summary>A return-and-skip-default action (E8).</summary>
    ReturnSkipDefault,
    /// <summary>A return-and-run-default action (E8).</summary>
    ReturnRunDefault,
    /// <summary>The last line continued past the end (skip default, auto-removal applies, E7).</summary>
    FellOffEnd,
    /// <summary>A goto to exactly the line count, or a start line outside the script: the line cannot be read
    /// (skip default, no auto-removal, E9).</summary>
    LineOutOfRange,
    /// <summary>The executed-line budget ran out (skip default, no auto-removal, E9).</summary>
    Budget,
    /// <summary>A dialog action ended the run (skip default; auto-removal applies). The conversation to start, if the
    /// triggerer is a PC, is in <see cref="ScrEffects.Dialog"/>; since ARC-SCRIPT-046 a called script's dialog action, which ends only
    /// the callee, puts its conversation there with any other stop of the run.</summary>
    Dialog,
    /// <summary>An object operand names a role the event does not have (no triggerer, attachee, extra or player); the
    /// original would pass a null handle (both fixed), the run stops with skip default.</summary>
    MissingObject,
    /// <summary>A computed global flag number outside 0..3199, which the original would access outside its flag store
    /// (both fixed); the run stops with skip default.</summary>
    IndexOutOfRange,
    /// <summary>The script is outside the admitted set; nothing ran.</summary>
    Refused,
    /// <summary>The event itself is not valid (a point outside 0..35, the dialog-override slot, a negative line).</summary>
    InvalidEvent,
    /// <summary>ARC-SCRIPT-018: a call script would nest deeper than <see cref="ScrRunOptions.MaxCallDepth"/>, or the dispatch's
    /// runs together executed more than <see cref="ScrRunOptions.MaxCallDepth"/> × <see cref="ScrRunOptions.MaxExecutedLines"/>
    /// lines (the original has no bound but its stack; project bounds); the dispatch ends with skip default and applies nothing.</summary>
    CallDepth,
    /// <summary>ARC-SCRIPT-042: a call-ex (81) names an attachment point outside 0..35 or the dialog-override slot; the dispatch ends with skip
    /// default and applies nothing (the original would index past the object's script array).</summary>
    CallExPoint,
    /// <summary>ARC-SCRIPT-050: the host cannot answer a question the original answers (get area on the start map with area 0, ledger GA3, not
    /// modelled; ARC-DEV-157: get stat of an object or a stat the host does not hold, GS3); the dispatch ends with skip default and applies nothing, and the caller reports the run as not ready.</summary>
    NotModelled,
}

/// <summary>A slot to store after the run: the object's slot for the point, or the tile's or sector's attachment.</summary>
public sealed record ScrWriteback(ScrSource Source, ScrObjectRef? Attachee, int Point, ScrSlot Slot);

/// <summary>A conversation for the caller to start after it applied the writeback (the original's dialog action stores
/// the slot before its start callback, 0x4472F1): the DLG of <paramref name="Script"/> at <paramref name="Node"/>, between the PC
/// triggerer and the attachee. A DLG end of 0 resumes the attachee's dialog slot at <paramref name="Line"/> + 1, and −N at
/// line N (the original runs the NPC's SAP_DIALOG slot, dialog_ui.c 400–407): for a talk that is this script, for a
/// conversation a deferred call started it is whatever the NPC's dialog slot holds.</summary>
public sealed record ScrDialogStart(ScrObjectRef Pc, ScrObjectRef? Npc, int Script, int Line, int Node)
{
    /// <summary>ARC-SCRIPT-047: the attachment point of the run whose dialog action asked for the conversation (the point whose slot of
    /// <see cref="Npc"/> the action stored its flags and counters in, X11): <see cref="ScrPoint.Dialog"/> for a talk, for a resumption and
    /// for a call-ex of the NPC's dialog point (a heartbeat's opening talk), the use point for a tile or sector script.</summary>
    public int Point { get; init; } = ScrPoint.Dialog;
}

public enum ScrCallKind { Schedule, Remove }

/// <summary>
/// A deferred call the run asks for, in action order (ARC-SCRIPT-010): <see cref="ScrCallKind.Schedule"/> runs
/// <paramref name="Script"/> from <paramref name="Line"/> with the stored triggerer and attachee (either may be absent)
/// after <paramref name="DelayMs"/> game milliseconds; <see cref="ScrCallKind.Remove"/> removes the first pending call
/// of <paramref name="Script"/> whose attachee is <paramref name="Attachee"/>.
/// </summary>
public sealed record ScrCallRequest(ScrCallKind Kind, int Script, int Line, ScrObjectRef? Triggerer, ScrObjectRef? Attachee, long DelayMs);

/// <summary>A deferred call firing: the named script from its file header, at the stored line, as a use event with
/// the stored roles and no extra object; nothing is written back except the attachee's use slot when the run starts a
/// conversation (E1, ARC-RES-042 section 3; see <see cref="ScrDispatcher.RunCall"/>).</summary>
public sealed record ScrCallEvent(int Script, int Line, ScrObjectRef? Triggerer, ScrObjectRef? Attachee);

/// <summary>
/// A print-line action (ARC-SCRIPT-013, type 24): line <paramref name="Line"/> of the DLG of <paramref name="Script"/>, the
/// running attachment's script (the number the run executes, as the dialog action names it: 2188 while a mind-controlled
/// NPC's attachment runs it, E6; the called script for a deferred call), with the action's second
/// number <paramref name="Kind"/> (the message window's entry type, read in the executable by ARC-DEV-309, ledger PW1). The original builds the line through
/// the dialog's NPC-line step, which also runs the line's action field, and hands the text to the interface (ledger IS2);
/// the host reads the DLG and decides whether it can show it. A run without an attachee prints nothing, as the original.
/// </summary>
public readonly record struct ScrPrintLine(int Script, int Line, int Kind);

/// <summary>
/// ARC-SCRIPT-050: one object of a float line action (23, ledger FL1–FL5): line <paramref name="Line"/> of the DLG of <paramref name="Script"/>
/// (the number the run executes, as for print line) floated over <paramref name="Speaker"/>, with <paramref name="Pc"/>, the first PC
/// in the game-window rectangle around the run's triggerer, as the dialog's PC. The original builds the text through the dialog's
/// NPC-line step (which runs the line's action field between that PC and the speaker) and hands it to the dialog interface's float
/// callback; the host reads the DLG and decides whether it can show it.
/// </summary>
public readonly record struct ScrFloatLine(int Script, int Line, ScrObjectRef Speaker, ScrObjectRef Pc);

/// <summary>What a script's sound action asks for (ARC-SCRIPT-040).</summary>
public enum ScrSoundKind
{
    /// <summary>Play sound (82): the effect <see cref="ScrSound.Id"/> once, not positional (volume 127, centre).</summary>
    Effect,
    /// <summary>Play sound on (83): the effect <see cref="ScrSound.Id"/> once on <see cref="ScrSound.Object"/>'s tile.</summary>
    OnObject,
    /// <summary>Play sound scheme (87): the scheme pair <see cref="ScrSound.Id"/> (music) and <see cref="ScrSound.Ambient"/>.</summary>
    Scheme,
}

/// <summary>
/// A sound action of a run (ARC-SCRIPT-040; checked in the 1.0.7.4 executable, docs/research/audio-evidence.md SN28): play sound
/// and play sound on name an effect id, play sound scheme a music and an ambient scheme as the session's scheme request takes them.
/// The original plays them at once; here the host plays them when it applies the run.
/// </summary>
public readonly record struct ScrSound(ScrSoundKind Kind, int Id, int Ambient = 0, ScrObjectRef? Object = null);

/// <summary>
/// ARC-SCRIPT-016: a set-auto-level-scheme action (139) for one object of its object operand (ledger OL30): the original writes the
/// number into the auto-level scheme of a PC or an NPC (0x4A6D90), with no range check, ignores any other object, and buys nothing
/// until the object's next level step. The host decides what the object is.
/// </summary>
public readonly record struct ScrSchemeRequest(ScrObjectRef Object, int Scheme);

/// <summary>
/// ARC-TECHNOLOGY-005: a start-schematic-UI action (129) for the object of its object operand (ledger SW8). The original calls an
/// interface callback slot that is never filled (TC2, both fixed): a PC opens the window as crafter and viewer, an NPC with its PC
/// leader as viewer, an NPC without a leader nothing. The host decides what the object is.
/// </summary>
public readonly record struct ScrSchematicRequest(ScrObjectRef Object);

/// <summary>ARC-UI-049: what a reading action shows: a book (64), a picture (65) or a plaque (114).</summary>
public enum ScrReadingKind { Book, Picture, Plaque }

/// <summary>
/// ARC-UI-049: a reading action (64 show book, 65 show picture, 114 show plaque; ledger WR rows) with its number operand (the start
/// line, or the art of a picture) and its viewer object. The original opens the reading window only for the local player, with no
/// item and no dead check; the host decides what the viewer is.
/// </summary>
public readonly record struct ScrReadingRequest(ScrReadingKind Kind, int Number, ScrObjectRef Viewer);

/// <summary>
/// ARC-MAGIC-041 (ledger TI8): use ammunition units (135): take <see cref="Count"/> units of ammunition type <see cref="AmmoType"/> from the object
/// operand 1 names, as the original's unit use (0x4658E0) does with no destination. The type is fixed at 2 (the power cells) by the action; the
/// original ignores the answer (a holder with too few units loses none and the script goes on), so the host does the same.
/// </summary>
public readonly record struct ScrAmmoUse(ScrObjectRef Holder, int Count, int AmmoType);

/// <summary>
/// ARC-SCRIPT-1241 (script action 103 `fade`, <c>docs/research/bink-movies.md</c> MV5): the four number operands in order. The original fades the
/// screen out, jumps the game clock by <see cref="Seconds"/> × 1000 ms when that is above 0, then plays movie number <see cref="Movie"/>
/// (<see cref="Sound"/>, when above 0, is the movie's audio track id) or, when <see cref="Movie"/> is 0, the sound <see cref="Sound"/> when above 0,
/// and brings the screen back at once when <see cref="Flag"/> is 0 or after <see cref="Flag"/> seconds. The whole action is skipped in a network
/// host game; a single-player game never does. The interpreter hands the operands as read, the host does the rest.
/// </summary>
public readonly record struct ScrFade(int Seconds, int Sound, int Movie, int Flag);

/// <summary>ARC-RENDER-1171: what a slide script asks of the slide show (ledger SS3, SS4).</summary>
public enum ScrSlideKind
{
    /// <summary>Queue slide (131, 0x447220 case 0x83 → 0x460B80 → 0x569AA0): the slide number joins the show's queue (up to 100; the rest are dropped).</summary>
    Queue,
    /// <summary>End game and play slides (132, 0x447220 case 0x84 → 0x460530 → 0x57CDE0): the end of the game is asked for; the interface shows the end-game slides 50 ms later and resets the game.</summary>
    EndGame,
}

/// <summary>ARC-RENDER-1171 (script actions 131 and 132, ledger SS3, SS4): a queued slide (<see cref="Slide"/> is its number as the operand read) or the end-game request
/// (<see cref="Slide"/> is 0, never read). Requests come in action order.</summary>
public readonly record struct ScrSlideRequest(ScrSlideKind Kind, int Slide);

/// <summary>ARC-SCRIPT-1764: what a newspaper script action asks of the host (ledger NP9, NP10).</summary>
public enum ScrNewspaperKind
{
    /// <summary>Queue newspaper (85, 0x447220 case 0x55 → 0x4BF140): the issue joins the session's queue by the rule of ledger NQ3.</summary>
    Queue,
    /// <summary>Float newspaper headline (86, case 0x56): the head issue's headline floats over the object of operand slot 1 (0x56CCA0 text, X9).</summary>
    FloatHeadline,
}

/// <summary>ARC-SCRIPT-1764 (script actions 85 and 86, ledger NP9, NP10): a queued issue (<see cref="Issue"/> and <see cref="Front"/>, the operands as read; the second is the dialog code's "second operand not 0")
/// or the float of the head issue's headline over <see cref="Speaker"/> (the issue and <see cref="Front"/> unused). Requests come in action order.
/// ARC-UI-2055 (TB26): <see cref="Listener"/> is the run's triggerer, which the original hands to the speech display unchecked (0x447220 case 0x56), so it can be null.</summary>
public readonly record struct ScrNewspaperRequest(ScrNewspaperKind Kind, int Issue, bool Front, ScrObjectRef? Speaker, ScrObjectRef? Listener = null);

/// <summary>The final value of a global flag the run changed.</summary>
public readonly record struct ScrFlagWrite(int Index, bool Value);

/// <summary>ARC-DEV-100: the final value of a global variable the run changed.</summary>
public readonly record struct ScrVariableWrite(int Index, int Value);

/// <summary>ARC-DEV-265: the final value of a variable of one PC the run changed (field 273 of that PC, SA14).</summary>
public readonly record struct ScrPcVariableWrite(ScrObjectRef Pc, int Index, int Value);

/// <summary>ARC-DEV-265: the final value of a flag of one PC the run changed (field 272 of that PC, SA14).</summary>
public readonly record struct ScrPcFlagWrite(ScrObjectRef Pc, int Index, bool Value);

/// <summary>ARC-DEV-100, ARC-DEV-265: what a dispatch changed in the script stores of <see cref="IScrStoreQueries"/>: the global variables in
/// ascending index order, the PC lists ordered by the PC (<see cref="ScrPcOrder"/>) and then by index. A PC flag write is already spread over
/// the flags of its 32-flag word (X10 under the Original setting), as a global flag's is.</summary>
public sealed record ScrStoreWrites(IReadOnlyList<ScrVariableWrite> GlobalVariables, IReadOnlyList<ScrPcVariableWrite> PcVariables, IReadOnlyList<ScrPcFlagWrite> PcFlags)
{
    public bool IsEmpty => GlobalVariables.Count == 0 && PcVariables.Count == 0 && PcFlags.Count == 0;
}

/// <summary>ARC-DEV-265: the order of the PC cells in <see cref="ScrStoreWrites"/>: the PC by map, then by code (ordinal), then the index.</summary>
public sealed class ScrPcOrder : IComparer<(ScrObjectRef Pc, int Index)>
{
    public static ScrPcOrder Instance { get; } = new();

    public int Compare((ScrObjectRef Pc, int Index) x, (ScrObjectRef Pc, int Index) y) =>
        x.Pc.Map != y.Pc.Map ? x.Pc.Map.CompareTo(y.Pc.Map)
        : string.CompareOrdinal(x.Pc.Code, y.Pc.Code) is var code and not 0 ? code
        : x.Index.CompareTo(y.Index);
}

/// <summary>ARC-SCRIPT-019: which game state a state request changes (actions 16, 49, 50, 72, 73); ARC-SCRIPT-1165 (E5-c, ledger SQ1–SQ12) adds the story actions of set <see cref="ScrAdmissionSet.S34"/>.</summary>
public enum ScrStateKind
{
    QuestState, KnownArea, Rumor, AddReputation, RemoveReputation,
    /// <summary>Set quest global state (17): <see cref="ScrStateRequest.Number"/> the quest, <see cref="ScrStateRequest.Value"/> the state; no object (0x4C51C0).</summary>
    GlobalQuestState,
    /// <summary>Quell rumor (51): <see cref="ScrStateRequest.Number"/> the rumor, asked for the first object of operand 1 (0x4C5690, for the local player only).</summary>
    QuellRumor,
    /// <summary>Give quest experience (63): <see cref="ScrStateRequest.Number"/> the quest experience level of operand 1 (0x4C53C0, 0x45F110), asked for the first object of operand 0.</summary>
    QuestExperience,
    /// <summary>Grant one fate point (117): the first object of operand 0, a PC only (0x44A2B3).</summary>
    FatePoint,
    /// <summary>Adjust stat (137, 0x447220 case 0x89): <see cref="ScrStateRequest.Number"/> the stat, <see cref="ScrStateRequest.Value"/> the number added to the object's base stat (0x4B0740) and written through the stat setter (0x4B0980, which clamps to the stat's range).</summary>
    AdjustStat,
    /// <summary>Set PC quest unbotched (119): <see cref="ScrStateRequest.Number"/> the quest, one request per object of operand 0 in list order (0x4C5070).</summary>
    QuestUnbotched,
    /// <summary>Add blessing (25): <see cref="ScrStateRequest.Number"/> the blessing, one request per object of operand 1 in list order (0x4C42F0).</summary>
    AddBlessing,
    /// <summary>Remove blessing (26): as <see cref="AddBlessing"/> for 0x4C4450.</summary>
    RemoveBlessing,
    /// <summary>Add curse (27): as <see cref="AddBlessing"/> for 0x4C3E40.</summary>
    AddCurse,
    /// <summary>Remove curse (28): as <see cref="AddBlessing"/> for 0x4C3FA0.</summary>
    RemoveCurse,
}

/// <summary>
/// ARC-SCRIPT-019: a change of the PC's story the run asks for (ledger SS2–SS5): set quest state (16: the quest number and the
/// state, with the run's attachee as the quest's giver), mark a world-map area known (49), learn a rumor (50), add or remove a
/// reputation (72, 73). <paramref name="Object"/> is the object it is asked for (null for a game-wide request, <see cref="ScrStateKind.GlobalQuestState"/>); the
/// host applies it to a PC and ignores any other object, as the original's functions do.
/// </summary>
public sealed record ScrStateRequest(ScrStateKind Kind, ScrObjectRef? Object, int Number, int Value = 0, ScrObjectRef? Giver = null);

/// <summary>
/// Every change of one dispatch, for the caller to apply together: the slot writeback (or the trap object to destroy
/// instead), the global flags in ascending order and the new story state. Empty when nothing changed.
/// </summary>
public sealed class ScrEffects
{
    internal ScrEffects(ScrWriteback? writeback, ScrObjectRef? destroyTrapObject, IReadOnlyList<ScrFlagWrite> globalFlags, int? storyState, ScrDialogStart? dialog = null,
        IReadOnlyList<ScrCallRequest>? calls = null, IReadOnlyList<ScrPrintLine>? lines = null, IReadOnlyList<ScrSound>? sounds = null,
        IReadOnlyList<ScrTeleport>? teleports = null, IReadOnlyList<ScrSchemeRequest>? schemes = null, IReadOnlyList<ScrSchematicRequest>? schematics = null,
        IReadOnlyList<ScrReadingRequest>? readings = null, IReadOnlyList<ScrSkillUse>? skillUses = null, IReadOnlyList<ScrStateRequest>? states = null,
        IReadOnlyList<ScrEffectRequest>? effectChanges = null, IReadOnlyList<ScrEyeCandy>? eyeCandies = null, IReadOnlyList<ScrWriteback>? slotWrites = null,
        IReadOnlyList<ScrCreateRequest>? creations = null, IReadOnlyList<ScrObjectStateRequest>? objectStates = null, IReadOnlyList<ScrFloatLine>? floats = null,
        ScrStoreWrites? stores = null, IReadOnlyList<int>? townMaps = null, IReadOnlyList<ScrAmmoUse>? ammoUses = null, IReadOnlyList<ScrFade>? fades = null,
        IReadOnlyList<ScrWorldRequest>? worldChanges = null, IReadOnlyList<ScrSlideRequest>? slides = null, IReadOnlyList<ScrNewspaperRequest>? newspapers = null)
    {
        Newspapers = newspapers ?? Array.Empty<ScrNewspaperRequest>();
        Slides = slides ?? Array.Empty<ScrSlideRequest>();
        WorldChanges = worldChanges ?? Array.Empty<ScrWorldRequest>();
        Fades = fades ?? Array.Empty<ScrFade>();
        AmmoUses = ammoUses ?? Array.Empty<ScrAmmoUse>();
        TownMaps = townMaps ?? Array.Empty<int>();
        GlobalVariables = stores?.GlobalVariables ?? Array.Empty<ScrVariableWrite>();
        PcVariables = stores?.PcVariables ?? Array.Empty<ScrPcVariableWrite>();
        PcFlags = stores?.PcFlags ?? Array.Empty<ScrPcFlagWrite>();
        Floats = floats ?? Array.Empty<ScrFloatLine>();
        Creations = creations ?? Array.Empty<ScrCreateRequest>();
        ObjectStates = objectStates ?? Array.Empty<ScrObjectStateRequest>();
        States = states ?? Array.Empty<ScrStateRequest>();
        EffectChanges = effectChanges ?? Array.Empty<ScrEffectRequest>();
        EyeCandies = eyeCandies ?? Array.Empty<ScrEyeCandy>();
        SlotWrites = slotWrites ?? Array.Empty<ScrWriteback>();
        Writeback = writeback; DestroyTrapObject = destroyTrapObject; GlobalFlags = globalFlags; StoryState = storyState; Dialog = dialog;
        Calls = calls ?? Array.Empty<ScrCallRequest>();
        Lines = lines ?? Array.Empty<ScrPrintLine>();
        Sounds = sounds ?? Array.Empty<ScrSound>();
        Teleports = teleports ?? Array.Empty<ScrTeleport>();
        Schemes = schemes ?? Array.Empty<ScrSchemeRequest>();
        Schematics = schematics ?? Array.Empty<ScrSchematicRequest>();
        Readings = readings ?? Array.Empty<ScrReadingRequest>();
        SkillUses = skillUses ?? Array.Empty<ScrSkillUse>();
    }

    public static ScrEffects None { get; } = new(null, null, Array.Empty<ScrFlagWrite>(), null);
    public ScrWriteback? Writeback { get; }
    public ScrObjectRef? DestroyTrapObject { get; }
    public IReadOnlyList<ScrFlagWrite> GlobalFlags { get; }
    /// <summary>ARC-DEV-100 (GV1): the global variables the dispatch changed, ascending, each with its final value.</summary>
    public IReadOnlyList<ScrVariableWrite> GlobalVariables { get; }
    /// <summary>ARC-DEV-100 (GV3), ARC-DEV-265: the PC variables the dispatch changed, each with its PC and final value, by PC and then index.</summary>
    public IReadOnlyList<ScrPcVariableWrite> PcVariables { get; }
    /// <summary>ARC-DEV-100 (GV4), ARC-DEV-265: the PC flags the dispatch changed, each with its PC and final value, by PC and then index.</summary>
    public IReadOnlyList<ScrPcFlagWrite> PcFlags { get; }
    public int? StoryState { get; }
    /// <summary>The conversation to start, applied after everything else.</summary>
    public ScrDialogStart? Dialog { get; }
    /// <summary>The deferred calls to schedule or remove, in action order (ARC-SCRIPT-010).</summary>
    public IReadOnlyList<ScrCallRequest> Calls { get; }
    /// <summary>The lines to print, in action order (ARC-SCRIPT-013).</summary>
    public IReadOnlyList<ScrPrintLine> Lines { get; }
    /// <summary>The sounds to play and scheme pairs to request, in action order (ARC-SCRIPT-040).</summary>
    public IReadOnlyList<ScrSound> Sounds { get; }
    /// <summary>The teleports to request, in action order (ARC-SCRIPT-014).</summary>
    public IReadOnlyList<ScrTeleport> Teleports { get; }
    /// <summary>The auto-level schemes to set, in action order, one per object (ARC-SCRIPT-016).</summary>
    public IReadOnlyList<ScrSchemeRequest> Schemes { get; }
    /// <summary>The schematic windows to open, in action order (ARC-TECHNOLOGY-005).</summary>
    public IReadOnlyList<ScrSchematicRequest> Schematics { get; }
    /// <summary>ARC-UI-049: the reading actions in action order.</summary>
    public IReadOnlyList<ScrReadingRequest> Readings { get; }
    /// <summary>ARC-SKILLS-009: the use-item-with-skill requests in action order.</summary>
    public IReadOnlyList<ScrSkillUse> SkillUses { get; }
    /// <summary>ARC-SCRIPT-019: the quest, area, rumor and reputation requests in action order.</summary>
    public IReadOnlyList<ScrStateRequest> States { get; }
    /// <summary>ARC-SCRIPT-042: the effect adds and removals in action order, one per object (ledger WE1).</summary>
    public IReadOnlyList<ScrEffectRequest> EffectChanges { get; }
    /// <summary>ARC-SCRIPT-042: the script eye candy to show, in action order (ledger WE4).</summary>
    public IReadOnlyList<ScrEyeCandy> EyeCandies { get; }
    /// <summary>
    /// ARC-SCRIPT-042: the slots the dispatch's call-ex runs (81, ledger WE3) changed, in the order the runs ended, each the object's
    /// slot for the point as that run left it; since ARC-SCRIPT-046 also the attachee slot a called script's dialog action stored its flags
    /// and counters into (ledger X11), in action order. The caller stores them in this order and then <see cref="Writeback"/>, so the event's
    /// own slot wins over a call-ex run of the same slot, as the original's outer run writes its copy back last.
    /// </summary>
    public IReadOnlyList<ScrWriteback> SlotWrites { get; }
    /// <summary>ARC-WORLD-081: the objects to create, in action order (ledger PG1).</summary>
    public IReadOnlyList<ScrCreateRequest> Creations { get; }
    /// <summary>ARC-WORLD-114: the toggle-state and kill requests, in action order (ledger SU6, PM1).</summary>
    public IReadOnlyList<ScrObjectStateRequest> ObjectStates { get; }
    /// <summary>ARC-SCRIPT-050: the float lines, one per object, in action order (ledger FL4).</summary>
    public IReadOnlyList<ScrFloatLine> Floats { get; }
    /// <summary>ARC-DEV-205: the town maps unfog town map (113) asked to reveal whole, in action order, each a positive number (ledger UF1). The
    /// original's town map is game-wide (a network game sends the reveal to the other machines), so the request names no object.</summary>
    public IReadOnlyList<int> TownMaps { get; }
    /// <summary>ARC-MAGIC-041: the use ammunition units requests (135), in action order (ledger TI8).</summary>
    public IReadOnlyList<ScrAmmoUse> AmmoUses { get; }
    /// <summary>ARC-SCRIPT-1241: the fade actions (103), in action order, each with its four operands as read (MV5).</summary>
    public IReadOnlyList<ScrFade> Fades { get; }
    /// <summary>ARC-SCRIPT-1164: the sector blocked and monster generator toggles (106, 110), in action order, each with the state it ends in (ledger WO5, WO9).</summary>
    public IReadOnlyList<ScrWorldRequest> WorldChanges { get; }
    /// <summary>ARC-RENDER-1171: the queue slide (131) and end game (132) requests, in action order (ledger SS3, SS4).</summary>
    public IReadOnlyList<ScrSlideRequest> Slides { get; }
    /// <summary>ARC-SCRIPT-1764: the queue newspaper (85) and float newspaper headline (86) requests, in action order (ledger NP9, NP10).</summary>
    public IReadOnlyList<ScrNewspaperRequest> Newspapers { get; }
    public bool IsEmpty => Writeback is null && DestroyTrapObject is null && GlobalFlags.Count == 0 && StoryState is null && Dialog is null && Calls.Count == 0
        && Lines.Count == 0 && Sounds.Count == 0 && Teleports.Count == 0 && Schemes.Count == 0 && Schematics.Count == 0 && Readings.Count == 0 && SkillUses.Count == 0 && States.Count == 0
        && EffectChanges.Count == 0 && EyeCandies.Count == 0 && SlotWrites.Count == 0 && Creations.Count == 0 && ObjectStates.Count == 0
        && Floats.Count == 0 && GlobalVariables.Count == 0 && PcVariables.Count == 0 && PcFlags.Count == 0
        && TownMaps.Count == 0 && AmmoUses.Count == 0 && Fades.Count == 0 && WorldChanges.Count == 0 && Slides.Count == 0 && Newspapers.Count == 0;

    /// <summary>ARC-SCRIPT-2440: whether any request of the run names <paramref name="obj"/> (the run's own <see cref="Writeback"/> and <see cref="DestroyTrapObject"/> are the event's attachee and are not asked).</summary>
    public bool Names(ScrObjectRef obj)
    {
        var found = false;
        Rewrite(o => { if (o == obj) found = true; return o; }, false);
        return found;
    }

    /// <summary>ARC-SCRIPT-2440 (ledger TI5, F11): the same effects with every request that names <paramref name="from"/> naming <paramref name="to"/>: a host that applies a transform (<see cref="IScrHost.RebindsTransformedAttachee"/>) rebinds the
    /// placeholder of the made object to it before it applies the later requests. The run's own <see cref="Writeback"/> (the old attachee's slot, which the original writes back to the old object) and <see cref="DestroyTrapObject"/> stay as they are.</summary>
    public ScrEffects Rebind(ScrObjectRef from, ScrObjectRef to) => Rewrite(o => o == from ? to : o, true);

    private ScrEffects Rewrite(Func<ScrObjectRef, ScrObjectRef> map, bool build)
    {
        ScrObjectRef? Opt(ScrObjectRef? o) => o is { } value ? map(value) : null;
        var calls = Calls.Select(c => c with { Triggerer = Opt(c.Triggerer), Attachee = Opt(c.Attachee) }).ToArray();
        var sounds = Sounds.Select(c => c with { Object = Opt(c.Object) }).ToArray();
        var teleports = Teleports.Select(c => c with { Object = map(c.Object) }).ToArray();
        var schemes = Schemes.Select(c => c with { Object = map(c.Object) }).ToArray();
        var schematics = Schematics.Select(c => c with { Object = map(c.Object) }).ToArray();
        var readings = Readings.Select(c => c with { Viewer = map(c.Viewer) }).ToArray();
        var skillUses = SkillUses.Select(c => c with { Source = map(c.Source), Item = Opt(c.Item), Target = map(c.Target) }).ToArray();
        var states = States.Select(c => c with { Object = Opt(c.Object), Giver = Opt(c.Giver) }).ToArray();
        var effectChanges = EffectChanges.Select(c => c with { Object = map(c.Object) }).ToArray();
        var eyeCandies = EyeCandies.Select(c => c with { Object = map(c.Object) }).ToArray();
        var slotWrites = SlotWrites.Select(c => c with { Attachee = Opt(c.Attachee) }).ToArray();
        var creations = Creations.Select(c => c with { Near = map(c.Near) }).ToArray();
        var objectStates = ObjectStates.Select(c => c with { Object = map(c.Object), Other = Opt(c.Other) }).ToArray();
        var floats = Floats.Select(c => c with { Speaker = map(c.Speaker), Pc = map(c.Pc) }).ToArray();
        var ammoUses = AmmoUses.Select(c => c with { Holder = map(c.Holder) }).ToArray();
        var newspapers = Newspapers.Select(c => c with { Speaker = Opt(c.Speaker), Listener = Opt(c.Listener) }).ToArray();
        var dialog = Dialog is null ? null : Dialog with { Pc = map(Dialog.Pc), Npc = Opt(Dialog.Npc) };
        if (!build) return this;
        return new ScrEffects(Writeback, DestroyTrapObject, GlobalFlags, StoryState, dialog, calls, Lines, sounds, teleports, schemes, schematics, readings, skillUses, states, effectChanges, eyeCandies, slotWrites, creations,
            objectStates, floats, new ScrStoreWrites(GlobalVariables, PcVariables, PcFlags), TownMaps, ammoUses, Fades, WorldChanges, Slides, newspapers);
    }
}

/// <summary>One executed line: the condition, whether it held, the action of the branch taken and where the run went
/// next (null when the line ended the run).</summary>
public readonly record struct ScrTraceStep(int Line, int Condition, bool Held, int Action, int? Next);

/// <summary>The result of one dispatch. <see cref="Script"/> is the number that ran (2188 for a mind-controlled NPC's
/// dialog), or null when nothing ran.</summary>
public sealed class ScrRunResult
{
    internal ScrRunResult(ScrOutcome outcome, ScrStop stop, int? script, int? stopLine, ScrRefusal? refusal, IReadOnlyList<ScrTraceStep> trace, ScrEffects effects)
    {
        Outcome = outcome; Stop = stop; Script = script; StopLine = stopLine; Refusal = refusal; Trace = trace; Effects = effects;
    }

    public ScrOutcome Outcome { get; }
    public ScrStop Stop { get; }
    public int? Script { get; }
    /// <summary>The line that ended the run, when one did.</summary>
    public int? StopLine { get; }
    public ScrRefusal? Refusal { get; }
    public IReadOnlyList<ScrTraceStep> Trace { get; }
    public ScrEffects Effects { get; }
}

/// <summary>How a dispatch runs: the rule mode's script switches, the admitted set (default <see cref="ScrAdmissionSet.S5"/>,
/// at most <see cref="ScrAdmissionSet.S65"/>, admitted for the lines reachable from the event's line), the executed-line
/// budget and the call depth.</summary>
public sealed record ScrRunOptions
{
    /// <summary>The original stops a run after 1,000 executed lines.</summary>
    public const int OriginalLineBudget = 1000;

    public ScrRuleProfile Rules { get; init; } = ScrRuleProfile.Steamwoven;
    public ScrAdmissionSet Admission { get; init; } = ScrAdmissionSet.S5;
    /// <summary>1..1000; lower values are for tests and tools.</summary>
    public int MaxExecutedLines { get; init; } = OriginalLineBudget;
    /// <summary>ARC-SCRIPT-018: how deep call script (7) may nest, 1..64 (the original's recursion is bounded only by its
    /// stack, ledger OV1). Each called run has its own line budget, and the whole dispatch at most this many budgets.</summary>
    public int MaxCallDepth { get; init; } = 16;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rules);
        ArgumentNullException.ThrowIfNull(Admission);
        if (MaxExecutedLines is <= 0 or > OriginalLineBudget) throw new ArgumentOutOfRangeException(nameof(MaxExecutedLines), "The line budget is 1..1000.");
        if (MaxCallDepth is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(MaxCallDepth), "The call depth is 1..64.");
        if (!Admission.IsSubsetOf(ScrAdmissionSet.S65)) throw new ArgumentException("The interpreter runs only the commands of ScrAdmissionSet.S65.", nameof(Admission));
    }

    // A set with a guard condition needs a host that answers it, checked before any event runs.
    internal void Validate(IScrHost host)
    {
        Validate();
        if (host is not IScrObjectQueries && (Admission.Conditions.Any(ScrAdmissionSet.ObjectQueryConditions.Contains) || Admission.Actions.Any(ScrAdmissionSet.ObjectQueryActions.Contains)))
            throw new ArgumentException("A set with a guard condition or get object type needs a host that implements IScrObjectQueries.", nameof(Admission));
        if (host is not IScrSkillQueries && Admission.Actions.Contains(ScrCatalogue.Action.GetSkill))
            throw new ArgumentException("A set with get skill needs a host that implements IScrSkillQueries.", nameof(Admission));
        if (host is not IScrReactionQueries && Admission.Actions.Contains(ScrCatalogue.Action.GetReaction))
            throw new ArgumentException("A set with get reaction needs a host that implements IScrReactionQueries.", nameof(Admission));
        if (host is not IScrStatQueries && Admission.Actions.Contains(ScrCatalogue.Action.GetStat))
            throw new ArgumentException("A set with get stat needs a host that implements IScrStatQueries.", nameof(Admission));
        if (host is not IScrItemQueries && (Admission.Conditions.Any(ScrAdmissionSet.ItemQueryConditions.Contains) || Admission.Actions.Contains(ScrCatalogue.Action.MagickTechAdjustment)))
            throw new ArgumentException("A set with the identified or rewielding condition or the magick-tech adjustment needs a host that implements IScrItemQueries.", nameof(Admission));
        if (host is not IScrCritterQueries && Admission.Conditions.Any(ScrAdmissionSet.CritterQueryConditions.Contains))
            throw new ArgumentException("A set with the animal condition needs a host that implements IScrCritterQueries.", nameof(Admission));
        if (host is not IScrCritterStateQueries && Admission.Conditions.Any(c => ScrAdmissionSet.CritterStateConditions.Contains(c) || ScrAdmissionSet.CritterTailQueryConditions.Contains(c)))
            throw new ArgumentException("A set with a critter-state condition (or bad associates, monster of type) needs a host that implements IScrCritterStateQueries.", nameof(Admission));
        if (host is not IScrCritterFieldQueries && Admission.Actions.Any(a => ScrAdmissionSet.CritterFieldActions.Contains(a) || ScrAdmissionSet.CritterArtActions.Contains(a)))
            throw new ArgumentException("A set with a critter getter (social class, origin, faction, armor) needs a host that implements IScrCritterFieldQueries.", nameof(Admission));
        if (host is not IScrStatAdjustQueries && Admission.Actions.Contains(ScrCatalogue.Action.AdjustStat))
            throw new ArgumentException("A set with adjust stat needs a host that implements IScrStatAdjustQueries.", nameof(Admission));
        if (host is not IScrMagickTechQueries && Admission.Actions.Contains(ScrCatalogue.Action.GetMagickTechAdjustment))
            throw new ArgumentException("A set with get magick-tech adjustment needs a host that implements IScrMagickTechQueries.", nameof(Admission));
        if (host is not IScrVitalQueries && Admission.Actions.Any(ScrAdmissionSet.VitalQueryActions.Contains))
            throw new ArgumentException("A set with get hit points or get fatigue points needs a host that implements IScrVitalQueries.", nameof(Admission));
        if (host is not IScrWorldQueries && (Admission.Actions.Any(ScrAdmissionSet.WorldQueryActions.Contains) || Admission.FocusKinds.Any(ScrAdmissionSet.WorldQueryFocusKinds.Contains) || Admission.Conditions.Contains(ScrCatalogue.Condition.WithinRange)))
            throw new ArgumentException("A set with random, loop for, distance between, get location, float line, obj is within range or the vicinity focus kinds needs a host that implements IScrWorldQueries.", nameof(Admission));
        if (host is not IScrGroupQueries && Admission.FocusKinds.Any(ScrAdmissionSet.GroupFocusKinds.Contains))
            throw new ArgumentException("A set with the focus kinds every one or anyone in the group, or every or any follower, needs a host that implements IScrGroupQueries.", nameof(Admission));
        if (host is not IScrPortalQueries && Admission.FocusKinds.Contains(ScrFocusKind.EveryPortalInVicinity))
            throw new ArgumentException("A set with the focus kind every portal in the vicinity needs a host that implements IScrPortalQueries.", nameof(Admission));
        if (host is not IScrContainerQueries && (Admission.FocusKinds.Contains(ScrFocusKind.EveryContainerInVicinity) || Admission.FocusKinds.Contains(ScrFocusKind.AnyContainerInVicinity)))
            throw new ArgumentException("A set with a container vicinity focus kind needs a host that implements IScrContainerQueries.", nameof(Admission));
        if (host is not IScrObjectStateQueries && Admission.Conditions.Any(ScrAdmissionSet.ObjectStateQueryConditions.Contains))
            throw new ArgumentException("A set with the dead or switched-off condition needs a host that implements IScrObjectStateQueries.", nameof(Admission));
        if (host is not IScrSpellQueries && Admission.Conditions.Any(ScrAdmissionSet.SpellQueryConditions.Contains))
            throw new ArgumentException("A set with a spell condition (body spell, influenced by spell, knows spell, mastered college, polymorphed, shrunk, mirror image) needs a host that implements IScrSpellQueries.", nameof(Admission));
        if (host is not IScrAreaQueries && Admission.Actions.Contains(ScrCatalogue.Action.GetArea))
            throw new ArgumentException("A set with get area needs a host that implements IScrAreaQueries.", nameof(Admission));
        if (host is not IScrStoryQueries && (Admission.Conditions.Any(ScrAdmissionSet.StoryQueryConditions.Contains) || Admission.Actions.Any(ScrAdmissionSet.StoryQueryActions.Contains)))
            throw new ArgumentException("A set with daytime, global quest state, a rumor, blessing or curse condition a clock action, set global quest state or unbotch needs a host that implements IScrStoryQueries.", nameof(Admission));
        if (host is not IScrWorldObjectQueries && (Admission.Conditions.Any(ScrAdmissionSet.WorldObjectQueryConditions.Contains) || Admission.Actions.Any(ScrAdmissionSet.WorldObjectQueryActions.Contains)))
            throw new ArgumentException("A set with obj is open, sector is blocked, monster generator disabled or a sector, generator or open toggle needs a host that implements IScrWorldObjectQueries.", nameof(Admission));
        if (host is not IScrStoreQueries && Admission.ValueKinds.Any(ScrAdmissionSet.StoreValueKinds.Contains))
            throw new ArgumentException("A set with a global variable, PC variable or PC flag operand needs a host that implements IScrStoreQueries.", nameof(Admission));
    }
}
