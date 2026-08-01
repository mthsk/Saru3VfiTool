using System.IO;

namespace Saru3VfiTool.Vfi;

public class VfiHeader
{
    public const uint MagicValue = 0x00494656; // 'VFI\\0' little-endian
    public const uint SectorSize = 0x800;

    public uint Magic { get; set; }
    public uint Version { get; set; }
    public uint DataOffSectors { get; set; }
    public uint Zero { get; set; }
    public ushort Files { get; set; }
    public ushort Folders { get; set; }
    public uint InfoOff { get; set; }
    public uint FoldersOff { get; set; }
    public uint InfoEnd { get; set; }

    public static VfiHeader Read(BinaryReader br)
    {
        var h = new VfiHeader
        {
            Magic = br.ReadUInt32(),
            Version = br.ReadUInt32(),
            DataOffSectors = br.ReadUInt32(),
            Zero = br.ReadUInt32(),
            Files = br.ReadUInt16(),
            Folders = br.ReadUInt16(),
            InfoOff = br.ReadUInt32(),
            FoldersOff = br.ReadUInt32(),
            InfoEnd = br.ReadUInt32(),
        };
        if (h.Magic != MagicValue)
            throw new InvalidDataException($"Invalid VFI magic: 0x{h.Magic:X8}");
        return h;
    }

    public void Write(BinaryWriter bw)
    {
        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(DataOffSectors);
        bw.Write(Zero);
        bw.Write(Files);
        bw.Write(Folders);
        bw.Write(InfoOff);
        bw.Write(FoldersOff);
        bw.Write(InfoEnd);
    }

    public long DataStartByte => DataOffSectors * SectorSize;
}
