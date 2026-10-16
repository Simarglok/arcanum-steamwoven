using System.Text.Encodings.Web;
using System.Text.Json;

namespace Arcanum.Data.Hd;

/// <summary><see cref="Changed"/>: <c>manifest.json</c> on disk is no longer the manifest
/// the pack was opened from (another program wrote it); nothing was written, reopen the
/// pack. <see cref="ManifestUnreadable"/>: the manifest is missing, unreadable or a link.
/// <see cref="Invalid"/>: the rewritten manifest would not open (not expected; nothing was
/// written). <see cref="WriteFailed"/>: the file system refused the write; the old
/// manifest is unchanged.</summary>
public enum HdReviewSaveStatus { Saved, Unchanged, Changed, ManifestUnreadable, Invalid, WriteFailed }

/// <param name="Pack">After <see cref="HdReviewSaveStatus.Saved"/> the pack as written
/// (a new identity: the manifest hash changed), after <see cref="HdReviewSaveStatus.Unchanged"/>
/// the same pack, otherwise null.</param>
public sealed record HdReviewSaveResult(HdReviewSaveStatus Status, HdPack? Pack, string? Detail = null);

/// <summary>Writes review marks into a pack's <c>manifest.json</c> (ARC-RENDER-015; the
/// field is defined by ARC-DATA-014). Only the <c>review</c> values change: every other
/// property, value and order is copied, and the rewritten manifest is parsed and checked
/// before it replaces the old one by an atomic rename in the pack directory. A marked pack
/// has a new identity (the manifest hash), so HD caches keyed by it reload; the frame
/// files are not touched.</summary>
public static class HdPackReview
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

    /// <summary>Sets the review of the given entries (by <see cref="HdPackEntry.Index"/>) and,
    /// when <paramref name="packReview"/> is set, of the whole pack. Marks equal to the
    /// current ones are no change; if nothing changes nothing is written. Throws only for a
    /// caller error (an index outside the pack or an undefined status).</summary>
    public static HdReviewSaveResult Save(HdPack pack, IReadOnlyDictionary<int, HdReviewStatus> entries, HdReviewStatus? packReview = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var (index, status) in entries)
        {
            if (index < 0 || index >= pack.Entries.Count) throw new ArgumentOutOfRangeException(nameof(entries), $"Entry {index} is not in the pack.");
            if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(entries), "Undefined review status.");
        }
        if (packReview is { } p && !Enum.IsDefined(p)) throw new ArgumentOutOfRangeException(nameof(packReview));
        var changes = entries.Where(e => pack.Entries[e.Key].Review != e.Value).ToDictionary(e => e.Key, e => e.Value);
        var newPackReview = packReview is { } r && r != pack.Review ? r : (HdReviewStatus?)null;
        if (changes.Count == 0 && newPackReview is null) return new(HdReviewSaveStatus.Unchanged, pack);

        var directory = pack.Directory;
        var options = pack.Options;
        if (HdPackPath.HasLink(directory, HdPack.ManifestFile)) return new(HdReviewSaveStatus.ManifestUnreadable, null, "manifest is a link");
        var current = HdPack.ReadFile(directory, HdPack.ManifestFile, options.MaxManifestBytes, out var readStatus);
        if (current is null) return new(HdReviewSaveStatus.ManifestUnreadable, null, readStatus.ToString());
        if (HdPackPath.Sha256(current) != pack.Identity.ManifestSha256) return new(HdReviewSaveStatus.Changed, null, "manifest changed since the pack was opened");

        byte[] rewritten;
        HdPack written;
        try
        {
            using (var document = JsonDocument.Parse(current, HdJson.Options)) rewritten = Rewrite(document.RootElement, changes, newPackReview);
            using var check = JsonDocument.Parse(rewritten, HdJson.Options);
            var opened = HdPack.Parse(directory, rewritten, check.RootElement, options);
            if (opened.Status != HdPackOpenStatus.Opened) return new(HdReviewSaveStatus.Invalid, null, $"{opened.Status}: {opened.Detail}");
            written = opened.Pack!;
        }
        catch (JsonException error) { return new(HdReviewSaveStatus.Invalid, null, error.GetType().Name); }
        catch (HdFormatException error) { return new(HdReviewSaveStatus.Invalid, null, error.Message); }
        if (!Matches(pack, written, changes, newPackReview)) return new(HdReviewSaveStatus.Invalid, null, "rewritten manifest differs beyond its review marks");

        var target = HdPackPath.Full(directory, HdPack.ManifestFile);
        var temporary = Path.Combine(directory, $".manifest-review-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(rewritten);
                stream.Flush(flushToDisk: true);
            }
            // Last check before the rename: another writer between the first read and now
            // keeps its manifest.
            var again = HdPack.ReadFile(directory, HdPack.ManifestFile, options.MaxManifestBytes, out _);
            if (again is null || HdPackPath.Sha256(again) != pack.Identity.ManifestSha256 || HdPackPath.HasLink(directory, HdPack.ManifestFile))
            {
                File.Delete(temporary);
                return new(HdReviewSaveStatus.Changed, null, "manifest changed while saving");
            }
            File.Move(temporary, target, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { /* Leave a hidden temporary file. */ }
            return new(HdReviewSaveStatus.WriteFailed, null, error.GetType().Name);
        }
        return new(HdReviewSaveStatus.Saved, written);
    }

    internal static string Name(HdReviewStatus status) => status switch
    {
        HdReviewStatus.Approved => "approved",
        HdReviewStatus.Rejected => "rejected",
        _ => "unreviewed",
    };

    private static byte[] Rewrite(JsonElement root, IReadOnlyDictionary<int, HdReviewStatus> changes, HdReviewStatus? packReview)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("review") && packReview is { } review) writer.WriteString("review", Name(review));
                else if (property.NameEquals("entries"))
                {
                    writer.WriteStartArray("entries");
                    var index = 0;
                    foreach (var entry in property.Value.EnumerateArray())
                    {
                        if (changes.TryGetValue(index, out var mark)) WriteEntry(writer, entry, mark);
                        else entry.WriteTo(writer);
                        index++;
                    }
                    writer.WriteEndArray();
                }
                else property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteEntry(Utf8JsonWriter writer, JsonElement entry, HdReviewStatus mark)
    {
        writer.WriteStartObject();
        foreach (var property in entry.EnumerateObject())
        {
            if (property.NameEquals("review")) writer.WriteString("review", Name(mark));
            else property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    // Everything the pack binds by stays equal; only the marks asked for changed.
    private static bool Matches(HdPack before, HdPack after, IReadOnlyDictionary<int, HdReviewStatus> changes, HdReviewStatus? packReview)
    {
        if (after.Id != before.Id || after.Scale != before.Scale || after.RecipeSha256 != before.RecipeSha256 || after.RecipeFile != before.RecipeFile
            || after.Build != before.Build || after.Entries.Count != before.Entries.Count || after.Review != (packReview ?? before.Review))
            return false;
        for (var i = 0; i < before.Entries.Count; i++)
        {
            var (a, b) = (before.Entries[i], after.Entries[i]);
            var review = changes.TryGetValue(i, out var mark) ? mark : a.Review;
            if (b != a with { Review = review, TileRgbaSha256 = b.TileRgbaSha256 } || b.Review != review
                || !(a.TileRgbaSha256 ?? []).SequenceEqual(b.TileRgbaSha256 ?? []))
                return false;
        }
        return true;
    }
}

/// <summary>Why a pack frame cannot be compared with this ART: <see cref="ArtMismatch"/>,
/// the ART bytes are not the ones the frame was built from (another DAT or version);
/// <see cref="ViewOutOfRange"/>, the frame's view is longer than these bytes;
/// <see cref="LegacyUndecodable"/>, the frame does not decode from the view;
/// <see cref="PaletteMismatch"/>, the palette slot falls back to another slot here;
/// <see cref="Refused"/>, the pack refused the frame (<see cref="HdReviewFrame.Hd"/> says why).
/// <see cref="Compared"/> holds both images.</summary>
public enum HdReviewFrameStatus { Compared, ArtMismatch, ViewOutOfRange, LegacyUndecodable, PaletteMismatch, Refused }

/// <param name="Legacy">The Legacy frame decoded from the entry's view of the ART bytes.</param>
/// <param name="Hd">The pack's answer; its pixels are present for <see cref="HdReviewFrameStatus.Compared"/>.</param>
/// <param name="AlphaMatches">For a compared frame: the recipe's alpha check passed. A
/// failed check is shown in review but the game draws Legacy for that frame.</param>
public sealed record HdReviewFrame(HdReviewFrameStatus Status, HdPackEntry Entry, ArtFrame? Legacy, HdFrameResult? Hd, bool AlphaMatches, string? Detail = null);

/// <summary>The before/after pair of one pack frame (ARC-RENDER-015): the Legacy frame
/// decoded from the given ART bytes exactly as the frame key says (view, physical group,
/// frame, effective palette) and the pack's HD frame bound to it. Review marks do not hide
/// a frame here. Godot-free; safe to call off the main thread.</summary>
public static class HdReviewComparison
{
    /// <param name="artPayloadSha256">SHA-256 of <paramref name="artBytes"/> when the caller
    /// already has it (it is recomputed otherwise).</param>
    public static HdReviewFrame Compare(HdPack pack, HdPackEntry entry, ReadOnlySpan<byte> artBytes, string? artPayloadSha256 = null,
        ArtDecoderOptions? decoderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Index < 0 || entry.Index >= pack.Entries.Count || pack.Entries[entry.Index] != entry)
            throw new ArgumentException("The entry is not one of the pack's entries.", nameof(entry));
        if (entry.Kind != HdEntryKind.Sprite || entry.Key is not { } key) throw new ArgumentException("Only sprite entries are compared.", nameof(entry));

        var sha = artPayloadSha256 ?? HdPackPath.Sha256(artBytes);
        if (sha != key.ArtSha256) return new(HdReviewFrameStatus.ArtMismatch, entry, null, null, false);
        if (key.View > artBytes.Length) return new(HdReviewFrameStatus.ViewOutOfRange, entry, null, null, false);
        ArtFrame legacy;
        try { legacy = ArtDecoder.Decode(artBytes[..key.View], new(key.Group, key.Frame, key.Palette), decoderOptions); }
        catch (Exception error) when (error is ArtException or ArgumentException)
        {
            return new(HdReviewFrameStatus.LegacyUndecodable, entry, null, null, false, error.GetType().Name);
        }
        if (legacy.EffectivePaletteSlot != key.Palette) return new(HdReviewFrameStatus.PaletteMismatch, entry, legacy, null, false);

        var rgba = legacy.Rgba.ToArray();
        var query = new HdSpriteQuery(key, entry.NameSha256!, new(legacy.Width, legacy.Height, HdPackPath.Sha256(rgba)));
        var hd = pack.ResolveForReview(query, rgba, out var alphaMatches);
        return hd.Status == HdFrameStatus.Hd
            ? new(HdReviewFrameStatus.Compared, entry, legacy, hd, alphaMatches)
            : new(HdReviewFrameStatus.Refused, entry, legacy, hd, false);
    }
}

/// <summary>Lookups the review needs over one pack's sprite entries: the entries of an
/// ART payload, the entry of a frame as a viewer shows it, review counts and stepping in
/// manifest order.</summary>
public sealed class HdReviewIndex
{
    private readonly Dictionary<string, List<HdPackEntry>> _byArt;
    private readonly Dictionary<string, List<HdPackEntry>> _byName;

    public HdReviewIndex(HdPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        Pack = pack;
        Sprites = pack.Entries.Where(e => e.Kind == HdEntryKind.Sprite).ToArray();
        _byArt = Sprites.GroupBy(e => e.Key!.Value.ArtSha256, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        _byName = Sprites.GroupBy(e => e.NameSha256!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    public HdPack Pack { get; }
    /// <summary>Sprite entries in manifest order (terrain chunks are not reviewed here).</summary>
    public IReadOnlyList<HdPackEntry> Sprites { get; }
    public int Count(HdReviewStatus status) => Sprites.Count(e => e.Review == status);
    public IReadOnlyList<HdPackEntry> ForArt(string artSha256) => _byArt.TryGetValue(artSha256, out var list) ? list : [];
    public IReadOnlyList<HdPackEntry> ForName(string nameSha256) => _byName.TryGetValue(nameSha256, out var list) ? list : [];

    /// <summary>The entry for a frame shown from the whole ART payload: the exact key with
    /// the full view, otherwise the same group/frame/palette under another view (a pack
    /// built from a shorter view of the same bytes); null when the pack has none.</summary>
    public HdPackEntry? Find(string artSha256, int view, int group, int frame, int palette)
    {
        HdPackEntry? other = null;
        foreach (var entry in ForArt(artSha256))
        {
            var key = entry.Key!.Value;
            if (key.Group != group || key.Frame != frame || key.Palette != palette) continue;
            if (key.View == view) return entry;
            other ??= entry;
        }
        return other;
    }

    /// <summary>The next sprite entry after (<paramref name="step"/> 1) or before (−1) the
    /// entry with index <paramref name="fromIndex"/> (−1 starts at either end) that the
    /// filter accepts, wrapping around; null when none does.</summary>
    public HdPackEntry? Step(int fromIndex, int step, Func<HdPackEntry, bool>? filter = null)
    {
        if (step is not (1 or -1)) throw new ArgumentOutOfRangeException(nameof(step));
        if (Sprites.Count == 0) return null;
        var position = -1;
        for (var i = 0; i < Sprites.Count && fromIndex >= 0; i++)
            if (Sprites[i].Index == fromIndex) { position = i; break; }
        for (var n = 1; n <= Sprites.Count; n++)
        {
            var at = position < 0 ? (step > 0 ? n - 1 : Sprites.Count - n) : ((position + step * n) % Sprites.Count + Sprites.Count) % Sprites.Count;
            if (filter is null || filter(Sprites[at])) return Sprites[at];
        }
        return null;
    }
}
