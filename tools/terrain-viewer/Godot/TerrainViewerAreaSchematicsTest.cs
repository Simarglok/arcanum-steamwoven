using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-TECHNOLOGY-005 over the strict synthetic AREA owner in the production app: T is refused without a hero; with
    /// a synthetic hero and synthetic schematic data T opens the window (3008), the discipline, page and tab controls move with
    /// their sounds, the window owns the keys, Tab assembles through the session (text 2, 3018 + discipline), a refused button
    /// shows its text, Escape closes (3009), the position is kept, the controller closes, script action 129 opens it for the PC
    /// and refuses an NPC crafter by name, and a dead hero closes and refuses it.</summary>
    private static async Task AssertAreaSchematicsAsync(Node host)
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
            summary = app.AreaSchematicsForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-TECHNOLOGY-005 self-test PASS synthetic schematics ({summary}); original data covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal string AreaSchematicsForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA schematics: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_SCHEMATICS_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var session = _areaSession;
            Require(session.QueryHero() is null, "the synthetic session starts without a hero");
            InventoryKey(Key.T);
            Require(!SchematicsOpen && !WorldModal && _schematicsStatus == "no hero (start with --hero)", "T without a hero: " + _schematicsStatus);
            Require(_worldLastReserved?.Contains("Technical") != true, "T is not reported as a reserved key");

            // Synthetic data (invented, ASCII): record 2000 makes two products from two parts (base 2100), record 2010 needs a gear
            // (base 2200, complexity −10, discipline 3) the hero lacks the expertise for.
            static MesFile Mes(string text) => MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(text)).File!;
            static string Record(int n, string c1, string c2, string quantity) =>
                $"{{{n}}}{{4}}{{{n + 1}}}{{5}}{{{n + 2}}}{{300}}{{{n + 3}}}{{{c1}}}{{{n + 4}}}{{{c2}}}{{{n + 5}}}{{2300}}{{{n + 6}}}{{{quantity}}}\n";
            var catalog = SchematicCatalog.Create(Mes(Record(2000, "2100", "2100", "2") + Record(2010, "2100", "2200", "1")),
                Mes("{0}{Missing}{1}{Expertise}{2}{Done}{3}{Hover}{4}{Widget}{5}{A widget.}")).Catalog ?? throw new InvalidOperationException("AREA schematics: catalogue");
            var texts = TechTexts.Read(Mes(string.Concat(Enumerable.Range(0, TechTexts.Messages).Select(n => $"{{{n}}}{{t{n}}}\n")))).Texts
                ?? throw new InvalidOperationException("AREA schematics: texts");
            var part = new SessionItemKind(SessionItemType.Generic, 2120, 1, 2);
            var data = new AreaSchematicData(catalog, texts, new Dictionary<int, (ComponentFacts, SessionItemKind)>
            {
                [2120] = (new(0, 3), part), [2220] = (new(-10, 3), new(SessionItemType.Generic, 2220, 3, 4)),
                [2320] = (new(0, 3), new(SessionItemType.Generic, 2320, 5, 6)),
            });
            _schematicDataTask = null; _schematicData = new(AreaSchematicDataStatus.Ready, data, "synthetic"); _schematicsStamp = null;

            // A synthetic hero (degree 2 in discipline 0, two parts) through the production replacement path, as a quick load does.
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 15, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 385, [new(part), new(part)], Advancement: SessionHeroAdvancement.None with { TechDegrees = [2, 0, 0, 0, 0, 0, 0, 0] });
            var actor = session.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic"));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA schematics: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));
            _schematicSounds.Clear();

            InventoryKey(Key.T);
            var first = _schematicsPanel?.ViewForSelfTest;
            Require(SchematicsOpen && WorldModal && !_worldGate.IsOpen && first is { Tab: AreaSchematicTab.Learned, Discipline: 0, Page: 0, Pages: 2, Record: 2000,
                Button: AssemblyButton.CanAssemble, Quantity: 2, Placeholder: false }, "T opened the learned tab at record 2000 (modal): " + first);
            Require(_schematicSounds.Last() == BookOpenSound, "open sound 3008");
            Require(_schematicsPanel!.TextForSelfTest.Contains("Widget", StringComparison.Ordinal), "panel text: " + _schematicsPanel.TextForSelfTest);
            Require(GetSelfTestSnapshot().Hud.Contains(" | schematics open: T or Escape closes", StringComparison.Ordinal), "HUD open status");
            InventoryKey(Key.C);
            Require(SchematicsOpen && !SheetOpen, "the open window owns the C key");

            SelectSchematicDiscipline(1);
            Require(_schematicsPanel.ViewForSelfTest is { Discipline: 1, Placeholder: true, Pages: 0 } empty && empty.Record == SchematicCatalog.PlaceholderNumber(1)
                && _schematicSounds.Last() == BookPageSound, "an empty discipline shows its placeholder (3010)");
            SelectSchematicDiscipline(0);
            TurnSchematicPage(1);
            Require(_schematicsPanel.ViewForSelfTest is { Page: 1, Record: 2010, Button: AssemblyButton.ExpertiseLacking } && _schematicSounds.Last() == BookPageSound, "next page (3010)");
            var sounds = _schematicSounds.Count;
            TurnSchematicPage(1);
            Require(_schematicsPanel.ViewForSelfTest!.Page == 1 && _schematicSounds.Count == sounds, "no page past the last, no sound");
            InventoryKey(Key.Tab);
            Require(_schematicsPanel.MessageForSelfTest == "Expertise" && _areaSession!.QueryMadeItems().Count == 0, "a refused button shows text 1 and changes nothing");
            SelectSchematicTab(AreaSchematicTab.Found);
            Require(_schematicsPanel.ViewForSelfTest is { Tab: AreaSchematicTab.Found, Placeholder: true } && _schematicSounds.Last() == BookSwitchSound, "found tab (3011)");
            sounds = _schematicSounds.Count;
            SelectSchematicTab(AreaSchematicTab.Found);
            Require(_schematicSounds.Count == sounds, "the shown tab's button does nothing");
            SelectSchematicTab(AreaSchematicTab.Learned);
            Require(_schematicsPanel.ViewForSelfTest is { Tab: AreaSchematicTab.Learned, Page: 1 }, "each tab keeps its discipline and page");
            TurnSchematicPage(-1);

            InventoryKey(Key.Tab);
            Require(_areaSession!.QueryMadeItems().Select(m => m.Entity).SequenceEqual(["made:1", "made:2"]) && _schematicsPanel.MessageForSelfTest == "Done"
                && _schematicSounds.Last() == SchematicAssemblyRules.CompletedSoundBase, "Tab assembled: two products, text 2, 3018 + discipline 0");
            Require(_schematicsPanel.ViewForSelfTest is { Button: AssemblyButton.ItemMissing }, "the parts are gone");
            InventoryKey(Key.Tab);
            Require(_schematicsPanel.MessageForSelfTest == "Missing", "text 0 for a missing item");
            TurnSchematicPage(1);
            Require(_schematicsPanel.ViewForSelfTest is { Page: 1 }, "page 1 before closing");
            InventoryKey(Key.Escape);
            Require(!SchematicsOpen && !WorldModal && _schematicSounds.Last() == BookCloseSound, "Escape closed it (3009)");
            Require(GetViewport().GuiGetFocusOwner() is null, "closing releases the GUI focus");
            SelectSchematicDiscipline(1);
            TurnSchematicPage(-1);
            SelectSchematicTab(AreaSchematicTab.Found);
            InventoryKey(Key.T);
            Require(_schematicsPanel.ViewForSelfTest is { Tab: AreaSchematicTab.Learned, Discipline: 0, Page: 1 }, "the controls act only while open; the position is kept");
            static GamepadLab.Core.IntentFrame Pulse(bool confirm, bool cancel) =>
                new(default, default, confirm, cancel, false, false, false, false, false, 0, 0, GamepadLab.Core.InputMethod.Gamepad, true, "ARC-TECHNOLOGY-005 self-test");
            HandleWorldPulse(Pulse(confirm: false, cancel: true), true);
            Require(!SchematicsOpen && !PaletteOpen, "controller cancel closed the window without opening the palette");

            // Script action 129 (SW8): the PC's request opens the window once nothing else owns input; an NPC crafter is refused by name.
            var pc = new ScrObjectRef(actor.Map.RuntimeMap, AreaScriptHost.PcCode);
            var npc = new ScrObjectRef(actor.Map.RuntimeMap, "npc:1");
            QueueScriptSchematics([new(npc, pc, AreaSchematicScriptStatus.NpcCrafterNotReady)]);
            TakeScriptSchematics();
            Require(!SchematicsOpen && _schematicsStatus == "script 129: an NPC crafter is not ready (later slice)", "NPC crafter refused: " + _schematicsStatus);
            ToggleWorldSettings();
            Require(PaletteOpen, "the palette is open");
            QueueScriptSchematics([new(pc, pc, AreaSchematicScriptStatus.Open)]);
            TakeScriptSchematics();
            Require(!SchematicsOpen, "the request waits while the palette owns input");
            ToggleWorldSettings();
            TakeScriptSchematics();
            Require(SchematicsOpen, "the PC's request opened the window");

            // A hero who dies while the window is open closes it; T is then refused.
            Require(owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            SyncAreaSchematics();
            Require(!SchematicsOpen, "the dead hero's window closed");
            InventoryKey(Key.T);
            Require(!SchematicsOpen && _schematicsStatus == "refused: the hero is dead", "T for a dead hero: " + _schematicsStatus);
            return "no-hero refusal, open with T (3008), discipline/page/tab controls with 3010/3011 and bounds, keys owned, refused button texts 0 and 1, " +
                "assembly through the session (2 made, text 2, 3018), Escape (3009), position kept, controller cancel, script 129 PC open and NPC refusal, dead-hero close";
        }
        finally
        {
            if (SchematicsOpen) EndSchematics();
            if (PaletteOpen) ToggleWorldSettings();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }
}

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-TECHNOLOGY-005 native observation on the original data: the module's schematic data load through the content stack;
    /// with a hero the production T key opens the window over the live session, a discipline, a page and the found tab move it,
    /// the open window owns the keys, Escape closes it and nothing changes. Then, on a detached session whose synthetic hero holds
    /// the first alternative of both components of a learned record (every degree 7), one assembly makes the record's products
    /// through the same session call the button makes. Prints counts and outcomes only, no text of the original data.
    /// </summary>
    private async Task RunAreaSchematicsSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-TECHNOLOGY-005 schematics smoke: " + why); }
        if (_areaSession is not { } session) { GD.Print("ARC-TECHNOLOGY-005 native-smoke schematics SKIP (no area session)"); return; }
        for (var i = 0; i < 600 && _schematicData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptSchematicData(); }
        Require(_schematicData is { Status: AreaSchematicDataStatus.Ready, Data: not null }, "data " + _schematicData?.Status + " " + _schematicData?.Detail);
        var data = _schematicData!.Data!;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var revision = session.Version.Revision;
            var hero = session.QueryHero() is not null;
            var live = "no hero: T refused";
            InventoryKey(Key.T);
            if (!hero) Require(!SchematicsOpen && _schematicsStatus == "no hero (start with --hero)", "T without a hero: " + _schematicsStatus);
            else
            {
                Require(SchematicsOpen && WorldModal && _schematicsPanel!.ViewForSelfTest is { Tab: AreaSchematicTab.Learned }, "T opened the window: " + _schematicsStatus);
                var learned = _schematicsPanel!.ViewForSelfTest!;
                SelectSchematicDiscipline((learned.Discipline + 1) % AreaSchematics.Disciplines);
                Require(_schematicsPanel.ViewForSelfTest!.Discipline == (learned.Discipline + 1) % AreaSchematics.Disciplines && _schematicsPanel.ViewForSelfTest.Page == 0, "discipline");
                if (_schematicsPanel.ViewForSelfTest.Pages > 1) { TurnSchematicPage(1); Require(_schematicsPanel.ViewForSelfTest!.Page == 1, "next page"); }
                SelectSchematicTab(AreaSchematicTab.Found);
                Require(_schematicsPanel.ViewForSelfTest!.Tab == AreaSchematicTab.Found, "found tab");
                InventoryKey(Key.I);
                Require(SchematicsOpen && !InventoryOpen, "the open window owns the I key");
                InventoryKey(Key.Escape);
                Require(!SchematicsOpen && session.Version.Revision == revision, "Escape closed it; the window changed nothing");
                live = $"opened, learned pages {learned.Pages} in discipline {learned.Discipline}, expertise {learned.Expertise}";
            }

            // One assembly on a detached synthetic hero holding a learned record's components (the first record that fits).
            var actor = session.QueryActor();
            string? made = null;
            var tried = 0;
            foreach (var record in data.Catalog.Records.Where(r => r.Kind == Arcanum.Data.Technology.SchematicKind.Learned && r.IsComplete))
            {
                if (record.Component1.Prototypes.Count == 0 || record.Component2.Prototypes.Count == 0) continue;
                if (data.Kind(record.Component1.Prototypes[0]) is not { } first || data.Kind(record.Component2.Prototypes[0]) is not { } second) continue;
                tried++;
                var definition = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 20, 8, 8, 8, 0, 0], 1, 5, 0,
                    null, null, 0, [new(first), new(second)], Advancement: SessionHeroAdvancement.None with { TechDegrees = [7, 7, 7, 7, 7, 7, 7, 7] });
                var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                    new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                    new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic"));
                if (created.Owner is not { } copy) continue;
                using (copy)
                {
                    if (AreaSchematics.Build(copy, data, AreaSchematicTab.Learned, 0, 0, SchematicItems, AreaHoverIndex.TextProfile).View is null) continue;
                    if (copy.QueryHeroAssembly(record, SchematicItems, data.Facts)?.CanAssemble != true) continue;
                    var result = copy.AssembleSchematic(copy.Version, record, SchematicItems, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck);
                    if (result.Disposition != GameSessionDisposition.Changed) continue;
                    Require(result.Made.Count == record.Quantity && copy.QueryMadeItems().Count == record.Quantity && copy.QueryHeroKit().Count == 0,
                        "the products replace the components");
                    made = $"record {record.Number}: {result.Made.Count} made";
                    break;
                }
            }
            Require(made is not null, $"no learned record assembled ({tried} tried)");
            Require(AreaSchematics.CompletedText(data, AreaHoverIndex.TextProfile).Length > 0, "the completion text decodes");
            Require(session.Version.Revision == revision, "the live session is untouched");
            GD.Print("ARC-TECHNOLOGY-005 native-smoke schematics PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Data = _schematicData.Detail, Hero = hero, Live = live, Assembly = made, Tried = tried }));
        }
        finally
        {
            if (SchematicsOpen) EndSchematics();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
