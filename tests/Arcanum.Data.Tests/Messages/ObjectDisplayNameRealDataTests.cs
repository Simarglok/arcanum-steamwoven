using System.Security.Cryptography;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Messages;

/// <summary>Opt-in check of the installed original against docs/formats/mes-reader.md:
/// DAT_ROOT is the game folder, ARC_AREA_* the Crash Site exterior inputs. Only counts,
/// numbers and digests are compared; no original text is asserted or printed.</summary>
[TestClass]
public sealed class ObjectDisplayNameRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_description_tables_parse_whole_and_name_every_crash_site_item_and_npc()
    {
        var discovery = ContentMountPlan.Discover(Required("DAT_ROOT"));
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var loaded = ObjectDescriptionCatalog.Load(stack);
        Assert.AreEqual(DescriptionCatalogStatus.Ready, loaded.Status, $"{loaded.File} {loaded.Parse?.Status} line {loaded.Parse?.Line}");
        var catalog = loaded.Catalog!;

        // Winners, sizes and digests match the RES-023 resource table.
        var files = new (MesFile File, ContentCandidate Source, string Archive, long Bytes, string ShaPrefix, string ShaSuffix, int Count, int First, int Last)[]
        {
            (catalog.Description, catalog.DescriptionSource!, "arcanum3.dat", 39_124, "d019b4ec", "471dc91", 1448, 1000, 29723),
            (catalog.GameDescription!, catalog.GameDescriptionSource!, "arcanum.dat", 34_179, "8254286b", "f68e60", 1152, 30000, 31213),
            (catalog.GameKey!, catalog.GameKeySource!, "arcanum.dat", 3_258, "21fce72d", "0efb35", 69, 0, 68),
        };
        foreach (var f in files)
        {
            Assert.AreEqual(f.Archive, Path.GetFileName(f.Source.Source.Path).ToLowerInvariant());
            var bytes = stack.Read(f.Source).Bytes!;
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.AreEqual((f.Bytes, true, true), (bytes.LongLength, sha.StartsWith(f.ShaPrefix, StringComparison.Ordinal), sha.EndsWith(f.ShaSuffix, StringComparison.Ordinal)), f.Archive);
            Assert.AreEqual((f.Count, f.First, f.Last), (f.File.Count, f.File.Messages[0].Number, f.File.LastNumber), f.Archive);
            Assert.IsTrue(f.File.Messages.All(m => MesText.Decode(m, MesTextProfile.Ascii).Status == MesDecodeStatus.Decoded), "The installed English text is ASCII.");
        }
        Assert.AreEqual(31213, catalog.MaxNumber);

        using var owner = AreaOriginalValidator.OpenFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var mobiles = owner.LoadStrict().Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile).ToArray();
        var core = mobiles.Where(o => o.Scope == AreaScope.Core).ToArray();

        // Every Crash Site core item, including those held in core NPC inventories (RES-023:
        // 52 items, 47 names in the base file, 5 in the module file; no keys, all identified).
        var held = core.Where(o => o.Type is 15 or 16).SelectMany(o => Handles(o, 56)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = mobiles.Where(o => o.Type is >= 5 and <= 14 && (o.Scope == AreaScope.Core || held.Contains(o.Key.ObjectIdSha256))).ToArray();
        var names = items.Select(o => ObjectDisplayName.Resolve(o, catalog, CritterRecognition.Recognized)).ToArray();
        Assert.AreEqual(52, items.Length);
        Assert.IsTrue(names.All(n => n.Status == ObjectNameStatus.Resolved && n.Source == ObjectNameSource.Description));
        Assert.AreEqual(47, names.Count(n => n.Lookup!.File == ObjectDescriptionFile.Description));
        Assert.AreEqual(5, names.Count(n => n.Lookup!.File == ObjectDescriptionFile.GameDescription));
        Assert.AreEqual(1, items.Count(o => Field(o, ObjectDisplayName.NameSlot) == 2804), "The ring's name class is unique.");
        Assert.AreEqual(ObjectDescriptionFile.GameDescription, names[Array.FindIndex(items, o => Field(o, ObjectDisplayName.NameSlot) == 2804)].Lookup!.File);

        // Every core NPC has a shown name whether or not the observer recognizes it.
        var npcs = core.Where(o => o.Type == 16).ToArray();
        Assert.IsTrue(npcs.Length > 0);
        foreach (var recognition in new[] { CritterRecognition.Recognized, CritterRecognition.NotRecognized })
            Assert.IsTrue(npcs.All(o => ObjectDisplayName.Resolve(o, catalog, recognition).Status == ObjectNameStatus.Resolved), recognition.ToString());
    }

    private static int? Field(AreaObjectRecord o, int slot) =>
        o.EffectiveFields.SingleOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } ? (int)n : null;

    // An inventory list element is the 24-byte wire ID as six little-endian words; its digest is the item's own ID digest.
    private static IEnumerable<string> Handles(AreaObjectRecord o, int slot)
    {
        if (o.EffectiveFields.SingleOrDefault(f => f.Slot == slot) is not { Presence: AreaPresence.Array } list) yield break;
        foreach (var element in list.Elements)
        {
            var raw = new byte[24];
            for (var i = 0; i < 6; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i * 4), checked((uint)element.Words[i]));
            yield return Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        }
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Fail(name + " is required for this opt-in check (missing inputs fail, not skip).");
        return value!;
    }
}
