using Godot;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.Presentation.Animation;
using Arcanum.World;
using System.Globalization;

namespace Arcanum.TerrainViewer;

using Input = global::Godot.Input;

public partial class TerrainViewerApp : Node
{
    private readonly TerrainLoadCoordinator _coordinator = new();
    private TerrainTextureCache _cache = new();
    private TerrainCanvas? _canvas;
    private TerrainCameraController? _camera;
    private TerrainDiagnosticHud? _hud;
    private long _generation;
    private bool _dragging;
    private bool _nativeSmoke;
    private string? _nativeSmokeScreenshot;
    private TerrainViewerArguments? _arguments;
    private Task? _lastLoad;
    private CancellationTokenSource? _loadCancellation;
    private bool _exiting;
    private string? _readySourceId;
    private int _readyDecodedCount;
    private int _readyStaticDecodedCount;
    private int _readyStaticAdmittedCount;
    private int _readyStaticCensus;
    private int _readyStaticOutsideScope;
    private ActorDiagnosticDrawCommand? _readyActor;
    private ActorDiagnosticPlacementInput? _readyActorInput;
    private ActorDiagnosticScenePlan? _readyActorPlan;
    private DiagnosticScenePlan? _readyDiagnosticPlan;
    private AnimationTimeline? _actorTimeline;
    private TerrainViewerMode _readyMode;
    private AreaLoadResult? _readyArea;
    /// <summary>ARC-RENDER-017: the interior shown by <c>--area-map</c>; null for the Crash Site exterior.</summary>
    private string? _readyMapName;
    private static bool IsWorldMode(TerrainViewerMode mode) => mode is TerrainViewerMode.WorldDiagnostic or TerrainViewerMode.Area;
    private long _readyElapsedMilliseconds;

    /// <summary>Internal-only bootstrap used by the deterministic SubViewport self-test.</summary>
    internal bool SelfTestBootstrap { get; set; }

    public override void _Ready()
    {
        // ARC-DEV-051: the bug report's 120 s log is filled from the first line on.
        BugReportLogger.Install();
        // ARC-DEV-047: the packaged commit (or a source run) and the user:// folder this build reads and writes.
        GD.Print($"ARC-DEV-047 build {ViewerBuild.Current.Text} user={OS.GetUserDataDir()}");
        // ARC-DEV-392: a map's transitions are kept on the device for the next process (a smoke run is one process); not in user://, which a smoke replaces.
        // The deterministic self-test (synthetic content) writes nothing into the owner's folder.
        if (!SelfTestBootstrap) AreaMapTransitionsStore.UseDeviceFolder();
        GD.Print($"ARC-DEV-392 map-cache folder={AreaMapTransitionsStore.Root ?? "off"}");
        var canvas = new TerrainCanvas { Name = "TerrainCanvas" };
        AddChild(canvas);
        _canvas = canvas;
        var cameraNode = new Camera2D { Name = "TerrainCamera", PositionSmoothingEnabled = false };
        AddChild(cameraNode);
        _camera = new TerrainCameraController(cameraNode);
        GetViewport().SizeChanged += HandleViewportSizeChanged;
        _hud = new TerrainDiagnosticHud { Name = "Hud" };
        AddChild(_hud);
        _hud.SetDiagnosticText("Editor diagnostic order | loading", automatedTestMode: false);

        // ARC-DEV-052: automated runs read and measure the panel, so it is shown for them.
        if (SelfTestBootstrap) { _hud.ShowPanel("self-test"); return; }

        var args = OS.GetCmdlineUserArgs().ToList();
        // The process hosting --self-test is automated too: its frame loop drives the frame-bound
        // tests, so it must not cap the engine frame rate (ARC-RENDER-009 pacing).
        _automatedHost = args.Contains("--self-test", StringComparer.Ordinal);
        // ARC-DEV-032: a self-test, a native smoke or the WORLD settings validation never reads or writes the player's settings and saves.
        if (_automatedHost || args.Contains("--native-smoke", StringComparer.Ordinal) || args.Contains("--world-settings-test", StringComparer.Ordinal))
        {
            ViewerAutomatedStorage.MarkAutomated();
            _hud.ShowPanel("automated run");
            // ARC-DEV-045: an automated run keeps drawing while its window cannot draw (minimized).
            // ARC-DEV-060: its pointer is only the events its tests push or parse, never the system pointer.
            if (DisplayServer.GetName() != "headless") { AddChild(new HiddenWindowDrawKeeper()); AddChild(new AutomatedPointerGate()); }
        }
        if (RunWorldSettingsTest(args)) return;
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            TerrainViewerSelfTest.Run(this);
            return;
        }
        try
        {
            (_nativeSmoke, _nativeSmokeScreenshot) = TakeNativeSmokeArguments(args);
            // ARC-UI-014: --launcher opens the launcher screen of a start without inputs.
            var launcher = args.Remove("--launcher");
            // ARC-DEV-050: --debug-mode turns the debug mode on for either kind of start.
            if (ViewerDebugMode.Take(args)) EnableDebugMode("command line");
            // ARC-DEV-046: without command-line inputs the game folder comes from the launcher settings.
            if (args.Count == 0)
            {
                _gameFolderStart = StartFromGameFolderAsync(launcher);
                return;
            }
            if (launcher) throw new ArgumentException("Usage");
            StartWithArguments(TerrainViewerArguments.Parse(args));
        }
        catch (ArgumentException)
        {
            // Parsing has no resolved mode.  Keep the safe Arguments stage
            // explicit rather than emitting an ambiguous bare usage string.
            ShowFailure("Diagnostic mode unresolved | failure stage=Arguments code=Usage | terrain 0/0/0 | static census 0/admitted 0/outside-scope 0");
        }
    }

    /// <summary>The start's arguments, then its preferences and the first load.</summary>
    private void StartWithArguments(TerrainViewerArguments arguments)
    {
        _arguments = _launchArguments = arguments;
        // ARC-DEV-059: the mode this start resolved, the start line `./arcanum run` waits for.
        GD.Print($"ARC-DEV-059 viewer start mode={ModeName(arguments.Mode)} map={arguments.AreaMapName ?? "-"} main-menu={arguments.MainMenu} smoke={_nativeSmoke}");
        // ARC-DEV-372: the viewer's readers share one index of the game folder per run (a self-test has no game folder and reads none).
        Arcanum.Data.Content.ContentStack.EnableSharing(!SelfTestBootstrap);
        // ARC-DEV-350: the game settings come first, the HD pack source is one of them.
        ConfigureGameSettings();
        ConfigureVisualProfile();
        ConfigureAudio();
        // ARC-DEV-350: after the first frame, so the box finds the viewport at its size.
        if (_hdPackProblem is not null) Callable.From(ShowStartupHdPackProblem).CallDeferred();
        _hud?.SetMode(_arguments.Mode);
        if (IsWorld) InitializeWorld();
        // ARC-DEV-387: a player's start with the menu in map mode does not load the start map yet: the menu opens at once and the map
        // loads when a new game's way begins (New Game), or the map of the save the player chooses loads instead.
        if (DefersStartLoad(arguments))
        {
            _startLoadPending = true; _startLoadLazy = true;
            OpenEarlyMainMenu();
            if (_mainMenu is null) BeginStartLoad("the menu did not open");
            return;
        }
        _lastLoad = StartLoadAsync(_arguments);
        // ARC-DEV-381 (O1): the load runs on its worker; the main menu opens now, not after the whole start map is published.
        OpenEarlyMainMenu();
    }

    private async Task StartLoadAsync(TerrainViewerArguments arguments, bool orchestrateSmoke = true)
    {
        if (_exiting) return;
        _hud?.SetMode(arguments.Mode);
        _startLoadPending = false; // ARC-DEV-387: any load is the load the menu waited for (or replaces it)
        if (IsWorldMode(arguments.Mode)) { InitializeWorld(); QuarantineWorld(); }
        GD.Print($"ARC-RENDER-004 load stage=begin smoke={orchestrateSmoke}");
        PerfProbe.Mark("load.begin");
        // ARC-RENDER-024: a load (a reload, a transfer, a quick load) supersedes a window move in flight; the move's session
        // stays the scene's (the load either carries it or ends it with the scene).
        if (_mapHandoff is { Kind: MapHandoffKind.Stream }) { _mapHandoff = null; _mapStreamStatus = null; }
        var priorCancellation = Interlocked.Exchange(ref _loadCancellation, new CancellationTokenSource());
        priorCancellation?.Cancel();
        priorCancellation?.Dispose();
        var cancellationToken = _loadCancellation!.Token;
        _generation = _coordinator.BeginGeneration();
        var requestGeneration = _generation;
        if (_exiting) return;
        ClearPublishedScene();
        ShowStatus($"Diagnostic mode {ModeName(arguments.Mode)} | loading stage=Arguments | terrain 0/0/0 | static census 0/admitted 0/outside-scope 0 | source-id {arguments.SourceId}");
        TerrainLoadResult? result = null;
        try
        {
            // ARC-DATA-037: a map opens under the data rules of the session the load carries; a load that carries none ends the
            // scene's session and starts a new one, so it opens under the default rules.
            arguments = WithSessionDataRules(arguments, _mapHandoff?.Session);
            result = await _coordinator.LoadAsync(requestGeneration, arguments, cancellationToken);
            GD.Print($"ARC-RENDER-004 load stage=decoded generation={requestGeneration}");
            PerfProbe.Mark("load.decoded");
            // ARC-DEV-367: the main-thread publication, and the first frame drawn after it.
            if (_exiting || !_coordinator.TryPublish(requestGeneration, () => { using (PerfProbe.Span("load.publish")) PublishReadyResult(arguments, result); })) return;
            PerfMarkAfterDraw("load.first-frame");
            TerrainViewerSmokeStageSet? stageClock = null; // ARC-DEV-399: ends the running stage's clock when the smoke fails
            if (!_exiting && _nativeSmoke && orchestrateSmoke)
            {
                try
                {
                    if (result.Area is not null)
                    {
                        // ARC-RENDER-024: ARC_AREA_MAP_SMOKE_ONLY (a comma list of map, stream, resources, transfer, roundtrip, worldmap, walk, census, cast, turn-based, knockdown, ranged, container, junk, barter, menu, scene, options, plants, bar, ai, ai-turn-based, ai-follower, premade, life, floaters, death, debug-travel, report, hover)
                        // narrows a map-mode smoke to those stages, so one map can be observed at several places quickly; absent, all but
                        // walk, census, barter, menu, scene, options, plants, bar, ai, ai-turn-based, life, floaters and death run.
                        TerrainViewerSmokeStageSet only = new(MapSmokeStages(), SmokeStageBoundary);
                        stageClock = only;
                        bool Stage(string name) => only!.Stage(name); // "!": the ?. tests below make the compiler see a captured non-null `only` as maybe null
                        if (_readyMapName is not null)
                        {
                            if (Stage("map")) { await RunAreaMapSmokeAsync(result); await RunAreaMapZoomFloorSmokeAsync(); await RunMapObjectLightSmokeAsync(); await RunLightAnimationSmokeAsync(); await RunSceneryAnimationSmokeAsync(); await RunAreaMapVaultSmokeAsync(result); await RunAreaHeroSmokeAsync(result); await RunAreaLevelMessageSmokeAsync(); await RunAreaLogbookSmokeAsync(); await RunAreaSchematicsSmokeAsync(); await RunAreaReadingSmokeAsync(); await RunAreaNpcCrafterSmokeAsync(); await RunReservedKeyHintSmokeAsync(); await RunAreaCombatModeSmokeAsync(result); await RunAreaAttackSmokeAsync(result); await RunAreaCastSmokeAsync(result); await RunAreaTurnBasedSmokeAsync(result); await RunAreaAudioSmokeAsync(); await RunAreaHeroKnockdownSmokeAsync(result); await RunAreaRoofSmokeAsync(); await RunAreaTalkSmokeAsync(); await RunAreaPortalSmokeAsync(); RunAreaDoorCloseSmoke(); await RunAreaGroundItemsSmokeAsync(); await RunAreaObjectAttackSmokeAsync(); await RunAreaFreedCellSmokeAsync(); await RunAreaFarClickSmokeAsync(); RunAreaFrameClockSmoke(); await RunAreaCriticalSmokeAsync(); await RunAreaLineOfFireSmokeAsync(); await RunAreaRangedSmokeAsync(); await RunAreaContainerLootSmokeAsync(); await RunAreaJunkPileSmokeAsync(); await RunAreaHoverWindowSmokeAsync(); await RunAreaBugReportSmokeAsync(); }
                            // ARC-MAGIC-015: the cast stage alone, for a quick first-cast observation (the map battery runs it too).
                            else
                            {
                                if (Stage("cast")) await RunAreaCastSmokeAsync(result);
                                // ARC-COMBAT-020: the turn-based round alone (the map battery runs it too).
                                if (Stage("turn-based")) await RunAreaTurnBasedSmokeAsync(result);
                                // ARC-COMBAT-021: the hero's fall and getting up alone (with --hero; the map battery runs it too).
                                if (Stage("knockdown")) await RunAreaHeroKnockdownSmokeAsync(result);
                                // ARC-COMBAT-015: the combat stages after the cast (object attack, critical, line of fire, ranged shot), as the
                                // map battery runs them last.
                                if (Stage("ranged")) { await RunAreaObjectAttackSmokeAsync(); await RunAreaCriticalSmokeAsync(); await RunAreaLineOfFireSmokeAsync(); await RunAreaRangedSmokeAsync(); }
                                // ARC-WORLD-101: the container loot window alone (the map battery runs it before the junk pile).
                                if (Stage("container")) await RunAreaContainerLootSmokeAsync();
                                // ARC-WORLD-063: the junk pile stage alone (the map battery runs it last).
                                if (Stage("junk")) await RunAreaJunkPileSmokeAsync();
                                // ARC-UI-050: the barter window over a speaker of the map (with --hero); ARC-WORLD-103: then an NPC's redress after
                                // its own regeneration (any map with such an NPC, a speaker or not).
                                if (Stage("barter")) { await RunAreaBarterSmokeAsync(); RunAreaNpcRedressSmoke(); }
                                // ARC-UI-011: the main menu over the start map (with --main-menu true, which blocks the other stages).
                                if (Stage("menu")) await RunAreaMainMenuSmokeAsync();
                                // ARC-UI-012: the scene flow from another map (with --main-menu true; the start map loads in it).
                                if (Stage("scene")) await RunAreaSceneFlowSmokeAsync();
                                // ARC-UI-013: the options window from the main menu and from play (start map, with --main-menu true).
                                if (Stage("options")) await RunAreaOptionsSmokeAsync();
                                // ARC-UI-018: the interface bars after a new game from the main menu (start map, with --main-menu true).
                                if (Stage("bar")) await RunAreaGameBarSmokeAsync();
                                // ARC-UI-020: the health and fatigue bars (with --hero, on a map with a speaker that can join).
                                if (Stage("life")) await RunAreaLifeBarsSmokeAsync();
                                // ARC-UI-021: the floating text (with --hero, on a map with a living NPC with combat values).
                                if (Stage("floaters")) await RunAreaFloatersSmokeAsync();
                                // ARC-UI-026: the hero's death to the reopened main menu (start map, with --main-menu true; wall clock).
                                if (Stage("death")) await RunAreaHeroDeathSmokeAsync();
                                // ARC-WORLD-081: only when named, the plant generator of a generated sector on a main-map window.
                                if (only?.Contains("plants") == true) await RunAreaTemplatePlantsSmokeAsync();
                                // ARC-SCRIPT-047: only when named, Virgil's heartbeat opens his talk with the PC at the start (AI on, start map, --hero);
                                // first of the AI stages: theirs run his heartbeat, which removes itself, and move the PC away.
                                if (only?.Contains("opening") == true) await RunAreaOpeningTalkSmokeAsync(result);
                                // ARC-WORLD-092: only when named, the first fight with the AI on (the other stages run with it off).
                                if (only?.Contains("ai") == true) await RunAreaAiSmokeAsync(result);
                                // ARC-WORLD-095: only when named, the fight in a turn-based round (the NPC acts in its own subturns).
                                if (only?.Contains("ai-turn-based") == true) await RunAreaAiTurnBasedSmokeAsync(result);
                                // ARC-WORLD-102: only when named, an NPC with a college level casts its spell at the hero (AI on).
                                if (only?.Contains("ai-cast") == true) await RunAreaAiCastSmokeAsync(result);
                                // ARC-DEV-248 (B13, SM11): only when named, a running animal killed by a spell lies on one cell: body, pool of blood and session (AI on, start map, --hero).
                                if (only?.Contains("critter-fall") == true) await RunAreaCritterFallSmokeAsync(result);
                                // ARC-DEV-325 (B10, SM30): only when named, a wolf running at the hero and a maintained spell survive F7 and F8 (AI on, Crash Site, --hero).
                                if (only?.Contains("save-resume") == true) await RunAreaSaveResumeSmokeAsync(result);
                                // ARC-DEV-264 (B08, SM06): only when named, an aggressive spell cast at a calm animal from afar makes it fight the hero (AI on, Crash Site, --hero).
                                if (only?.Contains("ai-react") == true) await RunAreaAiReactSmokeAsync(result);
                                // ARC-AI-1315 (VA4, VAG4): only when named, two NPCs put on one tile are separated by the attack starter's tile-sharer test and by the step apart (goal 68) (AI on, --hero).
                                if (only?.Contains("please-move") == true) await RunAreaPleaseMoveSmokeAsync();
                                // ARC-DEV-308 (B07, SM07): only when named, the map's NPC rows against the AI host's NPCs before a tick and after F7 and F8, read-only (AI on, start map, --hero).
                                if (only?.Contains("placement-census") == true) await RunAreaPlacementCensusSmokeAsync(result);
                                // ARC-DEV-301 (triage T1): only when named, an NPC's death and its loss of consciousness end the spell runs it casts and take their eye candy off the hero (AI on, Crash Site window, --hero).
                                if (only?.Contains("spell-end") == true) await RunAreaSpellEndSmokeAsync(result);
                                // ARC-DEV-304 (B11, SM09): only when named, the spell holds (stone, paralysis, entangle) written on the hero stop its steps, drop its clicks and let a walk resume after the END (--hero, Crash Site).
                                if (only?.Contains("hold-hero") == true) await RunAreaHoldHeroSmokeAsync(result);
                                // ARC-DEV-1148 (magic audit leftover): only when named, spells 30 and 77 on the hero and 46 and 76 on an NPC add their effects, move the stats and take them off at the END (--hero, Crash Site).
                                if (only?.Contains("spell-effects") == true) await RunAreaSpellEffectsSmokeAsync(result);
                                // ARC-MAGIC-036 (MA-5): only when named, Spatial Distortion's Movement component relocates the hero along the path request, at most eight tiles (--hero, Crash Site).
                                if (only?.Contains("movement") == true) await RunAreaMovementSmokeAsync(result);
                                // ARC-DEV-335 (V7d, B50): only when named, a light worn by the hero lights the area around it (with --hero, a lit map).
                                if (only?.Contains("worn-light") == true) await RunAreaWornLightSmokeAsync(result);
                                // ARC-MAGIC-1229 (FR-LIGHT-011, IW6): only when named, Illuminate keeps a live light on the hero and on an NPC while the spell holds, the darkness sampler sees it and the END takes it off (--hero, a lit map with a living NPC).
                                if (only?.Contains("eye-light") == true) await RunAreaEyeLightSmokeAsync(result);
                                // ARC-DEV-353 (V7d second part): only when named, an NPC's light is carried with the NPC (AI on, a lit map with an NPC that carries a light).
                                if (only?.Contains("critter-light") == true) await RunAreaCritterLightSmokeAsync();
                                // ARC-DEV-362 (technology audit T-a): only when named, degrees bought on the sheet, a Herbology heal and a Gun Smithy gun assembled, used and loaded again (--hero, best on the map "crash site cave").
                                if (only?.Contains("tech-craft") == true) await RunAreaTechCraftSmokeAsync();
                                // ARC-DEV-365 (technology audit T-g): only when named, the item cursor from the use icon, a press on a tile (goal 41) and on an NPC (goal 39) with a trap device (--hero, a map with a living NPC).
                                if (only?.Contains("item-aim") == true) await RunAreaItemAimSmokeAsync();
                                // ARC-MAGIC-051 (technology audit T-s): only when named, Therapeutics and Chemistry products assembled and used on the player's data (--hero, the Crash Site cave).
                                if (only?.Contains("tech-products") == true) await RunAreaTechProductsSmokeAsync();
                                // ARC-SKILLS-1156: only when named, the hotkey bar's Disarm traps entry arms the skill cursor and a press disarms a trapped container (--hero, a map with a container in its window).
                                if (only?.Contains("disarm") == true) await RunAreaDisarmSmokeAsync();
                                // ARC-DEV-367 (O10): only when named, the timing probe's tables: the startup marks, six opens of each window, a map change out and back (--hero, a sector with a map exit).
                                if (only?.Contains("perf") == true) await RunAreaPerfSmokeAsync();
                                // ARC-DEV-367 (O10): only when named, the startup marks to the main menu's first frame and a new game from it (--main-menu).
                                if (only?.Contains("perf-menu") == true) await RunAreaPerfMenuSmokeAsync();
                                // ARC-DEV-304 (B24, SM26): only when named, spell 56 sets the spoken-with-dead flag on a corpse: the click talks to it instead of looting it, and loots again when the flag is gone (--hero, Crash Site).
                                if (only?.Contains("spirit-talk") == true) await RunAreaSpiritTalkSmokeAsync(result);
                                // ARC-WORLD-1160 (E3-a): only when named, the sleep window on the hero (--hero): a rest of one hour, until healed and until morning jumps the clock and heals him, the window closes at the end, the screen fades and the key toggles it.
                                if (only?.Contains("sleep") == true) await RunAreaSleepSmokeAsync();
                                // ARC-WORLD-1162 (E3-d): only when named, the beds of the Crash Site cave (--hero, the cave): a bed with no script refuses the hero (message 8), an occupied one (10), the scripted bed seats him in a sleep window that heals twice the ground and empties on the close.
                                if (only?.Contains("bed") == true) await RunAreaBedSmokeAsync();
                                // ARC-DEV-347 (E1-a): only when named, a scenery and an eye-candy object the session creates by prototype are drawn, picked or not, resident, saved and destroyed (--hero, a map window).
                                if (only?.Contains("created-scenery") == true) await RunAreaCreatedSceneryStageAsync();
                                // ARC-DEV-370 (E1-c2): only when named, a critter the session creates by prototype is drawn, picked, moved by the AI's movers with a walk frame and gone once destroyed (--hero, a map window with the inventory).
                                if (only?.Contains("created-critter") == true) await RunAreaCreatedCritterStageAsync();
                                if (only?.Contains("trait-body") == true) await RunAreaTraitBodyStageAsync();
                                // ARC-RENDER-1544: only when named, a companion and an NPC the AI never moved are drawn with the stand ART of the weapon they show after a re-wield, not the weapon letter of the map art (--hero, an AI host).
                                if (only?.Contains("weapon-art") == true) await RunAreaWeaponArtStageAsync();
                                // ARC-MAGIC-045 (E4-a2): only when named, the object flags a spell writes (translucent, shrunk, don't-draw, invisible) change how an NPC's body is drawn, and a hit on a shielded critter plays the run spell's hit eye candy (--hero, a map window with the inventory).
                                if (only?.Contains("body-styles") == true) await RunAreaBodyStylesStageAsync();
                                // ARC-DEV-371 (E1-c3): only when named, the installed tile script 1629 creates critters next to the hero (--hero, a map window with the inventory): flagged, hosted by the AI host, notified, destroyed.
                                if (only?.Contains("script-critters") == true) await RunAreaScriptCrittersStageAsync();
                                // ARC-WORLD-1159 (E2-c): only when named, the world journey's random encounters on the start map (--hero, the wilderness sector): a forced roll makes critters on the ring spots, closes an open rest window and an open world map (which then refuses to open for 4 s), and ends a journey in a landing where the marker stood.
                                if (only?.Contains("encounters") == true) await RunAreaEncountersSmokeAsync();
                                // ARC-DEV-352 (E1-b): only when named, Fireflash's flash on an empty tile and none on an occupied one; Wall of Fire's pieces, their burn and their END (--hero, a map window).
                                if (only?.Contains("summon-flash") == true) await RunAreaSummonSmokeAsync(SummonFlashSpell);
                                if (only?.Contains("summon-wall") == true) await RunAreaSummonSmokeAsync(SummonWallSpell);
                                // ARC-MAGIC-053 (E1-e): only when named, Summon Undead makes a hidden, summoned, mind-controlled creature of list 5 that shows at the animation's callback, joins the party, has its creation notice queued and goes with its run (--hero, a map window).
                                if (only?.Contains("summon-undead") == true) await RunAreaSummonUndeadSmokeAsync();
                                // ARC-DEV-250 (B12, SM08): only when named, a damage spell with a radius cast at one of three animals that move before the hit (AI on, elven ruins, --hero elf-female).
                                if (only?.Contains("cast-multi") == true) await RunAreaCastMultiSmokeAsync(result);
                                // ARC-DEV-263 (B33, SM24): only when named, the buttons on a joined companion's portrait and body (start map, --hero).
                                if (only?.Contains("companion-click") == true) await RunAreaCompanionClickSmokeAsync(result);
                                // ARC-WORLD-115: only when named, a joined speaker fights beside the hero (AI on, start map, --hero).
                                if (only?.Contains("ai-follower") == true) await RunAreaAiFollowerSmokeAsync(result);
                                // ARC-DEV-170 (B06): only when named, the town's route NPCs walk their waypoints and standpoints (AI on, --hero, --sector 1409,1314) (SM05).
                                if (only?.Contains("npc-resident") == true) await RunAreaNpcResidentSmokeAsync(result);
                                // ARC-DEV-208 (MIG14 S2, SM08): only when named, the NPCs' goal runs survive F7 and F8 (AI on, --hero, --sector 1409,1314).
                                if (only?.Contains("saved-runs") == true) await RunAreaSavedRunsSmokeAsync(result);
                                // ARC-DEV-203 (B46, SM19): only when named, the fire of an animated scenery is drawn over its background.
                                if (only?.Contains("fire-order") == true) await RunFireOrderSmokeAsync();
                                // ARC-DEV-203 (B45, SM18): only when named, the hero's shade through a vault out of a window.
                                if (only?.Contains("indoor-fade") == true) await RunIndoorFadeSmokeAsync(result);
                                // ARC-DEV-206 (B25): only when named, an NPC walked onto the agent's exit tile runs the tile's script (AI on, --hero, --sector 1452,1291) (SM28).
                                if (only?.Contains("npc-tile") == true) await RunAreaNpcTileSmokeAsync();
                                // ARC-AI-1313 (VAG2): only when named, the standpoint step's wander goal 65/66 moves an NPC about its tether within four tiles (AI on, --hero, --sector 1452,1291).
                                if (only?.Contains("npc-wander") == true) await RunAreaNpcWanderSmokeAsync();
                                // ARC-DEV-298 (B20): only when named, the cave tiles of script 2768 (the ghost of Brego) run and cast (AI on, --hero, map crash site cave) (SM16).
                                if (only?.Contains("tile-ghost") == true) await RunAreaGhostTileSmokeAsync();
                                // ARC-DEV-299 (B23): only when named, every container of the window is clicked as the player does (--hero, best on --sector 1409,1314) (SM16).
                                if (only?.Contains("container-click") == true) await RunAreaContainerClickSmokeAsync();
                                // ARC-WORLD-114: only when named, the pre-made heroes' heartbeat scripts at the Crash Site (AI on, start map, --hero).
                                if (only?.Contains("premade") == true) await RunAreaPremadeHeroesSmokeAsync(result);
                                // ARC-SKILLS-010: only when named, the pick-pocket skill on a living NPC (AI on for the hero's sight).
                                if (only?.Contains("pickpocket") == true) await RunAreaPickPocketSmokeAsync();
                                // ARC-DEV-202 (B03): only when named, a dead NPC's worn items are looted like the others (SM03).
                                if (only?.Contains("loot-worn") == true) await RunAreaLootWornSmokeAsync();
                                // ARC-DEV-333 (B48): only when named, a take without room in the hero's grid is refused and the rest stays in the body.
                                if (only?.Contains("loot-room") == true) await RunAreaLootRoomSmokeAsync();
                                // ARC-DEV-111: only when named, the interface mode stack through the production keys (SM23).
                                if (only?.Contains("inv-ui") == true) await RunAreaInterfaceModesSmokeAsync();
                                // ARC-DEV-339 (B54): only when named, the inventory window's arrange button lays the hero's items out by rows, then by columns (--hero); it runs after inv-ui on purpose: run before it in one process, the edge-scroll check of inv-ui failed once (cause not found).
                                if (only?.Contains("inv-arrange") == true) await RunAreaInvArrangeSmokeAsync();
                                // ARC-SCRIPT-046: only when named, Virgil stepping onto the plane wreck tile opens his conversation (main map, Crash Site).
                                if (only?.Contains("wreck") == true) await RunAreaWreckTileSmokeAsync();
                                // ARC-SCRIPT-052: only when named, Virgil stepping onto the altar tile opens his altar talk (--sector 1452,1291).
                                if (only?.Contains("altar") == true) await RunAreaAltarTileSmokeAsync();
                                // ARC-DEV-138: a used door closes by itself after the original's interval and the Esc menu stops it (SM10; the map battery runs it after the portal stage).
                                if (Stage("door-close")) RunAreaDoorCloseSmoke();
                                // ARC-UI-057: the hovered object's highlight and message-window description alone (the map battery runs it too).
                                if (Stage("hover")) await RunAreaHoverWindowSmokeAsync();
                                // ARC-DEV-051: the bug report alone (the map battery runs it last).
                                if (Stage("report")) await RunAreaBugReportSmokeAsync();
                                // ARC-DEV-209 (MIG14 S3, GT49): only when named, an NPC the AI host walks across a closed door opens it first, and it runs last of the window stages because it leaves a door open (AI on, --hero, --sector 1409,1314).
                                if (only?.Contains("npc-door") == true) await RunAreaNpcDoorSmokeAsync();
                            }
                        }
                        else await RunAreaSmokeAsync(result);
                        // S5 must observe the retired generation become
                        // collectable; this frame may not keep it reachable.
                        result = null;
                        if (_readyMapName is null || Stage("resources")) await RunAreaResourcesAsync();
                        // ARC-MAGIC-042 (T-m1): only when named (with --hero), a creature item thrown from the pack becomes a critter that follows the hero, or one that attacks everyone around it. ARC-DEV-1187: it runs before the stages that move the window (transfer, roundtrip, stream): with the AI on, the hero it throws from stands where the first window's wolves are, and after the stream's move it stood among other ones that killed it.
                        if (_readyMapName is not null && only?.Contains("creature-item") == true) await RunAreaCreatureItemSmokeAsync();
                        // ARC-WORLD-031: last, because a transfer to another map publishes a new scene.
                        // ARC-DEV-116: the follower-home stage (only when named) is that same carry, with the former follower's homecoming after its jump smoke.
                        if (_readyMapName is not null && (Stage("transfer") || only?.Contains("follower-home") == true)) { await RunAreaMapCompanionSmokeAsync(); await RunAreaMapTransferSmokeAsync(); }
                        // ARC-RENDER-024: on a window map, into an interior and back by its exits.
                        if (_readyMapName is not null && Stage("roundtrip")) await RunAreaMapRoundTripSmokeAsync();
                        // ARC-RENDER-024: last on a window map, as the window it ends on holds no exit to use afterwards.
                        if (_readyMapName is not null && Stage("stream")) await RunAreaMapStreamSmokeAsync();
                        // ARC-COMBAT-024: only when named, a window published while the hero is dead (last: the hero stays dead).
                        if (_readyMapName is not null && only?.Contains("dead-window") == true) await RunAreaDeadHeroWindowSmokeAsync();
                        // ARC-WORLD-042: only when named, a long walk across the main map by its window route.
                        if (_readyMapName is not null && only?.Contains("walk") == true) await RunAreaMapWalkSmokeAsync();
                        // ARC-DEV-247 (B43, B44, SM17): only when named (with --hero), the hero's click on each barrel of the map walks only over Open steps, by a short route, and opens the search.
                        if (_readyMapName is not null && only?.Contains("route-barrel") == true) await RunAreaRouteBarrelSmokeAsync();
                        // ARC-DEV-249 (B14, SM12): only when named (with --hero), a real grenade thrown from the pack explodes where it lands, hurts the NPCs around and is gone.
                        if (_readyMapName is not null && only?.Contains("grenade") == true) await RunAreaGrenadeSmokeAsync();
                        // ARC-RENDER-1170: only when named (with --hero, and ffmpeg installed), the movie player on two generated test movies: the screen, the frozen clocks, the key skip, the failures that must not hang.
                        if (_readyMapName is not null && only?.Contains("movies") == true) await RunAreaMoviesSmokeAsync();
                        // ARC-DEV-282: only when named (with --hero), an NPC archer of the map shoots the hero: the arrow flies as a projectile and strikes through the AI host.
                        if (_readyMapName is not null && only?.Contains("npc-shot") == true) await RunAreaNpcShotSmokeAsync();
                        // ARC-UI-048: after the stream, as the landing loads the start map around another place.
                        if (_readyMapName is not null && Stage("worldmap")) await RunAreaWorldMapSmokeAsync();
                        // ARC-DEV-050: only when named (with --debug-mode), the debug teleport from this map to a far area.
                        if (_readyMapName is not null && only?.Contains("debug-travel") == true) await RunAreaDebugTravelSmokeAsync();
                        // ARC-DEV-170 (B05): only when named (with --debug-mode and --hero), a joined speaker, the window moved to a far area and a quick load there (SM01).
                        if (_readyMapName is not null && only?.Contains("follower-far") == true) await RunAreaFollowerFarSmokeAsync();
                        // ARC-MAGIC-1147 (MT16, TP6-TP9): only when named (with --hero), the same companion carried by a Teleportation cast's pick on the world map.
                        if (_readyMapName is not null && only?.Contains("teleport-follow") == true) await RunAreaFollowerFarSmokeAsync(viaSpell: true);
                        // ARC-DEV-305 (B01, B36): only when named (with --debug-mode and --hero), what the hero took and made survives the window move and a quick load (SM02).
                        if (_readyMapName is not null && only?.Contains("inv-carry") == true) await RunAreaInvCarrySmokeAsync();
                        // ARC-RENDER-027: only when named, every map of the catalogue in turn (last: it leaves the last map shown).
                        if (only?.Contains("census") == true) await RunAreaMapCensusAsync();
                        stageClock.Finish();
                        GetTree().Quit(0);
                    }
                    else await RunNativeSmokeAsync(result);
                }
                catch (TerrainViewerException exception)
                {
                    // All failures after ready publication while exercising a
                    // native-smoke frame are readback failures.  Preserve the
                    // load's reached aggregates rather than reverting to zero.
                    throw new TerrainViewerException(exception.Error with
                    {
                        Stage = TerrainViewerFailureStage.Readback,
                        Progress = result is null ? TerrainViewerProgress.Empty : ProgressFromResult(result)
                    });
                }
                finally { stageClock?.Finish(); }
            }
            GD.Print($"ARC-RENDER-004 load stage=complete generation={requestGeneration}");
        }
        catch (OperationCanceledException)
        {
            // Reload/exit cancellation is intentionally silent: a newer
            // generation owns the HUD and scene, and no cancelled load may
            // publish an error over it.
        }
        catch (Arcanum.Data.Area.AreaException exception)
        {
            if (_exiting) return;
            GD.Print($"ARC-RENDER-007 failure stage={exception.Issue.Stage} code={exception.Issue.Code} ready=False");
            var error = new TerrainViewerError(TerrainViewerErrorCode.RenderFailure,
                Stage: TerrainViewerFailureStage.Upload, Progress: TerrainViewerProgress.Empty);
            if (CensusLoadFailed($"{exception.Issue.Stage}/{exception.Issue.Code}")) return;
            if (_nativeSmoke) { GetTree().Quit(1); return; }
            _coordinator.TryPublish(requestGeneration, () => PublishFailure(arguments.Mode, error));
        }
        catch (Arcanum.Data.Traversal.AreaStepInvocationException exception)
        {
            if (_exiting) return;
            GD.Print($"ARC-RENDER-007 failure stage=AreaSteps.{exception.Stage} code={exception.Code} ready=False");
            var error = new TerrainViewerError(exception.Code switch
                {
                    Arcanum.Data.Traversal.AreaStepInvocationCode.InputUnavailable => TerrainViewerErrorCode.InputUnavailable,
                    Arcanum.Data.Traversal.AreaStepInvocationCode.LimitExceeded => TerrainViewerErrorCode.ResourceLimit,
                    _ => TerrainViewerErrorCode.FingerprintMismatch
                },
                Stage: TerrainViewerFailureStage.Fingerprint, Progress: TerrainViewerProgress.Empty);
            if (CensusLoadFailed($"AreaSteps.{exception.Stage}/{exception.Code}")) return;
            if (_nativeSmoke) { GetTree().Quit(1); return; }
            _coordinator.TryPublish(requestGeneration, () => PublishFailure(arguments.Mode, error));
        }
        catch (TerrainViewerException exception)
        {
            if (_exiting) return;
            if (CensusLoadFailed($"{exception.Error.Stage}/{exception.Error.Code}")) return;
            if (_nativeSmoke) { GD.Print(FormatFailure(arguments.Mode, exception.Error)); GetTree().Quit(1); return; }
            if (!_coordinator.TryPublish(requestGeneration, () => PublishFailure(arguments.Mode, exception.Error))) return;
        }
        catch (Exception exception)
        {
            if (_exiting) return;
            if (arguments.Mode == TerrainViewerMode.Area)
            {
                var frontier = exception.Message.StartsWith("AREA_", StringComparison.Ordinal)
                    && exception.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
                    ? exception.Message : exception.GetType().Name;
                GD.Print("ARC-RENDER-007 frontier=" + frontier);
                // An incomplete area names its first issues (codes and stages, never paths or bytes).
                if (exception is Arcanum.Data.Area.AreaIncompleteException incomplete)
                    GD.Print($"ARC-RENDER-007 issues={incomplete.Report.Issues.Count} first=[{string.Join(", ", incomplete.Report.Issues.Take(8).Select(issue => $"{issue.Code}/{issue.Stage}#{issue.SourceIndex?.ToString() ?? "-"}"))}]");
            }
            var error = new TerrainViewerError(TerrainViewerErrorCode.RenderFailure,
                Stage: TerrainViewerFailureStage.Upload,
                Progress: result is null ? TerrainViewerProgress.Empty : ProgressFromResult(result));
            if (CensusLoadFailed(exception.Message.StartsWith("AREA_", StringComparison.Ordinal) && exception.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
                ? exception.Message : exception.GetType().Name)) return;
            if (_nativeSmoke) { GD.Print(FormatFailure(arguments.Mode, error)); GetTree().Quit(1); return; }
            if (!_coordinator.TryPublish(requestGeneration, () => PublishFailure(arguments.Mode, error))) return;
        }
    }

    /// <summary>
    /// Sole ready-publication path for both production loads and the internal
    /// synthetic harness. It uploads decoded bytes only into the texture cache
    /// and retains scalar/current-plan state after the local result is gone.
    /// </summary>
    private void PublishReadyResult(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        if (result.Area is not null) { PublishAreaReady(arguments, result); return; }
        _cache.Upload(result.Textures);
        _cache.UploadStatic(result.StaticTextures);
        _cache.UploadActor(result.ActorTextures);
        _readyDiagnosticPlan = result.DiagnosticPlan ?? DiagnosticScenePlan.Create(result.Plan, result.StaticPlan);
        _canvas!.Publish(_readyDiagnosticPlan, _cache);
        _camera!.SetPlan(_readyDiagnosticPlan, GetViewport().GetVisibleRect().Size);
        _readySourceId = arguments.SourceId;
        _readyDecodedCount = result.Textures.Count;
        _readyStaticDecodedCount = result.StaticTextures.Count;
        _readyStaticAdmittedCount = result.StaticPlan?.Commands.Count ?? 0;
        _readyStaticCensus = result.StaticCensusCount;
        _readyStaticOutsideScope = result.StaticOutsideScopeCount;
        _readyActor = result.ActorPlan?.Current;
        _readyActorPlan = result.ActorPlan;
        _readyActorInput = arguments.ActorInput;
        _actorTimeline = result.ActorPlan is null ? null : new AnimationTimeline(result.ActorPlan.Current.FrameCount, result.ActorPlan.Current.RawFramesPerSecond);
        if (_actorTimeline is not null && result.ActorPlan!.Current.Frame != 0) _actorTimeline.Seek(result.ActorPlan.Current.Frame);
        _readyMode = arguments.Mode;
        _hud!.SetMode(arguments.Mode);
        if (arguments.Mode == TerrainViewerMode.WorldDiagnostic)
        {
            InitializeWorld(); _actorTimeline = null;
            RetireWorldLifecycle();
            _worldActor.Configure(result.TraversalSnapshot ?? throw new InvalidOperationException("GRID_SNAPSHOT_REQUIRED"), WorldTime);
            _worldGridInput = new(); QuarantineWorld(); PublishWorldSelection();
            _worldFitView = false;
            _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            PaletteScenePublished();
        }
        _readyElapsedMilliseconds = result.ElapsedMilliseconds;
        RefreshReadyStatus();
        if (arguments.Mode == TerrainViewerMode.WorldDiagnostic)
            GD.Print($"ARC-WORLD-003 ready terrain={result.ResolvedCount}/{result.TileCount}/{result.FacadeCount} static={_readyStaticAdmittedCount} actor-textures={result.ActorTextures.Count} viewport={WorldViewport.X}x{WorldViewport.Y} display={DisplayServer.GetName()} test={SelfTestBootstrap || _nativeSmoke} cardinal-profile={result.TraversalSnapshot?.ProfileId} parity=NOT_VERIFIED");
    }

    private void ClearPublishedScene()
    {
        RetireWorldLifecycle();
        _worldActor.DisposeSession(); _worldGridInput = new();
        // A disposed session's follower must not keep its step snapshot reachable.
        _worldRoute = null; _worldRouteSession = null;
        RetirePassabilityOverlay();
        RetireAreaOverview();
        ClearReadyMetrics();
        _canvas!.ClearScene();
        RetireHoverRing();
        _cache.Clear();
    }

    /// <summary>Sole failure publication route for normal loads and self-test injection.</summary>
    private void PublishFailure(TerrainViewerMode mode, TerrainViewerError error)
    {
        _lastPublishedFailure = $"{error.Stage}/{error.Code}";
        EarlyMenuSceneFailed();
        if (IsWorldMode(mode))
        {
            _worldGate.Close(); _worldReducer.Quarantine();
            _dragging = false; _worldMenuAnalogDirection = 0;
            _camera?.SafetyResetWorld();
            PaletteSceneFailed(error);
        }
        _hud?.SetMode(mode);
        ClearPublishedScene();
        ShowFailureStatus(mode, error);
    }

    internal void ConfigureSelfTestTextureCache(Func<Image, ImageTexture> createTexture, Action<ImageTexture> disposeTexture)
    {
        if (!SelfTestBootstrap || IsInsideTree())
            throw new InvalidOperationException("Texture-cache injection is limited to an unready self-test bootstrap.");
        ArgumentNullException.ThrowIfNull(createTexture);
        ArgumentNullException.ThrowIfNull(disposeTexture);
        _cache.Dispose();
        _cache = new TerrainTextureCache(createTexture, disposeTexture);
    }

    internal void PublishSyntheticForSelfTest(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("Synthetic publication is limited to the internal self-test bootstrap.");
        ClearPublishedScene();
        try { PublishReadyResult(arguments, result); }
        catch (TerrainViewerException exception) { PublishFailure(arguments.Mode, exception.Error); throw; }
        catch
        {
            var error = new TerrainViewerError(TerrainViewerErrorCode.RenderFailure, Stage: TerrainViewerFailureStage.Upload,
                Progress: ProgressFromResult(result));
            PublishFailure(arguments.Mode, error);
            throw;
        }
    }

    /// <summary>Self-test calls the production clear route used by reload and failure.</summary>
    internal void ClearSyntheticForSelfTest()
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("Synthetic clearing is limited to the internal self-test bootstrap.");
        ClearPublishedScene();
    }

    internal void PublishSyntheticFailureForSelfTest(TerrainViewerMode mode, TerrainViewerError error)
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("Synthetic failure is limited to the internal self-test bootstrap.");
        PublishFailure(mode, error);
    }

    internal TerrainViewerSelfTestSnapshot GetSelfTestSnapshot()
        => new(_readyActor, _readyActorInput, _actorTimeline?.IsPlaying ?? false, _cache.Count, _cache.CreatedCount, _cache.DisposedCount, _hud?.Text ?? string.Empty,
            _hud?.GlobalPosition ?? Vector2.Zero, _camera?.State.X ?? 0, _camera?.State.Y ?? 0, _camera?.State.Zoom ?? 0,
            _canvas?.GetGlobalTransformWithCanvas() ?? Transform2D.Identity);

    private static (bool Enabled, string? ScreenshotPath) TakeNativeSmokeArguments(List<string> args)
    {
        var enabled = args.Remove("--native-smoke");
        var screenshotIndex = args.IndexOf("--native-smoke-screenshot");
        if (screenshotIndex < 0) return (enabled, null);
        if (!enabled || screenshotIndex + 1 >= args.Count) throw new ArgumentException("Usage");
        var screenshotPath = args[screenshotIndex + 1];
        args.RemoveAt(screenshotIndex + 1);
        args.RemoveAt(screenshotIndex);
        return (enabled, screenshotPath);
    }

    private async Task RunNativeSmokeAsync(TerrainLoadResult result)
    {
        if (_readyMode == TerrainViewerMode.WorldDiagnostic) { await RunWorldNativeSmokeAsync(result); return; }
        var commandCount = _canvas!.CommandCount;
        var textureCount = _cache.Count;
        var geometrySignature = _canvas.GeometrySignature;
        await AssertRepresentativeProductionCanvasAsync(result);
        if (result.StaticPlan is not null) await AssertSelectedStaticProductionCanvasAsync(result);
        if (result.ActorPlan is not null) await AssertActorProductionCanvasAsync(result);
        await SetSmokeWindowSizeAsync(new Vector2I(1280, 800));
        await NextFrameAsync();
        var initial = _camera!.State;
        var initialCameraPosition = _camera.Camera.Position;
        var initialHudPosition = _hud!.GlobalPosition;
        GD.Print("ARC-RENDER-004 native-smoke stage=initial-readback");
        if (!await CaptureAndValidateSmokeFrameAsync(new Vector2I(1280, 800), commandCount, textureCount))
        {
            GetTree().Quit(0);
            return;
        }
        GD.Print("ARC-RENDER-004 native-smoke stage=input");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-004 native-smoke stage=input-keyboard-frame");
        var afterKeyboard = _camera.State;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-004 native-smoke stage=input-keyboard-release-frame");
        Input.ParseInputEvent(new InputEventMouseButton { Device = AutomatedPointerGate.TestDevice, ButtonIndex = MouseButton.WheelUp, Pressed = true });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-004 native-smoke stage=input-wheel-frame");
        if (_camera.State.FitMode || _camera.State.Zoom <= initial.Zoom || afterKeyboard.Y >= initial.Y
            || _camera.Camera.Position == initialCameraPosition || _camera.Camera.Zoom.X <= initial.Zoom
            || _hud.GlobalPosition != initialHudPosition || !HasReadyHudContent(textureCount)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        var beforeDrag = _camera.State;
        Input.ParseInputEvent(new InputEventMouseButton { Device = AutomatedPointerGate.TestDevice, ButtonIndex = MouseButton.Left, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseMotion { Device = AutomatedPointerGate.TestDevice, Relative = new Vector2(20, -10) });
        Input.ParseInputEvent(new InputEventMouseButton { Device = AutomatedPointerGate.TestDevice, ButtonIndex = MouseButton.Left, Pressed = false });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-004 native-smoke stage=input-drag-frame");
        var manual = _camera.State;
        if (_camera.State.FitMode || manual.X == beforeDrag.X || manual.Y == beforeDrag.Y) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        await SetSmokeWindowSizeAsync(new Vector2I(1440, 900));
        GD.Print("ARC-RENDER-004 native-smoke stage=input-manual-resize");
        if (_camera.State != manual) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
        await NextFrameAsync();
        if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, Pressed = true });
        await SetSmokeWindowSizeAsync(new Vector2I(1920, 1080));
        await NextFrameAsync();
        if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        // A display clamp truthfully makes the requested-size evidence
        // NOT_VERIFIED, but must not short-circuit the independent lifecycle,
        // input, reload, and cleanup checks below.
        var exactSecondSize = await CaptureAndValidateSmokeFrameAsync(new Vector2I(1920, 1080), commandCount, textureCount);
        var fullscreen = false;
        GD.Print("ARC-RENDER-004 native-smoke stage=fullscreen");
        var initialMode = GetWindow().Mode;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F11, Pressed = true });
        await NextFrameAsync();
        fullscreen = GetWindow().Mode != initialMode;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await NextFrameAsync();
        var fullscreenRestored = !fullscreen || GetWindow().Mode == Window.ModeEnum.Windowed;
        if (!fullscreenRestored) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        for (var cycle = 0; cycle < 100; cycle++)
        {
            if (cycle % 10 == 0) GD.Print($"ARC-RENDER-004 native-smoke stage=cycle-{cycle}");
            var beforePan = _camera.State;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
            var observedPan = false;
            try
            {
                // Movement is polled in _Process. A single queued input frame
                // does not guarantee that the held state was sampled, so keep
                // this actual-input assertion bounded but synchronized to an
                // observed world-camera coordinate mutation.
                for (var frame = 0; frame < 3 && !observedPan; frame++)
                {
                    await NextFrameAsync();
                    observedPan = _camera.State.X != beforePan.X || _camera.State.Y != beforePan.Y;
                }
                if (!observedPan) ThrowNativeSmokeCycleFailure(cycle, "keyboard-pan");
            }
            finally
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
            }
            var afterPan = _camera.State;
            var beforeZoom = _camera.Camera.Zoom;
            try
            {
                Input.ParseInputEvent(new InputEventMouseButton { Device = AutomatedPointerGate.TestDevice, ButtonIndex = MouseButton.WheelUp, Pressed = true });
                await NextFrameAsync();
                if (_camera.State.FitMode || _camera.Camera.Zoom.X <= beforeZoom.X || _camera.Camera.Position == new Vector2((float)beforePan.X, (float)beforePan.Y))
                    ThrowNativeSmokeCycleFailure(cycle, "wheel-pan");
            }
            finally
            {
                Input.ParseInputEvent(new InputEventMouseButton { Device = AutomatedPointerGate.TestDevice, ButtonIndex = MouseButton.WheelUp, Pressed = false });
            }
            try
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
                await NextFrameAsync();
                if (!_camera.State.FitMode) ThrowNativeSmokeCycleFailure(cycle, "reset-fit");
            }
            finally
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = false });
            }
            var requestedSize = cycle % 2 == 0 ? new Vector2I(1280, 800) : new Vector2I(1440, 900);
            await SetSmokeWindowSizeAsync(requestedSize);
            var actualViewport = GetViewport().GetVisibleRect().Size;
            var diagnosticBounds = result.DiagnosticPlan?.WorldBounds ?? result.Plan.WorldBounds;
            var expectedFit = CameraPolicy.Fit(diagnosticBounds.X, diagnosticBounds.Y, diagnosticBounds.Width,
                diagnosticBounds.Height, actualViewport.X, actualViewport.Y);
            var cycleValid = _camera.State.FitMode && _camera.Camera.Position == new Vector2((float)_camera.State.X, (float)_camera.State.Y)
                && _camera.Camera.Zoom == Vector2.One * (float)_camera.State.Zoom && _hud!.GlobalPosition == initialHudPosition
                && _camera.State.Zoom == expectedFit.Zoom && _canvas.CommandCount == commandCount && _cache.Count == textureCount && HasReadyHudContent(textureCount)
                && _canvas.GeometrySignature == geometrySignature && afterPan != _camera.State;
            if (!cycleValid)
            {
                ThrowNativeSmokeCycleFailure(cycle, $"post-resize fit={_camera.State.FitMode} camera={_camera.Camera.Position == new Vector2((float)_camera.State.X, (float)_camera.State.Y)} zoom={_camera.State.Zoom == expectedFit.Zoom} commands={_canvas.CommandCount == commandCount} textures={_cache.Count == textureCount} hud={HasReadyHudContent(textureCount)} geometry={_canvas.GeometrySignature == geometrySignature} panreset={afterPan != _camera.State}");
            }
        }
        var disposedBeforeReload = _cache.DisposedCount;
        GD.Print("ARC-RENDER-004 native-smoke stage=reload-input");
        var previousLoad = _lastLoad;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.L, Pressed = true });
        // ParseInputEvent queues _Input. Yield before reading _lastLoad so we
        // await the load started by the actual L route, rather than deadlocking
        // on this still-running native-smoke task from the initial load.
        await NextFrameAsync();
        if (ReferenceEquals(previousLoad, _lastLoad)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print("ARC-RENDER-004 native-smoke stage=reload-await");
        await (_lastLoad ?? Task.CompletedTask);
        await NextFrameAsync();
        GD.Print("ARC-RENDER-004 native-smoke stage=reload-complete");
        var expectedCommandCount = result.Plan.Commands.Count + (result.StaticPlan?.Commands.Count ?? 0) + (result.ActorPlan is null ? 0 : 1);
        var expectedTextureCount = result.Textures.Count + result.StaticTextures.Count + result.ActorTextures.Count;
        if (_cache.DisposedCount != disposedBeforeReload + textureCount || commandCount != expectedCommandCount || _canvas.CommandCount != commandCount
            || _cache.Count != textureCount || _canvas.GeometrySignature != geometrySignature || result.MaximumObservedConcurrency != 1
            || textureCount != expectedTextureCount || result.CheckedRgbaBytes <= 0 || result.ModuleFingerprint != result.ModuleFingerprintAfter || result.ResourceFingerprint != result.ResourceFingerprintAfter
            || (result.ActorPlan is not null && (result.ActorResourceFingerprint != result.ActorResourceFingerprintAfter || result.ActorPrototypeFingerprint != result.ActorPrototypeFingerprintAfter))
            || (result.StaticPlan is not null && (result.StaticCensusCount != 317 || result.StaticOutsideScopeCount != 312 || result.StaticPlan.Commands.Count != 5)))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print($"ARC-RENDER-004 native-smoke result status={(exactSecondSize ? "PASS" : "NOT_VERIFIED")} fullscreen={(fullscreen ? "PASS" : "NOT_VERIFIED")} commands={commandCount} textures={textureCount} bytes={result.CheckedRgbaBytes} loadMs={result.ElapsedMilliseconds} concurrency={result.MaximumObservedConcurrency} fingerprints=equal reload=PASS cycles=100");
        GetTree().Quit(0);
    }

    private async Task AssertRepresentativeProductionCanvasAsync(TerrainLoadResult result)
    {
        var expected = new[]
        {
            (392, 4587, 0, new TerrainRect(-79, 280, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-79, 280, 78, 40), false),
            (573, 4587, 0, new TerrainRect(-2119, 1380, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-2119, 1380, 78, 40), true),
            (821, 4577, 0, new TerrainRect(-1639, 1300, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-1639, 1300, 78, 40), true),
            (18, 4490, 0, new TerrainRect(-719, 360, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-719, 360, 78, 40), false),
            (2075, 90, 0, new TerrainRect(201, 1180, 80, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(201, 1180, 78, 40), false),
            (2011, 90, 1, new TerrainRect(161, 1160, 80, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(161, 1160, 78, 40), false),
            (1947, 90, 2, new TerrainRect(121, 1140, 80, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(121, 1140, 78, 40), false),
        };
        foreach (var literal in expected)
        {
            var command = result.Plan.Commands.SingleOrDefault(candidate => candidate.Ordinal == literal.Item1);
            if (command is null || command.TextureKey.DatOrdinal != literal.Item2 || command.TextureKey.FrameIndex != literal.Item3
                || command.Footprint != literal.Item4 || command.Source != literal.Item5 || command.Destination != literal.Item6 || command.FlipX != literal.Item7
                || !result.Textures.TryGetValue(command.TextureKey, out var decoded) || !TryFindOpaqueDecodedPixel(decoded, command.Source, out var sourcePixel))
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            var viewport = new SubViewport { Size = new Vector2I(command.Destination.Width + 2, command.Destination.Height + 2), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            var isolated = new TerrainCanvas { Position = new Vector2(1 - command.Destination.X, 1 - command.Destination.Y) };
            viewport.AddChild(isolated);
            AddChild(viewport);
            isolated.Publish(result.Plan.Isolate(command.Ordinal), _cache);
            await NextFrameAsync();
            using var image = viewport.GetTexture().GetImage();
            var offsetX = sourcePixel.X - command.Source.X;
            if (command.FlipX) offsetX = command.Source.Width - 1 - offsetX;
            var actual = image.GetPixel(1 + offsetX, 1 + sourcePixel.Y - command.Source.Y);
            viewport.QueueFree();
            if (Math.Round(actual.R * 255) != sourcePixel.R || Math.Round(actual.G * 255) != sourcePixel.G
                || Math.Round(actual.B * 255) != sourcePixel.B || Math.Round(actual.A * 255) != sourcePixel.A
                || Math.Round(image.GetPixel(0, 0).A * 255) != 0)
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            GD.Print($"ARC-RENDER-004 representative ordinal={literal.Item1} status=PASS");
        }
    }

    private async Task AssertSelectedStaticProductionCanvasAsync(TerrainLoadResult result)
    {
        var command = result.StaticPlan?.Commands.LastOrDefault();
        if (command is null || !result.StaticTextures.TryGetValue(command.TextureKey, out var decoded)
            || !TryFindOpaqueDecodedPixel(decoded, new TerrainRect(0, 0, decoded.Width, decoded.Height), out var sourcePixel))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticDecode));
        var viewport = new SubViewport { Size = new Vector2I(command.Destination.Width + 2, command.Destination.Height + 2), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var isolated = new TerrainCanvas { Position = new Vector2(1 - command.Destination.X, 1 - command.Destination.Y) };
        viewport.AddChild(isolated);
        AddChild(viewport);
        isolated.Publish(DiagnosticScenePlan.Create(result.Plan.Isolate(result.Plan.Commands[0].Ordinal), result.StaticPlan!.Isolate(command.SourceIndex)), _cache);
        await NextFrameAsync();
        using var image = viewport.GetTexture().GetImage();
        var offsetX = command.FlipX ? command.Destination.Width - 1 - sourcePixel.X : sourcePixel.X;
        var actual = image.GetPixel(1 + offsetX, 1 + sourcePixel.Y);
        viewport.QueueFree();
        if (Math.Round(actual.R * 255) != sourcePixel.R || Math.Round(actual.G * 255) != sourcePixel.G
            || Math.Round(actual.B * 255) != sourcePixel.B || Math.Round(actual.A * 255) != sourcePixel.A)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticDecode));
        GD.Print($"ARC-RENDER-004 static production-canvas source-index={command.SourceIndex} status=PASS");
    }

    /// <summary>
    /// Actor-mode native evidence goes through the ready app's real input
    /// routes, timeline and published production canvas.  It intentionally
    /// never changes caller-owned map/camera/instance anchor input.
    /// </summary>
    private async Task AssertActorProductionCanvasAsync(TerrainLoadResult result)
    {
        if (_readyActorInput is null || _readyActorPlan is null || _readyActor is null || _actorTimeline is null)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorPlan));
        var callerInput = _readyActorInput;
        var expectedFacings = new[] { (0, false), (7, true), (6, true), (5, true), (4, false), (5, false), (6, false), (7, false) };
        GD.Print("ARC-RENDER-005 actor native stage=idle-control");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key1, Pressed = true });
        await NextFrameAsync();
        if (_readyActor?.Action != ActorDiagnosticAction.Idle || !_hud!.Text.Contains("Actor: Idle", StringComparison.Ordinal))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        for (var turn = 0; turn < 8 && _readyActor?.RequestedFacing != 0; turn++)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Bracketright, Pressed = true });
            await NextFrameAsync();
        }
        if (_readyActor?.RequestedFacing != 0) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print("ARC-RENDER-005 actor native stage=facings-raster");
        for (var requested = 0; requested < 8; requested++)
        {
            var actor = _readyActor ?? throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorPlan));
            if (actor.RequestedFacing != requested || actor.PhysicalGroup != expectedFacings[requested].Item1 || actor.FlipX != expectedFacings[requested].Item2)
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            await AssertActorRasterAsync(result, actor);
            if (requested < 7)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Bracketright, Pressed = true });
                await NextFrameAsync();
            }
        }
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key2, Pressed = true });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-005 actor native stage=walk-raster");
        if (_readyActor?.Action != ActorDiagnosticAction.Walk || !_hud!.Text.Contains("Actor: Walk", StringComparison.Ordinal))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        for (var turn = 0; turn < 8 && _readyActor?.RequestedFacing != 0; turn++)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Bracketright, Pressed = true });
            await NextFrameAsync();
        }
        if (_readyActor?.RequestedFacing != 0) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        for (var requested = 0; requested < 8; requested++)
        {
            var actor = _readyActor ?? throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorPlan));
            if (actor.RequestedFacing != requested || actor.PhysicalGroup != expectedFacings[requested].Item1 || actor.FlipX != expectedFacings[requested].Item2
                || _readyActorInput.MapX != callerInput.MapX || _readyActorInput.MapY != callerInput.MapY
                || _readyActorInput.CameraOriginX != callerInput.CameraOriginX || _readyActorInput.CameraOriginY != callerInput.CameraOriginY
                || _readyActorInput.InstanceOffsetX != callerInput.InstanceOffsetX || _readyActorInput.InstanceOffsetY != callerInput.InstanceOffsetY)
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            await AssertActorRasterAsync(result, actor);
            if (requested < 7)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Bracketright, Pressed = true });
                await NextFrameAsync();
            }
        }
        GD.Print("ARC-RENDER-005 actor native stage=walk-timeline");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
        await NextFrameAsync();
        if (!_actorTimeline.IsPlaying || !_hud!.Text.Contains("Playback: playing", StringComparison.Ordinal))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
        await NextFrameAsync();
        if (_actorTimeline.IsPlaying || !_hud!.Text.Contains("Playback: paused", StringComparison.Ordinal))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        var pausedFrame = _readyActor?.Frame;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Period, Pressed = true });
        await NextFrameAsync();
        if (_readyActor?.Frame == pausedFrame || !_hud!.Text.Contains("Frame:", StringComparison.Ordinal))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Comma, Pressed = true });
        await NextFrameAsync();
        if (_readyActor?.Frame != pausedFrame || _readyActorInput.MapX != callerInput.MapX || _readyActorInput.MapY != callerInput.MapY
            || _readyActorInput.CameraOriginX != callerInput.CameraOriginX || _readyActorInput.CameraOriginY != callerInput.CameraOriginY
            || _readyActorInput.InstanceOffsetX != callerInput.InstanceOffsetX || _readyActorInput.InstanceOffsetY != callerInput.InstanceOffsetY)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print("ARC-RENDER-005 actor native controls=PASS actions=2 facings-per-action=8 rasters=16 mirror=PASS pause-step=PASS hud=PASS stationary-caller-input=PASS");
    }

    private async Task AssertActorRasterAsync(TerrainLoadResult result, ActorDiagnosticDrawCommand actor)
    {
        if (!result.ActorTextures.TryGetValue(actor.TextureKey, out var decoded)
            || !TryFindActorRasterPixel(decoded, actor, out var sourcePixel))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorDecode));
        var viewport = new SubViewport { Size = new Vector2I(actor.Destination.Width + 12, actor.Destination.Height + 12), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas { Position = new Vector2(6 - actor.Destination.X, 6 - actor.Destination.Y), ShowActorGuides = true };
        viewport.AddChild(canvas); AddChild(viewport);
        canvas.Publish(DiagnosticScenePlan.Create(result.Plan.Isolate(result.Plan.Commands[0].Ordinal), null, _readyActorPlan!), _cache);
        await NextFrameAsync();
        await NextFrameAsync();
        using var image = viewport.GetTexture().GetImage();
        var sourceX = actor.FlipX ? actor.Destination.Width - 1 - sourcePixel.X : sourcePixel.X;
        var rgba = image.GetPixel(6 + sourceX, 6 + sourcePixel.Y);
        var anchor = new Vector2I(6 + (int)(actor.AnchorX - actor.Destination.X), 6 + (int)(actor.AnchorY - actor.Destination.Y));
        viewport.QueueFree();
        var spriteMatches = Math.Abs(Math.Round(rgba.R * 255) - sourcePixel.R) <= 1 && Math.Abs(Math.Round(rgba.G * 255) - sourcePixel.G) <= 1
            && Math.Abs(Math.Round(rgba.B * 255) - sourcePixel.B) <= 1;
        var yellowGuide = HasGuideColor(image, anchor, true);
        var magentaOutline = HasGuideColor(image, new Vector2I(6, 6), false);
        if (!spriteMatches || !yellowGuide || !magentaOutline)
        {
            GD.Print($"ARC-RENDER-005 actor native raster requested={actor.RequestedFacing} sprite={spriteMatches} guide={yellowGuide} outline={magentaOutline}");
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        }
    }

    private static bool HasGuideColor(Image image, Vector2I center, bool yellow)
    {
        for (var y = center.Y - 5; y <= center.Y + 5; y++)
        {
            for (var x = center.X - 5; x <= center.X + 5; x++)
            {
                if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) continue;
                var pixel = image.GetPixel(x, y);
                if (yellow ? pixel.R > .9f && pixel.G > .9f && pixel.B < .2f : pixel.R > .9f && pixel.G < .2f && pixel.B > .9f)
                    return true;
            }
        }
        return false;
    }

    private static bool TryFindOpaqueDecodedPixel(DecodedTerrainTexture texture, TerrainRect source, out TerrainTexturePixel pixel)
        => TryFindOpaqueDecodedPixel(texture.Width, texture.Height, texture.Rgba, source, out pixel);

    private static bool TryFindOpaqueDecodedPixel(DecodedStaticTexture texture, TerrainRect source, out TerrainTexturePixel pixel)
        => TryFindOpaqueDecodedPixel(texture.Width, texture.Height, texture.Rgba, source, out pixel);

    /// <summary>
    /// Original-data native smoke derives one opaque actor texel only after
    /// excluding the diagnostic rectangle and anchor-cross pixels that paint
    /// later than the sprite. This preserves an actual sprite/reflection
    /// readback for each path instead of accidentally sampling a guide.
    /// </summary>
    private static bool TryFindActorRasterPixel(DecodedActorTexture texture, ActorDiagnosticDrawCommand actor, out TerrainTexturePixel pixel)
    {
        for (var y = 0; y < texture.Height; y++)
        {
            for (var x = 0; x < texture.Width; x++)
            {
                var offset = ((y * texture.Width) + x) * 4;
                if (texture.Rgba[offset + 3] == 0) continue;
                var destinationX = actor.FlipX ? actor.Destination.Width - 1 - x : x;
                var worldX = actor.Destination.X + destinationX;
                var worldY = actor.Destination.Y + y;
                var onOutline = worldX == actor.Destination.X || worldX == actor.Destination.X + actor.Destination.Width - 1
                    || worldY == actor.Destination.Y || worldY == actor.Destination.Y + actor.Destination.Height - 1;
                var onAnchorCross = (worldX == actor.AnchorX && Math.Abs(worldY - actor.AnchorY) <= 4)
                    || (worldY == actor.AnchorY && Math.Abs(worldX - actor.AnchorX) <= 4);
                if (onOutline || onAnchorCross) continue;
                pixel = new TerrainTexturePixel(x, y, texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2], texture.Rgba[offset + 3]);
                return true;
            }
        }
        pixel = default;
        return false;
    }

    private static bool TryFindOpaqueDecodedPixel(int width, int height, byte[] rgba, TerrainRect source, out TerrainTexturePixel pixel)
    {
        pixel = default;
        for (var y = source.Y; y < source.Y + source.Height; y++)
        {
            for (var x = source.X; x < source.X + source.Width; x++)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                var offset = ((y * width) + x) * 4;
                if (rgba[offset + 3] == 0) continue;
                pixel = new TerrainTexturePixel(x, y, rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                return true;
            }
        }
        return false;
    }

    private async Task NextFrameAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void ThrowNativeSmokeCycleFailure(int cycle, string branch)
    {
        // Keep the native-smoke diagnostic aggregate-only: it identifies the
        // control-flow branch and iteration without emitting input paths,
        // source content, or actor-specific language in terrain-only mode.
        GD.Print($"ARC-RENDER-005 native-smoke cycle-invalid cycle={cycle} branch={branch}");
        throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
    }

    private async Task SetSmokeWindowSizeAsync(Vector2I expected)
    {
        DisplayServer.WindowSetSize(expected);
        await NextFrameAsync();
    }

    private async Task<bool> CaptureAndValidateSmokeFrameAsync(Vector2I expectedSize, int commandCount, int textureCount)
    {
        await NextFrameAsync();
        using var screenshot = GetViewport().GetTexture().GetImage();
        var actualSize = new Vector2I(screenshot.GetWidth(), screenshot.GetHeight());
        if (_nativeSmokeScreenshot is not null)
        {
            var requested = Path.GetFullPath(_nativeSmokeScreenshot);
            var directory = Path.GetDirectoryName(requested);
            if (string.IsNullOrWhiteSpace(directory)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            Directory.CreateDirectory(directory);
            var stem = Path.GetFileNameWithoutExtension(requested);
            var extension = Path.GetExtension(requested);
            var output = Path.Combine(directory, $"{stem}-requested-{expectedSize.X}x{expectedSize.Y}-actual-{actualSize.X}x{actualSize.Y}{extension}");
            if (screenshot.SavePng(output) != Error.Ok) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        }
        if (actualSize != expectedSize)
        {
            GD.Print($"ARC-RENDER-004 native-smoke result status=NOT_VERIFIED requested={expectedSize.X}x{expectedSize.Y} actual={actualSize.X}x{actualSize.Y} reason=display-clamp");
            return false;
        }
        var stableCounts = _canvas!.CommandCount == commandCount && _cache.Count == textureCount;
        var hudReadable = !string.IsNullOrWhiteSpace(_hud?.Text) && _hud.GlobalPosition == new Vector2(16, 16) && HasReadyHudContent(textureCount);
        // At fitted zoom a particular edge texel can be downsampled away.
        // WORLD requires an opaque decoded texel surviving the actual window,
        // just as the static oracle does; never infer pixels from dimensions.
        var terrainLandmarkFound = _readyMode == TerrainViewerMode.WorldDiagnostic
            ? _canvas.TryGetOpaqueLandmark(candidate => TryAssertLandmark(screenshot, candidate), out var terrainLandmark)
            : _canvas.TryGetOpaqueLandmark(out terrainLandmark);
        var terrainLandmarkMatches = terrainLandmarkFound && TryAssertLandmark(screenshot, terrainLandmark);
        TerrainCanvasLandmark staticLandmark = default;
        // Camera Fit can downsample an arbitrary source texel away. Choose an
        // opaque pixel of the final static command that survives the actual
        // exact-size production screenshot, then assert its decoded RGBA.
        var requiresStatics = _readyMode is TerrainViewerMode.CombinedStaticDiagnostic or TerrainViewerMode.WorldDiagnostic;
        var staticLandmarkFound = !requiresStatics
            || _canvas.TryGetOpaqueStaticLandmark(candidate => TryAssertLandmark(screenshot, candidate), out staticLandmark);
        // The exact-size production frame itself must carry both semantic
        // landmarks. The pass-difference check below is only a supplementary
        // control, not a substitute for the selected static pixel oracle.
        var staticLandmarkMatches = !requiresStatics
            || (staticLandmarkFound && TryAssertLandmark(screenshot, staticLandmark));
        // Difference is an independent control only.  It is deliberately not
        // used as a proxy for the selected RGBA landmark assertion above.
        var staticPassDiffers = requiresStatics
            && staticLandmarkMatches && await HasStaticDifferenceInFullSceneAsync(screenshot, staticLandmark);
        GD.Print($"ARC-RENDER-004 native-smoke readback stable={stableCounts} hud={hudReadable} terrain={terrainLandmarkMatches} static={staticLandmarkMatches} static-landmark={staticLandmarkFound} static-index={staticLandmark.Ordinal} static-difference={staticPassDiffers}");
        if (!stableCounts || !hudReadable || !terrainLandmarkMatches || !staticLandmarkMatches
            || (requiresStatics && !staticLandmarkFound))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        return true;
    }

    private async Task<bool> HasStaticDifferenceInFullSceneAsync(Image combined, TerrainCanvasLandmark? selectedLandmark = null)
    {
        // Keep the exact-size production combined frame above as the terrain
        // preservation evidence, then render the same canvas/camera/viewport
        // without only its static pass. A differing pixel proves that an
        // admitted static object is present in that full production viewport,
        // without treating an isolated subviewport as the scene oracle.
        _canvas!.SetStaticVisibilityForReadback(false);
        try
        {
            await NextFrameAsync();
            using var terrainOnly = GetViewport().GetTexture().GetImage();
            if (terrainOnly.GetWidth() != combined.GetWidth() || terrainOnly.GetHeight() != combined.GetHeight()) return false;
            var point = selectedLandmark is TerrainCanvasLandmark landmark
                ? _canvas.GetGlobalTransformWithCanvas() * new Vector2(landmark.WorldX, landmark.WorldY)
                : Vector2.Zero;
            var left = selectedLandmark is null ? 0 : Math.Max(0, (int)Math.Round(point.X) - 2);
            var top = selectedLandmark is null ? 0 : Math.Max(0, (int)Math.Round(point.Y) - 2);
            var right = selectedLandmark is null ? combined.GetWidth() : Math.Min(combined.GetWidth(), (int)Math.Round(point.X) + 3);
            var bottom = selectedLandmark is null ? combined.GetHeight() : Math.Min(combined.GetHeight(), (int)Math.Round(point.Y) + 3);
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    if (combined.GetPixel(x, y) != terrainOnly.GetPixel(x, y)) return true;
                }
            }
            return false;
        }
        finally
        {
            _canvas.SetStaticVisibilityForReadback(true);
            await NextFrameAsync();
        }
    }

    private bool TryAssertLandmark(Image screenshot, TerrainCanvasLandmark landmark)
    {
        // Read TerrainCanvas's actual canvas transform rather than duplicating
        // Camera2D zoom/centering arithmetic in the smoke oracle.
        var point = _canvas!.GetGlobalTransformWithCanvas() * new Vector2(landmark.WorldX, landmark.WorldY);
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        for (var sampleY = y - 2; sampleY <= y + 2; sampleY++)
        {
            for (var sampleX = x - 2; sampleX <= x + 2; sampleX++)
            {
                if (sampleX < 0 || sampleY < 0 || sampleX >= screenshot.GetWidth() || sampleY >= screenshot.GetHeight()) continue;
                var actual = screenshot.GetPixel(sampleX, sampleY);
                // The full-window renderer can round sRGB upload/readback by
                // one channel step; require the same opaque decoded landmark
                // while allowing that documented transport quantization only.
                if (Math.Abs(Math.Round(actual.R * 255) - landmark.R) <= 1 && Math.Abs(Math.Round(actual.G * 255) - landmark.G) <= 1
                    && Math.Abs(Math.Round(actual.B * 255) - landmark.B) <= 1 && Math.Abs(Math.Round(actual.A * 255) - landmark.A) <= 1) return true;
            }
        }
        return false;
    }

    public override void _Process(double delta)
    {
        PaceFrames();
        SyncControllerPrompts();
        TickAudio();
        if (IsWorld)
        {
            // ARC-DEV-204 (B16): the change-of-place screen moves on the frame's real time, a map load's too; a hero change that waited
            // for the black screen runs now.
            TickMovies(delta * 1000);
            TickTransitionScreen(delta * 1000);
            try { TickAreaClock(); TickLightAnimation(); TickCarriedLight(); DrainTraitEquipment(); SyncHeroBody(); ProcessWorld(delta); SyncAreaSheet(); SyncAreaLogbook(); SyncAreaSchematics(); SyncAreaBarter(); SyncAreaItemService(); SyncAreaReading(); SyncAreaSleep(); SyncAreaFate(); SyncScriptFades(); SyncAreaWorldMap(); SyncAreaSaveList(); SyncMainMenu(); SyncAreaAwards(); SyncInterfaceHd(); TickWindowArtWarm(); SyncGameBar(); RefreshHeroGauges(); RefreshCombatHud(); TickFloaters(); }
            catch (Exception exception) when (exception is InvalidOperationException or GridReplayCapacityException or OverflowException)
            {
                GD.Print($"ARC-WORLD-003 runtime FAIL {exception.GetType().Name} (no state/arrival fabrication)");
                PublishFailure(_arguments?.Mode ?? _readyMode,new(TerrainViewerErrorCode.ActorPlan,Stage:TerrainViewerFailureStage.ActorPlan));
            }
            // ARC-UI-026: the hero's death, on the frame's real time.
            SyncHeroDeath(delta);
            SyncPassabilityOverlay(); SyncAreaInventory(); UpdateAreaHover(); SyncHoverRing(); ProcessGameCursorItem(); SyncCursorArt();
            // ARC-DEV-051: the session-event lines of the bug report's log.
            SyncBugReport();
            return;
        }
        var step = (float)(CameraPolicy.PanScreenPixelsPerSecond * delta);
        var before = _camera?.State;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) _camera?.Pan(new Vector2(0, -step));
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) _camera?.Pan(new Vector2(0, step));
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) _camera?.Pan(new Vector2(-step, 0));
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) _camera?.Pan(new Vector2(step, 0));
        if (before != _camera?.State) RefreshReadyStatus();
        if (_actorTimeline?.Advance(delta).Status == TimelineAdvanceStatus.FrameChanged) SelectActorFrame(_actorTimeline.CurrentFrame);
    }

    public override void _Input(InputEvent @event)
    {
        NotePacingActivity();
        ObserveControllerInput(@event);
        if (IsWorld) { InputWorld(@event); return; }
        var before = _camera?.State;
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) _camera?.Zoom(1.25);
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) _camera?.Zoom(0.8);
            else if (button.ButtonIndex == MouseButton.Left) _dragging = button.Pressed;
        }
        else if (@event is InputEventMouseMotion motion && _dragging) _camera?.Pan(-motion.Relative);
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode is Key.R or Key.F) _camera?.Reset(GetViewport().GetVisibleRect().Size);
            else if (key.Keycode == Key.Minus) _camera?.Zoom(0.8);
            else if (key.Keycode == Key.Plus) _camera?.Zoom(1.25);
            else if (key.Keycode == Key.L && _arguments is not null)
            {
                GD.Print("ARC-RENDER-004 input=L reload");
                _lastLoad = StartLoadAsync(_arguments, false);
            }
            else if (key.Keycode == Key.F11) GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
            else if (key.Keycode == Key.Escape && GetWindow().Mode == Window.ModeEnum.Fullscreen) GetWindow().Mode = Window.ModeEnum.Windowed;
            else if (_readyActorInput is not null && key.Keycode == Key.Key1) SelectActorAction(ActorDiagnosticAction.Idle);
            else if (_readyActorInput is not null && key.Keycode == Key.Key2) SelectActorAction(ActorDiagnosticAction.Walk);
            else if (_readyActorInput is not null && key.Keycode == Key.Bracketleft) SelectActorFacing(-1);
            else if (_readyActorInput is not null && key.Keycode == Key.Bracketright) SelectActorFacing(1);
            else if (_actorTimeline is not null && key.Keycode == Key.Space) { if (_actorTimeline.IsPlaying) _actorTimeline.Pause(); else _actorTimeline.Play(); RefreshReadyStatus(); }
            else if (_actorTimeline is not null && key.Keycode == Key.Comma) { _actorTimeline.Step(-1); SelectActorFrame(_actorTimeline.CurrentFrame); }
            else if (_actorTimeline is not null && key.Keycode == Key.Period) { _actorTimeline.Step(1); SelectActorFrame(_actorTimeline.CurrentFrame); }
        }
        if (before != _camera?.State) RefreshReadyStatus();
    }

    private void HandleViewportSizeChanged()
    {
        if (IsWorld && _readyActorPlan is not null) _camera?.ResizeWorld(WorldAnchor, WorldViewport);
        // A diagnostic resize reflows only the HUD.  Camera fitting is an
        // explicit user action (R/F); silently refitting here would alter the
        // renderer/camera observation while testing readability.
        RefreshReadyStatus();
    }

    private void RefreshReadyStatus()
    {
        if (_readySourceId is null || _camera is null) return;
        if (PaletteOpen) BindVisualPalette(); // a switch commits between frames (ARC-UI-006)
        if (_readyArea is not null) { RefreshAreaStatus(); return; }
        var mode = ModeName(_readyMode);
        var staticStatus = _readyMode is TerrainViewerMode.CombinedStaticDiagnostic or TerrainViewerMode.WorldDiagnostic
            ? $"static census {_readyStaticCensus}/admitted {_readyStaticAdmittedCount}/outside-scope {_readyStaticOutsideScope}"
            : "static not requested";
        var actorStatus = _readyActor is ActorDiagnosticDrawCommand actor
            ? $" | Actor anchor policy: caller-selected diagnostic | Actor render state: caller-supplied normal-uncached | scale 100 | shrunk false | Actor: {actor.Action} (source action {(actor.Action == ActorDiagnosticAction.Idle ? 0 : 1)}) | Facing: requested {actor.RequestedFacing} -> physical group {actor.PhysicalGroup} | mirror {(actor.FlipX ? "yes" : "no")} | Frame: {actor.Frame}/{actor.FrameCount} | Playback: {(_actorTimeline?.IsPlaying == true ? "playing" : "paused")} | raw ART FPS preview {actor.RawFramesPerSecond} (not source scheduler/gameplay) | World position: stationary | Layering: diagnostic terrain -> actor -> guides | Original placement parity: NOT VERIFIED | Controls: 1 Idle | 2 Walk | [ ] facing | Space play/pause | , . step"
            : string.Empty;
        ShowStatus($"Diagnostic mode {mode} | terrain 4096/3033/1063 | {staticStatus} | source-id {_readySourceId} | zoom {FormatZoom(_camera.State.Zoom)} | terrain decoded {_readyDecodedCount} | static decoded {_readyStaticDecodedCount} | cache {_cache.Count} | {_readyElapsedMilliseconds}ms{(_readyMode == TerrainViewerMode.WorldDiagnostic ? WorldStatus() : actorStatus)}");
    }

    private void ClearReadyMetrics()
    {
        // The conversation host borrows the area session, so it is unbound before the session is disposed.
        UnbindAreaConversations(); UnbindAreaHover();
        StopAreaAmbient(); StopAreaTransitions(); StopAreaWalls(); StopAreaRoofs(); StopSceneryAnimation();
        _readyArea = null; _readyMapName = null;
        _readySourceId = null;
        _readyDecodedCount = 0;
        _readyStaticDecodedCount = 0;
        _readyStaticAdmittedCount = 0;
        _readyStaticCensus = 0;
        _readyStaticOutsideScope = 0;
        _readyActor = null;
        _readyActorInput = null;
        _readyActorPlan = null;
        _readyDiagnosticPlan = null;
        _actorTimeline = null;
        _readyMode = TerrainViewerMode.TerrainOnly;
        _readyElapsedMilliseconds = 0;
    }

    private bool HasReadyHudContent(int textureCount)
        => _hud?.Text.Contains($"zoom {FormatZoom(_camera!.State.Zoom)}", StringComparison.Ordinal) == true
            && _hud.Text.Contains($"cache {textureCount}", StringComparison.Ordinal);

    private static string ModeName(TerrainViewerMode mode) => mode switch { TerrainViewerMode.Area => "area", TerrainViewerMode.WorldDiagnostic => "world-diagnostic", TerrainViewerMode.CombinedStaticDiagnostic => "combined", TerrainViewerMode.ActorDiagnostic => "actor-diagnostic", _ => "terrain-only" };
    private void SelectActorAction(ActorDiagnosticAction action)
    {
        if (_readyActorInput is null || _readyActorPlan is null) return;
        _readyActorInput = _readyActorInput with { Action = action, Frame = 0 };
        var resource = _readyActorPlan.Profile.Resource(action);
        _actorTimeline = new AnimationTimeline(resource.FramesPerGroup, resource.RawFramesPerSecond);
        PublishActorSelection();
    }
    private void SelectActorFacing(int delta)
    {
        if (_readyActorInput is null || _actorTimeline is null) return;
        _readyActorInput = _readyActorInput with { Facing = (_readyActorInput.Facing + delta + 8) % 8, Frame = _actorTimeline.CurrentFrame };
        PublishActorSelection();
    }
    private void SelectActorFrame(int frame)
    {
        if (_readyActorInput is null) return;
        _readyActorInput = _readyActorInput with { Frame = frame };
        PublishActorSelection();
    }
    private void PublishActorSelection()
    {
        if (_readyActorInput is null || _readyActorPlan is null || _readyDiagnosticPlan is null) return;
        _readyActorPlan = _readyActorPlan.Select(_readyActorInput);
        _readyDiagnosticPlan = DiagnosticScenePlan.Create(_readyDiagnosticPlan.Terrain, null, _readyActorPlan);
        _readyActor = _readyActorPlan.Current;
        _canvas?.Publish(_readyDiagnosticPlan, _cache);
        RefreshReadyStatus();
    }
    private static TerrainViewerProgress ProgressFromResult(TerrainLoadResult result)
        => result.Area is not null ? TerrainViewerProgress.Empty : new(result.ResolvedCount, result.TileCount, result.FacadeCount,
            result.StaticCensusCount, result.StaticPlan?.Commands.Count ?? 0, result.StaticOutsideScopeCount);
    private static string FormatFailure(TerrainViewerMode mode, TerrainViewerError error)
    {
        var progress = error.SafeProgress;
        return $"ARC-RENDER-004 native-smoke failure mode={ModeName(mode)} stage={error.Stage} code={error.Code} terrain={progress.TerrainResolved}/{progress.TerrainTiles}/{progress.TerrainFacades} static={progress.StaticCensus}/admitted {progress.StaticAdmitted}/outside-scope {progress.StaticOutsideScope}";
    }
    private void ShowFailureStatus(TerrainViewerMode mode, TerrainViewerError error)
    {
        var progress = error.SafeProgress;
        ShowFailure($"Diagnostic mode {ModeName(mode)} | failure stage={error.Stage} code={error.Code} | terrain {progress.TerrainResolved}/{progress.TerrainTiles}/{progress.TerrainFacades} | static census {progress.StaticCensus}/admitted {progress.StaticAdmitted}/outside-scope {progress.StaticOutsideScope}");
    }
    private static string FormatZoom(double zoom) => zoom.ToString("0.###", CultureInfo.InvariantCulture);
    private void ShowStatus(string text) => _hud?.SetDiagnosticText(text, _nativeSmoke || SelfTestBootstrap);
    // ARC-DEV-052: a failure stays on screen as a line of its own while the panel is hidden.
    // ARC-DEV-059: it is printed too, so `./arcanum run` sees a start that failed (the HUD alone is not in the log);
    // not by the self-test's bootstrap apps, whose failures are the expected outcomes of its stages.
    private void ShowFailure(string text)
    {
        if (!SelfTestBootstrap) GD.Print($"ARC-DEV-059 viewer failure {text}");
        _hud?.SetDiagnosticText(text, _nativeSmoke || SelfTestBootstrap, failure: true);
    }
    public override void _ExitTree()
    {
        if (_exiting) return;
        _exiting = true;
        CloseEvidence(); // ARC-GPAD-1376: an open evidence session ends with its last line
        _walkPlanCancellation?.Cancel(); // ARC-WORLD-042: the walk smoke's background route plan
        HdOptimizedStore.Stop(); // ARC-DEV-376: the background texture conversion must not be inside an engine call at shutdown
        _installCancel?.Cancel(); // ARC-DEV-074: closing the window stops a running innoextract (it would write on after the app)
        DropSectorChart(); // ARC-DEV-118: its task may be reading a sector, and its fallback port holds the game folder's stack open
        ExitWorld();
        GetViewport().SizeChanged -= HandleViewportSizeChanged;
        _generation = _coordinator.BeginGeneration();
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        cancellation?.Cancel();
        _coordinator.Dispose();
        cancellation?.Dispose();
        _canvas?.ReleaseHd(); _hdSource?.Dispose();
        _conversationHost = null; ReleaseConversationContent();
        DisposeAudio();
        // ARC-RENDER-033: the extra art's content stack.
        _extraArt?.Dispose(); _extraArt = null;
        // ARC-WORLD-081: the created items' prototypes.
        _prototypeItems?.Dispose(); _prototypeItems = null;
        // ARC-UI-032: the hotkey bar's item icons.
        _hotkeyItemIcons?.Dispose(); _hotkeyItemIcons = null;
        _inventoryItemIcons?.Dispose(); _inventoryItemIcons = null;
        _cache.Dispose();
        // ARC-DEV-032: the main app (not a self-test's child) removes the automated run's own folder after its last save.
        if (!SelfTestBootstrap) ViewerAutomatedStorage.Delete();
        if (!SelfTestBootstrap) BugReportLogger.Remove();
    }
}

/// <summary>Scalar-only harness observation; it deliberately contains no load result or decoded RGBA.</summary>
internal readonly record struct TerrainViewerSelfTestSnapshot(ActorDiagnosticDrawCommand? Actor,
    ActorDiagnosticPlacementInput? Input, bool Playing, int CacheCount, int CreatedTextures, int DisposedTextures, string Hud, Vector2 HudPosition,
    double CameraX, double CameraY, double CameraZoom, Transform2D CanvasTransform);
