namespace Arcanum.Scripting.Dialog;

/// <summary>How a generic greeting ended.</summary>
public enum DialogGenericGreetingStatus
{
    /// <summary>A line to say (<see cref="DialogGenericGreetingResult.Text"/>).</summary>
    Said,
    /// <summary>The greeting picked no line: the range holds no entry, the file is absent (Steamwoven setting) or the roll refused to say one.</summary>
    NoLine,
    /// <summary>The port knows no facts for the speaker (a frontier: nothing is said).</summary>
    FactsUnknown,
    /// <summary>A roll was refused or the text did not decode; nothing is said.</summary>
    Failed,
}

/// <param name="Origin">Where the line came from (an override record, a generated entry, a reputation entry); null when nothing is said.</param>
public sealed record DialogGenericGreetingResult(DialogGenericGreetingStatus Status, string Text = "", DialogLineOrigin? Origin = null);

/// <summary>
/// ARC-DEV-324 (B24, GT219): the generic greeting a talk request shows when the NPC's dialog point ran default (0x57CC70 calls 0x5681C0, which builds
/// the text with 0x4132A0 and shows it as a floating line over the NPC; no dialog window opens). The text is the greeting builder 0x419260 run over
/// the slot text a follower's talk passes (<see cref="DefaultSlotText"/>: slots 1..8 off, 0 and 9 untouched; none for any other NPC, so every slot is on), so a corpse says an NPC line 1500..1599 (the
/// "dead" greeting, with the NPC's own override line first), a surrendered NPC 3000..3099, any other NPC the reputation greeting or its reaction line.
/// </summary>
public static class DialogGenericGreeting
{
    /// <summary>The slot text at 0x5A0A60 that 0x4132A0 passes when the NPC's leader (0x45DDA0, up its leader chain) is the talker, that is when the NPC follows the PC
    /// (<see cref="DialogGreetingFacts.NpcLedByPc"/>); for any other NPC it passes none, which leaves every slot on.</summary>
    public const string DefaultSlotText = "1 0, 2 0, 3 0, 4 0, 5 0, 6 0, 7 0, 8 0";

    /// <summary>
    /// The generic greeting of the speaker <paramref name="query"/> names, over the greeting port's facts, texts and rolls. Reading the speaker's states
    /// is the port's: a state it does not model reads as "not that state".
    /// </summary>
    [OriginalFunction(0x4132A0, OriginalFunctionScope.Partial)]
    public static DialogGenericGreetingResult Say(IDialogGreetingPort port, DialogReactionQuery query, DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes)
    {
        ArgumentNullException.ThrowIfNull(port);
        var facts = port.Facts(query);
        if (facts is null) return new(DialogGenericGreetingStatus.FactsUnknown);
        GreetingOutcome outcome;
        var slotText = facts.NpcLedByPc ? DefaultSlotText : string.Empty;
        try { outcome = DialogGreeting.Resolve(DialogGreeting.ParseSlots(slotText), facts, port.Sources(query), port, query, names, encoding, maxTextBytes); }
        catch (DialogGreeting.InvalidGreetingRoll) { return new(DialogGenericGreetingStatus.Failed); }
        // The default slot text names no node, so a line is the only kind of answer; any other is a refusal.
        if (outcome.Kind == GreetingOutcomeKind.Failed) return new(DialogGenericGreetingStatus.Failed);
        if (outcome.Kind != GreetingOutcomeKind.Line || outcome.Origin is not { } origin || origin.Kind == DialogLineOriginKind.Empty || outcome.Text.Length == 0)
            return new(DialogGenericGreetingStatus.NoLine);
        return new(DialogGenericGreetingStatus.Said, outcome.Text, origin);
    }
}
