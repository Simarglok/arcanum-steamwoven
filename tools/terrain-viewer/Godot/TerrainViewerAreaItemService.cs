using Arcanum.Data.Combat;
using Arcanum.Input;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1731: the item identify window (the dialog reply `ii`) and the item repair window (`ri`) of the session's hero in the area (exterior,
// cave and map mode; contract docs/world/item-service-runtime.md, ledger IS1–IS6). The reply ends the talk (the original's interface-mode change,
// ledger WN1, WN2) and the window opens with the speaker as the service's NPC once nothing else owns input, as the barter window does. The
// windows are the inventory window's modes 5 and 6 on the interface mode stack (InventoryKind5, InventoryKind6); the viewer shows a diagnostic
// list (PROJECT: the original look is a follow-up). Clicks run through the model (AreaItemService); Escape, a controller cancel or the button
// closes it, and the talk does not resume.
public partial class TerrainViewerApp
{
    private AreaItemServicePanel? _servicePanel;
    private AreaItemService? _itemService;
    private GameSessionOwner? _serviceSession;
    private InterfaceMode? _serviceMode;
    private long _serviceRevision = -1;
    private string? _serviceStatus;
    // The session and talk host whose ii or ri reply asked for the window, the window kind and the speaker (entity, index); a replaced session drops it.
    private (GameSessionOwner Session, AreaConversationHost Host, DialogWindowKind Window, string Entity, int Speaker)? _servicePending;
    private bool ItemServiceOpen => WindowOpen(InterfaceMode.InventoryKind5) || WindowOpen(InterfaceMode.InventoryKind6);
    private bool ItemServiceActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area;

    private static InterfaceMode ServiceMode(ItemServiceKind kind) => kind == ItemServiceKind.Identify ? InterfaceMode.InventoryKind5 : InterfaceMode.InventoryKind6;

    /// <summary>An `ii` or `ri` reply ended the conversation (<see cref="DialogEndKind.ItemIdentifyWindow"/>, <see cref="DialogEndKind.ItemRepairWindow"/>): the
    /// window opens with the speaker once nothing else owns input.</summary>
    private void QueueDialogItemService(AreaConversationHost host, (DialogWindowKind Window, string Entity, int Speaker) request)
    {
        GD.Print($"ARC-UI-1731 dialog item service window={request.Window} speaker={request.Entity}");
        _servicePending = (_areaSession!, host, request.Window, request.Entity, request.Speaker);
    }

    private void TakeDialogItemService()
    {
        if (_servicePending is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession) || !ItemServiceActive) { _servicePending = null; return; }
        if (ItemServiceOpen || WorldModal) return;
        AdoptBarterData();
        if (_barterData is null) { _serviceStatus = "item service data loading"; return; }
        _servicePending = null;
        OpenItemService(pending.Host, pending.Window == DialogWindowKind.ItemIdentify ? ItemServiceKind.Identify : ItemServiceKind.Repair, pending.Entity, pending.Speaker);
    }

    private void OpenItemService(AreaConversationHost host, ItemServiceKind kind, string entity, int speaker)
    {
        var (barter, refusal) = BarterModel(host, entity, speaker, substitute: false, "ARC-UI-1731 item service");
        if (barter is null) { _serviceStatus = refusal; RefreshAreaStatus(); return; }
        var session = _areaSession!;
        var row = host.Speakers[speaker].Row;
        // The NPC's Repair skill: its levelled sheet from the stored fields, as the NPC sheet reads it (ARC-MAGIC-1475).
        Func<(Arcanum.Session.SessionNpcAdvancement, Arcanum.Simulation.Character.AdvancementModifiers)?> npc = () =>
            _areaEffectRules is { } effects && CombatFieldReader.Read(row).Fields is { } fields && AreaAdvancement.Npc(session, entity, fields, effects) is { } loaded ? loaded : null;
        OpenItemServiceWindow(new AreaItemService(barter, kind, npc));
    }

    /// <summary>Shows the window over the area session for <paramref name="service"/> (the dialog path above, or a self-test's model); another window
    /// on the stack or a hero the mode refuses shows none.</summary>
    private void OpenItemServiceWindow(AreaItemService service)
    {
        var mode = ServiceMode(service.Kind);
        if (!WindowCanOpen(mode)) return;
        _servicePanel ??= CreateServicePanel();
        QuarantineWorld(); _quickKeys.Reset();
        var session = _areaSession!;
        _itemService = service; _serviceSession = session; _serviceMode = mode; _serviceStatus = null;
        var view = service.View();
        if (!OpenWindowMode(mode, () => _servicePanel!.Open(view), () => _servicePanel!.CloseView()))
        {
            _itemService = null; _serviceSession = null; _serviceMode = null;
            return;
        }
        _serviceRevision = session.Version.Revision;
        GD.Print($"ARC-UI-1731 item service open kind={service.Kind} npc={service.Merchant.Entity} items={view.Items.Count} gold={view.HeroGold}");
        RefreshReadyStatus();
    }

    private AreaItemServicePanel CreateServicePanel()
    {
        var panel = new AreaItemServicePanel(); AddChild(panel);
        panel.CloseRequested += EndItemService;
        panel.ItemClicked += ClickServiceItem;
        return panel;
    }

    private void ClickServiceItem(string entity)
    {
        if (!ItemServiceOpen || _itemService is null || !ItemServiceActive) return;
        var step = _itemService.Click(entity);
        GD.Print($"ARC-UI-1731 item service click kind={_itemService.Kind} item={entity} action={step.Action} quote={step.Quote?.Status.ToString() ?? "-"} price={step.Quote?.Price ?? 0} paid={step.Paid} text={step.Text}");
        SyncAreaItemService(force: true);
    }

    private void EndItemService()
    {
        if (_servicePanel is null || !ItemServiceOpen || _serviceMode is not { } mode) return;
        GD.Print("ARC-UI-1731 item service closed");
        CloseWindowMode(mode); _itemService = null; _serviceSession = null; _serviceMode = null; _serviceRevision = -1;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame: a pending dialog open; while open, a changed session rebuilds the window and a replaced or inactive one (a quick load, a map
    /// change) or a dead hero closes it.</summary>
    private void SyncAreaItemService(bool force = false)
    {
        TakeDialogItemService();
        if (!ItemServiceOpen) return;
        if (!ItemServiceActive || !ReferenceEquals(_serviceSession, _areaSession) || !AreaItemService.HeroCanUse(_serviceSession!)) { EndItemService(); return; }
        var revision = _serviceSession!.Version.Revision;
        if (!force && revision == _serviceRevision) return;
        _serviceRevision = revision;
        _servicePanel!.Render(_itemService!.View());
    }

    /// <summary>While the window is open it owns every key (ARC-DEV-111: Escape opens the menu, which closes the window; the window has no key of its own).</summary>
    private static void HandleItemServiceKey(InputEventKey key) { }

    private void HandleItemServicePulse(IntentFrame intent)
    {
        if (intent.Cancel) EndItemService();
        else if (intent.MenuPressedDirection != 0) _servicePanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _servicePanel!.Confirm();
    }

    private string ItemServiceStatusText() => !ItemServiceActive ? "" : ItemServiceOpen ? " | item service open"
        : _serviceStatus is null ? "" : " | item service: " + _serviceStatus;
}
