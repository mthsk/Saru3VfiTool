using System;
using System.IO;
using System.IO.Compression;

namespace Saru3VfiTool.Compression;

public static class SzCompression
{
    public static byte[] Decompress(byte[] data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("SZ data too short");

        uint decompressedSize = BitConverter.ToUInt32(data, 0);

        using var input = new MemoryStream(data, 4, data.Length - 4);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream((int)decompressedSize);

        deflate.CopyTo(output);
        var result = output.ToArray();

        return result;
    }

    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, true))
        {
            deflate.Write(data, 0, data.Length);
        }

        var compressed = output.ToArray();
        var result = new byte[4 + compressed.Length];
        BitConverter.GetBytes((uint)data.Length).CopyTo(result, 0);
        compressed.CopyTo(result, 4);
        return result;
    }
}
