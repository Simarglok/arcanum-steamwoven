using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1884: the original's quantity pane (bar mode 9, ledger QW3, QW3a) in the bottom strip while the quantity window asks (interface mode 13, ARC-UI-029): the
// mode's background art 298, the five buttons (All, up, down, Accept, Cancel), the kind's icon (an item art of place 2) and the count in the roller font. The
// window's logic is the inventory panel's (QuantityBar, Accept, Cancel); the pane shows its count and acts on a release over a button, as the skill pane does.
// Without the bars or the pane's background art the diagnostic row of the inventory panel stays the question.
public partial class TerrainViewerApp
{
    private AreaQuantityPaneView? _quantityPaneView;
    private (object? Art, object? Font)? _quantityPaneShown;
    private (object? Move, object? Icons)? _quantityIconKey;
    private Texture2D? _quantityIcon;
    private bool _quantityPaneHiddenForCapture;
    private int _quantityPanePressesForSelfTest;

    internal AreaQuantityPaneView? QuantityPaneForSelfTest => _quantityPaneView is { Visible: true } ? _quantityPaneView : null;
    internal int QuantityPanePressesForSelfTest => _quantityPanePressesForSelfTest;

    /// <summary>Per frame while the bars show (after the hotkey bar, whose art the pane shares): the pane over the bottom strip while the quantity window asks and its
    /// background art read; it follows the window's count.</summary>
    private void SyncQuantityPane()
    {
        if (_gameBarPanel is null) return;
        if (_quantityPaneView is null)
        {
            _quantityPaneView = new AreaQuantityPaneView { Visible = false };
            // Over the message window and the other parts of the strip: the original's mode draws its background over the window's place.
            _gameBarPanel.AddPart(AreaBarStrip.Bottom, _quantityPaneView);
            _quantityPaneView.ButtonPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "quantity pane button");
            _quantityPaneView.ButtonReleased += QuantityPaneReleased;
        }
        // The pane takes the question from the diagnostic row only when all of it can be drawn and clicked: the background, the five buttons and the count's font.
        var font = TextFonts()?.Roller;
        var shown = _inventoryPanel is { Quantity: not null } && font is not null && Art(QuantityPaneRules.BackgroundArt) is not null
            && QuantityPaneRules.Buttons.All(button => Art(button.Art) is { Length: > 0 }) && !_quantityPaneHiddenForCapture;
        if (_quantityPaneView.Visible != shown) _quantityPaneView.Visible = shown;
        _inventoryPanel?.HideQuantityRow(shown);
        if (!shown || _inventoryPanel?.Quantity is not { } bar) return;
        var stamp = ((object?)_hotkeyBarArt, (object?)font);
        if (_quantityPaneShown != stamp)
        {
            _quantityPaneShown = stamp;
            _quantityPaneView.Bind(new(First(Art(QuantityPaneRules.BackgroundArt)), [.. QuantityPaneRules.Buttons.Select(button => Art(button.Art))], font));
        }
        _quantityPaneView.SetState(bar.Count, QuantityIcon());
    }

    // ARC-UI-1818: the opt-in generated-chrome part (see WindowArtSmokeAsync) for the pane while the question is up: the background (298) and the three buttons' idle frames (299, 33, 32; the 13 x 10 up and down buttons stay pictures) draw at the strip's own device scale.
    private Task QuantityPaneArtSmokeAsync() => WindowArtSmokeAsync("quantity-pane", () =>
        {
            var view = _quantityPaneView;
            var found = new List<HdInterfaceTexture>();
            if (view?.BackgroundForSelfTest is HdInterfaceTexture background) found.Add(background);
            foreach (var button in QuantityPaneRules.Buttons.Where(button => button.Art is not (805 or 804)))
                if (view?.ButtonArtForSelfTest(button.Action) is HdInterfaceTexture texture) found.Add(texture);
            return found;
        }, [QuantityPaneRules.BackgroundArt, .. QuantityPaneRules.Buttons.Select(button => button.Art).Where(art => art is not (805 or 804))], BarDeviceScale, () => _quantityPaneView?.BackgroundForSelfTest is not null, new Vector2I(1600, 1200));

    /// <summary>The bars went away or the question ended: no pane is drawn and the diagnostic row is as it was.</summary>
    private void HideQuantityPane()
    {
        if (_quantityPaneView is not null) _quantityPaneView.Visible = false;
        _inventoryPanel?.HideQuantityRow(false);
    }

    // The kind's icon (0x551210): gold, or an ammunition type 0..3 (the type of the stack's line); an item art of place 2 read through the content stack.
    private Texture2D? QuantityIcon()
    {
        if (_quantityMove is not { } move || _hotkeyItemIcons is not { } icons) return null;
        var key = ((object?)move, (object?)icons);
        if (_quantityIconKey == key) return _quantityIcon;
        _quantityIcon = null;
        uint? aid = null;
        if (move.Kind.Type == SessionItemType.Gold) aid = QuantityPaneRules.IconArtId(true, 0);
        else if (move.Kind.Type == SessionItemType.Ammo)
        {
            var view = InventoryView(move.Holder);
            var line = view.Loot.Concat(view.Pc).FirstOrDefault(l => l.Entity == move.Item);
            // A line that does not read yet is looked for again next frame; a type the pane leaves unset (4, 5) stays unset.
            if (line is null) return null;
            if (line.StackAmmoType is { } type) aid = QuantityPaneRules.IconArtId(false, type);
        }
        _quantityIconKey = key;
        if (aid is { } id)
        {
            var (frame, detail) = icons.PaperFrame(id);
            GD.Print($"ARC-UI-1884 quantity icon aid=0x{id:X8} {(frame is null ? "not read" : $"{frame.Width}x{frame.Height}")} {detail}");
            if (frame is not null) _quantityIcon = HotkeyTexture(frame);
        }
        return _quantityIcon;
    }

    /// <summary>A release over a button (the bar's push buttons act on the release): the window's own step, the same as the diagnostic row's button.</summary>
    private void QuantityPaneReleased(QuantityPaneAction action, bool over)
    {
        if (!over || _inventoryPanel is not { Asking: true } panel) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "quantity pane button");
        _quantityPanePressesForSelfTest++;
        GD.Print($"ARC-UI-1884 quantity-pane button={action}");
        switch (action)
        {
            case QuantityPaneAction.All: panel.PressQuantity(QuantityAction.All); break;
            case QuantityPaneAction.Up: panel.PressQuantity(QuantityAction.Up); break;
            case QuantityPaneAction.Down: panel.PressQuantity(QuantityAction.Down); break;
            case QuantityPaneAction.Accept: panel.AcceptQuantity(); break;
            case QuantityPaneAction.Cancel: panel.CancelQuantity(); break;
        }
    }
}
