using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
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
            Require(!SheetOpen && !WorldModal && _sheetStatus == "character sheet: no hero (start with --hero)" && _worldLastReserved?.Contains("Character sheet") != true,
                "C without a hero: " + _sheetStatus);
            Require(GetSelfTestSnapshot().Hud.Contains(" | C character sheet: character sheet: no hero", StringComparison.Ordinal), "HUD sheet status");

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
            Require(SheetOpen && WorldModal && !_worldGate.IsOpen && _sheetPanel!.ViewForSelfTest is { Level: 1, ToNextLevel: 1000, UnspentPoints: 5 },
                "C opened the sheet (modal)");
            var text = _sheetPanel!.TextForSelfTest;
            Require(text.StartsWith("half-elf-male | half-elf, male\nLevel 1 | experience 0, 1000 to the next level | unspent points 5", StringComparison.Ordinal)
                && text.Contains("Dexterity 9 |", StringComparison.Ordinal), "sheet text: " + text);
            Require(GetSelfTestSnapshot().Hud.Contains(" | character sheet open: C or Escape closes", StringComparison.Ordinal), "HUD open status");
            // The open sheet owns the keys: the Inventory key neither opens the inventory nor closes the sheet.
            InventoryKey(Key.I);
            Require(SheetOpen && !InventoryOpen, "I under the sheet");
            // A session change rebuilds the open sheet.
            var item = new SessionEffectDefinition(120, [new EffectChange(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 2)]);
            Require(_areaSession.AddHeroEffect(_areaSession.Version, item, EffectCause.Item).Failure == GameSessionFailure.None, "add an item effect");
            SyncAreaSheet();
            Require(_sheetPanel.TextForSelfTest.Contains("Strength 10 (raised by effects)", StringComparison.Ordinal), "rebuilt sheet: " + _sheetPanel.TextForSelfTest);
            InventoryKey(Key.C);
            Require(!SheetOpen && !WorldModal && !_worldGate.IsOpen, "C closed the sheet and quarantined the click guards");
            InventoryKey(Key.C); _sheetPanel.Confirm();
            Require(!SheetOpen, "the close button (controller confirm) closed the sheet");
            InventoryKey(Key.C); InventoryKey(Key.Escape);
            Require(!SheetOpen, "Escape closed the sheet");

            // A hero who dies while the sheet is open closes it; C is then refused, as the original refuses a dead PC.
            InventoryKey(Key.C);
            Require(_areaSession.Kill(_areaSession.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            SyncAreaSheet();
            Require(!SheetOpen, "the dead hero's sheet closed");
            InventoryKey(Key.C);
            Require(!SheetOpen && _sheetStatus == "character sheet refused: the hero is dead", "C for a dead hero: " + _sheetStatus);
            return "no-hero refusal, C/close/Escape, keys owned while open, rebuild on change, dead-hero close and refusal";
        }
        finally
        {
            if (SheetOpen) EndSheet();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }
}
