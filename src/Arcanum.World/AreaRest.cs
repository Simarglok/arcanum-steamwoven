using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-1160 (E3-a; ledger SWB1–SWB13, TL27): what the sleep window asks about the town map of the hero's sector before it opens
/// (0x57B180 with no bed, SWB3): there is none (resting on the ground), there is one whose <c>townmap.mes</c> entry carries the wait marker (only waiting is
/// allowed, no healing), or there is one without it (the rest is refused).
/// </summary>
public enum AreaRestTownMap { None = 0, WaitAllowed = 1, WaitRefused = 2 }

/// <summary>
/// ARC-WORLD-1160: the facts the open decision of the sleep window reads (SWB2–SWB4): whether the hero is dead, whether the window is already open (the key toggles it),
/// whether the hero is unconscious, the sector's town map, and the combat-mode exit gate (<see cref="HeroInCombatMode"/>; <see cref="MayLeaveCombatMode"/> is the answer of
/// 0x4B3D90, asked only when the hero is in combat mode). <see cref="Bed"/> (ARC-WORLD-1162, SWB3): the call carries a bed, which skips the town-map test (a bed is always usable, also in a town) and never waits.
/// </summary>
public readonly record struct AreaRestFacts(bool HeroDead, bool WindowOpen, bool HeroUnconscious, AreaRestTownMap TownMap, bool HeroInCombatMode, bool MayLeaveCombatMode, bool Bed = false);

/// <summary>What the open decision answered (SWB2–SWB4).</summary>
public enum AreaRestOpenOutcome
{
    /// <summary>The window opens (<see cref="AreaRestOpening.Wait"/> picks its kind; <see cref="AreaRestOpening.LeavesCombatMode"/> says the hero left combat mode first).</summary>
    Open,
    /// <summary>Nothing at all: the hero is dead (a network client does nothing either, which the viewer does not have).</summary>
    Nothing,
    /// <summary>The window was open: it is closed (the key toggles).</summary>
    Close,
    /// <summary>Refused with a message (<see cref="AreaRestOpening.Message"/>).</summary>
    Refused,
}

/// <summary>The decision of 0x57B180: the outcome, the kind of window, the refusal's message number (of <c>sleepUI.mes</c>) and whether the hero leaves combat mode before the window opens.</summary>
public sealed record AreaRestOpening(AreaRestOpenOutcome Outcome, bool Wait, int? Message, bool LeavesCombatMode)
{
    public static AreaRestOpening Nothing { get; } = new(AreaRestOpenOutcome.Nothing, false, null, false);
    public static AreaRestOpening Close { get; } = new(AreaRestOpenOutcome.Close, false, null, false);
    internal static AreaRestOpening Refuse(int message) => new(AreaRestOpenOutcome.Refused, false, message, false);
}

/// <summary>
/// ARC-WORLD-1160 (E3-a): the numbers of the sleep window's message file <c>mes\sleepUI.mes</c> that the rules name (ledger SWB2–SWB7): the eight button labels 0..7 (the seven of the
/// wait window 12..18, <see cref="WaitLabels"/>), and the messages of the refusals. Entries are named by number only; their texts are the player's file's, the project's own English text
/// stands in when it does not read (<see cref="AreaRestTexts"/>).
/// </summary>
public static class AreaRestMessages
{
    public const int ScriptRefused = 8, CombatRefused = 9, BedOccupied = 10, HeroUnconscious = 11, WaitLabels = 12, WaitRefused = 20;
}

/// <summary>
/// ARC-WORLD-1160 (E3-a): the rules of the sleep window (0x57B180, 0x57B710, 0x57B7F0 and the helpers around them, ledger SWB1–SWB13, TL27), Godot-free and pure: the open decision, the
/// button table and the step's end tests. The window itself, the fade and the clocks are the viewer's; the run that spends the steps is <see cref="AreaRestRun"/>.
/// </summary>
public static class AreaRestRules
{
    /// <summary>The length of each button in hours (SWB7, table 0x5CB340): 1, 2, 4, 8 and 24 hours, then the codes −1 (until 07:00), −2 (until 20:00) and −3 (until healed).</summary>
    private static readonly int[] ButtonCodes = [1, 2, 4, 8, 24, UntilMorning, UntilEvening, UntilHealed];

    /// <summary>Hour code −1: until the hour is 7 after a step.</summary>
    public const int UntilMorning = -1;
    /// <summary>Hour code −2: until the hour is 20 after a step.</summary>
    public const int UntilEvening = -2;
    /// <summary>Hour code −3: until the sleeper has no hit-point damage (fatigue damage is not looked at).</summary>
    public const int UntilHealed = -3;

    /// <summary>The morning hour of <see cref="UntilMorning"/> and the evening hour of <see cref="UntilEvening"/>.</summary>
    public const int MorningHour = 7, EveningHour = 20;

    /// <summary>The game time one step jumps (SWB9): one hour.</summary>
    public const long StepGameMs = 3_600_000;

    /// <summary>Eight buttons in the sleep window (resting on the ground), seven in the wait window: the until-healed button is dropped (SWB7).</summary>
    public static int ButtonCount(bool wait) => wait ? 7 : 8;

    /// <summary>The hour code of button <paramref name="button"/> (0-based); null for a button the window does not have.</summary>
    public static int? CodeOf(int button, bool wait) => button >= 0 && button < ButtonCount(wait) ? ButtonCodes[button] : null;

    /// <summary>The message number of a button's label (SWB7): 0..7 in the sleep window, 12 and up in the wait window.</summary>
    public static int LabelMessage(int button, bool wait) => wait ? AreaRestMessages.WaitLabels + button : button;

    /// <summary>
    /// The open decision (0x57B180, SWB2–SWB4): a dead hero does nothing; an open window is closed; an unconscious hero is refused with its message; the town
    /// map gives the kind (none: resting; a wait marker: waiting; neither: refused with its message), a call with a bed (<see cref="AreaRestFacts.Bed"/>) skips that test; a hero in combat mode is asked the exit gate
    /// and, when it refuses, gets its message, otherwise leaves combat mode at once. The open window of a call that is refused is left as it was. A bed's script and seat come after it (<see cref="BedScriptRefusal"/>).
    /// </summary>
    [OriginalFunction(0x57B180, OriginalFunctionScope.Partial)]
    public static AreaRestOpening Decide(AreaRestFacts facts)
    {
        if (facts.HeroDead) return AreaRestOpening.Nothing;
        if (facts.WindowOpen) return AreaRestOpening.Close;
        if (facts.HeroUnconscious) return AreaRestOpening.Refuse(AreaRestMessages.HeroUnconscious);
        // Only a call without a bed tests the town map (SWB3): a bed is always usable, also in a town, and heals as kind 2.
        var town = facts.Bed ? AreaRestTownMap.None : facts.TownMap;
        if (town == AreaRestTownMap.WaitRefused) return AreaRestOpening.Refuse(AreaRestMessages.WaitRefused);
        if (facts.HeroInCombatMode && !facts.MayLeaveCombatMode) return AreaRestOpening.Refuse(AreaRestMessages.CombatRefused);
        return new(AreaRestOpenOutcome.Open, town == AreaRestTownMap.WaitAllowed, null, facts.HeroInCombatMode);
    }

    /// <summary>
    /// The refusal of a bed's use script (0x57B180 after the combat mode, SWB5): the script on attachment point 1 runs with the hero as triggerer (0x441980) and the answer <b>1</b> (<paramref name="runDefault"/>:
    /// the entry point's answer for <i>run default</i> and for an object with no script on the point) refuses the hero with message 8; any other answer (<i>skip default</i>, a normal end, a trap that ended the call)
    /// goes on to the seat. The hero has already left combat mode when this refuses (<see cref="AreaRestOpening.LeavesCombatMode"/>).
    /// </summary>
    public static int? BedScriptRefusal(bool runDefault) => runDefault ? AreaRestMessages.ScriptRefused : null;

    /// <summary>
    /// The end tests of a step (0x57B7F0, SWB9, TL27), run after the hour jump and the heal: the sleeper's death ends the rest; otherwise a positive length goes on while more than
    /// one hour is left (the next step carries one less), −1 while the hour after the jump is not 7, −2 while it is not 20 and −3 while the sleeper still has hit-point damage. The
    /// unconscious state is not tested and fatigue damage is not looked at.
    /// </summary>
    [OriginalFunction(0x57B7F0, OriginalFunctionScope.Partial)]
    public static AreaRestEnd AfterStep(int code, int hourAfterJump, bool sleeperDead, bool sleeperHasHitPointDamage)
    {
        if (sleeperDead) return AreaRestEnd.Died;
        var goOn = code switch
        {
            UntilMorning => hourAfterJump != MorningHour,
            UntilEvening => hourAfterJump != EveningHour,
            UntilHealed => sleeperHasHitPointDamage,
            _ => code > 1,
        };
        return goOn ? AreaRestEnd.Continue : AreaRestEnd.Done;
    }

    /// <summary>The next step's code: a positive length counts down, the three codes stay.</summary>
    public static int NextCode(int code) => code > 1 ? code - 1 : code;

    /// <summary>The heal kind of a step on the ground (SWB9, SWB10): the hit-point damage drops by the healing rate times this and the fatigue damage by three times that; the wait window heals nothing.</summary>
    public const int GroundHealKind = 1;

    /// <summary>ARC-WORLD-1162 (SWB3, SWB9): the heal kind of a step in a bed, twice the ground's.</summary>
    public const int BedHealKind = 2;

    /// <summary>The heal kind of a rest: a bed's 2, else the ground's 1.</summary>
    public static int HealKind(bool bed) => bed ? BedHealKind : GroundHealKind;
}

/// <summary>How a step of the rest ended (SWB9).</summary>
public enum AreaRestEnd { Continue, Done, Died }

/// <summary>
/// ARC-WORLD-1160 (E3-a; TL27, SWB8, SWB9): the chain of the original's Sleeping events (kind 15, real time) of one open window, as a deadline the host's real clock spends. A press
/// on a button removes every pending Sleeping event and adds one 50 ms later; each step re-adds itself 200 ms of real time after it ran while the rest goes on. The chain is not saved: a
/// load closes the window (0x541710), as the original does. PROJECT: the event is this object's deadline, not an entry of the session's queue (the queue has no real-time kind), with the same
/// timing; the game's own events still fire inside the jumps (TL26).
/// </summary>
public sealed class AreaRestRun
{
    /// <summary>The first step after a press and the gap between steps, in real milliseconds (TL27).</summary>
    public const long FirstStepMs = 50, StepGapMs = 200;

    private long? _due;

    public AreaRestRun(bool wait) => Wait = wait;

    /// <summary>True for the wait window (seven buttons, no heal).</summary>
    public bool Wait { get; }

    /// <summary>The hour code of the chain that is running (positive: hours left; −1, −2, −3: the codes), 0 before the first press.</summary>
    public int Code { get; private set; }

    /// <summary>True from the first press (the overlay starts then, once, SWB11) until the rest ends.</summary>
    public bool Pressed { get; private set; }

    /// <summary>True while a step is pending.</summary>
    public bool Pending => _due is not null;

    /// <summary>The pending step's deadline in real milliseconds (null when there is none).</summary>
    public long? DueMs => _due;

    /// <summary>A left click on a button (SWB8): the pending step is removed and the first step is due 50 ms later. False for a button the window does not have (the click answers 0).</summary>
    [OriginalFunction(0x57B710, OriginalFunctionScope.Partial)]
    public bool Press(int button, long nowMs)
    {
        if (AreaRestRules.CodeOf(button, Wait) is not { } code) return false;
        Code = code; Pressed = true; _due = checked(nowMs + FirstStepMs);
        return true;
    }

    /// <summary>Takes the pending step when it is due; the caller then jumps the clock, heals and calls <see cref="Finish"/>.</summary>
    public bool TakeDue(long nowMs)
    {
        if (_due is not { } due || nowMs < due) return false;
        _due = null;
        return true;
    }

    /// <summary>The step's end tests (<see cref="AreaRestRules.AfterStep"/>): a rest that goes on re-adds the step 200 ms after <paramref name="nowMs"/> with the next code; otherwise the chain is spent.</summary>
    public AreaRestEnd Finish(long nowMs, int hourAfterJump, bool sleeperDead, bool sleeperHasHitPointDamage)
    {
        var end = AreaRestRules.AfterStep(Code, hourAfterJump, sleeperDead, sleeperHasHitPointDamage);
        if (end == AreaRestEnd.Continue) { Code = AreaRestRules.NextCode(Code); _due = checked(nowMs + StepGapMs); }
        else { _due = null; Code = 0; }
        return end;
    }

    /// <summary>Removes the pending step (the close, SWB12: every kind 15 event is removed) and forgets the press.</summary>
    public void Clear() { _due = null; Pressed = false; Code = 0; }
}

/// <summary>
/// ARC-WORLD-1160 (E3-a; SWB11): the screen fade of the rest. The start fades the screen out once per rest and the stop fades it in, each with the pair that the loading screen's
/// reading gives (2.0 s in 48 steps, ledger LS3); the colour is black (the triage's reading, not checked here). A stop that comes before the fade-out ended fades in from where it is.
/// </summary>
public sealed class AreaRestFade
{
    /// <summary>The fade's length in real milliseconds and its number of steps.</summary>
    public const long DurationMs = 2000;
    public const int Steps = 48;

    private double _from, _to;
    private long _startedMs;
    private bool _running;

    /// <summary>The screen's cover, 0 (clear) to 1 (black), at <paramref name="nowMs"/>: a whole number of steps of the fade.</summary>
    public double Alpha(long nowMs)
    {
        if (!_running) return _to;
        var elapsed = Math.Clamp(nowMs - _startedMs, 0, DurationMs);
        var step = (int)(elapsed * Steps / DurationMs);
        return _from + (_to - _from) * step / Steps;
    }

    /// <summary>True while the cover is not clear or a fade runs.</summary>
    public bool Visible(long nowMs) => _running || _to > 0;

    /// <summary>Starts the fade-out (the overlay start, 0x57B9E0).</summary>
    [OriginalFunction(0x57B9E0, OriginalFunctionScope.Partial)]
    public void Out(long nowMs) => Start(Alpha(nowMs), 1, nowMs);

    /// <summary>Starts the fade-in (the overlay stop, 0x57BA70).</summary>
    [OriginalFunction(0x57BA70, OriginalFunctionScope.Partial)]
    public void In(long nowMs) => Start(Alpha(nowMs), 0, nowMs);

    /// <summary>Settles the fade once its time is over (a frame calls it).</summary>
    public void Tick(long nowMs)
    {
        if (!_running || nowMs - _startedMs < DurationMs) return;
        _from = _to; _running = false;
    }

    /// <summary>Clears the fade at once (the window closed with a load, a map change or a session change).</summary>
    public void Reset() { _from = _to = 0; _running = false; }

    private void Start(double from, double to, long nowMs) { _from = from; _to = to; _startedMs = nowMs; _running = true; }
}

/// <summary>
/// ARC-WORLD-1160 (E3-a): the labels and refusals of the sleep window: the installed <c>mes\sleepUI.mes</c> when it reads (the original's text in the player's language), otherwise the
/// project's English text with the reason in <see cref="Detail"/>. Godot-free.
/// </summary>
public sealed class AreaRestTexts
{
    public const string Path = "mes/sleepUI.mes";
    private const int MaxText = 128;
    private static readonly IReadOnlyDictionary<int, string> ProjectText = new Dictionary<int, string>
    {
        [0] = "1 hour", [1] = "2 hours", [2] = "4 hours", [3] = "8 hours", [4] = "24 hours", [5] = "Until morning", [6] = "Until evening", [7] = "Until healed",
        [AreaRestMessages.ScriptRefused] = "You cannot sleep here.", [AreaRestMessages.CombatRefused] = "You cannot rest while enemies are fighting you.",
        [AreaRestMessages.BedOccupied] = "The bed is taken.", [AreaRestMessages.HeroUnconscious] = "You cannot rest while unconscious.",
        [12] = "Wait 1 hour", [13] = "Wait 2 hours", [14] = "Wait 4 hours", [15] = "Wait 8 hours", [16] = "Wait 24 hours", [17] = "Wait until morning", [18] = "Wait until evening",
        [AreaRestMessages.WaitRefused] = "You cannot rest in town.",
    };

    private readonly Func<int, string?> _original;

    private AreaRestTexts(Func<int, string?> original, bool fromGame, string detail) { _original = original; FromGame = fromGame; Detail = detail; }

    /// <summary>True when the texts come from the installed file.</summary>
    public bool FromGame { get; }
    public string Detail { get; }

    /// <summary>The project's English text only (no game folder, or the self-test).</summary>
    public static AreaRestTexts Project(string reason) => new(_ => null, false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Texts over a message lookup (tests): a number it does not answer takes the project's text.</summary>
    public static AreaRestTexts From(Func<int, string?> messages, string detail) =>
        new(messages ?? throw new ArgumentNullException(nameof(messages)), true, detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <summary>Reads <c>mes\sleepUI.mes</c> from the game folder of <paramref name="modulePath"/>; any failure gives the project's text with the reason.</summary>
    public static AreaRestTexts Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Project($"{Path} not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Project($"{Path} not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        if (AreaStartingHero.Table(stack, Path, ContentLoadPhase.ModuleLoaded, required: true, out var detail) is not { } table)
            return Project($"{Path} not read ({detail})");
        var kept = new Dictionary<int, string>();
        foreach (var number in ProjectText.Keys)
            if (table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } text } && text.Length <= MaxText)
                kept[number] = text;
        return new(number => kept.GetValueOrDefault(number), true, $"{Path}: {kept.Count} of {ProjectText.Count} texts");
    }

    /// <summary>The text of a message: the game's when it has it, else the project's.</summary>
    public string Text(int message) => _original(message) is { Length: > 0 } text ? text : ProjectText.GetValueOrDefault(message, $"[{message}]");

    /// <summary>The label of button <paramref name="button"/> of a window of this kind.</summary>
    public string Label(int button, bool wait) => Text(AreaRestRules.LabelMessage(button, wait));
}

/// <summary>What a step's heal left (SWB9, SWB10): how many critters were healed and the sleeper's state the end tests read.</summary>
public readonly record struct AreaRestHealed(int Healed, bool SleeperDead, bool SleeperHasHitPointDamage);

/// <summary>
/// ARC-WORLD-1160 (E3-a; 0x57B7F0 step (b), 0x441260 and 0x45E910, ledger SWB9 and SWB10): the heal of one step. The sleeper (the hero) first, then every follower the host runs, each through the
/// session's life domain (<see cref="GameSessionOwner.RestHeal"/>, <see cref="LifeRules.RestHeal"/>): the hit-point damage drops by the healing rate times <paramref name="kind"/> and the fatigue damage by three
/// times that. A dead critter is skipped by the rule itself. Followers the host does not run, and the followers of followers, are not reached (<see cref="AreaAiHost.FollowerLifeTargets"/>).
/// </summary>
public static class AreaRestParty
{
    /// <summary>Heals for one step (<paramref name="heal"/> false in the wait window: nothing is healed) and reads the sleeper's state.</summary>
    public static AreaRestHealed Step(GameSessionOwner session, AreaAiHost? ai, int kind, bool heal)
    {
        ArgumentNullException.ThrowIfNull(session);
        var healed = 0;
        if (heal)
        {
            if (session.RestHeal(session.Version, SessionLifeTarget.Hero, kind).Disposition != GameSessionDisposition.Rejected) healed++;
            foreach (var follower in ai?.FollowerLifeTargets() ?? [])
                if (session.RestHeal(session.Version, follower, kind).Disposition != GameSessionDisposition.Rejected) healed++;
        }
        var life = session.QueryHeroLife();
        return new(healed, life?.Status.IsDead ?? true, (life?.Record.State.HitPointDamage ?? 0) > 0);
    }
}
