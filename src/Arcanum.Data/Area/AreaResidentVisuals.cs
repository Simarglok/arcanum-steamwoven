namespace Arcanum.Data.Area;

/// <summary>
/// ARC-DEV-375: the visual memo a map load keeps between loads of the process (<see cref="AreaLoaderOptions.ResidentVisuals"/>). The strict build decodes every
/// distinct frame selection once to record its geometry and its payload and pixel digests (ARC-DATA-024); a window move, a cave return or a quick load builds an
/// owner again and meets mostly the same selections. A selection's record is a pure function of the entry's authenticated bytes and the selection, so
/// it is kept under the entry, the selection and the source's authenticated identity (length and SHA-256), and a later build serves it instead of reading
/// and decoding the entry again. Nothing here holds a file handle, an owner or a snapshot.
/// <para>Records are staged by the build that derives them and committed only after that build's closing authentication (I7) succeeded and the snapshot is
/// complete (<see cref="AreaDataOwner.Inspect"/>), so every record was derived inside a fully authenticated bracket. Bounded by count, least recently used
/// first; see docs/world/area-loader.md, "Resident frames and visuals across map loads".</para>
/// </summary>
public sealed class AreaResidentVisuals
{
    /// <summary>Records kept at most (about 0.5 KiB each).</summary>
    public const int MaxRecords = 65536;

    /// <summary>The process-wide memo the map plans pass (<see cref="AreaMapPlans"/>).</summary>
    public static AreaResidentVisuals Shared { get; } = new();

    /// <summary>Diagnostic switch for before/after timing: <c>ARC_AREA_RESIDENT=0</c> in the environment makes the map path keep nothing between loads
    /// (no visual memo, no frame store), exactly as before ARC-DEV-375. On by default; it changes no result, only what is read again.</summary>
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("ARC_AREA_RESIDENT") != "0";

    private readonly ResidentLru<ResidentVisualKey, AreaVisualRecord> _records;

    public AreaResidentVisuals(int maxRecords = MaxRecords) { _records = new(maxRecords, maxRecords); }

    public int Count => _records.Count;
    /// <summary>Lookups that were served / not served from the memo since construction or the last <see cref="Clear"/>.</summary>
    public (long Hits, long Misses) Lookups => _records.Lookups;

    internal bool TryGet(ResidentVisualKey key, out AreaVisualRecord record) => _records.TryGet(key, out record!);
    internal void Commit(IEnumerable<KeyValuePair<ResidentVisualKey, AreaVisualRecord>> staged)
    { foreach (var (key, record) in staged) _records.Add(key, record, 1); }

    /// <summary>Forgets every record (tests, or to give the memory back).</summary>
    public void Clear() => _records.Clear();
}

/// <summary>ARC-DEV-375: the key of one resident visual record: the strict build's own selection key and the identity (length, SHA-256) of the source the entry
/// is read from, as the plan records and the build's bracket authenticates it.</summary>
internal readonly record struct ResidentVisualKey(long SourceLength, string SourceSha256, AreaEntryIdentity Entry, int Group, int Frame, int Palette,
    bool SingleRotationClass, string? Policy);
