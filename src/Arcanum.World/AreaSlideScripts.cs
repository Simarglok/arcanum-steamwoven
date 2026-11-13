using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>The three slide shows of the original (ledger SS1): the show type picks the script that queues the slides (table 0x5A5700: 997, 998, 999).</summary>
public enum AreaSlideKind
{
    /// <summary>Type 0, the end of the game (script 997, the ending scripts); asked for by the script action 132.</summary>
    EndGame = 0,
    /// <summary>Type 1, the hero's death (script 998; slide 0 when nothing was queued).</summary>
    Death = 1,
    /// <summary>Type 2, the credits (script 999) the main menu's Credits button opens.</summary>
    Credits = 2,
}

/// <summary>What a slide script queued: the slide numbers in order (at most <see cref="AreaSlideScripts.MaxQueued"/>) and how the run ended.</summary>
/// <param name="Stop">Why the run ended; null when the script is missing or was refused before it ran (see <paramref name="Detail"/>).</param>
public sealed record AreaSlideScriptResult(IReadOnlyList<int> Slides, ScrStop? Stop, string Detail)
{
    /// <summary>The script queued nothing and says why.</summary>
    public static AreaSlideScriptResult None(string detail) => new([], null, detail);
}

/// <summary>
/// ARC-RENDER-1171 (ledger SS1, SS2): the engine runs a slide script before each slide show. The original builds an event holding nothing but the script
/// number of the show's type (0x569720, table 0x5A5700) and hands it to the script dispatcher (0x4449B0); the script's queue slide actions (131) push
/// slide numbers onto the show's queue (0x569AA0, bound 100). The run starts at line 0 with the script file's own flags and counters and no
/// attachee or triggerer; the player focus is the hero. The ending scripts 2921–2928 are reached through 997's call script. Godot-free.
/// </summary>
public static class AreaSlideScripts
{
    /// <summary>The original's slide queue holds up to 100 slides; a queue slide past it does nothing (0x569AA0).</summary>
    public const int MaxQueued = 100;

    /// <summary>The first slide script number; a show type adds its own number.</summary>
    public const int FirstScript = 997;

    public static int ScriptOf(AreaSlideKind kind) => FirstScript + (int)kind;

    /// <summary>Runs the slide script of <paramref name="kind"/> against the session (no session: nothing is set, no quest started, the hero is just there).
    /// A missing or refused script queues nothing, which is what the original's dispatcher does with a script it cannot open.</summary>
    public static AreaSlideScriptResult Run(AreaSlideKind kind, ScriptContent? content, GameSessionOwner? session)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var script = ScriptOf(kind);
        if (content is null) return AreaSlideScriptResult.None("no script content");
        var host = new Host(content, session);
        var result = ScrDispatcher.RunCall(new ScrCallEvent(script, 0, null, null), host, new ScrRunOptions { Admission = ScrAdmissionSet.SlideScripts });
        if (result.Stop is ScrStop.MissingScript or ScrStop.UnreadableScript) return AreaSlideScriptResult.None($"script {script} {result.Stop}");
        if (result.Outcome == ScrOutcome.Refused) return AreaSlideScriptResult.None($"script {script} refused: {result.Refusal?.Code} on line {result.Refusal?.Line}");
        var slides = new List<int>();
        foreach (var request in result.Effects.Slides)
            if (request.Kind == ScrSlideKind.Queue && slides.Count < MaxQueued) slides.Add(request.Slide);
        return new(slides, result.Stop, $"script {script} {result.Stop}");
    }

    /// <summary>The slide scripts' view of the game: the global flags and the hero's quest states, nothing else (no object of the world is asked).</summary>
    private sealed class Host(ScriptContent content, GameSessionOwner? session) : IScrHost, IScrObjectQueries, IScrStoreQueries
    {
        private static readonly ScrObjectRef Hero = new(1, AreaScriptHost.PcCode);

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => null;
        public ScrScriptLookup Script(int number) => AreaScriptHost.Lookup(content, number);
        public bool GlobalFlag(int index)
        {
            if (session is null) return false;
            var flags = session.Flags;
            var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
            return read.Status == GlobalFlagReadStatus.Known && read.Value is { } value && value;
        }

        public int StoryState() => session?.QueryStoryState() ?? 0;
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => Hero;
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter == Hero;

        // SG1 as the use host answers it: the hero's own entry (botched reads as 6); a session without a quest log reads 0.
        public int QuestState(ScrObjectRef obj, int quest)
        {
            if (session is null || !SessionStoryLimits.IsQuest(quest)) return 0;
            if (!IsPc(obj)) return (int)session.QueryGlobalQuest(quest);
            return session.QueryQuest(quest) is { } entry ? entry.Botched ? (int)SessionQuestState.Botched : (int)entry.State : 0;
        }

        // GV1–GV4: the session's global variables and the hero's variables and flags; a session without a hero reads 0 for the PC's.
        public int GlobalVariable(int index) => session?.QueryGlobalVariable(index) ?? 0;
        public int PcVariable(ScrObjectRef pc, int index) => session is not null && SessionScriptLimits.IsHero(pc) ? session.QueryPcVariable(index) : 0;
        public bool PcFlag(ScrObjectRef pc, int index) => session is not null && SessionScriptLimits.IsHero(pc) && session.QueryPcFlag(index);

        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => IsPc(obj) ? 15 : null;
    }
}
