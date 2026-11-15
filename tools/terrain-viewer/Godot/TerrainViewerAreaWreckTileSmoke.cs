using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-046 native observation (map mode on the main map at the Crash Site, the "wreck" stage alone): Virgil joins the party (as
// the follow smoke joins its NPC) and stands next to a plane wreck tile whose script 1695 waits for him, the PC four cells past the
// tile on the same line; following the PC, his first step goes onto the tile, its script runs with him as the triggerer and calls 1696,
// whose dialog opens his conversation with the PC at node 1 of DLG 1696 (no dialog script run); the smoke then closes it.
// It is the stage that lets a scripted conversation open in a native smoke; the other stages hold them (ARC-WORLD-118).
// ARC-SCRIPT-052: the "altar" stage (alone, `--sector 1452,1291`: a window with Virgil's home and the altar) does the same on the altar tiles of script 1328,
// whose call-ex (81) runs Virgil's own dialog point from line 14, so his talk opens in the DLG of his dialog script 1324.
public partial class TerrainViewerApp
{
    private const int WreckTileScript = 1695, WreckDialogScript = 1696, AltarTileScript = 1328, AltarDialogScript = 1324;

    private Task RunAreaWreckTileSmokeAsync() => RunAreaVirgilTileSmokeAsync("ARC-SCRIPT-046 wreck smoke PASS", "wreck", WreckTileScript, WreckDialogScript, atDialogPoint: false);

    private Task RunAreaAltarTileSmokeAsync() => RunAreaVirgilTileSmokeAsync("ARC-SCRIPT-052 altar smoke PASS", "altar", AltarTileScript, AltarDialogScript, atDialogPoint: true);

    // passLine: the stage's PASS marker (its FAIL line has FAIL in place of PASS); atDialogPoint: the talk runs at Virgil's dialog point
    // (a call-ex), which resumes his dialog slot, not a scripted conversation of a called script.
    private async Task RunAreaVirgilTileSmokeAsync(string passLine, string place, int tileScript, int dialogScript, bool atDialogPoint)
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print(passLine.Replace(" PASS", " FAIL", StringComparison.Ordinal) + ": " + why);
            throw new InvalidOperationException("AREA_" + place.ToUpperInvariant() + "_TILE_SMOKE_FAILED");
        }
        Require(_areaSession is not null && _conversationHost is { PartyWired: true } && _readyArea is not null && _worldActor.Session is AreaGridSession
            && _mapTransfers is not null, $"a map-mode session with a party (map {_readyMapName}: {_companionData?.Detail})");
        var session = _areaSession!; var grid = (AreaGridSession)_worldActor.Session!; var area = _readyArea!;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var tiles = _mapTransfers!.ScriptTiles;
        var wrecks = tiles.Where(c => _mapTransfers.TileScript(c)?.Script == tileScript).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        // ARC-DEV-089: the stage needs Virgil's home and the script's tile in the window (the Crash Site, or the altar's sector); another
        // window (the town, the wilderness) has neither, which is no failure of the game: the stage reports NONE there.
        var template = _companionTemplates.SingleOrDefault(t => t.Facts.ScriptId == AreaCompanionRoster.Virgil.ScriptId && t.Home.RuntimeMap == CurrentAreaMap);
        if (wrecks.Length == 0 || template is null)
        {
            GD.Print($"{passLine[..passLine.IndexOf(' ', StringComparison.Ordinal)]} native-smoke {place} NONE map={_readyMapName} ({(wrecks.Length == 0 ? $"no tile of script {tileScript} among {tiles.Count} script tiles" : $"Virgil is no companion template here, {_companionTemplates.Count} templates")})");
            return;
        }
        // A line through one of its tiles: Virgil one cell before it and the PC four cells past it, so he starts to follow (farther than
        // the start distance 4) and walks the line to within 2 of the PC, his first step onto the tile; every cell walkable, every step open.
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        bool Open(CellCoord from, NeighborDirection direction) => routes.Contains(from)
            && routes.Contains(Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(from, direction))
            && routes.Assess(from, direction) == Arcanum.Simulation.Navigation.RouteStepVerdict.Open;
        (Arcanum.Data.Traversal.AreaStepCell Tile, CellCoord From, CellCoord Pc)? line = null;
        var best = 0;
        foreach (var tile in wrecks)
        foreach (var direction in Enum.GetValues<NeighborDirection>())
        {
            var at = map.Chart.ToChart(tile);
            var from = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(at, (NeighborDirection)(((int)direction + 4) % 8));
            var cells = new List<CellCoord> { from };
            for (var step = 0; step < 5; step++) cells.Add(Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cells[^1], direction));
            if (line is null && cells.Take(5).All(c => Open(c, direction)))
                line = (tile, from, cells[^1]);
            best = Math.Max(best, cells.Take(5).TakeWhile(c => Open(c, direction)).Count());
        }
        Require(line is not null, $"a walkable line through a {place} tile ({wrecks.Length} tiles: {string.Join(" ", wrecks)}; longest open run {best} of 5)");
        var wreck = line!.Value.Tile;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var takesScripted = _smokeTakesScriptedConversations;
        _worldFocusSimulationForSelfTest = true; _smokeTakesScriptedConversations = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            BeginConversation(template!.Speaker);
            AreaSmokeFramesUntilConversation(); Require(ConversationOpen, "a conversation with Virgil opened: " + _conversationNotice);
            var joined = session.ForceJoinParty(session.Version, template.Entity);
            Require(joined.Disposition != GameSessionDisposition.Rejected, "Virgil joined: " + joined.Failure);
            for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "the conversation closed");
            AreaSmokePlace(map, line!.Value.Pc);
            var from = map.Chart.ToWorld(line.Value.From);
            var placed = session.ApplyChanges(session.Version, null,
                [AreaCompanionPlacement.Edit(template, new SessionMapId(CurrentAreaMap), new CellCoord(checked((int)from.X), checked((int)from.Y)), NeighborDirection.XPlus)]);
            Require(placed.Disposition != GameSessionDisposition.Rejected, "Virgil placed before the tile: " + placed.Failure);
            BindAreaCompanions();
            var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity);
            Require(companion is { Standing: CompanionStanding.Following } && companion.Grid.State.CommittedCell == line.Value.From,
                $"Virgil follows from {map.Chart.ToWorld(line.Value.From)}: {_areaCompanions?.Status()}");
            var frames = 0;
            for (; frames < 300 && !ConversationOpen; frames++) AreaSmokeFrame();
            Require(ConversationOpen && _conversationScripted != atDialogPoint, $"his conversation opened in {frames} frames: Virgil at {map.Chart.ToWorld(companion!.Grid.State.CommittedCell)}, notice {_conversationNotice}");
            Require(_voiceStart is { Status: AreaConversationStartStatus.Started } && _voiceStart.DialogScript == dialogScript && _conversationSpeaker?.Row.Key == template.Speaker.Row.Key,
                $"the DLG of script {dialogScript} with Virgil: {_voiceStart?.Status} {_voiceStart?.Detail}");
            var node = _conversationPanel!.Conversation?.CurrentNode.Value;
            RequireConversationArtFonts(Require);
            // ARC-SCRIPT-1358 (ledger VA1-VA5): at the altar Virgil's node 250 offers its `l:` reply as the world map reply: it is not blocked, and picking it ends the
            // talk for the world map (no script line resumed), which opens through the Map button's own function (refusals of the open apply, so only the request is required).
            var worldMapOpened = false; string? worldMapStatus = null;
            if (place == "altar")
            {
                var options = _conversationPanel.Conversation!.Options.ToList();
                var reply = options.FindIndex(o => o.Transition == Arcanum.Scripting.Dialog.DialogOptionTransition.WorldMap);
                Require(reply >= 0 && options[reply].Block is null && !_conversationPanel.ButtonsForSelfTest[reply].Disabled, $"the world map reply is offered at node {node} ({options.Count} replies)");
                _conversationPanel.Choose(options[reply].Number);
                for (var wait = 0; wait < 10 && ConversationOpen; wait++) AreaSmokeFrame();
                Require(!ConversationOpen, "the world map reply closed the talk");
                Require(_conversationNotice is null || !_conversationNotice.Contains("script", StringComparison.OrdinalIgnoreCase), "no script continues after it: " + _conversationNotice);
                // The world-map data may still load in the background: the request waits for it (up to 600 frames), so the smoke gives it real time.
                for (var wait = 0; wait < 400 && _dialogWorldMapPending is not null; wait++) { AreaSmokeFrame(); TakeDialogWorldMap(); if (_dialogWorldMapPending is not null) System.Threading.Thread.Sleep(50); }
                Require(_dialogWorldMapPending is null, "the queued world map request was taken");
                worldMapOpened = WorldMapOpen; worldMapStatus = _worldMapStatus;
                Require(worldMapOpened, $"the world map opened after the reply (status {worldMapStatus})");
                if (WorldMapOpen) CloseWorldMapByPlayer();
            }
            for (var close = 0; close < 8 && ConversationOpen; close++) _conversationPanel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "his conversation closed");
            GD.Print(passLine + " " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Tile = wreck, Virgil = from, Pc = map.Chart.ToWorld(line.Value.Pc), Frames = frames, Node = node,
                Script = dialogScript, WorldMapOpened = worldMapOpened, WorldMapStatus = worldMapStatus, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
        await Task.CompletedTask;
    }

    /// <summary>ARC-UI-071 (B29): the open conversation panel draws in the original's font art when the viewer read it, every shown text with its lines.</summary>
    private void RequireConversationArtFonts(Action<bool, string> Require)
    {
        var panel = _conversationPanel!;
        var fonts = TextFonts();
        Require(panel.ArtFontsForSelfTest == (fonts is not null), $"the conversation panel draws in the original's fonts exactly when they read (fonts {fonts is not null})");
        if (fonts is null) { GD.Print("ARC-UI-071 conversation panel in the viewer's font (no game fonts)"); return; }
        var texts = panel.ArtTextsForSelfTest();
        var shown = texts.Where(text => text.Visible).ToArray();
        // ARC-UI-2268: normal play draws only the reply rows (the band); the diagnostic header, hint and Leave draw only with ARC_DIALOG_DIAGNOSTICS=1.
        var chrome = shown.Where(text => text.Name is "header" or "outcome" or "effects" or "hint" or "leave").Select(text => text.Name).ToArray();
        Require(DialogDiagnostics ? shown.Length >= 3 : chrome.Length == 0 && shown.Length >= 1, $"the panel shows {(DialogDiagnostics ? "its header, hint and Leave" : "only reply rows")} in art text: {string.Join("; ", shown.Select(text => text.Name))}");
        Require(shown.All(text => text.Lines > 0 && text.Text.Length > 0), "every shown art text has lines: " + string.Join("; ", texts.Select(text => $"{text.Name}={text.Lines}")));
        Require(shown.All(text => text.Name != "npc"), "the NPC's line is not drawn in the panel (ARC-UI-2027: a bubble over the NPC)");
        GD.Print("ARC-UI-071 conversation panel art text " + string.Join(", ", shown.Select(text => $"{text.Name}:{text.Lines}@{text.Wrap}")));
        // ARC-UI-2083 (ledger DH15): the reply stack never holds more than the band's five slots (the source builder keeps five, DQR5; the trainer list refuses more, DTR3).
        Require(panel.ButtonsForSelfTest.Count <= DialogReplyBand.Slots, $"the reply stack holds at most {DialogReplyBand.Slots} replies ({panel.ButtonsForSelfTest.Count})");
        RequireReplyBandWidening(Require);
    }

    /// <summary>
    /// ARC-UI-2083 (ledger DH13): with the original's font art a reply whose drawn text is 388 px or wider (388 + the shadow pixel + the 12 px margins exceeds the box's 400)
    /// widens the band and re-centres it, the box does not shrink for a later short reply of the same set, and the next reply set starts at 400 again. The text is a single
    /// word searched on the installed font so that its width lands on the threshold (the original breaks a line at width 388, so a longer line cannot exist).
    /// </summary>
    private void RequireReplyBandWidening(Action<bool, string> Require)
    {
        var panel = _conversationPanel!;
        var wrap = DialogReplyBand.MinWidth - DialogReplyBand.Margins;
        string? wide = null; var drawn = 0;
        const string Letters = "abcdefghijklmnopqrstuvwxyz";
        for (var run = 1; run <= 120 && wide is null; run++)
            foreach (var body in Letters)
            {
                foreach (var last in Letters)
                {
                    var word = new string(body, run) + last;
                    var width = panel.FirstLineWidthForSelfTest(word, wrap);
                    if (DialogReplyBand.Widened(DialogReplyBand.MinWidth, width) > DialogReplyBand.MinWidth) { wide = word; drawn = width; break; }
                }
                if (wide is not null) break;
            }
        Require(wide is not null, $"a word that draws {wrap} px or wider at the wrap width {wrap} exists in the installed font");
        // The box left is an integer division: in a view of odd free width a box one pixel wider keeps the same left edge, so the re-centring cannot be told apart there.
        var view = GetViewport().GetVisibleRect().Size;
        var freeWidth = (int)AreaGameBarPanel.FreeArea(view).Size.X;
        var widened = DialogReplyBand.Widened(DialogReplyBand.MinWidth, drawn);
        var recentres = DialogReplyBand.BoxLeft(freeWidth, widened) != DialogReplyBand.BoxLeft(freeWidth, DialogReplyBand.MinWidth);
        if (!recentres) GD.Print($"ARC-UI-2083 re-centring not distinguishable in a free width of {freeWidth}: the left edge is checked for equality only");
        void Check((int X, int Y, int W, int H)? rect, int width, string what)
        {
            Require(rect is { } r && r.W == width && r.X == DialogReplyBand.BoxLeft(freeWidth, width) && r.H == DialogReplyBand.Height, $"{what}: the band is {width} wide, centred at {DialogReplyBand.BoxLeft(freeWidth, width)} ({rect})");
        }
        try
        {
            Check(panel.ShowReplySetForSelfTest(["Short", wide!]), widened, $"a {drawn} px reply after a short one");
            Require(widened > DialogReplyBand.MinWidth, $"the reply of {drawn} px widens the band past {DialogReplyBand.MinWidth} ({widened})");
            Check(panel.ShowReplySetForSelfTest([wide!, "Short", "Short"]), widened, "the short replies after the wide one do not shrink the band");
            Check(panel.ShowReplySetForSelfTest(["Short"]), DialogReplyBand.MinWidth, "the next reply set starts at 400 again");
            GD.Print($"ARC-UI-2083 reply band widening: {drawn} px reply -> {widened} wide, reset to {DialogReplyBand.MinWidth}");
        }
        finally { panel.RestoreRepliesForSelfTest(); }
    }
}
