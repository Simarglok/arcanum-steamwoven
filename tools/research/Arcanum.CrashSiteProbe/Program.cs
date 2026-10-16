using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.CrashSiteProbe;

internal static partial class Program
{
    private const string SectorEntryName = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    private const int SectorTileCount = 64 * 64;
    private const int SerializedLightBytes = 48;
    private const int TerminalObjectCountBytes = sizeof(int);
    private const int MaxLights = 1_024;
    private const int MaxDecodedSectorBytes = 1_024 * 1_024;
    private const int ExpectedObjectVersion = 119;
    private const int ExpectedTileEnd = 16_388;
    private const int ExpectedTrailingInt32 = 317;
    private const int ExpectedSectorOrdinal = 463;
    private const int ExpectedSectorDecodedBytes = 47_291;
    private const uint DynamicObjectFlag = 0x00002000;
    // Pinned CE OBJ_F_FLAGS is enum value 19, while serialized common fields are
    // zero-based because OBJ_F_CURRENT_AID (enum value 1) occupies slot 0.
    private const int CurrentArtField = 0;
    private const int LocationField = 1;
    // These are direct instance values only. Their prototype-default behavior
    // is deliberately not inferred by this bounded probe.
    private const int OffsetXField = 2;
    private const int OffsetYField = 3;
    private const int ObjectFlagsField = 18;
    private const int SpellFlagsField = 19;
    private const long ExpectedModuleBytes = 355_938_305;
    private const string ExpectedModuleSha256 = "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f";
    private const long ExpectedResourceBytes = 147_991_122;
    private const string ExpectedResourceSha256 = "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d";
    private static readonly PrototypeFingerprint[] ExpectedPrototypeFingerprints =
    [
        new(4026, new InputFingerprint(414, "1378ddb05e981098eca7816b22a366e6733c9df7c580ccc3f19345f782319874")),
        new(4028, new InputFingerprint(414, "c2442deca958715bc49f5f0504066f951a7d549e5787be41dcded2257e23d6e3")),
        new(4030, new InputFingerprint(406, "f15e7b0ed999ac9eeabcafa1bcddbd55b96dcbba9b2d71a7d485024c06f9c310")),
        new(4031, new InputFingerprint(414, "3c5ad2f204a010b3908d0a02e693128e9f8ff421b4a7dafb7ee3e78b66420c53")),
        new(4038, new InputFingerprint(414, "2531e1a1c6ddca2f483a2d557bebcfd6c1b0b89bd2d996a3f559adc28924214b")),
        new(4048, new InputFingerprint(378, "ec807b77ea283d8d84387993a439953b7acf1a1ef7bdfb30b6a86a334dcfe8d1")),
    ];

    private static int Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--time-lighting") return TimeLightingProbe.Run(args[1..]);
            if (args.SequenceEqual(["--time-lighting-self-test"])) { TimeLightingProbe.SelfTest(); return 0; }
            if (args.FirstOrDefault() == "--area-passability-closure") return ProbeAreaPassabilityClosure(args[1..]);
            if (args.SequenceEqual(["--area-passability-closure-self-test"])) { AreaPassabilityClosureSelfTest(); return 0; }
            if (args.FirstOrDefault() == "--area-traversal-research") return ProbeAreaTraversal(args[1..]);
            if (args.SequenceEqual(["--area-traversal-self-test"])) { AreaTraversalSelfTest(); return 0; }
            if (args.FirstOrDefault() == "--cave-transition") return ProbeCaveTransition(args[1..]);
            if (args.SequenceEqual(["--cave-transition-self-test"])) { CaveTransitionSelfTest(); return 0; }
            if (args.FirstOrDefault() == "--area-inventory") return ProbeArea(args[1..]);
            if (args.SequenceEqual(["--area-self-test"])) { AreaSelfTest(); return 0; }
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS (bounded sector, complete object delimiter, prototype inheritance, and object-flag slot checks)");
                return 0;
            }

            var passability = args.Length == 7 && args[0] == "--passability";
            var cardinal = args.Length == 7 && args[0] == "--cardinal-traversal";
            var argumentOffset = passability || cardinal ? 1 : 0;
            if (args.Length != argumentOffset + 6 || args[argumentOffset] != "--module" || args[argumentOffset + 2] != "--prototype-root" || args[argumentOffset + 4] != "--resource")
            {
                throw new ArgumentException("usage: --self-test | [--passability|--cardinal-traversal] --module PATH --prototype-root PATH --resource PATH");
            }

            ProbeModule(args[argumentOffset + 1], args[argumentOffset + 3], args[argumentOffset + 5], passability, cardinal);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(args.Any(arg => arg.StartsWith("--time-lighting", StringComparison.Ordinal))
                ? $"crash-site-probe: FAIL: {exception.GetType().Name}: {TimeLightingProbe.Diagnostic(exception)}"
                : args.Any(a => a.StartsWith("--area-passability-closure", StringComparison.Ordinal))
                ? $"crash-site-probe: FAIL: {exception.GetType().Name}: {(exception is InvalidDataException or ArgumentException && exception.Message.All(c => c is '_' || char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)) ||
                    exception is InvalidOperationException && exception.Message.StartsWith("closure self-test failed: ", StringComparison.Ordinal) ? exception.Message : "AREA_CLOSURE_INVOCATION_FAILED")}"
                : args.Any(a => a.StartsWith("--cave-transition", StringComparison.Ordinal))
                ? $"crash-site-probe: FAIL: {exception.GetType().Name}: {(exception is InvalidDataException or ArgumentException or InvalidOperationException && exception.Message.All(c => c is '_' or ' ' or ':' or '/' || char.IsAsciiLetter(c) || char.IsAsciiDigit(c)) && exception.Message.StartsWith("CAVE", StringComparison.Ordinal) || exception.Message.StartsWith("cave self-test failed: ", StringComparison.Ordinal) ? exception.Message : "CAVE_INVOCATION_FAILED")}"
                : args.Any(a => a.StartsWith("--area-traversal", StringComparison.Ordinal))
                ? $"crash-site-probe: FAIL: {exception.GetType().Name}: AREA_TRAVERSAL_INVOCATION_FAILED"
                : args.Contains("--cardinal-traversal")
                ? $"crash-site-probe: FAIL: {exception.GetType().Name}: CARDINAL_INVOCATION_FAILED"
                : $"crash-site-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void ProbeModule(string path, string prototypeRoot, string resourcePath, bool passability, bool cardinal)
    {
        var before = InputFingerprint.Create(path);
        if (before.Bytes != ExpectedModuleBytes || before.Sha256 != ExpectedModuleSha256)
        {
            throw new InvalidDataException("Module fingerprint does not match the bounded Crash Site input.");
        }
        var resourceBefore = InputFingerprint.Create(resourcePath);
        if (resourceBefore.Bytes != ExpectedResourceBytes || resourceBefore.Sha256 != ExpectedResourceSha256)
        {
            throw new InvalidDataException("Resource fingerprint does not match the explicit bounded ART source.");
        }

        using var archive = DatArchive.Open(path, BoundedOptions());
        var sectorEntries = archive.Entries.Where(entry => RawNameEquals(entry, SectorEntryName)).ToArray();
        if (sectorEntries.Length != 1)
        {
            throw new InvalidDataException("The bounded sector selector did not resolve exactly one DAT entry.");
        }

        var entry = sectorEntries[0];
        if (entry.Ordinal != ExpectedSectorOrdinal || entry.Flags != 0x00000002 || entry.DeclaredOutputSize != ExpectedSectorDecodedBytes)
        {
            throw new InvalidDataException("The selected sector entry does not match the fixed ordinal, compression, and decoded-length profile.");
        }

        var sectorBytes = archive.ReadEntry(entry);
        var sector = SectorSlice.Parse(sectorBytes, rejectNonTileArtIds: false);
        var framing = SectorFraming.Parse(sectorBytes, sector.LightCount);
        if (framing.TileEnd != ExpectedTileEnd || sector.TrailingInt32 != ExpectedTrailingInt32)
        {
            throw new InvalidDataException("The selected sector no longer matches the frozen prefix and terminal framing measurements.");
        }
        var instances = ObjectWire.ParseWindow(sectorBytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), sector.TrailingInt32);
        var sceneryInstances = instances.Where(instance => instance.Type == 3).ToArray();
        var inheritedPrototypeIds = sceneryInstances.Where(instance => !instance.DirectCurrentAid.HasValue || !instance.DirectFlags.HasValue).Select(instance => instance.PrototypeId).ToArray();
        var prototypeRead = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, inheritedPrototypeIds, ExpectedPrototypeFingerprints);
        var resolved = ObjectWire.ResolveEffectiveInstances(sceneryInstances, prototypeRead.Prototypes);
        var typeCounts = string.Join(',', instances.GroupBy(instance => instance.Type).OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}"));
        var staticScenery = resolved.Where(instance => instance.Instance.Type == 3 && (instance.EffectiveFlags & DynamicObjectFlag) == 0).ToArray();
        using var resource = DatArchive.Open(resourcePath, BoundedOptions());
        if (passability || cardinal)
        {
            var inspection = StaticObjectLoader.Inspect(
                new StaticObjectSource(archive, SectorEntryName, resource),
                new StaticObjectPrototypeSource(prototypeRoot));
            var selector = new TerrainSectorSelector(SectorEntryName, "Arcanum1-024-fixed", 1452, 1290, 86_570_436_012, 92_928, 82_560, 64, 64,
                new TerrainEntryExpectation(ExpectedSectorOrdinal, 0x00000002, ExpectedSectorDecodedBytes));
            var source = new TerrainTileNameSource(resource, "arcanum2-base", "art\\tile\\tilename.mes", TerrainPathProfile.CeNonWindowsMacOs);
            var report = TerrainObstructionInspector.InspectSector(archive, "arcanum-module", selector, source, new TerrainLoaderOptions
            {
                MaxSectorDecodedEntryBytes = MaxDecodedSectorBytes,
                MaxMesDecodedEntryBytes = MaxDecodedSectorBytes,
            });
            if (cardinal)
            {
                var staticReport = StaticObstructionInspector.InspectSector(
                    new StaticObjectSource(archive, SectorEntryName, resource), new StaticObjectPrototypeSource(prototypeRoot),
                    "CRASH_SITE_MODULE", "CRASH_SITE_PROTOTYPES", selector);
                CardinalTraversalProbe.Emit(report, staticReport, inspection, sectorBytes, framing.TileStart,
                    framing.BlockMaskStart, framing.TileScriptCount, framing.SectorScriptBytes,
                    before.Sha256, resourceBefore.Sha256, prototypeRead.FingerprintAggregate);
            }
            else PassabilityProbe.EmitStaticObstruction(report, inspection);
            var passabilityAfter = InputFingerprint.Create(path);
            var passabilityResourceAfter = InputFingerprint.Create(resourcePath);
            if (passabilityAfter != before || passabilityResourceAfter != resourceBefore)
                throw new InvalidDataException("An immutable probe input changed during the read-only passability check.");
            ObjectWire.VerifyExpectedPrototypeManifest(prototypeRead, ExpectedPrototypeFingerprints, requireUnchanged: true);
            Console.WriteLine("integrity before_after_match=true");
            return;
        }
        var artExamples = ObjectArt.ResolveExamples(resource, staticScenery.Take(5).ToArray());

        Console.WriteLine($"input bytes={before.Bytes} sha256={before.Sha256}");
        Console.WriteLine($"resource bytes={resourceBefore.Bytes} sha256={resourceBefore.Sha256}");
        Console.WriteLine($"sector ordinal={entry.Ordinal} flags=0x{entry.Flags:x8} decoded_bytes={entry.DeclaredOutputSize}");
        Console.WriteLine($"sector lights={sector.LightCount} tiles={SectorTileCount} distinct_tile_art_ids={sector.DistinctTileArtIds} invalid_tile_art_ids={sector.InvalidTileArtIds} non_tile_art_ids={sector.NonTileArtIds}");
        Console.WriteLine($"sector art_type_counts={string.Join(',', sector.ArtTypeCounts.Select(count => count.ToString()))}");
        Console.WriteLine($"framing light_count start=0 length=4 end=4 value={sector.LightCount}");
        Console.WriteLine($"framing tile_words start={framing.TileStart} length={framing.TileBytes} end={framing.TileEnd} formula=4+(light_count*48),4096*4");
        Console.WriteLine($"framing roof start={framing.RoofStart} length={framing.RoofBytes} end={framing.RoofEnd} empty={framing.RoofEmpty}");
        Console.WriteLine($"framing placeholder start={framing.PlaceholderStart} length=4 end={framing.PlaceholderEnd} value=0x{framing.Placeholder:x6}");
        Console.WriteLine($"framing tile_scripts start={framing.TileScriptsStart} length={framing.TileScriptsBytes} end={framing.TileScriptsEnd} count={framing.TileScriptCount}");
        Console.WriteLine($"framing sector_script start={framing.SectorScriptStart} length={framing.SectorScriptBytes} end={framing.SectorScriptEnd}");
        Console.WriteLine($"framing metadata_and_sound start={framing.MetadataAndSoundStart} length={framing.MetadataAndSoundBytes} end={framing.MetadataAndSoundEnd}");
        Console.WriteLine($"framing block_mask start={framing.BlockMaskStart} length={framing.BlockMaskBytes} end={framing.BlockMaskEnd}");
        Console.WriteLine($"framing objects start={framing.ObjectStart} length={framing.ObjectBytes} end={framing.ObjectEnd} count={sector.TrailingInt32} first_le_i32={framing.FirstObjectVersion}");
        Console.WriteLine($"framing trailing_i32 start={framing.TrailingInt32Start} length=4 end={framing.TrailingInt32End} value={sector.TrailingInt32}");
        Console.WriteLine($"objects records={instances.Count} consumed_bytes={framing.ObjectBytes} cursor={framing.ObjectEnd} type_counts={typeCounts}");
        Console.WriteLine($"objects scenery_type3={sceneryInstances.Length} current_aid_resolution=validated direct={resolved.Count(instance => !instance.CurrentAidInherited)} inherited={resolved.Count(instance => instance.CurrentAidInherited)} flags_resolution=validated direct={resolved.Count(instance => instance.Instance.DirectFlags.HasValue)} inherited={resolved.Count(instance => !instance.Instance.DirectFlags.HasValue)} prototype_files={prototypeRead.Prototypes.Count} prototype_fingerprint_aggregate={prototypeRead.FingerprintAggregate} static_scenery_type3={staticScenery.Length}");
        foreach (var example in artExamples)
        {
            var location = example.Object.Instance.Location!.Value;
            var localX = location & 0x3f;
            var localY = (location >> 32) & 0x3f;
            var offsetX = example.Object.Instance.DirectOffsetX?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent";
            var offsetY = example.Object.Instance.DirectOffsetY?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent";
            DiagnosticPlacement? placement = example.Object.Instance.DirectOffsetX is int directOffsetX && example.Object.Instance.DirectOffsetY is int directOffsetY
                ? DiagnosticPlacement.For(localX, localY, directOffsetX, directOffsetY, example.HotX, example.HotY)
                : null;
            var anchor = placement is null ? "absent" : $"{placement.Value.AnchorX},{placement.Value.AnchorY}";
            var topLeft = placement is null ? "absent" : $"{placement.Value.TopLeftX},{placement.Value.TopLeftY}";
            Console.WriteLine($"object_example source_index={example.Object.SourceIndex} prototype_aid={example.Object.Instance.PrototypeId.AidValue()} local={localX},{localY} direct_offset_x={offsetX} direct_offset_y={offsetY} diagnostic_anchor={anchor} diagnostic_top_left={topLeft} current_aid=0x{unchecked((uint)example.Object.CurrentAid):x8} current_aid_source={(example.Object.CurrentAidInherited ? "prototype" : "instance")} art_type={example.ArtType} scenery_category={example.SceneryCategory} art_number={example.ArtNumber} rotation_group={example.RotationGroup} frame={example.Frame} palette={example.Palette} frame_width={example.Width} frame_height={example.Height} frame_hotspot={example.HotX},{example.HotY} frame_offset={example.ArtOffsetX},{example.ArtOffsetY}");
        }
        var after = InputFingerprint.Create(path);
        if (after != before)
        {
            throw new InvalidDataException("The module changed during this read-only probe.");
        }
        var resourceAfter = InputFingerprint.Create(resourcePath);
        if (resourceAfter != resourceBefore)
        {
            throw new InvalidDataException("The explicit ART source changed during this read-only probe.");
        }
        ObjectWire.VerifyExpectedPrototypeManifest(prototypeRead, ExpectedPrototypeFingerprints, requireUnchanged: true);
        Console.WriteLine("integrity before_after_match=true");
    }

    private static DatReaderOptions BoundedOptions() => new()
    {
        MaxArchiveBytes = checked((uint)ExpectedModuleBytes),
        MaxStoredEntryBytes = MaxDecodedSectorBytes,
        MaxDecodedEntryBytes = MaxDecodedSectorBytes,
    };

    private static bool RawNameEquals(DatEntry entry, string expected)
    {
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected);
        return entry.RawNameBytes.Count == expectedBytes.Length + 1 && entry.RawNameBytes[^1] == 0 && entry.RawNameBytes.Take(expectedBytes.Length).SequenceEqual(expectedBytes);
    }

    private static void SelfTest()
    {
        PassabilityProbe.SelfTest();
        CardinalTraversalProbe.SelfTest();
        var valid = CreateFramedFixture(trailingObjectCount: 2);
        var sector = SectorSlice.Parse(valid, rejectNonTileArtIds: true);
        var framing = SectorFraming.Parse(valid, sector.LightCount);
        if (sector.TrailingInt32 != 2 || framing.TileEnd != 16_388 || framing.RoofEmpty != 1 || framing.Placeholder != 0xAA0001 || framing.TileScriptCount != 0 || framing.ObjectBytes != 0) throw new InvalidOperationException("The valid synthetic sector framing was not parsed as expected.");
        AssertRejected(() => SectorSlice.Parse(valid[..3], true), "truncated light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, -1), true), "negative light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, MaxLights + 1), true), "light count over cap");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)) - 1)], true), "truncated tile table");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)))], true), "missing terminal object count after complete tile table");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(0, 0x10000000, 0), true), "non-tile art identifier");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(0, 0, -1), true), "negative object count");
        AssertRejected(() => SectorFraming.Parse(valid[..(ExpectedTileEnd + 3)], 0), "truncated roof discriminator");
        AssertRejected(() => SectorFraming.Parse(WithI32(valid, ExpectedTileEnd, 2), 0), "invalid roof discriminator");
        AssertRejected(() => SectorFraming.Parse(WithI32(valid, ExpectedTileEnd + sizeof(int), 0xAA0005), 0), "invalid placeholder");
        AssertRejected(() => SectorFraming.Parse(WithI32(valid, ExpectedTileEnd + (sizeof(int) * 2), SectorTileCount + 1), 0), "oversized tile-script count");
        var completeFraming = SectorFraming.Parse(CreateFramedFixture(0, placeholder: 0xAA0004), 0);
        if (completeFraming.SectorScriptBytes != 12 || completeFraming.MetadataAndSoundBytes != 24 || completeFraming.BlockMaskBytes != 512) throw new InvalidOperationException("The complete placeholder-controlled framing branches were not consumed as expected.");
        AssertRejected(() => SectorFraming.Parse(CreateFramedFixture(0, placeholder: 0xAA0004)[..^1], 0), "511-byte block-mask framing");
        AssertProjection(0, 0, 0, 0); AssertProjection(1, 0, -40, 20); AssertProjection(0, 1, 40, 20); AssertProjection(30, 32, 80, 1240);

        var prototypeId = ObjectId.A(7001);
        foreach (var id in new[] { ObjectId.Null(), prototypeId, ObjectId.Guid(23), ObjectId.P(7, 8, 9) })
        {
            var form = ObjectWire.CreateSyntheticInstance(3, id, true, true, currentAid: 101);
            if (ObjectWire.ParseWindow(form, 1)[0].DirectCurrentAid != 101) throw new InvalidOperationException("A persisted ObjectID form did not retain the direct AID.");
        }
        foreach (var type in Enumerable.Range(0, 18))
        {
            var instance = ObjectWire.CreateSyntheticInstance(type, prototypeId, true, true, currentAid: 1000 + type);
            var parsed = ObjectWire.ParseWindow(instance, 1);
            if (parsed.Count != 1 || parsed[0].Type != type || parsed[0].DirectCurrentAid != 1000 + type) throw new InvalidOperationException("A supported type or wire kind was not delimited in the synthetic suite.");
        }

        var inherited = ObjectWire.CreateSyntheticInstance(3, prototypeId, false, true, flagsOverridden: false);
        var direct = ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, currentAid: 41, offsetX: -7, offsetY: 13);
        var chained = ObjectWire.ParseWindow(inherited.Concat(direct).ToArray(), 2);
        if (chained.Count != 2 || chained[0].Location is null || chained[1].DirectCurrentAid != 41 || chained[1].DirectOffsetX != -7 || chained[1].DirectOffsetY != 13) throw new InvalidOperationException("Synthetic record chaining or direct offset retention failed.");
        if (DiagnosticPlacement.For(6, 1, -7, 13, 34, 39) != new DiagnosticPlacement(-207, 153, -241, 114)) throw new InvalidOperationException("Synthetic direct-offset diagnostic placement failed.");
        var prototype = ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 73));
        if (!prototype.Id.Matches(prototypeId) || prototype.Type != 3 || prototype.CurrentAid != 73) throw new InvalidOperationException("Synthetic prototype validation failed.");
        if (!ObjectWire.ResolveCurrentAids(chained, [prototype]).SequenceEqual([73, 41])) throw new InvalidOperationException("Direct and inherited AID resolution did not return concrete values.");
        AssertStaticFlagResolution(prototypeId);
        AssertRejected(() => ObjectWire.ResolveCurrentAids(chained, []), "missing prototype");
        AssertRejected(() => ObjectWire.ResolveCurrentAids(chained, [prototype, prototype]), "duplicate prototype");
        AssertRejected(() => ObjectWire.ResolveCurrentAids(chained, [ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(4, prototypeId, 73))]), "type-mismatched prototype");
        AssertRejected(() => ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(3, prototypeId, 73)[..^1]), "malformed prototype");

        AssertRejected(() => ObjectWire.ParseWindow(WithI32(direct, 0, 118), 1), "wrong object version");
        AssertRejected(() => ObjectWire.ParseWindow(direct[..^1], 1), "one-byte record truncation");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateUncheckedInstance(18, prototypeId), 1), "type 18");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateUncheckedInstance(19, prototypeId), 1), "type 19");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 4, -2), 1), "runtime ObjectID form");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 4, -1), 1), "prototype ObjectID marker in sector");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 4, 4), 1), "invalid ObjectID form");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 56, 0), 1), "field-count mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(WithU32(direct, 62, 0x80000000), 1), "reserved mask bit");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, invalidPresence: true), 1), "invalid presence byte");

        var stringFixture = ObjectWire.CreateSyntheticSingleField(15, WireKind.OptString, out var stringOffset);
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(stringFixture, stringOffset + 1, -1), 1), "negative string length");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(stringFixture, stringOffset + 1, MaxDecodedSectorBytes + 1), 1), "oversized string length");
        AssertRejected(() => ObjectWire.ParseWindow(WithByte(stringFixture, stringOffset + 5, 1), 1), "missing string NUL");
        AssertRejected(() => ObjectWire.ParseWindow(stringFixture[..^1], 1), "truncated string");

        var arrayFixture = ObjectWire.CreateSyntheticSingleField(3, WireKind.ArrU32, out var arrayOffset);
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 1, 8), 1), "array element size mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 5, -1), 1), "negative array count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 5, (MaxDecodedSectorBytes / 4) + 1), 1), "oversized array count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 17, -1), 1), "negative bitset word count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 17, (MaxDecodedSectorBytes / 4) + 1), 1), "oversized bitset word count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 17, 1), 1), "bitset count mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(WithU32(arrayFixture, arrayOffset + 21, 0), 1), "bitset popcount mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(arrayFixture[..^1], 1), "truncated array or bitset");

        AssertPrototypeArchivePath(inherited, prototypeId);
        AssertPrototypeDirectoryPath(inherited, prototypeId);
        AssertObjectArtResolution();
    }

    private static void AssertProjection(int localX, int localY, int expectedX, int expectedY)
    {
        if (checked(40 * (localY - localX)) != expectedX || checked(20 * (localY + localX)) != expectedY) throw new InvalidOperationException("Synthetic numeric projection regression failed.");
    }

    private static void AssertStaticFlagResolution(ObjectId prototypeId)
    {
        // Test-only raw payload offsets for the fixed all-fields type-3 layouts:
        // instance header 70 + literal slots 0..17 = 297/301; prototype header 68
        // + literal slots 0..17 = 295/299. These deliberately do not use decoder
        // field constants, semantic fixture arguments, or field-layout helpers.
        const int InstanceSlot18RawOffset = 297;
        const int InstanceSlot19RawOffset = 301;
        const int PrototypeSlot18RawOffset = 295;
        const int PrototypeSlot19RawOffset = 299;
        const uint StaticFlagSentinel = 0x00000004;

        var directStaticBytes = ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, currentAid: 41, flags: 0, spellFlags: 0);
        var directDynamicBytes = ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, currentAid: 42, flags: 0, spellFlags: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(directStaticBytes.AsSpan(InstanceSlot18RawOffset), StaticFlagSentinel);
        BinaryPrimitives.WriteUInt32LittleEndian(directStaticBytes.AsSpan(InstanceSlot19RawOffset), DynamicObjectFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(directDynamicBytes.AsSpan(InstanceSlot18RawOffset), DynamicObjectFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(directDynamicBytes.AsSpan(InstanceSlot19RawOffset), StaticFlagSentinel);
        if (BinaryPrimitives.ReadUInt32LittleEndian(directStaticBytes.AsSpan(InstanceSlot18RawOffset)) != StaticFlagSentinel ||
            BinaryPrimitives.ReadUInt32LittleEndian(directStaticBytes.AsSpan(InstanceSlot19RawOffset)) != DynamicObjectFlag ||
            BinaryPrimitives.ReadUInt32LittleEndian(directDynamicBytes.AsSpan(InstanceSlot18RawOffset)) != DynamicObjectFlag ||
            BinaryPrimitives.ReadUInt32LittleEndian(directDynamicBytes.AsSpan(InstanceSlot19RawOffset)) != StaticFlagSentinel)
        {
            throw new InvalidOperationException("Direct literal slot fixture bytes changed unexpectedly.");
        }

        var directStatic = ObjectWire.ParseWindow(directStaticBytes, 1)[0];
        var directDynamic = ObjectWire.ParseWindow(directDynamicBytes, 1)[0];
        var directlyResolved = ObjectWire.ResolveEffectiveInstances([directStatic, directDynamic], []);
        if (directlyResolved[0].EffectiveFlags != StaticFlagSentinel || directlyResolved[1].EffectiveFlags != DynamicObjectFlag ||
            directlyResolved.Count(instance => instance.Instance.Type == 3 && (instance.EffectiveFlags & DynamicObjectFlag) == 0) != 1)
        {
            throw new InvalidOperationException("Direct literal object-flags slots were not distinguished from adjacent spell flags in the static predicate.");
        }

        const uint NoBlockFlag = 0x00000400;
        var directNoBlock = ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true,
            currentAid: 75, flags: NoBlockFlag, spellFlags: 0), 1)[0];
        var oppositeBlockerPrototype = ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(3, prototypeId,
            currentAid: 75, flags: 0, spellFlags: 0));
        var directOverride = ObjectWire.ResolveEffectiveInstances([directNoBlock], [oppositeBlockerPrototype])[0];
        if (directNoBlock.DirectFlags != NoBlockFlag || oppositeBlockerPrototype.Flags != 0 ||
            directOverride.EffectiveFlags != NoBlockFlag || directOverride.CurrentAidInherited)
        {
            throw new InvalidOperationException("Direct slot-18 flags did not override an opposite matching prototype default.");
        }
        PassabilityProbe.AssertResolvedDirectOverride(directOverride.EffectiveFlags, oppositeBlockerPrototype.Flags);
        CardinalTraversalProbe.AssertParsedFlags(directOverride.EffectiveFlags, expectedOpen: true, dynamic: false);
        CardinalTraversalProbe.AssertParsedFlags(directlyResolved[0].EffectiveFlags, expectedOpen: false, dynamic: false);
        CardinalTraversalProbe.AssertParsedFlags(directlyResolved[1].EffectiveFlags, expectedOpen: false, dynamic: true);

        var inherited = ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, prototypeId, false, true, flagsOverridden: false, spellFlags: 0), 1)[0];
        var staticPrototypeBytes = ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 73, flags: 0, spellFlags: 0);
        var dynamicPrototypeBytes = ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 74, flags: 0, spellFlags: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(staticPrototypeBytes.AsSpan(PrototypeSlot18RawOffset), StaticFlagSentinel);
        BinaryPrimitives.WriteUInt32LittleEndian(staticPrototypeBytes.AsSpan(PrototypeSlot19RawOffset), DynamicObjectFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(dynamicPrototypeBytes.AsSpan(PrototypeSlot18RawOffset), DynamicObjectFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(dynamicPrototypeBytes.AsSpan(PrototypeSlot19RawOffset), StaticFlagSentinel);
        if (BinaryPrimitives.ReadUInt32LittleEndian(staticPrototypeBytes.AsSpan(PrototypeSlot18RawOffset)) != StaticFlagSentinel ||
            BinaryPrimitives.ReadUInt32LittleEndian(staticPrototypeBytes.AsSpan(PrototypeSlot19RawOffset)) != DynamicObjectFlag ||
            BinaryPrimitives.ReadUInt32LittleEndian(dynamicPrototypeBytes.AsSpan(PrototypeSlot18RawOffset)) != DynamicObjectFlag ||
            BinaryPrimitives.ReadUInt32LittleEndian(dynamicPrototypeBytes.AsSpan(PrototypeSlot19RawOffset)) != StaticFlagSentinel)
        {
            throw new InvalidOperationException("Inherited literal slot fixture bytes changed unexpectedly.");
        }

        var inheritedStatic = ObjectWire.ResolveEffectiveInstances([inherited], [ObjectWire.ParsePrototype(staticPrototypeBytes)])[0];
        var inheritedDynamic = ObjectWire.ResolveEffectiveInstances([inherited], [ObjectWire.ParsePrototype(dynamicPrototypeBytes)])[0];
        CardinalTraversalProbe.AssertParsedFlags(inheritedStatic.EffectiveFlags, expectedOpen: false, dynamic: false);
        CardinalTraversalProbe.AssertParsedFlags(inheritedDynamic.EffectiveFlags, expectedOpen: false, dynamic: true);
        if (inherited.DirectFlags is not null || inheritedStatic.EffectiveFlags != StaticFlagSentinel || inheritedDynamic.EffectiveFlags != DynamicObjectFlag ||
            (inheritedStatic.EffectiveFlags & DynamicObjectFlag) != 0 || (inheritedDynamic.EffectiveFlags & DynamicObjectFlag) == 0)
        {
            throw new InvalidOperationException("Inherited literal object-flags slots were not distinguished from adjacent spell flags in the static predicate.");
        }
    }

    private static void AssertObjectArtResolution()
    {
        var currentAid = unchecked((int)0x40000000u);
        var example = new ResolvedInstance(0, new InstanceInfo(0, ObjectId.A(7001), 3, currentAid, 0, null, null, 0), currentAid, 0, false);
        var mapping = System.Text.Encoding.ASCII.GetBytes("{0}{fixture.art}\n");
        var art = CreateSyntheticArt();
        WithSyntheticArchive(CreateSyntheticDat(
            new SyntheticDatEntry(NameBytes("art\\scenery\\scenery.mes"), mapping),
            new SyntheticDatEntry(NameBytes("art\\scenery\\fixture.art"), art)), (archive, stream) =>
        {
            var resolved = ObjectArt.ResolveExamples(archive, [example]);
            if (resolved.Count != 1 || resolved[0].ArtType != 4 || resolved[0].SceneryCategory != 0 || resolved[0].ArtNumber != 0 || resolved[0].RotationGroup != 0 || resolved[0].Frame != 0 || resolved[0].Palette != 0 || resolved[0].Width != 1 || resolved[0].Height != 1)
            {
                throw new InvalidOperationException("Synthetic scenery ART resolution did not retain the selected metadata.");
            }
            AssertPayloadReadPattern(archive, stream, 0, 1);
        });
        AssertArchiveArtRejected(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("art\\scenery\\scenery.mes"), System.Text.Encoding.ASCII.GetBytes("{1}{fixture.art}\n"))), example, "missing scenery mapping");
        AssertArchiveArtRejected(CreateSyntheticDat(
            new SyntheticDatEntry(NameBytes("art\\scenery\\scenery.mes"), mapping),
            new SyntheticDatEntry(NameBytes("art\\scenery\\fixture.art"), art),
            new SyntheticDatEntry(NameBytes("ART\\SCENERY\\FIXTURE.ART"), art)), example, "ambiguous scenery ART");
    }

    private static void AssertArchiveArtRejected(SyntheticDatFixture fixture, ResolvedInstance example, string label)
    {
        WithSyntheticArchive(fixture, (archive, _) => AssertRejected(() => ObjectArt.ResolveExamples(archive, [example]), label));
    }

    private static byte[] CreateSyntheticArt()
    {
        var bytes = new byte[132 + 1024 + 28 + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        var frame = 132 + 1024;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1);
        bytes[^1] = 1;
        return bytes;
    }

    private static byte[] CreateFixture(int lightCount, uint tileId, int trailingObjectCount)
    {
        var tileStart = checked(sizeof(int) + (lightCount * SerializedLightBytes));
        var bytes = new byte[checked(tileStart + (SectorTileCount * sizeof(uint)) + sizeof(int))];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, lightCount);
        for (var index = 0; index < SectorTileCount; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tileStart + (index * sizeof(uint))), tileId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - sizeof(int)), trailingObjectCount);
        return bytes;
    }

    private static byte[] CreateFramedFixture(int trailingObjectCount, int placeholder = 0xAA0001, int tileScriptCount = 0)
    {
        var tileEnd = sizeof(int) + (SectorTileCount * sizeof(uint));
        var suffixBytes = checked((sizeof(int) * 2) + (placeholder == 0xAA0000 ? 0 : checked(sizeof(int) + (tileScriptCount * 24))) + (placeholder >= 0xAA0002 ? 12 : 0) + (placeholder >= 0xAA0003 ? 24 : 0) + (placeholder >= 0xAA0004 ? 128 * sizeof(uint) : 0) + sizeof(int));
        var bytes = new byte[tileEnd + suffixBytes];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(tileEnd), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(tileEnd + sizeof(int)), placeholder);
        if (placeholder != 0xAA0000) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(tileEnd + (sizeof(int) * 2)), tileScriptCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - sizeof(int)), trailingObjectCount);
        return bytes;
    }

    private static byte[] WithI32(byte[] source, int offset, int value)
    {
        var copy = source.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] WithI16(byte[] source, int offset, short value)
    {
        var copy = source.ToArray();
        BinaryPrimitives.WriteInt16LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] WithU32(byte[] source, int offset, uint value)
    {
        var copy = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] WithByte(byte[] source, int offset, byte value)
    {
        var copy = source.ToArray();
        copy[offset] = value;
        return copy;
    }

    private static void AssertRejected(Action action, string label, string? expectedMessageFragment = null)
    {
        try { action(); }
        catch (InvalidDataException exception)
        {
            if (expectedMessageFragment is not null && !exception.Message.Contains(expectedMessageFragment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} rejected from the wrong validation stage.");
            }
            return;
        }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    private static void AssertPrototypeArchivePath(byte[] inheritedBytes, ObjectId prototypeId)
    {
        var validPrototype = ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 73);
        var happy = CreateSyntheticDat(
            new SyntheticDatEntry(NameBytes("PrOtO\\007001 - fixture.PrO"), validPrototype, Compressed: true),
            new SyntheticDatEntry(NameBytes("proto\\007001 - fixtureNpro"), [1]),
            new SyntheticDatEntry(NameBytes("proto\\007001 - fixture.txt"), [1]),
            new SyntheticDatEntry(NameBytes("proto\\007002 - unrelated.pro"), [1]));
        WithSyntheticArchive(happy, (archive, stream) =>
        {
            var instance = ObjectWire.ParseWindow(inheritedBytes, 1);
            if (instance[0].CurrentAidOverridden) throw new InvalidOperationException("The inherited archive-path instance unexpectedly overrides its current AID.");
            var prototypes = ObjectWire.ReadReferencedPrototypes(archive, [prototypeId, prototypeId]);
            if (!ObjectWire.ResolveCurrentAids(instance, prototypes).SequenceEqual([73])) throw new InvalidOperationException("The archive-path prototype did not resolve inherited AID 73.");
            AssertPayloadReadPattern(archive, stream, 0);
        });

        AssertArchiveRejected(CreateSyntheticDat(), [prototypeId], "missing canonical prototype", [], "missing or duplicated");
        AssertArchiveRejected(CreateSyntheticDat(
            new SyntheticDatEntry(NameBytes("proto\\007001 - first.pro"), validPrototype),
            new SyntheticDatEntry(NameBytes("PROTO\\007001 - second.PRO"), validPrototype)), [prototypeId], "duplicate canonical prototype", [], "missing or duplicated");
        AssertArchiveRejected(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("proto\\007001 - truncated.pro"), validPrototype[..^1])), [prototypeId], "malformed canonical prototype", [0], "Object record is truncated");
        AssertArchiveRejected(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("proto\\007001 - wrong-id.pro"), ObjectWire.CreateSyntheticPrototype(3, ObjectId.A(7002), 73))), [prototypeId], "own-ID-mismatched canonical prototype", [0], "canonical requested A ID");

        WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("proto\\007001 - type.pro"), ObjectWire.CreateSyntheticPrototype(4, prototypeId, 73))), (archive, stream) =>
        {
            var prototypes = ObjectWire.ReadReferencedPrototypes(archive, [prototypeId]);
            AssertRejected(() => ObjectWire.ResolveCurrentAids(ObjectWire.ParseWindow(inheritedBytes, 1), prototypes), "type-mismatched archive prototype", "inherited current ART ID");
            AssertPayloadReadPattern(archive, stream, 0);
        });

        foreach (var unlocatable in new[] { ObjectId.Null(), ObjectId.Guid(23), ObjectId.P(7, 8, 9), ObjectId.A(-1), ObjectId.A(1_000_000) })
        {
            var parsed = ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, unlocatable, false, true), 1);
            AssertArchiveRejected(happy, [parsed[0].PrototypeId], "unlocatable inherited prototype", [], "locatable");
        }

        AssertCanonicalArchiveAcceptance(0);
        AssertCanonicalArchiveAcceptance(999999);
        foreach (var invalid in new (string Label, byte[] Name)[]
        {
            ("five digits", NameBytes("proto\\07001 - fixture.pro")),
            ("seven digits", NameBytes("proto\\0007001 - fixture.pro")),
            ("altered backslash", NameBytes("proto/007001 - fixture.pro")),
            ("altered space", NameBytes("proto\\007001_- fixture.pro")),
            ("altered hyphen", NameBytes("proto\\007001 + fixture.pro")),
            ("altered period", NameBytes("proto\\007001 - fixtureXpro")),
            ("Npro suffix", NameBytes("proto\\007001 - fixtureNpro")),
            ("non-pro extension", NameBytes("proto\\007001 - fixture.txt")),
            ("embedded NUL", System.Text.Encoding.ASCII.GetBytes("proto\\007001 - fi\0xture.pro\0")),
            ("empty label", NameBytes("proto\\007001 - .pro")),
        })
        {
            AssertCanonicalNameRejected(invalid.Name, invalid.Label);
            AssertArchiveRejected(CreateSyntheticDat(new SyntheticDatEntry(invalid.Name, [1])), [prototypeId], invalid.Label, [], "missing or duplicated");
        }
        AssertCanonicalNameRejected(System.Text.Encoding.ASCII.GetBytes("proto\\007001 - fixture.pro"), "missing terminal NUL");
        AssertCanonicalNameRejected(System.Text.Encoding.ASCII.GetBytes("proto\\007001 - fixture.pro\0x"), "extra byte after terminal NUL");
    }

    private static void AssertPrototypeDirectoryPath(byte[] inheritedBytes, ObjectId prototypeId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ArcanumCrashSiteProbe-{Guid.NewGuid():N}");
        var first = Path.Combine(root, "007001 - first.pro");
        var second = Path.Combine(root, "007001 - second.pro");
        try
        {
            Directory.CreateDirectory(root);
            var valid = ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 73);
            File.WriteAllBytes(first, valid);
            var resolved = ObjectWire.ReadReferencedPrototypesFromDirectory(root, [prototypeId]);
            if (resolved.Prototypes.Count != 1 || resolved.FingerprintAggregate.Length != 64 ||
                !ObjectWire.ResolveCurrentAids(ObjectWire.ParseWindow(inheritedBytes, 1), resolved.Prototypes).SequenceEqual([73]))
            {
                throw new InvalidOperationException("The directory-path prototype did not resolve inherited AID 73.");
            }
            var expected = resolved.Inputs.Select(input => input.Fingerprint).ToArray();
            ObjectWire.VerifyExpectedPrototypeManifest(resolved, expected, requireUnchanged: true);
            AssertRejected(() => ObjectWire.ReadReferencedPrototypesFromDirectory(root, [prototypeId], [new PrototypeFingerprint(prototypeId.AidValue(), new InputFingerprint(1, "mismatch"))]), "fixed prototype fingerprint mismatch");

            File.WriteAllBytes(second, valid);
            AssertRejected(() => ObjectWire.ReadReferencedPrototypesFromDirectory(root, [prototypeId]), "ambiguous directory prototype");
            File.Delete(second);
            File.WriteAllBytes(first, valid[..^1]);
            AssertRejected(() => ObjectWire.ReadReferencedPrototypesFromDirectory(root, [prototypeId]), "truncated directory prototype");
            File.WriteAllBytes(first, ObjectWire.CreateSyntheticPrototype(3, ObjectId.A(7002), currentAid: 73));
            AssertRejected(() => ObjectWire.ReadReferencedPrototypesFromDirectory(root, [prototypeId]), "ID-mismatched directory prototype");
            File.Delete(first);
            AssertRejected(() => ObjectWire.ReadReferencedPrototypesFromDirectory(root, [prototypeId]), "missing directory prototype");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertCanonicalArchiveAcceptance(int aid)
    {
        var id = ObjectId.A(aid);
        WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes($"pRoTo\\{aid:D6} - boundary.pRo"), ObjectWire.CreateSyntheticPrototype(3, id, 73), Compressed: true)), (archive, stream) =>
        {
            var prototypes = ObjectWire.ReadReferencedPrototypes(archive, [id]);
            if (prototypes.Count != 1 || !prototypes[0].Id.Matches(id)) throw new InvalidOperationException("A canonical six-digit boundary candidate was not retained.");
            AssertPayloadReadPattern(archive, stream, 0);
        });
    }

    private static void AssertCanonicalNameRejected(byte[] rawName, string label)
    {
        if (ObjectWire.TryParseCanonicalPrototypeAid(rawName, out _)) throw new InvalidOperationException($"The {label} grammar was accepted.");
    }

    private static void AssertArchiveRejected(SyntheticDatFixture fixture, ObjectId[] referenced, string label, int[] expectedReadOrdinals, string? expectedMessageFragment = null)
    {
        WithSyntheticArchive(fixture, (archive, stream) =>
        {
            AssertRejected(() => ObjectWire.ReadReferencedPrototypes(archive, referenced), label, expectedMessageFragment);
            AssertPayloadReadPattern(archive, stream, expectedReadOrdinals);
        });
    }

    private static void WithSyntheticArchive(SyntheticDatFixture fixture, Action<DatArchive, TrackingReadStream> action)
    {
        using var stream = new TrackingReadStream(fixture.Bytes);
        using var archive = DatArchive.Open(stream, SyntheticOptions(), leaveOpen: true);
        stream.ResetReadRanges();
        action(archive, stream);
    }

    private static DatReaderOptions SyntheticOptions() => new()
    {
        MaxArchiveBytes = 128 * 1024,
        MaxIndexBytes = 16 * 1024,
        MaxEntryCount = 16,
        MaxNameBytes = 256,
        MaxTotalNameBytes = 2 * 1024,
        MaxStoredEntryBytes = MaxDecodedSectorBytes,
        MaxDecodedEntryBytes = MaxDecodedSectorBytes,
    };

    private static void AssertPayloadReadPattern(DatArchive archive, TrackingReadStream stream, params int[] expectedReadOrdinals)
    {
        foreach (var entry in archive.Entries)
        {
            var actual = stream.PayloadBytesRead(entry);
            var expected = expectedReadOrdinals.Contains(entry.Ordinal) ? entry.StoredSize : 0;
            if (actual != expected) throw new InvalidOperationException("The synthetic archive path read an unexpected payload range.");
        }
    }

    private static SyntheticDatFixture CreateSyntheticDat(params SyntheticDatEntry[] entries)
    {
        using var archive = new MemoryStream();
        var stored = new List<byte[]>(entries.Length);
        var offsets = new List<uint>(entries.Length);
        foreach (var entry in entries)
        {
            var payload = entry.Compressed ? Compress(entry.Payload) : entry.Payload;
            offsets.Add(checked((uint)archive.Position));
            stored.Add(payload);
            archive.Write(payload);
        }

        using var index = new MemoryStream();
        WriteU32(index, checked((uint)entries.Length));
        for (var indexEntry = 0; indexEntry < entries.Length; indexEntry++)
        {
            var entry = entries[indexEntry];
            WriteU32(index, checked((uint)entry.RawName.Length));
            index.Write(entry.RawName);
            index.Write(new byte[4]);
            WriteU32(index, entry.Compressed ? 2u : 1u);
            WriteU32(index, checked((uint)entry.Payload.Length));
            WriteU32(index, checked((uint)stored[indexEntry].Length));
            WriteU32(index, offsets[indexEntry]);
        }
        index.Position = 0;
        index.CopyTo(archive);
        archive.Write(new byte[16]);
        archive.Write("1TAD"u8);
        WriteU32(archive, checked((uint)entries.Sum(entry => entry.RawName.Length)));
        WriteU32(archive, checked((uint)(index.Length + 28)));
        return new SyntheticDatFixture(archive.ToArray());
    }

    private static byte[] NameBytes(string value) => System.Text.Encoding.ASCII.GetBytes(value + "\0");

    private static byte[] Compress(byte[] input)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(input);
        return output.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record SyntheticDatEntry(byte[] RawName, byte[] Payload, bool Compressed = false);
    private sealed record SyntheticDatFixture(byte[] Bytes);

    private sealed class TrackingReadStream : MemoryStream
    {
        private readonly List<(long Start, int Count)> _readRanges = [];

        public TrackingReadStream(byte[] bytes) : base(bytes, writable: false) { }

        public override int Read(Span<byte> buffer)
        {
            var start = Position;
            var count = base.Read(buffer);
            if (count != 0) _readRanges.Add((start, count));
            return count;
        }

        public void ResetReadRanges() => _readRanges.Clear();

        public long PayloadBytesRead(DatEntry entry)
        {
            var entryStart = (long)entry.Offset;
            var entryEnd = checked(entryStart + entry.StoredSize);
            return _readRanges.Sum(range => Math.Max(0, Math.Min(checked(range.Start + range.Count), entryEnd) - Math.Max(range.Start, entryStart)));
        }
    }

    private sealed record SectorSlice(int LightCount, int DistinctTileArtIds, int InvalidTileArtIds, int NonTileArtIds, int TrailingInt32, int[] ArtTypeCounts)
    {
        public static SectorSlice Parse(ReadOnlySpan<byte> bytes, bool rejectNonTileArtIds)
        {
            if (bytes.Length < sizeof(int)) throw new InvalidDataException("Sector light count is truncated.");
            var lightCount = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            if (lightCount < 0 || lightCount > MaxLights) throw new InvalidDataException("Sector light count is outside the supported bound.");
            var tileOffset = checked(sizeof(int) + (lightCount * SerializedLightBytes));
            var tileBytes = checked(SectorTileCount * sizeof(uint));
            if (tileOffset > bytes.Length || tileBytes > bytes.Length - tileOffset) throw new InvalidDataException("Sector tile table is truncated.");
            var distinct = new HashSet<uint>(); var types = new int[16]; var invalid = 0; var nonTile = 0;
            for (var index = 0; index < SectorTileCount; index++)
            {
                var artId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(tileOffset + (index * sizeof(uint)), sizeof(uint)));
                distinct.Add(artId); types[artId >> 28]++;
                if (artId == uint.MaxValue) invalid++; else if ((artId >> 28) != 0) nonTile++;
            }
            if (rejectNonTileArtIds && nonTile != 0) throw new InvalidDataException("Sector tile table contains a non-tile ART identifier.");
            var terminalOffset = checked(tileOffset + tileBytes);
            if (bytes.Length - terminalOffset < TerminalObjectCountBytes) throw new InvalidDataException("Sector terminal object count is truncated after the tile table.");
            var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[^TerminalObjectCountBytes..]);
            if (count < 0 || count > SectorTileCount) throw new InvalidDataException("Sector trailing object count is outside the supported bound.");
            return new SectorSlice(lightCount, distinct.Count, invalid, nonTile, count, types);
        }
    }

    private sealed record SectorFraming(int TileStart, int TileBytes, int TileEnd, int RoofStart, int RoofBytes, int RoofEnd, int RoofEmpty, int PlaceholderStart, int PlaceholderEnd, int Placeholder, int TileScriptsStart, int TileScriptsBytes, int TileScriptsEnd, int TileScriptCount, int SectorScriptStart, int SectorScriptBytes, int SectorScriptEnd, int MetadataAndSoundStart, int MetadataAndSoundBytes, int MetadataAndSoundEnd, int BlockMaskStart, int BlockMaskBytes, int BlockMaskEnd, int ObjectStart, int ObjectBytes, int ObjectEnd, int FirstObjectVersion, int TrailingInt32Start, int TrailingInt32End)
    {
        public static SectorFraming Parse(ReadOnlySpan<byte> bytes, int lightCount)
        {
            var tileStart = checked(sizeof(int) + (lightCount * SerializedLightBytes));
            var tileBytes = checked(SectorTileCount * sizeof(uint));
            var tileEnd = checked(tileStart + tileBytes);
            var trailingStart = checked(bytes.Length - TerminalObjectCountBytes);
            if (trailingStart < tileEnd) throw new InvalidDataException("Sector framing overlaps the fixed tile table and trailing word.");
            var cursor = new Cursor(bytes.Slice(tileEnd, trailingStart - tileEnd));
            var roofStart = tileEnd;
            var roofEmpty = cursor.I32();
            if (roofEmpty is not 0 and not 1) throw new InvalidDataException("Roof empty discriminator is outside the bounded CE-derived form.");
            if (roofEmpty == 0) cursor.Skip(256 * sizeof(uint));
            var roofEnd = checked(tileEnd + (trailingStart - tileEnd - cursor.Remaining));
            var placeholderStart = roofEnd;
            var placeholder = cursor.I32();
            if (placeholder is < 0xAA0000 or > 0xAA0004) throw new InvalidDataException("Sector placeholder is outside the CE-derived discriminator range.");
            var placeholderEnd = checked(placeholderStart + sizeof(int));
            var tileScriptsStart = placeholderEnd;
            var tileScriptCount = 0;
            if (placeholder != 0xAA0000)
            {
                tileScriptCount = cursor.I32();
                if (tileScriptCount is < 0 or > SectorTileCount) throw new InvalidDataException("Tile-script count is outside the bounded sector range.");
                cursor.Skip(checked(tileScriptCount * 24));
            }
            var tileScriptsEnd = checked(tileEnd + (trailingStart - tileEnd - cursor.Remaining));
            var sectorScriptStart = tileScriptsEnd;
            if (placeholder >= 0xAA0002) cursor.Skip(12);
            var sectorScriptEnd = checked(tileEnd + (trailingStart - tileEnd - cursor.Remaining));
            var metadataAndSoundStart = sectorScriptEnd;
            if (placeholder >= 0xAA0003) cursor.Skip(24);
            var metadataAndSoundEnd = checked(tileEnd + (trailingStart - tileEnd - cursor.Remaining));
            var blockMaskStart = metadataAndSoundEnd;
            if (placeholder >= 0xAA0004) cursor.Skip(128 * sizeof(uint));
            var blockMaskEnd = checked(tileEnd + (trailingStart - tileEnd - cursor.Remaining));
            var objectStart = blockMaskEnd;
            var objectBytes = cursor.Remaining;
            var firstObjectVersion = objectBytes >= sizeof(int) ? BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(objectStart, sizeof(int))) : 0;
            return new SectorFraming(tileStart, tileBytes, tileEnd, roofStart, roofEnd - roofStart, roofEnd, roofEmpty, placeholderStart, placeholderEnd, placeholder, tileScriptsStart, tileScriptsEnd - tileScriptsStart, tileScriptsEnd, tileScriptCount, sectorScriptStart, sectorScriptEnd - sectorScriptStart, sectorScriptEnd, metadataAndSoundStart, metadataAndSoundEnd - metadataAndSoundStart, metadataAndSoundEnd, blockMaskStart, blockMaskEnd - blockMaskStart, blockMaskEnd, objectStart, objectBytes, trailingStart, firstObjectVersion, trailingStart, bytes.Length);
        }
    }

    private enum WireKind { I32, OptI64, ArrI32, ArrI64, ArrU32, ArrU64, ArrScript, ArrQuest, OptString, OptObjectId, ArrObjectId }

    private readonly record struct ObjectId(short Type, byte[] Payload)
    {
        public static ObjectId A(int value) { var payload = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(payload, value); return new ObjectId(1, payload); }
        public static ObjectId Null() => new(0, new byte[16]);
        public static ObjectId Guid(byte seed) { var payload = new byte[16]; payload[0] = seed; return new ObjectId(2, payload); }
        public static ObjectId P(long location, int temporaryId, int mapId) { var payload = new byte[16]; BinaryPrimitives.WriteInt64LittleEndian(payload, location); BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), temporaryId); BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), mapId); return new ObjectId(3, payload); }
        public int AidValue()
        {
            if (Type != 1) throw new InvalidDataException("A non-A ObjectID cannot be emitted as a prototype identifier.");
            return BinaryPrimitives.ReadInt32LittleEndian(Payload);
        }
        public bool Matches(ObjectId other)
        {
            if (Type != other.Type) return false;
            return Type switch { 0 => true, 1 => Payload.AsSpan(0, 4).SequenceEqual(other.Payload.AsSpan(0, 4)), 2 => Payload.AsSpan().SequenceEqual(other.Payload), 3 => Payload.AsSpan().SequenceEqual(other.Payload), _ => false };
        }
    }

    private sealed record InstanceInfo(int SourceIndex, ObjectId PrototypeId, int Type, int? DirectCurrentAid, long? Location, int? DirectOffsetX, int? DirectOffsetY, uint? DirectFlags)
    {
        public bool ScriptsOverridden { get; init; }
        public bool CurrentAidOverridden => DirectCurrentAid.HasValue;
        public ObjectId OwnId { get; init; }
        public IReadOnlyDictionary<int, int> NumericFields { get; init; } = new Dictionary<int, int>();
        public IReadOnlyList<ArraySpan> Arrays { get; init; } = [];
        public IReadOnlyList<int> OverlayOverrides { get; init; } = [];
    }
    private sealed record ArraySpan(int Field, int Start, int End, int Size, int Count, int WordStart, int Words);
    private sealed record PrototypeInfo(ObjectId Id, int Type, int CurrentAid, uint Flags)
    {
        public IReadOnlyList<ArraySpan> Arrays { get; init; } = [];
        public IReadOnlyList<ScriptAttachment> Scripts { get; init; } = [];
        public IReadOnlyDictionary<int, int> NumericFields { get; init; } = new Dictionary<int, int>();
    }
    private sealed record ResolvedInstance(int SourceIndex, InstanceInfo Instance, int CurrentAid, uint EffectiveFlags, bool CurrentAidInherited);
    private sealed record PrototypeFingerprint(int Aid, InputFingerprint Fingerprint);
    private sealed record PrototypeInput(string Path, PrototypeFingerprint Fingerprint);
    private sealed record PrototypeReadResult(List<PrototypeInfo> Prototypes, List<PrototypeInput> Inputs, string FingerprintAggregate);
    private sealed record ObjectArtExample(ResolvedInstance Object, int ArtType, int SceneryCategory, int ArtNumber, int RotationGroup, int Frame, int Palette, int Width, int Height, int HotX, int HotY, int ArtOffsetX, int ArtOffsetY);
    private readonly record struct DiagnosticPlacement(long AnchorX, long AnchorY, long TopLeftX, long TopLeftY)
    {
        public static DiagnosticPlacement For(long localX, long localY, int offsetX, int offsetY, int hotX, int hotY)
        {
            var anchorX = checked((40 * checked(localY - localX)) + offsetX);
            var anchorY = checked((20 * checked(localY + localX)) + offsetY);
            return new DiagnosticPlacement(anchorX, anchorY, checked(anchorX - hotX), checked(anchorY - hotY));
        }
    }

    private static class ObjectArt
    {
        private const string SceneryMesPath = "art\\scenery\\scenery.mes";

        public static List<ObjectArtExample> ResolveExamples(DatArchive resource, IReadOnlyList<ResolvedInstance> examples)
        {
            var mesEntry = ReadUnique(resource, SceneryMesPath, "scenery mapping");
            if (mesEntry.DeclaredOutputSize > MaxDecodedSectorBytes) throw new InvalidDataException("The scenery mapping exceeds the 1 MiB read cap.");
            var names = ParseMes(resource.ReadEntry(mesEntry));
            var resolved = new List<ObjectArtExample>(examples.Count);
            foreach (var example in examples)
            {
                var current = unchecked((uint)example.CurrentAid);
                var artType = (int)(current >> 28);
                if (artType != 4) throw new InvalidDataException("A selected static scenery record has a non-scenery current ART type.");
                var artNumber = (int)((current >> 19) & 0x1ff);
                var frame = (int)((current >> 14) & 0x1f);
                var rotation = (int)((current >> 11) & 0x7);
                var category = (int)((current >> 6) & 0x1f);
                var palette = (int)((current >> 4) & 0x3);
                var key = checked((category * 1000) + artNumber);
                if (!names.TryGetValue(key, out var filename) || !IsSafeRelativeArtFilename(filename)) throw new InvalidDataException("A selected scenery ART mapping is missing or unsafe.");
                var artEntry = ReadUnique(resource, SceneryMesPath[..12] + filename, "selected scenery ART");
                if (artEntry.DeclaredOutputSize > MaxDecodedSectorBytes) throw new InvalidDataException("A selected scenery ART entry exceeds the 1 MiB read cap.");
                var metadata = ArtDecoder.Inspect(resource.ReadEntry(artEntry), new ArtDecoderOptions { MaxInputBytes = MaxDecodedSectorBytes });
                if (rotation >= metadata.Header.PhysicalGroupCount || frame >= metadata.Header.FramesPerGroup) throw new InvalidDataException("A selected scenery ART selector is outside its physical frame table.");
                var selected = metadata.GetFrame(rotation, frame);
                resolved.Add(new ObjectArtExample(example, artType, category, artNumber, rotation, frame, palette, selected.Width, selected.Height, selected.HotX, selected.HotY, selected.OffsetX, selected.OffsetY));
            }
            return resolved;
        }

        private static DatEntry ReadUnique(DatArchive archive, string path, string label)
        {
            var normalized = NormalizePath(System.Text.Encoding.ASCII.GetBytes(path + "\0"));
            var matches = archive.Entries.Where(entry => NormalizePath(entry.RawNameBytes) == normalized).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"The {label} did not resolve exactly once in the explicit resource archive.");
            return matches[0];
        }

        public static SortedDictionary<int, string> ParseMes(byte[] bytes)
        {
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            var result = new SortedDictionary<int, string>();
            for (var cursor = 0; cursor < text.Length;)
            {
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                if (cursor == text.Length || text[cursor] == '\0') break;
                if (text[cursor] == ';' || (cursor + 1 < text.Length && text[cursor] == '/' && text[cursor + 1] == '/'))
                {
                    var lineEnd = text.IndexOf('\n', cursor + 2);
                    cursor = lineEnd < 0 ? text.Length : lineEnd + 1;
                    continue;
                }
                if (text[cursor] != '{')
                {
                    var next = text.IndexOf('{', cursor + 1);
                    if (next < 0) break;
                    cursor = next;
                }
                var keyEnd = text.IndexOf('}', cursor + 1);
                if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), out var key)) throw new InvalidDataException("The scenery mapping has an invalid key.");
                cursor = keyEnd + 1;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                if (cursor == text.Length || text[cursor] != '{') throw new InvalidDataException("The scenery mapping has a key without a braced value.");
                var valueEnd = text.IndexOf('}', cursor + 1);
                if (valueEnd < 0 || !result.TryAdd(key, text[(cursor + 1)..valueEnd])) throw new InvalidDataException("The scenery mapping is malformed or has duplicate keys.");
                cursor = valueEnd + 1;
            }
            if (result.Count == 0) throw new InvalidDataException("The scenery mapping has no entries.");
            return result;
        }

        public static bool IsSafeRelativeArtFilename(string value) => value.Length > 4 && value.EndsWith(".art", StringComparison.OrdinalIgnoreCase) && value.All(character => character is >= ' ' and <= '~' && character is not '/' and not '\\');

        private static string NormalizePath(IReadOnlyList<byte> raw)
        {
            if (raw.Count == 0 || raw[^1] != 0) throw new InvalidDataException("A resource pathname lacks its terminal NUL.");
            var normalized = new byte[raw.Count - 1];
            for (var index = 0; index < normalized.Length; index++)
            {
                var value = raw[index];
                if (value == (byte)'\\') value = (byte)'/';
                if (value is >= (byte)'A' and <= (byte)'Z') value = checked((byte)(value + ((byte)'a' - (byte)'A')));
                normalized[index] = value;
            }
            return System.Text.Encoding.ASCII.GetString(normalized);
        }
    }

    private static class ObjectWire
    {
        private static readonly WireKind[] Common = K("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64");
        private static readonly WireKind[] Wall = K("i*3,au32,au64");
        private static readonly WireKind[] Portal = K("i*6,au32,au64");
        private static readonly WireKind[] Container = K("i*4,aoid,i*4,au32,au64");
        private static readonly WireKind[] Scenery = K("i,poid,i*2,au32,au64");
        private static readonly WireKind[] Projectile = K("i*3,poid,i*2,au32,au64");
        private static readonly WireKind[] Item = K("i,poid,i*19,au32,au64");
        private static readonly WireKind[] Weapon = K("i*4,ai32*3,i*18,au32,au64");
        private static readonly WireKind[] Ammo = K("i*5,au32,au64");
        private static readonly WireKind[] Armor = K("i*4,ai32*2,i*4,au32,au64");
        private static readonly WireKind[] Gold = K("i*4,au32,au64");
        private static readonly WireKind[] ShortItem = K("i*3,au32,au64");
        private static readonly WireKind[] KeyRing = K("i,au32,i*2,au32,au64");
        private static readonly WireKind[] Written = K("i*6,au32,au64");
        private static readonly WireKind[] Critter = K("i*2,ai32*3,au32,i*4,au32*2,poid,i,poid*5,i,aoid,i*2,poid,p64,i*6,au32,au64");
        private static readonly WireKind[] Pc = K("i*2,au32,au64,i*2,aquest,au32,au64,au32,au64,i,au64,au32*3,i,s,i,au32*2,i*2,au32,au64");
        private static readonly WireKind[] Npc = K("i,poid,i,poid*2,i*2,au64,i,p64*2,i*3,poid,i*2,aoid,au32*2,i*3,au32,aoid");

        public static List<InstanceInfo> ParseWindow(ReadOnlySpan<byte> bytes, int expectedCount)
        {
            var cursor = new Cursor(bytes); var records = new List<InstanceInfo>(expectedCount);
            for (var index = 0; index < expectedCount; index++) records.Add(ParseInstance(ref cursor) with { SourceIndex = index });
            if (!cursor.AtEnd) throw new InvalidDataException("Object window has trailing bytes after the required record count.");
            return records;
        }

        public static InstanceInfo ParseNext(ReadOnlySpan<byte> bytes, out int consumed)
        {
            var cursor = new Cursor(bytes[..Math.Min(bytes.Length, MaxDecodedSectorBytes)]);
            var result = ParseInstance(ref cursor);
            consumed = cursor.Offset;
            if (consumed < 70) throw new InvalidDataException("Record failed minimum envelope progress.");
            return result;
        }

        public static List<PrototypeInfo> ReadReferencedPrototypes(DatArchive archive, ObjectId[] referenced)
        {
            var wanted = new HashSet<int>();
            foreach (var referencedId in referenced)
            {
                if (referencedId.Type != 1)
                {
                    throw new InvalidDataException("A referenced prototype ObjectID is not a locatable A form.");
                }

                var aid = BinaryPrimitives.ReadInt32LittleEndian(referencedId.Payload);
                if (aid is < 0 or > 999999)
                {
                    throw new InvalidDataException("A referenced prototype A ID is outside the locatable six-digit range.");
                }
                wanted.Add(aid);
            }
            if (wanted.Count == 0) return [];

            var candidates = wanted.ToDictionary(aid => aid, _ => new List<DatEntry>());
            foreach (var entry in archive.Entries)
            {
                if (TryParseCanonicalPrototypeAid(entry.RawNameBytes.ToArray(), out var aid) && candidates.TryGetValue(aid, out var matches))
                {
                    matches.Add(entry);
                }
            }

            foreach (var candidate in candidates)
            {
                if (candidate.Value.Count != 1)
                {
                    throw new InvalidDataException($"A referenced prototype is missing or duplicated in the immutable module (A ID {candidate.Key}, matches {candidate.Value.Count}).");
                }
            }

            var found = new List<PrototypeInfo>(wanted.Count);
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Key))
            {
                var entry = candidate.Value[0];
                if (entry.DeclaredOutputSize > MaxDecodedSectorBytes) throw new InvalidDataException("A candidate prototype exceeds the 1 MiB read cap.");
                var prototype = ParsePrototype(archive.ReadEntry(entry));
                if (prototype.Id.Type != 1 || BinaryPrimitives.ReadInt32LittleEndian(prototype.Id.Payload) != candidate.Key)
                {
                    throw new InvalidDataException("A selected prototype candidate does not match its canonical requested A ID.");
                }
                found.Add(prototype);
            }
            return found;
        }

        public static PrototypeReadResult ReadReferencedPrototypesFromDirectory(string root, ObjectId[] referenced, IReadOnlyList<PrototypeFingerprint>? expectedManifest = null)
        {
            if (!Directory.Exists(root)) throw new InvalidDataException("The explicit prototype root does not exist.");
            var wanted = ReferencedPrototypeAids(referenced);
            if (expectedManifest is not null && expectedManifest.Count != wanted.Count)
            {
                throw new InvalidDataException("The referenced prototype set does not match the fixed bounded profile.");
            }
            var found = new List<PrototypeInfo>(wanted.Count);
            var inputs = new List<PrototypeInput>(wanted.Count);
            foreach (var aid in wanted.Order())
            {
                var candidates = Directory.EnumerateFiles(root, $"{aid:D6} - *.pro", SearchOption.TopDirectoryOnly)
                    .Where(candidate => TryParseCanonicalPrototypeAid(System.Text.Encoding.ASCII.GetBytes("proto\\" + Path.GetFileName(candidate) + "\0"), out var parsed) && parsed == aid)
                    .ToArray();
                if (candidates.Length != 1) throw new InvalidDataException("A referenced prototype is missing or ambiguous in the explicit prototype root.");
                var before = InputFingerprint.Create(candidates[0]);
                if (before.Bytes > MaxDecodedSectorBytes) throw new InvalidDataException("A candidate prototype exceeds the 1 MiB read cap.");
                var expected = expectedManifest?.SingleOrDefault(item => item.Aid == aid);
                if (expectedManifest is not null && (expected is null || before != expected.Fingerprint))
                {
                    throw new InvalidDataException("A referenced prototype fingerprint does not match the fixed bounded profile.");
                }
                var prototype = ParsePrototype(File.ReadAllBytes(candidates[0]));
                var after = InputFingerprint.Create(candidates[0]);
                if (after != before) throw new InvalidDataException("A prototype changed during this read-only probe.");
                if (prototype.Id.Type != 1 || prototype.Id.AidValue() != aid) throw new InvalidDataException("A selected prototype candidate does not match its requested A ID.");
                found.Add(prototype);
                inputs.Add(new PrototypeInput(candidates[0], new PrototypeFingerprint(aid, before)));
            }
            var fingerprints = inputs.Select(input => input.Fingerprint).OrderBy(item => item.Aid).ToArray();
            if (expectedManifest is not null && !fingerprints.SequenceEqual(expectedManifest.OrderBy(item => item.Aid)))
            {
                throw new InvalidDataException("The referenced prototype manifest does not match the fixed bounded profile.");
            }
            var evidence = System.Text.Encoding.ASCII.GetBytes(string.Join("|", fingerprints.Select(item => $"{item.Aid:D6}:{item.Fingerprint.Bytes}:{item.Fingerprint.Sha256}")));
            return new PrototypeReadResult(found, inputs, Convert.ToHexString(SHA256.HashData(evidence)).ToLowerInvariant());
        }

        public static void VerifyExpectedPrototypeManifest(PrototypeReadResult result, IReadOnlyList<PrototypeFingerprint> expectedManifest, bool requireUnchanged)
        {
            var actual = result.Inputs.Select(input => input.Fingerprint).OrderBy(item => item.Aid).ToArray();
            if (!actual.SequenceEqual(expectedManifest.OrderBy(item => item.Aid)))
            {
                throw new InvalidDataException("The referenced prototype manifest does not match the fixed bounded profile.");
            }
            if (requireUnchanged && result.Inputs.Any(input => InputFingerprint.Create(input.Path) != input.Fingerprint.Fingerprint))
            {
                throw new InvalidDataException("A referenced prototype changed during this read-only probe.");
            }
        }

        private static HashSet<int> ReferencedPrototypeAids(ObjectId[] referenced)
        {
            var wanted = new HashSet<int>();
            foreach (var referencedId in referenced)
            {
                if (referencedId.Type != 1) throw new InvalidDataException("A referenced prototype ObjectID is not a locatable A form.");
                var aid = referencedId.AidValue();
                if (aid is < 0 or > 999999) throw new InvalidDataException("A referenced prototype A ID is outside the locatable six-digit range.");
                wanted.Add(aid);
            }
            return wanted;
        }

        public static List<int> ResolveCurrentAids(IEnumerable<InstanceInfo> instances, IReadOnlyList<PrototypeInfo> prototypes)
        {
            return ResolveEffectiveInstances(instances.ToList(), prototypes).Select(instance => instance.CurrentAid).ToList();
        }

        public static List<ResolvedInstance> ResolveEffectiveInstances(IReadOnlyList<InstanceInfo> instances, IReadOnlyList<PrototypeInfo> prototypes)
        {
            var resolved = new List<ResolvedInstance>(instances.Count);
            for (var index = 0; index < instances.Count; index++)
            {
                var instance = instances[index];
                PrototypeInfo? prototype = null;
                if (!instance.DirectCurrentAid.HasValue || !instance.DirectFlags.HasValue)
                {
                    var matches = prototypes.Where(candidate => candidate.Id.Matches(instance.PrototypeId)).ToArray();
                    if (matches.Length != 1 || matches[0].Type != instance.Type)
                    {
                        var field = instance.DirectCurrentAid.HasValue ? "object field" : "current ART ID";
                        throw new InvalidDataException($"An inherited {field} did not resolve to one matching validated prototype.");
                    }
                    prototype = matches[0];
                }
                if (instance.DirectCurrentAid is int direct)
                {
                    resolved.Add(new ResolvedInstance(instance.SourceIndex, instance, direct, instance.DirectFlags ?? prototype!.Flags, false));
                }
                else
                {
                    resolved.Add(new ResolvedInstance(instance.SourceIndex, instance, prototype!.CurrentAid, instance.DirectFlags ?? prototype.Flags, true));
                }
            }
            return resolved;
        }

        public static PrototypeInfo ParsePrototype(ReadOnlySpan<byte> bytes)
        {
            var cursor = new Cursor(bytes);
            RequireVersion(ref cursor);
            var marker = ReadObjectId(ref cursor, allowBlocked: true);
            if (marker.Type != -1) throw new InvalidDataException("Prototype record is missing its blocked marker.");
            var id = ReadObjectId(ref cursor, allowBlocked: false);
            var type = ReadType(ref cursor);
            ReadMasks(ref cursor, type, prototype: true, out _);
            var fields = FieldsFor(type);
            var currentAid = cursor.I32();
            var numeric = new Dictionary<int, int> { [0] = currentAid };
            uint flags = 0;
            var arrays = new List<ArraySpan>();
            for (var field = 1; field < fields.Count; field++)
            {
                if (fields[field] == WireKind.I32)
                {
                    var value = cursor.I32(); numeric[field] = value;
                    if (field == ObjectFlagsField) flags = unchecked((uint)value);
                }
                else Skip(ref cursor, fields[field], requireNul: true, arrays, field);
            }
            if (!cursor.AtEnd) throw new InvalidDataException("Prototype entry contains trailing bytes.");
            return new PrototypeInfo(id, type, currentAid, flags) { NumericFields = numeric, Scripts = DecodeScripts(bytes, arrays), Arrays = arrays };
        }

        private static InstanceInfo ParseInstance(ref Cursor cursor)
        {
            RequireVersion(ref cursor);
            var prototype = ReadObjectId(ref cursor, allowBlocked: false);
            if (prototype.Type is -1 or -2) throw new InvalidDataException("Sector instance has a prototype/runtime discriminator.");
            var ownId = ReadObjectId(ref cursor, allowBlocked: false);
            var type = ReadType(ref cursor);
            var fields = FieldsFor(type);
            var numFields = cursor.I16();
            if (numFields < 0) throw new InvalidDataException("Instance has a negative field count.");
            var set = ReadMasks(ref cursor, type, prototype: false, out var popcount);
            if (numFields != popcount || numFields > 94) throw new InvalidDataException("Instance field count does not match its valid override mask.");
            int? currentAid = null;
            long? location = null;
            int? offsetX = null;
            int? offsetY = null;
            uint? flags = null;
            var numeric = new Dictionary<int, int>();
            var arrays = new List<ArraySpan>();
            for (var field = 0; field < fields.Count; field++)
            {
                if (!set[field]) continue;
                if (fields[field] == WireKind.I32)
                {
                    var value = cursor.I32(); numeric[field] = value;
                    if (field == CurrentArtField) currentAid = value;
                    else if (field == OffsetXField) offsetX = value;
                    else if (field == OffsetYField) offsetY = value;
                    else if (field == ObjectFlagsField) flags = unchecked((uint)value);
                    continue;
                }
                if (field == LocationField) location = cursor.Presence() ? cursor.I64() : null;
                else
                {
                    try { Skip(ref cursor, fields[field], requireNul: true, arrays, field); }
                    catch (InvalidDataException error) { throw new InvalidDataException($"field={field} offset={cursor.Offset} {error.Message}"); }
                }
            }
            return new InstanceInfo(-1, prototype, type, currentAid, location, offsetX, offsetY, flags) { OwnId = ownId, NumericFields = numeric, Arrays = arrays, ScriptsOverridden = set[31], OverlayOverrides = Array.AsReadOnly(Enumerable.Range(15, 3).Where(i => set[i]).ToArray()) };
        }

        private static bool[] ReadMasks(ref Cursor cursor, int type, bool prototype, out int popcount)
        {
            var fields = FieldsFor(type); var words = MaskWords(type); var set = new bool[fields.Count]; popcount = 0;
            for (var word = 0; word < words; word++)
            {
                var mask = cursor.U32();
                var start = MaskStart(type, word);
                var valid = MaskLength(type, word);
                var allowed = valid == 32 ? uint.MaxValue : (1u << valid) - 1u;
                if ((mask & ~allowed) != 0) throw new InvalidDataException("Object override mask has a reserved bit set.");
                if (prototype) continue;
                for (var bit = 0; bit < valid; bit++) if ((mask & (1u << bit)) != 0) { set[start + bit] = true; popcount++; }
            }
            return set;
        }

        private static int MaskStart(int type, int word)
        {
            if (word == 0) return 0;
            if (word == 1) return 32;
            if (word == 2) return Common.Length;
            if (type is 15 or 16 && word == 3) return Common.Length + 32;
            if (UsesSecondGroup(type) && word == 3) return Common.Length + FirstTypeGroup(type).Length;
            if (type is 15 or 16 && word == 4) return Common.Length + FirstTypeGroup(type).Length;
            throw new InvalidDataException("Unexpected object mask word.");
        }
        private static int MaskLength(int type, int word)
        {
            if (word == 0) return 32;
            if (word == 1) return Common.Length - 32;
            if (word == 2) return Math.Min(32, FirstTypeGroup(type).Length);
            if (type is 15 or 16 && word == 3) return FirstTypeGroup(type).Length - 32;
            if (UsesSecondGroup(type) && word == 3) return SecondTypeGroup(type).Length;
            if (type is 15 or 16 && word == 4) return SecondTypeGroup(type).Length;
            throw new InvalidDataException("Unexpected object mask word.");
        }
        private static int MaskWords(int type) => type is >= 5 and <= 14 ? 4 : type is 15 or 16 ? 5 : 3;
        private static bool UsesSecondGroup(int type) => type is >= 5 and <= 16;
        private static IReadOnlyList<WireKind> FieldsFor(int type) => [.. Common, .. FirstTypeGroup(type), .. SecondTypeGroup(type)];
        private static WireKind[] FirstTypeGroup(int type) => type switch { 0 => Wall, 1 => Portal, 2 => Container, 3 => Scenery, 4 => Projectile, >= 5 and <= 14 => Item, 15 or 16 => Critter, 17 => ShortItem, _ => throw new InvalidDataException("Unsupported object type.") };
        private static WireKind[] SecondTypeGroup(int type) => type switch { 5 => Weapon, 6 => Ammo, 7 => Armor, 8 => Gold, 9 or 10 or 11 or 14 => ShortItem, 12 => KeyRing, 13 => Written, 15 => Pc, 16 => Npc, _ => [] };
        private static int ReadType(ref Cursor cursor) { var type = cursor.I32(); if (type is < 0 or > 17) throw new InvalidDataException("Object type is outside the supported concrete range."); return type; }
        private static void RequireVersion(ref Cursor cursor) { var version = cursor.I32(); if (version != ExpectedObjectVersion) throw new InvalidDataException($"Object version {version} is unsupported."); }

        public static bool TryParseCanonicalPrototypeAid(ReadOnlySpan<byte> rawName, out int aid)
        {
            aid = 0;
            const int prefixLength = 5;
            const int digitsStart = 6;
            const int labelStart = 15;
            if (rawName.Length < 21 || rawName[^1] != 0 ||
                !AsciiLetterEquals(rawName[0], (byte)'p') || !AsciiLetterEquals(rawName[1], (byte)'r') || !AsciiLetterEquals(rawName[2], (byte)'o') || !AsciiLetterEquals(rawName[3], (byte)'t') || !AsciiLetterEquals(rawName[4], (byte)'o') ||
                rawName[prefixLength] != (byte)'\\' || rawName[12] != (byte)' ' || rawName[13] != (byte)'-' || rawName[14] != (byte)' ' ||
                rawName[^5] != (byte)'.' || !AsciiLetterEquals(rawName[^4], (byte)'p') || !AsciiLetterEquals(rawName[^3], (byte)'r') || !AsciiLetterEquals(rawName[^2], (byte)'o'))
            {
                return false;
            }

            var labelEnd = rawName.Length - 5;
            if (labelEnd <= labelStart) return false;
            var parsed = 0;
            for (var index = 0; index < 6; index++)
            {
                var digit = rawName[digitsStart + index];
                if (digit is < (byte)'0' or > (byte)'9') return false;
                parsed = checked((parsed * 10) + (digit - (byte)'0'));
            }
            for (var index = labelStart; index < labelEnd; index++)
            {
                if (rawName[index] is 0 or (byte)'/' or (byte)'\\') return false;
            }
            aid = parsed;
            return true;
        }

        private static bool AsciiLetterEquals(byte actual, byte expected)
        {
            if (actual is >= (byte)'A' and <= (byte)'Z') actual = checked((byte)(actual + ((byte)'a' - (byte)'A')));
            return actual == expected;
        }

        private static ObjectId ReadObjectId(ref Cursor cursor, bool allowBlocked)
        {
            var type = cursor.I16(); cursor.Skip(6); var payload = cursor.Bytes(16).ToArray();
            if (type == -1 && allowBlocked) return new ObjectId(type, payload);
            if (type is < 0 or > 3) throw new InvalidDataException($"Object ID form {type} is unsupported.");
            return new ObjectId(type, payload);
        }

        private static void Skip(ref Cursor cursor, WireKind kind, bool requireNul, List<ArraySpan>? arrays = null, int field = -1)
        {
            switch (kind)
            {
                case WireKind.I32: cursor.Skip(4); return;
                case WireKind.OptI64: if (cursor.Presence()) cursor.Skip(8); return;
                case WireKind.OptString:
                    if (!cursor.Presence()) return;
                    var stringCount = cursor.I32(); if (stringCount < 0 || stringCount > cursor.Remaining - 1 || stringCount > MaxDecodedSectorBytes) throw new InvalidDataException("String count is outside the bounded record.");
                    var value = cursor.Bytes(stringCount + 1); if (requireNul && value[^1] != 0) throw new InvalidDataException("String lacks its required terminator."); return;
                case WireKind.OptObjectId: if (cursor.Presence()) _ = ReadObjectId(ref cursor, false); return;
                default:
                    var start = cursor.Offset;
                    if (!cursor.Presence()) return;
                    var expectedSize = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new InvalidDataException("Unknown wire kind.") };
                    var size = cursor.I32(); var arrayCount = cursor.I32(); _ = cursor.I32();
                    if (size != expectedSize || arrayCount < 0 || arrayCount > MaxDecodedSectorBytes / expectedSize) throw new InvalidDataException("Array header violates the canonical bounded form.");
                    cursor.Skip(checked(size * arrayCount));
                    var wordStart = cursor.Offset;
                    // Pinned FILE reader rejects fread(size=0,count=1). An absent
                    // array is distinct from a present array with empty contents.
                    var words = cursor.I32(); if (words < 1 || words > MaxDecodedSectorBytes / 4) throw new InvalidDataException("Array bitset word count is outside the supported FILE bound.");
                    var bits = cursor.Bytes(checked(words * 4));
                    var actual = 0; for (var index = 0; index < words; index++) actual += BitOperations.PopCount(BinaryPrimitives.ReadUInt32LittleEndian(bits.Slice(index * 4, 4)));
                    if (actual != arrayCount) throw new InvalidDataException("Array bitset population does not match its element count.");
                    arrays?.Add(new ArraySpan(field, start, cursor.Offset, size, arrayCount, wordStart, words));
                    return;
            }
        }

        private static WireKind[] K(string specification)
        {
            var values = new List<WireKind>();
            foreach (var token in specification.Split(','))
            {
                var parts = token.Split('*'); var count = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
                var kind = parts[0] switch { "i" => WireKind.I32, "p64" => WireKind.OptI64, "ai32" => WireKind.ArrI32, "ai64" => WireKind.ArrI64, "au32" => WireKind.ArrU32, "au64" => WireKind.ArrU64, "ascript" => WireKind.ArrScript, "aquest" => WireKind.ArrQuest, "s" => WireKind.OptString, "poid" => WireKind.OptObjectId, "aoid" => WireKind.ArrObjectId, _ => throw new InvalidOperationException("Invalid compact wire descriptor.") };
                for (var index = 0; index < count; index++) values.Add(kind);
            }
            return values.ToArray();
        }

        public static byte[] CreateSyntheticInstance(int type, ObjectId prototype, bool currentAidOverridden, bool includeAllFields, bool invalidPresence = false, int currentAid = 0, int offsetX = 0, int offsetY = 0, uint flags = 0, uint spellFlags = 0, bool flagsOverridden = true)
        {
            var fields = FieldsFor(type); var masks = new uint[MaskWords(type)];
            for (var index = 0; index < fields.Count; index++) if (includeAllFields || (index == CurrentArtField && currentAidOverridden)) SetBit(type, masks, index);
            if (!currentAidOverridden) ClearBit(type, masks, CurrentArtField);
            if (!flagsOverridden) ClearBit(type, masks, ObjectFlagsField);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, prototype); AddObjectId(list, ObjectId.A(17)); AddI32(list, type); AddI16(list, checked((short)masks.Sum(mask => BitOperations.PopCount(mask)))); foreach (var mask in masks) AddU32(list, mask);
            for (var index = 0; index < fields.Count; index++) if (IsSet(type, masks, index)) AddWire(list, fields[index], invalidPresence && index == LocationField, index == CurrentArtField ? currentAid : index == OffsetXField ? offsetX : index == OffsetYField ? offsetY : index == ObjectFlagsField ? unchecked((int)flags) : index == SpellFlagsField ? unchecked((int)spellFlags) : 0);
            return list.ToArray();
        }

        public static byte[] CreateSyntheticPrototype(int type, ObjectId id, int currentAid, uint flags = 0, uint spellFlags = 0)
        {
            var fields = FieldsFor(type);
            var masks = new uint[MaskWords(type)];
            for (var index = 0; index < fields.Count; index++) SetBit(type, masks, index);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, new ObjectId(-1, new byte[16])); AddObjectId(list, id); AddI32(list, type); foreach (var mask in masks) AddU32(list, mask); for (var index = 0; index < fields.Count; index++) AddWire(list, fields[index], false, index == CurrentArtField ? currentAid : index == ObjectFlagsField ? unchecked((int)flags) : index == SpellFlagsField ? unchecked((int)spellFlags) : 0); return list.ToArray();
        }

        public static byte[] CreateUncheckedInstance(int encodedType, ObjectId prototype)
        {
            var bytes = CreateSyntheticInstance(3, prototype, true, false);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), encodedType);
            return bytes;
        }

        public static byte[] CreateSyntheticSingleField(int type, WireKind kind, out int payloadOffset)
        {
            var fields = FieldsFor(type);
            var field = Array.IndexOf(fields.ToArray(), kind);
            if (field < 0) throw new InvalidOperationException("The requested synthetic wire kind is absent from this type.");
            var masks = new uint[MaskWords(type)]; SetBit(type, masks, field);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, ObjectId.A(7001)); AddObjectId(list, ObjectId.A(17)); AddI32(list, type); AddI16(list, 1); foreach (var mask in masks) AddU32(list, mask);
            payloadOffset = list.Count; AddWire(list, kind, false, 0); return list.ToArray();
        }

        private static void AddWire(List<byte> list, WireKind kind, bool invalidPresence, int i32Value)
        {
            if (kind == WireKind.I32) { AddI32(list, i32Value); return; }
            if (kind == WireKind.OptI64) { list.Add(invalidPresence ? (byte)2 : (byte)1); list.AddRange(new byte[8]); return; }
            if (kind == WireKind.OptString) { list.Add(1); AddI32(list, 0); list.Add(0); return; }
            if (kind == WireKind.OptObjectId) { list.Add(1); AddObjectId(list, ObjectId.A(1)); return; }
            list.Add(1); var size = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new InvalidOperationException() }; AddI32(list, size); AddI32(list, 1); AddI32(list, 0); list.AddRange(new byte[size]); AddI32(list, 2); AddU32(list, 1); AddU32(list, 0);
        }

        private static void SetBit(int type, uint[] masks, int field) { var word = WordFor(type, field, out var bit); masks[word] |= 1u << bit; }
        private static void ClearBit(int type, uint[] masks, int field) { var word = WordFor(type, field, out var bit); masks[word] &= ~(1u << bit); }
        private static bool IsSet(int type, uint[] masks, int field) { var word = WordFor(type, field, out var bit); return (masks[word] & (1u << bit)) != 0; }
        private static int WordFor(int type, int field, out int bit)
        {
            for (var word = 0; word < MaskWords(type); word++)
            {
                var start = MaskStart(type, word);
                var length = MaskLength(type, word);
                if (field >= start && field < start + length)
                {
                    bit = field - start;
                    return word;
                }
            }
            throw new InvalidOperationException("Synthetic field has no valid mask position.");
        }
        private static void AddObjectId(List<byte> list, ObjectId id) { AddI16(list, id.Type); list.AddRange(new byte[6]); list.AddRange(id.Payload); }
        private static void AddI16(List<byte> list, short value) { var bytes = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(bytes, value); list.AddRange(bytes); }
        private static void AddI32(List<byte> list, int value) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); list.AddRange(bytes); }
        private static void AddU32(List<byte> list, uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); list.AddRange(bytes); }
    }

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _bytes; private int _offset;
        public Cursor(ReadOnlySpan<byte> bytes) { _bytes = bytes; _offset = 0; }
        public int Remaining => _bytes.Length - _offset; public bool AtEnd => _offset == _bytes.Length;
        public int Offset => _offset;
        public short I16() { var value = BinaryPrimitives.ReadInt16LittleEndian(Bytes(2)); return value; }
        public int I32() { var value = BinaryPrimitives.ReadInt32LittleEndian(Bytes(4)); return value; }
        public long I64() { var value = BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)); return value; }
        public uint U32() { var value = BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4)); return value; }
        public bool Presence() { var value = Bytes(1)[0]; if (value is not 0 and not 1) throw new InvalidDataException("Optional wire value has a non-canonical presence byte."); return value == 1; }
        public ReadOnlySpan<byte> Bytes(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("Object record is truncated."); var result = _bytes.Slice(_offset, count); _offset += count; return result; }
        public void Skip(int count) => _ = Bytes(count);
    }

    private sealed record InputFingerprint(long Bytes, string Sha256)
    {
        public static InputFingerprint Create(string path) { using var stream = File.OpenRead(path); return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()); }
    }
}
