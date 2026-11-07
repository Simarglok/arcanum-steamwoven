using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Why the logbook does not open: the original refuses it while the local PC is dead (ledger LB2).</summary>
public enum AreaLogbookRefusal { None, Dead }

/// <summary>The two logbook tabs this viewer shows, in the original's tab order (rumors and notes first, then quests).</summary>
public enum AreaLogbookTab { Rumors, Quests }

/// <summary>How the original draws an entry (ledger LB5): plain black; struck through in black (a quelled rumor); blue
/// (an accepted or achieved quest); struck through in green (completed); struck through in red (completed by someone else
/// or botched).</summary>
public enum AreaLogbookStyle { Plain, Struck, Active, Completed, Failed }

/// <summary>One logbook entry: the quest or rumor number, the game time, the date line as the original composes it (with
/// the project's month names), the quest state label (null for a rumor), the text (empty when the data has none) and the
/// style.</summary>
public sealed record AreaLogbookEntry(int Number, long TimestampMs, string Date, string? State, string Text, AreaLogbookStyle Style);

/// <summary>A tab of the logbook. <see cref="LowIntelligence"/> says the hero reads the low-intelligence texts;
/// <see cref="Note"/> names what is missing (no hero, no texts, an undecodable text).</summary>
public sealed record AreaLogbookView(AreaLogbookTab Tab, IReadOnlyList<AreaLogbookEntry> Entries, bool LowIntelligence, string? Note);

/// <summary>The logbook of a session, or the reason it does not open.</summary>
public sealed record AreaLogbookBuild(AreaLogbookRefusal Refusal, AreaLogbookView? View);

public enum AreaLogbookTextsStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, TextsFailed }

/// <summary>The logbook texts of the module the area was read from, or why there are none.</summary>
public sealed record AreaLogbookTextsLoad(AreaLogbookTextsStatus Status, LogbookTexts? Texts, string Detail);

/// <summary>
/// ARC-UI-008: the quest and rumor logbook of the session's PC, Godot-free. Every value is a session query or a text of the
/// player's own data (<see cref="LogbookTexts"/>); the logbook changes nothing. What the original shows was checked in the
/// executable (ledger section "Logbook"): the rumors the PC knows with their quelled mark and the quests whose state is not
/// Unknown, each in game-time order (<see cref="GameSessionOwner.QueryRumorLog"/>, <see cref="GameSessionOwner.QueryQuestLog"/>),
/// each entry as a date line (the quest's with its state) over the quest description or rumor text chosen by the PC's
/// intelligence, and the colour of each state. The session works without a hero (the diagnostic PC profile): the texts are
/// then the normal ones. Original UI text (tab names, month and state labels) is not used; the labels are the project's.
/// </summary>
public static class AreaLogbook
{
    /// <summary>The intelligence the logbook reads without a hero: above the low-intelligence threshold.</summary>
    public const int NoHeroIntelligence = LogbookTexts.LowIntelligence + 1;

    private static readonly string[] Months =
    [
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
    ];

    /// <summary>Loads the logbook texts for the module archive the area was read from, through the game folder's content
    /// stack (<see cref="AreaContentRoot.Discover"/>, the rule of the description tables); otherwise no texts are loaded.</summary>
    public static AreaLogbookTextsLoad LoadTexts(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaLogbookTextsStatus.MountFailed : AreaLogbookTextsStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaLogbookTextsStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var loaded = LogbookTexts.Load(stack);
        if (loaded.Status != LogbookTextStatus.Ready) return new(AreaLogbookTextsStatus.TextsFailed, null, $"{loaded.Status} {loaded.Path} {loaded.Read} {loaded.Parse?.Status}");
        var texts = loaded.Texts!;
        return new(AreaLogbookTextsStatus.Ready, texts, $"quest descriptions {texts.QuestLog?.Count ?? 0}/{texts.QuestLogLow?.Count ?? 0}" +
            $"{(texts.QuestDescriptionsLoaded ? "" : " (no quest table)")}, rumor texts {texts.Rumors?.Count ?? 0}/{texts.RumorsLow?.Count ?? 0}");
    }

    /// <summary>Whether the logbook opens for this session, without building it.</summary>
    public static AreaLogbookRefusal Refusal(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.QueryHeroLife() is { Status.IsDead: true } ? AreaLogbookRefusal.Dead : AreaLogbookRefusal.None;
    }

    /// <summary>The tab of the session's logbook with the texts of <paramref name="texts"/> (null: no texts loaded) decoded
    /// under <paramref name="profile"/>, or the refusal.</summary>
    public static AreaLogbookBuild Build(GameSessionOwner session, LogbookTexts? texts, AreaLogbookTab tab, MesTextProfile profile)
    {
        var refusal = Refusal(session);
        if (refusal != AreaLogbookRefusal.None) return new(refusal, null);
        var facts = session.QueryHeroDialogFacts();
        var intelligence = facts?.Intelligence ?? NoHeroIntelligence;
        var startYear = session.QueryClockSnapshot().ProfileBinding.StartYear;
        var undecodable = 0;
        string Text(MesMessage? message)
        {
            if (message is null) return string.Empty;
            var decoded = MesText.Decode(message, profile);
            if (decoded.Status == MesDecodeStatus.Decoded) return decoded.Text!;
            undecodable++;
            return string.Empty;
        }
        var entries = tab switch
        {
            AreaLogbookTab.Rumors => session.QueryRumorLog().Select(rumor => new AreaLogbookEntry(rumor.Rumor, rumor.TimestampMs,
                Date(rumor.TimestampMs, startYear), null, Text(texts?.RumorText(rumor.Rumor, intelligence)),
                rumor.Quelled ? AreaLogbookStyle.Struck : AreaLogbookStyle.Plain)).ToArray(),
            AreaLogbookTab.Quests => session.QueryQuestLog().Select(quest => new AreaLogbookEntry(quest.Quest, quest.TimestampMs,
                Date(quest.TimestampMs, startYear), StateLabel(quest.State), Text(texts?.QuestDescription(quest.Quest, intelligence)),
                Style(quest.State))).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(tab)),
        };
        var notes = new List<string>();
        if (facts is null) notes.Add("no hero: normal texts");
        if (texts is null) notes.Add("no logbook texts loaded");
        else if (tab == AreaLogbookTab.Quests && !texts.QuestDescriptionsLoaded) notes.Add("the module has no quest table: no descriptions");
        if (undecodable > 0) notes.Add($"{undecodable} text(s) not decodable as {profile}");
        return new(AreaLogbookRefusal.None, new(tab, Array.AsReadOnly(entries), intelligence <= LogbookTexts.LowIntelligence,
            notes.Count == 0 ? null : string.Join("; ", notes)));
    }

    /// <summary>
    /// The entry's date line (ledger LB4): the month name, the day, a comma, the year, two spaces, the 12-hour time with two
    /// minute digits and "am"/"pm" (hour 0 is 12 am, hour 12 is 12 pm), then four spaces, from the calendar of the game time
    /// (30-day months, 12-month years from the start year, LB13). The month names are the project's English names, not the
    /// player's <c>logbk_ui.mes</c>.
    /// </summary>
    public static string Date(long timestampMs, long startYear)
    {
        if (ClockCalendar.TryDerive(timestampMs, startYear, out var calendar) != ClockFailure.None)
            throw new ArgumentOutOfRangeException(nameof(timestampMs));
        var hour = calendar.Hour;
        var suffix = hour < 12 ? 'a' : 'p';
        if (hour == 0) hour = 12;
        else if (hour > 12) hour -= 12;
        return $"{Months[calendar.Month - 1]} {calendar.DayOfMonth}, {calendar.Year}  {hour}:{calendar.Minute:00}{suffix}m    ";
    }

    /// <summary>The project's label of a quest state the logbook lists (never Unknown).</summary>
    public static string StateLabel(SessionQuestState state) => state switch
    {
        SessionQuestState.Mentioned => "mentioned",
        SessionQuestState.Accepted => "accepted",
        SessionQuestState.Achieved => "achieved",
        SessionQuestState.Completed => "completed",
        SessionQuestState.OtherCompleted => "completed by someone else",
        SessionQuestState.Botched => "botched",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The original's colour of a quest state (LB5).</summary>
    public static AreaLogbookStyle Style(SessionQuestState state) => state switch
    {
        SessionQuestState.Completed => AreaLogbookStyle.Completed,
        SessionQuestState.OtherCompleted or SessionQuestState.Botched => AreaLogbookStyle.Failed,
        SessionQuestState.Accepted or SessionQuestState.Achieved => AreaLogbookStyle.Active,
        _ => AreaLogbookStyle.Plain,
    };

    /// <summary>The window's text of one entry: the date line, the state for a quest, the text on the next line.</summary>
    public static string Line(AreaLogbookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Date + entry.State + "\n" + entry.Text;
    }

    /// <summary>The project's tab title.</summary>
    public static string TabName(AreaLogbookTab tab) => tab == AreaLogbookTab.Rumors ? "Rumors" : "Quests";
}
