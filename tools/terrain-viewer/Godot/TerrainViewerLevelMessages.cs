using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-046: the hero's level-up messages (docs/rendering/terrain-viewer.md#level-up-messages-arc-ui-046; ledger OL27–OL28). Every
// award passes here (LevelAreaFollowers: hits, kills and the dialogs' queued awards); each level it wrote posts one entry to the
// message window and plays the level-up sound, as the original draws each entry with sound 3005. The texts are the player's MES files,
// loaded in the background once per module.
public partial class TerrainViewerApp
{
    private AreaMessageWindow? _messageWindow;
    private string? _levelTextsModule;
    private Task<(AreaLevelTextsStatus Status, AreaLevelTexts Texts, string Detail)>? _levelTextsTask;
    private AreaLevelTexts? _levelTexts;
    private int _levelMessagesPosted;

    /// <summary>Starts loading the level-up texts for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaLevelTexts(TerrainViewerArguments arguments)
    {
        if (_levelTextsModule == arguments.ModulePath) return;
        _levelTextsModule = arguments.ModulePath; _levelTexts = null;
        var module = arguments.ModulePath;
        _levelTextsTask = Task.Run(() => AreaLevelTexts.Load(module));
    }

    /// <summary>The loaded texts, waiting briefly for a load still running (a level-up is rare); none when the load failed.</summary>
    private AreaLevelTexts LevelTexts()
    {
        if (_levelTexts is not null) return _levelTexts;
        if (_levelTextsTask is not { } task) return AreaLevelTexts.None;
        if (!task.IsCompleted && !task.Wait(TimeSpan.FromSeconds(5))) return AreaLevelTexts.None;
        var (status, texts, detail) = task.IsCompletedSuccessfully ? task.Result
            : (AreaLevelTextsStatus.StackFailed, AreaLevelTexts.None, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _levelTexts = texts; _levelTextsTask = null;
        GD.Print($"ARC-UI-046 level texts status={status} detail={detail}");
        return texts;
    }

    /// <summary>Posts one message-window entry per level the award wrote, each with the level-up sound.</summary>
    private void ShowLevelUps(SessionExperienceAward? award)
    {
        if (award?.LevelUps is not { Count: > 0 } levels) return;
        if (_messageWindow is null) { _messageWindow = new AreaMessageWindow(); AddChild(_messageWindow); }
        var texts = LevelTexts();
        foreach (var level in levels)
        {
            _messageWindow.Post(AreaLevelMessages.Compose(level, texts));
            _levelMessagesPosted++;
            PlayInterfaceSound(LevelUpSound, "level up");
            GD.Print($"ARC-UI-046 level-up level={level.Level} points={level.Points} scheme={(level.Scheme is not { } s ? "none" : !s.HasRule ? "no-rule"
                : $"purchases={s.Purchases.Count} completed={s.Completed}")}");
        }
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
        var texts = LevelTexts();
        Require(texts.Missing.Count == 0, "level texts missing: " + string.Join(", ", texts.Missing));
        var scheme = new SessionLevelUpScheme(true, [new(Arcanum.Simulation.Character.AdvancementTrack.Stat, 0, 9),
            new(Arcanum.Simulation.Character.AdvancementTrack.TechDiscipline, 0, 1)], Completed: true);
        var award = new SessionExperienceAward(0, 2, 3, null, null, [new(7, 9, null), new(8, 0, scheme)]);
        var (posted, played) = (_levelMessagesPosted, _interfaceSoundsPlayed);
        ShowLevelUps(award);
        Require(_levelMessagesPosted == posted + 2, "two entries posted");
        var history = _messageWindow!.HistoryForSelfTest;
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
            && _messageWindow.ShownForSelfTest == schemed, "the scheme entry names the player's texts and is shown");
        Require(_audio is null || _interfaceSoundsPlayed == played + 2, "each entry played the level-up sound: " + _cueNotice);
        GD.Print("ARC-UI-046 native-smoke level message PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Entries = 2, Missing = texts.Missing.Count, Sounds = _interfaceSoundsPlayed - played, History = history.Count, Parity = "NOT_VERIFIED" }));
        return Task.CompletedTask;
    }
}
