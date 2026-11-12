using System.Text.Json;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

public static partial class GameSessionSnapshotCodec
{
    // ARC-DEV-210 (schema 73): the settled spell runs, the maintain bar and the last charged cost.
    // ARC-DEV-302 (schema 83): the flags the spell components wrote on critters.
    // ARC-MAGIC-1234 (schema 92): each run's object list (the original's +0x130).
    private static readonly string[] SpellsFields = ["runs", "bar", "lastCost", "objectFlags", "traitArt"];
    private static readonly string[] TraitArtFields = ["entity", "species", "palette"];
    private static readonly string[] ObjectFlagFields = ["entity", "spellSet", "spellCleared", "critterSet", "critterCleared", "objectSet", "objectCleared", "critter2Set", "critter2Cleared", "blitSet", "blitCleared", "npcSet", "npcCleared"];
    private static readonly string[] SpellRunFields =
        ["id", "spell", "action", "source", "parent", "start", "target", "targetTile", "other", "flags", "triggers", "margin", "triggerCount", "setSpellFlags", "objectList"];
    private static readonly string[] SpellObjectFields = ["id", "type", "aptitude"];
    private static readonly string[] MaintainSlotFields = ["run", "state"];
    private const int SpellRunFlagMask = (int)(SpellRunFlags.Active | SpellRunFlags.Free | SpellRunFlags.Processed | SpellRunFlags.Reflected | SpellRunFlags.Unresistable | SpellRunFlags.ForcedEnd | SpellRunFlags.FinalEnd);

    private static void WriteSpells(Utf8JsonWriter w, SessionSpellState spells)
    {
        w.WriteStartObject("spells");
        w.WriteStartArray("runs");
        foreach (var run in spells.Runs.OrderBy(r => r.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("id", run.Id);
            w.WriteNumber("spell", run.Spell);
            w.WriteNumber("action", (int)run.Action);
            WriteSpellObject(w, "source", run.Source);
            WriteSpellObject(w, "parent", run.Parent);
            WriteSpellLocation(w, "start", run.StartLocation);
            WriteSpellObject(w, "target", run.Target);
            WriteSpellLocation(w, "targetTile", run.TargetTile);
            WriteSpellObject(w, "other", run.Other);
            w.WriteNumber("flags", (int)run.Flags);
            w.WriteNumber("triggers", run.Triggers);
            w.WriteNumber("margin", run.Margin);
            w.WriteNumber("triggerCount", run.TriggerCount);
            w.WriteNumber("setSpellFlags", run.SetSpellFlags);
            w.WriteStartArray("objectList");
            foreach (var listed in run.ObjectList.Items) WriteSpellObject(w, listed);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("bar");
        foreach (var slot in spells.Bar)
        {
            w.WriteStartObject();
            w.WriteNumber("run", slot.Run);
            w.WriteNumber("state", (int)slot.State);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteNumber("lastCost", spells.LastCost);
        w.WriteStartArray("objectFlags");
        foreach (var record in spells.ObjectFlags.OrderBy(r => r.Entity, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("entity", record.Entity);
            w.WriteNumber("spellSet", record.Flags.SpellSet);
            w.WriteNumber("spellCleared", record.Flags.SpellCleared);
            w.WriteNumber("critterSet", record.Flags.CritterSet);
            w.WriteNumber("critterCleared", record.Flags.CritterCleared);
            w.WriteNumber("objectSet", record.Flags.ObjectSet);
            w.WriteNumber("objectCleared", record.Flags.ObjectCleared);
            w.WriteNumber("critter2Set", record.Flags.Critter2Set);
            w.WriteNumber("critter2Cleared", record.Flags.Critter2Cleared);
            w.WriteNumber("blitSet", record.Flags.BlitSet);
            w.WriteNumber("blitCleared", record.Flags.BlitCleared);
            w.WriteNumber("npcSet", record.Flags.NpcSet);
            w.WriteNumber("npcCleared", record.Flags.NpcCleared);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("traitArt");
        foreach (var record in spells.TraitArt.OrderBy(r => r.Entity, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("entity", record.Entity);
            w.WriteNumber("species", record.Art.Species);
            w.WriteNumber("palette", record.Art.Palette);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteSpellObject(Utf8JsonWriter w, string name, SpellRunObject? value)
    {
        if (value is null) { w.WriteNull(name); return; }
        w.WriteStartObject(name);
        w.WriteString("id", value.Id);
        w.WriteNumber("type", value.Type);
        w.WriteNumber("aptitude", value.Aptitude);
        w.WriteEndObject();
    }

    private static void WriteSpellObject(Utf8JsonWriter w, SpellRunObject value)
    {
        w.WriteStartObject();
        w.WriteString("id", value.Id);
        w.WriteNumber("type", value.Type);
        w.WriteNumber("aptitude", value.Aptitude);
        w.WriteEndObject();
    }

    private static void WriteSpellLocation(Utf8JsonWriter w, string name, SpellLocation location)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(location.X);
        w.WriteNumberValue(location.Y);
        w.WriteEndArray();
    }

    // Shapes and ranges: the runs ascending by id (the canonical order, the table's slot), defined actions, the flag bits the rules define, five bar slots with defined states.
    // Which actions the build saves and that a showing slot names a run are Restore's.
    private static SessionSpellState Spells(JsonElement element)
    {
        const string p = "$.spells";
        var f = Fields(element, p, SpellsFields);
        var rows = Array(f["runs"], p + ".runs", SpellRunTable.Slots);
        var runs = new SpellRun[rows.Length];
        var previous = -1;
        for (var i = 0; i < rows.Length; i++)
        {
            var at = $"{p}.runs[{i}]";
            var r = Fields(rows[i], at, SpellRunFields);
            var id = Int(r["id"], at + ".id", 0, SpellRunTable.Slots - 1);
            if (id <= previous) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".id");
            previous = id;
            var action = Int(r["action"], at + ".action", 0, int.MaxValue);
            if (!Enum.IsDefined((SpellRunAction)action)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, at + ".action");
            runs[i] = new SpellRun(id, Int(r["spell"], at + ".spell", 0, int.MaxValue), (SpellRunAction)action, SpellObjectOf(r["source"], at + ".source"),
                SpellObjectOf(r["parent"], at + ".parent"), SpellLocationOf(r["start"], at + ".start"), SpellObjectOf(r["target"], at + ".target"),
                SpellLocationOf(r["targetTile"], at + ".targetTile"), SpellObjectOf(r["other"], at + ".other"),
                (SpellRunFlags)FlagBits(r["flags"], at + ".flags", SpellRunFlagMask), Int(r["triggers"], at + ".triggers", int.MinValue, int.MaxValue),
                Int(r["margin"], at + ".margin", int.MinValue, int.MaxValue), Int(r["triggerCount"], at + ".triggerCount", int.MinValue, int.MaxValue))
            { SetSpellFlags = Int(r["setSpellFlags"], at + ".setSpellFlags", int.MinValue, int.MaxValue), ObjectList = SpellObjectList(r["objectList"], at + ".objectList") };
        }
        var barRows = Array(f["bar"], p + ".bar", MaintainBarRules.Slots);
        if (barRows.Length != MaintainBarRules.Slots) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".bar");
        var bar = new MaintainSlot[barRows.Length];
        for (var i = 0; i < barRows.Length; i++)
        {
            var at = $"{p}.bar[{i}]";
            var s = Fields(barRows[i], at, MaintainSlotFields);
            var state = Int(s["state"], at + ".state", 0, int.MaxValue);
            if (!Enum.IsDefined((MaintainSlotState)state)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, at + ".state");
            bar[i] = new(Int(s["run"], at + ".run", -1, SpellRunTable.Slots - 1), (MaintainSlotState)state);
        }
        // The written flags ascending by entity (the canonical order); which entities may carry them and a change's own shape are Restore's.
        var flagRows = Array(f["objectFlags"], p + ".objectFlags", GameSessionOwner.MaxObjectFlagCritters);
        var objectFlags = new SessionObjectFlagRecord[flagRows.Length];
        for (var i = 0; i < flagRows.Length; i++)
        {
            var at = $"{p}.objectFlags[{i}]";
            var o = Fields(flagRows[i], at, ObjectFlagFields);
            var entity = Entity(o["entity"], at + ".entity");
            if (i > 0 && string.CompareOrdinal(objectFlags[i - 1].Entity, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".entity");
            objectFlags[i] = new(entity, new(Int(o["spellSet"], at + ".spellSet", int.MinValue, int.MaxValue), Int(o["spellCleared"], at + ".spellCleared", int.MinValue, int.MaxValue),
                Int(o["critterSet"], at + ".critterSet", int.MinValue, int.MaxValue), Int(o["critterCleared"], at + ".critterCleared", int.MinValue, int.MaxValue),
                Int(o["objectSet"], at + ".objectSet", int.MinValue, int.MaxValue), Int(o["objectCleared"], at + ".objectCleared", int.MinValue, int.MaxValue),
                Int(o["critter2Set"], at + ".critter2Set", int.MinValue, int.MaxValue), Int(o["critter2Cleared"], at + ".critter2Cleared", int.MinValue, int.MaxValue),
                Int(o["blitSet"], at + ".blitSet", int.MinValue, int.MaxValue), Int(o["blitCleared"], at + ".blitCleared", int.MinValue, int.MaxValue),
                Int(o["npcSet"], at + ".npcSet", int.MinValue, int.MaxValue), Int(o["npcCleared"], at + ".npcCleared", int.MinValue, int.MaxValue)));
        }
        // The monster bodies ascending by entity; which entities may carry one is Restore's.
        var artRows = Array(f["traitArt"], p + ".traitArt", GameSessionOwner.MaxTraitArtCritters);
        var traitArt = new SessionTraitArtRecord[artRows.Length];
        for (var i = 0; i < artRows.Length; i++)
        {
            var at = $"{p}.traitArt[{i}]";
            var o = Fields(artRows[i], at, TraitArtFields);
            var entity = Entity(o["entity"], at + ".entity");
            if (i > 0 && string.CompareOrdinal(traitArt[i - 1].Entity, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".entity");
            traitArt[i] = new(entity, new(Int(o["species"], at + ".species", 0, SessionTraitArt.SpeciesLimit - 1), Int(o["palette"], at + ".palette", 0, SessionTraitArt.PaletteLimit - 1)));
        }
        return new(runs, bar, Int(f["lastCost"], p + ".lastCost", 0, int.MaxValue)) { ObjectFlags = objectFlags, TraitArt = traitArt };
    }

    // The list's order is the original's (newest node first); its length is the run table's own bound (a run's list never holds more objects than a target list: 256).
    private static SpellRunObjectList SpellObjectList(JsonElement element, string p)
    {
        var rows = Array(element, p, SpellTargetRules.Capacity);
        if (rows.Length == 0) return SpellRunObjectList.Empty;
        var items = new SpellRunObject[rows.Length];
        for (var i = 0; i < rows.Length; i++) items[i] = SpellObjectOf(rows[i], $"{p}[{i}]") ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, $"{p}[{i}]");
        return new(items);
    }

    private static SpellRunObject? SpellObjectOf(JsonElement element, string p)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        var f = Fields(element, p, SpellObjectFields);
        return new(Entity(f["id"], p + ".id"), Int(f["type"], p + ".type", int.MinValue, int.MaxValue), Int(f["aptitude"], p + ".aptitude", int.MinValue, int.MaxValue));
    }

    private static SpellLocation SpellLocationOf(JsonElement element, string p)
    {
        var pair = Array(element, p, 2);
        if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.WrongType, p);
        return new(UInt(pair[0], p + "[0]"), UInt(pair[1], p + "[1]"));
    }
}
