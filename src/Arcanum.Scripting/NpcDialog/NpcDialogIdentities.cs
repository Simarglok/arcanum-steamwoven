using Arcanum.Data.Area;

namespace Arcanum.Scripting.NpcDialog;

internal static class IdentityValidation
{
    internal static string Hash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("An exact lowercase SHA256 is required.");
        return value;
    }
    internal static Guid Owner(Guid value) => value != Guid.Empty ? value : throw new ArgumentException("An owner epoch is required.");
    internal static int Range(int value, int min, int max) => value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(nameof(value));
    internal static AreaObjectKey Key(AreaObjectKey key)
    {
        Hash(key.SourceSha256); Hash(key.ObjectIdSha256); Range(key.RuntimeMap, 1, int.MaxValue);
        if (!Enum.IsDefined(key.Kind) || key.Offset < 0 || key.SourceIndex < 0 || key.Length <= 0 || key.Sector < 0)
            throw new ArgumentException("Invalid object key.");
        return key;
    }
}

public readonly record struct NpcDialogWorldId
{
    public NpcDialogWorldId(Guid value) => Value = IdentityValidation.Owner(value);
    public Guid Value { get; }
}
public readonly record struct NpcDialogMapId
{
    public NpcDialogMapId(int runtimeMap, string moduleSha256) { RuntimeMap = IdentityValidation.Range(runtimeMap, 1, int.MaxValue); ModuleSha256 = IdentityValidation.Hash(moduleSha256); }
    public int RuntimeMap { get; }
    public string ModuleSha256 { get; }
}
public readonly record struct NpcDialogNpcId
{
    public NpcDialogNpcId(NpcDialogWorldId worldId, NpcDialogMapId mapId, AreaObjectKey areaObjectKey, string rawSha256, string semanticSha256)
    { IdentityValidation.Owner(worldId.Value); IdentityValidation.Hash(mapId.ModuleSha256); WorldId = worldId; MapId = mapId; AreaObjectKey = IdentityValidation.Key(areaObjectKey); RawSha256 = IdentityValidation.Hash(rawSha256); SemanticSha256 = IdentityValidation.Hash(semanticSha256); }
    public NpcDialogWorldId WorldId { get; }
    public NpcDialogMapId MapId { get; }
    public AreaObjectKey AreaObjectKey { get; }
    public string RawSha256 { get; }
    public string SemanticSha256 { get; }
}
public readonly record struct NpcDialogPrototypeId
{
    public NpcDialogPrototypeId(int number, string ownIdSha256, string inputSha256) { Number = IdentityValidation.Range(number, 1, int.MaxValue); OwnIdSha256 = IdentityValidation.Hash(ownIdSha256); InputSha256 = IdentityValidation.Hash(inputSha256); }
    public int Number { get; }
    public string OwnIdSha256 { get; }
    public string InputSha256 { get; }
}
public readonly record struct NpcDialogPcId
{
    public NpcDialogPcId(NpcDialogWorldId worldId, NpcDialogMapId mapId, Guid ownerId) { IdentityValidation.Owner(worldId.Value); IdentityValidation.Hash(mapId.ModuleSha256); WorldId = worldId; MapId = mapId; OwnerId = IdentityValidation.Owner(ownerId); }
    public NpcDialogWorldId WorldId { get; }
    public NpcDialogMapId MapId { get; }
    public Guid OwnerId { get; }
}
public readonly record struct NpcDialogExtraId
{
    public NpcDialogExtraId(NpcDialogWorldId worldId, NpcDialogMapId mapId, AreaObjectKey areaObjectKey) { IdentityValidation.Owner(worldId.Value); IdentityValidation.Hash(mapId.ModuleSha256); WorldId = worldId; MapId = mapId; AreaObjectKey = IdentityValidation.Key(areaObjectKey); }
    public NpcDialogWorldId WorldId { get; }
    public NpcDialogMapId MapId { get; }
    public AreaObjectKey AreaObjectKey { get; }
}
public enum NpcDialogEvent { Dialog = 9 }
public readonly record struct NpcDialogScriptId
{
    public NpcDialogScriptId(int value) => Value = IdentityValidation.Range(value, 1, 99999);
    public int Value { get; }
}
public readonly record struct NpcDialogScrLine
{
    public NpcDialogScrLine(int value) => Value = IdentityValidation.Range(value, 0, int.MaxValue);
    public int Value { get; }
}
public readonly record struct NpcDialogDlgNode
{
    public NpcDialogDlgNode(int value) => Value = IdentityValidation.Range(value, 1, int.MaxValue);
    public int Value { get; }
}
public readonly record struct NpcDialogLocalFlag
{
    public NpcDialogLocalFlag(int value) => Value = IdentityValidation.Range(value, 0, 31);
    public int Value { get; }
}
public sealed record NpcDialogSourceBinding
{
    public NpcDialogSourceBinding(string role, Guid inputId, long length, string sha256)
    { if (string.IsNullOrWhiteSpace(role) || length < 0) throw new ArgumentException("Invalid source binding."); Role = role; InputId = IdentityValidation.Owner(inputId); Length = length; Sha256 = IdentityValidation.Hash(sha256); }
    public string Role { get; }
    public Guid InputId { get; }
    public long Length { get; }
    public string Sha256 { get; }
}
public sealed class NpcDialogSourceVersion : IEquatable<NpcDialogSourceVersion>
{
    public NpcDialogSourceVersion(Guid generation, string policyId, IEnumerable<NpcDialogSourceBinding> bindings)
    {
        Generation = IdentityValidation.Owner(generation);
        if (policyId != NpcDialogResourcePolicy.ExplicitSelectedArchives) throw new ArgumentException("Unsupported source policy.");
        PolicyId = policyId; Bindings = Array.AsReadOnly(bindings.ToArray());
        if (Bindings.Any(b => b is null) || Bindings.Select(b => b.Role).Distinct().Count() != Bindings.Count) throw new ArgumentException("Duplicate source roles.");
    }
    public Guid Generation { get; }
    public string PolicyId { get; }
    public IReadOnlyList<NpcDialogSourceBinding> Bindings { get; }
    public bool Equals(NpcDialogSourceVersion? other) => other is not null && Generation == other.Generation && PolicyId == other.PolicyId && Bindings.SequenceEqual(other.Bindings);
    public override bool Equals(object? obj) => obj is NpcDialogSourceVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Generation, PolicyId);
}
public readonly record struct NpcDialogStateVersion
{
    public NpcDialogStateVersion(Guid ownerEpoch, ulong revision) { OwnerEpoch = IdentityValidation.Owner(ownerEpoch); Revision = revision; }
    public Guid OwnerEpoch { get; }
    public ulong Revision { get; }
}
public readonly record struct NpcDialogAttachmentStateVersion
{
    public NpcDialogAttachmentStateVersion(Guid ownerEpoch, ulong revision) { OwnerEpoch = IdentityValidation.Owner(ownerEpoch); Revision = revision; }
    public Guid OwnerEpoch { get; }
    public ulong Revision { get; }
}
public sealed record NpcDialogInvocation(NpcDialogWorldId WorldId, NpcDialogMapId MapId, NpcDialogNpcId NpcId,
    NpcDialogPrototypeId PrototypeId, NpcDialogPcId PcId, NpcDialogEvent Event, NpcDialogScrLine CallerLine,
    NpcDialogSourceVersion SourceVersion, NpcDialogStateVersion StateVersion,
    NpcDialogAttachmentStateVersion AttachmentStateVersion, NpcDialogExtraId? Extra = null);

public sealed class NpcDialogAttachmentId
{
    internal NpcDialogAttachmentId(NpcDialogInvocation invocation, AreaFieldValue field, AreaSparseElement element)
    { NpcId = invocation.NpcId; PrototypeId = invocation.PrototypeId; Event = invocation.Event; Origin = field.Origin; Presence = field.Presence; SourceVersion = invocation.SourceVersion; Flags = checked((uint)element.Words[0]); Counters = checked((uint)element.Words[1]); ScriptId = new(checked((int)element.Words[2])); FieldSha256 = FieldDigest(field); }
    // DATA009 does not expose raw array-field fingerprints. This digest binds the
    // owned semantic projection, not a claim about original serialized bytes.
    internal static string FieldDigest(AreaFieldValue field)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(field.Slot); writer.Write((int)field.Presence); writer.Write(field.Elements.Count);
        foreach (var element in field.Elements) { writer.Write(element.Key); foreach (var word in element.Words) writer.Write(checked((uint)word)); }
        writer.Flush();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    public NpcDialogNpcId NpcId { get; }
    public NpcDialogPrototypeId PrototypeId { get; }
    public NpcDialogEvent Event { get; }
    public AreaValueOrigin Origin { get; }
    public AreaPresence Presence { get; }
    public NpcDialogSourceVersion SourceVersion { get; }
    public uint Flags { get; }
    public uint Counters { get; }
    public NpcDialogScriptId ScriptId { get; }
    public string FieldSha256 { get; }
}

/// <summary>The caller retains stream ownership and must exclusively lease it during Prepare.</summary>
public sealed class NpcDialogResourceSource
{
    public NpcDialogResourceSource(Guid sourceId, Stream stream, long expectedLength, string sha256)
    { SourceId = IdentityValidation.Owner(sourceId); Stream = stream ?? throw new ArgumentNullException(nameof(stream)); if (!stream.CanRead || !stream.CanSeek || expectedLength < 28 || expectedLength > uint.MaxValue) throw new ArgumentException("Invalid borrowed source."); ExpectedLength = expectedLength; Sha256 = IdentityValidation.Hash(sha256); }
    public Guid SourceId { get; }
    internal Stream Stream { get; }
    public long ExpectedLength { get; }
    public string Sha256 { get; }
}
public sealed class NpcDialogResourcePolicy
{
    public const string ExplicitSelectedArchives = "explicit-selected-archives-v1";
    public NpcDialogResourcePolicy(string policyId, NpcDialogSourceVersion sourceVersion, Guid scriptSourceId, Guid dialogSourceId, IEnumerable<NpcDialogResourceSource> sources)
    {
        if (policyId != ExplicitSelectedArchives || sourceVersion.PolicyId != policyId) throw new ArgumentException("Unsupported source policy.");
        PolicyId = policyId; SourceVersion = sourceVersion; ScriptSourceId = IdentityValidation.Owner(scriptSourceId); DialogSourceId = IdentityValidation.Owner(dialogSourceId); Sources = Array.AsReadOnly(sources.ToArray());
        if (Sources.Any(s => s is null) || Sources.Select(s => s.SourceId).Distinct().Count() != Sources.Count || Sources.Select(s => s.Stream).Distinct(ReferenceEqualityComparer.Instance).Count() != Sources.Count || Sources.Count != new[] { scriptSourceId, dialogSourceId }.Distinct().Count() || !Sources.Any(s => s.SourceId == scriptSourceId) || !Sources.Any(s => s.SourceId == dialogSourceId)) throw new ArgumentException("Exactly one distinct named source per role is required.");
    }
    public string PolicyId { get; }
    public NpcDialogSourceVersion SourceVersion { get; }
    public Guid ScriptSourceId { get; }
    public Guid DialogSourceId { get; }
    public IReadOnlyList<NpcDialogResourceSource> Sources { get; }
}
