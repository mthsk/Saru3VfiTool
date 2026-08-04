using System;
using System.Collections.Generic;
using System.IO;
using Saru3VfiTool.Commands;

namespace Saru3VfiTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            if (ShouldUseDragAndDropMode(args))
            {
                IndividualFileCommand.RunMany(args);
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "extract":
                    RunExtract(args);
                    break;
                case "rebuild":
                    RequireArgumentCount(args, 3, "rebuild <databin.manifest.json> <output.bin>");
                    RebuildCommand.Run(args[1], args[2]);
                    break;
                case "process":
                case "auto":
                case "convert":
                case "unpack":
                case "repack":
                    RequireArgumentCount(args, 2, $"{command} <input> [output]");
                    IndividualFileCommand.Run(args[1], args.Length >= 3 ? args[2] : null);
                    break;
                case "help":
                case "--help":
                case "-h":
                    PrintUsage();
                    break;
                default:
                    PrintUsage();
                    return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 2;
        }
    }

    private static bool ShouldUseDragAndDropMode(string[] args)
    {
        string first = args[0];
        if (File.Exists(first) || Directory.Exists(first))
            return true;

        // Shell drag-and-drop can pass several paths. If every argument exists,
        // treat all of them as independent auto-detected inputs.
        return args.Length > 1 && Array.TrueForAll(args, path => File.Exists(path) || Directory.Exists(path));
    }

    private static void RunExtract(string[] args)
    {
        bool keepPck = false;
        bool convertTim2 = false;
        bool keepSz = false;
        bool keepExdb = false;
        var positional = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--keep-pck":
                    keepPck = true;
                    break;
                case "--tim2":
                    convertTim2 = true;
                    break;
                case "--keep-sz":
                    keepSz = true;
                    break;
                case "--keep-exdb":
                    keepExdb = true;
                    break;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count < 2)
            throw new ArgumentException("Usage: extract [--tim2] [--keep-pck] [--keep-sz] [--keep-exdb] <DATA.BIN> <output_dir>");

        ExtractCommand.Run(positional[0], positional[1], keepPck, keepTim2: !convertTim2, keepSz: keepSz, keepExdb: keepExdb);
    }

    private static void RequireArgumentCount(string[] args, int minimum, string usage)
    {
        if (args.Length < minimum)
            throw new ArgumentException($"Usage: Saru3VfiTool {usage}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Saru3VfiTool - Ape Escape 3 / Saru! Get You! 3 (サルゲッチュ3)");
        Console.WriteLine("Build 2026.08.02");
        Console.WriteLine();
        Console.WriteLine("Drag one or more supported files/directories onto the executable, or use:");
        Console.WriteLine("  Saru3VfiTool process <input> [output]");
        Console.WriteLine("  Saru3VfiTool extract [--tim2] [--keep-pck] [--keep-sz] [--keep-exdb] <DATA.BIN> <output_dir>");
        Console.WriteLine("  Saru3VfiTool rebuild <databin.manifest.json> <output.bin>");
        Console.WriteLine();
        Console.WriteLine("Auto-detected round trips:");
        Console.WriteLine("  .tm2 <-> .tm2.json + PNG, .pck <-> .pck.json + directory");
        Console.WriteLine("  .sz <-> .sz.json + auto-converted payload, EXDB <-> .exdb.json");
        Console.WriteLine("  DATA.BIN/VFI <-> databin.manifest.json");
        Console.WriteLine();
        Console.WriteLine("License: GNU GPL v3");
    }
}
