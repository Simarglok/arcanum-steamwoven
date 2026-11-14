using Arcanum.Scripting.Dialog;
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
    // ARC-SCRIPT-1790 (ledger HLR1-HLR11) native observation (stage "healer-talk", only when named, with --hero, on a map whose dialog NPC offers the h: reply, the Crash Site): the hero's talk click opens an
    // installed healer's conversation through the AREA conversation host (the speaker is an NPC of the shown map's AI host), the dialog is walked to the h: reply, the healer menu, the first submenu's
    // spell (60) and the price screen; the yes pays the quote to the NPC and starts the cast through AreaAiHost.HealerCast: the run carries the free flag exactly when no PC leads the NPC (HLR11) and the hero's hit
    // points rise while the conversation is still open (the AI gate holds the partner, GT138). The hero's hit points are put down first and restored at the end; the gold the stage had to grant is kept.
    private async Task RunAreaHealerTalkSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _conversationHost is not { } host || _readyArea is not { } area || !InventoryActive)
        { GD.Print("ARC-SCRIPT-1790 native-smoke healer-talk SKIP (needs --hero, an area session, its combat and conversation hosts and the inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-1790 healer-talk smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_HEALER_TALK_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var hurt = false;
        string? joined = null;
        var passed = new List<object>();
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SpellCasterNow() is not null, "the map's cast host is built (spell data " + _spellData?.Status + ")");
            var ai = AreaAiHostNow();
            if (ai is null) { GD.Print("ARC-SCRIPT-1790 native-smoke healer-talk NONE (no AI host)"); return; }
            // The dialog NPCs of the map the AI host runs whose college 12 rank is above 0 (the host's spell facts): a healer's menu has a spell to buy.
            var candidates = host.Speakers
                .Where(s => SpeakerPresent(s) && s.Row.WorldX is not null && s.Row.WorldY is not null)
                .Select(s => (Speaker: s, Entity: AreaConversationState.EntityId(s.Row)))
                .Select(c => (c.Speaker, c.Entity, Facts: ai.HealerFacts(c.Entity)))
                .Where(c => c.Facts is { Rank: > 0 })
                .ToArray();
            if (candidates.Length == 0) { GD.Print($"ARC-SCRIPT-1790 native-smoke healer-talk NONE map={map.RuntimeMap} (no speaker with a healing college rank; speakers={host.Speakers.Count})"); return; }
            var tried = new List<string>();
            foreach (var (speaker, entity, _) in candidates)
            {
                // A standable cell 2..4 cells from the NPC, so the talk click walks at most a few steps.
                var at = map.Chart.ToChart(new(speaker.Row.WorldX!.Value, speaker.Row.WorldY!.Value));
                CellCoord? place = null;
                foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (2, 2), (-2, -2), (2, -2), (-2, 2), (4, 0), (-4, 0), (0, 4), (0, -4) })
                {
                    var near = new CellCoord(at.X + dx, at.Y + dy);
                    if (!map.Chart.Contains(near)) continue;
                    CellCoord cell;
                    try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                    if (Math.Max(Math.Abs(cell.X - at.X), Math.Abs(cell.Y - at.Y)) is >= 2 and <= 4) { place = cell; break; }
                }
                if (place is null) { tried.Add($"{speaker.ScriptId}: no standable cell"); continue; }
                AreaSmokePlace(map, place.Value);
                _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                PublishWorldSelection();
                for (var i = 0; i < 10; i++) AreaSmokeFrame();
                if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
                // First the NPC as it stands (no PC leads it: the quote is paid and the cast is free), then with the hero leading it (served at once, no quote, the normal cost).
                foreach (var lead in new[] { false, true })
                {
                if (lead)
                {
                    Require(session.ForceJoinParty(session.Version, entity).Disposition == GameSessionDisposition.Changed, "the healer joins the party");
                    joined = entity;
                    for (var i = 0; i < 10; i++) { TickAreaClock(_talkSmokeHostMs += 50); AreaSmokeFrame(); }
                    ai = AreaAiHostNow() ?? throw new InvalidOperationException("AREA_HEALER_TALK_SMOKE_FAILED no AI host after the join");
                }
                var facts = ai.HealerFacts(entity);
                if (facts is null) { tried.Add($"{speaker.ScriptId}: not an NPC of the AI host (lead {lead})"); continue; }
                // The hero is hurt so the heal shows, and holds the gold the quote asks (the reaction scale keeps it under 300 for the base 100).
                var hit = session.QueryHeroLife()!;
                var max = hit.Status.MaxHitPoints;
                Require(session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = max / 2 }).Disposition == GameSessionDisposition.Changed, "the hero is hurt");
                hurt = true;
                var granted = 0;
                if (session.QueryGold() < 300)
                {
                    granted = 300 - session.QueryGold();
                    Require(session.GiveGoldToHero(session.Version, granted).Disposition == GameSessionDisposition.Changed, "the hero got the gold the quote needs");
                }
                // The talk click (goal 21): the hero walks within reach and the dialog opens.
                _voiceStart = null;
                var click = HeroRequests.Apply(new ObjectClickRequest(TalkTargetOf(speaker)));
                Require(click.Click == HeroClickGoal.Talk && click.Outcome == HeroRequestOutcome.Done, $"the click on the healer talks (goal 21): {click.Click} {click.Outcome}");
                var frames = 0;
                for (; frames < 600 && !ConversationOpen && _voiceStart is null; frames++) AreaSmokeTalkFrame();
                if (!ConversationOpen) { tried.Add($"{speaker.ScriptId}: no window ({_voiceStart?.Status} {_conversationNotice})"); continue; }
                var panel = _conversationPanel!;
                var conversation = panel.Conversation;
                Require(ReferenceEquals(_conversationSpeaker, speaker), "the window is the healer's talk");
                Require(ConversationPartnerEntity() == entity && ai.Conversation(entity) == Arcanum.Simulation.Ai.AiConversation.Pc, "the AI gate holds the speaker as the hero's partner");

                // The dialog to the h: reply: the first reply a player could choose, up to 40 turns.
                DialogOption? healer = null;
                for (var turn = 0; turn < 40 && conversation is { Phase: DialogConversationPhase.AwaitingSelection }; turn++)
                {
                    healer = conversation.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.Healer && o.Block is null);
                    if (healer is not null) break;
                    var option = conversation.Options.FirstOrDefault(o => o.Block is null);
                    if (option is null) break;
                    panel.Choose(option.Number);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (!ReferenceEquals(panel.Conversation, conversation)) { conversation = panel.Conversation; }
                }
                if (healer is null)
                {
                    tried.Add($"{speaker.ScriptId}: no h: reply reached (phase {conversation?.Phase} {conversation?.Failure})");
                    for (var close = 0; close < 4 && ConversationOpen; close++) EndConversation();
                    continue;
                }
                GD.Print($"ARC-SCRIPT-1790 healer-talk speaker={speaker.ScriptId} rank={facts!.Value.Rank} skill-allowed={facts.Value.SkillAllowed} no-pc-leader={facts.Value.NoPcLeader} led={facts.Value.LedByPc}");
                panel.Choose(healer.Number);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                conversation = panel.Conversation;
                Require(conversation is { Phase: DialogConversationPhase.AwaitingSelection }, $"the h: reply opened the healer menu ({conversation?.Phase} {conversation?.Frontier})");
                var menu = conversation!.Options;
                Require(menu.Count(o => o.Transition == DialogOptionTransition.HealerStep) >= 1 && conversation.NpcLine?.Text.Length > 0, $"the menu offers the healer steps: {string.Join(",", menu.Select(o => o.Transition))}");
                // The skill service (when allowed) is the first option; the first submenu (spells 60 and 62) is the next.
                var submenu = menu.Where(o => o.Transition == DialogOptionTransition.HealerStep).ElementAt(facts.Value.SkillAllowed ? 1 : 0);
                panel.Choose(submenu.Number);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                conversation = panel.Conversation!;
                var spellOption = conversation.Options.First(o => o.Transition == DialogOptionTransition.HealerStep);
                panel.Choose(spellOption.Number);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                conversation = panel.Conversation!;
                // The price screen (a PC with no leader pays; a follower would be served at once and free): the quote and a yes and a no.
                var led = facts.Value.LedByPc;
                var goldBefore = session.QueryGold();
                var npcGoldBefore = session.QueryNpcGold(entity) ?? 0;
                var hpBefore = session.QueryHeroLife()!.Status.HitPoints;
                if (!led)
                {
                    Require(conversation.Options.Count == 2 && conversation.NpcLine!.Text.Any(char.IsAsciiDigit), $"the price screen quotes a price with a yes and a no ({conversation.Options.Count} options)");
                    panel.Choose(conversation.Options[0].Number);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    conversation = panel.Conversation!;
                    var paid = goldBefore - session.QueryGold();
                    Require(paid is > 0 and <= 300, $"the quote was paid by the hero's own gold ({paid} coins)");
                    Require((session.QueryNpcGold(entity) ?? 0) == npcGoldBefore + paid, $"the coins went to the NPC ({npcGoldBefore} to {session.QueryNpcGold(entity)})");
                    GD.Print($"ARC-SCRIPT-1790 healer-talk paid {paid} coins");
                }
                // The effect: the casting goal starts at once on the NPC (HealerCast, flags 0x2 when no PC leads it), and plays while the conversation is still open.
                var run = ai.RunOf(entity) ?? throw new InvalidOperationException("AREA_HEALER_TALK_SMOKE_FAILED no run after the yes: " + ai.TraceOf(entity)?.Cast);
                Require(run.Bottom.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation && run.Bottom.Target == Arcanum.Simulation.Ai.AiSchedulerRules.Pc, $"the NPC's run is the casting goal on the hero: {run.Bottom.Goal} {run.Bottom.Target}");
                var spellRun = run.Bottom.SpellRun is { } id ? session.QuerySpellRuns()[id] : null;
                Require(spellRun is { Spell: 60 }, $"the run's spell is 60: {spellRun?.Spell}");
                Require(spellRun!.Free == facts.Value.NoPcLeader, $"the run's free flag is set exactly when no PC leads the NPC (free={spellRun!.Free}, no PC leader={facts.Value.NoPcLeader}) (HLR11)");
                Require(ConversationOpen && ConversationPartnerEntity() == entity, "the conversation is still open while the cast plays");
                for (var f = 0; f < 400 && ai.RunOf(entity) is not null; f++) { TickAreaClock(_talkSmokeHostMs += 50); AreaSmokeFrame(); }
                FlushSpellLog(null);
                var hpAfter = session.QueryHeroLife()!.Status.HitPoints;
                GD.Print($"ARC-SCRIPT-1790 healer-talk hit points {hpBefore} to {hpAfter} of {max}; run {ai.TraceOf(entity)?.Cast}");
                Require(ai.RunOf(entity) is null, "the casting goal ended within 400 frames");
                Require(hpAfter > hpBefore, $"the hero's hit points rose with the conversation open ({hpBefore} to {hpAfter})");
                Require(ConversationOpen, "the conversation stayed open");
                // The NPC's line 15000 and the PC's class line 1000 as the only way on.
                Require(panel.Conversation is { Phase: DialogConversationPhase.AwaitingSelection, Options.Count: 1 }, $"the talk goes on with its one way out ({panel.Conversation?.Phase})");
                for (var close = 0; close < 4 && ConversationOpen; close++) panel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!ConversationOpen && !WorldModal, "Close ends the modal panel");
                passed.Add(new { Speaker = speaker.ScriptId, Led = led, Free = spellRun.Free, HitPointsBefore = hpBefore, HitPointsAfter = hpAfter, GoldGranted = granted });
                GD.Print($"ARC-SCRIPT-1790 healer-talk mode led={led} done: free={spellRun.Free} hit points {hpBefore} to {hpAfter}");
                if (joined is not null) { session.LeaveParty(session.Version, joined, new PartyLeave(Force: true)); joined = null; }
                }
                if (passed.Count > 0)
                {
                    GD.Print("ARC-SCRIPT-1790 native-smoke healer-talk PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = map.RuntimeMap, Spell = 60, Modes = passed, Tried = tried,
                        Parity = "NOT_VERIFIED (rules read in the original executable, HLR1-HLR12; no original-runtime run)" }));
                    return;
                }
            }
            GD.Print($"ARC-SCRIPT-1790 native-smoke healer-talk NONE map={map.RuntimeMap} (no healer talk reached its menu: {string.Join("; ", tried)})");
        }
        finally
        {
            if (ConversationOpen) EndConversation();
            if (joined is not null) session.LeaveParty(session.Version, joined, new PartyLeave(Force: true));
            if (hurt) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelInventoryApproach(null);
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
