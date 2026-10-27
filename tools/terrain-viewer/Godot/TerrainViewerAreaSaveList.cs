using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-010: the save and load windows of the save list in crash-site-area (exterior, cave and map mode, each mode with its own
// folder as the quick save). The palette opens them (the original opens them from its in-game menu, which is a later task); the
// window is modal like the logbook (gate closed, clock paused, a walk stopped) and owns the keys: Up/Down select, Return saves or
// loads, Delete or Backspace asks to delete, Escape answers no or closes, as the original's save window keys (U3). A save writes the
// game as it was when the window opened (the actor does not move while it is open) with the preview taken just before the window
// was drawn; a new save takes the next number, saving onto a listed save asks first and keeps its description, the auto-save
// cannot be deleted, a delete clears the selection (ledger SL3–SL6). A save or load that succeeds closes the window, as the
// original; a failed one keeps it open with the reason. The description is typed on the new-save row only.
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

    private async void PaletteOpenSaveList(AreaSaveListMode mode)
    {
        if (!QuickKeysActive) { _worldPanel?.SetActionStatus("Save list: no AREA game session"); return; }
        if (PaletteOpen) ToggleWorldSettings();
        // The palette is gone from the screen only once the next frame is drawn; the preview is taken then (a headless run draws no
        // frame and takes no preview, so it waits for the next process frame instead).
        if (DisplayServer.GetName() == "headless") await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        else await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!_exiting) OpenSaveList(mode);
    }

    private void OpenSaveList(AreaSaveListMode mode)
    {
        if (!QuickKeysActive || SaveListOpen || WorldModal) return;
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
            _saveListPanel.DescriptionChanged += text => { if (_saveList is not null) _saveList.Description = text; };
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("SAVE_LIST");
        _saveList = new AreaSaveList(mode, list.Entries);
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
                if (SaveGame(name, command.Description, _saveListThumbnail?.Png, "Save ") is not null) EndSaveList();
                else RefreshSaveList(command.Name, _quickSaveStatus);
                return;
            case AreaSaveCommandKind.Load:
                // A load replaces the session (or starts its map's load); the window then closes, a failed load keeps it.
                var slot = SaveLibrary.Slot(command.Name!);
                if (LoadGame(slot, "Load ")) { EndSaveList(); return; }
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

    private void EndSaveList()
    {
        if (_saveListPanel is null || !_saveListPanel.IsOpen) return;
        GD.Print("ARC-UI-010 save list closed");
        _saveListPanel.CloseView(); _saveList = null; _saveListThumbnail = null; _saveListPreview = null;
        QuarantineWorld(); RefreshReadyStatus();
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
}
