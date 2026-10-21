using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-TECHNOLOGY-005: the schematic window of the session's hero in crash-site-area (exterior, cave and map mode). The original
// key T (logical key down) opens it for the local PC as crafter and viewer, under the in-game key handler's guards like C and
// L (ledger SW1); T again, Escape or a controller cancel closes it. Like the character sheet it is modal (gate closed, clock
// paused, a walk stopped). It opens only with a hero and not for a dead one. The tab, the discipline and the page are kept per
// tab across close and open, as the original keeps them (ledger SW4). The data come from the module's own files, loaded once
// per module in the background. Script action 129 (ledger SW8, TC2 both fixed) opens the same window for the PC once no other
// window or dialog owns input. ARC-TECHNOLOGY-006: the dialog's `su` (after the talk ended, ledger NC1–NC2) and action 129 for an
// NPC with a PC leader open it with the NPC as crafter: its degrees and own list, an empty found tab, the products into its list;
// each NPC open resets the tab, discipline and page, as the original does (NC5).
public partial class TerrainViewerApp
{
    private AreaSchematicsPanel? _schematicsPanel;
    private string? _schematicsStatus;
    private (GameSessionOwner Session, long Revision, AreaSchematicTab Tab, int Discipline, int Page, AreaSchematicData? Data, SessionNpcCrafter? Crafter)? _schematicsStamp;
    // ARC-TECHNOLOGY-006: the NPC crafter of the open window, null for the hero.
    private SessionNpcCrafter? _schematicCrafter;
    private AreaSchematicTab _schematicTab;
    private readonly (int Discipline, int Page)[] _schematicTabState = new (int, int)[2];
    private GameSessionOwner? _schematicSession;
    private Task<AreaSchematicDataLoad>? _schematicDataTask;
    private string? _schematicDataModule;
    private AreaSchematicDataLoad? _schematicData;
    private string? _schematicMessage;
    // SW8: the session whose script or dialog asked to open the window and the NPC crafter it named (null for the PC); a replaced
    // session (a quick load) drops the request.
    private (GameSessionOwner Session, SessionNpcCrafter? Crafter)? _schematicScriptOpen;
    // The window's sound requests, newest last (the self-test and the smoke read them; the audio may be absent).
    private readonly Queue<int> _schematicSounds = new();
    private bool SchematicsOpen => _schematicsPanel?.IsOpen == true;
    private bool SchematicsActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    /// <summary>Starts loading the schematic data for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaSchematics(TerrainViewerArguments arguments)
    {
        if (_schematicDataModule == arguments.ModulePath) return;
        _schematicDataModule = arguments.ModulePath; _schematicData = null;
        var module = arguments.ModulePath;
        _schematicDataTask = Task.Run(() => AreaSchematics.LoadData(module));
    }

    private void AdoptSchematicData()
    {
        if (_schematicData is not null || _schematicDataTask is not { IsCompleted: true } task) return;
        _schematicData = task.IsCompletedSuccessfully ? task.Result
            : new(AreaSchematicDataStatus.CatalogueFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _schematicDataTask = null; _schematicsStamp = null;
        GD.Print($"ARC-TECHNOLOGY-005 schematic data status={_schematicData.Status} detail={_schematicData.Detail}");
    }

    private (int Discipline, int Page) SchematicPosition => _schematicTabState[(int)_schematicTab];

    private AreaSchematicsBuild BuildSchematics() => AreaSchematics.Build(_areaSession!, _schematicData?.Data, _schematicTab,
        SchematicPosition.Discipline, SchematicPosition.Page, SchematicItems, AreaHoverIndex.TextProfile, _schematicCrafter);

    /// <summary>ARC-TECHNOLOGY-006: an NPC's crafter facts from its home map's data (sheet, scheme and effect stat changes), or null.</summary>
    private SessionNpcCrafter? AreaNpcCrafter(string entity) =>
        _combatHost is { } host && _areaEffectRules is { } effects && host.HomeMap(entity) is { } home && host.Fields(home, entity) is { } fields
            && AreaAdvancement.Npc(entity, fields, effects) is { } npc ? new SessionNpcCrafter(npc.Loaded, npc.Modifiers) : null;

    // The viewer keeps no inventory locations: every held item is in the bag; the session knows the worn and broken ones.
    private static readonly SessionCraftLookup SchematicItems = _ => new SessionCraftFacts(0);

    private void OpenSchematics(SessionNpcCrafter? crafter = null)
    {
        if (!SchematicsActive || SchematicsOpen) return;
        AdoptSchematicData();
        // NC5: an NPC crafter resets the tab, discipline and page on every open (the state is the window's, shared with the hero's).
        if (!ReferenceEquals(_schematicSession, _areaSession) || crafter is not null)
        {
            _schematicTab = AreaSchematicTab.Learned; _schematicTabState[0] = (0, 0); _schematicTabState[1] = (0, 0); _schematicSession = _areaSession;
        }
        _schematicCrafter = crafter;
        var build = BuildSchematics();
        if (build.View is not { } view)
        {
            _schematicCrafter = null;
            _schematicsStatus = build.Refusal switch
            {
                AreaSchematicRefusal.NoHero => "no hero (start with --hero)",
                AreaSchematicRefusal.NoCrafter => "refused: no data of the NPC crafter",
                _ => crafter is null ? "refused: the hero is dead" : "refused: the crafter or the hero is dead",
            };
            GD.Print($"ARC-TECHNOLOGY-005 schematics refused={build.Refusal}");
            RefreshAreaStatus();
            return;
        }
        if (_schematicsPanel is null)
        {
            _schematicsPanel = new AreaSchematicsPanel(); AddChild(_schematicsPanel);
            _schematicsPanel.CloseRequested += CloseSchematicsByPlayer;
            _schematicsPanel.TabRequested += SelectSchematicTab;
            _schematicsPanel.DisciplineRequested += SelectSchematicDiscipline;
            _schematicsPanel.PageRequested += TurnSchematicPage;
            _schematicsPanel.AssembleRequested += AssembleSchematic;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        _schematicMessage = null;
        _schematicsPanel.Open(view, description => AreaSchematics.Name(description, _hoverNames?.Catalog));
        _schematicsStamp = SchematicStamp(); _schematicsStatus = null;
        // SW3: the window's build plays 3008.
        SchematicSound(BookOpenSound, "schematics open");
        GD.Print($"ARC-TECHNOLOGY-005 schematics open crafter={view.Crafter ?? "hero"} tab={view.Tab} discipline={view.Discipline} page={view.Page}/{view.Pages} record={view.Record} button={view.Button} note={view.Note ?? "none"}");
        RefreshReadyStatus();
    }

    private void SchematicSound(int id, string what)
    {
        if (_schematicSounds.Count == 16) _schematicSounds.Dequeue();
        _schematicSounds.Enqueue(id);
        PlayInterfaceSound(id, what);
    }

    private (GameSessionOwner, long, AreaSchematicTab, int, int, AreaSchematicData?, SessionNpcCrafter?) SchematicStamp() =>
        (_areaSession!, _areaSession!.Version.Revision, _schematicTab, SchematicPosition.Discipline, SchematicPosition.Page, _schematicData?.Data, _schematicCrafter);

    /// <summary>SW4: a list tab press acts only from the other tab, keeps each tab's own discipline and page, and plays 3011.</summary>
    private void SelectSchematicTab(AreaSchematicTab tab)
    {
        if (!SchematicsOpen || tab == _schematicTab) return;
        _schematicTab = tab; _schematicMessage = null;
        SchematicSound(BookSwitchSound, "schematics tab");
        SyncAreaSchematics();
    }

    /// <summary>SW4: a discipline tab sets the discipline, goes to page 0 and plays 3010.</summary>
    private void SelectSchematicDiscipline(int discipline)
    {
        if (!SchematicsOpen || discipline is < 0 or >= AreaSchematics.Disciplines) return;
        _schematicTabState[(int)_schematicTab] = (discipline, 0); _schematicMessage = null;
        SchematicSound(BookPageSound, "schematics discipline");
        SyncAreaSchematics();
    }

    /// <summary>SW4: previous and next move one page and play 3010; the window disables them on the first and last page.</summary>
    private void TurnSchematicPage(int by)
    {
        if (!SchematicsOpen || _schematicsPanel!.View is not { } view) return;
        var page = view.Page + by;
        if (page < 0 || page >= view.Pages) return;
        _schematicTabState[(int)_schematicTab] = (SchematicPosition.Discipline, page); _schematicMessage = null;
        SchematicSound(BookPageSound, "schematics page");
        SyncAreaSchematics();
    }

    /// <summary>The assemble button: a refused state shows its text (schematic_text 0 or 1); otherwise the session assembles,
    /// and the completion shows text 2 and plays 3018 + the discipline (SW6). An NPC crafter's assembly takes the components from its
    /// list and puts the products into it (SW7); the viewer has no NPC weight or room model, so its insert check accepts.</summary>
    private void AssembleSchematic()
    {
        if (!SchematicsOpen || _schematicsPanel!.View is not { Placeholder: false } view || _schematicData?.Data is not { } data) return;
        if (view.Button != AssemblyButton.CanAssemble) { _schematicMessage = view.ButtonText; Render(); return; }
        if (data.Catalog.Read(view.Record).Record is not { } record) return;
        var result = _schematicCrafter is { } crafter
            ? _areaSession!.AssembleSchematicForNpc(_areaSession.Version, crafter, record, SchematicItems, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck)
            : _areaSession!.AssembleSchematic(_areaSession.Version, record, SchematicItems, data.Facts, data.Kind,
                _inventoryHost?.InsertCheck ?? AreaInventoryHost.DiagnosticInsertCheck);
        if (result.Disposition == GameSessionDisposition.Changed)
        {
            _schematicMessage = AreaSchematics.CompletedText(data, AreaHoverIndex.TextProfile);
            SchematicSound(AreaSchematics.CompletedSound(view.Discipline), "schematics assembled");
            GD.Print($"ARC-TECHNOLOGY-005 assembled crafter={view.Crafter ?? "hero"} record={view.Record} made={string.Join(",", result.Made)} consumed={string.Join(",", result.Consumed)}");
        }
        else
        {
            _schematicMessage = "not assembled: " + AreaSchematics.Refused(result, _schematicCrafter is not null);
            GD.Print($"ARC-TECHNOLOGY-005 assembly refused={result.Failure}");
        }
        SyncAreaSchematics(force: true);
    }

    private void Render()
    {
        if (BuildSchematics().View is { } view) _schematicsPanel!.Render(view, _schematicMessage);
    }

    // The player closed it (T, Escape, Cancel, the button): the close sound plays (SW3); a teardown closes it silently.
    private void CloseSchematicsByPlayer() => EndSchematics(byPlayer: true);

    private void EndSchematics() => EndSchematics(byPlayer: false);

    private void EndSchematics(bool byPlayer)
    {
        if (_schematicsPanel is null || !_schematicsPanel.IsOpen) return;
        GD.Print("ARC-TECHNOLOGY-005 schematics closed");
        if (byPlayer) SchematicSound(BookCloseSound, "schematics close");
        _schematicsPanel.CloseView(); _schematicsStamp = null; _schematicCrafter = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame while open: a changed or replaced session, another tab, discipline or page, or newly loaded data rebuild
    /// the window; a session that can no longer show it (gone, or its hero died) closes it.</summary>
    private void SyncAreaSchematics(bool force = false)
    {
        TakeScriptSchematics();
        if (!SchematicsOpen) { AdoptSchematicData(); return; }
        if (!SchematicsActive) { EndSchematics(); return; }
        AdoptSchematicData();
        var stamp = SchematicStamp();
        if (!force && Equals(stamp, _schematicsStamp)) return;
        _schematicsStamp = stamp;
        if (BuildSchematics().View is { } view) _schematicsPanel!.Render(view, _schematicMessage);
        else EndSchematics();
    }

    /// <summary>
    /// SW8: the start schematic UI requests of the dialog scripts the viewer ran (<see cref="AreaScriptHost.ResolveSchematic"/>), and
    /// (ARC-TECHNOLOGY-006) a conversation's `su`. The window opens once no dialog, window or palette owns input (a dialog's script
    /// runs while its dialog is open; `su` ends its dialog first): the PC's own, or an NPC crafter's from its home map's data.
    /// </summary>
    private void TakeScriptSchematics()
    {
        if (_readyMode != TerrainViewerMode.CrashSiteArea) { _schematicScriptOpen = null; return; }
        foreach (var scripts in TalkHosts().Select(h => h.Scripts).ToArray()) QueueScriptSchematics(scripts.TakeSchematics());
        if (_schematicScriptOpen is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession)) { _schematicScriptOpen = null; return; }
        if (SchematicsOpen || WorldModal) return;
        _schematicScriptOpen = null;
        OpenSchematics(pending.Crafter);
    }

    /// <summary>ARC-TECHNOLOGY-006: `su` ended the conversation (<see cref="DialogEndKind.SchematicWindow"/>): the window opens with that NPC
    /// as crafter once nothing else owns input.</summary>
    private void QueueDialogSchematics(string npc)
    {
        if (AreaNpcCrafter(npc) is not { } crafter) { _schematicsStatus = "su: no data of the NPC crafter"; RefreshAreaStatus(); return; }
        GD.Print($"ARC-TECHNOLOGY-006 dialog schematics crafter={npc}");
        _schematicScriptOpen = (_areaSession!, crafter);
    }

    private void QueueScriptSchematics(IEnumerable<AreaSchematicScriptOpen> requests)
    {
        foreach (var request in requests)
        {
            GD.Print($"ARC-TECHNOLOGY-005 script schematics crafter={request.Crafter.Code} viewer={request.Viewer.Code} status={request.Status}");
            if (request.Status != AreaSchematicScriptStatus.Open || _areaSession is null) continue;
            if (request.Crafter == request.Viewer) _schematicScriptOpen = (_areaSession, null);
            else if (AreaNpcCrafter(request.Crafter.Code) is { } crafter) _schematicScriptOpen = (_areaSession, crafter);
            else { _schematicsStatus = "script 129: no data of the NPC crafter"; RefreshAreaStatus(); }
        }
    }

    /// <summary>The original Technical schematics key (T) acts on its logical key down while an area session exists. Returns true
    /// when the key belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteSchematicsKey(WorldKeyDecision decision)
    {
        if (!SchematicsActive || decision.Key != WorldKey.T) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _schematicsStatus = "T ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenSchematics();
        return true;
    }

    /// <summary>While the window is open it owns every key: T and Escape close it, Tab assembles, on a fresh press.</summary>
    private void HandleSchematicsKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed is Key.T or Key.Escape) CloseSchematicsByPlayer();
        else if (routed == Key.Tab) _schematicsPanel!.Confirm();
    }

    private void HandleSchematicsPulse(IntentFrame intent)
    {
        if (intent.Cancel) CloseSchematicsByPlayer();
        else if (intent.Confirm) _schematicsPanel!.Confirm();
    }

    private string AreaSchematicsStatus() => !SchematicsActive ? "" : SchematicsOpen ? " | schematics open: T or Escape closes"
        : " | T schematics" + (_schematicsStatus is null ? "" : ": " + _schematicsStatus);
}
