using Arcanum.Input;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Party;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-2305: the original's typed order line (bottom strip bar mode 7, ledger BM0, BM5; exe 0x54C8E0, 0x5718A0, 0x571910, 0x571950, 0x571990, 0x552160, text
// entry 0x566F00). Enter opens it while the world owns input; the typed text edits as the original's entry does (OrderLine); Enter sends a non-empty text as
// a broadcast (the hero says it, then the followers read it: ARC-WORLD-046's order walk with the text addressing) and closes the line; Escape (which opens the
// menu, the menu's closer ending the line), the menu, a window or any change of the strip's mode closes it and drops the text. All hotkeys stand down while
// the line is open (every key except Escape is the entry's).
public partial class TerrainViewerApp
{
    private readonly OrderLine _orderLine = new();
    private AreaOrderLineView? _orderLineView;
    private (object? Art, object? Font)? _orderLineShown;
    private int _orderLineShownRevision = -1;
    // The strip's mode when the line opened: a different one later is a bar mode change (0x5506C0 runs the closer).
    private (int PaneMode, bool Quantity)? _orderLineBarMode;
    private (string Module, AreaBroadcastKeywordsStatus Status, BroadcastKeywords? Keywords, string Detail)? _broadcastKeywords;
    private string? _orderLineLastSent;
    // The keys whose press the line took: their release is the line's too, so no release-acting hotkey (M, K, E, ...) sees the typing.
    private readonly HashSet<Key> _orderLineTaken = [];

    internal OrderLine OrderLineForSelfTest => _orderLine;
    internal AreaOrderLineView? OrderLineViewForSelfTest => _orderLineView is { Visible: true } ? _orderLineView : null;
    internal string? OrderLineLastSentForSelfTest => _orderLineLastSent;

    private (int PaneMode, bool Quantity) OrderLineBarMode() => (_messagePaneShown ? _messagePaneMode : 0, _inventoryPanel is { Quantity: not null });

    /// <summary>Enter while the line is closed (0x54C8E0 → 0x5718A0): opens it when the world owns input, the bars show and the message window's font read. Enter in
    /// the open line is the entry's (<see cref="HandleOrderLineKey"/>); other keys are not this route's.</summary>
    private bool RouteOrderLineKey(WorldKeyDecision decision, WorldKeyModifiers modifiers)
    {
        if (decision.Key != WorldKey.Enter || modifiers != WorldKeyModifiers.None || _orderLine.IsOpen) return false;
        if (!OrderInputOpen || _gameBarPanel is not { Visible: true } || TextFonts() is null || _inventoryPanel is { Quantity: not null }) return false;
        if (decision.Echo || !decision.Pressed) return true;
        _orderLine.Open();
        _orderLineBarMode = OrderLineBarMode();
        _orderLineTaken.Add(Key.Enter);
        var (status, _, detail) = OrderKeywords();
        GD.Print($"ARC-UI-2305 order line opened keywords={status} {detail}");
        SyncOrderLine();
        return true;
    }

    private (AreaBroadcastKeywordsStatus Status, BroadcastKeywords? Keywords, string Detail) OrderKeywords()
    {
        var module = _arguments?.ModulePath;
        if (module is null) return (AreaBroadcastKeywordsStatus.ModuleOutsideGameRoot, null, "no module");
        if (_broadcastKeywords is not { } cached || cached.Module != module)
        {
            var (status, keywords, detail) = AreaBroadcastKeywords.Load(module);
            _broadcastKeywords = (module, status, keywords, detail);
            GD.Print($"ARC-UI-2305 order words {status} {detail}");
        }
        var known = _broadcastKeywords!.Value;
        return (known.Status, known.Keywords, known.Detail);
    }

    /// <summary>While the line is open every key but Escape is its own (0x54C8E0 sends the character and key events to the entry and skips every hotkey):
    /// Enter accepts, Backspace, Delete, Home, the arrows and Insert edit, Tab does nothing, a printable character is typed. Escape is not taken (the menu opens and closes the line); the release of a key the line took is its own too, so no
    /// release-acting hotkey (M, K, E) sees the typing. True when the event is consumed.</summary>
    private bool HandleOrderLineKey(InputEventKey key)
    {
        var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed) return _orderLineTaken.Remove(code);
        if (!_orderLine.IsOpen) return false;
        if (code == Key.Escape) return false;
        _orderLineTaken.Add(code);
        switch (code)
        {
            case Key.Enter or Key.KpEnter: if (!key.Echo) AcceptOrderLine(); return true;
            case Key.Backspace: _orderLine.Type('\b'); return true;
            case Key.Delete: _orderLine.Press(OrderLineKey.Delete); return true;
            case Key.Home: _orderLine.Press(OrderLineKey.Home); return true;
            case Key.Left: _orderLine.Press(OrderLineKey.Left); return true;
            case Key.Right: _orderLine.Press(OrderLineKey.Right); return true;
            case Key.Up: _orderLine.Press(OrderLineKey.Up); return true;
            case Key.Down: _orderLine.Press(OrderLineKey.Down); return true;
            case Key.Insert: if (!key.Echo) _orderLine.Press(OrderLineKey.Insert); return true;
            case Key.Tab: return true;
        }
        if (key.Unicode >= ' ' && key.Unicode <= 0xFF && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) _orderLine.Type((char)key.Unicode);
        return true;
    }

    /// <summary>Enter in the open line (0x571950): a non-empty text is floated over the hero and sent to the followers with the highlighted object and the tile
    /// under the pointer, then the line closes.</summary>
    private void AcceptOrderLine()
    {
        if (!OrderInputOpen) { CloseOrderLine("the world no longer owns input"); return; }
        var text = _orderLine.Accept();
        _orderLineBarMode = null;
        if (text is null) { GD.Print("ARC-UI-2305 order line closed empty"); return; }
        var (status, keywords, detail) = OrderKeywords();
        if (keywords is null)
        {
            _companionNotice = $"Enter: order not sent (order words {status}: {detail})";
            RefreshReadyStatus();
            return;
        }
        _orderLineLastSent = text;
        // OL2: the PC's own text is floated over the PC first (callback 0x5FDC88 of 0x4C31A0).
        Bubble(HeroFloater, text);
        IssueAreaOrder("Enter", new(CompanionOrderType.None, CompanionOrderAddressing.Text, [], Tile: PointerCell())
            { Text = text, Keywords = keywords, Target = OrderTarget(_hoverKey) });
    }

    /// <summary>Per frame while the bars show (after the quantity pane): the line over the strip while it is open; a change of the strip's mode closes it
    /// (the original's closer runs on every mode change).</summary>
    private void SyncOrderLine()
    {
        if (_gameBarPanel is null) return;
        if (_orderLineView is null)
        {
            _orderLineView = new AreaOrderLineView { Visible = false };
            _gameBarPanel.AddPart(AreaBarStrip.Bottom, _orderLineView);
        }
        if (_orderLine.IsOpen && _orderLineBarMode is { } opened && opened != OrderLineBarMode()) CloseOrderLine("the bar mode changed");
        // A window (a dialog or the inventory opened by the mouse), a turn that is not the hero's, a pending map change or a lost focus: the world no longer owns the input.
        if (_orderLine.IsOpen && (!OrderInputOpen || WorldModal)) CloseOrderLine("the world no longer owns input");
        var font = TextFonts()?.Text;
        var shown = _orderLine.IsOpen && font is not null;
        if (_orderLineView.Visible != shown) _orderLineView.Visible = shown;
        if (!shown) return;
        var stamp = ((object?)Art(OrderLine.BackgroundArt), (object?)font);
        if (_orderLineShown != stamp)
        {
            _orderLineShown = stamp;
            _orderLineView.Bind(new(First(Art(OrderLine.BackgroundArt)), font!));
            _orderLineShownRevision = -1;
        }
        if (_orderLineShownRevision != _orderLine.Revision)
        {
            _orderLineShownRevision = _orderLine.Revision;
            _orderLineView.SetText(_orderLine.Shown);
        }
    }

    /// <summary>The bars went away (the menu, a window): the line closes with its text.</summary>
    private void HideOrderLine()
    {
        if (_orderLine.IsOpen) CloseOrderLine("the bars went away");
        if (_orderLineView is not null) _orderLineView.Visible = false;
    }

    private void CloseOrderLine(string reason)
    {
        if (!_orderLine.Close()) return;
        _orderLineBarMode = null;
        GD.Print($"ARC-UI-2305 order line closed: {reason}");
    }
}
