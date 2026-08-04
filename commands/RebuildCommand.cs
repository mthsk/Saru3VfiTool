using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Saru3VfiTool.Compression;
using Saru3VfiTool.Exdb;
using Saru3VfiTool.Models;
using Saru3VfiTool.Pck;
using Saru3VfiTool.Tim2;
using Saru3VfiTool.Vfi;

namespace Saru3VfiTool.Commands;

public static class RebuildCommand
{
    public static void Run(string manifestPath, string outputPath)
    {
        string baseDir = Path.GetDirectoryName(manifestPath)!;
        string manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonConvert.DeserializeObject<VfiManifest>(manifestJson)!;

        // First pass: build all file data
        var fileData = new List<(string path, byte[] data, bool compressed)>();

        foreach (var fileManifest in manifest.Files)
        {
            string sourcePath = Path.Combine(baseDir, fileManifest.Source.Replace('/', Path.DirectorySeparatorChar));
            byte[] data;
            bool compressed = fileManifest.Compressed;

            Console.WriteLine($"Packing: {fileManifest.Path}");

            if (sourcePath.EndsWith(".pck.json", StringComparison.OrdinalIgnoreCase))
            {
                data = RebuildPck(sourcePath, baseDir);
            }
            else if (sourcePath.EndsWith(".tm2.json", StringComparison.OrdinalIgnoreCase))
            {
                var tim2Manifest = JsonConvert.DeserializeObject<Tim2Manifest>(File.ReadAllText(sourcePath))!;
                string pngPath = Path.Combine(baseDir, tim2Manifest.Source.Replace('/', Path.DirectorySeparatorChar));
                var tim2 = Tim2Converter.FromPng(pngPath, tim2Manifest);
                data = Tim2Converter.WriteTim2(tim2);
            }
            else if (sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ReadJsonType(sourcePath), "exdb", StringComparison.OrdinalIgnoreCase))
            {
                var exdbDocument = JsonConvert.DeserializeObject<ExdbDocument>(File.ReadAllText(sourcePath))
                    ?? throw new InvalidDataException($"Invalid EXDB sidecar: {sourcePath}");
                data = ExdbConverter.Write(exdbDocument);
            }
            else
            {
                data = File.ReadAllBytes(sourcePath);
            }

            if (compressed && !fileManifest.KeepSz)
            {
                data = SzCompression.Compress(data);
            }

            fileData.Add((fileManifest.Path, data, compressed));
        }

        // Build folder table — prefer manifest folders, append any missing ones from file paths
        var folderMap = new Dictionary<string, ushort>();
        var folderDefs = new List<(ushort entrySize, ushort nextOff, ushort parentOff, ushort dummy, string path)>();

        var manifestFolderPaths = new HashSet<string>();
        foreach (var fm in manifest.Folders)
            manifestFolderPaths.Add(fm.Path);

        // Add any new folders implied by files that aren't in the manifest
        var extraFolderPaths = new HashSet<string>();
        foreach (var (path, _, _) in fileData)
        {
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string dirPath = path[..lastSlash];
                var parts = dirPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                string current = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    current = i == 0 ? parts[0] : $"{current}/{parts[i]}";
                    if (!manifestFolderPaths.Contains(current))
                        extraFolderPaths.Add(current);
                }
            }
        }

        var allFolderList = new List<VfiFolderManifest>();
        allFolderList.AddRange(manifest.Folders);
        foreach (var extra in extraFolderPaths.OrderBy(p => p))
        {
            allFolderList.Add(new VfiFolderManifest
            {
                Path = extra,
                Parent = extra.Contains('/') ? extra[..extra.LastIndexOf('/')] : "",
                Next = "",
                Dummy = 0
            });
        }

        // Ensure root exists at offset 0
        if (allFolderList.FindIndex(f => f.Path == "") < 0)
        {
            allFolderList.Insert(0, new VfiFolderManifest
            {
                Path = "",
                Parent = "",
                Next = "",
                Dummy = 0
            });
        }

        // Assign offsets and preserve original numeric linkage when available
        ushort currentFolderOffset = 0;
        foreach (var fm in allFolderList)
        {
            string name = fm.Path.Contains('/') ? fm.Path[(fm.Path.LastIndexOf('/') + 1)..] : fm.Path;
            var entrySize = VfiFolderEntry.ComputeSize(name);

            folderMap[fm.Path] = currentFolderOffset;
            folderDefs.Add((entrySize, fm.NextOff ?? 0, fm.ParentOff ?? 0, fm.Dummy, name));
            currentFolderOffset += entrySize;
        }

        // Resolve parent/next paths to offsets for extra folders (preserved folders keep their numeric values)
        for (int i = 0; i < allFolderList.Count; i++)
        {
            var fm = allFolderList[i];
            if (fm.ParentOff.HasValue && fm.NextOff.HasValue)
                continue; // already have original numeric values

            ushort parentOff = 0;
            if (!string.IsNullOrEmpty(fm.Parent) && folderMap.ContainsKey(fm.Parent))
                parentOff = folderMap[fm.Parent];

            ushort nextOff = 0;
            if (!string.IsNullOrEmpty(fm.Next) && folderMap.ContainsKey(fm.Next))
                nextOff = folderMap[fm.Next];

            var old = folderDefs[i];
            folderDefs[i] = (old.entrySize, nextOff, parentOff, old.dummy, old.path);
        }

        // Calculate layout
        uint headerSize = 0x20;
        uint hashTableSize = (uint)(fileData.Count * 4);
        uint infoOff = headerSize + hashTableSize;

        uint fileTableSize = 0;
        var fileEntries = new List<VfiFileEntry>();
        foreach (var (path, _, _) in fileData)
        {
            int lastSlash = path.LastIndexOf('/');
            string name = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
            string folderPath = lastSlash >= 0 ? path[..lastSlash] : "";

            ushort parentOff = 0;
            if (!string.IsNullOrEmpty(folderPath) && folderMap.ContainsKey(folderPath))
                parentOff = folderMap[folderPath];

            var entry = new VfiFileEntry
            {
                Name = name,
                ParentOff = parentOff,
                Size = 0,
                OffsetSectors = 0
            };
            int computedSize = entry.ComputeSize();
            entry.EntrySize = (ushort)computedSize;
            if (entry.EntrySize < computedSize) entry.EntrySize = (ushort)computedSize;

            fileEntries.Add(entry);
            fileTableSize += entry.EntrySize;
        }

        uint foldersOff = infoOff + fileTableSize;
        uint infoEnd = foldersOff;
        foreach (var (entrySize, _, _, _, _) in folderDefs)
            infoEnd += entrySize;

        uint calculatedDataStart = (infoEnd + VfiHeader.SectorSize - 1) / VfiHeader.SectorSize;
        uint dataStartSectors = manifest.DataOffSectors ?? calculatedDataStart;
        if (dataStartSectors < calculatedDataStart) dataStartSectors = calculatedDataStart;

        // Second pass: assign sector offsets
        uint currentSector = dataStartSectors;
        var finalData = new List<byte[]>();

        for (int i = 0; i < fileData.Count; i++)
        {
            var (_, data, _) = fileData[i];
            fileEntries[i].OffsetSectors = currentSector;
            currentSector += (uint)((data.Length + VfiHeader.SectorSize - 1) / VfiHeader.SectorSize);
            fileEntries[i].Size = (uint)data.Length;
            finalData.Add(data);
        }

        // Write output
        using var fs = File.Create(outputPath);
        using var bw = new BinaryWriter(fs);

        var header = new VfiHeader
        {
            Magic = VfiHeader.MagicValue,
            Version = 1,
            DataOffSectors = dataStartSectors,
            Zero = 0,
            Files = (ushort)fileEntries.Count,
            Folders = (ushort)folderDefs.Count,
            InfoOff = infoOff,
            FoldersOff = foldersOff,
            InfoEnd = infoEnd
        };
        header.Write(bw);

        while (fs.Position < 0x20) bw.Write((byte)0);

        // Compute file entry offsets from InfoOff
        uint fileOffset = 0;
        var fileOffsets = new List<uint>();
        foreach (var entry in fileEntries)
        {
            fileOffsets.Add(fileOffset);
            fileOffset += entry.EntrySize;
        }

        var hashEntries = new List<(ushort hash, ushort idx, int fileIdx)>();
        for (int i = 0; i < fileEntries.Count; i++)
        {
            string fullPath = fileData[i].path;
            ushort hash = VfiHash.Precomputed(fullPath) ?? VfiHash.Compute(fullPath); // hash of FULL PATH
            ushort idx = (ushort)(fileOffsets[i] / 4); // offset from InfoOff, divided by 4
            hashEntries.Add((hash, idx, i));
        }
        // Sort by hash only; stable sort preserves manifest order for collisions
        hashEntries = [.. hashEntries.OrderBy(h => h.hash).ThenBy(h => fileData[h.fileIdx].path)];

        foreach (var (hash, idx, _) in hashEntries)
        {
            bw.Write(hash);
            bw.Write(idx);
        }

        foreach (var entry in fileEntries)
            entry.Write(bw);

        foreach (var (entrySize, nextOff, parentOff, dummy, path) in folderDefs)
        {
            var folderEntry = new VfiFolderEntry
            {
                EntrySize = entrySize,
                NextOff = nextOff,
                ParentOff = parentOff,
                Dummy = dummy,
                Path = path
            };
            folderEntry.Write(bw);
        }

        while (fs.Position < header.DataStartByte) bw.Write((byte)0);

        for (int i = 0; i < finalData.Count; i++)
        {
            long expectedPos = fileEntries[i].OffsetSectors * VfiHeader.SectorSize;
            while (fs.Position < expectedPos) bw.Write((byte)0);

            bw.Write(finalData[i]);

            long endPos = fs.Position;
            long nextSectorPos = (endPos + VfiHeader.SectorSize - 1) / VfiHeader.SectorSize * VfiHeader.SectorSize;
            while (fs.Position < nextSectorPos) bw.Write((byte)0);
        }

        // Preserve original total size (trailing padding)
        if (manifest.TotalSize.HasValue && fs.Position < manifest.TotalSize.Value)
        {
            long pad = manifest.TotalSize.Value - fs.Position;
            for (long i = 0; i < pad; i++) bw.Write((byte)0);
        }

        Console.WriteLine($"Rebuild complete: {outputPath}");
        Console.WriteLine($"  Files: {fileEntries.Count}, Folders: {folderDefs.Count}, Data start: 0x{header.DataStartByte:X}");
        if (manifest.TotalSize.HasValue)
            Console.WriteLine($"  Total size: {fs.Position} (original: {manifest.TotalSize.Value})");
    }

    private static string ReadJsonType(string path) =>
        JObject.Parse(File.ReadAllText(path)).Value<string>("type") ?? "";

    private static byte[] RebuildPck(string pckJsonPath, string baseDir)
    {
        var pckManifest = JsonConvert.DeserializeObject<PckManifest>(File.ReadAllText(pckJsonPath))!;
        var members = new List<(string name, string attributes, byte[] data)>();

        foreach (var member in pckManifest.Members)
        {
            string memberSource = Path.Combine(baseDir, member.Source.Replace('/', Path.DirectorySeparatorChar));
            byte[] data;

            if (memberSource.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !memberSource.EndsWith(".pck.json", StringComparison.OrdinalIgnoreCase))
            {
                string sidecarType = ReadJsonType(memberSource);
                switch (sidecarType.ToLowerInvariant())
                {
                    case "tim2":
                    {
                        var tim2Manifest = JsonConvert.DeserializeObject<Tim2Manifest>(File.ReadAllText(memberSource))!;
                        string pngPath = Path.Combine(baseDir, tim2Manifest.Source.Replace('/', Path.DirectorySeparatorChar));
                        var tim2 = Tim2Converter.FromPng(pngPath, tim2Manifest);
                        data = Tim2Converter.WriteTim2(tim2);
                        break;
                    }
                    case "exdb":
                    {
                        var exdbDocument = JsonConvert.DeserializeObject<ExdbDocument>(File.ReadAllText(memberSource))
                            ?? throw new InvalidDataException($"Invalid EXDB sidecar: {memberSource}");
                        data = ExdbConverter.Write(exdbDocument);
                        break;
                    }
                    default:
                        throw new InvalidDataException($"Unsupported PCK member sidecar type '{sidecarType}' in {memberSource}");
                }
            }
            else
            {
                data = File.ReadAllBytes(memberSource);
            }

            members.Add((member.Name, member.Attributes, data));
        }

        return PckContainer.Rebuild(members);
    }
}
