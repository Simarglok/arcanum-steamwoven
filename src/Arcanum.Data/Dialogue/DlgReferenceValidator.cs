namespace Arcanum.Data;

public static class DlgReferenceValidator
{
    public static DlgReferenceValidationResult Validate(DlgDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var references = new List<DlgReference>();
        foreach (var record in document.Records)
        {
            if (record.Role != DlgRecordRole.Player)
            {
                continue;
            }

            var responseKind = record.ResponseKind!.Value;
            if (responseKind != DlgResponseKind.Direct)
            {
                references.Add(new DlgReference(record.Id, record.ResponseValue, responseKind, null, DlgReferenceStatus.Valid));
                continue;
            }

            var targetId = record.ResponseValue!.Value;
            var status = !document.TryGetRecord(targetId, out var target)
                ? DlgReferenceStatus.MissingTarget
                : target!.Role != DlgRecordRole.Npc
                    ? DlgReferenceStatus.WrongTargetRole
                    : DlgReferenceStatus.Valid;
            references.Add(new DlgReference(record.Id, record.ResponseValue, responseKind, targetId, status));
        }

        return new DlgReferenceValidationResult(references.ToArray());
    }
}
