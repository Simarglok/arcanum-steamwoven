using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GamepadLab.Core;

/// <summary>Lossless, self-contained content-addressed geometry witnesses. Never drops observations.</summary>
public static class LabEvidenceCodec
{
    public static string Encode(object report)
    {
        var root = JsonSerializer.SerializeToNode(report)!.AsObject();
        var witnesses = new JsonObject();
        JsonNode? Intern(JsonNode? node)
        {
            JsonNode? result = node switch
            {
                JsonObject obj => new JsonObject(obj.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Intern(pair.Value)))),
                JsonArray array => new JsonArray(array.Select(Intern).ToArray()),
                _ => node?.DeepClone()
            };
            if (result is not JsonObject && result is not JsonArray) return result;
            var json = result.ToJsonString();
            if (System.Text.Encoding.UTF8.GetByteCount(json) < 256) return result;
            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            if (!witnesses.ContainsKey(hash)) witnesses[hash] = json;
            return new JsonObject { ["$witness"] = hash };
        }
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(pair => pair.Key).ToArray())
                {
                    if (key is "scrollObservations" or "settlement" or "labelGeometry") obj[key] = Intern(obj[key]);
                    else Visit(obj[key]);
                }
            }
            else if (node is JsonArray array) foreach (var value in array) Visit(value);
        }
        Visit(root);
        root["witnessSchema"] = "arcanum-gamepad-geometry-witnesses-v1";
        root["witnesses"] = witnesses;
        return root.ToJsonString();
    }
}
