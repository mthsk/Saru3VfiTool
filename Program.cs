using System;
using System.Collections.Generic;
using Saru3VfiTool.Commands;

namespace Saru3VfiTool;

class Program
{
    static void Main(string[] args)
    {
        bool keepPck = false;
        bool keepTim2 = true;
        bool keepSz = false;
        var positional = new List<string>();

        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--keep-pck": keepPck = true; break;
                case "--tim2": keepTim2 = false; break;
                case "--keep-sz": keepSz = true; break;
                default: positional.Add(arg); break;
            }
        }

        if (positional.Count < 1)
        {
            PrintUsage();
            return;
        }

        string command = positional[0].ToLowerInvariant();

        try
        {
            switch (command)
            {
                case "extract":
                    if (positional.Count < 3)
                    {
                        Console.WriteLine("Usage: extract [--tim2] [--keep-pck] [--keep-sz] <DATA.BIN> <output_dir>");
                        return;
                    }
                    ExtractCommand.Run(positional[1], positional[2], keepPck, keepTim2, keepSz);
                    break;

                case "rebuild":
                    if (positional.Count < 3)
                    {
                        Console.WriteLine("Usage: rebuild <databin.manifest.json> <output.bin>");
                        return;
                    }
                    RebuildCommand.Run(positional[1], positional[2]);
                    break;

                default:
                    PrintUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Ape Escape 3 VFI Tool");
        Console.WriteLine("Build 2026.08.01");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Saru3VfiTool extract [--tim2] [--keep-pck] [--keep-sz] <DATA.BIN> <output_dir>");
        Console.WriteLine("  Saru3VfiTool rebuild <databin.manifest.json> <output.bin>");
        Console.WriteLine();
        Console.WriteLine("Extract: Decompresses .sz, expands .pck with .pck.json metadata,");
        Console.WriteLine("         --tim2 : (Experimental) Converts all TIM2 files to PNG with .json sidecars");
        Console.WriteLine("         --keep-pck  : Leave .pck files unexpanded");
        Console.WriteLine("         --keep-sz   : Leave .sz files compressed (opaque blobs)");
        Console.WriteLine("Rebuild:  Reads manifest and all .json sidecars to reconstruct DATA.BIN.");
        Console.WriteLine();
        Console.WriteLine("License: GNU GPL v3 (see LICENSE / https://www.gnu.org/licenses/gpl-3.0.html)");
    }
}