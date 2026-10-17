using System.Collections.ObjectModel;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum ExperienceTableStatus
{
    Read,
    /// <summary>An entry whose text is not one number: optional spaces, an optional sign and 1..10 digits within the int32
    /// range. The source's <c>atoi</c> would read a prefix or 0; the project refuses the table instead.</summary>
    InvalidNumber,
    /// <summary>A <c>rules\gamequest.mes</c> quest entry without its first number (the quest's experience level).</summary>
    MissingField,
}

/// <summary>A read table: <see cref="Values"/> by entry number, or a refusal naming the first entry that failed.</summary>
public sealed record ExperienceTableRead(ExperienceTableStatus Status, IReadOnlyDictionary<int, int>? Values, int? Number = null);

/// <summary>A read <c>rules\xp_level.mes</c>: the experience of levels 1..51 in order, or a refusal.</summary>
public sealed record ExperienceLevelsRead(ExperienceTableStatus Status, IReadOnlyList<int>? Thresholds, int? Number = null);

/// <summary>
/// The experience tables the source loads at startup (ARC-WORLD-030; CE <c>level_init</c>, <c>quest_init</c>,
/// <c>quest_mod_load</c>, <c>critter_init</c> at the pinned revision, checked in the original executable, ledger XP6):
/// <c>rules\xp_level.mes</c> (the experience a PC needs for each level), <c>rules\xp_quest.mes</c> (quest experience by the
/// quest's experience level), <c>rules\xp_critter.mes</c> (an NPC's experience worth by its level) and the module's
/// <c>rules\gamequest.mes</c> (each quest's experience level, its first number). Numbers only; nothing is decoded as text.
/// </summary>
public static class ExperienceTables
{
    /// <summary>The source's level count (<c>LEVEL_MAX</c>).</summary>
    public const int Levels = 51;
    /// <summary>The quest numbers <c>quest_mod_load</c> reads.</summary>
    public const int FirstQuest = 1000, LastQuest = 1999;
    /// <summary>The experience level of a quest with no entry: the source's default.</summary>
    public const int DefaultQuestLevel = 1;

    /// <summary>
    /// <c>rules\xp_level.mes</c>: entry <c>n</c> is the experience of level <c>n</c> (1..51); a missing entry is 0, and level 1
    /// is 0 whatever the file says, as <c>level_init</c> reads it. Entries outside 1..51 are not read.
    /// </summary>
    public static ExperienceLevelsRead ReadLevels(MesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var thresholds = new int[Levels];
        for (var level = 1; level <= Levels; level++)
        {
            if (file.Find(level) is not { } message) continue;
            if (!Number(message.Bytes.Span, out var value)) return new(ExperienceTableStatus.InvalidNumber, null, level);
            thresholds[level - 1] = value;
        }
        thresholds[0] = 0;
        return new(ExperienceTableStatus.Read, Array.AsReadOnly(thresholds));
    }

    /// <summary>
    /// A table of numbers by entry (<c>rules\xp_quest.mes</c>, <c>rules\xp_critter.mes</c>): every entry, as the source looks
    /// any number up; a number with no entry is 0 there (<c>quest_get_xp</c>, <c>critter_xp_worth</c>).
    /// </summary>
    public static ExperienceTableRead ReadValues(MesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var values = new Dictionary<int, int>();
        foreach (var message in file.Messages)
        {
            if (!Number(message.Bytes.Span, out var value)) return new(ExperienceTableStatus.InvalidNumber, null, message.Number);
            values[message.Number] = value;
        }
        return new(ExperienceTableStatus.Read, new ReadOnlyDictionary<int, int>(values));
    }

    /// <summary>
    /// The experience level of each quest 1000..1999 that <c>rules\gamequest.mes</c> lists: the first space-separated number of
    /// its entry. Quests without an entry are not listed (their level is <see cref="DefaultQuestLevel"/>); other entry numbers
    /// are not read. The other 22 numbers (alignment and dialog lines) belong to other owners.
    /// </summary>
    public static ExperienceTableRead ReadQuestLevels(MesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var values = new Dictionary<int, int>();
        foreach (var message in file.Messages)
        {
            if (message.Number is < FirstQuest or > LastQuest) continue;
            var text = message.Bytes.Span;
            var start = 0;
            while (start < text.Length && text[start] == (byte)' ') start++;
            if (start == text.Length) return new(ExperienceTableStatus.MissingField, null, message.Number);
            var end = start;
            while (end < text.Length && text[end] != (byte)' ') end++;
            if (!Number(text[start..end], out var level)) return new(ExperienceTableStatus.InvalidNumber, null, message.Number);
            values[message.Number] = level;
        }
        return new(ExperienceTableStatus.Read, new ReadOnlyDictionary<int, int>(values));
    }

    // Optional spaces around an optional sign and 1..10 ASCII digits within the int32 range; never a partial atoi.
    private static bool Number(ReadOnlySpan<byte> text, out int value)
    {
        value = 0;
        var start = 0;
        var end = text.Length;
        while (start < end && text[start] == (byte)' ') start++;
        while (end > start && text[end - 1] == (byte)' ') end--;
        var negative = false;
        if (start < end && text[start] is (byte)'+' or (byte)'-')
        {
            negative = text[start] == (byte)'-';
            start++;
        }
        if (end - start is < 1 or > 10) return false;
        long result = 0;
        for (var i = start; i < end; i++)
        {
            if (text[i] is < (byte)'0' or > (byte)'9') return false;
            result = result * 10 + (text[i] - '0');
        }
        if (negative) result = -result;
        if (result is < int.MinValue or > int.MaxValue) return false;
        value = (int)result;
        return true;
    }
}
