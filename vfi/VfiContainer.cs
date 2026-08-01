using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Saru3VfiTool.Vfi;

public class VfiContainer
{
    public VfiHeader Header { get; set; } = new();
    public List<VfiFileEntry> Files { get; } = [];
    public List<VfiFolderEntry> Folders { get; } = [];
    public List<(ushort hash, ushort entryIndex)> HashTable { get; } = [];

    // Maps folder offset (relative to FoldersOff) -> folder entry
    public Dictionary<ushort, VfiFolderEntry> FolderByOffset { get; } = [];

    public static VfiContainer Read(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var container = new VfiContainer
        {
            Header = VfiHeader.Read(br)
        };

        // Read hash table at +0x20
        br.BaseStream.Position = 0x20;
        for (int i = 0; i < container.Header.Files; i++)
        {
            ushort hash = br.ReadUInt16();
            ushort idx = br.ReadUInt16();
            container.HashTable.Add((hash, idx));
        }

        // Read file entries sequentially from InfoOff
        br.BaseStream.Position = container.Header.InfoOff;
        long fileTableEnd = container.Header.FoldersOff;
        while (br.BaseStream.Position < fileTableEnd && container.Files.Count < container.Header.Files)
        {
            var entry = VfiFileEntry.Read(br);
            container.Files.Add(entry);
        }

        // Read folder entries from FoldersOff to InfoEnd, tracking offsets
        br.BaseStream.Position = container.Header.FoldersOff;
        while (br.BaseStream.Position < container.Header.InfoEnd && container.Folders.Count < container.Header.Folders)
        {
            long startPos = br.BaseStream.Position;
            var folder = VfiFolderEntry.Read(br);
            ushort offset = (ushort)(startPos - container.Header.FoldersOff);
            container.Folders.Add(folder);
            container.FolderByOffset[offset] = folder;
        }

        return container;
    }

    public string ResolveFolderPath(ushort parentOff)
    {
        if (parentOff == 0) return "";
        var parts = new List<string>();
        ushort current = parentOff;
        int safety = 0;
        while (current != 0 && safety < 64)
        {
            safety++;
            if (!FolderByOffset.TryGetValue(current, out var folder))
                break;
            parts.Add(folder.Path);
            current = folder.ParentOff;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public void Write(Stream stream)
    {
        using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        Header.Write(bw);
    }
}