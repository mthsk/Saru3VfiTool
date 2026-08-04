using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Saru3VfiTool.Compression;
using Saru3VfiTool.Exdb;
using Saru3VfiTool.Models;
using Saru3VfiTool.Pck;
using Saru3VfiTool.Subtitles;
using Saru3VfiTool.Tim2;

namespace Saru3VfiTool.Commands;

public static class IndividualFileCommand
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        FloatFormatHandling = FloatFormatHandling.String
    };

    public static void RunMany(IEnumerable<string> inputs)
    {
        foreach (string input in inputs)
        {
            Console.WriteLine($"Processing: {input}");
            Run(input);
        }
    }

    public static void Run(string inputPath, string? outputPath = null)
    {
        inputPath = Path.GetFullPath(inputPath);
        if (Directory.Exists(inputPath))
        {
            ProcessDirectory(inputPath, outputPath);
            return;
        }
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input does not exist", inputPath);

        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (extension == ".json")
        {
            ProcessJson(inputPath, outputPath);
            return;
        }
        if (extension == ".png")
        {
            ProcessPng(inputPath, outputPath);
            return;
        }
        if (extension == ".sz")
        {
            UnpackSz(inputPath, outputPath);
            return;
        }

        byte[] prefix = ReadPrefix(inputPath, 4);
        if (HasMagic(prefix, "TIM2"))
        {
            UnpackTim2(inputPath, outputPath);
            return;
        }
        if (prefix.Length >= 4 && prefix[0] == (byte)'P' && prefix[1] == (byte)'C' && prefix[2] == (byte)'K' && prefix[3] == 0)
        {
            UnpackPck(inputPath, outputPath);
            return;
        }
        if (prefix.Length >= 3 && prefix[0] == (byte)'E' && prefix[1] == (byte)'D' && prefix[2] == (byte)'B')
        {
            UnpackExdb(inputPath, outputPath);
            return;
        }
        if (SubtitleMagic.IsTextContainer(prefix))
        {
            UnpackSubtitleText(inputPath, outputPath);
            return;
        }
        if (SubtitleMagic.IsTimingContainer(prefix))
        {
            UnpackSubtitleTiming(inputPath, outputPath);
            return;
        }
        if (prefix.Length >= 4 && prefix[0] == (byte)'V' && prefix[1] == (byte)'F' && prefix[2] == (byte)'I' && prefix[3] == 0)
        {
            string extractionDir = outputPath ?? Path.Combine(
                Path.GetDirectoryName(inputPath)!, Path.GetFileNameWithoutExtension(inputPath) + ".extracted");
            ExtractCommand.Run(inputPath, extractionDir, keepPck: false, keepTim2: false,
                keepSz: false, keepExdb: false, keepTextBin: false, keepSbt: false);
            return;
        }

        throw new InvalidDataException($"Unsupported or unrecognized input type: {inputPath}");
    }

    private static void ProcessDirectory(string directoryPath, string? outputPath)
    {
        string dataManifest = Path.Combine(directoryPath, "databin.manifest.json");
        if (File.Exists(dataManifest))
        {
            RebuildCommand.Run(dataManifest, outputPath ?? Path.Combine(Path.GetDirectoryName(directoryPath)!, "DATA_REBUILT.BIN"));
            return;
        }

        if (directoryPath.EndsWith(".pck.d", StringComparison.OrdinalIgnoreCase))
        {
            string manifest = directoryPath[..^2] + ".json";
            if (File.Exists(manifest))
            {
                PackPck(manifest, outputPath);
                return;
            }
        }

        throw new InvalidDataException("Directory is not a DATA.BIN extraction or a .pck.d directory with a sibling manifest");
    }

    private static void ProcessJson(string manifestPath, string? outputPath)
    {
        string type = ReadJsonType(manifestPath);
        switch (type)
        {
            case "tim2":
                PackTim2(manifestPath, outputPath);
                break;
            case "pck":
                PackPck(manifestPath, outputPath);
                break;
            case "sz":
                PackSz(manifestPath, outputPath);
                break;
            case "exdb":
                PackExdb(manifestPath, outputPath);
                break;
            case "text-bin":
                PackSubtitleText(manifestPath, outputPath);
                break;
            case "subtitle-timing":
                PackSubtitleTiming(manifestPath, outputPath);
                break;
            case "vfimanifest":
                RebuildCommand.Run(manifestPath, outputPath ?? Path.Combine(Path.GetDirectoryName(manifestPath)!, "DATA_REBUILT.BIN"));
                break;
            default:
                throw new InvalidDataException($"Unsupported JSON sidecar type '{type}' in {manifestPath}");
        }
    }

    private static void ProcessPng(string pngPath, string? outputPath)
    {
        string sidecar = Path.Combine(
            Path.GetDirectoryName(pngPath)!, Path.GetFileNameWithoutExtension(pngPath) + ".tm2.json");
        if (!File.Exists(sidecar))
            throw new FileNotFoundException("PNG needs a sibling <name>.tm2.json sidecar", sidecar);
        PackTim2(sidecar, outputPath);
    }

    private static void UnpackTim2(string inputPath, string? outputPath)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        string manifestPath = outputPath ?? inputPath + ".json";
        string pngPath = Path.ChangeExtension(manifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? manifestPath[..^5]
            : manifestPath, ".png");

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        Tim2File tim2 = Tim2File.Read(data);
        Tim2Manifest manifest = CreateTim2Manifest(tim2, data.Length, pngPath, Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, JsonSettings));
        Console.WriteLine($"TIM2 -> {pngPath}");
        Console.WriteLine($"Sidecar -> {manifestPath}");
    }

    private static Tim2Manifest CreateTim2Manifest(Tim2File tim2, int originalSize, string pngPath, string manifestDirectory)
    {
        if (tim2.Images.Count == 0)
            throw new InvalidDataException("TIM2 contains no pictures");

        List<Tim2MipmapManifest> mipmaps = Tim2Converter.ToPng(tim2, pngPath);
        Tim2Image image = tim2.Images[0];
        return new Tim2Manifest
        {
            Source = RelativePath(manifestDirectory, pngPath),
            Width = image.Header.ImageWidth,
            Height = image.Header.ImageHeight,
            HasClut = image.Header.ClutSize > 0,
            ClutColors = image.Header.ClutColors,
            ClutType = image.Header.ClutType,
            OriginalSize = originalSize,
            FileFormat = tim2.Header.Format,
            TimVersion = tim2.Header.Version,
            ImageType = image.Header.ImageType,
            MipMapTextures = image.Header.MipMapTextures,
            HeaderSize = image.Header.HeaderSize,
            PictFormat = image.Header.PictFormat,
            GsTex0 = image.Header.GsTex0,
            GsTex1 = image.Header.GsTex1,
            GsRegs = image.Header.GsRegs,
            GsTexClut = image.Header.GsTexClut,
            GsMiptbp1 = image.GsMiptbp1,
            GsMiptbp2 = image.GsMiptbp2,
            UserData = Convert.ToBase64String(image.UserData),
            Mipmaps = mipmaps
        };
    }

    private static void PackTim2(string manifestPath, string? outputPath)
    {
        Tim2Manifest manifest = JsonConvert.DeserializeObject<Tim2Manifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid TIM2 manifest");
        string baseDir = Path.GetDirectoryName(manifestPath)!;
        string pngPath = Path.Combine(baseDir, manifest.Source.Replace('/', Path.DirectorySeparatorChar));
        byte[] data = Tim2Converter.WriteTim2(Tim2Converter.FromPng(pngPath, manifest));
        string target = outputPath ?? RemoveJsonSuffix(manifestPath);
        File.WriteAllBytes(target, data);
        Console.WriteLine($"TIM2 rebuilt: {target}");
    }

    private static void UnpackPck(string inputPath, string? outputPath)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        PckContainer pck = PckContainer.Read(data);
        string manifestPath = outputPath ?? inputPath + ".json";
        if (!manifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            manifestPath += ".json";
        string memberDirectory = manifestPath[..^5] + ".d";
        string manifestDirectory = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(memberDirectory);

        var manifest = new PckManifest();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pck.Entries.Count; i++)
        {
            PckEntry entry = pck.Entries[i];
            if (entry.Offset > data.Length || entry.Size > data.Length - entry.Offset)
                throw new InvalidDataException($"PCK member {i} is outside the file");

            string stem = PckContainer.GetSafeMemberName(entry.Name);
            string typeHint = PckContainer.GetTypeHint(entry.Attributes);
            string extension = PckContainer.GetTypeOf(typeHint);
            string fileName = $"{stem}.{extension}";
            if (!usedNames.Add(fileName))
            {
                fileName = $"{stem}.{i:D3}.{extension}";
                usedNames.Add(fileName);
            }

            string memberPath = Path.Combine(memberDirectory, fileName);
            byte[] memberData = new byte[entry.Size];
            Array.Copy(data, entry.Offset, memberData, 0, memberData.Length);
            string sourcePath = memberPath;

            if (HasMagic(memberData, "TIM2"))
            {
                try
                {
                    string tim2Json = memberPath + ".json";
                    string pngPath = Path.ChangeExtension(memberPath, ".png");
                    Tim2File tim2 = Tim2File.Read(memberData);
                    Tim2Manifest tim2Manifest = CreateTim2Manifest(tim2, memberData.Length, pngPath, Path.GetDirectoryName(tim2Json)!);
                    File.WriteAllText(tim2Json, JsonConvert.SerializeObject(tim2Manifest, JsonSettings));
                    sourcePath = tim2Json;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  TIM2 conversion skipped for {entry.Name}: {ex.Message}");
                    File.WriteAllBytes(memberPath, memberData);
                }
            }
            else if (HasMagic(memberData, "EDB"))
            {
                try
                {
                    string exdbJson = memberPath + ".json";
                    File.WriteAllText(exdbJson, JsonConvert.SerializeObject(ExdbConverter.Read(memberData), JsonSettings));
                    sourcePath = exdbJson;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  EXDB conversion skipped for {entry.Name}: {ex.Message}");
                    File.WriteAllBytes(memberPath, memberData);
                }
            }
            else if (SubtitleMagic.IsTextContainer(memberData))
            {
                try
                {
                    string subtitleJson = memberPath + ".json";
                    File.WriteAllText(subtitleJson, JsonConvert.SerializeObject(SubtitleTextConverter.Read(memberData), JsonSettings));
                    sourcePath = subtitleJson;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Text BIN conversion skipped for {entry.Name}: {ex.Message}");
                    File.WriteAllBytes(memberPath, memberData);
                }
            }
            else if (SubtitleMagic.IsTimingContainer(memberData))
            {
                try
                {
                    string subtitleJson = memberPath + ".json";
                    File.WriteAllText(subtitleJson, JsonConvert.SerializeObject(SubtitleTimingConverter.Read(memberData), JsonSettings));
                    sourcePath = subtitleJson;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Subtitle timing conversion skipped for {entry.Name}: {ex.Message}");
                    File.WriteAllBytes(memberPath, memberData);
                }
            }
            else
            {
                File.WriteAllBytes(memberPath, memberData);
            }

            manifest.Members.Add(new PckMemberManifest
            {
                Name = entry.Name,
                Attributes = entry.Attributes,
                Source = RelativePath(manifestDirectory, sourcePath),
                OriginalSize = memberData.Length,
                TypeHint = typeHint
            });
        }

        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, JsonSettings));
        Console.WriteLine($"PCK unpacked: {memberDirectory}");
        Console.WriteLine($"Sidecar -> {manifestPath}");
    }

    private static void PackPck(string manifestPath, string? outputPath)
    {
        PckManifest manifest = JsonConvert.DeserializeObject<PckManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid PCK manifest");
        string baseDir = Path.GetDirectoryName(manifestPath)!;
        var members = new List<(string name, string attributes, byte[] data)>();

        foreach (PckMemberManifest member in manifest.Members)
        {
            string source = Path.Combine(baseDir, member.Source.Replace('/', Path.DirectorySeparatorChar));
            members.Add((member.Name, member.Attributes, BuildSourceData(source)));
        }

        string target = outputPath ?? RemoveJsonSuffix(manifestPath);
        File.WriteAllBytes(target, PckContainer.Rebuild(members));
        Console.WriteLine($"PCK rebuilt: {target}");
    }

    private static void UnpackSz(string inputPath, string? outputPath)
    {
        byte[] compressed = File.ReadAllBytes(inputPath);
        byte[] data = SzCompression.Decompress(compressed);
        string payloadPath = outputPath ?? inputPath[..^3];
        if (string.IsNullOrWhiteSpace(Path.GetFileName(payloadPath)))
            payloadPath = inputPath + ".decompressed";
        string manifestPath = inputPath + ".json";
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        File.WriteAllBytes(payloadPath, data);

        string sourcePath = payloadPath;
        try
        {
            sourcePath = ConvertNestedPayload(payloadPath, data) ?? payloadPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Nested conversion skipped: {ex.Message}");
        }

        var manifest = new SzManifest
        {
            Source = RelativePath(Path.GetDirectoryName(manifestPath)!, sourcePath),
            OriginalCompressedSize = compressed.Length,
            DecompressedSize = data.Length
        };
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, JsonSettings));
        Console.WriteLine($"SZ decompressed: {payloadPath}");
        Console.WriteLine($"Sidecar -> {manifestPath}");
    }

    private static void PackSz(string manifestPath, string? outputPath)
    {
        SzManifest manifest = JsonConvert.DeserializeObject<SzManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid SZ manifest");
        string source = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Source.Replace('/', Path.DirectorySeparatorChar));
        string target = outputPath ?? RemoveJsonSuffix(manifestPath);
        File.WriteAllBytes(target, SzCompression.Compress(BuildSourceData(source)));
        Console.WriteLine($"SZ rebuilt: {target}");
    }

    private static void UnpackExdb(string inputPath, string? outputPath)
    {
        ExdbDocument document = ExdbConverter.Read(File.ReadAllBytes(inputPath));
        string target = outputPath ?? inputPath + ".json";
        File.WriteAllText(target, JsonConvert.SerializeObject(document, JsonSettings));
        Console.WriteLine($"EXDB -> JSON: {target}");
    }

    private static void PackExdb(string manifestPath, string? outputPath)
    {
        ExdbDocument document = JsonConvert.DeserializeObject<ExdbDocument>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid EXDB JSON");
        string target = outputPath ?? RemoveJsonSuffix(manifestPath);
        File.WriteAllBytes(target, ExdbConverter.Write(document));
        Console.WriteLine($"EXDB rebuilt: {target}");
    }

    private static void UnpackSubtitleText(string inputPath, string? outputPath)
    {
        SubtitleTextDocument document = SubtitleTextConverter.Read(File.ReadAllBytes(inputPath));
        string target = outputPath ?? inputPath + ".json";
        File.WriteAllText(target, JsonConvert.SerializeObject(document, JsonSettings));
        Console.WriteLine($"Text BIN -> JSON: {target}");
    }

    private static void PackSubtitleText(string manifestPath, string? outputPath)
    {
        SubtitleTextDocument document = JsonConvert.DeserializeObject<SubtitleTextDocument>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid text BIN JSON");
        string target = outputPath ?? RemoveJsonSuffix(manifestPath);
        File.WriteAllBytes(target, SubtitleTextConverter.Write(document));
        Console.WriteLine($"Text BIN rebuilt: {target}");
    }

    private static void UnpackSubtitleTiming(string inputPath, string? outputPath)
    {
        SubtitleTimingDocument document = SubtitleTimingConverter.Read(File.ReadAllBytes(inputPath));
        string target = outputPath ?? inputPath + ".json";
        File.WriteAllText(target, JsonConvert.SerializeObject(document, JsonSettings));
        Console.WriteLine($"Subtitle timing -> JSON: {target}");
    }

    private static void PackSubtitleTiming(string manifestPath, string? outputPath)
    {
        SubtitleTimingDocument document = JsonConvert.DeserializeObject<SubtitleTimingDocument>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid subtitle timing JSON");
        string target = outputPath ?? RemoveJsonSuffix(manifestPath);
        File.WriteAllBytes(target, SubtitleTimingConverter.Write(document));
        Console.WriteLine($"Subtitle timing rebuilt: {target}");
    }


    private static string? ConvertNestedPayload(string payloadPath, byte[] data)
    {
        if (HasMagic(data, "TIM2"))
        {
            UnpackTim2(payloadPath, null);
            return payloadPath + ".json";
        }
        if (data.Length >= 4 && data[0] == (byte)'P' && data[1] == (byte)'C' && data[2] == (byte)'K' && data[3] == 0)
        {
            UnpackPck(payloadPath, null);
            return payloadPath + ".json";
        }
        if (HasMagic(data, "EDB"))
        {
            UnpackExdb(payloadPath, null);
            return payloadPath + ".json";
        }
        if (SubtitleMagic.IsTextContainer(data))
        {
            UnpackSubtitleText(payloadPath, null);
            return payloadPath + ".json";
        }
        if (SubtitleMagic.IsTimingContainer(data))
        {
            UnpackSubtitleTiming(payloadPath, null);
            return payloadPath + ".json";
        }
        if (data.Length >= 4 && data[0] == (byte)'V' && data[1] == (byte)'F' && data[2] == (byte)'I' && data[3] == 0)
        {
            string extractionDir = payloadPath + ".extracted";
            ExtractCommand.Run(payloadPath, extractionDir, keepPck: false, keepTim2: false,
                keepSz: false, keepExdb: false, keepTextBin: false, keepSbt: false);
            return Path.Combine(extractionDir, "databin.manifest.json");
        }
        return null;
    }

    private static byte[] BuildSourceData(string sourcePath)
    {
        if (!sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllBytes(sourcePath);

        return TryReadJsonType(sourcePath) switch
        {
            "tim2" => BuildTim2Data(sourcePath),
            "pck" => BuildPckData(sourcePath),
            "sz" => BuildSzData(sourcePath),
            "exdb" => BuildExdbData(sourcePath),
            "text-bin" => BuildSubtitleTextData(sourcePath),
            "subtitle-timing" => BuildSubtitleTimingData(sourcePath),
            "vfimanifest" => BuildVfiData(sourcePath),
            string type => throw new InvalidDataException($"Unsupported nested JSON sidecar type '{type}' in {sourcePath}")
        };
    }

    private static byte[] BuildTim2Data(string manifestPath)
    {
        Tim2Manifest manifest = JsonConvert.DeserializeObject<Tim2Manifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid TIM2 sidecar: {manifestPath}");
        string png = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Source.Replace('/', Path.DirectorySeparatorChar));
        return Tim2Converter.WriteTim2(Tim2Converter.FromPng(png, manifest));
    }

    private static byte[] BuildPckData(string manifestPath)
    {
        PckManifest manifest = JsonConvert.DeserializeObject<PckManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid PCK sidecar: {manifestPath}");
        string baseDir = Path.GetDirectoryName(manifestPath)!;
        var members = new List<(string name, string attributes, byte[] data)>();
        foreach (PckMemberManifest member in manifest.Members)
        {
            string source = Path.Combine(baseDir, member.Source.Replace('/', Path.DirectorySeparatorChar));
            members.Add((member.Name, member.Attributes, BuildSourceData(source)));
        }
        return PckContainer.Rebuild(members);
    }

    private static byte[] BuildSzData(string manifestPath)
    {
        SzManifest manifest = JsonConvert.DeserializeObject<SzManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid SZ sidecar: {manifestPath}");
        string source = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Source.Replace('/', Path.DirectorySeparatorChar));
        return SzCompression.Compress(BuildSourceData(source));
    }

    private static byte[] BuildExdbData(string manifestPath)
    {
        ExdbDocument document = JsonConvert.DeserializeObject<ExdbDocument>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid EXDB sidecar: {manifestPath}");
        return ExdbConverter.Write(document);
    }

    private static byte[] BuildSubtitleTextData(string manifestPath)
    {
        SubtitleTextDocument document = JsonConvert.DeserializeObject<SubtitleTextDocument>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid text BIN document: {manifestPath}");
        return SubtitleTextConverter.Write(document);
    }

    private static byte[] BuildSubtitleTimingData(string manifestPath)
    {
        SubtitleTimingDocument document = JsonConvert.DeserializeObject<SubtitleTimingDocument>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException($"Invalid subtitle timing sidecar: {manifestPath}");
        return SubtitleTimingConverter.Write(document);
    }

    private static byte[] BuildVfiData(string manifestPath)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"Saru3VfiTool-{Guid.NewGuid():N}.bin");
        try
        {
            RebuildCommand.Run(manifestPath, tempPath);
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string ReadJsonType(string path)
    {
        JObject root = JObject.Parse(File.ReadAllText(path));
        return root.Value<string>("type")?.Trim().ToLowerInvariant() ?? "";
    }

    private static string TryReadJsonType(string path)
    {
        try
        {
            return ReadJsonType(path);
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static byte[] ReadPrefix(string path, int count)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] prefix = new byte[(int)Math.Min(stream.Length, count)];
        _ = stream.Read(prefix, 0, prefix.Length);
        return prefix;
    }

    private static bool HasMagic(IReadOnlyList<byte> data, string magic)
    {
        if (data.Count < magic.Length)
            return false;
        for (int i = 0; i < magic.Length; i++)
            if (data[i] != (byte)magic[i])
                return false;
        return true;
    }

    private static string RelativePath(string baseDirectory, string path) =>
        Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');

    private static string RemoveJsonSuffix(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path[..^5] : path;
}
