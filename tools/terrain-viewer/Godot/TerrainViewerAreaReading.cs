using Arcanum.Data.Written;
using Arcanum.Input;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-049: the reading window in crash-site-area (exterior, cave and map mode). A written item of the hero's inventory is used
// from the inventory window's Read button (the original's item use 0x462CC0 → the reading start 0x56BB60): the session decides
// (GameSessionOwner.UseWrittenItem): a schematic document registers (tech.mes 89 or 88 in the inventory message, the document
// destroyed when new), any other kind gives its document, and the window opens in place of the inventory (the original pops the
// current interface mode and pushes the reading mode). Script actions 64 (book), 65 (picture) and 114 (plaque) of the dialog, use,
// tile and sector scripts the viewer runs open the window for a PC viewer once no dialog, window or palette owns input (PROJECT:
// the original changes the mode at once; a later request replaces a waiting one). The window is modal like the
// schematic window (gate closed, clock paused); Escape, a controller cancel or the close button close it (PROJECT keys: the
// original's window takes no keys and closes by its frame hotspot or any mode change). Sounds: open 3008, a book's page 3010, close
// 3009 (ledger WR rows). The texts load once per module in the background.
public partial class TerrainViewerApp
{
    private AreaReadingPanel? _readingPanel;
    private string? _readingStatus;
    private Task<AreaReadingDataLoad>? _readingDataTask;
    private string? _readingDataModule;
    private AreaReadingDataLoad? _readingData;
    // The open document, its title, the shown spread and the session it opened in (a replaced session closes the window).
    private (WrittenDocument Document, string Title, int Spread, GameSessionOwner Session)? _reading;
    // A script's reading request waiting for input to be free, and the session that asked; a replaced session drops it.
    private (GameSessionOwner Session, ScrReadingRequest Request)? _readingScriptOpen;
    // The window's sound requests, newest last (the self-test and the smoke read them; the audio may be absent).
    private readonly Queue<int> _readingSounds = new();
    private bool ReadingOpen => _readingPanel?.IsOpen == true;
    private bool ReadingActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    /// <summary>Starts loading the written texts for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaReading(TerrainViewerArguments arguments)
    {
        if (_readingDataModule == arguments.ModulePath) return;
        _readingDataModule = arguments.ModulePath; _readingData = null;
        var module = arguments.ModulePath;
        _readingDataTask = Task.Run(() => AreaReading.LoadData(module));
    }

    private void AdoptReadingData()
    {
        if (_readingData is not null || _readingDataTask is not { IsCompleted: true } task) return;
        _readingData = task.IsCompletedSuccessfully ? task.Result
            : new(AreaReadingDataStatus.StackFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _readingDataTask = null;
        GD.Print($"ARC-UI-049 reading data status={_readingData.Status} detail={_readingData.Detail}");
    }

    /// <summary>The inventory's Read button: the session uses the written item; a document opens the window in place of the inventory.</summary>
    private void ReadItem(string item)
    {
        if (!InventoryOpen || !InventoryActive) return;
        AdoptReadingData();
        var holder = _inventoryPanel!.Holder;
        string message;
        if (_readingData?.Data is not { } data) message = "not read: the written texts are " + (_readingData is null ? "still loading" : $"unavailable ({_readingData.Status})");
        else if (_inventoryHost!.WrittenFacts(_areaSession!, item, data) is not { } facts) message = "not read: not a written item the hero holds";
        else if (facts.UseScript && ItemUseScript(item) is { } stopped) message = stopped;
        else
        {
            var result = _areaSession!.UseWrittenItem(_areaSession.Version, item, facts, data.Texts, useScriptRan: facts.UseScript);
            GD.Print($"ARC-UI-049 read disposition={result.Disposition} failure={result.Failure} subtype={facts.Subtype} start={facts.TextStartLine} prototype={facts.Prototype} " +
                $"reading={result.Reading?.ToString() ?? "-"} schematic={result.Schematic?.ToString() ?? "-"} destroyed={result.DocumentDestroyed} document={result.Document?.Status.ToString() ?? "-"}");
            if (result.Disposition == GameSessionDisposition.Rejected) message = "not read: " + AreaReading.UseRefused(result.Failure);
            else if (result.Document is not { } document)
            {
                AdoptSchematicData();
                message = AreaReading.SchematicMessage(result.Reading, result.Schematic, _schematicData?.Data, AreaHoverIndex.TextProfile);
            }
            else if (!document.Readable) message = "not read: " + AreaReading.Refused(document.Status);
            else
            {
                // The reading mode replaces the inventory's (the original pops the current mode).
                EndInventory();
                OpenReading(document, _inventoryHost.Name(item, _hoverNames?.Catalog));
                return;
            }
        }
        _readingStatus = message; _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-SCRIPT-020: the written item's use script runs before the reading (item use 0x462CC0, ledger IU rows), with the hero as the
    /// user; null when it let the use go on (run default), otherwise why the use stopped. Its effects (flags, story, quests, areas, rumors,
    /// reputations, its own slot) are the session's either way the run ended; a completed quest's award levels the followers.
    /// </summary>
    private string? ItemUseScript(string item)
    {
        if (_readyArea?.Snapshot is not { } snapshot || UseScriptHost(snapshot) is not { } host) return "not read: its use script needs a game session";
        if (_areaSession!.QueryPcInventory().FirstOrDefault(held => held.Entity == item) is not { Entity: not null } held)
            return "not read: its use script runs only for an item taken from a map";
        var ran = Awarded(host, host.RunItemUse(held.Origin.RuntimeMap, item, _areaSession.Rules.Scripts));
        var lines = ran.Lines.Count > 0 ? " — \"" + string.Join(" / ", ran.Lines) + "\"" : "";
        GD.Print($"ARC-SCRIPT-020 item use script outcome={ran.Outcome} run={ran.Run?.Outcome.ToString() ?? "-"} detail={ran.Detail}");
        if (ran.Outcome is not (AreaScriptedUseOutcome.NoTeleport)) return "not read: its use script was not run (" + ran.Detail + ")";
        return ran.Run?.Outcome == ScrOutcome.SkipDefault ? "its use script stopped the reading" + lines : null;
    }

    private void OpenReading(WrittenDocument document, string title)
    {
        if (!ReadingActive || _readingData?.Data is not { } data) return;
        // The original's open toggles: a second open closes the window (WR2). The viewer's callers never reach it today: the inventory
        // closes first and a script request waits while any window is open.
        if (ReadingOpen) { CloseReadingByPlayer(); return; }
        if (AreaReading.View(document, data.Texts, 0, AreaHoverIndex.TextProfile, _areaSession!.Rules.Written) is not { } view)
        {
            _readingStatus = "not read: " + AreaReading.Refused(document.Status);
            RefreshAreaStatus();
            return;
        }
        if (_readingPanel is null)
        {
            _readingPanel = new AreaReadingPanel(); AddChild(_readingPanel);
            _readingPanel.CloseRequested += CloseReadingByPlayer;
            _readingPanel.PageRequested += TurnReadingPage;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        _reading = (document, title, 0, _areaSession!);
        _readingPanel.Open(view, title);
        _readingStatus = null;
        ReadingSound(BookOpenSound, "reading open");
        GD.Print($"ARC-UI-049 reading open title={title} subtype={view.Subtype} start={document.StartLine} art={view.BackgroundArt} spreads={view.Spreads} " +
            $"header={view.Header.Count} body={document.Body.Count} note={view.Note ?? "none"}");
        RefreshReadyStatus();
    }

    private void ReadingSound(int id, string what)
    {
        if (_readingSounds.Count == 16) _readingSounds.Dequeue();
        _readingSounds.Enqueue(id);
        PlayInterfaceSound(id, what);
    }

    /// <summary>A book's previous and next turn two pages and play 3010; nothing past the first or last spread.</summary>
    private void TurnReadingPage(int by)
    {
        if (!ReadingOpen || _reading is not { } reading || _readingPanel!.View is not { } view || _readingData?.Data is not { } data) return;
        if (by < 0 ? !view.HasPrevious : !view.HasNext) return;
        _reading = reading with { Spread = view.Spread + Math.Sign(by) };
        if (AreaReading.View(reading.Document, data.Texts, _reading.Value.Spread, AreaHoverIndex.TextProfile, reading.Session.Rules.Written) is { } turned) _readingPanel.Render(turned, reading.Title);
        ReadingSound(BookPageSound, "reading page");
    }

    // The player closed it (Escape, Cancel, the button): the close sound plays; a teardown closes it silently.
    private void CloseReadingByPlayer() => EndReading(byPlayer: true);

    private void EndReading() => EndReading(byPlayer: false);

    private void EndReading(bool byPlayer)
    {
        if (_readingPanel is null || !_readingPanel.IsOpen) return;
        GD.Print("ARC-UI-049 reading closed");
        if (byPlayer) ReadingSound(BookCloseSound, "reading close");
        _readingPanel.CloseView(); _reading = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame: takes the scripts' reading requests, opens a waiting one once input is free, and closes the window when the
    /// area session is gone.</summary>
    private void SyncAreaReading()
    {
        AdoptReadingData();
        TakeScriptReadings();
        if (ReadingOpen && (!ReadingActive || !ReferenceEquals(_reading?.Session, _areaSession))) EndReading();
    }

    /// <summary>The reading actions (64, 65, 114) of the dialog scripts and the use, tile and sector scripts the viewer ran, for a PC viewer.</summary>
    private void TakeScriptReadings()
    {
        if (_readyMode != TerrainViewerMode.CrashSiteArea) { _readingScriptOpen = null; return; }
        foreach (var scripts in TalkHosts().Select(h => h.Scripts).ToArray()) QueueScriptReadings(scripts.TakeReadings());
        if (_useScriptHost is { } use) QueueScriptReadings(use.Host.TakeReadings());
        if (_readingScriptOpen is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession)) { _readingScriptOpen = null; return; }
        if (WorldModal || _readingData is null) return;
        _readingScriptOpen = null;
        if (_readingData.Data is not { } data) { _readingStatus = $"script reading: the written texts are unavailable ({_readingData.Status})"; RefreshAreaStatus(); return; }
        var document = _areaSession!.QueryWrittenDocument(AreaReading.ScriptFacts(pending.Request), data.Texts);
        OpenReading(document, $"script {pending.Request.Kind} {pending.Request.Number}");
    }

    private void QueueScriptReadings(IEnumerable<ScrReadingRequest> requests)
    {
        foreach (var request in requests)
        {
            GD.Print($"ARC-UI-049 script reading kind={request.Kind} number={request.Number} viewer={request.Viewer}");
            if (_areaSession is not null) _readingScriptOpen = (_areaSession, request);
        }
    }

    /// <summary>While the window is open it owns every key: Escape closes it; Left/Right and Page Up/Down turn a book's pages.</summary>
    private void HandleReadingKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed == Key.Escape) CloseReadingByPlayer();
        else if (routed is Key.Left or Key.Pageup) TurnReadingPage(-1);
        else if (routed is Key.Right or Key.Pagedown) TurnReadingPage(1);
    }

    private void HandleReadingPulse(IntentFrame intent)
    {
        if (intent.Cancel) CloseReadingByPlayer();
        else if (intent.MenuPressedDirection != 0) _readingPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _readingPanel!.Confirm();
    }

    private string AreaReadingStatus() => !ReadingActive ? "" : ReadingOpen ? " | reading: Escape closes"
        : _readingStatus is null ? "" : " | reading: " + _readingStatus;
}
