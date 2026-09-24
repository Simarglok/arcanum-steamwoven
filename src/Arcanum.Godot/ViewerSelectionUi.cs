using Arcanum.Data;
using Arcanum.Presentation.Animation;
using Godot;

namespace Arcanum.Godot;

// Owns the actual selector and metadata controls so invalidation is observable and
// testable without a parallel synthetic UI model.
internal sealed class ViewerSelectionUi
{
    private readonly SpinBox _group;
    private readonly SpinBox _frame;
    private readonly SpinBox _palette;
    private readonly Label _metadata;
    private readonly Label _rate;

    internal ViewerSelectionUi(SpinBox group, SpinBox frame, SpinBox palette, Label metadata, Label rate)
    {
        _group = group;
        _frame = frame;
        _palette = palette;
        _metadata = metadata;
        _rate = rate;
    }

    internal void Clear(AnimationTimeline timeline)
    {
        ViewerState.ResetEntrySelectors(_group, _frame, _palette);
        _metadata.Text = "No frame";
        SyncTimeline(timeline);
    }

    internal void Commit(SelectedArtSnapshot snapshot, AnimationTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ViewerState.ConfigureEntrySelectors(_group, _frame, _palette, snapshot.Metadata);
        _metadata.Text = "No frame";
        SyncTimeline(timeline);
    }

    internal void PublishFrame(ArtFrame frame) => _metadata.Text = FrameMetadata(frame);

    internal void SyncTimeline(AnimationTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var source = timeline.SourceFramesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var effective = timeline.EffectiveFramesPerSecond?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "blocked";
        var manual = timeline.ManualFramesPerSecond?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        _rate.Text = $"source {source} fps; effective {effective} fps; override {manual}";
        _frame.Value = timeline.CurrentFrame;
    }

    private static string FrameMetadata(ArtFrame frame) =>
        $"{frame.Width} × {frame.Height}; raw hotspot ({frame.Frame.HotX}, {frame.Frame.HotY}); offset ({frame.Frame.OffsetX}, {frame.Frame.OffsetY}); requested palette {frame.RequestedPaletteSlot}; effective {frame.EffectivePaletteSlot}";
}