using Godot;

namespace GamepadLab;

// Passive instrumentation for the one explicit D4 diagnostic. It never writes
// scrolling/range/layout and is inactive during ordinary and matrix execution.
public partial class LabHud
{
    private bool _d4TraceEnabled;
    private long _d4ModalGeneration;
    private string _d4ScrollWriter = "outside production EnsureControlVisible; native allocation/input owner not inferred";
    private readonly List<object> _d4ScrollTrace = [];
    public object[] D4ScrollTrace => _d4ScrollTrace.ToArray();

    public void EnableD4Trace()
    {
        _d4TraceEnabled = true;
        _modalScroll.GetVScrollBar().ValueChanged += D4ValueChanged;
        _modalScroll.GetVScrollBar().Changed += D4RangeChanged;
        _modalRows.SortChildren += D4RowsSorted;
        ((MarginContainer)_modalRows.GetParent()).SortChildren += D4PaddingSorted;
        RenderingServer.FramePostDraw += D4RenderedAllocation;
        ObserveD4Scroll("trace.enabled", "explicit diagnostic");
    }
    private void DisableD4Trace()
    {
        if (!_d4TraceEnabled) return;
        _modalScroll.GetVScrollBar().ValueChanged -= D4ValueChanged;
        _modalScroll.GetVScrollBar().Changed -= D4RangeChanged;
        _modalRows.SortChildren -= D4RowsSorted;
        ((MarginContainer)_modalRows.GetParent()).SortChildren -= D4PaddingSorted;
        RenderingServer.FramePostDraw -= D4RenderedAllocation;
        _d4TraceEnabled = false;
    }
    private void D4ValueChanged(double value) => ObserveD4Scroll("native.range.ValueChanged", _d4ScrollWriter);
    private void D4RangeChanged() => ObserveD4Scroll("native.range.Changed", "native range/allocation signal");
    private void D4RowsSorted() => ObserveD4Scroll("native.rows.SortChildren", "native VBoxContainer allocation signal");
    private void D4PaddingSorted() => ObserveD4Scroll("native.padding.SortChildren", "native MarginContainer allocation signal");
    private void D4RenderedAllocation() => ObserveD4Scroll("passive.FramePostDraw", "passive rendered allocation; no write");
    private void BeginD4Rebuild()
    {
        if (!_d4TraceEnabled) return;
        ObserveD4Scroll("RenderModal.before-rebuild", "production RenderModal/ClearRows (previous live nodes)");
        _d4ModalGeneration++;
    }
    public void ObserveD4Scroll(string phase, string owner)
    {
        if (!_d4TraceEnabled) return;
        var padding = (MarginContainer)_modalRows.GetParent();
        var panel = _modalScroll.GetThemeStylebox("panel");
        var outerPanel = _modal.GetThemeStylebox("panel");
        var bar = _modalScroll.GetVScrollBar();
        Label? row = _selected < _choiceRows.Count ? _choiceRows[_selected] : null;
        Label? title = _modalRows.GetChildCount() > 0 ? _modalRows.GetChild<Label>(0) : null;
        static object R(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, width = r.Size.X, height = r.Size.Y };
        static object C(Control c) => new { instance = c.GetInstanceId(), parent = c.GetParent().GetInstanceId(),
            rect = R(c.GetGlobalRect()), local = R(new Rect2(c.Position, c.Size)), minimum = c.CustomMinimumSize.ToString(),
            nativeMinimum = R(new Rect2(Vector2.Zero, c.GetMinimumSize())), combinedMinimum = R(new Rect2(Vector2.Zero, c.GetCombinedMinimumSize())),
            valid = GodotObject.IsInstanceValid(c), insideTree = c.IsInsideTree(), queuedForDeletion = c.IsQueuedForDeletion(), visible = c.IsVisibleInTree() };
        static object Margins(StyleBox s) => new { left = s.GetMargin(Side.Left), top = s.GetMargin(Side.Top),
            right = s.GetMargin(Side.Right), bottom = s.GetMargin(Side.Bottom), minimum = s.GetMinimumSize().ToString(), style = s.GetClass().ToString() };
        _d4ScrollTrace.Add(new { sequence = _d4ScrollTrace.Count, frame = Engine.GetProcessFrames(), drawnFrame = Engine.GetFramesDrawn(),
            phase, owner, generation = _d4ModalGeneration, productionGeneration = _modalGeneration,
            selectionGeneration = _selectionScrollGeneration, selectionRowInstance = _selectionScrollRow,
            kind = ModalKind.ToString(), selected = _selected,
            pending = _selectionScrollPending, passes = _selectionScrollPasses, originalPassCap = 4, readiness = SelectionScrollStatus,
            row = row is null ? null : new { text = row.Text, allocation = C(row), font = row.GetThemeFontSize("font_size"), clipText = row.ClipText },
            title = title is null ? null : new { text = title.Text, allocation = C(title), font = title.GetThemeFontSize("font_size"), clipText = title.ClipText },
            titlePredicates = title is null ? null : new { enclosed = UsableModalClip.Encloses(title.GetGlobalRect()),
                hasA = title.Text.Contains("[A/Enter]"), hasB = title.Text.Contains("[B/Esc]"), unclipped = !title.ClipText,
                heightAllocated = title.Size.Y >= title.GetMinimumSize().Y },
            titleRowUnion = title is null || row is null ? null : R(title.GetGlobalRect().Merge(row.GetGlobalRect())),
            unionFits = title is not null && row is not null && title.GetGlobalRect().Merge(row.GetGlobalRect()).Size.Y <= UsableModalClip.Size.Y,
            allRowAllocations = _choiceRows.Select(C).ToArray(), outer = R(ModalClip), usable = R(UsableModalClip),
            outerEncloses = row is not null && ModalClip.Encloses(row.GetGlobalRect()),
            paddedEncloses = row is not null && UsableModalClip.Encloses(row.GetGlobalRect()),
            modalPanel = C(_modal), padding = C(padding), stack = C(_modalRows), scrollbar = C(bar),
            nativeRange = new { min = bar.MinValue, max = bar.MaxValue, page = bar.Page, value = bar.Value, step = bar.Step, scroll = _modalScroll.ScrollVertical },
            panelMargins = Margins(panel), outerPanelMargins = Margins(outerPanel),
            paddingConstants = new { left = padding.GetThemeConstant("margin_left"), top = padding.GetThemeConstant("margin_top"),
                right = padding.GetThemeConstant("margin_right"), bottom = padding.GetThemeConstant("margin_bottom") },
            separation = _modalRows.GetThemeConstant("separation") });
    }
}
