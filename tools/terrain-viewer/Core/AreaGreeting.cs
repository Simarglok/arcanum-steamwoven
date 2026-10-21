using Arcanum.Data;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Reputations;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Reputation;

namespace Arcanum.TerrainViewer.Core;

public enum AreaGreetingTextsStatus { Ready, StackFailed, FilesFailed, ReputationsFailed }

/// <summary>The texts every greeting of a module reads, or why they could not be read (the greeting then stays a frontier).</summary>
public sealed record AreaGreetingTexts(AreaGreetingTextsStatus Status, GeneratedDialogFiles? Files, IReadOnlyList<MesFile?> ReputationLines, string Detail)
{
    /// <summary>
    /// ARC-WORLD-058: the module's generated-dialog files (<see cref="GeneratedDialogFiles.Load"/>) and its four reputation
    /// greeting files (<see cref="ReputationTable.Load"/>, which reads them only after the reputation rules and names) through
    /// the content stack of <paramref name="plan"/>, the one the area's scripts are read from. Universal: any module.
    /// </summary>
    public static AreaGreetingTexts Load(ContentMountPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var opened = ContentStack.Open(plan);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaGreetingTextsStatus.StackFailed, null, [], opened.Status.ToString());
        using var stack = opened.Stack!;
        var files = GeneratedDialogFiles.Load(stack);
        if (files.Files is null) return new(AreaGreetingTextsStatus.FilesFailed, null, [], $"{files.Status} {files.Path} {files.Read} {files.Parse?.Status}");
        var reputations = ReputationTable.Load(stack);
        if (reputations.Table is null)
            return new(AreaGreetingTextsStatus.ReputationsFailed, null, [], $"{reputations.Status} {reputations.Path} {reputations.Read} {reputations.Parse?.Status}");
        return new(AreaGreetingTextsStatus.Ready, files.Files, reputations.Table.Greetings,
            $"{GeneratedDialogFiles.Count - files.Files.Absent.Count} generated-dialog files, {reputations.Table.Greetings.Count(file => file is not null)} reputation greeting files");
    }
}

/// <summary>
/// ARC-WORLD-058: the greeting seam of one AREA speaker over the game session. The NPC's facts are its stored fields
/// (<see cref="CombatFieldReader"/>: base stats gender 26, race 27 and intelligence 4 for the dumb test, social class); the
/// PC's are the conversation's PC facts; the category is the speaker's effective reaction (<see cref="ReactionRules.Category"/>)
/// and met before the session's. Of the states only "NPC down" is modelled (the session holds a death time for it); the
/// others (surrendered, summoned associates, the PC's spell flags and art code) read as not that state. The override dialog is
/// the DLG of the script at the speaker's SAP key 31 (<see cref="ScriptContent.Dialog"/>). Rolls are the session's
/// labelled rolls (<c>dialog.greeting.line</c>, <c>dialog.greeting.reputation</c>, <c>dialog.greeting.pick</c>) and the
/// rules the session's current dialog profile.
/// </summary>
public sealed class AreaGreeting : IDialogGreetingPort
{
    /// <summary>The NPC's override script point (the original reads element 31 of the scripts field, 0x418870).</summary>
    public const int OverrideScriptPoint = 31;

    private readonly GameSessionOwner _session;
    private readonly AreaSpeaker _speaker;
    private readonly AreaConversationState _state;
    private readonly DialogPcFacts _pc;
    private readonly DialogGreetingSources _sources;

    public AreaGreeting(GameSessionOwner session, AreaSpeaker speaker, AreaConversationState state, DialogPcFacts pc, AreaGreetingTexts texts,
        DlgDocument? overrides)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _pc = pc ?? throw new ArgumentNullException(nameof(pc));
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Files is null) throw new ArgumentException("The greeting texts did not load.", nameof(texts));
        _sources = new(texts.Files, texts.ReputationLines, overrides);
        OverrideScriptNumber = overrides is null ? null : OverrideScript(speaker);
    }

    /// <summary>The NPC's override dialog, or null without one.</summary>
    public DlgDocument? Overrides => _sources.Overrides;

    /// <summary>The script number of <see cref="Overrides"/> (its voices are numbered by it), or null.</summary>
    public int? OverrideScriptNumber { get; }

    /// <summary>The override script of a speaker: its SAP key 31 attachment's script number, or null without one.</summary>
    public static int? OverrideScript(AreaSpeaker speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        var scripts = speaker.Row.EffectiveFields.SingleOrDefault(field => field.Slot == 31);
        var element = scripts?.Elements.SingleOrDefault(candidate => candidate.Key == OverrideScriptPoint);
        return element is { Words.Count: 3 } && element.Words[2] is > 0 and <= 99999 ? (int)element.Words[2] : null;
    }

    public DialogRuleProfile Rules => _session.Rules.Dialog;

    public DialogGreetingFacts? Facts(DialogReactionQuery query)
    {
        var entity = AreaConversationState.EntityId(_speaker.Row);
        return FactsFor(CombatFieldReader.Read(_speaker.Row).Fields, _pc, _state.Reaction(_speaker.Index), _state.MetBefore(_speaker.Index),
            _session.QueryLifeRecord(entity)?.DeathTimeMs is not null);
    }

    /// <summary>The greeting facts of an NPC's stored fields (null when they are not an NPC's), the PC's facts, the NPC's effective
    /// reaction, met before and whether it is dead: base stats gender 26 (1 male), intelligence 4 (dumb at 4 or less, 0x45FC00),
    /// race 27, and the social class.</summary>
    public static DialogGreetingFacts? FactsFor(CombatFields? fields, DialogPcFacts pc, int reaction, bool metBefore, bool dead)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (fields is not { Critter: { } critter, Npc: { } npc } || critter.BaseStats.Count != CombatFieldReader.BaseStats) return null;
        var stats = critter.BaseStats;
        return new(stats[GenderStat] == (int)Arcanum.Simulation.Character.CharacterGender.Male, pc.Gender == DialogPcGender.Male,
            stats[IntelligenceStat] <= DumbIntelligence, npc.SocialClass, stats[RaceStat], pc.Race, (DialogReactionCategory)ReactionRules.Category(reaction),
            metBefore, dead ? DialogGreetingStates.NpcDown : DialogGreetingStates.None);
    }

    private const int IntelligenceStat = 4, GenderStat = 26, RaceStat = 27, DumbIntelligence = 4;

    public DialogGreetingSources Sources(DialogReactionQuery query) => _sources;

    public IReadOnlyList<int> ReputationCandidates(DialogReactionQuery query, DialogReactionCategory category)
    {
        var (origin, faction) = AreaConversationHost.Scope(_speaker.Row);
        return ReputationRules.GreetingCandidates(_session.HeldReputations(), _session.Reputations, origin, faction, (ReactionCategory)category);
    }

    public int? Roll(DialogGreetingRoll roll, int min, int max) => _session.Roll(roll switch
    {
        DialogGreetingRoll.Line => "dialog.greeting.line",
        DialogGreetingRoll.ReputationChance => "dialog.greeting.reputation",
        _ => "dialog.greeting.pick",
    }, min, max).Roll?.Value;
}
