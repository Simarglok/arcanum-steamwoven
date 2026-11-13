using System.Collections.ObjectModel;

namespace Arcanum.Data.Messages;

public enum QuestReplyTableStatus
{
    Read,
    /// <summary>A quest entry holds a token that is not one number (optional sign, 1..10 digits within the int32 range). The source's <c>atoi</c> would read a
    /// prefix or 0; the project refuses the table instead.</summary>
    InvalidNumber,
}

/// <summary>A read table or its refusal, naming the first quest entry that failed.</summary>
public sealed record QuestReplyTableRead(QuestReplyTableStatus Status, QuestReplyTable? Table, int? Number = null);

/// <summary>
/// The module's <c>rules\gamequest.mes</c> as the dialog's quest-state reply reads it (ARC-SCRIPT-1532, ledger DQR1; the table filler 0x4C4860): each quest 1000..1999
/// is one entry of space-separated numbers (the source splits it with one token call per field, 23 fields: the experience level, a second number, then three
/// groups of seven). The groups are the DLG record the <c>q:</c> reply shows for the PC's quest state 0..6: <see cref="NormalGroup"/> normally,
/// <see cref="LowReactionGroup"/> when the NPC's reaction toward the PC is below 20 and <see cref="LowIntelligenceGroup"/> when the PC's Intelligence is below 5
/// (the chooser 0x4C4C00). A field the entry does not hold, or a quest with no entry, answers null (the source would read an unfilled table cell).
/// </summary>
public sealed class QuestReplyTable
{
    public const int FirstQuest = 1000, LastQuest = 1999;
    /// <summary>The numbers the source reads per quest entry (token calls 0x4C4860 makes).</summary>
    public const int Fields = 23;
    public const int NormalGroup = 2, LowReactionGroup = 9, LowIntelligenceGroup = 16, States = 7;

    private readonly IReadOnlyDictionary<int, int[]> _rows;

    private QuestReplyTable(IReadOnlyDictionary<int, int[]> rows) => _rows = rows;

    /// <summary>A table of the given quest rows (synthetic data); each row holds at most <see cref="Fields"/> numbers.</summary>
    public static QuestReplyTable Create(IReadOnlyDictionary<int, int[]> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var copy = new Dictionary<int, int[]>();
        foreach (var (quest, row) in rows)
        {
            if (quest is < FirstQuest or > LastQuest || row is null || row.Length > Fields) throw new ArgumentException("A quest 1000..1999 and at most 23 numbers.", nameof(rows));
            copy[quest] = (int[])row.Clone();
        }
        return new(new ReadOnlyDictionary<int, int[]>(copy));
    }

    /// <summary>The numbers of every quest 1000..1999 entry of <paramref name="file"/> (other entries are not read, 0x4C4860 reads that range).</summary>
    [OriginalFunction(0x4C4860, OriginalFunctionScope.Partial)]
    public static QuestReplyTableRead Read(MesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var rows = new Dictionary<int, int[]>();
        foreach (var message in file.Messages)
        {
            if (message.Number is < FirstQuest or > LastQuest) continue;
            var numbers = new List<int>();
            var text = message.Bytes.Span;
            var at = 0;
            while (at < text.Length && numbers.Count < Fields)
            {
                while (at < text.Length && text[at] == (byte)' ') at++;
                var end = at;
                while (end < text.Length && text[end] != (byte)' ') end++;
                if (end == at) break;
                if (!Number(text[at..end], out var value)) return new(QuestReplyTableStatus.InvalidNumber, null, message.Number);
                numbers.Add(value);
                at = end;
            }
            rows[message.Number] = numbers.ToArray();
        }
        return new(QuestReplyTableStatus.Read, new QuestReplyTable(new ReadOnlyDictionary<int, int[]>(rows)));
    }

    /// <summary>The quests that have an entry.</summary>
    public IReadOnlyCollection<int> Quests => _rows.Keys.ToArray();

    /// <summary>Field <paramref name="index"/> (0..22) of the quest's entry, or null for a quest with no entry or an entry shorter than that.</summary>
    public int? Field(int quest, int index) =>
        index is >= 0 and < Fields && _rows.TryGetValue(quest, out var row) && index < row.Length ? row[index] : null;

    private static bool Number(ReadOnlySpan<byte> text, out int value)
    {
        value = 0;
        var start = 0;
        var negative = false;
        if (text.Length > 0 && text[0] is (byte)'+' or (byte)'-')
        {
            negative = text[0] == (byte)'-';
            start = 1;
        }
        if (text.Length - start is < 1 or > 10) return false;
        long result = 0;
        for (var i = start; i < text.Length; i++)
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
