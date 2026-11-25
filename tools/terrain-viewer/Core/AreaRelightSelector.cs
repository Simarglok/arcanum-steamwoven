namespace Arcanum.TerrainViewer.Core;

/// <summary>A relit light as the selector sees it (geometry only, world pixels); <see cref="Carried"/> marks a light that rides with a creature.</summary>
public readonly record struct RelightCandidate(float X, float Y, float Radius, bool Carried);

/// <summary>
/// ARC-RENDER-3127: the relit look's per-frame light choice without allocation (ARC-RES-2007 culling rule): of the candidates, the ones whose disk reaches the
/// shown view are kept and the <c>max</c> whose centre is nearest the view's centre are chosen, in that order, ties in candidate order (the stable sort of the
/// former LINQ chain); with no view every candidate reaches and the first <c>max</c> are chosen in candidate order. The buffers are fixed at construction, so
/// a call allocates nothing. The view is given as a Godot <c>Rect2</c> would hold it (position and size; end = position + size, centre = position + size / 2).
/// </summary>
public sealed class RelightSelector
{
    private readonly int[] _chosen;
    private readonly float[] _keys;

    public RelightSelector(int max)
    {
        if (max < 1) throw new ArgumentOutOfRangeException(nameof(max));
        _chosen = new int[max]; _keys = new float[max];
    }

    /// <summary>How many candidates were chosen at the last <see cref="Select"/>.</summary>
    public int Count { get; private set; }
    /// <summary>The indices (into the candidates of the last call) of the chosen lights, nearest the view's centre first.</summary>
    public ReadOnlySpan<int> Chosen => _chosen.AsSpan(0, Count);
    /// <summary>How many candidates reached the view before the cap, and how many of those were carried.</summary>
    public int InView { get; private set; }
    public int CarriedInView { get; private set; }

    public void Select(ReadOnlySpan<RelightCandidate> candidates, bool hasView, float viewX, float viewY, float viewWidth, float viewHeight)
    {
        var max = _chosen.Length;
        var count = 0; var inView = 0; var carried = 0;
        var endX = viewX + viewWidth; var endY = viewY + viewHeight;
        var centreX = viewX + viewWidth * 0.5f; var centreY = viewY + viewHeight * 0.5f;
        for (var i = 0; i < candidates.Length; i++)
        {
            var l = candidates[i];
            if (hasView)
            {
                var nx = Math.Clamp(l.X, viewX, endX); var ny = Math.Clamp(l.Y, viewY, endY);
                if (!((l.X - nx) * (l.X - nx) + (l.Y - ny) * (l.Y - ny) < l.Radius * l.Radius)) continue;
            }
            inView++;
            if (l.Carried) carried++;
            if (!hasView)
            {
                // No view: the candidates in their own order, the first ones kept.
                if (count < max) { _chosen[count] = i; count++; }
                continue;
            }
            var key = (l.X - centreX) * (l.X - centreX) + (l.Y - centreY) * (l.Y - centreY);
            // Stable partial sort: a later candidate of an equal key goes after the earlier ones, so it falls off a full list.
            if (count == max && key >= _keys[max - 1]) continue;
            var at = count < max ? count : max - 1;
            while (at > 0 && _keys[at - 1] > key) { _keys[at] = _keys[at - 1]; _chosen[at] = _chosen[at - 1]; at--; }
            _keys[at] = key; _chosen[at] = i;
            if (count < max) count++;
        }
        Count = count; InView = inView; CarriedInView = carried;
    }
}
