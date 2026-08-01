using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Saru3VfiTool.Pck;

public class PckContainer
{
    public const uint MagicValue = 0x004B4350; // 'PCK\0'

    public uint InfoOff { get; set; }
    public uint FileCount { get; set; }
    public List<PckEntry> Entries { get; } = [];

    // Type extensions mapped from the Python implementation
    private static readonly Dictionary<string, string> TypeExt = new()
    {
        { "i3r", "i3d" },
        { "i3c_s", "i3c" }
    };

    public static PckContainer Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms, Encoding.ASCII);
        
        uint magic = br.ReadUInt32();
        if (magic != MagicValue)
            throw new InvalidDataException($"Invalid PCK magic: 0x{magic:X8}");

        var container = new PckContainer
        {
            InfoOff = br.ReadUInt32(),
            FileCount = br.ReadUInt32(),
        };

        long entryTableOffset = container.InfoOff;
        
        if (entryTableOffset < 12 || entryTableOffset > data.Length - 16)
            throw new InvalidDataException($"PCK entry table offset out of bounds: {entryTableOffset} (dataLen={data.Length})");

        for (int i = 0; i < container.FileCount; i++)
        {
            // FORCE the stream back to the correct entry table position
            long currentEntryPos = entryTableOffset + (i * 16);
            br.BaseStream.Position = currentEntryPos;

            if (currentEntryPos + 16 > data.Length)
                throw new InvalidDataException($"PCK entry {i} extends past EOF");

            var entry = PckEntry.Read(br);
            
            // Reading these moves the stream position away from the entry table
            if (entry.NameOff < data.Length)
            {
                br.BaseStream.Position = entry.NameOff;
                entry.Name = ReadBoundedNullString(br, data.Length, 256);
            }
            
            if (entry.AttrOff < data.Length)
            {
                br.BaseStream.Position = entry.AttrOff;
                entry.Attributes = ReadBoundedNullString(br, data.Length, 512);
            }
            
            container.Entries.Add(entry);
        }

        return container;
    }

    private static string ReadBoundedNullString(BinaryReader br, long dataLength, int maxLen)
    {
        var sb = new StringBuilder();
        long startPos = br.BaseStream.Position;
        while (br.BaseStream.Position < dataLength && 
               br.BaseStream.Position < startPos + maxLen)
        {
            byte b = br.ReadByte();
            if (b == 0) break;
            
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    public static byte[] Rebuild(List<(string name, string attributes, byte[] data)> members)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII);

        uint headerSize = 12; 
        uint entryTableSize = (uint)(members.Count * 16); 
        uint stringPoolOff = headerSize + entryTableSize; 
        
        var stringPool = new List<byte>(); 
        var entries = new List<(uint nameOff, uint attrOff, uint offset, uint size)>(); 

        foreach (var (name, attributes, data) in members)
        {
            uint nameOff = stringPoolOff + (uint)stringPool.Count; 
            stringPool.AddRange(Encoding.ASCII.GetBytes(name)); 
            stringPool.Add(0); 
            
            uint attrOff = stringPoolOff + (uint)stringPool.Count; 
            stringPool.AddRange(Encoding.ASCII.GetBytes(attributes)); 
            stringPool.Add(0); 
            
            entries.Add((nameOff, attrOff, 0, (uint)data.Length)); 
        }

        uint dataOff = stringPoolOff + (uint)stringPool.Count; 
        dataOff = (dataOff + 15) & ~15u; 

        uint currentOff = dataOff; 
        var finalEntries = new List<(uint nameOff, uint attrOff, uint offset, uint size)>(); 
        foreach (var (nameOff, attrOff, _, size) in entries)
        {
            finalEntries.Add((nameOff, attrOff, currentOff, size)); 
            currentOff += size; 
            currentOff = (currentOff + 15) & ~15u; 
        }

        bw.Write(MagicValue); 
        bw.Write(headerSize); 
        bw.Write((uint)members.Count); 

        foreach (var (nameOff, attrOff, offset, size) in finalEntries)
        {
            bw.Write(nameOff); 
            bw.Write(attrOff); 
            bw.Write(offset); 
            bw.Write(size); 
        }

        bw.BaseStream.Position = stringPoolOff; 
        bw.Write(stringPool.ToArray()); 

        for (int i = 0; i < members.Count; i++)
        {
            bw.BaseStream.Position = finalEntries[i].offset; 
            bw.Write(members[i].data); 
        }

        bw.Flush(); 
        return ms.ToArray(); 
    }

    public static string GetSafeMemberName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "_unnamed";

        string clean = name.Replace("\\", "/").Trim('/');
        string baseName = Path.GetFileNameWithoutExtension(clean);
        return string.IsNullOrEmpty(baseName) ? "_unnamed" : baseName;
    }

    public static string GetTypeHint(string attributes)
    {
        if (string.IsNullOrWhiteSpace(attributes)) 
            return "bin";

        string[] toks = attributes.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return toks.Length > 0 ? toks[0] : "bin";
    }

    public static string GetTypeOf(string typeHint)
    {
        return TypeExt.TryGetValue(typeHint, out string? mapped) ? mapped : typeHint;
    }
}