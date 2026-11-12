using System.Text.Json;
using Arcanum.Simulation.Ai;

namespace Arcanum.Session;

public static partial class GameSessionSnapshotCodec
{
    // ARC-WORLD-1157 (schema 91): the monster generators' counter table (the slots that are not zero), the creatures the generators made so far and the slot mark of the critters they made.
    private static readonly string[] GeneratorsFields = ["slots", "made", "copies"];
    private static readonly string[] GeneratorSlotFields = ["slot", "live", "blocked"];
    private static readonly string[] GeneratorMadeFields = ["entity", "made"];
    private static readonly string[] GeneratorCopyFields = ["entity", "slot"];

    private static void WriteGenerators(Utf8JsonWriter w, SessionGeneratorState generators)
    {
        w.WriteStartObject("generators");
        w.WriteStartArray("slots");
        foreach (var slot in generators.Slots.OrderBy(slot => slot.Slot))
        {
            w.WriteStartObject();
            w.WriteNumber("slot", slot.Slot);
            w.WriteNumber("live", slot.Live);
            w.WriteBoolean("blocked", slot.Blocked);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("made");
        foreach (var made in generators.Made.OrderBy(made => made.Entity, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("entity", made.Entity);
            w.WriteNumber("made", made.Made);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("copies");
        foreach (var copy in generators.Copies.OrderBy(copy => copy.Entity, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("entity", copy.Entity);
            w.WriteNumber("slot", copy.Slot);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    // Shapes and ranges of the numbers; the cross checks (a copy is a created critter, a slot once) are Restore's.
    private static SessionGeneratorState Generators(JsonElement element)
    {
        const string p = "$.generators";
        var f = Fields(element, p, GeneratorsFields);
        var slots = Array(f["slots"], p + ".slots", GeneratorSettings.SlotCount).Select((row, i) =>
        {
            var path = $"{p}.slots[{i}]";
            var g = Fields(row, path, GeneratorSlotFields);
            return new SessionGeneratorSlot(Int(g["slot"], path + ".slot", 0, GeneratorSettings.SlotCount - 1), Int(g["live"], path + ".live", 0, GeneratorSettings.MaxCap), Bool(g["blocked"], path + ".blocked"));
        }).ToArray();
        var made = Array(f["made"], p + ".made", SessionGeneratorState.MaxMade).Select((row, i) =>
        {
            var path = $"{p}.made[{i}]";
            var g = Fields(row, path, GeneratorMadeFields);
            return new SessionGeneratorMade(Entity(g["entity"], path + ".entity"), Int(g["made"], path + ".made", 0, GeneratorSettings.MaxBudget));
        }).ToArray();
        var copies = Array(f["copies"], p + ".copies", SessionCreatedObject.MaxObjects).Select((row, i) =>
        {
            var path = $"{p}.copies[{i}]";
            var g = Fields(row, path, GeneratorCopyFields);
            return new SessionGeneratorCopy(Entity(g["entity"], path + ".entity"), Int(g["slot"], path + ".slot", 0, GeneratorSettings.SlotCount - 1));
        }).ToArray();
        return new(slots, made, copies);
    }
}
