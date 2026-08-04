using System.IO;

namespace Saru3VfiTool.Pck;

public class PckEntry
{
    public uint NameOff { get; set; }
    public uint AttrOff { get; set; }
    public uint Offset { get; set; }
    public uint Size { get; set; }

    public string Name { get; set; } = "";
    public string Attributes { get; set; } = "";

    public static PckEntry Read(BinaryReader br)
    {
        return new PckEntry
        {
            NameOff = br.ReadUInt32(),
            AttrOff = br.ReadUInt32(),
            Offset = br.ReadUInt32(),
            Size = br.ReadUInt32(),
        };
    }
}
