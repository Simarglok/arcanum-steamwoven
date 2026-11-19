using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-2787 (exe 0x416B00 over 0x441B60): the names a dialog line's <c>@pcname@</c> and <c>@npcname@</c> become. The PC's name is its
/// string field 0x10E (the session hero's player name, the bytes up to the first zero, decoded as the game's texts are; an unset name is the empty
/// string); the NPC's is the shown name of the speaker object taken with the speaker itself as observer (0x45F970 on the speaker: always recognized,
/// so its description field, not the unknown-critter one). A host with no hero or no NPC-name source keeps the fallback names (the diagnostic
/// profile's placeholders, used by tests only).
/// </summary>
public static class AreaDialogNames
{
    /// <summary>The hero's player name as the original prints it; null when the session has no hero.</summary>
    public static string? PcName(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is not { } hero) return null;
        var bytes = hero.PlayerName.AsSpan();
        var end = bytes.IndexOf((byte)0);
        if (end >= 0) bytes = bytes[..end];
        if (bytes.IsEmpty) return string.Empty;
        return MesText.Decode(bytes, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : string.Empty;
    }

    /// <summary>The names of one line: the hero's name when the session has a hero, else the fallback's; <paramref name="npcName"/> when given
    /// (<see cref="NpcName"/>), else the fallback's.</summary>
    public static DialogSpeakerNames For(GameSessionOwner session, DialogSpeakerNames fallback, string? npcName)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return new(PcName(session) ?? fallback.PcName, npcName ?? fallback.NpcName);
    }

    /// <summary>The speaker NPC's shown name when the host has an NPC-name source (<paramref name="source"/> given): the source's text, the empty string
    /// when it has none or the speaker is unknown (the original prints an empty name for a name without text); null without a source (the fallback applies).</summary>
    public static string? NpcName(Func<AreaObjectRecord, string?>? source, AreaObjectRecord? row) =>
        source is null ? null : row is null ? string.Empty : source(row) ?? string.Empty;

    /// <summary>The viewer's NPC-name source: the speaker's description message taken as a recognized NPC (see the class remarks), from the loaded description tables.</summary>
    public static Func<AreaObjectRecord, string?> NpcNameSource(Func<ObjectDescriptionCatalog?> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return row => catalog() is { } loaded ? AreaHoverIndex.Text(row, loaded, CritterRecognition.Recognized) : null;
    }
}
