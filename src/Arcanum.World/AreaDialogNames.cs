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

    /// <summary>ARC-SCRIPT-2811: the names of one printed line from the objects of its context (ledger NM1, NM4): <paramref name="pcName"/> for <c>@pcname@</c> when the first object is not the hero (an NPC triggerer's
    /// not-yet-met description, read by <see cref="AreaObjectNameSource"/>), else the hero's player name; <paramref name="npcName"/> for <c>@npcname@</c> when given, else the fallback's.</summary>
    public static DialogSpeakerNames Of(GameSessionOwner session, DialogSpeakerNames fallback, string? pcName, string? npcName)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return new(pcName ?? PcName(session) ?? fallback.PcName, npcName ?? fallback.NpcName);
    }

    /// <summary>The viewer's NPC-name source: the speaker's description message taken as a recognized NPC (see the class remarks), from the loaded description tables;
    /// null while the tables are not loaded or when the name does not resolve to text (the caller prints it empty, never a diagnostic bracket text).</summary>
    public static Func<AreaObjectRecord, string?> NpcNameSource(Func<ObjectDescriptionCatalog?> catalog)
    {
        var source = ObjectNameSource(catalog);
        return row => source(row, CritterRecognition.Recognized, false);
    }

    /// <summary>
    /// ARC-SCRIPT-2811 (ledger NM3, NM6): the viewer's object-name source: the shown name of any object row as an observer sees it (an NPC is its own observer: recognized, its description; with no observer: its not-yet-met description;
    /// an item the session marked identified shows its identified description), from the loaded description tables; null while the tables are not loaded or when the name does not resolve to text.
    /// </summary>
    public static AreaObjectNameSource ObjectNameSource(Func<ObjectDescriptionCatalog?> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return (row, recognition, markedIdentified) =>
        {
            if (catalog() is not { } loaded) return null;
            var resolution = ObjectDisplayName.Resolve(row, loaded, recognition, markedIdentified);
            return resolution.Status == ObjectNameStatus.Resolved && MesText.Decode(resolution.Message!, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } && text.Length > 0 ? text : null;
        };
    }
}

/// <summary>ARC-SCRIPT-2811: the shown name of an object row for a line's name token (<see cref="AreaDialogNames.ObjectNameSource"/>): <paramref name="recognition"/> is what the observer knows of an NPC (none: not recognized), <paramref name="markedIdentified"/>
/// that the session marked the item identified; null when the name has no text.</summary>
public delegate string? AreaObjectNameSource(AreaObjectRecord row, CritterRecognition recognition, bool markedIdentified);
