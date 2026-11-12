using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;

namespace Arcanum.Scripting.Dialog;

/// <summary>The NPC's reaction category toward the PC, in the original's order (0x4C0FC0: love 0 .. hatred 6).</summary>
public enum DialogReactionCategory { Love, Amiable, Courteous, Neutral, Suspicious, Dislike, Hatred }

/// <summary>
/// The states the greeting tests before its reaction line (0x419260), each in the original's order. The host sets what it
/// models; a state it does not model reads as "not that state".
/// </summary>
[Flags]
public enum DialogGreetingStates
{
    None = 0,
    /// <summary>Slot 0: the NPC's hit points are 0 or below.</summary>
    NpcDown = 1 << 0,
    /// <summary>No slot: the NPC has surrendered (critter flag 0x4000).</summary>
    NpcSurrendered = 1 << 1,
    /// <summary>Slot 1: a summoned member of the PC's party that is no familiar or animal ("bad associates").</summary>
    BadAssociates = 1 << 2,
    /// <summary>Slot 2: the PC is invisible (spell flag 0x1).</summary>
    PcInvisible = 1 << 3,
    /// <summary>Slot 3: the PC has an elemental body (spell flags 0x3C).</summary>
    PcElementalBody = 1 << 4,
    /// <summary>Slot 4: the PC is polymorphed (spell flag 0x20000).</summary>
    PcPolymorphed = 1 << 5,
    /// <summary>Slot 6: the PC is shrunk (spell flag 0x80000).</summary>
    PcShrunk = 1 << 6,
    /// <summary>Slot 7: the code of the PC's art (0x504030) is 0.</summary>
    PcArtCodeZero = 1 << 7,
    /// <summary>Slot 8: that code is 7.</summary>
    PcArtCodeSeven = 1 << 8,
}

/// <summary>
/// What the greeting reads of the NPC and the PC. <paramref name="NpcDumb"/> is the original's test 0x45FC00 on the NPC
/// (intelligence 4 or less); <paramref name="NpcSocialClass"/> is NPC field 0x128; races and genders are stats 27 and 26.
/// <paramref name="NpcLedByPc"/> (ARC-SCRIPT-043) is whether the PC is the NPC's leader (0x45DE00), which chooses the barter
/// phrase of a Player <c>b:</c> option.
/// </summary>
public sealed record DialogGreetingFacts(bool NpcMale, bool PcMale, bool NpcDumb, int NpcSocialClass, int NpcRace, int PcRace,
    DialogReactionCategory Category, bool MetBefore, DialogGreetingStates States = DialogGreetingStates.None, bool NpcLedByPc = false);

/// <summary>The texts a greeting reads: the module's generated-dialog files, its four reputation greeting files (NPC gender to
/// PC gender: m2m, m2f, f2f, f2m; null when absent) and the NPC's own override dialog (the DLG of its script at SAP key 31, null
/// without one). <c>RumorLines</c> (ARC-SCRIPT-1304) are the four rumor files in the same order, for the tale of an <c>r:</c> reply; null reads as no text.</summary>
public sealed record DialogGreetingSources(GeneratedDialogFiles Files, IReadOnlyList<MesFile?> ReputationLines, DlgDocument? Overrides,
    IReadOnlyList<MesFile?>? RumorLines = null);

/// <summary>What a greeting roll decides (the host names the session roll by it).</summary>
public enum DialogGreetingRoll
{
    /// <summary>The entry of a generated line.</summary>
    Line,
    /// <summary>The 20 % chance of a reputation greeting (1..100).</summary>
    ReputationChance,
    /// <summary>Which candidate reputation greets.</summary>
    ReputationPick,
    /// <summary>ARC-SCRIPT-043: the entry of a generated Player option (a phrase).</summary>
    Phrase,
    /// <summary>ARC-SCRIPT-1304: which of the rumors an <c>r:</c> reply lists is told (the candidate's index).</summary>
    Rumor,
}

/// <summary>
/// Greeting seam (ARC-WORLD-058): the facts and texts of an NPC <c>g:</c> greeting, the reputation candidates for a reaction
/// category (0x4C1F80), the rolls and the dialog rule profile, read at each greeting. Calls are synchronous and serialized by
/// the host; a thrown exception fails the conversation. Null facts are a frontier. A roll returns a value in
/// <c>min..max</c> inclusive, or null when the host's random source refuses.
/// </summary>
public interface IDialogGreetingPort
{
    DialogGreetingFacts? Facts(DialogReactionQuery query);
    DialogGreetingSources Sources(DialogReactionQuery query);
    IReadOnlyList<int> ReputationCandidates(DialogReactionQuery query, DialogReactionCategory category);
    int? Roll(DialogGreetingRoll roll, int min, int max);
    DialogRuleProfile Rules { get; }
}

public enum DialogLineOriginKind
{
    /// <summary>An entry of a generated-dialog file (<see cref="DialogLineOrigin.File"/>, <see cref="DialogLineOrigin.Number"/>).</summary>
    Generated,
    /// <summary>The original's missing-line text for a number the file does not hold (rule GD-A1, Original setting).</summary>
    MissingLine,
    /// <summary>No line: the range holds no entry (Steamwoven), the file is absent, or slot 9 is off.</summary>
    Empty,
    /// <summary>A record of the NPC's override dialog (<see cref="DialogLineOrigin.Number"/> is its id).</summary>
    Override,
    /// <summary>A reputation greeting (<see cref="DialogLineOrigin.Number"/> is the reputation).</summary>
    Reputation,
    /// <summary>ARC-SCRIPT-1304: the tale of a rumor (<see cref="DialogLineOrigin.Number"/> is the rumor); no voice.</summary>
    Rumor,
}

/// <summary>Where a generated NPC line came from. The original gives generated and reputation lines no voice (speech id −1) and
/// an override line the voice of its own record.</summary>
public sealed record DialogLineOrigin(DialogLineOriginKind Kind, int Number, GeneratedDialogFile? File = null);

/// <summary>A slot of the greeting's slot text: unset (the generated line), off, a node to continue at, or a node to say as a
/// floating line that ends the conversation.</summary>
public enum DialogGreetingSlotKind { Default, Off, Jump, Float }

public readonly record struct DialogGreetingSlot(DialogGreetingSlotKind Kind, int Node);

internal enum GreetingOutcomeKind { Line, Jump, Float, Failed, Frontier }

internal sealed record GreetingOutcome(GreetingOutcomeKind Kind, string Text = "", DialogLineOrigin? Origin = null, bool Ends = false,
    int Node = 0, DialogConversationFailureKind Failure = default, DialogTextFailureKind TextFailure = DialogTextFailureKind.None,
    bool KeepsLine = false);

/// <summary>
/// ARC-WORLD-058: the original's NPC greeting (0x419260 with its generators 0x418480, 0x4185F0, 0x418780, the override line
/// 0x418870 and the reputation line 0x4C2100), over the facts and texts a <see cref="IDialogGreetingPort"/> supplies.
/// </summary>
internal static class DialogGreeting
{
    public const int SlotCount = 10;
    public const int ReputationChancePercent = 20;
    private const int ClassBlock = 50;

    /// <summary>
    /// The slot text after <c>g:</c>, read as the original reads it (0x419260): skip to the next digit and read the slot index;
    /// skip to the next space, then over spaces, to the value; a lowercase <c>fl</c> there makes the slot float the node read
    /// after it, an <c>f</c> without <c>l</c> ends the whole parse, anything else is read as a number: 0 (or no number) is off,
    /// any other a node to continue at; then go on after the next comma. The text ending before a value ends the parse. An
    /// index of 10 or more is ignored (the original writes past its slot arrays: no-crash rule, both settings).
    /// </summary>
    public static DialogGreetingSlot[] ParseSlots(string text)
    {
        var slots = new DialogGreetingSlot[SlotCount];
        var at = 0;
        while (true)
        {
            while (at < text.Length && !char.IsAsciiDigit(text[at])) at++;
            if (at == text.Length) break;
            var index = Number(text, ref at);
            while (at < text.Length && text[at] != ' ') at++;
            while (at < text.Length && text[at] == ' ') at++;
            if (at == text.Length) break;
            DialogGreetingSlot slot;
            if (text[at] == 'f')
            {
                if (at + 1 == text.Length || text[at + 1] != 'l') break;
                at += 2;
                slot = new(DialogGreetingSlotKind.Float, Number(text, ref at));
            }
            else
            {
                var value = Number(text, ref at);
                slot = value == 0 ? new(DialogGreetingSlotKind.Off, 0) : new(DialogGreetingSlotKind.Jump, value);
            }
            if (index < SlotCount) slots[index] = slot;
            while (at < text.Length && text[at] != ',') at++;
            if (at + 1 >= text.Length) break;
            at++;
        }
        return slots;
    }

    // atol from at: leading white space, an optional sign and digits, saturating at the 32-bit range; no digits is 0.
    private static int Number(string text, ref int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '\r' or '\v' or '\f') at++;
        var negative = false;
        if (at < text.Length && text[at] is '+' or '-') negative = text[at++] == '-';
        long value = 0;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            value = Math.Min(value * 10 + (text[at++] - '0'), 1L << 31);
        }
        value = negative ? -value : value;
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// The greeting of an NPC whose node text is <c>g:</c> followed by <paramref name="slots"/>' text (0x419260), in the
    /// original's order: NPC down (slot 0), surrendered (no slot), bad associates, invisible, elemental body, polymorphed
    /// (slots 1–4, which end the talk unless the NPC's social class is 2), shrunk, art code 0, art code 7 (slots 6–8; slot 5 is
    /// never read), slot 9, the reputation greeting, then the reaction category by met before. A slot whose state holds says
    /// its node instead when it names one; an off slot is passed over.
    /// </summary>
    [OriginalFunction(0x419260, OriginalFunctionScope.Partial)]
    public static GreetingOutcome Resolve(DialogGreetingSlot[] slots, DialogGreetingFacts facts, DialogGreetingSources sources,
        IDialogGreetingPort port, DialogReactionQuery query, DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes)
    {
        var context = new Context(facts, sources, port, names, encoding, maxTextBytes);
        var states = facts.States;
        bool Has(DialogGreetingStates state) => (states & state) != 0;
        GreetingOutcome? Slot(int index, bool condition, Func<GreetingOutcome> line, bool endsUnlessClassTwo = false)
        {
            if (!condition || slots[index].Kind == DialogGreetingSlotKind.Off) return null;
            var slot = slots[index];
            if (slot.Kind == DialogGreetingSlotKind.Jump) return new(GreetingOutcomeKind.Jump, Node: slot.Node);
            if (slot.Kind == DialogGreetingSlotKind.Float) return new(GreetingOutcomeKind.Float, Node: slot.Node);
            var outcome = line();
            return outcome.Kind == GreetingOutcomeKind.Line && endsUnlessClassTwo && facts.NpcSocialClass != 2 ? outcome with { Ends = true } : outcome;
        }

        var result = Slot(0, Has(DialogGreetingStates.NpcDown), () => NpcLine(context, 1500, 1599))
            ?? (Has(DialogGreetingStates.NpcSurrendered) ? NpcLine(context, 3000, 3099, 12029) : null)
            ?? Slot(1, Has(DialogGreetingStates.BadAssociates), () => ClassLine(context, 18000), true)
            ?? Slot(2, Has(DialogGreetingStates.PcInvisible), () => ClassLine(context, 22000), true)
            ?? Slot(3, Has(DialogGreetingStates.PcElementalBody), () => ClassLine(context, 19000), true)
            ?? Slot(4, Has(DialogGreetingStates.PcPolymorphed), () => ClassLine(context, 20000), true)
            ?? Slot(6, Has(DialogGreetingStates.PcShrunk), () => RaceLine(context, 6000))
            ?? Slot(7, Has(DialogGreetingStates.PcArtCodeZero), () => ClassLine(context, 16000))
            ?? Slot(8, Has(DialogGreetingStates.PcArtCodeSeven), () => ClassLine(context, 17000));
        if (result is not null) return result;
        switch (slots[9].Kind)
        {
            // Slot 9 off writes no line: the NPC line keeps what it held (the previous node's line; empty at the start).
            case DialogGreetingSlotKind.Off: return new(GreetingOutcomeKind.Line, Origin: new(DialogLineOriginKind.Empty, 9), KeepsLine: true);
            case DialogGreetingSlotKind.Jump: return new(GreetingOutcomeKind.Jump, Node: slots[9].Node);
            case DialogGreetingSlotKind.Float: return new(GreetingOutcomeKind.Float, Node: slots[9].Node);
        }
        var reputation = ReputationLine(context, query);
        if (reputation is not null) return reputation;
        return (facts.Category, facts.MetBefore) switch
        {
            (DialogReactionCategory.Love, true) => ClassLine(context, 11000),
            (DialogReactionCategory.Love, false) => ClassLine(context, 8000),
            (DialogReactionCategory.Amiable or DialogReactionCategory.Courteous, true) => ClassLine(context, 12000),
            (DialogReactionCategory.Amiable or DialogReactionCategory.Courteous, false) => ClassLine(context, 9000),
            (DialogReactionCategory.Neutral, true) => ClassLine(context, 13000),
            (DialogReactionCategory.Neutral, false) => ClassLine(context, 10000),
            (DialogReactionCategory.Suspicious or DialogReactionCategory.Dislike, true) => RaceLine(context, 4000),
            (DialogReactionCategory.Suspicious or DialogReactionCategory.Dislike, false) => RaceLine(context, 2000),
            (DialogReactionCategory.Hatred, true) => RaceLine(context, 5000),
            _ => RaceLine(context, 3000),
        };
    }

    private sealed record Context(DialogGreetingFacts Facts, DialogGreetingSources Sources, IDialogGreetingPort Port, DialogSpeakerNames Names,
        DialogTextEncoding Encoding, int MaxTextBytes);

    /// <summary>A line of the class group (0x418480): override 9999 + group / 1000; the dumb file from the group, else the class
    /// file from the group + 50 × the NPC's social class; 50 entries.</summary>
    [OriginalFunction(0x418480, OriginalFunctionScope.Partial)]
    private static GreetingOutcome ClassLine(Context context, int group)
    {
        var facts = context.Facts;
        var first = facts.NpcDumb ? group : group + ClassBlock * facts.NpcSocialClass;
        return OverrideLine(context, 9999 + group / 1000)
            ?? Pick(context, GeneratedDialogFiles.NpcLineFile(GeneratedLineGroup.Class, facts.NpcMale, facts.PcMale, facts.NpcDumb), first, first + ClassBlock - 1);
    }

    /// <summary>A line of the race group (0x4185F0): override 10999 + group / 1000; the dumb file from the group, else the race
    /// file from the group + 50 × (0 when the PC's race is the NPC's, else the PC's race + 1); 50 entries.</summary>
    [OriginalFunction(0x4185F0, OriginalFunctionScope.Partial)]
    private static GreetingOutcome RaceLine(Context context, int group)
    {
        var facts = context.Facts;
        var offset = facts.PcRace == facts.NpcRace ? 0 : facts.PcRace + 1;
        var first = facts.NpcDumb ? group : group + ClassBlock * offset;
        return OverrideLine(context, 10999 + group / 1000)
            ?? Pick(context, GeneratedDialogFiles.NpcLineFile(GeneratedLineGroup.Race, facts.NpcMale, facts.PcMale, facts.NpcDumb), first, first + ClassBlock - 1);
    }

    /// <summary>A plain NPC line (0x418780): override 11999 + first / 100 (the surrendered line's caller 0x413EA0 uses 12029);
    /// the NPC file, no dumb variant, the range as given.</summary>
    [OriginalFunction(0x418780, OriginalFunctionScope.Partial)]
    private static GreetingOutcome NpcLine(Context context, int first, int last, int? overrideLine = null) =>
        OverrideLine(context, overrideLine ?? 11999 + first / 100)
        ?? Pick(context, GeneratedDialogFiles.NpcLineFile(GeneratedLineGroup.Npc, context.Facts.NpcMale, context.Facts.PcMale, false), first, last);

    /// <summary>The override line (0x418870): the NPC's override dialog's record of that number, its male-PC text or else its
    /// alternate text, copied raw (no name substitution); null without the dialog or the record.</summary>
    [OriginalFunction(0x418870, OriginalFunctionScope.Partial)]
    private static GreetingOutcome? OverrideLine(Context context, int number)
    {
        if (context.Sources.Overrides is not { } overrides || !overrides.TryGetRecord(number, out var record) || record is null) return null;
        return Text(context, context.Facts.PcMale ? record.Text.Bytes : record.AlternateText.Bytes, new(DialogLineOriginKind.Override, number), substitute: false);
    }

    // The line pick of the three generators (GD5, GD-A1).
    private static GreetingOutcome Pick(Context context, GeneratedDialogFile file, int first, int last, DialogGreetingRoll roll = DialogGreetingRoll.Line)
    {
        var files = context.Sources.Files;
        var mes = files.File(file);
        if (mes is null) return new(GreetingOutcomeKind.Line, Origin: new(DialogLineOriginKind.Empty, first, file));
        int number;
        if (context.Port.Rules.GeneratedLinePickSpansGaps)
        {
            // Original: the first number plus random(0, count − 1); with no first entry that is random(0, −1).
            var count = files.RunLength(file, first, last);
            if (Roll(context, roll, Math.Min(0, count - 1), Math.Max(0, count - 1)) is not { } offset) return RollFailed();
            number = first + offset;
        }
        else
        {
            var stored = mes.Messages.Where(message => message.Number >= first && message.Number <= last).ToArray();
            if (stored.Length == 0) return new(GreetingOutcomeKind.Line, Origin: new(DialogLineOriginKind.Empty, first, file));
            if (Roll(context, roll, 0, stored.Length - 1) is not { } index) return RollFailed();
            number = stored[index].Number;
        }
        var line = files.Line(file, number);
        if (line.Message is null)
            return new(GreetingOutcomeKind.Line, MissingLine(number, file), new(DialogLineOriginKind.MissingLine, number, file));
        return Text(context, line.Message.Bytes.ToArray(), new(DialogLineOriginKind.Generated, number, file), substitute: true);
    }

    /// <summary>
    /// ARC-SCRIPT-043: the phrase of a generated Player option whose whole text is a marker (0x416C10), or of the farewell
    /// offered when no reply qualifies (0x414E60: 400..499), picked like a greeting line; null for a letter without a phrase
    /// here (a prefix marker, or a letter the original shows as literal text). <paramref name="letter"/> is the lower-case
    /// marker letter, or null for the farewell; <paramref name="pcDumb"/> is the original's dumb test of the PC (0x45FC00).
    /// </summary>
    [OriginalFunction(0x416C10, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x419190, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x418460, OriginalFunctionScope.Partial)]
    public static GreetingOutcome? Phrase(char? letter, bool pcDumb, DialogGreetingFacts facts, DialogGreetingSources sources, IDialogGreetingPort port,
        DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes, int storyState = 0)
    {
        var context = new Context(facts, sources, port, names, encoding, maxTextBytes);
        return letter switch
        {
            null or 'e' => PcLine(context, pcDumb, 400, 499),
            'a' => PcClassLine(context, pcDumb, 1000),
            'c' => PcLine(context, pcDumb, StoryPcLine + storyState, StoryPcLine + storyState),
            'b' => facts.NpcLedByPc ? PcLine(context, pcDumb, 1600, 1699) : PcLine(context, pcDumb, 300, 399),
            'f' => PcLine(context, pcDumb, 800, 899),
            'h' => PcLine(context, pcDumb, 700, 799),
            'i' => PcClassLine(context, pcDumb, 3000),
            'k' => PcLine(context, pcDumb, 1500, 1599),
            'l' => PcLine(context, pcDumb, 2300, 2399),
            'n' => PcLine(context, pcDumb, 100, 199),
            'p' => PcLine(context, pcDumb, 1900, 1999),
            's' => PcLine(context, pcDumb, 200, 299),
            't' => PcLine(context, pcDumb, 500, 599),
            'r' => PcClassLine(context, pcDumb, 2000),
            'w' => PcLine(context, pcDumb, 1800, 1899),
            'y' => PcLine(context, pcDumb, 1, 99),
            _ => null,
        };
    }

    /// <summary>
    /// ARC-SCRIPT-1150: a line of the NPC's own talk inside the trainer's skill menu (ledger DTR3 to DTR8): the class group
    /// <paramref name="group"/> (1000, 2000, 3000, 4000, 5000, 6000), as the greeting reads it, with the override line 9999 + group / 1000.
    /// </summary>
    public static GreetingOutcome NpcClassGroup(int group, DialogGreetingFacts facts, DialogGreetingSources sources, IDialogGreetingPort port,
        DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes) =>
        ClassLine(new Context(facts, sources, port, names, encoding, maxTextBytes), group);

    /// <summary>
    /// ARC-SCRIPT-1304 (ledger RMR7): the text of the rumor the NPC tells: the file by the NPC's gender then the PC's (the same order as the
    /// reputation greetings), message 20 × rumor + the NPC's social class + 1, else 20 × rumor + 1, then the name substitution of the dialog text. A rumor without a text, or without its file, is an empty line (origin <see cref="DialogLineOriginKind.Empty"/>).
    /// </summary>
    public static GreetingOutcome RumorTale(int rumor, DialogGreetingFacts facts, DialogGreetingSources sources, IDialogGreetingPort port,
        DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes)
    {
        var index = facts.NpcMale ? (facts.PcMale ? 0 : 1) : (facts.PcMale ? 3 : 2);
        var file = sources.RumorLines is { } lines && index < lines.Count ? lines[index] : null;
        var message = file?.Find(rumor * RumorStride + facts.NpcSocialClass + 1) ?? file?.Find(rumor * RumorStride + 1);
        var empty = new GreetingOutcome(GreetingOutcomeKind.Line, string.Empty, new(DialogLineOriginKind.Empty, rumor));
        if (message is null || message.Bytes.Length == 0) return empty;
        return Text(new Context(facts, sources, port, names, encoding, maxTextBytes), message.Bytes.ToArray(), new(DialogLineOriginKind.Rumor, rumor), substitute: true);
    }

    private const int RumorStride = 20;

    /// <summary>The PC's line of a <c>c:</c> reply is the single line this number plus the story state (0x418460).</summary>
    public const int StoryPcLine = 1700;

    /// <summary>The story line's message is the NPC's race plus this many per story state (0x41A440).</summary>
    public const int StoryStateStride = 100;

    /// <summary>
    /// ARC-SCRIPT-1321 (ledger STY1–STY3): the NPC's story line (0x41A440): the NPC's own override record 10000 + the story state when it has one,
    /// else the entry numbered the NPC's race (stat 27) + 100 × the story state of the story file by the NPC's gender then the PC's, fetched by number
    /// (no draw) and name-substituted. An entry or file the module lacks reads like a missing generated line (the missing-line text, or an empty line without the file).
    /// </summary>
    [OriginalFunction(0x41A440, OriginalFunctionScope.Partial)]
    public static GreetingOutcome StoryTale(int storyState, DialogGreetingFacts facts, DialogGreetingSources sources, IDialogGreetingPort port,
        DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes)
    {
        var context = new Context(facts, sources, port, names, encoding, maxTextBytes);
        if (OverrideLine(context, 10000 + storyState) is { } overridden) return overridden;
        var number = facts.NpcRace + StoryStateStride * storyState;
        var file = GeneratedDialogFiles.StoryLineFile(facts.NpcMale, facts.PcMale);
        if (sources.Files.File(file) is null) return new(GreetingOutcomeKind.Line, Origin: new(DialogLineOriginKind.Empty, number, file));
        var line = sources.Files.Line(file, number);
        if (line.Message is null) return new(GreetingOutcomeKind.Line, MissingLine(number, file), new(DialogLineOriginKind.MissingLine, number, file));
        return Text(context, line.Message.Bytes.ToArray(), new(DialogLineOriginKind.Generated, number, file), substitute: true);
    }

    /// <summary>ARC-SCRIPT-1150: a PC line of the range <paramref name="first"/>..<paramref name="last"/> (the trainer talk's answers).</summary>
    public static GreetingOutcome PcRange(int first, int last, bool pcDumb, DialogGreetingFacts facts, DialogGreetingSources sources,
        IDialogGreetingPort port, DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes) =>
        PcLine(new Context(facts, sources, port, names, encoding, maxTextBytes), pcDumb, first, last);

    /// <summary>ARC-SCRIPT-1150: a PC line of the class group (the trained notice's answer).</summary>
    public static GreetingOutcome PcClassGroup(int group, bool pcDumb, DialogGreetingFacts facts, DialogGreetingSources sources,
        IDialogGreetingPort port, DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes) =>
        PcClassLine(new Context(facts, sources, port, names, encoding, maxTextBytes), pcDumb, group);

    /// <summary>A PC line (0x4182D0): the PC file by the NPC's gender (the dumb one for a dumb PC), the range as given.</summary>
    [OriginalFunction(0x4182D0, OriginalFunctionScope.Partial)]
    private static GreetingOutcome PcLine(Context context, bool pcDumb, int first, int last) =>
        Pick(context, GeneratedDialogFiles.PcLineFile(false, context.Facts.NpcMale, pcDumb), first, last, DialogGreetingRoll.Phrase);

    /// <summary>A PC line of the class group (0x418390): the PC class file by the NPC's gender (the dumb one for a dumb PC), from
    /// the group + 50 × the NPC's social class whether or not the PC is dumb; 50 entries.</summary>
    [OriginalFunction(0x418390, OriginalFunctionScope.Partial)]
    private static GreetingOutcome PcClassLine(Context context, bool pcDumb, int group)
    {
        var first = group + ClassBlock * context.Facts.NpcSocialClass;
        return Pick(context, GeneratedDialogFiles.PcLineFile(true, context.Facts.NpcMale, pcDumb), first, first + ClassBlock - 1, DialogGreetingRoll.Phrase);
    }

    /// <summary>
    /// The reputation greeting (0x4C2100 after the pick of 0x4C1F80): a 1..100 roll of at most 20, then one candidate picked
    /// uniformly; its line is the reputation's entry of the file for the NPC's gender then the PC's, raw. Null (the reaction line
    /// follows) when the roll is above 20, no reputation fits, or the entry is missing or empty.
    /// </summary>
    [OriginalFunction(0x4C2100, OriginalFunctionScope.Partial)]
    private static GreetingOutcome? ReputationLine(Context context, DialogReactionQuery query)
    {
        var facts = context.Facts;
        if (Roll(context, DialogGreetingRoll.ReputationChance, 1, 100) is not { } chance) return RollFailed();
        if (chance > ReputationChancePercent) return null;
        var candidates = context.Port.ReputationCandidates(query, facts.Category);
        if (candidates is null) return new(GreetingOutcomeKind.Failed, Failure: DialogConversationFailureKind.InvalidQueryResult);
        if (candidates.Count == 0) return null;
        if (Roll(context, DialogGreetingRoll.ReputationPick, 0, candidates.Count - 1) is not { } pick) return RollFailed();
        var reputation = candidates[pick];
        var index = facts.NpcMale ? (facts.PcMale ? 0 : 1) : (facts.PcMale ? 3 : 2);
        var lines = context.Sources.ReputationLines;
        var entry = index < lines.Count ? lines[index]?.Find(reputation) : null;
        if (entry is null || entry.Bytes.Length == 0) return null;
        return Text(context, entry.Bytes.ToArray(), new(DialogLineOriginKind.Reputation, reputation), substitute: false);
    }

    // A roll outside its bounds is a broken port: the caller fails the conversation.
    private static int? Roll(Context context, DialogGreetingRoll roll, int min, int max)
    {
        var value = context.Port.Roll(roll, min, max);
        return value is { } v && (v < min || v > max) ? throw new InvalidGreetingRoll() : value;
    }

    private static GreetingOutcome Text(Context context, IReadOnlyList<byte> bytes, DialogLineOrigin origin, bool substitute)
    {
        var failure = DialogText.Decode(bytes, context.Encoding, context.MaxTextBytes, out var text);
        if (failure != DialogTextFailureKind.None)
            return new(GreetingOutcomeKind.Failed, Failure: DialogConversationFailureKind.InvalidText, TextFailure: failure);
        return new(GreetingOutcomeKind.Line, substitute ? DialogText.SubstituteGenerated(text, context.Names) : text, origin);
    }

    private static GreetingOutcome RollFailed() => new(GreetingOutcomeKind.Failed, Failure: DialogConversationFailureKind.InvalidQueryResult);

    /// <summary>The line for a number its file does not hold: the project's wording of the original's error message (0x4D43A0),
    /// with the same number and path.</summary>
    public static string MissingLine(int number, GeneratedDialogFile file) =>
        $"[missing generated line {number} in {GeneratedDialogFiles.PathOf(file).Replace('/', '\\')}]";

    internal sealed class InvalidGreetingRoll : Exception;
}
