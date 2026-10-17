using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-007: the character sheet of the session's hero in crash-site-area (exterior, cave and map mode). The original
// Character sheet key (C, logical key down) opens it from the world, as the original's in-game key handler calls
// charedit_open for the local PC in active mode; C again, Escape or a controller cancel closes it. Like the inventory window
// it is modal (gate closed, clock paused, a walk stopped: the original interrupts the PC's animation when it opens). It opens
// only with a hero (--hero) and not for a dead hero. Read only: point spending, skills, tech disciplines and spells are not
// session state yet.
public partial class TerrainViewerApp
{
    private AreaCharacterSheetPanel? _sheetPanel;
    private string? _sheetStatus;
    private (GameSessionOwner Session, long Revision)? _sheetStamp;
    private bool SheetOpen => _sheetPanel?.IsOpen == true;
    private bool SheetActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    private void OpenSheet()
    {
        if (!SheetActive || SheetOpen) return;
        var build = AreaCharacterSheet.Build(_areaSession!);
        if (build.View is not { } view)
        {
            _sheetStatus = build.Refusal == AreaSheetRefusal.NoHero
                ? "character sheet: no hero (start with --hero)" : "character sheet refused: the hero is dead";
            GD.Print($"ARC-UI-007 sheet refused={build.Refusal}");
            RefreshAreaStatus();
            return;
        }
        if (_sheetPanel is null)
        {
            _sheetPanel = new AreaCharacterSheetPanel(); AddChild(_sheetPanel);
            _sheetPanel.CloseRequested += EndSheet;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        _sheetPanel.Open(view); _sheetStamp = (_areaSession!, _areaSession!.Version.Revision); _sheetStatus = null;
        GD.Print($"ARC-UI-007 sheet open level={view.Level} unspent={view.UnspentPoints} next={view.ToNextLevel?.ToString() ?? "none"}");
        RefreshReadyStatus();
    }

    private void EndSheet()
    {
        if (_sheetPanel is null || !_sheetPanel.IsOpen) return;
        GD.Print("ARC-UI-007 sheet closed");
        _sheetPanel.CloseView(); _sheetStamp = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame while open: a changed or replaced session rebuilds the sheet; a session that can no longer show it
    /// (gone, or its hero died) closes it.</summary>
    private void SyncAreaSheet()
    {
        if (!SheetOpen) return;
        if (!SheetActive) { EndSheet(); return; }
        var stamp = (_areaSession!, _areaSession!.Version.Revision);
        if (Equals(stamp, _sheetStamp)) return;
        _sheetStamp = stamp;
        if (AreaCharacterSheet.Build(_areaSession!).View is { } view) _sheetPanel!.Render(view);
        else EndSheet();
    }

    /// <summary>The original Character sheet key (C) acts on its logical key down while an area session exists. Returns true
    /// when the key belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteSheetKey(WorldKeyDecision decision)
    {
        if (!SheetActive || decision.Key != WorldKey.C) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _sheetStatus = "C ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenSheet();
        return true;
    }

    /// <summary>While the sheet is open it owns every key: C and Escape close it on a fresh press.</summary>
    private void HandleSheetKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (key.Pressed && !key.Echo && routed is Key.C or Key.Escape) EndSheet();
    }

    private void HandleSheetPulse(IntentFrame intent)
    {
        if (intent.Cancel) EndSheet();
        else if (intent.Confirm) _sheetPanel!.Confirm();
    }

    private string AreaSheetStatus() => !SheetActive ? "" : SheetOpen ? " | character sheet open: C or Escape closes"
        : " | C character sheet" + (_sheetStatus is null ? "" : ": " + _sheetStatus);
}
