using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-008 over the strict synthetic AREA owner in the production app: L opens the logbook without a hero,
    /// lists the session's rumors and quests in time order with synthetic texts and the original's styles, owns the keys,
    /// switches tabs with Tab and the controller, keeps the tab across close and open, follows the session, and a dead hero
    /// closes and refuses it.</summary>
    private static async Task AssertAreaLogbookAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = app.AreaLogbookForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-008 self-test PASS synthetic logbook ({summary}); original texts covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    /// <summary>ARC-INPUT-004 through the production key route: each window letter (L, C, I, T, R and W) does nothing with Ctrl,
    /// Alt, Command or Ctrl+Shift held (no window, status, notice or reserved report), and acts with Shift as bare; an open
    /// window's letter does not close it under Ctrl. W is routed on a named map, so the check names one for its silent half.</summary>
    private void AssertWindowKeyModifiers(Action<bool, string> require)
    {
        void Press(Key key, WorldKeyModifiers modifiers)
        {
            foreach (var pressed in new[] { true, false })
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = modifiers.HasFlag(WorldKeyModifiers.Ctrl),
                    ShiftPressed = modifiers.HasFlag(WorldKeyModifiers.Shift), AltPressed = modifiers.HasFlag(WorldKeyModifiers.Alt), MetaPressed = modifiers.HasFlag(WorldKeyModifiers.Meta) });
        }
        string Windows() => string.Join("|", LogbookOpen, SheetOpen, InventoryOpen, SchematicsOpen, WorldMapOpen, WorldModal, HeroInCombatMode,
            _logbookStatus, _sheetStatus, _inventoryStatus, _schematicsStatus, _worldMapStatus, _combatModeNotice, _worldLastReserved);
        var silent = new[] { WorldKeyModifiers.Ctrl, WorldKeyModifiers.Alt, WorldKeyModifiers.Meta, WorldKeyModifiers.Ctrl | WorldKeyModifiers.Shift };
        var mapName = _readyMapName;
        _readyMapName ??= "synthetic-map";
        try
        {
            require(LogbookActive && SheetActive && SchematicsActive && CombatModeActive && WorldMapActive, "the window keys are routed");
            foreach (var key in new[] { Key.L, Key.C, Key.I, Key.T, Key.R, Key.W })
                foreach (var modifiers in silent)
                {
                    var before = Windows();
                    Press(key, modifiers);
                    require(Windows() == before, $"{modifiers}+{key} did something: {before} -> {Windows()}");
                }
        }
        finally { _readyMapName = mapName; }
        // Shift gives the capital, the same action as the bare letter (the original's character table has both cases).
        foreach (var key in new[] { Key.C, Key.T, Key.R })
        {
            var before = Windows();
            Press(key, WorldKeyModifiers.Shift);
            require(Windows() != before && !WorldModal, $"Shift+{key} acted as {key} (no hero: a refusal status)");
        }
        Press(Key.L, WorldKeyModifiers.Shift);
        require(LogbookOpen && WorldModal, "Shift+L opened the logbook");
        Press(Key.L, WorldKeyModifiers.Ctrl);
        require(LogbookOpen, "Ctrl+L did not close the open logbook");
        Press(Key.L, WorldKeyModifiers.Shift);
        require(!LogbookOpen && !WorldModal, "Shift+L closed the logbook");
    }

    internal string AreaLogbookForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA logbook: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_LOGBOOK_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // Synthetic texts (invented, ASCII): quest 1000 and 1001 described, rumor 1001 and 1002 with text at 20 × number.
            static MesFile Mes(string text) => MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(text)).File!;
            _logbookTextsTask = null; _logbookStamp = null;
            _logbookTexts = new(AreaLogbookTextsStatus.Ready, LogbookTexts.Create(true, Mes("{1000}{Find the [lost] gear.}\n{1001}{Mend the pump.}"),
                Mes("{1000}{Gear lost.}"), Mes("{20020}{The mill is haunted.}\n{20040}{The well is dry.}"), Mes("{20020}{Mill bad.}")), "synthetic");

            var session = _areaSession;
            Require(session.QueryHero() is null, "the synthetic session starts without a hero");
            InventoryKey(Key.L);
            Require(LogbookOpen && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen && _logbookPanel!.ViewForSelfTest is { Tab: AreaLogbookTab.Rumors, Entries.Count: 0, LowIntelligence: false },
                "L opened an empty rumors tab without a hero (modal)");
            Require(_worldLastReserved?.Contains("Logbook") != true, "L is not reported as a reserved key");
            Require(GetSelfTestSnapshot().Hud.Contains(" | logbook open: L or Escape closes", StringComparison.Ordinal), "HUD open status");
            InventoryKey(Key.L);
            Require(!LogbookOpen && !WorldModal, "L closed the logbook");
            AssertWindowKeyModifiers(Require);

            // Story through the session: rumor 1002 then 1001 an hour later, 1002 quelled; quest 1001 completed, 1000 accepted later.
            Require(session.LearnRumor(session.Version, 1002).Disposition == GameSessionDisposition.Changed, "learn rumor 1002");
            Require(session.SetQuestState(session.Version, 1001, SessionQuestState.Completed).Disposition == GameSessionDisposition.Changed, "complete quest 1001");
            Require(session.AdvanceGameTime(session.QueryClockSnapshot().Token, 3_600_000, ClockAdvanceReason.ExplicitWait).Disposition == ClockMutationDisposition.Changed, "wait an hour");
            Require(session.LearnRumor(session.Version, 1001).Disposition == GameSessionDisposition.Changed, "learn rumor 1001");
            Require(session.SetQuestState(session.Version, 1000, SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, "accept quest 1000");
            Require(session.QuellRumor(session.Version, 1002).Disposition == GameSessionDisposition.Changed, "quell rumor 1002");
            var revision = session.Version.Revision;

            InventoryKey(Key.L);
            var rumors = _logbookPanel!.ViewForSelfTest!;
            Require(rumors.Tab == AreaLogbookTab.Rumors && rumors.Entries.Select(e => (e.Number, e.Style)).SequenceEqual([(1002, AreaLogbookStyle.Struck), (1001, AreaLogbookStyle.Plain)])
                && rumors.Entries[0].Text == "The well is dry." && rumors.Note == "no hero: normal texts", "rumors in time order with their mark: " + string.Join(",", rumors.Entries));
            Require(_logbookPanel.TextForSelfTest.Contains("The mill is haunted.", StringComparison.Ordinal), "panel text: " + _logbookPanel.TextForSelfTest);
            // The open logbook owns the keys: the Character sheet key neither opens the sheet nor closes the logbook.
            InventoryKey(Key.C);
            Require(LogbookOpen && !SheetOpen, "C under the logbook");
            InventoryKey(Key.Tab);
            var quests = _logbookPanel.ViewForSelfTest!;
            Require(quests.Tab == AreaLogbookTab.Quests && quests.Entries.Select(e => (e.Number, e.State, e.Style)).SequenceEqual(
                [(1001, "completed", AreaLogbookStyle.Completed), (1000, "accepted", AreaLogbookStyle.Active)]) && quests.Entries[1].Text == "Find the [lost] gear.",
                "Tab switched to the quests: " + string.Join(",", quests.Entries));
            Require(_logbookPanel.TextForSelfTest.Contains("Find the [lost] gear.", StringComparison.Ordinal), "a bracket in the data is shown literally");
            // A session change rebuilds the open logbook.
            Require(session.SetQuestState(session.Version, 1000, SessionQuestState.Botched).Disposition == GameSessionDisposition.Changed, "botch quest 1000");
            revision = session.Version.Revision;
            SyncAreaLogbook();
            Require(_logbookPanel.ViewForSelfTest!.Entries[^1] is { Number: 1000, State: "botched", Style: AreaLogbookStyle.Failed }, "rebuilt quests");
            InventoryKey(Key.Escape);
            Require(!LogbookOpen && !WorldModal && !_worldGate.IsOpen && session.Version.Revision == revision, "Escape closed it; the logbook changed nothing");
            // The tab is kept across close and open.
            InventoryKey(Key.L);
            Require(_logbookPanel.ViewForSelfTest!.Tab == AreaLogbookTab.Quests, "the quests tab is kept");
            // Controller pulses through the production pulse route: confirm switches the tab, cancel closes.
            static GamepadLab.Core.IntentFrame Pulse(bool confirm, bool cancel) =>
                new(default, default, confirm, cancel, false, false, false, false, false, 0, 0, GamepadLab.Core.InputMethod.Gamepad, true, "ARC-UI-008 self-test");
            HandleWorldPulse(Pulse(confirm: true, cancel: false), true);
            Require(LogbookOpen && _logbookPanel.ViewForSelfTest!.Tab == AreaLogbookTab.Rumors, "controller confirm switched the tab");
            HandleWorldPulse(Pulse(confirm: false, cancel: true), true);
            Require(!LogbookOpen && !PaletteOpen, "controller cancel closed the logbook without opening the palette");

            // A synthetic hero session (invented values, intelligence 4) through the production replacement path, as a quick load
            // does: the tab starts at the rumors again and the low-intelligence texts are read.
            var levels = MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(
                string.Concat(Enumerable.Range(2, 50).Select(level => $"{{{level}}}{{{1000 * (level - 1) * (level - 1)}}}")))).File!;
            var empty = MesFile.Parse(System.Text.Encoding.Latin1.GetBytes("")).File!;
            var experience = AreaStartingHero.Experience(levels, empty, empty, out var detail) ?? throw new InvalidOperationException("AREA logbook: rules " + detail);
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 4, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 385, []);
            var actor = session.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic", experience));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA logbook: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));
            Require(owner.QueryHeroDialogFacts()?.Intelligence == 4, "the synthetic hero's intelligence is 4");
            Require(owner.LearnRumor(owner.Version, 1001).Disposition == GameSessionDisposition.Changed, "the hero learns rumor 1001");
            InventoryKey(Key.L);
            Require(_logbookPanel.ViewForSelfTest is { Tab: AreaLogbookTab.Rumors, LowIntelligence: true, Note: null } low && low.Entries.Single().Text == "Mill bad.",
                "a new session starts at the rumors; low-intelligence text");

            // A hero who dies while the logbook is open closes it; L is then refused, as the original refuses a dead PC.
            Require(owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            SyncAreaLogbook();
            Require(!LogbookOpen, "the dead hero's logbook closed");
            InventoryKey(Key.L);
            Require(!LogbookOpen && _logbookStatus == "refused: the hero is dead", "L for a dead hero: " + _logbookStatus);
            return "no-hero open, rumor and quest order with quelled/completed/accepted/botched styles, Tab and controller tab switch, keys owned, " +
                "tab kept and reset for a new session, low-intelligence texts, rebuild on change, dead-hero close and refusal";
        }
        finally
        {
            if (LogbookOpen) EndLogbook();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    /// <summary>
    /// ARC-UI-008 native observation on the original data: the module's logbook texts load through the content stack; the
    /// production L key opens the logbook over the live session, Tab switches the tab, the open logbook owns the keys, Escape
    /// closes it, the tab is kept and the logbook changes nothing. Then, on a detached copy of the session, every quest with a
    /// normal description is accepted and every rumor with a normal text is learned (every seventh quelled) and both tabs are
    /// built: every entry must carry its text. Prints counts and outcomes only, no text of the original data.
    /// </summary>
    private async Task RunAreaLogbookSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-008 logbook smoke: " + why); }
        // A map without a session clock has no session (ARC-WORLD-057: only one whose start clock cannot be read); there is
        // no logbook to observe.
        if (_areaSession is not { } session) { GD.Print("ARC-UI-008 native-smoke logbook SKIP (no area session)"); return; }
        for (var i = 0; i < 600 && _logbookTexts is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptLogbookTexts(); }
        Require(_logbookTexts is { Status: AreaLogbookTextsStatus.Ready, Texts: not null }, "texts " + _logbookTexts?.Status + " " + _logbookTexts?.Detail);
        var texts = _logbookTexts!.Texts!;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var revision = session.Version.Revision;
            InventoryKey(Key.L);
            Require(LogbookOpen && WorldModal && _logbookPanel!.ViewForSelfTest is not null, "L opened the logbook: " + _logbookStatus);
            var first = _logbookPanel!.ViewForSelfTest!.Tab;
            InventoryKey(Key.Tab);
            Require(_logbookPanel.ViewForSelfTest!.Tab != first, "Tab switched the tab");
            InventoryKey(Key.I);
            Require(LogbookOpen && !InventoryOpen, "the open logbook owns the I key");
            InventoryKey(Key.Escape);
            Require(!LogbookOpen && session.Version.Revision == revision, "Escape closed the logbook; the logbook changed nothing");
            InventoryKey(Key.L);
            Require(LogbookOpen && _logbookPanel.ViewForSelfTest!.Tab != first, "the tab is kept");
            InventoryKey(Key.L);
            Require(!LogbookOpen, "L closed the logbook");

            var saved = session.Capture();
            var options = new GameSessionRestoreOptions(new(saved.Identity.World, session.Flags.Binding.Identity.Module),
                session.QueryClockSnapshot().ProfileBinding, new(Guid.NewGuid()), session.Limits, session.Rules.Mode, session.Experience, session.Difficulty,
                LoadRandom: session.QueryRandom());
            using var copy = GameSessionOwner.Restore(saved, options).Owner ?? throw new InvalidOperationException("ARC-UI-008 logbook smoke: copy");
            // The texts the logbook reads for this PC: the hero's intelligence, the normal texts without a hero.
            var intelligence = copy.QueryHeroDialogFacts()?.Intelligence ?? AreaLogbook.NoHeroIntelligence;
            var quests = Enumerable.Range(LogbookTexts.FirstQuest, LogbookTexts.LastQuest - LogbookTexts.FirstQuest + 1)
                .Where(quest => texts.QuestDescription(quest, intelligence) is not null && copy.QueryQuest(quest) is null).ToHashSet();
            var rumors = Enumerable.Range(SessionStoryLimits.FirstRumor, SessionStoryLimits.MaxRumors)
                .Where(rumor => texts.RumorText(rumor, intelligence) is not null && copy.QueryRumor(rumor) is null && !copy.QueryRumorQuelled(rumor)).ToArray();
            foreach (var quest in quests) Require(copy.SetQuestState(copy.Version, quest, SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, $"accept a quest");
            var quelled = new HashSet<int>();
            for (var i = 0; i < rumors.Length; i++)
            {
                Require(copy.LearnRumor(copy.Version, rumors[i]).Disposition == GameSessionDisposition.Changed, "learn a rumor");
                if (i % 7 == 0 && copy.QuellRumor(copy.Version, rumors[i]).Disposition == GameSessionDisposition.Changed) quelled.Add(rumors[i]);
            }
            var lowQuests = Enumerable.Range(LogbookTexts.FirstQuest, LogbookTexts.LastQuest - LogbookTexts.FirstQuest + 1).Count(quest => texts.QuestLogLow?.Find(quest) is not null);
            var lowRumors = Enumerable.Range(SessionStoryLimits.FirstRumor, SessionStoryLimits.MaxRumors).Count(rumor => texts.RumorText(rumor, LogbookTexts.LowIntelligence) is not null);
            var questView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Quests, AreaHoverIndex.TextProfile).View!;
            var rumorView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Rumors, AreaHoverIndex.TextProfile).View!;
            var hero = copy.QueryHeroDialogFacts() is not null;
            Require(quests.Count > 0 && rumors.Length > 0, "the module has logbook texts");
            var ours = questView.Entries.Where(e => quests.Contains(e.Number)).ToArray();
            Require(questView.Entries.Count == copy.QueryQuestLog().Count && ours.Length == quests.Count
                && ours.All(e => e.Text.Length > 0 && e.Style == AreaLogbookStyle.Active && e.State == "accepted"), "every accepted quest shows its description");
            var learned = rumorView.Entries.Where(e => rumors.Contains(e.Number)).ToArray();
            Require(rumorView.Entries.Count == copy.QueryRumorLog().Count && learned.Length == rumors.Length && learned.All(e => e.Text.Length > 0
                && (e.Style == AreaLogbookStyle.Struck) == quelled.Contains(e.Number)), "every learned rumor shows its text and mark");
            Require(questView.Note is null or "no hero: normal texts" && rumorView.Note is null or "no hero: normal texts", "texts decodable: " + questView.Note + " / " + rumorView.Note);
            Require(session.Version.Revision == revision, "the live session is untouched");
            GD.Print("ARC-UI-008 native-smoke logbook PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Texts = _logbookTexts.Detail, Quests = quests.Count, LowQuestTexts = lowQuests, Rumors = rumors.Length, LowRumorTexts = lowRumors,
                QuestEntries = questView.Entries.Count, RumorEntries = rumorView.Entries.Count, Hero = hero, questView.LowIntelligence, Quelled = quelled.Count }));
        }
        finally
        {
            if (LogbookOpen) EndLogbook();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
