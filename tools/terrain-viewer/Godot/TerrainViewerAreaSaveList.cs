using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-010: the save and load windows of the save list in crash-site-area (exterior, cave and map mode, each mode with its own
// folder as the quick save). The palette opens them, and the in-play menu's Save Game and Load Game (ARC-UI-012), as the original's; the
// window is modal like the logbook (gate closed, clock paused, a walk stopped) and owns the keys: Up/Down select, Return saves or
// loads, Delete or Backspace asks to delete, Escape answers no or closes, as the original's save window keys (U3). A save writes the
// game as it was when the window opened (the actor does not move while it is open) with the preview taken just before the window
// was drawn; a new save takes the next number, saving onto a listed save asks first and keeps its description, the auto-save
// cannot be deleted, a delete clears the selection (ledger SL3–SL6). A save or load that succeeds closes the window, as the
// original; a failed one keeps it open with the reason. The description is typed on the new-save row only. ARC-UI-062: with a game
// folder the window shows the original's look (AreaSaveListArtView; ledger MW11–MW15), the art read once a module; without one (the
// synthetic self-tests) it keeps the diagnostic window.
public partial class TerrainViewerApp
{
    private AreaSaveListPanel? _saveListPanel;
    private AreaSaveList? _saveList;
    // PNG bytes live in a holder only while the window is open (the ready app keeps no byte arrays: ARC-RENDER-003 self-test).
    private sealed record SavePicture(byte[] Png);
    private SavePicture? _saveListThumbnail;
    private (string Name, DateTime Saved, SavePicture? Picture)? _saveListPreview;
    private string? _saveListStatus;
    private bool SaveListOpen => _saveListPanel?.IsOpen == true;

    private void PaletteSaveGame() => PaletteOpenSaveList(AreaSaveListMode.Save);
    private void PaletteLoadGame() => PaletteOpenSaveList(AreaSaveListMode.Load);

    private void PaletteOpenSaveList(AreaSaveListMode mode)
    {
        if (!QuickKeysActive) { _worldPanel?.SetActionStatus("Save list: no AREA game session"); return; }
        if (PaletteOpen) ToggleWorldSettings();
        OpenSaveListAfterDraw(mode);
    }

    /// <summary>The palette (or the in-play menu, ARC-UI-012) is gone from the screen only once the next frame is drawn; the window
    /// opens and takes its preview then (a headless run draws no frame and takes no preview, so it waits for the next process frame).</summary>
    private async void OpenSaveListAfterDraw(AreaSaveListMode mode)
    {
        if (DisplayServer.GetName() == "headless") await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        else await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_exiting) return;
        OpenSaveList(mode);
        // A window that could not open gives the waiting menu back with the reason.
        if (!SaveListOpen && _mainMenu is not null && !MainMenuOpen && QuickKeysActive)
        {
            _mainMenu.Resume("the save list could not open: " + _saveListStatus);
            QuarantineWorld(); _quickKeys.Reset();
            RenderMainMenu(); RefreshReadyStatus();
        }
    }

    private void OpenSaveList(AreaSaveListMode mode)
    {
        // ARC-DEV-111: the menu Escape opens over a window that stays, so a window under the save list does not refuse it.
        if (!QuickKeysActive || SaveListOpen || PanelOutsideStackOpen) return;
        var list = SaveLibrary.List();
        if (list.Failure != GameSaveFailure.None)
        {
            _saveListStatus = $"the save folder could not be listed ({list.Failure} {list.Detail})";
            GD.Print($"ARC-UI-010 save list refused={list.Failure} detail={list.Detail}");
            RefreshAreaStatus();
            return;
        }
        _saveListThumbnail = mode == AreaSaveListMode.Save && CaptureSaveThumbnail() is { } png ? new(png) : null;
        if (_saveListPanel is null)
        {
            _saveListPanel = new AreaSaveListPanel(); AddChild(_saveListPanel);
            _saveListPanel.RowRequested += row => SaveListCommand(() => { _saveList!.Select(row); return AreaSaveCommand.Nothing; });
            _saveListPanel.RowActivated += row => SaveListCommand(() => _saveList!.Activate(row));
            _saveListPanel.ConfirmRequested += () => SaveListCommand(() => _saveList!.Confirm());
            _saveListPanel.DeleteRequested += () => SaveListCommand(() => _saveList!.RequestDelete());
            _saveListPanel.AnswerRequested += yes => SaveListCommand(() => _saveList!.Answer(yes));
            _saveListPanel.CloseRequested += () => EndSaveList();
            _saveListPanel.DescriptionChanged += text =>
            {
                if (_saveList is null) return;
                _saveList.Description = text;
                // The original's look draws the typed description in the new-save row.
                if (_saveListPanel.ArtMode) _saveListPanel.Render(_saveList, SelectedPreview());
            };
            _saveListPanel.SoundRequested += id => PlayBarSound(id, "save window button");
        }
        AttachSaveListArt(_saveListPanel);
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("SAVE_LIST");
        _saveList = new AreaSaveList(mode, list.Entries);
        // Same layer as the windows: the last child draws on top, so the list is above a window under the menu.
        if (_saveListPanel.GetParent() == this) MoveChild(_saveListPanel, GetChildCount() - 1);
        _saveListPanel.Open(_saveList, SelectedPreview());
        _saveListStatus = null;
        GD.Print($"ARC-UI-010 save list open mode={mode} saves={list.Entries.Count} truncated={list.Truncated} thumbnail={_saveListThumbnail?.Png.Length ?? 0}");
        RefreshReadyStatus();
    }

    /// <summary>The selected save's preview, read once per save and file date.</summary>
    private byte[]? SelectedPreview()
    {
        if (_saveList?.SelectedEntry is not { HasThumbnail: true } entry) return null;
        if (_saveListPreview is { } cached && cached.Name == entry.Name && cached.Saved == entry.SavedAtUtc) return cached.Picture?.Png;
        var png = SaveLibrary.ReadThumbnail(entry.Name);
        _saveListPreview = (entry.Name, entry.SavedAtUtc, png is null ? null : new(png));
        return png;
    }

    /// <summary>Runs one step of the window (a selection, a confirm, an answer) and then the command it returns.</summary>
    private void SaveListCommand(Func<AreaSaveCommand> step)
    {
        if (!SaveListOpen || _saveList is null) return;
        var command = step();
        switch (command.Kind)
        {
            case AreaSaveCommandKind.SaveNew:
            case AreaSaveCommandKind.Overwrite:
                var name = command.Kind == AreaSaveCommandKind.SaveNew ? null : command.Name;
                // As the original, a save that succeeded closes the window (SL5); a failure keeps it with the reason.
                if (SaveGame(name, command.Description, _saveListThumbnail?.Png, "Save ", AreaSaveMessageRoute.Window) is not null) EndSaveList(finished: true);
                else RefreshSaveList(command.Name, _quickSaveStatus);
                return;
            case AreaSaveCommandKind.Load:
                // A load replaces the session (or starts its map's load); the window then closes, a failed load keeps it.
                var slot = SaveLibrary.Slot(command.Name!);
                // From the main menu the placeholder has no hero tables; from play (ARC-UI-012) the running session's are kept, as F8's.
                if (LoadGame(slot, "Load ", _mainMenu is { Root: AreaMenuWindow.Main } ? MainMenuTables() : null, AreaLoadMessageRoute.Result)) { EndSaveList(finished: true); return; }
                RefreshSaveList(command.Name, _quickSaveStatus);
                return;
            case AreaSaveCommandKind.Delete:
                var deleted = SaveLibrary.Delete(command.Name!);
                GD.Print($"ARC-UI-010 save delete name={command.Name} failure={deleted.Failure} warning={deleted.Warning ?? "none"} detail={deleted.Detail ?? "none"}");
                RefreshSaveList(null, deleted.Deleted ? "deleted" + (deleted.Warning is null ? "" : $" (warning: {deleted.Warning})")
                    : $"delete FAILED ({deleted.Failure} {deleted.Detail})", clearSelection: deleted.Deleted);
                return;
        }
        _saveListPanel!.Render(_saveList, SelectedPreview());
    }

    private void RefreshSaveList(string? select, string? notice, bool clearSelection = false)
    {
        if (!SaveListOpen || _saveList is null) return;
        var list = SaveLibrary.List();
        _saveList.Refresh(list.Entries, select, list.Failure == GameSaveFailure.None ? notice : $"the save folder could not be listed ({list.Failure})", clearSelection);
        _saveListPanel!.Render(_saveList, SelectedPreview());
        RefreshAreaStatus();
    }

    /// <param name="finished">True when a load or a save closes it (ARC-UI-011, ARC-UI-012: the menu waiting under it then ends).</param>
    private void EndSaveList(bool finished = false)
    {
        if (_saveListPanel is null || !_saveListPanel.IsOpen) return;
        GD.Print("ARC-UI-010 save list closed");
        _saveListPanel.CloseView(); _saveList = null; _saveListThumbnail = null; _saveListPreview = null;
        QuarantineWorld(); RefreshReadyStatus();
        SaveListClosedUnderMainMenu(finished);
    }

    /// <summary>Per frame while open: a session that is gone (map mode left, a reset) closes the window.</summary>
    private void SyncAreaSaveList()
    {
        if (SaveListOpen && !QuickKeysActive) EndSaveList();
    }

    /// <summary>While the window is open it owns the keys; false lets a typed key reach the description field.</summary>
    private bool HandleSaveListKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        var typing = _saveListPanel!.TypingDescription;
        if (!key.Pressed) return !typing;
        switch (routed)
        {
            case Key.Escape:
                if (!key.Echo && !_saveList!.Cancel()) EndSaveList();
                else _saveListPanel.Render(_saveList!, SelectedPreview());
                return true;
            case Key.Up: SaveListCommand(() => { _saveList!.Move(-1); return AreaSaveCommand.Nothing; }); return true;
            case Key.Down: SaveListCommand(() => { _saveList!.Move(1); return AreaSaveCommand.Nothing; }); return true;
            case Key.Enter or Key.KpEnter:
                if (!key.Echo) SaveListCommand(() => _saveList!.Confirm());
                return true;
            case Key.Delete or Key.Backspace when !typing:
                if (!key.Echo) SaveListCommand(() => _saveList!.RequestDelete());
                return true;
            default:
                return !typing;
        }
    }

    private void HandleSaveListPulse(IntentFrame intent)
    {
        if (intent.Cancel) { if (!_saveList!.Cancel()) EndSaveList(); else _saveListPanel!.Render(_saveList, SelectedPreview()); }
        else if (intent.Confirm) SaveListCommand(() => _saveList!.Confirm());
        else if (intent.MenuPressedDirection != 0) SaveListCommand(() => { _saveList!.Move(intent.MenuPressedDirection); return AreaSaveCommand.Nothing; });
    }

    private string AreaSaveListStatus() => !QuickKeysActive ? "" : SaveListOpen ? " | save list open: Escape closes"
        : _saveListStatus is null ? "" : " | save list: " + _saveListStatus;

    private AreaSaveListArtContent? _saveListArtContent;
    private string? _saveListArtModule;

    /// <summary>ARC-UI-062: gives the window the current module's art, read on first use; nothing when it did not read.</summary>
    private void AttachSaveListArt(AreaSaveListPanel panel)
    {
        var module = _arguments?.ModulePath;
        if (_saveListArtContent is not null && module == _saveListArtModule) return;
        _saveListArtModule = module;
        var started = Time.GetTicksMsec();
        _saveListArtContent = SelfTestBootstrap || module is null ? AreaSaveListArtContent.Empty("self-test: no game folder") : AreaSaveListArtContent.Load(module);
        if (_saveListArtContent.Font is null)
        {
            panel.ClearArt();
            GD.Print($"ARC-UI-062 save window art not ready: {_saveListArtContent.Detail}");
            return;
        }
        var art = AreaSaveListArtLayout.ArtNumbers.Where(number => number != AreaSaveListArtLayout.FontArt)
            .ToDictionary(number => number, number => _saveListArtContent.Art[number].Frames.Select(frame => (Texture2D)HotkeyTexture(frame)).ToArray());
        var font = new AreaArtFontFace(_saveListArtContent.Art[AreaSaveListArtLayout.FontArt].Frames);
        panel.SetArt(art, font, _saveListArtContent.NewSaveLabel, _saveListArtContent.Messages,
            AreaSaveListArtLayout.DateOrder(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern), _saveListArtContent.StoryTexts);
        panel.PortraitSource = SavePortrait;
        GD.Print($"ARC-UI-062 save window art ready {_saveListArtContent.Detail}; glyphs={font.Frames.Count}; ms={Time.GetTicksMsec() - started}");
    }

    /// <summary>The smokes' art part of an open save or load window: the original's look with its rows at the exe's places (MW13),
    /// the selected row in view; the pointer over the confirm button lights it (frame 2, GB4). ARC_AREA_SAVE_CAPTURE saves an in-app
    /// readback of the window to the caller's ignored path (the mode's name is added before the extension).</summary>
    private async Task SaveListArtForSmoke(Action<bool, string> require)
    {
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        var panel = _saveListPanel!;
        var art = panel.ArtForSelfTest;
        var list = _saveList!;
        require(panel.ArtMode && _saveListArtContent is { ArtReady: true }, "the save window shows the game's art: " + _saveListArtContent?.Detail);
        var shown = art.ShownRowsForSelfTest;
        var font = _saveListArtContent!.Font!;
        var expected = list.Rows().Skip(art.TopForSelfTest).Take(AreaSaveListArtLayout.ShownRows)
            .Select(row => AreaSaveListArtLayout.Fit(font, row.Entry?.DisplayDescription ?? panel.NewSaveRow(list.Description), AreaSaveListArtLayout.Row(0).Width)).ToArray();
        require(shown.Count > 0 && shown.SequenceEqual(expected), $"the rows show the descriptions: {string.Join(" | ", shown)}");
        require(list.SelectedEntry is null || panel.ArtDetailForSelfTest == AreaSaveList.Detail(list.SelectedEntry), "the box shows the selected save's details: " + panel.ArtDetailForSelfTest);
        require(list.Selected < 0 || (list.Selected >= art.TopForSelfTest && list.Selected < art.TopForSelfTest + AreaSaveListArtLayout.ShownRows), "the selected row is shown");
        require(list.SelectedEntry is not null || art.InfoTextsForSelfTest.Count == 0, "ARC-UI-067: with no save selected the preview shows no info");
        await Frames(2);
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_SAVE_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            var path = System.IO.Path.ChangeExtension(capture, null) + "-" + list.Mode.ToString().ToLowerInvariant() + System.IO.Path.GetExtension(capture);
            require(image.SavePng(path) == Error.Ok, "the capture saved to " + path);
        }
        var (_, cx, cy) = AreaSaveListArtLayout.Button(AreaSaveListArtButton.Confirm);
        var over = art.ScreenPointForSelfTest(cx + 30, cy + 30);
        GetViewport().PushInput(new InputEventMouseMotion { Position = over, GlobalPosition = over });
        await Frames(1);
        require(art.FrameForSelfTest(AreaSaveListArtButton.Confirm) == 2 && art.FrameForSelfTest(AreaSaveListArtButton.Back) == 0,
            "the pointer over the confirm button lights it: " + art.FrameForSelfTest(AreaSaveListArtButton.Confirm));
        var away = art.ScreenPointForSelfTest(400, 20);
        GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
        await Frames(1);
        GD.Print("ARC-UI-062 native-smoke save window art PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Mode = list.Mode.ToString(), _saveListArtContent!.Detail, Rows = shown, Top = art.TopForSelfTest, list.Selected }));
    }

    /// <summary>ARC-UI-067: the smokes' info display part on a selected save with details: its texts at the exe's places (MW24) and
    /// its portrait; a click on the toggle hides them with the button down and the next shows them again (MW25).
    /// ARC_AREA_SAVE_CAPTURE adds "-info" before the extension.</summary>
    private async Task SaveInfoForSmoke(Action<bool, string> require, Func<Vector2, Task> click)
    {
        var art = _saveListPanel!.ArtForSelfTest;
        var content = _saveListArtContent!;
        var entry = _saveList!.SelectedEntry;
        var info = entry is null ? null : AreaSaveListArtLayout.Info(entry, content.StoryTexts);
        require(info is not null, $"the selected save has details: {entry?.Name} {entry?.Details}");
        var order = AreaSaveListArtLayout.DateOrder(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern);
        var expected = AreaSaveListArtLayout.InfoTexts(content.Font!, info!, content.Messages, order);
        string Shown() => string.Join(" | ", art.InfoTextsForSelfTest.Select(text => $"{text.Text}@{text.X},{text.Y}"));
        require(art.InfoShownForSelfTest && art.InfoTextsForSelfTest.SequenceEqual(expected), "the info texts show at the exe's places: " + Shown());
        require(info!.Portrait > 0 && art.PortraitDrawnForSelfTest, $"the save's portrait {info.Portrait} is drawn");
        for (var i = 0; i < 2; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_SAVE_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            var path = System.IO.Path.ChangeExtension(capture, null) + "-info" + System.IO.Path.GetExtension(capture);
            require(image.SavePng(path) == Error.Ok, "the capture saved to " + path);
        }
        var toggle = art.ScreenPointForSelfTest(AreaSaveListArtLayout.Toggle.X + 16, AreaSaveListArtLayout.Toggle.Y + 16);
        await click(toggle);
        require(!art.InfoShownForSelfTest && art.ToggleFrameForSelfTest == 1 && art.InfoTextsForSelfTest.Count == 0,
            $"a click on the toggle hid the info, the button down: frame {art.ToggleFrameForSelfTest} texts {Shown()}");
        await click(toggle);
        require(art.InfoShownForSelfTest && art.ToggleFrameForSelfTest == 0 && art.InfoTextsForSelfTest.SequenceEqual(expected),
            $"the next click showed it again, the button up: frame {art.ToggleFrameForSelfTest}");
        GD.Print("ARC-UI-067 native-smoke save info PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            entry!.Name, info.Portrait, Story = info.StoryText, Order = order.ToString(), Texts = expected.Select(text => $"{text.Text}@{text.X},{text.Y}") }));
    }
}
