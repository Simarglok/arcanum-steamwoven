using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-007 over the strict synthetic AREA owner in the production app: C without a hero, then a synthetic hero
    /// session through the production replacement path; C opens and closes the sheet, Escape closes it, the open sheet owns the
    /// keys and follows the session, a dead hero closes it and refuses it.</summary>
    private static async Task AssertAreaSheetAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
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
            summary = app.AreaSheetForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-007 self-test PASS synthetic character sheet ({summary}); original hero covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal string AreaSheetForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA sheet: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_SHEET_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // The synthetic session has no hero: C is the sheet's key, but nothing opens.
            Require(_areaSession.QueryHero() is null, "the synthetic session starts without a hero");
            InventoryKey(Key.C);
            Require(!SheetOpen && !WorldModal && _sheetStatus == "no hero (start with --hero)" && _worldLastReserved?.Contains("Character sheet") != true,
                "C without a hero: " + _sheetStatus);
            Require(GetSelfTestSnapshot().Hud.Contains(" | C character sheet: no hero", StringComparison.Ordinal), "HUD sheet status");

            // A synthetic hero session (invented values) through the production replacement path, as a quick load does.
            var levels = Arcanum.Data.Messages.MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(
                string.Concat(Enumerable.Range(2, 50).Select(level => $"{{{level}}}{{{1000 * (level - 1) * (level - 1)}}}")))).File!;
            var empty = Arcanum.Data.Messages.MesFile.Parse(System.Text.Encoding.Latin1.GetBytes("")).File!;
            var experience = AreaStartingHero.Experience(levels, empty, empty, out var detail) ?? throw new InvalidOperationException("AREA sheet: rules " + detail);
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
                new(67, [new(CharacterStat.Dexterity, StatModifierOperator.Add, 1)]), null, 385, []);
            var actor = _areaSession.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(_areaSession.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic", experience));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA sheet: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));

            InventoryKey(Key.C);
            Require(SheetOpen && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen && _sheetPanel!.ViewForSelfTest is { Level: 1, ToNextLevel: 1000, UnspentPoints: 5 },
                "C opened the sheet (modal)");
            var text = _sheetPanel!.TextForSelfTest;
            Require(text.StartsWith("half-elf-male | half-elf, male\nLevel 1 | experience 0, 1000 to the next level | unspent points 5", StringComparison.Ordinal)
                && text.Contains("Dexterity 9 |", StringComparison.Ordinal), "sheet text: " + text);
            Require(GetSelfTestSnapshot().Hud.Contains(" | character sheet open: C closes", StringComparison.Ordinal), "HUD open status");
            // ARC-UI-009: the open sheet opened the hero's editor; + and − on the skills page go through the session's pool.
            Require(_areaSession.HeroEditorOpen && _sheetPanel.SkillsForSelfTest is { EditorOpen: true, UnspentPoints: 5, Rows.Count: 16 }, "skills page with the editor open");
            Require(_sheetPanel.SkillTextForSelfTest(0) == "Bow: level 0/20, 0 point(s); next point needs Dexterity 6"
                && _sheetPanel.SkillHintForSelfTest(0) == "Bow\nDexterity: 6", "bow row and hint: " + _sheetPanel.SkillTextForSelfTest(0));
            var before = _areaSession.Version.Revision;
            _sheetPanel.SkillPlusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_areaSession.Version.Revision == before + 1 && _sheetPanel.SkillsForSelfTest is { UnspentPoints: 4 } page && page.Rows[0].Points == 1
                && _sheetPanel.SkillMessageForSelfTest == "spent 1 point(s)" && _sheetPanel.TextForSelfTest.Contains("unspent points 4", StringComparison.Ordinal),
                "+ spent a point and rebuilt the sheet: " + _sheetPanel.SkillMessageForSelfTest);
            _sheetPanel.SkillMinusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillsForSelfTest is { UnspentPoints: 5 } back && back.Rows[0].Points == 0, "− refunded it");
            _sheetPanel.SkillMinusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillMessageForSelfTest == "refused: nothing bought since the sheet opened to refund", "− at the floor: " + _sheetPanel.SkillMessageForSelfTest);
            _sheetPanel.SkillPlusForSelfTest(12).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillsForSelfTest!.Rows[12].Points == 1, "+ on a tech row");
            // ARC-MAGIC-008: the spells page. College tabs pick the shown college; + learns its next rank and − unlearns it through
            // the pool (the synthetic hero: willpower 8, so rank 2's minimum of 9 refuses); − is disabled at the editor's floor.
            Require(_sheetPanel.SpellsForSelfTest is { College: 0, Rank: 0, CanLearn: true, CanUnlearn: false, EditorOpen: true }
                && _sheetPanel.SpellTextForSelfTest(0) == "1. College 1 spell 1 (next) | minimum level 1"
                && _sheetPanel.SpellHintForSelfTest(0) == "College 1 spell 1\nMinimum willpower: 6" && _sheetPanel.SpellMinusForSelfTest.Disabled,
                "spells page: " + _sheetPanel.SpellTextForSelfTest(0));
            _sheetPanel.CollegeTabForSelfTest(2).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SpellsForSelfTest is { College: 2 } && _sheetPanel.CollegeTabForSelfTest(2).ButtonPressed
                && !_sheetPanel.CollegeTabForSelfTest(0).ButtonPressed, "a college tab shows its spells");
            _sheetPanel.SpellPlusForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SpellsForSelfTest is { Rank: 1, CanUnlearn: true, UnspentPoints: 3 } && _sheetPanel.SkillMessageForSelfTest == "learned College 3 spell 1"
                && _areaSession.QueryHeroSpellBook()!.Ranks[2] == 1, "+ learned rank 1: " + _sheetPanel.SkillMessageForSelfTest);
            _sheetPanel.SpellPlusForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillMessageForSelfTest == "refused: raise Willpower first" && _sheetPanel.SpellsForSelfTest!.Rank == 1,
                "+ refused for willpower: " + _sheetPanel.SkillMessageForSelfTest);
            _sheetPanel.SpellMinusForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SpellsForSelfTest is { Rank: 0, CanUnlearn: false, UnspentPoints: 4 } && _sheetPanel.SkillMessageForSelfTest == "unlearned College 3 spell 1"
                && _sheetPanel.SpellMinusForSelfTest.Disabled, "− unlearned it: " + _sheetPanel.SkillMessageForSelfTest);
            // ARC-UI-034: the stats page's + and − on charisma (row 9) through the same pool, down to the opening.
            _sheetPanel.StatPlusForSelfTest(9).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.StatsForSelfTest is { UnspentPoints: 3 } stats && stats.Rows[9] is { Stat: CharacterStat.Charisma, Value: 9, FloorValue: 8 }
                && _sheetPanel.SkillMessageForSelfTest == "spent 1 point(s)", "stat + bought charisma: " + _sheetPanel.SkillMessageForSelfTest);
            _sheetPanel.StatMinusForSelfTest(9).EmitSignal(BaseButton.SignalName.Pressed);
            _sheetPanel.StatMinusForSelfTest(9).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.StatsForSelfTest is { UnspentPoints: 4 } && _areaSession.QueryHero()!.Stats[(int)CharacterStat.Charisma] == 8
                && _sheetPanel.SkillMessageForSelfTest == "refused: nothing bought since the sheet opened to refund", "stat − to the opening: " + _sheetPanel.SkillMessageForSelfTest);
            // ARC-DEV-111: the Inventory key is the original's global key (it replaces the open sheet); the logbook test covers the move.
            // A session change rebuilds the open sheet.
            var item = new SessionEffectDefinition(120, [new EffectChange(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 2)]);
            Require(_areaSession.AddHeroEffect(_areaSession.Version, item, EffectCause.Item).Failure == GameSessionFailure.None, "add an item effect");
            SyncAreaSheet();
            Require(_sheetPanel.TextForSelfTest.Contains("Strength 10 (raised by effects)", StringComparison.Ordinal), "rebuilt sheet: " + _sheetPanel.TextForSelfTest);
            InventoryKey(Key.C);
            Require(!SheetOpen && !WorldModal && !_worldGate.IsOpen, "C closed the sheet and quarantined the click guards");
            Require(!_areaSession.HeroEditorOpen, "closing the sheet closed the editor");
            // Reopened: the tech point bought before is the new floor.
            InventoryKey(Key.C);
            _sheetPanel.SkillMinusForSelfTest(12).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillsForSelfTest!.Rows[12] is { Points: 1, FloorPoints: 1 } && _sheetPanel.SkillMessageForSelfTest == "refused: nothing bought since the sheet opened to refund",
                "the reopened editor's floor holds the bought point");
            Require(_sheetPanel.SpellsForSelfTest is { College: 2 }, "the shown college is kept between opens");
            InventoryKey(Key.C);
            // Controller pulses through the production pulse route: confirm presses the close button, cancel closes.
            static GamepadLab.Core.IntentFrame Pulse(bool confirm, bool cancel) =>
                new(default, default, confirm, cancel, false, false, false, false, false, 0, 0, GamepadLab.Core.InputMethod.Gamepad, true, "ARC-UI-007 self-test");
            InventoryKey(Key.C); HandleWorldPulse(Pulse(confirm: true, cancel: false), true);
            Require(!SheetOpen && !PaletteOpen, "controller confirm pressed the close button");
            InventoryKey(Key.C); HandleWorldPulse(Pulse(confirm: false, cancel: true), true);
            Require(!SheetOpen && !PaletteOpen, "controller cancel closed the sheet without opening the palette");
            InventoryKey(Key.C); InventoryKey(Key.Escape);
            Require(SheetOpen, "Escape does not close the sheet (it opens the menu, which leaves the sheet open)");
            Require(MainMenuOpen, "Escape opened the menu"); InventoryKey(Key.Escape); Require(SheetOpen && !MainMenuOpen, "Escape continued play");
            InventoryKey(Key.C);
            Require(!SheetOpen, "C closed the sheet");

            // ARC-UI-046: an award through the production hook posts one message-window entry per level written (the project's labels
            // without the player's texts); the window keeps the last ten.
            var award = _areaSession.AwardHeroExperience(_areaSession.Version, 4000).Award;
            Require(award?.LevelUps is { Count: 2 }, "an award to level 3 writes two levels");
            var posted = _levelMessagesPosted;
            LevelAreaFollowers(award);
            Require(_levelMessagesPosted == posted + 2 && _messageLog.History.Count == 2
                && _messageLog.Shown == new AreaMessageEntry(AreaMessageKind.LevelUp, "Level up", "Level 3", $"Unspent points: {_areaSession.QueryHero()!.UnspentPoints}"),
                "two level-up entries, the newest shown: " + _messageLog.Shown);
            LevelAreaFollowers(_areaSession.AwardHeroExperience(_areaSession.Version, 169000 - 4000).Award);
            Require(_levelMessagesPosted == posted + 13 && _messageLog.History.Count == AreaMessageLog.Capacity
                && _messageLog.History[0].Line == "Level 5" && _messageLog.Shown?.Line == "Level 14",
                "eleven more levels: the window keeps the last ten");

            // ARC-SKILLS-008: the palette's Prowl action conceals the hero; an untrained prowler's step is the stealth walk (ARC-RENDER-2539), and off again.
            PaletteProwlToggle();
            Require(_areaSession.IsConcealed(SessionCombatant.Hero) && _prowlNotice == "prowling (concealed)", "prowl on: " + _prowlNotice);
            SetObjectApproachGait();
            Require(StepGait() == Arcanum.World.MovementGait.Sneak && GaitSummary().Contains("prowling: walks only", StringComparison.Ordinal),
                "a concealed untrained hero walks: " + GaitSummary());
            PaletteProwlToggle();
            Require(!_areaSession.IsConcealed(SessionCombatant.Hero) && StepGait() == Arcanum.World.MovementGait.Run, "prowl off runs again");

            // A hero who dies while the sheet is open closes it; C is then refused, as the original refuses a dead PC.
            InventoryKey(Key.C);
            Require(_areaSession.Kill(_areaSession.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            SyncAreaSheet();
            Require(!SheetOpen && !_areaSession.HeroEditorOpen, "the dead hero's sheet closed and so did its editor");
            InventoryKey(Key.C);
            Require(!SheetOpen && _sheetStatus == "refused: the hero is dead", "C for a dead hero: " + _sheetStatus);
            return "no-hero refusal, C/controller confirm and cancel/Escape, keys owned while open, rebuild on change, prowl toggle and the stealth gait, skills page + and − with the editor floor, stats page + and − (ARC-UI-034), spells page tabs, learn, refusal and unlearn (ARC-MAGIC-008), level-up messages (ARC-UI-046), dead-hero close and refusal";
        }
        finally
        {
            if (SheetOpen) EndSheet();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }
}
