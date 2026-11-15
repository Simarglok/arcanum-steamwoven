using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-081: what the world actions of a use, tile or sector script read from the viewer (set S15): the PC's live cell (the session's
/// actor placement follows the walking actor only at a save or a map change), whether a shown row is destroyed or switched off in the session
/// (the original's object lists skip it), the item a script's prototype number names (<see cref="AreaPrototypeItems.ScriptItem"/>), and the
/// shown map as the create action's placement reads it (null when there is none). ARC-WORLD-114: <see cref="Kill"/> applies a script's
/// kill (58) to an NPC of the shown map, a death with no killer, and answers whether the session took it; <see cref="CanKill"/> says
/// beforehand whether it can (the NPC's combat values read; null: always). Without <see cref="Kill"/>, or for an NPC it cannot kill, a
/// kill leaves the run unapplied. ARC-SCRIPT-047: <see cref="CritterCell"/> is an NPC's live cell when it is not where its map data puts
/// it (an NPC the AI moved, a companion; null: its row's cell), for the critters in a run's vicinity. ARC-SCRIPT-050: <see cref="MapArea"/> is
/// the shown map's map-list area and whether it is the start map, for get area (84); null when the viewer has no map-list row for it (outside
/// map mode), and then get area is not answered. ARC-SCRIPT-052: <see cref="PeaceFacts"/> are an NPC's facts for a script's stop attacking
/// (109; null: the session's defaults) and <see cref="Stopped"/> is the host's part of each NPC's stop (its animation interrupt), run after
/// the session took it. ARC-DEV-178 (B19): <see cref="StartMapAreaAt"/> is the area at a tile of the start map (<see cref="AreaStartMapAreas"/>) for get
/// area when the shown map is the start map with map-list area 0; null (or an answer of null): not answered. ARC-DEV-231 (MIG13 S14a, GT158–GT161):
/// <see cref="ScriptWalk"/> starts a script's walk to (69) or run to (74) of an NPC of the shown map to a cell (the AI host's goal 7 / 55 starter) and answers what happened;
/// without it a run with a walk or run action is left unapplied. ARC-DEV-233 (S14b, GT162): <see cref="HealFatigue"/> takes an amount of fatigue damage off an NPC of the shown map through the session's life
/// step (whose get-up starts for a critter that comes round) and answers whether the session took it; without it a run with a heal fatigue is left unapplied. <see cref="ScriptAnimate"/> starts a script's perform animation (62, goal 0 or 60) of an NPC of the shown map in the AI host and
/// answers what happened; without it a run with a perform animation is left unapplied. ARC-DEV-234 (S14c, GT164): <see cref="ScriptSteal"/> starts a script's try to steal 100 coins (115) of an NPC of the shown map
/// from another NPC or the PC (<see cref="AreaScriptWorld"/> passes the victim as the PC's scheduler id) and answers what happened; without it a run with a steal is left unapplied. ARC-DEV-236 (S14e, GT187): <see cref="ScriptProne"/> makes an NPC of the shown map lie down (a level-4 interrupt of its runs, then the fall art) and answers what happened; without it a run with a become prone is left unapplied. ARC-DEV-241 (S14f, GT183/GT184): <see cref="ScriptCast"/> (caster, target, spell, free, unresistable) casts a script's spell at an NPC of the shown map or the PC by an NPC of the shown map or by none, and answers what happened; without it a run with a cast is left unapplied. ARC-DEV-243 (S14g, GT194–GT197): <see cref="ScriptDamage"/> (victim, amount, damage type, unresistable) deals a script's damage to an NPC of the shown map or the PC and <see cref="ScriptHeal"/> (victim, amount) heals its hit points; each answers what happened, and without it a run with the action is left unapplied.
/// ARC-DEV-371 (E1-c3): <see cref="Critter"/> says whether a script's prototype number (the basic number, <see cref="AreaPrototypeItems.PrototypeOffset"/> added for the file) names a critter prototype the
/// session can create (a prototype file that reads as an NPC's), so a create object of it makes a critter (<see cref="GameSessionOwner.CreateCritterNear"/>) and not a ground item; without it a critter prototype is
/// refused as before. <see cref="CreatedRow"/> answers a created critter's base record by its entity (the run's NPC rows are the shown map's and the loaded maps'; a critter created at run time has none of them).
/// ARC-DEV-386 (E1-c4): <see cref="CritterMade"/> is told the entity of each critter the run created, right after it exists, so the host generates its inventory (the original's create finish runs the restock body 0x463C60, OM16, RK14);
/// without it a created critter's list stays empty. ARC-SCRIPT-1163 (E5-a, NS2): <see cref="ScriptReaction"/> changes an NPC of the shown map's reaction toward the PC (set reaction 30 when its flag is true, adjust reaction 31) in the AI host and answers what happened;
/// ARC-SCRIPT-1481 (VT1–VT3): <see cref="Vitals"/> answers an NPC's vitals (the hero's are the session's) for get hit points, get fatigue points and get stat 24, and <see cref="ScriptHealLife"/> (victim, fatigue amount, poison amount) takes fatigue damage and poison level off the hero or an NPC of the shown map through the session's life step; without it a run with a hero's heal fatigue or a heal poison is left unapplied.
/// without it a run with a reaction action is left unapplied. ARC-SCRIPT-1164 (E5-b, WO3, WO4): <see cref="Openable"/> answers a portal's or container's lock identity, T0 baseline and ART facts on the shown map
/// (<see cref="AreaScriptOpenable"/>; null for an object that is neither or not shown), for set lock state (53), toggle open or closed (88) and obj is open (36); without it those runs are left unapplied and the condition stops the run.
/// ARC-SCRIPT-1294 (RM6): <see cref="ScriptStopSpell"/> (object entity, spell) interrupts the first active run of the spell that holds the object (the cast host's <see cref="Arcanum.Session.SessionSpellCaster.StopSpellOnObject"/>) and answers what happened;
/// without it a run with a stop spell is left unapplied. ARC-SCRIPT-1620 (TI6): <see cref="ScriptAttack"/> (attacker entity, victim entity) raises the attack event of a script's attack (36, 0x447D2F) in the AI host (flags 2, loudness 1) and answers the stop of its plan;
/// without it a run with an attack is left unapplied.
/// ARC-SCRIPT-1735 (E5-a tail, ledger NS5, NS9): <see cref="ArtExists"/> says whether an art id names an ART file of the module's content (the original's test 0x502220 before a change art number, action 59, writes the new art id); without it a run with a change art number is left unapplied.
/// ARC-SCRIPT-1562 (WE2, CP7, CP8): <see cref="Aptitude"/> answers an NPC of the shown map's effective magick-tech aptitude (the hero's is the session's) for the magick-tech adjustments (91, 80) of a combat-point script; without it those runs stop at an NPC.
/// ARC-SCRIPT-1733 (CS5, CS6): <see cref="CanSee"/> and <see cref="CanHear"/> (observer, target; the PC as the scheduler's id) answer the sight (0x4AF260) and hearing (0x4AF470, normal loudness) checks of an NPC through the AI host, null for an observer or target it
/// holds no facts of (the PC included since ARC-SCRIPT-1773); without them obj can see obj and obj can hear obj stop the run.
/// ARC-WORLD-1837 (ledger TT20): <see cref="NpcTeleportRefusal"/> names why a script's teleport (44, 102) of an NPC of the shown map to a cell of the shown map cannot be requested (the AI host does not hold it, it is a follower, the cell is not held), or null,
/// and <see cref="RequestNpcTeleport"/> records the request in the session's one slot (the original's 0x4D3380; the object moves at the next ping), called only after the run applied; without them a run that teleports an object other than the hero is left unapplied.
/// </summary>
public sealed record AreaScriptWorld(Func<CellCoord?> PcCell, Func<AreaObjectRecord, bool> Gone, Func<int, AreaPrototypeItem?> Item, Func<IDropMap?> DropMap,
    Func<ScrObjectRef, bool>? Kill = null, Func<ScrObjectRef, bool>? CanKill = null, Func<ScrObjectRef, CellCoord?>? CritterCell = null,
    Func<(int Area, bool StartMap)?>? MapArea = null, Func<string, SessionPeaceFacts>? PeaceFacts = null, Action<SessionPeaceStep>? Stopped = null,
    Func<CellCoord, int?>? StartMapAreaAt = null, Func<string, CellCoord, bool, string>? ScriptWalk = null,
    Func<ScrObjectRef, int, bool>? HealFatigue = null, Func<string, int, string>? ScriptAnimate = null, Func<string, string, string>? ScriptSteal = null, Func<string, string>? ScriptProne = null,
    Func<string?, string, int, bool, bool, string>? ScriptCast = null, Func<string, int, int, bool, string>? ScriptDamage = null, Func<string, int, string>? ScriptHeal = null,
    Func<string, bool>? ScriptDamageAsk = null, Func<int, bool>? Critter = null, Func<string, AreaObjectRecord?>? CreatedRow = null,
    Action<string>? CritterMade = null, Func<string, int, bool, string>? ScriptReaction = null, Func<ScrObjectRef, AreaScriptOpenable?>? Openable = null,
    Func<string, int, string>? ScriptStopSpell = null, Func<string, AreaScriptVitals?>? Vitals = null, Func<string, int, int, string>? ScriptHealLife = null,
    Func<string, string, string>? ScriptAttack = null, Func<string, int?>? Aptitude = null, Func<string, string, bool?>? CanSee = null, Func<string, string, bool?>? CanHear = null,
    Func<uint, bool>? ArtExists = null, Func<ScrTeleport, string?>? NpcTeleportRefusal = null, Action<ScrTeleport>? RequestNpcTeleport = null);

/// <summary>ARC-SCRIPT-1481 (VT1–VT3): the vitals of an NPC of the shown map for get hit points (107), get fatigue points (108) and get stat of the poison level (24): the current and maximum hit points and fatigue and the poison level,
/// as the session's life status gives them (<see cref="AreaScriptWorld.Vitals"/>).</summary>
public readonly record struct AreaScriptVitals(int HitPoints, int MaxHitPoints, int Fatigue, int MaxFatigue, int Poison);

/// <summary>ARC-SCRIPT-1164 (WO3, WO4): what a script's lock and open actions need of a portal or container of the shown map: the session's lock identity and T0 baseline (<see cref="GameSessionOwner.QueryOpenable"/>) and, for a portal, its ART
/// (<see cref="PortalArt"/>, the frames a toggle steps through, ledger LK10); for a container the ART's frame count (it opens to frame 1 when it has a second frame).</summary>
public sealed record AreaScriptOpenable(SessionOpenable Openable, SessionOpenableBaseline Baseline, Arcanum.Simulation.Locks.PortalArt? Art, int FrameCount)
{
    /// <summary>Whether a toggle of the closed object ends open: a portal has an open frame (<see cref="Arcanum.Simulation.Locks.PortalFrameRules.Toggled"/> from frame 0), a container a second frame.</summary>
    public bool CanOpen => Art is { } art ? Arcanum.Simulation.Locks.PortalFrameRules.Toggled(art, 0) != 0 : FrameCount > 1;

    /// <summary>The frame a toggle leaves from <paramref name="frame"/>: a portal's (<see cref="Arcanum.Simulation.Locks.PortalFrameRules.Toggled"/>), a container's previous frame when it is open (not frame 0) and 1 (its next frame) when closed (ledger WO4) with a second frame.</summary>
    public int ToggledFrame(int frame) => Art is { } art ? Arcanum.Simulation.Locks.PortalFrameRules.Toggled(art, frame) : frame != 0 ? frame - 1 : FrameCount > 1 ? 1 : 0;
}

/// <summary>ARC-WORLD-081: one create object a run applied: the prototype number as the script named it, the object it was created next to,
/// and the session's result (<see cref="InventoryMutationResult.Item"/> the new item; none under the Steamwoven limit).</summary>
public sealed record AreaScriptCreation(int Prototype, ScrObjectRef Near, InventoryMutationResult Result)
{
    /// <summary>ARC-DEV-371: the entity of the critter this create object made (null for an item or a creation that made nothing).</summary>
    public string? Critter { get; init; }

    /// <summary>ARC-DEV-371: the cell the critter was put on, where the location notification of its creation (ledger TG9) runs the tile's script.</summary>
    public CellCoord? Cell { get; init; }
}

/// <summary>What a scripted use did: ran with a teleport of the player (<see cref="Teleport"/>), ran without one, was refused
/// by the dispatcher or by the session, found no script content, or asked for a teleport the caller cannot take (then
/// nothing of the run is applied).</summary>
public enum AreaScriptedUseOutcome { Teleport, NoTeleport, Refused, EffectsRejected, ContentUnavailable, TeleportRefused }

/// <summary>ARC-WORLD-1162 (SWB5): the answer of a bed's use script to the rest hook: <see cref="RunDefault"/> is the answer 1 (the run-default end and an object with no script on the point), which refuses the hero;
/// <see cref="SkipDefault"/> any other answer, which goes on to the seat; <see cref="NotModelled"/> a run the host could not apply (a script it refuses or cannot read, a teleport from a bed), which decides nothing.</summary>
public enum AreaBedAnswer { RunDefault, SkipDefault, NotModelled }

/// <summary>A bed's use run: the answer and the run it came from (its printed lines, dialog and detail).</summary>
public sealed record AreaBedScriptRun(AreaBedAnswer Answer, AreaScriptedUse Use);

/// <summary>A scripted use: the outcome, the first teleport of the player the run asked for, the printed lines the viewer
/// shows, the dispatcher's result and a detail (identifiers and statuses only).</summary>
public sealed record AreaScriptedUse(AreaScriptedUseOutcome Outcome, ScrTeleport? Teleport, IReadOnlyList<string> Lines, ScrRunResult? Run, string Detail)
{
    /// <summary>ARC-WORLD-110: the trap meeting before the use script (null when the object carries no trap or no trap host is set).
    /// When it cancels the use (<see cref="AreaTrapMeeting.Cancels"/>) the script did not run: the outcome is
    /// <see cref="AreaScriptedUseOutcome.NoTeleport"/> with no run.</summary>
    public AreaTrapMeeting? Trap { get; init; }

    /// <summary>ARC-SCRIPT-046: the conversation a tile or sector script asked for (its own or a called script's dialog action, which
    /// the original starts between the PC and the action's attachee, an NPC of the shown map), with the run's other effects applied and
    /// no teleport; the caller opens it. Null for every other run.</summary>
    public ScrDialogStart? Dialog { get; init; }

    /// <summary>ARC-DEV-309 (B21): the lines the run printed (print line, 24) that the original hands to the interface's message window
    /// (ledger IS2, PW1), each with the type the script named (the entry type of the window, ML1–ML2: 4 and 5 are stored, 6 is a transient
    /// text); only for a run that applied (<see cref="AreaScriptedUseOutcome.Teleport"/> or <see cref="AreaScriptedUseOutcome.NoTeleport"/>),
    /// and empty otherwise. <see cref="Lines"/> holds the same texts without the type.</summary>
    public IReadOnlyList<AreaPrintedLine> Printed { get; init; } = [];

    /// <summary>ARC-WORLD-1837 (ledger TT20): the teleports of NPCs the applied run recorded as requests (<see cref="AreaScriptWorld.RequestNpcTeleport"/>), in action order; the run's outcome stays
    /// <see cref="AreaScriptedUseOutcome.NoTeleport"/> (that outcome is the hero's change of place, which no such run asks for).</summary>
    public IReadOnlyList<ScrTeleport> NpcTeleports { get; init; } = [];
}

/// <summary>ARC-DEV-309: one printed line of a run with the entry type of the message window the script named (the print line's second
/// number, ledger IS2, PW1).</summary>
public readonly record struct AreaPrintedLine(int Type, string Text);

/// <summary>
/// ARC-SCRIPT-015 (plan slice S4, viewer part): the AREA viewer's SCR host for a use click on an object whose use script can
/// reach a teleport, and since ARC-SCRIPT-017 for the tile and sector scripts the player enters (<see cref="RunPlaced"/>). The attachee is a T0 object of the shown map (any row of its snapshot, by <see cref="EntityId"/>; an
/// item or holder of another loaded map through its <see cref="AreaInventoryMap"/> rows); its use slot is the row's
/// effective attachment, overridden by the session's map changes <c>use.lf</c>, <c>use.counters</c> and
/// <c>use.script</c>. It answers the S4 guards (<see cref="IScrObjectQueries"/>) from the session: the PC's quest log and the
/// quests' global states, the hero's kit, carried and worn items, the holders' current lists, and from the rows the T0 name and
/// spell flags.
/// The PC's reputations are the session's (ARC-WORLD-044); no other object has one. Everything else the viewer does not model
/// answers "no", as for the dialog and item hosts.
/// </summary>
public sealed class AreaUseScriptHost : IScrHost, IScrWorldObjectQueries, IScrObjectQueries, IScrWorldQueries, IScrObjectStateQueries, IScrAreaQueries, IScrStoreQueries, IScrStatQueries, IScrSpellQueries, IScrStoryQueries, IScrVitalQueries, IScrItemQueries, IScrMagickTechQueries, IScrCritterQueries, IScrCritterStateQueries, IScrCritterFieldQueries, IScrStatAdjustQueries, IScrPortalQueries
{
    /// <summary>Map-change keys of an object's use slot.</summary>
    public const string LocalFlagsProperty = "use.lf", CountersProperty = "use.counters", ScriptProperty = "use.script";
    /// <summary>ARC-SKILLS-010: the catching-thief point (11) whose slot an NPC's run keeps as map changes too, under <c>scr11.*</c>.</summary>
    public const int CatchingThiefPoint = 11;
    /// <summary>ARC-WORLD-114: the map-change key of an NPC a script's toggle state (56) switched off (value 1; absent while it is as its
    /// map data has it). The original flips object flag 0x2; an NPC switched off is neither drawn nor in the object lists, and its AI
    /// passes stop at the gate (<see cref="SwitchedOff"/>).</summary>
    public const string OffProperty = "obj.off";
    /// <summary>ARC-SCRIPT-049: the map-change key of the faction a script's set faction (134) gave an NPC (absent while it has its map
    /// data's; HW4). The original writes the NPC's faction field (0x124), which the save keeps; <see cref="Faction"/> reads it back.</summary>
    public const string FactionProperty = "npc.faction";
    /// <summary>ARC-SCRIPT-052: the map-change key of an NPC a script's destroy (68) took out of the world (value 1). The original sets object
    /// flag 0x1 (TD2); a destroyed NPC is neither drawn nor in the object lists, and the view no longer reaches it (<see cref="Destroyed"/>).</summary>
    public const string DestroyedProperty = GameSessionOwner.ObjectDestroyedProperty;
    /// <summary>The object flag "off" (0x2) of the original.</summary>
    public const int OffObjectFlagBit = 0x2;
    private const int NpcType = 16, DestroyedObjectFlag = 0x1, OffObjectFlag = OffObjectFlagBit, FirstItemType = 5, LastItemType = 14, WrittenItemType = 13;
    private const string ScriptPart = "script", LocalFlagsPart = "lf", CountersPart = "counters";

    /// <summary>The map-change property of a slot's part at a point the host writes back: <c>use.*</c> for the use point,
    /// <c>scr&lt;point&gt;.*</c> for the catching-thief point and (ARC-WORLD-114) the first-heartbeat and heartbeat points, and
    /// (ARC-SCRIPT-047) the dialog point's <c>dialog.*</c>, the keys the dialog host keeps a speaker's dialog slot under
    /// (<see cref="AreaScriptHost"/>), which a heartbeat's call-ex of the dialog point writes.</summary>
    public static string SlotProperty(int point, string part) => point switch
    {
        ScrPoint.Use => "use." + part,
        ScrPoint.Dialog => "dialog." + part,
        _ => $"scr{point}.{part}",
    };
    /// <summary>The spell flag the original reads as mind-controlled (field 20, bit 27; ledger SG3).</summary>
    public const uint MindControlledFlag = 0x0800_0000;

    /// <summary>
    /// The set a use click runs: <see cref="ScrAdmissionSet.S4"/> (with the session's story-state number since ARC-SCRIPT-019)
    /// without the dialog action (a use click does not open a conversation here), plus the reading actions of
    /// <see cref="ScrAdmissionSet.S10"/> (ARC-UI-049: a plaque, book or picture a use, tile or sector script shows), plus call script,
    /// assign object, get object type and the object variables of <see cref="ScrAdmissionSet.S12"/> (ARC-SCRIPT-018), plus the quest,
    /// area, rumor and reputation actions of <see cref="ScrAdmissionSet.S13"/> (ARC-SCRIPT-019), plus the world actions of
    /// <see cref="ScrAdmissionSet.S15"/> (ARC-WORLD-081): random, loop for and loop end over the scenery in the vicinity, create object and a
    /// counter operand with any index. ARC-DEV-157 (bug B23): plus get stat (33) of <see cref="ScrAdmissionSet.S22"/>, answered for the PC only
    /// (<see cref="EffectiveStat"/>). ARC-DEV-205 (bug B22): plus unfog town map (113) of <see cref="ScrAdmissionSet.S24"/>, which reveals the town map
    /// in the session (<see cref="AreaScriptStory"/>); the wreck's sector script 3036 and one use script carry it (the census counts two scripts).
    /// ARC-SCRIPT-1166: plus loop break (20) of <see cref="ScrAdmissionSet.S40"/>, which leaves the run's loop over the vicinity early.
    /// ARC-MAGIC-030 (MT21b, ledger SC1–SC6): plus the spell conditions body spell (14), influenced by spell (35), knows spell (48) and mastered spell college (49) of
    /// <see cref="ScrAdmissionSet.S31"/>, which this host answers (<see cref="IScrSpellQueries"/>), and give spell mastery in college (112), which it writes through the session.
    /// ARC-SCRIPT-1241: plus fade (103) of <see cref="ScrAdmissionSet.S35"/>, which jumps the game clock and hands its movie to the viewer (<see cref="TakeFades"/>); the use script 2862 of the Vendigroth
    /// underground and the dying scripts 2863 and 3061 of Kerghan carry it, and the placed and dying sets are built on this one.
    /// ARC-SCRIPT-1620 (TI6): plus attack (36, 0x447D2F), which raises the attack event of the first object of operand 1 on each object of operand 0 through the AI host (<see cref="AreaScriptWorld.ScriptAttack"/>); 79 installed scripts use it (111 clauses), and the heartbeat set admits it too.
    /// ARC-SCRIPT-1764 (NP9–NP12): plus queue newspaper (85) and float newspaper headline (86) of <see cref="ScrAdmissionSet.S59"/> (<see cref="AreaScriptNewspaper"/>; the headline floats through <see cref="TakeFloats"/>), which the placed, dying and combat-point sets and the heartbeat set admit too.
    /// ARC-SCRIPT-1772 (CQ1–CQ8): plus the critter getters get social class (38), get origin (39) and get faction (89), toggle invulnerability (57) and rename (92) of <see cref="ScrAdmissionSet.S56"/> (<see cref="CritterFieldWriteActions"/>), which the placed, heartbeat, dying and combat-point sets admit too.
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S4-area-use-reading-objects-story-world", ScrAdmissionSet.S4.Conditions.Concat(ScrAdmissionSet.SpellQueryConditions).Concat(ScrAdmissionSet.StoryConditions).Concat(ScrAdmissionSet.WorldObjectConditions)
            .Concat(ScrAdmissionSet.CritterQueryConditions).Concat(ScrAdmissionSet.CritterStateConditions).Concat(ScrAdmissionSet.CritterTailConditions),
        ScrAdmissionSet.S4.Actions.Where(a => a is not ScrCatalogue.Action.Dialog)
            .Concat([ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque,
                ScrCatalogue.Action.CallScript, ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.GetObjectType,
                ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor,
                ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation,
                ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.LoopBreak, ScrCatalogue.Action.CreateObject, ScrCatalogue.Action.GetStat,
                ScrCatalogue.Action.UnfogTownMap, ScrCatalogue.Action.GiveSpellMastery, ScrCatalogue.Action.Attack, .. CritterFieldWriteActions, .. ItemUseVitalActions, .. EyeCandyActions, .. ScrAdmissionSet.StoryActions, ScrCatalogue.Action.Fade, ScrCatalogue.Action.EndGame, ScrCatalogue.Action.QueueNewspaper, ScrCatalogue.Action.FloatNewspaperHeadline, .. ScrAdmissionSet.NpcStateHostActions, .. ScrAdmissionSet.WorldObjectHostActions]),
        ScrAdmissionSet.S4.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds),
        ScrAdmissionSet.S4.FocusKinds.Concat([ScrFocusKind.LocalObject, ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EverySceneryInVicinity, ScrFocusKind.AnySceneryInVicinity, ScrFocusKind.EveryPortalInVicinity]),
        counterIndexWraps: true);

    /// <summary>ARC-SCRIPT-1481 (VT1–VT6): what the food use script 30282 needs besides the use set's own: get hit points (107), get fatigue points (108), heal poison (128), heal hit points (75), heal fatigue (76), the magick-tech adjustment (91; the item's power
    /// ratio, <see cref="PowerRatio"/>), play script eye candy (120; the use host shows none) and destroy (68; of the item being used only, <see cref="ObjectStateRefusal"/>).</summary>
    private static int[] ItemUseVitalActions => [ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints, ScrCatalogue.Action.HealPoison, ScrCatalogue.Action.HealHitPoints,
        ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy, ScrCatalogue.Action.Destroy];

    /// <summary>ARC-SCRIPT-1772 (CQ1–CQ5): the critter getters (38, 39, 89; <see cref="ScrAdmissionSet.CritterFieldActions"/>), toggle invulnerability (57) and rename (92) of <see cref="ScrAdmissionSet.S56"/>, which the use set and so the placed, combat-point, dying and
    /// heartbeat sets admit: the getters are answered here (<see cref="IScrCritterFieldQueries"/>), the two writes go through the session for the PC and an NPC (<see cref="ObjectStateRefusal"/> refuses the rest).</summary>
    private static int[] CritterFieldWriteActions => [.. ScrAdmissionSet.CritterFieldActions, ScrCatalogue.Action.ToggleInvulnerability, ScrCatalogue.Action.Rename];
    /// <summary>ARC-SCRIPT-1734 (WE11, WE12): play spell eye candy (104) and stop script eye candy (124) of <see cref="ScrAdmissionSet.S55"/>, which the use set admits beside play script eye candy (120): the run's result hands them on as eye candy requests; the viewer shows the stop since ARC-SCRIPT-1828 (the spell play, 104, is not shown yet).</summary>
    private static int[] EyeCandyActions => [ScrCatalogue.Action.PlaySpellEyeCandy, ScrCatalogue.Action.StopScriptEyeCandy];

    /// <summary>ARC-DEV-243 (S14g): damage (60), apply unresistable damage (138) and heal hit points (75), which the placed and heartbeat sets admit.</summary>
    private static readonly int[] DamageActions = [ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.HealHitPoints];

    /// <summary>ARC-SCRIPT-1294 (RM6): stop spell on object (130) of <see cref="ScrAdmissionSet.S42"/>, which the placed and heartbeat sets admit beside the cast actions.</summary>
    private static readonly int[] StopSpellActions = [ScrCatalogue.Action.StopSpellOnObject];

    /// <summary>ARC-DEV-241 (S14f): the six cast actions (48, 61, 118, 121, 122, 136) the placed and heartbeat sets admit.</summary>
    private static readonly int[] CastActions = [ScrCatalogue.Action.CastSpell, ScrCatalogue.Action.CastSpellOn, ScrCatalogue.Action.CastFreeSpell, ScrCatalogue.Action.CastUnresistableSpellBy,
        ScrCatalogue.Action.CastFreeUnresistableSpell, ScrCatalogue.Action.CastUnresistableSpellOn];

    /// <summary>
    /// ARC-SCRIPT-046: the set a tile or sector script runs (<see cref="RunPlaced"/>): <see cref="Admission"/> with the dialog action, which
    /// such a run, or a script it calls, uses to start a conversation of the PC with an NPC (<see cref="AreaScriptedUse.Dialog"/>).
    /// ARC-SCRIPT-052 (audit slice 8, the first group of the tile scripts' refusals): plus call-ex (81), by which a tile an NPC steps on runs
    /// his own dialog point (Virgil's talk at the altar, 1328), destroy (68) and stop attacking (109) of <see cref="ScrAdmissionSet.S20"/>,
    /// which the story tiles apply to the NPC that steps on them. ARC-DEV-157 (bug B20): plus the focus kinds everyone (8) and anyone (9) in
    /// the vicinity of <see cref="ScrAdmissionSet.S17"/> in actions (the loop over the critters around the tile, 443 refused tile rows in the
    /// audit), which <see cref="CrittersInVicinity"/> answers. ARC-DEV-298 (bug B20): plus float line (23), which the ghost of Brego's tile script (2768 calling 2789) uses to float the
    /// ghost's lines over him (<see cref="TakeFloats"/>). ARC-SCRIPT-1294: plus stop spell on object (130) of <see cref="ScrAdmissionSet.S42"/>.
    /// </summary>
    public static ScrAdmissionSet PlacedAdmission { get; } = new("S4-area-placed-dialog-actor", Admission.Conditions,
        Admission.Actions.Concat([ScrCatalogue.Action.Dialog, ScrCatalogue.Action.CallScriptEx, ScrCatalogue.Action.Destroy, ScrCatalogue.Action.StopAttacking,
            ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo, ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.PerformAnimation, ScrCatalogue.Action.StealCoins, ScrCatalogue.Action.BecomeProne, ScrCatalogue.Action.FloatLine, .. CastActions, .. StopSpellActions, .. DamageActions]),
        Admission.ValueKinds, Admission.FocusKinds.Concat([ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity]), counterIndexWraps: true);

    /// <summary>
    /// ARC-WORLD-114: the set an NPC's first-heartbeat (10) and heartbeat (19) scripts run from the AI tick (<see cref="RunHeartbeat"/>):
    /// <see cref="ScrAdmissionSet.S2"/> (control flow, local and global flags, counters, arithmetic, story state, remove and change this
    /// script) with the single-object focus kinds of <see cref="ScrAdmissionSet.S3"/>, plus toggle state (56) and kill (58) of
    /// <see cref="ScrAdmissionSet.S16"/>. ARC-SCRIPT-047: plus what a heartbeat needs to open an NPC's talk with the PC once the PC is near
    /// (1100, ledger TG3–TG6): loop for and loop end over everyone or anyone in the vicinity (<see cref="ScrAdmissionSet.S17"/>) with the
    /// current looped object, get object type, call-ex (of the NPC's dialog point among others) and the dialog action, which in a call-ex
    /// of the dialog point asks for the talk (<see cref="AreaScriptedUse.Dialog"/>; a heartbeat's own dialog action has the NPC as its
    /// triggerer and starts nothing). A heartbeat runs every few seconds for every NPC near the PC, so the set stays this narrow; a
    /// script outside it is refused and the tick goes on as without a script. ARC-SCRIPT-049 (audit slice 1): plus the world questions the
    /// installed heartbeats ask first: the conditions following (18), is named (20), is dead (22), is switched off (27) and has a reputation
    /// (33), and the actions call script in (54, through the session's queue), distance between (71) and set faction (134) of
    /// <see cref="ScrAdmissionSet.S18"/>. ARC-MAGIC-030 (MT21b): plus the spell conditions (14, 35, 48, 49) and give spell mastery in college (112) of <see cref="ScrAdmissionSet.S31"/>.
    /// ARC-SCRIPT-1359: plus get location (95) of <see cref="ScrAdmissionSet.S38"/> (the Crash Site Molochean agent's heartbeat 1717 reads where it stands before it runs to its tile).
    /// ARC-SCRIPT-1166: plus loop break (20) of <see cref="ScrAdmissionSet.S40"/>. ARC-SCRIPT-1294: plus stop spell on object (130) of <see cref="ScrAdmissionSet.S42"/>.
    /// ARC-SCRIPT-1734: plus the spell-state conditions (12, 13, 16) and the eye candy actions (104, 124) of <see cref="ScrAdmissionSet.S55"/>; 21 main-map heartbeats stop a script eye candy.
    /// ARC-SCRIPT-1815: plus the focus kinds every scenery in the vicinity (15) of <see cref="ScrAdmissionSet.S15"/> and the run's own object variable (11, which assign object writes), assign object (15) of <see cref="ScrAdmissionSet.S11"/> and float line (23) of <see cref="ScrAdmissionSet.S19"/>. The main map's script 2231
    /// (21 heartbeats of point 19) loops over the scenery around its NPC and stops a script eye candy on each; the scenery focus was the one thing the set lacked for it (the host answers it as for a use click,
    /// <see cref="SceneryInVicinity"/>). The original's script executor (0x447220) has no per-point restriction, so a command the host can answer runs the same in a heartbeat; assign object only writes the run's own roles
    /// and variables, and float line reaches the viewer through the same <see cref="TakeFloats"/> as for a tile script. Get reaction (29) stays out: this host has no reaction answer (<see cref="IScrReactionQueries"/>).
    /// ARC-SCRIPT-1842 (#1322): plus make follow (21) of <see cref="ScrAdmissionSet.S33"/>, which the heartbeat and dying scripts use to make an NPC follow another NPC (<see cref="MakeFollowRefusal"/>).
    /// </summary>
    public static ScrAdmissionSet HeartbeatAdmission { get; } = new("S2-area-heartbeat-state-kill-talk-world",
        ScrAdmissionSet.S2.Conditions.Concat([ScrCatalogue.Condition.FollowingPc, ScrCatalogue.Condition.IsNamed, ScrCatalogue.Condition.IsDead,
            ScrCatalogue.Condition.IsSwitchedOff, ScrCatalogue.Condition.HasReputation, ScrCatalogue.Condition.PcQuestState, .. ScrAdmissionSet.SpellQueryConditions, .. ScrAdmissionSet.StoryConditions, .. ScrAdmissionSet.WorldObjectConditions, .. ScrAdmissionSet.CritterQueryConditions, .. ScrAdmissionSet.CritterStateConditions, .. ScrAdmissionSet.CritterTailConditions]),
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.LoopBreak,
            ScrCatalogue.Action.GetObjectType, ScrCatalogue.Action.CallScriptEx, ScrCatalogue.Action.Dialog, ScrCatalogue.Action.CallScriptIn,
            ScrCatalogue.Action.DistanceBetween, ScrCatalogue.Action.GetLocation, ScrCatalogue.Action.SetFaction, ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo,
            ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.PerformAnimation, ScrCatalogue.Action.StealCoins, ScrCatalogue.Action.BecomeProne, ScrCatalogue.Action.GiveSpellMastery, ScrCatalogue.Action.Attack, ScrCatalogue.Action.MakeFollow, ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.QueueNewspaper, ScrCatalogue.Action.FloatNewspaperHeadline, ScrCatalogue.Action.Teleport, ScrCatalogue.Action.FadeAndTeleport, .. CritterFieldWriteActions, .. ScrAdmissionSet.StoryActions, .. ScrAdmissionSet.NpcStateHostActions, .. ScrAdmissionSet.WorldObjectHostActions, .. CastActions, .. StopSpellActions, .. DamageActions, .. EyeCandyActions]),
        ScrAdmissionSet.S2.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds),
        ScrAdmissionSet.S3.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity, ScrFocusKind.EverySceneryInVicinity, ScrFocusKind.AnySceneryInVicinity, ScrFocusKind.LocalObject]));

    /// <summary>ARC-WORLD-114: whether a script switched the NPC <paramref name="entity"/> of map <paramref name="map"/> off
    /// (<see cref="OffProperty"/>).</summary>
    public static bool SwitchedOff(GameSessionOwner session, int map, string entity) =>
        (session ?? throw new ArgumentNullException(nameof(session))).QueryMapChange(new SessionMapId(map), MapChangeKey.ForEntity(entity, OffProperty)) == 1;

    /// <summary>
    /// ARC-SCRIPT-1360: whether an NPC of the shown map is switched off now. Toggle state (56) flips the object flag "off" (0x2) of the original, so an NPC the map data
    /// stores off (the quest NPCs a heartbeat switches on once the story reaches them) is off until a toggle clears the bit: the session's object-flag change over the data's
    /// (<see cref="GameSessionOwner.WriteObjectFlag"/>), or a script's toggle of an NPC the data stores on (<see cref="OffProperty"/>).
    /// </summary>
    public static bool NpcOff(GameSessionOwner session, int map, string entity, long dataFlags) =>
        (session ?? throw new ArgumentNullException(nameof(session))).QueryObjectFlags(entity).Object(unchecked((int)dataFlags)) is var flags && (flags & OffObjectFlag) != 0 || SwitchedOff(session, map, entity);

    /// <summary>ARC-SCRIPT-052: whether a script destroyed the NPC <paramref name="entity"/> of map <paramref name="map"/>
    /// (<see cref="DestroyedProperty"/>).</summary>
    public static bool Destroyed(GameSessionOwner session, int map, string entity) =>
        (session ?? throw new ArgumentNullException(nameof(session))).QueryMapChange(new SessionMapId(map), MapChangeKey.ForEntity(entity, DestroyedProperty)) == 1;

    /// <summary>ARC-SCRIPT-049: the faction of the NPC <paramref name="row"/> now: the one a script's set faction gave it
    /// (<see cref="FactionProperty"/>), else <paramref name="loaded"/>, its map data's.</summary>
    public static int Faction(GameSessionOwner session, AreaObjectRecord row, int loaded)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(row);
        return session.QueryMapChange(new SessionMapId(row.Key.RuntimeMap), MapChangeKey.ForEntity(EntityId(row), FactionProperty)) is { } set
            ? unchecked((int)set) : loaded;
    }

    /// <summary>ARC-SCRIPT-1163 (NS3): the standpoint of the NPC <paramref name="entity"/> now: the location a script's set day or night standpoint wrote (<see cref="SessionNpcStandpoint"/>), else null (the map data's stands).</summary>
    public static AreaRouteLocation? Standpoint(GameSessionOwner session, int map, string entity, bool day) =>
        (session ?? throw new ArgumentNullException(nameof(session))).QueryObjectChange(new SessionMapId(map), SessionNpcStandpoint.Key(entity, day)) is { } set ? AreaRouteLocation.FromNumber(set) : null;

    /// <summary>ARC-WORLD-081: the label of a script's random draws on the session's generator.</summary>
    public const string RandomLabel = "scr.random";

    private readonly List<ScrReadingRequest> _readings = [];
    private readonly List<AreaScriptFloat> _floats = [];
    private readonly List<AreaScriptFade> _fades = [];
    private int _fadeClears;
    private readonly List<AreaScriptEyeCandy> _eyeCandies = [];

    /// <summary>ARC-SCRIPT-1592: the script eye candy (action 120) of the runs applied since the last call, in order, for the viewer to play on its objects (<see cref="AreaEyeCandyHost.PlayScript"/>); the talk host's
    /// <see cref="AreaScriptHost.TakeEyeCandies"/> holds those of a dialog's runs.</summary>
    public IReadOnlyList<AreaScriptEyeCandy> TakeEyeCandies()
    {
        var eyeCandies = _eyeCandies.ToArray();
        _eyeCandies.Clear();
        return eyeCandies;
    }

    private bool _endGame;
    private AreaScriptNewspaper.TextsCache? _newspaperTextsField;
    private AreaScriptNewspaper.TextsCache _newspaperTexts => _newspaperTextsField ??= new(() => _content);

    /// <summary>ARC-RENDER-1171 (SS4): whether a run applied since the last call asked for the end of the game (132); see <see cref="AreaScriptHost.TakeEndGame"/>.</summary>
    public bool TakeEndGame()
    {
        var asked = _endGame;
        _endGame = false;
        return asked;
    }

    /// <summary>ARC-SCRIPT-1241: the fades (103) of the runs applied since the last call that ask for a movie, in order, for the viewer to play (their clock jumps are done; a sound-only fade plays
    /// nothing here, as no effect sound does for a use click).</summary>
    public IReadOnlyList<AreaScriptFade> TakeFades()
    {
        var fades = _fades.ToArray();
        _fades.Clear();
        return fades;
    }

    /// <summary>ARC-UI-2034 (ledger TB16): how many fade actions (103) the runs applied since the last call, whether or not they ask for a movie; each empties the speech bubble table first (see <see cref="AreaScriptHost.TakeFadeClears"/>).</summary>
    public int TakeFadeClears()
    {
        var count = _fadeClears;
        _fadeClears = 0;
        return count;
    }

    /// <summary>ARC-DEV-298: the float lines (23) of the runs applied since the last call, in order, each with its text, for the viewer to float over its speaker
    /// (the talk host's <see cref="AreaScriptHost.TakeFloats"/> for a tile script).</summary>
    public IReadOnlyList<AreaScriptFloat> TakeFloats()
    {
        var floats = _floats.ToArray();
        _floats.Clear();
        return floats;
    }

    /// <summary>ARC-UI-049: the reading actions of the runs applied since the last call whose viewer is the PC, in order.</summary>
    public IReadOnlyList<ScrReadingRequest> TakeReadings()
    {
        var readings = _readings.ToArray();
        _readings.Clear();
        return readings;
    }

    /// <summary>
    /// The session entity of a row: a mobile's own id (<see cref="AreaConversationState.EntityId"/>, as the inventory and
    /// dialog hosts name it); a sector object's sector and window ordinal, because sector objects carry a null id and so
    /// share one id digest (the transition reader and the area owner key them the same way).
    /// </summary>
    public static string EntityId(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // A static row without a sector is not a use site here (the transfers match statics by sector and ordinal).
        return row.Key.Kind == AreaSourceKind.Static && row.Key.Sector is { } sector ? $"sector:{sector}:{row.Key.SourceIndex}" : AreaConversationState.EntityId(row);
    }

    private readonly GameSessionOwner _session;
    private readonly IReadOnlyCollection<AreaInventoryMap> _maps;
    private readonly ScriptContent? _content;
    private readonly int _shownMap;
    private readonly List<SessionExperienceAward> _awards = [];

    /// <summary>ARC-SCRIPT-019: the experience awards the runs' completed quests gave since the last call, in order; the viewer passes
    /// their follower steps and level-up messages on.</summary>
    public IReadOnlyList<SessionExperienceAward> TakeAwards()
    {
        var awards = _awards.ToArray();
        _awards.Clear();
        return awards;
    }
    private readonly Dictionary<string, AreaObjectRecord> _shown = new(StringComparer.Ordinal);
    // ARC-WORLD-081: the shown map's scenery in snapshot order, for the vicinity; ARC-SCRIPT-047: its NPCs likewise.
    // ARC-SCRIPT-2041: its portals likewise (focus kind 19).
    private readonly List<(string Entity, AreaObjectRecord Row)> _scenery = [], _npcs = [], _portals = [];
    private readonly AreaScriptWorld? _world;
    private readonly List<AreaScriptCreation> _created = [];
    private (string Entity, AreaObjectRecord Row)? _clicked;

    /// <summary>ARC-WORLD-081: the create objects of the runs applied since the last call, in order.</summary>
    public IReadOnlyList<AreaScriptCreation> TakeCreations()
    {
        var created = _created.ToArray();
        _created.Clear();
        return created;
    }

    /// <param name="shownMap">The shown map's runtime number.</param>
    /// <param name="shown">The shown map's object rows (its snapshot's): any of them can be the attachee or a guard's object.</param>
    /// <param name="maps">The loaded maps' inventory rows (items and holders), for objects of other maps.</param>
    /// <param name="content">The game's scripts; null when they could not be loaded. The host borrows it.</param>
    /// <param name="world">ARC-WORLD-081: the viewer's answers for the world actions; without it the vicinity is empty, a create object is
    /// not applied (the run's effects are refused) and random still draws.</param>
    public AreaUseScriptHost(GameSessionOwner session, int shownMap, IEnumerable<AreaObjectRecord> shown, IReadOnlyCollection<AreaInventoryMap> maps,
        ScriptContent? content, AreaScriptWorld? world = null)
    {
        _world = world;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(shown);
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _content = content;
        _shownMap = shownMap;
        // A mobile id repeated by several rows keeps the first; the clicked row answers for its own (Run).
        foreach (var row in shown)
        {
            _shown.TryAdd(EntityId(row), row);
            if (row.Type == SceneryType && row.WorldX is not null && row.WorldY is not null) _scenery.Add((EntityId(row), row));
            if (row.Type == PortalObjectType && row.WorldX is not null && row.WorldY is not null) _portals.Add((EntityId(row), row));
            if (row.Type == NpcType && row.WorldX is not null && row.WorldY is not null && _shown[EntityId(row)] == row) _npcs.Add((EntityId(row), row));
        }
    }

    /// <summary>
    /// ARC-SCRIPT-1165 (E5-c): the effect rule of a blessing or curse number (the module's blessing and curse table's effect entry, then the effect table's rule), which a script's add blessing (25) or add curse (27)
    /// needs; the viewer sets it once the tables are read. Without it such an action is refused (<see cref="AreaScriptStory.Apply"/>).
    /// </summary>
    public Func<SessionBlessingKind, int, SessionEffectDefinition?>? BlessingEffects { get; set; }

    // SQ1–SQ12 (ARC-SCRIPT-1165): the story and time questions, from the session.
    public ScrGameTime? Time() => AreaScriptStoryQueries.Time(_session);
    public int GlobalQuestState(int quest) => AreaScriptStoryQueries.GlobalQuestState(_session, quest);
    public bool? RumorKnown(ScrObjectRef obj, int rumor) => AreaScriptStoryQueries.RumorKnown(_session, IsPc(obj), rumor);
    public bool RumorQuelled(int rumor) => AreaScriptStoryQueries.RumorQuelled(_session, rumor);
    public bool? HasBlessing(ScrObjectRef obj, int number, bool curse) => AreaScriptStoryQueries.HasBlessing(_session, IsPc(obj), number, curse);

    private const int SceneryType = 3;

    /// <summary>
    /// ARC-WORLD-081 (PG3): the shown map's scenery within <see cref="ScrWorld.VicinityReach"/> tiles of the centre on both axes, not
    /// destroyed or switched off, in snapshot order; the PC's place is its live cell. Empty for a centre of another map or without a place.
    /// </summary>
    public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre)
    {
        if (centre.Map != _shownMap || Place(centre) is not { } at) return [];
        return _scenery.Where(s => Math.Abs(s.Row.WorldX!.Value - at.X) <= ScrWorld.VicinityReach && Math.Abs(s.Row.WorldY!.Value - at.Y) <= ScrWorld.VicinityReach
                && _world?.Gone(s.Row) != true)
            .Select(s => new ScrObjectRef(_shownMap, s.Entity)).ToArray();
    }

    /// <summary>
    /// ARC-SCRIPT-2041 (PV1–PV3): the shown map's portals within <see cref="ScrWorld.VicinityReach"/> tiles of the centre on both axes, not destroyed or switched off, open, closed or locked alike, in snapshot order
    /// (the scenery's rule with the portal type, ledger PV1–PV3). Empty for a centre of another map or without a place.
    /// </summary>
    public IReadOnlyList<ScrObjectRef> PortalsInVicinity(ScrObjectRef centre)
    {
        if (centre.Map != _shownMap || Place(centre) is not { } at) return [];
        return _portals.Where(s => Math.Abs(s.Row.WorldX!.Value - at.X) <= ScrWorld.VicinityReach && Math.Abs(s.Row.WorldY!.Value - at.Y) <= ScrWorld.VicinityReach
                && _world?.Gone(s.Row) != true)
            .Select(s => new ScrObjectRef(_shownMap, s.Entity)).ToArray();
    }

    /// <summary>
    /// ARC-SCRIPT-047 (TG4): the shown map's PCs and NPCs within <see cref="ScrWorld.VicinityReach"/> tiles of the centre on both axes, not
    /// destroyed or switched off, the centre itself and the dead included: the NPC rows in snapshot order at their live cells
    /// (<see cref="AreaScriptWorld.CritterCell"/>, else the row's), then the PC at its live cell (the original's list order is not read).
    /// Empty for a centre of another map or without a place.
    /// </summary>
    [OriginalFunction(0x440FF0, OriginalFunctionScope.Partial)]
    public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre)
    {
        if (centre.Map != _shownMap || Place(centre) is not { } at) return [];
        static bool Near(CellCoord cell, CellCoord at) => Math.Abs((long)cell.X - at.X) <= ScrWorld.VicinityReach && Math.Abs((long)cell.Y - at.Y) <= ScrWorld.VicinityReach;
        // Off in the play mask (0x5E2F88 = 3): destroyed or switched off in the map data (object flags 0x1, 0x2) or gone in the session.
        var list = _npcs.Where(n => ((Number(n.Row, ObjectFlagsSlot) ?? 0) & DestroyedObjectFlag) == 0 && !NpcOff(_session, _shownMap, n.Entity, Number(n.Row, ObjectFlagsSlot) ?? 0) && _world?.Gone(n.Row) != true && Place(new ScrObjectRef(_shownMap, n.Entity)) is { } cell && Near(cell, at))
            .Select(n => new ScrObjectRef(_shownMap, n.Entity)).ToList();
        if (Place(Pc) is { } pc && Near(pc, at)) list.Add(Pc);
        return list;
    }

    /// <summary>
    /// ARC-SCRIPT-050 (GA2, GA3): what get area stores for any object of a run on the shown map, the current map of the original: the shown
    /// map's map-list area when it is not 0, else 0 unless the shown map is the start map, where the original places the object by its
    /// location (the nearest area whose town map is the object's sector's, 0x4CB630; ARC-DEV-178, <see cref="AreaScriptWorld.StartMapAreaAt"/>).
    /// Null (the dispatch ends as not modelled) when the world gives no map-list row (outside map mode), and on the start map when the object has
    /// no place here or the world cannot answer for its sector.
    /// </summary>
    public int? AreaOf(ScrObjectRef obj) => _world?.MapArea?.Invoke() is not { } map ? null
        : map.Area != 0 ? map.Area
        : !map.StartMap ? 0
        : Place(obj) is { } cell ? _world.StartMapAreaAt?.Invoke(cell) : null;

    /// <summary>ARC-DEV-157 (GS1, GS3): the PC's stat by the original's number from the session hero (<see cref="GameSessionOwner.QueryHeroScriptStat"/>);
    /// null (the dispatch ends as not modelled) for an NPC or any other object, whose stats this host does not read, and for a stat the session does
    /// not hold.</summary>
    public int? EffectiveStat(ScrObjectRef obj, int stat) =>
        stat == PoisonStat ? Vitals(obj)?.Poison is { } poison ? Math.Clamp(poison, 0, Arcanum.Simulation.Combat.LifeLimits.MaxPoisonLevel) : null
        : IsPc(obj) ? _session.QueryHeroScriptStat(stat) : null;

    /// <summary>ARC-SCRIPT-1481 (VT5): the stat number of the poison level (24). The getter (0x4B0490) adds the object's effects to the base level and clamps to 0..1000 (the range tables 0x5B5194 and 0x5B5204); no installed effect changes it (ledger PZ6).</summary>
    public const int PoisonStat = 24;

    // ARC-SCRIPT-1481: the vitals of the PC (the session's life) or of an NPC of the shown map (the world's answer); null for any other object.
    private AreaScriptVitals? Vitals(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHeroLife() is { } life ? new(life.Status.HitPoints, life.Status.MaxHitPoints, life.Status.Fatigue, life.Status.MaxFatigue, life.Record.State.PoisonLevel) : null;
        return obj.Map == _shownMap && Row(obj) is { Type: NpcType } ? _world?.Vitals?.Invoke(obj.Code) : null;
    }

    /// <summary>ARC-SCRIPT-1481 (VT1): get hit points, the current and the maximum of the PC or of an NPC of the shown map; null for another object (not modelled).</summary>
    public (int Current, int Maximum)? HitPoints(ScrObjectRef obj) => Vitals(obj) is { } v ? (v.HitPoints, v.MaxHitPoints) : null;

    /// <summary>ARC-SCRIPT-1481 (VT2): get fatigue points, as <see cref="HitPoints"/>.</summary>
    public (int Current, int Maximum)? FatiguePoints(ScrObjectRef obj) => Vitals(obj) is { } v ? (v.Fatigue, v.MaxFatigue) : null;

    /// <summary>ARC-SCRIPT-1481 (VT4, ledger WE2, EQ9): the item's power ratio for the PC or NPC that holds it, <see cref="Arcanum.Simulation.Equipment.EquipmentRules.EffectivePowerRatio"/> of its complexity and the owner's aptitude (stat 16, clamped to the
    /// aptitude range); 100 for an owner the host holds a row for that is neither (ARC-SCRIPT-1662); null for an owner it has no row for, an NPC whose aptitude is not known, or an item the host does not know. ARC-SCRIPT-1562: a hero's worn item is read as the session has it (<see cref="GameSessionOwner.QueryWornPowerRatio"/>, the helper of the deferred item calls; a made item has no row), the
    /// rest of the items by their row; an NPC of the shown map has its aptitude from the world (<see cref="AreaScriptWorld.Aptitude"/>).</summary>
    public int? PowerRatio(ScrObjectRef item, ScrObjectRef owner)
    {
        if (IsPc(owner) && _session.QueryWornPowerRatio(item.Code) is { } worn) return worn;
        if (ComplexityOf(item) is not { } complexity || CritterAptitude(owner) is not { } holder) return null;
        // ARC-SCRIPT-1662 (WE2, CP8; exe 0x4614A0, 0x461540): an owner that is neither the PC nor an NPC (a container, scenery, an item) has its complexity as the item's power, so the ratio is 100 whatever the complexity (0 reads 100 as well).
        var limits = Arcanum.Simulation.Equipment.EquipmentLimits.MaxMagnitude;
        if (complexity < -limits || complexity > limits) return null;
        if (!holder.Critter) return 100;
        if (holder.Aptitude is not { } aptitude) return null;
        return Arcanum.Simulation.Equipment.EquipmentRules.EffectivePowerRatio(complexity, Math.Clamp(aptitude, Arcanum.Simulation.Equipment.EquipmentLimits.MinAptitude, Arcanum.Simulation.Equipment.EquipmentLimits.MaxAptitude));
    }

    /// <summary>
    /// ARC-SCRIPT-1562 (CP7, exe read 2026-11-13: action 80 at 0x44933F, 0x461620; <see cref="Arcanum.Simulation.Combat.AttackRules.MagickCancelChance"/>): the chance in percent that the target's aptitude cancels the item for the attacker: 0 unless both
    /// are a PC or an NPC (the original's type test of 0xF and 0x10); the aptitudes are the PC's of the session and an NPC's of the world. Null for an item without a row here, an NPC whose aptitude the world does not give, or an object the host has no row for.
    /// </summary>
    public int? MagickCancelPercent(ScrObjectRef item, ScrObjectRef attacker, ScrObjectRef target)
    {
        // The original's order: the attacker's type, then the target's (neither a critter: no chance, whatever the item); only then the aptitudes. A critter's aptitude is the attack path's, clamped to the aptitude range.
        if (CritterAptitude(attacker) is not { } from) return null;
        if (!from.Critter) return 0;
        if (CritterAptitude(target) is not { } to) return null;
        if (!to.Critter) return 0;
        if (ComplexityOf(item) is not { } complexity || from.Aptitude is not { } a || to.Aptitude is not { } b) return null;
        var (min, max) = (Arcanum.Simulation.Equipment.EquipmentLimits.MinAptitude, Arcanum.Simulation.Equipment.EquipmentLimits.MaxAptitude);
        return Arcanum.Simulation.Combat.AttackRules.MagickCancelChance(complexity, Math.Clamp(a, min, max), Math.Clamp(b, min, max));
    }

    private const uint AnimalCritterFlag = 0x8;

    /// <summary>
    /// ARC-SCRIPT-1562 (CP9, exe read 2026-11-13: condition 37 at 0x4451B0 case 0x25): whether the object is an NPC with the animal critter flag (0xDA bit 0x8) as the session has it (<see cref="GameSessionOwner.QueryObjectFlags"/> over the row's stored flags); false for the PC and
    /// any other object type; null for an object this host has no row for or one whose fields do not read.
    /// </summary>
    public bool? IsAnimal(ScrObjectRef obj) => NpcCritterBit(obj, AnimalCritterFlag);

    // ARC-SCRIPT-1733: whether a critter-flag bit (field 0xDA) is set on an NPC as the session has the flags (the stored flags of the row under the written ones); false for the PC and any other object type (the original tests the object type 0x10 first); null for an object this
    // host has no row for or one whose fields do not read.
    private bool? NpcCritterBit(ScrObjectRef obj, uint bit)
    {
        if (IsPc(obj)) return false;
        if (Row(obj) is not { } row) return null;
        if (row.Type != NpcType) return false;
        return Arcanum.Data.Combat.CombatFieldReader.Read(row).Fields?.Critter is { } critter
            ? (unchecked((uint)_session.QueryObjectFlags(obj.Code).Critter(unchecked((int)critter.Flags))) & bit) != 0 : null;
    }

    /// <summary>
    /// ARC-SCRIPT-1661 (CP11): whether the object is a PC or an NPC with the combat-mode bit (critter flags 0xDA bit 0x40000) as the session has it (<see cref="GameSessionOwner.QueryCombatMode"/>: the hero's, or the NPC's by entity); false for any other object type; null for an object
    /// this host has no row for.
    /// </summary>
    /// <remarks>Exe read 2026-11-13: condition 31 at 0x4451B0 case 0x1f, the test 0x4B3D50.</remarks>
    public bool? IsInCombat(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHero() is not null && _session.QueryCombatMode(SessionCombatant.Hero);
        if (Row(obj) is not { } row) return null;
        return row.Type == NpcType && _session.QueryCombatMode(SessionCombatant.Critter(obj.Code));
    }

    /// <summary>
    /// ARC-SCRIPT-1661 (CP12): this host adjusts the hero's alignment (stat 19) only, which the session stores as a base stat; an NPC's alignment, which the world keeps in its combat values and the session does not write, and every other stat stop the dispatch as not modelled.
    /// </summary>
    /// <remarks>Exe read 2026-11-13: action 137 at 0x447220 case 0x89, base stat 0x4B0740, stat setter 0x4B0980 with the range tables 0x5B5194 and 0x5B5204.</remarks>
    public bool CanAdjustStat(ScrObjectRef obj, int stat) => stat == AreaScriptStory.AlignmentStat && IsPc(obj) && _session.QueryHero() is not null;

    private const uint ProwlingCritterFlag = 0x1, UndeadCritterFlag = 0x4, InvulnerableObjectFlag = 0x400000, InvisibleSpellFlag = 0x1,
        SummonedSpellFlag = 0x4000, NoStrayNpcFlag = 0x8000, AssociateBlockingObjectFlags = 0x102;

    // Object field 0x18 (obj_f_aid, slot 23): the art id the object was built with, which a polymorph does not change (the current art is field 1).
    private const int BaseArtSlot = 23;

    /// <summary>ARC-SCRIPT-1733 (CS1, exe read 2026-11-13: condition 10 at 0x4451B0, 0x4C0C40): met before, of an NPC toward the PC: the same object twice is true; a second object that is not the PC or a first that is not an NPC is false; else whether the NPC's reaction memory holds the PC
    /// in a remembered slot (<see cref="SessionReaction.MetBefore"/>), which the conversation slot is not, so it is false during the first talk. Null for an NPC this host has no row for.</summary>
    public bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc)
    {
        if (npc == pc) return true;
        if (!IsPc(pc) || IsPc(npc)) return false;
        if (Row(npc) is not { } row) return null;
        return row.Type == NpcType && _session.QueryReactionRecord(npc.Code) is { MetBefore: true };
    }

    /// <summary>ARC-SCRIPT-1733 (CS2, case 15): bit 0x1 of the spell flags (<see cref="SpellFlags"/>, field 0x14) of any object, the flags the session's spells wrote included.</summary>
    public bool? IsInvisible(ScrObjectRef obj) => SpellFlags(obj) is { } flags ? (unchecked((uint)flags) & InvisibleSpellFlag) != 0 : null;

    /// <summary>ARC-SCRIPT-1733 (CS3, case 25 → 0x4AF210 → 0x4AAB00): an NPC whose danger state is surrender (the fleeing bit 0x10 is read first and wins, then critter flag 0x4000), the session's danger record; false for the PC and any other object type.</summary>
    public bool? IsSurrendered(ScrObjectRef obj)
    {
        if (IsPc(obj)) return false;
        if (Row(obj) is not { } row) return null;
        return row.Type == NpcType && _session.QueryDanger(obj.Code).State == Arcanum.Simulation.Ai.DangerState.Surrender;
    }

    /// <summary>
    /// ARC-SCRIPT-1733 / ARC-SCRIPT-1773 (CS4, case 26 → 0x4C1110, 0x460460): an NPC is in dialog while its conversation slot (entry 0 of the reaction-PC array, field 0x129) holds the PC: the session's reaction record. Every other object type, and the PC, goes to the dialog layer's callback
    /// (0x460460 → 0x567400 → 0x567420: in single player always the dialog slot 0 of the PC, whichever object was named), which returns the slot's word +0x1850: 1 from the dialog start (0x567460, after the NPC's conversation slot opened) until its close (0x5678D0,
    /// 0x567A20), else 0. The session's conversation slots tell the same span (<see cref="GameSessionOwner.QueryDialogOpen"/>: the conversation host opens an NPC's slot at the start and closes it at every exit), so a scenery or item answers as the PC does. Null for an object this host
    /// has no row for.
    /// </summary>
    public bool? IsInDialog(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryDialogOpen();
        if (Row(obj) is not { } row) return null;
        return row.Type == NpcType ? _session.QueryReactionRecord(obj.Code) is { InConversation: true } : _session.QueryDialogOpen();
    }

    /// <summary>ARC-SCRIPT-1733 / ARC-SCRIPT-1773 (CS5): sight (0x4AF260) of an NPC of the shown map or the PC toward an NPC of it or the PC, through the AI host (<see cref="AreaScriptWorld.CanSee"/>); null for any other pair.</summary>
    public bool? CanSee(ScrObjectRef observer, ScrObjectRef target) => Perceives(observer, target, _world?.CanSee);

    /// <summary>ARC-SCRIPT-1733 / ARC-SCRIPT-1773 (CS6): hearing (0x4AF470 at the normal loudness 1) of an NPC of the shown map or the PC toward an NPC of it or the PC, through the AI host (<see cref="AreaScriptWorld.CanHear"/>); null for any other pair.</summary>
    public bool? CanHear(ScrObjectRef listener, ScrObjectRef source) => Perceives(listener, source, _world?.CanHear);

    private bool? Perceives(ScrObjectRef from, ScrObjectRef to, Func<string, string, bool?>? check)
    {
        if (check is null || (!IsPc(from) && (from.Map != _shownMap || Row(from) is not { Type: NpcType }))) return null;
        if (!IsPc(to) && (to.Map != _shownMap || Row(to) is not { Type: NpcType })) return null;
        return check(IsPc(from) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : from.Code, IsPc(to) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : to.Code);
    }

    /// <summary>ARC-SCRIPT-1733 (CS7, case 30): object flag 0x400000 (field 0x13) of any object: the PC's and an NPC's as the session has them (the stored flags under the written ones), another row's as stored; null for an object this host has no row for.</summary>
    public bool? IsInvulnerable(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHero() is null ? null : (unchecked((uint)_session.QueryObjectFlags(AreaScriptHost.PcCode).Object(0)) & InvulnerableObjectFlag) != 0;
        if (Row(obj) is not { } row) return null;
        var stored = unchecked((int)(Number(row, ObjectFlagsSlot) ?? 0));
        return (unchecked((uint)(row.Type == NpcType ? _session.QueryObjectFlags(obj.Code).Object(stored) : stored)) & InvulnerableObjectFlag) != 0;
    }

    /// <summary>ARC-SCRIPT-1733 (CS8, case 38): an NPC with the undead critter flag (0xDA bit 0x4); false for the PC and any other object type.</summary>
    public bool? IsUndead(ScrObjectRef obj) => NpcCritterBit(obj, UndeadCritterFlag);

    /// <summary>ARC-SCRIPT-1733 (CS9, case 51 → 0x45EDE0): the prowling bit (0xDA bit 0x1) of a PC or an NPC, which the session keeps as concealment of the hero and of its followers (<see cref="GameSessionOwner.IsConcealed"/>); an NPC also reads prowling when its stored critter flags hold the bit; false for any other object type.</summary>
    public bool? IsProwling(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHero() is not null && _session.IsConcealed(SessionCombatant.Hero);
        if (Row(obj) is not { } row) return null;
        return row.Type == NpcType && (_session.IsConcealed(SessionCombatant.Critter(obj.Code)) || NpcCritterBit(obj, ProwlingCritterFlag) == true);
    }

    /// <summary>ARC-SCRIPT-1772 (CQ1, exe read 2026-11-13: action 38 at 0x447220 case 0x26, 0x45D010): an NPC's social class (field 0x128, the map data's; no script or action writes it), 0 for the PC and any other object type; null for an object this host has no row for.</summary>
    public int? SocialClassOf(ScrObjectRef obj) => NpcField(obj, row => row.EffectiveFields.FirstOrDefault(field => field.Slot == SocialClassSlot) is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= int.MaxValue ? (int)n : 0);

    /// <summary>ARC-SCRIPT-1772 (CQ2, case 0x27, 0x45D290): an NPC's origin (field 0x123) as it is now, a dialog action's set origin over the map data's (<see cref="AreaConversationHost.Scope(AreaObjectRecord, GameSessionOwner)"/>), 0 for the PC and any other object type; null for an object this host has no row for.</summary>
    public int? OriginOf(ScrObjectRef obj) => NpcField(obj, row => AreaConversationHost.Scope(row, _session).Origin);

    /// <summary>ARC-SCRIPT-1772 (CQ3, case 0x59, 0x45D080): an NPC's faction (field 0x124) as it is now, a script's set faction over the map data's (<see cref="Faction"/>), 0 for the PC and any other object type; null for an object this host has no row for.</summary>
    public int? FactionOf(ScrObjectRef obj) => NpcField(obj, row => AreaConversationHost.Scope(row, _session).Faction);

    // The three getters read a field only of an object whose type is NPC (0x10) and answer 0 for any other type, the PC included.
    private int? NpcField(ScrObjectRef obj, Func<AreaObjectRecord, int> read)
    {
        if (IsPc(obj)) return 0;
        if (Row(obj) is not { } row) return null;
        return row.Type == NpcType ? read(row) : 0;
    }

    /// <summary>
    /// ARC-SCRIPT-1771 (CS12, exe read 2026-11-13: condition 11 at 0x4451B0 → 0x45F460): for the PC the PC itself, else the object's leader (0x45DE00: the hero, or since ARC-SCRIPT-1842 an NPC leader; none: false). True when one of the leader's followers (the hero's party, or the NPC leader's follower array)
    /// is summoned (spell flag 0x4000), has no NPC flag 0x8000 and no animal critter flag and has neither object flag 0x102; a follower with 0x102 only logs a warning and the walk goes on. A follower this host cannot read (it has no row on the shown map) is unknown and the answer is null unless
    /// another follower decides it. The followers of a follower (0x441260 recurses) are not held by the session and are not walked.
    /// </summary>
    public bool? HasBadAssociates(ScrObjectRef obj)
    {
        if (IsPc(obj)) { if (_session.QueryHero() is null) return null; }
        else
        {
            if (Row(obj) is not { } row) return null;
            if (row.Type != NpcType || _session.QueryLeader(obj.Code) is null) return false;
        }
        var leader = IsPc(obj) ? AreaScriptHost.PcCode : _session.QueryLeader(obj.Code)!;
        var unknown = false;
        var followers = leader == AreaScriptHost.PcCode ? _session.QueryParty().Followers.Select(f => f.Entity).ToList() : _session.QueryFollowers(leader);
        foreach (var follower in followers)
        {
            var who = new ScrObjectRef(_shownMap, follower);
            if (SpellFlags(who) is not { } spell || Row(who) is not { } followerRow) { unknown = true; continue; }
            if ((unchecked((uint)spell) & SummonedSpellFlag) == 0) continue;
            if (Arcanum.Data.Combat.CombatFieldReader.Read(followerRow).Fields?.Npc is not { } npc) { unknown = true; continue; }
            if ((unchecked((uint)_session.QueryObjectFlags(who.Code).Npc(unchecked((int)npc.Flags))) & NoStrayNpcFlag) != 0) continue;
            if (NpcCritterBit(who, AnimalCritterFlag) is not { } animal) { unknown = true; continue; }
            if (animal) continue;
            var stored = unchecked((int)(Number(followerRow, ObjectFlagsSlot) ?? 0));
            if ((unchecked((uint)_session.QueryObjectFlags(who.Code).Object(stored)) & AssociateBlockingObjectFlags) == 0) return true;
        }
        return unknown ? null : false;
    }

    /// <summary>
    /// ARC-SCRIPT-1771 (CS13, exe read 2026-11-13: condition 19 at 0x4451B0, 0x406CA0 field 0x18, 0x502700 = art type = bits 28..31, 0x503F20 = monster number = bits 23..27 of a type 0xC art): whether the base art of the object (its own art field 0x18, which a polymorph does not touch) is a monster art
    /// of that number. The PC's base art is a human critter art (class 2: its body and gender come from the character's race), so false. Null for an object this host has no row for, and for an NPC whose art field does not read.
    /// </summary>
    public bool? IsMonsterOfType(ScrObjectRef obj, int type)
    {
        if (IsPc(obj)) return _session.QueryHero() is null ? null : false;
        if (Row(obj) is not { } row) return null;
        if (Number(row, BaseArtSlot) is not { } art) return row.Type == NpcType ? null : false;
        var id = unchecked((uint)art);
        return id >> 28 == Arcanum.Simulation.Equipment.BodyArmorArtRules.MonsterArt && (int)((id >> 23) & 31) == type;
    }

    // ARC-SCRIPT-1562: what a critter-or-not object gives a magick-tech adjustment: Critter false for a row of the shown map that is neither the PC nor an NPC (no aptitude is read), else the aptitude (the PC's of the session, an NPC's of the world; null when not known).
    // Null for an object this host has no row for.
    private (bool Critter, int? Aptitude)? CritterAptitude(ScrObjectRef obj)
    {
        if (IsPc(obj)) return (true, _session.QueryHeroScriptStat(Arcanum.Session.GameSessionOwner.ScriptStatAptitude));
        if (Row(obj) is not { } row) return null;
        if (row.Type != NpcType) return (false, null);
        return (true, obj.Map == _shownMap ? _world?.Aptitude?.Invoke(obj.Code) : null);
    }

    // The magick-tech complexity of an item the host has a row for (the item in use is a row of its origin map), else of an item the hero wears (a made or kit item has no row, ARC-SCRIPT-1562); null for any other (not modelled).
    private int? ComplexityOf(ScrObjectRef item) => Row(item) is { } row
        ? Arcanum.Data.Combat.CombatFieldReader.Read(row).Fields?.Item is { } fields ? fields.MagickTechComplexity : null
        : _session.QueryWornComplexity(item.Code);

    /// <summary>The identified test is not answered by the use host (its set holds no condition that asks it).</summary>
    public bool? IsIdentified(ScrObjectRef obj) => null;

    /// <summary>The use host runs no rewield.</summary>
    public bool ItemsBeingRewielded() => false;

    /// <summary>ARC-DEV-157 (GS2): the PC's dumb background (55, 74, 75); false for any other object.</summary>
    public bool IsDumbByBackground(ScrObjectRef obj) => IsPc(obj) && _session.QueryHeroDumbBackground();

    /// <summary>ARC-WORLD-081 (PG5): a draw from the session's generator, labelled <see cref="RandomLabel"/>.</summary>
    public int Random(int low, int high)
    {
        var roll = _session.Roll(RandomLabel, low, high);
        return roll.Roll?.Value ?? throw new InvalidOperationException($"AREA_USE_SCRIPT_RANDOM_REFUSED {roll.Failure}");
    }

    /// <summary>ARC-SCRIPT-049 (HW3): an object's tile as the vicinity places it (the PC's and an NPC's live cell, a row's world cell); null
    /// without one.</summary>
    public (long X, long Y)? Location(ScrObjectRef obj) => Place(obj) is { } cell ? (cell.X, cell.Y) : null;

    /// <summary>ARC-SCRIPT-049 (HW1): the PC's and an NPC's death as the session holds it (an NPC: its map data or a kill); null for any
    /// other object (an object's hit points are not read here) and for an object of no loaded row.</summary>
    public bool? IsDead(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHeroLife()?.Status.IsDead ?? false;
        return Row(obj) is { Type: NpcType } ? _session.QueryNpcDead(obj.Code) : null;
    }

    /// <summary>ARC-SCRIPT-049 (HW2): the "off" object flag of the row's map data, or for an NPC (of the shown map or a carried follower's home map, ARC-WORLD-1436) what <see cref="NpcOff"/> says:
    /// the session's object-flag change over the data's and a script's toggle state (<see cref="OffProperty"/>); the PC is never off; null for an object of no loaded row.</summary>
    public bool? IsSwitchedOff(ScrObjectRef obj)
    {
        if (IsPc(obj)) return false;
        if (Row(obj) is not { } row) return null;
        var data = Number(row, ObjectFlagsSlot) ?? 0;
        return row.Type == NpcType ? NpcOff(_session, obj.Map, obj.Code, data) : (data & OffObjectFlag) != 0;
    }

    // ARC-MAGIC-030 (MT21b, SC1): the object's spell flags (field 20) as the original's field getter reads them for any object type: a row's effective value, and for a
    // critter the flags the session's spell components wrote over it (QueryObjectFlags); the PC has no row, its flags are the written ones. The PC of a session without a hero and
    // an object of no loaded row are unknown (null: the run stops). The body-of-element bits are written by no spell component yet (the ObjFlag component takes only the hold
    // bits), so a running body spell does not show here until it does.
    public int? SpellFlags(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHero() is null ? null : _session.QueryObjectFlags(AreaScriptHost.PcCode).Spell(0);
        if (Row(obj) is not { } row) return null;
        var stored = unchecked((int)(Number(row, SpellFlagsSlot) ?? 0));
        return row.Type == NpcType ? _session.QueryObjectFlags(obj.Code).Spell(stored) : stored;
    }

    // SC2: the influence test 0x459380 over the session's spell runs (the run's target only: its two object lists are not modelled). The PC without a hero is asked nothing
    // (no runs); an object of no loaded row is unknown.
    public bool? IsInfluencedBy(ScrObjectRef obj, int spell)
    {
        if (!IsPc(obj) && Row(obj) is null) return null;
        return SpellRunRules.UnderInfluence(_session.QuerySpellRuns(), IsPc(obj) ? AreaScriptHost.PcCode : obj.Code, spell);
    }

    // SC3: known (0x4B1950) over the object's spell book (0x4B1AB0: a PC's or NPC's stored ranks, rank 0 for any other object).
    public bool? KnowsSpell(ScrObjectRef obj, int spell) => BookOf(obj) is { } of ? of.Book is { } book && SpellBookRules.IsKnown(book, spell) : null;

    // SC4: the mastered college (0x4B1CB0): the book's, -1 for none and for any object that is neither a PC nor an NPC.
    public int? MasteredCollege(ScrObjectRef obj) => BookOf(obj) is { } of ? of.Book?.MasteredCollege ?? SpellBookRules.NoMastery : null;

    // The spell book the original reads for the object: the hero's (null without a hero: unknown), an NPC's as the session records it, else as its map data stores it (the book
    // outside its ranges or missing fields: unknown), none for another object type (a PC or NPC type is the only one with ranks); an object of no loaded row is unknown.
    private ObjectBook? BookOf(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHeroSpellBook() is { } hero ? new ObjectBook(hero) : null;
        if (Row(obj) is not { } row) return null;
        if (row.Type is not (NpcType or PcType)) return new ObjectBook(null);
        var book = _session.QueryNpcSpellBook(obj.Code) ?? SpellBookRules.FromFields(Arcanum.Data.Combat.CombatFieldReader.Read(row).Fields?.Critter?.SpellTech);
        return book is null ? null : new ObjectBook(book);
    }

    // The spell book of an object that has a place in the model; Book is null for an object type with no ranks.
    private readonly record struct ObjectBook(SpellBook? Book);

    // The loaded advancement of an NPC row as its map data gives it (the effects change no sheet value), null when its fields give no admissible sheet.
    // ARC-MAGIC-1475: the base stats a spell wrote into the NPC lie over its stored ones (the sheet a script reads and the one a recruit is seeded from).
    private SessionNpcAdvancement? AdvancementOf(ScrObjectRef npc) =>
        Row(npc) is { } row && Arcanum.Data.Combat.CombatFieldReader.Read(row).Fields is { } fields
            ? AreaAdvancement.Npc(_session, npc.Code, fields, _ => null)?.Loaded : null;

    // An object's tile: the PC's live cell (the session's placement without a world), an NPC's live cell (ARC-SCRIPT-047), a row's world cell.
    private CellCoord? Place(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _world?.PcCell() ?? _session.QueryActor().Cell;
        if (_world?.CritterCell?.Invoke(obj) is { } live) return live;
        return Row(obj) is { WorldX: { } x, WorldY: { } y } && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
            ? new CellCoord((int)x, (int)y) : null;
    }

    public ScrObjectRef Pc => new(_session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode);

    /// <summary>ARC-SCRIPT-050: the shown map's runtime number (the dialog host borrows this host's answers only for its own map).</summary>
    public int ShownMap => _shownMap;

    private AreaObjectRecord? Row(ScrObjectRef obj)
    {
        if (obj.Map == _shownMap && _clicked is { } clicked && clicked.Entity == obj.Code) return clicked.Row;
        if (obj.Map == _shownMap && _shown.TryGetValue(obj.Code, out var shown)) return shown;
        if (_maps.FirstOrDefault(map => map.RuntimeMap == obj.Map) is { } map && map.TryGetRow(obj.Code, out var row)) return row;
        // ARC-DEV-371: a critter created at run time is in none of the maps' rows.
        if (obj.Map == _shownMap && _world?.CreatedRow?.Invoke(obj.Code) is { } created) return created;
        return _itemUse is { } item && item.Equals(obj) ? OtherMapRow?.Invoke(obj) : null;
    }

    /// <summary>ARC-DEV-316: the item whose use script runs now (the only object <see cref="OtherMapRow"/> answers for), else null.</summary>
    private ScrObjectRef? _itemUse;

    /// <summary>
    /// ARC-DEV-316 (B32 tail): the row of the item whose use script runs (<see cref="RunItemUse"/>) when its origin map is one the host was
    /// not given (an item taken on another map and used here), read by identity from the stored rows (the viewer wires the inventory host's
    /// record resolver). In the original an object carries its script slots wherever it is, so an item's use script runs on any map. Only
    /// the running item is asked: every other query about an object of an unheld map still answers as before (no row). Called with the
    /// item's origin map as <see cref="ScrObjectRef.Map"/>; null when no such row is found.
    /// </summary>
    public Func<ScrObjectRef, AreaObjectRecord?>? OtherMapRow { get; set; }

    /// <summary>The slot an object's row loaded for the point, or null.</summary>
    public static ScrSlot? LoadedSlot(AreaObjectRecord row, int point)
    {
        ArgumentNullException.ThrowIfNull(row);
        var element = row.EffectiveFields.SingleOrDefault(field => field.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == point);
        if (element is not { Words.Count: 3 } || element.Words[2] is < 0 or > ScrCatalogue.MaxScriptNumber || element.Words[0] is < 0 or > uint.MaxValue
            || element.Words[1] is < 0 or > uint.MaxValue) return null;
        return new((int)element.Words[2], (uint)element.Words[0], (uint)element.Words[1]);
    }

    // ARC-WORLD-125: an object whose map data holds no use slot still has the (empty) element in the original (SA1); a script trap device writes its slot onto such an object, so the use point reads as an empty
    // slot under the written script exactly when the session holds one. Every other point and every object the session wrote nothing on keep reading the loaded slot alone.
    private ScrSlot? LoadedOrPlaced(AreaObjectRecord row, string entity, int point) =>
        LoadedSlot(row, point) ?? (point == ScrPoint.Use && _session.QueryObjectChange(new SessionMapId(row.Key.RuntimeMap), MapChangeKey.ForEntity(entity, SlotProperty(ScrPoint.Use, ScriptPart))) is not null ? new ScrSlot(0, 0, 0) : null);

    /// <summary>The slot of the object at the point. The use slot is the loaded one under the session's map changes, and since
    /// ARC-WORLD-110 its script is 0 once the trap state removed the trap it names (docs/world/trap-state-runtime.md).</summary>
    public ScrSlot? Slot(ScrObjectRef attachee, int point)
    {
        if (Row(attachee) is not { } row || LoadedOrPlaced(row, attachee.Code, point) is not { } loaded) return null;
        if (!KeptAsMapChanges(point)) return loaded;
        long? Read(string property) => _session.QueryObjectChange(new SessionMapId(attachee.Map), MapChangeKey.ForEntity(attachee.Code, property));
        var slot = new ScrSlot(Read(SlotProperty(point, ScriptPart)) is { } script ? checked((int)script) : loaded.Script,
            Read(SlotProperty(point, LocalFlagsPart)) is { } flags ? checked((uint)flags) : loaded.LocalFlags,
            Read(SlotProperty(point, CountersPart)) is { } counters ? checked((uint)counters) : loaded.Counters);
        return point == ScrPoint.Use && TrapStateOf(row, attachee.Code, slot) is { Removed: true } ? slot with { Script = 0 } : slot;
    }

    // The points whose slot a run writes back as map changes (SlotProperty). ARC-DEV-312: the dying point's slot is kept under scr12.* (no save schema step: the keys are strings).
    private static bool KeptAsMapChanges(int point) => point is ScrPoint.Examine or ScrPoint.Use or ScrPoint.Dialog or CatchingThiefPoint or ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat or ScrPoint.TakingDamage or ScrPoint.LeaderSleeping or ScrPoint.Bust or ScrPoint.Dying
        or ScrPoint.EnterCombat or ScrPoint.ExitCombat or ScrPoint.Raise or ScrPoint.Hit or ScrPoint.CritterHits or ScrPoint.CriticalHit or ScrPoint.Miss or ScrPoint.CriticalMiss;

    // The trap's view of the use slot: the loaded one under the session's script and counters; null without a use slot or for a stored
    // script number outside the catalogue's range (it names no script the content holds).
    private ScrSlot? TrapSlot(AreaObjectRecord row, string entity)
    {
        if (LoadedOrPlaced(row, entity, ScrPoint.Use) is not { } loaded) return null;
        long? Read(string part) => _session.QueryObjectChange(new SessionMapId(row.Key.RuntimeMap), MapChangeKey.ForEntity(entity, SlotProperty(ScrPoint.Use, part)));
        var script = Read(ScriptPart) ?? loaded.Script;
        if (script is < 0 or > ScrCatalogue.MaxScriptNumber) return null;
        return new((int)script, loaded.LocalFlags, Read(CountersPart) is { } counters ? unchecked((uint)counters) : loaded.Counters);
    }

    /// <summary>
    /// ARC-WORLD-125: the use script now in the slot 1 of the shown map's object <paramref name="row"/> as a script trap device finds it: the loaded slot under the session's writes, 0 for a slot that holds no script, one
    /// the trap state removed or an object with no slot at all.
    /// </summary>
    public int UseScriptOf(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Slot(new ScrObjectRef(row.Key.RuntimeMap, EntityId(row)), ScrPoint.Use)?.Script ?? 0;
    }

    /// <summary>
    /// ARC-WORLD-125: the use slot (attachment point 1) of the item <paramref name="entity"/> that was taken from the map <paramref name="originMap"/> and is carried now, as its own data holds it (the
    /// item's use script a trap device copies); null for an item the host does not find, as <see cref="RunItemUse"/> reads it.
    /// </summary>
    public ScrSlot? ItemUseSlot(int originMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var item = new ScrObjectRef(originMap, entity);
        var before = _itemUse;
        _itemUse = item;
        try { return Row(item) is null ? null : Slot(item, ScrPoint.Use); }
        finally { _itemUse = before; }
    }

    /// <summary>ARC-WORLD-125: the script file header flags of a script number the content holds (0 for any other): the trap class of a device's script (<see cref="Arcanum.Simulation.Traps.TrapRules.Classify"/>).</summary>
    public uint TrapHeaderFlags(int script) =>
        script != 0 && _content is not null && Script(script) is { Status: ScrScriptStatus.Found, Program: { } program } ? program.FileFlags : 0;

    /// <summary>ARC-WORLD-110: a trap's session object: the row's map and its entity (<see cref="EntityId"/>).</summary>
    public static SessionTrapped Trapped(AreaObjectRecord row) => new(new SessionMapId((row ?? throw new ArgumentNullException(nameof(row))).Key.RuntimeMap), EntityId(row));

    /// <summary>
    /// ARC-WORLD-110: the trap baseline of a row (<see cref="SessionTrapBaseline"/>): a trap object (type 17) or another object, its use
    /// slot as the session's map changes hold it (the script a use wrote back, before the trap state's removal), the script file's header
    /// flags (0 for a script the content does not hold) and the row's object flags (slot 18). Null without script content, without a
    /// use slot, or for a stored number outside the catalogue's range.
    /// </summary>
    public SessionTrapBaseline? TrapBaseline(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return TrapSlot(row, EntityId(row)) is { } slot ? Baseline(row, slot) : null;
    }

    private SessionTrapBaseline? Baseline(AreaObjectRecord row, ScrSlot slot)
    {
        if (_content is null || slot.Script is < 0 or > ScrCatalogue.MaxScriptNumber) return null;
        var header = slot.Script != 0 && Script(slot.Script) is { Status: ScrScriptStatus.Found, Program: { } program } ? program.FileFlags : 0;
        return new(row.Type == TrapObjectType ? Arcanum.Simulation.Traps.TrapCarrier.TrapObject : Arcanum.Simulation.Traps.TrapCarrier.TrappedObject,
            slot.Script, header, unchecked((int)slot.Counters), unchecked((int)(Number(row, ObjectFlagsSlot) ?? 0)));
    }

    /// <summary>ARC-WORLD-110: the row's trap state now (<see cref="GameSessionOwner.QueryTrap"/> over <see cref="TrapBaseline"/>), or
    /// null without a baseline.</summary>
    public SessionTrapState? TrapState(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var entity = EntityId(row);
        return TrapSlot(row, entity) is { } slot ? TrapStateOf(row, entity, slot) : null;
    }

    // The trap state over the written slot; without a recorded trap state the baseline alone (no session call).
    private SessionTrapState? TrapStateOf(AreaObjectRecord row, string entity, ScrSlot slot)
    {
        if (Baseline(row, slot) is not { } baseline) return null;
        var map = new SessionMapId(row.Key.RuntimeMap);
        if (_session.QueryMapChange(map, MapChangeKey.ForEntity(entity, GameSessionOwner.TrapStateProperty)) is null)
            return new(Arcanum.Simulation.Traps.TrapRules.Classify(baseline.Script, baseline.HeaderFlags), baseline.Script, baseline.ObjectFlags, false);
        return _session.QueryTrap(new(map, entity), baseline);
    }

    private const int TrapObjectType = 17, ObjectFlagsSlot = 18;

    /// <summary>
    /// ARC-COMBAT-011: whether the object's use script is a trap (0x4BBDD0, ledger OD6): the script of its point-1 slot as the
    /// session holds it, when it is not 0 and its file's header flags carry 0x1 (non-magical) or 0x2 (magical); a script the
    /// content does not hold or cannot read is none, as the original's header read fails. Since ARC-WORLD-110 a trap the trap state
    /// removed is none. Null without script content (unknown).
    /// </summary>
    public bool? IsTrap(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_content is null) return null;
        // A stored number outside the catalogue's range names no script the content holds, so it is no trap, like a script the
        // original cannot load.
        return TrapState(row) is { Trapped: true };
    }

    public ScrScriptLookup Script(int number) => AreaScriptHost.Lookup(_content, number);

    public bool GlobalFlag(int index)
    {
        var flags = _session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        return read.Status == GlobalFlagReadStatus.Known && read.Value is { } value ? value : throw new InvalidOperationException("AREA_USE_SCRIPT_FLAG_UNREADABLE");
    }

    /// <summary>ARC-SCRIPT-019: the session's story-state number.</summary>
    public int StoryState() => _session.QueryStoryState();

    /// <summary>ARC-DEV-100 (GV1–GV4): the session's global variables and the hero's variables and flags, which the sets of this host admit
    /// (the use, tile, sector and heartbeat runs). A session without a hero reads 0 for the PC's.</summary>
    public int GlobalVariable(int index) => _session.QueryGlobalVariable(index);
    // ARC-DEV-265: the session holds the hero's arrays alone; another PC reads 0 (SessionScriptLimits.IsHero).
    public int PcVariable(ScrObjectRef pc, int index) => SessionScriptLimits.IsHero(pc) ? _session.QueryPcVariable(index) : 0;
    public bool PcFlag(ScrObjectRef pc, int index) => SessionScriptLimits.IsHero(pc) && _session.QueryPcFlag(index);
    public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
    // ARC-SCRIPT-047: the dialog gates of a call-ex of an NPC's dialog point (E6): dead as the session holds it (map data or killed);
    // surrender and mind control are not modelled here (normal).
    public ScrDialogState DialogState(ScrObjectRef attachee) =>
        !IsPc(attachee) && attachee.Map == _shownMap && Row(attachee) is { Type: NpcType } && _session.QueryNpcDead(attachee.Code) ? ScrDialogState.Dead : ScrDialogState.Normal;
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
    public bool IsTrapObject(ScrObjectRef attachee) => false;
    public ScrObjectRef? Player() => Pc;
    public bool IsPc(ScrObjectRef critter) => critter.Code == AreaScriptHost.PcCode;
    /// <summary>ARC-SCRIPT-1842 (0x45DE00): the critter's leader as the session holds it: the PC for a follower or waiting NPC of the hero, the NPC leader of a follower of an NPC (named on the critter's map), else null.</summary>
    public ScrObjectRef? Leader(ScrObjectRef critter) => IsPc(critter) ? null : _session.QueryLeader(critter.Code) switch
    {
        null => null,
        AreaScriptHost.PcCode => Pc,
        var leader => new ScrObjectRef(critter.Map, leader),
    };

    // The hero's side: the PC's follower or waiting NPC (the host's own reads that mean the hero's party, not the script leader query).
    private bool FollowsPc(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).LedByPc;
    public bool IsJilted(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).Jilted;

    // SG1: the PC's own entry (botched reads as 6); any other object the quest's global state. A number outside the quest
    // log's range (1000..1999) names no quest the session holds and reads 0; no installed script asks one (the real-data check).
    public int QuestState(ScrObjectRef obj, int quest)
    {
        if (!SessionStoryLimits.IsQuest(quest)) return 0;
        if (!IsPc(obj)) return (int)_session.QueryGlobalQuest(quest);
        return _session.QueryQuest(quest) is { } entry ? entry.Botched ? (int)SessionQuestState.Botched : (int)entry.State : 0;
    }

    // SG2: a direct item of the object's current list (the hero: kit and carried items; a holder: its list now).
    public bool HoldsItemNamed(ScrObjectRef obj, int name) => Items(obj).Any(item => item.Name == name);

    // ARC-SCRIPT-1164 (WO4, WO5, WO9): the world-object questions. A portal's or container's open state is the session's ART frame (0x4F08C0, 0x502B50: not 0 is open); any other object is not open; an object the host has no row of is unknown.
    private AreaScriptOpenable? OpenableOf(ScrObjectRef obj) => _world?.Openable?.Invoke(obj);

    bool? IScrWorldObjectQueries.IsOpen(ScrObjectRef obj)
    {
        if (IsPc(obj)) return false;
        if (Row(obj) is not { } row) return null;
        if (row.Type is not (PortalObjectType or ContainerObjectType)) return false;
        return OpenableOf(obj) is { } facts ? _session.QueryOpenable(facts.Openable, facts.Baseline).Open : null;
    }

    bool? IScrWorldObjectQueries.CanOpen(ScrObjectRef obj)
    {
        if (IsPc(obj)) return false;
        if (Row(obj) is not { } row) return null;
        if (row.Type is not (PortalObjectType or ContainerObjectType)) return false;
        return OpenableOf(obj)?.CanOpen;
    }

    bool IScrWorldObjectQueries.SectorBlocked(int x, int y) => _session.QuerySectorBlocked(new SessionMapId(_shownMap), x, y);

    bool IScrWorldObjectQueries.GeneratorBlocked(int slot) => _session.QueryGeneratorSlot(slot)?.Blocked ?? false;

    private const int PortalObjectType = 1, ContainerObjectType = 2;

    // SG3: the object's own name number (field 22) and spell flags (field 20) of any type, from its row's effective fields.
    // The PC has no row: its name number is not modelled and reads 0, its spell flags none.
    public int NameOf(ScrObjectRef obj) => Row(obj) is { } row ? NameOf(_session, row) : 0;

    public bool IsMindControlled(ScrObjectRef obj) => Row(obj) is { } row && IsMindControlled(row);

    /// <summary>SG3 (and the dialog's PA condition, ARC-DEV-227): a row's own name number (field 22), 0 when it holds none.</summary>
    public static int NameOf(AreaObjectRecord row) => Number(row, NameSlot) is { } name ? unchecked((int)name) : 0;

    /// <summary>SG3 (and the dialog's PA condition, ARC-DEV-227): whether a row's own spell flags (field 20) carry the mind-controlled bit.</summary>
    public static bool IsMindControlled(AreaObjectRecord row) => Number(row, SpellFlagsSlot) is { } flags && (unchecked((uint)flags) & MindControlledFlag) != 0;

    /// <summary>
    /// ARC-SCRIPT-1772 (CQ5): <see cref="NameOf(AreaObjectRecord)"/> with the name number a script's rename gave an NPC (<see cref="SessionNpcName.Property"/>, kept only while it differs from the map data's), which the name conditions and the dialog's follower-named test
    /// read; every other row keeps its own.
    /// </summary>
    public static int NameOf(GameSessionOwner session, AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(row);
        return row.Type == NpcType && session.QueryMapChange(new SessionMapId(row.Key.RuntimeMap), SessionNpcName.Key(EntityId(row))) is { } renamed ? unchecked((int)renamed) : NameOf(row);
    }

    private const int SpellFlagsSlot = 19, NameSlot = 21, SocialClassSlot = 85;

    private static long? Number(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(field => field.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= uint.MaxValue ? n : null;

    // SG4: the item in that inventory location (the hero's worn slots; a holder's items by their location).
    public int? WieldedItemName(ScrObjectRef obj, int location) => Items(obj).FirstOrDefault(item => item.Location == location)?.Name;

    /// <summary>SG5: only a PC has reputations; the session PC's (ARC-WORLD-044).</summary>
    public bool HasReputation(ScrObjectRef obj, int reputation) => IsPc(obj) && _session.HeroHasReputation(reputation);

    /// <summary>OV3: a row's type field; the PC (no row) is type 15.</summary>
    public int? ObjectType(ScrObjectRef obj) => IsPc(obj) ? PcType : Row(obj)?.Type;

    private const int PcType = 15;

    private sealed record HeldItem(int Name, int? Location);

    private IEnumerable<HeldItem> Items(ScrObjectRef obj)
    {
        if (IsPc(obj))
        {
            var worn = _session.QueryEquipment().Worn.ToDictionary(item => item.Entity, item => (int)item.Slot, StringComparer.Ordinal);
            int? Location(string entity) => worn.TryGetValue(entity, out var slot) ? slot : null;
            return _session.QueryHeroKit().Select(item => new HeldItem(item.Kind.Name, Location(item.Entity)))
                .Concat(_session.QueryPcInventory().Select(item => new HeldItem(item.Kind.Name, Location(item.Entity))))
                // ARC-MAGIC-051: an item the hero assembled lives in the made items, not the kit or the pack (the powered shield's timer needs its name).
                .Concat(_session.QueryMadeItems().Select(item => new HeldItem(item.Kind.Name, Location(item.Entity)))).ToArray();
        }
        var holder = _session.QueryMapInventory(new SessionMapId(obj.Map))?.Holders.FirstOrDefault(h => h.Entity == obj.Code);
        return holder is null ? [] : holder.Items.Select(item => new HeldItem(item.Facts.Kind.Name, item.Facts.InventoryLocation)).ToArray();
    }

    /// <summary>
    /// Runs the object's use script with the PC as the triggerer and applies its effects: the global flags, the deferred
    /// calls, then the object's use slot as map changes (a value equal to the loaded one clears its key). The first teleport of
    /// the player is returned for the caller's transfer; <paramref name="teleportRefusal"/> names why the caller cannot take
    /// it (or null), and then the run is not applied. Another object's teleport is not modelled and leaves the run unapplied.
    /// Effects apply as the run goes: a step the session refuses leaves the earlier ones applied, as for a dialog script.
    /// Printed lines are read for the caller to show; effect sounds are not played here.
    /// </summary>
    [OriginalFunction(0x441980, OriginalFunctionScope.Partial)]
    public AreaScriptedUse Run(AreaObjectRecord row, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal = null)
    {
        var used = RunUse(row, rules, teleportRefusal, out var trap);
        return trap is null ? used : used with { Trap = trap };
    }

    /// <summary>
    /// ARC-WORLD-1162 (0x57B180, SWB5): the bed's use script on attachment point 1, run with the hero as triggerer exactly as a use is (<see cref="Run"/>: the trap meeting first, then the script with its effects applied), and
    /// its answer in the hook's terms. A trap that ended the call answers 0 (the entry point's trap branch), so the hero goes on to the seat; a script that ended <i>run default</i>, and a bed with no script on the
    /// point (the entry point's answer for it, SA10), answer 1, which refuses the hero. A run the host does not model (a refused script, a teleport from a bed, a conversation) is <see cref="AreaBedAnswer.NotModelled"/>.
    /// </summary>
    public AreaBedScriptRun RunBed(AreaObjectRecord row, ScrRuleProfile rules)
    {
        var used = Run(row, rules, _ => "a teleport from a bed's script is not modelled");
        if (used.Trap is { Cancels: true }) return new(AreaBedAnswer.SkipDefault, used);
        // A conversation the script asks for is the viewer's to start (the rest hook then pops the interface stack it stands on), which is not modelled: no answer is taken from such a run.
        if (used.Outcome != AreaScriptedUseOutcome.NoTeleport || used.Run is not { } run || used.Dialog is not null) return new(AreaBedAnswer.NotModelled, used);
        return new(run.Outcome == ScrOutcome.RunDefault ? AreaBedAnswer.RunDefault : AreaBedAnswer.SkipDefault, used);
    }

    /// <summary>
    /// ARC-WORLD-110: the trap meeting of a use (<see cref="AreaTraps.Use"/>), asked by <see cref="Run"/> before the use script: null
    /// lets the use go on as before (the object carries no trap, or no trap host is set).
    /// </summary>
    public Func<AreaObjectRecord, AreaTrapMeeting?>? Traps { get; set; }

    private AreaScriptedUse RunUse(AreaObjectRecord row, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal, out AreaTrapMeeting? trap)
    {
        trap = null;
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the object is not on the shown map");
        // The object entry point's spot and trap run (E2, E4) come first; a trap spotted now or a native run fired ends the use (skip
        // default), and an ordinary trap script runs below with the spot decided.
        trap = Traps?.Invoke(row);
        if (trap is { Cancels: true }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "trap: " + trap.Describe());
        // The clicked row answers for its entity during this run, whichever copy of a repeated id the index kept.
        _clicked = (entity, row);
        try
        {
            var attachee = new ScrObjectRef(_shownMap, entity);
            var run = ScrDispatcher.RunObject(new(ScrPoint.Use, attachee, Pc, null), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, teleportRefusal, writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = null; }
    }

    /// <summary>
    /// ARC-SKILLS-010: an NPC's catching-thief script (point 11, PP7) after a noticed pick-pocket failure, with the PC (the thief) as the
    /// triggerer (ARC-DEV-246: or <paramref name="thief"/>, an NPC thief of the shown map) and no extra object (ARC-AI-2084: or <paramref name="guarded"/>, the portal, container or scenery a guard witness watches); its slot is kept as map changes (<see cref="SlotProperty"/>). A teleport is not modelled from it and
    /// leaves the run unapplied. The caller reads its outcome: anything but "run default" stops the theft's consequences.
    /// </summary>
    public AreaScriptedUse RunCatchingThief(AreaObjectRecord row, ScrRuleProfile rules, ScrObjectRef? thief = null, ScrObjectRef? guarded = null)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the object is not on the shown map");
        _clicked = (entity, row);
        try
        {
            var attachee = new ScrObjectRef(_shownMap, entity);
            var run = ScrDispatcher.RunObject(new(CatchingThiefPoint, attachee, thief ?? Pc, guarded), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, _ => "a teleport from a catching-thief script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = null; }
    }

    /// <summary>
    /// ARC-WORLD-114: an NPC's first-heartbeat (10) or heartbeat (19) script, which the AI tick runs with the NPC as the attachee and
    /// the triggerer (0x4AD200), under <see cref="HeartbeatAdmission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>).
    /// Its toggle state switches an NPC of the shown map off and back on (<see cref="OffProperty"/>), its kill goes to the world's
    /// <see cref="AreaScriptWorld.Kill"/>; either for another object leaves the run
    /// unapplied. The caller reads the outcome: anything but "run default" stops the tick's pass.
    /// </summary>
    public AreaScriptedUse RunHeartbeat(AreaObjectRecord row, int point, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (point is not (ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat)) throw new ArgumentOutOfRangeException(nameof(point), "Not a heartbeat point.");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        // A tick runs often: as for a step, only the files this run reads are checked (after the run, in Finish).
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap || row.Type != NpcType) return new(AreaScriptedUseOutcome.Refused, null, [], null, "not an NPC of the shown map");
        _clicked = (entity, row);
        try
        {
            var npc = new ScrObjectRef(_shownMap, entity);
            var run = ScrDispatcher.RunObject(new(point, npc, npc, null), this, new() { Rules = rules, Admission = HeartbeatAdmission });
            return Finish(run, _ => "a teleport from a heartbeat script is not modelled", writeback => WriteObjectSlot(writeback, npc));
        }
        finally { _clicked = null; }
    }

    // ARC-DEV-254 (GT198): the original's guard (global 0x5FC26C) is set around the point-0x17 script call only; a damage dealt while it runs skips the script.
    private bool _inTakingDamage;

    /// <summary>
    /// ARC-DEV-254 (MIG13 S14j, GT198–GT203): the taking-damage script (attachment point 0x17, 23) of <paramref name="victim"/>, an object of the shown map (any type: the original runs it for portals, containers, scenery and items too), run by the
    /// damage apply 0x4B4390 before the destroyed, invulnerable and wall gates and before the resistance step. The triggerer is the damage's attacker (<paramref name="attacker"/>, null for a damage without one: a script's own damage,
    /// the null handle), the extra object none, line 0, under <see cref="HeartbeatAdmission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>). A nested damage dealt while the script runs (the guard, which brackets the script
    /// call only) skips it: no run. The caller reads <see cref="TakingDamageVetoes"/>. An object that is not a row of the shown map, or has no slot at the point, has no script (the step goes on).
    /// </summary>
    public AreaScriptedUse RunTakingDamage(string victim, ScrObjectRef? attacker, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(victim); ArgumentNullException.ThrowIfNull(rules);
        if (_inTakingDamage) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "nested damage: the guard skips the script");
        var attachee = new ScrObjectRef(_shownMap, victim);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.TakingDamage) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        var previous = _clicked;
        _clicked = (victim, row);
        _inTakingDamage = true;
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.TakingDamage, attachee, attacker, null), this, new() { Rules = rules, Admission = HeartbeatAdmission });
            return Finish(run, _ => "a teleport from a taking-damage script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _inTakingDamage = false; _clicked = previous; }
    }

    /// <summary>
    /// ARC-SCRIPT-1483 (script trigger audit slice 5, ledger CP1–CP5): the set a combat-point script (hit 7, critter hits 26, critical hit 34) runs under: the placed set (<see cref="PlacedAdmission"/>), whose damage and cast actions deal the
    /// installed poison weapons' damage and the college poison spell. ARC-SCRIPT-1562 (CP7): plus the commands of <see cref="ScrAdmissionSet.S51"/> the poison weapon scripts 30032, 30076 and 30199 and the hunting weapon 28007 use: the magick-tech adjustments (91, 80), play script eye candy (120) and obj is animal (37). ARC-SCRIPT-1661 (CP11, CP12): plus the commands of <see cref="ScrAdmissionSet.S52"/> the area weapon scripts 30064 and 30074 and the alignment weapon 30252 use: obj is in combat (31, <see cref="IsInCombat"/>) and adjust stat (137, the hero's alignment only, <see cref="CanAdjustStat"/>). A script outside it is refused and the hit goes on as without one.
    /// </summary>
    public static ScrAdmissionSet CombatPointAdmission { get; } = new("S4-area-combat-point", PlacedAdmission.Conditions.Concat([ScrCatalogue.Condition.IsAnimal, ScrCatalogue.Condition.IsInCombat]),
        PlacedAdmission.Actions.Concat([ScrCatalogue.Action.GetMagickTechAdjustment, ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy, ScrCatalogue.Action.AdjustStat]),
        PlacedAdmission.ValueKinds, PlacedAdmission.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// ARC-SCRIPT-1483 (exe read 2026-11-13, ledger CP1–CP5): the combat-point script <paramref name="point"/> of <paramref name="attachee"/>, which the original's hit step runs once the hit is decided and before the damage step (0x4B3770 for a swing or an
    /// arrow's strike: the calls at 0x4B37A3, 0x4B37CD and 0x4B37FC; 0x4B2F60 for a projectile's strike: 0x4B2F90, 0x4B2FD4, 0x4B3003; both through 0x441980, whose answer these call sites ignore). The three points: the hit script (7) of the attack
    /// context's weapon (the weapon item, nothing for bare hands) with the attacker as the triggerer and the victim as the extra object; the critical-hit script (34) of the same weapon, for a critical hit, with the same objects; and the critter-hits
    /// script (26) of the attacker with the victim as the triggerer and no extra object. The attachee is a row of the shown map (an NPC, or an item an NPC holds or lies there) or, for a carried item, a row the item host reads by identity
    /// (<see cref="OtherMapRow"/>, as <see cref="RunItemUse"/> does); an object with no slot at the point has no script (<see cref="AreaScriptedUseOutcome.NoTeleport"/> with no run). Under <see cref="CombatPointAdmission"/>; the slot is kept as map
    /// changes. A teleport and a conversation are not modelled from a hit and leave the run unapplied; no answer cancels the hit.
    /// ARC-COMBAT-1719 (0x4B2870, ledger PF9, PS5): the same call serves the two ends of a projectile's flight, where the original runs the weapon's hit script (7, and 34 for a critical weapon) at the destination of a shot with no target
    /// and its miss script (8, and 35) at the range end, with the shooter as the triggerer and the shot's target (none for a scattered throw) as the extra object.
    /// </summary>
    public AreaScriptedUse RunCombatPoint(int point, ScrObjectRef attachee, ScrObjectRef? triggerer, ScrObjectRef? extra, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (point is not (ScrPoint.Hit or ScrPoint.CritterHits or ScrPoint.CriticalHit or ScrPoint.Miss or ScrPoint.CriticalMiss)) throw new ArgumentOutOfRangeException(nameof(point), "Not a combat point.");
        var previousItem = _itemUse;
        // An item the host was not given a map of is read by identity, as an item's use is; every other object is a row of a loaded map.
        _itemUse = attachee;
        try
        {
            if (Row(attachee) is not { } row || Slot(attachee, point) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
            if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
            var previous = _clicked;
            _clicked = (attachee.Code, row);
            try
            {
                var run = ScrDispatcher.RunObject(new(point, attachee, triggerer, extra), this, new() { Rules = rules, Admission = CombatPointAdmission });
                return Finish(run, _ => "a teleport from a combat-point script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
            }
            finally { _clicked = previous; }
        }
        finally { _itemUse = previousItem; }
    }

    /// <summary>
    /// ARC-WORLD-1160 (E3-a; 0x57BAC0, 0x4411C0, ledger SWB8): the leader-sleeping script (attachment point 29) of <paramref name="follower"/>, an NPC of the shown map that leads the hero's rest: the rest's option click runs it for
    /// every direct follower of the sleeper with the hero as the triggerer and the follower as the attachee, no extra object, line 0, under <see cref="HeartbeatAdmission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>).
    /// The answer decides nothing (the click goes on whatever it says). A follower that is not a row of the shown map, or has no script at the point, runs nothing.
    /// </summary>
    [OriginalFunction(0x57BAC0, OriginalFunctionScope.Partial)]
    public AreaScriptedUse RunLeaderSleeping(string follower, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(follower); ArgumentNullException.ThrowIfNull(rules);
        var attachee = new ScrObjectRef(_shownMap, follower);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.LeaderSleeping) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        var previous = _clicked;
        _clicked = (follower, row);
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.LeaderSleeping, attachee, Pc, null), this, new() { Rules = rules, Admission = HeartbeatAdmission });
            return Finish(run, _ => "a teleport from a leader-sleeping script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = previous; }
    }

    /// <summary>
    /// ARC-DEV-312: the set a dying script runs under (<see cref="RunDying"/>): the union of <see cref="PlacedAdmission"/> (the quest, reputation, area, vicinity and dialog commands the installed dying scripts 1805, 2137 and 2173 use,
    /// besides what a tile script does) and <see cref="HeartbeatAdmission"/> (kill and toggle state). A script outside it is refused and the death goes on as without one.
    /// </summary>
    public static ScrAdmissionSet DyingAdmission { get; } = new("S4-area-dying",
        PlacedAdmission.Conditions.Union(HeartbeatAdmission.Conditions), PlacedAdmission.Actions.Union(HeartbeatAdmission.Actions),
        PlacedAdmission.ValueKinds.Union(HeartbeatAdmission.ValueKinds), PlacedAdmission.FocusKinds.Union(HeartbeatAdmission.FocusKinds), counterIndexWraps: true);

    /// <summary>
    /// ARC-DEV-312 (exe read 2026-11-10, ledger DY1–DY3): the dying script (attachment point 12) of <paramref name="victim"/>, a creature of the shown map whose hit points the damage or kill has just written, which the kill (0x45DA20)
    /// runs through the script dispatcher (0x441980, 0x45DA57) before any death processing: the killer (<paramref name="killer"/>, null for a death with no source: a script's kill or damage, a burn) as the triggerer, the victim as the
    /// attachee, no extra object, line 0, under <see cref="DyingAdmission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>). The caller reads <see cref="DyingStops"/>. A victim that is not a row of the shown map,
    /// or has no script at the point, runs nothing.
    /// </summary>
    public AreaScriptedUse RunDying(string victim, ScrObjectRef? killer, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(victim); ArgumentNullException.ThrowIfNull(rules);
        var attachee = new ScrObjectRef(_shownMap, victim);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.Dying) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        var previous = _clicked;
        _clicked = (victim, row);
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.Dying, attachee, killer, null), this, new() { Rules = rules, Admission = DyingAdmission });
            return Finish(run, _ => "a teleport from a dying script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = previous; }
    }

    /// <summary>
    /// ARC-AI-1530 (exe read 2026-11-13, ledger VB2-6): the enter-combat (13, 0x4AAE29) or exit-combat (14, 0x4AAF32) script of <paramref name="npc"/>, an NPC of the shown map, which the danger setter 0x4AABE0 runs through the script
    /// dispatcher (0x441980) when the NPC starts or stops fighting: the setter's target (<paramref name="triggerer"/>, none for an exit with no target) as the triggerer, the NPC as the attachee, no extra object, line 0, under
    /// <see cref="DyingAdmission"/> (the set of the other NPC death and combat scripts); its slot is kept as map changes (<see cref="SlotProperty"/>). The setter ignores the answer. An NPC that is not a row of the shown map, or has
    /// no script at the point, runs nothing.
    /// </summary>
    public AreaScriptedUse RunDangerPoint(int point, string npc, ScrObjectRef? triggerer, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(npc); ArgumentNullException.ThrowIfNull(rules);
        if (point is not (ScrPoint.EnterCombat or ScrPoint.ExitCombat)) throw new ArgumentOutOfRangeException(nameof(point), "Not an enter-combat or exit-combat point.");
        var attachee = new ScrObjectRef(_shownMap, npc);
        if (Row(attachee) is not { } row || Slot(attachee, point) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        // ARC-AI-1634: a danger setter reached while another danger script of this host is being applied (the script's stop attacking or set-state effect, which the session commits while the outer run's effects are still
        // being applied) runs its script nested, as the original's does (the setter has no guard: the state bits written before the script keep a call from entering twice); the run is counted, and the nesting is
        // bounded so that a script chain cannot grow without end.
        if (_dangerRunDepth >= MaxDangerRunDepth) return new(AreaScriptedUseOutcome.Refused, null, [], null, $"nested danger scripts: the depth bound {MaxDangerRunDepth} is reached");
        if (_dangerRunDepth > 0) NestedDangerRuns++;
        var previous = _clicked;
        _clicked = (npc, row);
        _dangerRunDepth++;
        try
        {
            var run = ScrDispatcher.RunObject(new(point, attachee, triggerer, null), this, new() { Rules = rules, Admission = DyingAdmission });
            return Finish(run, _ => "a teleport from a danger-point script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _dangerRunDepth--; _clicked = previous; }
    }

    /// <summary>
    /// ARC-MAGIC-1167 (magic audit MA-10, exe read 2026-11-13, ledger RZ1-RZ3): the raise script (attachment point 18, 0x12) of <paramref name="target"/>, a creature of the shown map that a heal with the raise bit is about to bring back:
    /// the heal applier (0x4B58C0, the call at 0x4B5A46) runs it through the script dispatcher (0x441980) after it has computed the refusal by the creature's flags and before it tests that refusal, so it runs whether the flags refuse or not. The healer
    /// (<paramref name="healer"/>, the heal record's parent; null for a heal with none) is the triggerer, the target the attachee, line 0, under <see cref="DyingAdmission"/> (the set of the other NPC point scripts); its slot is kept as map
    /// changes (<see cref="SlotProperty"/>). The record's source (a critter parent's wielded weapon, 0x4B23B0) is the extra object in the original; it is none here (the spell host knows no weapon identity, and MG36 passes the caster no weapon). The caller reads <see cref="RaiseRefuses"/>.
    /// A target that is not a row of the shown map, or has no script at the point, runs nothing (the raise goes on). The original has no guard; a script whose effects raise the same creature again reaches this call nested, so the depth is bounded
    /// (<see cref="MaxDangerRunDepth"/>) and a deeper call runs nothing.
    /// </summary>
    public AreaScriptedUse RunRaise(string target, ScrObjectRef? healer, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(rules);
        var attachee = new ScrObjectRef(_shownMap, target);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.Raise) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (_raiseRunDepth >= MaxDangerRunDepth) return new(AreaScriptedUseOutcome.Refused, null, [], null, $"nested raise scripts: the depth bound {MaxDangerRunDepth} is reached");
        var previous = _clicked;
        _clicked = (target, row);
        _raiseRunDepth++;
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.Raise, attachee, healer, null), this, new() { Rules = rules, Admission = DyingAdmission });
            return Finish(run, _ => "a teleport from a raise script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _raiseRunDepth--; _clicked = previous; }
    }

    private int _raiseRunDepth;

    /// <summary>
    /// ARC-MAGIC-1167 (RZ2): whether a raise run (<see cref="RunRaise"/>) refuses the raise: the dispatcher's "skip default" (return 0 of 0x441980, which the applier tests as <c>result == 0</c>, 0x4B5A4E), as for the taking-damage script
    /// (<see cref="TakingDamageVetoes"/>): a script number whose file the content lacks, a trap script and a script that runs past its last line skip the default too. A run that was not applied (refused by the admission, effects the viewer
    /// cannot apply, a teleport) refuses nothing: the raise goes on.
    /// </summary>
    public static bool RaiseRefuses(AreaScriptedUse ran) => TakingDamageVetoes(ran);

    /// <summary>ARC-AI-1634: how deep danger scripts may nest (the dispatcher's own call bound, <see cref="ScrRunOptions.MaxCallDepth"/>'s default).</summary>
    public const int MaxDangerRunDepth = 16;

    private int _dangerRunDepth;

    /// <summary>ARC-AI-1634: how many danger-script runs of this host began while another was being applied (the logged nested runs of <see cref="RunDangerPoint"/>).</summary>
    public int NestedDangerRuns { get; private set; }

    /// <summary>
    /// ARC-DEV-312 (DY2): whether a dying run (<see cref="RunDying"/>) stops the death processing: the dispatcher's "skip default" (return 0 of 0x441980 ends the kill at 0x45DA61), which a script that runs past its last line
    /// also returns. A run that was not applied (refused by the admission, effects the viewer cannot apply, an unreadable or changed file, a requested teleport) stops nothing: the death goes on.
    /// </summary>
    public static bool DyingStops(AreaScriptedUse ran)
    {
        ArgumentNullException.ThrowIfNull(ran);
        return ran.Outcome is AreaScriptedUseOutcome.NoTeleport && ran.Run is { } run && run.Outcome == ScrOutcome.SkipDefault;
    }

    /// <summary>ARC-TECHNOLOGY-1760: the script reference of an object of the shown map by its script entity (<see cref="EntityId"/>), as a BUST breaker.</summary>
    public ScrObjectRef ObjectRef(string entity) => new(_shownMap, entity);

    /// <summary>
    /// ARC-DEV-260 (ledger OD4, OD15): the BUST script (attachment point 30) of <paramref name="victim"/>, an object of the shown map, which the break
    /// (0x43F1C0) runs through the script dispatcher (0x441980) with <paramref name="breaker"/> as the triggerer (null: a break with no attacker,
    /// the null handle), the object as the attachee and no extra object, under <see cref="HeartbeatAdmission"/>. The break ignores the answer, so
    /// the caller reads nothing from it; its slot is kept as map changes (<see cref="SlotProperty"/>). An object that is not a row of the shown
    /// map, or has no script at the point, runs nothing.
    /// </summary>
    public AreaScriptedUse RunBust(string victim, ScrObjectRef? breaker, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(victim); ArgumentNullException.ThrowIfNull(rules);
        var attachee = new ScrObjectRef(_shownMap, victim);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.Bust) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        var previous = _clicked;
        _clicked = (victim, row);
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.Bust, attachee, breaker, null), this, new() { Rules = rules, Admission = HeartbeatAdmission });
            return Finish(run, _ => "a teleport from a bust script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = previous; }
    }

    /// <summary>
    /// ARC-SKILLS-1156 (ledger TT5): a new item of the prototype <paramref name="prototype"/> (a script's create-object number, as a disarmed trap's device
    /// is named) in the hero's pack (<see cref="GameSessionOwner.CreateItemForHero"/>), with its prototype's weight and scripts. Null when the world has
    /// no item data for the number.
    /// </summary>
    public InventoryMutationResult? GiveHeroPrototypeItem(int prototype, SessionInsertCheck insertCheck)
    {
        if (_world?.Item(prototype) is not { } item) return null;
        _session.AttachItemWeights(number => _world.Item(number - AreaPrototypeItems.PrototypeOffset)?.Weight);
        _session.AttachItemScripts(number => _world.Item(number - AreaPrototypeItems.PrototypeOffset)?.Scripts);
        _session.AttachItemNoDrop(number => _world.Item(number - AreaPrototypeItems.PrototypeOffset)?.NoDrop == true);
        return _session.CreateItemForHero(_session.Version, item.Kind, insertCheck);
    }

    /// <summary>
    /// ARC-DEV-260 (ledger OD4, OD6): the ordinary trap script (the use slot, point 1) of <paramref name="row"/> run by the trap trigger (0x4BCB00)
    /// with <paramref name="triggerer"/> (null: none, a ranged attack or a damage with no attacker) as the triggerer and the object as the
    /// attachee, under <see cref="Admission"/>. Unlike a use it makes no spot attempt and no trap check first. Its slot is kept as map changes
    /// (<see cref="SlotProperty"/>); a teleport is not modelled from it and leaves the run unapplied.
    /// </summary>
    public AreaScriptedUse RunTrapScript(AreaObjectRecord row, ScrObjectRef? triggerer, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the object is not on the shown map");
        var attachee = new ScrObjectRef(_shownMap, entity);
        if (Slot(attachee, ScrPoint.Use) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        var previous = _clicked;
        _clicked = (entity, row);
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.Use, attachee, triggerer, null), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, _ => "a teleport from a trap script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = previous; }
    }

    /// <summary>
    /// ARC-DEV-254 (GT199): whether a taking-damage run (<see cref="RunTakingDamage"/>) cancels the damage: the dispatcher's "skip default" (return 0 of 0x441980; a script that runs past its last line ends the same way). A script number whose file the content lacks, a trap script and a script that runs past its last line skip the default, as
    /// the original does (GT199). A run that was not applied (refused by the admission, effects the viewer cannot apply, an unreadable or changed file, a requested teleport) cancels nothing: the damage goes on. A dialog the script asks for is not started.
    /// </summary>
    public static bool TakingDamageVetoes(AreaScriptedUse ran)
    {
        ArgumentNullException.ThrowIfNull(ran);
        return ran.Outcome is AreaScriptedUseOutcome.NoTeleport && ran.Run is { } run && run.Outcome == ScrOutcome.SkipDefault;
    }

    /// <summary>
    /// ARC-DEV-326 (B18, ledger HK22, OH1): the examine script (attachment point 0) of <paramref name="row"/>, which the original runs
    /// whenever it describes an object (0x57CD60, the call at 0x57CD93, reached from the world's hover 0x553A70, the hotkey bar's hover
    /// 0x550150, the inventory's still pointer 0x573C74 and the barter and dialog windows; the viewer runs it from the first three) through the script dispatcher (0x441980) with the
    /// local PC as the triggerer, the object as the attachee, no extra object and line 0, for any object type. The description follows only
    /// when the answer is not "skip default" (<see cref="ExamineSkips"/>). An object with no script at the point answers "run default"
    /// without a run (0x441980 finds no attachment). The run's effects apply as a use's do, under <see cref="Admission"/>; its slot is kept
    /// as map changes (<see cref="SlotProperty"/>). A teleport is not modelled from an examine: the run stays unapplied. A conversation it asks for is applied but not started (the answer is then skip default: no description, as in the original).
    /// </summary>
    public AreaScriptedUse RunExamine(AreaObjectRecord row, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        var entity = EntityId(row);
        var attachee = new ScrObjectRef(row.Key.RuntimeMap, entity);
        var previous = _clicked;
        _clicked = (entity, row);
        try
        {
            if (Slot(attachee, ScrPoint.Examine) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
            if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
            if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
            var run = ScrDispatcher.RunObject(new(ScrPoint.Examine, attachee, Pc, null), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, _ => "a teleport from an examine script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = previous; }
    }

    /// <summary>
    /// ARC-MAGIC-048 (E4-c, ledger CN6, CN7): the use script (attachment point 1) of <paramref name="target"/>, an object of the shown map, run through the script dispatcher (0x441980) with any triggerer (<paramref name="triggerer"/>;
    /// null: none) and any extra object (<paramref name="extra"/>), which is the seam the Use spell component (0x452ED0) needs: it passes the target in all three object pairs, so the object runs its own use script as if it had used itself. The attachee is the target,
    /// line 0, under <see cref="Admission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>). An object that is not a row of the shown map, or has no use slot with a script, has none (no run: the original's dispatcher finds no
    /// attachment). Not modelled: the trap preamble of the dispatcher's use point (0x4BBFE0, 0x4BBE40: a trapped target does not meet its trap here, <see cref="AreaTraps.Use"/> is the PC's) and a teleport the script asks for
    /// unless the caller takes it (<paramref name="teleportRefusal"/>; without one the run is left unapplied). One method per original address: the PC's use is <see cref="Run"/>.
    /// ARC-WORLD-1805: a caller that can queue the hero's change of place (the door's use node for the hero, ledger TT18, TT20) passes <paramref name="teleportRefusal"/>, which names why it cannot take the asked teleport (or null);
    /// a run that asked for one applied returns <see cref="AreaScriptedUseOutcome.Teleport"/> with it, for the caller to queue (the original's action only records the request, 0x447220 case 0x2C → 0x4D3380).
    /// </summary>
    public AreaScriptedUse RunUseScript(string target, ScrObjectRef? triggerer, ScrObjectRef? extra, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal = null)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(rules);
        var attachee = new ScrObjectRef(_shownMap, target);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.Use) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var previous = _clicked;
        _clicked = (target, row);
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.Use, attachee, triggerer, extra), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, teleportRefusal ?? (_ => "a teleport from a use component's script is not modelled"), writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = previous; }
    }

    /// <summary>
    /// ARC-WORLD-1668 (ledger TT18, 0x428A10 → 0x441980 point 1): whether the use script a door's use node ran (<see cref="RunUseScript"/>, the opener as the triggerer and the extra object) stops the use: the dispatcher's answer 0, "skip default",
    /// which includes a script that runs past its last line. A run that was not applied (refused by the admission, effects the viewer cannot apply, an unreadable or changed file, a requested teleport the caller could not take), an object with no use script and a native trap script the trap code left unmet stop nothing:
    /// the use goes on (the original's node returns 1 for no attachment: 0x441980 answers 1). ARC-WORLD-1805: a run that asked for a teleport the caller took (<see cref="AreaScriptedUseOutcome.Teleport"/>) answers as its script does: the teleport is only a request
    /// the script recorded (0x4D3380), so "run default" lets the door open first and "skip default" keeps it shut, the request standing either way.
    /// </summary>
    public static bool UseStops(AreaScriptedUse ran)
    {
        ArgumentNullException.ThrowIfNull(ran);
        // The dispatcher's stop at a native trap script (30000..30007) is no answer of a script: the trap code runs those, and a trap whose meeting could not be made here (no cast host, a critter the host cannot apply
        // it for) was left unmet, so the use goes on as before and the door opens (a divergence: the original fires and, for a self-removing trap, consumes it).
        return ran.Outcome is AreaScriptedUseOutcome.NoTeleport or AreaScriptedUseOutcome.Teleport && ran.Run is { } run && run.Outcome == ScrOutcome.SkipDefault && run.Stop != ScrStop.TrapScript;
    }

    /// <summary>ARC-DEV-326: whether an examine run (<see cref="RunExamine"/>) leaves the object without a description: the dispatcher's
    /// "skip default" (return 0 of 0x441980, 0x57CD9B), which includes a script that runs past its last line. A run that was not applied
    /// (refused by the admission, effects the viewer cannot apply, an unreadable or changed file, a requested teleport) or an object with no
    /// script describes as before: the viewer shows the description of an object it cannot examine.</summary>
    public static bool ExamineSkips(AreaScriptedUse ran)
    {
        ArgumentNullException.ThrowIfNull(ran);
        return ran.Outcome is AreaScriptedUseOutcome.NoTeleport && ran.Run is { } run && run.Outcome == ScrOutcome.SkipDefault;
    }

    /// <summary>
    /// ARC-SCRIPT-020: an item's use script (point 1, item use 0x462CC0; ledger IU rows) with the PC as the user and no target, before
    /// the use goes on: the run's outcome decides (run default lets the use go on, skip default stops it). The item is a row of a
    /// loaded map's inventory (<see cref="AreaInventoryMap"/>), named by its origin map and entity; its use slot is kept as map
    /// changes there. A teleport is not modelled from an item's use and leaves the run unapplied. ARC-SCRIPT-1481 (IU2): with a <paramref name="target"/> entity (the PC's, or an NPC of the shown map) that object is the use's target too (the extra object: a right click and the use icon with Shift
    /// target the hero, the item cursor a critter; the food use script 30282 heals its extra object).
    /// </summary>
    public AreaScriptedUse RunItemUse(int originMap, string entity, ScrRuleProfile rules, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(entity); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var item = new ScrObjectRef(originMap, entity);
        _itemUse = item;
        try
        {
            if (Row(item) is null) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the item is not a row of a loaded map");
            var run = ScrDispatcher.RunObject(new(ScrPoint.Use, item, Pc, target is null ? null : target == AreaScriptHost.PcCode ? Pc : new ScrObjectRef(_shownMap, target)), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, _ => "a teleport from an item's use is not modelled", writeback => WriteObjectSlot(writeback, item));
        }
        finally { _itemUse = null; }
    }

    /// <summary>Map-change keys of a tile or sector script's slot, on its sector: <c>tile.N.script</c>, <c>tile.N.lf</c>,
    /// <c>tile.N.counters</c> for the tile index N, <c>sector.script</c>, <c>sector.lf</c>, <c>sector.counters</c>.</summary>
    public static string PlacedProperty(MapPlacedScript placed, string part)
    {
        ArgumentNullException.ThrowIfNull(placed);
        return placed.Trigger == MapTransitionTrigger.TileEntry ? $"tile.{placed.Tile}.{part}" : $"sector.{part}";
    }

    public const string PlacedScriptPart = "script", PlacedLocalFlagsPart = "lf", PlacedCountersPart = "counters";

    /// <summary>A tile or sector script's slot now: the sector file's attachment, overridden by the session's map changes on
    /// its sector (<see cref="PlacedProperty"/>) of the shown map.</summary>
    public ScrSlot PlacedSlot(MapPlacedScript placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        long? Read(string part) => _session.QueryMapChange(new SessionMapId(_shownMap), MapChangeKey.ForSector(placed.Sector.Id, PlacedProperty(placed, part)));
        return new(Read(PlacedScriptPart) is { } script ? checked((int)script) : placed.Script,
            Read(PlacedLocalFlagsPart) is { } flags ? checked((uint)flags) : placed.LocalFlags,
            Read(PlacedCountersPart) is { } counters ? checked((uint)counters) : placed.Counters);
    }

    /// <summary>
    /// ARC-SCRIPT-017: runs a tile script (the PC entered its tile) or a sector script (the PC entered its sector) of the
    /// shown map, from line 0 as a use with the PC as the triggerer and no attachee, and applies its effects as
    /// <see cref="Run"/> does. A changed slot is written back as map changes on its sector; the dispatcher discards a tile
    /// script's changes under the original's rule (ledger X3). ARC-SCRIPT-046: a tile script runs for an NPC of the shown map that
    /// entered its tile as well (<paramref name="triggerer"/>; ledger TR3: for a PC or an NPC), and a dialog action of the run is
    /// handed to the caller (<see cref="AreaScriptedUse.Dialog"/>, set <see cref="PlacedAdmission"/>).
    /// </summary>
    public AreaScriptedUse RunPlaced(MapPlacedScript placed, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal = null, ScrObjectRef? triggerer = null)
    {
        ArgumentNullException.ThrowIfNull(placed); ArgumentNullException.ThrowIfNull(rules);
        if (placed.Trigger is not (MapTransitionTrigger.TileEntry or MapTransitionTrigger.SectorEntry)) throw new ArgumentException("Not a tile or sector script.", nameof(placed));
        // The original runs a sector script for the PC only.
        if (triggerer is not null && placed.Trigger != MapTransitionTrigger.TileEntry) throw new ArgumentException("A sector script runs for the PC only.", nameof(triggerer));
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        // ARC-DEV-328: the original keeps one object wherever it stands (FM1), so a companion carried here runs the tile's script as any NPC does; it is named by
        // its home map, and the viewer's live cell for it (<see cref="AreaScriptWorld.CritterCell"/>) says it stands here.
        if (triggerer is { } npc && (Row(npc) is not { Type: NpcType } || (npc.Map != _shownMap && _world?.CritterCell?.Invoke(npc) is null)))
            return new(AreaScriptedUseOutcome.Refused, null, [], null, "the triggerer is not an NPC of the shown map");
        // A step runs often: unlike a click it checks only the files this run reads (the check after the run), not every
        // file seen so far.
        var slot = PlacedSlot(placed);
        var options = new ScrRunOptions { Rules = rules, Admission = PlacedAdmission };
        var run = placed.Trigger == MapTransitionTrigger.TileEntry
            ? ScrDispatcher.RunTile(new(slot, triggerer ?? Pc), this, options)
            : ScrDispatcher.RunSector(new(slot, Pc), this, options);
        var source = placed.Trigger == MapTransitionTrigger.TileEntry ? ScrSource.Tile : ScrSource.Sector;
        return Finish(run, teleportRefusal, writeback => WritePlacedSlot(writeback, placed, source));
    }

    /// <summary>
    /// ARC-WORLD-2038 (ledger TT20): why a script's teleport of an object other than the hero cannot be recorded as the NPC request (<see cref="AreaScriptWorld.RequestNpcTeleport"/>), or null: no world seam, an object that is no NPC of the shown map,
    /// a destination on another map (FM1: only its record would move, not built) or the AI host's own refusal (a follower, a cell it does not hold). The use host's finish and the talk and call host's apply (<see cref="AreaScriptHost.Apply"/>) both ask it before anything applies.
    /// </summary>
    public string? NpcTeleportRefusedBecause(ScrTeleport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_world?.NpcTeleportRefusal is not { } npcRefusal || _world.RequestNpcTeleport is null) return $"a teleport of {other.Object.Code} is not modelled";
        if (other.Object.Map != _shownMap || Row(other.Object) is not { Type: NpcType }) return $"a teleport of {other.Object.Code}, which is no NPC of the shown map, is not modelled";
        if (other.RuntimeMap != -1 && other.RuntimeMap != _shownMap) return $"a teleport of {other.Object.Code} to another map (catalogue {other.CatalogueMap}) is not modelled";
        return npcRefusal(other);
    }

    /// <summary>ARC-WORLD-2038 (TT20): records a script's teleport of an NPC in the session's one change-of-place slot (the object moves at the next ping), after <see cref="NpcTeleportRefusedBecause"/> answered null and the run applied.</summary>
    public void RequestNpcTeleport(ScrTeleport teleport) => _world!.RequestNpcTeleport!(teleport);

    private AreaScriptedUse Finish(ScrRunResult run, Func<ScrTeleport, string?>? teleportRefusal, Func<ScrWriteback, bool> write)
    {
        if (run.Outcome == ScrOutcome.Refused)
            return new(AreaScriptedUseOutcome.Refused, null, [], run, $"script {run.Script} SCR line {run.Refusal!.Line}: {run.Refusal.Code} type {run.Refusal.Type}");
        // ARC-SCRIPT-018: a call that ended the dispatch (an unreadable callee, the call bound) applied nothing: not a run.
        // ARC-DEV-157: likewise a question the host cannot answer (get stat of an NPC or of a stat the session does not hold, GS3), which the dispatch
        // ends as not modelled with nothing applied: reported as refused, not as a silent default.
        if (run.Stop is ScrStop.UnreadableScript or ScrStop.CallDepth or ScrStop.NotModelled)
            return new(AreaScriptedUseOutcome.Refused, null, [], run, $"script {run.Script}: {run.Stop}");
        if (_content!.Changed) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], run, $"script {run.Script}: a script file changed since it was first read");
        var effects = run.Effects;
        // ARC-WORLD-1837 (TT20): a teleport of an NPC of the shown map to a cell of the shown map is only a request the script records (0x447220 case 0x2C → 0x4D3380), which the AI host's relocation carries out at the next ping.
        // The original's slot holds one request whoever makes it, so a run that asks for the hero's and another object's teleport is not told apart here (not modelled), nor is a teleport to another map or of an object that is no NPC of the shown map.
        var npcTeleports = new List<ScrTeleport>();
        if (effects.Teleports.Any(t => !IsPc(t.Object)))
        {
            if (effects.Teleports.FirstOrDefault(t => IsPc(t.Object)) is { } mixed)
                return new(AreaScriptedUseOutcome.EffectsRejected, null, [], run, $"script {run.Script}: a run that teleports {mixed.Object.Code} and another object is not modelled");
            foreach (var other in effects.Teleports)
            {
                if (NpcTeleportRefusedBecause(other) is { } npcWhy) return new(AreaScriptedUseOutcome.EffectsRejected, null, [], run, $"script {run.Script}: {npcWhy}");
                npcTeleports.Add(other);
            }
        }
        // A teleport the caller cannot take (a map the mode cannot open, an arrival not admitted) leaves the use undone.
        else if (effects.Teleports.FirstOrDefault() is { } asked && teleportRefusal?.Invoke(asked) is { } refusal)
            return new(AreaScriptedUseOutcome.TeleportRefused, asked, [], run, refusal);
        var lines = new List<string>();
        var printedLines = new List<AreaPrintedLine>();
        foreach (var line in effects.Lines)
        {
            if (_content.Dialog(line.Script, _session.Rules.Data) is not { Status: ScriptContentStatus.Read, Document: { } document }) continue;
            var printed = ScrPrintedLine.Read(document, line.Line, false, AreaConversationProfile.Diagnostic.Names);
            if (printed.Status != ScrPrintedLineStatus.Shown) continue;
            lines.Add(printed.Text);
            printedLines.Add(new(line.Kind, printed.Text));
        }
        // ARC-DEV-298 (FL2, FL4): each float line's text, read before anything applies; a DLG that is missing or unreadable floats nothing (the original's load fails), a line
        // the NPC-line step would do more for (an action field, generated text) leaves the run unapplied.
        var floats = new List<AreaScriptFloat>();
        foreach (var line in effects.Floats)
        {
            if (_content.Dialog(line.Script, _session.Rules.Data) is not { Status: ScriptContentStatus.Read, Document: { } document }) continue;
            var printed = ScrPrintedLine.Read(document, line.Line, false, AreaConversationProfile.Diagnostic.Names);
            if (printed.Status != ScrPrintedLineStatus.Shown) return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: float line {line.Line} of script {line.Script} is not a plain line");
            floats.Add(new(line.Speaker, printed.Text, line.Pc));
        }
        // ARC-SCRIPT-1764 (NP10): the float newspaper headline requests, read before anything applies.
        floats.AddRange(AreaScriptNewspaper.Floats(_session, effects, _newspaperTexts.Get));
        // ARC-WORLD-081: a create object the viewer cannot apply (no world, a prototype that is no item, an object without a place on the shown
        // map) leaves the whole run unapplied.
        if (Creatable(effects) is { } refusedCreation)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: create object {refusedCreation}");
        // ARC-WORLD-114: likewise a toggle state or kill the viewer cannot apply.
        if (ObjectStateRefusal(effects) is { } refusedState)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: {refusedState}");
        // ARC-SCRIPT-046: a conversation goes to an NPC of the shown map and not with a teleport (not modelled together); such a run
        // applies nothing and counts as refused, so a tile's unconditional teleport still fires as before (ARC-WORLD-031).
        if (effects.Dialog is { } dialog && (dialog.Npc is not { } speaker || speaker.Map != _shownMap || Row(speaker) is not { Type: NpcType } || effects.Teleports.Count > 0))
            return new(AreaScriptedUseOutcome.Refused, null, lines, run, $"script {run.Script}: a conversation of script {dialog.Script} with "
                + (dialog.Npc?.Code ?? "no NPC") + (effects.Teleports.Count > 0 ? " and a teleport" : "") + " is not modelled");
        // ARC-SCRIPT-052: a conversation with an NPC destroyed (before or by this run) is not modelled (the view no longer has him).
        if (effects.Dialog is { Npc: { } talker } && (AlreadyDestroyed(talker)
            || effects.ObjectStates.Any(r => r.Change == ScrObjectStateChange.Destroy && r.Object == talker)))
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: a conversation with the destroyed {talker.Code} is not modelled");
        // A slot a callee's dialog stored, or (ARC-SCRIPT-047) a call-ex run changed, is kept as the object's map changes of that point, which
        // need its loaded element; a point the host does not keep, or an object without the element (the original writes its sparse
        // attachment array), leaves the run unapplied, checked before anything applies.
        if (effects.SlotWrites.FirstOrDefault(w => w.Source != ScrSource.Object || !KeptAsMapChanges(w.Point) || w.Attachee is not { } owner
            || Row(owner) is not { } held || LoadedOrPlaced(held, owner.Code, w.Point) is null) is { } unkept)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: the point {unkept.Point} slot of {unkept.Attachee?.Code ?? "no object"} a callee or a change script wrote is not kept (no loaded element)");
        if (!Apply(effects, write)) return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script} {run.Stop}: the session refused its effects");
        _floats.AddRange(floats);
        _eyeCandies.AddRange(AreaScriptEyeCandy.Of(effects));
        // ARC-WORLD-1837: the requests are recorded after the run applied, in action order (the script goes on; the object moves at the next ping).
        foreach (var requested in npcTeleports) RequestNpcTeleport(requested);
        var teleport = npcTeleports.Count > 0 ? null : effects.Teleports.FirstOrDefault();
        if (effects.Dialog is { } talk)
            return new(AreaScriptedUseOutcome.NoTeleport, null, lines, run, $"script {run.Script} {run.Stop} at SCR line {run.StopLine}: conversation of script {talk.Script} node {talk.Node}")
            { Dialog = talk, Printed = printedLines };
        return teleport is null
            ? new(AreaScriptedUseOutcome.NoTeleport, null, lines, run, $"script {run.Script} {run.Stop} at SCR line {run.StopLine}: " + (npcTeleports.Count > 0 ? $"{npcTeleports.Count} teleport request(s) of NPCs" : "no teleport"))
                { Printed = printedLines, NpcTeleports = npcTeleports }
            : new(AreaScriptedUseOutcome.Teleport, teleport, lines, run, $"script {run.Script}: teleport to {teleport.CatalogueMap} ({teleport.X},{teleport.Y})") { Printed = printedLines };
    }

    private bool Apply(ScrEffects effects, Func<ScrWriteback, bool> writeSlot)
    {
        if (effects.DestroyTrapObject is not null) return false;
        // The effect rule of every blessing or curse the run adds is known before anything applies (a missing rule refuses the run whole).
        if (!AreaScriptStory.BlessingRulesResolve(_session, effects, IsPc, BlessingEffects)) return false;
        foreach (var write in effects.GlobalFlags)
        {
            var flags = _session.Flags;
            var receipt = flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(write.Index), write.Value));
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)) return false;
        }
        // ARC-DEV-100: the global variables and the hero's variables and flags.
        if (!_session.ApplyScriptStores(effects)) return false;
        // ARC-SCRIPT-019: the story-state number and the quest, area, rumor and reputation requests, for the PC.
        if (!AreaScriptStory.Apply(_session, effects, IsPc, _awards.Add, BlessingEffects)) return false;
        // ARC-SCRIPT-1764 (NP9): the queue newspaper requests, in action order, by the rule of the original's queue (0x4BF140).
        if (!AreaScriptNewspaper.Queue(_session, effects)) return false;
        // As for a dialog script (AreaScriptHost.Apply): scheme pairs go to the session, applied steps stay applied when a later
        // one is refused (the original applies as it goes); effect sounds are not played by a use click.
        foreach (var sound in effects.Sounds)
            if (sound.Kind == ScrSoundKind.Scheme) _session.PlaySoundScheme(_session.Version, sound.Id, sound.Ambient);
        foreach (var request in effects.Calls)
        {
            var queued = request.Kind == ScrCallKind.Schedule
                ? _session.ScheduleScriptCall(_session.Version, new(request.Script, request.Line, AreaScriptHost.Role(request.Triggerer), AreaScriptHost.Role(request.Attachee)), request.DelayMs)
                : _session.RemoveScriptCall(_session.Version, request.Script, AreaScriptHost.Role(request.Attachee));
            if (queued.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-UI-049: a reading action shows its document to the PC only; it changes no session state.
        foreach (var request in effects.Readings)
            if (IsPc(request.Viewer)) _readings.Add(request);
        // ARC-SCRIPT-1241 (MV5): each fade's clock jump, then its movie for the viewer; the later steps stop when the clock refuses.
        var asked = new List<AreaScriptFade>();
        _fadeClears += effects.Fades.Count;
        var fadesApplied = AreaScriptFades.Apply(_session, effects, asked);
        _fades.AddRange(asked.Where(fade => fade.Movie != 0));   // the fades before a refused jump keep their movie, as their clock jumps stay
        if (effects.Slides.Any(request => request.Kind == ScrSlideKind.EndGame)) _endGame = true;
        if (!fadesApplied) return false;
        // ARC-WORLD-081: each create object adds its item to the ground next to its object (Creatable checked them); under Steamwoven a patch
        // at the limit takes no more (no change).
        // An item a script creates has its prototype's weight (not saved, so attached before the creations).
        if (effects.Creations.Count > 0)
        {
            _session.AttachItemWeights(prototype => _world!.Item(prototype - AreaPrototypeItems.PrototypeOffset)?.Weight);
            // ARC-DEV-097 (MIG8, SA4): and their prototypes' script arrays, which an item a script creates reads until its first write.
            _session.AttachItemScripts(prototype => _world!.Item(prototype - AreaPrototypeItems.PrototypeOffset)?.Scripts);
            _session.AttachItemNoDrop(prototype => _world!.Item(prototype - AreaPrototypeItems.PrototypeOffset)?.NoDrop == true);
        }
        foreach (var request in effects.Creations)
        {
            // ARC-DEV-371 (PG1): a critter prototype makes a critter on the same tile search; the action ignores the result.
            if (_world!.Item(request.Prototype) is null)
            {
                var made = _session.CreateCritterNear(_session.Version, request.Prototype + AreaPrototypeItems.PrototypeOffset, new SessionMapId(_shownMap), Place(request.Near)!.Value,
                    _world.DropMap()!, OwnerOf(request.Near));
                _created.Add(new(request.Prototype, request.Near, new(made.Disposition, made.Failure, made.Object?.Entity, null, made.Version)) { Critter = made.Object?.Entity, Cell = made.Object?.Cell });
                if (made.Disposition == GameSessionDisposition.Rejected) return false;
                if (made.Object is { } critter) _world.CritterMade?.Invoke(critter.Entity);
                continue;
            }
            var item = _world.Item(request.Prototype)!;
            var result = _session.CreateGroundItem(_session.Version, item.Kind, new SessionMapId(_shownMap), Place(request.Near)!.Value, _world.DropMap()!,
                OwnerOf(request.Near));
            _created.Add(new(request.Prototype, request.Near, result));
            if (result.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-SCRIPT-1164 (WO5, WO9): the sector and generator toggles, each to the state the run left it in.
        foreach (var change in effects.WorldChanges)
        {
            if (change.Change == ScrWorldChange.SectorBlocked)
            {
                if (_session.SetSectorBlocked(_session.Version, new SessionMapId(_shownMap), change.A, change.B, change.Blocked).Disposition == GameSessionDisposition.Rejected) return false;
            }
            else if (_session.QueryGeneratorSlot(change.A) is { } slot && slot.Blocked != change.Blocked
                && _session.ToggleGeneratorBlock(_session.Version, change.A).Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-WORLD-114: toggle state and kill in action order (ObjectStateRefusal checked them); ARC-SCRIPT-052: destroy and stop attacking.
        foreach (var request in effects.ObjectStates)
        {
            if (request.Change is ScrObjectStateChange.SetLockState or ScrObjectStateChange.ToggleOpenClosed or ScrObjectStateChange.CreateItem
                or ScrObjectStateChange.TransferItem or ScrObjectStateChange.DestroyItemNamed or ScrObjectStateChange.SetWrittenStart or ScrObjectStateChange.ToggleItemInventoryDisplay)
            {
                if (!ApplyWorldObject(request)) return false;
                continue;
            }
            if (request.Change == ScrObjectStateChange.Kill) { if (!_world!.Kill!(request.Object)) return false; continue; }
            if (request.Change == ScrObjectStateChange.Destroy)
            {
                // ARC-SCRIPT-1481 (VT6): the item being used goes out of the PC's list (0x43CCA0 through the session's used-item destruction).
                if (_itemUse is { } used && used.Equals(request.Object))
                {
                    if (_session.IsItemDestroyed(used.Code)) continue;
                    if (_session.DestroyUsedItem(_session.Version, used.Code).Disposition == GameSessionDisposition.Rejected) return false;
                    continue;
                }
                // TD2: an NPC already destroyed is left as it is.
                if (AlreadyDestroyed(request.Object)) continue;
                var destroyed = _session.ApplyChanges(_session.Version, null,
                    [new(new SessionMapId(request.Object.Map), MapChangeKey.ForEntity(request.Object.Code, DestroyedProperty), 1)]);
                if (destroyed.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            if (request.Change == ScrObjectStateChange.StopAttacking) { if (!StopAttacking(request.Object)) return false; continue; }
            if (request.Change == ScrObjectStateChange.PerformAnimation)
            {
                // GT155 has no type test and GT173 makes a non-critter art ignore the action; the PC and other maps are not modelled: such a target is left as it is, the run's other effects apply.
                if (ShownNpc(request.Object)) LastAnimation = _world!.ScriptAnimate!(request.Object.Code, request.Value);
                continue;
            }
            if (request.Change is ScrObjectStateChange.Damage or ScrObjectStateChange.DamageUnresistable) { ApplyScriptDamage(request); continue; }
            if (request.Change == ScrObjectStateChange.Attack) { ApplyScriptAttack(request); continue; }
            if (request.Change == ScrObjectStateChange.MakeFollow) { if (!ApplyMakeFollow(request)) return false; continue; }
            if (request.Change == ScrObjectStateChange.GiveSpellMastery)
            {
                // SC5: the PC, or an NPC of the shown map (ObjectStateRefusal checked its sheet); any other object does nothing in the original. The spell-book rule decides, the run goes on.
                if (IsPc(request.Object) ? !AreaScriptSpells.GiveMastery(_session, null, request.Value)
                    : ShownNpc(request.Object) && AdvancementOf(request.Object) is { } npc && !AreaScriptSpells.GiveMastery(_session, npc, request.Value)) return false;
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealHitPoints) { ApplyScriptHeal(request); continue; }
            if (request.Change == ScrObjectStateChange.CastSpell) { ApplyScriptCast(request); continue; }
            if (request.Change == ScrObjectStateChange.StopSpell)
            {
                // RM6 (0x4593F0): the PC or any object of the shown map; the lookup finds a run only where one holds the object, and the run goes on whatever it answers.
                LastStopSpell = null;
                if (IsPc(request.Object) || request.Object.Map == _shownMap)
                    LastStopSpell = _world!.ScriptStopSpell!(IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value);
                continue;
            }
            if (request.Change == ScrObjectStateChange.BecomeProne)
            {
                // GT187: an NPC of the shown map; the PC and any other object are left as they are (the run's other effects apply).
                if (ShownNpc(request.Object)) LastProne = _world!.ScriptProne!(request.Object.Code);
                continue;
            }
            if (request.Change == ScrObjectStateChange.StealCoins)
            {
                // GT164: a thief that is an NPC of the shown map; the victim an NPC of the shown map or the PC; any other pair is left as it is. The thief's type is not checked in the original.
                if (ShownNpc(request.Object) && request.Other is { } victim && (IsPc(victim) || ShownNpc(victim)))
                    LastSteal = _world!.ScriptSteal!(request.Object.Code, IsPc(victim) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : victim.Code);
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealFatigue)
            {
                // GT162: a negative amount raises the damage in the original (not modelled); the session's refusal does not stop the run (GT154).
                // ARC-SCRIPT-1481: the PC's fatigue damage falls through the session's life step too (VT2).
                if (request.Value > 0 && IsPc(request.Object)) LastHealLife = _world!.ScriptHealLife!(Arcanum.Simulation.Ai.AiSchedulerRules.Pc, request.Value, 0);
                else if (request.Value > 0 && ShownNpc(request.Object)) _world!.HealFatigue!(request.Object, request.Value);
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealPoison)
            {
                // VT3: the PC or an NPC of the shown map; the level falls by the amount to 0 at most through the session's life step (the stat setter's timer event, ledger PZ1, PZ3).
                if (request.Value > 0 && (IsPc(request.Object) || ShownNpc(request.Object) && !AlreadyDestroyed(request.Object)))
                    LastHealLife = _world!.ScriptHealLife!(IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, 0, request.Value);
                continue;
            }
            if (request.Change is ScrObjectStateChange.WalkTo or ScrObjectStateChange.RunTo)
            {
                // GT154: fire and forget. GT158: a negative X gives the original's Y = -1 (an off-map tile) and a negative Y is off the map too: nothing is started (a project choice, the walk's result is never seen).
                if (request.Value >= 0 && request.Value2 >= 0 && ShownNpc(request.Object))
                    LastWalk = _world!.ScriptWalk!(request.Object.Code, new CellCoord(request.Value, request.Value2), request.Change == ScrObjectStateChange.RunTo);
                continue;
            }
            if (request.Change == ScrObjectStateChange.Disband)
            {
                // NS1: a follower or waiting NPC of the PC, or (ARC-SCRIPT-1842) a follower of an NPC, is let go (the session's leave with the follow check skipped); a free object, the PC and any object that follows no one do nothing. The AI host sees the leave on its next tick.
                if (IsPc(request.Object)) continue;
                var left = _session.LeaveParty(_session.Version, request.Object.Code, new PartyLeave(Force: true));
                if (left.Disposition == GameSessionDisposition.Rejected && left.Failure != GameSessionFailure.InvalidCompanion) return false;
                continue;
            }
            if (request.Change is ScrObjectStateChange.SetReaction or ScrObjectStateChange.AdjustReaction)
            {
                // NS2: the NPC of the shown map toward the PC only; any other pair is left as it is (the original acts for an NPC and a PC only, and this viewer's facts are the shown map's).
                if (ShownNpc(request.Object) && request.Other is { } who && IsPc(who))
                    LastReaction = _world!.ScriptReaction!(request.Object.Code, request.Value, request.Change == ScrObjectStateChange.SetReaction);
                continue;
            }
            if (request.Change == ScrObjectStateChange.ChangeArtNumber)
            {
                if (!ApplyArt(request)) return false;
                continue;
            }
            if (request.Change is ScrObjectStateChange.SetDayStandpoint or ScrObjectStateChange.SetNightStandpoint)
            {
                // NS3: an NPC (type 16) of the shown map takes the location as its standpoint (the record is kept even when it equals the row's); any other object is left as it is. The extended actions also write the teleport map.
                if (!ShownNpc(request.Object)) continue;
                var day = request.Change == ScrObjectStateChange.SetDayStandpoint;
                var packed = SessionNpcStandpoint.Pack(unchecked((uint)request.Value), unchecked((uint)request.Value2));
                var set = _session.SetMapChange(_session.Version, new SessionMapId(request.Object.Map), SessionNpcStandpoint.Key(request.Object.Code, day), packed);
                if (set.Disposition == GameSessionDisposition.Rejected) return false;
                if (request.Map is { } catalogueMap)
                {
                    var teleport = _session.SetTeleportMap(_session.Version, request.Object.Code, new SessionMapId(unchecked(catalogueMap - 4999)));
                    if (teleport.Disposition == GameSessionDisposition.Rejected) return false;
                }
                continue;
            }
            if (request.Change == ScrObjectStateChange.SetFaction)
            {
                // HW4: an NPC only (ObjectStateRefusal checked the map); a faction equal to its map data's clears the key.
                if (IsPc(request.Object) || Row(request.Object) is not { Type: NpcType } npc) continue;
                var loaded = AreaConversationHost.Scope(npc).Faction;
                var set = _session.ApplyChanges(_session.Version, null, [new(new SessionMapId(request.Object.Map),
                    MapChangeKey.ForEntity(request.Object.Code, FactionProperty), request.Value == loaded ? null : request.Value)]);
                if (set.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            if (request.Change == ScrObjectStateChange.ToggleInvulnerability)
            {
                // CQ4: the flag 0x400000 of the hero or an NPC (ObjectStateRefusal checked the object) is flipped over the state the session has now, so the writes of a run with the same object twice follow each other.
                var stored = IsPc(request.Object) ? 0 : unchecked((int)(Number(Row(request.Object)!, ObjectFlagsSlot) ?? 0));
                var flipped = _session.WriteObjectFlag(_session.Version, request.Object.Code, SessionObjectFlagField.Object, unchecked((int)InvulnerableObjectFlag), IsInvulnerable(request.Object) != true,
                    dataHas: (unchecked((uint)stored) & InvulnerableObjectFlag) != 0);
                if (flipped.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            if (request.Change == ScrObjectStateChange.Rename)
            {
                // CQ5: an NPC's name number (ObjectStateRefusal checked the object); a number equal to its map data's clears the key.
                if (Row(request.Object) is not { Type: NpcType } renamed) continue;
                var written = _session.ApplyChanges(_session.Version, null, [new(new SessionMapId(request.Object.Map),
                    SessionNpcName.Key(request.Object.Code), request.Value == NameOf(renamed) ? null : request.Value)]);
                if (written.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            // ARC-SCRIPT-1360: the original flips the object flag "off" itself. An NPC the map data stores off is switched on (and off again) through the session's object-flag change over
            // the data's; one the data stores on keeps the toggle key.
            if (Row(request.Object) is { Type: NpcType } toggledNpc && ((Number(toggledNpc, ObjectFlagsSlot) ?? 0) & OffObjectFlag) != 0)
            {
                var data = Number(toggledNpc, ObjectFlagsSlot) ?? 0;
                var flipped = _session.WriteObjectFlag(_session.Version, request.Object.Code, SessionObjectFlagField.Object, OffObjectFlag, !NpcOff(_session, request.Object.Map, request.Object.Code, data), dataHas: true);
                if (flipped.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            var key = MapChangeKey.ForEntity(request.Object.Code, OffProperty);
            var toggled = _session.ApplyChanges(_session.Version, null,
                [new(new SessionMapId(request.Object.Map), key, SwitchedOff(_session, request.Object.Map, request.Object.Code) ? null : 1)]);
            if (toggled.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-SCRIPT-046: the slots a called script's dialog action stored (an object's use slot), and (ARC-SCRIPT-047) the slots call-ex
        // runs changed, as map changes.
        foreach (var stored in effects.SlotWrites)
            if (stored.Attachee is not { } owner || !WriteObjectSlot(stored, owner)) return false;
        // ARC-SCRIPT-1481: the item in use that the run destroyed keeps no slot (a counter raised after the destroy would be an orphan map change).
        if (_itemUse is { } gone && _session.IsItemDestroyed(gone.Code)) return true;
        return effects.Writeback is not { } writeback || writeSlot(writeback);
    }

    /// <summary>ARC-SCRIPT-1164 (WO1): what the session answered to the last create item a run applied (smoke and test seam; null before any).</summary>
    public InventoryMutationResult? LastCreatedItem { get; private set; }

    /// <summary>ARC-SCRIPT-1164 (WO1): the insert check the created item's holder answers (the viewer's inventory host's); null: every item is taken.</summary>
    public SessionInsertCheck? InsertCheck { get; init; }

    /// <summary>ARC-MAGIC-1823 (ledger IE3, IE4, IE6): the shown map's item script host; with it a script's transfer item from the hero to an NPC or container runs the item's wield-off, remove-item and insert-item scripts as the hero's give does
    /// (<see cref="GameSessionOwner.TransferNamedItem"/>), and (ARC-MAGIC-2252) so does the take from an NPC or container (the hero's transfer script, remove item, insert item); the plan makes a dry run of the move through it. Null: such an item is refused when the run is planned.</summary>
    public ISessionItemScriptHost? ItemScripts { get; init; }

    // ARC-SCRIPT-1164 (E5-b): set lock state (53, 0x441DD0), toggle open or closed (88, 0x4EEF80 and the container's 0x4EEF20, 0x4EEEC0) and create item (66) on what ObjectStateRefusal admitted. False when the session refuses.
    private bool ApplyWorldObject(ScrObjectStateRequest request)
    {
        if (request.Change is ScrObjectStateChange.TransferItem or ScrObjectStateChange.DestroyItemNamed or ScrObjectStateChange.SetWrittenStart or ScrObjectStateChange.ToggleItemInventoryDisplay)
            return ApplyItemAction(request);
        if (request.Change == ScrObjectStateChange.CreateItem)
        {
            var item = _world!.Item(request.Value)!;
            _session.AttachItemWeights(number => _world.Item(number - AreaPrototypeItems.PrototypeOffset)?.Weight);
            _session.AttachItemScripts(number => _world.Item(number - AreaPrototypeItems.PrototypeOffset)?.Scripts);
            _session.AttachItemNoDrop(number => _world.Item(number - AreaPrototypeItems.PrototypeOffset)?.NoDrop == true);
            var check = InsertCheck ?? AreaInventoryHost.DiagnosticInsertCheck;
            var created = IsPc(request.Object) ? _session.CreateItemForHero(_session.Version, item.Kind, check) : _session.AddItemToNpc(_session.Version, request.Object.Code, item.Kind, check);
            LastCreatedItem = created;
            // A full list or an insert check that says no leaves the item not taken (the hero's path drops it to the ground); the run's other effects stay applied, so this is no refusal of the run (review of ARC-SCRIPT-1164).
            return created.Disposition != GameSessionDisposition.Rejected || created.Failure is GameSessionFailure.BudgetExceeded or GameSessionFailure.InsertRefused;
        }
        // Any other object type (and the PC) does nothing, as in the original (WO3, WO4).
        if (IsPc(request.Object) || Row(request.Object) is not { Type: PortalObjectType or ContainerObjectType } || OpenableOf(request.Object) is not { } facts) return true;
        if (request.Change == ScrObjectStateChange.SetLockState)
        {
            // WO3: the value 1 locks and 0 unlocks, both as a key or script does (the Lock event an hour after unlocking a locked object); any other value clears the locked bit with a plain write, no event (0x441DD0).
            var locked = request.Value == 1 ? _session.SetOpenableLocked(_session.Version, facts.Openable, facts.Baseline, true)
                : request.Value == 0 ? _session.SetOpenableLocked(_session.Version, facts.Openable, facts.Baseline, false)
                : _session.WriteOpenableFlag(_session.Version, facts.Openable, facts.Baseline, Arcanum.Simulation.Locks.LockRules.LockedBit, false);
            return locked.Disposition != GameSessionDisposition.Rejected;
        }
        // WO4: a destroyed object is left as it is; a portal goes to the frame its steps end on (LK10), a container opens to its next frame or closes to 0.
        if (AlreadyDestroyed(request.Object)) return true;
        var frame = _session.QueryOpenable(facts.Openable, facts.Baseline).Frame;
        var target = facts.ToggledFrame(frame);
        return target == frame || _session.SetOpenableFrame(_session.Version, facts.Openable, facts.Baseline, target).Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>ARC-SCRIPT-1279 (WO2, WO7): what the session answered to the last transfer item or destroy item named a run applied (smoke and test seam; null before any).</summary>
    public InventoryMutationResult? LastNamedItem { get; private set; }

    // The holder the session's named-item finder takes for an object of a run: the hero for the PC, the object's own code for any other (the session answers nothing for one no attached map lists as a critter or container, as the finder 0x462480 does).
    private string? HolderOf(ScrObjectRef holder) => IsPc(holder) ? null : holder.Code;

    // ARC-SCRIPT-1279 (WO6, WO8): the item of the object operand a field action writes: a row of the loaded maps whose type is an item (5..14), a written one (13) for set written start; null when the action does nothing (the PC, another type, an item
    // already deleted), as in the original.
    private string? ItemFieldTarget(ScrObjectRef obj, bool writtenOnly)
    {
        if (IsPc(obj) || Row(obj) is not { } row || row.Type is < FirstItemType or > LastItemType || writtenOnly && row.Type != WrittenItemType || _session.IsItemDestroyed(obj.Code)) return null;
        return obj.Code;
    }

    // ARC-SCRIPT-1279: transfer item (41), destroy item named (126), set written start (94) and toggle item inventory display (127) on what ObjectStateRefusal admitted. False when the session refuses.
    private bool ApplyItemAction(ScrObjectStateRequest request)
    {
        switch (request.Change)
        {
            case ScrObjectStateChange.DestroyItemNamed:
            {
                var destroyed = _session.DestroyNamedItem(_session.Version, HolderOf(request.Object), request.Value);
                LastNamedItem = destroyed;
                return destroyed.Disposition != GameSessionDisposition.Rejected;
            }
            case ScrObjectStateChange.TransferItem:
            {
                var moved = _session.TransferNamedItem(_session.Version, HolderOf(request.Object), HolderOf(request.Other!.Value), request.Value, InsertCheck ?? AreaInventoryHost.DiagnosticInsertCheck, ItemScripts);
                LastNamedItem = moved;
                // A holder's insert check that says no, or a full list, leaves the item where it was and the run goes on (0x461810 returns without moving it). The session's own budgets (its NPC item total, ammunition and durability records, a 32-bit sum) answer the same way: the item stays and the run goes on (ledger WO17).
                return moved.Disposition != GameSessionDisposition.Rejected || moved.Failure is GameSessionFailure.BudgetExceeded or GameSessionFailure.InsertRefused;
            }
            case ScrObjectStateChange.SetWrittenStart:
                return ItemFieldTarget(request.Object, writtenOnly: true) is not { } written || _session.SetItemWrittenStart(_session.Version, written, request.Value).Disposition != GameSessionDisposition.Rejected;
            default:
                return ItemFieldTarget(request.Object, writtenOnly: false) is not { } shown || _session.ToggleItemDisplay(_session.Version, shown).Disposition != GameSessionDisposition.Rejected;
        }
    }

    /// <summary>ARC-DEV-231: what the AI host answered to the last walk to or run to a run applied (smoke and test seam; null before any).</summary>
    public string? LastWalk { get; private set; }

    /// <summary>ARC-DEV-233: what the AI host answered to the last perform animation a run applied (smoke and test seam; null before any).</summary>
    public string? LastAnimation { get; private set; }

    /// <summary>ARC-DEV-234: what the AI host answered to the last try to steal 100 coins a run applied (smoke and test seam; null before any).</summary>
    public string? LastSteal { get; private set; }

    /// <summary>ARC-DEV-243: what the viewer answered to the last script damage a run applied (smoke and test seam; null before any or when nothing was applied).</summary>
    public string? LastDamage { get; private set; }

    /// <summary>ARC-DEV-243: what the viewer answered to the last script heal of hit points a run applied (smoke and test seam).</summary>
    public string? LastHeal { get; private set; }

    /// <summary>ARC-DEV-241: what the AI host answered to the last script cast a run applied (smoke and test seam; null before any).</summary>
    public string? LastCast { get; private set; }

    /// <summary>ARC-SCRIPT-1481: what the life step answered to the last heal of fatigue (the PC's) or poison a run applied (test and smoke seam; null before any).</summary>
    public string? LastHealLife { get; private set; }

    /// <summary>ARC-DEV-236: what the AI host answered to the last become prone a run applied (smoke and test seam; null before any).</summary>
    public string? LastProne { get; private set; }

    /// <summary>ARC-SCRIPT-1294: what the viewer answered to the last script stop spell a run applied (smoke and test seam; null before any).</summary>
    public string? LastStopSpell { get; private set; }

    /// <summary>ARC-SCRIPT-1163 (NS2): what the AI host answered for the last reaction change a run applied (null: none yet).</summary>
    public string? LastReaction { get; private set; }

    /// <summary>ARC-SCRIPT-1620 (TI6): what the AI host answered to the last script attack a run applied (the stop of the attack event's plan; smoke and test seam; null before any or when nothing was raised).</summary>
    public string? LastAttack { get; private set; }

    /// <summary>ARC-SCRIPT-1620: whether the world gives the script attack (<see cref="ApplyScriptAttack"/>) this host applies; the dialog host asks before it applies anything, so a run it cannot finish is refused whole.</summary>
    public bool CanApplyScriptAttack => _world?.ScriptAttack is not null;

    /// <summary>
    /// ARC-SCRIPT-1620 (TI6, VB2-2 gap d), ARC-SCRIPT-1679: why one attack (36) request of a run cannot be applied here, null when it can. The attacker (<see cref="ScrObjectStateRequest.Other"/>) is an object the AI host holds: the PC; an NPC standing on
    /// the shown map, a carried follower named by its home map included (the original keeps one object wherever it stands, FM1, so it is told apart by <see cref="AreaScriptWorld.CritterCell"/> and not by the map of its record); or an object of the shown map
    /// that is no critter and no item (a wall, portal, container, scenery or trap object: 0x447D2F gives 0x4A9650 no type test of either side, and an NPC victim of such an attacker is asked into COMBAT_FOCUS on it, 0x4AABE0, through the AI host's object facts,
    /// #1619; a victim of another kind does nothing more). Not modelled: an item as the attacker (the AI host describes no item), an object of no loaded row, and an object of another map that no companion stands for.
    /// </summary>
    public string? AttackRefusal(ScrObjectStateRequest request)
    {
        if (request.Other is not { } attacker) return "attack has no attacker";
        if (IsPc(attacker) || StandsHere(attacker)) return null;
        var row = attacker.Map == _shownMap ? Row(attacker) : null;
        if (row is { } && row.Type is not (PcType or NpcType) && row.Type is < FirstItemType or > LastItemType) return null;
        var what = attacker.Map != _shownMap ? "an object of another map that no companion stands for" : row is null ? "an object of no loaded row" : row.Type is >= FirstItemType and <= LastItemType ? "an item" : "an NPC row that is not standing here";
        return $"attack by {attacker.Code} is not modelled ({what} as the attacker; only the PC, an NPC standing on the shown map or an object of it that is no item)";
    }

    /// <summary>
    /// One attack (36) request of a run, applied (<see cref="AttackRefusal"/> said it can be): the attack event (0x4A9650, flags 2, loudness 1) of the attacker on the victim in the AI host. A victim that is neither the PC nor an NPC standing here (a carried
    /// follower counts by identity, <see cref="StandsHere"/>) is left as it is (the host holds no such object: the event's early exit); the answer is the stop of the event's plan. Shared by the dialog host (<see cref="AreaScriptHost.Apply"/>).
    /// </summary>
    public void ApplyScriptAttack(ScrObjectStateRequest request)
    {
        LastAttack = null;
        if (request.Other is { } attacker && (IsPc(request.Object) || StandsHere(request.Object))) LastAttack = _world!.ScriptAttack!(attacker.Code, request.Object.Code);
    }

    /// <summary>
    /// ARC-SCRIPT-1842 (#1322, TI6): why one make follow (21) request of a run cannot be applied here, null when it can. The original's join (0x45DE90, force 1) tests only that the follower is an NPC, and the leader may be any object; here the follower is
    /// the PC (the join does nothing for it), or an NPC standing on the shown map (a carried follower by identity, <see cref="StandsHere"/>), and the leader the PC or such an NPC. Not modelled: an NPC of another map that no companion stands for, and a leader
    /// that is neither (an object of the shown map that is no critter: the session would keep it as a leader no host describes).
    /// </summary>
    public string? MakeFollowRefusal(ScrObjectStateRequest request)
    {
        if (request.Other is not { } leader) return "make follow has no leader";
        if (IsPc(request.Object)) return null;
        if (!StandsHere(request.Object)) return $"make follow of {request.Object.Code} is not modelled (only an NPC standing on the shown map follows)";
        if (!IsPc(leader) && !StandsHere(leader)) return $"make follow under {leader.Code} is not modelled (only the PC or an NPC standing on the shown map leads)";
        return null;
    }

    /// <summary>
    /// ARC-SCRIPT-1842 (#1322, TI6): one make follow (21) request of a run, applied (<see cref="MakeFollowRefusal"/> said it can be): the session's forced join under the leader (<see cref="GameSessionOwner.ForceJoinLeader"/>, 0x45DE90 with force 1): the
    /// follower leaves any leader it had, the hero's party or an NPC's array, and joins the leader's array at its end. The PC as the follower does nothing (the original's NPC test). A join the session refuses as a follower of itself or a cycle of leaders
    /// (a project choice: the original would write the cycle), or over the session's bounds (the hero's companion limit, 256 followers of an NPC, the written-flags bound: the original's forced join has no limit), joins nothing and leaves
    /// the run going on; any other refusal (a stale version) stops it. The AI host sees the new leader on its next tick.
    /// </summary>
    public bool ApplyMakeFollow(ScrObjectStateRequest request)
    {
        if (IsPc(request.Object) || request.Other is not { } leader) return true;
        var joined = _session.ForceJoinLeader(_session.Version, request.Object.Code, IsPc(leader) ? AreaScriptHost.PcCode : leader.Code);
        return joined.Disposition != GameSessionDisposition.Rejected || joined.Failure is GameSessionFailure.InvalidCompanion or GameSessionFailure.BudgetExceeded;
    }

    /// <summary>ARC-SCRIPT-1168: whether the world gives the script cast (<see cref="ApplyScriptCast"/>) this host applies; the dialog host asks before it applies anything, so a run it cannot finish is refused whole.</summary>
    public bool CanApplyScriptCast => _world?.ScriptCast is not null;

    /// <summary>
    /// One cast request of a run (the six cast actions 48, 61, 118, 121, 122, 136), applied (ARC-SCRIPT-1168: shared by the dialog host, <see cref="AreaScriptHost.Apply"/>). GT183/GT184: the target an NPC of the shown map or the PC; the caster an NPC of the
    /// shown map, the PC (ARC-DEV-244: the hero's casting goal) or none (a missing caster handle, 61 and 136). Another map's object and a non-NPC caster are left as they are.
    /// </summary>
    public void ApplyScriptCast(ScrObjectStateRequest request)
    {
        LastCast = null;
        var free = (request.Value2 & (int)ScrCastFlags.Free) != 0;
        var unresistable = (request.Value2 & (int)ScrCastFlags.Unresistable) != 0;
        if ((IsPc(request.Object) || ShownNpc(request.Object)) && (request.Other is not { } by || IsPc(by) || ShownNpc(by)))
            LastCast = _world!.ScriptCast!(request.Other is { } caster ? IsPc(caster) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : caster.Code : null, IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value, free, unresistable);
    }

    // ARC-SCRIPT-1679 (FM1): an NPC standing on the shown map, told by identity: a row of the shown map, or the row of a carried follower named by its home map whose live cell the viewer gives here (the tile script's test of the same).
    private bool StandsHere(ScrObjectRef obj) => ShownNpc(obj) || obj.Map != _shownMap && !IsPc(obj) && Row(obj) is { Type: NpcType } && _world?.CritterCell?.Invoke(obj) is not null;

    /// <summary>ARC-SCRIPT-1563: whether the world gives the script damage (<see cref="ApplyScriptDamage"/>) this host applies; the dialog host asks before it applies anything, so a run it cannot finish is refused whole.</summary>
    public bool CanApplyScriptDamage => _world?.ScriptDamage is not null;

    /// <summary>ARC-SCRIPT-1563: whether the world gives the script heal (<see cref="ApplyScriptHeal"/>) this host applies.</summary>
    public bool CanApplyScriptHeal => _world?.ScriptHeal is not null;

    /// <summary>
    /// One damage (60) or apply unresistable damage (138) request of a run, applied (ARC-SCRIPT-1563: shared by the dialog host, <see cref="AreaScriptHost.Apply"/>). GT194: an NPC of the shown map, the PC, or (ARC-DEV-255, GT195) a door, window, container, scenery or trap
    /// of the shown map; any other object is left as it is, and a destroyed object takes none (object flags 0x400001, GT195). The victim's taking-damage script is asked (GT198) through the world's damage; a destroyed object is asked all the same.
    /// </summary>
    public void ApplyScriptDamage(ScrObjectStateRequest request)
    {
        LastDamage = null;
        if (IsPc(request.Object) || (ShownNpc(request.Object) || ShownDamageable(request.Object)) && !AlreadyDestroyed(request.Object))
            LastDamage = _world!.ScriptDamage!(IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value, request.Value2,
                request.Change == ScrObjectStateChange.DamageUnresistable);
        // ARC-DEV-261 (GT198): the taking-damage script comes before the destroyed gate, so a destroyed object is asked all the same (its answer cannot change the outcome: it takes none).
        else if (ShownNpc(request.Object) || ShownDamageable(request.Object)) _world!.ScriptDamageAsk?.Invoke(request.Object.Code);
    }

    /// <summary>One heal hit points (75) request of a run, applied (GT196: a living PC or NPC of the shown map; any other object does nothing in the original). ARC-SCRIPT-1563: shared by the dialog host.</summary>
    public void ApplyScriptHeal(ScrObjectStateRequest request)
    {
        LastHeal = null;
        if (IsPc(request.Object) || ShownNpc(request.Object) && !AlreadyDestroyed(request.Object))
            LastHeal = _world!.ScriptHeal!(IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value);
    }

    // ARC-SCRIPT-1735 (E5-a tail, ledger NS5, NS9), ARC-SCRIPT-1779, ARC-SCRIPT-1778 (door and container rows of the change art number ledger): change art number (59) writes an object's art id (field 1). The plan of one request: what the run must
    // refuse (an object the viewer cannot draw with another art), no change (the PC and the critter classes, a destroyed object, an id no ART file names, an id equal to the current one), or the art id to keep. Scenery of a loaded map, items with a
    // loaded row, and a door or window and a container of the shown map; the rest is refused so the run is left unapplied before anything applies. A door or window and a container hold their open state in the art id's frame (ledger LK10, LW2),
    // which the action resets to 0 with the new id: the object is closed after it, whatever its frame was and also when the id stays, and its lock fields stay.
    private const int ArtSlot = 0;

    private readonly record struct ArtChange(string? Refusal, uint? Art, uint Baseline, AreaScriptOpenable? Openable = null);

    private ArtChange ArtPlan(ScrObjectRef obj, int number)
    {
        // NS5: the PC's art is a critter class, whose number setter answers the id 0 (no ART file of this project's reading): nothing changes.
        if (IsPc(obj)) return default;
        // 0x43EB30: an object destroyed (object flag 0x1, or by a script) is left as it is, whatever its type.
        if (AlreadyDestroyed(obj)) return default;
        // ARC-SCRIPT-1826 (AN18): an item the session deleted (its lists no longer name it, so no row or prototype is found for it) is a destroyed object too: left as it is, the run goes on.
        if (_session.IsItemDestroyed(obj.Code)) return default;
        // ARC-SCRIPT-1826 (AN18): the object's row on a map the host holds; else, for an item, the row of its origin map (a map-data item carried here) or its prototype's art (a made or script-created item).
        int type; long? stored;
        if (Row(obj) is { } row) (type, stored) = (row.Type, Number(row, ArtSlot));
        else if (CarriedItemRow(obj) is { } carried)
        {
            // A carried item destroyed in its map data (object flag 0x1) is left as it is, like any destroyed object (AN5).
            if (((Number(carried, ObjectFlagsSlot) ?? 0) & DestroyedObjectFlag) != 0) return default;
            (type, stored) = (carried.Type, Number(carried, ArtSlot));
        }
        else if (MadeItemArt(obj) is { } made) (type, stored) = made;
        else return new($"change art number of {obj.Code} is not modelled (no loaded row)", null, 0);
        if (stored is not { } storedArt) return new($"change art number of {obj.Code} is not modelled (the row names no art)", null, 0);
        var baseline = unchecked((uint)storedArt);
        if (type == NpcType)
            // A critter class (2 critter, 12 monster) answers the id 0: nothing changes. A unique NPC's art (13) takes a number, but the viewer draws an NPC from its body, not from one art id: not modelled.
            return ObjectArtNumber.IsCritterClass(baseline) ? default : new($"change art number of {obj.Code} is not modelled (an NPC of art class {ObjectArtNumber.Class(baseline)})", null, baseline);
        var portal = type == Arcanum.Data.Combat.CombatFieldReader.PortalType; var container = type == Arcanum.Data.Combat.CombatFieldReader.ContainerType;
        // ARC-SCRIPT-1779 (AN10): an item (types 5..14) takes the number in its own range (0x502830) and keeps the ground art as object field 1; its inventory art (field 0x5D) and its paper-doll art are other fields the action does not write.
        var isItem = type is >= FirstItemType and <= LastItemType;
        if (type != SceneryType && !isItem && !portal && !container)
            return new($"change art number of {obj.Code} is not modelled (only scenery, items, a door, a window and a container take a new art, not an object of type {type})", null, baseline);
        if (isItem && _session.IsItemDestroyed(obj.Code)) return default;
        var expected = portal ? 3 : container ? 7 : isItem ? 6 : 4;
        var current = _session.QueryObjectArt(new SessionMapId(obj.Map), obj.Code) ?? baseline;
        if (ObjectArtNumber.Class(current) != expected)
            return new($"change art number of {obj.Code} is not modelled (art class {ObjectArtNumber.Class(current)})", null, baseline);
        var next = ObjectArtNumber.Changed(current, number, out _);
        if (_world?.ArtExists is not { } exists) return new($"change art number of {obj.Code} needs the module's art index", null, baseline);
        // 0x502220: the new id is written only when its ART file exists.
        if (!exists(next)) return new(null, null, baseline);
        if (!portal && !container)
        {
            // An id equal to the current one (a number out of range keeps it) writes nothing: no record moves and no revision.
            return next == current ? new(null, null, baseline) : new(null, next, baseline);
        }
        // The openable's lock identity and T0 baseline (the shown map's door or container); the frame reset needs them, and it runs also when the id stays.
        return OpenableOf(obj) is { } openable ? new(null, next, baseline, openable) : new($"change art number of {obj.Code} is not modelled (the object is not a door or container of the shown map)", null, baseline);
    }

    private ArtChange ArtPlan(ScrObjectStateRequest request) => ArtPlan(request.Object, request.Value);

    // ARC-SCRIPT-1826 (AN18): the map the session names as the origin of an item it moved (the hero's taken item, an item of the hero lying on the ground, an item the hero gave away), or null.
    private SessionMapId? ItemOrigin(string entity)
    {
        foreach (var taken in _session.QueryPcInventory()) if (taken.Entity == entity) return taken.Origin;
        foreach (var placed in _session.QueryPlacedItems()) if (placed.Entity == entity) return placed.Origin;
        foreach (var given in _session.QueryGivenItems()) if (given.Entity == entity) return given.Origin;
        return null;
    }

    // ARC-SCRIPT-1826 (AN18): the row of a map-data item that is not a row of a map the host holds under the map the script names (the shown map): carried here from another map, so its row is read by identity from the origin map the session names
    // (the same lookup an item's use script makes, <see cref="OtherMapRow"/>). Null for an object that is no item the session moved, and for an item whose row no map reads.
    private AreaObjectRecord? CarriedItemRow(ScrObjectRef obj)
    {
        if (OtherMapRow is not { } read || ItemOrigin(obj.Code) is not { } origin) return null;
        return read(new(origin.RuntimeMap, obj.Code)) is { Type: >= FirstItemType and <= LastItemType } row ? row : null;
    }

    // ARC-SCRIPT-1826 (AN18): a made or script-created item (made:n) owns no row: the original makes it as a copy of its prototype, so its art is the prototype's current art (the world's prototype reader, as the viewer draws a created item). The hero's
    // starting kit items (kit:n) are not read this way (the viewer draws their art from the kit load): null, and the run is refused.
    private (int Type, long? Art)? MadeItemArt(ScrObjectRef obj)
    {
        if (GameSessionOwner.ObjectOriginOf(obj.Code) != SessionObjectOrigin.Made || _session.QueryPrototypeItemKind(obj.Code) is not { } kind) return null;
        return _world?.Item(kind.Prototype - AreaPrototypeItems.PrototypeOffset) is { } prototype ? ((int)kind.Type, prototype.Art) : null;
    }

    // The change of one request, written as the object's art id (the data's id returns the object to its baseline) and, for a door, window or container, the closed frame (AD1). False when the session refuses.
    private bool ApplyArt(ScrObjectStateRequest request)
    {
        var plan = ArtPlan(request);
        if (plan.Art is not { } art) return true;
        var set = _session.SetObjectArt(_session.Version, new SessionMapId(request.Object.Map), request.Object.Code, art, plan.Baseline);
        if (set.Disposition == GameSessionDisposition.Rejected) return false;
        if (plan.Openable is not { } openable) return true;
        return _session.SetOpenableFrame(_session.Version, openable.Openable, openable.Baseline, 0).Disposition != GameSessionDisposition.Rejected;
    }

    // ARC-DEV-233: an NPC of the shown map, the only target the goal-starting actions (62, 69, 74, 76) are applied to; the PC, another map's object and a non-NPC are left as they are.
    private bool ShownNpc(ScrObjectRef obj) => obj.Map == _shownMap && !IsPc(obj) && Row(obj) is { Type: NpcType };

    // ARC-DEV-255 (GT195): a portal, container, scenery or trap of the shown map, the objects a script's damage reaches besides the PC and an NPC.
    private bool ShownDamageable(ScrObjectRef obj) => obj.Map == _shownMap && !IsPc(obj) && Row(obj) is { Type: Arcanum.Data.Combat.CombatFieldReader.PortalType or Arcanum.Data.Combat.CombatFieldReader.ContainerType
        or Arcanum.Data.Combat.CombatFieldReader.SceneryType or Arcanum.Data.Combat.CombatFieldReader.TrapType };

    // ARC-SCRIPT-052 (TD2): destroyed in its map data (object flag 0x1) or by a script (DestroyedProperty).
    private bool AlreadyDestroyed(ScrObjectRef obj) =>
        Row(obj) is { } row && ((Number(row, ObjectFlagsSlot) ?? 0) & DestroyedObjectFlag) != 0 || Destroyed(_session, obj.Map, obj.Code);

    // ARC-SCRIPT-052 (TD3): the NPCs a stop attacking of the object reaches (the PC's whole follower tree for the PC (0x4AA4A0, ARC-WORLD-2119)) with the world's facts for each.
    private Dictionary<string, SessionPeaceFacts> StopFacts(ScrObjectRef obj)
    {
        var npcs = IsPc(obj) ? _session.QueryFollowerTree(AreaScriptHost.PcCode).ToArray() : [obj.Code];
        return npcs.ToDictionary(npc => npc, npc => _world?.PeaceFacts?.Invoke(npc) ?? new SessionPeaceFacts(), StringComparer.Ordinal);
    }

    // The session refuses a stop (InvalidDangerFacts) for an NPC with a focus on the hero's side and no reaction facts, or with a focus while in
    // combat mode and no combat-mode facts: such a run is left unapplied before anything applies.
    private string? StopRefusal(ScrObjectRef obj)
    {
        foreach (var (npc, facts) in StopFacts(obj))
        {
            if (_session.QueryDanger(npc).Focus is not { } focus) continue;
            if ((focus.IsHero || _session.QueryPcLeader(focus.Entity!) is not null) && facts.Reaction is null)
                return $"stop attacking of {npc}, which fights the hero's side, needs its reaction facts";
            if (_session.QueryCombatMode(SessionCombatant.Critter(npc)) && facts.CombatMode?.Facts is null)
                return $"stop attacking of {npc}, in combat mode, needs its combat-mode facts";
        }
        return null;
    }

    // ARC-SCRIPT-052 (TD3): the session's stop attacking for the PC (each follower) or an NPC, with the world's facts for each NPC it reaches,
    // then the host's part of each step.
    private bool StopAttacking(ScrObjectRef obj)
    {
        var hero = IsPc(obj);
        var facts = StopFacts(obj);
        var stop = _session.StopAttacking(_session.Version, new SessionStopAttackingRequest
            { Combatant = hero ? SessionCombatant.Hero : SessionCombatant.Critter(obj.Code), Facts = facts });
        if (stop.Disposition == GameSessionDisposition.Rejected) return false;
        foreach (var step in stop.Steps) _world?.Stopped?.Invoke(step);
        return true;
    }

    // ARC-WORLD-114: the first toggle state or kill of the run the viewer cannot apply, named; null when every one can be. Both act on an NPC
    // of the shown map only; a kill needs the world's kill (ARC-SCRIPT-1360: an NPC its map data has off is switched on by a toggle, see NpcOff). ARC-SCRIPT-052: destroy takes an NPC of the shown map out of the world unless it follows the PC (the party removal is not
    // read) or has a destroy-point script (the destroy event is audit slice 4); stop attacking acts on the PC or an NPC of the shown map.
    private string? ObjectStateRefusal(ScrEffects effects)
    {
        // ARC-SCRIPT-1164 (WO5): a sector with a negative number, or an x beyond the 26 packed bits, is not kept in the session's keys.
        foreach (var change in effects.WorldChanges)
            if (change.Change == ScrWorldChange.SectorBlocked && SessionSectorBlock.Pack(change.A, change.B) is null)
                return $"toggle sector blocked of ({change.A},{change.B}) is not modelled (a negative sector or an x beyond 26 bits)";
        // ARC-SCRIPT-1279: the (holder, name) pairs earlier item actions of the run touched: each plan below is read against the state before the run, so an action that meets a pair an earlier one changed would be planned on a stale list.
        var touched = new HashSet<(string? Holder, int Name)>();
        foreach (var request in effects.ObjectStates)
        {
            if (request.Change == ScrObjectStateChange.StopAttacking)
            {
                if (!IsPc(request.Object) && (request.Object.Map != _shownMap || Row(request.Object) is not { Type: NpcType }))
                    return $"stop attacking of {request.Object.Code} is not modelled (only the PC or an NPC of the shown map)";
                if (StopRefusal(request.Object) is { } refused) return refused;
                continue;
            }
            if (request.Change == ScrObjectStateChange.Destroy)
            {
                // ARC-SCRIPT-1481 (VT6): the item whose use script runs destroys itself (the food use script 30282 ends by it): the PC's food or scroll leaves its list through the session.
                if (_itemUse is { } used && used.Equals(request.Object))
                {
                    if (_session.IsItemDestroyed(used.Code)) continue;
                    if (_session.UsedItemDestructionRefusal(used.Code) is { } why) return $"destroy of the item in use {used.Code} is refused by the session ({why})";
                    continue;
                }
                if (request.Object.Map != _shownMap || IsPc(request.Object) || Row(request.Object) is not { Type: NpcType } npc)
                    return $"destroy of {request.Object.Code} is not modelled (only an NPC of the shown map)";
                // TD2: one destroyed in its map data, or by an earlier run, is left as it is.
                if (AlreadyDestroyed(request.Object)) continue;
                if (FollowsPc(request.Object)) return $"destroy of {request.Object.Code}, a follower of the PC, is not modelled";
                // ARC-SCRIPT-1842: the map change does not reach the session's destroy (which unlinks, 0x45E180), so a leader or follower of an NPC is left as it is.
                if (_session.QueryLeader(request.Object.Code) is not null || _session.QueryFollowers(request.Object.Code).Count > 0)
                    return $"destroy of {request.Object.Code}, which leads or follows an NPC, is not modelled";
                if (LoadedSlot(npc, ScrPoint.Destroy) is { Script: not 0 } own) return $"destroy of {request.Object.Code} runs its destroy script {own.Script}, which is not modelled";
                continue;
            }
            if (request.Change == ScrObjectStateChange.PerformAnimation)
            {
                if (_world?.ScriptAnimate is null) return "perform animation needs the AI host";
                continue;
            }
            if (request.Change is ScrObjectStateChange.Damage or ScrObjectStateChange.DamageUnresistable)
            {
                if (_world?.ScriptDamage is null) return "damage needs the viewer's session";
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealHitPoints)
            {
                if (_world?.ScriptHeal is null) return "heal hit points needs the viewer's session";
                continue;
            }
            // ARC-SCRIPT-1620 (TI6, VB2-2 gap d), ARC-SCRIPT-1679: the attacker (the first object of operand 1) is the PC, an NPC standing here (a carried follower by identity) or an object of the shown map that is no item (the AI host's object facts, #1619);
            // an item or an object it does not hold is refused whole. The victim needs no test: the event's own early exits leave an object the host does not hold as it is.
            if (request.Change == ScrObjectStateChange.Attack)
            {
                if (_world?.ScriptAttack is null) return "attack needs the AI host";
                if (AttackRefusal(request) is { } why) return why;
                continue;
            }
            if (request.Change == ScrObjectStateChange.MakeFollow)
            {
                if (MakeFollowRefusal(request) is { } why) return why;
                continue;
            }
            // ARC-MAGIC-030 (SC5): give spell mastery acts on the PC and on an NPC of the shown map whose sheet its map data gives; a PC or NPC row of another map, or an object of no loaded
            // row, is not modelled (the original would write it), and any other object type does nothing, as in the original.
            if (request.Change == ScrObjectStateChange.GiveSpellMastery)
            {
                if (IsPc(request.Object)) continue;
                if (Row(request.Object) is not { } masteryRow) return $"give spell mastery of {request.Object.Code} is not modelled (no loaded row)";
                if (masteryRow.Type is not (NpcType or PcType)) continue;
                if (masteryRow.Type == PcType || request.Object.Map != _shownMap) return $"give spell mastery of {request.Object.Code} is not modelled (only the PC or an NPC of the shown map)";
                if (AdvancementOf(request.Object) is null) return $"give spell mastery of {request.Object.Code} needs its sheet, which its map data does not give";
                continue;
            }
            if (request.Change == ScrObjectStateChange.CastSpell)
            {
                if (_world?.ScriptCast is null) return "cast spell needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.StopSpell)
            {
                if (_world?.ScriptStopSpell is null) return "stop spell needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.BecomeProne)
            {
                if (_world?.ScriptProne is null) return "become prone needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.StealCoins)
            {
                if (_world?.ScriptSteal is null) return "steal needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealFatigue)
            {
                if (IsPc(request.Object) ? _world?.ScriptHealLife is null : _world?.HealFatigue is null) return "heal fatigue needs the session's life step";
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealPoison)
            {
                // VT3: a negative amount raises the level in the original (the level minus the amount); that is not modelled.
                if (_world?.ScriptHealLife is null) return "heal poison needs the session's life step";
                if (request.Value < 0) return "heal poison of a negative amount raises the poison level, which is not modelled";
                continue;
            }
            if (request.Change is ScrObjectStateChange.WalkTo or ScrObjectStateChange.RunTo)
            {
                // ARC-DEV-231: the walk and the run of an NPC of the shown map that the AI host drives; any other target is left as it is (ShownNpc), the run's other effects apply.
                var walkName = request.Change == ScrObjectStateChange.WalkTo ? "walk to" : "run to";
                if (_world?.ScriptWalk is null) return $"{walkName} needs the AI host";
                continue;
            }
            // ARC-SCRIPT-1163 (E5-a, NS1–NS3): disband acts through the session's party and needs nothing of the viewer; the reactions need the AI host's facts of an NPC of the shown map; the standpoints are written for an NPC of the shown
            // map (any other object does nothing, as in the original); change art number has no host yet.
            if (request.Change == ScrObjectStateChange.Disband) continue;
            if (request.Change is ScrObjectStateChange.SetReaction or ScrObjectStateChange.AdjustReaction)
            {
                if (_world?.ScriptReaction is null) return "a reaction change needs the AI host";
                continue;
            }
            if (request.Change is ScrObjectStateChange.SetDayStandpoint or ScrObjectStateChange.SetNightStandpoint)
            {
                // The extended actions' teleport map is the catalogue map less 4,999; the session holds maps from 1 (the original stores any value), so a lower one is left unapplied before anything applies.
                if (ShownNpc(request.Object) && request.Map is { } catalogueMap && unchecked(catalogueMap - 4999) < 1)
                    return $"set standpoint of {request.Object.Code} names the map {catalogueMap}, which the session cannot keep as a teleport map";
                continue;
            }
            if (request.Change == ScrObjectStateChange.ChangeArtNumber)
            {
                if (ArtPlan(request) is { Refusal: { } artRefusal }) return artRefusal;
                continue;
            }
            // ARC-SCRIPT-049 (HW4): set faction changes an NPC and leaves any other object as it is; an NPC of another map is not kept.
            if (request.Change == ScrObjectStateChange.SetFaction)
            {
                // An object of no loaded row may be an NPC as well: not modelled either.
                if (!IsPc(request.Object) && Row(request.Object) is null or { Type: NpcType } && request.Object.Map != _shownMap)
                    return $"set faction of {request.Object.Code} is not modelled (only an NPC of the shown map)";
                if (!IsPc(request.Object) && Row(request.Object) is null)
                    return $"set faction of {request.Object.Code} is not modelled (no loaded row)";
                continue;
            }
            // ARC-SCRIPT-1772 (CQ4): toggle invulnerability writes the object flags (field 0x13), which the session keeps for the hero and a mobile NPC; any other object type is not modelled (its flags are the map data's).
            if (request.Change == ScrObjectStateChange.ToggleInvulnerability)
            {
                if (IsPc(request.Object)) { if (_session.QueryHero() is null) return "toggle invulnerability of the PC needs a hero"; continue; }
                if (Row(request.Object) is not { Type: NpcType }) return $"toggle invulnerability of {request.Object.Code} is not modelled (only the PC or an NPC)";
                continue;
            }
            // ARC-SCRIPT-1772 (CQ5): rename writes the name number (field 0x16), which the session keeps for an NPC (SessionNpcName) and the name readers consult; the PC's name number is not modelled and any other object type's is the map data's.
            if (request.Change == ScrObjectStateChange.Rename)
            {
                if (IsPc(request.Object)) return "rename of the PC is not modelled (the hero's name number is not kept)";
                if (Row(request.Object) is not { Type: NpcType }) return $"rename of {request.Object.Code} is not modelled (only an NPC)";
                continue;
            }
            // ARC-SCRIPT-1164 (E5-b, WO1, WO3, WO4): lock state and the open toggle act on a portal or container of the shown map (any other object type does nothing, as in the original), create item on the PC or an NPC with an
            // inventory list; the item actions (transfer, destroy named, written start, display) are checked below.
            if (request.Change is ScrObjectStateChange.SetLockState or ScrObjectStateChange.ToggleOpenClosed)
            {
                var action = request.Change == ScrObjectStateChange.SetLockState ? "set lock state" : "toggle open or closed";
                if (IsPc(request.Object)) continue;
                if (Row(request.Object) is not { } lockRow) return $"{action} of {request.Object.Code} is not modelled (no loaded row)";
                if (lockRow.Type is not (PortalObjectType or ContainerObjectType)) continue;
                if (request.Object.Map != _shownMap || OpenableOf(request.Object) is null) return $"{action} of {request.Object.Code} needs its lock and art facts, which only a portal or container of the shown map has";
                continue;
            }
            if (request.Change == ScrObjectStateChange.CreateItem)
            {
                if (_world?.Item(request.Value) is not { } made) return $"create item {request.Value}: no item prototype";
                if (made.Kind.Type is SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing or SessionItemType.Ammo) return $"create item {request.Value}: a {made.Kind.Type} is not modelled";
                touched.Add((HolderOf(request.Object), made.Kind.Name));
                if (IsPc(request.Object)) continue;
                if (Row(request.Object) is not { Type: NpcType }) return $"create item in {request.Object.Code} is not modelled (only the PC or an NPC)";
                if (_session.QueryMapInventory(new SessionMapId(request.Object.Map))?.Holders.Any(h => h.Entity == request.Object.Code) != true)
                    return $"create item in {request.Object.Code}: the NPC has no inventory list";
                continue;
            }
            // ARC-SCRIPT-1279 (WO2, WO7): the session's named-item move and destroy say, before anything applies, whether they can (a holder that is no critter or container, or has no such item, does nothing, as in the original).
            if (request.Change is ScrObjectStateChange.TransferItem or ScrObjectStateChange.DestroyItemNamed)
            {
                var holders = request.Change == ScrObjectStateChange.TransferItem ? new[] { HolderOf(request.Object), HolderOf(request.Other!.Value) } : new[] { HolderOf(request.Object) };
                // A second action on an item name in a holder an earlier action of the run moved, destroyed or created in is not planned: the earlier step may have changed which item is first.
                if (holders.Any(holder => touched.Contains((holder, request.Value)))) return $"{request.Change} of the item named {request.Value} meets an earlier item action of the run on the same holder and name, which is not modelled";
                foreach (var holder in holders) touched.Add((holder, request.Value));
                var plan = request.Change == ScrObjectStateChange.TransferItem
                    ? _session.QueryNamedItemTransfer(HolderOf(request.Object), HolderOf(request.Other!.Value), request.Value, ItemScripts) : _session.QueryNamedItemDestroy(HolderOf(request.Object), request.Value);
                if (plan.Refusal is { } why) return why;
                continue;
            }
            // ARC-SCRIPT-1279 (WO6, WO8): the item's row decides: an item row of the loaded maps takes the write, the PC and any other type do nothing, an object with no loaded row cannot be told from an item.
            if (request.Change is ScrObjectStateChange.SetWrittenStart or ScrObjectStateChange.ToggleItemInventoryDisplay)
            {
                if (!IsPc(request.Object) && Row(request.Object) is null) return $"{request.Change} of {request.Object.Code} is not modelled (no loaded row)";
                continue;
            }
            var name = request.Change == ScrObjectStateChange.Kill ? "kill" : "toggle state";
            if (request.Object.Map != _shownMap || IsPc(request.Object) || Row(request.Object) is not { Type: NpcType } row)
                return $"{name} of {request.Object.Code} is not modelled (only an NPC of the shown map)";
            if (request.Change == ScrObjectStateChange.Kill && (_world?.Kill is null || _world.CanKill?.Invoke(request.Object) == false))
                return $"kill of {request.Object.Code} needs its combat values";
        }
        return null;
    }

    // The first create object of the run the viewer or the session cannot apply, named; null when every one can be, so a run is never left
    // half applied by a creation.
    private string? Creatable(ScrEffects effects)
    {
        if (effects.Creations.Count == 0) return null;
        if (_world is null || _world.DropMap() is null) return "needs the shown map";
        var critters = effects.Creations.Count(request => _world.Item(request.Prototype) is null && _world.Critter?.Invoke(request.Prototype) == true);
        foreach (var request in effects.Creations)
        {
            var item = _world.Item(request.Prototype);
            // ARC-DEV-371: a critter prototype (a file that reads as an NPC's) is created as a critter; any other type stays refused.
            if (item is null && _world.Critter?.Invoke(request.Prototype) != true) return $"{request.Prototype}: no item prototype and no critter prototype";
            if (request.Near.Map != _shownMap || Place(request.Near) is not { } near) return $"{request.Prototype}: {request.Near.Code} has no place on the shown map";
            var refusal = item is null
                ? _session.QueryCreateCritterRefusal(request.Prototype + AreaPrototypeItems.PrototypeOffset, new SessionMapId(_shownMap), near, critters)
                : _session.QueryCreateGroundItemRefusal(item.Kind, new SessionMapId(_shownMap), near, effects.Creations.Count - critters);
            if (refusal is { } refused) return $"{request.Prototype}: the session would refuse it ({refused})";
        }
        return null;
    }

    // The walk's owner beside the object a creation names (PG2): the PC and its companions are the PC's side, another NPC an NPC, any other
    // object an object.
    private DropOwner OwnerOf(ScrObjectRef near)
    {
        if (IsPc(near) || FollowsPc(near)) return DropOwner.PcSide;
        return Row(near) is { Type: 15 or 16 } ? DropOwner.Npc : DropOwner.Object;
    }

    private bool WriteObjectSlot(ScrWriteback writeback, ScrObjectRef attachee)
    {
        if (writeback.Source != ScrSource.Object || !KeptAsMapChanges(writeback.Point) || writeback.Attachee != attachee
            || Row(attachee) is not { } row || LoadedOrPlaced(row, attachee.Code, writeback.Point) is not { } loaded) return false;
        var map = new SessionMapId(attachee.Map);
        // ARC-DEV-096: a part back at its loaded value is cleared where the object's record sits, which may be another map than the attachee's.
        MapChangeEdit Edit(string property, long value, long baseline)
        {
            var key = MapChangeKey.ForEntity(attachee.Code, property);
            return value == baseline ? new(_session.QueryObjectRecordMap(key) ?? map, key, null) : new(map, key, value);
        }
        var point = writeback.Point;
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(SlotProperty(point, ScriptPart), writeback.Slot.Script, loaded.Script),
            Edit(SlotProperty(point, LocalFlagsPart), writeback.Slot.LocalFlags, loaded.LocalFlags),
            Edit(SlotProperty(point, CountersPart), writeback.Slot.Counters, loaded.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    private bool WritePlacedSlot(ScrWriteback writeback, MapPlacedScript placed, ScrSource source)
    {
        if (writeback.Source != source || writeback.Attachee is not null || writeback.Point != ScrPoint.Use) return false;
        var map = new SessionMapId(_shownMap);
        MapChangeEdit Edit(string part, long value, long baseline) =>
            new(map, MapChangeKey.ForSector(placed.Sector.Id, PlacedProperty(placed, part)), value == baseline ? null : value);
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(PlacedScriptPart, writeback.Slot.Script, placed.Script),
            Edit(PlacedLocalFlagsPart, writeback.Slot.LocalFlags, placed.LocalFlags),
            Edit(PlacedCountersPart, writeback.Slot.Counters, placed.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }
}
