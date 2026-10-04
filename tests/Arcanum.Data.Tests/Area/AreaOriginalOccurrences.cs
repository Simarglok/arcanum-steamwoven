using System.Text.Json;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

public sealed partial class AreaOriginalTests
{
    private static void CheckOccurrences(AreaSnapshot snapshot, JsonElement oracle)
    {
        (long Sector, int Dat, int Count)[] core =
        [
            (86503327147, 458, 4), (86503327148, 459, 7), (86503327149, 460, 0),
            (86570436011, 462, 15), (86570436012, 463, 317), (86570436013, 464, 31),
            (86637544875, 466, 0), (86637544876, 467, 51), (86637544877, 468, 1)
        ];
        CollectionAssert.AreEqual(core.Select(s => s.Sector).ToArray(), snapshot.Sectors.Where(s => s.Scope == AreaScope.Core).Select(s => s.Key.Packed).ToArray());
        Assert.AreEqual(snapshot.Objects.Count, snapshot.Objects.Select(o => o.Key).Distinct().Count());
        var sectors = oracle.GetProperty("sectors").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetInt64());
        foreach (var actual in snapshot.Sectors.Where(s => s.Present))
        {
            var expected = sectors[actual.Key.Packed]; var entry = expected.GetProperty("entry");
            Assert.AreEqual(entry.GetProperty("ordinal").GetInt32(), actual.DatOrdinal);
            Assert.AreEqual(entry.GetProperty("offset").GetUInt32(), actual.Entry!.Offset);
            Assert.AreEqual(entry.GetProperty("name_sha256").GetString(), actual.Entry.NameSha256);
            Assert.AreEqual(expected.GetProperty("payload_sha256").GetString(), actual.PayloadSha256);
            Assert.AreEqual(expected.GetProperty("framing").GetProperty("terminal").GetInt32(), actual.TerminalCount);
        }
        foreach (var (sector, dat, count) in core)
        {
            var actual = snapshot.Sectors.Single(s => s.Key.Packed == sector);
            Assert.AreEqual(dat, actual.DatOrdinal); Assert.AreEqual(count, actual.TerminalCount);
            Assert.AreEqual(count, snapshot.Objects.Count(o => o.Key.Sector == sector && o.Key.Kind == AreaSourceKind.Static));
        }
        var statics = oracle.GetProperty("statics").EnumerateArray().ToDictionary(r => (r.GetProperty("sector_id").GetInt64(), r.GetProperty("ordinal").GetInt32()));
        var mobiles = oracle.GetProperty("mobiles").EnumerateArray().ToDictionary(r => r.GetProperty("index").GetInt32());
        var prototypes = oracle.GetProperty("prototypes").EnumerateArray().ToDictionary(r => r.GetProperty("own_number").GetInt32());
        foreach (var actual in snapshot.Objects)
        {
            var expected = actual.Key.Kind == AreaSourceKind.Static ? statics[(actual.Key.Sector!.Value, actual.Key.SourceIndex)] : mobiles[actual.Key.SourceIndex];
            Assert.AreEqual(expected.GetProperty("offset").GetInt64(), actual.Key.Offset);
            Assert.AreEqual(expected.GetProperty("length").GetInt32(), actual.Key.Length);
            Assert.AreEqual(expected.GetProperty("sha256").GetString(), actual.RawSha256);
            Assert.AreEqual(expected.GetProperty("semantic_sha256").GetString(), actual.SemanticSha256);
            Assert.AreEqual(expected.GetProperty("own_sha256").GetString(), actual.Key.ObjectIdSha256);
            Assert.AreEqual(expected.GetProperty("parent_sha256").GetString(), actual.PrototypeReferenceSha256);
            Assert.AreEqual(expected.GetProperty("parent_number").GetInt32(), actual.PrototypeNumber);
            Assert.AreEqual(prototypes[actual.PrototypeNumber].GetProperty("own_sha256").GetString(), actual.PrototypeOwnSha256);
            Assert.AreEqual(expected.GetProperty("type").GetInt32(), actual.Type);
            Assert.AreEqual(expected.GetProperty("world_x").GetInt64(), actual.WorldX);
            Assert.AreEqual(expected.GetProperty("world_y").GetInt64(), actual.WorldY);
            var input = snapshot.Inputs.Single(i => i.Key.Role == (actual.Key.Kind == AreaSourceKind.Static ? AreaSourceRole.Module : AreaSourceRole.NormalGameMobile));
            Assert.AreEqual(input.Sha256, actual.Key.SourceSha256);
            Assert.AreEqual(snapshot.RuntimeMap, actual.Key.RuntimeMap);
            CheckFields(actual.DirectFields, expected.GetProperty("fields"), false);
            CheckFields(actual.EffectiveFields, expected.GetProperty("effective"), true);
            Assert.AreEqual(actual.SerializedLocation, actual.DirectFields.Single(f => f.Slot == 1).Number);
            if (actual.Scope == AreaScope.Core)
            {
                var visual = snapshot.Visuals.Single(v => v.Key.Object == actual.Key && v.Key.Layer == AreaLayerKind.Body);
                Assert.AreEqual(actual.Key.ObjectIdSha256, visual.Key.ObjectIdSha256);
                Assert.AreEqual(actual.Key.SourceIndex, visual.Key.SourceIndex);
                Assert.AreEqual(actual.Key.SourceSha256, visual.Key.Sector.SourceSha256);
            }
        }
        Assert.AreEqual(426, statics.Count);
        Assert.IsTrue(statics.Values.All(r => r.GetProperty("own_discriminator").GetInt32() == 0));
        Assert.AreEqual(397, snapshot.Objects.Count(o => o.Key.Kind == AreaSourceKind.Static && o.Key.ObjectIdSha256 == "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0"));
        var terrain = oracle.GetProperty("terrain").EnumerateArray().ToDictionary(r => (r[0].GetInt64(), r[1].GetInt32()));
        Assert.AreEqual(81920, terrain.Count);
        var visualKeys = snapshot.Visuals.Select(v => v.Key).ToHashSet();
        foreach (var actual in snapshot.Terrain)
        {
            var expected = terrain[(actual.Key.Sector.Packed, actual.Key.SourceIndex)];
            Assert.AreEqual(expected[2].GetUInt32(), actual.RawArt);
            Assert.AreEqual(expected[4].GetInt64(), actual.WorldX); Assert.AreEqual(expected[5].GetInt64(), actual.WorldY);
            Assert.AreEqual(actual.Key.SourceIndex & 63, actual.LocalX); Assert.AreEqual(actual.Key.SourceIndex >> 6, actual.LocalY);
            Assert.IsTrue(visualKeys.Contains(actual.SelectedVisual));
        }
    }

    private static void CheckFields(IReadOnlyList<AreaFieldValue> actual, JsonElement expected, bool effective)
    {
        Assert.AreEqual(expected.EnumerateObject().Count(), actual.Count);
        foreach (var field in actual)
        {
            var value = expected.GetProperty(field.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var presence = value.GetProperty("presence").GetString();
            var expectedPresence = presence switch { "null" => AreaPresence.Null, "array" => value.GetProperty("count").GetInt32() == 0 ? AreaPresence.Empty : AreaPresence.Array, "empty" => AreaPresence.Empty, _ => AreaPresence.Value };
            Assert.AreEqual(expectedPresence, field.Presence, $"slot{field.Slot}");
            if (effective) Assert.AreEqual(value.GetProperty("origin").GetString() == "instance" ? AreaValueOrigin.Instance : AreaValueOrigin.Prototype, field.Origin);
            if (value.TryGetProperty("value", out var number) && number.ValueKind == JsonValueKind.Number)
                Assert.AreEqual(number.GetInt64(), field.Number);
            if (value.TryGetProperty("id_sha256", out var id)) Assert.AreEqual(id.GetString(), field.Fingerprint);
            if (value.TryGetProperty("entries", out var entries))
            {
                Assert.AreEqual(entries.GetArrayLength(), field.Elements.Count);
                foreach (var (element, index) in entries.EnumerateArray().Select((e, i) => (e, i)))
                {
                    Assert.AreEqual(element.GetProperty("key").GetInt32(), field.Elements[index].Key);
                    if (element.TryGetProperty("value", out var scalar) && scalar.ValueKind == JsonValueKind.Number && value.GetProperty("width").GetInt32() == 4)
                        Assert.AreEqual((long)unchecked((uint)scalar.GetInt64()), field.Elements[index].Words[0]);
                }
            }
        }
    }
}
