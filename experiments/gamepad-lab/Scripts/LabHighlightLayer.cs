using GamepadLab.Core;
using Godot;

namespace GamepadLab;

public interface ILabDisplayLabels { bool TryGetLabel(string id, string locale, out string label); }
public sealed class SyntheticDisplayLabels : ILabDisplayLabels
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["npc-ada"] = "Синтетическая Ада", ["npc-bram"] = "Synthetic Bram", ["enemy-ash"] = "Синтетический противник Ash",
        ["enemy-briar"] = "Synthetic Briar", ["door-north"] = "Северная дверь", ["door-east"] = "East door",
        ["crate"] = "Синтетический контейнер с очень длинной подписью для проверки переноса текста",
        ["tiny-gear"] = "Tiny synthetic gear", ["loose-vial"] = "Синтетический флакон", ["isolated-marker"] = "Synthetic marker", ["ally-iris"] = "Союзник Ирис"
    };
    public bool TryGetLabel(string id, string locale, out string label) => Labels.TryGetValue(id, out label!);
}

/// <summary>UI-scale labels from injected synthetic data; no collision/name/target authority.</summary>
public partial class LabHighlightLayer : Control
{
    public ILabDisplayLabels Provider { get; set; } = new SyntheticDisplayLabels();
    public string Locale { get; set; } = "ru";
    private readonly Dictionary<string, Label> _labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2> _anchors = new(StringComparer.Ordinal);
    private ScrollContainer _overflow = null!; private VBoxContainer _stack = null!;
    private sealed record LabelSpec(string Id, string Text, Vector2 Anchor);
    private LabelSpec[] _lastSpecs = [];
    private Rect2 _lastViewport;
    private int _lastScale;
    public long LayoutGeneration { get; private set; }
    public long SettledGeneration { get; private set; }
    public string SettlementStatus { get; private set; } = "settled";
    public long ShapeWork { get; private set; }
    public long SortWork { get; private set; }
    public long ReparentWork { get; private set; }
    private int _observations, _passes, _stableSamples;
    private bool _queryNeeded;
    private string? _previousAllocation;
    private readonly List<object> _settlementTrace = new();
    public object SettlementEvidence() => new { generation = LayoutGeneration, settledGeneration = SettledGeneration,
        status = SettlementStatus, observations = _observations, passes = _passes, shapeWork = ShapeWork,
        sortWork = SortWork, reparentWork = ReparentWork, trace = _settlementTrace.ToArray() };
    public ScrollContainer Overflow => _overflow;
    public IReadOnlyDictionary<string, Vector2> Anchors => _anchors;
    public IReadOnlyDictionary<string, Label> Labels => _labels;
    public int OverflowCount { get; private set; }
    private readonly Dictionary<string, object> _sizing = new(StringComparer.Ordinal);
    private static object Shape(Vector2 v) => new { x = v.X, y = v.Y };
    public object PassiveGeometryEvidence() => new
    {
        frame = Engine.GetProcessFrames(), generation = LayoutGeneration, requestedScale = _lastScale,
        overflow = Shape(_overflow.Size), overflowPosition = Shape(_overflow.GlobalPosition), stackSize = Shape(_stack.Size),
        scroll = new { value = _overflow.GetVScrollBar().Value, max = _overflow.GetVScrollBar().MaxValue, page = _overflow.GetVScrollBar().Page },
        labels = _labels.Select(p => new { id = p.Key, instance = p.Value.GetInstanceId(), index = p.Value.GetIndex(), text = p.Value.Text, parent = p.Value.GetParent().GetClass(),
            localPosition = Shape(p.Value.Position), customMinimum = Shape(p.Value.CustomMinimumSize),
            size = Shape(p.Value.Size), position = Shape(p.Value.GlobalPosition), maximum = Shape(p.Value.CustomMaximumSize),
            fontSize = p.Value.GetThemeFontSize("font_size") }).ToArray()
    };
    public object GeometryEvidence() => new
    {
        frame = Engine.GetProcessFrames(), generation = LayoutGeneration, overflow = Shape(_overflow.Size), overflowPosition = Shape(_overflow.GlobalPosition),
        stackSize = Shape(_stack.Size), stackMinimum = Shape(_stack.GetMinimumSize()), stackBoundMinimum = Shape(_stack.GetBoundMinimumSize()),
        stackCombinedMinimum = Shape(_stack.GetCombinedMinimumSize()), stackMaximum = Shape(_stack.CustomMaximumSize),
        scroll = new { value = _overflow.GetVScrollBar().Value, max = _overflow.GetVScrollBar().MaxValue, page = _overflow.GetVScrollBar().Page },
        labels = _labels.Select(p => new { id = p.Key, text = p.Value.Text, parent = p.Value.GetParent().GetClass(),
            size = Shape(p.Value.Size), position = Shape(p.Value.GlobalPosition), minimum = Shape(p.Value.GetMinimumSize()),
            combinedMinimum = Shape(p.Value.GetCombinedMinimumSize()), customMinimum = Shape(p.Value.CustomMinimumSize),
            maximum = Shape(p.Value.CustomMaximumSize), boundMinimum = Shape(p.Value.GetBoundMinimumSize()),
            lines = p.Value.GetLineCount(), fontSize = p.Value.GetThemeFontSize("font_size"), sizing = _sizing.GetValueOrDefault(p.Key) }).ToArray()
    };
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore; ClipContents = true;
        _overflow = new ScrollContainer { Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
        _stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _overflow.AddChild(_stack); AddChild(_overflow);
        RenderingServer.FramePostDraw += ObserveSettlement;
    }
    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= ObserveSettlement;
        LayoutGeneration++; SettlementStatus = "cancelled";
    }
    // Scroll values/global row positions are deliberately excluded: ordinary
    // user scrolling is not a new shaping generation.
    public string AllocationSignature() => System.Text.Json.JsonSerializer.Serialize(new
    {
        generation = LayoutGeneration, stack = Shape(_stack.Size), overflow = Shape(_overflow.Size),
        max = _overflow.GetVScrollBar().MaxValue, page = _overflow.GetVScrollBar().Page,
        labels = _labels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key,
            instance = p.Value.GetInstanceId(), parent = p.Value.GetParent().GetInstanceId(), index = p.Value.GetIndex(),
            local = Shape(p.Value.Position), size = Shape(p.Value.Size), minimum = Shape(p.Value.CustomMinimumSize), maximum = Shape(p.Value.CustomMaximumSize) }).ToArray()
    });
    private void ObserveSettlement()
    {
        if (!IsInsideTree() || SettlementStatus != "pending") return;
        var generation = LayoutGeneration;
        var allocation = AllocationSignature();
        _observations++;
        _settlementTrace.Add(new { phase = "passive-postdraw", observation = _observations, geometry = PassiveGeometryEvidence() });
        if (_queryNeeded)
        {
            if (++_passes > 4) { SettlementStatus = "failed/pass-budget"; return; }
            var changed = ReconcileNativeShape();
            if (!IsInsideTree() || generation != LayoutGeneration) return;
            _settlementTrace.Add(new { phase = "native-query", pass = _passes, changed });
            _queryNeeded = false; _previousAllocation = null; _stableSamples = 0;
        }
        else
        {
            if (!NativeRangeCoherent())
            {
                _queryNeeded = true; _previousAllocation = null; _stableSamples = 0;
            }
            else
            {
                if (_previousAllocation == allocation) _stableSamples++;
                else
                {
                    if (_previousAllocation is not null) _queryNeeded = true;
                    _stableSamples = 1; _previousAllocation = allocation;
                }
                if (_stableSamples >= 2)
                {
                    SettledGeneration = generation; SettlementStatus = "settled"; return;
                }
            }
        }
        if (_observations >= 8) SettlementStatus = "failed/observation-budget";
    }
    private bool ReconcileNativeShape()
    {
        var changed = false; var adjacentChanged = false; var padding = 8 * _lastScale / 100f;
        foreach (var label in _labels.Values)
        {
            ShapeWork++; _ = label.GetLineCount();
            var native = label.GetMinimumSize();
            var requiredHeight = native.Y + padding;
            if (label.GetParent() == _stack)
            {
                // Only actual text height, never an invented stack/range extent.
                if (!Mathf.IsEqualApprox(label.CustomMinimumSize.Y, requiredHeight))
                { label.CustomMinimumSize = new Vector2(0, requiredHeight); changed = true; }
            }
            else if (!Mathf.IsEqualApprox(label.Size.Y, requiredHeight)) adjacentChanged = true;
        }
        if (adjacentChanged) { PlaceLabels(); changed = true; }
        else if (changed || !NativeRangeCoherent()) QueueNativeSort();
        return changed;
    }
    private bool NativeRangeCoherent() => !_overflow.Visible ||
        Mathf.IsEqualApprox((float)_overflow.GetVScrollBar().MaxValue, _stack.Size.Y);
    private void QueueNativeSort()
    {
        SortWork++; _stack.QueueSort(); _overflow.QueueSort();
    }
    public void UpdateLabels(IEnumerable<LabWorldTarget> targets, Node2D world, Rect2 viewport, int scalePercent)
    {
        var ordered = targets.Where(t => t.Visible && t.Interactive && t.Alive).OrderBy(t => t.CycleOrder).ThenBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => (Target: t, Anchor: world.GetGlobalTransformWithCanvas() * new Vector2((float)t.Anchor.X, (float)t.Anchor.Y)))
            .Where(p => viewport.HasPoint(p.Anchor)).Take(32).ToArray();
        var specs = ordered.Select(p => new LabelSpec(p.Target.Id,
            Provider.TryGetLabel(p.Target.Id, Locale, out var text) ? text : $"[Missing synthetic display label: {p.Target.Id}]", p.Anchor)).ToArray();
        if (_lastScale == scalePercent && _lastViewport == viewport && _lastSpecs.SequenceEqual(specs)) return;
        _lastSpecs = specs; _lastScale = scalePercent; _lastViewport = viewport; LayoutGeneration++;
        SettlementStatus = specs.Length == 0 ? "settled" : "pending";
        if (specs.Length == 0) SettledGeneration = LayoutGeneration;
        _observations = 0; _passes = 0; _stableSamples = 0; _previousAllocation = null;
        _queryNeeded = true; _settlementTrace.Clear();
        var ids = specs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _labels.Keys.Where(id => !ids.Contains(id)).ToArray()) { var old = _labels[id]; old.GetParent().RemoveChild(old); old.Free(); _labels.Remove(id); }
        PlaceLabels();
    }
    private void PlaceLabels()
    {
        var specs = _lastSpecs; var viewport = _lastViewport; var scalePercent = _lastScale;
        _anchors.Clear(); _sizing.Clear(); OverflowCount = 0;
        var scale = scalePercent / 100f; var fontSize = (int)(16 * scale); var width = 200 * scale;
        var placed = new List<Rect2>();
        _overflow.Position = new Vector2(410, 70); _overflow.Size = new Vector2(Math.Max(220, viewport.Size.X - 820), Math.Max(160, viewport.Size.Y * .55f));
        foreach (var (id, text, anchor) in specs)
        {
            if (!_labels.TryGetValue(id, out var label))
            {
                label = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = false };
                label.AddThemeColorOverride("font_color", Colors.White); label.AddThemeColorOverride("font_shadow_color", Colors.Black); label.AddThemeConstantOverride("shadow_outline_size", 3);
                _labels[id] = label; AddChild(label);
            }
            // Reparent before shaping: a Container must not own the adjacent wrap width.
            if (label.GetParent() != this) { label.GetParent().RemoveChild(label); AddChild(label); ReparentWork++; }
            label.Text = text; label.AddThemeFontSizeOverride("font_size", fontSize);
            var size = ShapeAtWidth(label, width, 8 * scale);
            _sizing[id] = new { requested = Shape(size), actualBeforePlacement = Shape(label.Size), minimum = Shape(label.GetMinimumSize()) };
            var positions = new[] { anchor + new Vector2(24, -size.Y), anchor + new Vector2(24, 20), anchor - new Vector2(width + 24, size.Y), anchor + new Vector2(-width - 24, 20) };
            Rect2? chosen = null;
            foreach (var point in positions)
            {
                var rect = new Rect2(point, size);
                if (viewport.Encloses(rect) && !rect.Intersects(new Rect2(_overflow.Position, _overflow.Size)) && placed.All(p => !p.Grow(4).Intersects(rect))) { chosen = rect; break; }
            }
            if (chosen is Rect2 adjacent)
            {
                label.Position = adjacent.Position; label.Size = adjacent.Size;
                var actual = label.GetRect();
                if (viewport.Encloses(actual) && placed.All(p => !p.Grow(4).Intersects(actual)))
                { placed.Add(actual); _anchors[id] = anchor; continue; }
            }
            // Use the final scrolling content width, including the native scrollbar.
            var panel = _overflow.GetThemeStylebox("panel");
            var overflowWidth = _overflow.Size.X - _overflow.GetVScrollBar().GetCombinedMinimumSize().X - panel.GetMinimumSize().X;
            var row = ShapeAtWidth(label, overflowWidth, 8 * scale);
            label.CustomMinimumSize = new Vector2(0, row.Y);
            label.GetParent().RemoveChild(label); _stack.AddChild(label); ReparentWork++; OverflowCount++;
        }
        _overflow.Visible = OverflowCount > 0;
        // Final child/reparent/show changes must invalidate the parent's cached
        // minimum too, otherwise the scrollbar can omit the last row's extent.
        // This runs only on a changed generation, not on every input frame.
        QueueNativeSort();
        QueueRedraw();
    }
    private Vector2 ShapeAtWidth(Label label, float width, float padding)
    {
        ShapeWork++;
        label.CustomMinimumSize = Vector2.Zero;
        // Godot 4.7 wrapped Labels require an effective maximum width, not only
        // Size.X. Height remains unbounded so all native wrapped lines contribute
        // to the container's minimum and therefore its real scrollbar range.
        label.CustomMaximumSize = new Vector2(width, -1);
        // Width first, then force native Label shaping/minimum invalidation before packing.
        // A transient zero-width wrapped minimum is not a usable measured rectangle.
        label.Size = new Vector2(width, Math.Max(1, label.Size.Y));
        // Force the current text/font/wrap generation to shape before reading
        // its minimum. A minimum query alone can still report the previous
        // shaped font height immediately after a theme override.
        _ = label.GetLineCount();
        var minimum = label.GetBoundMinimumSize();
        label.Size = new Vector2(width, minimum.Y + padding);
        _ = label.GetLineCount();
        minimum = label.GetBoundMinimumSize();
        label.Size = new Vector2(width, minimum.Y + padding);
        return label.Size;
    }
    public override void _Draw()
    {
        foreach (var (id, anchor) in _anchors)
            DrawLine(anchor, _labels[id].Position + new Vector2(0, _labels[id].Size.Y / 2), Colors.Cyan, 1);
    }
}
