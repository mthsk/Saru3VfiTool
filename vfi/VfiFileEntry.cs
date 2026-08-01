using System.IO;
using System.Text;

namespace Saru3VfiTool.Vfi;

public class VfiFileEntry
{
    public ushort EntrySize { get; set; }
    public ushort ParentOff { get; set; }
    public uint OffsetSectors { get; set; }
    public uint Size { get; set; }
    public string Name { get; set; } = "";

    public long OffsetByte => OffsetSectors * VfiHeader.SectorSize;

    public static VfiFileEntry Read(BinaryReader br)
    {
        var entry = new VfiFileEntry
        {
            EntrySize = br.ReadUInt16(),
            ParentOff = br.ReadUInt16(),
            OffsetSectors = br.ReadUInt32(),
            Size = br.ReadUInt32(),
        };
        int nameLen = entry.EntrySize - 12;
        if (nameLen < 0 || nameLen > 512)
            throw new InvalidDataException($"Invalid entry size: {entry.EntrySize}");
        var nameBytes = br.ReadBytes(nameLen);
        entry.Name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
        return entry;
    }

    public void Write(BinaryWriter bw)
    {
        var nameBytes = Encoding.ASCII.GetBytes(Name);
        int nameLen = nameBytes.Length + 1; // include null terminator
        int computedSize = 12 + nameLen;
        if (EntrySize < computedSize)
            EntrySize = (ushort)computedSize;

        bw.Write(EntrySize);
        bw.Write(ParentOff);
        bw.Write(OffsetSectors);
        bw.Write(Size);
        bw.Write(nameBytes);
        bw.Write((byte)0);

        // Honor original padding stored in EntrySize
        int padding = EntrySize - computedSize;
        for (int i = 0; i < padding; i++)
            bw.Write((byte)0);
    }

    public int ComputeSize()
    {
        int rawSize = 12 + Encoding.ASCII.GetByteCount(Name) + 1;
        // Round up to the nearest multiple of 4
        return (rawSize + 3) & ~3;
    }
}