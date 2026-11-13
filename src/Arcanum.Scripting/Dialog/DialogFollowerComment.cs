namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-AI-1527 (FR-DIALOG-003, VB4-1, VB4-2, VB4-4): the text of a follower's comment line, which the original builds in three twin functions, the combat taunt 0x413F50 (kinds 0 and 1: 2400–2499, 2500–2599),
/// the upset line 0x414370 (4800–5199) and the near-death line 0x414490 (5500–5599). Each asks the can-speak test (the caller's: <see cref="Arcanum.Simulation.Reaction.CanSpeakRules"/>), then reads the
/// plain NPC line of its range through 0x418780 (the speaker's own override line 11999 + range / 100 first, else a random entry of the gender pair's <c>gd_npc_*</c> file) and expands the name tokens
/// (0x416B00). The voice id is the override record's, else none; no voice is played here. NOT READY: the can-speak test's tail.
/// </summary>
public static class DialogFollowerComment
{
    /// <summary>The comment line <paramref name="first"/>..<paramref name="last"/> the speaker <paramref name="query"/> names says to the PC, over the greeting port's facts, texts and rolls.</summary>
    [OriginalFunction(0x414490, OriginalFunctionScope.Partial)]
    public static DialogGenericGreetingResult Say(IDialogGreetingPort port, DialogReactionQuery query, int first, int last, DialogSpeakerNames names, DialogTextEncoding encoding, int maxTextBytes)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (port.Facts(query) is not { } facts) return new(DialogGenericGreetingStatus.FactsUnknown);
        GreetingOutcome outcome;
        try { outcome = DialogGreeting.Comment(facts, port.Sources(query), port, names, encoding, maxTextBytes, first, last); }
        catch (DialogGreeting.InvalidGreetingRoll) { return new(DialogGenericGreetingStatus.Failed); }
        if (outcome.Kind == GreetingOutcomeKind.Failed) return new(DialogGenericGreetingStatus.Failed);
        if (outcome.Kind != GreetingOutcomeKind.Line || outcome.Origin is not { } origin || origin.Kind == DialogLineOriginKind.Empty || outcome.Text.Length == 0)
            return new(DialogGenericGreetingStatus.NoLine);
        return new(DialogGenericGreetingStatus.Said, outcome.Text, origin);
    }
}
