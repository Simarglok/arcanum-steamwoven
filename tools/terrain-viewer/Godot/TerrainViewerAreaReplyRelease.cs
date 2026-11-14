using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-2071 (ledger DH8, DH10, SW12): the lone left release after a dialog reply. In the original no window captures a press: a reply is chosen on the press, the dialog ends, the
// window the reply opens exists when the release of that same click is dispatched, and the window's mouse branch takes it with no press state (the identify and repair windows quote
// the item under the pointer, the round view's square closes the inventory-style windows, the NPC sheet and the schematic window close on a release in their lens or close
// rectangle). A Godot control gets a release only from its own press, so the app keeps the release of a press that began over the open dialog and hands it to the window the reply
// opened, in both settings (the original's behaviour, not an exe bug: no switch). Not wired: the barter window (its shop opens with the main menu up, IW34) and the world map (it acts on the press only, DH10).
public partial class TerrainViewerApp
{
    // A left press began while the dialog was open: its release is the reply click's (the press that picked the reply; a press elsewhere on the dialog whose dialog then ends by itself is taken for it too).
    private bool _replyPressDown;
    // The release's screen point and the frames it has waited for the window the reply opens (the viewer opens it on a later frame than the press).
    private (Vector2 At, int Frames)? _replyRelease;
    private const int ReplyReleaseWaitFrames = 12;
    /// <summary>Where the last owed release went (self-test): "item-service", "sheet", "schematics", "dropped" or null.</summary>
    internal string? ReplyReleaseTargetForSelfTest { get; private set; }

    /// <summary>Called for every input event of the world: remembers a left press that began while the dialog was open and, at its release, hands the release to the window the reply
    /// opened. True when a window took the release.</summary>
    private bool TrackReplyRelease(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button) return false;
        if (button.Pressed) { _replyPressDown = ConversationOpen; _replyRelease = null; return false; }
        var owed = _replyPressDown;
        _replyPressDown = false;
        // A dialog that is still open took the release as its own (a reply that went on).
        if (!owed || ConversationOpen) return false;
        _replyRelease = (button.Position, 0);
        return DeliverReplyRelease();
    }

    /// <summary>Per frame: an owed release waits a few frames for the window the reply opens, then is dropped.</summary>
    private void SyncReplyRelease()
    {
        if (_replyRelease is not { } owed) return;
        if (DeliverReplyRelease()) return;
        if (owed.Frames + 1 >= ReplyReleaseWaitFrames) { _replyRelease = null; ReplyReleaseTargetForSelfTest = "dropped"; return; }
        _replyRelease = (owed.At, owed.Frames + 1);
    }

    private bool DeliverReplyRelease()
    {
        if (_replyRelease is not { } owed) return false;
        string? target = null;
        bool took = false;
        if (ItemServiceOpen && _servicePanel is { } service) { took = service.LoneRelease(owed.At); target = "item-service"; }
        else if (SheetOpen && _sheetNpc is not null && _sheetPanel is { } sheet) { took = sheet.LoneRelease(owed.At); target = "sheet"; }
        else if (SchematicsOpen && _schematicsPanel is { } schematics) { took = schematics.LoneRelease(owed.At); target = "schematics"; }
        if (target is null) return false;
        // A window open in its diagnostic look takes no such release (it has no lens and no frame): the release is spent, and goes on to the world as any other.
        _replyRelease = null; ReplyReleaseTargetForSelfTest = target;
        return took;
    }
}
