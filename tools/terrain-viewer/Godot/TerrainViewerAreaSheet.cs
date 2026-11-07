using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-007: the character sheet of the session's hero in crash-site-area (exterior, cave and map mode). The original
// Character sheet key (C, logical key down) opens it from the world, as the original's in-game key handler calls
// charedit_open for the local PC in active mode; C again, Escape or a controller cancel closes it. Like the inventory window
// it is modal (gate closed, clock paused, a walk stopped: the original interrupts the PC's animation when it opens). It opens
// only with a hero (--hero) and not for a dead hero. ARC-UI-009: the sheet opens the hero's character editor in the session
// (its floor, as the original's open takes its snapshot) and closes it on close; the skills page's + and − spend and refund
// through the session's point pool. ARC-MAGIC-008: the spells page's college tabs, + and − learn and unlearn spell ranks through
// the same pool (the spell book's lane); the shown college is kept between opens, as the original keeps it. ARC-UI-034: the stats
// page's + and − (hit points, fatigue, the eight primary stats) through the same pool (ledger CP3, CP4). Tech disciplines have no
// buttons yet.
public partial class TerrainViewerApp
{
    private AreaCharacterSheetPanel? _sheetPanel;
    private Task<AreaSkillTextsLoad>? _skillTextsTask;
    private string? _skillTextsModule;
    private AreaSkillTextsLoad? _skillTexts;
    private Task<AreaSpellDataLoad>? _spellDataTask;
    private string? _spellDataModule;
    private AreaSpellDataLoad? _spellData;
    private int _sheetCollege;
    private string? _sheetStatus;
    private (GameSessionOwner Session, long Revision)? _sheetStamp;
    private bool SheetOpen => WindowOpen(InterfaceMode.Character);
    private bool SheetActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area;

    private void OpenSheet()
    {
        if (!SheetActive || !WindowCanOpen(InterfaceMode.Character)) return;
        AdoptSkillTexts(); AdoptSpellData();
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
            _sheetPanel.CollegeSelected += SelectCollege;
            _sheetPanel.SpellLearnRequested += college => PressSpell(college, learn: true);
            _sheetPanel.SpellUnlearnRequested += college => PressSpell(college, learn: false);
            _sheetPanel.StatSpendRequested += row => PressStat(row, spend: true);
            _sheetPanel.StatRefundRequested += row => PressStat(row, spend: false);
            _sheetPanel.HoverTextRequested += SheetHover;
            _sheetPanel.HoverEntryRequested += SheetHoverEntry;
            _sheetPanel.SpellEntry = SheetSpellEntry;
            _sheetPanel.TechSpendRequested += discipline => PressTech(discipline, spend: true);
            _sheetPanel.TechRefundRequested += discipline => PressTech(discipline, spend: false);
            _sheetPanel.MessageRequested += text => PostMessage(AreaMessageEntry.Text(AreaMessageKind.Message, text), "sheet");
            _sheetPanel.SoundRequested += id => PlayBarSound(id, "sheet button");
        }
        AttachSheetArt(_sheetPanel);
        _sheetPanel.SetPortrait(SheetPortrait());
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        // The original's open takes the editor's snapshot (0x5597C0, CP1): the floor of every − until the sheet closes.
        var editor = _areaSession!.OpenHeroEditor();
        if (editor.Failure != GameSessionFailure.None) GD.Print($"ARC-UI-009 editor open refused={editor.Failure}");
        var (skills, spells, stats, tech) = (BuildSkillsPage(), BuildSpellsPage(), AreaStatsPage.Build(_areaSession!), AreaTechPage.Build(_areaSession!));
        if (!OpenWindowMode(InterfaceMode.Character, () => _sheetPanel!.Open(view, skills, spells, stats, tech), () => _sheetPanel!.CloseView()))
        {
            _areaSession.CloseHeroEditor();
            return;
        }
        _sheetStamp = (_areaSession!, _areaSession!.Version.Revision); _sheetStatus = null;
        GD.Print($"ARC-UI-007 sheet open level={view.Level} unspent={view.UnspentPoints} next={view.ToNextLevel?.ToString() ?? "none"}");
        RefreshReadyStatus();
    }

    private void EndSheet()
    {
        if (_sheetPanel is null || !SheetOpen) return;
        GD.Print("ARC-UI-007 sheet closed");
        SheetHover(null);
        // Closing drops the floor: nothing bought so far can be refunded any more (owner rule 2026-10-20).
        _areaSession?.CloseHeroEditor();
        CloseWindowMode(InterfaceMode.Character); _sheetStamp = null;
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
            // Defensive: the open sheet owns every key, so no quick load or map change replaces the session under it; should a
            // session's editor be closed anyway, the open sheet opens it again.
            if (!_areaSession!.HeroEditorOpen) _areaSession.OpenHeroEditor();
            _sheetPanel!.Render(view, BuildSkillsPage(), BuildSpellsPage(), AreaStatsPage.Build(_areaSession!), AreaTechPage.Build(_areaSession!));
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

    /// <summary>Starts loading the spell data for this module once, as the skill texts.</summary>
    private void BindAreaSpellData(TerrainViewerArguments arguments)
    {
        if (_spellDataModule == arguments.ModulePath) return;
        _spellDataModule = arguments.ModulePath; _spellData = null; _spellRecords = null;
        var module = arguments.ModulePath;
        _spellDataTask = Task.Run(() => AreaSpellsPage.LoadData(module));
    }

    private void AdoptSpellData()
    {
        if (_spellData is not null || _spellDataTask is not { IsCompleted: true } task) return;
        _spellData = task.IsCompletedSuccessfully ? task.Result
            : new(AreaSpellDataStatus.DataFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _spellDataTask = null;
        GD.Print($"ARC-MAGIC-008 spell data status={_spellData.Status} detail={_spellData.Detail} {_spellData.EyeCandyDetail}");
    }

    private AreaSpellsPageView? BuildSpellsPage() => AreaSpellsPage.Build(_areaSession!, _spellData?.Data, _sheetCollege);

    /// <summary>A college tab press shows that college's spells.</summary>
    private void SelectCollege(int college)
    {
        if (!SheetOpen || !SheetActive || college is < 0 or >= AreaSpellsPage.Colleges) return;
        AdoptSpellData();
        _sheetCollege = college;
        if (AreaCharacterSheet.Build(_areaSession!).View is { } view) _sheetPanel!.Render(view, BuildSkillsPage(), BuildSpellsPage(), AreaStatsPage.Build(_areaSession!), AreaTechPage.Build(_areaSession!));
    }

    /// <summary>A + or − press on the spells page: one call of the session's point pool, its message, and the sheet rebuilt.</summary>
    private void PressSpell(int college, bool learn)
    {
        if (!SheetOpen || !SheetActive) return;
        AdoptSpellData();
        var data = _spellData?.Data;
        var press = learn ? AreaSpellsPage.Learn(_areaSession!, college, data) : AreaSpellsPage.Unlearn(_areaSession!, college, data);
        _sheetPanel!.ShowSkillMessage(press.Message, SheetPressText(AreaSheetPage.Spell, learn, press.Result, press.Message));
        GD.Print($"ARC-MAGIC-008 spell {(learn ? "learn" : "unlearn")} college={college} changed={press.Changed} failure={press.Result.Failure} " +
            $"status={press.Result.Change?.Status}");
        SyncAreaSheet();
    }

    /// <summary>A + or − press on the skills page: one call of the session's point pool, its message on the page, and the sheet
    /// rebuilt at once.</summary>
    private void PressSkill(int row, bool spend)
    {
        if (!SheetOpen || !SheetActive) return;
        var press = spend ? AreaSkillsPage.Spend(_areaSession!, row) : AreaSkillsPage.Refund(_areaSession!, row);
        _sheetPanel!.ShowSkillMessage(press.Message, SheetPressText(AreaSheetPage.Skill, spend, press.Result, press.Message));
        GD.Print($"ARC-UI-009 skill {(spend ? "spend" : "refund")} row={row} changed={press.Changed} failure={press.Result.Failure} " +
            $"status={press.Result.Change?.Status} points={press.Result.Change?.Points}");
        SyncAreaSheet();
    }

    /// <summary>ARC-UI-034: a + or − press on the stats page: one call of the session's point pool, its message, and the sheet rebuilt.</summary>
    private void PressStat(int row, bool spend)
    {
        if (!SheetOpen || !SheetActive) return;
        var press = spend ? AreaStatsPage.Spend(_areaSession!, row) : AreaStatsPage.Refund(_areaSession!, row);
        _sheetPanel!.ShowSkillMessage(press.Message, SheetPressText(AreaSheetPage.Stat, spend, press.Result, press.Message, row >= 2 ? AreaStatsPage.StatOrder[row - 2] : null));
        GD.Print($"ARC-UI-034 stat {(spend ? "spend" : "refund")} row={row} changed={press.Changed} failure={press.Result.Failure} " +
            $"status={press.Result.Change?.Status} points={press.Result.Change?.Points}");
        SyncAreaSheet();
    }

    /// <summary>ARC-UI-076: a + or − press on the tech page: one call of the session's point pool (the technology lane), its message, and the sheet rebuilt.</summary>
    private void PressTech(int discipline, bool spend)
    {
        if (!SheetOpen || !SheetActive) return;
        var press = spend ? AreaTechPage.Spend(_areaSession!, discipline) : AreaTechPage.Refund(_areaSession!, discipline);
        _sheetPanel!.ShowSkillMessage(press.Message, SheetPressText(AreaSheetPage.Tech, spend, press.Result, press.Message));
        GD.Print($"ARC-UI-076 tech {(spend ? "spend" : "refund")} discipline={discipline} changed={press.Changed} failure={press.Result.Failure} " +
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
        if (key.Pressed && !key.Echo && routed == Key.C && !CharacterKeySilenced(key, routed)) EndSheet();
    }

    private void HandleSheetPulse(IntentFrame intent)
    {
        if (intent.Cancel) EndSheet();
        else if (intent.Confirm) _sheetPanel!.Confirm();
    }

    private string AreaSheetStatus() => !SheetActive ? "" : SheetOpen ? " | character sheet open: C closes"
        : " | C character sheet" + (_sheetStatus is null ? "" : ": " + _sheetStatus);
}
