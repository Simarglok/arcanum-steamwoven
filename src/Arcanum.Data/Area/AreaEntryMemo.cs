namespace Arcanum.Data.Area;

public sealed partial class AreaDataOwner
{
    /// <summary>Most entry bytes one memo keeps (ARC-RENDER-009); larger entries are read again.</summary>
    internal const long EntryMemoMaxBytes = 16L * 1024 * 1024;
    private Dictionary<AreaEntryIdentity, (byte[] Bytes, string Sha256)>? _entryMemo;
    private long _entryMemoHeld;

    // Many selected frames share one ART entry (Crash Site: 1,975 frames from 335 entries).
    // Inside one authenticated bracket (a strict build between I0 and I7, or one frame batch
    // between a source's first authentication and Complete) an entry's bytes cannot differ
    // between reads that the bracket's authentication does not already cover, so the entry is
    // read, inflated and payload-hashed once per bracket. Every frame keeps its own budget
    // admission, decode and pixel/geometry checks. The memo is owner-private, bounded by
    // EntryMemoMaxBytes and held in the retained domain until its bracket ends.
    private void BeginEntryMemo() { EndEntryMemo(); _entryMemo = new(); }
    private void EndEntryMemo()
    {
        if (_entryMemo is null) return;
        _budget.Release(_entryMemoHeld); _entryMemoHeld = 0; _entryMemo = null;
    }
    private (byte[] Bytes, string Sha256) ReadFrameEntry(AreaEntryIdentity identity)
    {
        if (_entryMemo is not null && _entryMemo.TryGetValue(identity, out var cached)) return cached;
        var bytes = ReadEntry(identity); var read = (bytes, Objects.ObjectWireReader.Hash(bytes));
        if (_entryMemo is not null && bytes.Length <= EntryMemoMaxBytes - _entryMemoHeld && _budget.TryHold(bytes.Length))
        { _entryMemo.Add(identity, read); _entryMemoHeld += bytes.Length; }
        return read;
    }
}
