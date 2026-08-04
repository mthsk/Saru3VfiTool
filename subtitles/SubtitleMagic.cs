using System;

namespace Saru3VfiTool.Subtitles;

public static class SubtitleMagic
{
    // Little-endian 0x72312487.
    public static bool IsTextContainer(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0x87 && data[1] == 0x24 &&
        data[2] == 0x31 && data[3] == 0x72;

    public static bool IsTimingContainer(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == (byte)'s' && data[1] == (byte)'b' &&
        data[2] == (byte)'t' && data[3] == 0;
}
