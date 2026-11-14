using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
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
    // The node of Virgil's DLG 1324 that offers the h: reply (the hub his follower dialog returns to); a script's dialog action can start the DLG at any node (ledger HLR1).
    private const int HealerTalkHubNode = 385;

    // ARC-SCRIPT-1790 (ledger HLR1-HLR11) native observation (stage "healer-talk", only when named, with --hero, on the Crash Site): the installed healer of the start window (Virgil, DLG 1324) talks to the hero through
    // the AREA conversation host and the shown map's AI host. Virgil's own first talk is the story talk (it ends with him joining), so the conversation is opened the way a script's dialog action opens it, at the DLG node
    // that offers the h: reply (BeginScriptedConversation); the healer menu, the first submenu's spell (60) and the price screen follow; the yes pays the quote to the NPC and starts the cast through AreaAiHost.HealerCast:
    // the run carries the free flag exactly when no PC leads the NPC (HLR11), and the hero's hit points rise while the conversation is still open (the AI gate holds the partner, GT138). The hero's hit points are put
    // down first and healed at the end; the gold the stage had to grant is kept. NOT covered: a healer who follows the hero (a companion is not an NPC of the AI host's list, so the reply stays blocked, ledger HLR open limits).
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
        _aiForSmoke = true;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var hurt = false;
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
                .Where(c => c.Facts is { Rank: > 0, NoPcLeader: true })
                .ToArray();
            if (candidates.Length == 0) { GD.Print($"ARC-SCRIPT-1790 native-smoke healer-talk NONE map={map.RuntimeMap} (no leaderless speaker with a healing college rank; speakers={host.Speakers.Count})"); return; }
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

            // The DLG at the hub node, as a script's dialog action opens it.
            var pc = new ScrObjectRef(map.RuntimeMap, AreaScriptHost.PcCode);
            BeginScriptedConversation(speaker, new ScrDialogStart(pc, new ScrObjectRef(map.RuntimeMap, entity), speaker.ScriptId, 0, HealerTalkHubNode));
            Require(ConversationOpen && ReferenceEquals(_conversationSpeaker, speaker), $"the healer's conversation opened ({_conversationNotice})");
            var panel = _conversationPanel!;
            var conversation = panel.Conversation;
            Require(conversation is { Phase: DialogConversationPhase.AwaitingSelection }, $"the talk awaits a reply ({conversation?.Phase} {conversation?.Failure})");
            Require(ConversationPartnerEntity() == entity && ai.Conversation(entity) == Arcanum.Simulation.Ai.AiConversation.Pc, "the AI gate holds the speaker as the hero's partner");
            GD.Print($"ARC-SCRIPT-1790 healer-talk hub options: " + string.Join(" ", conversation!.Options.Select(o => $"[{o.Number} r{o.ResponseId.Value} {o.Transition}{(o.Block is null ? "" : " blocked " + o.Block.Kind)}]")));
            var healer = conversation.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.Healer && o.Block is null);
            Require(healer is not null, "the hub offers the h: reply as a healer reply (not blocked)");
            panel.Choose(healer!.Number);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            conversation = panel.Conversation;
            Require(conversation is { Phase: DialogConversationPhase.AwaitingSelection }, $"the h: reply opened the healer menu ({conversation?.Phase} {conversation?.Failure})");
            var menu = conversation!.Options;
            Require(menu.Count(o => o.Transition == DialogOptionTransition.HealerStep) >= 1 && conversation.NpcLine?.Text.Length > 0, $"the menu offers the healer steps: {string.Join(",", menu.Select(o => o.Transition))}");
            // The skill service (when allowed) is the first option; the first submenu (spells 60 and 62) is the next.
            var submenu = menu.Where(o => o.Transition == DialogOptionTransition.HealerStep).ElementAt(facts.SkillAllowed ? 1 : 0);
            panel.Choose(submenu.Number);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            conversation = panel.Conversation!;
            var spellOption = conversation.Options.First(o => o.Transition == DialogOptionTransition.HealerStep);
            panel.Choose(spellOption.Number);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            conversation = panel.Conversation!;

            // The price screen (no PC leads the NPC: it asks the quote): the quote and a yes and a no.
            var goldBefore = session.QueryGold();
            var npcGoldBefore = session.QueryNpcGold(entity) ?? 0;
            var hpBefore = session.QueryHeroLife()!.Status.HitPoints;
            Require(conversation.Options.Count == 2 && conversation.NpcLine!.Text.Any(char.IsAsciiDigit), $"the price screen quotes a price with a yes and a no ({conversation.Options.Count} options)");
            panel.Choose(conversation.Options[0].Number);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            conversation = panel.Conversation!;
            var paid = goldBefore - session.QueryGold();
            Require(paid is > 0 and <= 300, $"the quote was paid by the hero's own gold ({paid} coins)");
            Require((session.QueryNpcGold(entity) ?? 0) == npcGoldBefore + paid, $"the coins went to the NPC ({npcGoldBefore} to {session.QueryNpcGold(entity)})");

            // The effect: the casting goal starts at once on the NPC (HealerCast, flags 0x2 when no PC leads it) and plays while the conversation is still open.
            var run = ai.RunOf(entity) ?? throw new InvalidOperationException("AREA_HEALER_TALK_SMOKE_FAILED no run after the yes: " + ai.TraceOf(entity)?.Cast);
            Require(run.Bottom.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation && run.Bottom.Target == Arcanum.Simulation.Ai.AiSchedulerRules.Pc, $"the NPC's run is the casting goal on the hero: {run.Bottom.Goal} {run.Bottom.Target}");
            var spellRun = run.Bottom.SpellRun is { } id ? session.QuerySpellRuns()[id] : null;
            Require(spellRun is { Spell: 60 }, $"the run's spell is 60: {spellRun?.Spell}");
            var free = spellRun!.Free;
            Require(free == facts.NoPcLeader, $"the run's free flag is set exactly when no PC leads the NPC (free={free}, no PC leader={facts.NoPcLeader}) (HLR11)");
            Require(ConversationOpen && ConversationPartnerEntity() == entity, "the conversation is still open while the cast plays");
            for (var f = 0; f < 400 && ai.RunOf(entity) is not null; f++) { TickAreaClock(_talkSmokeHostMs += 50); AreaSmokeFrame(); }
            FlushSpellLog(null);
            var hpAfter = session.QueryHeroLife()!.Status.HitPoints;
            GD.Print($"ARC-SCRIPT-1790 healer-talk paid {paid} coins; hit points {hpBefore} to {hpAfter} of {max}; run {ai.TraceOf(entity)?.Cast}");
            Require(ai.RunOf(entity) is null, "the casting goal ended within 400 frames");
            Require(hpAfter > hpBefore, $"the hero's hit points rose with the conversation open ({hpBefore} to {hpAfter})");
            Require(ConversationOpen, "the conversation stayed open");
            // The NPC's line 15000 and the PC's class line 1000 as the only way on.
            Require(panel.Conversation is { Phase: DialogConversationPhase.AwaitingSelection, Options.Count: 1 }, $"the talk goes on with its one way out ({panel.Conversation?.Phase})");
            for (var close = 0; close < 4 && ConversationOpen; close++) panel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen && !WorldModal, "Close ends the modal panel");
            GD.Print("ARC-SCRIPT-1790 native-smoke healer-talk PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = map.RuntimeMap, Speaker = speaker.ScriptId, Node = HealerTalkHubNode, Spell = 60, Free = free, Paid = paid,
                HitPointsBefore = hpBefore, HitPointsAfter = hpAfter, GoldGranted = granted, Parity = "NOT_VERIFIED (rules read in the original executable, HLR1-HLR12; no original-runtime run)" }));
        }
        finally
        {
            if (ConversationOpen) EndConversation();
            if (hurt) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelInventoryApproach(null);
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
