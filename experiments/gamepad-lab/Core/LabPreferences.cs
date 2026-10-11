using System.Collections.ObjectModel;
using System.Text.Json;
using Arcanum.Input;

namespace GamepadLab.Core;

public sealed class LabSlots : IEquatable<LabSlots>
{
    public IReadOnlyDictionary<string, string?> Assignments { get; }
    public static LabSlots Default { get; } = new(new Dictionary<string,string?> {
        ["slot-01"]="synthetic.item.self", ["slot-02"]="synthetic.skill.enemy", ["slot-03"]="synthetic.spell.ally", ["slot-04"]="synthetic.spell.ground",
        ["slot-05"]="synthetic.item.self", ["slot-06"]="synthetic.skill.enemy", ["slot-07"]="synthetic.spell.ally", ["slot-08"]="synthetic.spell.ground",
        ["slot-09"]="synthetic.item.self", ["slot-10"]="synthetic.spell.ground" });
    public LabSlots(IEnumerable<KeyValuePair<string,string?>> assignments) => Assignments = new ReadOnlyDictionary<string,string?>(assignments.Take(11).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal));
    public bool IsValid => Assignments.Count==10 && Enumerable.Range(1,10).All(n=>Assignments.TryGetValue($"slot-{n:00}",out var action) && (action is null || LabActionCatalog.IsAssignable(action)));
    public LabSlots Assign(string slot,string? action)
    {
        if (!GamepadTokens.IsSlot(slot) || (action is not null && !LabActionCatalog.IsAssignable(action))) throw new ArgumentException("invalid slot assignment");
        var copy=Assignments.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal); copy[slot]=action; return new(copy);
    }
    public bool Equals(LabSlots? other)=> other is not null && Assignments.Count==other.Assignments.Count && Assignments.All(p=>other.Assignments.TryGetValue(p.Key,out var value)&&p.Value==value);
    public override bool Equals(object? other)=>other is LabSlots slots&&Equals(slots);
    public override int GetHashCode() { var hash=new HashCode(); foreach(var p in Assignments.OrderBy(p=>p.Key,StringComparer.Ordinal)){hash.Add(p.Key);hash.Add(p.Value);} return hash.ToHashCode(); }
}

public static class LabPreferences
{
    public const string Schema="arcanum-gamepad-lab-settings-v2";
    public const int MaximumBytes=65536;
    public static string Serialize(LabSettings settings)
    {
        var s=settings.Validated();
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteString("schema",Schema); w.WriteNumber("version",2);
            w.WriteNumber("cameraDeadzone",s.CameraDeadzone); w.WriteNumber("actorDeadzone",s.ActorDeadzone);
            w.WriteNumber("cameraSensitivity",s.CameraSensitivity); w.WriteNumber("actorSensitivity",s.ActorSensitivity);
            w.WriteBoolean("invertCameraX",s.InvertCameraX); w.WriteBoolean("invertCameraY",s.InvertCameraY);
            w.WriteString("movementMode",s.MovementMode.ToString()); w.WriteString("activePresetId",s.ActivePresetId);
            w.WritePropertyName("userCopy"); if(s.UserCopy is null) w.WriteNullValue(); else w.WriteRawValue(GamepadBindingCodec.Serialize(s.UserCopy));
            w.WriteStartObject("slots"); foreach(var p in s.Slots.Assignments.OrderBy(p=>p.Key,StringComparer.Ordinal)) {if(p.Value is null)w.WriteNull(p.Key);else w.WriteString(p.Key,p.Value);} w.WriteEndObject();
            w.WriteNumber("uiScalePercent",s.UiScalePercent); w.WriteEndObject();
        }
        var text=System.Text.Encoding.UTF8.GetString(stream.ToArray());
        if(System.Text.Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new InvalidOperationException("settings byte-limit");
        return text;
    }
    public static bool TryDeserialize(string? text,out LabSettings settings,out string diagnostic,bool legacy=false)
    {
        settings=LabSettings.Default;diagnostic="settings-malformed";
        if(text is null||System.Text.Encoding.UTF8.GetByteCount(text)>MaximumBytes){diagnostic="settings-byte-limit";return false;}
        try
        {
            using var doc=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=16});var r=doc.RootElement;
            string[] axes=["version","cameraDeadzone","actorDeadzone","cameraSensitivity","actorSensitivity","invertCameraX","invertCameraY","movementMode"];
            GamepadBindingCodec.RequireFields(r,legacy?axes:["schema",..axes,"activePresetId","userCopy","slots","uiScalePercent"]);
            if(r.GetProperty("version").GetInt32()!=(legacy?1:2))throw new JsonException();
            if(!legacy&&GamepadBindingCodec.Token(r,"schema")!=Schema)throw new JsonException();
            var mode=legacy?(MovementMode)r.GetProperty("movementMode").GetInt32():GamepadBindingCodec.Token(r,"movementMode") switch {"DirectIntent"=>MovementMode.DirectIntent,"NearbyGoal"=>MovementMode.NearbyGoal,_=>throw new JsonException()};
            var s=new LabSettings(2,r.GetProperty("cameraDeadzone").GetDouble(),r.GetProperty("actorDeadzone").GetDouble(),r.GetProperty("cameraSensitivity").GetDouble(),r.GetProperty("actorSensitivity").GetDouble(),r.GetProperty("invertCameraX").GetBoolean(),r.GetProperty("invertCameraY").GetBoolean(),mode);
            if(!legacy)
            {
                BindingProfile? copy=null;var j=r.GetProperty("userCopy");
                if(j.ValueKind!=JsonValueKind.Null && (!GamepadBindingCodec.TryDeserialize(j.GetRawText(),out copy,out _)||copy!.Id!="user-copy"))throw new JsonException();
                var slots=r.GetProperty("slots");GamepadBindingCodec.RequireFields(slots,Enumerable.Range(1,10).Select(n=>$"slot-{n:00}").ToArray());
                s=s with {ActivePresetId=GamepadBindingCodec.Token(r,"activePresetId"),UserCopy=copy,UiScalePercent=r.GetProperty("uiScalePercent").GetInt32(),
                    Slots=new LabSlots(slots.EnumerateObject().Select(p=>new KeyValuePair<string,string?>(p.Name,p.Value.ValueKind==JsonValueKind.Null?null:p.Value.GetString())))};
            }
            if(!s.IsValid)throw new JsonException();
            settings=s;diagnostic=legacy?"settings-migrated-v1":"settings-loaded-v2";return true;
        }
        catch(Exception e)when(e is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException){return false;}
    }
}
