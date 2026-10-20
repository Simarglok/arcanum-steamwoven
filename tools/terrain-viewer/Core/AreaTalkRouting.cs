namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-WORLD-048: which conversation host a speaker talks through. A speaker's index means something only in its own host, so a
/// visitor's speaker never goes to the shown map's host. Any other speaker (the shown map's own, or one that no host lists, such
/// as a row the viewer stands in for a speaker) goes to the shown map's host, as every speaker did before visitor hosts existed.
/// </summary>
public static class AreaTalkRouting
{
    /// <summary>The host of <paramref name="speaker"/>: the visitor host that lists it, else null when a visitor host lists a speaker
    /// of the same object (a visitor's speaker from an earlier host), else <paramref name="shown"/>.</summary>
    public static AreaConversationHost? HostOf(AreaSpeaker speaker, AreaConversationHost shown, IEnumerable<AreaConversationHost> visitors)
    {
        ArgumentNullException.ThrowIfNull(speaker); ArgumentNullException.ThrowIfNull(shown); ArgumentNullException.ThrowIfNull(visitors);
        if (shown.Speakers.Contains(speaker)) return shown;
        var hosts = visitors.ToArray();
        if (hosts.FirstOrDefault(host => host.Speakers.Contains(speaker)) is { } own) return own;
        return hosts.Any(host => host.Speakers.Any(s => s.Row.Key == speaker.Row.Key)) ? null : shown;
    }
}
