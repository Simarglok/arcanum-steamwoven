using System.Text;

namespace Arcanum.Session;

public enum SaveSlotFailure
{
    None,
    /// <summary>The slot has no save file yet.</summary>
    NoSave,
    /// <summary>The file is larger than <see cref="GameSessionSaveSlot.MaxBytes"/>.</summary>
    TooLarge,
    /// <summary>The file exists but could not be opened or read.</summary>
    ReadFailed,
    /// <summary>The bytes are not strict UTF-8.</summary>
    NotUtf8,
    /// <summary>The text is not a snapshot the codec accepts; see the decode failure and path.</summary>
    Corrupt,
    /// <summary>The temporary file could not be written, flushed or moved over the slot.</summary>
    WriteFailed,
    /// <summary>The written bytes did not read back identically; the slot was not replaced.</summary>
    VerifyFailed,
}

/// <summary>Outcome of one write. On any failure the previous save file is untouched.</summary>
public sealed record SaveSlotWriteResult(SaveSlotFailure Failure, long Bytes, string? Detail)
{
    public bool Saved => Failure == SaveSlotFailure.None;
}

/// <summary>Outcome of one read. <see cref="DecodeFailure"/> and <see cref="DecodePath"/> are set for <see cref="SaveSlotFailure.Corrupt"/>.</summary>
public sealed record SaveSlotReadResult(GameSessionSnapshot? Snapshot, SaveSlotFailure Failure,
    SnapshotDecodeFailure DecodeFailure, string? DecodePath, string? Detail);

/// <summary>
/// One save file holding exactly the <see cref="GameSessionSnapshotCodec"/> text of a session snapshot. The slot
/// knows nothing about the snapshot's sections: a domain added to the snapshot and its codec is saved and loaded
/// here without changing this class. Writing is atomic: the text is encoded and decoded back in memory, written
/// to a sibling temporary file, flushed to disk, read back and compared, and only then moved over the slot, so a
/// failed or interrupted save never replaces the previous one. Reading is bounded and strict and never throws on
/// file content. Restoring the decoded snapshot (identity, clock profile, budgets) stays with
/// <see cref="GameSessionOwner.Restore"/>. Caller-serialized; one process per slot.
/// </summary>
public sealed class GameSessionSaveSlot
{
    public const string QuickSaveFileName = "quicksave.json";
    public const string TemporarySuffix = ".tmp";
    /// <summary>The codec writes ASCII only, so its character limit is also the file's byte limit.</summary>
    public const int MaxBytes = GameSessionSnapshotCodec.MaxEncodedCharacters;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public GameSessionSaveSlot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("SAVE_SLOT_PATH_NOT_ABSOLUTE", nameof(path));
        if (string.IsNullOrEmpty(System.IO.Path.GetFileName(path))) throw new ArgumentException("SAVE_SLOT_PATH_HAS_NO_FILE_NAME", nameof(path));
        Path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>The quick-save slot inside <paramref name="directory"/>.</summary>
    public static GameSessionSaveSlot QuickSave(string directory) =>
        new(System.IO.Path.Combine(directory, QuickSaveFileName));

    public string Path { get; }
    public string TemporaryPath => Path + TemporarySuffix;
    public bool Exists => File.Exists(Path);

    /// <summary>Test seam: runs after the temporary file is verified and before it replaces the slot.</summary>
    internal Action<string>? BeforeReplaceForTest { get; set; }

    public SaveSlotWriteResult Write(GameSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = GameSessionSnapshotCodec.Encode(snapshot);
        var bytes = Strict.GetBytes(text);
        if (bytes.Length > MaxBytes) return new(SaveSlotFailure.TooLarge, bytes.Length, "ENCODED_SNAPSHOT_TOO_LARGE");
        // A save that this build cannot load again is never written.
        var check = GameSessionSnapshotCodec.TryDecode(text);
        if (check.Snapshot is null) return new(SaveSlotFailure.VerifyFailed, bytes.Length, $"ENCODED_TEXT_DOES_NOT_DECODE {check.Failure} {check.Path}");
        var temporary = TemporaryPath;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (!File.ReadAllBytes(temporary).AsSpan().SequenceEqual(bytes))
            {
                TryDelete(temporary);
                return new(SaveSlotFailure.VerifyFailed, bytes.Length, "READ_BACK_MISMATCH");
            }
            BeforeReplaceForTest?.Invoke(temporary);
            File.Move(temporary, Path, overwrite: true);
            return new(SaveSlotFailure.None, bytes.Length, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
            return new(SaveSlotFailure.WriteFailed, bytes.Length, e.GetType().Name);
        }
    }

    public SaveSlotReadResult Read()
    {
        byte[] bytes;
        try
        {
            // Opened directly: a directory or an unreadable entry at the path is ReadFailed, not NoSave.
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) return Failed(SaveSlotFailure.TooLarge, $"FILE_BYTES {stream.Length}");
            // Bounded even if the file grows while it is read.
            var buffer = new byte[MaxBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
            if (total > MaxBytes) return Failed(SaveSlotFailure.TooLarge, "FILE_GREW_PAST_LIMIT");
            bytes = buffer[..total];
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(SaveSlotFailure.NoSave, "NO_SAVE_FILE");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failed(SaveSlotFailure.ReadFailed, e.GetType().Name);
        }
        string text;
        try { text = Strict.GetString(bytes); }
        catch (DecoderFallbackException) { return Failed(SaveSlotFailure.NotUtf8, "INVALID_UTF8"); }
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        return decoded.Snapshot is null
            ? new(null, SaveSlotFailure.Corrupt, decoded.Failure, decoded.Path, $"{decoded.Failure} at {decoded.Path}")
            : new(decoded.Snapshot, SaveSlotFailure.None, SnapshotDecodeFailure.None, null, null);
    }

    private static SaveSlotReadResult Failed(SaveSlotFailure failure, string detail) =>
        new(null, failure, SnapshotDecodeFailure.None, null, detail);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
