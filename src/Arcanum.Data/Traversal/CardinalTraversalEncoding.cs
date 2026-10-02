using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Traversal;

// Versioned project evidence policy, NOT an original wire format or permission signature.
internal static class CardinalTraversalEncoding
{
    internal static string EvidenceDigest(CardinalTraversalInputKind kind, string moduleId, string resourceId, string prototypeId,
        CardinalTraversalSectorIdentity sector, TerrainEntryExpectation? nameEntry, StaticObstructionInputIdentity? original,
        uint[] words, uint[]? mask, CardinalTerrainCell[] cells, int terminalCount,
        IReadOnlyList<CardinalTraversalCensusRecord> census, int scripts, int scriptBytes)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        Text(w, "CARDINAL_EVIDENCE_V1"); w.Write((byte)kind); Text(w, moduleId); Text(w, resourceId); Text(w, prototypeId);
        w.Write(sector.SectorX); w.Write(sector.SectorY); w.Write(sector.PackedId); w.Write(sector.OriginX); w.Write(sector.OriginY);
        w.Write(sector.Width); w.Write(sector.Height); w.Write(sector.EntryOrdinal); w.Write(sector.Flags); w.Write(sector.DecodedSize);
        w.Write((byte)0); w.Write(nameEntry is not null);
        if (nameEntry is not null) { w.Write(nameEntry.Ordinal); w.Write(nameEntry.Flags); w.Write(nameEntry.DeclaredDecodedSize); }
        w.Write(original is not null);
        if (original is not null)
        {
            Fingerprint(w, original.Module); Fingerprint(w, original.Resource);
            w.Write((uint)original.Prototypes.Count);
            foreach (var p in original.Prototypes.OrderBy(p => p.Aid)) { w.Write(p.Aid); Fingerprint(w, p.Fingerprint); }
            Text(w, original.PrototypeAggregateSha256);
        }
        w.Write((uint)words.Length); foreach (var word in words) w.Write(word);
        w.Write(mask is not null);
        if (mask is not null) { w.Write((uint)mask.Length); foreach (var word in mask) w.Write(word); }
        w.Write((uint)cells.Length);
        foreach (var cell in cells)
        {
            Nullable(w, cell.CategoryBits); Nullable(w, cell.NameCategoryIndex); Nullable(w, cell.MaterialOrdinal); Nullable(w, cell.MesKey);
            var result = cell.Components.TileNameRule;
            w.Write((byte)(result.Supportedness == TerrainObstructionSupportedness.Supported ? CardinalTraversalCoverage.Supported : CardinalTraversalCoverage.Unknown));
            w.Write((byte)result.Applicability); w.Write(result.Disposition.HasValue);
            if (result.Disposition.HasValue) w.Write((byte)(result.Disposition == TerrainObstructionDisposition.Blocked ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open));
            Text(w, result.ReasonCode); Text(w, result.Provenance.SourceId); Nullable(w, result.Provenance.EntryOrdinal);
        }
        w.Write(terminalCount); w.Write((uint)census.Count);
        foreach (var row in census)
        {
            w.Write(row.SourceIndex); w.Write(row.ObjectType); Nullable(w, row.PrototypeAid); Nullable(w, row.PackedLocation);
            Nullable(w, row.WorldX); Nullable(w, row.WorldY); Nullable(w, row.LocalCell?.X); Nullable(w, row.LocalCell?.Y);
            Nullable(w, row.DirectFlags); Nullable(w, row.EffectiveFlags); w.Write((byte)row.FlagsSource); w.Write((byte)row.LocationStatus);
        }
        w.Write(scripts); w.Write(scriptBytes); w.Flush();
        return Digest(stream);
    }
    internal static string ContextDigest(CardinalTraversalContext context)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        Text(w, "CARDINAL_CONTEXT_V1"); Text(w, context.ContextId); w.Write(context.ActorKind); w.Write(context.CurrentArtClass);
        w.Write(context.SpellFlags); w.Write(context.PathFlags); w.Write(context.Polymorph); w.Write(context.Editor);
        // Invalid numeric enums reach semantic Unsupported. This byte is NOT an admission credential.
        w.Write(unchecked((byte)context.ActorState)); w.Write(context.SupplementalRegistryCount); w.Flush();
        return Digest(stream);
    }
    private static string Digest(MemoryStream stream) => Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length))).ToLowerInvariant();
    private static void Text(BinaryWriter w, string value) { var bytes = Encoding.ASCII.GetBytes(value); w.Write((uint)bytes.Length); w.Write(bytes); }
    private static void Fingerprint(BinaryWriter w, StaticObjectInputFingerprint value) { w.Write(value.Length); Text(w, value.Sha256); }
    private static void Nullable(BinaryWriter w, int? value) { w.Write(value.HasValue); if (value.HasValue) w.Write(value.Value); }
    private static void Nullable(BinaryWriter w, long? value) { w.Write(value.HasValue); if (value.HasValue) w.Write(value.Value); }
    private static void Nullable(BinaryWriter w, uint? value) { w.Write(value.HasValue); if (value.HasValue) w.Write(value.Value); }
}
