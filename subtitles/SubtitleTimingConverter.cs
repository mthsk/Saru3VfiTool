using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Saru3VfiTool.Subtitles;

public static class SubtitleTimingConverter
{
    private const int HeaderSize = 0x10;
    private const int CueSize = 0x10;

    public static SubtitleTimingDocument Read(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Subtitle timing file is truncated");
        if (!SubtitleMagic.IsTimingContainer(data))
            throw new InvalidDataException("Invalid subtitle timing magic (expected sbt\\0)");

        uint countValue = ReadUInt32(data, 4);
        if (countValue > int.MaxValue)
            throw new InvalidDataException("Subtitle timing cue count is too large");
        int count = (int)countValue;
        long expectedSize = HeaderSize + (long)count * CueSize;
        if (expectedSize != data.Length)
            throw new InvalidDataException($"Subtitle timing size {data.Length} does not match {count} cues");

        float firstStart = ReadSingle(data, 8);
        float totalDuration = ReadSingle(data, 12);
        ValidateFinite(firstStart, "first start");
        ValidateFinite(totalDuration, "total duration");
        if (firstStart < 0 || totalDuration < firstStart)
            throw new InvalidDataException("Subtitle timing header range is invalid");

        var cues = new List<SubtitleTimingCue>(count);
        float previousStart = -1;
        for (int i = 0; i < count; i++)
        {
            int offset = HeaderSize + i * CueSize;
            uint index = ReadUInt32(data, offset);
            uint reserved = ReadUInt32(data, offset + 4);
            float start = ReadSingle(data, offset + 8);
            float end = ReadSingle(data, offset + 12);
            if (index != i)
                throw new InvalidDataException($"Subtitle timing cue {i} has index {index}");
            if (reserved != 0)
                throw new InvalidDataException($"Subtitle timing cue {i} has a nonzero reserved word");
            ValidateCue(i, start, end, totalDuration, previousStart);
            cues.Add(new SubtitleTimingCue { Start = start, End = end });
            previousStart = start;
        }

        if (count != 0 && !firstStart.Equals(cues[0].Start))
            throw new InvalidDataException("Subtitle first-start header does not match cue zero");

        return new SubtitleTimingDocument { Cues = cues, TotalDuration = totalDuration };
    }

    public static byte[] Write(SubtitleTimingDocument document)
    {
        if (document.Cues is null)
            throw new InvalidDataException("Subtitle timing cues are required");
        ValidateFinite(document.TotalDuration, "total duration");
        if (document.TotalDuration < 0)
            throw new InvalidDataException("Subtitle total duration cannot be negative");

        float previousStart = -1;
        for (int i = 0; i < document.Cues.Count; i++)
        {
            SubtitleTimingCue cue = document.Cues[i];
            ValidateCue(i, cue.Start, cue.End, document.TotalDuration, previousStart);
            previousStart = cue.Start;
        }

        byte[] output = new byte[checked(HeaderSize + document.Cues.Count * CueSize)];
        output[0] = (byte)'s';
        output[1] = (byte)'b';
        output[2] = (byte)'t';
        WriteUInt32(output, 4, checked((uint)document.Cues.Count));
        WriteSingle(output, 8, document.Cues.Count == 0 ? 0 : document.Cues[0].Start);
        WriteSingle(output, 12, document.TotalDuration);

        for (int i = 0; i < document.Cues.Count; i++)
        {
            int offset = HeaderSize + i * CueSize;
            WriteUInt32(output, offset, (uint)i);
            WriteSingle(output, offset + 8, document.Cues[i].Start);
            WriteSingle(output, offset + 12, document.Cues[i].End);
        }
        return output;
    }

    private static void ValidateCue(int index, float start, float end, float total, float previousStart)
    {
        ValidateFinite(start, $"cue {index} start");
        ValidateFinite(end, $"cue {index} end");
        if (start < 0 || start > end || end > total || start < previousStart)
            throw new InvalidDataException($"Subtitle timing cue {index} has invalid range {start}..{end}");
    }

    private static void ValidateFinite(float value, string label)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException($"Subtitle timing {label} is not finite");
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

    private static float ReadSingle(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));

    private static void WriteSingle(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
}
