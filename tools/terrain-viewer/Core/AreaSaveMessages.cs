namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-168 (B34, B35): the messages the original posts when a game is saved or loaded, numbers of <c>mes\MainMenu.mes</c> (read
/// in the 1.0.7.4 executable: the in-game key handler 0x401560, the load 0x5432B0 and the save window's action 0x5435D0; ledger
/// SL9–SL11). Godot-free.
/// <para>
/// Two kinds of message. A <em>result</em> is a type 4 entry of the message window (stored in its history, played with sound 3004,
/// <see cref="AreaMessageKind.Message"/>): the load posts <see cref="LoadDone"/> or <see cref="LoadFailed"/> whoever asked for it (F8, the
/// load window, the main menu's last save), the save window posts <see cref="SaveDone"/> or <see cref="SaveFailed"/>. A <em>note</em> is a
/// type 6 entry (<see cref="AreaMessageKind.Hover"/>: shown only in the message mode and only when no entry was stored in the last 3 s,
/// never stored): F7 posts <see cref="Saving"/> before and <see cref="SaveCompleted"/> after the save, F8 posts <see cref="Loading"/> before
/// and <see cref="LoadCompleted"/> after the load. The notes come after the results of the same key, so a note that follows a result is
/// held back by the 3 s rule on its own.
/// </para>
/// <para>
/// The original's F7 posts <see cref="SaveCompleted"/> whether the save wrote or not (it ignores the save's result). A completion
/// note after a failed save is a technical display bug, fixed in both rule modes: <see cref="QuickSaveAfter"/> posts the save-failure
/// result (<see cref="SaveFailed"/>) instead.
/// </para>
/// </summary>
public static class AreaSaveMessages
{
    /// <summary>A load that read (type 4).</summary>
    public const int LoadDone = 5000;
    /// <summary>A load that failed: the version is not the supported one, or the save did not read (type 4).</summary>
    public const int LoadFailed = 5001;
    /// <summary>The save window's save that wrote (type 4).</summary>
    public const int SaveDone = 5002;
    /// <summary>The save window's save that failed (type 4).</summary>
    public const int SaveFailed = 5003;
    /// <summary>F7 before the save (type 6).</summary>
    public const int Saving = 5060;
    /// <summary>F8 before the load (type 6).</summary>
    public const int Loading = 5061;
    /// <summary>F7 after the save (type 6).</summary>
    public const int SaveCompleted = 5062;
    /// <summary>F8 after the load (type 6).</summary>
    public const int LoadCompleted = 5063;

    /// <summary>The numbers this class posts, with the project's own wording used when the installed <c>MainMenu.mes</c> does not read.</summary>
    public static IReadOnlyDictionary<int, string> ProjectText { get; } = new Dictionary<int, string>
    {
        [LoadDone] = "Game loaded.", [LoadFailed] = "The save is damaged. Load failed.",
        [SaveDone] = "Game saved.", [SaveFailed] = "The save could not be written. Save failed.",
        [Saving] = "Saving...", [Loading] = "Loading...", [SaveCompleted] = "Save completed.", [LoadCompleted] = "Load completed.",
    };

    /// <summary>A result entry (type 4, stored, with a sound) of the message.</summary>
    public static AreaMessageEntry Result(AreaMainMenuTexts texts, int message) =>
        AreaMessageEntry.Text(AreaMessageKind.Message, (texts ?? throw new ArgumentNullException(nameof(texts))).Text(message));

    /// <summary>A note entry (type 6, shown at most, never stored) of the message.</summary>
    public static AreaMessageEntry Note(AreaMainMenuTexts texts, int message) =>
        AreaMessageEntry.Text(AreaMessageKind.Hover, (texts ?? throw new ArgumentNullException(nameof(texts))).Text(message));

    /// <summary>F7 after the save: the completion note when it wrote, else the save-failure result (see the type's remark).</summary>
    public static AreaMessageEntry QuickSaveAfter(AreaMainMenuTexts texts, bool saved) =>
        saved ? Note(texts, SaveCompleted) : Result(texts, SaveFailed);

    /// <summary>The save window's result: the success or failure message of the save.</summary>
    public static AreaMessageEntry SaveWindowResult(AreaMainMenuTexts texts, bool saved) => Result(texts, saved ? SaveDone : SaveFailed);

    /// <summary>A load's result: the success or failure message.</summary>
    public static AreaMessageEntry LoadResult(AreaMainMenuTexts texts, bool loaded) => Result(texts, loaded ? LoadDone : LoadFailed);
}

/// <summary>Which of the original's messages a save asks for (ARC-DEV-168): none (a smoke, a death reload), F7's notes, or the save window's result.</summary>
public enum AreaSaveMessageRoute { None, QuickKey, Window }

/// <summary>Which of the original's messages a load asks for (ARC-DEV-168): none, the load's result (the load window, the main menu's last
/// save), or F8's notes around the result.</summary>
public enum AreaLoadMessageRoute { None, Result, QuickKey }
