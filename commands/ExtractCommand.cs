using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Saru3VfiTool.Compression;
using Saru3VfiTool.Models;
using Saru3VfiTool.Pck;
using Saru3VfiTool.Tim2;
using Saru3VfiTool.Vfi;

namespace Saru3VfiTool.Commands;

public static class ExtractCommand
{
    private static readonly Dictionary<string, string> TypeExt = new()
    {
        { "i3r", "i3d" },
        { "i3c_s", "i3c" }
    };

    public static void Run(string dataBinPath, string outputDir, bool keepPck, bool keepTim2, bool keepSz)
    {
        Directory.CreateDirectory(outputDir);
        using var fs = File.OpenRead(dataBinPath);
        var container = VfiContainer.Read(fs);

        var manifest = new VfiManifest
        {
            // Store original hash table verbatim
            HashTable = [.. container.HashTable.Select(h => new HashEntry { Hash = h.hash, Index = h.entryIndex })]
        };

        // Store original folder table verbatim, including original numeric offsets
        var sortedFolderOffsets = container.FolderByOffset.Keys.OrderBy(k => k).ToList();
        foreach (var offset in sortedFolderOffsets)
        {
            var folder = container.FolderByOffset[offset];
            string path = container.ResolveFolderPath(offset);
            string parentPath = folder.ParentOff == 0
                ? ""
                : container.ResolveFolderPath(folder.ParentOff);
            string nextPath = folder.NextOff == 0 || !container.FolderByOffset.ContainsKey(folder.NextOff)
                ? ""
                : container.ResolveFolderPath(folder.NextOff);

            manifest.Folders.Add(new VfiFolderManifest
            {
                Path = path,
                Parent = parentPath,
                Next = nextPath,
                ParentOff = folder.ParentOff,
                NextOff = folder.NextOff,
                Dummy = folder.Dummy,
                EntrySize = folder.EntrySize
            });
        }

        foreach (var entry in container.Files)
        {
            string folderPath = container.ResolveFolderPath(entry.ParentOff);
            string fullPath = string.IsNullOrEmpty(folderPath)
                ? entry.Name
                : $"{folderPath}/{entry.Name}";

            Console.WriteLine($"Extracting: {fullPath}");

            fs.Position = entry.OffsetByte;
            byte[] rawData = new byte[entry.Size];
            int read = fs.Read(rawData, 0, (int)entry.Size);
            if (read != entry.Size)
                throw new IOException($"Failed to read full data for {fullPath}");

            // --keep-sz: keep the .sz blob entirely opaque
            if (keepSz && entry.Name.EndsWith(".sz", StringComparison.OrdinalIgnoreCase))
            {
                string outFilePath2 = Path.Combine(outputDir, fullPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outFilePath2)!);
                File.WriteAllBytes(outFilePath2, rawData);

                manifest.Files.Add(new VfiFileManifest
                {
                    Path = fullPath,
                    Source = Path.GetRelativePath(outputDir, outFilePath2).Replace('\\', '/'),
                    Compressed = true,
                    KeepSz = true,
                    OriginalSize = entry.Size
                });
                continue;
            }

            bool isCompressed = false;
            byte[] data = rawData;

            if (entry.Name.EndsWith(".sz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    data = SzCompression.Decompress(rawData);
                    isCompressed = true;
                }
                catch
                {
                    Console.WriteLine($"  Warning: failed to decompress {fullPath}, keeping raw");
                    data = rawData;
                    isCompressed = false;
                }
            }

            string outSubPath = fullPath;
            string outFilePath = Path.Combine(outputDir, outSubPath);
            outFilePath = isCompressed
                && outFilePath.EndsWith(".sz", StringComparison.OrdinalIgnoreCase)
                ? outFilePath[..^3]
                : outFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(outFilePath)!);

            bool isPck = outSubPath.EndsWith(".pck", StringComparison.OrdinalIgnoreCase) ||
                         outSubPath.EndsWith(".pck.sz", StringComparison.OrdinalIgnoreCase);
            bool isTim2 = data.Length >= 4 &&
                          data[0] == 'T' && data[1] == 'I' && data[2] == 'M' && data[3] == '2';

            if (!keepPck && isPck)
            {
                string pckDir = outFilePath + ".d";
                Directory.CreateDirectory(pckDir);

                var pckManifest = new PckManifest();
                PckContainer pck;

                try
                {
                    pck = PckContainer.Read(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: failed to parse PCK {fullPath}: {ex.Message}");
                    File.WriteAllBytes(outFilePath, data);
                    manifest.Files.Add(new VfiFileManifest
                    {
                        Path = fullPath,
                        Source = Path.GetRelativePath(outputDir, outFilePath).Replace('\\', '/'),
                        Compressed = isCompressed,
                        OriginalSize = entry.Size
                    });
                    continue;
                }

                var usedNames = new HashSet<string>();

                for (int i = 0; i < pck.Entries.Count; i++)
                {
                    var pckEntry = pck.Entries[i];

                    if (pckEntry.Offset > data.Length || pckEntry.Size > data.Length - pckEntry.Offset)
                    {
                        Console.WriteLine($"  Warning: skipping PCK member '{pckEntry.Name}' with out-of-bounds offset/size");
                        continue;
                    }

                    string stem = PckContainer.GetSafeMemberName(pckEntry.Name);
                    string typeHint = PckContainer.GetTypeHint(pckEntry.Attributes);
                    string ext = PckContainer.GetTypeOf(typeHint);
                    string fname = $"{stem}.{ext}";

                    if (usedNames.Contains(fname))
                        fname = $"{stem}.{i:D3}.{ext}";
                    usedNames.Add(fname);

                    string memberPath = Path.Combine(pckDir, fname);

                    byte[] memberData = new byte[pckEntry.Size];
                    Array.Copy(data, pckEntry.Offset, memberData, 0, (int)pckEntry.Size);

                    string sourcePath = memberPath;
                    bool memberIsTim2 = memberData.Length >= 4 &&
                                        memberData[0] == 'T' && memberData[1] == 'I' &&
                                        memberData[2] == 'M' && memberData[3] == '2';

                    if (!keepTim2 && memberIsTim2)
                    {
                        try
                        {
                            var tim2 = Tim2File.Read(memberData);
                            string pngPath = Path.ChangeExtension(memberPath, ".png");
                            var mipmaps = Tim2Converter.ToPng(tim2, pngPath);

                            var tim2Manifest = new Tim2Manifest
                            {
                                Source = Path.GetRelativePath(outputDir, pngPath).Replace('\\', '/'),
                                Width = tim2.Images[0].Header.ImageWidth,
                                Height = tim2.Images[0].Header.ImageHeight,
                                HasClut = tim2.Images[0].Header.ClutSize > 0,
                                ClutColors = tim2.Images[0].Header.ClutColors,
                                ClutType = tim2.Images[0].Header.ClutType,
                                OriginalSize = memberData.Length, 
                                
                                FileFormat = tim2.Header.Format,
                                TimVersion = tim2.Header.Version,
                                ImageType = tim2.Images[0].Header.ImageType,
                                MipMapTextures = tim2.Images[0].Header.MipMapTextures,
                                HeaderSize = tim2.Images[0].Header.HeaderSize,
                                PictFormat = tim2.Images[0].Header.PictFormat,
                                GsTex0 = tim2.Images[0].Header.GsTex0,
                                GsTex1 = tim2.Images[0].Header.GsTex1,
                                GsRegs = tim2.Images[0].Header.GsRegs,
                                GsTexClut = tim2.Images[0].Header.GsTexClut,

                                GsMiptbp1 = tim2.Images[0].GsMiptbp1,
                                GsMiptbp2 = tim2.Images[0].GsMiptbp2,
                                UserData = Convert.ToBase64String(tim2.Images[0].UserData),
                                Mipmaps = mipmaps
                            };

                            string tim2JsonPath = memberPath + ".json";
                            File.WriteAllText(tim2JsonPath, JsonConvert.SerializeObject(tim2Manifest, Formatting.Indented));
                            sourcePath = tim2JsonPath;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Warning: TIM2 conversion failed for {pckEntry.Name}: {ex.Message}");
                            File.WriteAllBytes(memberPath, memberData);
                        }
                    }
                    else
                    {
                        File.WriteAllBytes(memberPath, memberData);
                    }

                    pckManifest.Members.Add(new PckMemberManifest
                    {
                        Name = pckEntry.Name,
                        Attributes = pckEntry.Attributes,
                        Source = Path.GetRelativePath(outputDir, sourcePath).Replace('\\', '/'),
                        OriginalSize = (int)pckEntry.Size,
                        TypeHint = typeHint
                    });
                }

                string pckJsonPath = outFilePath + ".json";
                File.WriteAllText(pckJsonPath, JsonConvert.SerializeObject(pckManifest, Formatting.Indented));

                manifest.Files.Add(new VfiFileManifest
                {
                    Path = fullPath,
                    Source = Path.GetRelativePath(outputDir, pckJsonPath).Replace('\\', '/'),
                    Compressed = isCompressed,
                    OriginalSize = entry.Size
                });
            }
            else if (!keepTim2 && isTim2)
            {
                try
                {
                    var tim2 = Tim2File.Read(data);
                    string pngPath = Path.ChangeExtension(outFilePath, ".png");
                    var mipmaps = Tim2Converter.ToPng(tim2, pngPath);

                    var tim2Manifest = new Tim2Manifest
                    {
                        Source = Path.GetRelativePath(outputDir, pngPath).Replace('\\', '/'),
                        Width = tim2.Images[0].Header.ImageWidth,
                        Height = tim2.Images[0].Header.ImageHeight,
                        HasClut = tim2.Images[0].Header.ClutSize > 0,
                        ClutColors = tim2.Images[0].Header.ClutColors,
                        ClutType = tim2.Images[0].Header.ClutType,
                        OriginalSize = data.Length, 
                        
                        FileFormat = tim2.Header.Format,
                        TimVersion = tim2.Header.Version,
                        ImageType = tim2.Images[0].Header.ImageType,
                        MipMapTextures = tim2.Images[0].Header.MipMapTextures,
                        HeaderSize = tim2.Images[0].Header.HeaderSize,
                        PictFormat = tim2.Images[0].Header.PictFormat,
                        GsTex0 = tim2.Images[0].Header.GsTex0,
                        GsTex1 = tim2.Images[0].Header.GsTex1,
                        GsRegs = tim2.Images[0].Header.GsRegs,
                        GsTexClut = tim2.Images[0].Header.GsTexClut,

                        GsMiptbp1 = tim2.Images[0].GsMiptbp1,
                        GsMiptbp2 = tim2.Images[0].GsMiptbp2,
                        UserData = Convert.ToBase64String(tim2.Images[0].UserData),
                        Mipmaps = mipmaps
                    };

                    string tim2JsonPath = outFilePath + ".json";
                    File.WriteAllText(tim2JsonPath, JsonConvert.SerializeObject(tim2Manifest, Formatting.Indented));

                    manifest.Files.Add(new VfiFileManifest
                    {
                        Path = fullPath,
                        Source = Path.GetRelativePath(outputDir, tim2JsonPath).Replace('\\', '/'),
                        Compressed = isCompressed,
                        OriginalSize = entry.Size
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: TIM2 conversion failed for {fullPath}: {ex.Message}");
                    File.WriteAllBytes(outFilePath, data);
                    manifest.Files.Add(new VfiFileManifest
                    {
                        Path = fullPath,
                        Source = Path.GetRelativePath(outputDir, outFilePath).Replace('\\', '/'),
                        Compressed = isCompressed,
                        OriginalSize = entry.Size
                    });
                }
            }
            else
            {
                File.WriteAllBytes(outFilePath, data);
                manifest.Files.Add(new VfiFileManifest
                {
                    Path = fullPath,
                    Source = Path.GetRelativePath(outputDir, outFilePath).Replace('\\', '/'),
                    Compressed = isCompressed,
                    OriginalSize = entry.Size
                });
            }
        }

        manifest.DataOffSectors = container.Header.DataOffSectors;
        manifest.TotalSize = fs.Length;
        string manifestPath = Path.Combine(outputDir, "databin.manifest.json");
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        Console.WriteLine($"Extraction complete. Manifest written to {manifestPath}");
    }
}