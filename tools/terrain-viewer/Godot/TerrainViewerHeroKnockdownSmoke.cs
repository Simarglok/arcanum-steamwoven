using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-COMBAT-021 native observation (with --hero, on the installed exterior and in map mode, after the audio stage, as it moves the
    // game clock on by the recovery's minutes, which the audio's night checks would see): a fatigue
    // packet leaves the hero unconscious; it falls (the body's fall-down ART, last frame drawn and uploaded), a press on the ground and the
    // stick do nothing; the session clock flows until the recovery brings it round, and it gets up (the get-up ART) and stands, taking a
    // press again. The hero's fatigue is healed back afterwards. ARC_AREA_KNOCKDOWN_CAPTURE saves in-app readbacks (down, standing, stunned-N, dying-BLOW-mid and dying-BLOW-end) to
    // the caller's ignored path.
    private async Task RunAreaHeroKnockdownSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession)
        { GD.Print($"ARC-COMBAT-021 native-smoke knockdown NONE map={_readyMapName} (needs a hero)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-021 knockdown smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_KNOCKDOWN_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var aiBefore = _aiForSmoke;
        _worldFocusSimulationForSelfTest = true;
        var before = session.QueryHeroLife()!.Record.State.FatigueDamage;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } area, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(area.Snapshot, mapSteps, area.Plan.Chart) : _areaTransitions!.Current;
            var start = AreaSmokeStandable(map, map.Chart.Spawn, 2);
            var far = RunGoal(map, start);
            Require(far is not null, "a cell the PC can walk to at least 8 cells from its start");
            AreaSmokePlace(map, start);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SyncHeroBody();
            var profile = _worldActor.Profile;
            Require(profile.FallDown is not null && profile.GetUp is not null, "the hero's body has its fall-down and get-up ARTs: " + _heroBodyStatus);
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_KNOCKDOWN_CAPTURE");
            async Task Capture(string name)
            {
                if (string.IsNullOrWhiteSpace(capture)) return;
                _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
            }
            Vector2 Screen(CellCoord cell)
            {
                var center = AreaCellPicking.Center(cell);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y);
            }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            void Frame(Vec2 stick = default)
            {
                _areaSmokeTime++;
                // ARC-DEV-2043: a native smoke's viewer does not tick the area clock, so the AI host's own frame part never runs, and with the AI host on (the stun and dying parts) nothing else drains the session's unclaimed
                // goal ends. A kill that lands while the lying hero's knock-down goal (62) is still on the table (the fall reached Down between two of the goal's steps) ends it by the dying start: without its end node
                // (HeroFalls(false)) the forced fall stayed on and the raised hero never got up. The game drains them at its next frame; the smoke's frame does the same.
                if (AiEnabled && _aiHost is { } ai) foreach (var end in session.DrainGoalEnds()) ai.RunEndNodes(end);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(stick) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var grid = (AreaGridSession)_worldActor.Session!;

            // The fatigue packet: current fatigue to −3, as in the owner's report.
            var fatigue = session.QueryHeroLife()!.Status.Fatigue;
            Require(fatigue > 0 && !HeroLyingDown, $"the hero starts conscious and standing (fatigue {fatigue})");
            var knocked = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = fatigue + 3 });
            Require(knocked.Disposition != GameSessionDisposition.Rejected && session.QueryHeroLife()!.Status is { IsUnconscious: true, IsDead: false },
                $"the packet left the hero unconscious: {knocked.Disposition} {knocked.Failure} fatigue {session.QueryHeroLife()!.Status.Fatigue}");
            var lowest = session.QueryHeroLife()!.Status.Fatigue;
            var fell = 0;
            for (var i = 0; i < 100 && _heroKnockdown.Stage != AreaKnockdownStage.Down; i++)
            {
                Frame();
                if (_readyActor?.Action == ActorDiagnosticAction.FallDown) fell++;
            }
            Require(_heroKnockdown.Stage == AreaKnockdownStage.Down && fell > 1, $"the hero fell over {fell} drawn frames: {_heroKnockdown.Stage}");
            Frame();
            Require(_worldActor.DownPose is { Action: ActorDiagnosticAction.FallDown } pose && pose.Frame == profile.FallDown!.FramesPerGroup - 1
                && _readyActor is { Action: ActorDiagnosticAction.FallDown } drawn && drawn.Frame == pose.Frame && _cache.TryGet(drawn.TextureKey, out _),
                $"the fall's last frame is drawn and uploaded: {_worldActor.DownPose} {_readyActor?.Action} {_readyActor?.Frame}");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("hero: unconscious", StringComparison.Ordinal), "the status line names the unconscious hero");
            await Capture("down");

            // Lying, the hero takes no press and does not walk on the stick.
            var cell = grid.State.CommittedCell;
            WorldRouteClick(Screen(far!.Value));
            Require(_worldRouteClick == "ignored (the hero is down)" && !HeroWalking,
                $"a ground press while down does nothing: {_worldRouteClick}");
            for (var i = 0; i < 20; i++) Frame(new Vec2(1, 0));
            for (var i = 0; i < 5; i++) Frame();
            Require(grid.State.CommittedCell == cell && grid.State.Phase == MovementPhase.Settled && _heroKnockdown.Stage == AreaKnockdownStage.Down,
                $"the stick did not move the lying hero: {cell} -> {grid.State.CommittedCell}");

            // The session clock flows: the fatigue recovery brings the hero round, and it gets up.
            Require(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.HeroFatigue) && _areaAmbient is not null,
                $"the fatigue recovery is on the queue and the clock driver is there ({_areaAmbient is not null})");
            // Game-time jumps of 10 s (as a world-map travel step): the events that come due fire, the recovery among them.
            var jumps = 0;
            for (; jumps < 1000 && session.QueryHeroLife()!.Status.IsUnconscious; jumps++) { _areaAmbient!.AdvanceMilliseconds(10_000, AmbientSector()); LogAreaEvents(_areaAmbient); }
            var after = session.QueryHeroLife()!.Status;
            Require(!after.IsUnconscious && after.Fatigue > 0, $"the recovery brought the hero round after {jumps * 10} game s: fatigue {after.Fatigue}");
            var rose = 0;
            for (var i = 0; i < 200 && _heroKnockdown.Lying; i++)
            {
                Frame();
                if (_readyActor?.Action == ActorDiagnosticAction.GetUp) rose++;
            }
            Frame();
            Require(_heroKnockdown.Stage == AreaKnockdownStage.Standing && rose > 1 && _worldActor.DownPose is null && _readyActor?.Action == ActorDiagnosticAction.Idle,
                $"the hero got up over {rose} drawn frames and stands: {_heroKnockdown.Stage} {_readyActor?.Action}");
            await Capture("standing");
            WorldRouteClick(Screen(far.Value));
            Require(HeroWalkGoal == far && _worldRouteClick != "ignored (the hero is down)", $"the standing hero takes a press again: {_worldRouteClick}");
            // ARC-DEV-187 (MIG13 S13a): a stun result on the walking hero starts its stun goal (57) on the table, which interrupts the walk at once (B11's mechanism);
            // the goal's first step ends it and clears the stunned bit, and the hero takes a press again.
            Require(HeroRunsHost()?.WalkRun is not null, "the hero is walking before the stun");
            var stunned = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Stun });
            Require(stunned.Disposition == GameSessionDisposition.Changed && stunned.Outcome!.Events.HasFlag(LifeEvents.Stunned), $"the stun landed: {stunned.Disposition} {stunned.Failure}");
            HeroStunned(session);
            Frame(); // the interrupted walk's end node runs with the AI host's frame
            Require(HeroRunsHost()?.StunRun is not null && HeroRunsHost()?.WalkRun is null && !HeroWalking, "the stun goal interrupted the walk");
            Require(!session.QueryHeroLife()!.Status.IsActive, "the stunned hero cannot act while the goal runs");
            // The goal's own step needs the area clock (a unit test covers it); the smoke cuts the goal, whose end node clears the bit as a reset or a quick load does.
            HeroRunsHost()!.LoseAll("SMOKE_STUN_CUT");
            Require(HeroRunsHost()?.StunRun is null && session.QueryHeroLife()!.Status.IsActive, "the cut stun goal's end node cleared the bit");
            // ARC-COMBAT-1741: left to play, the stun lasts its animation (23) and counts its plays out: the hero stays unable to act, is drawn in the stunned ART frame by frame (when its body has
            // the ART; a body without it ends the stun at its first step, as before), and acts again when the goal ends and clears the bit.
            Require(HeroRunsHost()?.WalkRun is null, "no walk is left before the stun");
            // The stun nodes run over the AI host's ports (the host is off in automated runs unless asked for): it is on for this part and off again after.
            _aiForSmoke = true;
            for (var i = 0; i < 4; i++) { Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector()); }
            Require(_aiHost is not null, "the AI host is built for the stun's nodes");
            // ARC-COMBAT-1865: the hero's stun that plays out and the deaths of its dying goal, as local steps run for the hero's own body and again for a hero in a monster body (<paramref name="label"/> names the body in the output and the captures).
            async Task HeroStunPlays(ActorDiagnosticProfile bodyProfile, string label)
            {
                var stunLands = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Stun });
                Require(stunLands.Outcome?.Events.HasFlag(LifeEvents.Stunned) == true && HeroRunsHost()?.StunRun is not null, $"the second stun started its goal: {stunLands.Disposition} {stunLands.Failure}");
                var stunFramesDrawn = new HashSet<int>(); var stunTicks = 0;
                var stunPlays = HeroRunsHost()!.StunRun!.Bottom.Count;
                for (var i = 0; i < 1200 && HeroRunsHost()?.StunRun is not null; i++, stunTicks++)
                {
                    Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector());
                    if (_readyActor is { Action: ActorDiagnosticAction.Stunned } stunDrawn) stunFramesDrawn.Add(stunDrawn.Frame);
                    if (i is 3 or 60 or 110 or 160 && bodyProfile.Stunned is not null) await Capture($"{label}stunned-{i}");
                    if (HeroRunsHost()?.StunRun is not null) Require(!session.QueryHeroLife()!.Status.IsActive, "the stunned hero cannot act while its goal plays");
                }
                if (bodyProfile.Stunned is { } stunArt)
                {
                    var cycleTicks = (int)Math.Ceiling(stunArt.FramesPerGroup * 20.0 / Math.Max(stunArt.RawFramesPerSecond, 1));
                    Require(stunFramesDrawn.Count > 1 && stunTicks >= cycleTicks, $"the stun played {stunFramesDrawn.Count} frames over {stunTicks} ticks (one cycle is {cycleTicks})");
                    Require(stunPlays <= 1 || stunTicks >= cycleTicks * (stunPlays - 1), $"the stun lasted its {stunPlays} plays: {stunTicks} ticks of {cycleTicks}");
                }
                Frame();
                Require(HeroRunsHost()?.StunRun is null && session.QueryHeroLife()!.Status.IsActive && _worldActor.DownPose is null,
                    $"the stun ended: the goal is gone, the bit is clear and the hero stands again (run {HeroRunsHost()?.StunRun is not null}, active {session.QueryHeroLife()!.Status.IsActive}, pose {_worldActor.DownPose})");
                GD.Print($"ARC-COMBAT-1741 hero stun lasted {label} ticks={stunTicks} frames={stunFramesDrawn.Count} plays={stunPlays} art={(bodyProfile.Stunned is null ? "none" : $"{bodyProfile.Stunned.FramesPerGroup}f@{bodyProfile.Stunned.RawFramesPerSecond}")}");
            }
            await HeroStunPlays(profile, "");
            // ARC-COMBAT-1741: an NPC's stun likewise lasts its animation (23) where its art has one: the AI host shows the stunned art frame by frame, the viewer draws the pose, the stun ends and the NPC acts again; an
            // NPC whose art has no stun ART is a typed fallback (its stun ends at once, as before).
            var npcCandidate = _combatHost is null ? default : session.QueryMapInventory(new SessionMapId(CurrentAreaMap))?.Holders
                .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
                .Select(h => (h.Entity, Combat: _combatHost.Build(session, CurrentAreaMap, h.Entity).Combat))
                .Where(c => c.Combat is not null && session.QueryLife(c.Combat.Target).View?.Status is { IsDead: false, IsUnconscious: false, IsActive: true })
                // Near the hero (at most 25 tiles): a far NPC's AI tick interrupts its animations (0x4AD200), a stun among them, as in the original.
                .Select(c => (c.Entity, c.Combat, Distance: _aiHost?.DistanceToPc(c.Entity) ?? -1)).Where(c => c.Distance is >= 0 and <= 25)
                .OrderBy(c => c.Distance).ThenBy(c => c.Entity, StringComparer.Ordinal).Select(c => (c.Entity, c.Combat)).FirstOrDefault() ?? default;
            if (npcCandidate.Combat is null) GD.Print("ARC-COMBAT-1741 npc stun NONE (no living NPC with combat values within 25 tiles of the hero)");
            else
            {
                var npc = npcCandidate.Entity; var npcTarget = npcCandidate.Combat.Target;
                var npcClip = NpcAnimationClip(npc, AreaFallenPoses.Stunned);
                var npcRow = _inventoryHost!.Maps.Single(m => m.RuntimeMap == CurrentAreaMap).Row(npc);
                var npcLands = session.ApplyDamage(session.Version, npcTarget, new LifeDamage { Effects = CriticalHitEffects.Stun });
                Require(npcLands.Outcome?.Events.HasFlag(LifeEvents.Stunned) == true, $"the NPC's stun landed: {npcLands.Disposition} {npcLands.Failure}");
                var npcFrames = new HashSet<int>(); var npcTicks = 0; var npcSeen = false; var npcDrawn = false;
                for (var i = 0; i < 1500; i++, npcTicks++)
                {
                    Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector());
                    SyncAreaCombatBodies();
                    if (_aiHost?.ArtOf(npc) is { Animation: AreaFallenPoses.Stunned } art) { npcSeen = true; npcFrames.Add(art.Frame); npcDrawn |= _fallenBodies.ContainsKey(npcRow.Key); }
                    else if (npcSeen || i > 60) break;
                }
                if (npcClip is { } clip)
                    Require(npcSeen && npcFrames.Count > 1 && npcDrawn, $"the NPC's stun played {npcFrames.Count} frames over {npcTicks} ticks and was drawn as a pose ({npcDrawn})");
                else Require(!npcSeen, "an NPC with no stunned ART shows none");
                Frame();
                Require(session.QueryLife(npcTarget).View?.Status.IsActive != false && _aiHost?.ArtOf(npc) is null, "the NPC's stun ended: its bit is clear and its art is back to standing");
                GD.Print($"ARC-COMBAT-1741 npc stun lasted entity={npc} ticks={npcTicks} frames={npcFrames.Count} art={(npcClip is { } c ? $"{c.Frames}f@{c.FramesPerSecond}" : "none")}");
            }
            // ARC-DEV-189 (MIG13 S13c): a knock-down result leaves the hero conscious and it still falls: the session starts goal 62, which interrupts the walk, forces the fall and ends when it lies;
            // the viewer's own rule then gets the conscious hero up.
            WorldRouteClick(Screen(far.Value));
            Require(HeroRunsHost()?.WalkRun is not null, "the hero walks before the knock-down");
            var crit = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Knockdown, HitPoints = 1 });
            Require(crit.Outcome is { } critOutcome && critOutcome.Events.HasFlag(LifeEvents.KnockedDown), $"the knock-down landed: {crit.Disposition} {crit.Failure}");
            Require(HeroRunsHost()?.DownRun?.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.KnockDown && HeroRunsHost()?.WalkRun is null, "goal 62 interrupted the walk");
            var crumpled = false; var stood = false;
            for (var i = 0; i < 600 && !(stood && HeroRunsHost()?.DownRun is null); i++)
            {
                Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector());
                crumpled |= _heroKnockdown.Stage is AreaKnockdownStage.Falling or AreaKnockdownStage.Down;
                // The run spans the fall: while the stage is Falling goal 62 is still on the table.
                if (_heroKnockdown.Stage == AreaKnockdownStage.Falling) Require(HeroRunsHost()?.DownRun?.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.KnockDown, "goal 62 lives while the fall plays");
                stood |= crumpled && _heroKnockdown.Stage == AreaKnockdownStage.Standing;
            }
            Require(crumpled && stood && HeroRunsHost()?.DownRun is null && !_heroKnockdown.Lying, $"the conscious hero fell and got up: fell {crumpled} rose {stood} stage {_heroKnockdown.Stage}");
            // ARC-COMBAT-1793: a hero a blow kills plays the dying animation its dying goal (31) names (the killing blow's choice: head 17, torso 18, leg 19, a plain kill 7; the Violence Filter forces 7) where its
            // body has the ART, else the fall; goal 31 lasts while it plays and the hero lies on its last frame. The death flow is off in an automated run, so the smoke raises the hero again after each.
            async Task HeroDeaths(ActorDiagnosticProfile bodyProfile, string label)
            {
                if (bodyProfile.FallDown is not null)
                {
                    var dyingReport = new List<string>();
                    foreach (var (blow, location, expected, shown) in new (string, HitLocation?, DeathAnimation, ActorDiagnosticAction)[] {
                        ("head", HitLocation.Head, DeathAnimation.Head, ActorDiagnosticAction.Decapitation), ("torso", HitLocation.Torso, DeathAnimation.Body, ActorDiagnosticAction.BlownOutChunk),
                        ("leg", HitLocation.Leg, DeathAnimation.Leg, ActorDiagnosticAction.SeveredLeg), ("kill", null, DeathAnimation.Plain, ActorDiagnosticAction.FallDown) })
                    {
                        Require(!_heroKnockdown.Lying && session.QueryHeroLife()!.Status.IsActive && HeroRunsHost()?.DownRun is null, $"the hero stands before the {blow}");
                        var slain = location is { } where ? session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1000, Location = where })
                            : session.Kill(session.Version, SessionLifeTarget.Hero);
                        Require(slain.Outcome is { } slainOutcome && slainOutcome.Events.HasFlag(LifeEvents.Died) && slainOutcome.Death == expected, $"the {blow} killed the hero with {expected}: {slain.Disposition} {slain.Failure}");
                        var named = Arcanum.Simulation.Ai.AiExecutorRules.DyingAnimationOf(expected);
                        Require(HeroRunsHost()?.DownRun?.Bottom is { Goal: Arcanum.Simulation.Goals.GoalType.Dying } dying && dying.Animation == named, $"goal 31 names animation {named} after the {blow}: {HeroRunsHost()?.DownRun?.Bottom}");
                        var wantedAnimation = DyingAnimationRules.Chosen(named, _gameSettings.ViolenceFilter);
                        var wantedArt = bodyProfile.DyingArt(wantedAnimation)!.Value;
                        var dyingFrames = new HashSet<int>(); var dyingTicks = 0; var dyingDrawn = false; var wrongDrawn = "";
                        for (var i = 0; i < 900 && (HeroRunsHost()?.DownRun is not null || i < 3); i++, dyingTicks++)
                        {
                            Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector());
                            if (_readyActor is { Action: var drawnAction } dyingActor && drawnAction is ActorDiagnosticAction.FallDown or ActorDiagnosticAction.Decapitation or ActorDiagnosticAction.BlownOutChunk or ActorDiagnosticAction.SeveredLeg)
                            {
                                if (drawnAction == wantedArt.Action) { dyingDrawn = true; dyingFrames.Add(dyingActor.Frame); } else wrongDrawn = drawnAction.ToString();
                            }
                            if (i == 12) await Capture($"{label}dying-{blow}-mid");
                        }
                        Require(dyingDrawn && wrongDrawn.Length == 0 && dyingFrames.Count > 1, $"the {blow} drew {wantedArt.Action} over {dyingTicks} ticks, {dyingFrames.Count} frames (another action drawn: '{wrongDrawn}')");
                        Frame();
                        Require(HeroRunsHost()?.DownRun is null && _worldActor.DownPose is { } held && held.Action == wantedArt.Action && held.Frame == wantedArt.Resource.FramesPerGroup - 1,
                            $"goal 31 ended and the hero lies on the last frame of {wantedArt.Action}: {_worldActor.DownPose}");
                        await Capture($"{label}dying-{blow}-end");
                        dyingReport.Add($"{blow}:{wantedArt.Action}:{wantedArt.Resource.FramesPerGroup}f@{wantedArt.Resource.RawFramesPerSecond}:{dyingTicks}ticks");
                        session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                        for (var i = 0; i < 600 && !(_heroKnockdown.Stage == AreaKnockdownStage.Standing && HeroRunsHost()?.DownRun is null); i++) { Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector()); }
                        Require(!_heroKnockdown.Lying && session.QueryHeroLife()!.Status is { IsDead: false, IsActive: true }, $"the hero was raised and stands: {_heroKnockdown.Stage}");
                    }
                    // A hero already knocked out lies in its fall and keeps it when killed (0x45D730): no bloody animation, the goal ends at once.
                    {
                        var koHit = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 1000 });
                        Require(session.QueryHeroLife()!.Status.IsUnconscious, $"the knock-out landed: {koHit.Disposition} {koHit.Failure}");
                        for (var i = 0; i < 400 && _heroKnockdown.Stage != AreaKnockdownStage.Down; i++) { Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector()); }
                        Require(_heroKnockdown.Stage == AreaKnockdownStage.Down, $"the knocked-out hero lies: {_heroKnockdown.Stage}");
                        Require(session.Kill(session.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the lying hero");
                        for (var i = 0; i < 60; i++) { Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector()); if (HeroRunsHost()?.DownRun is null && i > 2) break; }
                        Require(HeroRunsHost()?.DownRun is null && _worldActor.DownPose is { Action: ActorDiagnosticAction.FallDown } && _heroKnockdown.Stage == AreaKnockdownStage.Down,
                            $"the lying hero keeps its fall when killed: {_worldActor.DownPose} {_heroKnockdown.Stage}");
                        session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                        for (var i = 0; i < 600 && !(_heroKnockdown.Stage == AreaKnockdownStage.Standing && HeroRunsHost()?.DownRun is null); i++) { Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector()); }
                        Require(!_heroKnockdown.Lying && session.QueryHeroLife()!.Status is { IsDead: false, IsActive: true }, $"the hero was raised again: {_heroKnockdown.Stage} forced={_heroForcedDown} status={session.QueryHeroLife()!.Status} run={HeroRunsHost()?.DownRun?.Bottom?.Goal}");
                    }
                    GD.Print("ARC-COMBAT-1793 hero dying " + string.Join(" ", dyingReport) + $" body={label} art={(bodyProfile.Decapitation is null ? "-" : "17")}{(bodyProfile.BlownOutChunk is null ? "-" : "18")}{(bodyProfile.SeveredLeg is null ? "-" : "19")} filter={_gameSettings.ViolenceFilter}");
                }
            }
            await HeroDeaths(profile, "");
            // ARC-COMBAT-1865: the same stun and deaths for a hero in a monster body (a Trait component's body: species of the first of a few candidates whose installed ARTs include the stunned one): the body reads
            // its optional stunned ART (23) and bloody dying ARTs (17 to 19) where the species has them, plays the stun and the dying animation its goal names, else falls back (a dying animation to the fall).
            {
                var monsterReport = "none";
                foreach (var species in new[] { 4, 1, 3, 20, 25, 26 })
                {
                    var wrote = session.WriteTraitArt(session.Version, SessionSpellCaster.Hero, new SessionTraitArt(species, 0));
                    Require(wrote.Disposition == GameSessionDisposition.Changed, $"the monster body of species {species} was put on the hero: {wrote.Disposition} {wrote.Failure}");
                    for (var i = 0; i < 900 && !(WantedHeroBody().IsMonster && _heroBodies.TryGetValue(WantedHeroBody(), out var load) && ReferenceEquals(load.Profile, _worldActor.Profile)); i++)
                    { Frame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncHeroBody(); }
                    var monster = _worldActor.Profile;
                    Require(WantedHeroBody() is { IsMonster: true } && monster.Idle.Selector >> 28 == 12, $"the hero is drawn with a monster profile for species {species}: {_heroBodyStatus}");
                    if (monster.Stunned is null || monster.FallDown is null) { GD.Print($"ARC-COMBAT-1865 hero monster species={species} SKIP (no stunned or fall ART: stunned {monster.Stunned is not null})"); continue; }
                    for (var i = 0; i < 10; i++) Frame();
                    await Capture($"monster{species}-idle");
                    await HeroStunPlays(monster, $"monster{species}-");
                    await HeroDeaths(monster, $"monster{species}-");
                    monsterReport = $"species={species} stunned={monster.Stunned.FramesPerGroup}f dying={(monster.Decapitation is null ? "-" : "17")}{(monster.BlownOutChunk is null ? "-" : "18")}{(monster.SeveredLeg is null ? "-" : "19")}";
                    break;
                }
                var restored = session.WriteTraitArt(session.Version, SessionSpellCaster.Hero, null);
                Require(restored.Disposition == GameSessionDisposition.Changed, $"the monster body came off the hero: {restored.Disposition} {restored.Failure}");
                for (var i = 0; i < 900 && !(!WantedHeroBody().IsMonster && ReferenceEquals(_worldActor.Profile, profile)); i++)
                { Frame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncHeroBody(); }
                Require(!WantedHeroBody().IsMonster, "the hero has its own body again");
                Require(monsterReport != "none", "a monster body with the stunned ART was found among the candidates");
                GD.Print("ARC-COMBAT-1865 hero monster body " + monsterReport);
            }
            HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "SMOKE_DONE"); StopWorldRoute("SMOKE_DONE");
            GD.Print("ARC-COMBAT-021 native-smoke knockdown PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName ?? "crash-site", Body = _heroBodyStatus, FellFrames = fell, RoseFrames = rose, Fatigue = $"{fatigue}->{lowest}->{after.Fatigue}", RecoveredAfterGameSeconds = jumps * 10,
                Parity = "NOT_VERIFIED (rules read in the original executable, UC1–UC5; no original-runtime run)" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            if (_aiForSmoke != aiBefore) { _aiForSmoke = aiBefore; RetireAiHost(); _aiHostStamp = null; }
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            var damage = session.QueryHeroLife()!.Record.State.FatigueDamage;
            if (damage > before) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = damage - before });
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
