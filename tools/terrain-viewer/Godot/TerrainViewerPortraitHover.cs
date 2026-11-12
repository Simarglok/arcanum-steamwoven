using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1281 (ledger HK26, FR-ITEM-008, FR-UI-004): the follower portraits' hover. A pointer entering a follower's portrait button (callback 0x56A9D0,
// strip message type 1, code 2) with no order menu open (0x67BC0C = -1) and no dialog active (0x567400 = 0) describes the follower for the hero as the world's
// hover describes an object (0x57CD60: the follower's examine script first, the call at 0x56AD44; then 0x553BE0: the description in the message window;
// then 0x43C270: the highlight and the rings). Leaving the portrait clears them (0x550720, 0x43C270). The follower's row is the world hover's own row, so
// the same path serves both: `SyncHoverWindow` (the window and the script) and the highlight and ring decisions, which read the hovered key
// (<see cref="HoveredKeyNow"/>). The order menu's own rows have their own cases of the callback (OL34) and are not read here.
public partial class TerrainViewerApp
{
    private AreaObjectKey? _portraitHoverKey;
    private int _portraitHoversForSelfTest;
    // The examine's answer for the follower hovered by his portrait (true: the description follows), kept so a redraw of his window (the bars shown again) does not run the script a second time.
    private bool? _portraitAllows;

    /// <summary>The object the hover shows now: the one under the pointer, else the follower whose portrait the pointer rests on.</summary>
    private AreaObjectKey? HoveredKeyNow => _hoverKey ?? _portraitHoverKey;

    /// <summary>How many portrait hovers described a follower (the entry's examine, once each).</summary>
    internal int PortraitHoversForSelfTest => _portraitHoversForSelfTest;

    /// <summary>The follower the portrait hover describes now (null none).</summary>
    internal AreaObjectKey? PortraitHoverKeyForSelfTest => _portraitHoverKey;

    /// <summary>HK26: the pointer entered <paramref name="entity"/>'s portrait with no menu open. A dialog that is active (the dialog window is open)
    /// leaves it alone. The follower's examine script runs once and its description follows unless it answered skip default; the script's
    /// printed lines go to the message window. The window is drawn only while the interface bars show (as the dialog start's, HK25), the script runs
    /// either way.</summary>
    private void FollowerPortraitHovered(string entity)
    {
        if (ConversationOpen || _followerPortraits is not { MenuShown: false } || _readyArea is null
            || _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity) is not { } companion) return;
        var row = companion.Speaker.Row;
        var key = row.Key;
        _portraitHoversForSelfTest++;
        _portraitHoverKey = key;
        _portraitAllows = ExamineAllows(row);
        if (_gameBarPanel is { Visible: true } && _hoverIndex is { } index && index.Contains(key)) { _hoverWindowKey = null; SyncHoverWindow(key); }
        // 0x43C270: the highlight, whatever the script answered.
        if (_canvas is not null && _hoverIndex is { } rows && rows.Contains(key) && AreaHoverWindow.Highlighted(rows.Row(key))) _canvas.AreaHighlight = key;
        _hoverStamp = null;
    }

    /// <summary>HK26 (0x550720, 0x43C270): the pointer left the portrait: the description and the highlight go, and the world's hover takes over again.</summary>
    private void FollowerPortraitHoverLeft()
    {
        if (_portraitHoverKey is null) return;
        _portraitHoverKey = null; _portraitAllows = null;
        _hoverWindowKey = null;
        LeaveHoverWindow();
        if (_canvas is not null && _hoverKey is null) _canvas.AreaHighlight = null;
        _hoverStamp = null;
    }
}
