using System.Globalization;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

public enum AreaSaveListMode { Save, Load }

/// <summary>The question the save window is asking, if any.</summary>
public enum AreaSavePrompt { None, ConfirmOverwrite, ConfirmDelete }

public enum AreaSaveCommandKind
{
    /// <summary>Nothing to do (no row, or a question was just asked or answered no).</summary>
    None,
    /// <summary>Write a new numbered save with <see cref="AreaSaveCommand.Description"/>.</summary>
    SaveNew,
    /// <summary>Overwrite <see cref="AreaSaveCommand.Name"/> (asked and confirmed).</summary>
    Overwrite,
    Load,
    /// <summary>Delete <see cref="AreaSaveCommand.Name"/> (asked and confirmed).</summary>
    Delete,
}

public readonly record struct AreaSaveCommand(AreaSaveCommandKind Kind, string? Name, string Description)
{
    public static AreaSaveCommand Nothing => new(AreaSaveCommandKind.None, null, "");
}

/// <summary>One row of the window: in save mode row 0 is the new save (no entry).</summary>
public sealed record AreaSaveRow(GameSaveEntry? Entry, string Title, string Detail);

/// <summary>
/// ARC-UI-010 save and load window state, Godot-free. Save mode lists a "new save" row first and then the saves; load mode lists the
/// saves only; both in the library's order (auto slot first, then newest first). As the original's save window (ledger SL3–SL7):
/// saving onto an existing row asks before overwriting it and keeps that save's description; a delete asks first and the auto
/// slot is never deleted; an empty or blank description saves under the slot name. Unlike the original, which selects nothing in
/// an empty save window, the new-save row is selected there so Return saves. Up and Down move the selection, Return confirms, Delete or Backspace asks to delete
/// (outside the description field), Escape answers a question with no or closes. The host runs the returned command and calls
/// <see cref="Refresh"/> with the new list. Caller-serialized.
/// </summary>
public sealed class AreaSaveList
{
    public AreaSaveList(AreaSaveListMode mode, IReadOnlyList<GameSaveEntry> entries)
    {
        Mode = mode;
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        // The original selects the first save on opening; with none, the new-save row (save) or nothing (load).
        Selected = mode == AreaSaveListMode.Save ? Entries.Count > 0 ? 1 : 0 : Entries.Count > 0 ? 0 : -1;
    }

    public AreaSaveListMode Mode { get; }
    public IReadOnlyList<GameSaveEntry> Entries { get; private set; }
    public int Selected { get; private set; }
    public AreaSavePrompt Prompt { get; private set; }
    /// <summary>The description typed for a save (save mode).</summary>
    public string Description { get; set; } = "";
    public string? Notice { get; private set; }

    /// <summary>The description a new save takes: as typed, or empty (shown as the slot name) when it is empty or only spaces (SL5).</summary>
    private string TypedDescription => string.IsNullOrWhiteSpace(Description) ? "" : Description;

    public int RowCount => Mode == AreaSaveListMode.Save ? Entries.Count + 1 : Entries.Count;
    private int Offset => Mode == AreaSaveListMode.Save ? 1 : 0;

    /// <summary>The selected save, or null for the new-save row or no selection.</summary>
    public GameSaveEntry? SelectedEntry => Selected - Offset is var i && i >= 0 && i < Entries.Count ? Entries[i] : null;

    public IReadOnlyList<AreaSaveRow> Rows()
    {
        var rows = new List<AreaSaveRow>(RowCount);
        if (Mode == AreaSaveListMode.Save) rows.Add(new(null, "<new save>", "a new numbered save"));
        rows.AddRange(Entries.Select(e => new AreaSaveRow(e, Title(e), Detail(e))));
        return rows;
    }

    public static string Title(GameSaveEntry entry) => (entry.IsAuto ? "[auto] " : "") + entry.DisplayDescription;

    public static string Detail(GameSaveEntry entry)
    {
        var saved = entry.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var details = entry.Summary is { } s
            ? $"{s.HeroName}, level {s.Level} | {s.Location} | {GameTime(s.GameTime)} | saved {saved}"
            : $"saved {saved} | no details";
        return entry.Details switch
        {
            GameSaveDetails.Stale => details + " | details may be out of date",
            GameSaveDetails.Damaged => details + " (details file damaged)",
            _ => details,
        } + $" | {entry.Name}";
    }

    public static string GameTime(GameSaveTime time) =>
        string.Create(CultureInfo.InvariantCulture, $"{time.Day:D2}.{time.Month:D2}.{time.Year} {time.Hour:D2}:{time.Minute:D2}");

    public void Select(int row)
    {
        if (Prompt != AreaSavePrompt.None || RowCount == 0) return;
        Selected = Math.Clamp(row, 0, RowCount - 1);
        Notice = null;
    }

    public void Move(int delta) => Select(Selected < 0 ? 0 : Selected + delta);

    /// <summary>Return or the Save/Load button: saves, loads or asks before overwriting; a pending question is answered yes.</summary>
    public AreaSaveCommand Confirm()
    {
        if (Prompt != AreaSavePrompt.None) return Answer(true);
        if (Selected < 0) { Notice = "select a save first"; return AreaSaveCommand.Nothing; }
        if (Mode == AreaSaveListMode.Load)
            return SelectedEntry is { } load ? new(AreaSaveCommandKind.Load, load.Name, "") : AreaSaveCommand.Nothing;
        if (SelectedEntry is null) return new(AreaSaveCommandKind.SaveNew, null, TypedDescription);
        Prompt = AreaSavePrompt.ConfirmOverwrite;
        Notice = $"Overwrite \"{SelectedEntry.DisplayDescription}\"? Return or Yes overwrites, Escape or No keeps it";
        return AreaSaveCommand.Nothing;
    }

    /// <summary>Delete or Backspace (outside the description field) or the Delete button: asks first; the auto slot is refused.</summary>
    public AreaSaveCommand RequestDelete()
    {
        if (Prompt != AreaSavePrompt.None || SelectedEntry is not { } entry) return AreaSaveCommand.Nothing;
        if (entry.IsAuto) { Notice = "the auto-save cannot be deleted"; return AreaSaveCommand.Nothing; }
        Prompt = AreaSavePrompt.ConfirmDelete;
        Notice = $"Delete \"{entry.DisplayDescription}\"? Return or Yes deletes, Escape or No keeps it";
        return AreaSaveCommand.Nothing;
    }

    public AreaSaveCommand Answer(bool yes)
    {
        var prompt = Prompt;
        Prompt = AreaSavePrompt.None;
        Notice = null;
        if (!yes || SelectedEntry is not { } entry) return AreaSaveCommand.Nothing;
        return prompt switch
        {
            // The original puts the listed save's own description in the field before it asks, so an overwrite keeps it (SL5).
            AreaSavePrompt.ConfirmOverwrite => new(AreaSaveCommandKind.Overwrite, entry.Name,
                entry.Summary?.Description ?? (entry.IsAuto ? GameSaveLibrary.AutoDescription : "")),
            AreaSavePrompt.ConfirmDelete => new(AreaSaveCommandKind.Delete, entry.Name, ""),
            _ => AreaSaveCommand.Nothing,
        };
    }

    /// <summary>Escape: a question is answered no (true: the window stays); otherwise false and the host closes the window.</summary>
    public bool Cancel()
    {
        if (Prompt == AreaSavePrompt.None) return false;
        Answer(false);
        return true;
    }

    /// <summary>A new list after a save or delete: the selection follows <paramref name="select"/> when it is listed, else stays in range.</summary>
    public void Refresh(IReadOnlyList<GameSaveEntry> entries, string? select, string? notice)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        Prompt = AreaSavePrompt.None;
        var found = select is null ? -1 : entries.ToList().FindIndex(e => e.Name == select);
        Selected = found >= 0 ? found + Offset : RowCount == 0 ? -1 : Math.Clamp(Selected, 0, RowCount - 1);
        Notice = notice;
    }

    /// <summary>The details a save records: the hero's name and level (or the diagnostic profile), the location and the game time.</summary>
    public static GameSaveSummary Summary(GameSessionOwner session, string description, string location)
    {
        ArgumentNullException.ThrowIfNull(session);
        var hero = session.QueryHero();
        var level = session.QueryHeroExperience()?.Level ?? hero?.Level ?? 0;
        var clock = session.QueryClockSnapshot();
        var limits = GameSaveLimits.Default;
        // Typed or data text is kept to one line of plain characters within the library's bounds.
        static string Fit(string text, int length)
        {
            var plain = new string(text.Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).ToArray());
            return plain.Length <= length ? plain : plain[..length];
        }
        return new(Fit(description, limits.MaxDescriptionLength), Fit(hero is null ? "diagnostic PC" : AreaStartingHero.Name(hero), limits.MaxHeroNameLength),
            level, Fit(location, limits.MaxLocationLength), session.QueryActor().Map.RuntimeMap,
            new(clock.Year, clock.Month, clock.DayOfMonth, clock.Hour, clock.Minute));
    }
}
