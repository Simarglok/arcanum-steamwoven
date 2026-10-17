using System.Security.Cryptography;

namespace Arcanum.Data.Fixes;

/// <summary>
/// The data-fix switches of the two rule modes (owner decision D8, contract docs/world/rule-modes.md; mechanism
/// docs/formats/data-fixes.md). Each switch is a defect in the installed game data that makes the 1.0.7.4 game lose
/// content, confirmed in the verification ledger with a fix recorded by ARC-DATA-022 under decision D2; the original's
/// behaviour is the <c>true</c> value.
/// </summary>
public sealed record DataFixProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>. A profile changed with <c>with</c> is a custom
    /// mix and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>DF1: four stone-examination dialogs hold one record without its effects field, so the game opens no
    /// file and the print line shows nothing. Steamwoven completes the record with an empty effects field.</summary>
    public bool StoneExamineTextLost { get; init; }

    /// <summary>DF2: an NPC line whose effects are written in its response field (an NPC line's response is never
    /// read), so the game never applies them. Steamwoven moves them to the effects field.</summary>
    public bool NpcLineEffectsInResponseLost { get; init; }

    /// <summary>DF3: a player reply whose final-line action (<c>fl N</c>) is written in its response field, which
    /// <c>atoi</c> reads as 0, so the reply ends the talk without the final line. Steamwoven appends it to the effects.</summary>
    public bool ReplyFinalLineInResponseLost { get; init; }

    /// <summary>Recorded fixes; the default setting.</summary>
    public static readonly DataFixProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail data as the game reads it.</summary>
    public static readonly DataFixProfile Original = new()
    {
        Name = "original", StoneExamineTextLost = true, NpcLineEffectsInResponseLost = true, ReplyFinalLineInResponseLost = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<DataFixProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static DataFixProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>Whether the fixes of a ledger row apply under this profile; an unknown row never does.</summary>
    public bool Applies(string ledgerId) => ledgerId switch
    {
        "DF1" => !StoneExamineTextLost,
        "DF2" => !NpcLineEffectsInResponseLost,
        "DF3" => !ReplyFinalLineInResponseLost,
        _ => false,
    };
}

/// <summary>How a DLG fix edits its file. Operations name fields, never carry game text.</summary>
public enum DlgFixOperation
{
    /// <summary>The file's only record lacks its last (effects) field: an empty field is appended.</summary>
    CompleteLastRecord,
    /// <summary>The record's response field holds an action list: it is moved to the end of the effects field
    /// (after <c>", "</c> when that field is not empty) and the response field is left empty.</summary>
    MoveResponseToActions,
}

/// <summary>One fix: the file it was written for (content-stack key and SHA-256 of that exact copy), the operation, the
/// record it edits (<see cref="DlgFixOperation.MoveResponseToActions"/>) and the ledger row whose switch enables it.</summary>
public sealed record DlgDataFix(string LedgerId, string Path, string Sha256, DlgFixOperation Operation, int RecordId = 0);

public enum DlgFixOutcome { Applied, Refused }

/// <summary>What a fix did: <see cref="DlgFixOutcome.Refused"/> when the file did not have the defect the fix expects
/// (the bytes are then left as they were).</summary>
public sealed record DlgFixAttempt(DlgDataFix Fix, DlgFixOutcome Outcome);

/// <summary>The bytes after the fixes, and every fix that matched the file.</summary>
public sealed record DlgFixResult(byte[] Bytes, IReadOnlyList<DlgFixAttempt> Attempts)
{
    public IEnumerable<DlgDataFix> Applied => Attempts.Where(a => a.Outcome == DlgFixOutcome.Applied).Select(a => a.Fix);
}

/// <summary>
/// A catalogue of DLG data fixes (ARC-DATA-022). A fix applies only to the copy it was written for: the path and the
/// SHA-256 of the file as read must both match, so a changed file, a patch or a mod is never edited. The fixes of a file
/// are matched against its unedited digest and applied in catalogue order; each checks the defect it expects first.
/// </summary>
public sealed class DlgDataFixCatalogue
{
    private readonly ILookup<string, DlgDataFix> _byPath;

    public DlgDataFixCatalogue(IEnumerable<DlgDataFix> fixes)
    {
        ArgumentNullException.ThrowIfNull(fixes);
        var list = fixes.ToArray();
        foreach (var fix in list)
        {
            ArgumentNullException.ThrowIfNull(fix);
            if (Content.ContentStack.NormalizeQuery(fix.Path) != fix.Path)
                throw new ArgumentException($"Fix path {fix.Path} is not a normalized content-stack key.", nameof(fixes));
            if (fix.Sha256.Length != 64 || fix.Sha256.Any(c => c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))))
                throw new ArgumentException($"Fix {fix.LedgerId} {fix.Path}: the digest must be 64 lower-case hex digits.", nameof(fixes));
            if (!Enum.IsDefined(fix.Operation) || (fix.Operation == DlgFixOperation.MoveResponseToActions) != (fix.RecordId != 0))
                throw new ArgumentException($"Fix {fix.LedgerId} {fix.Path}: a record is named exactly when the operation edits one.", nameof(fixes));
            if (string.IsNullOrEmpty(fix.LedgerId)) throw new ArgumentException("A fix names its ledger row.", nameof(fixes));
        }
        if (list.Distinct().Count() != list.Length) throw new ArgumentException("A fix is listed twice.", nameof(fixes));
        Fixes = Array.AsReadOnly(list);
        _byPath = list.ToLookup(fix => fix.Path, StringComparer.Ordinal);
    }

    public IReadOnlyList<DlgDataFix> Fixes { get; }

    /// <summary>The fixes for the installed 1.0.7.4 game (module Arcanum), checked by ARC-DATA-022's real-data test.</summary>
    public static DlgDataFixCatalogue Installed { get; } = new(
    [
        new("DF1", "dlg/03042blue_stone_exa.dlg", "c41151dbcbe4aa8567eacb522fc9ce515b31afe1522c4cd786213536e42d0b1b", DlgFixOperation.CompleteLastRecord),
        new("DF1", "dlg/03043red_stone_exa.dlg", "e07c657070951a33920d2091fe1bd7ad103c96e3db64c5ace2931d56ee1eb376", DlgFixOperation.CompleteLastRecord),
        new("DF1", "dlg/03044green_stone_exa.dlg", "57affa83a428d7885d31f18a7ced00b2af2c2f182e27a4880bdf75130278448d", DlgFixOperation.CompleteLastRecord),
        new("DF1", "dlg/03045yellow_stone_exa.dlg", "4bf7f4be804f6a885d22128d9902db20ad85c65de316aff757451ead188dcc7f", DlgFixOperation.CompleteLastRecord),
        new("DF2", "dlg/01306benjamin_gershwin.dlg", "d4f2fa3aac3ce797a97902660bcb8f67b8628b4f8ce8d5cf975c8ca90fabde89", DlgFixOperation.MoveResponseToActions, 315),
        new("DF3", "dlg/01009mayor_blackroot.dlg", "fc3acfae557c1de492d63de44aae066fac9d8542b50cfc49785a7d06120eee43", DlgFixOperation.MoveResponseToActions, 361),
    ]);

    /// <summary>Applies the fixes the profile enables to one file read through the content stack; a file no fix names
    /// comes back as it was (the same array).</summary>
    public DlgFixResult Apply(string path, byte[] bytes, DataFixProfile profile)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(profile);
        var candidates = _byPath[path].Where(fix => profile.Applies(fix.LedgerId)).ToArray();
        if (candidates.Length == 0) return new(bytes, Array.Empty<DlgFixAttempt>());
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var attempts = new List<DlgFixAttempt>();
        var current = bytes;
        foreach (var fix in candidates.Where(fix => fix.Sha256 == digest))
        {
            var edited = fix.Operation == DlgFixOperation.CompleteLastRecord ? CompleteLastRecord(current) : MoveResponseToActions(current, fix.RecordId);
            attempts.Add(new(fix, edited is null ? DlgFixOutcome.Refused : DlgFixOutcome.Applied));
            current = edited ?? current;
        }
        return new(current, attempts.AsReadOnly());
    }

    private static readonly DlgReaderOptions Original = new() { Mode = DlgReadMode.Original };

    private static DlgDocument? TryRead(byte[] bytes)
    {
        try { return DlgReader.Read(bytes, Original); }
        catch (DlgException) { return null; }
    }

    // The defect: no record read, the read ended inside the first record. The fix must yield exactly one record.
    private static byte[]? CompleteLastRecord(byte[] bytes)
    {
        if (TryRead(bytes) is not { Records.Count: 0, StopReason: DlgStopReason.IncompleteRecord }) return null;
        var edited = bytes.Concat("{}"u8.ToArray()).ToArray();
        return TryRead(edited) is { Records.Count: 1, StopReason: DlgStopReason.EndOfInput } ? edited : null;
    }

    // The defect: one record with this id, whose response field holds text that is not a plain number.
    private static byte[]? MoveResponseToActions(byte[] bytes, int recordId)
    {
        if (TryRead(bytes) is not { } document || document.Records.Count(r => r.Id == recordId) != 1) return null;
        var record = document.Records.Single(r => r.Id == recordId);
        var response = record.Response; var actions = record.Action;
        var text = response.Bytes.ToArray();
        var trimmed = System.Text.Encoding.ASCII.GetString(text).Trim();
        if (trimmed.Length == 0 || trimmed.TrimStart('-').All(char.IsAsciiDigit) || actions.PayloadOffset < response.PayloadOffset) return null;
        var separator = actions.Length > 0 ? ", "u8.ToArray() : Array.Empty<byte>();
        var actionsEnd = actions.PayloadOffset + actions.Length;
        var edited = bytes[..response.PayloadOffset].Concat(bytes[(response.PayloadOffset + response.Length)..actionsEnd])
            .Concat(separator).Concat(text).Concat(bytes[actionsEnd..]).ToArray();
        // The edit keeps every record and its order.
        return TryRead(edited) is { } after && after.Records.Count == document.Records.Count && after.StopReason == document.StopReason ? edited : null;
    }
}
