using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // The node of Virgil's DLG 1324 that offers the h: reply (the hub his follower dialog returns to); a script's dialog action can start the DLG at any node (ledger HLR1).
    private const int HealerTalkHubNode = 385, HealerTalkScript = 1324;

    // ARC-SCRIPT-1790 (ledger HLR1-HLR11) native observation (stage "healer-talk", only when named, with --hero, on the Crash Site): the installed healer of the start window (Virgil, DLG 1324) talks to the hero through
    // the AREA conversation host and the shown map's AI host. Virgil's own first talk is the story talk (it ends with him joining), so the conversation is opened the way a script's dialog action opens it, at the DLG node
    // that offers the h: reply (BeginScriptedConversation); the healer menu, the first submenu's spell (60) and the price screen follow; the yes pays the quote to the NPC and starts the cast through AreaAiHost.HealerCast:
    // the run carries the free flag exactly when no PC leads the NPC (HLR11), and the hero's hit points rise while the conversation is still open (the AI gate holds the partner, GT138). The hero's hit points are put
    // down first and healed at the end; the gold the stage had to grant is kept. ARC-SCRIPT-1996 second pass: Virgil joins the hero (a companion, run through the host's follower list) and the same DLG node is
    // opened again: the h: reply is offered, the pick of the spell starts the cast at once (no price screen, no gold paid, no free flag: the hero leads him, HLR8, HLR11) and the hero's hit points rise.
    // ARC-DEV-2074 (0x567460, the order of the dialog start's refusals, both the click's and the script's way in): before the healer talks, the hero dead (silent, before the can-speak test), then Virgil in combat focus
    // with the hero in combat mode (can-speak answer 2, not the combat-mode refusal; the hero stays in it), the dead hero's refusal with a walk order of the NPC that it must not end; at the open conversation the walk order is
    // ended by the can-speak test's level-2 interrupt although the second-dialog refusal follows (the click's and the script's way), and the cast run of the leaderless pass (priority 3) answers 3 and stays.
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
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime; var takesScripted = _smokeTakesScriptedConversations;
        _worldFocusSimulationForSelfTest = true; _smokeTakesScriptedConversations = true;
        var aiBefore = _aiForSmoke;
        _aiForSmoke = true;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var hurt = false; var paidBack = (Entity: (string?)null, Coins: 0); string? joinedHealer = null;
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
                .Select(s => (Speaker: s, Entity: AreaConversationState.EntityId(s.Row), Facts: ai.HealerFacts(AreaConversationState.EntityId(s.Row))))
                .Where(c => c.Speaker.ScriptId == HealerTalkScript && c.Facts is { Rank: > 0, NoPcLeader: true })
                .ToArray();
            if (candidates.Length == 0) { GD.Print($"ARC-SCRIPT-1790 native-smoke healer-talk NONE map={map.RuntimeMap} (no leaderless speaker of script 1324 (Virgil) with a healing college rank; speakers={host.Speakers.Count})"); return; }
            var (speaker, entity, firstFacts) = candidates[0];
            var facts = firstFacts!.Value;
            GD.Print($"ARC-SCRIPT-1790 healer-talk speaker={speaker.ScriptId} rank={facts.Rank} skill-allowed={facts.SkillAllowed} no-pc-leader={facts.NoPcLeader} led={facts.LedByPc}");

            // A standable cell 2..4 cells from the NPC, so the talk stands within reach.
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
            Require(place is not null, "a standable cell two to four cells from the healer");
            AreaSmokePlace(map, place!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            for (var i = 0; i < 10; i++) AreaSmokeFrame();
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);

            // ARC-DEV-2074: a cell 5..8 cells from the NPC for its walk orders (the runs the refusals must leave alone, or the interrupt must end).
            CellCoord? walkCell = null;
            foreach (var (dx, dy) in new[] { (6, 0), (-6, 0), (0, 6), (0, -6), (5, 5), (-5, -5), (5, -5), (-5, 5), (8, 0), (-8, 0), (0, 8), (0, -8) })
            {
                var far = new CellCoord(at.X + dx, at.Y + dy);
                if (!map.Chart.Contains(far)) continue;
                try { walkCell = AreaSmokeStandable(map, far, 1); break; } catch (InvalidOperationException) { }
            }
            Require(walkCell is not null, "a standable cell for the healer's walk orders");
            var dialogStart = new ScrDialogStart(new ScrObjectRef(map.RuntimeMap, AreaScriptHost.PcCode), new ScrObjectRef(map.RuntimeMap, entity), speaker.ScriptId, 0, HealerTalkHubNode);
            void WalkOrder(AreaAiHost aiHost, string why)
            {
                var order = aiHost.OrderWalk(entity, walkCell!.Value);
                Require(aiHost.RunOf(entity) is not null, $"{why}: the walk order gave the NPC a run ({order})");
            }
            // The two ways in a dialog start comes: a click's talk goal (BeginConversation, replacing windows) and a script's dialog action.
            void StartBothWays(Action<string> check)
            {
                foreach (var way in new[] { "click", "script" })
                {
                    _conversationNotice = null; _combatModeNotice = "";
                    if (way == "click") BeginConversation(speaker, replaceWindows: true); else BeginScriptedConversation(speaker, dialogStart);
                    Require(!ConversationOpen && !WorldModal, $"[{way}] no dialog opened");
                    check(way);
                }
            }

            // 1. The hero down: the first refusal, silent, before the can-speak test, so the NPC's walk is not interrupted and nothing is said.
            WalkOrder(ai, "hero down");
            hurt = true;
            Require(session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1_000_000 }).Disposition == GameSessionDisposition.Changed, "the hero went down");
            // No frame runs while the hero is down (the death cover starts in a frame).
            Require(HeroDownState() is (true, _) or (_, true), $"the hero is down: {HeroDownState()}");
            StartBothWays(way =>
            {
                Require(_conversationNotice?.Contains("hero is dead or unconscious", StringComparison.Ordinal) == true, $"[{way}] the dead hero's refusal is the first one: {_conversationNotice}");
                Require(ai.RunOf(entity) is not null, $"[{way}] the NPC's walk is untouched: the can-speak test's interrupt did not run");
            });
            Require(session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true }).Disposition != GameSessionDisposition.Rejected && HeroDownState() == (false, false), "the hero is up again");
            ai.InterruptForTalk(entity);
            Require(ai.RunOf(entity) is null, "the NPC's walk is ended for the next steps");

            // 2. The NPC in combat focus on the hero (danger state 1) and the hero in combat mode: the can-speak test refuses (answer 2) before the combat-mode leave, which would refuse too (under attack): the notice says which came first.
            Require(session.SetCombatMode(session.Version, SessionCombatant.Hero, true, SessionCombatModeContext.RealTime).Disposition != GameSessionDisposition.Rejected && HeroInCombatMode, "the hero is in combat mode");
            // The attack event (the way the other smokes make a fight) also lowers the NPC's reaction toward the hero, which stays: a fight on the hero needs the reaction record, so the danger state is not written alone.
            ai.HeroAttacked(entity);
            Require(session.QueryDanger(entity) is { State: Arcanum.Simulation.Ai.DangerState.CombatFocus }, $"the NPC fights the hero: {session.QueryDanger(entity).State}");
            StartBothWays(way =>
            {
                Require(_conversationNotice?.Contains("cannot speak (answer 2)", StringComparison.Ordinal) == true, $"[{way}] the can-speak test refused first, with answer 2: {_conversationNotice}");
                Require(HeroInCombatMode && !_combatModeNotice.Contains("under attack", StringComparison.Ordinal), $"[{way}] the combat-mode leave was not asked; the hero stays in combat mode: {_combatModeNotice}");
            });
            CalmNpcsForSmoke(session, [entity]);
            Require(session.QueryDanger(entity).State == Arcanum.Simulation.Ai.DangerState.None, "the NPC is calm again");
            session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            Require(!HeroInCombatMode, "the hero is out of combat mode again");
            ai.InterruptForTalk(entity);
            Require(ai.RunOf(entity) is null, $"the fight left the NPC no run for the talks that follow: {ai.TraceOf(entity)?.Perform}");
            hurt = false;

            // The hero is hurt so the heal shows, and holds the gold the quote asks (the reaction scale keeps it under 300 for the base 100).
            var max = session.QueryHeroLife()!.Status.MaxHitPoints;
            Require(session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = max / 2 }).Disposition == GameSessionDisposition.Changed, "the hero is hurt");
            hurt = true;
            var granted = 0;
            if (session.QueryGold() < 300)
            {
                granted = 300 - session.QueryGold();
                Require(session.GiveGoldToHero(session.Version, granted).Disposition == GameSessionDisposition.Changed, "the hero got the gold the quote needs");
            }

            // One talk with the healer at the hub node: the leaderless pass pays the quote at the price screen; the led pass (the hero leads him, HLR7, HLR8) is served at once and free.
            async Task<(int Paid, int HpBefore, int HpAfter, bool Free)> TalkAsync(AreaSpeaker talker, bool led, bool skillAllowed)
            {
                var pass = led ? "led" : "leaderless";
                var talkAi = AreaAiHostNow() ?? throw new InvalidOperationException("AREA_HEALER_TALK_SMOKE_FAILED no AI host");
                // The DLG at the hub node, as a script's dialog action opens it.
                var pc = new ScrObjectRef(map.RuntimeMap, AreaScriptHost.PcCode);
                BeginScriptedConversation(talker, new ScrDialogStart(pc, new ScrObjectRef(map.RuntimeMap, entity), talker.ScriptId, 0, HealerTalkHubNode));
                Require(ConversationOpen && ReferenceEquals(_conversationSpeaker, talker), $"[{pass}] the healer's conversation opened ({_conversationNotice})");
                if (!led)
                {
                    // ARC-DEV-2074: with the conversation open (the second dialog), the can-speak test's level-2 interrupt still ends the NPC's walk before the second-dialog refusal, by the click's way and by the script's.
                    foreach (var way in new[] { "click", "script" })
                    {
                        WalkOrder(talkAi, $"[{way}] open conversation");
                        if (way == "click") BeginConversation(talker, replaceWindows: true); else BeginScriptedConversation(talker, dialogStart);
                        Require(ConversationOpen && ReferenceEquals(_conversationSpeaker, talker), $"[{way}] the second dialog opened nothing: the first one stays");
                        Require(talkAi.RunOf(entity) is null, $"[{way}] the interrupt of the can-speak test stayed after the second-dialog refusal");
                    }
                }
                var panel = _conversationPanel!;
                var conversation = panel.Conversation;
                Require(conversation is { Phase: DialogConversationPhase.AwaitingSelection }, $"[{pass}] the talk awaits a reply ({conversation?.Phase} {conversation?.Failure})");
                Require(ConversationPartnerEntity() == entity && talkAi.Conversation(entity) == Arcanum.Simulation.Ai.AiConversation.Pc, $"[{pass}] the AI gate holds the speaker as the hero's partner");
                GD.Print($"ARC-SCRIPT-1790 healer-talk [{pass}] hub options: " + string.Join(" ", conversation!.Options.Select(o => $"[{o.Number} r{o.ResponseId.Value} {o.Transition}{(o.Block is null ? "" : " blocked " + o.Block.Kind)}]")));
                var healer = conversation.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.Healer && o.Block is null);
                Require(healer is not null, $"[{pass}] the hub offers the h: reply as a healer reply (not blocked)");
                panel.Choose(healer!.Number);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                conversation = panel.Conversation;
                Require(conversation is { Phase: DialogConversationPhase.AwaitingSelection }, $"[{pass}] the h: reply opened the healer menu ({conversation?.Phase} {conversation?.Failure})");
                var menu = conversation!.Options;
                Require(menu.Count(o => o.Transition == DialogOptionTransition.HealerStep) >= 1 && conversation.NpcLine?.Text.Length > 0, $"[{pass}] the menu offers the healer steps: {string.Join(",", menu.Select(o => o.Transition))}");
                // The skill service (when allowed) is the first option; the first submenu (spells 60 and 62) is the next.
                var submenu = menu.Where(o => o.Transition == DialogOptionTransition.HealerStep).ElementAt(skillAllowed ? 1 : 0);
                panel.Choose(submenu.Number);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                conversation = panel.Conversation!;
                var spellOption = conversation.Options.First(o => o.Transition == DialogOptionTransition.HealerStep);
                panel.Choose(spellOption.Number);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                conversation = panel.Conversation!;

                var goldBefore = session.QueryGold();
                var npcGoldBefore = session.QueryNpcGold(entity) ?? 0;
                var hpBefore = session.QueryHeroLife()!.Status.HitPoints;
                int paid;
                if (!led)
                {
                    // The price screen (no PC leads the NPC: it asks the quote): the quote and a yes and a no.
                    Require(conversation.Options.Count == 2 && conversation.NpcLine!.Text.Any(char.IsAsciiDigit), $"[{pass}] the price screen quotes a price with a yes and a no ({conversation.Options.Count} options)");
                    panel.Choose(conversation.Options[0].Number);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    conversation = panel.Conversation!;
                    paid = goldBefore - session.QueryGold();
                    paidBack = (entity, paid);
                    Require(paid is > 0 and <= 300, $"[{pass}] the quote was paid by the hero's own gold ({paid} coins)");
                    Require((session.QueryNpcGold(entity) ?? 0) == npcGoldBefore + paid, $"[{pass}] the coins went to the NPC ({npcGoldBefore} to {session.QueryNpcGold(entity)})");
                }
                else
                {
                    // The hero leads him (0x45DE00 is the PC): the pick of the spell is the effect, with no price screen and no coin moving.
                    paid = goldBefore - session.QueryGold();
                    Require(paid == 0 && (session.QueryNpcGold(entity) ?? 0) == npcGoldBefore, $"[{pass}] no coin moved ({paid} paid)");
                    Require(conversation.Options.Count == 1, $"[{pass}] no price screen (yes and no) follows the pick, only the one way out ({conversation.Options.Count} options)");
                }

                // The effect: the casting goal starts at once on the NPC (HealerCast, flags 0x2 when no PC leads it) and plays while the conversation is still open.
                var run = talkAi.RunOf(entity) ?? throw new InvalidOperationException($"AREA_HEALER_TALK_SMOKE_FAILED [{pass}] no run after the pick: " + talkAi.TraceOf(entity)?.Cast);
                Require(run.Bottom.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation && run.Bottom.Target == Arcanum.Simulation.Ai.AiSchedulerRules.Pc, $"[{pass}] the NPC's run is the casting goal on the hero: {run.Bottom.Goal} {run.Bottom.Target}");
                var spellRun = run.Bottom.SpellRun is { } id ? session.QuerySpellRuns()[id] : null;
                Require(spellRun is { Spell: 60 }, $"[{pass}] the run's spell is 60: {spellRun?.Spell}");
                if (!led)
                {
                    // ARC-DEV-2074: the cast goal (priority 3) refuses the level-2 interrupt: the can-speak test answers 3 and leaves the run as it was.
                    Require(DialogStartCanSpeak(talker) == Arcanum.Simulation.Reaction.CanSpeakRules.Busy && ReferenceEquals(talkAi.RunOf(entity), run), $"[{pass}] the priority-3 cast run answers can-speak 3 and stays");
                }
                var free = spellRun!.Free;
                Require(free == !led, $"[{pass}] the run's free flag is set exactly when no PC leads the NPC (free={free}, led={led}) (HLR11)");
                Require(ConversationOpen && ConversationPartnerEntity() == entity, $"[{pass}] the conversation is still open while the cast plays");
                for (var f = 0; f < 400 && talkAi.RunOf(entity) is not null; f++) { TickAreaClock(_talkSmokeHostMs += 50); AreaSmokeFrame(); }
                FlushSpellLog(null);
                var hpAfter = session.QueryHeroLife()!.Status.HitPoints;
                GD.Print($"ARC-SCRIPT-1790 healer-talk [{pass}] paid {paid} coins; hit points {hpBefore} to {hpAfter} of {max}; run {talkAi.TraceOf(entity)?.Cast}");
                Require(talkAi.RunOf(entity) is null, $"[{pass}] the casting goal ended within 400 frames");
                Require(hpAfter > hpBefore, $"[{pass}] the hero's hit points rose with the conversation open ({hpBefore} to {hpAfter})");
                Require(session.QueryGold() == goldBefore - paid, $"[{pass}] no other gold moved");
                Require(ConversationOpen, $"[{pass}] the conversation stayed open");
                // The NPC's line 15000 and the PC's class line 1000 as the only way on.
                Require(panel.Conversation is { Phase: DialogConversationPhase.AwaitingSelection, Options.Count: 1 }, $"[{pass}] the talk goes on with its one way out ({panel.Conversation?.Phase})");
                for (var close = 0; close < 4 && ConversationOpen; close++) panel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!ConversationOpen && !WorldModal, $"[{pass}] Close ends the modal panel");
                return (paid, hpBefore, hpAfter, free);
            }

            var first = await TalkAsync(speaker, led: false, facts.SkillAllowed);

            // ARC-SCRIPT-1996: Virgil follows the hero (the session's forced join, as the follower smokes join a speaker), is run by the AI host as a companion, and the same node is opened again: the AI host's NPC list
            // does not hold him any more, so the reply was blocked before this task. The hero is hurt again and the reply is served at once.
            var template = _companionTemplates.SingleOrDefault(t => t.Entity == entity)
                ?? throw new InvalidOperationException("AREA_HEALER_TALK_SMOKE_FAILED Virgil is no companion template here");
            Require(session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true }).Disposition != GameSessionDisposition.Rejected
                && session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = max / 2 }).Disposition == GameSessionDisposition.Changed, "the hero is hurt again");
            var join = session.ForceJoinParty(session.Version, entity);
            Require(join.Disposition != GameSessionDisposition.Rejected, "the healer joined the party: " + join.Failure);
            joinedHealer = entity;
            AreaSmokePlace(map, place!.Value);
            long Near() => _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == entity) is { } now
                ? Arcanum.Simulation.Party.FollowDistanceRules.Distance(now.Grid.State.CommittedCell, place!.Value) : long.MaxValue;
            for (var f = 0; f < 600 && !(AreaAiHostNow() is { } runner && runner.RunCompanions.Contains(entity) && Near() <= 4); f++) AreaSmokeFrame();
            var followerAi = AreaAiHostNow();
            Require(followerAi is not null && followerAi.RunCompanions.Contains(entity) && Near() <= 4, $"the AI host runs the healer as a companion near the hero: {_areaCompanions?.Status()}");
            var companionFacts = followerAi!.HealerFacts(entity);
            Require(companionFacts is { LedByPc: true, NoPcLeader: false }, $"the host answers for the companion: {companionFacts}");
            var second = await TalkAsync(template.Speaker, led: true, companionFacts!.Value.SkillAllowed);
            session.LeaveParty(session.Version, entity, new PartyLeave(Force: true));
            joinedHealer = null;
            for (var f = 0; f < 20; f++) AreaSmokeFrame();

            GD.Print("ARC-SCRIPT-1790 native-smoke healer-talk PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = map.RuntimeMap, Speaker = speaker.ScriptId, Node = HealerTalkHubNode, Spell = 60, Free = first.Free, Paid = first.Paid,
                HitPointsBefore = first.HpBefore, HitPointsAfter = first.HpAfter, GoldGranted = granted, FollowerFree = second.Free, FollowerPaid = second.Paid, FollowerHitPointsBefore = second.HpBefore, FollowerHitPointsAfter = second.HpAfter,
                Parity = "NOT_VERIFIED (rules read in the original executable, HLR1-HLR12; no original-runtime run)" }));
        }
        finally
        {
            if (ConversationOpen) EndConversation();
            // The quote goes back from the NPC to the hero (the take gives the coins to the hero), so the later stages see the economy as it was.
            if (joinedHealer is { } joiner) session.LeaveParty(session.Version, joiner, new PartyLeave(Force: true));
            if (paidBack.Entity is { } payee && paidBack.Coins > 0) session.TakeGoldFromNpc(session.Version, payee, paidBack.Coins);
            if (hurt) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelInventoryApproach(null);
            RestoreHeroForSmoke(session, heroBefore);
            if (!aiBefore) { _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null; }
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
