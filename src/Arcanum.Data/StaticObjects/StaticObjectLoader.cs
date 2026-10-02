using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.StaticObjects;

/// <summary>Fail-closed v119 reader for the selected type-3 static-object window.</summary>
public static class StaticObjectLoader
{
    private const string CrashSiteModuleEntryPath = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    private static readonly StaticObjectInputFingerprint CrashSiteModuleIdentity = new(355938305, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f");
    private static readonly StaticObjectInputFingerprint CrashSiteResourceIdentity = new(147991122, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d");
    private static readonly IReadOnlyDictionary<int, StaticObjectInputFingerprint> CrashSitePrototypeIdentities =
        new Dictionary<int, StaticObjectInputFingerprint>
        {
            [4026] = new(414, "1378ddb05e981098eca7816b22a366e6733c9df7c580ccc3f19345f782319874"),
            [4028] = new(414, "c2442deca958715bc49f5f0504066f951a7d549e5787be41dcded2257e23d6e3"),
            [4030] = new(406, "f15e7b0ed999ac9eeabcafa1bcddbd55b96dcbba9b2d71a7d485024c06f9c310"),
            [4031] = new(414, "3c5ad2f204a010b3908d0a02e693128e9f8ff421b4a7dafb7ee3e78b66420c53"),
            [4038] = new(414, "2531e1a1c6ddca2f483a2d557bebcfd6c1b0b89bd2d996a3f559adc28924214b"),
            [4048] = new(378, "ec807b77ea283d8d84387993a439953b7acf1a1ef7bdfb30b6a86a334dcfe8d1")
        };
    private const int ObjectVersion = 119;
    private const uint DynamicFlag = 0x00002000;
    private const int CurrentArtSlot = 0;
    private const int LocationSlot = 1;
    private const int OffsetXSlot = 2;
    private const int OffsetYSlot = 3;
    private const int ObjectFlagsSlot = 18;
    private static readonly WireKind[] CommonFields = K("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64");
    private static readonly WireKind[] WallFields = K("i*3,au32,au64");
    private static readonly WireKind[] PortalFields = K("i*6,au32,au64");
    private static readonly WireKind[] ContainerFields = K("i*4,poid,i*4,au32,au64");
    private static readonly WireKind[] SceneryFields = K("i,poid,i*2,au32,au64");
    private static readonly WireKind[] ProjectileFields = K("i*3,poid,i*2,au32,au64");
    private static readonly WireKind[] ItemFields = K("i,poid,i*19,au32,au64");
    private static readonly WireKind[] WeaponFields = K("i*4,ai32*3,i*18,au32,au64");
    private static readonly WireKind[] AmmoFields = K("i*5,au32,au64");
    private static readonly WireKind[] ArmorFields = K("i*4,ai32*2,i*4,au32,au64");
    private static readonly WireKind[] GoldFields = K("i*4,au32,au64");
    private static readonly WireKind[] ShortItemFields = K("i*3,au32,au64");
    private static readonly WireKind[] KeyRingFields = K("i,au32,i*2,au32,au64");
    private static readonly WireKind[] WrittenFields = K("i*6,au32,au64");
    private static readonly WireKind[] CritterFields = K("i*2,ai32*3,au32,i*4,au32*2,poid,i,poid*5,i,aoid,i*2,poid,p64,i*6,au32,au64");
    private static readonly WireKind[] PcFields = K("i*2,au32,au64,i*2,aquest,au32,au64,au32,au64,i,au64,au32*3,i,s,i,au32*2,i*2,au32,au64");
    private static readonly WireKind[] NpcFields = K("i,poid,i,poid*2,i*2,au64,i,p64*2,i*3,poid,i*2,aoid,au32*2,i*3,au32,aoid");

    public static StaticObjectInspection Inspect(StaticObjectSource source, StaticObjectPrototypeSource prototypes, StaticObjectLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prototypes);
        var limits = options ?? new StaticObjectLoaderOptions();
        limits.Validate();
        ValidateArchiveBounds(source.ModuleArchive, limits);
        ValidateArchiveBounds(source.ResourceArchive, limits);
        return InspectParsed(source, prototypes, limits, null, null);
    }

    // Assessment policy only: same strict parser and prototype reader, but
    // unresolved flag references are retained per record. Public loader policies
    // still abort; malformed bytes and limits always abort.
    internal static StaticObjectInspection InspectForObstruction(StaticObjectSource source,
        StaticObjectPrototypeSource prototypes, StaticObjectLoaderOptions limits)
    {
        limits.Validate();
        ValidateArchiveBounds(source.ModuleArchive, limits);
        ValidateArchiveBounds(source.ResourceArchive, limits);
        return InspectParsed(source, prototypes, limits, null, null, flagsOnly: true);
    }

    /// <summary>
    /// Resolves a bounded structural resource chain for source indexes zero through four.
    /// This caller-owned operation does not authenticate an original-data identity.
    /// </summary>
    public static StaticObjectInspection InspectSelectedResources(StaticObjectSource source, StaticObjectPrototypeSource prototypes,
        StaticObjectLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prototypes);
        var limits = options ?? new StaticObjectLoaderOptions();
        limits.Validate();
        ValidateArchiveBounds(source.ModuleArchive, limits);
        ValidateArchiveBounds(source.ResourceArchive, limits);
        return InspectParsed(source, prototypes, limits, SelectedResourceIndexes, null);
    }

    /// <summary>
    /// Decodes the already-resolved selected frame only after the immutable resource
    /// identity and the recorded group/frame/palette selection match this archive.
    /// This is structural; it does not authenticate caller-owned archives.
    /// </summary>
    public static ArtFrame DecodeSelectedFrame(DatArchive resourceArchive, StaticObjectRecord record, ArtDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resourceArchive);
        ArgumentNullException.ThrowIfNull(record);
        var metadata = record.ArtMetadata;
        if (record.ResourceStatus != StaticObjectResourceStatus.Resolved || metadata is null)
            throw Failure(StaticObjectErrorCode.InvalidArtSelector, StaticObjectFailureStage.Art, "The selected resource frame is not resolved.", record.SourceIndex);
        try
        {
            var matches = resourceArchive.Entries.Where(entry => ResourceIdentity(entry) == metadata.ResourceIdentity).ToArray();
            if (matches.Length != 1)
                throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Art, "The selected resource identity is unavailable or ambiguous.", record.SourceIndex);
            var decoded = ArtDecoder.Decode(resourceArchive.ReadEntry(matches[0]),
                new ArtFrameSelection(metadata.RotationGroup, metadata.Frame, metadata.Palette), options);
            if (decoded.Width != metadata.Width || decoded.Height != metadata.Height || decoded.Frame.HotX != metadata.HotX || decoded.Frame.HotY != metadata.HotY ||
                decoded.Frame.OffsetX != metadata.ArtOffsetX || decoded.Frame.OffsetY != metadata.ArtOffsetY || decoded.GroupIndex != metadata.RotationGroup ||
                decoded.FrameIndex != metadata.Frame || decoded.RequestedPaletteSlot != metadata.Palette || decoded.EffectivePaletteSlot != metadata.EffectivePalette)
                throw Failure(StaticObjectErrorCode.InvalidArtSelector, StaticObjectFailureStage.Art, "The selected resource frame metadata changed.", record.SourceIndex);
            return decoded;
        }
        catch (StaticObjectException) { throw; }
        catch (DatException) { throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Art, "The selected resource archive cannot supply the frame.", record.SourceIndex); }
        catch (ArtException) { throw Failure(StaticObjectErrorCode.InvalidArtSelector, StaticObjectFailureStage.Art, "The selected resource frame is invalid.", record.SourceIndex); }
    }

    private static StaticObjectInspection InspectParsed(StaticObjectSource source, StaticObjectPrototypeSource prototypes,
        StaticObjectLoaderOptions limits, IReadOnlyList<int>? selectedIndexes,
        IReadOnlyDictionary<int, PrototypeInput>? fingerprints, bool flagsOnly = false)
    {
        try
        {
            var (terminalCount, parsed) = ParseSelectedWindow(source, limits);
            var loadedPrototypes = ReadPrototypes(prototypes, parsed.Where(record => record.Type == 3 &&
                (record.DirectFlags is null || (!flagsOnly && record.DirectCurrentArt is null))), limits, fingerprints, flagsOnly);
            var resolved = parsed.Select(record => ResolveFields(record, loadedPrototypes, flagsOnly)).ToArray();
            if (selectedIndexes is not null)
            {
                ValidateSelectedResourceEligibility(resolved, selectedIndexes);
                for (var index = 0; index < resolved.Length; index++)
                    if (!selectedIndexes.Contains(index)) resolved[index] = Copy(resolved[index], StaticObjectResourceStatus.OutsideResourceScope, null);
                foreach (var sourceIndex in selectedIndexes)
                {
                    var resolvedRecord = ResolveSelectedResource(resolved[sourceIndex], source.ResourceArchive, limits);
                    if (resolvedRecord.ResourceStatus != StaticObjectResourceStatus.Resolved)
                        throw Failure(resolvedRecord.ResourceStatus == StaticObjectResourceStatus.InvalidArtSelector ? StaticObjectErrorCode.InvalidArtSelector : StaticObjectErrorCode.UnresolvedResource,
                            resolvedRecord.ResourceStatus == StaticObjectResourceStatus.InvalidArtSelector ? StaticObjectFailureStage.Art : StaticObjectFailureStage.Mapping,
                            "An admitted selected resource chain did not resolve.", sourceIndex);
                    resolved[sourceIndex] = resolvedRecord;
                }
            }
            return new StaticObjectInspection(StaticObjectCensusStatus.Complete, terminalCount, resolved,
                parsed.Select((record, index) => new StaticObjectTraversalRecord(record.SourceIndex, record.Type,
                    record.PrototypeAid < 0 ? null : record.PrototypeAid, record.Location, record.DirectFlags,
                    record.Type == 3 ? resolved[index].EffectiveFlags : null, record.Type == 3 && resolved[index].FlagsInherited)));
        }
        catch (TerrainException exception)
        {
            throw Failure(exception.Code == TerrainErrorCode.LimitExceeded ? StaticObjectErrorCode.LimitExceeded : StaticObjectErrorCode.InvalidFraming,
                StaticObjectFailureStage.SectorFraming, "The selected sector framing is invalid.");
        }
        catch (DatException)
        {
            throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.Archive, "A caller-owned archive cannot supply a bounded object window.");
        }
    }

    /// <summary>
    /// Authenticates and validates the one fixed Crash Site input. This is the only API
    /// that returns trusted original-data validation evidence.
    /// </summary>
    public static StaticObjectValidation ValidateOriginalData(string modulePath, string resourcePath, string prototypeDirectory,
        StaticObjectLoaderOptions? options = null)
        => WithValidatedOriginalData(modulePath, resourcePath, prototypeDirectory, options, (_, _, validation) => validation);

    // Assembly-internal projection executes BEFORE final authentication checks; no public bypass.
    internal static T WithValidatedOriginalData<T>(string modulePath, string resourcePath, string prototypeDirectory,
        StaticObjectLoaderOptions? options, Func<StaticObjectSource, StaticObjectPrototypeSource, StaticObjectValidation, T> projection)
    {
        ArgumentException.ThrowIfNullOrEmpty(modulePath); ArgumentException.ThrowIfNullOrEmpty(resourcePath);
        ArgumentException.ThrowIfNullOrEmpty(prototypeDirectory);
        var limits = options ?? new StaticObjectLoaderOptions(); limits.Validate();
        FileStream? moduleStream = null;
        FileStream? resourceStream = null;
        DatArchive? module = null;
        DatArchive? resource = null;
        try
        {
            moduleStream = OpenReadOnly(modulePath);
            resourceStream = OpenReadOnly(resourcePath);
            var moduleBefore = Fingerprint(moduleStream, limits.MaxArchiveBytes);
            var resourceBefore = Fingerprint(resourceStream, limits.MaxArchiveBytes);
            if (moduleBefore != CrashSiteModuleIdentity || resourceBefore != CrashSiteResourceIdentity)
                throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.FingerprintInput, "The selected inputs do not match the fixed Crash Site profile.");
            module = DatArchive.Open(moduleStream, DatOptions(limits), leaveOpen: true);
            resource = DatArchive.Open(resourceStream, DatOptions(limits), leaveOpen: true);
            var source = new StaticObjectSource(module, CrashSiteModuleEntryPath, resource);
            var prototypes = new StaticObjectPrototypeSource(prototypeDirectory);
            var (_, parsed) = ParseSelectedWindow(source, limits);
            var prototypeBefore = PreparePrototypeFingerprints(prototypes, parsed, limits);
            if (prototypeBefore.Count != CrashSitePrototypeIdentities.Count || prototypeBefore.Any(item =>
                    item.Fingerprint is null || !CrashSitePrototypeIdentities.TryGetValue(item.Aid, out var expected) || item.Fingerprint != expected))
                throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.FingerprintInput, "The selected prototypes do not match the fixed Crash Site profile.");
            var inspection = InspectParsed(source, prototypes, limits, SelectedResourceIndexes,
                prototypeBefore.ToDictionary(item => item.Aid));
            if (inspection.TerminalRecordCount != 317)
                throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector, "The authenticated input does not contain the fixed selected record count.");
            var aggregate = Aggregate(prototypeBefore.Select(item => (item.Aid, item.Fingerprint!)));
            var validation = new StaticObjectValidation(inspection, moduleBefore, resourceBefore,
                prototypeBefore.Select(item => new StaticObjectPrototypeFingerprint(item.Aid, item.Fingerprint!)).ToArray(), aggregate);
            return CompleteGuardedProjection(() => projection(source, prototypes, validation), () =>
            {
                if (moduleBefore != Fingerprint(moduleStream, limits.MaxArchiveBytes) || resourceBefore != Fingerprint(resourceStream, limits.MaxArchiveBytes) ||
                    prototypeBefore.Any(item => item.Fingerprint != FingerprintSafe(item.Path, (uint)limits.MaxPrototypeBytes)))
                    throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.FingerprintInput, "An immutable input changed during validation.");
            });
        }
        catch (StaticObjectException) { throw; }
        catch (IOException) { throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.FingerprintInput, "An immutable validation input is unavailable."); }
        catch (UnauthorizedAccessException) { throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.FingerprintInput, "An immutable validation input is unavailable."); }
        catch (DatException) { throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.Archive, "An immutable validation archive is outside static-object limits."); }
        finally
        {
            module?.Dispose(); resource?.Dispose(); moduleStream?.Dispose(); resourceStream?.Dispose();
        }
    }

    private static (int TerminalCount, ParsedRecord[] Records) ParseSelectedWindow(StaticObjectSource source, StaticObjectLoaderOptions limits)
    {
        var selected = source.ModuleArchive.Entries.Where(entry => entry.RawNameBytes.SequenceEqual(Encoding.ASCII.GetBytes(source.ModuleEntryPath + "\0"))).ToArray();
        if (selected.Length != 1) throw Failure(selected.Length == 0 ? StaticObjectErrorCode.MissingModule : StaticObjectErrorCode.AmbiguousModule, StaticObjectFailureStage.Selector, "The selected module entry is missing or ambiguous.");
        ValidateEntrySize(selected[0], limits.MaxSectorBytes, StaticObjectFailureStage.Archive, "The selected sector exceeds static-object limits.");
        var window = TerrainLoader.ExtractObjectWindow(source.ModuleArchive, source.ModuleEntryPath, new TerrainLoaderOptions
        {
            MaxSectorDecodedEntryBytes = limits.MaxSectorBytes,
            MaxObjectTerminalCount = limits.MaxRecords
        });
        if (window.TerminalObjectCount > limits.MaxRecords)
            throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.SectorFraming, "The terminal object count exceeds the configured limit.");
        return (window.TerminalObjectCount, ParseWindow(window.Bytes.ToArray(), window.TerminalObjectCount, limits));
    }

    internal static T CompleteGuardedProjection<T>(Func<T> projection, Action verifyInputs)
    {
        var owned = projection();
        verifyInputs();
        return owned;
    }

    private static readonly IReadOnlyList<int> SelectedResourceIndexes = [0, 1, 2, 3, 4];

    private static void ValidateSelectedResourceEligibility(IReadOnlyList<StaticObjectRecord> records, IReadOnlyList<int> selectedIndexes)
    {
        if (records.Count < selectedIndexes.Count)
            throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector, "The selected resource indexes are unavailable.");
        foreach (var sourceIndex in selectedIndexes)
        {
            if (sourceIndex < 0 || sourceIndex >= records.Count)
                throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector, "A selected resource source index is unavailable.");
            var actual = records[sourceIndex];
            if (actual.FieldStatus != StaticObjectFieldStatus.Resolved || actual.ObjectType != 3 || actual.IsStatic != true)
                throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector, "A selected resource record is not structurally eligible.", sourceIndex);
            if (actual.PrototypeAid is null || actual.Location is null || actual.DirectCurrentArt is null || actual.CurrentArtInherited ||
                actual.CurrentArt != actual.DirectCurrentArt || actual.DirectOffsetX is null || actual.DirectOffsetY is null)
                throw Failure(StaticObjectErrorCode.InvalidSelector, StaticObjectFailureStage.Selector, "A selected resource lacks required direct fields.", sourceIndex);
        }
    }

    private static DatReaderOptions DatOptions(StaticObjectLoaderOptions limits) => new()
    {
        MaxArchiveBytes = limits.MaxArchiveBytes,
        MaxIndexBytes = limits.MaxArchiveIndexBytes,
        MaxEntryCount = limits.MaxArchiveEntries,
        MaxNameBytes = 64 * 1024,
        MaxTotalNameBytes = limits.MaxArchiveIndexBytes,
        MaxStoredEntryBytes = (uint)Math.Max(limits.MaxSectorBytes, limits.MaxResourceBytes),
        MaxDecodedEntryBytes = (uint)Math.Max(limits.MaxSectorBytes, limits.MaxResourceBytes)
    };

    private static FileStream OpenReadOnly(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void ValidateArchiveBounds(DatArchive archive, StaticObjectLoaderOptions limits)
    {
        try
        {
            var archiveBytes = checked(archive.Footer.IndexOffset + (long)archive.Footer.TailRegionBytes);
            var indexBytes = checked((long)archive.Footer.TailRegionBytes - 28);
            if (archiveBytes > limits.MaxArchiveBytes || indexBytes > limits.MaxArchiveIndexBytes || archive.Entries.Count > limits.MaxArchiveEntries)
                throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Archive, "A caller-owned archive exceeds static-object limits.");
        }
        catch (OverflowException)
        {
            throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.Archive, "A caller-owned archive has invalid bounds.");
        }
    }

    private static ParsedRecord[] ParseWindow(byte[] bytes, int terminalCount, StaticObjectLoaderOptions limits)
    {
        var cursor = new Cursor(bytes);
        var records = new ParsedRecord[terminalCount];
        for (var index = 0; index < terminalCount; index++)
        {
            try { records[index] = ParseRecord(ref cursor, index, limits); }
            catch (StaticObjectException exception) { throw Failure(exception.Code, StaticObjectFailureStage.Record, "An object record is invalid.", index, cursor.Offset); }
            catch (OverflowException) { throw Failure(StaticObjectErrorCode.InvalidRecord, StaticObjectFailureStage.Record, "An object record overflows a bounded calculation.", index, cursor.Offset); }
        }
        if (!cursor.AtEnd)
            throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.Record, "The object window has trailing bytes after its terminal record count.", terminalCount, cursor.Offset);
        return records;
    }

    private static ParsedRecord ParseRecord(ref Cursor cursor, int sourceIndex, StaticObjectLoaderOptions limits)
    {
        if (cursor.I32() != ObjectVersion) throw new StaticObjectException(StaticObjectErrorCode.UnsupportedVersion, "The object version is unsupported.");
        var prototype = cursor.ObjectId(allowBlocked: false);
        if (prototype.Kind is < 0 or > 3) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "The prototype identifier form is invalid.");
        _ = cursor.ObjectId(allowBlocked: false);
        var type = cursor.I32();
        if (type is < 0 or > 17) throw new StaticObjectException(StaticObjectErrorCode.UnsupportedType, "The object type is unsupported.");
        var fields = FieldsFor(type);
        var fieldCount = cursor.I16();
        if (fieldCount < 0 || fieldCount > limits.MaxFields) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "The object field count exceeds the configured limit.");
        var set = ReadMasks(ref cursor, type, fields.Length, out var count);
        if (fieldCount != count) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "The object field count differs from the override-mask population.");
        int? art = null; long? location = null; int? offsetX = null; int? offsetY = null; uint? flags = null;
        for (var field = 0; field < fields.Length; field++)
        {
            if (!set[field]) continue;
            if (type == 3 && field == CurrentArtSlot) art = cursor.I32();
            else if (field == LocationSlot) location = cursor.OptionalI64();
            else if (type == 3 && field == OffsetXSlot) offsetX = cursor.I32();
            else if (type == 3 && field == OffsetYSlot) offsetY = cursor.I32();
            else if (field == ObjectFlagsSlot) flags = cursor.U32();
            else Skip(ref cursor, fields[field], limits);
        }
        return new ParsedRecord(sourceIndex, prototype.Aid ?? -1, type, art, flags, location, offsetX, offsetY);
    }

    private static bool[] ReadMasks(ref Cursor cursor, int type, int fieldLength, out int count)
    {
        var set = new bool[fieldLength]; count = 0;
        for (var word = 0; word < MaskWords(type); word++)
        {
            var start = MaskStart(type, word);
            var length = MaskLength(type, word);
            var mask = cursor.U32();
            var allowed = length == 32 ? uint.MaxValue : (1u << length) - 1u;
            if ((mask & ~allowed) != 0) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An object override mask has a reserved bit.");
            for (var bit = 0; bit < length; bit++) if ((mask & (1u << bit)) != 0) { set[start + bit] = true; count++; }
        }
        return set;
    }

    private static Dictionary<int, Prototype> ReadPrototypes(StaticObjectPrototypeSource source, IEnumerable<ParsedRecord> records,
        StaticObjectLoaderOptions limits, IReadOnlyDictionary<int, PrototypeInput>? inputs, bool allowUnresolved = false)
    {
        if (!DirectoryExists(source.DirectoryPath))
        {
            if (allowUnresolved) return new Dictionary<int, Prototype>();
            throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "The explicit prototype source is unavailable.");
        }
        var result = new Dictionary<int, Prototype>();
        foreach (var required in records.GroupBy(record => record.PrototypeAid).Select(group => new { Aid = group.Key, SourceIndex = group.Min(record => record.SourceIndex) }))
        {
            var aid = required.Aid;
            try
            {
                if (aid is < 0 or > 999999)
                    throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "An inherited value has no canonical six-digit type-A prototype identifier.");
                var input = inputs is not null && inputs.TryGetValue(aid, out var selected)
                    ? selected : new PrototypeInput(aid, UniquePrototypePath(source.DirectoryPath, aid), null);
                result.Add(aid, ParsePrototype(ReadBoundedFile(input.Path, limits.MaxPrototypeBytes, input.Fingerprint), aid, limits));
            }
            catch (StaticObjectException exception) when (allowUnresolved && exception.Code == StaticObjectErrorCode.UnresolvedReference)
            {
                // No default for a missing, ambiguous or mismatched reference.
            }
            catch (StaticObjectException exception)
            {
                throw Failure(exception.Code, StaticObjectFailureStage.Prototype, "A required prototype is invalid.", required.SourceIndex);
            }
        }
        return result;
    }

    private static Prototype ParsePrototype(byte[] bytes, int expectedAid, StaticObjectLoaderOptions limits)
    {
        var cursor = new Cursor(bytes);
        if (cursor.I32() != ObjectVersion) throw new StaticObjectException(StaticObjectErrorCode.UnsupportedVersion, "A prototype version is unsupported.");
        if (cursor.ObjectId(allowBlocked: true).Kind != -1) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "A prototype lacks its required marker.");
        var id = cursor.ObjectId(allowBlocked: false);
        if (id.Aid != expectedAid) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "A prototype identifier does not match its canonical file selector.");
        if (cursor.I32() != 3) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "A prototype type does not match its static-object reference.");
        _ = ReadMasks(ref cursor, 3, FieldsFor(3).Length, out _);
        var art = cursor.I32(); uint flags = 0;
        var fields = FieldsFor(3);
        for (var field = 1; field < fields.Length; field++)
        {
            if (field == ObjectFlagsSlot) flags = cursor.U32();
            else Skip(ref cursor, fields[field], limits);
        }
        if (!cursor.AtEnd) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "A prototype has trailing bytes.");
        return new Prototype(art, flags);
    }

    private static StaticObjectRecord ResolveFields(ParsedRecord record, IReadOnlyDictionary<int, Prototype> prototypes, bool flagsOnly = false)
    {
        if (record.Type != 3)
            return ToRecord(record, StaticObjectFieldStatus.UnsupportedType, null, false, null, false, null, StaticObjectResourceStatus.OutsideResourceScope, null);
        if (flagsOnly)
        {
            prototypes.TryGetValue(record.PrototypeAid, out var defaultValue);
            var effectiveFlags = record.DirectFlags ?? defaultValue?.Flags;
            var effectiveArt = record.DirectCurrentArt ?? defaultValue?.CurrentArt;
            return ToRecord(record, effectiveFlags.HasValue && effectiveArt.HasValue ? StaticObjectFieldStatus.Resolved : StaticObjectFieldStatus.UnresolvedReference,
                effectiveArt, record.DirectCurrentArt is null && effectiveArt.HasValue, effectiveFlags,
                record.DirectFlags is null && effectiveFlags.HasValue,
                effectiveFlags is uint value ? (value & DynamicFlag) == 0 : null, StaticObjectResourceStatus.NotEvaluated, null);
        }
        Prototype? prototype = null;
        if (record.DirectCurrentArt is null || record.DirectFlags is null)
        {
            if (!prototypes.TryGetValue(record.PrototypeAid, out prototype))
                return ToRecord(record, StaticObjectFieldStatus.UnresolvedReference, null, false, null, false, null, StaticObjectResourceStatus.OutsideResourceScope, null);
        }
        var currentArt = record.DirectCurrentArt ?? prototype!.CurrentArt;
        var flags = record.DirectFlags ?? prototype!.Flags;
        var isStatic = record.Type == 3 && (flags & DynamicFlag) == 0;
        return ToRecord(record, StaticObjectFieldStatus.Resolved, currentArt, record.DirectCurrentArt is null, flags, record.DirectFlags is null, isStatic, StaticObjectResourceStatus.NotEvaluated, null);
    }

    private static StaticObjectRecord ResolveSelectedResource(StaticObjectRecord record, DatArchive archive, StaticObjectLoaderOptions limits)
    {
        if (record.FieldStatus != StaticObjectFieldStatus.Resolved || record.CurrentArt is not int aid)
            return Copy(record, StaticObjectResourceStatus.OutsideResourceScope, null);
        try
        {
            var value = unchecked((uint)aid);
            var artType = (int)(value >> 28);
            var number = (int)((value >> 19) & 0x1ff); var frame = (int)((value >> 14) & 0x1f);
            var group = (int)((value >> 11) & 0x7); var category = (int)((value >> 6) & 0x1f); var palette = (int)((value >> 4) & 0x3);
            if (artType != 4) return Copy(record, StaticObjectResourceStatus.InvalidArtSelector, null);
            var mes = Unique(archive, "art/scenery/scenery.mes", StaticObjectFailureStage.Mapping, record.SourceIndex);
            ValidateEntrySize(mes, limits.MaxResourceBytes, StaticObjectFailureStage.Mapping, "The scenery mapping exceeds its configured limit.", record.SourceIndex);
            var map = ParseMapping(archive.ReadEntry(mes), limits, record.SourceIndex);
            if (!map.TryGetValue(checked(category * 1000 + number), out var file) || !SafeFileName(file))
                throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The selected resource mapping is missing or unsafe.", record.SourceIndex);
            var art = Unique(archive, "art/scenery/" + file, StaticObjectFailureStage.Art, record.SourceIndex);
            ValidateEntrySize(art, limits.MaxResourceBytes, StaticObjectFailureStage.Art, "A selected ART entry exceeds its configured limit.", record.SourceIndex);
            var metadata = ArtDecoder.Inspect(archive.ReadEntry(art), limits.ArtOptions);
            if (group >= metadata.Header.PhysicalGroupCount || frame >= metadata.Header.FramesPerGroup || metadata.Header.PalettePresenceWords[palette] == 0)
                throw Failure(StaticObjectErrorCode.InvalidArtSelector, StaticObjectFailureStage.Art, "The selected ART group, frame, or palette is unavailable.", record.SourceIndex);
            var selected = metadata.GetFrame(group, frame);
            return Copy(record, StaticObjectResourceStatus.Resolved, new StaticObjectArtMetadata(artType, category, number, group, frame, palette, palette,
                selected.Width, selected.Height, selected.HotX, selected.HotY, selected.OffsetX, selected.OffsetY, ResourceIdentity(art)));
        }
        catch (StaticObjectException) { throw; }
        catch (DatException) { throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Archive, "The selected resource archive cannot supply a bounded ART chain.", record.SourceIndex); }
        catch (ArtException) { throw Failure(StaticObjectErrorCode.InvalidArtSelector, StaticObjectFailureStage.Art, "The selected ART metadata is invalid.", record.SourceIndex); }
    }

    private static void ValidateEntrySize(DatEntry entry, int cap, StaticObjectFailureStage stage, string message, int? sourceIndex = null)
    {
        if (entry.StoredSize > cap || entry.DeclaredOutputSize > cap) throw Failure(StaticObjectErrorCode.LimitExceeded, stage, message, sourceIndex);
    }

    private static string UniquePrototypePath(string root, int aid)
    {
        try
        {
            using var enumerator = Directory.EnumerateFiles(root, $"{aid:D6} - *.pro", SearchOption.TopDirectoryOnly).GetEnumerator();
            if (!enumerator.MoveNext()) throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "A required prototype is missing or ambiguous.");
            var match = enumerator.Current;
            if (enumerator.MoveNext()) throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "A required prototype is missing or ambiguous.");
            return match;
        }
        catch (StaticObjectException) { throw; }
        catch (IOException) { throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "The explicit prototype source is unavailable."); }
        catch (UnauthorizedAccessException) { throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "The explicit prototype source is unavailable."); }
    }

    private static IReadOnlyList<PrototypeInput> PreparePrototypeFingerprints(StaticObjectPrototypeSource source,
        IEnumerable<ParsedRecord> records, StaticObjectLoaderOptions limits)
    {
        if (!DirectoryExists(source.DirectoryPath)) throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "The explicit prototype source is unavailable.");
        return records.Where(record => record.Type == 3 && (record.DirectCurrentArt is null || record.DirectFlags is null)).Select(record => record.PrototypeAid).Distinct().Order()
            .Select(aid =>
            {
                var path = UniquePrototypePath(source.DirectoryPath, aid);
                return new PrototypeInput(aid, path, FingerprintSafe(path, (uint)limits.MaxPrototypeBytes));
            }).ToArray();
    }

    private static byte[] ReadBoundedFile(string path, int cap, StaticObjectInputFingerprint? expected)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > cap || stream.Length > int.MaxValue) throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Prototype, "A required prototype exceeds the configured byte limit.");
            var bytes = new byte[(int)stream.Length]; var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "A required prototype changed while it was read.");
                offset += read;
            }
            if (stream.ReadByte() != -1) throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Prototype, "A required prototype exceeds the configured byte limit.");
            var actual = new StaticObjectInputFingerprint(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            if (expected is not null && expected != actual) throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.FingerprintInput, "A caller-selected immutable input changed before its production read.");
            return bytes;
        }
        catch (StaticObjectException) { throw; }
        catch (IOException) { throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "A required prototype is unavailable."); }
        catch (UnauthorizedAccessException) { throw Failure(StaticObjectErrorCode.UnresolvedReference, StaticObjectFailureStage.Prototype, "A required prototype is unavailable."); }
    }

    private static StaticObjectInputFingerprint Fingerprint(Stream stream, uint cap)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024]; long total = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total = checked(total + read);
            if (total > cap) throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.FingerprintInput, "A caller-selected input exceeds its configured byte limit.");
            hash.AppendData(buffer, 0, read);
        }
        stream.Seek(0, SeekOrigin.Begin);
        return new StaticObjectInputFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static StaticObjectInputFingerprint Fingerprint(string path, uint cap)
    {
        using var stream = OpenReadOnly(path);
        return Fingerprint(stream, cap);
    }

    private static StaticObjectInputFingerprint FingerprintSafe(string path, uint cap)
    {
        try { return Fingerprint(path, cap); }
        catch (StaticObjectException) { throw; }
        catch (IOException) { throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.FingerprintInput, "A caller-selected immutable input is unavailable."); }
        catch (UnauthorizedAccessException) { throw Failure(StaticObjectErrorCode.InvalidFraming, StaticObjectFailureStage.FingerprintInput, "A caller-selected immutable input is unavailable."); }
    }

    private static bool DirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string Aggregate(IEnumerable<(int Aid, StaticObjectInputFingerprint Fingerprint)> inputs)
    {
        var bytes = Encoding.ASCII.GetBytes(string.Join("|", inputs.OrderBy(input => input.Aid).Select(input => $"{input.Aid:D6}:{input.Fingerprint.Sha256}")));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static DatEntry Unique(DatArchive archive, string path, StaticObjectFailureStage stage, int sourceIndex)
    {
        var normalized = Normalize(path);
        var matches = archive.Entries.Where(entry => Normalize(entry.RawNameBytes) == normalized).ToArray();
        if (matches.Length != 1) throw Failure(StaticObjectErrorCode.UnresolvedResource, stage, "A selected resource is missing or ambiguous.", sourceIndex);
        return matches[0];
    }

    private static SortedDictionary<int, string> ParseMapping(byte[] bytes, StaticObjectLoaderOptions limits, int sourceIndex)
    {
        if (bytes.Any(value => value > 0x7f)) throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The scenery mapping is not ASCII.", sourceIndex);
        var text = Encoding.ASCII.GetString(bytes); var result = new SortedDictionary<int, string>(); var cursor = 0;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor] == '\0') break;
            if (text[cursor] == ';' || (cursor + 1 < text.Length && text[cursor] == '/' && text[cursor + 1] == '/'))
            {
                var lineEnd = text.IndexOf('\n', cursor);
                cursor = lineEnd < 0 ? text.Length : lineEnd + 1;
                continue;
            }
            if (text[cursor] != '{') throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The scenery mapping has unsupported syntax.", sourceIndex);
            var keyEnd = text.IndexOf('}', cursor + 1);
            if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), out var key)) throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The scenery mapping key is invalid.", sourceIndex);
            cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            var valueEnd = cursor < text.Length && text[cursor] == '{' ? text.IndexOf('}', cursor + 1) : -1;
            if (valueEnd < 0) throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The scenery mapping is malformed or ambiguous.", sourceIndex);
            var value = text[(cursor + 1)..valueEnd];
            if (value.Length > limits.MaxStringBytes)
                throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Mapping, "A scenery mapping value exceeds the configured string limit.", sourceIndex);
            if (!result.TryAdd(key, value)) throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The scenery mapping is malformed or ambiguous.", sourceIndex);
            if (result.Count > limits.MaxMetadataEntries) throw Failure(StaticObjectErrorCode.LimitExceeded, StaticObjectFailureStage.Mapping, "The scenery mapping exceeds the configured metadata-entry limit.", sourceIndex);
            cursor = valueEnd + 1;
        }
        if (result.Count == 0) throw Failure(StaticObjectErrorCode.UnresolvedResource, StaticObjectFailureStage.Mapping, "The scenery mapping has no entries.", sourceIndex);
        return result;
    }

    private static string Normalize(string value) => Normalize(Encoding.ASCII.GetBytes(value + "\0"));
    private static StaticObjectResourceIdentity ResourceIdentity(DatEntry entry) => new(entry.Ordinal, entry.Flags, entry.DeclaredOutputSize,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(entry.RawNameBytes)))).ToLowerInvariant());
    private static string Normalize(IReadOnlyList<byte> raw)
    {
        if (raw.Count == 0 || raw[^1] != 0 || raw.Take(raw.Count - 1).Any(value => value > 0x7f)) return string.Empty;
        var bytes = new byte[raw.Count - 1];
        for (var index = 0; index < bytes.Length; index++) { var value = raw[index]; bytes[index] = value == (byte)'\\' ? (byte)'/' : value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value; }
        return Encoding.ASCII.GetString(bytes);
    }
    private static bool SafeFileName(string value) => value.Length > 4 && value.EndsWith(".art", StringComparison.OrdinalIgnoreCase) && value.All(c => c is >= ' ' and <= '~' && c is not '/' and not '\\');

    private static StaticObjectRecord ToRecord(ParsedRecord value, StaticObjectFieldStatus status, int? art, bool artInherited, uint? flags, bool flagsInherited,
        bool? isStatic, StaticObjectResourceStatus resourceStatus, StaticObjectArtMetadata? metadata) =>
        new(value.SourceIndex, value.Type, value.PrototypeAid < 0 ? null : value.PrototypeAid, value.DirectCurrentArt,
            value.Type == 3 ? value.DirectFlags : null, value.Type == 3 ? value.Location : null, value.DirectOffsetX, value.DirectOffsetY,
            status, art, artInherited, flags, flagsInherited, isStatic, resourceStatus, metadata);
    private static StaticObjectRecord Copy(StaticObjectRecord value, StaticObjectResourceStatus status, StaticObjectArtMetadata? metadata) =>
        new(value.SourceIndex, value.ObjectType, value.PrototypeAid, value.DirectCurrentArt, value.DirectFlags, value.Location, value.DirectOffsetX, value.DirectOffsetY,
            value.FieldStatus, value.CurrentArt, value.CurrentArtInherited, value.EffectiveFlags, value.FlagsInherited, value.IsStatic, status, metadata);

    private static void Skip(ref Cursor cursor, WireKind kind, StaticObjectLoaderOptions limits)
    {
        switch (kind)
        {
            case WireKind.I32: cursor.Skip(4); return;
            case WireKind.OptI64: _ = cursor.OptionalI64(); return;
            case WireKind.OptObjectId: if (cursor.Present()) _ = cursor.ObjectId(allowBlocked: false); return;
            case WireKind.OptString:
                if (!cursor.Present()) return;
                var length = cursor.I32(); if (length < 0 || length > limits.MaxStringBytes) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "A string exceeds its configured limit.");
                var text = cursor.Bytes(checked(length + 1)); if (text[^1] != 0) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "A string lacks its terminal NUL."); return;
            default:
                if (!cursor.Present()) return;
                var size = cursor.I32(); var count = cursor.I32(); _ = cursor.I32(); var expected = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An object wire kind is unknown.") };
                if (size != expected || count < 0) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An array envelope is invalid.");
                if (count > limits.MaxArrayElements) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "An array element count exceeds its configured limit.");
                cursor.Skip(checked(size * count)); var words = cursor.I32(); if (words < 2) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An array bitset is invalid.");
                if (words > limits.MaxArrayElements) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "An array bitset exceeds its configured limit.");
                var bitset = cursor.Bytes(checked(words * 4)); var population = 0; for (var index = 0; index < words; index++) population += BitOperations.PopCount(BinaryPrimitives.ReadUInt32LittleEndian(bitset.Slice(index * 4, 4)));
                if (population != count) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An array bitset population differs from its element count."); return;
        }
    }

    private static WireKind[] K(string specification)
    {
        var values = new List<WireKind>();
        foreach (var token in specification.Split(','))
        {
            var parts = token.Split('*'); var count = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
            var kind = parts[0] switch { "i" => WireKind.I32, "p64" => WireKind.OptI64, "ai32" => WireKind.ArrI32, "ai64" => WireKind.ArrI64, "au32" => WireKind.ArrU32, "au64" => WireKind.ArrU64, "ascript" => WireKind.ArrScript, "aquest" => WireKind.ArrQuest, "s" => WireKind.OptString, "poid" => WireKind.OptObjectId, "aoid" => WireKind.ArrObjectId, _ => throw new InvalidOperationException("Invalid static-object wire descriptor.") };
            for (var index = 0; index < count; index++) values.Add(kind);
        }
        return values.ToArray();
    }

    private static WireKind[] FieldsFor(int type) => [.. CommonFields, .. FirstTypeFields(type), .. SecondTypeFields(type)];
    private static WireKind[] FirstTypeFields(int type) => type switch
    {
        0 => WallFields,
        1 => PortalFields,
        2 => ContainerFields,
        3 => SceneryFields,
        4 => ProjectileFields,
        >= 5 and <= 14 => ItemFields,
        15 or 16 => CritterFields,
        17 => ShortItemFields,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static WireKind[] SecondTypeFields(int type) => type switch
    {
        5 => WeaponFields,
        6 => AmmoFields,
        7 => ArmorFields,
        8 => GoldFields,
        9 or 10 or 11 or 14 => ShortItemFields,
        12 => KeyRingFields,
        13 => WrittenFields,
        15 => PcFields,
        16 => NpcFields,
        _ => []
    };
    private static int MaskWords(int type) => type is >= 5 and <= 14 ? 4 : type is 15 or 16 ? 5 : 3;
    private static int MaskStart(int type, int word)
    {
        if (word == 0) return 0;
        if (word == 1) return 32;
        if (word == 2) return CommonFields.Length;
        if (type is 15 or 16 && word == 3) return CommonFields.Length + 32;
        if (type is >= 5 and <= 16 && word == 3) return CommonFields.Length + FirstTypeFields(type).Length;
        if (type is 15 or 16 && word == 4) return CommonFields.Length + FirstTypeFields(type).Length;
        throw new InvalidOperationException("The object mask word is invalid.");
    }
    private static int MaskLength(int type, int word)
    {
        if (word == 0) return 32;
        if (word == 1) return CommonFields.Length - 32;
        if (word == 2) return Math.Min(32, FirstTypeFields(type).Length);
        if (type is 15 or 16 && word == 3) return FirstTypeFields(type).Length - 32;
        if (type is >= 5 and <= 16 && word == 3) return SecondTypeFields(type).Length;
        if (type is 15 or 16 && word == 4) return SecondTypeFields(type).Length;
        throw new InvalidOperationException("The object mask word is invalid.");
    }

    private enum WireKind { I32, OptI64, ArrI32, ArrI64, ArrU32, ArrU64, ArrScript, ArrQuest, OptString, OptObjectId, ArrObjectId }
    private readonly record struct ObjectId(short Kind, int? Aid);
    private sealed record ParsedRecord(int SourceIndex, int PrototypeAid, int Type, int? DirectCurrentArt, uint? DirectFlags, long? Location, int? DirectOffsetX, int? DirectOffsetY);
    private sealed record Prototype(int CurrentArt, uint Flags);
    private sealed record PrototypeInput(int Aid, string Path, StaticObjectInputFingerprint? Fingerprint);

    private static StaticObjectException Failure(StaticObjectErrorCode code, StaticObjectFailureStage stage, string message, int? sourceIndex = null, int? offset = null) =>
        new(code, stage, message, sourceIndex, offset);

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _bytes;
        public Cursor(ReadOnlySpan<byte> bytes) { _bytes = bytes; Offset = 0; }
        public int Offset { get; private set; }
        public bool AtEnd => Offset == _bytes.Length;
        public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Bytes(2));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public long? OptionalI64() => Present() ? BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)) : null;
        public bool Present() { var value = Bytes(1)[0]; if (value is not 0 and not 1) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An optional envelope has an invalid presence byte."); return value == 1; }
        public ObjectId ObjectId(bool allowBlocked) { var kind = I16(); Skip(6); var payload = Bytes(16); if (kind == -1 && allowBlocked) return new ObjectId(kind, null); if (kind is < 0 or > 3) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An object identifier form is invalid."); return new ObjectId(kind, kind == 1 ? BinaryPrimitives.ReadInt32LittleEndian(payload) : null); }
        public ReadOnlySpan<byte> Bytes(int count) { if (count < 0 || count > _bytes.Length - Offset) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "The object record is truncated."); var result = _bytes.Slice(Offset, count); Offset = checked(Offset + count); return result; }
        public void Skip(int count) => _ = Bytes(count);
    }
}
