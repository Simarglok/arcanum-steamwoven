using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-304 (B24, SM26) native observation (stage "spirit-talk", only when named, with --hero, on the Crash Site window): the spell "spirit of the dead" (spell 56) sets the spell flag
    // OSF_SPOKEN_WITH_DEAD (0x2000000) on a dead critter and removes it at its END (the ObjFlag component 0x454A10, no other effect). What the executable does with that flag (GT216-GT218): the click
    // 0x54ED30 sends a dead critter that carries it to the talk (goal 21) instead of the loot; the can-speak test 0x4AD800 answers 0 for it, so the dialog starts as for a living NPC (the greeting is the
    // NPC's dead line group); with the flag gone it is a corpse to loot again. The stage kills the first dialog NPC it can talk to, shows the click looting it, casts the spell on it, shows the click
    // talking to it (the conversation opens with the corpse), ends the spell and shows the loot again. The NPC and the hero are restored for the later stages.
    private async Task RunAreaSpiritTalkSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _conversationHost is not { } host || _readyArea is not { } area || !InventoryActive)
        { GD.Print("ARC-DEV-304 native-smoke spirit-talk SKIP (needs --hero, an area session, its combat and conversation hosts and the inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-304 spirit-talk smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SPIRIT_TALK_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        var candidates = host.Speakers
            .Where(s => commands.ContainsKey(s.Row.Key) && SpeakerPresent(s) && s.Row.WorldX is not null && s.Row.WorldY is not null)
            .Select(s => (Speaker: s, Entity: AreaConversationState.EntityId(s.Row)))
            .Where(c => _combatHost.Build(session, map.RuntimeMap, c.Entity).Combat is not null && session.QueryCompanion(c.Entity).Standing == CompanionStanding.Free)
            .OrderByDescending(c => host.DeathSpeech(c.Speaker) == true) // an NPC whose script file has the death-speech flag 0x8 first: its own dialog runs (SU15)
            .ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (candidates.Length == 0) { GD.Print($"ARC-DEV-304 native-smoke spirit-talk NONE map={map.RuntimeMap} (no dialog NPC with combat values)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        (AreaSpeaker Speaker, string Entity)? used = null;
        SessionLifeTarget? life = null;
        var spiritRuns = new List<int>();
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var caster = SpellCasterNow();
            Require(caster is not null, "the map's cast host is built (spell data " + _spellData?.Status + ")");
            Require(_spellRecords?.Any(r => r is not null && r.Data.Number == 56) == true, "the data holds spell 56");
            // A dialog NPC with a standable cell 2..3 cells from it, so the click talks without a long walk.
            CellCoord? place = null;
            foreach (var candidate in candidates)
            {
                var at = map.Chart.ToChart(new(candidate.Speaker.Row.WorldX!.Value, candidate.Speaker.Row.WorldY!.Value));
                foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (2, 2), (-2, -2), (2, -2), (-2, 2), (4, 0), (-4, 0), (0, 4), (0, -4) })
                {
                    var near = new CellCoord(at.X + dx, at.Y + dy);
                    if (!map.Chart.Contains(near)) continue;
                    CellCoord cell;
                    try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                    var distance = Math.Max(Math.Abs(cell.X - at.X), Math.Abs(cell.Y - at.Y));
                    if (distance is >= 2 and <= 4) { place = cell; used = candidate; break; }
                }
                if (place is not null) break;
            }
            Require(place is not null && used is not null, "a dialog NPC with a standable cell two to four cells from it");
            var (speaker, entity) = used!.Value;
            life = _combatHost.Build(session, map.RuntimeMap, entity).Combat!.Target;
            AreaSmokePlace(map, place!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            for (var i = 0; i < 10; i++) AreaSmokeFrame();
            Require(SpeakerPresent(speaker) && !SpeakerDead(speaker) && !SpokenWithDead(entity), "the NPC is a living speaker before the kill");

            // The click tables differ in combat mode (0x54FCA0: a body is looted only with Alt there); an earlier stage of a chain may have left the hero in it.
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            Require(!HeroInCombatMode, "the hero is out of combat mode");

            // 1. The corpse: not a speaker any more; the click sends the hero to loot it (goal 81), not to talk (GT115).
            var kill = session.ApplyDamage(session.Version, life!, new LifeDamage { HitPoints = 1_000_000 });
            Require(kill.Outcome is { } killed && killed.Events.HasFlag(LifeEvents.Died), $"the lethal packet killed the NPC: {kill.Disposition} {kill.Failure}");
            for (var i = 0; i < 5; i++) AreaSmokeFrame();
            SyncAreaInventory();
            Require(SpeakerDead(speaker) && !SpeakerPresent(speaker), "the dead NPC is not a present speaker without the flag");
            var lootClick = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(speaker.Row.Key)));
            Require(lootClick.Click == HeroClickGoal.UseContainer, $"the click on the corpse without the flag loots it (goal 81): {lootClick.Click} {lootClick.Outcome}");
            CancelInventoryApproach("smoke: the loot click is shown");
            for (var i = 0; i < 5; i++) AreaSmokeFrame();
            Require(!TryAreaTalk(speaker), "the talk click on the corpse without the flag does not talk");

            // 2. The spirit: a script's free cast of spell 56 at the corpse with no caster (the cast the hero's own willpower and fatigue checks would refuse, RN5, does not apply to a run with no PC or NPC parent):
            // the run's BEGIN is an event of the session's queue, which the next frames fire.
            var attempt = caster!.CastByScript(null, 56, entity, free: true, unresistable: true);
            Require(attempt.Outcome == SpellCastAttemptOutcome.Started && attempt.RunId is not null, $"the script cast of spell 56 started: {attempt.Outcome} {attempt.Run?.Creation?.Status} {attempt.Run?.Creation?.Check}");
            var run = attempt.RunId!.Value;
            spiritRuns.Add(run);
            var frames = 0;
            for (; frames < 120 && !SpokenWithDead(entity); frames++) { TickAreaClock(_talkSmokeHostMs += 50); AreaSmokeFrame(); }
            FlushSpellLog(null);
            GD.Print($"ARC-DEV-304 spirit-talk cast frames={frames} flag={SpokenWithDead(entity)} run={session.QuerySpellRuns()[run]?.Action.ToString() ?? "gone"}");
            var log = caster.Log.Where(e => e.Run == run).ToArray();
            Require(!caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Run == run && e.Value == (int)Arcanum.Data.Magic.SpellComponentType.ObjFlag),
                "the ObjFlag component of spell 56 is ready: " + string.Join(", ", log.Select(e => e.Kind)));
            Require(SpokenWithDead(entity) && caster.SpokenWithDead(entity), $"the flag 0x2000000 is on the corpse: {string.Join(", ", log.Select(e => e.Kind))}");
            Require(session.QuerySpellRuns()[run] is { } spirit && spirit.Spell == 56, "spell 56's maintained run lives (the flag lasts as long as the run)");
            Require(!SpeakerSpokenWithSilent(speaker) && SpeakerPresent(speaker), "the corpse with the flag is a present speaker");

            // 3. The click talks (goal 21) and the conversation opens with the corpse: the can-speak test answers 0 for it (0x4AD800), the dialog starts as for a living NPC.
            Require(SpeakerDead(speaker) && SpokenWithDead(entity) && caster.SpokenWithDead(entity) && session.QueryNpcDead(entity), "the NPC is still dead and flagged right before the talk click");
            // ARC-DEV-324 (GT220-GT222): a start that ran default (the generic greeting) opens no window and floats the corpse's dead line, so the wait ends with either the window or the typed start.
            _voiceStart = null;
            var floatedBefore = _scriptFloats;
            var talkClick = HeroRequests.Apply(new ObjectClickRequest(TalkTargetOf(speaker)));
            Require(talkClick.Click == HeroClickGoal.Talk && talkClick.Outcome == HeroRequestOutcome.Done, $"the click on the corpse with the flag talks (goal 21): {talkClick.Click} {talkClick.Outcome}");
            for (frames = 0; frames < 600 && !ConversationOpen && _voiceStart is null; frames++) AreaSmokeTalkFrame();
            Require(_voiceStart is not null, $"the talk with the corpse started after {frames} frames ({_conversationNotice})");
            // SU15 (GT218): point 9 of a dead NPC runs its own dialog only when its script file has the death-speech flag 0x8; otherwise it runs default (the generic greeting: a floating line, no window).
            var deathSpeech = host.DeathSpeech(speaker);
            Require(deathSpeech is not null, "the NPC's dialog script reads");
            var expected = deathSpeech == true ? AreaConversationStartStatus.Started : AreaConversationStartStatus.GenericGreeting;
            Require(_voiceStart?.Status == expected, $"the dialog start of the corpse (death speech {deathSpeech}) is {expected}: {_voiceStart?.Status} {_voiceStart?.Detail}");
            Require(deathSpeech == true ? host.LastRun?.Stop != Arcanum.Scripting.Scr.ScrStop.DeadWithoutSpeech : host.LastRun is { Outcome: Arcanum.Scripting.Scr.ScrOutcome.RunDefault, Stop: Arcanum.Scripting.Scr.ScrStop.DeadWithoutSpeech },
                $"the dialog point's run: {host.LastRun?.Outcome} {host.LastRun?.Stop}");
            string origin;
            if (deathSpeech == true)
            {
                Require(ConversationOpen && ReferenceEquals(_conversationSpeaker, speaker), $"the conversation with the corpse opened after {frames} frames ({_conversationNotice})");
                origin = _conversationPanel!.Conversation?.NpcLine?.Origin?.Kind.ToString() ?? "none";
                GD.Print($"ARC-DEV-304 spirit-talk conversation opened with the corpse, first NPC line from {origin}, phase {_conversationPanel.Conversation?.Phase}");
            }
            else
            {
                Require(!ConversationOpen, "the generic greeting opens no dialog window");
                var line = _voiceStart!.GreetingLine ?? string.Empty;
                Require(!string.IsNullOrEmpty(line), $"the generic greeting of the corpse has a line: {_voiceStart.Detail}");
                for (var i = 0; i < 5; i++) AreaSmokeFrame();
                // A smoke's frames do not run the viewer's process step, which floats the queued script lines: tick it by hand.
                TickFloaters();
                Require(_scriptFloats > floatedBefore, $"the greeting floated over the corpse: {_scriptFloats - floatedBefore}");
                // ARC-UI-1997: the greeting is a text bubble over the corpse (the wrapped lines read back as the line).
                var drawn = FloatersForSelfTest?.LayOutBubbles().Where(d => d.Owner == entity).Select(d => string.Join(" ", d.Lines.Select(l => l.Text))).ToArray() ?? [];
                Require(drawn.Contains(line), $"the corpse's line is a bubble over it: \"{line}\" among [{string.Join(" | ", drawn)}]");
                origin = "generic greeting";
                GD.Print($"ARC-DEV-324 spirit-talk generic greeting over the corpse: {line} ({_voiceStart.Detail})");
            }
            if (ConversationOpen) EndConversation();
            for (var i = 0; i < 5; i++) AreaSmokeFrame();
            Require(!ConversationOpen, "the conversation closed");

            // 4. The END takes the flag off: the corpse is a corpse again, the click loots.
            caster.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            FlushSpellLog(null);
            Require(!SpokenWithDead(entity) && !caster.SpokenWithDead(entity), "the END took the flag off");
            Require(!SpeakerPresent(speaker), "without the flag the dead NPC is no present speaker again");
            Require(!TryAreaTalk(speaker), "the talk click on the corpse without the flag does not talk again");
            var lootAgain = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(speaker.Row.Key)));
            Require(lootAgain.Click == HeroClickGoal.UseContainer, $"the click on the corpse loots it again: {lootAgain.Click}");
            CancelInventoryApproach("smoke: the loot click is shown");

            GD.Print("ARC-DEV-304 native-smoke spirit-talk PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = map.RuntimeMap, Speaker = speaker.ScriptId, Spell = 56, Run = run,
                Flag = "0x2000000", DeathSpeech = deathSpeech, DialogStart = expected.ToString(), FirstLine = origin, Parity = "NOT_VERIFIED (rules read in the original executable, GT216-GT218; no original-runtime run)" }));
        }
        finally
        {
            foreach (var run in spiritRuns) if (session.QuerySpellRuns()[run] is not null) SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            FlushSpellLog(null);
            if (ConversationOpen) EndConversation();
            if (life is not null) session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (used is { } npc) CalmNpcsForSmoke(session, [npc.Entity]);
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelInventoryApproach(null);
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }

    // A dead speaker that is present only through the spoken-with-dead flag is "silent" when it has none: the smoke checks the flag and the presence together.
    private bool SpeakerSpokenWithSilent(AreaSpeaker speaker) => SpeakerDead(speaker) && !SpeakerSpokenWith(speaker);
}
