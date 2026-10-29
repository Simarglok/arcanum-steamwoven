using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arcanum.Simulation;

namespace Arcanum.Session;

public enum GameSaveFailure
{
    None,
    /// <summary>The name is not the auto slot or a numbered slot (<c>slot0000</c>..<c>slot9998</c>).</summary>
    BadName,
    /// <summary>No save file under that name.</summary>
    NoSuchSave,
    /// <summary>The auto slot is never deleted (the original's delete helper refuses its auto-save name).</summary>
    Protected,
    /// <summary>The next slot number would reach 9999 (the original refuses a new save there).</summary>
    SlotNumbersExhausted,
    /// <summary>The summary or thumbnail breaks <see cref="GameSaveLimits"/>; nothing was written.</summary>
    InvalidSummary,
    /// <summary>The snapshot was not written (<see cref="GameSaveWriteResult.Snapshot"/> says why); the previous save is untouched.</summary>
    SnapshotFailed,
    /// <summary>The save file could not be deleted; nothing was removed.</summary>
    DeleteFailed,
    /// <summary>The save folder could not be listed.</summary>
    ListFailed,
}

/// <summary>What the listing knows about a save's details file.</summary>
public enum GameSaveDetails
{
    /// <summary>Read and written with this snapshot (its recorded byte count matches the file).</summary>
    Present,
    /// <summary>No details file (a save from before ARC-UI-010, or its details write failed).</summary>
    Missing,
    /// <summary>The details file is not one this build reads.</summary>
    Damaged,
    /// <summary>The details file was written for another snapshot (an interrupted overwrite); shown, but marked.</summary>
    Stale,
}

/// <summary>A game date and clock time as the session clock reports it (<see cref="Second"/> is 0 in a details file of schema 1).</summary>
public readonly record struct GameSaveTime(long Year, int Month, int Day, int Hour, int Minute, int Second = 0);

/// <summary>The player-facing details of one save, supplied by the host (the session holds no hero name or map title).
/// Texts are single-line and bounded by <see cref="GameSaveLimits"/>. ARC-UI-067: the hero's portrait id (0 for none) and the story
/// state (null in a details file of schema 1, which did not record it), as the original's save info holds them (ledger MW22).</summary>
public sealed record GameSaveSummary(string Description, string HeroName, int Level, string Location, int RuntimeMap, GameSaveTime GameTime,
    int Portrait = 0, int? StoryState = null);

/// <summary>One listed save. <see cref="Summary"/> is null unless <see cref="Details"/> is Present or Stale. <see cref="SavedAt"/> is the
/// save file's last write time, which orders the list as the original orders it by file date.</summary>
public sealed record GameSaveEntry(string Name, bool IsAuto, long Bytes, DateTime SavedAtUtc, GameSaveDetails Details, GameSaveSummary? Summary,
    bool HasThumbnail)
{
    /// <summary>The description to show: the saved one, else the auto slot's fixed text or the slot name (the original uses the slot name
    /// for an empty description).</summary>
    public string DisplayDescription => Summary?.Description is { Length: > 0 } description ? description
        : IsAuto ? GameSaveLibrary.AutoDescription : Name;
}

public sealed record GameSaveListResult(IReadOnlyList<GameSaveEntry> Entries, GameSaveFailure Failure, bool Truncated, string? Detail);

/// <summary>One save, overwrite or auto save. <see cref="Saved"/> is true once the snapshot is written; a details or thumbnail
/// write that then fails leaves the save loadable and is reported in <see cref="Warning"/>.</summary>
public sealed record GameSaveWriteResult(string? Name, GameSaveFailure Failure, SaveSlotWriteResult? Snapshot, string? Warning, string? Detail)
{
    public bool Saved => Failure == GameSaveFailure.None;
}

public sealed record GameSaveDeleteResult(GameSaveFailure Failure, string? Warning, string? Detail)
{
    public bool Deleted => Failure == GameSaveFailure.None;
}

/// <summary>Bounds of a save folder. The defaults hold the original's 9999 slot names and a 468×300 preview as PNG.</summary>
public sealed record GameSaveLimits(int MaxEntries = 10_000, int MaxDetailsBytes = 16 * 1024, int MaxThumbnailBytes = 2 * 1024 * 1024,
    int MaxDescriptionLength = 80, int MaxHeroNameLength = 64, int MaxLocationLength = 128)
{
    public static GameSaveLimits Default { get; } = new();
}

/// <summary>
/// ARC-UI-010 save list: one folder of saves, each a <see cref="GameSessionSaveSlot"/> file (<c>NAME.json</c>, the snapshot), a
/// details file (<c>NAME.meta.json</c>: description, hero, level, location, game time, portrait, story state and the snapshot's byte count) and an optional
/// PNG preview (<c>NAME.png</c>). The auto slot is the quick save file (<c>quicksave.json</c>, the original's <c>SlotAuto</c>
/// "Auto-Save" that F7 writes); numbered saves are <c>slot0000</c>..<c>slot9998</c>. As the original (ledger SL1–SL8):
/// a new save takes the highest number plus one (<c>slot0000</c> when there is none) and is refused at 9999; the list shows the
/// auto slot first, then the rest newest first by file date; the latest save (for F8 and a later "continue") is the newest of all,
/// the auto slot included; the auto slot cannot be deleted. The snapshot is written first and atomically, so a failed save keeps the
/// previous one; the details and preview follow and are display only (a stale or missing details file never blocks a load).
/// Caller-serialized; one process per folder; no Godot.
/// </summary>
public sealed class GameSaveLibrary
{
    public const string AutoName = "quicksave";
    public const string AutoDescription = "Auto-Save";
    public const int FirstNumberRefused = 9999;
    public const string DetailsSuffix = ".meta.json";
    public const string ThumbnailSuffix = ".png";
    private const string SnapshotSuffix = ".json";
    private const string DetailsFormat = "arcanum-steamwoven-save-details";
    /// <summary>ARC-UI-067: schema 2 adds the portrait, the story state and the game time's seconds; schema 1 files still read.</summary>
    private const int DetailsSchema = 2, OldDetailsSchema = 1;
    private static readonly Regex Names = new("^(quicksave|slot(?!9999)[0-9]{4})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public GameSaveLibrary(string directory, GameSaveLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("SAVE_LIBRARY_PATH_NOT_ABSOLUTE", nameof(directory));
        Directory = Path.GetFullPath(directory);
        Limits = limits ?? GameSaveLimits.Default;
        if (Limits.MaxEntries < 1 || Limits.MaxDetailsBytes < 256 || Limits.MaxThumbnailBytes < PngSignature.Length
            || Limits.MaxDescriptionLength < 1 || Limits.MaxHeroNameLength < 1 || Limits.MaxLocationLength < 1)
            throw new ArgumentException("SAVE_LIBRARY_LIMITS_INVALID", nameof(limits));
    }

    public string Directory { get; }
    public GameSaveLimits Limits { get; }

    public static bool IsValidName(string? name) => name is not null && Names.IsMatch(name);

    /// <summary>The snapshot file of a save, for reading and loading through the ordinary slot.</summary>
    public GameSessionSaveSlot Slot(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("SAVE_NAME_INVALID", nameof(name));
        return new(Path.Combine(Directory, name + SnapshotSuffix));
    }

    /// <summary>The auto slot: the quick save file F7 writes (ARC-WORLD-011's slot, unchanged).</summary>
    public GameSessionSaveSlot AutoSlot => Slot(AutoName);

    private string DetailsPath(string name) => Path.Combine(Directory, name + DetailsSuffix);
    private string ThumbnailPath(string name) => Path.Combine(Directory, name + ThumbnailSuffix);

    /// <summary>The saves in list order: the auto slot first, then newest first by file date (ties: the higher name first).</summary>
    [OriginalFunction(0x403C10, OriginalFunctionScope.Partial)]
    public GameSaveListResult List()
    {
        var scan = Scan();
        if (scan.Failure != GameSaveFailure.None) return scan;
        var ordered = scan.Entries.OrderByDescending(e => e.IsAuto).ThenByDescending(e => e.SavedAtUtc).ThenByDescending(e => e.Name, StringComparer.Ordinal).ToArray();
        return scan with { Entries = Array.AsReadOnly(ordered) };
    }

    /// <summary>The newest save by file date, the auto slot included (the original's latest-save helper), or null when there is none.</summary>
    [OriginalFunction(0x403850, OriginalFunctionScope.Partial)]
    public GameSaveEntry? Latest()
    {
        // Only the file dates decide; the details files are not read (F8 runs this on every press).
        var scan = Scan(readDetails: false);
        return scan.Entries.OrderByDescending(e => e.SavedAtUtc).ThenByDescending(e => e.Name, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>A new numbered save: the highest number in the folder plus one, slot0000 for the first.</summary>
    [OriginalFunction(0x5435D0, OriginalFunctionScope.Partial)]
    public GameSaveWriteResult Create(GameSessionSnapshot snapshot, GameSaveSummary summary, byte[]? thumbnail = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(summary);
        var scan = Scan();
        if (scan.Failure != GameSaveFailure.None) return new(null, scan.Failure, null, null, scan.Detail);
        if (scan.Truncated) return new(null, GameSaveFailure.ListFailed, null, null, "SAVE_FOLDER_HAS_TOO_MANY_ENTRIES");
        var highest = scan.Entries.Where(e => !e.IsAuto).Select(e => int.Parse(e.Name.AsSpan(4), provider: System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(-1).Max();
        var next = highest + 1;
        if (next >= FirstNumberRefused) return new(null, GameSaveFailure.SlotNumbersExhausted, null, null, $"NEXT_SLOT_NUMBER {next}");
        return Write($"slot{next:D4}", snapshot, summary, thumbnail);
    }

    /// <summary>Writes (or overwrites) the named save. The host asks the player before overwriting an existing one.</summary>
    public GameSaveWriteResult Write(string name, GameSessionSnapshot snapshot, GameSaveSummary summary, byte[]? thumbnail = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(summary);
        if (!IsValidName(name)) return new(null, GameSaveFailure.BadName, null, null, "SAVE_NAME_INVALID");
        if (SummaryRefusal(summary) is { } refusal) return new(name, GameSaveFailure.InvalidSummary, null, null, refusal);
        if (thumbnail is { } png && (png.Length > Limits.MaxThumbnailBytes || !png.AsSpan().StartsWith(PngSignature)))
            return new(name, GameSaveFailure.InvalidSummary, null, null, png.Length > Limits.MaxThumbnailBytes ? "THUMBNAIL_TOO_LARGE" : "THUMBNAIL_NOT_PNG");
        var written = Slot(name).Write(snapshot);
        if (!written.Saved) return new(name, GameSaveFailure.SnapshotFailed, written, null, $"{written.Failure} {written.Detail}");
        var warnings = new List<string>();
        var details = Strict.GetBytes(EncodeDetails(summary, written.Bytes));
        // Details or a preview that were not written are removed, so the list never shows another snapshot's (Missing instead).
        var detailsFailure = details.Length > Limits.MaxDetailsBytes ? "DETAILS_TOO_LARGE" : WriteAtomic(DetailsPath(name), details);
        if (detailsFailure is not null)
            warnings.Add("DETAILS_NOT_WRITTEN " + detailsFailure + (TryDelete(DetailsPath(name)) is { } old ? " OLD_DETAILS_NOT_REMOVED " + old : ""));
        if (thumbnail is { } image)
        {
            if (WriteAtomic(ThumbnailPath(name), image) is { } thumbnailFailure)
                warnings.Add("THUMBNAIL_NOT_WRITTEN " + thumbnailFailure + (TryDelete(ThumbnailPath(name)) is { } old ? " OLD_THUMBNAIL_NOT_REMOVED " + old : ""));
        }
        // A save without a preview must not show the previous save's preview.
        else if (TryDelete(ThumbnailPath(name)) is { } stale) warnings.Add("OLD_THUMBNAIL_NOT_REMOVED " + stale);
        return new(name, GameSaveFailure.None, written, warnings.Count == 0 ? null : string.Join("; ", warnings), null);
    }

    /// <summary>Deletes a numbered save (snapshot first, so a failure removes nothing the list shows); the auto slot is refused.</summary>
    [OriginalFunction(0x403790, OriginalFunctionScope.Partial)]
    public GameSaveDeleteResult Delete(string name)
    {
        if (!IsValidName(name)) return new(GameSaveFailure.BadName, null, "SAVE_NAME_INVALID");
        if (name == AutoName) return new(GameSaveFailure.Protected, null, "AUTO_SLOT_IS_PROTECTED");
        var slot = Slot(name);
        if (!slot.Exists) return new(GameSaveFailure.NoSuchSave, null, "NO_SAVE_FILE");
        if (TryDelete(slot.Path) is { } failure) return new(GameSaveFailure.DeleteFailed, null, failure);
        var leftovers = new[] { DetailsPath(name), ThumbnailPath(name), slot.TemporaryPath, DetailsPath(name) + GameSessionSaveSlot.TemporarySuffix,
            ThumbnailPath(name) + GameSessionSaveSlot.TemporarySuffix }.Select(TryDelete).Where(f => f is not null).ToArray();
        return new(GameSaveFailure.None, leftovers.Length == 0 ? null : "LEFTOVERS_NOT_REMOVED " + string.Join(",", leftovers), null);
    }

    /// <summary>The preview of a save (PNG bytes), or null when it has none or it is unreadable or too large.</summary>
    public byte[]? ReadThumbnail(string name)
    {
        if (!IsValidName(name)) return null;
        var bytes = ReadBounded(ThumbnailPath(name), Limits.MaxThumbnailBytes);
        return bytes is not null && bytes.AsSpan().StartsWith(PngSignature) ? bytes : null;
    }

    private GameSaveListResult Scan(bool readDetails = true)
    {
        var entries = new List<GameSaveEntry>();
        var truncated = false;
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return new([], GameSaveFailure.None, false, null);
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + SnapshotSuffix))
            {
                var file = Path.GetFileName(path);
                if (file.EndsWith(DetailsSuffix, StringComparison.Ordinal)) continue;
                var name = file[..^SnapshotSuffix.Length];
                if (!IsValidName(name)) continue;
                if (entries.Count == Limits.MaxEntries) { truncated = true; break; }
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                var (details, summary) = readDetails ? ReadDetails(name, info.Length) : (GameSaveDetails.Missing, null);
                entries.Add(new(name, name == AutoName, info.Length, info.LastWriteTimeUtc, details, summary, File.Exists(ThumbnailPath(name))));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new([], GameSaveFailure.ListFailed, false, e.GetType().Name);
        }
        return new(Array.AsReadOnly(entries.ToArray()), GameSaveFailure.None, truncated, truncated ? "SAVE_FOLDER_HAS_TOO_MANY_ENTRIES" : null);
    }

    private (GameSaveDetails, GameSaveSummary?) ReadDetails(string name, long snapshotBytes)
    {
        var path = DetailsPath(name);
        if (!File.Exists(path)) return (GameSaveDetails.Missing, null);
        var bytes = ReadBounded(path, Limits.MaxDetailsBytes);
        if (bytes is null) return (GameSaveDetails.Damaged, null);
        string text;
        try { text = Strict.GetString(bytes); }
        catch (DecoderFallbackException) { return (GameSaveDetails.Damaged, null); }
        if (DecodeDetails(text) is not { } decoded || SummaryRefusal(decoded.Summary) is not null) return (GameSaveDetails.Damaged, null);
        return (decoded.SnapshotBytes == snapshotBytes ? GameSaveDetails.Present : GameSaveDetails.Stale, decoded.Summary);
    }

    private string? SummaryRefusal(GameSaveSummary summary)
    {
        static bool SingleLine(string text) => !text.Any(char.IsControl) && !text.Any(char.IsSurrogate);
        if (summary.Description is null || summary.Description.Length > Limits.MaxDescriptionLength || !SingleLine(summary.Description)) return "DESCRIPTION_INVALID";
        if (summary.HeroName is null || summary.HeroName.Length > Limits.MaxHeroNameLength || !SingleLine(summary.HeroName)) return "HERO_NAME_INVALID";
        if (summary.Location is null || summary.Location.Length > Limits.MaxLocationLength || !SingleLine(summary.Location)) return "LOCATION_INVALID";
        if (summary.Level is < 0 or > 1000) return "LEVEL_INVALID";
        if (summary.RuntimeMap < 0) return "MAP_INVALID";
        var time = summary.GameTime;
        if (time.Year is < 0 or > 1_000_000_000 || time.Month is < 1 or > 12 || time.Day is < 1 or > 31 || time.Hour is < 0 or > 23 || time.Minute is < 0 or > 59
            || time.Second is < 0 or > 59)
            return "GAME_TIME_INVALID";
        if (summary.Portrait < 0) return "PORTRAIT_INVALID";
        // The story state starts at 0 and only rises (ledger SS1).
        if (summary.StoryState < 0) return "STORY_STATE_INVALID";
        return null;
    }

    private static string EncodeDetails(GameSaveSummary summary, long snapshotBytes)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("format", DetailsFormat);
            json.WriteNumber("schema", DetailsSchema);
            json.WriteNumber("snapshotBytes", snapshotBytes);
            json.WriteString("description", summary.Description);
            json.WriteString("hero", summary.HeroName);
            json.WriteNumber("level", summary.Level);
            json.WriteString("location", summary.Location);
            json.WriteNumber("map", summary.RuntimeMap);
            json.WriteStartArray("gameTime");
            json.WriteNumberValue(summary.GameTime.Year); json.WriteNumberValue(summary.GameTime.Month); json.WriteNumberValue(summary.GameTime.Day);
            json.WriteNumberValue(summary.GameTime.Hour); json.WriteNumberValue(summary.GameTime.Minute); json.WriteNumberValue(summary.GameTime.Second);
            json.WriteEndArray();
            json.WriteNumber("portrait", summary.Portrait);
            if (summary.StoryState is { } story) json.WriteNumber("storyState", story);
            else json.WriteNull("storyState");
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static readonly string[] OldDetailsProperties = ["format", "schema", "snapshotBytes", "description", "hero", "level", "location", "map", "gameTime"];
    private static readonly string[] DetailsProperties = [.. OldDetailsProperties, "portrait", "storyState"];

    /// <summary>Strict: exactly the written properties of schema 2 (or of schema 1, without the portrait, the story state and the
    /// seconds), of their kinds; anything else is a damaged details file.</summary>
    private static (long SnapshotBytes, GameSaveSummary Summary)? DecodeDetails(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("schema", out var schemaValue) || schemaValue.ValueKind != JsonValueKind.Number) return null;
            var schema = schemaValue.GetInt32();
            var expected = schema == DetailsSchema ? DetailsProperties : schema == OldDetailsSchema ? OldDetailsProperties : null;
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (expected is null || names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length
                || !names.All(expected.Contains)) return null;
            if (root.GetProperty("format").GetString() != DetailsFormat) return null;
            var time = root.GetProperty("gameTime");
            if (time.ValueKind != JsonValueKind.Array || time.GetArrayLength() != (schema == DetailsSchema ? 6 : 5)) return null;
            var t = time.EnumerateArray().ToArray();
            var story = schema == DetailsSchema ? root.GetProperty("storyState") : default;
            var summary = new GameSaveSummary(root.GetProperty("description").GetString()!, root.GetProperty("hero").GetString()!,
                root.GetProperty("level").GetInt32(), root.GetProperty("location").GetString()!, root.GetProperty("map").GetInt32(),
                new(t[0].GetInt64(), t[1].GetInt32(), t[2].GetInt32(), t[3].GetInt32(), t[4].GetInt32(), schema == DetailsSchema ? t[5].GetInt32() : 0),
                schema == DetailsSchema ? root.GetProperty("portrait").GetInt32() : 0,
                schema != DetailsSchema || story.ValueKind == JsonValueKind.Null ? null : story.GetInt32());
            var bytes = root.GetProperty("snapshotBytes").GetInt64();
            return bytes < 0 ? null : (bytes, summary);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException) { return null; }
    }

    /// <summary>Writes a sibling temporary file, flushes and reads it back, then moves it over the target; null on success.</summary>
    private static string? WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + GameSessionSaveSlot.TemporarySuffix;
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (!File.ReadAllBytes(temporary).AsSpan().SequenceEqual(bytes)) { TryDelete(temporary); return "READ_BACK_MISMATCH"; }
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
            return e.GetType().Name;
        }
    }

    private static byte[]? ReadBounded(string path, int limit)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > limit) return null;
            var buffer = new byte[(int)stream.Length + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
            return total > limit || total == buffer.Length ? null : buffer[..total];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>Deletes a file if it is there; null on success or when it was absent.</summary>
    private static string? TryDelete(string path)
    {
        try { File.Delete(path); return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.GetType().Name; }
    }
}
