using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamepadLab.Core;

namespace GamepadLab.Tests;

internal static class EvidenceCodecTests
{
    public static void Run()
    {
        var sample = new { text = "Синтетическая длинная подпись " + new string('x', 320), width = 25.5, height = 45, alive = false };
        var report = new { tuples = new[] { new { labelGeometry = sample, settlement = new[] { sample, sample },
            scrollObservations = new[] { new { before = sample, after = sample, frame = 21 }, new { before = sample, after = sample, frame = 22 } } } } };
        var expected = JsonSerializer.SerializeToNode(report)!;
        var encoded = LabEvidenceCodec.Encode(report);
        var root = JsonNode.Parse(encoded)!.AsObject();
        var table = root["witnesses"]!.AsObject();
        Assertions.True(table.Count > 0 && Encoding.UTF8.GetByteCount(encoded) < Encoding.UTF8.GetByteCount(expected.ToJsonString()), "lossless witness sharing must reduce repeated geometry");
        JsonNode? Expand(JsonNode? node)
        {
            if (node is JsonObject obj && obj["$witness"] is JsonValue reference)
            {
                var key = reference.GetValue<string>(); var json = table[key]!.GetValue<string>();
                Assertions.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(), key, "actual witness bytes match stored SHA256");
                return Expand(JsonNode.Parse(json));
            }
            if (node is JsonObject value) return new JsonObject(value.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Expand(pair.Value))));
            if (node is JsonArray array) return new JsonArray(array.Select(Expand).ToArray());
            return node?.DeepClone();
        }
        Assertions.True(JsonNode.DeepEquals(expected["tuples"], Expand(root["tuples"])), "all original adverse/followup geometry, strings, numbers and frame witnesses survive exactly");
        Console.WriteLine("Evidence codec: lossless actual SHA256/repeated geometry/adverse samples PASS");
    }
}
