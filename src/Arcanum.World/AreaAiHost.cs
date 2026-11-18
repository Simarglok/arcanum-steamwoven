using System.Collections.Immutable;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Items;

namespace Arcanum.World;

/// <summary>ARC-WORLD-092: one NPC of the shown map the AI host runs: its session entity, its map row and its map data's world cell.</summary>
public sealed record AreaAiNpc(string Entity, AreaObjectRecord Row, CellCoord Origin);

/// <summary>
/// ARC-WORLD-092: the shown map as the AI host sees it, supplied by the viewer. Cells are world cells (the line of fire's and the
/// map rows'); the step evidence and the planners take chart cells through <see cref="Chart"/>. Every provider is read at each
/// question, so the host follows the viewer's live state (the hero's cell, the companions, the removed objects).
/// </summary>
public sealed record AreaAiScene
{
    public required int RuntimeMap { get; init; }
    public required AreaChart Chart { get; init; }
    public required AreaStepSnapshot Steps { get; init; }
    /// <summary>ARC-DEV-118 (MIG9 S5b, K1 §3.5): the host's chart over the loaded sectors (<c>LoadedSectorChart.Steps</c>), read at each question: while it
    /// answers a snapshot the planners and the steps read it instead of <see cref="Steps"/>, so a cell of a sector the session's cache has not loaded is
    /// outside the evidence; null (the provider, or its answer before a first sector is read): <see cref="Steps"/>. A whole map leaves it null.</summary>
    public Func<AreaStepSnapshot?>? LoadedSteps { get; init; }
    /// <summary>ARC-DEV-209 (MIG14 S3, GT49): the door ports the walkers' opener goals run over (the shown map's door host), or null while the map has none.</summary>
    public Func<IAiDoorWorld?>? DoorWorld { get; init; }
    /// <summary>ARC-DEV-209: the closed portals a step crosses, from the step's T0 assessment (the shown map's portals read in the session); the host keeps the first that is closed and idle.</summary>
    public Func<AreaStepAssessment, IReadOnlyList<string>>? ClosedPortalsOf { get; init; }
    /// <summary>ARC-DEV-209: the live passage of every door and window of the shown map (<see cref="AreaPortals.Passages"/>), which the planners and the step tests read in place of the T0 edge rules; null: T0.</summary>
    public Func<IReadOnlyDictionary<Arcanum.Data.Area.AreaObjectKey, AreaPortalPassage>?>? Passages { get; init; }
    /// <summary>ARC-DEV-118: told after the host touched sectors in the session's cache (a planner reading a goal in an unloaded sector, RL11), so the
    /// chart reads what entered the cache.</summary>
    public Action? SectorsTouched { get; init; }
    /// <summary>The NPCs the host runs (companions are left out: ARC-WORLD-115 runs the hero's through <see cref="Followers"/>).</summary>
    public required IReadOnlyList<AreaAiNpc> Npcs { get; init; }
    public required AreaCombatHost Combat { get; init; }
    /// <summary>ARC-DEV-137 (MIG13 S6): the ART of an NPC's animation (frames and rate) for the critter-state goals' timing; null (the provider, or its answer) when the header does not read or
    /// the viewer cannot read that animation (the stun's 23 is not read yet: the stun then ends at once, GT26).</summary>
    public Func<string, int, AiAnimationClip?>? AnimationClip { get; init; }
    /// <summary>ARC-AI-1486 (the item system's pickup switch 0x5E8800, lowered by 0x468190 when an inventory window opens in loot or See Contents mode on a container, 0x5727B0; raised by 0x468180 at its close,
    /// 0x573440): whether such a window is open now (a container of the map or a junk pile). While it is, no NPC starts a pickup (<see cref="AreaAiHost.NpcPickupsOn"/>); null: no window is ever open.</summary>
    public Func<bool>? LootWindowOpen { get; init; }
    /// <summary>ARC-AI-1655 (0x461810, 0x466640): the shown map's item script host, which runs the get (4) and insert-item (21) scripts of an item an NPC picks up (<see cref="GameSessionOwner.PickUpItemByNpc"/>); null (the provider, or its answer):
    /// an item with one of those scripts is refused for an NPC.</summary>
    public Func<ISessionItemScriptHost?>? ItemScripts { get; init; }
    /// <summary>ARC-AI-1313 (0x4D9240 at the dark wander's node 0x425D60): the light level 0..255 at a world cell, as the viewer's lighting answers it for a light-aware check; null: full light everywhere, so the dark wander accepts every tile.</summary>
    public Func<CellCoord, int>? CellLight { get; init; }
    /// <summary>ARC-WORLD-1162 (SWB17–SWB21): the beds of the shown map and who lies in them, shared with the hero's rest; null: the map has none, so an NPC at its standpoint finds no bed.</summary>
    public AreaAiBeds? Beds { get; init; }
    /// <summary>ARC-TECHNOLOGY-1619 (VB2-2 gap d): what the host knows of an object other than a critter by its session entity (a trap's shooter object, a scenery, a door): the facts the hostility rules read of it (its type, tile, object
    /// flags and busted state), or null for an id that names none. Null delegate: no such object is known, so an attack event of one ends at <see cref="AttackEventStop.Missing"/>.</summary>
    public Func<string, AreaAiObjectFacts?>? ObjectFacts { get; init; }
    /// <summary>ARC-AI-1636 (VB2-2 gap d): the door, window, container, scenery or trap an object id names as the session damages it (<see cref="AreaObjectAttackTarget.Object"/>, with its map row), for an NPC's fight against an object it holds as
    /// its COMBAT_FOCUS; null for an id that names none (a wall, an item, an object the session destroyed). Null delegate: such a fight swings and finds nothing to damage.</summary>
    public Func<string, AreaObjectAttackTarget?>? ObjectAttackTarget { get; init; }
    /// <summary>The hero's world cell now, or null without a hero on this map.</summary>
    public required Func<CellCoord?> HeroCell { get; init; }
    /// <summary>ARC-SCRIPT-1773 (CS5, AW3): the hero's current facing as the object rotation 0..7 (the octant the sight check compares: <see cref="GridActorMath.Octant"/>), for a sight check with the hero as the observer
    /// (<see cref="AreaAiHost.ScriptCanSee"/>); null (the provider, or its answer): the hero's facing is not known, and that check stops as not modelled.</summary>
    public Func<int?>? HeroRotation { get; init; }
    /// <summary>ARC-DEV-116 (MIG11 S5): whether the map list holds a map (the destination of an NPC's change of place, the original's map list lookup);
    /// null: only the shown map is listed.</summary>
    public Func<int, bool>? MapListed { get; init; }
    /// <summary>ARC-DEV-116: the requests of NPCs waiting for the session's slot (Steamwoven). The viewer owns the list, so a rebuilt host (a companion
    /// joined or went home, a reload of the area) goes on with the requests the earlier host held; null: the host keeps its own.</summary>
    public List<(string Npc, int Map, CellCoord Cell)>? Homecomings { get; init; }
    /// <summary>ARC-WORLD-1837: the same for the script teleports of NPCs waiting for the session's slot (Steamwoven), with the request's flags and clock seconds; null: the host keeps its own.</summary>
    public List<(string Npc, int Map, CellCoord Cell, ChangeOfPlaceFlags Flags, int Seconds)>? ScriptTeleports { get; init; }
    /// <summary>The companions standing here: their session entity, the T0 record they were made from (a visitor's is a row of its home map) and
    /// the world cells they hold now (their live overlay). ARC-DEV-075: the entity is given, never read back from the key on the shown map.</summary>
    public Func<IReadOnlyList<(string Entity, AreaObjectKey Key, CellCoord Cell)>>? Companions { get; init; }
    /// <summary>ARC-AI-1515: the follow check's facts of a companion the hero hit (<see cref="AreaCompanionLoyalty.Facts"/>: its alignment, level and thresholds against the
    /// PC side, its effective reaction now); null (or a null answer): the check cannot run and the hit changes the reaction only.</summary>
    public Func<string, PartyConsentFacts?>? FollowFacts { get; init; }
    /// <summary>The T0 records the session destroyed or switched off.</summary>
    public Func<IReadOnlySet<AreaObjectKey>>? Removed { get; init; }
    /// <summary>ARC-WORLD-1436: the NPCs the map data stores off that the session switched on, with the world cell of their map data (<see cref="AreaObjectTargets.SwitchedOn"/>): the T0
    /// evidence lists no blocker for them, so the planners hold their cells while the host has not walked them off (a companion, which the host does not run, is not held).</summary>
    public Func<IReadOnlyList<(AreaObjectKey Key, string Entity, CellCoord World)>>? SwitchedOn { get; init; }
    /// <summary>The shown map's line of fire now (walls, doors, objects, the companions on their cells), or null.</summary>
    public Func<ILineOfFireMap<AreaObjectRecord>?>? LineOfFire { get; init; }
    /// <summary>The shown map as the drop's placement reads it, or null.</summary>
    public Func<Arcanum.Simulation.Items.IDropMap?>? DropMap { get; init; }
    /// <summary>ARC-WORLD-1157 (SZ6): what the viewer shows (the game window and an object's drawn rectangle), for a generator's off-screen option; null (the provider or its answer): nothing is shown, so every rectangle is off the view.</summary>
    public Func<IAreaRestockScreen?>? Screen { get; init; }
    /// <summary>The hero's worn items with their durability values, or null.</summary>
    public Func<IReadOnlyDictionary<EquipmentSlot, SessionWearItem>?>? HeroWorn { get; init; }
    /// <summary>ARC-AI-2673 (ledger DSV5): the condition baseline (hp_pts, hp_adj, hp_damage, destroyed bits) of an item the PC holds: a kit item's comes from the hero load (the viewer's kit durability), a taken or made item's from its own fields
    /// (<see cref="AreaInventoryHost.RepairBaseline"/>); null for an item the PC does not hold. An NPC's repair of such an item reads it. Null provider: the PC's items are not repairable by an NPC.</summary>
    public Func<string, ItemCondition?>? HeroItemCondition { get; init; }
    /// <summary>The NPC the hero talks with, or null.</summary>
    public Func<string?>? ConversationPartner { get; init; }
    /// <summary>ARC-DEV-177 (MIG13 S11e): the dialog-end callback the AI gate calls (0x5678D0 through 0x5F848C): the viewer ends the PC's conversation with this NPC at its next safe point (in the viewer a barter has already closed the dialog, so nothing is open to end).</summary>
    public Action<string>? ConversationEnded { get; init; }
    /// <summary>The context the hero enters combat mode with when attacked (the viewer's turn-based setting and round world).</summary>
    public Func<SessionCombatModeContext>? HeroCombatMode { get; init; }
    /// <summary>The AI parameter table; null: every NPC reads row values 0 (the original's out-of-table read is refused as data).</summary>
    public AiParameterTable? Parameters { get; init; }
    /// <summary>The per-gait step durations; null: the live step of <see cref="DiagnosticGridSession.LiveDuration"/>.</summary>
    public GaitTiming? Timing { get; init; }
    /// <summary>ARC-RENDER-038: the attack ART an NPC's body has for a request (its animation, weapon part and shield), or null when it
    /// has none the viewer can read: the attack then plays with the fixed human attack ART's timing and is not drawn. Null: always so.</summary>
    public Func<AreaAiNpc, AreaAiAttackArtRequest, AreaAiAttackArt?>? AttackArt { get; init; }
    /// <summary>ARC-RENDER-1126 (0x4AABE0, 0x4A92D0): the sound id of an NPC's alerted bark (its sound base + 5, 0x4F0ED0), or null when it has none. The danger update's setter call
    /// that makes the NPC hostile stores it in the pass record, and the combat approach goal plays it once (<see cref="PlaySound"/>, node 0x4296D0). Null: no NPC has one.</summary>
    public Func<string, int?>? AlertedSound { get; init; }
    /// <summary>ARC-WORLD-099: whether the hero runs no animation (its walk or attack, which count in the original's animations-complete
    /// test); null: idle.</summary>
    public Func<bool>? HeroIdle { get; init; }
    /// <summary>ARC-WORLD-102: the shown map's cast host (the hero's casts go through it too), or null while the spell data or the
    /// map's magic world is missing: the NPCs then know no spell.</summary>
    public Func<SessionSpellCaster?>? Spells { get; init; }
    /// <summary>ARC-WORLD-102: the cast entry's eye candy of a spell on the NPC (goal 27's play, 0x429F00), started at 0 ms, or null
    /// when none plays (no table, art, part or free slot, an ART that does not read).</summary>
    public Func<string, int, CastEyeCandyAnimation?>? CastEyeCandy { get; init; }
    /// <summary>ARC-WORLD-102: whether a spell's projectile eye candy has art (0x458B70), so its release launches a missile
    /// (ARC-WORLD-105: <see cref="AreaAiNoticeKind.Missile"/>, flown by the viewer).</summary>
    public Func<int, bool>? MissileArt { get; init; }
    /// <summary>ARC-WORLD-114: runs an NPC's first-heartbeat or heartbeat script (the NPC as attachee and triggerer) and answers whether
    /// the tick goes on ("run default", or no script that runs); null: no script runs at these points.</summary>
    public Func<AiScriptPoint, string, bool>? Heartbeat { get; init; }
    /// <summary>ARC-SCRIPT-1590 (0x441980, item use 0x462CC0): runs the use script (point 1) of an item an NPC holds, the NPC as the user and triggerer and the target (the entity the use names, "pc" for the hero) as the extra object, and answers whether the use goes on (a script that
    /// ran default, or none that runs: refused, not applied); false when it skipped the default (the food script 30282 always does, after it destroyed the item); null: no NPC's item use runs a script, and the use goes on as the script let it.</summary>
    public Func<string, string, string?, bool>? ItemUseScript { get; init; }
    /// <summary>ARC-WORLD-109: an NPC's map data's advancement sheet and its effects' modifiers (its skills for a skill attempt), or null
    /// when they cannot be read; null: no NPC has skills, so its heal attempt is refused.</summary>
    public Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?>? NpcSheet { get; init; }
    /// <summary>ARC-COMBAT-1776 (0x42A4E0): the lock a pick-locks goal's target names (a door, window or container of the shown map, by the goal's target string), as the session picks it: the object, its T0 lock facts, the lock's difficulty and the
    /// light at it; null (the provider, or its answer) for a target that is none or gone. Null delegate: no NPC picks a lock, and the attempt names it a gap.</summary>
    public Func<string, SessionPickLockTarget?>? PickLockTarget { get; init; }
    /// <summary>ARC-AI-2625 (0x4C7160 case 7, the target test 0x4BBDD0): the trap class of the object a spot-trap attempt names (its script at point 1: magical for header flag 0x2, non-magical for 0x1; <see cref="TrapClass.None"/> for a target with no trap, the
    /// hero, or one the shown map does not hold). Null delegate: every target holds no trap, so the attempt is the roll and the master's second roll alone.</summary>
    public Func<string, TrapClass>? TargetTrapClass { get; init; }
    /// <summary>ARC-DEV-246 (MIG13 S14l, GT208): the victim's catching-thief script (point 11) after a noticed pick pocket, with the NPC thief as the triggerer; true when the consequences go on ("run default", or no script); false when the script's answer, or a run the viewer cannot finish, stops them; null: no script runs.</summary>
    public Func<string, string, bool>? CatchingThief { get; init; }
    /// <summary>ARC-AI-2084 (ledger GW5): a guard witness's catching-thief script (point 11, 0x4AEE50) after it saw or heard the actor at an act on a portal, container or scenery: the witness (the attachee), the actor (the triggerer; the hero is "pc") and the guarded object (the script's extra object); true when the witness goes on ("run default", or no script that runs), false when the script skipped the default and the witness is left out. Null: no script runs.</summary>
    public Func<string, string, string, bool>? GuardCatchingThief { get; init; }
    /// <summary>ARC-DEV-258 (MIG13 S14n, GT63): launches the projectile of a generic item (a grenade) an NPC threw: the thrower, its shot, the thrower's tile and the throw's location; true when the
    /// flight began (the viewer then lands the item where it ends and fires its event). Null or false: the item lands on the location at once, with no event (a flight the projectile host refused to start has ended the shot, and the item stays on the thrower's cell).
    /// ARC-COMBAT-1717: the same for a thrown weapon, whose destination is the struck critter's tile (a decided hit, the viewer strikes through <see cref="AreaAiHost.LandShot"/> and lands the item) or the scatter tile (a miss); a host that can fly it
    /// makes the throw fly (<see cref="SessionNpcThrow.Flight"/>), and a false answer there resolves the throw at once (a hit striking first).</summary>
    public Func<string, SessionShot, CellCoord, CellCoord, bool>? LaunchThrown { get; init; }
    /// <summary>ARC-DEV-282 (PF1, PF2): launches the arrow or bullet of an NPC's bow or firearms attack: the shooter, its target (an NPC's entity or the hero), the shot, the shooter's tile and the target's; true
    /// when the flight began (the viewer then lands the strike through <see cref="AreaAiHost.LandShot"/> where it ends). Null: the attack lands at once, as ARC-DEV-273's; false: the host strikes the aimed target at once.</summary>
    public Func<string, string, SessionShot, CellCoord, CellCoord, bool>? LaunchShot { get; init; }
    /// <summary>ARC-COMBAT-1140 (RA4): the door, window, container, scenery or trap a map row of the line of fire's blocker stands for, with its session target (<see cref="AreaObjectAttackTarget.Object"/>); null for a row that is
    /// none (an item, a projectile, one the session destroyed, a wall: a wall is struck without a target). Null delegate: no object takes an NPC's shot, the line's check just fails (ARC-DEV-284).</summary>
    public Func<AreaObjectRecord, AreaObjectAttackTarget?>? ObjectTarget { get; init; }
    /// <summary>ARC-COMBAT-1140 (RA4, PF2): launches the arrow or bullet of an NPC's bow or firearms attack on the object or wall that stopped its line of fire: the shooter, the blocker's row (the shot's target: it strikes it
    /// where it crosses the tile), the shot, the shooter's tile and the aimed target's (the original keeps the target location); true when the flight began (the viewer then lands the strike through
    /// <see cref="AreaAiHost.LandShotOnObject"/> where it ends). Null or false: the host strikes the blocker at once.</summary>
    public Func<string, AreaObjectRecord, SessionShot, CellCoord, CellCoord, bool>? LaunchShotAtObject { get; init; }
    /// <summary>ARC-COMBAT-1140: an NPC's shot decided and landed at once on an object or wall (the shot did not fly): the shooter, the blocker's row, its target (null for a wall) as it was before the hit, and the session's result, for what the viewer does after an object hit
    /// (the floaters, the trap and BUST script, the sounds, the redraw). A shot that flew is landed by the viewer itself.</summary>
    public Action<string, AreaObjectRecord, AreaObjectAttackTarget?, SessionObjectAttackResult>? ObjectStruck { get; init; }
    /// <summary>ARC-DEV-261 (MIG13 S14m, GT198–GT199): a victim's taking-damage script (point 0x17) asked by an NPC's damage: the victim and the damage's attacker (null: none) in, whether the damage goes on out (false: the script skipped the default, which cancels the whole damage step). The hero has no script data and is not asked. Null: the damage goes on.</summary>
    public Func<string, string?, bool>? TakingDamage { get; init; }
    /// <summary>ARC-SCRIPT-1483 (CP1–CP3): the combat-point scripts (hit 7, critter hits 26, critical hit 34) of an NPC's hit, asked before its damage step: the attacker, the victim (the hero is <c>"pc"</c>) and the session's hit event in. Null: none run.</summary>
    public Action<string, string, SessionHitScriptEvent>? HitScripts { get; init; }
    /// <summary>ARC-WORLD-115 (NPC AI slice I9, host part): the companions of the shown map the host runs too, and their moves; null:
    /// the companions run no AI (their follow goal moves them). ARC-WORLD-119: the free ones too, as any leaderless NPC.</summary>
    public IAreaAiFollowers? Followers { get; init; }
    /// <summary>ARC-MAGIC-1189 (ledger TT10, SP2): the shown map's trap host (<see cref="AreaTraps"/>), which checks the next tile of an NPC's or a companion's step for an unknown trap
    /// before the step starts (0x427110) and rolls the NPC's own spot attempt; null (the provider or its answer): the NPCs meet no trap, as before.</summary>
    public Func<AreaTraps?>? Traps { get; init; }
    /// <summary>ARC-DEV-287 (MIG13 tail T6): the hero's body for the knockback (its art rotation and its relocation onto a tile); null: the hero cannot be pushed here.</summary>
    public IAreaHeroPush? HeroPush { get; init; }
}

/// <summary>
/// ARC-DEV-287 (MIG13 tail T6): what the knockback needs of the hero's body, which is the viewer's movement runtime (the hero has no mover in the AI host). Goal 43 pushes a player character through the ordinary move
/// 0x43E770: the location and the pixel offsets only (goal-table.md Tail T6), so the push is a relocation onto each tile of the line, the facing kept, and the tile's entry events are the viewer's.
/// </summary>
public interface IAreaHeroPush
{
    /// <summary>The rotation 0..7 the hero's art shows now (a push from its own tile sends it backward).</summary>
    int Rotation { get; }
    /// <summary>Puts the hero on the world tile <paramref name="tile"/> at once, its facing kept, and tells the viewer what the original's move does there (the tile's jump point and script, the view left where it is); false when the body refuses it.</summary>
    bool Slide(CellCoord tile);
}

/// <summary>
/// ARC-WORLD-115: the companions of the shown map as the AI host runs them (ARC-WORLD-119: whether the hero leads them or they are
/// free, released or cursed: the removal leaves no mark but the null leader, which the AI's follower branches read, FO20). A companion stands where its own walk put it (the
/// scene's <see cref="AreaAiScene.Companions"/> answers its cell); the host's step and turn of one go through the viewer's walk of
/// that companion, which follows nothing else while the host holds it (<see cref="AreaAiHost.Holds"/>).
/// </summary>
public interface IAreaAiFollowers
{
    /// <summary>The companions standing on the map: their session entity, T0 row and home map data's world cell.</summary>
    IReadOnlyList<AreaAiNpc> Npcs { get; }
    /// <summary>Walks the companion one step onto the world cell <paramref name="next"/> (a neighbour of its cell); false when its walk
    /// refuses it.</summary>
    bool Step(string entity, CellCoord next, bool running, bool sneaking = false);
    /// <summary>Turns the companion toward the world cell <paramref name="toward"/>.</summary>
    void Face(string entity, CellCoord toward);
    /// <summary>ARC-DEV-105 (MIG9 S5): moves the companion onto the world cell <paramref name="cell"/> at once (the follower catch-up, 0x4AD4D0);
    /// false when it is not here or its walk refuses the cell. A follower source without the move refuses it.</summary>
    bool Jump(string entity, CellCoord cell) => false;
}

/// <summary>
/// ARC-TECHNOLOGY-1619: an object other than a critter as the hostility rules read it (the original's object type and flags): its type (0 wall, 1 portal, 2 container, 3 scenery, 17 trap object and so on), the world tile it stands on and
/// the object flags 0x1 destroyed, 0x2 off, 0x100 don't draw and 0x400000 invulnerable, and 0x43F630's busted state.
/// </summary>
public sealed record AreaAiObjectFacts(int Type, CellCoord Cell)
{
    public bool Destroyed { get; init; }
    public bool Off { get; init; }
    public bool DontDraw { get; init; }
    public bool Invulnerable { get; init; }
    public bool Busted { get; init; }
    /// <summary>ARC-AI-1756 (0x42B9C0 node 5): the object's current art id (field 1), which the attack animation's height and animation tests read; 0 where the viewer reads none.</summary>
    public uint Art { get; init; }
    /// <summary>ARC-AI-1756 (0x42B9C0 node 5): the object flags (field 0x13) whose bit 0x80 (shrunk) picks the low attack.</summary>
    public uint Flags { get; init; }
}

/// <summary>What the AI host tells the viewer: an NPC's attack swing (its pose), the hit's result, a step, a sound, (ARC-WORLD-099)
/// the fidget an NPC starts after a done run (0x4B4320; its animation is not drawn: NPC ART is not read yet), (ARC-WORLD-102) a
/// cast's play, its release and its goal's end, (ARC-WORLD-105) a missile launched at the play's action frame, (ARC-WORLD-109) a
/// skill attempt at its action frame, (ARC-WORLD-113) a throw at its action frame, (ARC-WORLD-108) an item use's magic branch and (ARC-DEV-206, GR12)
/// the cell an NPC the host moved was placed on (<see cref="AreaAiNotice.Cell"/>), for the tile's script.</summary>
public enum AreaAiNoticeKind { Swing, Hit, Step, Sound, Fidget, Cast, Release, CastEnd, Missile, Skill, Throw, ItemUse, Entered, PickUp, Rewield, PickUpDeclined }

/// <summary>ARC-WORLD-092: one notice for the viewer (the attack pose, the hit's sounds and status line, the redraw).</summary>
public sealed record AreaAiNotice(AreaAiNoticeKind Kind, string Npc, string? Target)
{
    /// <summary>ARC-DEV-206 (GR12, <see cref="AreaAiNoticeKind.Entered"/>): the world cell the NPC was placed on.</summary>
    public CellCoord? Cell { get; init; }
    /// <summary>ARC-AI-1655 (<see cref="AreaAiNoticeKind.PickUp"/>, <see cref="AreaAiNoticeKind.PickUpDeclined"/>; ARC-MAGIC-1684, <see cref="AreaAiNoticeKind.Rewield"/>): the lines the item's get or insert-item script (or the scripts of the items a re-wield moved) printed, for the message window.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get; init; } = [];
    /// <summary>ARC-MAGIC-1830 (<see cref="AreaAiNoticeKind.Rewield"/>): the eye candy requests of the scripts of the items a re-wield moved, in order, for the viewer to play.</summary>
    public IReadOnlyList<Arcanum.Scripting.Scr.ScrEyeCandy> EyeCandies { get; init; } = [];
    public SessionAttackResult? Result { get; init; }
    public int Sound { get; init; }
    public AiAttackSwing Swing { get; init; }
    /// <summary>ARC-RENDER-1126 (0x432700): the attacker's roll of 1..4 for its attacking bark came up 1 (<see cref="AreaAiNoticeKind.Swing"/>); the roll is the original's, made at the swing's start whatever plays it.</summary>
    public bool AttackBark { get; init; }
    /// <summary>ARC-RENDER-038: the attack animation the swing started (<see cref="AreaAiNoticeKind.Swing"/>).</summary>
    public AreaAiAttackPlay? Play { get; init; }
    /// <summary>ARC-WORLD-102: the cast's spell and magic run, its play's timing (<see cref="AreaAiNoticeKind.Cast"/>; none plays: the
    /// release came at once), whether the release began the run (<see cref="AreaAiNoticeKind.Release"/>) and the goal end's free
    /// (<see cref="AreaAiNoticeKind.CastEnd"/>); ARC-WORLD-105: the launched missile's spell and run, its target object in
    /// <see cref="Target"/> (<see cref="AreaAiNoticeKind.Missile"/>).</summary>
    public int Spell { get; init; } = -1;
    public int Run { get; init; } = -1;
    public AiCastSwing CastSwing { get; init; }
    public bool Released { get; init; }
    public SessionSpellRunResult? SpellRun { get; init; }
    /// <summary>ARC-WORLD-105: the cast host that launched the missile (its arrival and loss go through it).</summary>
    public SessionSpellCaster? Caster { get; init; }
    /// <summary>ARC-WORLD-109: the attempted skill (<see cref="AreaAiNoticeKind.Skill"/>) and, for a heal that reached the session, its
    /// result.</summary>
    public int Skill { get; init; } = -1;
    public SessionHealResult? Heal { get; init; }
    /// <summary>ARC-DEV-246: what an NPC's pick pocket did (<see cref="AreaAiNoticeKind.Skill"/> with the pick pocket skill).</summary>
    public SessionNpcPickPocketResult? PickPocket { get; init; }
    /// <summary>ARC-AI-1854: the session's plain roll of a skill with no handler of its own (the goal-33 attempt's remaining cases).</summary>
    public SessionNpcSkillRoll? PlainRoll { get; init; }
    /// <summary>ARC-AI-2622: the session's answer of an NPC's repair of an item (<see cref="GameSessionOwner.RepairByNpc"/>); null when the host did not ask it (no sheet or no repairable item).</summary>
    public SessionNpcRepairResult? Repair { get; init; }
    /// <summary>ARC-COMBAT-1776 (<see cref="AreaAiNoticeKind.Skill"/> with the pick-locks skill): the session's answer of an NPC's pick of a lock (<see cref="GameSessionOwner.PickLockByNpc"/>); null when the host did not ask it (no sheet or no lock target).</summary>
    public SessionPickLockResult? PickLock { get; init; }
    /// <summary>ARC-WORLD-113: the thrown item and the session's throw (<see cref="AreaAiNoticeKind.Throw"/>; the struck critter in
    /// <see cref="Target"/>, its attack in <see cref="Result"/>); ARC-WORLD-108: the used item (<see cref="AreaAiNoticeKind.ItemUse"/>).</summary>
    public string? Item { get; init; }
    public SessionNpcThrowResult? Throw { get; init; }
    /// <summary>ARC-WORLD-108: what the item use's magic branch did (<see cref="AreaAiNoticeKind.ItemUse"/>).</summary>
    public SessionItemUseResult? ItemUse { get; init; }
}

/// <summary>
/// ARC-WORLD-092 (NPC AI slice I10, first fight): the AREA viewer's AI host. It is the scheduler's host (<see cref="ISessionAiWorld"/>,
/// forwarding the event pass's script and party questions to the viewer's event world), the executor's world (<see cref="IAiPerformWorld"/>)
/// and the hostility and morale rules' world (<see cref="IMoraleWorld"/>) over the shown map. ARC-DEV-134 (MIG13 S4): an NPC's goal run is a run of the session's goal
/// table (<see cref="GoalRun"/>, not saved), started by the host's starts and stepped by the frame step at its deadline (<see cref="StepGoal"/>, the world runtime's
/// <see cref="GoalStepper"/>); <see cref="RunOf"/> and <see cref="AnimationRunning"/> read the table. The pass's perform stage
/// runs <see cref="AiExecutorRules.Perform"/>; the danger stage runs <see cref="HostilityRules.DangerUpdate"/> and applies its plan through
/// the session's setter; the hit step is the session's <see cref="GameSessionOwner.AttackByNpc"/>, followed by the attack event; the
/// hero's hits run the attack event here too (<see cref="HeroAttacked"/>). The walker moves NPCs tile by tile (<see cref="Movers"/>).
/// The runs go by the session's animation clock (<c>goal.step</c> events), the ticks by the session's game clock. In a turn-based round
/// (ARC-WORLD-095) the host follows the round's subturns (<see cref="ObserveRound"/>): a round's start and each new subturn interrupt
/// runs as the original's do, the pending cost reads 0 after each subturn start, and the round host
/// (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/>) runs the NPCs' subturn hooks.
/// ARC-WORLD-099: an NPC's subturn end asks its end-combat script (<see cref="SubturnsEnded"/>), a turn set's interrupt that frees the
/// last run runs the completion check, and a done run's end may start the NPC's fidget (<see cref="AiExecutorRules.FidgetAfterRun"/>).
/// ARC-WORLD-102: the support rules' spell half (<see cref="IAiSupportWorld"/>, the spell answers through the map's cast host) chooses
/// a spell at the danger update's target (<see cref="AiSupportRules.ChooseSpell"/>), perform casts it (<see cref="Cast"/>,
/// <see cref="SessionSpellCaster.CastByAi"/>) and the casting goals play through the cast host (start, turn, play, release, end).
/// ARC-WORLD-104: a cast while the NPC's casting goal 26 or 27 plays is queued beneath it (SU13, AC5), its goal played once the goals
/// above it are done; the AI pass itself never meets one, as the gate holds a casting NPC busy (AC6). ARC-WORLD-105: a spell with a
/// missile launches it at the play's action frame (AC8), the viewer flying it from the NPC. ARC-WORLD-113: the attack starter's throw of
/// a "default throws" weapon plays the throw goal 37 (<see cref="ThrowItem"/>, the session's <see cref="GameSessionOwner.ThrowByNpc"/>).
/// ARC-WORLD-108: the NPCs' items are listed (<see cref="Inventory"/>, <see cref="Item"/>), the pass's heal step chooses among them
/// (<see cref="Heal"/>), perform casts from an item (<see cref="Cast"/>) and goal 39 uses one through the cast host's item use.
/// ARC-WORLD-115 (I9's host part): the companions of the shown map are run too (<see cref="AreaAiScene.Followers"/>): their ticks, passes
/// and goal runs, their steps and turns through their own walk, which the host holds while they fight (<see cref="Holds"/>); every
/// attack event tells the attacker's and the victim's followers (the follower rule, <see cref="TellFollowers"/>).
/// ARC-WORLD-119: free companions are run as any leaderless NPC; a leave ends the leaver's run (<see cref="ObserveLeaves"/>), and an
/// order's attack event (the curse's) runs through <see cref="OrderAttack"/>.
/// ARC-DEV-075 (MIG2, TM3): a companion carried here from another home map is run like any other: the host asks its combat values, worn
/// and held items, AI parameter row and sheet by identity (<see cref="AreaCombatHost.Fields(string)"/> and the other identity overloads, the
/// record resolver of MIG1 reading its home map's rows), so it is told of attacks, stands in scans' and noises' squares, counts in a flee
/// test's party, attacks and is attacked (the ARC-DEV-065 skip of every visitor is gone). A follower left on
/// another map is still not here (<see cref="Followers"/>). A companion whose home map cannot be read (the main map's window is not read
/// on demand: the resolver's NOT READY, MIG9) has no record, so it is not found and stays out of the rules as before (<see cref="Unresolved"/>).
/// NOT READY (later slices): the visitor's faction change of a script (the shown map's change), the main-map window as a home;
/// the heal skill's rung and goal 40, pickups, a thrown item's flight, grenades, the back-off's away path, the attack order's target and pass, the comment lines, NPC
/// ranged attacks, scripts at the AI points, routines and waypoints, saved NPC positions.
/// </summary>
public sealed partial class AreaAiHost : ISessionAiWorld, ISessionPartyEventWorld, ISessionLootWindowWorld, ISessionBodyWorld, IAiPerformWorld, IMoraleWorld, IAiSupportWorld, IAiGrenadeWorld, IAiItemSearchWorld, IGoalStepHost
{
    private const string Pc = AiSchedulerRules.Pc;
    // Object flags: destroyed, off, don't draw, invisible, invulnerable; critter flags; spell flags; NPC flags (HostilityCritter's bits).
    private const uint DontDraw = 0x100, ObjectInvisible = 0x200, Invulnerable = 0x400000, OffObject = 0x2;
    private const uint Undead = 0x4, Animal = 0x8, SleepingFlag = 0x1000, NoFleeFlag = 0x8000000, NonLethal = 0x10000000, AnimalEnshrouded = 0x40000000;
    private const uint NighInvulnerable2 = 0x200, AutoAnimates2 = 0x2, DarkSight2 = 0x800;
    private const uint InvisibleSpell = 0x1, DetectsInvisibleSpell = 0x200, StonedSpell = 0x10000, EntangledSpell = 0x1000000, MindControlledSpell = 0x8000000, EnshroudedSpell = 0x20000000;
    private const uint WaitingHereNpc = 0x8, KillOnSightNpc = 0x100, KosOverrideNpc = 0x800, GeneratorNpc = 0x80000, AloofNpc = 0x20000;
    // ARC-WORLD-102: critter flag MECHANICAL and NPC flag CAST_HIGHEST (field 0x118, read in 0x4AE720).
    private const uint MechanicalFlag = 0x20000000, CastHighestNpc = 0x40000;
    // ARC-WORLD-108: item flags identified, hexed and valid AI action (field 0x57, read by 0x4CCA30, 0x4CC810, 0x4CC930).
    private const uint IdentifiedItem = 0x1, HexedItem = 0x40, ValidAiActionItem = 0x200000;
    // The lists of a host without spell records: every spell without AI values.
    private static readonly AiGlobalLists NoLists = AiGlobalLists.Build([.. Enumerable.Repeat(AiSpellValues.None, AiGlobalLists.Spells)]);
    private const int GuardClass = 5;
    // The fixed human attack ART (hmfuwxbu: 9 frames, action frame 6, 15 frames per second) stands in for an NPC's own when the viewer
    // reads none (ARC-RENDER-038: <see cref="AreaAiScene.AttackArt"/>).
    private static readonly (int Frames, int ActionFrame, int RawFramesPerSecond) FixedAttackArt = (9, 6, 15);
    // ARC-WORLD-109: the skill animation (12), and since ARC-WORLD-113 the throw animation (14), stand in with the same frames, played
    // at their raw rate: NPC ART is not read yet.
    private static readonly (int Frames, int ActionFrame, int RawFramesPerSecond) FixedSkillArt = FixedAttackArt;
    // ARC-WORLD-109: object flag 0x10000 and critter flags stunned and paralysed refuse an action's turn and start (0x42C0F0, 0x42CB10).
    private const uint ActionRefusedObject = 0x10000, StunnedCritter = 0x20, ParalysedCritter = 0x40, BlindedCritter = 0x80, ProwlingCritter = 0x1;
    // ARC-DEV-135 (MIG13 S5): the levels of the host's own interrupts (GS20): the AI tick's far path, the follower catch-up and the waypoint and standpoint moves interrupt at 4
    // (TL42), a follower's removal from the party at 5 (0x45E040).
    private const int FarPathLevel = 4, LeaveLevel = 5;

    private readonly GameSessionOwner _session;
    // ARC-DEV-119 (MIG9 S5c): replaced by Rebind when the map's window moves; the host itself is built once per map.
    private AreaAiScene _scene;
    private readonly ISessionScriptWorld _events;
    private readonly ISessionCombatWorld _round;
    private readonly Dictionary<string, AreaAiNpc> _npcs;
    // ARC-DEV-359 (E1-c): the critters the session created that this host runs (a created critter has no window to give its row back, so a window move never forgets it).
    private readonly HashSet<string> _created = new(StringComparer.Ordinal);
    // A created critter whose first tick the session refused (the event budget): asked again at the next sync, since nothing else starts a chain for it.
    private readonly HashSet<string> _createdUnticked = new(StringComparer.Ordinal);
    // ARC-WORLD-115: the companions the host runs (their cells are their walk's, never the movers'); ARC-WORLD-119: free ones too.
    private readonly Dictionary<string, AreaAiNpc> _followers;
    // ARC-WORLD-119: the companions the hero led at the host's last look (ObserveLeaves).
    private readonly HashSet<string> _led;
    // ARC-WORLD-1840: the leader each NPC of the map had at the host's last look (ObserveLeaves).
    private readonly Dictionary<string, string> _npcLeaders = new(StringComparer.Ordinal);
    // ARC-DEV-134 (MIG13 S4): the runs are the session's goal table (GameSessionOwner.QueryGoals); the host keeps none. The steps taken since TakeSteps.
    private int _steps;
    private readonly Dictionary<string, AreaAiTrace> _traces = new(StringComparer.Ordinal);
    private readonly List<AreaAiNotice> _notices = [];
    // The round's events of the host's own session calls (the executor's consumes and subturn ends), for the viewer's round log.
    private readonly List<SessionCombatEvent> _roundEvents = [];
    // The world milliseconds of the last frame or step (the frame step's clock, ARC-DEV-124).
    private long _nowMs;
    // ARC-DEV-134: the run the stepper is ending (its end hooks run before the session frees it, so the fidget's "no run" test leaves it out).
    private GoalHandle? _ending;
    // ARC-DEV-134: the runs stepped since the last frame (a run steps at most once a frame, as the former pump stepped it: a delay shorter than the frame, 0 among
    // them, is the next frame), the world milliseconds of the last frame and its length (the wait of a run that came due again within its frame).
    private readonly HashSet<GoalHandle> _steppedThisFrame = [];
    private long _lastFrameMs = -1;
    private long _frameLengthMs = 16;
    // ARC-WORLD-095: the round as last observed (whether one ran, the session's subturn starts then, the critter of the last turn set:
    // 0x5FC248), the pending cost (0x5FC244) and the subturn start it was written in.
    private bool _roundSeen;
    private long _subturnsSeen = -1;
    private string? _turnSetActor;
    private int _pendingCost;
    private long _pendingAt = -1;
    // ARC-WORLD-099: the round actor whose done run's fidget call waits for its subturn hook.
    private string? _fidgetAfterHook;
    // ARC-WORLD-102: the pass record's choice the danger update wrote for perform (0x4ABC20 → 0x4AC180), the list cache (SP11, not
    // saved), the facing a casting goal's start computed for its turn, and the magic runs whose play reaches no action frame.
    private readonly Dictionary<string, AiPassChoice> _passChoices = new(StringComparer.Ordinal);
    // ARC-RENDER-1126: the alerted sound the pass's setter call stored in its pass record (0x4A92D0), for this pass's perform.
    private readonly Dictionary<string, int> _passSounds = new(StringComparer.Ordinal);
    // ARC-DEV-292: the NPCs whose immediate pass after a hit is running (Struck): a hit inside it does not start another.
    private readonly HashSet<string> _striking = new(StringComparer.Ordinal);
    // ARC-DEV-292: the NPCs whose own goal step is running (StepGoal): a hit on one of them inside its step (a critical miss that turned on it, a spell its step cast) does not start a pass in the middle of that step (a project choice: the original's call stack has no such guard).
    private readonly HashSet<string> _stepping = new(StringComparer.Ordinal);
    private AiListCache _listCache = AiListCache.Empty;
    // ARC-WORLD-108: the items each NPC used or cast from, until they are gone (the list cache's drops, List).
    private readonly Dictionary<string, HashSet<string>> _usedItems = new(StringComparer.Ordinal);
    private readonly Dictionary<int, SpellFacing?> _castFacing = [];
    private readonly HashSet<int> _noRelease = [];
    // ARC-DEV-289 (MIG14 S8): the magic runs whose play this host started (BeginCast): a casting goal a load restored reaches its action frame without one, and the host then asks
    // the play again whether it reaches its release (the play is the same eye candy for the same spell; its slots are the caster's).
    private readonly HashSet<int> _playedCasts = [];
    // ARC-WORLD-104: the magic runs of queued casting goals that have not started yet (SU13); an end step leaves them unplayed.
    private readonly HashSet<int> _queuedCasts = [];
    // ARC-WORLD-105: the runs launched by a casting goal still playing, with the cast host that launched them (its handled frame and
    // missile live there): a host rebuilt before the goal's end would see neither and free the run charged.
    private readonly Dictionary<int, SessionSpellCaster> _launched = [];
    // ARC-RENDER-038: each NPC's last attack animation (the viewer draws it while it plays).
    private readonly Dictionary<string, AreaAiAttackPlay> _attackPlays = new(StringComparer.Ordinal);
    // ARC-RENDER-038: the weapon part and shield each NPC shows, for one session revision.
    private readonly Dictionary<string, (int Weapon, int Shield)> _shown = new(StringComparer.Ordinal);
    private long _shownRevision = -1;

    /// <param name="events">The viewer's event world: the pass's script calls, life facts, party and loot questions.</param>
    /// <param name="round">The turn-based round's world (the scan of the map's critters).</param>
    public AreaAiHost(GameSessionOwner session, AreaAiScene scene, ISessionScriptWorld events, ISessionCombatWorld round)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _round = round ?? throw new ArgumentNullException(nameof(round));
        _npcs = scene.Npcs.ToDictionary(n => n.Entity, StringComparer.Ordinal);
        _followers = (scene.Followers?.Npcs ?? []).Where(n => !_npcs.ContainsKey(n.Entity)).ToDictionary(n => n.Entity, StringComparer.Ordinal);
        _led = _followers.Keys.Where(f => _session.QueryCompanion(f).LedByPc).ToHashSet(StringComparer.Ordinal);
        // ARC-WORLD-1840: the NPCs of the map with a leader (an NPC summoner's summon, a script's group) at the first look.
        foreach (var npc in _npcs.Keys) if (_session.QueryLeader(npc) is { } leader) _npcLeaders[npc] = leader;
        RestoreMovers();
        RestoreQueuedCasts();
    }

    // ARC-DEV-289 (MIG14 S8): a load restores the NPCs' casting goals (the goals section, saved with the spell runs they name). The queued ones are the casting goals 26 and 82 a
    // cast put beneath a playing goal (Perform's queue, SU13): below the run's top and never started (node 1), so a host built for the restored runs knows them as the one that
    // queued them did. The facing a start computed is applied in the same step, and a launched run's release lives in the cast host (rebuilt from the session's missiles).
    private void RestoreQueuedCasts()
    {
        foreach (var run in LiveRuns())
            for (var i = 0; i < run.Frames.Length - 1; i++)
                if (run.Frames[i] is { Goal: GoalType.CastSpell or GoalType.CastSpellWithAnimation, Node: 1, SpellRun: { } id }) _queuedCasts.Add(id);
    }

    // ARC-DEV-094 (MIG7c, GR10): where an NPC stands is session state, the place the host last walked or turned it to. A host built for the map
    // (after a load, a map change or a window move) stands each of its NPCs there again; an NPC with no record is on its map data's cell.
    private void RestoreMovers() => RestorePlaces(null);

    // ARC-DEV-119: only = the NPCs a window move added to a standing host (their places are restored as a built host would); null: every NPC.
    private void RestorePlaces(ISet<string>? only)
    {
        foreach (var place in _session.QueryNpcPlaces(new SessionMapId(_scene.RuntimeMap)))
        {
            if ((only is not null && !only.Contains(place.Entity)) || !_npcs.TryGetValue(place.Entity, out var npc) || Gone(place.Entity)) continue;
            // A place the shown map cannot hold (outside the chart, not admitted: a window moved since, or a damaged value) or the hero's own cell is
            // not restored: the NPC stands on its map data's cell, as before the place was state.
            // ARC-INPUT-1946: a body the hero pulled onto its own cell is restored there (only a dead NPC may lie on the hero's cell).
            if (!PlaceHeld(place.World, Status(place.Entity) is { IsDead: true }))
            {
                Trace(npc.Entity).Step = $"place {place.World.X},{place.World.Y} not restored";
                continue;
            }
            Movers.Restore(npc.Entity, npc.Row.Key, npc.Origin, place.World, place.Facing);
        }
    }

    /// <summary>
    /// ARC-DEV-206 (B25, GR12, RL6): the location notification of a relocation the host made for an NPC (a step into a cell, a knockback's slide, a route's relocation): the original runs the tile's
    /// scripts for a PC or an NPC after every move, so the viewer is told the cell to run the shown map's tile script for the NPC (<see cref="AreaAiNoticeKind.Entered"/>). A follower is not told:
    /// the companion drive runs its tile scripts (ARC-SCRIPT-046). The wading test and a new sector's script and point 27 are not part of this slice.
    /// </summary>
    private void NoteEntered(string id, CellCoord cell)
    {
        if (!_followers.ContainsKey(id)) _notices.Add(new(AreaAiNoticeKind.Entered, id, null) { Cell = cell });
    }

    // The mover's cell and facing into the session (one revision, none when it is already recorded). A refusal only leaves the NPC's place unsaved.
    private void RecordPlace(string id)
    {
        if (Movers.Of(id) is not { } mover) return;
        var result = _session.RecordNpcPlace(_session.Version, new SessionMapId(_scene.RuntimeMap), id, mover.Cell, mover.Facing);
        if (result.Failure != GameSessionFailure.None) Trace(id).Step = $"place not recorded: {result.Failure}";
    }

    public AreaAiScene Scene => _scene;
    public AreaAiMovers Movers { get; } = new();
    public GameSessionOwner Session => _session;

    /// <summary>The NPC's run now, or null: ARC-DEV-134 (MIG13 S4), the first of its runs in the session's goal table in slot order (the host starts one run per NPC;
    /// the table could hold overlays beside it, which no starter of the host makes yet).</summary>
    // ARC-DEV-143: the NPC's own runs; an overlay (an eye candy or the hover) beside them is not read as the NPC's goal here, so a spell's eye candy in a lower slot does not hide a held cast or a walk from the host's queries (GS4's first-run shadowing is the table's, kept for MIG15).
    public GoalRun? RunOf(string npc) => _session.QueryGoals().RunsOf(npc).FirstOrDefault(r => !AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal));

    /// <summary>ARC-DEV-136 (MIG13 S5b, GT3): every run of the NPC in the session's table, in slot order: the runs the executor's equal-goal and alias tests read (the original's 0x44E710 walks every run;
    /// the host starts no overlay yet, so this is the one run it makes).</summary>
    public IReadOnlyList<GoalRun> RunsOf(string npc) => _session.QueryGoals().RunsOf(npc);

    // The runs of the table that are not done (a run marked done during its own step is ending).
    private IEnumerable<GoalRun> LiveRuns() => _session.QueryGoals().Runs.Where(r => !r.Done);

    // The owners of the live runs, in slot order, each once.
    // ARC-DEV-138 (MIG13 S7): a door's run (70, 71, 72) is the door host's, not an NPC's: a follower joining or a map's AI turned off retires the NPCs' runs and leaves the doors' alone.
    // ARC-DEV-146 (MIG13 S10b): nor are the hero's runs (its walk is the hero host's): turning the AI off leaves the hero walking.
    private string[] RunOwners() => [.. LiveRuns().Where(r => r.Owner != Pc && !AiExecutorRules.IsObjectRunGoal(r.Bottom.Goal)).Select(r => r.Owner).Distinct(StringComparer.Ordinal)];

    /// <summary>ARC-DEV-138 (MIG13 S7): where the end nodes of a door's run go when this host drains one the session ended by itself (a map's close): the door host's. The doors are not this host's
    /// (they run with the AI off too), but the session's ends are drained by one consumer.</summary>
    public Action<GoalRunEnd>? DoorEnds { get; set; }

    /// <summary>ARC-DEV-139 (MIG13 S8): where the end nodes of a spell missile's run go when this host drains one the session ended by itself (an interrupt, a map's close): the missile host's. A missile is a created
    /// object that owns its run (GT52), not an NPC's, and its host runs with the AI off too.</summary>
    public Action<GoalRunEnd>? MissileEnds { get; set; }

    /// <summary>ARC-DEV-140 (MIG13 S8b): where the end nodes of a projectile's run go when this host drains one the session ended by itself (a map's close): the projectile host's. A projectile is a created object that owns its
    /// run (GT55), not an NPC's, and its host runs with the AI off too.</summary>
    public Action<GoalRunEnd>? ProjectileEnds { get; set; }

    /// <summary>ARC-DEV-141 (MIG13 S8c): where the end of a knockback's run goes when this host drains one the session ended by itself (an interrupt, a map's close): the knockback host's. The run is the pushed NPC's own,
    /// but its goals have no end nodes (GT60) and its host steps it.</summary>
    public Action<GoalRunEnd>? KnockbackEnds { get; set; }

    /// <summary>ARC-DEV-146 (MIG13 S10b): where the end nodes of the hero's walk or turn run go when this host drains one the session ended by itself (a round's start sweep, a map's close): the hero host's, which stops the route.
    /// The hero's runs are not this host's; the session's ends are drained by one consumer.</summary>
    public Action<GoalRunEnd>? HeroEnds { get; set; }

    /// <summary>ARC-DEV-141: the end nodes of a run the table ended from outside (a start's interrupt, a sweep) that a host other than this one caused, run as this host's own ends are.</summary>
    public void RunEndNodes(GoalRunEnd end) => EndNodes(end);

    /// <summary>
    /// ARC-DEV-302 (GT146, GT147, 0x44E4D0 with the author −1): an ObjFlag component set entangled or paralysed on <paramref name="npc"/>: its runs whose bottom goal is 3 (the walk) or whose
    /// current goal is 61 (the melee approach) are interrupted at level 6, each with its end nodes. No goal starts and no turn ends. Answers how many runs ended (0 for an NPC this host does not run).
    /// </summary>
    public int InterruptForHold(string npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (!_npcs.ContainsKey(npc) && !_followers.ContainsKey(npc)) return 0;
        var ended = 0;
        foreach (var run in HoldInterrupt.Runs(_session.QueryGoals(), npc))
        {
            var change = _session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel);
            EndNodes(change.Steps);
            if (change.Succeeded) ended++;
        }
        return ended;
    }

    /// <summary>ARC-DEV-138: the world tiles of the living NPCs and companions this host runs (the hero's is the viewer's), for a door's pending close: a critter on the door's own tile keeps it open.</summary>
    public IEnumerable<CellCoord> LiveCritterTiles()
    {
        foreach (var id in _npcs.Keys.Concat(_followers.Keys).ToArray())
            if (!Gone(id) && !DeadCritter(id) && Tile(id) is { } tile) yield return tile;
    }

    /// <summary>What the overlay shows of an NPC: its last pass, perform, run step and reason.</summary>
    public AreaAiTrace? TraceOf(string npc) => _traces.GetValueOrDefault(npc);

    /// <summary>The subturn start whose subturn hook the round host ran last (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/>),
    /// -1 before any; a host built during a round asks the NPC whose subturn it is again.</summary>
    public long HookedSubturn { get; set; } = -1;

    /// <summary>The round's events of the host's own session calls since the last call, in order.</summary>
    public IReadOnlyList<SessionCombatEvent> DrainRoundEvents()
    {
        var drained = _roundEvents.ToArray();
        _roundEvents.Clear();
        return drained;
    }

    /// <summary>
    /// ARC-WORLD-095: follows the session's turn-based round. A round that began since the last call interrupts every run (the round's
    /// start 0x4B71E0: the session's sweep of the table, 0x424310, then each participant's runs, 0x424070, both at level 3); a subturn that started since then interrupts the runs
    /// of the critter of the previous turn set when that is an NPC (the turn set 0x4B6E70, 0x424070 at level 3). ARC-DEV-135 (MIG13 S5): the table applies the rule
    /// (0x44E2C0: level 3 ends the runs whose current goal has priority 3 or less, walks, attacks and flights; an attempted cast's 4 goes on, a no-interrupt overlay is left, a
    /// priority-5 goal refuses) and the end nodes of every run it ended run here. ARC-DEV-136 (MIG13 S5b, TL39, TH10): the round start's interrupts come before the animation system's callback
    /// is registered and the round flag set, so they call nothing (<see cref="GameSessionOwner.SuppressRoundCallback"/>); a turn set's interrupt that frees the last significant goal calls the
    /// callback itself (the table does, <see cref="GameSessionOwner.AnimationsDone"/>), with the new participant and its action points, before its hook. Called by the pump before each step and
    /// after the last, and by the round host.
    /// </summary>
    public void ObserveRound()
    {
        if (_session.QueryCombatRound() is not { } round) { _roundSeen = false; return; }
        if (!_roundSeen)
        {
            _roundSeen = true;
            using (_session.SuppressRoundCallback())
            {
                EndNodes(_session.SweepGoalsForRoundStart().Steps);
                foreach (var participant in round.Participants)
                    if (Id(participant) is { } who && who != Pc) InterruptRuns(who, GoalInterruptRules.OrdinaryActions);
            }
        }
        var starts = _session.QueryCombatSubturnStarts();
        if (starts == _subturnsSeen) return;
        _subturnsSeen = starts;
        var previous = _turnSetActor;
        _turnSetActor = Id(round.Actor);
        // The previous critter's runs end; their frees call the callback (the stall detector sees the new participant before its hook, TH10, and the event is asked for).
        // A check that passes the turn on (an actor seen again with unchanged action points) is the next observation's turn set.
        if (previous is not null && previous != Pc) InterruptRuns(previous, GoalInterruptRules.OrdinaryActions);
    }

    /// <summary>The notices since the last call, in order.</summary>
    public IReadOnlyList<AreaAiNotice> DrainNotices()
    {
        var drained = _notices.ToArray();
        _notices.Clear();
        return drained;
    }

    // ---- the viewer's calls ----

    /// <summary>
    /// Schedules a delayed AI tick (0x4AD6E0, as for a loaded NPC) for every NPC of the map that has none pending and is not dead in
    /// the session; refused schedules are counted. Called when the map's AI starts and after a load. ARC-DEV-105 (MIG9 S5, RL16, RL17): a map
    /// that has a sector cache starts no heartbeat chain for a map NPC whose sector is not loaded (a chain starts only at creation, resurrection,
    /// the fold and its own reschedule; opening a map starts none, and the fold's first ticks came before this call); the companions here are
    /// created with the map's host, so they get theirs whatever their sector.
    /// </summary>
    public int ScheduleTicks()
    {
        var refused = 0;
        foreach (var npc in _scene.Npcs.Concat(_followers.Values))
        {
            if (_session.QueryAiTick(npc.Entity) is not null || Life(npc.Entity) is null) continue;
            if (!_followers.ContainsKey(npc.Entity) && !SectorLoaded(npc.Entity)) continue;
            var result = _session.ScheduleAiTick(_session.Version, new SessionAiTickRequest
            {
                Entity = npc.Entity, Start = SessionAiTickStart.Delayed, DistanceToPc = DistanceToPc(npc.Entity),
            });
            if (result.Disposition == GameSessionDisposition.Rejected) refused++;
        }
        return refused;
    }

    /// <summary>ARC-DEV-124: the world milliseconds of the last frame or goal step (<see cref="Frame"/>, <see cref="StepGoal"/>), the clock the movers' steps and the attack
    /// plays are timed on; the viewer reads it to draw them, so a pause that stops the frames stops their drawing too.</summary>
    public long NowMs => _nowMs;

    /// <summary>The goal steps the host took since the last call (the frame participant's count), and clears it.</summary>
    public int TakeSteps() { var steps = _steps; _steps = 0; return steps; }

    /// <summary>
    /// ARC-DEV-134 (MIG13 S4): the host's part of a frame, once per frame after the frame step's passes and goal steps (<see cref="AreaAiFrameParticipant"/>, not while
    /// paused): the movers' moves that ended by <paramref name="nowMs"/> settle, the companions' leaves are seen and the round is observed after the last step (a step that
    /// began a round or passed the subturn on interrupts runs as the original's round start and turn set do). The runs no longer step here: each is a session run
    /// whose <c>goal.step</c> fires at its deadline on the animation clock and is stepped by <see cref="StepGoal"/>.
    /// </summary>
    public void Frame(long nowMs)
    {
        if (_lastFrameMs >= 0 && nowMs > _lastFrameMs) _frameLengthMs = Math.Clamp(nowMs - _lastFrameMs, 1, 250);
        _lastFrameMs = Math.Max(_lastFrameMs, nowMs);
        _steppedThisFrame.Clear();
        _nowMs = Math.Max(_nowMs, nowMs);
        ClaimSessionEnds();
        DrainFleeInterrupts();
        Movers.Settle(_nowMs);
        ObserveLeaves();
        ObserveRound();
    }

    // ARC-DEV-134: the runs the session ended by itself (a map's close, a departure, a lost step, a refused step close: GameSessionOwner.DrainGoalEnds) are run
    // through the host's end nodes, as the former host's own drop of them did (ARC-WORLD-102: no NPC's magic run outlives its goal). The hero's runs are not the host's.
    private void ClaimSessionEnds()
    {
        foreach (var end in _session.DrainGoalEnds()) EndNodes(end);
    }

    // ARC-DEV-135 (MIG13 S5, GS24): the end nodes of every run the table ended from outside the stepper (a start's interrupts, an interrupt, a sweep, a kill, the session's own ends),
    // top down. No subturn hook, no idle restart and no AP charge follow them: those are the stepper's own end (StepHooks). A run only marked done, the one being stepped, runs
    // them at once as well (its step's close frees it); a reset's end runs none (GS16). The hero's runs are not the host's.
    private void EndNodes(IEnumerable<GoalInterruptStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Ended is { } end) EndNodes(end);
            else if (step.Outcome == GoalInterruptOutcome.MarkedDone && _session.QueryGoals().TryGet(step.Handle, out var marked) && marked.Owner != Pc && !AiExecutorRules.IsObjectRunGoal(marked.Bottom.Goal)) EndDroppedRun(marked);
        }
    }

    private void EndNodes(GoalRunEnd end)
    {
        if (AiExecutorRules.IsDoorRunGoal(end.Run.Bottom.Goal)) { DoorEnds?.Invoke(end); return; }
        if (AiExecutorRules.IsMissileRunGoal(end.Run.Bottom.Goal)) { MissileEnds?.Invoke(end); return; }
        if (AiExecutorRules.IsProjectileRunGoal(end.Run.Bottom.Goal)) { ProjectileEnds?.Invoke(end); return; }
        if (AiExecutorRules.IsKnockbackRunGoal(end.Run.Bottom.Goal)) { KnockbackEnds?.Invoke(end); return; }
        // ARC-DEV-142: a scenery's loop has no modelled end node (the ambient sound is not played), and its owner is no NPC. ARC-DEV-143: nor has an overlay (an eye candy or the hover), whose owner is any object.
        if (AiExecutorRules.IsSceneryRunGoal(end.Run.Bottom.Goal) || AiExecutorRules.IsOverlayRunGoal(end.Run.Bottom.Goal)) return;
        // ARC-DEV-146: the hero's walk (goal 3 or 4 of the owner pc) and turn (53) have the hero host's end nodes (the route stops, the facing snaps); the hero has no other run on the table yet.
        if (end.Run.Owner == Pc) { if (AreaHeroRunsHost.IsHeroRun(end.Run)) HeroEnds?.Invoke(end); return; }
        if (end.Kind != GoalEndKind.Reset) EndDroppedRun(end.Run);
    }

    /// <summary>
    /// ARC-DEV-134 (MIG13 S4, goal-table.md §3.2): one step of a session run whose <c>goal.step</c> came due (<see cref="IGoalStepHost"/>), at the clocks of its deadline.
    /// Before it the movers settle, the leaves are seen and the round is observed (a step that began a round or passed the subturn interrupts runs, this one too: then it
    /// ends without stepping, its end nodes run by the interrupt). A run of a map NPC whose sector the session's cache has evicted waits a frame (the NPC is not alive there,
    /// RL16) and goes on when the sector loads; a map without a cache holds none back (ARC-DEV-119). A run steps at most once a frame (the former pump's cadence: a
    /// delay shorter than the frame, 0 among them, is the next frame), so one that comes due again within its frame waits for the next. Otherwise the world runtime's stepper steps it
    /// (<see cref="GoalStepper.Step"/>) over this host's node ports and end hooks, and its answer goes back to the session: the next frame delay in real milliseconds
    /// (the session queues it ×8 on the animation clock from now, TL40), or the end.
    /// </summary>
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        _nowMs = Math.Max(_nowMs, clocks.WorldMs);
        ClaimSessionEnds();
        Movers.Settle(_nowMs);
        ObserveLeaves();
        ObserveRound();
        if (Ended(run.Handle)) return SessionGoalStepAnswer.End;
        if (!_followers.ContainsKey(run.Owner) && !SectorLoaded(run.Owner)) return SessionGoalStepAnswer.After(NextFrameDelay(clocks));
        // Stepped in this frame already: it waits for the next one. The guard holds only while the host's frame runs (its participant is bound, as in the viewer).
        if (_lastFrameMs >= 0 && _steppedThisFrame.Contains(run.Handle)) return SessionGoalStepAnswer.After(NextFrameDelay(clocks));
        _steppedThisFrame.Add(run.Handle);
        var npc = run.Owner;
        GoalStepperResult stepped;
        _ending = run.Handle;
        var outermost = _stepping.Add(npc);
        try { stepped = GoalStepper.Step(run, this, _session.AiRolls, _session.Rules.Combat, new StepHooks(this), TraceOf(npc)?.Reason ?? AiExecutorReason.None); }
        finally { _ending = null; if (outermost) _stepping.Remove(npc); }
        _steps++;
        var step = stepped.Step;
        Trace(npc).Step = step is null ? $"{stepped.End}" : $"{step.End} {step.Reason} nodes={step.Nodes} delay={step.DelayMs}";
        if (step is not null) Trace(npc).Reason = step.Reason;
        // A node that ended the run from outside (an interrupt or a drop of its owner) ran its end nodes: the session's close ends the marked run.
        return Ended(run.Handle) ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    // A wait of one frame: to just past this frame's expected end (the last frame's end plus its length), or a whole frame's length when this frame already ran
    // longer, so a waiting run does not wake within its frame (each wake is a sub-step of the frame step's budget).
    private long NextFrameDelay(FrameClocks clocks)
    {
        var remaining = _lastFrameMs + _frameLengthMs + 1 - clocks.WorldMs;
        return _lastFrameMs >= 0 && remaining >= 1 ? remaining : _frameLengthMs;
    }

    // Whether the run is gone or marked done in the table (an interrupt during its step).
    private bool Ended(GoalHandle handle) => !_session.QueryGoals().TryGet(handle, out var now) || now.Done;

    // The stepper's end hooks (IGoalStepHooks), this host's: the end nodes of a dropped run, the queued casts an end step leaves, the fidget after a done run.
    private sealed class StepHooks(AreaAiHost host) : IGoalStepHooks
    {
        public void EndDropped(GoalRun run) => host.EndDroppedRun(run);

        // ARC-WORLD-104: the end step runs the end node of every goal (QC5); a queued goal 26's (0x42B090) frees nothing (MG43).
        public void EndStepped(GoalRun run) => host.FreeQueuedCasts(run);

        // ARC-WORLD-099: the stepper's end of a done run (TH4): the fidget's call (0x4B4320), after the owner's subturn hook. The round actor's hook is the round
        // host's next frame (FidgetAfterHook); anyone else's hook does nothing.
        public void Restart(GoalRun run)
        {
            if (host.InRound && host.RoundActor == run.Owner) host._fidgetAfterHook = run.Owner;
            else host.Fidget(run.Owner);
        }
    }

    /// <summary>
    /// The attack event of the hero's attack on the NPC <paramref name="victim"/> (0x4A9650, <see cref="HostilityRules.AttackEvent"/>):
    /// the session's who-hit-me-last and team writes, the reaction change toward the hero, the victim's turn and its reaction to the
    /// attack (fight or flee, <see cref="MoraleRules.ReactToAttack"/>) through the setter, and the witnesses of the noise
    /// (<see cref="HostilityRules.NoiseWitnesses{T}"/>, <see cref="HostilityRules.Witness"/>). ARC-WORLD-115: the attacker's and the
    /// victim's followers are told (<see cref="TellFollowers"/>).
    /// </summary>
    /// <remarks>ARC-COMBAT-1930 (VCG9): <paramref name="loudness"/> is the weapon's (<see cref="AwarenessRules.WeaponLoudness"/>), which sets the noise radius of the witnesses; a stray strike (accidental) raises no noise.</remarks>
    public void HeroAttacked(string victim, Loudness loudness = Loudness.Normal) => AttackEvent(Pc, victim, loudness: loudness);

    /// <summary>
    /// ARC-AI-1516 (EX14, VBG5): the attack event of the hero's missile that met <paramref name="victim"/> on its way, not the aimed target: the accidental
    /// bit is set (no noise; no who-hit-me-last write and no reaction to the attack between one faction; -10 in place of the lowered reaction toward the hero).
    /// </summary>
    public void HeroStrayAttacked(string victim) => AttackEvent(Pc, victim, AttackEventFlags.Accidental);

    /// <summary>
    /// ARC-COMBAT-2164 (EX15, 0x4B3170): the attack event of the hero's attack that does not fly when the line of fire struck <paramref name="blocker"/> in place of the aimed <paramref name="aimed"/> (null: the aimed target is no critter): the
    /// aimed target's, deliberate, in the Original setting, the blocker's with the accidental bit in Steamwoven (<see cref="HostilityRules.RangedBlockerNotice"/>).
    /// </summary>
    public void HeroBlockerAttacked(string? aimed, string blocker, Loudness loudness = Loudness.Normal) => BlockerAttackEvent(Pc, aimed, blocker, loudness);

    private void BlockerAttackEvent(string attacker, string? aimed, string blocker, Loudness loudness)
    {
        var (victim, flags) = HostilityRules.RangedBlockerNotice(_session.Rules.Combat, aimed, blocker);
        if (victim is not null) AttackEvent(attacker, victim, flags, loudness);
    }

    /// <summary>
    /// ARC-DEV-281 (B08, TA16): the AI notice of an aggressive spell (0x453EE0, 0x453F20): the attack event (0x4A9650, loudness 1, no
    /// flags) of <paramref name="attacker"/> (the hero as <c>pc</c>, an NPC by its entity) on <paramref name="victim"/>, whatever the
    /// distance between them (the chain tests none for the victim's own reaction; read in the executable on 2026-11-08). A caster or a
    /// victim this host does not hold ends at the attack event's own early exits.
    /// </summary>
    public void SpellAttacked(string? attacker, string victim)
    {
        if (attacker is not null) AttackEvent(attacker, victim);
    }

    /// <summary>
    /// ARC-DEV-292: the immediate AI pass of a struck NPC (0x4A84F0, called at once from the damage step 0x4B4390 at 0x4B52FC after a critter's damage, and from the ranged and melee miss paths 0x4B30F5 and 0x4B3948): one
    /// pass of <paramref name="victim"/> through the session (<see cref="GameSessionOwner.RunAiPass"/>) with no tick and no distance test, so the NPC reacts at the hit and not at its next tick (up to 5 s later). The original calls it
    /// for an NPC victim only (object type 16: no pass for the hero) that is not dead (hit points above 0, 0x45D8D0), after the attack event (0x4A9650, so the victim already has its focus) and after the life step (a hit reaction,
    /// a stun or a fall it started stops the pass at the gate's animation test). The caller says a hit or a miss landed and the damage step ran (a refused attack, a vetoed or prevented damage reach no pass). A victim this host does
    /// not run (an NPC of a window it has not seen), the hero and a dead NPC do nothing; a pass of the victim already running here is not started again (the original has no such guard; a project choice against endless recursion).
    /// Returns whether a pass ran.
    /// </summary>
    public bool Struck(string victim)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (victim == Pc || !TryNpc(victim, out _) || Life(victim) is null || _stepping.Contains(victim) || !_striking.Add(victim)) return false;
        try
        {
            var result = _session.RunAiPass(_session.Version, victim, this);
            Trace(victim).Struck = $"{result.Failure} gate={result.Pass?.Gate?.Stop} performed={result.Pass?.Performed}";
            return result.Failure == GameSessionFailure.None;
        }
        finally { _striking.Remove(victim); }
    }

    /// <summary>
    /// ARC-DEV-303 (RS10): the hit effect (0x4B6930 → 0x459A30) of an attack that hit a PC or NPC: the victim's entangle asks its save again through the cast host
    /// (<see cref="SessionSpellCaster.CritterHit"/>). Only a hit that reached the victim counts (<see cref="HitReached"/>); a miss, a refused attack or a shot still in flight does nothing.
    /// </summary>
    public void HitEffect(string victim, SessionAttackResult? result, int? weapon = null)
    {
        // The cast host is asked only for a hit that reached the victim: its factory rebuilds a stale host (and clears the eye candy), which a miss must not do.
        if (AttackEndOf(result) is not null) AttackEnd(_scene.Spells?.Invoke(), victim == Pc ? SessionSpellCaster.Hero : victim, result, weapon);
    }

    /// <summary>
    /// ARC-RENDER-1794 (ledger ER5): the attack end of a weapon's attack on the PC or NPC <paramref name="victim"/> (the cast host's id, <see cref="SessionSpellCaster.Hero"/> for the hero): a hit that reached the victim
    /// (<see cref="HitReached"/>) runs the damage effect with the blood of the hit, or the hit effect when no slot is positive or the hit was critical (<see cref="SessionSpellCaster.AttackEnd"/>); a miss and a refused attack do nothing.
    /// ARC-RENDER-1990: <paramref name="weapon"/> is the prototype number of the attack's weapon (null: none or unnamed), whose own hit effect, when it plays, ends the attack end before either.
    /// </summary>
    public static void AttackEnd(SessionSpellCaster? caster, string victim, SessionAttackResult? result, int? weapon = null)
    {
        if (caster is null || AttackEndOf(result) is not { } end) return;
        caster.AttackEnd(victim, end.Damage, end.Critical, weapon);
    }

    /// <summary>
    /// ARC-RENDER-2011 (ledger RH9): the NPC's weapon's own effect of <paramref name="kind"/> (<see cref="WeaponHitEffectRules.Begin"/> at the attack's start, <see cref="WeaponHitEffectRules.Swing"/> in the strike) for <paramref name="weapon"/>, the prototype
    /// number it wielded when the attack began (read before the strike, which a critical may take it out of the hand; null: none, no effect), on the NPC itself, turned to <paramref name="toward"/>, the tile it attacks (an attacker faces its target;
    /// the movers' facing is not kept for a follower, which the viewer walks). The cast host is asked only for an armed attacker, since its factory rebuilds a stale host.
    /// </summary>
    private void WeaponAttackEffect(string id, int kind, int? weapon, CellCoord? toward)
    {
        if (weapon is not { } prototype || _scene.Spells?.Invoke() is not { } caster) return;
        var facing = Tile(id) is { } here && toward is { } there && AreaCombatHost.Facing(here, there) is { } turned ? turned : Movers.FacingOf(id, NeighborDirection.XPlus);
        caster.WeaponAttackEffect(id, kind, prototype, GridActorMath.Octant(facing));
    }

    /// <summary>ARC-RENDER-1990: the prototype number (<see cref="CombatObjectFields.Prototype"/>) of the weapon an NPC wields now, null when it wields none or its worn items are unknown.</summary>
    private int? WieldedPrototype(string id) =>
        _scene.Combat.Worn(_session, id)?.FirstOrDefault(item => item.Slot == EquipmentSlot.Weapon && item.Entity is not null) is { Entity: not null } weapon ? weapon.Fields.Object.Prototype : null;

    /// <summary>ARC-SCRIPT-2342 (AR2): what the NPC wears on its body-armour slot now, as get armor reads it (<see cref="AreaScriptWorld.NpcBodyArmor"/>): its use art, or none when nothing is worn there; null when the combat host cannot say.</summary>
    public AreaScriptWornArmor? WornBodyArmor(string id) =>
        _scene.Combat.Worn(_session, id) is { } worn ? new(worn.Where(item => item.Slot == EquipmentSlot.Armor && item.Entity is not null).Select(item => item.Fields.Item?.UseArtFragment).FirstOrDefault()) : null;

    /// <summary>ARC-RENDER-1794: what the attack end reads of an attack result: the damage step the victim took (null when the hit had none or the damage fell on the attacker) and the packet's critical flag; null when the attack
    /// is no hit that reached its victim (<see cref="HitReached"/>).</summary>
    public static (AttackDamage? Damage, bool Critical)? AttackEndOf(SessionAttackResult? result) =>
        HitReached(result) ? (result!.Resolution!.Damage is { Recipient: DamageRecipient.Target } damage ? damage : null, result.Resolution.Outcome == AttackOutcome.CriticalHit) : null;

    /// <summary>ARC-DEV-303: whether an attack result is a hit that reached its victim (<see cref="VictimReached"/> with the hit or critical-hit outcome): the attack end's hit flag (0x4B6930 tests it).</summary>
    public static bool HitReached(SessionAttackResult? result) =>
        VictimReached(result) && result!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;

    /// <summary>
    /// ARC-DEV-292: whether an attack result is one that reached the victim's hit or miss path (<see cref="Struck"/>): the attack was resolved (not refused), no shot is still in flight (its strike is the damage step), the victim's
    /// taking-damage script did not cancel the victim's damage step, the session's steps did not fail, and the damage to the victim was not prevented (an invulnerable one or a wall ends the damage step before the pass). A miss
    /// has no damage and always reaches it; a critical miss that hit the attacker reaches the victim's miss path whatever became of the attacker's own damage (its veto or failure concerns the attacker's damage step).
    /// </summary>
    public static bool VictimReached(SessionAttackResult? result) =>
        result is { Disposition: not GameSessionDisposition.Rejected, Resolution: { Refusal: AttackRefusal.None } resolution } && result.Shots.Count == 0
        && (resolution.Damage is not { Recipient: DamageRecipient.Target } ? true : !result.Vetoed && result.StepFailure == GameSessionFailure.None && resolution.Damage.Prevented == DamagePrevention.None);

    /// <summary>
    /// ARC-WORLD-099: the subturn ends among <paramref name="events"/> (a round call's): an NPC's end runs its end-combat script (point 16,
    /// 0x4B7630: the NPC as attachee and triggerer; the PC's end calls the interface's callback instead). This host runs no SCR at the AI
    /// points (<see cref="RunScript"/> continues), so only the trace counts it. The host passes the events of its own round calls (the
    /// executor's consumes and subturn ends, a turn set's completion check); the round host those of its calls.
    /// </summary>
    public void SubturnsEnded(IEnumerable<SessionCombatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var ended in events)
            if (ended is { Kind: SessionCombatEventKind.SubturnEnded, Combatant: { IsHero: false } critter } && Id(critter) is var npc)
            {
                RunScript(AiScriptPoint.EndCombat, npc, npc);
                Trace(npc).EndCombatScripts++;
            }
    }

    /// <summary>ARC-WORLD-099: the fidget call of the round actor's done run, which the stepper makes after the subturn hook the round host
    /// ran for <paramref name="npc"/> since (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/>);
    /// nothing when its run's end left none pending.</summary>
    public void FidgetAfterHook(string npc)
    {
        if (_fidgetAfterHook != npc) return;
        _fidgetAfterHook = null;
        Fidget(npc);
    }

    // The fidget after a done run (0x4B4320): an NPC in combat mode outside a round, or auto-animating, without a run. Its art test
    // (0x503E20) allows it: NPC ART is not read yet. The fidget's run (goal 2, priority 1) is not held: the round's completion test does
    // not count it, every start replaces it, and the subturn hook, the only reader of "a run runs" (0x423300), never meets it, since a
    // round starts none.
    private void Fidget(string npc)
    {
        // ARC-DEV-272 (TH9, GS28): the hero fidgets too, in combat mode outside a round (its critter flags are no auto-animate source).
        var critter = npc == Pc ? null : Fields(npc)?.Critter;
        if (npc != Pc && critter is null) return;
        var combatMode = _session.QueryCombatMode(npc == Pc ? SessionCombatant.Hero : SessionCombatant.Critter(npc));
        // ARC-DEV-134: the run being ended is still in the table until its step closes; it is not the NPC's animation any more.
        var animating = LiveRuns().Any(r => r.Owner == npc && r.Handle != _ending && !AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal));
        if (!AiExecutorRules.FidgetAfterRun(this, npc, combatMode, critter is not null && (critter.Flags2 & AutoAnimates2) != 0, animating, artAllows: true)) return;
        _notices.Add(new(AreaAiNoticeKind.Fidget, npc, null));
    }

    /// <summary>
    /// ARC-DEV-272: the idle restart (0x4B4320) after a run the stepper freed as done and no AI-host step ended: a hero's run (<see cref="AreaHeroRunsHost"/>) or the push of a knocked-back NPC (<see cref="AreaKnockbackHost"/>).
    /// The run being freed (<paramref name="ending"/>) is still in the table, so it is not the owner's animation; the same fidget as after an NPC's own run (a notice).
    /// </summary>
    public void IdleRestart(string owner, GoalHandle ending)
    {
        var before = _ending;
        _ending = ending;
        try { Fidget(owner); }
        finally { _ending = before; }
    }

    /// <summary>
    /// ARC-WORLD-102: ends every run as a dropped one (its end nodes, <see cref="EndDroppedRun"/>: a casting goal's magic run is freed,
    /// so no NPC's spell run outlives its goal), then <see cref="Reset"/>. The viewer calls it before it drops or replaces the host of
    /// the same session (a follower joining or leaving, a map change, the AI turned off).
    /// </summary>
    public void Retire()
    {
        ClaimSessionEnds();
        foreach (var npc in RunOwners()) DropRun(npc);
        Reset();
    }

    /// <summary>Forgets the host's own state and movers (a load: the session's goal table belongs to the restored session, which starts with no run; ARC-DEV-134:
    /// the runs are the session's, so the host holds none to clear).</summary>
    public void Reset()
    {
        _steps = 0; _ending = null; _steppedThisFrame.Clear(); _lastFrameMs = -1; _frameLengthMs = 16;
        _traces.Clear();
        _pickupItems.Clear();
        _notices.Clear();
        _roundEvents.Clear();
        _roundSeen = false; _subturnsSeen = -1; _turnSetActor = null; _pendingCost = 0; _pendingAt = -1; HookedSubturn = -1; _fidgetAfterHook = null;
        _passChoices.Clear(); _passSounds.Clear(); _listCache = AiListCache.Empty; _castFacing.Clear(); _noRelease.Clear(); _queuedCasts.Clear(); _launched.Clear(); _playedCasts.Clear();
        _attackPlays.Clear(); _shown.Clear(); _shownRevision = -1;
        Movers.Clear();
        RestoreMovers();
        RestoreQueuedCasts();
    }

    /// <summary>
    /// ARC-DEV-119 (MIG9 S5c, K1 §3.5): the map's window moved, so the viewer built the scene again (the chart, the evidence, the rows the new window holds, the
    /// companion group and every provider) while the host goes on: its goal runs, traces, movers, round state and pending ticks are the map's, not the
    /// window's. The NPCs the host runs become those of every window it has seen: a row the new window holds replaces the earlier one (its record key is the new
    /// window's, and so is its mover's), an NPC the new window no longer holds keeps its row (it still walks, fights and ticks while its sector is loaded). The companions
    /// are the new group's. Casting runs end first (<see cref="ReleaseCasts"/>), since the window's cast host and eye candy are gone. Another map is a new host.
    /// </summary>
    public void Rebind(AreaAiScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.RuntimeMap != _scene.RuntimeMap) throw new ArgumentException("A host serves one map; another map is a new host.", nameof(scene));
        ReleaseCasts();
        var followers = (scene.Followers?.Npcs ?? []).ToDictionary(n => n.Entity, StringComparer.Ordinal);
        // The NPCs the earlier windows gave that stand on their map data's cell and run nothing are forgotten (a window that holds them again gives their rows back): the
        // host keeps the NPCs that act, so the set stays that of the windows near the hero and the ones walking.
        foreach (var entity in _npcs.Keys.ToArray())
            if (!_created.Contains(entity) && !AnimationRunning(entity) && Movers.Of(entity) is null && scene.Npcs.All(n => n.Entity != entity)) _npcs.Remove(entity);
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in scene.Npcs) { if (!_npcs.ContainsKey(npc.Entity)) added.Add(npc.Entity); _npcs[npc.Entity] = npc; }
        foreach (var companion in followers.Keys) _npcs.Remove(companion);
        _followers.Clear();
        foreach (var (entity, npc) in followers) _followers[entity] = npc;
        foreach (var npc in scene.Npcs) if (_npcs.ContainsKey(npc.Entity)) Movers.Rekey(npc.Entity, npc.Row.Key, npc.Origin);
        _led.RemoveWhere(entity => !_followers.ContainsKey(entity));
        foreach (var gone in _npcLeaders.Keys.Where(entity => !_npcs.ContainsKey(entity)).ToArray()) _npcLeaders.Remove(gone);
        _scene = scene with { Npcs = [.. _npcs.Values] };
        // A row the host did not hold stands where the session recorded the NPC, as in a host built for it (the first step would overwrite the place).
        if (added.Count > 0) RestorePlaces(added);
        _listCache = AiListCache.Empty; _shown.Clear(); _shownRevision = -1;
    }

    /// <summary>
    /// ARC-DEV-359 (E1-c, TM4): the critters the session created on this host's map, as the host runs them: a critter new to the host joins its NPCs (stands where the session recorded it, else on its
    /// creation cell, as a host built for it would put it) and gets its heartbeat chain, the delayed first tick a created NPC gets (<see cref="SessionAiTickStart.Delayed"/>, no sector gate: a chain starts at creation);
    /// a critter this host ran that the list no longer holds (the session destroyed it) is dropped: its goal runs end at the highest level, its mover goes and it is no NPC of the host. A row of another
    /// map is skipped. Called whenever the session's created critters change (the viewer's frame, a load); a list with nothing new and nothing gone changes nothing. Answers the critters added.
    /// </summary>
    public int SyncCreated(IReadOnlyList<AreaAiNpc> created)
    {
        ArgumentNullException.ThrowIfNull(created);
        // A created critter that joined the party is run as a follower (Rebind takes followers out of the NPCs): it is no NPC of this list.
        var wanted = created.Where(npc => npc.Row.Key.RuntimeMap == _scene.RuntimeMap && !_followers.ContainsKey(npc.Entity)).GroupBy(npc => npc.Entity, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var changed = false;
        foreach (var entity in _created.ToArray())
        {
            if (wanted.ContainsKey(entity)) continue;
            DropRun(entity);
            _npcs.Remove(entity); _created.Remove(entity); _createdUnticked.Remove(entity); Movers.Remove(entity); _traces.Remove(entity);
            changed = true;
        }
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entity, npc) in wanted)
        {
            if (!_npcs.ContainsKey(entity)) added.Add(entity);
            _npcs[entity] = npc; _created.Add(entity);
        }
        _createdUnticked.UnionWith(added);
        if (added.Count == 0 && !changed && _createdUnticked.Count == 0) return 0;
        if (added.Count > 0 || changed)
        {
            _scene = _scene with { Npcs = [.. _npcs.Values] };
            RestorePlaces(added);
            _listCache = AiListCache.Empty; _shown.Clear(); _shownRevision = -1;
        }
        foreach (var entity in _createdUnticked.ToArray())
        {
            if (!_created.Contains(entity) || _session.QueryAiTick(entity) is not null || Life(entity) is null) { _createdUnticked.Remove(entity); continue; }
            var result = _session.ScheduleAiTick(_session.Version, new SessionAiTickRequest { Entity = entity, Start = SessionAiTickStart.Delayed, DistanceToPc = DistanceToPc(entity) });
            if (result.Disposition != GameSessionDisposition.Rejected) _createdUnticked.Remove(entity);
        }
        return added.Count;
    }

    /// <summary>Ends the casting runs as dropped ones (their end nodes free the magic run) and forgets the cast bookkeeping: what a window move takes from under them.</summary>
    public void ReleaseCasts()
    {
        foreach (var npc in LiveRuns().Where(r => r.Frames.Any(f => f.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation)).Select(r => r.Owner)
                     .Distinct(StringComparer.Ordinal).ToArray())
            DropRun(npc);
        _castFacing.Clear(); _noRelease.Clear(); _queuedCasts.Clear(); _launched.Clear(); _playedCasts.Clear();
    }

    /// <summary>
    /// ARC-MAGIC-042 (T-m1, TI6): the script action attack (36, 0x447D2F): the attack event (0x4A9650) of <paramref name="attacker"/> on <paramref name="victim"/> with flags 2 (no fan-out: the attacker's and the victim's followers
    /// are not told, a PC victim does not respond and no noise is raised) and loudness 1. The victim's own reaction follows as for any attack event: an NPC victim turns, records who hit it and reacts, a PC victim only loses its concealment. A side
    /// this host does not hold ends at the event's own early exits (<see cref="AttackEventStop.Missing"/>), and an attack on itself at <see cref="AttackEventStop.Self"/>. The attacker is the host's NPC (a created critter must be synced
    /// first, <see cref="SyncCreated"/>) or companion, or (ARC-SCRIPT-1679, 0x447D2F gives the event no type test) an object the scene describes (<see cref="AreaAiScene.ObjectFacts"/>: a trap, scenery, portal or container, which makes an NPC victim fight it,
    /// <see cref="AttackEventStop.ObjectAttacker"/>; a scene without the <c>ObjectFacts</c> hook, or an id it does not describe, such as an item, ends at <see cref="AttackEventStop.Missing"/>); returns the stop of the event's plan.
    /// </summary>
    public AttackEventStop ScriptAttack(string attacker, string victim)
    {
        ArgumentNullException.ThrowIfNull(attacker); ArgumentNullException.ThrowIfNull(victim);
        AttackEvent(attacker, victim, AttackEventFlags.NoFanOut);
        return Enum.TryParse<AttackEventStop>(Trace(victim).Event, out var stop) ? stop : AttackEventStop.None;
    }

    private void AttackEvent(string attacker, string victim, AttackEventFlags flags = AttackEventFlags.None, Loudness loudness = Loudness.Normal)
    {
        var plan = HostilityRules.AttackEvent(this, attacker, victim, flags);
        Trace(victim).Event = $"{plan.Stop}";
        // ARC-WORLD-1162 (SWB18c, HS13): a sleeping victim is woken (the re-seat) whatever the exits after it say.
        // The facts of a seated sleeper say it is not drawn, so the plan above stopped at VictimGone: once the wake took the flag away the event is asked again, as the original's later tests read the cleared flags.
        if (plan.WakesVictim && WakeFromBed(victim)) { plan = HostilityRules.AttackEvent(this, attacker, victim, flags); Trace(victim).Event = $"{plan.Stop}"; }
        if (plan.Stop is AttackEventStop.Missing or AttackEventStop.VictimProtected or AttackEventStop.VictimDead or AttackEventStop.VictimGone
            or AttackEventStop.VictimDecoy or AttackEventStop.VictimWall or AttackEventStop.Self) return;
        // ARC-WORLD-115: the attacker's followers first, right after the early exits (0x4A9B80, side attacker, the victim the other).
        if (plan.TellsAttackerFollowers) TellFollowers(attacker, victim, FollowerSide.Attacker);
        // ARC-AI-1517 (HS14, VBG6, 0x4A9650 → 0x45EE30): the victim's PC leader (or the victim), when concealed, and all its followers stop prowling, whoever attacks:
        // the hero and the followers in its follower list when the victim is the hero or a follower of it (a victim of no PC group holds no concealment here).
        if (plan.LosesConcealment is not null) _session.EndConcealmentForAttackEvent(_session.Version, Combatant(victim)!);
        // ARC-MAGIC-1142 (HS14, 0x4A9650 at 0x4A983D): the NPC victim loses the kill-on-sight override (NPC flag 0x800) before the PC victim's response, whoever it was given by (a charm, the data).
        if (plan.ClearsKosOverride) ClearKosOverride(victim);
        // A PC victim responds (0x4A9AD0): first the dialog-end callback (0x5F848C → 0x5678D0(PC, 0), GT140: an attack on the PC ends the PC's dialog, whoever the partner is), then it enters combat mode (its call of 0x4B3F50).
        if (plan.PcVictimResponds)
        {
            if (_scene.ConversationPartner?.Invoke() is { } partner) EndConversation(partner);
            HeroIntoCombatMode();
        }
        // ARC-WORLD-115: then the victim's followers (side victim, the attacker the other), before the noise.
        if (plan.TellsVictimFollowers) TellFollowers(victim, attacker, FollowerSide.Victim);
        if (plan.RaisesNoise) Witnesses(attacker, victim, loudness);
        // ARC-TECHNOLOGY-1619 (VB2-2 gap d, 0x4A9650 → 0x4AABE0): an NPC victim of an object that is no critter (a trap's shooter) is asked into COMBAT_FOCUS on it, with no alert sound, and the event returns.
        if (plan.VictimFightsObject) { SetDanger(victim, DangerState.CombatFocus, attacker); return; }
        if (plan.Stop != AttackEventStop.None) return;
        _session.ApplyAttackEvent(_session.Version, new SessionAttackEventRequest { Attacker = Combatant(attacker)!, Victim = victim, Plan = plan });
        if (plan.TurnsVictim && Tile(attacker) is { } there && TryNpc(victim, out var npc)) FaceNpc(npc, there, keepStep: true);
        if (plan.Reaction is AttackReactionChange.Deliberate or AttackReactionChange.Accidental && attacker == Pc && ReactionFacts(victim) is { } facts && Row(victim) is { } row)
            _session.ReactToPcHit(_session.Version, victim, facts.Npc, facts.Pc, new(row.FollowerHitReaction, row.HostileReaction), deliberate: plan.Reaction == AttackReactionChange.Deliberate);
        if (plan.Reaction is AttackReactionChange.FollowerDeliberate or AttackReactionChange.FollowerAccidental && attacker == Pc)
            HitFollower(victim, plan.Reaction == AttackReactionChange.FollowerDeliberate);
        if (plan.ReactsToAttack) React(victim, attacker);
    }

    // ARC-AI-1585 (0x413DF0): the accidental-attack line range, messages 0x8FC..0x95F.
    private static readonly LoyaltyLines AccidentalAttackLines = new(2300, 2399);

    /// <summary>
    /// ARC-AI-1515 (attack event 0x4A9650, the follower branch of a PC attacker, VBG4): the hero hit its direct follower. A deliberate hit changes the
    /// reaction by AI parameter 9 (<see cref="GameSessionOwner.ReactToPcHit"/>, ARC-WORLD-021's profile: added in the Original setting, taken in Steamwoven; an accidental one
    /// changes nothing); then the follow check runs with the follower limit ignored (<see cref="GameSessionOwner.CheckFollowerAfterHit"/>, 0x4AD950): a refusal makes the
    /// follower leave and sets NPC flag 0x20 (JILTED) and it says the refusal line of its reason (ARC-AI-1585: through <see cref="FollowerSpeaks"/>; the range is also traced), and a follower that stays (the check passed, or the leave was refused) says the accidental-attack line (lines 2300-2399) one time in three
    /// (0x413DF0, through <see cref="FollowerSpeaks"/>) when it is active (0x45D790; the can-speak test 0x4AD800 with flag 1 is the speaker's: its tail skips the combat focus and the interrupt, <see cref="Arcanum.Simulation.Reaction.CanSpeakRules.Tail"/>). The leave is seen by
    /// <see cref="ObserveLeaves"/> at the next look. Without the check's facts (the scene gives none) only the reaction changes.
    /// </summary>
    [OriginalFunction(0x413DF0, OriginalFunctionScope.Partial)]
    internal void HitFollower(string victim, bool deliberate)
    {
        var changed = false;
        if (deliberate && ReactionFacts(victim) is { } facts && Row(victim) is { } row)
        {
            var hit = _session.ReactToPcHit(_session.Version, victim, facts.Npc, facts.Pc, new(row.FollowerHitReaction, row.HostileReaction), deliberate: true);
            changed = hit.Adjustment is { Delta: not 0 };
        }
        if (_scene.FollowFacts?.Invoke(victim) is not { } consent)
        {
            Trace(victim).FollowerHit = $"{(deliberate ? "deliberate" : "accidental")} reaction-changed={changed} follow check: no facts";
            return;
        }
        var result = _session.CheckFollowerAfterHit(_session.Version, victim, consent);
        string outcome;
        if (result.Left && result.RefusalLines is { } lines)
        {
            var speaks = Active(victim);
            outcome = $"left jilted, {(speaks ? $"refusal lines {lines.First}-{lines.Last}" : "inactive: no refusal line")} ({result.Consent})";
            // ARC-AI-1585 (0x413A90): the refusal line of the reason is said (the viewer's line pick: its own draw of 0x413DF0's family, 0x418780).
            if (speaks) Speak(victim, lines);
        }
        else if (result.Disposition == GameSessionDisposition.Rejected) outcome = $"check refused ({result.Failure})";
        else
        {
            // The original draws on every stay (0x4CEF00) and tests the can-speak only after a hit of 1 (its line pick's own draw is not made: no line text is loaded).
            var rolled = result.Consent is not null && _session.AiRolls.Roll(AiRollPurpose.FollowerHitLine, 1, 3) == 1;
            var speaks = rolled && Active(victim);
            outcome = $"stays ({(result.Consent is { } reason ? reason.ToString() : "no check")}{(result.LeaveRefused ? ", leave refused" : "")})"
                + (speaks ? ", accidental-attack lines 2300-2399" : "");
            // ARC-AI-1585 (0x413DF0): the accidental-attack line, a random one of the lines 2300-2399.
            if (speaks) Speak(victim, AccidentalAttackLines);
        }
        Trace(victim).FollowerHit = $"{(deliberate ? "deliberate" : "accidental")} reaction-changed={changed} follow check: {outcome}";
        ObserveLeaves();
    }

    // The attack event's clear of NPC flag 0x800 on the victim (0x406D40 of field 0x118): the session's NPC flag layer takes it off the data's value; a victim without the bit writes nothing.
    private void ClearKosOverride(string victim)
    {
        if (Fields(victim)?.Npc is not { } fields) return;
        var data = (fields.Flags & KosOverrideNpc) != 0;
        _session.WriteObjectFlag(_session.Version, victim, SessionObjectFlagField.Npc, unchecked((int)KosOverrideNpc), false, data);
    }

    /// <summary>
    /// ARC-AI-1527 (FR-DIALOG-003, VB4-1..VB4-5): the viewer's line speaker for a follower comment: the follower's session entity and the message range it says a random line of (the taunt, the upset line, the near-death line;
    /// 0x413F50, 0x414370, 0x414490). The viewer reads the line text from the module's generated files and floats it over the follower. Null: the comment is chosen but nobody says it (a host without line texts).
    /// </summary>
    public Action<string, LoyaltyLines>? FollowerSpeaks { get; set; }

    private void Speak(string follower, LoyaltyLines lines) => FollowerSpeaks?.Invoke(follower, lines);

    /// <summary>
    /// ARC-UI-2079 (ledger TB28): the viewer's line speaker for an AI bark that has its own listener: the speaker's session entity, the message range and the listener the speech display turns the speaker to (an entity
    /// id; <c>"pc"</c> for the hero; null: no listener). The same line pick and float as <see cref="FollowerSpeaks"/>, which stays the PC-listener path of the follower comments. Null: nobody says it.
    /// </summary>
    public Action<string, LoyaltyLines, string?>? AiBarks { get; set; }

    /// <summary>The fleeing line (0x413EA0, can-speak flag 1): said when an NPC takes a fleeing or surrendered state it was not in, and when a surrendered NPC keeps its surrender and runs from a new attacker.</summary>
    public static readonly LoyaltyLines FleeBarkLines = new(3000, 3099);

    // The AI float callback (0x5F8488) of an active NPC (0x45D790): the speaker and the listener the speech display turns it to (the attacker or the danger object).
    private void Bark(string npc, LoyaltyLines lines, string? listener)
    {
        if (AiBarks is not { } barks || !Active(npc)) return;
        barks(npc, lines, listener);
    }

    /// <summary>
    /// ARC-AI-1677: a follower that left by the loyalty step (<see cref="GameSessionOwner.RunLoyaltyStep"/>, not by the hero's hit) sets JILTED and, when it is active and a line
    /// callback exists, says the refusal line of its reason (the same line as the hit's leave) through <see cref="FollowerSpeaks"/>. Returns the line range asked, or null when the step did not leave, the
    /// follower is not active or nobody listens. The can-speak test with flag 1 (its head and the viewer's float, <see cref="Arcanum.Simulation.Reaction.CanSpeakRules.Tail"/> for the rest) is the speaker's.
    /// </summary>
    /// <remarks>The leave branch of 0x4AC380 (its registry row names the step rule): JILTED, the active test 0x45D790, then 0x413A90.</remarks>
    public LoyaltyLines? SayLoyaltyLeave(string follower, LoyaltyStepResult step)
    {
        ArgumentNullException.ThrowIfNull(follower);
        if (step.RefusalLines is not { } lines || FollowerSpeaks is null || !Active(follower)) return null;
        Speak(follower, lines);
        return lines;
    }

    /// <summary>
    /// ARC-AI-1685: a follower that stayed through the loyalty step with a leader-check warning (<see cref="LoyaltyStepResult.Warning"/>: too good 5200-5299, too evil 5300-5399, low
    /// reaction 5400-5499) says the warning line when it is active and a line callback exists, through <see cref="FollowerSpeaks"/>. Returns the line range asked, or null when the step gave no
    /// warning, the follower is not active or nobody listens. The can-speak test with flag 1 (its head and the viewer's float) is the speaker's, as for <see cref="SayLoyaltyLeave"/>.
    /// </summary>
    /// <remarks>The warning branch of 0x4AC380 (its registry row names the step rule): the active test 0x45D790, then 0x414290.</remarks>
    public LoyaltyLines? SayLoyaltyWarning(string follower, LoyaltyStepResult step)
    {
        ArgumentNullException.ThrowIfNull(follower);
        if (step.WarningLines is not { } lines || FollowerSpeaks is null || !Active(follower)) return null;
        Speak(follower, lines);
        return lines;
    }

    /// <summary>
    /// ARC-AI-1527 (0x4B6B90 → 0x4AE9E0, the end of one attack): the combat taunt of the PC's followers. <see cref="FollowerRules.TauntKindOf"/> asks the kind of the attack's facts; then
    /// <see cref="FollowerRules.Taunt"/> draws (half the time none) one of the PC's following followers, in the party's order (a follower this host does not resolve is a null entry, FO17), which says its line
    /// through <see cref="FollowerSpeaks"/>. Returns the comment chosen, or null. The facts' setting is the "combat taunts" option; the attacker is the hero.
    /// </summary>
    public FollowerComment? AttackEnded(TauntFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (FollowerRules.TauntKindOf(facts) is not { } kind) return null;
        var followers = _session.QueryParty().Followers.Where(f => f.Standing == CompanionStanding.Following).Select(f => Find(f.Entity)).ToArray();
        if (FollowerRules.Taunt(kind, followers, _session.AiRolls) is not { } comment) return null;
        Speak(comment.Speaker, comment.Lines);
        return comment;
    }

    /// <summary>
    /// ARC-AI-1527 (0x4AEAB0, from the damage step 0x4B4390): a critter that took <paramref name="hitPointsBefore"/> − <paramref name="hitPointsAfter"/> hit points of <paramref name="maximum"/> in a hit says its near-death
    /// line when it is a follower of the PC and its hit-point percent fell from 20 or more to below (<see cref="FollowerRules.NearDeathOfHit"/>, a killed critter says nothing: FO16). Returns the comment, or null.
    /// </summary>
    public FollowerComment? Hurt(string critter, int hitPointsBefore, int hitPointsAfter, int maximum, bool died)
    {
        ArgumentNullException.ThrowIfNull(critter);
        if (critter == Pc || !_session.QueryCompanion(critter).LedByPc) return null;
        if (FollowerRules.NearDeathOfHit(critter, true, hitPointsBefore, hitPointsAfter, maximum, died) is not { } comment) return null;
        Speak(comment.Speaker, comment.Lines);
        return comment;
    }

    /// <summary>
    /// ARC-WORLD-115 (I9 host part): tell followers (0x4A9B80, <see cref="FollowerRules.TellFollowers"/>) of <paramref name="leader"/>
    /// about <paramref name="other"/>: the leader's direct followers in its follower array's order (0x4411C0, the array of any leader: ARC-WORLD-2116; the waiting ones are out of it), the ones that stand on this map
    /// (the hero's followers that follow it, or an NPC leader's followers this host runs), each through the follower rule (never ordered,
    /// never accidental here: the host's attacks are deliberate). A lock or focus and a complaint's −5 toward the hero go through the
    /// session (<see cref="GameSessionOwner.ApplyFollowerRule"/>; the −5 is written only toward a player character, so an NPC leader's follower complains in words alone); the reaction to an attack by the other (case 5) is
    /// <see cref="MoraleRules.ReactToAttack"/> through the setter, as a victim's. The complaint's upset line is said through <see cref="FollowerSpeaks"/> under the hero (ARC-AI-1527) and through <see cref="AiBarks"/> with the NPC leader as listener (ARC-DEV-2367).
    /// </summary>
    private void TellFollowers(string leader, string other, FollowerSide side)
    {
        if (Find(other) is null) return;
        var followers = (leader == Pc
                ? _session.QueryParty().Followers.Where(f => f.Standing == CompanionStanding.Following && _followers.ContainsKey(f.Entity)).Select(f => f.Entity)
                : _session.QueryFollowers(leader).Where(Walks))
            .Where(f => !Unresolved(f)).ToArray();
        if (followers.Length == 0) return;
        foreach (var outcome in FollowerRules.TellFollowers(this, leader, other, followers, side, accidental: false, _session.Rules.Combat))
        {
            var follower = outcome.Follower;
            Trace(follower).Event = $"told {side} {outcome.Step}{(outcome.Upset == UpsetReason.None ? "" : " " + outcome.Upset)} {other}";
            switch (outcome.Step)
            {
                case FollowerRuleStep.ReactToAttack:
                    React(follower, other);
                    break;
                case FollowerRuleStep.Lock or FollowerRuleStep.Focus or FollowerRuleStep.Complain:
                    // ARC-AI-1527 (0x414370): the complaint's upset line is the follower's, said whether or not the reaction step below can run.
                    // ARC-DEV-2367 (NLD7, 0x414370 → 0x418780): the float takes the follower's LEADER as listener, of any type: the hero's through the PC-listener path, an NPC leader's through the bark path (its gender pair, name and turn).
                    if (outcome.Line is { } upset)
                    {
                        if (leader == Pc) Speak(follower, upset);
                        else Bark(follower, upset, leader);
                    }
                    var facts = ReactionFacts(follower);
                    if (outcome.Step == FollowerRuleStep.Complain && leader == Pc && facts is null) break;
                    var result = _session.ApplyFollowerRule(_session.Version, new SessionFollowerRuleRequest
                    {
                        // The hero as leader never is the other side (the rule says nothing then): no reaction facts. A fight on the hero under an NPC leader carries them as the setter's rule 8 asks.
                        Outcome = outcome, Leader = Combatant(leader)!, Target = Combatant(other)!, Setter = SetterFacts(follower),
                        Complaint = leader == Pc && facts is { } f ? new SessionPeaceReaction(f.Npc, f.Pc) : null,
                        Reaction = other == Pc && outcome.Step != FollowerRuleStep.Complain && facts is { } hero ? new SessionDangerReaction(hero.Npc, hero.Pc, Row(follower)?.HostileReaction ?? 0) : null,
                    });
                    Trace(follower).Setter = $"follower rule {outcome.Step} {other} {result.Disposition} {result.Failure}";
                    break;
            }
        }
    }

    /// <summary>
    /// ARC-WORLD-117: a follower's combat facts for an order (<see cref="CompanionOrderFacts.Combat"/>): the setter's facts of an attack
    /// order's lock, its reaction facts toward the hero (a complaint's change, a stop's peace with the hero's side) and its combat-mode
    /// facts (a stop's exit). Missing reaction or combat-mode facts stay null, as the session's defaults.
    /// </summary>
    public SessionPeaceFacts OrderFacts(string follower)
    {
        ArgumentNullException.ThrowIfNull(follower);
        return new()
        {
            Setter = SetterFacts(follower),
            Reaction = ReactionFacts(follower) is { } facts ? new SessionPeaceReaction(facts.Npc, facts.Pc) : null,
            CombatMode = NpcCombatMode(follower),
        };
    }

    /// <summary>
    /// ARC-WORLD-117: the host's part of an order's stop attacking (back off, FO5), run right after the follower's order and before
    /// its AI pass (<see cref="CompanionOrderRequest.Ordered"/>): the stop's animation interrupt at priority 3
    /// (<see cref="SessionPeaceStep.Interrupted"/>, 0x44E2C0 as at a round's start) ends the follower's walk, attack or flight run with
    /// its end nodes, so the pass finds it idle. True: it freed a run.
    /// </summary>
    public bool ApplyStop(SessionPeaceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Interrupted && InterruptRuns(step.Entity, GoalInterruptRules.OrdinaryActions);
    }

    /// <summary>
    /// ARC-WORLD-119: the leaves since the last look. A companion the host runs that the hero led and leads no more (any leave: an
    /// order's or the curse's, a dialog's, the loyalty step's, a script's disband) has its run dropped with its end nodes, as the
    /// original's removal (0x45E040, ledger FO20) nulls the leader and then interrupts the NPC's animations at priority 5 (0x424070),
    /// which ends every run of this host (its goals are of priority 4 or less); from then on it is run as any leaderless NPC. A join
    /// interrupts nothing (0x45DE90, C5): the joined companion keeps its run. The pump calls it before its steps and perform before it
    /// reads the run; an order's host part calls it right after the order (<see cref="AreaCompanionOrders.Send"/>), so the curse's
    /// attack event and the pass after it meet the leave already done. Returns the companions whose run was dropped.
    /// </summary>
    public IReadOnlyList<string> ObserveLeaves()
    {
        List<string>? dropped = null;
        foreach (var companion in _followers.Keys)
        {
            var led = _session.QueryCompanion(companion).LedByPc;
            if (led) { _led.Add(companion); continue; }
            if (!_led.Remove(companion)) continue;
            Trace(companion).Event = "left the hero";
            if (RunOf(companion) is null) continue;
            if (InterruptRuns(companion, LeaveLevel)) (dropped ??= []).Add(companion);
        }
        // ARC-WORLD-1840: an NPC of the map that left the leader it had (a summon's END, a forced leave, or a join under another leader, whose first half is that leave: 0x45DE90 → 0x45DFC0
        // → 0x45E040) is interrupted the same way; a join of an NPC that had no leader interrupts nothing.
        foreach (var npc in _npcs.Keys)
        {
            var now = _session.QueryLeader(npc);
            var before = _npcLeaders.GetValueOrDefault(npc);
            if (now is null) _npcLeaders.Remove(npc); else _npcLeaders[npc] = now;
            if (before is null || before == now) continue;
            Trace(npc).Event = now is null ? "left its leader" : $"left {before} for {now}";
            if (RunOf(npc) is null) continue;
            if (InterruptRuns(npc, LeaveLevel)) (dropped ??= []).Add(npc);
        }
        return dropped ?? (IReadOnlyList<string>)[];
    }

    /// <summary>
    /// ARC-WORLD-119: the attack event an order leaves to the host (<see cref="CompanionOrderOutcome.CurseAttack"/>: the curse's, the
    /// hero on the former follower at loudness 1 with no flags, FO2), run in full as the hero's attacks are (<see cref="HeroAttacked"/>):
    /// the hero's remaining followers are told, the noise's witnesses react, and the victim, now free, reacts to the attack (fight or
    /// flee through the setter). False: an attacker or victim this host does not know (nothing runs).
    /// </summary>
    public bool OrderAttack(CompanionOrderAttack attack)
    {
        ArgumentNullException.ThrowIfNull(attack);
        if (Id(attack.Attacker) is not { } attacker || Id(attack.Victim) is not { } victim || Find(attacker) is null || Find(victim) is null) return false;
        AttackEvent(attacker, victim, attack.Flags, attack.Loudness);
        return true;
    }

    /// <summary>
    /// ARC-DEV-193: the dialog reply step's hostile check (0x413130 → 0x45D8D0, 0x4AE120; ledger HR1–HR3): the speaker is alive (a critter this
    /// host does not know counts as not alive, as the original's null object does) and the AI turns kill-on-sight toward the hero now
    /// (<see cref="HostilityRules.KillOnSight"/>; the will-kos script is not hosted, <see cref="AllowsKillOnSight"/>).
    /// </summary>
    public bool HostileToHero(string npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return Find(npc) is { Dead: false } && HostilityRules.KillOnSight(this, npc, Pc, _session.Rules.Combat) != Arcanum.Simulation.Ai.KillOnSight.No;
    }

    // The witnesses of an attack's noise: the attacker's square when under 30 tiles apart, then the victim's (AW16 under the switch).
    private void Witnesses(string attacker, string victim, Loudness loudness)
    {
        var distance = Distance(attacker, victim);
        var radius = AwarenessRules.NoiseRadius(loudness);
        var witnesses = HostilityRules.NoiseWitnesses(distance, Square(attacker, radius), Square(victim, radius), _session.Rules.Combat);
        foreach (var witness in witnesses)
        {
            if (witness == Pc || witness == attacker || witness == victim || !Walks(witness)) continue;
            var outcome = HostilityRules.Witness(this, witness, attacker, victim, loudness);
            // ARC-WORLD-1162 (SWB18c, AW19): a sleeping witness is woken before any perception test.
            if (outcome.Wakes) WakeFromBed(witness);
            switch (outcome.Response)
            {
                case WitnessResponse.ReactToAttacker: React(witness, attacker); break;
                case WitnessResponse.ReactToVictim: React(witness, victim); break;
                // ARC-AI-1514 (0x4A9F10, VBG3): a neutral bystander that perceives the victim asks the setter for FLEE from the attacker
                // (the setter applies its own rules: no-flee, leader charisma, the NPC-fighting switch); a refusal leaves it as it was.
                case WitnessResponse.FleeFromAttacker: SetDanger(witness, DangerState.Flee, attacker); break;
            }
        }
    }

    // The reaction to an attack (0x4AA620 with the should-flee of 0x4AB2A0): a weighed reaction asks the setter; a surrendered NPC that keeps its surrender to another source runs the flee goal
    // away from the new attacker (0x434980, ARC-AI-1521, VBG10); every NPC that is not dead then enters a running round (0x4B7E00).
    private void React(string npc, string attacker)
    {
        var reaction = MoraleRules.ReactToAttack(this, npc, attacker, _session.AiRolls, _session.Rules.Combat).Reaction;
        Trace(npc).Reaction = $"{reaction.Step} {reaction.Request}";
        switch (reaction)
        {
            case { Step: AttackReactionStep.Weigh, Request: { } state, Target: { } target }:
                SetDanger(npc, state, target);
                break;
            case { Step: AttackReactionStep.FleeGoal }:
                // The starter's own gate, level-3 interrupt and duplicate test are the table's (Started); its answer is not read by the reaction.
                // ARC-UI-2079: the surrendered NPC's fleeing line (0x413EA0, speaker the NPC, listener the new attacker) comes before the starter, as the callback 0x5F8488 does in the original.
                Bark(npc, FleeBarkLines, attacker);
                Started(npc, RunOf(npc), AiExecutorRules.StartFlee(this, npc, attacker, RunOf(npc)));
                break;
        }
        if (reaction.JoinsRound) JoinRound(npc);
    }

    // The round entry (0x4B7E00): with a round running, an NPC not in its list is appended and the list re-sorted with the hero's party last (the session's AddCombatant);
    // with no round, or when it is listed already, nothing changes. The round's current participant and its action points stay as they were.
    private void JoinRound(string npc)
    {
        if (npc == Pc || _session.QueryCombatRound() is null) return;
        var joined = _session.AddCombatant(_session.Version, SessionCombatant.Critter(npc));
        if (joined.Disposition == GameSessionDisposition.Changed) Trace(npc).Reaction += " joined the round";
    }

    // ---- the pipeline's stages (ISessionAiWorld, IAiHost) ----

    public long DistanceToPc(string npc) => Tile(npc) is { } at && Tile(Pc) is { } pc ? AiExecutorRules.Distance(at, pc) : -1;

    /// <summary>ARC-WORLD-114: the first-heartbeat and heartbeat points run the NPC's script through the scene's
    /// <see cref="AreaAiScene.Heartbeat"/> (true: continue); the other points run no script yet and continue.</summary>
    public bool RunScript(AiScriptPoint point, string npc, string? triggerer) =>
        point is not (AiScriptPoint.FirstHeartbeat or AiScriptPoint.Heartbeat) || _scene.Heartbeat?.Invoke(point, npc) != false;
    // ARC-DEV-117 (MIG9 S4b): the fold's waypoint and standpoint steps (0x4ACBB0, 0x4ACDB0), in AreaAiHostRoutes.cs.
    public bool RunWaypoints(string npc) => RunRouteStep(npc, NpcRouteRules.ForcedWaypoints, waypoints: true);
    public void RunStandpoints(string npc) => RunRouteStep(npc, NpcRouteRules.ForcedStandpoint, waypoints: false);
    // ARC-WORLD-1157 (0x4BA720): a critter a generator made is no generator, whatever its prototype's flags say.
    public bool IsGenerator(string npc) => Fields(npc)?.Npc is { } fields && (fields.Flags & GeneratorNpc) != 0 && _session.QueryGeneratorCopy(npc) is null;
    // ARC-WORLD-1157 (E2-a): the spawner of a generator is in AreaAiHostGenerators.cs.
    /// <summary>ARC-DEV-371 (PG1): the critter flag 0x20000 (field 0xDA) the script action create object writes on a critter it made, over the data's own flags. Only a critter the session created is asked (a map
    /// NPC's data has none of them): the AI tick then destroys it when it is left without a next tick outside a round (the sector its tick found not loaded), which this host answers by queuing the
    /// session's destroy of the created critter (<see cref="TakeDestroyedCreated"/>; the viewer applies it outside the tick, which holds the session).</summary>
    public bool IsEncounterCreature(string npc) => _created.Contains(npc)
        && (_session.QueryObjectFlags(npc).Critter(unchecked((int)(Fields(npc)?.Critter?.Flags ?? 0))) & SessionCreatedObject.EncounterCritterFlag) != 0;

    private readonly List<string> _destroyedCreated = [];

    /// <summary>ARC-DEV-371: the created critters an AI tick destroyed since the last call (an encounter creature left without a next tick), in order; the caller destroys them in the session
    /// (<see cref="GameSessionOwner.DestroyCreatedObject"/>), after which <see cref="SyncCreated"/> drops them.</summary>
    public IReadOnlyList<string> TakeDestroyedCreated()
    {
        var taken = _destroyedCreated.ToArray();
        _destroyedCreated.Clear();
        return taken;
    }
    // The object destroy (0x43CCA0) kills every run of the object (0x423FF0, GS21): no rule, no level; the end nodes of the runs that ended run (they do nothing for a killed run
    // in the original; this host's also free a cast's unbegun magic run, ARC-WORLD-102).
    // ARC-DEV-301: the destroy also tells the spell runs (0x43CCA0 calls 0x459740 three times).
    public void Destroy(string npc)
    {
        EndNodes(_session.KillGoals(npc).Steps); _scene.Spells?.Invoke()?.ObjectGone(npc);
        if (_created.Contains(npc) && !_destroyedCreated.Contains(npc)) _destroyedCreated.Add(npc);
    }
    // The AI tick's far path (0x424070 at level 4, TL42, GS20).
    public void InterruptAnimations(string npc) => InterruptRuns(npc, FarPathLevel);
    /// <summary>ARC-DEV-104 (MIG9 S4, RL16's loaded test): the session answers for the open map's sector cache (a whole map whose residency the
    /// viewer opened); a map without one (a window of the main map) counts every sector loaded, as before.</summary>
    public bool SectorLoaded(string npc) =>
        Tile(npc) is not { } at || _session.QuerySectorLoaded(new(_scene.RuntimeMap), ResidentSector.Of(at)) != false;
    /// <summary>ARC-DEV-105 (MIG9 S5, 0x4AD4D0): the follower catch-up's tests as the original reads them: a PC leader, its distance, the NPC flag
    /// WAITING_HERE (field 0x118, 0x8; ours also a waiting standing), the critter flag paralysed (field 0xDA, 0x40) and active (0x45D790).
    /// ARC-WORLD-1840, ARC-WORLD-2116: the original reads the chain's player character (0x45DDA0), so a follower of a companion (or of any NPC the hero's chain reaches) catches up too, and its distance is the one to that PC.
    /// A waiting leader does not hold it back: the waiting leader keeps its leader handle and the catch-up tests nothing of the immediate leader.</summary>
    public AiCatchUpFacts CatchUp(string npc) => new(PcLeader(npc) == Pc, DistanceToPc(npc),
        (Fields(npc)?.Npc is { } fields && (fields.Flags & WaitingHereNpc) != 0) || Waiting(npc),
        Paralysed(npc), Active(npc));
    /// <summary>ARC-DEV-105 (MIG9 S5, 0x4AD4D0 then 0x43E770): the follower's goal run ends (the original's interrupt at priority 4), then it
    /// stands on the PC's cell, through the follower source (the companion group's jump); a follower the source does not hold stays put.</summary>
    public void MoveToLeader(string npc)
    {
        InterruptRuns(npc, FarPathLevel);
        // ARC-DEV-328 (B25, GR12): the move 0x43E770 ends in the notification 0x444270, so the leader's tile's script runs for the follower that landed on it (TG2, TG9).
        if (_scene.Followers is { } followers && _followers.ContainsKey(npc) && Tile(Pc) is { } pc && followers.Jump(npc, pc))
            _notices.Add(new(AreaAiNoticeKind.Entered, npc, null) { Cell = pc });
        // ARC-WORLD-2116 (0x4AD4D0 → 0x43E770): an NPC of the map whose chain of leaders reaches the hero through another leader is moved onto the hero's cell as well (the move is the chain's PC, not the immediate
        // leader); it is the mover's placement, the cell's script told through Place's notice. The hero's own cell is no refusal here (PlaceHeld's is for a placement that must not land on the hero): the original stacks
        // the follower on the PC (as a companion's jump does), and the shared-cell rules (ARC-AI-1315) step it apart.
        else if (_npcs.TryGetValue(npc, out var row) && Tile(Pc) is { } hero && Tile(npc) != hero) Place(npc, row, hero);
    }
    public bool MapClearing => false;
    // ARC-WORLD-1162: the critter flag 0x1000 as the session left it (an NPC seated in a bed is written there) over the data's.
    public bool Sleeping(string npc) => Fields(npc)?.Critter is { } critter && (_session.QueryObjectFlags(npc).Critter(unchecked((int)critter.Flags)) & unchecked((int)SleepingFlag)) != 0;
    // Object flags 0x102: not drawn or switched off in its map data, or (ARC-WORLD-114) switched off by a script (gone from the scene).
    public bool Hidden(string npc) => Fields(npc) is { } fields && (ObjectFlagsOf(npc, fields.Object.Flags) & (DontDraw | OffObject)) != 0 || Gone(npc);

    // ARC-DEV-358: an NPC's object flags (obj_f_flags) as the spells left them: the session's written change (invisible, translucent, shrunk, water walking, stoned, don't-draw) over its data's.
    private uint SpellFlagsOf(string id, uint stored) => unchecked((uint)_session.QueryObjectFlags(id).Spell(unchecked((int)stored)));

    private uint ObjectFlagsOf(string id, uint stored) => unchecked((uint)_session.QueryObjectFlags(id).Object(unchecked((int)stored)));

    // ARC-MAGIC-1142: an NPC's NPC flags (obj_f_npc_flags, 0x118) and critter flags (0xDA) as the session left them (a charm's kill-on-sight override, the attack event's clear, the undead bit) over its data's.
    private uint NpcFlagsOf(string id, uint stored) => unchecked((uint)_session.QueryObjectFlags(id).Npc(unchecked((int)stored)));

    private uint CritterFlagsOf(string id, uint stored) => unchecked((uint)_session.QueryObjectFlags(id).Critter(unchecked((int)stored)));
    public bool PcHidden => false;
    public AiConversation Conversation(string npc) => _scene.ConversationPartner?.Invoke() == npc ? AiConversation.Pc : AiConversation.None;
    // The dialog end callback 0x5F848C is stored once by the dialog UI's init (0x567377 → 0x4A84D0) and never cleared: it is always set (ARC-DEV-177, the S11e read GT138).
    public bool ConversationCallback => true;
    public void EndConversation(string npc) => _scene.ConversationEnded?.Invoke(npc);

    /// <summary>The gate's animation priority (0x4233D0, AC6): that of the bottom goal (stack slot 0) of the NPC's run, 0 without a
    /// run. ARC-WORLD-105: the top goal was read before; for a run whose goals differ, such as an attack under its approach, the gate
    /// then let the pass run mid-approach (a technical slip of the host, fixed in both modes).</summary>
    // The AI pre-check (0x4A8570, GS20) reads the priority of the main run's bottom goal (0x4233D0: the first run that is not an overlay; 0 with none).
    public int AnimationPriority(string npc) => _session.QueryGoals().MainRunPriority(npc);
    // GT35: the gate asks whether the NPC's first run is the stun (goal 57); the stun's own animation keeps its stunned bit.
    public bool StunAnimationRunning(string npc) => RunOf(npc) is { Bottom.Goal: GoalType.AnimateStunned };
    // ARC-DEV-143: an overlay (an eye candy or the hover) is beside the NPC's own runs and is not its animation here: a spell's eye candy on a companion or on the NPC whose subturn starts changes none of these answers (the original's own test of the animation list is not read).
    public bool AnimationRunning(string npc) => _session.QueryGoals().RunsOf(npc).Any(r => !AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal));
    /// <summary>
    /// ARC-AI-1139 (0x4A8570 steps CHECK_WIELD and CHECK_WEAPON): the gate's re-wield of the NPC's best items, every slot (0x4654F0) or the weapon slot alone
    /// (0x465170 with 1004), over its list and ammunition stacks (<see cref="GameSessionOwner.RewieldNpc"/>), so a companion whose bow ran dry takes it off
    /// and fights with the best weapon it can use, fists when none. The session's answer is traced; a refusal (no wielder for the NPC, no restock world) leaves the NPC as it was.
    /// </summary>
    public void ReWield(string npc, bool allSlots)
    {
        var result = _session.RewieldNpc(_session.Version, npc, allSlots, _events as ISessionRestockWorld);
        Trace(npc).Item = $"rewield {(allSlots ? "all" : "weapon")} {result.Disposition} {result.Failure} " + string.Join(", ", result.Steps.Select(step => $"{step.Slot} off={step.TakenOff ?? "-"} on={step.Wielded ?? "-"}"));
        // ARC-RENDER-1485: a changed worn item changes the weapon part and shield bit the NPC's body shows (0x465020, ShownWeapon); the viewer draws its body again.
        // ARC-MAGIC-1684: the lines the moved items' scripts printed go to the message window with the notice.
        if (result.Disposition == GameSessionDisposition.Changed || result.Messages.Count > 0 || result.EyeCandies.Count > 0) _notices.Add(new(AreaAiNoticeKind.Rewield, npc, null) { Messages = result.Messages, EyeCandies = result.EyeCandies });
    }
    // ARC-DEV-301: the gate's demaintain (0x4A8570 step 15 → 0x457450) ends the NPC's maintained spell runs through the cast host.
    public void EndMaintainedSpells(string npc) => _scene.Spells?.Invoke()?.EndMaintainedSpells(npc);
    /// <summary>
    /// ARC-WORLD-108: heal (0x4A8940, <see cref="AiSupportRules.Heal"/>), the pass's first step: the healer's lists chosen for the NPC,
    /// its leader and the party; a choice goes to this pass's perform as the pass record's, an AI-action item is used at once
    /// (<see cref="UseItemAtOnce"/>). True when it chose: the pass skips the item search and the danger update. The heal skill's rung
    /// finds the NPC's healing item (ARC-WORLD-116, <see cref="HealingItem"/>) and its choice starts the skill goal 33 at perform.
    /// </summary>
    public bool Heal(string npc)
    {
        _passChoices.Remove(npc);
        var heal = AiSupportRules.Heal(this, npc, _session.AiRolls, _session.Rules.Combat);
        if (!heal.Chose) return false;
        Trace(npc).Item = $"heal {heal.Attempts[^1].Target} {heal.Attempts[^1].Step} spell={heal.Choice?.Spell.ToString() ?? "-"} item={heal.Choice?.Item ?? heal.ItemUse?.Entry?.Item ?? "-"}";
        if (heal.ItemUse is { } atOnce) UseItemAtOnce(npc, atOnce);
        else if (heal.Choice is { } choice) _passChoices[npc] = choice;
        return true;
    }

    /// <summary>
    /// ARC-WORLD-108: the chooser's use of an AI-action item at once (0x4ABF10 → 0x462CC0, <see cref="AiChooserOutcome.ItemUsedAtOnce"/>):
    /// the item use with the NPC as user and the entry's target, unchecked, before the pass goes on; the pass record keeps no choice.
    /// </summary>
    private void UseItemAtOnce(string npc, AiChooserResult chosen)
    {
        if (chosen.Entry?.Item is not { } item) return;
        var used = AiExecutorRules.UseItemNow(this, npc, item, chosen.Target);
        Trace(npc).Item += $" used at once {used.Step} spent={used.Spent}";
    }
    /// <summary>
    /// ARC-AI-1139: the pipeline's item search (0x4A8E70, <see cref="AiGroundRules.SearchItems"/>): each look-for bit the NPC holds is cleared just before its search
    /// (the setters are the ranged attack without ammunition, 0x4ADFF0, and an item drop, 0x461AB0). ARC-AI-1309: the ground is the shown map's loose items and junk piles
    /// (<see cref="LooseItems"/>, <see cref="JunkPiles"/>, <see cref="FirstInPile"/>), the pickup starts goal 15 (<see cref="StartPickup"/>) and its end moves the item into the NPC's list
    /// (<see cref="PickUp"/>). A pass whose search starts a pickup answers true and skips the danger update; any other answers false.
    /// </summary>
    public bool SearchItems(string npc)
    {
        using var ground = HoldGround(npc);
        var result = AiGroundRules.SearchItems(this, npc);
        if (result.Cleared != DangerFlags.None) Trace(npc).Item = $"item search {result.Stop} cleared {result.Cleared}";
        return result.Stop == AiItemSearchStop.PickupStarted;
    }

    // IAiItemSearchWorld (ARC-AI-1139): the blinded condition (critter flag 0x80) and the interface object test (no window holds an object here); ARC-AI-1309: the ground, the pickup starter and the
    // goal's end are in AreaAiHostGround.cs.
    public bool Blinded(string npc) => LifeState(npc)?.Conditions.HasFlag(LifeConditions.Blinded) ?? false;
    public bool InterfaceHoldsObject => false;
    public bool Loading => false;

    /// <summary>
    /// The danger stage: the update's plan applied through the setter. ARC-WORLD-102: for a fight the spell and item choice runs for
    /// the found target before the setter (0x4ABC20, HS1, SP4), the setter's target is then <see cref="HostilityRules.FocusTarget"/>
    /// of the pass record's (SP16), and the choice stays in the pass record for perform unless the setter's NO_ATTACK or "NPC
    /// fighting" rule refused the fight in the Steamwoven setting (FO8, <see cref="HostilityRules.KeepsChosenAction"/>).
    /// </summary>
    public void UpdateDanger(string npc)
    {
        _passChoices.Remove(npc);
        _passSounds.Remove(npc);
        var plan = HostilityRules.DangerUpdate(this, npc, _session.AiRolls, _session.Rules.Combat);
        Trace(npc).Danger = $"{plan.Path} {plan.Request?.ToString() ?? "-"} {plan.Target ?? "-"}";
        if (plan.Path == DangerUpdatePath.Flee) { _session.RunFleeSpellStep(_session.Version, npc, () => false); return; }
        // ARC-AI-1518 (0x4AB460's end, ST5): a scan that found no target but perceived a concealed PC turns the scanner to face it; a step in progress goes on.
        if (plan.Scan is { Target: null, TurnToward: { } toward } && TryNpc(npc, out var scanner) && Tile(toward) is { } facing) FaceNpc(scanner, facing, keepStep: true);
        if (plan.Unlocks) _session.UnlockDangerTarget(_session.Version, npc);
        // ARC-AI-1317 (VAG6): the pick's purge of the saved enemy list (the original runs it inside the pick, before the setter).
        if (plan.PurgesEnemies) _session.PurgeDangerEnemies(_session.Version, npc, enemy => Exists(Id(enemy)));
        if (plan.Request is not { } state) return;
        AiPassChoice? choice = null;
        if (plan.ChoosesAction && plan.Target is { } found)
        {
            var chosen = AiSupportRules.ChooseSpell(this, npc, found, _session.AiRolls);
            choice = chosen.Choice;
            Trace(npc).Spell = $"{chosen.Stop} chance={chosen.Chance} spell={choice?.Spell.ToString() ?? "-"} item={choice?.Item ?? chosen.ItemUse?.Entry?.Item ?? "-"} target={choice?.Target ?? "-"}";
            // ARC-WORLD-108: an AI-action item entry is used at once (0x462CC0); the setter then runs without a choice.
            if (chosen.ItemUse is { } atOnce) UseItemAtOnce(npc, atOnce);
        }
        var target = state == DangerState.CombatFocus ? HostilityRules.FocusTarget(plan, choice?.Target ?? plan.Target, _session.Rules.Combat) : null;
        // ARC-RENDER-1126: the danger update's setter call asks for the alerted sound (0x4A92D0 passes the pass record's bark slot); the setter gives it when it made the NPC hostile (rule 7).
        var result = SetDanger(npc, state, target, alertSound: true);
        if (result.Transition is { AlertSound: true } && _scene.AlertedSound?.Invoke(npc) is { } bark) _passSounds[npc] = bark;
        if (choice is not null && (result.Transition is not { } transition || HostilityRules.KeepsChosenAction(transition, _session.Rules.Combat)))
            _passChoices[npc] = choice;
    }

    /// <summary>The perform stage: <see cref="AiExecutorRules.Perform"/> with the danger source as the pass's target; a started run
    /// replaces the NPC's (the old one's end nodes run, <see cref="AiExecutorRules.EndRun"/>) and steps at the next pump.
    /// ARC-WORLD-115: a companion's follow goal (the non-combat perform's, 0x4346A0) is its own walk's, which follows the hero while
    /// the host does not hold it (<see cref="Holds"/>), so a follow the perform starts for one is not run here.</summary>
    public void Perform(string npc)
    {
        ObserveLeaves();
        var danger = _session.QueryDanger(npc);
        var target = danger.Source is { } source ? Id(source) : null;
        var current = RunOf(npc);
        // ARC-WORLD-102: the pass record's choice from this pass's danger update (none: the action type 0).
        _passChoices.Remove(npc, out var choice);
        // ARC-RENDER-1126: the alerted sound of this pass's setter call goes into the attack goal (the approach node plays it once).
        int? alerted = _passSounds.Remove(npc, out var bark) ? bark : null;
        var result = AiExecutorRules.Perform(this, npc, target, choice, alerted, current);
        if (_followers.ContainsKey(npc) && result.Kind == AiPerformKind.Follow)
        {
            // A run left from the fight (a flight, a back-off) is interrupted as the follow starter's own interrupt would (level 3, the table's rule: a spell attempt, priority 4,
            // goes on), so the walk is no longer held by it. ARC-DEV-135: it was a drop at the highest level.
            // ARC-AI-1523 (VBG12, 0x4346A0): the original interrupts only on its start path; with the leader near or an equal run kept (LeaderNear, AlreadyQueued) the run stays,
            // so a companion's please-move (68) goes on.
            Trace(npc).Perform = $"{result.Kind} by its own walk";
            if (current is not null && result.Refusal is not (AiStartRefusal.LeaderNear or AiStartRefusal.AlreadyQueued)) InterruptRuns(npc, GoalInterruptRules.OrdinaryActions);
            return;
        }
        // ARC-AI-1512: the back-off's run starter (0x433C80) changes the destination of a run to a tile the NPC already has instead of starting another one.
        if (result is { Kind: AiPerformKind.BackOffRun, Run.Bottom.Tile: { } away } && RetargetRunToTile(npc, result.Run.Bottom, result.Run.Flags, away)) return;
        Started(npc, current, result);
    }

    // 0x433C80 on an object whose first run already walks or runs to a tile (0x44E830 with goal 4 and its aliases): the movement gate, then the same destination is kept and a new one
    // overwrites the run's destination in place (GS14). False: no such run, the caller starts one.
    private bool RetargetRunToTile(string npc, GoalFrame frame, GoalRunFlags flags, CellCoord destination)
    {
        if (_session.QueryGoals().RunRunningGoal(npc, GoalType.RunToTile) is not { } running) return false;
        if (!StartGatePasses(new GoalStartRequest(npc, GoalStarters.Run, frame, flags))) { Trace(npc).Perform = "BackOffRun gate refused"; return true; }
        var retarget = _session.RetargetGoal(running.Handle, destination);
        // The found run is marked running whether or not the destination changed (0x433C80 sets flag 0x40 on it before the same-tile test).
        if (retarget.Failure == SessionGoalFailure.None) _session.TurnOnRunFlags(running.Handle, GoalRunFlags.Running);
        Trace(npc).Perform = $"BackOffRun retarget {retarget.Outcome?.ToString() ?? retarget.Failure.ToString()}";
        return true;
    }

    /// <summary>
    /// ARC-WORLD-115: whether the host holds the companion <paramref name="entity"/>, so its own walk follows nothing: while it has a
    /// goal run here, or its danger state is not NONE (a follower follows only in state NONE, the non-combat perform, A6). Else
    /// false (a map NPC or an unknown one too).
    /// </summary>
    public bool Holds(string entity) =>
        _followers.ContainsKey(entity) && (AnimationRunning(entity) || _session.QueryDanger(entity).State != DangerState.None);

    /// <summary>ARC-WORLD-115: the companions the host runs (ARC-WORLD-119: free ones too).</summary>
    public IReadOnlyCollection<string> RunCompanions => _followers.Keys;

    /// <summary>ARC-WORLD-113 test seam: the attack starter (0x434B00, <see cref="AiExecutorRules.StartAttack"/>) on
    /// <paramref name="target"/>, as combat perform's attack starts it: a "default throws" weapon starts the throw goal 37.</summary>
    public void PerformAttack(string npc, string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = RunOf(npc);
        Started(npc, current, AiExecutorRules.StartAttack(this, npc, target, null, current));
    }

    // True when the run is in the table now (a started run the table took).
    private bool Started(string npc, GoalRun? current, AiPerformResult result)
    {
        Trace(npc).Perform = $"{result.Kind}{(result.Refusal == AiStartRefusal.None ? "" : " " + result.Refusal)}{(result.Started ? " started" : "")}";
        if (!result.Started || result.Run is not { } run || ReferenceEquals(current, run)) return false;
        // ARC-DEV-135 (MIG13 S5): the proposed run starts in the session's table with its starter's own plan (GS12, GS13): the table interrupts the NPC's runs at the starter's
        // level by the rule (the executor's starters that interrupt in their own body, the attack, throw and follow, did so already, so what is left is the runs the rule kept, a
        // no-interrupt overlay among them, and the attack retarget's, which asks no port), refuses a duplicate and a refused interrupt, and the end nodes of every run the start
        // ended run here (the replaced run's among them). The first step comes 5 ms of animation time after the start (GS7), at the next pass (a start inside the AI tick's pass waits for it).
        var started = _session.StartGoal(new GoalStartRequest(npc, StarterPlan(run.Bottom.Goal), run.Bottom, run.Flags), _startPorts ??= new StartPorts(this));
        foreach (var end in started.Ended) EndNodes(end);
        if (started.Started) { Trace(npc).Reason = AiExecutorReason.None; return true; }
        // A start the table refused (an interrupt a priority-4 or -5 goal refuses, a duplicate, no free slot, a full queue) leaves the NPC with the runs the rule kept: the proposed
        // run's end nodes free what its starter took (a cast's magic run).
        // Only the proposed run's end nodes run: the kept run's attack animation (a priority-4 goal's run can have one) is not the proposed run's.
        Trace(npc).Perform += $" not in the table: {started.Failure} {started.Outcome}";
        AiExecutorRules.EndRun(run, this);
        FreeUnbegunCasts(run, queuedOnly: false);
        // ARC-AI-1315 (TS4): the attack starter's own run start failing (0x44D520) ends in the step apart as its other refusals do.
        if (run.Bottom.Goal == GoalType.Attack) AiTileSharerRules.StepApart(this, npc);
        return false;
    }

    // The interrupt plan of the starter that builds a run with this bottom goal (GS12, GS13): level 3 for the walks, runs, follow, attack, throw and spread out (with the
    // duplicate refusal for the last four); every other start interrupts at its goal's own priority (the cast, skill, item, pickup and flee goals are 3).
    internal static GoalStartPlan StarterPlan(GoalType goal) => goal switch
    {
        GoalType.MoveToTile => GoalStarters.Walk,
        GoalType.RunToTile => GoalStarters.Run,
        GoalType.Follow => GoalStarters.Follow,
        GoalType.Attack => GoalStarters.Attack,
        GoalType.ThrowItem => GoalStarters.Throw,
        GoalType.SpreadOut => GoalStarters.SpreadOut,
        _ => GoalStarters.Generic,
    };

    // The table's start ports for the host's starts. ARC-DEV-136 (MIG13 S5b, TL41, GS15): the gated starters (the walks and runs through the movement gate 0x4339A0; follow, attack, throw and
    // spread out through the shared start gate 0x4348E0) refuse a critter that is not the current participant of a round, whoever asks (the executor's starters run the same test with the
    // action-point rule before they get here, so the table's gate is the second line); nothing loads and a destroyed NPC is not performed for. The other starts (the hit reactions, dying,
    // the casts, the goals scripts or the combat code push) pass the gate: the table gives them the 1 ms first frame instead (`GameSessionOwner.RoundFirstGoalStepDelayMs`).
    private StartPorts? _startPorts;

    private sealed class StartPorts(AreaAiHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => host.StartGatePasses(request);
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    // The table's start gate (0x4339A0, 0x4348E0 for the gated starters, TL41): outside a round everything passes; in one only the current participant.
    // The fidget start (0x435BD0), the get-up starter (0x42E590) and the wander starters (0x435E60, 0x436040) go through the same gates (TL41) though their plan is the generic one.
    internal bool StartGatePasses(GoalStartRequest request) =>
        !(Gated(request.Plan.Kind) || request.Frame.Goal is GoalType.Fidget or GoalType.GetUp or GoalType.Wander or GoalType.WanderSeekDarkness or GoalType.MoveNearTile or GoalType.RunNearTile) || !InRound || RoundActor == request.Owner;

    internal static bool Gated(GoalStarterKind kind) =>
        kind is GoalStarterKind.Walk or GoalStarterKind.Run or GoalStarterKind.Follow or GoalStarterKind.Attack or GoalStarterKind.Throw or GoalStarterKind.SpreadOut;

    /// <summary>ARC-WORLD-105 smoke seam: perform with <paramref name="choice"/> as the pass record's choice, as a danger update that
    /// chose it would (the native ai-cast smoke's missile spell).</summary>
    public void PerformChoice(string npc, AiPassChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        _passChoices[npc] = choice;
        Perform(npc);
    }

    /// <summary>ARC-WORLD-109 test and smoke seam: perform with a skill use of <paramref name="skill"/> on <paramref name="target"/> with
    /// the tool <paramref name="item"/> as the pass record's choice (the action type 3), as the heal ladder's heal skill would choose it
    /// (the skill starter 0x4350F0, goal 33).</summary>
    public void PerformSkill(string npc, string target, int skill, string? item)
    {
        ArgumentNullException.ThrowIfNull(target);
        PerformChoice(npc, new AiPassChoice(AiPassAction.UseSkill, -1, skill, item, target));
    }

    /// <summary>
    /// ARC-COMBAT-1777 (0x571CB0, ledger PL3, PL5): the chosen follower's lock pick, as the lockpick item use hands it over: the skill starter (0x4350F0 → 0x436220, goal 33 with skill 14) on the lock <paramref name="target"/> (a door's entity or a container's
    /// "inv:" string) with the follower's own <paramref name="item"/> (null: without lockpicks), so it walks within range 1 and picks at its attempt node (<see cref="AttemptPickLocks"/>). True when the run is in the table.
    /// NOT READY: the original starts the use-item goal (0x28, no animation) for a follower that holds lockpicks and the timed pick-lock goal for one that holds none; both run as the timed goal 33 here.
    /// </summary>
    public bool StartPickLocks(string follower, string target, string? item)
    {
        ArgumentNullException.ThrowIfNull(follower);
        ArgumentNullException.ThrowIfNull(target);
        if (!_followers.ContainsKey(follower)) return false;
        PerformSkill(follower, target, AiExecutorRules.PickLocksSkill, item);
        return RunOf(follower) is { Bottom: { Goal: GoalType.UseSkill or GoalType.AttemptPickLocks, Skill: AiExecutorRules.PickLocksSkill } bottom } && bottom.Target == target;
    }

    // An NPC that joined the hero since the host was built is the companions' (it counts as gone here); ARC-WORLD-115: a companion the
    // host runs is its own.
    public SessionLifeTarget? AiLifeTarget(string entity) =>
        !_followers.ContainsKey(entity) && _session.QueryCompanion(entity).LedByPc ? null : RawBuild(entity)?.Target;

    // ARC-DEV-135 (MIG13 S5): an interrupt of every run of the NPC at a level (0x424070, GS17; the first refusal stops the walk) with the end nodes of each run it ended. True: it
    // freed a run (or marked the stepped one done); Succeeded is the original's answer (no run, a no-interrupt goal left running and a run that ended all succeed; a refusal, or the
    // spell-run rules holding the table, do not).
    // ARC-DEV-2074: Refused is a run's own refusal (the session asked the table and the table said no); a session failure (an entity that is none, a table the spell-run rules hold) is no run's refusal.
    private (bool Freed, bool Succeeded, bool Refused) InterruptOwner(string npc, int level)
    {
        var change = _session.InterruptGoals(npc, level);
        EndNodes(change.Steps);
        var succeeded = change.Failure == SessionGoalFailure.None && change.Succeeded;
        if (!succeeded) Trace(npc).Step = $"interrupt {level} refused: {change.Failure} {string.Join(",", change.Steps.Select(s => s.Outcome))}";
        return (change.Steps.Any(s => s.Outcome is GoalInterruptOutcome.Ended or GoalInterruptOutcome.MarkedDone), succeeded, change.Failure == SessionGoalFailure.None && !change.Succeeded);
    }

    private bool InterruptRuns(string npc, int level) => InterruptOwner(npc, level).Freed;

    /// <summary>ARC-UI-2030: the interrupt the can-speak test 0x4AD800 makes at flag 0 on a talk start (0x424070, level 2, the NPC's runs, the first refusal stops the walk). False only when a run refused it (the test answers 3).
    /// ARC-DEV-2074: a session failure of the call (an invalid entity, a table the spell-run rules hold) is the session's own wait, no run's refusal, so it answers true and the talk goes on without the interrupt.</summary>
    public bool InterruptForTalk(string npc) => !InterruptOwner(npc, TalkInterruptLevel).Refused;

    /// <summary>The level of the can-speak test's interrupt (0x4AD800: <c>0x424070(NPC, 2, 0, 0)</c>).</summary>
    public const int TalkInterruptLevel = 2;

    // A drop of the NPC's runs at the highest level, which every goal and overlay yields to (GS18): the host's own end of a run (a map reset's level, GS19).
    private void DropRun(string npc) => InterruptRuns(npc, GoalInterruptRules.MaxLevel);

    // A run the host drops without stepping it to its end: its end nodes (AiExecutorRules.EndRun). ARC-WORLD-102: a casting goal 26 or
    // 82 dropped before its attempt goal has only the animation reset as its end node (0x42B090), which frees nothing, so its magic run
    // would keep its slot, never begun, as MG43's queued cast does; as there (owner decision 2026-10-28, both modes) the NPC's run that
    // still waits for its BEGIN is freed, unpaid.
    private void EndDroppedRun(GoalRun run)
    {
        // ARC-RENDER-038: a dropped run's attack animation is not drawn on.
        _attackPlays.Remove(run.Owner);
        AiExecutorRules.EndRun(run, this);
        FreeUnbegunCasts(run, queuedOnly: false);
    }

    // ARC-WORLD-104: a run that ended through the end step leaves the casts queued beneath its goals unplayed: their goals' end node,
    // the animation reset (0x42B090), frees nothing, so in the original the runs keep their slots (MG43, fixed in both modes: the
    // host frees them unpaid, as the hero's dropped queue). Only the goals that never started are theirs: one that started ended its
    // own run.
    private void FreeQueuedCasts(GoalRun run) => FreeUnbegunCasts(run, queuedOnly: true);

    private void FreeUnbegunCasts(GoalRun run, bool queuedOnly)
    {
        var caster = _scene.Spells?.Invoke();
        foreach (var frame in run.Frames)
        {
            if (frame is not { Goal: GoalType.CastSpell or GoalType.CastSpellWithAnimation, SpellRun: { } id }) continue;
            var queued = _queuedCasts.Remove(id);
            if ((queuedOnly && !queued) || caster is null) continue;
            if (OwnRun(run.Owner, id) && caster.DropQueued(id) is { } freed)
                _notices.Add(new(AreaAiNoticeKind.CastEnd, run.Owner, null) { Run = id, SpellRun = freed });
        }
    }

    // A goal's magic run is the NPC's own: a queued run cancelled meanwhile (QC6) may have lent its slot to another caster's run.
    private bool OwnRun(string id, int run) => _session.QuerySpellRuns()[run]?.Parent?.Id == id;

    // The setter call with the facts its rules read; a fight on the hero carries the reaction facts (rule 8).
    private SessionDangerResult SetDanger(string npc, DangerState state, string? target, bool alertSound = false)
    {
        var request = new SessionDangerRequest
        {
            Entity = npc, State = state, AlertSound = alertSound, Target = target is null ? null : Combatant(target), Facts = SetterFacts(npc),
            Reaction = target == Pc && state == DangerState.CombatFocus && ReactionFacts(npc) is { } facts
                ? new SessionDangerReaction(facts.Npc, facts.Pc, Row(npc)?.HostileReaction ?? 0) : null,
            CombatMode = NpcCombatMode(npc),
        };
        var result = _session.SetDangerState(_session.Version, request);
        Trace(npc).Setter = $"{state} {target ?? "-"} {result.Disposition} {result.Failure} {result.Transition?.Rule}";
        // ARC-UI-2079 (ledger TB28): the setter's fleeing line (0x413EA0) when the NPC takes a fleeing or surrendered state it was not in; the listener is the requested target (null: none).
        if (result.Transition is { FleeLine: true }) Bark(npc, FleeBarkLines, target);
        return result;
    }

    // ---- the executor's world (IAiGoalWorld) ----

    /// <summary>ARC-DEV-118: the evidence the planners and steps read: the loaded sectors' chart when the scene has one that holds a sector, else the scene's.</summary>
    private AreaStepSnapshot Steps => _scene.LoadedSteps?.Invoke() ?? _scene.Steps;

    /// <summary>ARC-DEV-118: whether a cell is a place the shown map holds: admitted by the scene's evidence or by a loaded sector's (a relocation is no walk,
    /// so it does not wait for its sector to load).</summary>
    private bool Admitted(AreaStepCell cell) => _scene.Steps.IsAdmitted(cell) || _scene.LoadedSteps?.Invoke()?.IsAdmitted(cell) == true;

    /// <summary>The most unloaded sectors one planned path touches (the cache holds 16: the arrival's 3 × 3 pre-cache and the view square already take most of them).</summary>
    public const int PathTouchLimit = 4;

    /// <summary>
    /// ARC-DEV-118 (MIG9 S5b, K1 §3.5, RL11), ARC-DEV-119 (S5c): a planner reads the tiles it plans over and the original's tile reads lock the sector of a tile
    /// whose sector is not loaded; the host touches the sectors a path goes through when the scene has a loaded-sector chart and the session holds the map's
    /// sector cache: the sectors on the straight line from the NPC to the goal, the corridor the planner's search reads first, the goal's last. At most
    /// <see cref="PathTouchLimit"/> unloaded ones, the nearest to the NPC first (a longer walk loads the next ones when its next plan asks); a loaded sector is left alone. The
    /// chart reads what entered the cache when its sync runs (<see cref="AreaAiScene.SectorsTouched"/>), so the next question sees the evidence. A deliberate
    /// difference (K1 §4): the cells the search explores beside the line are not touched one by one.
    /// </summary>
    private void TouchPath(CellCoord from, CellCoord goal)
    {
        if (_scene.LoadedSteps is null || goal.X < 0 || goal.Y < 0 || from.X < 0 || from.Y < 0) return;
        var map = new SessionMapId(_scene.RuntimeMap);
        if (_session.QuerySectors(map)?.Map is not { } extent) return;
        var cells = new List<CellCoord>();
        // The NPC's own sector is not part of the corridor: an NPC whose sector was evicted does not load it by planning (its fold is the cache's to run).
        var seen = new HashSet<ResidentSector> { ResidentSector.Of(from) };
        foreach (var cell in Corridor(from, goal))
        {
            var sector = ResidentSector.Of(cell);
            if (sector.X >= extent.SectorsX || sector.Y >= extent.SectorsY || !seen.Add(sector) || _session.QuerySectorLoaded(map, sector) != false) continue;
            cells.Add(cell);
            if (cells.Count == PathTouchLimit) break;
        }
        if (cells.Count == 0) return;
        _session.TouchSectors(_session.Version, map, cells);
        _scene.SectorsTouched?.Invoke();
    }

    // The cells of the straight line from one cell to another every 4 cells (a sector is 64), the end last.
    private static IEnumerable<CellCoord> Corridor(CellCoord from, CellCoord to)
    {
        long dx = to.X - from.X, dy = to.Y - from.Y;
        var n = Math.Max(Math.Abs(dx), Math.Abs(dy));
        for (long i = 0; i < n; i += 4) yield return new(checked((int)(from.X + Math.Round(dx * (double)i / n))), checked((int)(from.Y + Math.Round(dy * (double)i / n))));
        yield return to;
    }

    /// <summary>ARC-SCRIPT-1601: where an NPC of this host's map stands now (the cell its walker moved it to, else its own origin); null for an entity that is no NPC of this host.</summary>
    public CellCoord? NpcCell(string entity) => _npcs.TryGetValue(entity, out var npc) ? Movers.CellOf(entity, npc.Origin) : null;

    public CellCoord? Tile(string id)
    {
        if (id == Pc) return _scene.HeroCell();
        // ARC-AI-1309: an item an NPC's pickup goal walks to lies on a cell of the shown map (its own, or its junk pile's). ARC-TECHNOLOGY-1619: an object that is no critter (a trap's shooter) stands on its own tile.
        return _npcs.TryGetValue(id, out var npc) ? Movers.CellOf(id, npc.Origin) : CompanionCell(id) ?? PickupCell(id) ?? _scene.ObjectFacts?.Invoke(id)?.Cell;
    }

    /// <summary>ARC-WORLD-1158: where a critter of this host's map lies now (its body's cell for the Decay Dead Bodies event); null for another map or a critter the host does not hold.</summary>
    public CellCoord? BodyCell(SessionMapId map, string entity) => map.RuntimeMap == _scene.RuntimeMap && entity != Pc ? Tile(entity) : null;

    /// <summary>ARC-AI-1636 (0x4294F0: the target's object flags 0x1 destroyed and 0x2 off): a map row the session removed, or an object that is no critter (<see cref="AreaAiScene.ObjectFacts"/>) destroyed or switched off.</summary>
    public bool Gone(string id)
    {
        if (id == Pc) return false;
        // A map row or companion the host runs is told by its key; only an id that is none of those (and no created critter) can be an object of the scene, which the cast map answers at some cost.
        if (Key(id) is { } key) return _scene.Removed?.Invoke().Contains(key) == true;
        return !id.StartsWith(SessionCreatedObject.CritterPrefix, StringComparison.Ordinal) && _scene.ObjectFacts?.Invoke(id) is { } facts && (facts.Destroyed || facts.Off);
    }

    /// <summary>ARC-AI-1636 (0x429450 and 0x4294F0 test the target's death only for a type 15 or 16): an object that is no critter is never dead; an id the host knows nothing of counts as dead, as before.</summary>
    public bool DeadCritter(string id) => Status(id) is { } status ? status.IsDead : _scene.ObjectFacts?.Invoke(id) is null;

    public AiMoverFacts Mover(string id)
    {
        var status = Status(id);
        var prowling = id != Pc && _session.IsConcealed(SessionCombatant.Critter(id));
        // ARC-AI-1653 (0x426E80): the goals' unconceal test reads the shown art (animation 5, the crouch), not the flag; the end nodes' standing art reads the flag (0x45EDE0).
        return new(Pc: id == Pc, KnockedDown: id != Pc && status is { IsDead: false, IsUnconscious: false } && ShownAnimation(id) == DyingAnimationRules.FallDown,
            Concealed: id != Pc && ShownAnimation(id) == AiExecutorRules.ConcealedAnimation, Prowling: prowling, Fatigue: status?.Fatigue ?? 0, MaxFatigue: status?.MaxFatigue ?? 0, CannotTurn: !Active(id),
            SpreadOut: id != Pc && _session.QueryCompanion(id).SpreadOut,
            // ARC-AI-1711 (0x42EDC0, 0x4C60C0 with skill 6): a flagged critter runs only as a prowling master; no sheet reads as untrained.
            ProwlingTraining: prowling ? NpcSkill(id, Skill.Prowling)?.Training ?? SkillTraining.None : SkillTraining.None);
    }

    /// <summary>The terrain step from the object's tile toward the other's: walls, doors and objects other than critters decide
    /// (inference: the step test 0x425130 measures the way, the occupants are the walk's own test). ARC-WORLD-115 (ledger AP1: the
    /// target is made no-block for the test, other critters still block): the walker's overlay toward the target
    /// (<see cref="Occupancy"/>: the target's T0 record and every moved record away, the live cells of the hero, the companions and the
    /// other movers held). Before, an attacker next to an NPC standing on its own record's cell read that record as an object in its
    /// way, so its approach found no first step and no path.</summary>
    public bool FirstStepClear(string id, string toward, bool ignoreTarget)
    {
        if (Tile(id) is not { } from || Tile(toward) is not { } to) return false;
        if (from == to) return true;
        var chart = ToChart(from);
        var direction = RoutePlanner.Toward(chart, ToChart(to));
        return new AreaRouteSource(Steps, _scene.Chart, () => Occupancy(id, toward), _scene.Passages).Assess(chart, direction) == RouteStepVerdict.Open;
    }

    public AiAttackFacts AttackFacts(string id, string target)
    {
        var line = Line(id, target, out var targetRow);
        var foreign = line?.Blocker is { } blocker && !ReferenceEquals(blocker, targetRow);
        return new(AttackerInactive: !Active(id), TargetGone: Gone(target), TargetDead: DeadCritter(target), ForeignBlocker: foreign,
            NoPathToTarget: foreign && PlanPath(id, new AiPathRequest(AiPathKind.Attack, target, null, 1, false, false), touch: false) is null);
    }

    public AiReachFacts Reach(string id, string target)
    {
        var distance = Tile(id) is { } a && Tile(target) is { } b ? AiExecutorRules.Distance(a, b) : -1;
        var values = Values(id);
        var line = Line(id, target, out var targetRow);
        var clear = line is null || LineOfFireRules.ClearShot(line, targetRow!);
        return new(distance, values?.Weapon.Kind == WeaponKind.Weapon ? values.Range : 0, values?.Weapon.Kind == WeaponKind.Weapon, clear);
    }

    public int AttackCost(string id) => id == Pc
        ? TurnBasedRules.AttackCost(_session.QueryHeroAttack()?.WeaponSpeed ?? TurnBasedRules.UnarmedWeaponSpeed)
        : Values(id)?.AttackCost ?? TurnBasedRules.AttackCost(TurnBasedRules.UnarmedWeaponSpeed);

    public AiPath? PlanPath(string id, AiPathRequest request) => PlanPath(id, request, touch: true);

    /// <summary>ARC-AI-1313: the light level at a world cell for the dark wander (<see cref="AreaAiScene.CellLight"/>; full light without one).</summary>
    public int CellLight(CellCoord cell) => _scene.CellLight?.Invoke(cell) ?? AttackRules.MaxLightLevel;

    // A facts question (AttackFacts) plans without touching: a touch can load a sector and run its fold, which a question must not do.
    private AiPath? PlanPath(string id, AiPathRequest request, bool touch)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Tile(id) is not { } from) return null;
        // ARC-DEV-302 (GT149): the path request set-up (0x425BF0) refuses an entangled PC or NPC: no new path, though one it holds is walked on.
        if (Entangled(id)) return null;
        var goal = request.Tile ?? (request.Target is { } target ? Tile(target) : null);
        if (goal is not { } to) return null;
        if (touch) TouchPath(from, to);
        var source = new AreaRouteSource(Steps, _scene.Chart, () => Occupancy(id, request.Target), _scene.Passages);
        RoutePlan plan;
        switch (request.Kind)
        {
            case AiPathKind.Attack: plan = RoutePlanner.PlanApproach(source, ToChart(from), ToChart(to)); break;
            case AiPathKind.Near when request.Range <= 0:
                // ARC-AI-1309: a walk onto the object's own tile (the pickup goal's approach, range 0): the route to the tile itself, where the approach below ends one step short of it (0x4280D0).
                plan = RoutePlanner.Plan(source, ToChart(from), ToChart(to));
                break;
            case AiPathKind.Near:
                plan = RoutePlanner.PlanApproach(source, ToChart(from), ToChart(to));
                if (plan.Outcome != RouteOutcome.Found) return null;
                // Within the range of the object: the route up to its first cell there.
                var cells = new List<CellCoord>();
                foreach (var cell in plan.Cells)
                {
                    cells.Add(ToWorld(cell));
                    if (AiExecutorRules.Distance(ToWorld(cell), to) <= request.Range) break;
                }
                return new([.. cells]);
            case AiPathKind.Escape when request.Straight: return Straight(from, to);
            default: plan = RoutePlanner.Plan(source, ToChart(from), ToChart(to)); break;
        }
        return plan.Outcome == RouteOutcome.Found ? new([.. plan.Cells.Select(ToWorld)]) : null;
    }

    // A flight's escape line: the straight line of neighbour steps toward the tile, every step accepted, no search.
    private static AiPath? Straight(CellCoord from, CellCoord to)
    {
        var cells = new List<CellCoord>();
        var at = from;
        for (var i = 0; i < 100 && at != to; i++)
        {
            at = RoutePlanner.Neighbor(at, RoutePlanner.Toward(at, to));
            cells.Add(at);
        }
        return cells.Count == 0 ? null : new([.. cells]);
    }

    /// <inheritdoc />
    public IAiDoorWorld? Doors => _scene.DoorWorld?.Invoke();

    /// <inheritdoc />
    [OriginalFunction(0x440700, OriginalFunctionScope.Partial)]
    public string? ClosedDoorOn(string id, CellCoord next) => Tile(id) is { } from ? ClosedDoorBetween(from, next) : null;

    /// <summary>ARC-DEV-209: the closed, idle door the open step from <paramref name="from"/> to the neighbour <paramref name="next"/> crosses (its entity), or null: the step is not a
    /// neighbour step, is not open at T0, crosses no door or only doors that are open or busy.</summary>
    public string? ClosedDoorBetween(CellCoord from, CellCoord next)
    {
        if (_scene.ClosedPortalsOf is not { } portalsOf || _scene.DoorWorld?.Invoke() is not { } doors) return null;
        if (AiExecutorRules.Distance(from, next) != 1) return null;
        var source = new AreaRouteSource(Steps, _scene.Chart, MovedRecords);
        var chart = ToChart(from);
        var direction = RoutePlanner.Toward(chart, ToChart(next));
        var assessment = source.Snapshot.Assess(source.Snapshot.Request(source.Chart.ToWorld(chart), source.Chart.ToWorld(ToChart(next)), GridReplay.MapDirection(direction)), null);
        if (assessment.Disposition != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) return null;
        foreach (var door in portalsOf(assessment))
            if (doors.DoorValid(door) && !doors.DoorOpen(door) && !doors.DoorBusy(door)) return door;
        return null;
    }

    public AiStepBlock StepBlocked(string id, CellCoord next, bool flight)
    {
        if (Tile(id) is not { } from) return AiStepBlock.CannotStart;
        // ARC-DEV-302 (GT148): the step start refuses a paralysed or stoned mover; the walk waits in place and goes on when the hold ends.
        if (Paralysed(id) || Stoned(id)) return AiStepBlock.Held;
        if (LivingCritterOn(next, id)) return AiStepBlock.Occupied;
        var chart = ToChart(from);
        if (AiExecutorRules.Distance(from, next) != 1) return AiStepBlock.CannotStart;
        var direction = RoutePlanner.Toward(chart, ToChart(next));
        var open = new AreaRouteSource(Steps, _scene.Chart, MovedRecords, _scene.Passages).Assess(chart, direction) == RouteStepVerdict.Open;
        // ARC-MAGIC-1189 (TT10): on a step that can start, a trap the walker spots on the next tile blocks it, which ends the walk as a step that cannot start does (EX20);
        // a step that cannot start anyway rolls nothing, as the hero's step gate (asked for an open step only).
        if (open && TrapSpottedOn(id, next)) return AiStepBlock.CannotStart;
        return open ? AiStepBlock.Clear : flight ? AiStepBlock.Occupied : AiStepBlock.CannotStart;
    }

    /// <summary>
    /// ARC-MAGIC-1189 (ledger TT10, SP2): the walk check of the walker's next tile (0x427110): every unknown trap object there gets the NPC's own spot attempt
    /// (<see cref="AreaTraps.WalkCheckNpc"/>, with the NPC's sheet for its spot trap skill and whether the PC leads it); true when it spots one, whose mark a companion writes
    /// (the player knows it from then on). False for the hero, an NPC with no sheet or no life, and when the map has no trap host.
    /// </summary>
    public bool TrapSpottedOn(string id, CellCoord next)
    {
        if (id == Pc || _scene.Traps?.Invoke() is not { } traps || TrapWalker(id)?.Spot is not { } spot) return false;
        var walk = traps.WalkCheckNpc(spot, next);
        if (walk.Spotted is null) return false;
        Trace(id).Step = $"spotted a trap on {next.X},{next.Y}: the walk stops";
        return true;
    }

    /// <summary>ARC-WORLD-1469: the NPC or companion as a trap's triggerer: its entity, whether the PC leads it and, with a sheet and a life, its spot roll's inputs. Null for the hero.</summary>
    private AreaTrapWalker? TrapWalker(string id)
    {
        if (id == Pc) return null;
        // TT2: an NPC with a player character at the top of its chain (0x45DDA0).
        var led = PcLeader(id) == Pc;
        SessionNpcSpot? spot = _scene.NpcSheet?.Invoke(id) is { } sheet && RawBuild(id)?.Target is { } life ? new(life, sheet.Loaded, sheet.Modifiers, led) : null;
        return new(id, led, spot);
    }

    /// <summary>
    /// ARC-WORLD-1469 (ledger TT9, TT8, 0x4305D0): the walker's step arrives on <paramref name="cell"/> (the step the host began, or a companion's own follow step the viewer reports): the first trap
    /// object there meets it (<see cref="AreaTraps.StepNpc"/>, the walker as the triggerer: a known trap fires, an unknown one gets its own spot attempt first). True when the walk ends on that
    /// tile: the trap was spotted or went off, or (ARC-WORLD-1582, TT17) the tile holds any trap object the walker does not step over (<see cref="AreaTraps.StepEndsWalk"/>). False for the hero, when the
    /// map has no trap host, and for a tile with no trap object.
    /// </summary>
    public bool TrapMetOn(string id, CellCoord cell)
    {
        if (TrapWalker(id) is not { } walker || _scene.Traps?.Invoke() is not { } traps) return false;
        // ARC-WORLD-1582 (TT17): the walk ends on any trap object's tile the walker does not step over, also where no meeting was made.
        var ends = traps.StepEndsWalk(cell, walker);
        if (traps.StepNpc(walker, cell) is not { } met) return ends;
        Trace(id).Step = $"met a trap on {cell.X},{cell.Y}: {met.Describe()}";
        _trapMeetings.Add(new(id, met));
        return met.EndsWalk || ends;
    }

    /// <summary>
    /// ARC-WORLD-1469 (ledger TT7, 0x441980 with point 1): the walker uses a trapped object (a door its opener goal pushes). The meeting of its trap with the walker as the triggerer
    /// (<see cref="AreaTraps.UseNpc"/>); true when the use stops (the trap was spotted now, a native run went off, or the session refused the meeting), which ends the opener's run.
    /// </summary>
    public bool TrapMetByUse(string id, AreaObjectRecord row)
    {
        if (TrapWalker(id) is not { } walker || _scene.Traps?.Invoke() is not { } traps || traps.UseNpc(row, walker) is not { } met) return false;
        Trace(id).Step = $"used a trapped object: {met.Describe()}";
        _trapMeetings.Add(new(id, met));
        return met.Cancels;
    }

    private readonly List<AreaTrapNotice> _trapMeetings = [];

    /// <summary>ARC-WORLD-1469: the trap meetings the walkers had since the last call (the viewer logs them and shows the status line), oldest first; the list is emptied.</summary>
    public IReadOnlyList<AreaTrapNotice> TakeTrapMeetings()
    {
        var taken = _trapMeetings.ToArray();
        _trapMeetings.Clear();
        return taken;
    }

    /// <summary><see cref="IAiGoalWorld.StepMeetsTrap"/>: the step just begun onto <paramref name="next"/> meets its trap object (<see cref="TrapMetOn"/>); nothing when that step did not begin.</summary>
    public bool StepMeetsTrap(string id, CellCoord next)
    {
        // Only a step that really began meets its trap: a companion's walk that refused the step, or a mover the host does not run, stands on no new tile.
        var began = _startedStep == id;
        _startedStep = null;
        return began && TrapMetOn(id, next);
    }

    // The walker whose step <see cref="BeginStep"/> just began (null: none began).
    private string? _startedStep;

    // ---- ARC-DEV-141 (MIG13 S8c): the pushed NPC's bodies for the knockback host (AreaAiKnockbackBodies) ----

    /// <summary>Whether <paramref name="id"/> is a map NPC or (ARC-DEV-285, GT59: goal 43 has no test of the pushed object's kind) a companion the host runs that the knockback can push here, or (ARC-DEV-287) the
    /// hero when the scene gives its body (<see cref="AreaAiScene.HeroPush"/>). A companion's place is the followers' own, so its slide is their jump onto each tile (<see cref="IAreaAiFollowers.Jump"/>); the hero's is the viewer's relocation.</summary>
    public bool CanBePushed(string id) => id == Pc ? _scene.HeroPush is not null : _npcs.ContainsKey(id) || _followers.ContainsKey(id) && _scene.Followers is not null;

    /// <summary>The pushed object's rotation 0..7 now (an NPC's mover's facing, else east; the hero's art rotation).</summary>
    public int PushRotation(string id) => id == Pc ? _scene.HeroPush?.Rotation ?? 0 : (int)Movers.FacingOf(id, NeighborDirection.XPlus);

    /// <summary>Whether the shown map holds the world tile (the loaded-sector chart, else the chart's range).</summary>
    public bool OnMap(CellCoord world) => _scene.LoadedSteps is not null || _scene.Chart.Contains(ToChart(world));

    /// <summary>
    /// The block test of the knockback's step from <paramref name="left"/> toward its neighbour <paramref name="tested"/> (0x42FD70, GT61): the terrain test of a walker's step (walls, doors, objects other than critters,
    /// the map data's blocked tiles), and a living critter other than the pushed NPC (the hero, a companion, an NPC) on that tile, on any tile change.
    /// </summary>
    [OriginalFunction(0x42FD70, OriginalFunctionScope.Partial)]
    public KnockbackBlock PushBlock(string id, CellCoord left, CellCoord tested)
    {
        var chart = ToChart(left);
        var direction = RoutePlanner.Toward(chart, ToChart(tested));
        if (new AreaRouteSource(Steps, _scene.Chart, MovedRecords).Assess(chart, direction) != RouteStepVerdict.Open) return KnockbackBlock.Terrain;
        return LivingCritterOn(tested, id) ? KnockbackBlock.Critter : KnockbackBlock.None;
    }

    /// <summary>The NPC's slide (a companion's: the followers' jump onto the tile, its place being the group's, no session record, its tile script run through an <see cref="AreaAiNoticeKind.Entered"/> notice (ARC-DEV-298), false when the walk refuses) onto <paramref name="tile"/> over <paramref name="ms"/> milliseconds from <paramref name="nowMs"/> (the step's world clock, so the slide does not start a frame late), its facing kept; the place is recorded in the session (one revision).</summary>
    public bool PushSlide(string id, CellCoord tile, int ms, long nowMs)
    {
        // ARC-DEV-287: the hero's place is the viewer's movement runtime: the relocation, its location write and its tile's entry events are the body's.
        if (id == Pc) return _scene.HeroPush?.Slide(tile) == true;
        // ARC-DEV-285: a companion's body is the followers' own: the push puts it on each tile at once (no sub-tile slide, as an NPC's neither), its facing kept.
        if (!_npcs.TryGetValue(id, out var npc))
        {
            // ARC-DEV-298 (B25, GR12): a pushed companion is a PC-side NPC that moved onto the tile, so the tile's script runs for it as for a step (TG2, TR3); its
            // catch-up to the leader (MoveToLeader) and its route relocation (RelocateFollower) tell their own cell (ARC-DEV-328); its steps are the companion drive's.
            if (!_followers.ContainsKey(id) || _scene.Followers?.Jump(id, tile) != true) return false;
            _notices.Add(new(AreaAiNoticeKind.Entered, id, null) { Cell = tile });
            return true;
        }
        _nowMs = Math.Max(_nowMs, nowMs);
        Movers.Step(id, npc.Row.Key, npc.Origin, tile, _nowMs, ms, keepFacing: true);
        RecordPlace(id);
        NoteEntered(id, tile);
        return true;
    }

    /// <summary>
    /// The collision damage of a blocked knockback step (GT66): normal and fatigue damage, each rolled 1 to <paramref name="maximum"/>, through the damage step with no attacker; the session's life change
    /// starts the stun, the fall or the death its result asks for (which ends the push, GT62). Nothing for an NPC without a life record.
    /// </summary>
    public void PushBump(string id, int maximum)
    {
        if (id == Pc) { PushHeroBump(maximum); return; }
        if (Build(id) is not { } build || Status(id) is not { } status || LifeState(id) is not { } state) return;
        var normal = _session.AiRolls.Roll(AiRollPurpose.KnockbackBump, 1, Math.Max(maximum, 1));
        var fatigue = _session.AiRolls.Roll(AiRollPurpose.KnockbackBump, 1, Math.Max(maximum, 1));
        // ARC-DEV-261 (GT198): the pushed NPC's taking-damage script comes first in the damage apply, with no attacker; a veto cancels the bump (the amounts are drawn already).
        if (_scene.TakingDamage is { } asks && !asks(id, null)) return;
        var step = AttackResolver.DamageStep(new DamageStepRequest
        {
            Attacker = new Combatant(), Target = build.Combatant(status, state, _session.Rules.Combat), TargetKind = AttackTargetKind.Critter, Difficulty = _session.Difficulty,
            Damage = DamageByType.Zero.With(DamageType.Normal, normal).With(DamageType.Fatigue, fatigue),
        });
        if (LifeDamage.FromStep(step) is { } damage && _session.ApplyDamage(_session.Version, build.Target, damage, new SessionDamageParties(null, null)).Disposition == GameSessionDisposition.Changed)
            Struck(id);
    }

    // ARC-DEV-287: the hero's collision damage, the same draw and damage step as an NPC's, from the hero's own combatant and life (no stats of an attacker, no attacker party). The taking-damage script is the NPC's (the hero has
    // none); a dead hero takes nothing (0x4B4784), and the life step starts the fatigue knock-out or the death its result asks for (the bump itself never stuns or knocks down, Tail T4d read).
    private void PushHeroBump(int maximum)
    {
        if (_session.QueryHero() is null || Status(Pc) is not { IsDead: false }) return;
        var normal = _session.AiRolls.Roll(AiRollPurpose.KnockbackBump, 1, Math.Max(maximum, 1));
        var fatigue = _session.AiRolls.Roll(AiRollPurpose.KnockbackBump, 1, Math.Max(maximum, 1));
        var step = AttackResolver.DamageStep(new DamageStepRequest
        {
            Attacker = new Combatant(), Target = _session.QueryHeroAttack()?.Combatant ?? new Combatant { IsPc = true }, TargetKind = AttackTargetKind.Critter, Difficulty = _session.Difficulty,
            Damage = DamageByType.Zero.With(DamageType.Normal, normal).With(DamageType.Fatigue, fatigue),
        });
        if (LifeDamage.FromStep(step) is { } damage) _session.ApplyDamage(_session.Version, SessionLifeTarget.Hero, damage, new SessionDamageParties(null, null));
    }

    /// <summary>The step onto <paramref name="next"/>: a map NPC's mover takes the cell at once and slides its body there over the step's
    /// time; ARC-WORLD-115: a companion's own walk is told to step there (<see cref="IAreaAiFollowers.Step"/>), its cell changing as
    /// that walk goes.</summary>
    // ARC-WORLD-115: the step's terrain test with the T0 records the planner moves too (the moved NPCs' and the companions'; the ones
    // the session removed), no live cells (the destination's critter is LivingCritterOn's): a route through a companion's home cell or
    // a walked NPC's origin is then not refused step by step while its planner keeps planning it.
    private AreaOccupancy MovedRecords()
    {
        var occupancy = new AreaOccupancy(Movers.Moved.Concat(_followers.Values.Select(n => n.Row.Key))
            .Concat((_scene.Companions?.Invoke() ?? []).Select(c => c.Key)), []);
        return _scene.Removed?.Invoke() is { Count: > 0 } removed ? occupancy.WithRemoved(removed) : occupancy;
    }

    /// <summary>ARC-DEV-124: the steps a run began while a round ran and the round's actor was another participant (a diagnostic: the executor pays a step with the
    /// round's action points, so a step off its turn is a defect; the ai-turn-based smoke requires none).</summary>
    public int OffTurnSteps { get; private set; }

    public int BeginStep(string id, CellCoord next, bool running)
    {
        _startedStep = null;
        if (!TryNpc(id, out var npc) || Tile(id) is not { } from) return 1;
        if (InRound && RoundActor != id) OffTurnSteps++;
        // ARC-RENDER-2539 (0x42EDC0): a flagged critter below prowling expert steps the stealth walk, a run wanted ignored (the goals' RunPermitted already refuses it).
        var sneaking = id != Pc && _session.IsConcealed(SessionCombatant.Critter(id)) && AiExecutorRules.Sneaks(Mover(id));
        if (sneaking) running = false;
        var gait = running ? MovementGait.Run : sneaking ? MovementGait.Sneak : MovementGait.Walk;
        var direction = AreaCombatHost.Facing(from, next) ?? NeighborDirection.XPlus;
        var duration = _scene.Timing?.For(gait, direction) ?? DiagnosticGridSession.LiveDuration;
        var ms = (int)Int128.Clamp(duration.Value.Numerator * 1000 / duration.Value.Denominator, 1, 10_000);
        if (_followers.ContainsKey(id)) { if (_scene.Followers!.Step(id, next, running, sneaking)) _startedStep = id; else Trace(id).Step = $"walk refused the step to {next.X},{next.Y}"; }
        else { Movers.Step(id, npc.Row.Key, npc.Origin, next, _nowMs, ms, running, sneaking: sneaking); RecordPlace(id); NoteEntered(id, next); _startedStep = id; }
        _notices.Add(new(AreaAiNoticeKind.Step, id, null));
        return ms;
    }

    public int GetUp(string id) => 1;

    /// <summary>
    /// ARC-AI-1653 (goal 54, collapsed to its end node 0x42AFB0 as the get-up is): the NPC stands up (its art goes back to animation 0) and, unless its leader is concealed,
    /// the session ends its concealment (<see cref="GameSessionOwner.EndNpcConcealmentForUnconceal"/>), so a follower of a sneaking hero only stands up and
    /// crouches again when its next goal ends, as the original's does. The run's animation time is not modelled (1 ms, as the get-up).
    /// </summary>
    public int Unconceal(string id)
    {
        if (id == Pc) return 1;
        ShowArt(id, 0, 0);
        _session.EndNpcConcealmentForUnconceal(_session.Version, id);
        return 1;
    }

    // ARC-AI-1653 (0x45EE90): setting the flag shows the crouch art and clearing it the standing one, at the moment of the change. The session owns the flag, so the host sees the change here
    // (the flag as it last saw it), before it reads the art; a follower that stood up with the flag still set (leader concealed) keeps standing until a goal's end node crouches it again.
    private readonly HashSet<string> _crouchFlag = [];

    private void SyncCrouch(string id)
    {
        if (id == Pc) return;
        var flagged = _session.IsConcealed(SessionCombatant.Critter(id));
        if (flagged == _crouchFlag.Contains(id)) return;
        if (flagged)
        {
            _crouchFlag.Add(id);
            if (Status(id) is { IsDead: false, IsUnconscious: false } && !_arts.ContainsKey(id))
                _arts[id] = (AiExecutorRules.ConcealedAnimation, Math.Max((Clip(id, AiExecutorRules.ConcealedAnimation)?.Frames ?? 1) - 1, 0));
        }
        else
        {
            _crouchFlag.Remove(id);
            if (_arts.TryGetValue(id, out var art) && art.Animation == AiExecutorRules.ConcealedAnimation) _arts.Remove(id);
        }
    }

    // ---- the critter states (ARC-DEV-137, MIG13 S6: goals 57, 62, 63 and 31) ----

    // The art each NPC shows now, as the critter-state goals set it (the original's object art: animation and frame). An NPC with no entry shows what its life says (a dead or unconscious one
    // lies in its fall, others stand), as a body first seen after a load or a map change does.
    private readonly Dictionary<string, (int Animation, int Frame)> _arts = [];

    /// <summary>Counts the art writes of the critter-state goals, so the viewer redraws the lying bodies when a frame changes.</summary>
    public long ArtRevision { get; private set; }

    /// <summary>The art the critter-state goals set for the NPC (null: none set, or standing again).</summary>
    public (int Animation, int Frame)? ArtOf(string id)
    {
        if (!_arts.TryGetValue(id, out var art)) return null;
        // A dying animation of an NPC that is no longer dead (a resurrection) is a stale write, not a pose.
        if (art.Animation is >= DyingAnimationRules.Decapitation and <= DyingAnimationRules.SeveredLeg && Status(id) is { IsDead: false }) { _arts.Remove(id); return null; }
        return art;
    }

    /// <summary>Whether a dying run (goal 31) of the NPC is on the table: its pool of blood waits for the run to end (B13, 0x42FED0).</summary>
    public bool DyingRunning(string id) => _session.QueryGoals().RunsOf(id).Any(run => run.Frames.Any(f => f.Goal == GoalType.Dying));

    /// <summary>ARC-UI-1737 (ledger OH2): the "violence filter" setting the dying goal's art node reads when it runs (the viewer sets it at the start and at every change of the option).</summary>
    public bool ViolenceFilter { get; set; }

    public AiAnimationClip? Clip(string id, int animation) => _scene.AnimationClip?.Invoke(id, animation);

    /// <summary>
    /// ARC-COMBAT-1741: the hero's stun run (goal 57, started by the session's life step) stepped by the NPC's stun nodes over this host's ports (the art writes, the clip of the hero's stunned ART, the round test), so the stun
    /// lasts its frames and plays out the countdown as an NPC's does (GT26, GT27). The hero host owns the run's end hooks; nothing here frees it. No readable stun ART (the clip provider answers none) ends it at its first step.
    /// </summary>
    public SessionGoalStepAnswer StepHeroStun(GoalRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var step = AiExecutorRules.Step(run, this, _session.AiRolls, _session.Rules.Combat);
        _steps++;
        if (!step.Ended && step.Run is { } next) return SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next);
        ClearHeroArt();
        return SessionGoalStepAnswer.End;
    }

    /// <summary>ARC-COMBAT-1741: the hero shows its standing art again (its stun ended or was cut short).</summary>
    public void ClearHeroArt()
    {
        if (_arts.Remove(Pc)) ArtRevision++;
    }

    // An NPC with no art written lies in its fall when it is unconscious or dead (a body first seen), except a dead one whose dying goal is on the table: it was killed standing and the goal sets the art.
    public int ShownAnimation(string id) => SyncedArt(id) is { } art ? art.Animation
        : Status(id) switch { { IsDead: true } => DyingRunning(id) ? 0 : DyingAnimationRules.FallDown, { IsUnconscious: true } => DyingAnimationRules.FallDown, _ => 0 };

    public int ShownFrame(string id) => SyncedArt(id) is { } art ? art.Frame : ShownAnimation(id) == 0 ? 0 : Math.Max((Clip(id, ShownAnimation(id))?.Frames ?? 1) - 1, 0);

    private (int Animation, int Frame)? SyncedArt(string id)
    {
        SyncCrouch(id);
        return _arts.TryGetValue(id, out var art) ? art : null;
    }

    public void ShowArt(string id, int animation, int frame)
    {
        if (animation == 0) _arts.Remove(id);
        else _arts[id] = (animation, frame);
        ArtRevision++;
    }

    public bool Unconscious(string id) => Status(id)?.IsUnconscious ?? false;

    // ARC-DEV-302: the spell and critter flags the spells wrote (the session's) over the critter's data; the hero has no data flags here.
    public bool Stoned(string id) => (_session.QueryObjectFlags(id).Spell(id == Pc ? 0 : unchecked((int)(Fields(id)?.Object.SpellFlags ?? 0))) & StonedSpell) != 0
        && (id == Pc || Fields(id) is not null);

    /// <summary>ARC-AI-1519: critter flag 0x20 (the stun condition, or the flag the data or a spell wrote), without paralysis 0x40, as the sight and hearing checks read it.</summary>
    private bool Stunned(string id) =>
        id == Pc ? (HeroLifeState()?.Conditions.HasFlag(LifeConditions.Stunned) ?? false) || (_session.QueryObjectFlags(Pc).Critter(0) & unchecked((int)StunnedCritter)) != 0 :
        (LifeState(id)?.Conditions.HasFlag(LifeConditions.Stunned) ?? false) || Fields(id)?.Critter is { } critter && (CritterFlagsOf(id, critter.Flags) & StunnedCritter) != 0;

    public bool Paralysed(string id) => (_session.QueryObjectFlags(id).Critter(id == Pc ? 0 : unchecked((int)(Fields(id)?.Critter?.Flags ?? 0))) & ParalysedCritter) != 0
        && (id == Pc || Fields(id)?.Critter is not null);

    /// <summary>ARC-DEV-302 (GT145): the spell flag entangled (0x1000000) the spells wrote over the critter's data.</summary>
    public bool Entangled(string id) => (_session.QueryObjectFlags(id).Spell(id == Pc ? 0 : unchecked((int)(Fields(id)?.Object.SpellFlags ?? 0))) & EntangledSpell) != 0
        && (id == Pc || Fields(id) is not null);

    public void ClearStunned(string id)
    {
        if (id != Pc && RawBuild(id) is { } build && LifeState(id) is { } state && state.Conditions.HasFlag(LifeConditions.Stunned)) _session.ClearStun(_session.Version, build.Target);
    }

    public void DyingFinished(string id) { }

    public void Face(string id, string toward)
    {
        if (TryNpc(id, out var npc) && Tile(toward) is { } there) FaceNpc(npc, there);
    }

    /// <summary>
    /// ARC-UI-2029 (ledger TB26): the stop-and-face step of the speech display for a speaking NPC. With a listener, a speaker that is alive (hit points 1 or more), able to act and running no animation
    /// of its own (an overlay is no animation) turns to the listener's cell; the display's level-3 interrupt of the speaker's runs finds only overlays at that point and ends nothing, so a walking
    /// or acting speaker neither stops nor turns. The turn is the host's instant one (<see cref="FaceNpc"/>), as every other host turn. False when nothing turned: no listener cell, not an NPC the host
    /// walks, dead, unconscious, unable to act, or busy.
    /// </summary>
    [OriginalFunction(0x568540, OriginalFunctionScope.Partial)]
    public bool FaceListener(string speaker, string listener)
    {
        if (!TryNpc(speaker, out var npc) || Tile(listener) is not { } there) return false;
        if (Status(speaker) is not { IsDead: false, IsUnconscious: false } || !CanAct(speaker) || AnimationRunning(speaker)) return false;
        FaceNpc(npc, there);
        return true;
    }

    public void PlaySound(string id, int sound) => _notices.Add(new(AreaAiNoticeKind.Sound, id, null) { Sound = sound });

    /// <summary>
    /// The attempt-attack goal's start: its node 5 (0x42B9C0, read by this task) puts the attacker into combat mode, and a PC target,
    /// or an NPC target's living PC leader, too (0x4B3F50); then the turn and the attack animation (0x432700): the NPC's attack ART
    /// played at its Speed and weapon speed (<see cref="RealTimeCombatRules"/>), whose action frame is the hit.
    /// </summary>
    public AiAttackSwing BeginAttack(string id, string target)
    {
        if (!TryNpc(id, out var npc) || Values(id) is not { } values) return new(1, 1);
        if (NpcCombatMode(id) is { } context) _session.SetCombatMode(_session.Version, SessionCombatant.Critter(id), true, context);
        // ARC-COMBAT-2597 (VC38): the NPC target's leader is followed up the chain to the PC (0x45DDA0), not only the direct leader.
        if (target == Pc || PcLeader(target) is not null && !(Status(Pc)?.IsDead ?? true)) HeroIntoCombatMode();
        // ARC-RENDER-038: the attack ART of the animation and the weapon the attacker's body shows (WA1, WA4), its frames, action frame
        // and the rate of its weapon slot; an ART the viewer cannot read plays with the fixed human attack ART's timing.
        var wanted = AttackRequest(id, target);
        var answer = _scene.AttackArt?.Invoke(npc, wanted);
        // ARC-COMBAT-1932 (VC38, 0x42B9C0): an ART that exists in no file after the missing-art repair fails the node, which has only entered
        // combat mode so far (it does not turn the attacker): the goal ends with no swing, no bark, no begin effect and no hit.
        if (answer is { Missing: true }) return AiAttackSwing.NoArt;
        Face(id, target);
        var art = answer is { Frames: >= 1 and <= RealTimeCombatRules.MaxFrames, ActionFrame: >= 0 and <= RealTimeCombatRules.MaxFrames } read ? read : null;
        var (frames, actionFrame, rate, weapon) = art is null
            ? (FixedAttackArt.Frames, FixedAttackArt.ActionFrame, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Unarmed,
                Math.Clamp(values.Speed, 1, RealTimeCombatRules.MaxStat), FixedAttackArt.RawFramesPerSecond), AttackArtWeapon.Unarmed)
            : (art.Frames, art.ActionFrame, AreaAiAttackArtRules.FramesPerSecond(art, wanted.Animation, values.Speed), AreaAiAttackArtRules.ClipWeapon(art));
        var clip = new AttackClip(frames, actionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, Math.Clamp(values.WeaponSpeed, -RealTimeCombatRules.MaxStat, RealTimeCombatRules.MaxStat)),
            weapon);
        var moments = RealTimeCombatRules.Schedule(clip);
        var hit = moments.FirstOrDefault(m => m.Kind == AttackUpdateKind.Hit);
        var end = moments[^1];
        var action = (int)Math.Max(1, hit.Kind == AttackUpdateKind.Hit ? hit.AtMs : end.AtMs);
        var swing = new AiAttackSwing(action, (int)Math.Max(1, end.AtMs - action));
        var play = new AreaAiAttackPlay(id, _nowMs, wanted, art, moments);
        _attackPlays[id] = play;
        // ARC-RENDER-1126 (0x432700): every attacking PC or NPC rolls 1..4 at the attack's start and barks on 1; the roll comes from the session's generator here, so a headless host draws it as the viewer's does.
        var bark = _session.Roll("combat.AttackSound", 1, RealTimeCombatRules.AttackSoundSides).Roll?.Value == 1;
        _notices.Add(new(AreaAiNoticeKind.Swing, id, target) { Swing = swing, Play = play, AttackBark = bark });
        // ARC-RENDER-2011 (0x432700, the last step of the begin node): the weapon's begin effect (kind 0) on the attacker.
        WeaponAttackEffect(id, WeaponHitEffectRules.Begin, WieldedPrototype(id), Tile(target));
        return swing;
    }

    /// <summary>ARC-RENDER-038: the NPC's attack animation playing at <paramref name="nowMs"/> and its frame, or null.</summary>
    public (AreaAiAttackPlay Play, int Frame)? AttackPose(string id, long nowMs) =>
        _attackPlays.TryGetValue(id, out var play) && play.Frame(nowMs) is { } frame ? (play, frame) : null;

    /// <summary>
    /// ARC-RENDER-038: the weapon part and shield bit the NPC's body shows now (0x465020, ledger WA1): none out of combat mode; in it the
    /// use-art subtype of the item it wields (bare hands without one) and the shield bit for an armour item in its shield slot, its worn
    /// items as the session shows them (<see cref="AreaCombatHost.Worn"/>; none read: bare hands).
    /// </summary>
    public (int Weapon, int Shield) ShownWeapon(string id)
    {
        if (id == Pc) return (0, 0);
        // Asked per mover and frame: kept per session revision (combat mode and worn items change only with it).
        var revision = _session.Version.Revision;
        if (_shownRevision != revision) { _shown.Clear(); _shownRevision = revision; }
        if (_shown.TryGetValue(id, out var known)) return known;
        var shown = (0, 0);
        if (_session.QueryCombatMode(SessionCombatant.Critter(id)))
        {
            var (use, _, shield) = Wielded(id);
            shown = (WeaponArtRules.WeaponPart(true, use), WeaponArtRules.ShieldPart(true, shield));
        }
        _shown[id] = shown;
        return shown;
    }

    /// <summary>
    /// ARC-RENDER-038: the attack ART an NPC's attack on <paramref name="target"/> wants (WA1, WA4): its wielded weapon's part and flags,
    /// its shield, and the target's stored object flags (the shrunk flag), its art (its height; the hero's is a critter's) and its current
    /// animation, read as lying (7) while it is unconscious or dead, else its stored art's (<see cref="WeaponArtRules.AnimationOf"/>; a
    /// target mid-fall, getting up or knocked down is NOT READY).
    /// </summary>
    public AreaAiAttackArtRequest AttackRequest(string id, string target)
    {
        var (use, flags, shield) = Wielded(id);
        var targetFields = Fields(target);
        // ARC-AI-1756 (0x42B9C0 node 5): the node reads the target's art (field 1) and object flags (field 0x13) whatever the target is. An object that is no critter (a bed, a trap's shooter, a door) has its own: the
        // flag 0x80 can pick the low attack, its art type is no body so its animation reads 0 and its height 1 (0x503E20, 0x503F60), and it is never lying (the status tests are a critter's).
        if (target != Pc && targetFields is null && _scene.ObjectFacts?.Invoke(target) is { } facts)
            return AreaAiAttackArtRules.Request(use, flags, shield, facts.Flags, WeaponArtRules.AnimationOf(facts.Art), facts.Art);
        var lying = Status(target) is { IsUnconscious: true } or { IsDead: true };
        var targetArt = targetFields?.Object.CurrentArt ?? BodyArmorArtRules.CritterArt << 28;
        return AreaAiAttackArtRules.Request(use, flags, shield, targetFields?.Object.Flags ?? 0,
            lying ? AreaAiAttackArtRules.LyingAnimation : WeaponArtRules.AnimationOf(targetArt), targetArt);
    }

    // The use art and weapon flags of the item in the NPC's weapon slot and the type of the item in its shield slot.
    private (uint? UseArt, uint Flags, int? ShieldType) Wielded(string id)
    {
        var worn = _scene.Combat.Worn(_session, id);
        var weapon = worn?.FirstOrDefault(item => item.Slot == EquipmentSlot.Weapon);
        var shield = worn?.FirstOrDefault(item => item.Slot == EquipmentSlot.Shield);
        return (weapon is { Entity: not null } w ? w.Fields.Item?.UseArtFragment : null,
            weapon is { Entity: not null } f ? (uint)(f.Fields.Weapon?.Flags ?? 0) : 0,
            shield is { Entity: not null } s ? s.Fields.Object.Type : null);
    }

    // ARC-DEV-261 (GT198): the taking-damage script of whoever the NPC's attack damages: the target (not the hero, who has no script data here), or the NPC itself on a critical self-hit, with the attacker as the damage's attacker.
    private Func<DamageRecipient, bool>? TakingDamageOf(string attacker, string? target) =>
        _scene.TakingDamage is not { } asks ? null : recipient => recipient == DamageRecipient.Target ? target is null || target == Pc || asks(target, attacker) : asks(attacker, attacker);

    // ARC-SCRIPT-1483: the combat-point scripts of an NPC's hit on <paramref name="victim"/> (null: no victim named, none run).
    private Action<SessionHitScriptEvent>? HitScriptsOf(string attacker, string? victim) =>
        _scene.HitScripts is not { } run || victim is null ? null : hit => run(attacker, victim, hit);

    /// <summary>The hit step: the session's NPC attack (<see cref="GameSessionOwner.AttackByNpc"/>), then the attack event. ARC-DEV-282: a bow or firearms attack whose weapon has a projectile
    /// art is decided here and its arrow flies (<see cref="AreaAiScene.LaunchShot"/>); its damage is <see cref="LandShot"/>'s, at the strike.</summary>
    public void Hit(string id, string target)
    {
        // ARC-AI-1756 (0x4B3170, SC16): a scenery target that holds an occupant (a bed's sleeper) is replaced by the occupant in both target slots before anything else; the check then carries -30.
        var inBed = false;
        if (OccupantInPlaceOf(id, target) is { } occupant) { target = occupant; inBed = true; }
        var aimed = target;
        // ARC-COMBAT-1140 (RA4, RH5): a door, container, scenery, trap or wall that stops the line of fire takes the shot in place of the target.
        if (HitBlockingObject(id, target)) return;
        target = LineOfFireStruck(id, target);
        // ARC-AI-1636: the focus is an object that is no critter (a door, container, scenery or trap) and nothing stops the line to it: the attack is the session's object attack.
        if (target == aimed && target != Pc && _scene.ObjectFacts?.Invoke(target) is not null) { HitObjectFocus(id, target); return; }
        if (NpcAttackOf(id, target, flight: _scene.LaunchShot is not null, inBed) is not { } attack) return;
        // ARC-COMBAT-1930 (0x4B2810): the loudness is the context weapon's, read before the roll (a critical may drop or break the weapon).
        var noise = AwarenessRules.WeaponLoudness(Wielded(id).Flags);
        var weapon = WieldedPrototype(id); // the context's weapon, read before the strike (a critical may drop or break it)
        var result = _session.AttackByNpc(_session.Version, attack);
        Trace(id).Hit = $"{result.Disposition} {result.Failure} {result.Resolution?.Outcome} {result.Life?.HitPointDamage}" + (target == aimed ? "" : $" (the line of fire struck {target} instead of {aimed})");
        _notices.Add(new(AreaAiNoticeKind.Hit, id, target) { Result = result });
        NeedsAmmunition(id, result.Resolution);
        if (result.Disposition != GameSessionDisposition.Rejected && result.Resolution?.Refusal == AttackRefusal.None)
        {
            // ARC-COMBAT-1930 (0x4B3170, PF1, VC28, VCG9): a shot that flies sends no attack event at the shot; the projectile's end alerts only the object it strikes. A shot decided a hit raises its event here for the aimed target
            // (its arrival's time is the accepted deviation A), a shot decided a miss raises none (PF5, PF6: it never strikes its aimed target; a stray it meets is alerted by LandShot). An attack that does not fly raises it,
            // hit or miss, with the weapon's loudness (0x4B2810).
            // ARC-COMBAT-2164 (EX15, 0x4B3170): an attack that does not fly and met a line-of-fire blocker notifies the aimed target in the Original setting and the blocker (accidental) in Steamwoven (HostilityRules.RangedBlockerNotice); an aimed object that is no critter has no event to take.
            if (result.Shots.Count == 0 && target != aimed) BlockerAttackEvent(id, aimed != Pc && _scene.ObjectFacts?.Invoke(aimed) is not null ? null : aimed, target, noise);
            else if (result.Shots.Count == 0 || result.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit) AttackEvent(id, target, loudness: noise);
            // ARC-RENDER-2011 (0x4B3170): the weapon's swing effect (kind 1) on the attacker for every attack that was not refused, hit or miss, after the roll and the strike and before the hit effect.
            WeaponAttackEffect(id, WeaponHitEffectRules.Swing, weapon, Tile(target));
            // ARC-DEV-292: a hit or a miss on an NPC that did not fly (a shot's damage is LandShot's) gives the victim its immediate pass (0x4B52FC, 0x4B3948, the ranged miss 0x4B30F5).
            HitEffect(target, result, WieldedPrototype(id));
            if (VictimReached(result)) Struck(target);
        }
        // ARC-DEV-282: the arrows leave the shooter's tile for the target's; one the viewer cannot fly strikes at once (a hit lands, a miss ends).
        foreach (var shot in result.Shots)
            if (Tile(id) is not { } from || Tile(target) is not { } to || _scene.LaunchShot?.Invoke(id, target, shot, from, to) != true)
            {
                LandShot(id, shot, target);
                _session.EndShot(shot);
            }
    }

    /// <summary>
    /// ARC-AI-1139 (0x4B3170 → 0x4ADFF0): an NPC's ranged attack the ammunition check refused (<see cref="AttackRefusal.OutOfAmmunition"/>, a companion with too few units of its
    /// weapon's type) marks the NPC as needing ammunition, a weapon and a weapon check: LOOK_FOR_AMMO, LOOK_FOR_WEAPON and CHECK_WEAPON (flags 0x0A000080) in one revision. Its next AI pass
    /// re-wields (<see cref="ReWield"/>) and runs the item search (<see cref="SearchItems"/>). Any other outcome sets nothing.
    /// </summary>
    private void NeedsAmmunition(string npc, AttackResolution? resolution)
    {
        if (resolution?.Refusal != AttackRefusal.OutOfAmmunition) return;
        NeedsAmmunition(npc);
    }

    // ARC-COMBAT-2415 (0x4B65D0 → 0x4ADFF0): the same marks for a thrown weapon the ammunition check refused (a throw at a location makes no attack to read the refusal from).
    private void NeedsAmmunition(string npc)
    {
        var set = _session.SetAiSupportBits(_session.Version, npc, DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.CheckWeapon);
        Trace(npc).Item = $"out of ammunition {set.Disposition} {set.Failure}";
    }

    /// <summary>
    /// ARC-DEV-284 (RA4, RH7; the hero's StrikeBlocker): on the ranged path the line of fire to the target (0x4ADE00) may be stopped by another critter, and the original strikes that blocker instead
    /// of the target (its attack check, distance and line are then the blocker's). Returns the entity of the critter that stops the line before <paramref name="target"/>, else <paramref name="target"/>.
    /// A blocker that is no critter of the host (a wall, a door, scenery, a container, the hero: the line map holds no hero) is not ready: the shot keeps its target, whose check the blocked line fails.
    /// </summary>
    private string LineOfFireStruck(string id, string target)
    {
        if (Distance(id, target) <= 1 || Tile(id) is not { } from || Tile(target) is not { } to || _scene.LineOfFire?.Invoke() is not { } map) return target;
        var trace = LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat);
        if (trace.Blocker is not { Type: 15 or 16 } blocker || blocker.Key == Key(target)) return target;
        var other = AreaConversationState.EntityId(blocker);
        // The host must know where the blocker stands (a critter it neither walks nor runs as a companion would be hit point-blank with no line of its own).
        if (other == id || other == target || RawBuild(other) is null || Status(other) is not { IsDead: false } || Tile(other) is null) return target;
        return other;
    }

    /// <summary>
    /// ARC-DEV-282 (PF2, PF3): an arrow or bullet of <paramref name="shooter"/> reached <paramref name="struck"/> (an NPC, or the hero): the damage of the attack decided at the shot
    /// (<see cref="GameSessionOwner.LandNpcShot"/>): the shot's own target takes the carried hit, another one it met on the way a forced hit. The result is noticed as a hit
    /// (<see cref="AreaAiNoticeKind.Hit"/>), and a stray strike raises the attack event on the struck one (the aimed target's was raised at the shot; the shot's own target gets none). Null when the shooter or the struck
    /// one has no combat values or the strike is refused (the shot is gone either way).
    /// </summary>
    public SessionAttackResult? LandShot(string shooter, SessionShot shot, string struck)
    {
        // The aimed target's attack event was raised at the shot; a stray target's comes with its strike.
        var shotFacts = _session.QueryNpcShot(shot);
        var aimed = shotFacts is { } facts ? facts.Target == SessionNpcShotFacts.HeroTarget ? Pc : facts.Target : null;
        // ARC-COMBAT-2693: a grenade's stray strike is the grenade's (no weapon of its own): the wielded weapon's hit effect does not play on the victim.
        var strikeWeapon = shotFacts is { Generic: true } ? null : WieldedPrototype(shooter);
        if (NpcAttackOf(shooter, struck, flight: false) is not { } attack) { EndStruckShot(shot); return null; }
        // The strike is a distance-1 hit with no line of its own (the carried check holds the cover); the shooter's worn items are what it wears now.
        var result = _session.LandNpcShot(_session.Version, shot, attack with { Distance = 1, LineOfFire = null });
        EndStruckShot(shot);
        Trace(shooter).Hit = $"shot {result.Disposition} {result.Failure} {result.Resolution?.Outcome} {result.Life?.HitPointDamage}";
        if (result.Disposition == GameSessionDisposition.Rejected) return result;
        _notices.Add(new(AreaAiNoticeKind.Hit, shooter, struck) { Result = result });
        if (result.Resolution?.Refusal == AttackRefusal.None)
        {
            // ARC-AI-1516 (EX14, VBG5): a missile that met an object other than its target raises the event with the accidental bit.
            if (struck != aimed) AttackEvent(shooter, struck, AttackEventFlags.Accidental);
            // ARC-DEV-292: the strike is the damage step (or the miss path) of the shot: the struck NPC's immediate pass, after its attack event.
            // ARC-RENDER-1990: the weapon asked is the shooter's wielded one as it stands at the strike (the original reads the projectile's field 0x50 set at the shot; an NPC's thrown item is not told apart here, issue 2013).
            HitEffect(struck, result, strikeWeapon);
            if (VictimReached(result)) Struck(struck);
        }
        return result;
    }

    /// <summary>
    /// ARC-COMBAT-1140 (RA4, RH5, PF8; tail T4e): on the ranged path the line of fire to the target may be stopped by a door, window, container, scenery, trap or wall, which replaces the target whatever its type (RA4); a target that is
    /// no critter is hit without the skill roll (RH5). The NPC's attack is then <see cref="GameSessionOwner.AttackObjectByNpc"/> on that blocker, from the shooter's tile at the blocker's distance; the arrow flies toward the aimed
    /// target's tile as the hero's does and strikes the blocker where it crosses its tile (<see cref="AreaAiScene.LaunchShotAtObject"/>, <see cref="LandShotOnObject"/>), or the attack lands at once
    /// (<see cref="AreaAiScene.ObjectStruck"/>). True when a blocker took the attack; false leaves the shot to <see cref="LineOfFireStruck"/> (no scene hook, a critter or hero, an item or a row the host cannot place).
    /// </summary>
    private bool HitBlockingObject(string id, string target)
    {
        if (_scene.ObjectTarget is not { } objectOf || Distance(id, target) <= 1 || Tile(id) is not { } from || Tile(target) is not { } to || _scene.LineOfFire?.Invoke() is not { } map) return false;
        var trace = LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat);
        if (trace.Blocker is not { } blocker || blocker.Type is 15 or 16 || blocker.Key == Key(target) || blocker.WorldX is not { } bx || blocker.WorldY is not { } by) return false;
        AreaObjectAttackTarget? info = null;
        // A wall (type 0) is struck without a target; another blocker must be an object the session damages (an item or a projectile keeps the target, whose check the blocked line fails).
        if (blocker.Type != 0 && ((info = objectOf(blocker)) is not { Refusal: null, Object: not null } || info.Kind == DamageableObjectKind.Item)) return false;
        var cell = new CellCoord(checked((int)bx), checked((int)by));
        var result = StrikeObject(id, blocker, info, from, cell, to, $"shot on {(info?.Object?.Entity ?? "a wall")} instead of {target}");
        // A refused attack leaves the shot to the ordinary flow (the target keeps it).
        return result is { Disposition: not GameSessionDisposition.Rejected };
    }

    /// <summary>
    /// The NPC's attack on the object <paramref name="blocker"/> (row) with the session target <paramref name="info"/> (none: a wall) at <paramref name="cell"/>, from the NPC's tile <paramref name="from"/>; the arrows of a ranged attack
    /// fly toward <paramref name="to"/>. Shared by the line-of-fire blocker (<see cref="HitBlockingObject"/>) and the object focus (<see cref="HitObjectFocus"/>). Null when the NPC has no combat values; else the session's result, whose
    /// follow-up (the viewer's floaters, bust script, redraw; the arrows' flight or strike) has been started unless the attack was rejected.
    /// </summary>
    private SessionObjectAttackResult? StrikeObject(string id, AreaObjectRecord blocker, AreaObjectAttackTarget? info, CellCoord from, CellCoord cell, CellCoord to, string what)
    {
        var struck = info?.Object;
        if (Build(id) is not { } attacker) return null;
        var weapon = WieldedPrototype(id);
        // ARC-COMBAT-2538 (RH5): the victim a script names: the object's entity, or a wall's row (a wall's scripts are offered like any struck object's).
        var victim = struck?.Entity ?? (info is null ? AreaUseScriptHost.EntityId(blocker) : null);
        var result = _session.AttackObjectByNpc(_session.Version, new SessionNpcObjectAttack(attacker)
        {
            Target = struck, Wall = struck is null, Distance = AiExecutorRules.Distance(from, cell), AttackerWorn = _scene.Combat.WornItems(_session, id), AttackerCell = from, DropMap = _scene.DropMap?.Invoke(),
            Flight = _scene.LaunchShotAtObject is not null, TakingDamage = TakingDamageOfObject(id, victim), HitScripts = HitScriptsOf(id, victim),
        });
        Trace(id).Hit = $"{what}: {result.Disposition} {result.Failure} {result.Resolution?.Outcome} {result.Object?.Damage}";
        NeedsAmmunition(id, result.Resolution);
        if (result.Disposition == GameSessionDisposition.Rejected) return result;
        // ARC-RENDER-2011 (0x4B3170): the swing effect of an attack on an object or a wall too, unless the attack was refused (out of ammunition).
        if (result.Resolution?.Refusal == AttackRefusal.None) WeaponAttackEffect(id, WeaponHitEffectRules.Swing, weapon, to);
        if ((result.Shots.Count == 0 || result.Life is not null) && result.Resolution?.Refusal == AttackRefusal.None) _scene.ObjectStruck?.Invoke(id, blocker, info, result);
        // The arrows leave the shooter's tile for the aimed target's; one the viewer cannot fly strikes the blocker at once.
        foreach (var shot in result.Shots)
            if (_scene.LaunchShotAtObject?.Invoke(id, blocker, shot, from, to) != true)
            {
                if (LandShotOnObject(id, shot, struck, struck is null, blocker) is { } landed && landed.Disposition != GameSessionDisposition.Rejected) _scene.ObjectStruck?.Invoke(id, blocker, info, landed);
                _session.EndShot(shot);
            }
        return result;
    }

    /// <summary>
    /// ARC-AI-1756 (VB2-2 gap d; 0x4B3170, SC16): the attack record's first step. A target of type 3 (scenery) whose occupant field 0x46 is set is replaced by that occupant, in both target slots, and the occupant leaves
    /// the object through the unseat 0x45F3A0 (the sleeper's flags, its latch and the bed's occupied frame, <see cref="Unseat"/>); the attack is then the attack on the occupant, its check carrying the invocation modifier -30
    /// (<see cref="AttackRequest.TargetWasInBed"/>) and its event naming the occupant. Only a bed holds an occupant. Returns the occupant, or null for any other target. ARC-AI-1939: a bed the hero rests in gives the hero
    /// (the occupancy's seat is taken out, the rest window stays open as in the original).
    /// </summary>
    private string? OccupantInPlaceOf(string attacker, string target)
    {
        if (target == Pc || _scene.Beds is not { } beds || _scene.ObjectFacts?.Invoke(target) is not { Type: 3 }
            || _scene.ObjectAttackTarget?.Invoke(target) is not { } info) return null;
        // The host's first bed question syncs the shared occupancy with the NPCs' saved sleeping state (a load), so the occupant is read after it.
        ReconcileBeds(beds);
        if (beds.Occupancy.Occupant(info.Row.Key) is not { } occupant) return null;
        // ARC-AI-1939 (0x45F3A0 takes a PC out too; only its NPC flag-0x200 write is NPC-only): the hero's seat is the occupancy's own (the rest window put it there); nothing else of the hero is written here (the window gives
        // him neither the sleeping flag nor the don't-draw flag, sleep-window-runtime.md). The window stays open: nothing in it or its step reads the seat or a blow (SWB13), and its close's own unseat of the empty bed only
        // logs an error in the original and refuses here (SWB12).
        if (occupant == Pc)
        {
            if (attacker == Pc || !beds.Occupancy.Unseat(info.Row.Key, Pc)) return null;
            var heroTrace = Trace(attacker);
            heroTrace.Item = (heroTrace.Item is { Length: > 0 } before ? before + "; " : "") + $"attack on bed {target}: the hero pulled out of the bed, the rest window stays open";
            return Pc;
        }
        if (!TryNpc(occupant, out _)) return null;
        var text = Unseat(occupant, beds, info.Row.Key);
        if (text == "unseat refused") return null;
        // The hero is no AI host entity: his blow leaves no NPC trace.
        if (attacker != Pc)
        {
            var trace = Trace(attacker);
            trace.Item = (trace.Item is { Length: > 0 } earlier ? earlier + "; " : "") + $"attack on bed {target}: {occupant} pulled out, {text}";
        }
        return occupant;
    }

    /// <summary>
    /// ARC-COMBAT-1942 (0x4B3170 is the hero's attack record too, SC16): the hero's blow on the scenery <paramref name="bedEntity"/> takes the NPC lying in it out (the unseat 0x45F3A0, as an NPC's blow does,
    /// <see cref="OccupantInPlaceOf"/>) and returns that occupant, whom the blow then strikes with the check's modifier -30 (<see cref="AttackRequest.TargetWasInBed"/>); null for any other target, an empty bed or a
    /// bed the hero lies in himself (his own blow does not take him out of the bed he rests in).
    /// </summary>
    public string? HeroBlowOccupant(string bedEntity) => OccupantInPlaceOf(Pc, bedEntity);

    /// <summary>
    /// ARC-AI-1636 (VB2-2 gap d; the attack goal's hit step 0x4B3170 on a target that is no critter, exe read in the ledger): an NPC's attack on the object it holds as its COMBAT_FOCUS (a trap's shooter object a trap shot put there,
    /// <see cref="AttackEvent"/>'s <c>VictimFightsObject</c>). The session damages the object as it does a blocker (<see cref="GameSessionOwner.AttackObjectByNpc"/>, no skill roll: an object has no dodge, RH5) and the attack event of the
    /// record then goes out for the object as the victim (it tells the NPC's followers and stops). An object the session cannot damage (no target hook, a refusal) is swung at with no effect, as the trace says.
    /// </summary>
    private void HitObjectFocus(string id, string target)
    {
        if (Tile(id) is not { } from || Tile(target) is not { } cell) return;
        if (_scene.ObjectAttackTarget?.Invoke(target) is not { Refusal: null, Object: not null } info || info.Kind == DamageableObjectKind.Item)
        {
            Trace(id).Hit = $"attack on object {target}: nothing the session damages";
            return;
        }
        var result = StrikeObject(id, info.Row, info, from, cell, cell, $"attack on object {target}");
        if (result is { Disposition: not GameSessionDisposition.Rejected } && result.Resolution?.Refusal == AttackRefusal.None) AttackEvent(id, target);
    }

    private Func<bool>? TakingDamageOfObject(string attacker, string? victim) =>
        _scene.TakingDamage is { } asks && victim is not null ? () => asks(victim, attacker) : null;

    /// <summary>
    /// ARC-COMBAT-1140 (PF8, RH5, OD1–OD5; tail T4e): an arrow or bullet of <paramref name="shooter"/> reached an object it met: the one the shot was decided for (<see cref="HitBlockingObject"/>) takes the carried hit, another
    /// object or wall past its destination a forced hit (<see cref="GameSessionOwner.LandNpcShotOnObject"/>); the object's taking-damage script is asked with the shooter as the attacker. A door, window, container,
    /// scenery or trap is <paramref name="target"/>, a wall is none (<paramref name="wall"/>), named by its row <paramref name="wallRow"/> for the scripts it is offered (ARC-COMBAT-2538: the weapon's hit scripts and the wall's
    /// taking-damage script, as for any struck object). What follows (the floaters, the trap and BUST script, the sounds, the redraw) is the viewer's, from the returned result.
    /// Null when the shooter has no combat values (the shot is gone either way).
    /// </summary>
    public SessionObjectAttackResult? LandShotOnObject(string shooter, SessionShot shot, SessionObjectTarget? target, bool wall, AreaObjectRecord? wallRow = null)
    {
        // ARC-COMBAT-2538 (RH5): a wall's row names it for the hit scripts and its taking-damage script (a wall without a row offers none).
        var victim = target?.Entity ?? (wall && wallRow is not null ? AreaUseScriptHost.EntityId(wallRow) : null);
        if (Build(shooter) is not { } attacker) { EndStruckShot(shot); return null; }
        var result = _session.LandNpcShotOnObject(_session.Version, shot, new SessionNpcObjectAttack(attacker)
        {
            Target = target, Wall = wall, AttackerWorn = _scene.Combat.WornItems(_session, shooter), AttackerCell = Tile(shooter), DropMap = _scene.DropMap?.Invoke(),
            TakingDamage = TakingDamageOfObject(shooter, victim), HitScripts = HitScriptsOf(shooter, victim),
        });
        EndStruckShot(shot);
        Trace(shooter).Hit = $"shot on {target?.Entity ?? "a wall"} {result.Disposition} {result.Failure} {result.Resolution?.Outcome} {result.Object?.Damage}";
        return result;
    }

    // The end of a shot that struck (or could not): ARC-COMBAT-1717: a thrown weapon's shot stays for the item's landing (<see cref="GameSessionOwner.LandThrownItem"/>, the viewer's, which ends the shot).
    private void EndStruckShot(SessionShot shot)
    {
        if (_session.QueryThrownShot(shot) is null) _session.EndShot(shot);
    }

    // The NPC's attack facts on the hero or another NPC (null: no combat values); ARC-DEV-282: <paramref name="flight"/> lets a shot on the ranged path fly.
    private SessionNpcAttack? NpcAttackOf(string id, string target, bool flight, bool inBed = false)
    {
        if (Build(id) is not { } attacker) return null;
        var targetBuild = target == Pc ? null : Build(target);
        if (target != Pc && targetBuild is null) return null;
        return new SessionNpcAttack(attacker)
        {
            Target = targetBuild,
            Distance = Tile(id) is { } a && Tile(target) is { } b ? AiExecutorRules.Distance(a, b) : 1,
            AttackerWorn = _scene.Combat.WornItems(_session, id),
            TargetWorn = target == Pc ? _scene.HeroWorn?.Invoke() : _scene.Combat.WornItems(_session, target),
            AttackerCell = Tile(id),
            TargetCell = target == Pc ? null : Tile(target),
            DropMap = _scene.DropMap?.Invoke(),
            TakingDamage = TakingDamageOf(id, target),
            HitScripts = HitScriptsOf(id, target),
            // ARC-MAGIC-1188: the damage step's item events (the NPC's worn items fire as any attacker's, 0x4B4390) through the cast host's trigger host.
            ItemTriggers = _scene.Spells?.Invoke()?.ItemTriggers,
            // ARC-DEV-273 (RA4): a bow or firearms attack reads the line to the target (a melee one ignores it).
            LineOfFire = Distance(id, target) > 1 && Tile(id) is { } from && Tile(target) is { } to && _scene.LineOfFire?.Invoke() is { } map ? LineTo(from, to, Key(target), map) : null,
            Flight = flight,
            // ARC-COMBAT-1942 (0x4B2210): the backstab bit is built for the clicked target (a bed is no PC or NPC) before 0x4B3170 retargets, so an occupant pulled out of a bed is no backstab.
            Backstab = inBed ? null : BackstabFacts(id, target),
            TargetWasInBed = inBed,
        };
    }

    /// <summary>ARC-COMBAT-1929: <see cref="BackstabFacts"/> of the hero's melee strike on the NPC <paramref name="target"/>.</summary>
    public SessionBackstabFacts? HeroBackstabFacts(string target) => BackstabFacts(Pc, target);

    /// <summary>
    /// ARC-COMBAT-1929 (0x45F9D0 from the context builder 0x4B2210): what decides whether <paramref name="attacker"/>'s melee strike on
    /// <paramref name="target"/> is a backstab, as the session asks it (<see cref="SessionAttack.Backstab"/>): whether the target faces the
    /// attacker (0x45EFF0: its facing, an NPC's mover's, the hero's the scene's) and whether it sees (0x4AF260) or hears (0x4AF470, silent) it,
    /// both read when the session asks. Null for a pair this host holds no tile of, and while the scene has no line-of-fire map.
    /// </summary>
    public SessionBackstabFacts? BackstabFacts(string attacker, string target)
    {
        // Without a line-of-fire map sight and hearing answer 'not perceived', which would over-grant true backstabs: no facts, no backstab.
        if (attacker == target || Tile(attacker) is not { } from || Tile(target) is not { } to || _scene.LineOfFire?.Invoke() is null) return null;
        return new(() => AwarenessRules.Faces(to, ObserverRotation(target, null), from),
            () => See(target, attacker).Perceived || Hear(target, attacker, Loudness.Silent).Perceived);
    }

    /// <summary>
    /// ARC-AI-1513 (ledger VB2-7): set no-flee (0x4AF1D0) as the move-away goal's nodes ask it: the session first stops the fleeing (FLEE to NONE through the setter) and sets NO_FLEE in one revision; an NPC that was
    /// in FLEE has its flee runs ended (the interrupt of 0x4AF170, which the original runs in the FLEE state only). Not run for the hero or an NPC the session does not know.
    /// </summary>
    public void SetNoFlee(string id)
    {
        if (id == Pc) return;
        var fleeing = _session.QueryDanger(id).State == DangerState.Flee;
        var result = _session.SetNoFlee(_session.Version, id, SetterFacts(id));
        if (fleeing && result.Failure == GameSessionFailure.None) InterruptFleeRuns(id);
    }

    /// <summary>ARC-AI-1513: the flee-goal interrupt of stop fleeing (0x44E4D0, goal 25) for the start of mind control, which has set no-flee through the session: every run of the NPC whose bottom or top goal is flee ends. False: the NPC is not run here.</summary>
    public bool SpellEndFleeGoals(string npc)
    {
        if (npc == Pc || !TryNpc(npc, out _)) return false;
        InterruptFleeRuns(npc);
        return true;
    }

    // The flee runs end at level 6. While the spell system holds the table (the start of mind control runs inside a spell run) the session refuses the interrupt: the NPC waits and its runs end when the table frees,
    // at the next step of this host (DrainFleeInterrupts).
    private readonly HashSet<string> _fleeInterruptsPending = [];

    private void InterruptFleeRuns(string npc)
    {
        _fleeInterruptsPending.Remove(npc);
        foreach (var run in RunsOf(npc).Where(r => !AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal) && (r.Bottom.Goal == GoalType.Flee || r.Frames[^1].Goal == GoalType.Flee)).ToArray())
        {
            var change = _session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel);
            if (change.Failure == SessionGoalFailure.PassInProgress) _fleeInterruptsPending.Add(npc);
            else EndNodes(change.Steps);
        }
    }

    private void DrainFleeInterrupts()
    {
        if (_fleeInterruptsPending.Count == 0) return;
        foreach (var npc in _fleeInterruptsPending.ToArray()) InterruptFleeRuns(npc);
    }

    // ARC-MAGIC-035: the interrupt of the helpers 0x44E050 and 0x44E4D0 ends only the runs that test true, not every run of the NPC (an eye candy or a burning run beside it is left): each at the level, with its end nodes.
    private bool InterruptRunsWhere(string npc, int level, Func<GoalRun, bool> test)
    {
        var ended = false;
        foreach (var run in RunsOf(npc).Where(r => !AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal) && test(r)).ToArray())
        {
            var change = _session.InterruptGoal(run.Handle, level);
            EndNodes(change.Steps);
            ended |= change.Succeeded;
        }
        return ended;
    }

    // ---- the social flags of a spell (ARC-MAGIC-035, MA-4: the steps 0x454A10 calls; ledger SO1 to SO8) ----

    /// <summary>
    /// ARC-MAGIC-035: the reaction change of an NPC this host runs toward the hero (0x4C0DE0: <see cref="GameSessionOwner.AdjustReaction"/> with the NPC's and the hero's facts). False: the NPC is not run
    /// here, no facts are known, or the session refused (the cast host logs it).
    /// </summary>
    public bool SpellReaction(string npc, int delta)
    {
        if (npc == Pc || !TryNpc(npc, out _) || ReactionFacts(npc) is not { } facts) return false;
        var result = _session.AdjustReaction(_session.Version, npc, facts.Npc, facts.Pc, delta);
        Trace(npc).Setter = $"spell reaction {delta} {result.Disposition} {result.Failure}";
        return result.Failure == GameSessionFailure.None;
    }

    /// <summary>What one guard witness did (<see cref="AreaAiHost.GuardWitnesses"/>): who, whether its reaction was lowered and whether the attack event ran on it.</summary>
    public sealed record GuardWitnessReport(string Witness, bool ReactionLowered, bool Attacked, bool Shouted);

    /// <summary>
    /// ARC-AI-2084 (ledger GW1-GW8, the guard witness rule): the NPCs near an object that guard it or are guards see or hear an act of the hero (or of an NPC a player character leads) on it. The callers are the damage step of an attack
    /// on a portal, container or scenery (loud; <see cref="GuardWitnessMode.Done"/> when it broke, else <see cref="GuardWitnessMode.Attempt"/>), the pick-locks skill (silent; done when it opened the lock, attempt when it failed; only for a locked
    /// object) and a spell that clears a lock flag (loud, done). <paramref name="guarded"/> is the object's entity id (the script's extra object), <paramref name="objectType"/> its type (1 portal, 2 container, 3 scenery, any other returns), <paramref name="notifyNpc"/>
    /// the portal's or container's notify NPC (0 returns) and <paramref name="at"/> its tile. Each NPC in the square of half-size 10 around it, in scan order, qualifies when its name number equals the notify NPC (-1 for scenery) or it is a guard,
    /// and it is neither dead nor mind-controlled; it must see the actor, else hear it at <paramref name="loudness"/>; its catching-thief script (<see cref="AreaAiScene.GuardCatchingThief"/>) must run the default. The effects are
    /// <see cref="GuardWitnessRules.Plan"/>'s: the attack event of the actor on the witness, or (an awake witness of an attempt) its reaction -20 toward the hero, the attack event when its effective reaction is then at most AI parameter 10, and
    /// its line 4000-4099 (the active test, a callback) with the <b>actor</b> as listener. No hero-only gate. NOT READY: the viewer picks the line's gender pair from the hero's, not the actor's; the reaction's clamp is the session's.
    /// </summary>
    /// <remarks>The rule is 0x4AEE50 (<see cref="GuardWitnessRules"/> holds its marked, map-free part).</remarks>
    public IReadOnlyList<GuardWitnessReport> GuardWitnesses(string actor, string guarded, int objectType, int notifyNpc, CellCoord at, GuardWitnessMode mode, Loudness loudness)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(guarded);
        if (GuardWitnessRules.Key(objectType, notifyNpc) is not { } key || actor != Pc && PcLeader(actor) is null) return [];
        var reports = new List<GuardWitnessReport>();
        foreach (var witness in SquareAt(at, GuardWitnessRules.Radius).Distinct())
        {
            if (witness == Pc || !TryNpc(witness, out var npc) || Find(witness) is not { } facts
                || !GuardWitnessRules.Qualifies(key, AreaUseScriptHost.NameOf(_session, npc.Row), facts.Guard, facts.Dead, facts.MindControlled)) continue;
            if (!(See(witness, actor).Perceived || Hear(witness, actor, loudness).Perceived)) continue;
            if (!(_scene.GuardCatchingThief?.Invoke(witness, actor, guarded) ?? true)) continue;
            var plan = GuardWitnessRules.Plan(mode, facts.Asleep);
            var lowered = plan.ReactionDelta != 0 && SpellReaction(witness, plan.ReactionDelta);
            var attacks = plan.AttackEvent
                || plan.ChecksReaction && StandardReaction(witness) is { } reaction && Row(witness) is { } row && reaction <= row.HostileReaction;
            if (attacks) AttackEvent(actor, witness, loudness: loudness);
            if (plan.Shouts) Bark(witness, GuardWitnessRules.ShoutLines, actor);
            Trace(witness).Setter = $"guard witness of {guarded} mode={mode} lowered={lowered} attacked={attacks}";
            reports.Add(new(witness, lowered, attacks, plan.Shouts));
        }
        return reports;
    }

    /// <summary>
    /// ARC-SCRIPT-1163 (E5-a, ledger NS2): a script's set reaction (30, <paramref name="set"/>: the delta from the NPC's standard reaction to <paramref name="value"/>) or adjust reaction (31, the delta <paramref name="value"/>) of an NPC this host runs toward the hero, through the
    /// reaction change (0x4C0DE0, <see cref="GameSessionOwner.AdjustReaction"/> / <see cref="GameSessionOwner.SetReaction"/>) with the NPC's and the hero's facts. Answers what happened for the viewer's trace.
    /// </summary>
    public string ScriptReaction(string npc, int value, bool set)
    {
        if (npc == Pc || !TryNpc(npc, out _)) return "not an NPC of the shown map";
        if (ReactionFacts(npc) is not { } facts) return "no reaction facts";
        var result = set ? _session.SetReaction(_session.Version, npc, facts.Npc, facts.Pc, value) : _session.AdjustReaction(_session.Version, npc, facts.Npc, facts.Pc, value);
        var what = $"{(set ? "set" : "adjust")} reaction {value} {result.Disposition} {result.Failure}";
        Trace(npc).Setter = what;
        return what;
    }

    /// <summary>
    /// ARC-MAGIC-035: peace (0x4AA300) of an NPC with the side of <paramref name="with"/> (an animal's charm): the session's peace (focus and last attacker cleared, the danger state NONE, the followers' lists), then the host's
    /// part of it. ARC-AI-1522 (ledger VB2-3): the head is the leader of <paramref name="with"/> (the hero for a follower of the hero), else <paramref name="with"/> itself; the NPC's attack run (goal 18 or 19) whose target is the head
    /// or anyone led by it is interrupted at level 5 (0x44E050), and for a head with followers (the hero's party) each follower's attack run at the NPC itself is interrupted at level 5 too (0x44E0E0, after the follower forgot the NPC).
    /// False: the NPC is not run here or the session refused.
    /// </summary>
    public bool SpellPeace(string npc, string with)
    {
        if (npc == Pc || !TryNpc(npc, out _) || Combatant(with) is not { } other) return false;
        var result = _session.MakePeace(_session.Version, new SessionPeaceRequest { Entity = npc, Other = other, Facts = OrderFacts(npc) });
        Trace(npc).Setter = $"spell peace {with} {result.Disposition} {result.Failure}";
        if (result.Failure != GameSessionFailure.None) return false;
        // 0x4AA300 and its interrupt 0x44E050 both read the chain's player character (0x45DDA0).
        var head = PcLeader(with) ?? with;
        InterruptRunsWhere(npc, 5, r => r.Bottom is { Goal: GoalType.Attack or GoalType.AttemptAttack, Target: { } at } && (at == head || PcLeader(at) == head));
        if (result.Steps.Count > 0)
            foreach (var follower in result.Steps[0].Followers)
                InterruptRunsWhere(follower, 5, r => r.Bottom is { Goal: GoalType.Attack or GoalType.AttemptAttack, Target: { } at } && at == npc);
        return true;
    }

    /// <summary>ARC-MAGIC-035: flee (0x4AF130): the danger setter asked for FLEE from <paramref name="from"/>, with the NPC's own facts. False: the NPC is not run here or the setter refused.</summary>
    public bool SpellFlee(string npc, string from)
    {
        if (npc == Pc || !TryNpc(npc, out _)) return false;
        var result = SetDanger(npc, DangerState.Flee, from);
        return result.Failure == GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-MAGIC-035: stop fleeing (0x4AF170): an NPC in FLEE goes to NONE through the setter, then every run of the NPC whose bottom or top goal is flee (25) is ended at level 6
    /// (the helper 0x44E4D0; an eye candy or a burning run is left). Both steps are the FLEE state's only (<see cref="MoraleRules.StopFleeing"/>, issue #1595): an NPC in another state keeps its flee runs.
    /// False: the NPC is not run here or the session refused.
    /// </summary>
    public bool SpellStopFleeing(string npc)
    {
        if (npc == Pc || !TryNpc(npc, out _)) return false;
        // The state is read before the setter: 0x4AF170 does everything inside its FLEE branch.
        var stop = MoraleRules.StopFleeing(new HostilityCritter { Id = npc, Kind = HostilityKind.Npc }, _session.QueryDanger(npc).State);
        var result = _session.StopFleeing(_session.Version, npc, SetterFacts(npc));
        // The flee goals end whatever the setter did (0x4AF170 calls 0x44E4D0 after it), so a refused setter does not keep them: only the runs whose bottom or top goal is 25 end, at level 6.
        if (stop.InterruptsFleeGoals) InterruptFleeRuns(npc);
        return result.Failure == GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-AI-1134 (ledger DY3, DY7): the kill notification (0x4AA1B0) of an NPC whose death the session processed (<see cref="GameSessionOwner.KillNotice"/>, after the combat focus := killer):
    /// <see cref="MoraleRules.KillNotification"/> over this host's world (the victim stops fleeing; for a PC killer, the live NPCs of the victim's faction in the 7 × 7 square centred on the killer whose combat focus is the killer start backing off) applied in one
    /// revision, then, when the victim was in FLEE, every run of the victim whose bottom or top goal is flee ends at level 6 (the stop-fleeing helper's interrupt, as <see cref="SpellStopFleeing"/>). False: the victim is not run here, or the world
    /// no longer knows it, or the session refused the plan.
    /// </summary>
    public bool NoticeKill(string victim, string? killer)
    {
        if (victim == Pc || !TryNpc(victim, out _)) return false;
        ReleaseBed(victim);
        if (Find(victim) is null) return false;
        var plan = MoraleRules.KillNotification(this, victim, killer);
        var result = _session.ApplyKillNotification(_session.Version, new SessionKillNotificationRequest
        {
            Victim = SessionCombatant.Critter(victim), Plan = plan, VictimFacts = SetterFacts(victim),
        });
        // Only a victim that was in FLEE loses its flee runs (the plan read the state before the session applied it).
        if (plan.VictimStopsFleeing.InterruptsFleeGoals) InterruptFleeRuns(victim);
        return result.Failure == GameSessionFailure.None;
    }

    // ---- the casting goals (ARC-WORLD-102) ----

    /// <summary>The casting goal's start (0x429B50 → 0x429BC0 → 0x42C850) through the cast host (<see cref="SessionSpellCaster.StartGoal"/>):
    /// the 4 action points of a round (their events are the round's), the pre-animation check, the target, then the facing turn, which
    /// <see cref="FaceCast"/> applies.</summary>
    public AiCastGoalStart StartCast(string id, int run)
    {
        _queuedCasts.Remove(run);
        _launched.Remove(run);
        _playedCasts.Remove(run);
        // A queued goal whose run was cancelled before it (QC6) starts without one and ends (QC4).
        if (_scene.Spells?.Invoke() is not { } caster || !OwnRun(id, run)) return AiCastGoalStart.Refused;
        var start = caster.StartGoal(run, _round);
        if (start.ActionPoints is { } points) RoundCall(points);
        Trace(id).Cast = $"start run={run} {start.Outcome}";
        if (start.Outcome is SpellCastGoalOutcome.Started or SpellCastGoalOutcome.CannotTurn) _castFacing[run] = start.Facing;
        return start.Outcome switch
        {
            SpellCastGoalOutcome.Started or SpellCastGoalOutcome.CannotTurn => AiCastGoalStart.Started,
            SpellCastGoalOutcome.TargetGone => AiCastGoalStart.TargetGone,
            _ => AiCastGoalStart.Refused,
        };
    }

    /// <summary>The facing turn (0x42C850, <see cref="SpellFacingRules.Turn"/>) the start computed: a stoned, stunned or paralysed caster
    /// cannot turn; a turn faces the NPC toward its run's target object (its body's facing; NPC ART is not read).</summary>
    public bool FaceCast(string id, int run)
    {
        _castFacing.Remove(run, out var facing);
        if (facing?.Kind == SpellFacingKind.Refused) return false;
        if (facing?.Kind == SpellFacingKind.Turned && _session.QuerySpellRuns()[run]?.Target?.Id is { } target) Face(id, target);
        return true;
    }

    /// <summary>
    /// The play's start (0x429F00, AG4): the target re-check (0x456A90, <see cref="SessionSpellCaster.TargetStillValid"/>), then the
    /// caster's cast eye candy (6 · spell, <see cref="AreaAiScene.CastEyeCandy"/>), whose release frame and last frame time the action
    /// frame and the rest (<see cref="CastEyeCandyAnimation"/>, played through with a passing release). A failed re-check, goal 83's
    /// casting animation (NPC ART is not read) or no eye candy: no play, so the release runs at once. A play whose action frame is never
    /// reached (EC7) answers its whole length, and its action frame then releases nothing.
    /// </summary>
    [OriginalFunction(0x429F00, OriginalFunctionScope.Partial)]
    public AiCastSwing BeginCast(string id, int run, bool castAnimation)
    {
        if (_scene.Spells?.Invoke() is not { } caster || _session.QuerySpellRuns()[run] is not { } r) return new(false);
        if (!caster.TargetStillValid(r) || castAnimation || _scene.CastEyeCandy?.Invoke(id, r.Spell) is not { Immediate: false } play)
        {
            _notices.Add(new(AreaAiNoticeKind.Cast, id, r.Target?.Id) { Spell = r.Spell, Run = run, CastSwing = new(false) });
            return new(false);
        }
        // ARC-WORLD-105: a release that launches a missile restarts the goal one pace later (0x4246E0, SM1), as the hero's does.
        play.PaceAfterRelease = _scene.MissileArt?.Invoke(r.Spell) == true;
        long? release = null;
        var end = play.StartMs;
        foreach (var step in play.Advance(long.MaxValue, () => true))
        {
            if (step.Kind == EyeCandyStepKind.Release) release ??= step.AtMs;
            end = Math.Max(end, step.AtMs);
        }
        var swing = release is { } at
            ? new AiCastSwing(true, (int)Math.Clamp(at - play.StartMs, 1, int.MaxValue), (int)Math.Clamp(end - at, 0, int.MaxValue))
            : new AiCastSwing(true, (int)Math.Clamp(end - play.StartMs, 1, int.MaxValue));
        if (release is null) _noRelease.Add(run);
        _playedCasts.Add(run);
        _notices.Add(new(AreaAiNoticeKind.Cast, id, r.Target?.Id) { Spell = r.Spell, Run = run, CastSwing = swing });
        return swing;
    }

    /// <summary>The play's action frame (0x429C40): a play that never reaches it releases nothing; a spell whose projectile eye candy has
    /// art launches its missile (<see cref="LaunchCast"/>); any other is released (<see cref="ReleaseCast"/>).</summary>
    public bool CastActionFrame(string id, int run)
    {
        if (_noRelease.Remove(run)) return false;
        // ARC-DEV-289: a goal a load restored between its play's start and its action frame: the play is asked again.
        if (!_playedCasts.Remove(run) && !CastPlayReleases(id, run)) return false;
        return _session.QuerySpellRuns()[run] is { } r && _scene.MissileArt?.Invoke(r.Spell) == true ? LaunchCast(id, run, r) : ReleaseCast(id, run);
    }

    // ARC-DEV-289: whether the play BeginCast would start for the run reaches its release frame (EC7): a play that does not start releases at once, as BeginCast's.
    private bool CastPlayReleases(string id, int run)
    {
        if (_session.QuerySpellRuns()[run] is not { } r || _scene.CastEyeCandy?.Invoke(id, r.Spell) is not { Immediate: false } play) return true;
        play.PaceAfterRelease = _scene.MissileArt?.Invoke(r.Spell) == true;
        return play.Advance(long.MaxValue, () => true).Any(step => step.Kind == EyeCandyStepKind.Release);
    }

    /// <summary>ARC-WORLD-105: the launch (0x429C40, AC8, <see cref="SessionSpellCaster.LaunchMissile"/>): the release gate marks the
    /// frame handled, so the goal's end frees nothing, and no re-check or BEGIN runs now; the viewer flies the missile from the NPC's
    /// tile (<see cref="AreaAiNoticeKind.Missile"/>, goal 28), whose arrival begins the run and whose loss frees it. False when the run
    /// was released or begun before: the goal then ends.</summary>
    private bool LaunchCast(string id, int run, SpellRun r)
    {
        if (_scene.Spells?.Invoke() is not { } caster) return false;
        var launched = caster.LaunchMissile(run);
        Trace(id).Cast = $"missile run={run} {(launched ? "launched" : "not launched")}";
        if (!launched) return false;
        _launched[run] = caster;
        _notices.Add(new(AreaAiNoticeKind.Missile, id, r.Target?.Id) { Spell = r.Spell, Run = run, Caster = caster });
        return true;
    }

    /// <summary>The release (0x429C80, <see cref="SessionSpellCaster.Release"/>): the target re-check and the run's BEGIN; false when the
    /// re-check refused it (the run freed unpaid in both settings, MG41) or the frame was released before.</summary>
    public bool ReleaseCast(string id, int run)
    {
        _noRelease.Remove(run);
        if (_scene.Spells?.Invoke() is not { } caster) return false;
        var spell = _session.QuerySpellRuns()[run]?.Spell ?? -1;
        var begun = caster.Release(run);
        var released = begun is not null && begun.Disposition != GameSessionDisposition.Rejected;
        Trace(id).Cast = $"release run={run} {(begun is null ? "re-check failed" : $"{begun.Disposition} {begun.Failure}")}";
        _notices.Add(new(AreaAiNoticeKind.Release, id, null) { Spell = spell, Run = run, Released = released, SpellRun = begun });
        return released;
    }

    /// <summary>The casting goal's end (0x429CD0, <see cref="SessionSpellCaster.EndGoal"/>): before the release frame the run is freed
    /// with its BEGIN charged (MG39: refused at the start, Steamwoven frees it unpaid); after it nothing. ARC-WORLD-105: a launched
    /// run's goal ends at the host that launched it, which knows the handled frame (nothing is freed: the run is its missile's).</summary>
    public void EndCast(string id, int run, bool refused)
    {
        _noRelease.Remove(run);
        _playedCasts.Remove(run);
        _castFacing.Remove(run);
        var launcher = _launched.Remove(run, out var launching) ? launching : null;
        if ((launcher ?? _scene.Spells?.Invoke()) is not { } caster) return;
        var freed = OwnRun(id, run) ? caster.EndGoal(run, refused) : null;
        _notices.Add(new(AreaAiNoticeKind.CastEnd, id, null) { Run = run, SpellRun = freed });
    }
    // ---- the skill goals (ARC-WORLD-109) ----

    /// <summary>The turn to an action's target object or tile (0x42C0F0, 0x42C390): refused for an object the host does not walk (the
    /// hero, a companion the host does not run), a target without a cell, object flag 0x10000 or a stunned or paralysed critter
    /// (critter flags 0x60, or the session's stun).</summary>
    [OriginalFunction(0x42C0F0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x42C390, OriginalFunctionScope.Partial)]
    public bool FaceForAction(string id, string? target, CellCoord? tile)
    {
        if (!TryNpc(id, out var npc) || !CanAct(id)) return false;
        if ((target is null ? tile : Tile(target)) is not { } there) return false;
        FaceNpc(npc, there);
        return true;
    }

    /// <summary>
    /// An action animation's start (0x42BD40, 0x42CB10, 0x42D300): the skill animation (12) of goals 34 and 86 to its action frame and
    /// rest; goal 67's pick-locks loops, as many as the pick-locks starter (0x436220, AG12) counts from the art's frame rate (rate × 10 ÷
    /// max(frames − 2, 1) + 1, halved when the NPC's pick-locks training is apprentice or more), the attempt after the last;
    /// ARC-WORLD-113: goal 37's throw animation (14) to its action frame, the throw, and rest. The art is the fixed stand-in's (NPC ART
    /// is not read yet), played at its raw rate. An object flag 0x10000 or a stunned or paralysed critter cannot start.
    /// </summary>
    [OriginalFunction(0x42BD40, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x42CB10, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x42D300, OriginalFunctionScope.Partial)]
    public AiActionSwing BeginAction(string id, AiActionAnimation animation)
    {
        if (!Walks(id) || !CanAct(id)) return new(AiActionStart.CannotStart);
        var art = FixedSkillArt;
        var frameMs = 1000.0 / art.RawFramesPerSecond;
        if (animation == AiActionAnimation.PickLocks)
        {
            var loops = art.RawFramesPerSecond * 10 / Math.Max(art.Frames - 2, 1) + 1;
            if (NpcSkill(id, Skill.PickLocks)?.Training is { } training && training >= SkillTraining.Apprentice) loops /= 2;
            return new(AiActionStart.Started, (int)Math.Max(1, Math.Round(loops * art.Frames * frameMs)), 0);
        }
        var action = (int)Math.Max(1, Math.Round(art.ActionFrame * frameMs));
        return new(AiActionStart.Started, action, (int)Math.Max(1, Math.Round(art.Frames * frameMs)) - action);
    }

    /// <summary>
    /// The skill invocation at the attempt's action frame (0x42A2A0 → 0x4C7090, 0x4C7160; the 4 action points are already paid): the
    /// heal skill (10) is the session's <see cref="GameSessionOwner.HealByNpc"/> with the NPC's tool item (it must be a healing item the
    /// NPC holds; without one the heal is impossible and nothing changes); its answer is the check's success, which sets the run's flag
    /// 0x40000 (ledger AK2). The pick pocket (skill 5) is <see cref="PickPocketAttempt"/> (ARC-DEV-246). ARC-AI-1854: the plain-roll skills are <see cref="PlainSkillAttempt"/>. ARC-AI-2623: gambling is <see cref="GamblingAttempt"/>. ARC-AI-2622: repair is <see cref="RepairAttempt"/>. Pick locks has its own attempt goal (67, <see cref="AttemptPickLocks"/>), so goal 34 never carries it. NOT READY: repair, which does nothing and
    /// answers false.
    /// </summary>
    [OriginalFunction(0x42A2A0, OriginalFunctionScope.Partial)]
    public bool AttemptSkill(string id, string target, int skill, string? item)
    {
        if (skill == AiExecutorRules.PickpocketSkill) return PickPocketAttempt(id, target);
        if (skill == (int)Skill.Repair) return RepairAttempt(id, target);
        if (skill == (int)Skill.Gambling) return GamblingAttempt(id, target);
        if (skill != AiPassChoice.HealSkill)
            return PlainSkillAttempt(id, target, skill);
        var heal = HealByNpc(id, target, item, out var refusal);
        Trace(id).Skill = heal is null ? $"heal {target}: {refusal}"
            : $"heal {target} {heal.Disposition} {heal.Failure} success={heal.Check?.Success} hp={heal.Heal?.HitPoints} uses={heal.UsesLeft?.ToString() ?? "-"}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = skill, Heal = heal });
        return heal is { Disposition: GameSessionDisposition.Changed, Check.Success: true };
    }

    /// <summary>
    /// ARC-AI-1854 (0x4C7160, the cases with no world write): the skills that have no case of their own in the attempt (bow, melee, throwing, backstab, prowling, haggle, persuasion, firearms), dodge (its incapacity gate), spot trap
    /// (the master's second roll) and disarm traps (no trap flag on this path) are the session's plain roll (<see cref="GameSessionOwner.RollNpcSkill"/>); its success is the run's flag 0x40000. The incapacity of a dodge is
    /// <see cref="DodgeRefused"/> (ARC-AI-2625: unconscious 0x45D800, stoned, stunned or paralysed); the spot trap's target test 0x4BBDD0 is <see cref="AreaAiScene.TargetTrapClass"/>. The opposed skills read the target's stat (<see cref="OpposedStat"/>, ARC-AI-2652). NOT READY: the spot trap's null-target read (undefined in the exe), and the other skills' UI callbacks (the local PC's messages); a skill
    /// outside the 16 answers false.
    /// </summary>
    private bool PlainSkillAttempt(string id, string target, int skill)
    {
        string? refusal = null;
        SessionNpcSkillRoll? roll = null;
        int? opponent = null;
        if (skill is < 0 or > (int)Skill.DisarmTraps || !GameSessionOwner.IsPlainNpcSkill((Skill)skill)) refusal = "not ready";
        else if (!OpposedStat(target, (Skill)skill, out opponent, out refusal)) { }
        else if (Build(id) is not { } user || _scene.NpcSheet?.Invoke(id) is not { } sheet) refusal = "no sheet";
        else
        {
            var incapable = skill == (int)Skill.Dodge && DodgeRefused(id);
            var trap = skill == (int)Skill.SpotTrap ? _scene.TargetTrapClass?.Invoke(target) ?? TrapClass.None : TrapClass.None;
            roll = _session.RollNpcSkill(_session.Version, new SessionNpcSkillUser(user.Target, sheet.Loaded, sheet.Modifiers, _session.QueryCompanion(id).LedByPc), (Skill)skill, incapable, trap, opponent);
        }
        Trace(id).Skill = roll is null ? $"skill {skill} on {target}: {refusal}"
            : $"skill {skill} on {target} {roll.Disposition} {roll.Failure} success={roll.Success} check={roll.Check?.Success} critical={roll.Critical} second={roll.SecondRoll}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = skill, PlainRoll = roll });
        return roll is { Disposition: GameSessionDisposition.Changed, Success: true };
    }

    /// <summary>
    /// ARC-AI-2652 (0x4C6410): the value a plain attempt's effectiveness is opposed by. A skill the table 0x5B6F64 flags as opposed by perception (0x10: prowling) or intelligence (0x20: persuasion) reads the target's effective
    /// stat through the getter 0x4B0490 (5 or 4): the hero's through the session, a critter of the shown map from its sheet and effects (<see cref="Find"/>), 0 for a target that is no critter (the getter answers 0 for any
    /// object but a critter). No target (an empty id) is no opponent (an effectiveness of 100); the other plain skills have none. A target the host holds no fields of, or a critter it holds no facts of, is refused and the attempt does nothing.
    /// </summary>
    private bool OpposedStat(string target, Skill skill, out int? value, out string? refusal)
    {
        value = null;
        refusal = null;
        var kind = SkillCheckRules.Opponent(skill);
        if (kind is not (SkillOpponent.Perception or SkillOpponent.Intelligence) || string.IsNullOrEmpty(target)) return true;
        var perception = kind == SkillOpponent.Perception;
        if (target == Pc)
        {
            value = _session.QueryHeroScriptStat(perception ? (int)CharacterStat.Perception : (int)CharacterStat.Intelligence);
            if (value is null) refusal = "no hero";
        }
        else if (Fields(target) is not { } fields) refusal = "no target";
        else if (fields.Critter is null) value = 0;
        else if (Find(target) is { } critter) value = perception ? critter.Perception : critter.Intelligence;
        else refusal = "no target sheet";
        return refusal is null;
    }

    /// <summary>
    /// ARC-AI-2623 (0x4C7160 with skill 8, the roll 0x4C6560): an NPC's gambling attempt, the session's <see cref="GameSessionOwner.RollNpcGambling"/> with the stored roll of the skill module. The skill is opposed by the target's
    /// gambling level (0x4C6410 over 0x4C5EB0): the hero's, a map NPC's from its sheet, 0 for a target that is no critter. A target the host holds no fields of, or a critter with no sheet, is not read and the attempt does nothing. The answer is
    /// the check's success (run flag 0x40000); the handler has no case of its own for the skill. The other opposed skills (prowling, persuasion) read a stat of the target: <see cref="OpposedStat"/> (ARC-AI-2652).
    /// </summary>
    private bool GamblingAttempt(string id, string target)
    {
        string? refusal = null;
        SessionNpcSkillRoll? roll = null;
        int? opponent = null;
        if (target == Pc) { if (_session.QueryHeroSkills() is { } heroSkills) opponent = heroSkills[Skill.Gambling].Level; else refusal = "no hero"; }
        else if (Fields(target) is not { } fields) refusal = "no target";
        else if (fields.Critter is null) opponent = 0;
        else if (_scene.NpcSheet?.Invoke(target) is { } targetSheet) opponent = _session.QueryNpcSkills(targetSheet.Loaded, targetSheet.Modifiers)[Skill.Gambling].Level;
        else refusal = "no target sheet";
        if (refusal is null)
        {
            if (Build(id) is not { } user || _scene.NpcSheet?.Invoke(id) is not { } sheet) refusal = "no sheet";
            else roll = _session.RollNpcGambling(_session.Version, new SessionNpcSkillUser(user.Target, sheet.Loaded, sheet.Modifiers, _session.QueryCompanion(id).LedByPc), opponent);
        }
        Trace(id).Skill = roll is null ? $"gambling on {target}: {refusal}"
            : $"gambling on {target} {roll.Disposition} {roll.Failure} success={roll.Success} check={roll.Check?.Success} roll={roll.Check?.Check.Roll} next={roll.Check?.NextGamblingRoll}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = (int)Skill.Gambling, PlainRoll = roll });
        return roll is { Disposition: GameSessionDisposition.Changed, Success: true };
    }

    /// <summary>
    /// ARC-AI-2622 (0x4C7160, repair case 12): the NPC repairs the item the goal names with the session's <see cref="GameSessionOwner.RepairByNpc"/>, on the item's own fields: an item it holds or one lying loose on the shown map
    /// (<see cref="AreaCombat.HeldItemFields"/>, <see cref="AreaCombat.GroundItemFields"/>), in a junk pile or held by another NPC (ARC-AI-2663, <see cref="RepairBaseline"/>). The answer is the case's success, which sets the run's flag 0x40000.
    /// The original's repair has no target test, so a critter or a scenery target would be "healed"; here a target that is no item the host can read is refused (the answer is false, nothing changes). The roll modifier 0x4C8430 of the case is
    /// the shared check's (<see cref="Arcanum.Simulation.Skills.SkillCheckRules"/>: Repair's table 0x980, blinded +30, both arms +50 or one +20, nothing else; it only picks which critical test runs). NOT READY: the swap of a ground item's drawn
    /// art after a master's broken-art clear (the viewer draws no broken art of an item) and the repair UI callbacks of the local player. The items the PC holds (kit, taken, made) are repaired through <see cref="AreaAiScene.HeroItemCondition"/> (ARC-AI-2673).
    /// </summary>
    private bool RepairAttempt(string id, string target)
    {
        string? refusal = null;
        SessionNpcRepairResult? result = null;
        if (Build(id) is not { } user || _scene.NpcSheet?.Invoke(id) is not { } sheet) refusal = "no sheet";
        else if (RepairBaseline(id, target) is not { } baseline) refusal = "no item to repair";
        else result = _session.RepairByNpc(_session.Version, new SessionNpcSkillUser(user.Target, sheet.Loaded, sheet.Modifiers, _session.QueryCompanion(id).LedByPc), target, baseline);
        Trace(id).Skill = result is null ? $"repair {target}: {refusal}"
            : $"repair {target} {result.Disposition} {result.Failure} success={result.Success} check={result.Check?.Success} critical={result.Check?.Critical} status={result.Repair} amount={result.Amount}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = (int)Skill.Repair, Repair = result });
        return result is { Disposition: GameSessionDisposition.Changed, Success: true };
    }

    // The condition baseline of the item a repair names: its fields' hp_pts, hp_adj, hp_damage and destroyed bits, for an item the NPC holds, one lying loose on the shown map, one in a junk pile (the session's own or the map's data) or one another
    // NPC of any covered map holds (ARC-AI-2663; the original's repair case works on the target handle's own fields and asks nothing of its holder, 0x4C7160), or (ARC-AI-2673) one the PC holds, whose baseline the viewer supplies (a kit item's
    // lives in the hero load, not in the session); null for any other target, a destroyed item and a critter.
    private ItemCondition? RepairBaseline(string id, string target)
    {
        if (_session.IsItemDestroyed(target)) return null;
        var fields = _scene.Combat.HeldItemFields(_session, id, target);
        if (fields is null && GroundView() is { } view)
        {
            var lying = view.Ground.Select(ground => ground.Item).Concat(view.JunkPiles.SelectMany(pile => pile.Items)).FirstOrDefault(item => item.Entity == target);
            if (lying is not null) fields = _scene.Combat.GroundItemFields(_scene.RuntimeMap, lying);
        }
        if (fields is null && MapPileOf(target) is not null && MapPileHolder(target) is { } listed) fields = _scene.Combat.GroundItemFields(_scene.RuntimeMap, listed);
        fields ??= _scene.Combat.NpcItemFields(_session, target);
        return fields is null ? _scene.HeroItemCondition?.Invoke(target) : SessionItemConditions.FromCombatFields(fields);
    }

    // The item as the standing junk pile of the map's data that lists it now holds it (its list as the session shows it), or null.
    private SessionInventoryItem? MapPileHolder(string item) =>
        _scene.Combat.MapJunkPileHolding(_session, _scene.RuntimeMap, item) is { } pile ? _session.QueryHolderItems(pile.Entity)?.FirstOrDefault(listed => listed.Entity == item) : null;

    /// <summary>The coins' inventory art, in cells, when the host does not know the art (a 1 × 1 pile): the pick-pocket check's size term (SC11).</summary>
    private const int GoldArtCells = 1;

    /// <summary>
    /// ARC-DEV-246 (MIG13 S14l, GT204–GT209): an NPC's pick pocket of gold at the attempt's action frame (goal 86 node 4 → the shared check and the pick-pocket case of 0x4C7160; only a script's steal, action 115, starts it: the AI
    /// starts no pick pocket). The session's <see cref="GameSessionOwner.PickPocketByNpc"/> with the thief's sheet and the victim's measured facts (its effective perception, its state, whether it is unaware of the thief); the victim is the PC
    /// or an NPC of the map. A noticed failure runs the victim's catching-thief script with the thief as the triggerer (<see cref="AreaAiScene.CatchingThief"/>; any answer but run default stops there) and then the attack event of the thief on the
    /// victim (loudness 1). Answers the check's success (run flag 0x40000). NOT READY: the thief's own caught-thief script (point 33), the thief's alignment change and the victim's "item stolen" mark (the session keeps no NPC alignment or
    /// critter flags 2), the follower-of-the-thief stop (only the PC leads here), the gold pile's art size (1 × 1) and an insert check for a thief with no pile (accepted).
    /// </summary>
    private bool PickPocketAttempt(string id, string target)
    {
        string? refusal = null;
        SessionNpcPickPocket? steal = null;
        if (Build(id) is not { } thief || _scene.NpcSheet?.Invoke(id) is not { } sheet) refusal = "no sheet";
        else if (Find(target) is not { } victim) refusal = "no victim";
        else if (victim.Destroyed || victim.Off) refusal = "the victim is destroyed or off";
        else
        {
            var conditions = (victim.Unconscious ? SkillTargetConditions.Unconscious : 0) | (victim.Asleep ? SkillTargetConditions.Sleeping : 0) | (victim.Stunned ? SkillTargetConditions.Stunned : 0);
            var unaware = !victim.InCombatMode && See(target, id).Excess != 0 && Hear(target, id, Loudness.Normal).Excess != 0;
            steal = new SessionNpcPickPocket(thief.Target, sheet.Loaded, sheet.Modifiers, target == Pc ? SessionSpellCaster.Hero : target,
                new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0), _ => SessionInsertVerdict.Accepted)
            {
                Perception = Math.Clamp(victim.Perception, -GameSessionOwner.MaxPickPocketStat, GameSessionOwner.MaxPickPocketStat),
                Conditions = conditions, Unaware = unaware, ItemWidth = GoldArtCells, ItemHeight = GoldArtCells,
            };
        }
        if (steal is null)
        {
            Trace(id).Skill = $"pick pocket {target}: {refusal}";
            _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = AiExecutorRules.PickpocketSkill });
            return false;
        }
        var result = _session.PickPocketByNpc(_session.Version, steal);
        Trace(id).Skill = $"pick pocket {target} {result.Disposition} {result.Failure} success={result.Check?.Success} critical={result.Check?.Critical} moved={result.Moved} noticed={result.Noticed}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = AiExecutorRules.PickpocketSkill, PickPocket = result });
        // PP7: a noticed failure's victim script, then (unless it stopped them) the attack event of the thief on the victim.
        if (result.Noticed && (_scene.CatchingThief?.Invoke(target, id) ?? true)) AttackEvent(id, target);
        return result.Check?.Success == true && result.Disposition == GameSessionDisposition.Changed;
    }

    /// <summary>
    /// The pick-locks attempt (0x42A4E0, ledger PL1, PL3–PL5): the session's <see cref="GameSessionOwner.PickLockByNpc"/> with the NPC's sheet, the lock the scene names (<see cref="AreaAiScene.PickLockTarget"/>) and the lockpicks
    /// the goal's tool item is when the NPC holds them (anything else, or none: it picks without, as a follower, the AI and a script may). The attempt charges no action points (the goal's own was paid) and has no fate. Witnesses (PL6)
    /// and the hand-over line are the viewer's: the notice carries the answer. NOT READY: the NPC's lock pick without a scene that names the lock does nothing, and the NPC's wear of its lockpicks is none (PL4).
    /// </summary>
    [OriginalFunction(0x42A4E0, OriginalFunctionScope.Partial)]
    public void AttemptPickLocks(string id, string target, string? item)
    {
        string? refusal = null;
        SessionPickLockResult? result = null;
        if (Build(id) is not { } picker || _scene.NpcSheet?.Invoke(id) is not { } sheet) refusal = "no sheet";
        else if (_scene.PickLockTarget?.Invoke(target) is not { } lockTarget) refusal = "no lock target";
        else
        {
            var lockpick = item is not null && _scene.Combat.HeldItemFields(_session, id, item) is { } fields ? AreaLockpicks.From(item, fields) : null;
            result = _session.PickLockByNpc(_session.Version, new SessionNpcSkillUser(picker.Target, sheet.Loaded, sheet.Modifiers, _session.QueryCompanion(id).LedByPc), lockTarget, lockpick);
        }
        Trace(id).Skill = result is null ? $"pick locks {target}: {refusal}"
            : $"pick locks {target} {result.Disposition} {result.Failure} success={result.Check?.Success} critical={result.Check?.Critical} result={result.Outcome?.Result}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = AiExecutorRules.PickLocksSkill, PickLock = result });
    }

    // The heal: the healer's sheet, the target's life and the held healing item; null with the refusal when the session is not asked.
    private SessionHealResult? HealByNpc(string id, string target, string? item, out string refusal)
    {
        refusal = "";
        if (Build(id) is not { } healer || _scene.NpcSheet?.Invoke(id) is not { } sheet) { refusal = "no sheet"; return null; }
        var life = target == Pc ? SessionLifeTarget.Hero : RawBuild(target)?.Target;
        if (life is null) { refusal = "no target"; return null; }
        if (item is null || _scene.Combat.HeldItemFields(_session, id, item) is not { } fields
            || AreaHealingItems.From(item, fields) is not { } healing)
        {
            refusal = "no healing item (impossible)";
            return null;
        }
        return _session.HealByNpc(_session.Version, new SessionNpcHeal(healer.Target, sheet.Loaded, sheet.Modifiers, life, healing));
    }

    /// <summary>
    /// ARC-AI-2625 (0x4C7160 case 1): the actor tests of the dodge attempt, in the exe's order: unconscious (0x45D800: fatigue at or below 0 and not undead), the spell flag 0x10000 (stoned), critter flags 0x60 (stunned 0x20 or paralysed
    /// 0x40). Any of them forces the failure (no success, no critical). The attempt tests no death: a dead critter that none of these holds could dodge, and the object flag 0x10000 <see cref="CanAct"/> reads is not tested.
    /// </summary>
    private bool DodgeRefused(string id) => Unconscious(id) || Stoned(id) || Stunned(id) || Paralysed(id);

    // Object flag 0x10000 and a stunned or paralysed critter refuse an action's turn and start.
    private bool CanAct(string id)
    {
        if (Fields(id) is not { } fields) return false;
        if ((ObjectFlagsOf(id, fields.Object.Flags) & ActionRefusedObject) != 0 || fields.Critter is { } critter && (critter.Flags & StunnedCritter) != 0 || Paralysed(id)) return false;
        return !(LifeState(id)?.Conditions.HasFlag(LifeConditions.Stunned) ?? false);
    }

    private SessionSkill? NpcSkill(string id, Skill skill) =>
        _scene.NpcSheet?.Invoke(id) is { } sheet ? _session.QueryNpcSkills(sheet.Loaded, sheet.Modifiers)[skill] : null;

    // ---- the throw (ARC-WORLD-113) ----

    /// <summary>
    /// The throw at goal 37's action frame (0x42AB90 → 0x4B3C00, ledger TW rows, TA rows): the session's <see cref="GameSessionOwner.ThrowByNpc"/>
    /// with the NPC's live cell, the item's weight as the thrower holds it (0x461370 with the thrower, TW5: its stored weight and its
    /// magic adjustment under the thrower's aptitude) and the critter the line of fire to the tile names (TW4 as corrected by TW10 and TW11,
    /// <see cref="StruckAt"/>: never the critter on the tile itself), then the attack event on it for a resolved attack (the projectile's end alerts the struck object, PF8).
    /// The item must be one the NPC holds (an item gone or taken fails, AG10). A <see cref="AreaAiNoticeKind.Throw"/> notice tells the
    /// viewer. ARC-DEV-258: a generic item (a grenade) flies as a projectile the scene launches (<see cref="AreaAiScene.LaunchThrown"/>; without a viewer it lands
    /// on the location at once), the thrower the event's parent. ARC-COMBAT-1717: a thrown weapon flies the same way when the scene can fly it (<see cref="AreaAiScene.LaunchThrown"/>): to the struck critter's tile (a hit, the strike is <see cref="LandShot"/>'s at the viewer's crossing) or free to the scatter tile (a miss); a flight that does not launch resolves at once, a decided hit striking first. ARC-COMBAT-1761: the item's removal and throw scripts run through the scene's item script host before the throw is decided (the session's <see cref="SessionNpcThrow.Scripts"/>); a throw script that
    /// answers skip default throws nothing, puts the item back with the thrower and still answers true (the goal's action frame ran), with the printed lines and eye candy in the notice. NOT READY: a first blocker on the line that is a scenery, a wall,
    /// a dead or unknown critter, and the hero on the line to another target (the line's map holds no hero): the item then lands on the
    /// tile, striking nothing.
    /// </summary>
    [OriginalFunction(0x42AB90, OriginalFunctionScope.Partial)]
    public bool ThrowItem(string id, string item, CellCoord tile)
    {
        if (!Walks(id) || Build(id) is not { } thrower || Tile(id) is not { } from || LifeState(id) is not { } state) return false;
        if (_scene.Combat.HeldItemFields(_session, id, item) is not { Item: { } stored } fields)
        {
            Trace(id).Throw = $"throw {item}: not held";
            return false;
        }
        var aptitude = Math.Clamp(thrower.EffectiveStat(CharacterStat.MagickTechAptitude, state), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        int weight;
        try
        {
            weight = Math.Clamp(Arcanum.Simulation.Items.CarryRules.ItemWeight(new(fields.Object.Type, stored.Weight, stored.MagicWeight,
                stored.MagickTechComplexity), Arcanum.Simulation.Items.CarryHolder.Critter, aptitude), 0, ThrowRules.MaxWeight);
        }
        catch (ArgumentException)
        {
            Trace(id).Throw = $"throw {item}: weight facts out of range";
            return false;
        }
        var struck = StruckAt(id, from, tile);
        var hero = struck?.Who == Pc;
        var request = new SessionNpcThrow(thrower, item, weight, from, tile)
        {
            Struck = struck is { Who: { } who } && who != Pc ? Build(who) : null,
            StruckHero = hero,
            StruckCell = struck?.Cell,
            LineOfFire = struck is { } s && _scene.LineOfFire?.Invoke() is { } map ? LineTo(from, s.Cell, s.Key, map) : LocationLine(from, tile),
            // ARC-COMBAT-2523 (TW11): a throw that names no struck critter rolls as a throw at the location: the light of the tile and the line of fire's cost are its terms.
            TargetLight = Math.Clamp(CellLight(tile), 0, AttackRules.MaxLightLevel),
            ThrowerWorn = _scene.Combat.WornItems(_session, id),
            StruckWorn = struck is not { Who: { } target } ? null : hero ? _scene.HeroWorn?.Invoke() : _scene.Combat.WornItems(_session, target),
            DropMap = _scene.DropMap?.Invoke(),
            TakingDamage = TakingDamageOf(id, struck?.Who),
            HitScripts = HitScriptsOf(id, struck?.Who),
            ItemTriggers = _scene.Spells?.Invoke()?.ItemTriggers,
            // ARC-COMBAT-1717: a thrown weapon flies when the scene can fly a thrown item (the viewer); otherwise it lands at once.
            Flight = _scene.LaunchThrown is not null,
            // ARC-COMBAT-1761 (0x466E00 → 0x467860, 0x441980): the shown map's item script host and the thrower's life target and stored stats run the item's removal and throw scripts.
            Scripts = (_events as ISessionRestockWorld)?.NpcWieldScripts(_session.QueryActor().Map, id),
        };
        var result = _session.ThrowByNpc(_session.Version, request);
        var attack = result.Attack;
        Trace(id).Throw = $"throw {item} at {tile.X},{tile.Y} struck={struck?.Who ?? "-"} {result.Disposition} {result.Failure} {attack?.Resolution?.Outcome}"
            + $" landed={(result.Landed is { } at ? $"{at.X},{at.Y}" : "-")}";
        if (result.ScriptDeclined) Trace(id).Throw += " script-declined";
        if (result.AmmunitionShort) { Trace(id).Throw += " out-of-ammunition"; NeedsAmmunition(id); }
        _notices.Add(new(AreaAiNoticeKind.Throw, id, struck?.Who) { Item = item, Throw = result, Result = attack, Messages = result.Messages, EyeCandies = result.EyeCandies });
        // The attack event on the struck critter comes first (the projectile's end alerts it, PF8), before a flight's strike or the at-once fallback's pass.
        // ARC-COMBAT-2523: a grenade flies to the location whatever the line names, so its roll is the location's and no critter is its victim.
        var attackedVictim = struck?.Who is { } hurt && fields.Object.Type != AreaHealingItems.GenericType && attack is { Disposition: not GameSessionDisposition.Rejected, Resolution.Refusal: AttackRefusal.None } ? hurt : null;
        if (attackedVictim is { } victim)
        {
            // ARC-COMBAT-1930 (0x4B3170, PF1, PF5, VCG9): a thrown item flies and sends no event at the throw; only a throw decided a hit raises it here (the strike's alert, at the shot: deviation A), with the loudness of the thrown item
            // as a weapon (0x4B2810); a throw decided a miss alerts nobody it does not strike. A throw that does not fly (no shot) keeps its event.
            if (result.Shots.Count == 0 || attack!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit)
                AttackEvent(id, victim, loudness: AwarenessRules.WeaponLoudness(fields.Weapon is { } thrownWeapon ? (uint)thrownWeapon.Flags : null));
            // ARC-DEV-292: the thrown item's hit on the struck critter is a damage step on it (the session applied it above).
            // ARC-RENDER-1990: the context's weapon is the thrown item (0x4B2210).
            HitEffect(victim, attack, fields.Object.Prototype);
            if (VictimReached(attack)) Struck(victim);
        }
        // ARC-DEV-258: a thrown generic item (a grenade) flies as a projectile; without a viewer to fly it, it lands on the location at once.
        foreach (var shot in result.Shots)
        {
            // ARC-COMBAT-1717: a thrown weapon flies to the struck critter (a hit) or the scatter tile (a miss); a generic item or an unstruck weapon, to the location.
            var destination = result.Destination ?? tile;
            var flies = _scene.LaunchThrown?.Invoke(id, shot, from, destination) == true;
            // A launch the projectile host refused has ended the shot already (the item stays on the thrower's cell); a shot still there lands on the location, and one that cannot is given up.
            var outcome = "flies";
            if (!flies)
            {
                if (_session.QueryThrownShot(shot) is null) outcome = "not flown";
                else
                {
                    // ARC-COMBAT-1717 (PF1: no art, the hit resolves at once): a thrown weapon decided a hit strikes where the flight would have met it, then the item lands there.
                    if (_session.QueryNpcShot(shot) is { Hit: true, Target: not SessionNpcShotFacts.LocationTarget } decided) LandShot(id, shot, decided.Target == SessionNpcShotFacts.HeroTarget ? Pc : decided.Target);
                    // A decided miss that resolves at once gives the victim its miss-path pass, as an unflown throw's did (ARC-DEV-292).
                    else if (attackedVictim is { } missed && _session.QueryNpcShot(shot) is not null) Struck(missed);
                    if (_session.LandThrownItem(_session.Version, shot, destination).Disposition == GameSessionDisposition.Rejected) { _session.EndShot(shot); outcome = "given up"; }
                    else outcome = "lands at once";
                }
            }
            Trace(id).Throw += $" {outcome} shot={shot.Id}";
        }
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>The throw's struck critter (ARC-COMBAT-2425, ledger TW10, TW11): the original's throw goal built by an NPC (the attack starter, the grenade) holds a
    /// location and no target object, so the only critter a throw is launched at is the first object the line of fire from <paramref name="from"/> to the tile
    /// stops at (it skips every non-wall object of the target's own tile, so the critter standing on the tile, the hero included, is never named) when it is a living
    /// critter the host knows (a walked NPC or a companion), with its cell and map key. Null when the line names no critter (a scenery or wall blocker among them: not ready):
    /// the item then lands on the tile, striking nothing.</summary>
    private (string Who, CellCoord Cell, AreaObjectKey? Key)? StruckAt(string id, CellCoord from, CellCoord tile)
    {
        if (_scene.LineOfFire?.Invoke() is { } map && LineOfFireRules.Trace(from, 0, 0, tile, map, _session.Rules.Combat).Blocker is { } row)
            return EntityOf(row.Key) is { } blocker && blocker != id && Tile(blocker) is { } at && !DeadCritter(blocker)
                && (Walks(blocker) || CompanionCell(blocker) is not null) ? (blocker, at, row.Key) : null;
        return null;
    }

    // ARC-COMBAT-2523 (TW11): the line of fire to a throw's location with no struck critter (the blocker, if any, is no living critter, or it would be the struck one): its cost, never blocked, as the hero's.
    private SessionLineOfFire LocationLine(CellCoord from, CellCoord to) =>
        new(from != to && _scene.LineOfFire?.Invoke() is { } map ? Math.Min(LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat).Cost, AttackResolver.MaxMagnitude) : 0, false);

    // The line of fire to the struck critter's cell as the check reads it (the hero's attack's SessionLineOfFire): its cost and whether
    // an object other than the struck one stops it.
    private SessionLineOfFire LineTo(CellCoord from, CellCoord to, AreaObjectKey? struck, ILineOfFireMap<AreaObjectRecord> map)
    {
        var trace = LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat);
        return new(Math.Min(trace.Cost, AttackResolver.MaxMagnitude), trace.Blocker is { } b && b.Key != struck);
    }

    /// <summary>ARC-WORLD-108: the NPC that holds the item now (0x4612A0; ARC-DEV-075: of any map the host holds, a visiting companion's holder entry is its home map's), or null (the hero's items are not the AI's).</summary>
    public string? ItemHolder(string item) => _scene.Combat.NpcHolder(_session, item);

    // ---- ARC-WORLD-108: an NPC's item use (0x462CC0: goal 39's last node and the chooser's use at once) ----

    /// <summary>The item use's facts of an NPC's item: its fields' type and flags (<see cref="AreaItemUseFacts"/>), its spell charges and
    /// magical flag as the cast host's item lookup gives them, and the scroll test (a scroll and the user's intelligence below 5,
    /// 0x4B1750). An item the NPCs do not hold reads as an empty use.</summary>
    public AiUsedItem UsedItem(string user, string item)
    {
        if (UseFacts(item) is not { } facts) return new();
        var tooHard = facts.Type == AreaItemUseFacts.ScrollType && (Find(user)?.Intelligence ?? 0) < SpellBookRules.MinimumIntelligence;
        return AreaItemUse.Used(facts, _scene.Spells?.Invoke()?.Item(item), tooHard);
    }

    /// <summary>The use script (0x441980) of an item the NPC holds (ARC-SCRIPT-1590): run through the scene's script runner with the NPC as the user and the use's target as the extra object (<see cref="AreaAiScene.ItemUseScript"/>); false when the script skipped the default,
    /// which ends the use (its action points are still charged; the food script 30282 destroys the food itself). Without a runner, or for a run it could not apply (refused, effects rejected, an item made or kit without a row), an item with a script goes on as if the script let it,
    /// which the trace notes.</summary>
    public bool RunUseScript(string user, string item, string? target)
    {
        if (UseFacts(item) is not { UseScript: true }) return true;
        if (_scene.ItemUseScript is not { } run) { Trace(user).Item += $" use-script {item} not run"; return true; }
        var goesOn = run(user, item, target);
        NoteUsedItem(user, item);
        Trace(user).Item += $" use-script {item} {(goesOn ? "default" : "skipped")}";
        return goesOn;
    }

    // The targeting cursor (0x4EE060) is the player's: an NPC's use always has a target (the starter's and the chooser's), so it never opens.
    public void OpenTargeting(string user, string item) => Trace(user).Item += $" targeting {item} not ready";
    // NOT READY for an NPC: a trap device's use, reading a written item, picking a lock and the heal skill with an item (goal 40, 0x4352C0).
    public void UseTrapDevice(string user, string item, string? target) => Trace(user).Item += $" trap-device {item} not ready";
    public void ReadWritten(string user, string item) => Trace(user).Item += $" read {item} not ready";
    public bool Lockable(string target) => false;
    public void StartSkillWithItem(string user, string item, string target, int skill) => Trace(user).Item += $" skill {skill} with {item} not ready";

    /// <summary>The magic branch (0x4F2810 … 0x4CBF70) through the cast host's item use (<see cref="SessionItemTriggers.UseItem"/>):
    /// for an NPC the slot-0 activation casts nothing (NU2), the item used executor's spells start at the target and play at once,
    /// a food or scroll is spent. Without the map's cast host or its item executor nothing happens (the trace says so).</summary>
    public void UseItemMagic(string user, string item, string? target)
    {
        if (_scene.Spells?.Invoke()?.ItemTriggers is not SessionItemTriggers triggers) { Trace(user).Item += $" magic {item} needs the cast host"; return; }
        var used = triggers.UseItem(item, target);
        NoteUsedItem(user, item);
        Trace(user).Item += $" magic {item} {used.Outcome} {used.Cast} fired={used.ItemUsed?.Spells.Count ?? 0} spent={used.Spent}";
        _notices.Add(new(AreaAiNoticeKind.ItemUse, user, target) { Item = item, ItemUse = used });
    }

    /// <summary>Spends a used food or scroll the magic branch did not already spend (<see cref="SessionSpellCaster.SpendUsedItem"/>: the
    /// deletion out of the NPC's list, then the runs' sources cleared).</summary>
    public void SpendItem(string item)
    {
        if (_session.IsItemDestroyed(item) || _scene.Spells?.Invoke() is not { } caster) return;
        if (ItemHolder(item) is { } holder) NoteUsedItem(holder, item);
        caster.SpendUsedItem(item);
    }

    // An item the NPC used or cast from, whose cached list slot the next list request checks.
    private void NoteUsedItem(string npc, string item)
    {
        if (!_usedItems.TryGetValue(npc, out var items)) _usedItems[npc] = items = new HashSet<string>(StringComparer.Ordinal);
        items.Add(item);
    }

    // The item use's facts of an item an NPC holds, or null.
    private AreaItemUseFacts? UseFacts(string item) => ItemFields(item) is { } fields ? AreaItemUseFacts.From(item, fields) : null;

    public bool InRound => _session.QueryCombatRound() is not null;
    public string? RoundActor => _session.QueryCombatRound() is { } round ? Id(round.Actor) : null;
    public int ActionPoints(string id) => _session.QueryCombatRound() is { } round && Id(round.Actor) == id ? round.ActionPoints : 0;
    /// <summary>The round's pending cost (0x5FC244): what the last action-point check of this subturn wrote, 0 once a subturn started
    /// since (the subturn start's check of cost 0, 0x4B75D0, ARC-WORLD-095), whichever call began it.</summary>
    public int PendingCost
    {
        get => _pendingAt == _session.QueryCombatSubturnStarts() ? _pendingCost : 0;
        set { _pendingCost = value; _pendingAt = _session.QueryCombatSubturnStarts(); }
    }
    public void Consume(string id, int cost)
    {
        if (Combatant(id) is { } combatant) RoundCall(_session.ConsumeActionPoints(_session.Version, combatant, cost, _round));
    }
    public void EndSubturn(string id)
    {
        if (Combatant(id) is { } combatant) RoundCall(_session.EndCombatTurn(_session.Version, combatant, _round));
    }

    private void RoundCall(SessionCombatResult result)
    {
        _roundEvents.AddRange(result.Events);
        SubturnsEnded(result.Events);
    }

    // ---- perform's world (IAiPerformWorld) ----

    /// <summary>Active (0x45D790): not dead, unconscious, stunned, paralysed or stoned.</summary>
    public bool Active(string id)
    {
        if (Status(id) is not { } status || status.IsDead || status.IsUnconscious) return false;
        if (id == Pc) return status.IsActive;
        return Build(id) is { } build && !build.Facts.Traits.HasFlag(LifeTraits.Stoned) && !build.Facts.Traits.HasFlag(LifeTraits.Paralyzed)
            && !Stoned(id) && !Paralysed(id) && !(LifeState(id)?.Conditions.HasFlag(LifeConditions.Stunned) ?? false);
    }

    public DangerState ReadDanger(string npc) => _session.QueryDanger(npc).State;
    public DangerFlags DangerBits(string npc) => _session.QueryDanger(npc).Flags;

    /// <summary>The grenade step's bit write (the session's support-bit operations: only CHECK_GRENADE and the look-for bits may be cleared,
    /// the support bits set).</summary>
    public void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear)
    {
        if (clear != DangerFlags.None) _session.ClearAiSupportBits(_session.Version, npc, clear);
        if (set != DangerFlags.None) _session.SetAiSupportBits(_session.Version, npc, set);
    }
    public BackOffRun BackOff(string npc, string? target) => MoraleRules.BackOff(this, npc, target ?? Pc, DangerBits(npc));
    // ARC-AI-1512 (VBG1): the back-off's "away" path (0x41F3C0 with flag 0x800, 0x41F6C0): the first of the eight lines from the side opposite the target, range - distance
    // tiles out, that the direct attempt completes over the NPC's own step evidence (critters block it as in the NPC's other paths, no gate, no search); none when no line is clear.
    public CellCoord? AwayTile(string npc, string? target, int range)
    {
        if (Tile(npc) is not { } from || Tile(target ?? Pc) is not { } to) return null;
        // The target is no exception: the direct attempt treats it as any other blocker of a line through its cell.
        var source = new AreaRouteSource(Steps, _scene.Chart, () => Occupancy(npc, null), _scene.Passages);
        return RoutePlanner.AwayTile(source, ToChart(from), ToChart(to), range) is { } tile ? ToWorld(tile) : null;
    }
    public void EndBackOff(string npc) => _session.EndBackOff(_session.Version, npc);
    // ARC-DEV-241: set by ScriptCast around its one Cast call so the cast uses the script entry point (free and unresistable flags).
    private (bool Free, bool Unresistable)? _scriptCast;

    /// <summary>
    /// Perform's cast (0x4AC250 → 0x455AC0 → 0x455C30): the cast host's <see cref="SessionSpellCaster.CastByAi"/> with the choice's
    /// spell and target; a started run's casting goal is 82 when the record's casting animation is not −1, else 26. ARC-WORLD-104
    /// (SU13, AC5): when the NPC's run has goal 26 or 27 at its bottom (0x44E830; 82 and 83 are not found) the new goal is queued
    /// beneath it (<see cref="SessionSpellCaster.Queue"/>, the run's current index as its depth): inserted at the bottom of the run,
    /// so the queued goals play in the order they were cast, each when the goals above it have popped. A depth of 4 or more or a
    /// turn-based round refuses, freeing the run. Either way no run starts and perform keeps the current one.
    /// ARC-WORLD-108: a choice with an item casts the item's spell with the item as source (a scroll or food item spent, MG47 for the
    /// others' run sources). NOT READY: a cast while goal 82 or 83 is at the run's bottom, which the original starts as a second run
    /// beside it (AC5; the host holds one run per NPC): it starts no run.
    /// </summary>
    public AiCastStart Cast(string npc, AiPassChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (_scene.Spells?.Invoke() is not { } caster) return new(null);
        if (RunOf(npc)?.Bottom.Goal is GoalType.CastSpellWithAnimation or GoalType.AttemptSpellWithAnimation)
        {
            Trace(npc).Cast = $"cast {choice.Spell} not started (goal 82 or 83 plays: a second run)";
            return new(null);
        }
        // ARC-DEV-241: a script's cast (ScriptCast) runs the same body with the script entry point (no cost, no line of fire, the free and unresistable flags).
        var attempt = _scriptCast is { } script && choice.Item is null && choice.Target is { } scriptTarget
            ? caster.CastByScript(npc, choice.Spell, scriptTarget, script.Free, script.Unresistable)
            : caster.CastByAi(npc, choice.Spell, choice.Target, choice.Item);
        if (choice.Item is { } fromItem) NoteUsedItem(npc, fromItem);
        Trace(npc).Cast = $"cast {choice.Spell}{(choice.Item is null ? "" : " from " + choice.Item)} at {choice.Target ?? "-"} {attempt.Outcome} {attempt.Run?.Failure} {attempt.Run?.Creation?.Check?.Refusal} {(attempt.Refusal == Arcanum.Simulation.Magic.SpellInvocationRefusal.None ? "" : "check " + attempt.Refusal)} {attempt.RefusalNote}";
        if (attempt.RunId is not { } run) return new(null);
        var castAnimation = (caster.SpellRecord(choice.Spell)?.Run.CastAnimation ?? -1) != -1;
        if (RunOf(npc) is not { } held || held.Bottom.Goal is not (GoalType.CastSpell or GoalType.AttemptSpell))
            return new(run, CastAnimation: castAnimation);
        var depth = held.Frames.Length - 1;
        var queued = caster.Queue(run, depth);
        Trace(npc).Cast += $" queue depth={depth} {queued}";
        if (queued == SpellCastQueueOutcome.Queued)
        {
            // The perform's target, as a started goal's; a queued goal whose run was freed meanwhile (QC6) lost its slot to this one.
            var target = choice.Target ?? (_session.QueryDanger(npc).Source is { } source ? Id(source) : null);
            var goal = new GoalFrame(castAnimation ? GoalType.CastSpellWithAnimation : GoalType.CastSpell, target) { SpellRun = run };
            var frames = held.Frames.RemoveAll(f => !ReferenceEquals(f, held.Top) && f.SpellRun == run
                && f.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation).Insert(0, goal);
            // ARC-DEV-134: the queued goal goes beneath the stack in the session's table (GS14). A stack the table refuses (eight goals, the run being stepped) has no
            // goal to play the queued run, which is freed unpaid as a dropped queue's.
            var refused = _session.ReplaceGoalFrames(held.Handle, frames);
            if (refused == SessionGoalFailure.None) _queuedCasts.Add(run);
            else
            {
                if (OwnRun(npc, run) && caster.DropQueued(run) is { } freed) _notices.Add(new(AreaAiNoticeKind.CastEnd, npc, null) { Run = run, SpellRun = freed });
                Trace(npc).Cast += $" not queued in the table: {refused}";
                return new(null, castAnimation);
            }
        }
        return new(null, castAnimation, Queued: queued == SpellCastQueueOutcome.Queued);
    }

    /// <summary>ARC-RENDER-1544: puts the NPC into combat mode as the attack goal's start does (<see cref="BeginAttack"/>), for a caller that shows or tests what it wields.</summary>
    public SessionCombatModeResult? EnterCombatMode(string npc) =>
        NpcCombatMode(npc) is { } context ? _session.SetCombatMode(_session.Version, SessionCombatant.Critter(npc), true, context) : null;

    public void LeaveCombatMode(string npc)
    {
        if (NpcCombatMode(npc) is { } context) _session.SetCombatMode(_session.Version, SessionCombatant.Critter(npc), false, context);
    }

    /// <summary>ARC-WORLD-1840: the critter's own leader (0x45DE00): the hero, an NPC leader (a summoner, a script's group) or none (the session's <see cref="GameSessionOwner.QueryLeader"/>).</summary>
    public string? Leader(string npc) => npc == Pc ? null : _session.QueryLeader(npc);

    /// <summary>ARC-WORLD-1840: the player character at the top of the critter's chain of leaders (0x45DDA0): the hero for a follower of the hero or of anyone the hero leads, else null.</summary>
    public string? PcLeader(string npc) => npc == Pc ? null : _session.QueryPcLeader(npc);
    public bool Waiting(string npc) => _session.QueryCompanion(npc).WaitBit;
    /// <summary>ARC-DEV-170 (B06): the routine of a leaderless NPC (the non-combat perform's branch, 0x4AC583): the unforced waypoint step, the unforced standpoint step when it answered 0
    /// (WS11), each starting the walk goal to the waypoint or the standpoint. Left out (NOT READY): the fidget start (0x435CE0), the step apart (0x4364D0) after both answered 0 and the
    /// 1 in 100 bark (0x4A8F90).</summary>
    public void RunRoutine(string npc)
    {
        if (RunRouteStep(npc, NpcRouteRules.UnforcedWaypoints, waypoints: true)) return;
        RunRouteStep(npc, facts => NpcRouteRules.UnforcedStandpoint(facts, () => _session.AiRolls.Roll(AiRollPurpose.StandpointHourRoll, 1, NpcRouteRules.HourRollSides) == 1), waypoints: false);
    }

    public bool StartBackOff(string npc, string? target) =>
        target is not null && MoraleRules.StartsBackOff(this, npc, target, _session.AiRolls)
        && _session.StartBackOff(_session.Version, npc).Disposition == GameSessionDisposition.Changed;

    /// <summary>
    /// ARC-DEV-364 (T-h): combat perform's grenade step (0x4AC910, <see cref="AiGroundRules.Grenade"/>) over this host as the
    /// <see cref="IAiGrenadeWorld"/>: the NPC's first generic item with the grenade flag (<see cref="Grenade(string)"/>), its throwing
    /// points, the grenade's blast radius, the line of fire to the target's tile and the critters of the blast's square. A throw is then
    /// the executor's goal 37 (<see cref="ThrowItem"/>). NOT READY: nothing; the original has no further test.
    /// </summary>
    public AiGrenadeResult Grenade(string npc, string target)
    {
        var result = AiGroundRules.Grenade(this, npc, target, _session.Rules.Combat);
        Trace(npc).Grenade = $"grenade at {target}: {result.Stop}{(result.Grenade is { } item ? " " + item : "")}{(result.Protected is { } friend ? " friend " + friend : "")}";
        return result;
    }

    /// <summary>The generic item flag of a grenade (generic flags 0x10, ledger TI3): the AI's throw takes the first such item of the NPC's list.</summary>
    public const uint GrenadeItemFlag = 0x10;

    /// <summary>The first generic item (type 14) of the NPC's list (fields 0xED/0xEE, listed or worn, in its order) whose generic flags hold
    /// <see cref="GrenadeItemFlag"/> (0x462760, TI3); none for the hero.</summary>
    public string? Grenade(string npc) =>
        npc == Pc ? null : _scene.Combat.NpcItems(_session, npc).Where(item => GrenadeTool(item.Fields)).Select(item => item.Entity).FirstOrDefault();

    private static bool GrenadeTool(CombatFields fields) =>
        fields.Object.Type == AreaHealingItems.GenericType && fields.Generic is { } generic && (generic.Flags & GrenadeItemFlag) != 0;

    /// <summary>The thrower's stored throwing points (basic skill 3, 0x4C5FA0, bits 0..5): at least one. An NPC without a sheet has none.</summary>
    public bool HasThrowingPoints(string npc) => NpcSkill(npc, Skill.Throwing) is { Points: > 0 };

    /// <summary>The grenade's blast radius (0x466230, <see cref="AiGroundRules.BlastRadius"/>): from the item's first spell slot and that
    /// spell's Begin radius as the cast host's record gives it (0 without a cast host or a record).</summary>
    public int BlastRadius(string grenade)
    {
        var slot = ItemFields(grenade)?.Item is { } item && item.Spells.Count > 0 ? item.Spells[0] : ItemSpellTriggerRules.NoSpell;
        var caster = _scene.Spells?.Invoke();
        return AiGroundRules.BlastRadius(slot, spell => caster?.SpellRecord(spell)?.Areas is { Length: > 0 } areas ? areas[0].Radius : 0);
    }

    /// <summary>The first object that stops the line of fire (0x4ADE00, cost ignored) from the NPC's tile to the target's; null when it is
    /// clear or the host has no map. The trace skips the target tile's own non-wall objects (<see cref="LineOfFireRules.Trace{TObject}"/>), and any other blocker stops the throw, as in the original (TH2).</summary>
    public string? LineOfFireBlocker(string npc, string target) =>
        Line(npc, target, out _) is { Blocker: { } row } ? EntityOf(row.Key) ?? row.Key.ToString() : null;

    /// <summary>The critters in the square of half-size <paramref name="halfSize"/> around the object (0x4AE4E0): the Original query asks
    /// for NPCs only, so the hero is left out unless <paramref name="withPcs"/>.</summary>
    public IReadOnlyList<string> SquareAround(string center, int halfSize, bool withPcs) =>
        [.. Square(center, halfSize).Where(id => withPcs || id != Pc)];

    /// <summary>ARC-WORLD-113: the attack starter's throw (0x434B00, TW1, AG13): the item the NPC wears in its weapon slot (the session's
    /// view) when it is a weapon with the "default throws" flag (weapon flag 0x200) that the remove check (0x466DA0: no no-drop flag)
    /// lets go. Not ready: a weapon the session's throw refuses (one with scripts, one switched off, or one the session added to the NPC, which no seed
    /// holds) is answered as not throwable, so the NPC attacks with it instead of starting a throw that always fails.</summary>
    public string? ThrowableWeapon(string npc)
    {
        if (!Walks(npc) || _scene.Combat.Worn(_session, npc) is not { } worn) return null;
        foreach (var item in worn)
            if (item.Slot == EquipmentSlot.Weapon && item.Fields.Weapon is { } weapon && weapon.Flags.HasFlag(CombatWeaponFlags.DefaultThrows) && !item.Wear.NoDrop
                && item.Fields.Object.ScriptPoints.Count == 0 && !_session.QueryNpcInventory(npc).Any(made => made.Entity == item.Entity)
                // ARC-SCRIPT-2397 (IV21, IV22): a switched-off weapon (a toggle or the data's bit, object flag 0x2) is refused by the throw node 0x42AB90, so it is not a throw here (the original loses it, which is not copied).
                && !_session.QueryItemOff(_session.QueryActor().Map, item.Entity, (item.Fields.Object.Flags & AreaUseScriptHost.OffObjectFlagBit) != 0))
                return item.Entity;
        return null;
    }

    /// <summary>ARC-WORLD-113: the attack starter's removal (0x466E00, read by this task: 0x4612A0 then 0x467860, the item leaves its
    /// holder's inventory entirely, ledger TA1). The original then loses the weapon whenever the throw goal never reaches its throw (a
    /// refused start, an interrupted or replaced run); a lost object, so both rule modes keep it in the NPC's hand until the throw,
    /// which removes it (<see cref="GameSessionOwner.ThrowByNpc"/>). Nothing changes here.</summary>
    public void Unwield(string npc, string item) => Trace(npc).Throw = $"unwield {item} (kept until the throw)";
    /// <summary>ARC-WORLD-109: the skill animation's art (0x436220's test) is the fixed stand-in's, so every NPC the host walks has it.</summary>
    public bool HasSkillArt(string npc) => Walks(npc);
    public bool BoomerangInFlight(string npc) => false;
    // The starters' interrupt (0x424070 at level 3, GS13): the end nodes of the runs it ended run now, and a refusal (a run whose current goal has priority 4 or 5) refuses the start.
    public bool InterruptLowerAnimations(string id) => InterruptOwner(id, GoalInterruptRules.OrdinaryActions).Succeeded;

    /// <summary>
    /// ARC-AI-1315 (VA4, VAG4): the critters on the object's tile for the tile-sharer finder and the step apart (0x436720, 0x4364D0): the hero, the shown map's NPCs and the
    /// companions on it, dead ones included (<see cref="CrittersOn"/>), each with its life, its busy-run test (<see cref="GoalRunTable.BusyRun"/>) and a stand-in for the handle
    /// order: the original compares run-time pool handles (OM1), so the order of this list (the hero, the map's NPCs in the scene's order, then the companions) stands for it.
    /// </summary>
    public IReadOnlyList<AiTileCritter> TileCritters(string id)
    {
        if (Tile(id) is not { } cell) return [];
        var goals = _session.QueryGoals();
        var order = 0L;
        return CrittersOn(cell).Select(c => new AiTileCritter(c, c == Pc, !DeadCritter(c), goals.BusyRun(c) is not null, order++)).ToList();
    }

    /// <summary>
    /// ARC-AI-1315: the please-move starter's start (0x4363E0 after its choice, gate and busy test): goal 68 on <paramref name="mover"/> with <paramref name="awayFrom"/> stored,
    /// through the table's generic start (the interrupt at the goal's own priority, the first step 5 ms later).
    /// </summary>
    public bool StartPleaseMove(string mover, string awayFrom)
    {
        var run = new GoalRun(mover, default, new GoalFrame(GoalType.PleaseMove, awayFrom));
        return Started(mover, RunOf(mover), new AiPerformResult(AiPerformKind.PleaseMove, run) { Started = true });
    }

    /// <summary>
    /// ARC-UI-2085 (the order handler's move order): the please-move starter asked with the hero first and the follower <paramref name="follower"/> second
    /// (<see cref="AiTileSharerRules.PleaseMove"/>: the follower, not a player character, moves away from the hero when the start gate with cost 0 passes and it is not busy). True when
    /// goal 68 started, which decides the order's line (the acknowledgement; else the refusal). False for a follower this host does not run.
    /// </summary>
    public bool AskToMove(string follower)
    {
        ArgumentNullException.ThrowIfNull(follower);
        if (!_followers.ContainsKey(follower) || follower == Pc) return false;
        var goals = _session.QueryGoals();
        return AiTileSharerRules.PleaseMove(this, new AiTileCritter(Pc, true, !DeadCritter(Pc), goals.BusyRun(Pc) is not null, 0),
            new AiTileCritter(follower, false, !DeadCritter(follower), goals.BusyRun(follower) is not null, 1));
    }

    /// <summary>
    /// ARC-SCRIPT-1135 (CF4): the dialog <c>so</c> action's goal start (0x436960 right after the flag, ledger OL8/CF4): the spread-out goal (69) on the follower <paramref name="npc"/> toward the hero, through the
    /// executor's starter (gate with cost 0) and the table's own start (level 3, a duplicate refused). The answer of the starter is never seen by the action. False: not a follower this host runs, one the hero
    /// does not lead (no leader: the original's null-leader case is not followed), or a start the gate or the table refused (the text is in the NPC's trace).
    /// </summary>
    public bool StartSpreadOut(string npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (!_followers.ContainsKey(npc) || !_session.QueryCompanion(npc).LedByPc) return false;
        return Started(npc, RunOf(npc), AiExecutorRules.StartSpreadOut(this, npc, Pc));
    }

    /// <summary>
    /// ARC-AI-1315: the blocked-step test of goal 68's tile choice (0x425760): the step from the object's tile to the neighbouring <paramref name="next"/> is clear when no living
    /// critter stands on it and the terrain, walls and objects let the step through (the route source's verdict, with the moved records and the other movers' cells as the planner
    /// sees them). No trap check and no random wait, which the walker's own step test (<see cref="StepBlocked"/>) adds.
    /// </summary>
    public bool StepClear(string id, CellCoord next)
    {
        if (Tile(id) is not { } from || AiExecutorRules.Distance(from, next) != 1) return false;
        if (LivingCritterOn(next, id)) return false;
        var chart = ToChart(from);
        var direction = RoutePlanner.Toward(chart, ToChart(next));
        return new AreaRouteSource(Steps, _scene.Chart, () => Occupancy(id, null), _scene.Passages).Assess(chart, direction) == RouteStepVerdict.Open;
    }

    public AiFleeing PerformFleeing(string npc)
    {
        var result = _session.PerformFleeing(_session.Version, new SessionPerformFleeingRequest
        {
            Entity = npc, Facts = SetterFacts(npc), CombatMode = NpcCombatMode(npc),
        });
        var source = _session.QueryDanger(npc).FleeingFrom is { } from ? Id(from) : null;
        return new(result.StartsFleeGoal, source);
    }

    public void PerformSurrender(string npc)
    {
        if (MoraleRules.SurrenderEnds(this, npc)) SetDanger(npc, DangerState.None, null);
    }

    // ---- the support rules' world, the spell half (IAiSupportWorld, ARC-WORLD-102) ----

    /// <summary>The support facts: MECHANICAL (critter flag 0x20000000), CAST_HIGHEST (NPC flag 0x40000), the fatigue percent (0x4AB430:
    /// 100 × current / maximum, toward zero; 0 for a maximum of 0, where the original divides by zero), the current fatigue, the
    /// poison level, the effective willpower and the magick points (an NPC's session record when it has one, else its effective stat).</summary>
    public AiSupportCritter Support(string id)
    {
        var status = Status(id);
        var fatigue = status is { MaxFatigue: not 0 } s ? (int)(100L * s.Fatigue / s.MaxFatigue) : 0;
        if (id == Pc)
        {
            // The hero is a healer only in the heal step (not run yet): its stats without the poison's effect, as the setter's facts read them.
            var stats = _session.QueryHeroStats(_session.HeroStatContext())?.Snapshot;
            return new AiSupportCritter
            {
                FatiguePercent = fatigue, CurrentFatigue = status?.Fatigue ?? 0, PoisonLevel = _session.QueryHeroLife()?.Record.State.PoisonLevel ?? 0,
                Willpower = stats?.GetEffective(CharacterStat.Willpower) ?? 0, MagickPoints = stats?.GetEffective(CharacterStat.MagickPoints) ?? 0,
            };
        }
        if (Fields(id) is not { Critter: { } critter, Npc: { } npc } || Build(id) is not { } build) return new AiSupportCritter();
        var state = LifeState(id) ?? build.Loaded;
        return new AiSupportCritter
        {
            Mechanical = (critter.Flags & MechanicalFlag) != 0, CastHighest = (npc.Flags & CastHighestNpc) != 0,
            FatiguePercent = fatigue, CurrentFatigue = status?.Fatigue ?? 0, PoisonLevel = state.PoisonLevel,
            Willpower = build.EffectiveStat(CharacterStat.Willpower, state),
            MagickPoints = _session.QueryNpcAdvancement(id)?.Sheet.MagickPoints ?? build.EffectiveStat(CharacterStat.MagickPoints, state),
        };
    }

    /// <summary>The AI parameters 13 (the defensive list's skip), 14 (the combat heal chance) and 15 (the combat distance); out of the
    /// table 0.</summary>
    public AiSupportParameters SupportParameters(string npc) => Row(npc) is { } row
        ? new(row.DefensiveSpellRoll, row.CombatHealChance, row.CombatDistance) : default;

    public bool NpcFighting => true;
    public AiGlobalLists GlobalLists => _scene.Spells?.Invoke()?.AiLists ?? NoLists;

    /// <summary>The list request (0x4CCA90) through the host's cache (<see cref="AiListCache.Request"/>: the Original setting's frozen
    /// first list, SP11; built fresh in Steamwoven). The cache is not saved: a load or a map change builds the host again.</summary>
    public ImmutableArray<AiListEntry> List(string critter, AiActionList list)
    {
        // ARC-WORLD-108: an item the NPC used or cast from that is gone drops the NPC's slot (the force-removal 0x467860 → 0x4CCC00), once
        // each, so the Original setting's frozen list lists it no more. ARC-AI-2402: an item out of spell charges (field 105) drops nothing
        // (the charge branch of 0x450420 never calls 0x4CCC40); a worn store drained to 0 does, through ManaStoreEmptied.
        if (_usedItems.TryGetValue(critter, out var used))
            foreach (var item in used.ToArray())
            {
                if (!_session.IsItemDestroyed(item)) continue;
                _listCache = _listCache.Clear(critter);
                used.Remove(item);
            }
        var request = _listCache.Request(this, critter, list, _session.Rules.Combat);
        _listCache = request.Cache;
        return request.Entries;
    }

    /// <summary>ARC-AI-2402 (ledger BC10, SP11): a payment drained the worn item's mana store of <paramref name="critter"/> to 0 (the cast host's
    /// <see cref="SessionSpellCaster.ManaStoreEmptied"/>; the original's 0x450420 → 0x4CCC40 at 0x45062B): the critter's slot of the list cache
    /// drops when one of its lists holds the item (<see cref="AiListCache.ItemExhausted"/>), so the Original setting's frozen list is
    /// rebuilt at the next request. Steamwoven builds every list fresh, so the call changes nothing there.</summary>
    public void ManaStoreEmptied(string critter, string item)
    {
        ArgumentNullException.ThrowIfNull(critter);
        ArgumentNullException.ThrowIfNull(item);
        _listCache = _listCache.ItemExhausted(critter, item);
    }

    /// <summary>The list cache as it stands (read for tests and the trace).</summary>
    public AiListCache ListCache => _listCache;

    /// <summary>ARC-WORLD-108: the NPC's wielded weapon (slot 1004, as the session shows what it wears) when it has item flag 0x40
    /// (hexed); null for the hero (the lists are an NPC's own) and without one.</summary>
    public string? HexedWeapon(string critter) =>
        critter != Pc && _scene.Combat.Worn(_session, critter)?.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is { Entity: { } weapon } worn
            && worn.Fields.Item is { } item && (item.Flags & HexedItem) != 0 ? weapon : null;

    /// <summary>ARC-WORLD-108: the NPC's items, listed or worn, in its list's order as the session shows them (the combat host's
    /// <see cref="AreaCombatHost.NpcItems"/>); none for the hero.</summary>
    public IReadOnlyList<string> Inventory(string critter) =>
        critter == Pc ? [] : [.. _scene.Combat.NpcItems(_session, critter).Select(item => item.Entity)];

    /// <summary>
    /// ARC-WORLD-108: an item as the list build and the chooser read it: the spell charges (field 0x69, as the cast host's item lookup
    /// gives them with the session's spent charges), identified (item flag 0x1, or identified in the session), exempt as food or a
    /// scroll, the five slots (10000 none → −1), the valid-AI-action flag (0x200000) and the AI action (field 0x6A). An item the NPCs
    /// do not hold reads as empty.
    /// </summary>
    public AiListItem Item(string item)
    {
        if (ItemFields(item) is not { Item: { } fields } all) return new();
        var spells = _scene.Spells?.Invoke()?.Item(item);
        return new AiListItem
        {
            ManaStore = spells?.Charges ?? fields.SpellManaStore,
            Identified = (fields.Flags & IdentifiedItem) != 0 || _session.QueryItemIdentified(item),
            IdentificationExempt = all.Object.Type is SessionItemSpells.FoodType or SessionItemSpells.ScrollType,
            Spells = [.. Enumerable.Range(0, ItemSpellTriggerRules.Slots).Select(slot => slot < fields.Spells.Count && fields.Spells[slot] != ItemSpellTriggerRules.NoSpell ? fields.Spells[slot] : -1)],
            ValidAiAction = (fields.Flags & ValidAiActionItem) != 0,
            AiAction = fields.AiAction,
        };
    }

    public bool CanUseItem(string critter, string item) => _scene.Spells?.Invoke()?.AiCanUseItem(critter, item) ?? false;
    public int ItemFailChance(string critter, string item, string target, int spell) =>
        _scene.Spells?.Invoke()?.AiItemFailChance(critter, item, target, spell) ?? 0;

    /// <summary>
    /// ARC-WORLD-116: the heal skill's tool (0x4C91F0 with skill 10 → 0x462760 with flag 0x8, ledger HI1): the first item of the NPC's
    /// list (fields 0xED/0xEE, listed or worn, <see cref="AreaCombatHost.NpcItems"/>) that is a generic item (type 14) with the healing
    /// flag (generic flags 0x8); none for the hero. NOT READY: the original then searches the substitute container of an NPC without a
    /// leader (0x45F650), which the host does not read; it applies only without a leader, so never to a companion.
    /// </summary>
    public string? HealingItem(string npc) =>
        npc == Pc ? null : _scene.Combat.NpcItems(_session, npc).Where(item => HealingTool(item.Fields)).Select(item => item.Entity).FirstOrDefault();

    /// <summary>
    /// ARC-WORLD-116: the heal skill check (0x4AE570, skill 10, ledger SP3 as corrected by HI2): the item must be a generic item with the
    /// healing flag (refusal 1), the target the hero or an NPC (refusal 2), and the healer neither undead (critter flag 0x4) nor
    /// mechanical (0x20000000; refusal 2). The target's own flags are not read here (the heal helper's mechanical test, P4, comes first).
    /// </summary>
    public bool HealSkillAccepts(string npc, string item, string target)
    {
        if (ItemFields(item) is not { } fields || !HealingTool(fields)) return false;
        if (target != Pc && Fields(target)?.Critter is null) return false;
        return Fields(npc)?.Critter is { } healer && HealSkillUsable(healer.Flags);
    }

    /// <summary>ARC-DEV-129: the heal skill check's healer test (0x4AE570, refusal 2): a critter that is neither undead (0x4) nor
    /// mechanical (0x20000000) can use the skill.</summary>
    public static bool HealSkillUsable(uint critterFlags) => (critterFlags & (Undead | MechanicalFlag)) == 0;

    /// <summary>ARC-DEV-129: a follower the follower smoke can use: it heals with the skill (<see cref="HealSkillUsable"/>) and takes the hero's
    /// victim as its own (the danger setter's rule 1, 0x4AABE0, keeps an invulnerable or nigh-invulnerable NPC out of every fight).</summary>
    public static bool CanHealAndFight(uint objectFlags, uint critterFlags, uint critterFlags2) =>
        HealSkillUsable(critterFlags) && (objectFlags & Invulnerable) == 0 && (critterFlags2 & NighInvulnerable2) == 0;

    private static bool HealingTool(CombatFields fields) =>
        fields.Object.Type == AreaHealingItems.GenericType && fields.Generic is { } generic && (generic.Flags & SessionHealingItem.HealingFlag) != 0;

    // The fields of an item an NPC (of the shown map, or a companion carried here) holds now, or null.
    private CombatFields? ItemFields(string item) => _scene.Combat.NpcItemFields(_session, item);

    public bool Knows(string critter, int spell) => _scene.Spells?.Invoke()?.AiKnows(critter, spell) ?? false;
    public AiSpellFacts Spell(int spell) => _scene.Spells?.Invoke()?.AiSpell(spell) ?? default;
    public int CollegeLevel(string critter, int college) => _scene.Spells?.Invoke()?.AiCollegeLevel(critter, college) ?? 0;
    public int CastCost(string critter, int spell) => _scene.Spells?.Invoke()?.AiCastCost(critter, spell) ?? 0;
    public int FreeMaintainSlots(string critter) => _scene.Spells?.Invoke()?.AiFreeMaintainSlots(critter) ?? 0;
    public bool UnderInfluence(string target, int spell) => _scene.Spells?.Invoke()?.AiUnderInfluence(target, spell) ?? false;
    public int FailChance(string critter, string target, int spell) => _scene.Spells?.Invoke()?.AiFailChance(critter, target, spell) ?? 0;

    /// <summary>The line of fire (0x4ADE00) from the critter's tile to the target's: its cost and the first object that stops it (the
    /// target's own id for its own row), as the attack's reach reads it; no map: cost 0, nothing in the way.</summary>
    public AiLineOfFire LineOfFire(string critter, string target)
    {
        if (Line(critter, target, out var targetRow) is not { } line) return new(0, null);
        var blocker = line.Blocker is not { } row ? null
            : ReferenceEquals(row, targetRow) ? target
            : EntityOf(row.Key) ?? row.Key.ToString();
        return new(line.Cost, blocker);
    }

    // ---- the hostility and morale world (IMoraleWorld) ----

    /// <summary>
    /// ARC-AI-1317: whether the handle still names an object (<see cref="IHostilityWorld.Exists"/>): the hero always; an NPC of the shown map
    /// until it is destroyed (<see cref="Gone"/>); a created critter until the session destroys it; any other object the host does not
    /// hold (another map's) is kept, since only its own map can tell.
    /// </summary>
    public bool Exists(string id) =>
        id == Pc || !Gone(id) && !(id.StartsWith(SessionCreatedObject.CritterPrefix, StringComparison.Ordinal) && _session.QueryCreatedObject(id) is null);

    // ARC-TECHNOLOGY-1619 (0x4A9650, 0x4AB990): an object that is no critter (a wall, a door, a container, a scenery, a trap object) as the hostility rules read it: its object flags and busted state; null for an id the scene names none for.
    private HostilityCritter? ObjectFound(string id) =>
        _scene.ObjectFacts?.Invoke(id) is { } facts
            ? new HostilityCritter
            {
                Id = id, Kind = facts.Type == 0 ? HostilityKind.Wall : HostilityKind.Object, Destroyed = facts.Destroyed, Off = facts.Off, DontDraw = facts.DontDraw,
                Invulnerable = facts.Invulnerable, Busted = facts.Busted,
            }
            : null;

    public HostilityCritter? Find(string id)
    {
        if (id == Pc)
        {
            if (_session.QueryHeroLife() is not { } hero) return null;
            var concealed = _session.IsConcealed(SessionCombatant.Hero);
            return new HostilityCritter
            {
                Id = Pc, Kind = HostilityKind.Pc, Invulnerable = (ObjectFlagsOf(Pc, 0) & Invulnerable) != 0, Dead = hero.Status.IsDead, Unconscious = hero.Status.IsUnconscious,
                Stunned = !hero.Status.IsActive && !hero.Status.IsDead && !hero.Status.IsUnconscious,
                InCombatMode = _session.QueryCombatMode(SessionCombatant.Hero), Level = _session.QueryHero()?.Level ?? 0,
                HealthPercent = Percent(hero.Status),
                Perception = _session.QueryHeroAttack()?.Combatant.Perception ?? 0,
                // ARC-DEV-358: the spell flags the hero's spells wrote (Invisibility, Sense Hidden, Sanctuary).
                InvisibleSpell = (SpellFlagsOf(Pc, 0) & InvisibleSpell) != 0, DetectsInvisible = (SpellFlagsOf(Pc, 0) & DetectsInvisibleSpell) != 0,
                Enshrouded = (SpellFlagsOf(Pc, 0) & EnshroudedSpell) != 0,
                // ARC-AI-1518 (critter flag 0x1, 0x45EDE0; prowling trained to master): the scan hears a concealed hero at the silent level,
                // remembers it, and refuses a master prowler it cannot see.
                Concealed = concealed, MasterProwler = concealed && _session.QueryHeroSkills()?[Skill.Prowling].Training >= SkillTraining.Master,
            };
        }
        if (!_npcs.ContainsKey(id) && CompanionCell(id) is null) return ObjectFound(id);
        if (Fields(id) is not { Critter: { } critter, Npc: { } npc } fields || Build(id) is not { } build) return null;
        var status = Status(id);
        var state = LifeState(id) ?? build.Loaded;
        var danger = _session.QueryDanger(id);
        var leader = Leader(id);
        var pcLeader = PcLeader(id);
        var npcConcealed = _session.IsConcealed(SessionCombatant.Critter(id));
        return new HostilityCritter
        {
            Id = id, Kind = HostilityKind.Npc,
            Destroyed = Gone(id), DontDraw = (ObjectFlagsOf(id, fields.Object.Flags) & DontDraw) != 0, ObjectInvisible = (ObjectFlagsOf(id, fields.Object.Flags) & ObjectInvisible) != 0,
            Invulnerable = (ObjectFlagsOf(id, fields.Object.Flags) & Invulnerable) != 0, NighInvulnerable = (critter.Flags2 & NighInvulnerable2) != 0,
            Dead = status?.IsDead ?? true, Unconscious = status?.IsUnconscious ?? false, Asleep = Sleeping(id),
            NonLethal = (critter.Flags & NonLethal) != 0, Undead = (CritterFlagsOf(id, critter.Flags) & Undead) != 0, Animal = (critter.Flags & Animal) != 0,
            AnimalEnshrouded = (critter.Flags & AnimalEnshrouded) != 0, Monstrous = build.Monstrous,
            // The NO_FLEE the AI set (session bit, ARC-WORLD-086) is ORed into the critter's stored flag.
            NoFlee = (critter.Flags & NoFleeFlag) != 0 || danger.Flags.HasFlag(DangerFlags.NoFlee),
            InvisibleSpell = (SpellFlagsOf(id, fields.Object.SpellFlags) & InvisibleSpell) != 0, DetectsInvisible = (SpellFlagsOf(id, fields.Object.SpellFlags) & DetectsInvisibleSpell) != 0,
            Stoned = Stoned(id), Stunned = state.Conditions.HasFlag(LifeConditions.Stunned) || build.Facts.Traits.HasFlag(LifeTraits.Paralyzed) || Paralysed(id),
            MindControlled = (SpellFlagsOf(id, fields.Object.SpellFlags) & MindControlledSpell) != 0, Enshrouded = (SpellFlagsOf(id, fields.Object.SpellFlags) & EnshroudedSpell) != 0,
            AttemptingAttack = RunOf(id)?.Top.Goal is GoalType.AttemptAttack or GoalType.AttemptSpell or GoalType.AttemptSpellWithAnimation,
            InCombatMode = _session.QueryCombatMode(SessionCombatant.Critter(id)),
            Guard = npc.SocialClass == GuardClass, WaitingHere = (npc.Flags & WaitingHereNpc) != 0 || Waiting(id),
            KillOnSight = (npc.Flags & KillOnSightNpc) != 0, KosOverride = (NpcFlagsOf(id, npc.Flags) & KosOverrideNpc) != 0,
            Perception = build.EffectiveStat(CharacterStat.Perception, state), Intelligence = build.EffectiveStat(CharacterStat.Intelligence, state),
            Level = build.Level, Alignment = critter.BaseStats.Count > AreaCompanionData.AlignmentStat ? critter.BaseStats[AreaCompanionData.AlignmentStat] : 0,
            HealthPercent = status is null ? 0 : Percent(status), Leader = leader, PcLeader = pcLeader,
            // ARC-AI-1560 (critter flag 0x1, 0x45EDE0, for any critter): a follower the hero's sneak hides is heard at the silent level and refused unseen when it prowls as a master.
            Concealed = npcConcealed, MasterProwler = npcConcealed && NpcSkill(id, Skill.Prowling)?.Training >= SkillTraining.Master,
        };
    }

    private static int Percent(LifeStatus status) => status.MaxHitPoints <= 0 ? 0 : (int)(100L * status.HitPoints / status.MaxHitPoints);

    public int Distance(string from, string to) => Tile(from) is { } a && Tile(to) is { } b ? AiExecutorRules.Distance(a, b) : int.MaxValue;

    /// <summary>One faction (0x45D110) as far as this host knows: the hero and every critter whose chain of leaders reaches it; two NPCs under the same player character, one the other's leader,
    /// or two followers of one leader (ARC-WORLD-1840: NPC leaders too); else one non-zero faction. The PC's reputations granting a faction are not read here.</summary>
    public bool FactionSame(string a, string b)
    {
        if (a == b) return a != Pc;
        if (a == Pc || b == Pc) return PcLeader(a == Pc ? b : a) == Pc;
        if (PcLeader(a) is { } pa && pa == PcLeader(b)) return true;
        var la = Leader(a);
        var lb = Leader(b);
        if (la == b || lb == a || la is not null && la == lb) return true;
        return Faction(a) is { } fa && fa != 0 && fa == Faction(b);
    }

    /// <summary>ARC-UI-109 (ledger OH11): the standard effective reaction (0x4C0CC0) of one NPC toward the hero, as the hovered critter's window
    /// shows it; null for an NPC this host holds no reaction facts of.</summary>
    public int? StandardReaction(string npc) =>
        ReactionFacts(npc) is { } facts ? _session.QueryReaction(npc, facts.Npc, facts.Pc).View?.Effective : null;

    /// <summary>ARC-UI-109 (ledger OH11): how well the hero hides from one NPC, as the hovered critter's window reads it while the hero is
    /// concealed (0x553F70): the sight check of the NPC toward the hero (0x4AF260), else the smaller of its excess and the hearing check with
    /// the silent loudness (0x4AF470); 0 means seen. A sleeping NPC that would see the hero counts as 1. The excess is the checks' own
    /// (<see cref="AwarenessRules.NotPerceived"/> for an NPC that cannot perceive).</summary>
    public int SneakExcess(string npc) => HoverSneakExcess(See(npc, Pc).Excess, () => Hear(npc, Pc, Loudness.Silent).Excess, Find(npc)?.Asleep == true);

    /// <summary>The window's combination of the two checks (<see cref="SneakExcess"/>), which reads the hearing check only when the sight check
    /// fails.</summary>
    public static int HoverSneakExcess(int sight, Func<int> hearing, bool asleep)
    {
        ArgumentNullException.ThrowIfNull(hearing);
        var excess = sight == 0 ? 0 : Math.Min(hearing(), sight);
        return asleep && excess == 0 ? 1 : excess;
    }

    public int HostilityReaction(string npc, string pc)
    {
        if (ReactionFacts(npc) is not { } facts) return 0;
        var query = _session.QueryReaction(npc, facts.Npc, facts.Pc, ReactionQuery.Hostility);
        return query.View?.Effective ?? 0;
    }

    public bool AllowsKillOnSight(string npc, string candidate) => true;

    public HostilityParameters Parameters(string npc) => Row(npc) is { } row
        ? new(row.FleeDistance, row.HostileReaction, row.HostileAlignmentGap, row.GoodFollowerStop) : default;

    public MoraleParameters MoraleParameters(string npc) => Row(npc) is { } row
        ? new(row.FleeHpPercent, row.FleeOutnumbered, row.FleeOutlevelled, row.StandWhileTargetWeakPercent, row.CombatDistance) : default;

    // The party's followers this world knows (the interface's contract): a follower left on another map, or a companion here with no record
    // (ARC-DEV-075: only a home the resolver cannot read), is not in a flee test's party count.
    [OriginalFunction(0x441260, OriginalFunctionScope.Partial)]
    public IReadOnlyList<string> Followers(string id) =>
        (id == Pc ? _session.QueryParty().Followers.Select(f => f.Entity) : _session.QueryFollowers(id)).Where(f => Find(f) is not null).ToArray();

    /// <summary>
    /// ARC-WORLD-1160 (E3-a; ledger RT2 and SWB4): the NPCs near the hero as the combat-mode exit gate reads them: each active NPC of the shown map that is in the combat-focus state
    /// with the hero or another critter as its focus, in the view rectangle around the hero. PROJECT: the original's rectangle is the game view's size around the hero (the vicinity list); this one is
    /// the isometric window of the original's 800 by 400 game view at its 80 by 40 tiles (|dx - dy| and |dx + dy| up to 10 cells), the viewer's own view size not being read. The gate (<see cref="Arcanum.Session.GameSessionOwner.QueryCanExitCombatMode"/>) tests the focus against the
    /// hero and the party.
    /// </summary>
    public IReadOnlyList<SessionCombatThreat> CombatThreats()
    {
        var threats = new List<SessionCombatThreat>();
        if (Tile(Pc) is not { } pc) return threats;
        foreach (var id in _npcs.Keys.ToArray())
        {
            if (Tile(id) is not { } at || Math.Abs((at.X - pc.X) - (at.Y - pc.Y)) > ViewHalfTiles || Math.Abs((at.X - pc.X) + (at.Y - pc.Y)) > ViewHalfTiles) continue;
            var danger = _session.QueryDanger(id);
            if (danger.State != DangerState.CombatFocus || danger.Focus is not { } focus) continue;
            threats.Add(new(focus, Status(id)?.IsActive ?? false, Fighting: true));
        }
        return threats;
    }

    private const int ViewHalfTiles = 10;

    /// <summary>
    /// ARC-WORLD-1160 (E3-a; ledger SWB9, the follower recursion), ARC-WORLD-2596: the life targets of the hero's follower tree the host runs, for the rest's heal, in the order of the original's follower walk (the
    /// direct followers, then each one's own tree, <see cref="GameSessionOwner.QueryFollowerTree"/>): a follower of a follower (a companion's summon or a script group under a companion) is healed too. A critter the
    /// host holds no record of is left out, as in <see cref="Followers"/>.
    /// </summary>
    public IReadOnlyList<SessionLifeTarget> FollowerLifeTargets() =>
        [.. _session.QueryFollowerTree(Pc).Where(id => Find(id) is not null).Select(RawBuild).Where(build => build is not null).Select(build => build!.Target)];

    /// <summary>
    /// ARC-COMBAT-1777 (0x441260, 0x4C9050; ledger OH4, PL5): the users the best-follower chooser scores after the PC, in the PC's all-followers list's order (<see cref="FollowerSkillRules.AllFollowers"/>): each follower of the hero this host
    /// runs and can build a life target and a sheet for (a follower it cannot read cannot be scored). NOT READY: the followers of a follower (the original's list recurses) are not a list this world keeps, so the list holds the hero's own.
    /// </summary>
    public IReadOnlyList<SessionNpcSkillUser> FollowerSkillUsers()
    {
        var users = new List<SessionNpcSkillUser>();
        foreach (var id in FollowerSkillRules.AllFollowers(Pc, leader => leader == Pc ? Followers(Pc) : []))
            if (Build(id) is { } follower && _scene.NpcSheet?.Invoke(id) is { } sheet && _followers.ContainsKey(id))
                users.Add(new SessionNpcSkillUser(follower.Target, sheet.Loaded, sheet.Modifiers, _session.QueryCompanion(id).LedByPc));
        return users;
    }

    /// <summary>
    /// ARC-COMBAT-1777 (the chooser 0x4C9050): who the skill use goes to: the hero (null) or the follower with the strictly best score (<see cref="FollowerSkillRules.Choose"/>: the hero first, ties keep the earlier). <paramref name="score"/>
    /// is the session's score of a user for the use (the hero for null; <see cref="GameSessionOwner.QueryPickLockScore"/>, <see cref="GameSessionOwner.QueryDisarmScore"/>); a user that cannot be scored is left out, and a hero that cannot is the user.
    /// </summary>
    public SessionNpcSkillUser? ChooseSkillUser(Func<SessionNpcSkillUser?, int?> score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score(null) is not { } hero) return null;
        var scores = new List<int> { hero };
        var candidates = new List<SessionNpcSkillUser?> { null };
        foreach (var user in FollowerSkillUsers())
            if (score(user) is { } value) { scores.Add(value); candidates.Add(user); }
        return candidates[FollowerSkillRules.Choose(scores)];
    }

    /// <summary>ARC-COMBAT-1777 (PL4: the finder 0x462760 returns the first lockpicks in inventory order): the first lockpicks the NPC holds, else null (a follower picks with its own lockpicks if it has any, else without).</summary>
    public string? FirstLockpicks(string id)
    {
        foreach (var listed in _session.QueryHolderItems(id) ?? [])
            if (_scene.Combat.HeldItemFields(_session, id, listed.Entity) is { } fields && AreaLockpicks.From(listed.Entity, fields) is not null) return listed.Entity;
        return null;
    }

    /// <summary>
    /// ARC-WORLD-124 (E6-b; 0x4AA0D0, ledger TP2, awareness research section 7): the alarm a trap device of record 176 gives when the hero uses it, after the device is consumed. For every NPC in the
    /// square of half-size <see cref="TrapDeviceRules.AlarmRadius"/> around the hero: a dead, gone or mind-controlled NPC and one the hero leads are skipped; one that sees the hero or hears
    /// it (silent) is attacked by the hero through the attack event, which raises the silent noise of radius 2. The notice is given when the device is used, not when it goes off. Returns the NPCs attacked.
    /// </summary>
    [OriginalFunction(0x4AA0D0, OriginalFunctionScope.Partial)]
    public IReadOnlyList<string> TrapDeviceAlarm()
    {
        var alerted = new List<string>();
        if (Tile(Pc) is not { } at) return alerted;
        foreach (var id in _npcs.Keys.Concat(_followers.Keys).Distinct(StringComparer.Ordinal).ToArray())
        {
            if (id == Pc || Gone(id) || DeadCritter(id) || Tile(id) is not { } there) continue;
            if (Math.Max(Math.Abs(there.X - at.X), Math.Abs(there.Y - at.Y)) > TrapDeviceRules.AlarmRadius) continue;
            if ((SpellFlagsOf(id, Fields(id)?.Object.SpellFlags ?? 0) & MindControlledSpell) != 0 || PcLeader(id) == Pc) continue;
            if (See(id, Pc).Excess != 0 && Hear(id, Pc, Loudness.Silent).Excess != 0) continue;
            AttackEvent(Pc, id, loudness: Loudness.Silent);
            alerted.Add(id);
        }
        return alerted;
    }

    public AwarenessResult See(string observer, string target) => SeeFacing(observer, target, null);

    /// <summary>ARC-SKILLS-010: sight (0x4AF260) with the observer turned toward the target (the hero's facing is not tracked: the viewer
    /// turns it toward the critter it picks the pocket of).</summary>
    public AwarenessResult SeeToward(string observer, string target) =>
        Tile(observer) is { } from && Tile(target) is { } to ? SeeFacing(observer, target, AreaCombatHost.Facing(from, to)) : See(observer, target);

    /// <summary>
    /// ARC-SCRIPT-1733 / ARC-SCRIPT-1773 (CS5, exe 0x4451B0 case 28 → 0x4AF260): whether a script's obj can see obj holds: the sight check of an NPC or the hero this host holds sight facts of toward an NPC or the hero, which needs nothing more than the observer's own
    /// facing (excess 0). An NPC's facing is its mover's; the hero's is the scene's <see cref="AreaAiScene.HeroRotation"/> (the hero's conditions come from its life state, the sleeping bit from the session's flags: <see cref="SightFacts"/>). Null (the script stops as not
    /// modelled) for an observer or target it holds no facts of, a hero observer when the scene gives no hero facing, a hero observer and a target that is concealed (the hero's check reads the margin of a prowling target, which is not built for it, <see cref="HeroSeesProwler"/>), the
    /// hero toward itself, and while the scene has no line-of-fire map.
    /// </summary>
    public bool? ScriptCanSee(string observer, string target) =>
        ScriptAware(observer, target) && (observer != Pc || _scene.HeroRotation?.Invoke() is not null) ? See(observer, target).Excess == 0 : null;

    /// <summary>ARC-SCRIPT-1733 / ARC-SCRIPT-1773 (CS6, case 29 → 0x4AF470 with loudness 1): the hearing check of an NPC or the hero toward an NPC or the hero at the normal loudness; null as <see cref="ScriptCanSee"/> (hearing needs no facing).</summary>
    public bool? ScriptCanHear(string listener, string source) =>
        ScriptAware(listener, source) ? Hear(listener, source, Loudness.Normal).Excess == 0 : null;

    // What both script checks need: facts of both ends, a line-of-fire map, and for the hero as the observer no prowling target (its margin is not built, ARC-SCRIPT-1773) and not itself.
    private bool ScriptAware(string from, string to) =>
        Find(from) is not null && Find(to) is { } target && _scene.LineOfFire?.Invoke() is not null && (from != Pc || (to != Pc && !HeroSeesProwler(to, target)));

    // The hero's own check reads the prowling margin of its target (0x4C62E0 with the target as the prowler: ledger AW4); this host builds it for a concealed hero toward an NPC, and for a concealed follower toward an NPC observer, not toward the hero:
    // a concealed target and an NPC with the prowling bit of its own flags (0xDA bit 0x1) refuse.
    private bool HeroSeesProwler(string to, HostilityCritter target) =>
        target.Concealed || (CritterFlagsOf(to, Fields(to)?.Critter?.Flags ?? 0) & ProwlingCritter) != 0;

    // The observer's rotation for a sight check: the turned one when the caller gives it, else the hero's own facing (<see cref="AreaAiScene.HeroRotation"/>, ARC-SCRIPT-1773) or the NPC's mover's.
    private int ObserverRotation(string observer, NeighborDirection? facing) =>
        facing is { } turned ? GridActorMath.Octant(turned)
        : observer == Pc && _scene.HeroRotation?.Invoke() is { } hero ? hero & 7
        : GridActorMath.Octant(Movers.FacingOf(observer, NeighborDirection.XPlus));

    // ARC-SCRIPT-1773 (AW2, 0x45D800): the observer refused for being down: unconscious, and the dead hero (0x4AF260 starts with that test; the hero's dead state is a fact the rules have no field for).
    private static bool Down(string observer, HostilityCritter? facts) => facts?.Unconscious == true || (observer == Pc && facts?.Dead == true);

    private AwarenessResult SeeFacing(string observer, string target, NeighborDirection? facing)
    {
        if (Tile(observer) is not { } at || Tile(target) is not { } there || _scene.LineOfFire?.Invoke() is not { } map) return new(AwarenessRules.NotPerceived, AwarenessReason.State);
        var facts = Find(observer);
        var stunned = Stunned(observer);
        var blinded = observer == Pc ? (HeroLifeState()?.Conditions.HasFlag(LifeConditions.Blinded) ?? false) || (_session.QueryObjectFlags(Pc).Critter(0) & unchecked((int)BlindedCritter)) != 0 : Blinded(observer);
        var rotation = ObserverRotation(observer, facing);
        var sleeping = observer == Pc ? (_session.QueryObjectFlags(Pc).Critter(0) & unchecked((int)SleepingFlag)) != 0 : facts?.Asleep ?? false;
        var sight = new SightFacts
        {
            Perception = Math.Clamp(facts?.Perception ?? 0, 0, AwarenessRules.MaxPerception),
            Rotation = rotation,
            // ARC-AI-1519 (VBG8): the refusal flags 0x10A0 are stunned 0x20, blinded 0x80 and sleeping 0x1000, not paralysis 0x40; Perception 20 or more (0x4B0EE0) sees the invisible.
            Stunned = stunned, Blinded = blinded, Sleeping = sleeping, Unconscious = Down(observer, facts),
            ExtraordinaryPerception = (facts?.Perception ?? 0) >= AwarenessRules.ExtraordinaryPerceptionMin,
            TargetInvisible = Find(target) is { } seen && (seen.ObjectInvisible || seen.InvisibleSpell), DetectsInvisible = facts?.DetectsInvisible ?? false,
            // The margin is read only where the sight check gets as far as Perception (not for a stunned, blinded, sleeping or unconscious observer, nor one facing away).
            ProwlingMargin = !Down(observer, facts) && !sleeping && !stunned && !blinded && AwarenessRules.Faces(at, rotation, there)
                ? ProwlingMargin(observer, target, at, there, facts, map, true) : 0,
        };
        var (offsetX, offsetY) = ObserverOffset(observer);
        return AwarenessRules.See(sight, at, offsetX, offsetY, there, map, _session.Rules.Combat);
    }

    /// <summary>
    /// ARC-AI-1518 (ledger AW4, AW5): the margin 0..100 by which a concealed hero lowers the observer's
    /// Perception in one sight or hearing check, from the session's stealth facts (<see cref="GameSessionOwner.HeroProwlingMargin"/>) and
    /// the observer's own: its conditions, dark sight, the light at the hero, and for sight the line from the observer to the hero (a
    /// blocker hides the prowler, else the line's cover). ARC-AI-1560: the same for a concealed follower as the target, from its own
    /// prowling level and training, light and worn armour (<see cref="GameSessionOwner.NpcProwlingMargin"/>); a follower with no sheet or
    /// build here gives 0. 0 for a target that is not concealed.
    /// </summary>
    private int ProwlingMargin(string observer, string target, CellCoord at, CellCoord there, HostilityCritter? facts, ILineOfFireMap<AreaObjectRecord> map, bool seeing)
    {
        if (observer == Pc || observer == target) return 0;
        SessionNpcProwler? prowler = null;
        if (target == Pc)
        {
            if (!_session.IsConcealed(SessionCombatant.Hero)) return 0;
        }
        else
        {
            if (!_session.IsConcealed(SessionCombatant.Critter(target)) || Build(target) is not { } build || _scene.NpcSheet?.Invoke(target) is not { } sheet) return 0;
            prowler = new(build, sheet.Loaded, sheet.Modifiers);
        }
        var perception = Math.Clamp(facts?.Perception ?? 0, 0, AwarenessRules.MaxPerception);
        var (offsetX, offsetY) = ObserverOffset(observer);
        var trace = seeing ? LineOfFireRules.Trace(at, offsetX, offsetY, there, map, _session.Rules.Combat) : null;
        var state = LifeState(observer);
        var conditions = (facts is { Stunned: true } && Paralysed(observer) ? SkillTargetConditions.Paralyzed : 0)
            | (facts?.Unconscious == true ? SkillTargetConditions.Unconscious : 0)
            | (state?.Conditions.HasFlag(LifeConditions.Stunned) == true ? SkillTargetConditions.Stunned : 0)
            | (facts?.Asleep == true ? SkillTargetConditions.Sleeping : 0);
        var observerFacts = new SkillDifficultyFacts
        {
            TargetConditions = conditions,
            Light = Math.Clamp(CellLight(there), 0, AttackRules.MaxLightLevel),
            ObserverDarkSight = ((Fields(observer)?.Critter?.Flags2 ?? 0) & DarkSight2) != 0,
        };
        if (trace is not null) observerFacts = AwarenessRules.WithProwlerLine(observerFacts, trace);
        return (prowler is null ? _session.HeroProwlingMargin(perception, observerFacts, seeing) : _session.NpcProwlingMargin(prowler, perception, observerFacts, seeing)).Margin;
    }

    private const int OffsetXSlot = 2, OffsetYSlot = 3;

    /// <summary>
    /// ARC-AI-1520 (ledger AW18, VBG9): the observer's pixel offset (object fields 3 and 4) that the original adds to the start of the sight line
    /// and the sound line (ledger RA7, VB3-16, VB3-18): the offset of its map data plus, for an NPC the host walked, the screen shift its
    /// step has left (the pose's shift less the projection of its cell from the T0 cell, <see cref="AreaCritterLights.Stand"/>). The hero and an
    /// NPC this host does not walk stand at their cell centre (0, 0): the hero's step offset and a follower's are not tracked here.
    /// </summary>
    private (int X, int Y) ObserverOffset(string observer)
    {
        if (!_npcs.TryGetValue(observer, out var npc)) return (0, 0);
        int Base(int slot) => npc.Row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number is { } n && n is >= int.MinValue and <= int.MaxValue ? (int)n : 0;
        var (offsetX, offsetY) = (Base(OffsetXSlot), Base(OffsetYSlot));
        if (Movers.PoseOf(observer, _nowMs) is not { } pose) return (offsetX, offsetY);
        var stood = AreaCritterLights.Stand(npc.Origin.X, npc.Origin.Y, offsetX, offsetY, pose);
        return (stood.OffsetX, stood.OffsetY);
    }

    public AwarenessResult Hear(string observer, string target, Loudness loudness)
    {
        if (Tile(observer) is not { } at || Tile(target) is not { } there || _scene.LineOfFire?.Invoke() is not { } map) return new(AwarenessRules.NotPerceived, AwarenessReason.State);
        var facts = Find(observer);
        var stunned = Stunned(observer);
        var sleeping = observer == Pc ? (_session.QueryObjectFlags(Pc).Critter(0) & unchecked((int)SleepingFlag)) != 0 : facts?.Asleep ?? false;
        var hearing = new HearingFacts
        {
            Perception = Math.Clamp(facts?.Perception ?? 0, 0, AwarenessRules.MaxPerception),
            // ARC-AI-1519 (VBG8): hearing refuses critter flag 0x20 only; a paralysed observer still hears.
            Stunned = stunned, Sleeping = sleeping, Unconscious = Down(observer, facts),
            ProwlingMargin = facts is { Unconscious: false } && !Down(observer, facts) && !stunned ? ProwlingMargin(observer, target, at, there, facts, map, false) : 0,
        };
        var (offsetX, offsetY) = ObserverOffset(observer);
        return AwarenessRules.Hear(hearing, at, offsetX, offsetY, there, loudness, new SoundMap(map), _session.Rules.Combat);
    }

    public HostilityDanger Danger(string npc)
    {
        var record = _session.QueryDanger(npc);
        return new(record.State, record.Source is { } source ? Id(source) : null, record.Focus is { } focus ? Id(focus) : null,
            record.HitBy is { } hit ? Id(hit) : null, record.Enemies.Select(Id).ToArray());
    }

    /// <summary>ARC-WORLD-2144 (0x459040, mask OSF_MIND_CONTROLLED): the controller of a mind-controlled NPC, the parent of the spell run that holds it (see <see cref="SessionSpellCaster.AiMindController"/>); null when the NPC is not mind controlled or no cast host is built.</summary>
    public string? MindController(string npc) => _scene.Spells?.Invoke() is { } spells && Fields(npc)?.Object is { } o
        ? spells.AiMindController(npc, unchecked((int)SpellFlagsOf(npc, o.SpellFlags))) : null;

    /// <summary>The critters of the square of half-size <paramref name="halfSize"/> around the object, in the map's scan order.</summary>
    public IReadOnlyList<string> Square(string npc, int halfSize) => Tile(npc) is { } center ? SquareAt(center, halfSize) : [];

    /// <summary>The critters of the square of half-size <paramref name="halfSize"/> around a tile, in the map's scan order (the hero included).</summary>
    public IReadOnlyList<string> SquareAt(CellCoord center, int halfSize)
    {
        bool Inside(CellCoord cell) => Math.Abs((long)cell.X - center.X) <= halfSize && Math.Abs((long)cell.Y - center.Y) <= halfSize;
        var critters = new List<AreaScanCritter>();
        if (Tile(Pc) is { } hero && Inside(hero)) critters.Add(new(SessionCombatant.Hero, hero, 0, null));
        foreach (var other in _scene.Npcs.Concat(_followers.Values))
            if (Tile(other.Entity) is { } cell && Inside(cell) && !Unresolved(other.Entity)) critters.Add(new(SessionCombatant.Critter(other.Entity), cell, 0, other.Row.Key));
        return AreaCombatScan.Order(critters).Select(c => Id(c.Combatant)).ToArray();
    }

    // ---- the event world's questions (forwarded) ----

    public LifeFacts? CritterFacts(SessionMapId map, string entity) => _events.CritterFacts(map, entity);
    public bool ObjectExists(SessionMapId map, string entity) => _events.ObjectExists(map, entity);
    public bool RunScriptCall(SessionScriptCall call) => _events.RunScriptCall(call);
    public ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => (_events as ISessionPartyEventWorld)?.ReactionNpcFacts(map, entity);
    public ReactionPcFacts? ReactionPcFacts() => (_events as ISessionPartyEventWorld)?.ReactionPcFacts();
    public bool LootWindowShowsContainer => (_events as ISessionLootWindowWorld)?.LootWindowShowsContainer == true;
    public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => _round.ScanCombatants(map);
    public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => _round.CombatantFacts(map, entity);

    // ---- helpers ----

    private AreaAiTrace Trace(string npc)
    {
        if (!_traces.TryGetValue(npc, out var trace)) _traces[npc] = trace = new();
        return trace;
    }

    private static string Id(SessionCombatant combatant) => combatant.Entity ?? Pc;

    private SessionCombatant? Combatant(string id) => id == Pc ? SessionCombatant.Hero : SessionCombatant.Critter(id);

    // The session entity of a map row: a row of the shown map, or the T0 row of a companion standing here (ARC-DEV-075: a visitor's is a
    // row of its home map, which the shown map's entity index does not hold).
    private string? EntityOf(AreaObjectKey key) =>
        _scene.Combat.Entity(_scene.RuntimeMap, key) ?? _scene.Companions?.Invoke().FirstOrDefault(c => c.Key == key).Entity;

    private AreaObjectKey? Key(string id) => TryNpc(id, out var npc) ? npc.Row.Key : null;

    // ARC-WORLD-115: an NPC the host walks: one of the map's or a companion the host runs.
    // ARC-DEV-075: the one case left of ARC-DEV-065's skip: a companion run here whose record no reachable map gives (Find is null: its home
    // is the main map's window, which the resolver does not read on demand, MIG1's NOT READY, MIG9). The rules' Get refuses an object the
    // world cannot describe, so such a follower stays out of them; any companion with a record is run like an NPC of the shown map.
    private bool Unresolved(string id) => _followers.ContainsKey(id) && Find(id) is null;
    private bool TryNpc(string id, out AreaAiNpc npc) => _npcs.TryGetValue(id, out npc!) || _followers.TryGetValue(id, out npc!);
    private bool Walks(string id) => _npcs.ContainsKey(id) || _followers.ContainsKey(id);

    // The turn of a walked NPC toward a world cell: a map NPC's mover, a companion's own walk.
    private void FaceNpc(AreaAiNpc npc, CellCoord there, bool keepStep = false)
    {
        if (_followers.ContainsKey(npc.Entity)) _scene.Followers!.Face(npc.Entity, there);
        // ARC-DEV-281: the host's turn cuts the step the NPC is in the middle of (the mover snaps to its cell). The victim's turn of an attack event (0x4A9650 step 9) leaves a step in progress alone
        // (the turn is skipped for that step and the next step faces its own way), so that an aggressive spell, which now runs the attack event on its target, does not snap a running animal; a goal's or an action's turn still cuts.
        else if (!keepStep || Movers.Of(npc.Entity)?.From is null) { Movers.Face(npc.Entity, npc.Row.Key, npc.Origin, there); RecordPlace(npc.Entity); }
    }

    private CombatFields? Fields(string id) => id == Pc ? null : _scene.Combat.Fields(id);

    // ARC-SCRIPT-049: an NPC's faction now: a script's set faction (AreaUseScriptHost.FactionProperty), else its fields'.
    private int? Faction(string id) => Fields(id)?.Npc?.Faction is not { } loaded ? null
        : _session.QueryMapChange(new SessionMapId(_scene.RuntimeMap), MapChangeKey.ForEntity(id, AreaUseScriptHost.FactionProperty)) is { } set ? unchecked((int)set) : loaded;

    // The NPC's build as the map data gives it: enough for readers of its target, life status and life state (no list applied).
    private SessionCritterCombat? RawBuild(string id) => id == Pc ? null : _scene.Combat.Build(_session, id).Combat;

    // ARC-MAGIC-027: the NPC's combat values under its effect list (speed, strength and the rest, resistances), for the readers of its combat values.
    private SessionCritterCombat? Build(string id) => RawBuild(id) is { } combat ? _session.WithNpcEffects(combat) : null;

    // ARC-SCRIPT-1773: the hero's conditions (stunned, blinded) as the session's life state has them, for the sight and hearing checks with the hero as the observer.
    private LifeState? HeroLifeState() => _session.QueryHeroLife()?.Record.State;

    private LifeState? LifeState(string id) => RawBuild(id) is { } build ? _session.QueryLife(build.Target).View?.Record.State : null;

    private LifeStatus? Status(string id) => id == Pc ? _session.QueryHeroLife()?.Status
        : RawBuild(id) is { } build ? _session.QueryLife(build.Target).View?.Status : null;

    private (SessionCombatant Who, SessionLifeTarget Life)? Life(string id) =>
        RawBuild(id) is { } build && Status(id) is { IsDead: false } ? (SessionCombatant.Critter(id), build.Target) : null;

    private SessionNpcAttackValues? Values(string id)
    {
        if (Build(id) is not { } build || Status(id) is not { } status || LifeState(id) is not { } state) return null;
        try { return build.AttackValues(status, state, _session.Rules.Combat, _scene.Combat.WornItems(_session, id)?.Keys); }
        catch (ArgumentException) { return null; }
    }

    private AiParameterRow? Row(string npc) =>
        _scene.Parameters is { } table && Fields(npc)?.Npc is { } fields && table.Lookup(fields) is { Status: AiParameterLookupStatus.Found, Row: { } row } ? row : null;

    /// <summary>
    /// ARC-WORLD-2117: the effective charisma (stat 7 through the getter 0x4B0490) of the NPC's DIRECT leader (0x45DE00, never the chain): the hero's, or a leading NPC's under its effects in its life state. Null without a leader or when the
    /// leader's stats are not readable here (a leader of another map, ARC-WORLD-2130): the setter then reads no extraordinary charisma, as for a leaderless NPC, and the loyalty step does not run.
    /// </summary>
    internal int? LeaderCharisma(string npc) => Leader(npc) is { } leader ? CharismaOf(leader) : null;

    /// <summary>
    /// ARC-WORLD-2135 (0x4AD950, step "another leader"): the effective charisma (0x4B0490) of the DIRECT leader (0x45DE00) of <paramref name="npc"/> when that leader is another critter than the hero, the value the follow check of a join under the hero compares with the
    /// hero's charisma. Null for an NPC the hero leads or that has no leader (no other leader), and when the leading NPC's stats are not readable here (a leader of another map): the session then refuses the join's facts that name none (<see cref="GameSessionFailure.InvalidConsentFacts"/>).
    /// </summary>
    internal int? OtherLeaderCharisma(string npc) => Leader(npc) is { } leader && leader != Pc ? CharismaOf(leader) : null;

    // The effective charisma of a critter this host can read: the hero's, or an NPC's under its effects in its life state (any life status: the getters 0x4B0490 → 0x4B0740 test the object type and the stat only).
    private int? CharismaOf(string critter)
    {
        if (critter == Pc) return _session.QueryHeroStats(new(0))?.Snapshot?.GetEffective(CharacterStat.Charisma);
        return Build(critter) is { } build && LifeState(critter) is { } state ? build.EffectiveStat(CharacterStat.Charisma, state) : null;
    }

    /// <summary>
    /// ARC-WORLD-2117: the unforced leave (0x45DFC0 without the force flag) of a follower from its direct leader, with the leader's charisma read here: an extraordinary-charisma leader (20 or more, 0x4B0EE0) or
    /// <paramref name="mindControlled"/> refuses it (<see cref="GameSessionFailure.LeaveRefused"/>), an NPC leader's included (the session holds no NPC's stats, so the caller of an NPC-led leave is this host).
    /// <paramref name="jilt"/> (ARC-WORLD-2130, 0x4AC380 after a leave that went through) sets the JILTED flag.
    /// </summary>
    internal GameSessionMutationResult LeaveUnforced(string npc, bool mindControlled, bool jilt = false) =>
        _session.LeaveParty(_session.Version, npc, new PartyLeave(Force: false, LeaderCharisma: LeaderCharisma(npc) ?? 0, MindControlled: mindControlled, Jilt: jilt));

    // ARC-WORLD-2127: an NPC's stat-side values as the party rules read them, for the NPC leading a follower: its Persuasion training (0x4C60C0, the NPC's sheet skill), effective charisma, level, alignment
    // (the stat getter's value, AlignmentOf: the effects on that stat are applied) and max followers (stat 15 under its effects). Null when the NPC's stats are not readable here (another map's, no sheet).
    private (PersuasionTraining Persuasion, int Charisma, int Level, int Alignment, int MaxFollowers)? NpcLeaderSide(string leader)
    {
        if (Build(leader) is not { } build || LifeState(leader) is not { } state || AlignmentOf(leader) is not { } alignment || NpcSkill(leader, Skill.Persuasion) is not { } persuasion) return null;
        var training = (PersuasionTraining)(int)persuasion.Training;
        return (training, build.EffectiveStat(CharacterStat.Charisma, state), build.Level, alignment, build.MaxFollowers(state, training));
    }

    /// <summary>
    /// ARC-WORLD-2127: the facts of the wait timer (0x4AA7A0) of a follower of an NPC leader: the DIRECT leader's Persuasion training and effective charisma (0x4B0EE0, 0x4C60C0), the follower's wait index
    /// (field 300) and its mind-control flag. Null when the NPC has no NPC leader, or the leader's stats are not readable here: the caller's facts (the hero's side) then stand.
    /// </summary>
    internal PartyWaitFacts? LeaderWaitFacts(string npc)
    {
        if (Leader(npc) is not { } leader || leader == Pc || Fields(npc)?.Npc is not { } fields || NpcLeaderSide(leader) is not { } side) return null;
        return new(fields.Wait, side.Persuasion, side.Charisma, (SpellFlagsOf(npc, Fields(npc)!.Object.SpellFlags) & MindControlledSpell) != 0);
    }

    /// <summary>
    /// ARC-WORLD-2127: the facts of the unforced unwait (0x4AA8C0 → 0x4AD950 with the NPC leader as the player) of a follower waiting under an NPC leader, read here: the leader's side, the follower's alignment,
    /// level and the thresholds of its AI row, and the mind-control acceptance (a controller equal to the leader accepts both checks; with no controller record the first check accepts a mind-controlled follower whose chain of leaders reaches a player character,
    /// the join's second check none; another controller accepts neither). Null when the NPC has no NPC leader, no AI row or the leader's stats are not readable here.
    /// </summary>
    internal NpcLeaderUnwaitFacts? LeaderUnwaitFacts(string npc)
    {
        if (Leader(npc) is not { } leader || leader == Pc || NpcLeaderSide(leader) is not { } side || Fields(npc) is not { } fields || AlignmentOf(npc) is not { } alignment || Row(npc) is not { } row || Build(npc) is not { } build) return null;
        // ARC-WORLD-2144 (0x4AD950): a controller equal to the leader accepts in both checks; with no controller found the chain walk to a PC accepts the first check only (the handle is cleared at the second); another controller accepts neither.
        var mind = (SpellFlagsOf(npc, fields.Object.SpellFlags) & MindControlledSpell) != 0;
        var (first, second) = MindControlAcceptance(mind, MindController(npc), leader, () => PcLeader(npc) is not null);
        return new(side.Persuasion, side.Alignment, side.Level, side.MaxFollowers, new(row.FollowReaction, row.FollowAlignmentAbove, row.FollowAlignmentBelow, row.JoinLevelGap),
            alignment, build.Level, first, second, side.Charisma);
    }

    /// <summary>
    /// ARC-WORLD-2144 (0x4AD950's mind-control branch): whether a mind-controlled follower is accepted by the first check and by the join's second one (the handle is cleared, so the chain walk 0x45DDA0 cannot see a PC): a controller equal
    /// to the leader accepts both; no controller record accepts the first when a PC leads it up its chain; another controller accepts neither (the ordinary tests follow). Not mind controlled: neither.
    /// </summary>
    internal static (bool First, bool Second) MindControlAcceptance(bool mindControlled, string? controller, string leader, Func<bool> pcChain) =>
        !mindControlled ? (false, false) : controller is null ? (pcChain(), false) : (controller == leader, controller == leader);

    /// <summary>The unforced unwait of a follower waiting under an NPC leader with the facts read here (<see cref="LeaderUnwaitFacts"/>); null when they cannot be read.</summary>
    internal PartyMutationResult? UnwaitUnforced(string npc) =>
        LeaderUnwaitFacts(npc) is { } facts ? _session.UnwaitNpcLed(_session.Version, npc, facts) : null;

    // The setter's facts of the NPC: invulnerable (or nigh-invulnerable), NO_FLEE (the stored flag ORed with the session bit), the
    // direct leader's extraordinary charisma (0x4AABE0 → 0x45DE00 → 0x4B0EE0: a PC or NPC leader, stat 7 at 20 or more). ARC-WORLD-2130: the charisma is read only where the setter reads it (a FLEE
    // that NO_FLEE did not turn into COMBAT_FOCUS, or a SURRENDER, after the invulnerable test), through the facts' lazy read.
    private DangerSetterFacts SetterFacts(string npc)
    {
        var critter = Find(npc);
        return new(Invulnerable: critter is { Invulnerable: true } or { NighInvulnerable: true }, NoFlee: critter?.NoFlee ?? false,
            FloatsLine: AiBarks is not null && Active(npc), ReadLeaderCharisma: () => LeaderCharisma(npc) >= FollowConsentRules.ExtraordinaryCharisma);
    }

    private (ReactionNpcFacts Npc, ReactionPcFacts Pc)? ReactionFacts(string npc)
    {
        if (Fields(npc) is not { Critter: { } critter, Npc: { } fields } || Build(npc) is not { } build) return null;
        if (_session.QueryHeroReactionFacts(0, Faction(npc) ?? fields.Faction) is not { } pc) return null;
        return (new ReactionNpcFacts(fields.ReactionBase, build.Race, (fields.Flags & AloofNpc) != 0, build.Monstrous), pc);
    }

    // The NPC's combat-mode facts (the toggle's refusals read them), or null when it has none.
    private SessionCombatModeContext? NpcCombatMode(string npc)
    {
        if (Build(npc) is not { } build || Fields(npc) is not { } fields || LifeState(npc) is not { } state) return null;
        return new SessionCombatModeContext { Facts = AreaCombatWorld.Facts(build, fields, state) };
    }

    private void HeroIntoCombatMode()
    {
        if (_session.QueryHero() is null || _session.QueryCombatMode(SessionCombatant.Hero)) return;
        _session.SetCombatMode(_session.Version, SessionCombatant.Hero, true, _scene.HeroCombatMode?.Invoke() ?? SessionCombatModeContext.RealTime);
    }

    private CellCoord? CompanionCell(string id)
    {
        foreach (var (entity, _, cell) in _scene.Companions?.Invoke() ?? [])
            if (entity == id) return cell;
        return null;
    }

    /// <summary>
    /// ARC-DEV-144 (MIG13 S9c, GT94): the critters standing on <paramref name="cell"/> for the burning objects' damage: the hero, the shown map's NPCs that are still on it and the companions here, dead ones included (the damage path itself
    /// ignores a dead critter, 0x4B4784, but it counts as reached for the round gate). Items lying on the tile are not listed (NOT READY: the map's objects take no damage in the viewer).
    /// </summary>
    public IReadOnlyList<string> CrittersOn(CellCoord cell)
    {
        var found = new List<string>();
        if (Tile(Pc) == cell) found.Add(Pc);
        foreach (var npc in _scene.Npcs)
            if (!Gone(npc.Entity) && Tile(npc.Entity) == cell && !found.Contains(npc.Entity)) found.Add(npc.Entity);
        foreach (var companion in _scene.Companions?.Invoke() ?? [])
            if (companion.Cell == cell && !found.Contains(companion.Entity)) found.Add(companion.Entity);
        return found;
    }

    // A living critter (the hero, a companion, an NPC of the map) other than the mover on the cell.
    private bool LivingCritterOn(CellCoord cell, string mover)
    {
        if (mover != Pc && Tile(Pc) == cell && !(Status(Pc)?.IsDead ?? true)) return true;
        foreach (var npc in _scene.Npcs)
            if (npc.Entity != mover && Tile(npc.Entity) == cell && !Gone(npc.Entity) && !(Status(npc.Entity)?.IsDead ?? true)) return true;
        return _scene.Companions?.Invoke().Any(c => c.Cell == cell && c.Entity != mover) == true;
    }

    // The planner's overlay for the mover: every moved T0 record, and the live cells of the hero, the companions and the other movers
    // (chart cells); the target's own cell does not block a path into it.
    private AreaOccupancy Occupancy(string mover, string? target)
    {
        var moved = new List<AreaObjectKey>(Movers.Moved);
        var held = new List<CellCoord>();
        if (mover != Pc && target != Pc && Tile(Pc) is { } hero) held.Add(ToChart(hero));
        foreach (var other in Movers.All)
            if (other.Entity != mover && other.Entity != target) held.AddRange(new[] { other.Cell }.Concat(other.From is { } f ? [f] : []).Select(ToChart));
        foreach (var (entity, key, cell) in _scene.Companions?.Invoke() ?? [])
        {
            moved.Add(key);
            if (entity != mover && entity != target) held.Add(ToChart(cell));
        }
        if (target is not null && Key(target) is { } targetKey) moved.Add(targetKey);
        // ARC-WORLD-1436: an NPC switched on from the map data's off flag stands where the data has it and holds that cell (the T0 evidence lists none for it).
        foreach (var (_, entity, world) in _scene.SwitchedOn?.Invoke() ?? [])
            if (entity != mover && entity != target && _npcs.ContainsKey(entity) && !Movers.IsMoved(entity) && !Gone(entity)) held.Add(ToChart(world));
        var occupancy = new AreaOccupancy(moved, held);
        return _scene.Removed?.Invoke() is { Count: > 0 } removed ? occupancy.WithRemoved(removed) : occupancy;
    }

    // The line of fire from the object's tile to the target's, and the target's row (null for the hero).
    private LineOfFireTrace<AreaObjectRecord>? Line(string id, string target, out AreaObjectRecord? targetRow)
    {
        targetRow = TryNpc(target, out var npc) ? npc.Row : null;
        if (Tile(id) is not { } from || Tile(target) is not { } to || _scene.LineOfFire?.Invoke() is not { } map) return null;
        return LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat);
    }

    private CellCoord ToChart(CellCoord world) => _scene.Chart.ToChart(new AreaStepCell(world.X, world.Y));

    private CellCoord ToWorld(CellCoord chart)
    {
        var world = _scene.Chart.ToWorld(chart);
        return new(checked((int)world.X), checked((int)world.Y));
    }

    // The line of fire as a hearing map: no tile is soundproof here (not read yet).
    private sealed class SoundMap(ILineOfFireMap<AreaObjectRecord> map) : IAwarenessMap<AreaObjectRecord>
    {
        public CellCoord? Step(CellCoord from, int rotation) => map.Step(from, rotation);
        public bool Contains(CellCoord tile) => map.Contains(tile);
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => map.Edge(tile, edge);
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => map.Objects(tile);
        public bool Soundproof(CellCoord tile) => false;
    }
}

/// <summary>ARC-WORLD-092: what the AI overlay shows of one NPC: its last danger update, setter call, perform, run step and reason,
/// hit and attack event.</summary>
public sealed class AreaAiTrace
{
    public string? Danger { get; set; }
    public string? Setter { get; set; }
    public string? Perform { get; set; }
    public string? Step { get; set; }
    public AiExecutorReason Reason { get; set; }
    public string? Hit { get; set; }
    public string? Event { get; set; }
    /// <summary>ARC-DEV-292: its last immediate pass after a hit (<see cref="AreaAiHost.Struck"/>): the session's failure, the gate's stop and whether perform ran; null when none ran.</summary>
    public string? Struck { get; set; }
    /// <summary>ARC-AI-1515: what the hero's hit on its follower did (the attack event's follower branch): the reaction change and the follow check's outcome with the line range named; null when none.</summary>
    public string? FollowerHit { get; set; }
    public string? Reaction { get; set; }
    /// <summary>ARC-WORLD-099: the end-combat scripts (point 16) its subturn ends asked.</summary>
    public int EndCombatScripts { get; set; }
    /// <summary>ARC-WORLD-102: its last spell choice and its last cast call (perform's cast, a casting goal's start or release).</summary>
    public string? Spell { get; set; }
    public string? Cast { get; set; }
    /// <summary>ARC-WORLD-108: its last heal choice and item use.</summary>
    public string? Item { get; set; }
    /// <summary>ARC-WORLD-109: the last skill attempt.</summary>
    public string? Skill { get; set; }
    /// <summary>ARC-WORLD-113: the last throw (or the attack starter's unwield).</summary>
    public string? Throw { get; set; }
    /// <summary>ARC-DEV-364: the grenade step's last answer (0x4AC910): the stop, the grenade and a friend in the blast.</summary>
    public string? Grenade { get; set; }
    /// <summary>ARC-DEV-117: its last fold step, waypoint or standpoint: what the rules decided and what the host did.</summary>
    public string? Route { get; set; }
}
