using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Saru3VfiTool.Subtitles;

public static class SubtitleTextConverter
{
    private const int HeaderSize = 0x28;
    private const int NameSize = 0x28;
    private const int IndexSize = 0x08;
    private const int FieldSize = 0x08;
    private const uint StringType = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static SubtitleTextDocument Read(byte[] data)
    {
        RequireRange(data, 0, HeaderSize, "text BIN header");
        if (!SubtitleMagic.IsTextContainer(data))
            throw new InvalidDataException("Invalid text BIN magic (expected 0x72312487)");

        uint groupCountValue = ReadUInt32(data, 4);
        if (groupCountValue > int.MaxValue)
            throw new InvalidDataException("Text BIN group count is too large");
        int groupCount = (int)groupCountValue;

        int[] sections = ReadSections(data);
        int names = sections[0];
        int index = sections[1];
        int records = sections[2];
        int text = sections[3];
        if (names < HeaderSize || names > index || index > records || records > text || text > data.Length)
            throw new InvalidDataException("Text BIN sections are out of order");
        RequireTable(data, names, groupCount, NameSize, "text BIN names");
        if (names + (long)groupCount * NameSize > index)
            throw new InvalidDataException("Text BIN names overlap the index section");

        var document = new SubtitleTextDocument();
        int textContentEnd = text;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int nameOffset = checked(names + groupIndex * NameSize);
            string name = ReadFixedString(data.AsSpan(nameOffset, 32), $"group name {groupIndex}");

            uint recordCountValue = ReadUInt32(data, nameOffset + 0x20);
            uint indexOffsetValue = ReadUInt32(data, nameOffset + 0x24);
            if (recordCountValue > int.MaxValue || indexOffsetValue > int.MaxValue)
                throw new InvalidDataException($"Text BIN group {groupIndex} is too large");
            int recordCount = (int)recordCountValue;
            int groupIndexOffset = checked(index + (int)indexOffsetValue);
            RequireTable(data, groupIndexOffset, recordCount, IndexSize, $"group {groupIndex} index");
            if (groupIndexOffset + (long)recordCount * IndexSize > records)
                throw new InvalidDataException($"Text BIN group {groupIndex} index overlaps the records section");

            var group = new TextBinGroup { Name = name };
            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                int indexEntry = checked(groupIndexOffset + recordIndex * IndexSize);
                uint fieldCountValue = ReadUInt32(data, indexEntry);
                uint recordOffsetValue = ReadUInt32(data, indexEntry + 4);
                if (fieldCountValue > int.MaxValue || recordOffsetValue > int.MaxValue)
                    throw new InvalidDataException($"Text BIN record {groupIndex}:{recordIndex} is too large");
                int fieldCount = (int)fieldCountValue;
                int recordOffset = checked(records + (int)recordOffsetValue);
                RequireTable(data, recordOffset, fieldCount, FieldSize,
                    $"record {groupIndex}:{recordIndex} fields");
                if (recordOffset + (long)fieldCount * FieldSize > text)
                    throw new InvalidDataException($"Text BIN record {groupIndex}:{recordIndex} overlaps the text section");

                var record = new TextBinRecord();
                for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                {
                    int fieldOffset = checked(recordOffset + fieldIndex * FieldSize);
                    uint type = ReadUInt32(data, fieldOffset);
                    uint value = ReadUInt32(data, fieldOffset + 4);
                    var field = new TextBinField { Type = type };
                    if (type == StringType)
                    {
                        field.Text = ReadText(data, text, value, groupIndex, recordIndex, fieldIndex,
                            out int stringEnd);
                        textContentEnd = Math.Max(textContentEnd, stringEnd);
                    }
                    else
                        field.Value = value;
                    record.Fields.Add(field);
                }
                group.Records.Add(record);
            }
            document.Groups.Add(group);
        }
        return document;
    }

    public static byte[] Write(SubtitleTextDocument document)
    {
        Validate(document);
        return WriteCanonical(document);
    }

    private static byte[] WriteCanonical(SubtitleTextDocument document)
    {
        int groupCount = document.Groups.Count;
        int totalRecords = 0;
        int totalFields = 0;
        foreach (TextBinGroup group in document.Groups)
        {
            totalRecords = checked(totalRecords + group.Records.Count);
            foreach (TextBinRecord record in group.Records)
                totalFields = checked(totalFields + record.Fields.Count);
        }

        int names = Align16(HeaderSize);
        int index = Next16(checked(names + groupCount * NameSize));
        // Each metadata table ends at the next 0x10 boundary, even when its
        // entries already end on a boundary.
        int records = Next16(checked(index + totalRecords * IndexSize));
        int text = Next16(checked(records + totalFields * FieldSize));
        byte[] prefix = new byte[text];

        WriteUInt32(prefix, 0, 0x72312487);
        WriteUInt32(prefix, 4, checked((uint)groupCount));
        WriteSection(prefix, 0x08, names);
        WriteSection(prefix, 0x10, index);
        WriteSection(prefix, 0x18, records);
        WriteSection(prefix, 0x20, text);

        using var textData = new MemoryStream();
        int indexCursor = 0;
        int recordCursor = 0;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            TextBinGroup group = document.Groups[groupIndex];
            int nameOffset = names + groupIndex * NameSize;
            WriteFixedString(prefix, nameOffset, group.Name);
            WriteUInt32(prefix, nameOffset + 0x20, checked((uint)group.Records.Count));
            WriteUInt32(prefix, nameOffset + 0x24, checked((uint)indexCursor));

            foreach (TextBinRecord record in group.Records)
            {
                int indexEntry = index + indexCursor;
                WriteUInt32(prefix, indexEntry, checked((uint)record.Fields.Count));
                WriteUInt32(prefix, indexEntry + 4, checked((uint)recordCursor));
                indexCursor = checked(indexCursor + IndexSize);

                foreach (TextBinField field in record.Fields)
                {
                    int fieldOffset = records + recordCursor;
                    WriteUInt32(prefix, fieldOffset, field.Type);
                    if (field.Type == StringType)
                    {
                        if (textData.Position > uint.MaxValue)
                            throw new InvalidDataException("Text BIN string data exceeds four GiB");
                        WriteUInt32(prefix, fieldOffset + 4, (uint)textData.Position);
                        byte[] encoded = StrictUtf8.GetBytes(field.Text!);
                        textData.Write(encoded);
                        textData.WriteByte(0);
                    }
                    else
                    {
                        WriteUInt32(prefix, fieldOffset + 4, field.Value!.Value);
                    }
                    recordCursor = checked(recordCursor + FieldSize);
                }
            }
        }

        int unpaddedSize = checked(text + (int)textData.Length);
        byte[] output = new byte[Align16(unpaddedSize)];
        Array.Copy(prefix, output, prefix.Length);
        textData.Position = 0;
        _ = textData.Read(output, text, (int)textData.Length);
        return output;
    }

    private static void Validate(SubtitleTextDocument document)
    {
        if (document.Groups is null)
            throw new InvalidDataException("Text BIN groups are required");
        foreach (TextBinGroup group in document.Groups)
        {
            group.Name ??= "";
            ValidateString(group.Name, "Text BIN group name", 32);
            if (group.Records is null)
                throw new InvalidDataException($"Text BIN group '{group.Name}' has no records array");
            foreach (TextBinRecord record in group.Records)
            {
                if (record.Fields is null)
                    throw new InvalidDataException($"Text BIN group '{group.Name}' has a record without fields");
                foreach (TextBinField field in record.Fields)
                {
                    if (field.Type == StringType)
                    {
                        if (field.Text is null)
                            throw new InvalidDataException($"Text BIN group '{group.Name}' has a type 1 field without text");
                        ValidateString(field.Text, $"Text BIN group '{group.Name}' string", null);
                        if (field.Value.HasValue)
                            throw new InvalidDataException("Text BIN type 1 fields use text, not value");
                    }
                    else
                    {
                        if (!field.Value.HasValue)
                            throw new InvalidDataException($"Text BIN type {field.Type} field is missing value");
                        if (field.Text is not null)
                            throw new InvalidDataException($"Text BIN type {field.Type} fields use value, not text");
                    }
                }
            }
        }
    }

    private static void ValidateString(string value, string label, int? maximumBytes)
    {
        if (value.Contains('\0'))
            throw new InvalidDataException($"{label} cannot contain NUL characters");
        int byteCount = StrictUtf8.GetByteCount(value);
        if (maximumBytes.HasValue && byteCount > maximumBytes.Value)
            throw new InvalidDataException($"{label} exceeds {maximumBytes.Value} UTF-8 bytes");
    }

    private static int[] ReadSections(byte[] data)
    {
        int[] sections = new int[4];
        for (int i = 0; i < sections.Length; i++)
        {
            int offset = 0x08 + i * 8;
            uint value = ReadUInt32(data, offset);
            uint reserved = ReadUInt32(data, offset + 4);
            if (value > int.MaxValue)
                throw new InvalidDataException("Text BIN section offset is too large");
            if (reserved != 0)
                throw new InvalidDataException("Text BIN section offset has a nonzero reserved word");
            sections[i] = (int)value;
        }
        return sections;
    }

    private static string ReadText(byte[] data, int text, uint relativeOffset,
        int groupIndex, int recordIndex, int fieldIndex, out int contentEnd)
    {
        long absoluteOffset = (long)text + relativeOffset;
        if (absoluteOffset < text || absoluteOffset >= data.Length)
            throw new InvalidDataException(
                $"Text BIN string {groupIndex}:{recordIndex}:{fieldIndex} points outside the text section");
        int start = (int)absoluteOffset;
        int end = Array.IndexOf(data, (byte)0, start);
        if (end < 0)
            throw new InvalidDataException($"Text BIN string {groupIndex}:{recordIndex}:{fieldIndex} is not NUL-terminated");
        contentEnd = end + 1;
        return DecodeUtf8(data.AsSpan(start, end - start),
            $"Text BIN string {groupIndex}:{recordIndex}:{fieldIndex}");
    }

    private static string ReadFixedString(ReadOnlySpan<byte> data, string label)
    {
        int end = data.IndexOf((byte)0);
        if (end < 0)
            end = data.Length;
        return DecodeUtf8(data[..end], label);
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> data, string label)
    {
        try
        {
            return StrictUtf8.GetString(data);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"{label} is not valid UTF-8", ex);
        }
    }

    private static void WriteFixedString(byte[] data, int offset, string value)
    {
        byte[] encoded = StrictUtf8.GetBytes(value);
        Array.Copy(encoded, 0, data, offset, encoded.Length);
    }

    private static int Align16(int value) => checked((value + 15) & ~15);

    private static int Next16(int value) => checked((value + 16) & ~15);

    private static void WriteSection(byte[] data, int offset, int value)
    {
        WriteUInt32(data, offset, checked((uint)value));
        WriteUInt32(data, offset + 4, 0);
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

    private static void RequireTable(byte[] data, int offset, int count, int stride, string label)
    {
        long size = (long)count * stride;
        if (size > int.MaxValue)
            throw new InvalidDataException($"{label} is too large");
        RequireRange(data, offset, (int)size, label);
    }

    private static void RequireRange(byte[] data, int offset, int size, string label)
    {
        if (offset < 0 || size < 0 || offset > data.Length - size)
            throw new InvalidDataException($"{label} range is outside the file");
    }
}
