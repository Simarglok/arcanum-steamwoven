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
// only with a hero (--hero) and not for a dead hero. ARC-UI-009: the sheet opens the hero's character editor in the session
// (its floor, as the original's open takes its snapshot) and closes it on close; the skills page's + and − spend and refund
// through the session's point pool. Stats, tech disciplines and spells have no buttons yet.
public partial class TerrainViewerApp
{
    private AreaCharacterSheetPanel? _sheetPanel;
    private Task<AreaSkillTextsLoad>? _skillTextsTask;
    private string? _skillTextsModule;
    private AreaSkillTextsLoad? _skillTexts;
    private string? _sheetStatus;
    private (GameSessionOwner Session, long Revision)? _sheetStamp;
    private bool SheetOpen => _sheetPanel?.IsOpen == true;
    private bool SheetActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    private void OpenSheet()
    {
        if (!SheetActive || SheetOpen) return;
        AdoptSkillTexts();
        var build = AreaCharacterSheet.Build(_areaSession!);
        if (build.View is not { } view)
        {
            _sheetStatus = build.Refusal == AreaSheetRefusal.NoHero
                ? "no hero (start with --hero)" : "refused: the hero is dead";
            GD.Print($"ARC-UI-007 sheet refused={build.Refusal}");
            RefreshAreaStatus();
            return;
        }
        if (_sheetPanel is null)
        {
            _sheetPanel = new AreaCharacterSheetPanel(); AddChild(_sheetPanel);
            _sheetPanel.CloseRequested += EndSheet;
            _sheetPanel.SkillSpendRequested += row => PressSkill(row, spend: true);
            _sheetPanel.SkillRefundRequested += row => PressSkill(row, spend: false);
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        // The original's open takes the editor's snapshot (0x5597C0, CP1): the floor of every − until the sheet closes.
        var editor = _areaSession!.OpenHeroEditor();
        if (editor.Failure != GameSessionFailure.None) GD.Print($"ARC-UI-009 editor open refused={editor.Failure}");
        _sheetPanel.Open(view, BuildSkillsPage()); _sheetStamp = (_areaSession!, _areaSession!.Version.Revision); _sheetStatus = null;
        GD.Print($"ARC-UI-007 sheet open level={view.Level} unspent={view.UnspentPoints} next={view.ToNextLevel?.ToString() ?? "none"}");
        RefreshReadyStatus();
    }

    private void EndSheet()
    {
        if (_sheetPanel is null || !_sheetPanel.IsOpen) return;
        GD.Print("ARC-UI-007 sheet closed");
        // Closing drops the floor: nothing bought so far can be refunded any more (owner rule 2026-10-20).
        _areaSession?.CloseHeroEditor();
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
        if (AreaCharacterSheet.Build(_areaSession!).View is { } view)
        {
            // A replaced session (a quick load, a map change) has its own editor: the open sheet opens it there too.
            if (!_areaSession!.HeroEditorOpen) _areaSession.OpenHeroEditor();
            _sheetPanel!.Render(view, BuildSkillsPage());
        }
        else EndSheet();
    }

    /// <summary>Starts loading the skill texts for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaSkillTexts(TerrainViewerArguments arguments)
    {
        if (_skillTextsModule == arguments.ModulePath) return;
        _skillTextsModule = arguments.ModulePath; _skillTexts = null;
        var module = arguments.ModulePath;
        _skillTextsTask = Task.Run(() => AreaSkillsPage.LoadTexts(module));
    }

    private void AdoptSkillTexts()
    {
        if (_skillTexts is not null || _skillTextsTask is not { IsCompleted: true } task) return;
        _skillTexts = task.IsCompletedSuccessfully ? task.Result
            : new(AreaSkillTextsStatus.TextsFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _skillTextsTask = null;
        GD.Print($"ARC-UI-009 skill texts status={_skillTexts.Status} detail={_skillTexts.Detail}");
    }

    private AreaSkillsPageView? BuildSkillsPage() => AreaSkillsPage.Build(_areaSession!, _skillTexts?.Texts, AreaHoverIndex.TextProfile);

    /// <summary>A + or − press on the skills page: one call of the session's point pool, its message on the page; the session's
    /// new revision rebuilds the sheet on the next frame.</summary>
    private void PressSkill(int row, bool spend)
    {
        if (!SheetOpen || !SheetActive) return;
        var press = spend ? AreaSkillsPage.Spend(_areaSession!, row) : AreaSkillsPage.Refund(_areaSession!, row);
        _sheetPanel!.ShowSkillMessage(press.Message);
        GD.Print($"ARC-UI-009 skill {(spend ? "spend" : "refund")} row={row} changed={press.Changed} failure={press.Result.Failure} " +
            $"status={press.Result.Change?.Status} points={press.Result.Change?.Points}");
        SyncAreaSheet();
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
