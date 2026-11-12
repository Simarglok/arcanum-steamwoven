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
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-TECHNOLOGY-005 over the strict synthetic AREA owner in the production app: T is refused without a hero; with
    /// a synthetic hero and synthetic schematic data T opens the window (3008), the discipline, page and tab controls move with
    /// their sounds, the window owns the keys, Tab assembles through the session (text 2, 3018 + discipline), a refused button
    /// shows its text, Escape closes (3009), the position is kept, the controller closes, script action 129 opens it for the PC
    /// and names an NPC without data, an NPC crafter's window (ARC-TECHNOLOGY-006) resets the tabs, has an empty found tab and
    /// assembles into the NPC's list, and a dead hero closes and refuses it.</summary>
    private static async Task AssertAreaSchematicsAsync(Node host)
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
            Require(owner.HasItemScriptSource, "ARC-DEV-099: the replacement attached the prototype script source");
            _schematicSounds.Clear();

            InventoryKey(Key.T);
            var first = _schematicsPanel?.ViewForSelfTest;
            Require(SchematicsOpen && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen && first is { Tab: AreaSchematicTab.Learned, Discipline: 0, Page: 0, Pages: 2, Record: 2000,
                Button: AssemblyButton.CanAssemble, Quantity: 2, Placeholder: false }, "T opened the learned tab at record 2000 (modal): " + first);
            Require(_schematicSounds.Last() == BookOpenSound, "open sound 3008");
            Require(_schematicsPanel!.TextForSelfTest.Contains("Widget", StringComparison.Ordinal), "panel text: " + _schematicsPanel.TextForSelfTest);
            Require(GetSelfTestSnapshot().Hud.Contains(" | schematics open: T closes", StringComparison.Ordinal), "HUD open status");
            // ARC-DEV-111: the sheet key is a global key (it replaces the open window); the logbook test covers the move.

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
            Require(SchematicsOpen, "Escape does not close the schematic window");
            Require(MainMenuOpen, "Escape opened the menu"); InventoryKey(Key.Escape); Require(SchematicsOpen && !MainMenuOpen, "Escape continued play");
            CloseSchematicsByPlayer();
            Require(!SchematicsOpen && !WorldModal && _schematicSounds.Last() == BookCloseSound, "the player's close closed it (3009)");
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

            // Script action 129 (SW8): the PC's request opens the window once nothing else owns input; an NPC crafter without map data
            // (ARC-TECHNOLOGY-006: the window opens with the NPC's home-map facts) is named and opens nothing.
            var pc = new ScrObjectRef(actor.Map.RuntimeMap, AreaScriptHost.PcCode);
            var npc = new ScrObjectRef(actor.Map.RuntimeMap, "npc:1");
            QueueScriptSchematics([new(npc, pc, AreaSchematicScriptStatus.Open)]);
            TakeScriptSchematics();
            Require(!SchematicsOpen && _schematicsStatus == "script 129: no data of the NPC crafter", "NPC crafter without data: " + _schematicsStatus);
            ToggleWorldSettings();
            Require(PaletteOpen, "the palette is open");
            QueueScriptSchematics([new(pc, pc, AreaSchematicScriptStatus.Open)]);
            TakeScriptSchematics();
            Require(!SchematicsOpen, "the request waits while the palette owns input");
            ToggleWorldSettings();
            TakeScriptSchematics();
            Require(SchematicsOpen, "the PC's request opened the window");
            CloseSchematicsByPlayer();

            // ARC-UI-1125 (SA40): a script's request replaces an open window (the inventory leaves through its End routine); a second request closes
            // the window (the opener 0x56D130 toggles).
            InventoryKey(Key.I);
            Require(InventoryOpen && !SchematicsOpen, "the inventory is open for the replacement");
            QueueScriptSchematics([new(pc, pc, AreaSchematicScriptStatus.Open)]);
            TakeScriptSchematics();
            Require(SchematicsOpen && !InventoryOpen && WindowPanelsMatchStack && StackText() == "Game>Schematic" && _schematicScriptOpen is null, "the request replaced the inventory: " + StackText());
            QueueScriptSchematics([new(pc, pc, AreaSchematicScriptStatus.Open)]);
            TakeScriptSchematics();
            Require(!SchematicsOpen && WindowPanelsMatchStack && _schematicScriptOpen is null, "a second request closed the window (the opener toggles)");

            // ARC-TECHNOLOGY-006: an NPC crafter (synthetic: degree 1 in discipline 0, two parts in its own list on another map's seed)
            // through the production open path the dialog's `su` and action 129 use; its products go to its list, the hero's parts stay.
            var smithSeed = new SessionMapInventorySeed([new("smith", SessionHolderKind.Npc, false, false, [], ["s1", "s2"])],
                [new("s1", new SessionItemFacts(part, 5, 0, 10, null, []), null), new("s2", new SessionItemFacts(part, 5, 0, 10, null, []), null)]);
            Require(owner.AttachMapInventory(new SessionMapId(actor.Map.RuntimeMap + 1), smithSeed).Disposition == GameSessionDisposition.Changed, "attach the smith");
            var smith = new SessionNpcCrafter(new("smith", new(CharacterRace.Human, false, [8, 8, 8, 8, 8, 8, 8, 8], 0, 0, 1, 0, [.. new int[12]], [.. new int[4]],
                [.. new int[16]], [1, 0, 0, 0, 0, 0, 0, 0], 1, 2, 0, 0), 0), AdvancementModifiers.None);
            // NC5: the hero's window left on the found tab at another discipline; an NPC crafter's open resets both tabs.
            InventoryKey(Key.T);
            SelectSchematicTab(AreaSchematicTab.Found);
            SelectSchematicDiscipline(2);
            CloseSchematicsByPlayer();
            Require(!SchematicsOpen && _schematicTab == AreaSchematicTab.Found && SchematicPosition.Discipline == 2, "the hero's window closed on the found tab");
            _schematicScriptOpen = (owner, smith);
            TakeScriptSchematics();
            Require(SchematicsOpen && _schematicsPanel.ViewForSelfTest is { Crafter: "smith", Tab: AreaSchematicTab.Learned, Pages: 1, Record: 2000, Button: AssemblyButton.CanAssemble },
                "the NPC crafter's window opened, reset to the learned tab: " + _schematicsPanel.ViewForSelfTest);
            Require(_schematicTabState[0] == (0, 0) && _schematicTabState[1] == (0, 0), "both tabs' discipline and page were reset");
            SelectSchematicTab(AreaSchematicTab.Found);
            Require(_schematicsPanel.ViewForSelfTest is { Tab: AreaSchematicTab.Found, Placeholder: true, Pages: 0 }, "an NPC crafter's found tab is empty");
            SelectSchematicTab(AreaSchematicTab.Learned);
            var heroMade = owner.QueryMadeItems().Count;
            InventoryKey(Key.Tab);
            Require(owner.QueryNpcInventory("smith").Count == 2 && owner.QueryMadeItems().Count == heroMade && owner.IsItemDestroyed("s1")
                && _schematicsPanel.MessageForSelfTest == "Done" && _schematicSounds.Last() == SchematicAssemblyRules.CompletedSoundBase,
                "the NPC assembled from its own list into its own list (text 2, 3018)");
            CloseSchematicsByPlayer();
            Require(!SchematicsOpen, "the player's close closed the NPC crafter's window");
            _schematicScriptOpen = (owner, smith);
            TakeScriptSchematics();
            Require(SchematicsOpen, "the NPC crafter's window is open again");

            // A hero (the viewer) who dies while the window is open closes it, an NPC crafter's included (the original's open refuses a
            // dead viewer); T is then refused.
            Require(owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            SyncAreaSchematics();
            Require(!SchematicsOpen, "the dead hero's window closed");
            InventoryKey(Key.T);
            Require(!SchematicsOpen && _schematicsStatus == "refused: the hero is dead", "T for a dead hero: " + _schematicsStatus);
            return "no-hero refusal, open with T (3008), discipline/page/tab controls with 3010/3011 and bounds, keys owned, refused button texts 0 and 1, " +
                "assembly through the session (2 made, text 2, 3018), Escape (3009), position kept, controller cancel, script 129 PC open and an NPC without data named, an NPC crafter's window (reset, empty found tab, assembly into its list), dead-hero close";
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
                CloseSchematicsByPlayer();
                Require(!SchematicsOpen && session.Version.Revision == revision, "the player's close closed it; the window changed nothing");
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
                GameSessionOwner? Copy() => AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                    new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                    new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic")).Owner;
                if (Copy() is not { } copy) continue;
                using (copy)
                {
                    if (AreaSchematics.Build(copy, data, AreaSchematicTab.Learned, 0, 0, SchematicItems, AreaHoverIndex.TextProfile).View is null) continue;
                    if (copy.QueryHeroAssembly(record, SchematicItems, data.Facts)?.CanAssemble != true) continue;
                    var result = copy.AssembleSchematic(copy.Version, record, SchematicItems, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck);
                    if (result.Disposition != GameSessionDisposition.Changed) continue;
                    Require(result.Made.Count == record.Quantity && copy.QueryMadeItems().Count == record.Quantity && copy.QueryHeroKit().Count == 0,
                        "the products replace the components");
                    made = $"record {record.Number}: {result.Made.Count} made";
                    // ARC-WORLD-070: with a full inventory the products lie on the hero's tile in the ground store, whose items on an attached
                    // map the drawing reads (RENDER-033; this detached copy has no map seed, so the store itself is checked).
                    var fullCopy = Copy();
                    Require(fullCopy is not null, "a second detached hero for the full-inventory assembly");
                    using var full = fullCopy!;
                    var ground = full.AssembleSchematic(full.Version, record, SchematicItems, data.Facts, data.Kind, _ => SessionInsertVerdict.NoRoom, data.AmmoOf);
                    var lying = full.QueryPlacedItems().Where(p => ground.OnGround.Contains(p.Entity)).ToArray();
                    Require(ground.Disposition == GameSessionDisposition.Changed && ground.OnGround.Count == record.Quantity && full.QueryHeroKit().Count == 0 &&
                        lying.Length == record.Quantity && lying.All(p => p.Map == actor.Map && p.Cell == actor.Cell), "a full inventory leaves the products on the hero's tile");
                    // The drawing finds a made product's ground ART through the schematic data (the extra-art reader of RENDER-033).
                    var productArt = data.ArtOf(lying[0].Kind.Prototype) is { } art ? ExtraArt()?.Load(art).Status.ToString() ?? "no extra art" : "no art";
                    Require(productArt == nameof(AreaExtraArtStatus.Ready), "the product's ground art reads: " + productArt);
                    made += $", full inventory: {lying.Length} on the ground ({AreaSchematics.OnGroundText(ground).Trim()}), ground art {productArt}";
                    // ARC-WORLD-062: a product created on the tile is not dropped, so it never decays (ledger ID9): two game days on the
                    // world runtime's frame step fires nothing for it and the products still lie there.
                    Require(!full.QueryItemDecays().Any(d => ground.OnGround.Contains(d.Entity)), "no product on the ground has a decay event");
                    var later = new Arcanum.World.FrameStep(full, new PlainEventWorld()).Jump(Arcanum.Simulation.Items.ItemDecayRules.DelayMs);
                    Require(later.Decays.Count == 0 && ground.OnGround.All(entity => !full.IsItemDestroyed(entity)) &&
                        ground.OnGround.All(entity => full.QueryPlacedItems().Any(p => p.Entity == entity)),
                        $"two game days on the products still lie ({later.Decays.Count} decays fired)");
                    made += $", still {ground.OnGround.Count} on the ground after 48 game hours";
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

// ARC-WORLD-062: a pass world for a detached session in a smoke (no NPC, no loot window).
internal sealed class PlainEventWorld : ISessionEventWorld
{
    public Arcanum.Simulation.Combat.LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
}

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-TECHNOLOGY-006 native observation on the original data: a dialog speaker of the shown map whose home-map data give an NPC
    /// crafter is passed to the production path the dialog's `su` takes once its talk ended; the window opens over the live session
    /// with that NPC as crafter and closes, changing nothing. Then, on a detached copy with the seeds attached again, the NPC is given
    /// the components of a learned record and (a synthetic sheet with every degree 7) assembles it into its own list. Prints counts and
    /// outcomes only.
    /// </summary>
    private async Task RunAreaNpcCrafterSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-TECHNOLOGY-006 NPC crafter smoke: " + why); }
        if (_areaSession is not { } session || _conversationHost is null) { GD.Print("ARC-TECHNOLOGY-006 native-smoke npc crafter SKIP (no area session or dialog host)"); return; }
        for (var i = 0; i < 600 && _schematicData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptSchematicData(); }
        Require(_schematicData is { Status: AreaSchematicDataStatus.Ready, Data: not null }, "data " + _schematicData?.Status);
        var data = _schematicData!.Data!;
        var npc = _conversationHost.Speakers.Select(speaker => AreaConversationState.EntityId(speaker.Row))
            .FirstOrDefault(entity => AreaNpcCrafter(entity) is not null && !session.QueryNpcDead(entity) && session.QueryNpcAdvancement(entity) is null);
        if (npc is null)
        {
            var speakers = _conversationHost.Speakers.Select(speaker => AreaConversationState.EntityId(speaker.Row)).ToArray();
            var homed = speakers.Count(entity => _combatHost?.HomeMap(entity) is not null);
            var fielded = speakers.Count(entity => _combatHost?.Fields(entity) is not null);
            GD.Print($"ARC-TECHNOLOGY-006 native-smoke npc crafter SKIP (no living speaker with home-map data: speakers {speakers.Length}, " +
                $"on a covered map {homed}, with fields {fielded}, effect rules {(_areaEffectRules is null ? "absent" : "present")})");
            return;
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var revision = session.Version.Revision;
            QueueDialogSchematics(npc);
            TakeScriptSchematics();
            Require(SchematicsOpen && _schematicsPanel!.ViewForSelfTest is { Crafter: { } crafter } && crafter == npc, "the window opened with the speaker as crafter: " + _schematicsStatus);
            var view = _schematicsPanel!.ViewForSelfTest!;
            CloseSchematicsByPlayer();
            Require(!SchematicsOpen && session.Version.Revision == revision, "the player's close closed it; nothing changed");

            var saved = session.Capture();
            var options = new GameSessionRestoreOptions(new(saved.Identity.World, session.Flags.Binding.Identity.Module),
                session.QueryClockSnapshot().ProfileBinding, new(Guid.NewGuid()), session.Limits, session.Rules.Mode, session.Experience, session.Difficulty,
                LoadRandom: session.QueryRandom());
            using var copy = GameSessionOwner.Restore(saved, options).Owner ?? throw new InvalidOperationException("copy");
            Require(_inventoryHost?.Attach(copy) == GameSessionFailure.None, "seeds attached to the copy");
            var real = AreaNpcCrafter(npc)!;
            var stats = real.Loaded.Sheet.Stats.SetItem(4, 20);
            var expert = real with { Loaded = real.Loaded with { Sheet = real.Loaded.Sheet with { Stats = stats, TechDegrees = [7, 7, 7, 7, 7, 7, 7, 7] } } };
            string? made = null;
            foreach (var record in data.Catalog.Records.Where(r => r.Kind == Arcanum.Data.Technology.SchematicKind.Learned && r.IsComplete))
            {
                if (record.Component1.Prototypes.Count == 0 || record.Component2.Prototypes.Count == 0) continue;
                if (data.Kind(record.Component1.Prototypes[0]) is not { } first || data.Kind(record.Component2.Prototypes[0]) is not { } second) continue;
                if (copy.AddItemToNpc(copy.Version, npc, first, AreaInventoryHost.DiagnosticInsertCheck).Disposition != GameSessionDisposition.Changed) continue;
                if (copy.AddItemToNpc(copy.Version, npc, second, AreaInventoryHost.DiagnosticInsertCheck).Disposition != GameSessionDisposition.Changed) continue;
                var before = copy.QueryNpcInventory(npc).Count;
                var result = copy.AssembleSchematicForNpc(copy.Version, expert, record, SchematicItems, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck);
                if (result.Disposition != GameSessionDisposition.Changed) continue;
                Require(copy.QueryNpcInventory(npc).Count == before - 2 + record.Quantity && result.Made.All(entity => copy.QueryNpcInventory(npc).Any(i => i.Entity == entity)),
                    "the products replace the components in the NPC's list");
                made = $"record {record.Number}: {result.Made.Count} made into the NPC";
                break;
            }
            Require(made is not null, "no learned record assembled");
            Require(session.Version.Revision == revision, "the live session is untouched");
            GD.Print("ARC-TECHNOLOGY-006 native-smoke npc crafter PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Live = $"opened from the su path, learned pages {view.Pages} in discipline {view.Discipline}, button {view.Button}", Assembly = made }));
        }
        finally
        {
            if (SchematicsOpen) EndSchematics();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
