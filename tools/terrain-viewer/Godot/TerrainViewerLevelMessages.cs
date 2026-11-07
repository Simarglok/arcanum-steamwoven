using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-046: the hero's level-up messages (docs/rendering/terrain-viewer.md#level-up-messages-arc-ui-046; ledger OL27–OL28). Every
// award passes here (LevelAreaFollowers: hits, kills and the dialogs' queued awards); each level it wrote posts one entry to the
// message log, whose drawing plays the level-up sound 3005 as the original's does. The texts are the player's MES files, loaded in the
// background once per module. ARC-UI-023: the entries go to the original's message log in the bottom strip (ledger ML1–ML8).
public partial class TerrainViewerApp
{
    private readonly AreaMessageLog _messageLog = new();
    // The self-test moves the log's real-time clock forward to pass the quiet time after a stored entry (ML2) without waiting.
    private long _messageClockShiftForSelfTest;
    private string? _levelTextsModule;
    private Task<(AreaLevelTextsStatus Status, AreaLevelTexts Texts, string Detail)>? _levelTextsTask;
    private AreaLevelTexts? _levelTexts;
    private int _levelMessagesPosted;
    private readonly List<SessionLevelUp> _pendingLevelUps = [];
    // The session the window's entries and the queue belong to: a map transfer carries it, a quick load or a new game replaces it.
    private GameSessionOwner? _levelMessagesSession;

    /// <summary>Starts loading the level-up texts for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaLevelTexts(TerrainViewerArguments arguments)
    {
        if (_levelTextsModule == arguments.ModulePath) return;
        _levelTextsModule = arguments.ModulePath; _levelTexts = null;
        var module = arguments.ModulePath;
        _levelTextsTask = Task.Run(() => AreaLevelTexts.Load(module));
    }

    /// <summary>The loaded texts, waiting at most <paramref name="wait"/> for a load still running; null while it runs (the entries wait
    /// in the queue), none when the load failed or threw.</summary>
    private AreaLevelTexts? LevelTexts(TimeSpan wait)
    {
        if (_levelTexts is not null) return _levelTexts;
        if (_levelTextsTask is not { } task) return AreaLevelTexts.None;
        // A wait on the handle never throws for a faulted task (Task.Wait would).
        if (!task.IsCompleted && !((IAsyncResult)task).AsyncWaitHandle.WaitOne(wait)) return null;
        var (status, texts, detail) = task.IsCompletedSuccessfully ? task.Result
            : (AreaLevelTextsStatus.StackFailed, AreaLevelTexts.None, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _levelTexts = texts; _levelTextsTask = null;
        GD.Print($"ARC-UI-046 level texts status={status} detail={detail}");
        return texts;
    }

    /// <summary>Queues one message-window entry per level the award wrote and posts them (waiting up to a second for the texts; while
    /// they still load the entries wait and the next frame posts them).</summary>
    private void ShowLevelUps(SessionExperienceAward? award)
    {
        if (award?.LevelUps is not { Count: > 0 } levels) return;
        SyncLevelMessageSession();
        _pendingLevelUps.AddRange(levels);
        FlushLevelUps(TimeSpan.FromSeconds(1));
    }

    /// <summary>Posts the queued level-up entries once the texts are there, each with the level-up sound.</summary>
    private void FlushLevelUps(TimeSpan wait)
    {
        FlushEncumbranceMessage(wait);
        SyncLevelMessageSession();
        if (_pendingLevelUps.Count == 0 || LevelTexts(wait) is not { } texts) return;
        // Taken off the queue first: an entry is posted at most once.
        var batch = _pendingLevelUps.ToArray();
        _pendingLevelUps.Clear();
        foreach (var level in batch)
        {
            PostMessage(AreaLevelMessages.Compose(level, texts), "level up");
            _levelMessagesPosted++;
            GD.Print($"ARC-UI-046 level-up level={level.Level} points={level.Points} scheme={(level.Scheme is not { } s ? "none" : !s.HasRule ? "no-rule"
                : $"purchases={s.Purchases.Count} completed={s.Completed}")}");
        }
    }

    // ARC-WORLD-087: the last encumbrance change shown, by session (a quick load or a new game starts its own count).
    private (GameSessionOwner Session, long Sequence)? _encumbranceShown;
    private int _encumbranceMessagesPosted;

    /// <summary>ARC-WORLD-087: gives the session the rule of effect 158 from the hero's effect tables (none without them); the session
    /// does not save it, so every new or loaded session gets it before its first change.</summary>
    private void AttachAreaEncumbranceEffect()
    {
        if (_areaSession is not { } session) return;
        var rule = _areaEffectRules?.Invoke(Arcanum.Simulation.Items.CarryRules.EffectNumber);
        try { session.AttachEncumbranceEffect(rule is null ? null : SessionEffectDefinition.FromRule(rule)); }
        catch (ArgumentException) { session.AttachEncumbranceEffect(null); GD.Print("ARC-WORLD-087 effect 158 refused: no encumbrance entries"); return; }
        GD.Print($"ARC-WORLD-087 effect 158 {(rule is null ? "missing" : "attached")}");
    }

    /// <summary>ARC-WORLD-087: posts the message of the hero's latest encumbrance change once the texts are there (one entry for the
    /// latest level when several changes came between two frames, as the window shows only the last).</summary>
    private void FlushEncumbranceMessage(TimeSpan wait)
    {
        if (_areaSession is not { } session || session.QueryHeroEncumbranceChange() is not { } change) return;
        if (_encumbranceShown is { } shown && ReferenceEquals(shown.Session, session) && shown.Sequence >= change.Sequence) return;
        SyncLevelMessageSession();
        if (LevelTexts(wait) is not { } texts) return;
        _encumbranceShown = (session, change.Sequence);
        PostMessage(AreaLevelMessages.Encumbrance(change.Level, texts), "encumbrance");
        _encumbranceMessagesPosted++;
        GD.Print($"ARC-WORLD-087 encumbrance level={change.Level} entries={change.Entries} sequence={change.Sequence}");
    }

    /// <summary>A different session than the one the entries belong to (a quick load, a new game) empties the window and the queue;
    /// a session a map transfer carries keeps them, and none (between loads) changes nothing.</summary>
    private void SyncLevelMessageSession()
    {
        if (_areaSession is not { } session || ReferenceEquals(session, _levelMessagesSession)) return;
        if (_levelMessagesSession is not null) ClearLevelMessages();
        _levelMessagesSession = session;
    }

    /// <summary>A session that ends (not carried by a map transfer) empties the window when the entries are its own.</summary>
    private void EndLevelMessages(GameSessionOwner? session)
    {
        if (session is null || !ReferenceEquals(session, _levelMessagesSession)) return;
        ClearLevelMessages();
        _levelMessagesSession = null;
    }

    /// <summary>Empties the log and drops the queued entries (another session, or the native smoke's own entries).</summary>
    private void ClearLevelMessages()
    {
        _pendingLevelUps.Clear();
        _messageLog.Clear();
        _gameBarPanel?.MessageLog.Render();
    }

    private long MessageLogNow => (long)Time.GetTicksMsec() + _messageClockShiftForSelfTest;

    /// <summary>ARC-UI-023: posts an entry to the message log and plays the sound its drawing plays (ML5).</summary>
    private AreaMessagePost PostMessage(AreaMessageEntry entry, string what)
    {
        var post = _messageLog.Post(entry, MessageLogNow);
        // ARC-DEV-051: a stored entry is a session event of the bug report's log.
        if (entry.Stored) BugReportLog.Process.Add(BugReportLineKind.Event, $"message log {entry.Kind}: {entry.Title} {entry.Line} {entry.Detail}");
        if (post.Sound is { } sound) PlayInterfaceSound(sound, what);
        _gameBarPanel?.MessageLog.Render();
        return post;
    }
}

public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-046 native observation on the original installation: the level texts load with every file, and an award built
    /// here (no session change: one points level, one scheme level naming a stat, a degree and the completion) goes through the
    /// production display path. The entries read the player's title, level and points formats and purchase names, not the project's
    /// labels, and each plays the level-up sound. Counts and checks only; no text is printed.</summary>
    private Task RunAreaLevelMessageSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-046 level message smoke FAIL: " + why); throw new InvalidOperationException("ARC-UI-046 level message smoke: " + why); }
        var texts = LevelTexts(TimeSpan.FromSeconds(60)) ?? AreaLevelTexts.None;
        Require(texts.Missing.Count == 0, "level texts missing: " + string.Join(", ", texts.Missing));
        var scheme = new SessionLevelUpScheme(true, [new(Arcanum.Simulation.Character.AdvancementTrack.Stat, 0, 9),
            new(Arcanum.Simulation.Character.AdvancementTrack.TechDiscipline, 0, 1)], Completed: true);
        var award = new SessionExperienceAward(0, 2, 3, null, null, [new(7, 9, null), new(8, 0, scheme)]);
        var (posted, played) = (_levelMessagesPosted, _interfaceSoundsPlayed);
        ShowLevelUps(award);
        Require(_levelMessagesPosted == posted + 2, "two entries posted");
        var history = _messageLog.History;
        var (points, schemed) = (history[^2], history[^1]);
        string Text(string path, int number) => texts.Text(path, number) ?? throw new InvalidOperationException($"ARC-UI-046 level message smoke: {path} {number} missing");
        Require(points.Title == Text(AreaLevelTexts.InterfacePath, AreaLevelMessages.TitleMessage)
            && points.Line == AreaLevelMessages.Format(Text(AreaLevelTexts.InterfacePath, AreaLevelMessages.LevelMessage), 7)
            && points.Detail == AreaLevelMessages.Format(Text(AreaLevelTexts.InterfacePath, AreaLevelMessages.PointsMessage), 9)
            && !points.Line.Contains("%d") && !points.Detail.Contains("%d"), "the points entry reads the player's title and formats");
        Require(schemed.Detail.StartsWith(Text(AreaLevelTexts.LevelPath, AreaLevelMessages.SchemeMessage), StringComparison.Ordinal)
            && schemed.Detail.Contains(Text(AreaLevelTexts.StatPath, 0) + " 9, ", StringComparison.Ordinal)
            && schemed.Detail.Contains(Text(AreaLevelTexts.TechPath, 0) + " " + Text(AreaLevelTexts.TechPath, AreaLevelMessages.FirstDegreeName + 1), StringComparison.Ordinal)
            && schemed.Detail.EndsWith(" " + Text(AreaLevelTexts.LevelPath, AreaLevelMessages.CompletedMessage), StringComparison.Ordinal)
            && _messageLog.Shown == schemed && schemed.Scheme, "the scheme entry names the player's texts and is shown");
        Require(_audio is null || _interfaceSoundsPlayed == played + 2, "each entry played the level-up sound: " + _cueNotice);
        // The entries were built here: the window empties so later stages see none of them.
        var shownHistory = history.Count;
        ClearLevelMessages();
        GD.Print("ARC-UI-046 native-smoke level message PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Entries = 2, Missing = texts.Missing.Count, Sounds = _interfaceSoundsPlayed - played, History = shownHistory, Parity = "NOT_VERIFIED" }));
        RunAreaEncumbranceSmoke(texts);
        return Task.CompletedTask;
    }

    /// <summary>ARC-WORLD-087 native observation: the hero's effect tables hold effect 158 as a rule the session admits with a speed change
    /// (stat 11), each level's message (<c>mes\critter.mes</c> 11..17) is the player's text, and an entry goes through the production
    /// window path. Counts and checks only; no text is printed.</summary>
    private void RunAreaEncumbranceSmoke(AreaLevelTexts texts)
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-WORLD-087 encumbrance smoke FAIL: " + why); throw new InvalidOperationException("ARC-WORLD-087 encumbrance smoke: " + why); }
        if (_areaEffectRules is null || _areaSession is null) { GD.Print("ARC-WORLD-087 native-smoke encumbrance SKIP no hero tables or session"); return; }
        var rule = _areaEffectRules(Arcanum.Simulation.Items.CarryRules.EffectNumber);
        Require(rule is not null, "effect 158 missing");
        var definition = SessionEffectDefinition.FromRule(rule!);
        Require(definition.Changes.Any(c => c.Kind == Arcanum.Data.Characters.EffectAttributeKind.Stat && c.Attribute == 11), "effect 158 changes the speed");
        _areaSession!.AttachEncumbranceEffect(definition);
        var lines = Enumerable.Range(0, Arcanum.Simulation.Items.CarryRules.Levels).Count(level => texts.Text(AreaLevelTexts.CritterPath, AreaLevelMessages.EncumbranceMessage + level) is not null);
        Require(lines == Arcanum.Simulation.Items.CarryRules.Levels, $"critter.mes level lines {lines}/7");
        var played = _interfaceSoundsPlayed;
        PostMessage(AreaLevelMessages.Encumbrance(4, texts), "encumbrance");
        Require(_messageLog.Shown is { Kind: AreaMessageKind.Message } shown && shown.Title == texts.Text(AreaLevelTexts.CritterPath, AreaLevelMessages.EncumbranceMessage + 4),
            "the level-4 entry is shown");
        Require(_audio is null || _interfaceSoundsPlayed == played + 1, "the message entry played its sound (3004): " + _cueNotice);
        ClearLevelMessages();
        GD.Print("ARC-WORLD-087 native-smoke encumbrance PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Changes = definition.Changes.Length, Lines = lines, Level = _areaSession.QueryHeroCarry()?.Level, Parity = "NOT_VERIFIED" }));
    }
}
