namespace Arcanum.Data.Area;

/// <summary>
/// ARC-DEV-388: the memo of the editor <c>.mob</c> correspondence pass a map load keeps between loads of the process
/// (<see cref="AreaLoaderOptions.ResidentEditorRecords"/>). The strict build's I5 pass reads, inflates and wire-parses every
/// <c>.mob</c> entry of the map module (one per mobile object of the map, thousands on the main map) only to take four
/// facts from each (its id, type, prototype and semantic hash) and compare them with the aggregate's records. Those facts
/// are a pure function of the module's authenticated bytes and the sector prefix, so they are kept under the module's
/// authenticated identity (length and SHA-256, as the plan records it) and a later build of the same module serves them
/// instead of reading the files again. The comparison with the aggregate, the distinct-id check and the count check still
/// run on every build; a stale or foreign entry cannot be served, since the key is the module's content hash.
/// <para>A pass is staged by the build that derives it and committed only after that build's closing authentication (I7)
/// succeeded and its snapshot is complete (<see cref="AreaDataOwner.Inspect"/>), as the visual memo is, and only when the
/// whole pass succeeded. Nothing here holds a file handle, an owner or a snapshot. Bounded by passes and by facts, least
/// recently used first; see docs/world/area-loader.md, "Resident editor records".</para>
/// </summary>
public sealed class AreaResidentEditorRecords
{
    /// <summary>Passes (module and sector-prefix pairs) kept at most.</summary>
    public const int MaxPasses = 16;
    /// <summary>Facts kept at most over all passes (about 0.4 KiB each, about 26 MiB).</summary>
    public const int MaxFacts = 65536;

    /// <summary>The process-wide memo the map plans pass (<see cref="AreaMapPlans"/>).</summary>
    public static AreaResidentEditorRecords Shared { get; } = new();

    /// <summary>Diagnostic switch for before/after timing: <c>ARC_AREA_RESIDENT_EDITOR=0</c> in the environment (or <c>ARC_AREA_RESIDENT=0</c>, which turns every
    /// resident store off) makes the map path read every editor record in every build, exactly as before ARC-DEV-388. On by default; it changes no result.</summary>
    public static bool Enabled { get; } = AreaResidentVisuals.Enabled && Environment.GetEnvironmentVariable("ARC_AREA_RESIDENT_EDITOR") != "0";

    private readonly ResidentLru<ResidentEditorKey, ResidentEditorFact[]> _passes;
    private (bool Served, double Milliseconds, int Records) _last;

    public AreaResidentEditorRecords(int maxFacts = MaxFacts, int maxPasses = MaxPasses) { _passes = new(maxFacts, maxPasses); }

    /// <summary>Passes kept.</summary>
    public int Count => _passes.Count;
    /// <summary>Facts kept over all passes.</summary>
    public long Facts => _passes.Cost;
    /// <summary>Lookups that were served / not served from the memo since construction or the last <see cref="Clear"/>.</summary>
    public (long Hits, long Misses) Lookups => _passes.Lookups;

    /// <summary>The most recent editor pass of a build using this memo: whether it was served from the memo, how long it took (milliseconds) and how many records it covered
    /// (a diagnostic; the pass of a build that failed is not recorded).</summary>
    public (bool Served, double Milliseconds, int Records) LastPass { get { lock (_passes) return _last; } }
    internal void Note(bool served, double milliseconds, int records) { lock (_passes) _last = (served, milliseconds, records); }

    internal bool TryGet(ResidentEditorKey key, out ResidentEditorFact[] facts) => _passes.TryGet(key, out facts!);
    internal void Commit(ResidentEditorKey key, ResidentEditorFact[] facts) => _passes.Add(key, facts, Math.Max(1, facts.Length));

    /// <summary>Forgets every pass (tests, or to give the memory back).</summary>
    public void Clear() => _passes.Clear();
}

/// <summary>ARC-DEV-388: one resident pass: the module's authenticated identity (length, SHA-256) and the sector prefix whose <c>.mob</c> entries it covers.</summary>
internal readonly record struct ResidentEditorKey(long ModuleLength, string ModuleSha256, string SectorPrefix);

/// <summary>ARC-DEV-388: what the correspondence check takes from one editor record: the entry's ordinal in the module and the record's id, type,
/// prototype number and semantic hash.</summary>
internal readonly record struct ResidentEditorFact(int Ordinal, string OwnSha256, int Type, int? Prototype, string Semantic);
