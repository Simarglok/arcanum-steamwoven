using Arcanum.Data;
using Godot;

namespace Arcanum.Godot;

// The owned texture and the canvas reference must move as one transaction. Keeping
// this small seam outside ViewerApp makes the actual production publication path
// deterministic to exercise in the Godot self-test.
internal sealed class ViewerPublicationState
{
    internal ImageTexture? Texture { get; private set; }
    internal SelectedArtSnapshot? CanvasArt { get; private set; }
    internal int? CanvasGroup { get; private set; }

    internal void Commit(ImageTexture texture, SelectedArtSnapshot snapshot, int group)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Texture = texture;
        CanvasArt = snapshot;
        CanvasGroup = group;
    }

    internal void Clear(FrameCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.ClearAsset();
        CanvasArt = null;
        CanvasGroup = null;
        var previous = Texture;
        Texture = null;
        previous?.Dispose();
    }
}

internal interface IViewerFramePublicationObserver
{
    void ReplacementCreated();
    void ReplacementDiscarded();
    void PriorTextureDisposed();
}

internal static class ViewerFramePublication
{
    internal static FrameCanvasPublicationEffect Commit(FrameCanvas canvas, ViewerPublicationState state,
        SelectedArtSnapshot snapshot, FrameTarget target, ArtFrame decoded, Action? afterCanvasMutation = null,
        IViewerFramePublicationObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(decoded);

        var layout = ViewerApp.GroupLayout(snapshot.Metadata, target.Group);
        ImageTexture? replacement = null;
        try
        {
            replacement = ViewerApp.CreateFrameTexture(decoded);
            observer?.ReplacementCreated();
            var effect = canvas.CommitFramePublication(replacement, new FrameHotspot(decoded.Frame.HotX, decoded.Frame.HotY), layout,
                resetAsset: !ReferenceEquals(state.CanvasArt, snapshot), updateGroupLayout: state.CanvasGroup != target.Group);
            afterCanvasMutation?.Invoke();

            // This is the ownership commit. No operation above can leave state
            // changed if it fails, and no cleanup below can roll back a committed
            // canvas texture by disposing the replacement.
            var previous = state.Texture;
            state.Commit(replacement, snapshot, target.Group);
            replacement = null;
            previous?.Dispose();
            if (previous is not null)
            {
                observer?.PriorTextureDisposed();
            }

            return effect;
        }
        finally
        {
            if (replacement is not null)
            {
                replacement.Dispose();
                observer?.ReplacementDiscarded();
            }
        }
    }
}
