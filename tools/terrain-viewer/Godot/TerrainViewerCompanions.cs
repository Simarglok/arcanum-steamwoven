using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-016: Virgil follows the PC in crash-site-area. The measured companions of the shown map (the roster in
// AreaCompanionRoster) are moving actors of an AreaCompanionGroup: each walks on its own grid session over the map's
// T0 step evidence, and while the session party says it follows the PC it is driven after the PC to the same frame
// boundary (start beyond 4 cells, stop within 2, stand on the PC beyond 30 then step off). The PC and the companions
// block each other where they stand; a companion's T0 record no longer blocks its serialized cell. The group is
// rebuilt from the session whenever the PC's session is (spawn, reset, reload, transfer, quick load); a companion's
// cell is written to the session when the PC leaves the map and on F7, like the PC's. The body is the NPC's T0 frame
// moved with the actor, drawn with its walk or run cycle and facing since ARC-WORLD-111 (TerrainViewerBodyMoves.cs).
// ARC-WORLD-017: a following companion goes with the PC through the cave entrance and exit and is drawn, lit, picked and
// talked to where it stands there; on the exterior its T0 body is not in the world while it is away.
public partial class TerrainViewerApp
{
    private AreaCompanionGroup? _areaCompanions;
    private string? _companionNotice;
    // ARC-WORLD-017: the roster companions' home T0 data (exterior), set with the area session.
    private IReadOnlyList<AreaCompanionTemplate> _companionTemplates = [];

    /// <summary>Rebuilds the companions of the shown map from the session (placement and standing), at the PC's time.</summary>
    private void BindAreaCompanions()
    {
        // ARC-WORLD-051: the portals are bound with the same session and grid.
        BindAreaPortals();
        _areaCompanions = null; _unreadVisitors = [];
        CancelPendingOrder("the companions were rebuilt");
        RefreshFollowerPortraits();
        if (_areaSession is not { } session || _conversationHost is not { PartyWired: true } || _worldActor.Session is not AreaGridSession grid) return;
        // ARC-WORLD-037: in map mode only the NPCs the party has touched move as companions (following, waiting, or recorded
        // elsewhere than their T0 cell); every other speaker stays its T0 body until it joins.
        // ARC-WORLD-049: a dead NPC is a corpse where it lies, never a moving companion.
        // ARC-DEV-079 (MIG4): a companion standing here whose body the shown scene does not draw is read from its home map's data.
        if (_readyMapName is not null) RefreshVisitorBodies(session, grid);
        var templates = _readyMapName is null ? _companionTemplates
            : _companionTemplates.Where(t => CompanionIncluded(session, t) && !SpeakerDead(t.Speaker)).ToArray();
        _areaCompanions = AreaCompanionGroup.Create(templates, grid.Snapshot, grid.Chart, session, new(CurrentAreaMap), grid.State.Time,
            // ARC-UI-039: the companions keep the human timing, not the hero body's.
            grid.Timing is null ? null : GaitTiming.HumanFixed);
        // ARC-WORLD-065: the companions step over what the session destroyed or switched off, as the PC does.
        _areaCompanions.Removed = AreaRemovedObjects;
        // ARC-WORLD-075: outside combat mode they walk through living critters; the dead are not checked on a goal or a segment end.
        _areaCompanions.Dead = AreaCritterDeadNow;
        // ARC-WORLD-115: a companion the AI host holds while it fights walks only the host's steps.
        _areaCompanions.HeldByAi = entity => _aiHost?.Holds(entity) == true;
        _areaCompanions.Extra = AiMoversOverlay;
        if (_readyMapName is not null) AdoptVisitorFrames(_areaCompanions);
        foreach (var skipped in _areaCompanions.Skipped) GD.Print("ARC-WORLD-016 companion skipped " + skipped);
        // ARC-WORLD-047: a waiting follower whose map data this run can read now gets the level steps it missed.
        ApplyMissedAreaLevels();
        RefreshFollowerPortraits();
        GD.Print("ARC-WORLD-016 companions bound " + System.Text.Json.JsonSerializer.Serialize(_areaCompanions.Companions.Select(c => new {
            c.Speaker.ScriptId, Standing = c.Standing.ToString(), Cell = grid.Chart.ToWorld(c.Grid.State.CommittedCell), Visiting = c.Template.Home.RuntimeMap != CurrentAreaMap }).ToArray())
            + " map=" + CurrentAreaMap + " absent=" + _areaCompanions.Absent.Count);
    }

    private void UnbindAreaCompanions()
    {
        _areaCompanions = null; _companionNotice = null; _unreadVisitors = []; _visitorFrames.Clear(); if (_pendingOrder is not null) CloseTargetMode(Arcanum.World.InterfaceMode.FollowerOrder);
        RefreshFollowerPortraits();
    }

    // ARC-DEV-079 (MIG4): companions recorded on the shown map whose body their home map could not give (the entity and why), named on the HUD.
    private IReadOnlyList<string> _unreadVisitors = [];

    // ARC-WORLD-037: companions on any map (--area-map). Their follow facts come from the map's own data (AreaCompanionData:
    // the AI parameter table and the effect tables); a companion that follows the PC to another map goes with it. ARC-DEV-079 (MIG4):
    // its T0 body (commands, visuals and frames) is read from its home map's data (AreaHomeDirectory, by identity: the home map the session's
    // away change names and the stored cell it recorded), on any map and after a quick load or a fresh run; the viewer keeps no body of its own.
    private (string Module, AreaCompanionData? Data, string Detail)? _companionData;
    private readonly Dictionary<AreaObjectKey, AreaCompanionFacts?> _companionFacts = new();
    // The bodies of the shown map's own speakers (its templates), and the frames of the visitors' bodies this scene adds.
    private IReadOnlyList<AreaCompanionTemplate> _ownCompanionTemplates = [];
    private readonly Dictionary<string, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>> _visitorFrames = new(StringComparer.Ordinal);
    // The home maps' data, read on demand and kept (derived: a fresh run or a quick load reads again).
    private AreaHomeDirectory? _homes;

    private AreaCompanionData? MapCompanionData(string module)
    {
        if (_companionData is { } cached && cached.Module == module) return cached.Data;
        var (status, data, detail) = AreaCompanionData.Open(module);
        GD.Print($"ARC-WORLD-037 companion data status={status} detail={detail}");
        _companionData = (module, data, $"{status}: {detail}"); _companionFacts.Clear();
        return data;
    }

    private AreaCompanionFacts? MapCompanionFacts(AreaSpeaker speaker)
    {
        if (_companionData?.Data is not { } data) return null;
        if (_companionFacts.TryGetValue(speaker.Row.Key, out var known)) return known;
        var facts = data.Facts(speaker, out var refusal);
        if (refusal is not null) GD.Print($"ARC-WORLD-037 companion facts script={speaker.ScriptId} refused: {refusal}");
        _companionFacts[speaker.Row.Key] = facts;
        return facts;
    }

    /// <summary>The map's own companion templates (every drawn speaker with follow facts); the companions visiting it come from their home maps
    /// (<see cref="RefreshVisitorBodies"/>).</summary>
    private IReadOnlyList<AreaCompanionTemplate> MapCompanionTemplates(TerrainViewerArguments arguments, AreaLoadResult area)
    {
        _ownCompanionTemplates = [];
        if (MapCompanionData(arguments.ModulePath) is null) return [];
        return _ownCompanionTemplates = AreaCompanionTemplate.From(AreaConversationHost.FindSpeakers(area.Snapshot), area.Plan, area.Snapshot.Visuals, area.Plan.Chart,
            new(area.Snapshot.RuntimeMap), MapCompanionFacts);
    }

    private static bool CompanionIncluded(GameSessionOwner session, AreaCompanionTemplate template)
    {
        if (session.QueryCompanion(template.Entity).Standing != Arcanum.Session.CompanionStanding.Free) return true;
        var where = AreaCompanionPlacement.Where(session, template.Home, template.Entity);
        return where.Map != template.Home || where.Placement is not null || where.Error is not null;
    }

    /// <summary>
    /// ARC-DEV-079 (MIG4): the companions standing on the shown map whose body its own scene does not draw (<see cref="AreaVisitors.Here"/>)
    /// get their body, and the frames it draws, from their home map's data through <see cref="AreaHomeDirectory.Body"/> (the home map the
    /// session's away change names, the stored cell it recorded; a map the viewer holds answers from memory, any other is read once). A body
    /// that cannot be read is named on the HUD and left out, never guessed. The templates the shown map's rules see are its own plus these.
    /// </summary>
    private void RefreshVisitorBodies(GameSessionOwner session, AreaGridSession grid)
    {
        _visitorFrames.Clear(); _unreadVisitors = [];
        var visitors = AreaVisitors.Here(session, new(CurrentAreaMap), _ownCompanionTemplates,
            world => grid.Chart.Contains(grid.Chart.ToChart(new(world.X, world.Y))));
        var bodies = new List<AreaCompanionTemplate>(); var unread = new List<string>();
        foreach (var visitor in visitors)
        {
            var (template, frames, detail) = _homes?.Body(visitor.Entity, visitor.HomeMap, visitor.HomeCell)
                ?? (null, new Dictionary<AreaFrameKey, DecodedAreaFrame>(), "no home map reader");
            if (template is null)
            {
                unread.Add($"{visitor.Entity} ({detail})");
                GD.Print($"ARC-DEV-079 visitor body unread entity={visitor.Entity} home={visitor.HomeMap} stored={visitor.HomeCell} reason={detail}");
                continue;
            }
            bodies.Add(template); _visitorFrames[visitor.Entity] = frames;
            GD.Print($"ARC-DEV-079 visitor body entity={visitor.Entity} home={visitor.HomeMap} script={template.Speaker.ScriptId} frames={frames.Count}");
        }
        _unreadVisitors = unread;
        IReadOnlyList<AreaCompanionTemplate> next = [.. _ownCompanionTemplates, .. bodies];
        if (next.SequenceEqual(_companionTemplates, ReferenceEqualityComparer.Instance)) return;
        _companionTemplates = next; _hoverIndex = null; _hoverIndexArea = null;
    }

    /// <summary>Adds the frames of the visiting companions' bodies (read from their home maps) to the shown map's frame repository.</summary>
    private void AdoptVisitorFrames(AreaCompanionGroup group)
    {
        var frames = (AreaFrameOverlay)AreaFrames();
        foreach (var companion in group.Companions)
        {
            var template = companion.Template;
            if (!_visitorFrames.TryGetValue(template.Entity, out var kept)) continue;
            foreach (var command in template.Commands)
                if (kept.TryGetValue(command.Frame, out var frame)) frames.Add(command.Frame, frame);
                else GD.Print($"ARC-WORLD-037 companion script={template.Speaker.ScriptId} frame {command.Frame} not read; not drawn here");
        }
    }

    /// <summary>
    /// ARC-DEV-073 (MIG10): what the session's move list asks the viewer for. A follower that is unconscious (0x45D800: no fatigue left,
    /// not undead) is ordered to wait and stays behind, with its wait timer from the roster facts; NPC-led followers of followers are
    /// not modelled (no source of leaders), so none travel below a follower.
    /// </summary>
    private SessionTransferFacts TransferFacts()
    {
        var session = _areaSession; var host = _combatHost;
        return new SessionTransferFacts
        {
            // ARC-DEV-076 (MIG3): by identity, wherever the companion stands (no home-map fallback).
            NoFatigue = entity => session is not null && host is not null && _areaCompanions?.Companions.Any(c => c.Entity == entity) == true
                && AreaLife.Unconscious(session, host, entity) == true,
            WaitFacts = entity => session is not null && _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity) is { } companion
                ? AreaCompanionRoster.WaitFacts(companion.Facts, session.QueryHeroPartySide()) : null,
        };
    }

    /// <summary>The companions that follow the PC off this map, with their facing (recorded before the PC leaves), and the session's
    /// move list for the change of place (ARC-DEV-073).</summary>
    private (IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)> Companions, SessionMoveList? MoveList) LeavingCompanions()
    {
        if (_areaCompanions is not { } group || _areaSession is not { } session) return ([], null);
        RecordAreaCompanions("leaving the map");
        return group.Leaving(session, TransferFacts());
    }

    /// <summary>ARC-DEV-073 (FM6, another map only): the followers that travel with the PC leave combat mode, as the source's move does
    /// (0x4B3F20); the one rule of the toggle applies (a stunned, stoned or paralysed follower stays in it). Run on the map being left.</summary>
    private void LeaveFollowersCombatMode(SessionMoveList? moveList)
    {
        if (moveList is null || _aiHost is not { } host || _areaSession is not { } session) return;
        foreach (var entity in moveList.Travelling)
            if (session.QueryCombatMode(SessionCombatant.Critter(entity))) host.LeaveCombatMode(entity);
    }

    /// <summary>Writes the companions' cells to the session (quick save, leaving the map). False when the session refused.</summary>
    private bool RecordAreaCompanions(string why)
    {
        if (_areaCompanions is not { } group || _areaSession is not { } session) return true;
        var failure = group.Record(session);
        GD.Print($"ARC-WORLD-016 companions recorded why={why} failure={failure}");
        if (failure == GameSessionFailure.None) return true;
        _companionNotice = $"companion placement not saved ({failure})";
        return false;
    }

    /// <summary>One frame after the PC's advance to <paramref name="frontier"/>: every following companion drives to it.</summary>
    private void DriveAreaCompanions(IGridSession session, SimulationTime frontier)
    {
        if (_areaCompanions is not { } group || _areaSession is not { } owner || session is not AreaGridSession pc) return;
        // ARC-COMBAT-020: in a turn-based round a companion moves only on its own subturn, by its AI (ARC-WORLD-115: the steps the AI
        // host gives the companion it holds); the others stand.
        var round = owner.QueryCombatRound() is not null;
        var entered = new List<(AreaCompanion Companion, Arcanum.Data.Traversal.AreaStepCell Cell)>();
        foreach (var (companion, advance) in group.Drive(owner, pc.State, frontier, heldOnly: round))
        {
            // The steps it started into a tile, as the player's (NoteMapEntries), for its tile scripts after the drive.
            if (_mapTransfers is { } transfers)
                foreach (var evidence in advance.AreaEvidence)
                    if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) == Arcanum.Data.Traversal.CardinalTraversalDisposition.Open)
                        entered.Add((companion, transfers.Map.Chart.ToWorld(evidence.Key.To)));
        foreach (var item in advance.Movement.Events)
            if (item.Kind is MovementEventKind.StepStarted or MovementEventKind.StepRejected or MovementEventKind.CenterArrived)
                GD.Print($"ARC-WORLD-016 companion script={companion.Speaker.ScriptId} event={item.Kind} {item.Key.From.X},{item.Key.From.Y}->{item.Key.To.X},{item.Key.To.Y} reason={item.Reason}"
                    + (advance.AreaEvidence.LastOrDefault(e => e.Key == item.Key) is { } evidence ? $" step={evidence.Live?.ReasonCode ?? evidence.Assessment.ReasonCode}" : ""));
        }
        foreach (var companion in group.Companions.Where(c => c.LastUpdate.Action == FollowAction.Jump && c.Grid.State.Time == frontier))
            GD.Print($"ARC-WORLD-016 companion script={companion.Speaker.ScriptId} jumped onto the PC cell {pc.State.CommittedCell}");
        if (!round) RunCompanionLoyalty(group, owner, pc, frontier);
        RunCompanionTileScripts(entered);
        RefreshFollowerPortraits();
    }

    // ARC-SCRIPT-046: the original runs a tile's script for an NPC that moved onto it too (ledger TR3), with the NPC as the triggerer: each
    // step a following companion of the shown map (not a visitor from another map) started into a tile with a script runs it, in step
    // order. A run that asked for a conversation opens it with the dialog's NPC; once one is open the remaining steps of this drive run
    // nothing. Runs that did nothing are not logged.
    private void RunCompanionTileScripts(List<(AreaCompanion Companion, Arcanum.Data.Traversal.AreaStepCell Cell)> entered)
    {
        if (_mapTransfers is not { } transfers) return;
        foreach (var (companion, cell) in entered)
        {
            if (ConversationOpen) break;
            if (companion.Template.Home.RuntimeMap != CurrentAreaMap || transfers.EnterByCritter(cell, companion.Template.Entity) is not { } ran) continue;
            if (ran.Outcome != AreaScriptedUseOutcome.NoTeleport || ran.Dialog is not null || ran.Lines.Count > 0)
                GD.Print("ARC-SCRIPT-046 companion-tile " + System.Text.Json.JsonSerializer.Serialize(new { Script = companion.Speaker.ScriptId, Cell = cell,
                    Outcome = ran.Outcome.ToString(), ran.Detail, Dialog = ran.Dialog?.Script, Node = ran.Dialog?.Node }));
            if (ran.Dialog is { } dialog) BeginScriptedConversation(dialog);
        }
    }

    // ARC-WORLD-045: each following companion's AI tick runs the loyalty step (the original's non-combat pass, so none while a
    // turn-based round runs) with the shown area's conversation state as the PC side; a step that changed the session or was
    // refused is logged, and its line range shown on the HUD.
    private void RunCompanionLoyalty(AreaCompanionGroup group, GameSessionOwner owner, AreaGridSession pc, SimulationTime now)
    {
        if (_conversationHost is not { } host || owner.QueryCombatRound() is not null) return;
        foreach (var (companion, result) in group.Loyalty(owner, pc.State, now, c => AreaCompanionLoyalty.Facts(owner, c, host.State)))
        {
            if (result.Disposition == GameSessionDisposition.NoChange) continue;
            GD.Print(AreaCompanionLoyalty.Log(companion, result));
            if (result.Step is { } step && AreaCompanionLoyalty.Shown(step) is { } shown) _companionNotice = $"companion script {companion.Speaker.ScriptId}: {shown}";
            else if (result.Disposition == GameSessionDisposition.Rejected) _companionNotice = $"companion script {companion.Speaker.ScriptId}: loyalty step refused ({result.Failure})";
        }
    }

    // ARC-WORLD-045: a wait timer that came due made its companion free and jilted; the HUD names it.
    private void ReportCompanionWaits(IReadOnlyList<SessionWaitFiring> waits)
    {
        foreach (var firing in waits)
        {
            GD.Print(AreaCompanionLoyalty.Log(firing));
            _companionNotice = $"wait ended for {firing.Entity}: jilted" + (firing.Reaction is { } reaction ? $" (reaction {reaction.Delta:+0;-0;0})" : " (no reaction facts)");
        }
        if (waits.Count > 0 && _areaSession is { } session) _areaCompanions?.Refresh(session);
    }

    /// <summary>The companions' bodies as they stand now, for the shown plan only, with their gait and facing (ARC-WORLD-111).</summary>
    private IReadOnlyList<AreaDrawCommand>? AreaCompanionCommands() =>
        _areaCompanions is { } group && _readyArea is { } area && group.Map.RuntimeMap == area.Snapshot.RuntimeMap ? AnimateCompanions(group, group.Commands()) : null;

    /// <summary>ARC-WORLD-017: whether a dialog NPC is in the shown map's world: a companion standing here, or (on the
    /// exterior, the speakers' map) any other speaker that is not a companion away on another map.</summary>
    // ARC-DEV-081: a companion carried here (listed by the one host after the map's own) is present only while the companion group holds it:
    // its template's cell is its home map's, so a speaker the group dropped must not stand at it on this map.
    private bool SpeakerPresent(AreaSpeaker speaker) => !SpeakerDead(speaker) && (_areaCompanions is { } group
        ? group.Of(speaker) is not null || (!InCave && !group.Absent.Contains(speaker.Row.Key) && _conversationHost?.IsCarried(speaker) != true)
        : !InCave && _conversationHost?.IsCarried(speaker) != true);

    /// <summary>ARC-COMBAT-005: a dead speaker (its map data or the session) is looted, not talked to, as the original's click on a
    /// corpse that was not spoken with by spell.</summary>
    private bool SpeakerDead(AreaSpeaker speaker) => InventoryActive
        && _inventoryHost!.Target(_areaSession!, CurrentAreaMap, speaker.Row.Key) is { Kind: AreaInventoryTargetKind.DeadNpc };

    /// <summary>Where a speaker stands now: a companion's live cell, else its T0 cell.</summary>
    private Func<AreaSpeaker, (long X, long Y)>? SpeakerCell => _areaCompanions is { } group ? group.WorldCell : null;

    // ARC-WORLD-067: the PC side of the party rules for the HUD, read once per session revision.
    private (GameSessionOwner Session, long Revision, string Label)? _partySideLabel;

    private string PartySideLabel()
    {
        if (_areaSession is not { } session) return AreaCompanionRoster.PartySideLabel(null);
        if (_partySideLabel is { } cached && ReferenceEquals(cached.Session, session) && cached.Revision == session.Version.Revision) return cached.Label;
        var label = AreaCompanionRoster.PartySideLabel(session.QueryHeroPartySide());
        _partySideLabel = (session, session.Version.Revision, label);
        return label;
    }

    private string CompanionStatus()
    {
        if (_conversationHost is not { PartyWired: true }) return "";
        var text = (_areaCompanions is { } group ? group.Status() : "companions: none") + " | " + PartySideLabel();
        var unbodied = (_unreadVisitors.Count == 0 ? "" : " | companion body not read from its home map: " + string.Join(", ", _unreadVisitors))
            + (_pendingOrder is not { } pending ? "" : pending.Type == CompanionOrderType.Attack
                ? " | attack order: click an object (any key cancels)" : " | walk order: click a tile (any key cancels)");
        return " | " + text + unbodied + (_companionNotice is { } notice ? " | " + notice : "");
    }

    // Native observation (ARC-WORLD-016) on the exterior: Virgil joins through his own dialog and the session party
    // port, then follows the PC to within 2 cells through the frames, is drawn on his new cell, waits when told to, and
    // his cell is session state. Frames run on simulated focus and test time, as the transition smoke does. Runs last
    // of the area stages, so the others see Virgil at T0.
    private async Task RunAreaFollowSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            // A failed check prints its detail, then throws a closed code the smoke runner reports.
            static void Require(bool value, string why)
            {
                if (value) return;
                GD.Print("ARC-WORLD-016 follow smoke FAIL: " + why);
                throw new InvalidOperationException("AREA_FOLLOW_SMOKE_FAILED");
            }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            Require(_areaTransitions?.CurrentMap == 1 && !InCave, "the exterior is published");
            var map = _areaTransitions!.Current;
            var host = _conversationHost!;
            var speaker = host.Speakers.Single(s => AreaCompanionRoster.For(s) is not null);
            var origin = map.Chart.ToChart(new(speaker.WorldX, speaker.WorldY));
            // Stand next to Virgil and talk: the first Direct replies walk his join (JO through the session party).
            AreaSmokePlace(map, AreaSmokeStandable(map, origin, 2));
            var companion = _areaCompanions!.Companions.Single();
            Require(companion.Standing == CompanionStanding.Free && companion.Grid.State.CommittedCell == origin, "Virgil starts free on his T0 cell");
            BeginConversation(speaker);
            var conversation = _conversationPanel!.Conversation ?? throw new InvalidOperationException("ARC-WORLD-016 follow smoke: no conversation");
            for (var i = 0; i < 64 && conversation.Phase == Arcanum.Scripting.Dialog.DialogConversationPhase.AwaitingSelection; i++)
            {
                var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == Arcanum.Scripting.Dialog.DialogOptionTransition.Direct)
                    ?? conversation.Options.First(o => o.Block is null);
                conversation.Select(option.Token);
            }
            EndConversation();
            Require(_areaSession!.QueryCompanion(companion.Entity).Standing == CompanionStanding.Following, "the dialog joined him through the session: " + conversation.Phase);

            // ARC-WORLD-045: a negative reaction change by his leader arms his loyalty marks (his join dialog's AL may have armed the
            // alignment marks too); standing near the PC without a follow goal, his next AI tick runs the loyalty step, which clears
            // them all (his AI row warns at no reaction above −980).
            const CompanionLoyalty reactionMarks = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestReaction;
            var state = host.State; var reactionBefore = state.Reaction(speaker.Index);
            var dropped = _areaSession.AdjustReaction(_areaSession.Version, companion.Entity, state.NpcFacts(), state.PcFacts, -5);
            Require(dropped.Disposition == GameSessionDisposition.Changed && (_areaSession.QueryCompanion(companion.Entity).Loyalty & reactionMarks) == reactionMarks,
                $"a leader's reaction drop arms him: {dropped.Disposition} {dropped.Failure} {_areaSession.QueryCompanion(companion.Entity)}");
            for (var i = 0; i < 8 && _areaSession.QueryCompanion(companion.Entity).Loyalty != CompanionLoyalty.None; i++) AreaSmokeFrame();
            Require(_areaSession.QueryCompanion(companion.Entity) is { Loyalty: CompanionLoyalty.None, Standing: CompanionStanding.Following } && !companion.FollowGoal,
                "the loyalty step ran on his AI tick and cleared his marks: " + _areaSession.QueryCompanion(companion.Entity));

            // The PC 12 cells away: frames bring Virgil within 2, settled, and draw him there.
            var far = AreaSmokeStandable(map, origin with { X = origin.X + 12 }, 3);
            AreaSmokePlace(map, far);
            companion = _areaCompanions!.Companions.Single();
            // The placement frames already run: rebound following from his T0 cell, he has started toward the PC.
            Require(companion.Standing == CompanionStanding.Following && companion.Grid.State.CommittedCell.X >= origin.X, "rebound following: " + group());
            var pc = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            for (var i = 0; i < 400 && (companion.Grid.State.Phase != MovementPhase.Settled
                || FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc) > FollowDistanceRules.Range); i++) AreaSmokeFrame();
            var cell = companion.Grid.State.CommittedCell;
            var plans = companion.Follow.Route.PlannedSegments;
            Require(FollowDistanceRules.Distance(cell, pc) <= FollowDistanceRules.Range && companion.Grid.State.Phase == MovementPhase.Settled,
                $"Virgil followed within 2: {cell} vs PC {pc}; {group()}");
            // ARC-WORLD-045 (decision D5): his follow goal lives on after he arrived, so the loyalty step does not run; the reaction
            // goes back to what it was before the check.
            Require(companion.FollowGoal && _areaSession.AdjustReaction(_areaSession.Version, companion.Entity, state.NpcFacts(), state.PcFacts, -5).Disposition
                == GameSessionDisposition.Changed, "armed again while his follow goal lives");
            for (var i = 0; i < 8; i++) AreaSmokeFrame();
            Require(_areaSession.QueryCompanion(companion.Entity).Loyalty == reactionMarks,
                "no loyalty step while his follow goal lives: " + _areaSession.QueryCompanion(companion.Entity));
            Require(_areaSession.SetReaction(_areaSession.Version, companion.Entity, state.NpcFacts(), state.PcFacts, reactionBefore).Disposition != GameSessionDisposition.Rejected
                && state.Reaction(speaker.Index) == reactionBefore, "his reaction restored");
            // Frame the PC (an earlier stage may have left the camera elsewhere), then observe the drawn submission.
            _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            PublishWorldSelection();
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            var world = map.Chart.ToWorld(cell);
            Require(_canvas!.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == world.X && c.MapY == world.Y)
                && !_canvas.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == speaker.WorldX && c.MapY == speaker.WorldY),
                "his body is drawn on his new cell only: expected " + world + " drawn " + string.Join(",", _canvas.AreaVisibleCommands
                    .Where(c => c.Identity == companion.Key).Select(c => $"({c.MapX},{c.MapY}) dest {c.Destination.X},{c.Destination.Y}"))
                    + $" moving {AreaCompanionCommands()?.Count ?? -1} map {_areaCompanions?.Map.RuntimeMap} ready {_readyArea?.Snapshot.RuntimeMap} visible {_canvas.AreaVisibleCount}");
            // He is picked where he is drawn: the hover names him and a left click there talks (his follower entry).
            var drawn = _canvas.AreaVisibleCommands.First(c => c.Identity == companion.Key);
            var point = PickedScreenPoint(drawn);
            Require(point is not null, "the pick finds him on an opaque pixel of his drawn body");
            AreaSmokeClick(point!.Value);
            AreaSmokeFramesUntilConversation(); Require(ConversationOpen, "a click on his drawn body talks: " + _conversationNotice);
            EndConversation();
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_FOLLOW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-016 follow smoke: capture failed");
            }
            // His cell is session state, and talking now uses where he stands.
            Require(RecordAreaCompanions("smoke"), "placement recorded");
            Require(_areaSession.QueryMapChange(new(1), AreaCompanionPlacement.Key(companion.Entity)) is { } value
                && AreaCompanionPlacement.Decode(value)?.World == new CellCoord((int)world.X, (int)world.Y), "the session holds his cell");
            Require(host.Nearest(map.Chart.ToWorld(pc).X, map.Chart.ToWorld(pc).Y, SpeakerCell) == speaker, "within talk reach where he stands");

            // ARC-WORLD-111: the PC 10 cells farther from his T0 cell (later stages need him away from it); on 50 ms frames (a whole-second frame ends each of his steps on a cell
            // centre) his run and walk after the PC are drawn with his own move cycle, never his T0 frame slid along. A pause is a
            // frame he stands before arriving.
            var walkFrames = new HashSet<AreaFrameKey>(); var walkingFrames = 0; var t0Frames = 0; var pauses = 0;
            var back = AreaSmokeStandable(map, origin with { X = origin.X + 22 }, 3);
            _areaSmokeTime = 0; _worldTestTime = SimulationTime.At(0);
            ConfigureAreaActor(map, back, NeighborDirection.XPlus);
            companion = _areaCompanions!.Companions.Single();
            var pcBack = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            Require(FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pcBack) > FollowDistanceRules.Range + 2, "the PC stands away from him again");
            var fine = 0;
            for (var i = 1; i <= 2000 && (walkingFrames == 0 || companion.Grid.State.Phase != MovementPhase.Settled
                || FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pcBack) > FollowDistanceRules.Range); i++)
            {
                fine = i;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(i, 20));
                if (companion.Grid.State.Action == MovementAction.Idle)
                {
                    if (walkingFrames > 0 && FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pcBack) > FollowDistanceRules.Range) pauses++;
                    continue;
                }
                if (AreaCompanionCommands()?.FirstOrDefault(c => c.Identity == companion.Key) is not { } walking) continue;
                walkingFrames++;
                if (walking.Frame.Policy == AreaBodyMoveArt.Policy) walkFrames.Add(walking.Frame); else t0Frames++;
            }
            _areaSmokeTime = fine / 20 + 1;
            Require(walkingFrames > 0 && t0Frames == 0 && walkFrames.Count >= 3,
                $"his walk is drawn with his move cycle: {walkFrames.Count} distinct frames over {walkingFrames} walking frames, {t0Frames} with his T0 frame, {pauses} pauses; "
                + string.Join(", ", _bodyMoves.Values.Select(m => $"{m.Status} {m.Detail}")));
            var stood = AreaCompanionCommands()?.FirstOrDefault(c => c.Identity == companion.Key);
            // Turned, he stands on frame 0 of his stand ART; facing as at T0, on his T0 frame.
            Require(stood is not null && (stood.Frame.Policy != AreaBodyMoveArt.Policy || stood.Frame.Frame == 0),
                $"he stands when he arrives: {stood?.Frame.Policy} frame {stood?.Frame.Frame}");
            GD.Print("ARC-WORLD-111 native-smoke follower walk PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Script = speaker.ScriptId, WalkingFrames = walkingFrames, DistinctFrames = walkFrames.Count, Pauses = pauses,
                Stood = stood!.Frame.Policy == AreaBodyMoveArt.Policy ? "stand ART" : "T0 frame",
                Moves = _bodyMoves.Values.Select(m => m.Detail).ToArray(), Parity = "NOT_VERIFIED" }));

            // ARC-WORLD-046: F4 (spread out) through the keyboard path sets his spread flag and steps him away from the PC; F3 (stay
            // close) clears it, and from beyond 4 cells he closes to 2 again. Then the portrait menu's wait makes him wait (his wait
            // index 6 schedules no wait timer, ARC-WORLD-045).
            void Press(Key key)
            {
                GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            }
            long Apart() => FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            void SettleOrder() { for (var i = 0; i < 120 && (companion.Follow.Ordered || companion.Grid.State.Phase != MovementPhase.Settled); i++) AreaSmokeFrame(); }
            GetViewport().GuiReleaseFocus();
            // The original sends on key up: the press alone arms F4 and sends nothing.
            GetViewport().PushInput(new InputEventKey { Keycode = Key.F4, PhysicalKeycode = Key.F4, Pressed = true });
            Require(!_areaSession.QueryCompanion(companion.Entity).SpreadOut, "F4's press alone sent nothing");
            GetViewport().PushInput(new InputEventKey { Keycode = Key.F4, PhysicalKeycode = Key.F4, Pressed = false });
            Require(_areaSession.QueryCompanion(companion.Entity).SpreadOut, "F4 spread him out: " + _companionNotice);
            SettleOrder();
            var spread = Apart();
            for (var i = 0; i < 8; i++) AreaSmokeFrame();
            Require(Apart() == spread, $"spread out he holds at {spread} (start distance 7): {group()}");
            Press(Key.F3);
            Require(!_areaSession.QueryCompanion(companion.Entity).SpreadOut, "F3 called him close: " + _companionNotice);
            for (var i = 0; i < 120 && spread > FollowDistanceRules.StartDistance
                && (Apart() > FollowDistanceRules.Range || companion.Grid.State.Phase != MovementPhase.Settled); i++) AreaSmokeFrame();
            var close = Apart();
            Require(spread > FollowDistanceRules.StartDistance ? close <= FollowDistanceRules.Range : close == spread,
                $"staying close he came back from {spread} to {close}: {group()}");
            Require(_followerPortraits?.EntitiesForSelfTest.Contains(companion.Entity) == true, "his portrait is shown");
            // The production path with mouse presses: his portrait button opens the order menu, which must not take the world
            // window's focus (an embedded popup did, also without OS focus: that stops the PC's route and refuses the choice), and
            // its wait button chooses it. The GUI takes both presses: no world click and no PC move.
            var focusExits = _worldFocusExitSignals; var windowFocused = GetWindow().HasFocus();
            var lastClick = _worldRouteClick; var pcCell = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            async Task ClickAsync(Vector2? at, string what)
            {
                Require(at is not null, what + " is shown");
                foreach (var down in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at!.Value, GlobalPosition = at.Value });
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await ClickAsync(_followerPortraits!.PortraitCentreForSelfTest(companion.Entity), "his portrait button");
            Require(_followerPortraits.MenuShown, "a press on his portrait opened the order menu");
            Require(_worldFocusExitSignals == focusExits && GetWindow().HasFocus() == windowFocused,
                $"the order menu kept the world window's focus (exits {focusExits}->{_worldFocusExitSignals}, focused {windowFocused}->{GetWindow().HasFocus()})");
            await ClickAsync(_followerPortraits.MenuItemCentreForSelfTest(FollowerMenuItem.Wait), "the menu's wait button");
            Require(!_followerPortraits.MenuShown && _worldFocusExitSignals == focusExits, "choosing closed the menu, still without a focus change");
            Require(ReferenceEquals(_worldRouteClick, lastClick) && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == pcCell,
                $"the GUI took both presses (last world click '{_worldRouteClick}', was '{lastClick}')");
            Require(_areaSession.QueryCompanion(companion.Entity).Standing == CompanionStanding.Waiting && _areaSession.QueryWaitTimer(companion.Entity) is null,
                "the portrait's wait made him wait, with no wait timer: " + _companionNotice);
            Require(_followerPortraits?.EntitiesForSelfTest.Contains(companion.Entity) == false, "a waiting follower has no portrait (OL16)");
            cell = companion.Grid.State.CommittedCell;
            Require(RecordAreaCompanions("smoke orders"), "placement recorded after the orders");
            GD.Print("ARC-WORLD-046 native-smoke orders PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Script = speaker.ScriptId, SpreadDistance = spread, CloseDistance = close, Waited = true, WaitTimer = "none (index 6)",
                Menu = new { Path = "mouse: portrait button, inline menu, wait button", WindowFocused = windowFocused, FocusExits = _worldFocusExitSignals - focusExits },
                Parity = "NOT_VERIFIED" }));

            // Waiting: the PC goes back to where Virgil started; he stays on his recorded cell.
            AreaSmokePlace(map, AreaSmokeStandable(map, origin, 2));
            companion = _areaCompanions!.Companions.Single();
            Require(companion.Grid.State.CommittedCell == cell && companion.Standing == CompanionStanding.Waiting, "rebound waiting on his recorded cell: " + group());
            var revision = _areaSession.Version.Revision;
            for (var i = 0; i < 40; i++) AreaSmokeFrame();
            Require(companion.Grid.State.CommittedCell == cell && companion.Standing == CompanionStanding.Waiting
                && FollowDistanceRules.Distance(cell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell) > FollowDistanceRules.StartDistance,
                "a waiting Virgil stays although the PC is farther than 4");
            GD.Print("ARC-WORLD-045 native-smoke loyalty PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Script = speaker.ScriptId, StepClearedMarks = true, GoalGated = true, WaitTimer = "none (index 6)", Parity = "NOT_VERIFIED" }));
            // ARC-WORLD-067: the HUD names the PC side of the party rules; ARC-WORLD-075: a wiring check that out of combat mode his walk
            // passes critters (the walk through a critter itself is covered by the Core tests).
            RefreshAreaStatus();
            var side = _areaSession.QueryHero() is null ? "PC side: diagnostic profile" : "PC side: hero (";
            Require(_hud!.Text.Contains(side, StringComparison.Ordinal), $"the HUD names the PC side ('{side}')");
            Require(companion.PassesCritters && !_areaSession.QueryCombatMode(SessionCombatant.Critter(companion.Entity)),
                "out of combat mode Virgil's walk is set to pass critters");
            GD.Print("ARC-WORLD-075 native-smoke companion pass PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Script = speaker.ScriptId, PassesCritters = "wired", PcSide = PartySideLabel(), Parity = "NOT_VERIFIED" }));
            GD.Print("ARC-WORLD-016 native-smoke follow PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Origin = new { speaker.WorldX, speaker.WorldY }, Pc = map.Chart.ToWorld(pc), Followed = world, Distance = FollowDistanceRules.Distance(cell, pc),
                Plans = plans, Session = revision, Parity = "NOT_VERIFIED" }));
            string group() => _areaCompanions?.Status() ?? "none";
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    // Native observation (ARC-WORLD-017), after the follow stage (Virgil waits on the exterior): following again, he goes
    // with the PC through the entrance onto the arrival cell, steps off it, is drawn and lit there and talks when clicked;
    // told to wait in the cave he stays there while the PC leaves, and the exterior has no T0 Virgil meanwhile; following
    // again he comes back out with the PC. Every transfer goes through the production use click (UseNextToTriggerAsync).
    private async Task RunAreaCompanionCaveSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            static void Require(bool value, string why)
            {
                if (value) return;
                GD.Print("ARC-WORLD-017 cave smoke FAIL: " + why);
                throw new InvalidOperationException("AREA_COMPANION_CAVE_SMOKE_FAILED");
            }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            Require(_areaTransitions?.CurrentMap == 1 && !InCave && _areaCompanions is { Companions.Count: 1 }, "the exterior with Virgil is published");
            var session = _areaSession!;
            var template = _companionTemplates.Single();
            var key = template.Key; var entity = template.Entity;
            var facts = AreaCompanionRoster.Facts(template.Facts, 0, 50, hero: session.QueryHeroPartySide());
            Require(session.UnwaitParty(session.Version, entity, facts, false).Disposition == GameSessionDisposition.Changed, "unwait on the exterior");
            string group() => _areaCompanions?.Status() ?? "none";
            AreaCompanion Virgil() => _areaCompanions!.Companions.Single();
            CellCoord Pc() => ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            void Settle()
            {
                for (var i = 0; i < 80 && (Virgil().Grid.State.Phase != MovementPhase.Settled || Virgil().Grid.State.CommittedCell == Pc()); i++) AreaSmokeFrame();
            }
            async Task<bool> Drawn(Arcanum.Data.Traversal.AreaStepCell? at)
            {
                PublishWorldSelection();
                for (var frame = 0; frame < 3; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                return _canvas!.AreaVisibleCommands.Any(c => c.Identity == key && (at is null || (c.MapX == at.Value.X && c.MapY == at.Value.Y)));
            }

            // In: he goes with the PC onto the arrival cell, then steps off it.
            var inbound = await UseNextToTriggerAsync(79);
            var caveMap = _areaTransitions!.Current;
            var arrival = caveMap.Chart.Spawn;
            Require(InCave && _areaCompanions is { Companions.Count: 1 } && Virgil().Template.Home.RuntimeMap == 1, "Virgil came into the cave: " + group());
            Require(AreaCompanionPlacement.Where(session, template.Home, entity) is { Map.RuntimeMap: 79, Placement: { } placed }
                && caveMap.Chart.ToChart(new(placed.World.X, placed.World.Y)) == arrival, "the session puts him on the cave arrival cell");
            Settle();
            var caveCell = Virgil().Grid.State.CommittedCell;
            Require(FollowDistanceRules.Distance(caveCell, Pc()) == 1 && Virgil().Grid.State.Phase == MovementPhase.Settled, "he stepped off the shared cell: " + group());
            var caveWorld = caveMap.Chart.ToWorld(caveCell);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            Require(await Drawn(caveWorld), "he is drawn on his cave cell: " + string.Join(",", _canvas!.AreaVisibleCommands.Where(c => c.Identity == key).Select(c => $"({c.MapX},{c.MapY})")));
            var drawn = _canvas.AreaVisibleCommands.First(c => c.Identity == key);
            var point = PickedScreenPoint(drawn);
            Require(point is not null, "the pick finds him in the cave");
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_CAVE_FOLLOW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-017 cave smoke: capture failed");
            }
            AreaSmokeClick(point!.Value);
            AreaSmokeFramesUntilConversation(); Require(ConversationOpen, "a click on him in the cave talks: " + _conversationNotice);
            EndConversation();

            // Waiting in the cave: the PC leaves alone; he stays, and the exterior has no T0 Virgil.
            Require(session.WaitParty(session.Version, entity, AreaCompanionRoster.WaitFacts(template.Facts, session.QueryHeroPartySide())).Disposition == GameSessionDisposition.Changed
                && session.QueryWaitTimer(entity) is null, "wait in the cave, with no wait timer");
            // The smoke stands the PC next to the exit with a fresh session, which rebuilds the group from the session: his
            // cell is recorded first, as F7 or leaving the map would.
            Require(RecordAreaCompanions("smoke"), "placement recorded");
            await UseNextToTriggerAsync(1);
            Require(AreaCompanionPlacement.Where(session, template.Home, entity) is { Map.RuntimeMap: 79, Placement: { } stayed }
                && stayed.World == new CellCoord((int)caveWorld.X, (int)caveWorld.Y), "a waiting Virgil stays on his cave cell");
            Require(_areaCompanions is { Companions.Count: 0 } && _areaCompanions.Absent.Contains(key), "absent from the exterior: " + group());
            var t0 = template.Commands.First();
            _camera.ResetWorld(new GamepadLab.Core.Vec2(t0.Destination.X + t0.Destination.Width / 2d, t0.Destination.Y + t0.Destination.Height / 2d), WorldViewport, 1);
            Require(!await Drawn(null), "his T0 body is not drawn while he is away");
            Require(_conversationHost!.Nearest(template.Speaker.WorldX, template.Speaker.WorldY, SpeakerCell, SpeakerPresent) is null, "nobody to talk to on his T0 cell");
            Require(_areaCompanions!.OccupancyForPc().Moved.Contains(key), "his T0 record does not block");

            // Back in: he waits where he stood; following again, he comes out with the PC.
            await UseNextToTriggerAsync(79);
            Require(_areaCompanions is { Companions.Count: 1 } && Virgil().Grid.State.CommittedCell == caveCell && Virgil().Standing == CompanionStanding.Waiting,
                "he waited in the cave: " + group());
            Require(session.UnwaitParty(session.Version, entity, facts, false).Disposition == GameSessionDisposition.Changed, "unwait in the cave");
            var outbound = await UseNextToTriggerAsync(1);
            Require(_areaCompanions is { Companions.Count: 1, Absent.Count: 0 } && session.QueryMapChange(template.Home, AreaCompanionPlacement.MapKey(entity)) is null,
                "he came back out: " + group());
            Require(Virgil().Grid.State.CommittedCell == Pc(), "on the PC's arrival cell");
            Settle();
            var home = Virgil().Grid.State.CommittedCell;
            _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            var homeWorld = _areaTransitions!.Current.Chart.ToWorld(home);
            Require(FollowDistanceRules.Distance(home, Pc()) == 1 && await Drawn(homeWorld), "drawn next to the PC outside: " + group());
            GD.Print("ARC-WORLD-017 native-smoke cave PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Inbound = inbound, CaveArrival = caveMap.Chart.ToWorld(arrival), Cave = caveWorld, Outbound = outbound, Outside = homeWorld,
                Session = session.Version.Revision, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>A screen point outside the HUD where <paramref name="command"/> draws an opaque pixel and the production
    /// pick answers its object, or null.</summary>
    private Vector2? PickedScreenPoint(AreaDrawCommand command)
    {
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var size = GetViewport().GetVisibleRect().Size;
        var hud = _hud!.Rect;
        for (var y = 0; y < (int)size.Y; y += 2)
        for (var x = 0; x < (int)size.X; x += 2)
        {
            var point = new Vector2(x + .5f, y + .5f);
            if (!hud.HasPoint(point) && AreaAlphaAt(command, AreaFrames(), transform, x, y) != 0 && AreaObjectAtScreen(point) == command.Identity) return point;
        }
        return null;
    }

    /// <summary>A cell within <paramref name="range"/> of <paramref name="center"/> the PC can stand on (see <c>ApproachCell</c>).</summary>
    private CellCoord AreaSmokeStandable(AreaMapRuntime map, CellCoord center, int range)
    {
        var world = map.Chart.ToWorld(center);
        var probe = new AreaGridSession(map.Steps, SimulationTime.At(0), chart: map.Chart, start: map.Chart.Spawn);
        return ApproachCell(probe, world.X, world.Y, range) ?? throw new InvalidOperationException("ARC-WORLD-016 follow smoke: no standable cell near " + center);
    }
}
