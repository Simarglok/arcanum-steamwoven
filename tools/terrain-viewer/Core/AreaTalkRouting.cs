namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-WORLD-048: which conversation host a speaker talks through. A speaker's index means something only in the host that lists
/// it (the host's dialog state is indexed by it), so a speaker goes only to the host that lists it: the shown map's, or a visiting
/// companion's own. A speaker no host lists has none: sending it to another host would read and write that host's NPC at the same
/// index. The viewer's self-test, whose synthetic map has no dialog NPC, routes its stand-in speaker itself.
/// </summary>
public static class AreaTalkRouting
{
    /// <summary>The host that lists <paramref name="speaker"/>: <paramref name="shown"/> or one of <paramref name="visitors"/>; null when
    /// none does.</summary>
    public static AreaConversationHost? HostOf(AreaSpeaker speaker, AreaConversationHost shown, IEnumerable<AreaConversationHost> visitors)
    {
        ArgumentNullException.ThrowIfNull(speaker); ArgumentNullException.ThrowIfNull(shown); ArgumentNullException.ThrowIfNull(visitors);
        return shown.Speakers.Contains(speaker) ? shown : visitors.FirstOrDefault(host => host.Speakers.Contains(speaker));
    }
}
