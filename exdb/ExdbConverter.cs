using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Saru3VfiTool.Exdb;

public static class ExdbConverter
{
    public static ExdbDocument Read(byte[] data)
    {
        if (data.Length < 4 || data[0] != (byte)'E' || data[1] != (byte)'D' || data[2] != (byte)'B')
            throw new InvalidDataException("No EXDB/EDB magic");

        List<string> headerLines = ReadHeaderLines(data, out int textualHeaderEnd);
        string stn = headerLines.FirstOrDefault(line => line.StartsWith("stn:", StringComparison.Ordinal))
            ?? throw new InvalidDataException("EXDB header has no stn line");
        string[] stnParts = stn.Split(':');
        if (stnParts.Length != 4 || !int.TryParse(stnParts[2], out int declaredFields) ||
            !int.TryParse(stnParts[3], out int recordCount) || declaredFields < 0 || recordCount < 0)
            throw new InvalidDataException($"Invalid EXDB stn line: {stn}");

        int recordSize = ParseIntegerLine(headerLines, "sizest:");
        int baseOffset = ParseIntegerLine(headerLines, "b:");
        if (baseOffset < textualHeaderEnd || baseOffset > data.Length)
            throw new InvalidDataException($"EXDB record-table offset is out of bounds: {baseOffset}");
        if (recordSize <= 0)
            throw new InvalidDataException($"Invalid EXDB record size: {recordSize}");
        if ((long)baseOffset + (long)recordCount * recordSize > data.Length)
            throw new InvalidDataException("EXDB record table extends past end of file");

        var rawFields = new List<(string type, int offset, string name)>();
        foreach (string line in headerLines)
        {
            string[] parts = line.Split(':', 3);
            if (parts.Length == 3 && (parts[0] == "s" || parts[0] == "f" || parts[0] == "i") &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int offset))
            {
                rawFields.Add((parts[0], offset, parts[2]));
            }
        }

        if (rawFields.Count != declaredFields)
            throw new InvalidDataException($"EXDB header declares {declaredFields} fields, parsed {rawFields.Count}");

        List<string> jsonNames = Deduplicate(rawFields.Select(field => field.name));
        int[] sortedOffsets = rawFields.Select(field => field.offset).Distinct().OrderBy(offset => offset)
            .Concat(new[] { recordSize }).ToArray();

        var document = new ExdbDocument
        {
            SchemaName = stnParts[1],
            RecordSize = recordSize,
            HeaderBlockSize = baseOffset,
            HeaderBase64 = Convert.ToBase64String(data, 0, baseOffset)
        };

        for (int index = 0; index < rawFields.Count; index++)
        {
            var field = rawFields[index];
            if (field.offset < 0 || field.offset >= recordSize)
                throw new InvalidDataException($"EXDB field '{field.name}' has invalid offset {field.offset}");

            int nextOffset = sortedOffsets.First(offset => offset > field.offset);
            int span = nextOffset - field.offset;
            if ((field.type == "i" || field.type == "f") && span < 4)
                throw new InvalidDataException($"EXDB field '{field.name}' is smaller than four bytes");

            document.Fields.Add(new ExdbField
            {
                Type = field.type,
                Offset = field.offset,
                Name = field.name,
                JsonName = jsonNames[index],
                Span = span
            });
        }

        for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            int recordOffset = baseOffset + recordIndex * recordSize;
            ReadOnlySpan<byte> record = data.AsSpan(recordOffset, recordSize);
            var jsonRecord = new JObject();
            foreach (ExdbField field in document.Fields)
                jsonRecord[field.JsonName] = ReadValue(record, field);

            // Raw preservation data is intentionally last so editable values stay visible first.
            jsonRecord["_raw"] = Convert.ToBase64String(record);
            document.Records.Add(jsonRecord);
        }

        int trailingOffset = baseOffset + recordCount * recordSize;
        if (trailingOffset < data.Length)
            document.TrailingDataBase64 = Convert.ToBase64String(data, trailingOffset, data.Length - trailingOffset);

        return document;
    }

    public static byte[] Write(ExdbDocument document)
    {
        ValidateDocument(document);
        byte[] header = TryReuseHeader(document) ?? BuildCanonicalHeader(document);
        int baseOffset = header.Length;

        int capacity = checked(baseOffset + document.Records.Count * document.RecordSize);
        using var output = new MemoryStream(capacity);
        output.Write(header, 0, header.Length);

        foreach (JObject jsonRecord in document.Records)
        {
            byte[] record = CreateRecordBuffer(jsonRecord, document.RecordSize);
            foreach (ExdbField field in document.Fields)
            {
                if (!jsonRecord.TryGetValue(field.JsonName, StringComparison.Ordinal, out JToken? token))
                    continue;
                if (!ValueMatches(record, field, token))
                    WriteValue(record, field, token);
            }
            output.Write(record, 0, record.Length);
        }

        if (!string.IsNullOrWhiteSpace(document.TrailingDataBase64))
        {
            byte[] trailing = Convert.FromBase64String(document.TrailingDataBase64);
            output.Write(trailing, 0, trailing.Length);
        }

        return output.ToArray();
    }

    private static List<string> ReadHeaderLines(byte[] data, out int textualHeaderEnd)
    {
        var lines = new List<string>();
        int lineStart = 0;
        textualHeaderEnd = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != (byte)'\n')
                continue;

            string line = Encoding.ASCII.GetString(data, lineStart, i - lineStart).TrimEnd('\r');
            lines.Add(line);
            lineStart = i + 1;
            if (line.StartsWith("b:", StringComparison.Ordinal))
            {
                textualHeaderEnd = lineStart;
                return lines;
            }

            if (i > 1024 * 1024)
                throw new InvalidDataException("EXDB text header is unreasonably large");
        }

        throw new InvalidDataException("EXDB header has no complete b line");
    }

    private static int ParseIntegerLine(IEnumerable<string> lines, string prefix)
    {
        string line = lines.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"EXDB header has no {prefix.TrimEnd(':')} line");
        if (!int.TryParse(line[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            throw new InvalidDataException($"Invalid EXDB header line: {line}");
        return value;
    }

    private static List<string> Deduplicate(IEnumerable<string> names)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (string name in names)
        {
            seen.TryGetValue(name, out int count);
            seen[name] = count + 1;
            result.Add(count == 0 ? name : $"{name}.{count}");
        }
        return result;
    }

    private static JToken ReadValue(ReadOnlySpan<byte> record, ExdbField field)
    {
        ReadOnlySpan<byte> value = record.Slice(field.Offset, field.Span);
        return field.Type switch
        {
            "s" => new JValue(ReadAsciiString(value)),
            "i" => new JValue(BinaryPrimitives.ReadInt32LittleEndian(value[..4])),
            "f" => FloatToJson(BinaryPrimitives.ReadInt32LittleEndian(value[..4])),
            _ => throw new InvalidDataException($"Unsupported EXDB field type: {field.Type}")
        };
    }

    private static string ReadAsciiString(ReadOnlySpan<byte> value)
    {
        int nul = value.IndexOf((byte)0);
        if (nul >= 0)
            value = value[..nul];
        return Encoding.ASCII.GetString(value);
    }

    private static JToken FloatToJson(int bits)
    {
        float value = BitConverter.Int32BitsToSingle(bits);
        if (float.IsNaN(value)) return new JValue("NaN");
        if (float.IsPositiveInfinity(value)) return new JValue("Infinity");
        if (float.IsNegativeInfinity(value)) return new JValue("-Infinity");
        return new JValue(value);
    }

    private static byte[] CreateRecordBuffer(JObject jsonRecord, int size)
    {
        if (jsonRecord.TryGetValue("_raw", StringComparison.Ordinal, out JToken? rawToken) &&
            rawToken.Type == JTokenType.String)
        {
            try
            {
                byte[] raw = Convert.FromBase64String(rawToken.Value<string>() ?? "");
                if (raw.Length == size)
                    return raw;
            }
            catch (FormatException)
            {
                // Fall through to a zeroed record. The editable field values are still applied.
            }
        }
        return new byte[size];
    }

    private static bool ValueMatches(byte[] record, ExdbField field, JToken token)
    {
        ReadOnlySpan<byte> source = record.AsSpan(field.Offset, field.Span);
        return field.Type switch
        {
            "s" => string.Equals(ReadAsciiString(source), token.Type == JTokenType.Null ? "" : token.ToString(), StringComparison.Ordinal),
            "i" => BinaryPrimitives.ReadInt32LittleEndian(source[..4]) == token.Value<int>(),
            "f" => FloatMatches(BinaryPrimitives.ReadInt32LittleEndian(source[..4]), ParseFloat(token)),
            _ => false
        };
    }

    private static bool FloatMatches(int rawBits, float value)
    {
        float rawValue = BitConverter.Int32BitsToSingle(rawBits);
        if (float.IsNaN(rawValue) && float.IsNaN(value))
            return true;
        return rawValue.Equals(value);
    }

    private static void WriteValue(byte[] record, ExdbField field, JToken token)
    {
        Span<byte> destination = record.AsSpan(field.Offset, field.Span);
        switch (field.Type)
        {
            case "s":
            {
                destination.Clear();
                byte[] bytes = Encoding.ASCII.GetBytes(token.Type == JTokenType.Null ? "" : token.ToString());
                int count = Math.Min(bytes.Length, Math.Max(0, field.Span - 1));
                bytes.AsSpan(0, count).CopyTo(destination);
                break;
            }
            case "i":
                BinaryPrimitives.WriteInt32LittleEndian(destination[..4], token.Value<int>());
                break;
            case "f":
                BinaryPrimitives.WriteInt32LittleEndian(destination[..4], BitConverter.SingleToInt32Bits(ParseFloat(token)));
                break;
            default:
                throw new InvalidDataException($"Unsupported EXDB field type: {field.Type}");
        }
    }

    private static float ParseFloat(JToken token)
    {
        if (token.Type == JTokenType.String)
        {
            return token.Value<string>() switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "+Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                string text when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) => value,
                _ => throw new InvalidDataException($"Invalid EXDB float value: {token}")
            };
        }
        return token.Value<float>();
    }

    private static void ValidateDocument(ExdbDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.SchemaName))
            throw new InvalidDataException("EXDB schemaName is required");
        if (document.RecordSize <= 0)
            throw new InvalidDataException("EXDB recordSize must be positive");

        var jsonNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ExdbField field in document.Fields)
        {
            if (field.Type is not ("s" or "i" or "f"))
                throw new InvalidDataException($"Unsupported EXDB field type: {field.Type}");
            if (field.Offset < 0 || field.Span <= 0 || field.Offset + field.Span > document.RecordSize)
                throw new InvalidDataException($"EXDB field '{field.Name}' is outside the record");
            if (!jsonNames.Add(field.JsonName))
                throw new InvalidDataException($"Duplicate EXDB jsonName: {field.JsonName}");
        }
    }

    private static byte[]? TryReuseHeader(ExdbDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.HeaderBase64))
            return null;

        try
        {
            byte[] header = Convert.FromBase64String(document.HeaderBase64);
            if (header.Length != document.HeaderBlockSize)
                return null;

            List<string> lines = ReadHeaderLines(header, out _);
            string stn = lines.FirstOrDefault(line => line.StartsWith("stn:", StringComparison.Ordinal)) ?? "";
            string expectedStn = $"stn:{document.SchemaName}:{document.Fields.Count}:{document.Records.Count}";
            if (!string.Equals(stn, expectedStn, StringComparison.Ordinal))
                return null;
            if (ParseIntegerLine(lines, "sizest:") != document.RecordSize || ParseIntegerLine(lines, "b:") != header.Length)
                return null;

            var expectedFields = document.Fields.Select(field => $"{field.Type}:{field.Offset}:{field.Name}").ToList();
            var actualFields = lines.Where(line =>
            {
                string[] parts = line.Split(':', 3);
                return parts.Length == 3 && (parts[0] == "s" || parts[0] == "i" || parts[0] == "f");
            }).ToList();
            return expectedFields.SequenceEqual(actualFields, StringComparer.Ordinal) ? header : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] BuildCanonicalHeader(ExdbDocument document)
    {
        int baseOffset = Math.Max(16, document.HeaderBlockSize);
        byte[] text;
        while (true)
        {
            text = Encoding.ASCII.GetBytes(BuildHeaderText(document, baseOffset));
            int needed = Align(text.Length, 16);
            int next = Math.Max(baseOffset, needed);
            if (next == baseOffset)
                break;
            baseOffset = next;
        }

        byte[] header = Enumerable.Repeat((byte)'U', baseOffset).ToArray();
        text.CopyTo(header, 0);
        document.HeaderBlockSize = baseOffset;
        document.HeaderBase64 = Convert.ToBase64String(header);
        return header;
    }

    private static string BuildHeaderText(ExdbDocument document, int baseOffset)
    {
        var builder = new StringBuilder();
        builder.Append("EDB\n");
        builder.Append("stn:").Append(document.SchemaName).Append(':')
            .Append(document.Fields.Count.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(document.Records.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (ExdbField field in document.Fields)
            builder.Append(field.Type).Append(':').Append(field.Offset.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(field.Name).Append('\n');
        builder.Append("sizest:").Append(document.RecordSize.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("b:").Append(baseOffset.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return builder.ToString();
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
}
