using System;
using System.IO;
using System.Text;

namespace Saru3VfiTool.Vfi;

public class VfiFolderEntry
{
    public ushort EntrySize { get; set; }
    public ushort NextOff { get; set; }
    public ushort ParentOff { get; set; }
    public ushort Dummy { get; set; }
    public string Path { get; set; } = "";

    public static VfiFolderEntry Read(BinaryReader br)
    {
        var entry = new VfiFolderEntry
        {
            EntrySize = br.ReadUInt16(),
            NextOff = br.ReadUInt16(),
            ParentOff = br.ReadUInt16(),
            Dummy = br.ReadUInt16(),
        };
        int pathLen = entry.EntrySize - 8;
        if (pathLen < 0 || pathLen > 512)
            throw new InvalidDataException($"Invalid folder entry size: {entry.EntrySize}");
        var pathBytes = br.ReadBytes(pathLen);
        entry.Path = Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');
        return entry;
    }

    public void Write(BinaryWriter bw)
    {
        var pathBytes = Encoding.ASCII.GetBytes(Path);
        int pathLen = pathBytes.Length + 1;
        int computedSize = 8 + pathLen;
        if (EntrySize < computedSize)
            EntrySize = (ushort)computedSize;

        bw.Write(EntrySize);
        bw.Write(NextOff);
        bw.Write(ParentOff);
        bw.Write(Dummy);
        bw.Write(pathBytes);
        bw.Write((byte)0);

        int padding = EntrySize - computedSize;
        for (int i = 0; i < padding; i++)
            bw.Write((byte)0);
    }

    public static ushort ComputeSize(string name)
    {
        var rawSize = 8 + name.Length + 1;
        var computed = (rawSize + 1) & ~1;   // 2-byte align
        return (ushort)Math.Max(12, computed);
    }
}