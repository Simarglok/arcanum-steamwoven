using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

// ARC-UI-1924 (ledger IW46, docs/ui/original-windows.md): the stat hot spots of the inventory windows. A release over one (button message subtype 0) first brings the bottom bar to the message mode
// (0x5506C0 with 0: the Spells, Skills and weapon panes go) and shows the weapon stat lines (0x556220) or the armour stat lines (0x5566B0) of the hero or the other party in the message window;
// the pointer entering one (subtype 2) shows them without changing the mode, and in a pane mode only the title is written into the pane's text area (0x550770).
public partial class TerrainViewerApp
{
    /// <summary>The last stat lines the hot spots showed, for the smoke.</summary>
    internal (AreaStatRequest Request, AreaMessageEntry Entry)? LastStatLinesForSelfTest { get; private set; }

    private void ShowStatLines(AreaStatRequest request)
    {
        var texts = _gameBarContent?.Texts ?? AreaGameBarTexts.Project("no interface bars");
        var weapon = request.Target is AreaStatTarget.HeroWeapon or AreaStatTarget.PartnerWeapon;
        var entry = weapon ? AreaStatLines.Weapon(request.Ratings, texts) : AreaStatLines.Armour(request.Ratings, texts);
        if (entry is null) return;
        if (request.Release) LeaveBarPane();
        LastStatLinesForSelfTest = (request, entry);
        var shown = _messageLog.PaneMode ? _messageLog.ShowPaneName(MessagePaneMode, entry.Title) : _messageLog.ShowItem(entry);
        if (shown) _gameBarPanel?.MessageLog.Render();
    }

    /// <summary>0x5506C0 with 0: the bar goes to the message mode; the Spells and Skills toggles come up and the weapon's spells pane goes, as the mode request replaces the mode shown.</summary>
    private void LeaveBarPane()
    {
        if (!_gameBar.IsOn(AreaBarButton.Spells) && !_gameBar.IsOn(AreaBarButton.Skills) && _itemPaneItem is null && !_messageLog.PaneMode) return;
        TakeWindowFromSpells();
        _gameBar.SetOn(AreaBarButton.Spells, false);
        _gameBar.SetOn(AreaBarButton.Skills, false);
        if (_messagePaneShown)
        {
            _messagePaneShown = false;
            _messagePaneMode = 0;
        }
        _messageLog.EnterMessages();
        _gameBarPanel?.Render();
    }
}
