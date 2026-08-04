using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Saru3VfiTool.Compression;

public static class SzCompression
{
    public static byte[] Decompress(byte[] data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("SZ data too short");

        uint decompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        if (decompressedSize > int.MaxValue)
            throw new InvalidDataException($"SZ output is too large: {decompressedSize} bytes");

        using var input = new MemoryStream(data, 4, data.Length - 4, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream((int)decompressedSize);
        deflate.CopyTo(output);

        byte[] result = output.ToArray();
        if (result.Length != decompressedSize)
            throw new InvalidDataException($"SZ size mismatch: header says {decompressedSize}, stream produced {result.Length}");

        return result;
    }

    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        output.Write(new byte[4], 0, 4);
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        byte[] result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), checked((uint)data.Length));
        return result;
    }
}
