namespace Arcanum.Data;

public static class DlgReader
{
    private static readonly DlgFieldKind[] FieldKinds =
    [
        DlgFieldKind.Id, DlgFieldKind.Text, DlgFieldKind.AlternateText, DlgFieldKind.Iq,
        DlgFieldKind.Condition, DlgFieldKind.Response, DlgFieldKind.Action,
    ];

    public static DlgDocument Read(ReadOnlySpan<byte> bytes, DlgReaderOptions? options = null)
    {
        var effectiveOptions = options ?? new DlgReaderOptions();
        effectiveOptions.Validate();
        if (bytes.Length > effectiveOptions.MaxInputBytes)
        {
            throw new DlgLimitExceededException("DLG input exceeds MaxInputBytes.", effectiveOptions.MaxInputBytes);
        }

        return Parse(bytes, effectiveOptions);
    }

    public static DlgDocument Read(Stream stream, DlgReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var effectiveOptions = options ?? new DlgReaderOptions();
        effectiveOptions.Validate();
        try
        {
            if (!stream.CanRead)
            {
                throw new ArgumentException("The DLG stream must be readable.", nameof(stream));
            }

            if (stream.CanSeek && stream.Length - stream.Position > effectiveOptions.MaxInputBytes)
            {
                throw new DlgLimitExceededException("DLG input exceeds MaxInputBytes.", effectiveOptions.MaxInputBytes);
            }

            using var buffer = new MemoryStream();
            var scratch = new byte[Math.Min(8192, effectiveOptions.MaxInputBytes)];
            while (true)
            {
                var remaining = effectiveOptions.MaxInputBytes - checked((int)buffer.Length);
                if (remaining == 0)
                {
                    if (stream.ReadByte() != -1)
                    {
                        throw new DlgLimitExceededException("DLG input exceeds MaxInputBytes.", effectiveOptions.MaxInputBytes);
                    }
                    break;
                }

                var read = stream.Read(scratch, 0, Math.Min(scratch.Length, remaining));
                if (read == 0)
                {
                    break;
                }
                buffer.Write(scratch, 0, read);
            }

            return Parse(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), effectiveOptions);
        }
        finally
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }
        }
    }

    private static DlgDocument Parse(ReadOnlySpan<byte> bytes, DlgReaderOptions options)
    {
        var cursor = 0;
        var records = new List<DlgRecord>();
        var recordsById = new Dictionary<int, DlgRecord>();
        ConsumeTrivia(bytes, ref cursor);
        while (cursor < bytes.Length)
        {
            if (bytes[cursor] != (byte)'{')
            {
                if (bytes[cursor] == (byte)'}')
                {
                    throw new DlgFormatException("DLG input has an unmatched closing brace.", cursor, records.Count);
                }
                if (bytes[cursor] == 0)
                {
                    throw new DlgFormatException("DLG input contains a NUL byte.", cursor, records.Count);
                }
                ThrowUnsupported("Unsupported token outside DLG records.", cursor, records.Count);
            }
            if (records.Count == options.MaxRecordCount)
            {
                throw new DlgLimitExceededException("DLG record count exceeds MaxRecordCount.", cursor, records.Count);
            }

            var fields = new DlgField[FieldKinds.Length];
            for (var fieldIndex = 0; fieldIndex < FieldKinds.Length; fieldIndex++)
            {
                var kind = FieldKinds[fieldIndex];
                if (cursor >= bytes.Length)
                {
                    throw new DlgFormatException("DLG record is truncated.", cursor, records.Count, kind);
                }
                if (bytes[cursor] != (byte)'{')
                {
                    if (bytes[cursor] == (byte)'}')
                    {
                        throw new DlgFormatException("DLG record has an unmatched closing brace.", cursor, records.Count, kind);
                    }
                    ThrowUnsupported("DLG record fields must be adjacent brace-delimited fields.", cursor, records.Count, kind);
                }

                var openingOffset = cursor++;
                var payloadOffset = cursor;
                while (cursor < bytes.Length && bytes[cursor] != (byte)'}')
                {
                    var current = bytes[cursor];
                    if (current == 0)
                    {
                        throw new DlgFormatException("DLG field contains a NUL byte.", cursor, records.Count, kind);
                    }
                    if (current == (byte)'{')
                    {
                        ThrowUnsupported("Nested or escaped DLG fields are unsupported.", cursor, records.Count, kind);
                    }
                    cursor++;
                    if (cursor - payloadOffset > options.MaxFieldBytes)
                    {
                        throw new DlgLimitExceededException("DLG field exceeds MaxFieldBytes.", cursor, records.Count, kind);
                    }
                }
                if (cursor == bytes.Length)
                {
                    throw new DlgFormatException("DLG field is unterminated.", openingOffset, records.Count, kind);
                }

                fields[fieldIndex] = new DlgField(kind, payloadOffset, bytes[payloadOffset..cursor].ToArray());
                cursor++;
            }

            var id = ParseRequiredInteger(fields[(int)DlgFieldKind.Id], records.Count);
            if (recordsById.ContainsKey(id))
            {
                throw new DlgFormatException("DLG record ID is duplicated.", fields[(int)DlgFieldKind.Id].PayloadOffset, records.Count, DlgFieldKind.Id);
            }

            var iqValue = ParseOptionalInteger(fields[(int)DlgFieldKind.Iq], records.Count);
            var role = iqValue.HasValue ? DlgRecordRole.Player : DlgRecordRole.Npc;
            if (iqValue == 0)
            {
                throw new DlgFormatException("DLG IQ field must be empty or nonzero.", fields[(int)DlgFieldKind.Iq].PayloadOffset, records.Count, DlgFieldKind.Iq);
            }
            var responseValue = ParseOptionalInteger(fields[(int)DlgFieldKind.Response], records.Count);
            DlgResponseKind? responseKind = role == DlgRecordRole.Player
                ? responseValue switch
                {
                    null or 0 => DlgResponseKind.Exit,
                    > 0 => DlgResponseKind.Direct,
                    _ => DlgResponseKind.Special,
                }
                : null;
            var record = new DlgRecord(records.Count, id, role, iqValue, responseValue, responseKind, fields);
            records.Add(record);
            recordsById.Add(id, record);
            ConsumeTrivia(bytes, ref cursor);
        }

        if (records.Count == 0)
        {
            throw new DlgFormatException("DLG input contains no records.", 0);
        }
        return new DlgDocument(bytes.Length, records.ToArray(), recordsById);
    }

    private static void ConsumeTrivia(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        while (cursor < bytes.Length)
        {
            var current = bytes[cursor];
            if (current == (byte)' ')
            {
                cursor++;
                continue;
            }
            if (current == (byte)'\n')
            {
                cursor++;
                continue;
            }
            if (current == (byte)'\r')
            {
                if (cursor + 1 >= bytes.Length || bytes[cursor + 1] != (byte)'\n')
                {
                    ThrowUnsupported("DLG trivia contains a lone carriage return.", cursor, null);
                }
                cursor += 2;
                continue;
            }
            if (current == (byte)'/' && cursor + 1 < bytes.Length && bytes[cursor + 1] == (byte)'/')
            {
                cursor += 2;
                while (cursor < bytes.Length && bytes[cursor] is not (byte)'\r' and not (byte)'\n')
                {
                    if (bytes[cursor] == 0)
                    {
                        throw new DlgFormatException("DLG comment contains a NUL byte.", cursor);
                    }
                    if (bytes[cursor] < 0x20 || bytes[cursor] > 0x7e)
                    {
                        ThrowUnsupported("DLG comment contains an unsupported byte.", cursor, null);
                    }
                    cursor++;
                }
                continue;
            }
            return;
        }
    }

    private static int ParseRequiredInteger(DlgField field, int recordIndex)
    {
        if (field.Length == 0)
        {
            throw new DlgFormatException("DLG numeric field is empty.", field.PayloadOffset, recordIndex, field.Kind);
        }
        return ParseInteger(field, recordIndex);
    }

    private static int? ParseOptionalInteger(DlgField field, int recordIndex) =>
        field.Length == 0 ? null : ParseInteger(field, recordIndex);

    private static int ParseInteger(DlgField field, int recordIndex)
    {
        var bytes = field.Bytes;
        var negative = bytes[0] == (byte)'-';
        var start = negative ? 1 : 0;
        if (start == bytes.Count)
        {
            throw new DlgFormatException("DLG numeric field has no digits.", field.PayloadOffset, recordIndex, field.Kind);
        }
        long magnitude = 0;
        var maximum = negative ? 2_147_483_648L : int.MaxValue;
        for (var index = start; index < bytes.Count; index++)
        {
            var value = bytes[index];
            if (value is < (byte)'0' or > (byte)'9')
            {
                throw new DlgFormatException("DLG numeric field is not strict ASCII decimal.", field.PayloadOffset + index, recordIndex, field.Kind);
            }
            magnitude = (magnitude * 10) + value - (byte)'0';
            if (magnitude > maximum)
            {
                throw new DlgFormatException("DLG numeric field is outside Int32 range.", field.PayloadOffset, recordIndex, field.Kind);
            }
        }
        return negative ? magnitude == 2_147_483_648L ? int.MinValue : checked(-(int)magnitude) : (int)magnitude;
    }

    private static void ThrowUnsupported(string message, int offset, int? recordIndex, DlgFieldKind? fieldKind = null) =>
        throw new DlgUnsupportedFormatException(message, offset, recordIndex, fieldKind);
}
