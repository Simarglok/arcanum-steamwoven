using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Why the logbook does not open: the original refuses it while the local PC is dead (ledger LB2).</summary>
public enum AreaLogbookRefusal { None, Dead }

/// <summary>The logbook's seven tabs in the original's order (the table 0x5C33F8 and the list builder 0x53FBB0, ledger LB14); the value is the
/// tab's index and its title's message in <c>logbk_ui.mes</c>. ARC-UI-074 builds the first two; <see cref="AreaLogbook.Built"/> says which are built.</summary>
public enum AreaLogbookTab { Rumors, Quests, Reputation, BlessingsCurses, KillsInjuries, Background, Keys }

/// <summary>How the original draws an entry (ledger LB5): plain black; struck through in black (a quelled rumor); blue
/// (an accepted or achieved quest); struck through in green (completed); struck through in red (completed by someone else
/// or botched).</summary>
public enum AreaLogbookStyle { Plain, Struck, Active, Completed, Failed, Blessing, Curse }

/// <summary>One logbook entry: the quest or rumor number, the game time, the date line as the original composes it (with
/// the project's month names), the quest state label (null for a rumor), the text (empty when the data has none) and the
/// style.</summary>
public sealed record AreaLogbookEntry(int Number, long TimestampMs, string Date, string? State, string Text, AreaLogbookStyle Style);

/// <summary>A tab of the logbook. <see cref="LowIntelligence"/> says the hero reads the low-intelligence texts;
/// <see cref="Note"/> names what is missing (no hero, no texts, an undecodable text).</summary>
/// <param name="Title">The tab's title (ARC-UI-074): message 0 or 1 of the game folder's <c>logbk_ui.mes</c> when it has it, otherwise null.</param>
public sealed record AreaLogbookView(AreaLogbookTab Tab, IReadOnlyList<AreaLogbookEntry> Entries, bool LowIntelligence, string? Note, string? Title = null);

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
        // ARC-UI-074 (LB14): the game folder's own month names (messages 7..18), state labels (20..25) and tab titles (0, 1) replace the
        // project's English ones when the file has them.
        string? Ui(int number) => texts?.UiLine(number) is { } line && MesText.Decode(line, profile) is { Status: MesDecodeStatus.Decoded } decoded
            && decoded.Text!.Length > 0 ? decoded.Text : null;
        var months = Enumerable.Range(1, 12).Select(month => Ui(LogbookTexts.FirstMonthMessage + month)).ToArray();
        var useMonths = months.All(name => name is not null) ? months.Select(name => name!).ToArray() : null;
        string State(SessionQuestState state) => Ui(LogbookTexts.FirstStateMessage + (int)state) ?? StateLabel(state);
        var entries = tab switch
        {
            AreaLogbookTab.Rumors => session.QueryRumorLog().Select(rumor => new AreaLogbookEntry(rumor.Rumor, rumor.TimestampMs,
                Date(rumor.TimestampMs, startYear, useMonths), null, Text(texts?.RumorText(rumor.Rumor, intelligence)),
                rumor.Quelled ? AreaLogbookStyle.Struck : AreaLogbookStyle.Plain)).ToArray(),
            AreaLogbookTab.Quests => session.QueryQuestLog().Select(quest => new AreaLogbookEntry(quest.Quest, quest.TimestampMs,
                Date(quest.TimestampMs, startYear, useMonths), State(quest.State), Text(texts?.QuestDescription(quest.Quest, intelligence)),
                Style(quest.State))).ToArray(),
            // ARC-UI-079 (LB18): the PC's reputations in stored order, each a date line over the reputation's logbook name (message of
            // gamereplog.mes; no text for a missing one).
            AreaLogbookTab.Reputation => session.QueryReputations().Select(reputation => new AreaLogbookEntry(reputation.Reputation, reputation.TimestampMs,
                Date(reputation.TimestampMs, startYear, useMonths), null, Text(texts?.Reputations?.Name(reputation.Reputation)), AreaLogbookStyle.Plain)).ToArray(),
            // The hero's background description (the part of its text after the first line feed), one entry the page layout cuts into column-sized
            // pieces; no hero, or a description with no text, is an empty page.
            AreaLogbookTab.Background => BackgroundEntries(session, texts),
            // ARC-UI-080 (LB19): the PC's blessings and curses merged oldest first, each a date line over the name from gamebless.mes or
            // gamecurse.mes; blessings blue, curses red, neither struck.
            AreaLogbookTab.BlessingsCurses => BlessingEntries(session, texts, startYear, useMonths, Text),
            // ARC-UI-081 (LB20): the PC's key numbers in the order they were added (no sort, repeats kept), each only the key's name from
            // gamekey.mes (message = the number): plain black, no date line.
            // ARC-UI-082 (LB21): the nine fixed lines (the kill total and six victims, a blank line, the injuries' head) and the PC's injuries, oldest first.
            AreaLogbookTab.KillsInjuries => KillEntries(session, texts, Ui, Text),
            AreaLogbookTab.Keys => (session.QueryHeroKeys() ?? []).Select(key => new AreaLogbookEntry(key, 0, string.Empty, null,
                Text(texts?.KeyName(key)), AreaLogbookStyle.Plain)).ToArray(),
            _ when Enum.IsDefined(tab) => [],
            _ => throw new ArgumentOutOfRangeException(nameof(tab)),
        };
        var notes = new List<string>();
        if (!Built(tab)) notes.Add("this tab is not built yet");
        if (tab == AreaLogbookTab.Reputation && texts?.Reputations is null) notes.Add("no reputation names loaded");
        if (tab == AreaLogbookTab.BlessingsCurses && session.QueryHeroBlessings(SessionBlessingKind.Blessing) is null) notes.Add("no hero: no blessings or curses");
        else if (tab == AreaLogbookTab.BlessingsCurses && (texts?.Blessings is null || texts.Curses is null)) notes.Add("no blessing or curse names loaded");
        if (tab == AreaLogbookTab.KillsInjuries && session.QueryHeroKillLog() is null) notes.Add("no hero: no kills or injuries");
        else if (tab == AreaLogbookTab.KillsInjuries && texts?.Descriptions is null) notes.Add("no description names loaded");
        if (tab == AreaLogbookTab.Keys && session.QueryHeroKeys() is null) notes.Add("no hero: no keys");
        else if (tab == AreaLogbookTab.Keys && texts?.Keys is null) notes.Add("no key names loaded");
        if (tab == AreaLogbookTab.Background && (texts?.Backgrounds is null || facts is null)) notes.Add(facts is null ? "no hero: no background" : "no background texts loaded");
        if (facts is null) notes.Add("no hero: normal texts");
        if (texts is null) notes.Add("no logbook texts loaded");
        else if (tab == AreaLogbookTab.Quests && !texts.QuestDescriptionsLoaded) notes.Add("the module has no quest table: no descriptions");
        if (undecodable > 0) notes.Add($"{undecodable} text(s) not decodable as {profile}");
        return new(AreaLogbookRefusal.None, new(tab, Array.AsReadOnly(entries), intelligence <= LogbookTexts.LowIntelligence,
            notes.Count == 0 ? null : string.Join("; ", notes), Ui((int)tab)));
    }

    /// <summary>
    /// The entry's date line (ledger LB4): the month name, the day, a comma, the year, two spaces, the 12-hour time with two
    /// minute digits and "am"/"pm" (hour 0 is 12 am, hour 12 is 12 pm), then four spaces, from the calendar of the game time
    /// (30-day months, 12-month years from the start year, LB13). The month names are the project's English names, not the
    /// player's <c>logbk_ui.mes</c>.
    /// </summary>
    public static string Date(long timestampMs, long startYear, IReadOnlyList<string>? months = null)
    {
        if (months is { Count: not 12 }) throw new ArgumentException("twelve month names are needed", nameof(months));
        if (ClockCalendar.TryDerive(timestampMs, startYear, out var calendar) != ClockFailure.None)
            throw new ArgumentOutOfRangeException(nameof(timestampMs));
        var hour = calendar.Hour;
        var suffix = hour < 12 ? 'a' : 'p';
        if (hour == 0) hour = 12;
        else if (hour > 12) hour -= 12;
        return $"{(months ?? Months)[calendar.Month - 1]} {calendar.DayOfMonth}, {calendar.Year}  {hour}:{calendar.Minute:00}{suffix}m    ";
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
        // A background has no date line: its text alone.
        return entry.Date.Length == 0 ? entry.Text : entry.Date + entry.State + "\n" + entry.Text;
    }

    /// <summary>The project's tab title.</summary>
    public static string TabName(AreaLogbookTab tab) => tab switch
    {
        AreaLogbookTab.Rumors => "Rumors",
        AreaLogbookTab.Quests => "Quests",
        AreaLogbookTab.Reputation => "Reputation",
        AreaLogbookTab.BlessingsCurses => "Blessings and curses",
        AreaLogbookTab.KillsInjuries => "Kills and injuries",
        AreaLogbookTab.Background => "Background",
        AreaLogbookTab.Keys => "Keys",
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    /// <summary>Whether the viewer builds the tab's entries (the others show an empty page and a note).</summary>
    public static bool Built(AreaLogbookTab tab) => tab is AreaLogbookTab.Rumors or AreaLogbookTab.Quests or AreaLogbookTab.Reputation
        or AreaLogbookTab.BlessingsCurses or AreaLogbookTab.KillsInjuries or AreaLogbookTab.Background or AreaLogbookTab.Keys;

    /// <summary>
    /// The blessings and curses tab's entries (0x53FE30, LB19): the two lists are merged oldest first by the game time (day, then millisecond),
    /// a blessing before a curse of the same time; each list is already in the order it was appended, so it is merged, not sorted. The text is
    /// the name (message 10 × number of the list's table; no name for a number below 50 or a missing message).
    /// </summary>
    private static AreaLogbookEntry[] BlessingEntries(GameSessionOwner session, LogbookTexts? texts, long startYear, IReadOnlyList<string>? months,
        Func<MesMessage?, string> text)
    {
        var blessings = session.QueryHeroBlessings(SessionBlessingKind.Blessing);
        var curses = session.QueryHeroBlessings(SessionBlessingKind.Curse);
        if (blessings is null || curses is null) return [];
        var entries = new List<AreaLogbookEntry>(blessings.Count + curses.Count);
        AreaLogbookEntry Make(SessionBlessing item, bool curse) => new(item.Number, item.TimeMs, Date(item.TimeMs, startYear, months), null,
            text(texts?.BlessingName(curse, item.Number)), curse ? AreaLogbookStyle.Curse : AreaLogbookStyle.Blessing);
        int b = 0, c = 0;
        while (b < blessings.Count || c < curses.Count)
        {
            var takeBlessing = c >= curses.Count || (b < blessings.Count && blessings[b].TimeMs <= curses[c].TimeMs);
            entries.Add(takeBlessing ? Make(blessings[b++], false) : Make(curses[c++], true));
        }
        return [.. entries];
    }

    /// <summary>The project's words for the fixed lines and the injury kinds when the game folder's <c>logbk_ui.mes</c> has none (messages 26..37 are not read).</summary>
    private static readonly string[] KillLabels =
        ["Kills", "Highest level kill", "Lowest level kill", "Most positive kill", "Most negative kill", "Highest aptitude kill", "Lowest aptitude kill"];
    private static readonly string[] InjuryKinds = ["Blinded by", "Arm crippled by", "Legs crippled by", "Scarred by"];

    /// <summary>
    /// The kills and injuries tab's entries (0x53FF50, 0x5405C0, ledger LB21): nine fixed plain lines, "label: value" with the kill total and then, for each of
    /// the six slots (highest and lowest level, most positive and most negative alignment, highest and lowest aptitude), the victim's name from the
    /// description files or "-----" for none; an empty line (a page break); the injuries' head; then one line per injury, oldest first, the kind's text
    /// and the name of whoever caused it (an empty line when the name has no text). An injury is struck through unless it is current: the PC's critter
    /// flags pick the newest blinding (0x80), crippled legs (0x400) and crippled arm (0x200, then 0x100 the next older one) and its scar count the
    /// newest scars. A session without a hero has an empty page.
    /// </summary>
    private static AreaLogbookEntry[] KillEntries(GameSessionOwner session, LogbookTexts? texts, Func<int, string?> ui, Func<MesMessage?, string> text)
    {
        if (session.QueryHeroKillLog() is not { } log) return [];
        string? Name(int id) => texts?.DescriptionName(id) is { } message ? text(message) : null;
        var entries = new List<AreaLogbookEntry>();
        void Add(int number, string line, AreaLogbookStyle style) => entries.Add(new(number, 0, string.Empty, null, line, style));
        string Label(int line) => ui(LogbookTexts.FirstKillLabelMessage + line) ?? KillLabels[line];
        Add(0, $"{Label(0)}: {log.Total}", AreaLogbookStyle.Plain);
        var slots = new[] { log.HighestLevel, log.LowestLevel, log.MostPositiveAlignment, log.MostNegativeAlignment, log.HighestAptitude, log.LowestAptitude };
        for (var i = 0; i < slots.Length; i++) Add(i + 1, $"{Label(i + 1)}: {Name(slots[i].NameId) ?? "-----"}", AreaLogbookStyle.Plain);
        Add(7, string.Empty, AreaLogbookStyle.Plain);
        Add(8, ui(LogbookTexts.KillInjuryHeadMessage) ?? "Injuries", AreaLogbookStyle.Plain);
        var first = entries.Count;
        foreach (var injury in log.Injuries)
        {
            var name = Name(injury.NameId);
            var kind = ui(LogbookTexts.FirstInjuryKindMessage + injury.Kind) ?? InjuryKinds[injury.Kind];
            Add(injury.NameId, name is null ? string.Empty : $"{kind} {name}", AreaLogbookStyle.Struck);
        }
        // The newest current injuries lose the strike (0x54001C..0x54011E): searches run from the newest line down to the first injury line.
        var state = session.QueryHeroLife()?.Record.State;
        if (state is not null)
        {
            void Plain(int index) => entries[index] = entries[index] with { Style = AreaLogbookStyle.Plain };
            int Find(int from, int kind)
            {
                for (var i = from; i >= first; i--) if (log.Injuries[i - first].Kind == kind) return i;
                return -1;
            }
            var last = entries.Count - 1;
            if (state.Value.Conditions.HasFlag(LifeConditions.Blinded) && Find(last, 0) is >= 0 and var blinded) Plain(blinded);
            if (state.Value.Conditions.HasFlag(LifeConditions.CrippledLegsBoth) && Find(last, 2) is >= 0 and var legs) Plain(legs);
            // The two arm searches share one cursor: a crippled arm pair marks the two newest arm injuries, and when the first finds none the second marks nothing.
            var cursor = last;
            if (state.Value.Conditions.HasFlag(LifeConditions.CrippledArmsBoth))
            {
                var found = Find(cursor, 1);
                if (found >= 0) Plain(found);
                cursor = found >= 0 ? found - 1 : first - 1;
            }
            if (state.Value.Conditions.HasFlag(LifeConditions.CrippledArmOne) && Find(cursor, 1) is >= 0 and var arm) Plain(arm);
            for (int scars = state.Value.Scars, i = last; scars > 0 && i >= first; i--)
                if (log.Injuries[i - first].Kind == 3) { Plain(i); scars--; }
        }
        return [.. entries];
    }

    private static AreaLogbookEntry[] BackgroundEntries(GameSessionOwner session, LogbookTexts? texts)
    {
        if (session.QueryHeroBackgroundDescription() is not { } description || texts?.Backgrounds is not { } table) return [];
        var text = table.Text(description);
        if (text.Status == BackgroundTextStatus.Missing || text.Body.IsEmpty) return [];
        return [new(description, 0, string.Empty, null, System.Text.Encoding.Latin1.GetString(text.Body.Span), AreaLogbookStyle.Plain)];
    }

    /// <summary>The next built tab after <paramref name="tab"/> in the original's order (the keyboard's and the controller's tab switch).</summary>
    public static AreaLogbookTab Next(AreaLogbookTab tab)
    {
        var tabs = Enum.GetValues<AreaLogbookTab>();
        for (var step = 1; step <= tabs.Length; step++)
            if (tabs[((int)tab + step) % tabs.Length] is var next && Built(next)) return next;
        return tab;
    }
}
