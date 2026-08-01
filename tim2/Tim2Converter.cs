using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Saru3VfiTool.Models;
using SkiaSharp;

namespace Saru3VfiTool.Tim2;

public static class Tim2Converter
{
    private const int RGBA16 = 1;
    private const int RGB24 = 2;
    private const int RGBA32 = 3;
    private const int IDTEX4 = 4;
    private const int IDTEX8 = 5;

    public static string GetFormatName(byte imageType) => imageType switch
    {
        RGBA32 => "Psmct32",
        RGB24 => "Psmct24",
        RGBA16 => "Psmct16",
        IDTEX8 => "Psmt8",
        IDTEX4 => "Psmt4",
        _ => $"ImageType{imageType}"
    };

    public static List<Tim2MipmapManifest> ToPng(Tim2File tim2, string basePath)
    {
        if (tim2.Images.Count == 0)
            throw new InvalidDataException("TIM2 has no images");

        var img = tim2.Images[0];
        var manifests = new List<Tim2MipmapManifest>();

        uint[]? clut = null;
        if (img.Header.ImageType == IDTEX4 || img.Header.ImageType == IDTEX8)
        {
            if (img.Header.ClutSize == 0)
                throw new InvalidDataException("Indexed picture has no CLUT");
            clut = DecodeClut(img);
        }

        int currentByteOffset = 0;

        for (int level = 0; level < img.Header.MipMapTextures; level++)
        {
            int w = Math.Max(1, img.Header.ImageWidth >> level);
            int h = Math.Max(1, img.Header.ImageHeight >> level);

            string ext = Path.GetExtension(basePath);
            string outputPath = level == 0 
                ? basePath 
                : basePath[..^ext.Length] + $".mip{level:D2}{ext}";

            ExtractLevelToPng(img, currentByteOffset, w, h, clut, outputPath);

            manifests.Add(new Tim2MipmapManifest
            {
                Source = Path.GetFileName(outputPath),
                Level = level,
                Width = w,
                Height = h
            });

            // The offset for the next mipmap is stored in MMImageSize
            if (level < img.MMImageSize.Length)
            {
                currentByteOffset += (int)img.MMImageSize[level];
            }
        }

        return manifests;
    }

    private static void ExtractLevelToPng(Tim2Image img, int byteOffset, int w, int h, uint[]? clut, string outputPath)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        unsafe
        {
            uint* pixels = (uint*)bitmap.GetPixels();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = GetPixel(img, byteOffset, x, y, w, clut);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    private static uint GetPixel(Tim2Image img, int byteOffset, int x, int y, int w, uint[]? clut)
    {
        int idx = y * w + x;
        int itype = img.Header.ImageType;

        if (itype == IDTEX8)
        {
            if (byteOffset + idx >= img.ImageData.Length) return 0xFF00FF00;
            byte clutIdx = img.ImageData[byteOffset + idx];
            return clut != null && clutIdx < clut.Length ? clut[clutIdx] : 0xFF00FF00;
        }
        else if (itype == IDTEX4)
        {
            int totalNibble = (byteOffset * 2) + idx;
            int byteIdx = totalNibble / 2;
            if (byteIdx >= img.ImageData.Length) return 0xFF00FF00;
            byte b = img.ImageData[byteIdx];
            int nibble = (totalNibble % 2 == 0) ? (b & 0x0F) : (b >> 4);
            return clut != null && nibble < clut.Length ? clut[nibble] : 0xFF00FF00;
        }
        else if (itype == RGBA32)
        {
            int off = byteOffset + (idx * 4);
            if (off + 4 > img.ImageData.Length) return 0xFF00FF00;
            byte r = img.ImageData[off + 0];
            byte g = img.ImageData[off + 1];
            byte b = img.ImageData[off + 2];
            byte a = ScaleAlpha(img.ImageData[off + 3]);
            return (uint)((a << 24) | (r << 16) | (g << 8) | b);
        }
        else if (itype == RGB24)
        {
            int off = byteOffset + (idx * 3);
            if (off + 3 > img.ImageData.Length) return 0xFF00FF00;
            byte r = img.ImageData[off + 0];
            byte g = img.ImageData[off + 1];
            byte b = img.ImageData[off + 2];
            return (uint)(0xFF000000 | (r << 16) | (g << 8) | b);
        }
        else if (itype == RGBA16)
        {
            int off = byteOffset + (idx * 2);
            if (off + 2 > img.ImageData.Length) return 0xFF00FF00;
            ushort v = (ushort)(img.ImageData[off] | (img.ImageData[off + 1] << 8));
            return DecodeRgba16(v);
        }

        return 0xFFFF00FF;
    }

    private static byte ScaleAlpha(byte v) => v >= 0x80 ? (byte)255 : (byte)(v * 255 / 128);

    private static int Csm1Index(int i) => (i & 0xE7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);

    private static uint[] DecodeClut(Tim2Image img)
    {
        int ncol = img.Header.ClutColors;
        byte ctype = img.Header.ClutType;
        bool csm2 = (ctype & 0x80) != 0;
        int kind = ctype & 0x3F;

        uint[] pal = new uint[ncol];
        for (int i = 0; i < ncol; i++)
        {
            if (kind == RGBA32)
            {
                int off = i * 4;
                byte a = ScaleAlpha(img.ClutData[off + 3]);
                pal[i] = (uint)((a << 24) | (img.ClutData[off + 0] << 16) | (img.ClutData[off + 1] << 8) | img.ClutData[off + 2]);
            }
            else if (kind == RGB24)
            {
                int off = i * 3;
                pal[i] = (uint)(0xFF000000 | (img.ClutData[off + 0] << 16) | (img.ClutData[off + 1] << 8) | img.ClutData[off + 2]);
            }
            else if (kind == RGBA16)
            {
                int off = i * 2;
                ushort v = (ushort)(img.ClutData[off] | (img.ClutData[off + 1] << 8));
                pal[i] = DecodeRgba16(v);
            }
            else pal[i] = 0xFFFF00FF;
        }

        if (!csm2 && ncol == 256)
        {
            uint[] swizzled = new uint[256];
            for (int i = 0; i < 256; i++) swizzled[i] = pal[Csm1Index(i)];
            return swizzled;
        }

        return pal;
    }

    private static uint DecodeRgba16(ushort v)
    {
        int r = (v & 0x1F) << 3;
        int g = ((v >> 5) & 0x1F) << 3;
        int b = ((v >> 10) & 0x1F) << 3;
        uint a = (v & 0x8000) != 0 ? 255u : 0u;
        return (a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    public static int GetMipMapPictureSize(byte imageType, int w, int h)
    {
        int n = w * h;
        switch (imageType)
        {
            case RGBA32: n *= 4; break;
            case RGB24: n *= 3; break;
            case RGBA16: n *= 2; break;
            case IDTEX4: n = (n + 1) / 2; break; // odd widths handled safely
            case IDTEX8: break;
        }
        return (n + 15) & ~15; // 16-byte aligned
    }

    public static Tim2File FromPng(string basePath, Tim2Manifest manifest)
    {
        int formatType = manifest.ImageType; 
        
        var filesToLoad = new List<(string path, int level, int w, int h)>
        {
            (basePath, 0, manifest.Width, manifest.Height)
        };
        
        if (manifest.Mipmaps != null)
        {
            foreach (var mip in manifest.Mipmaps.OrderBy(m => m.Level))
            {
                if (mip.Level == 0) continue;
                filesToLoad.Add((Path.Combine(Path.GetDirectoryName(basePath)!, mip.Source), mip.Level, mip.Width, mip.Height));
            }
        }

        var bitmaps = new List<SKBitmap>();
        foreach (var (path, _, _, _) in filesToLoad)
        {
             var b = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Missing PNG: {path}");
             bitmaps.Add(b);
        }

        var file = new Tim2File
        {
            Header = new Tim2Header
            {
                Magic = 0x324D4954,
                Version = manifest.TimVersion > 0 ? manifest.TimVersion : (byte)4,
                Format = manifest.FileFormat,
                ImageCount = 1,
                Reserved = new byte[8]
            }
        };

        var img = new Tim2Image();
        byte[] userDataBytes = string.IsNullOrEmpty(manifest.UserData) ? [] : Convert.FromBase64String(manifest.UserData);

        int totalMipmaps = bitmaps.Count;
        int[] mmSizes = [0, 0, 32, 32, 32, 48, 48, 48];
        int mmHeaderSize = totalMipmaps < 8 ? mmSizes[totalMipmaps] : 0;
        
        var imgHeader = new Tim2PictureHeader
        {
            ImageWidth = (ushort)manifest.Width,
            ImageHeight = (ushort)manifest.Height,
            PictFormat = manifest.PictFormat,
            ImageType = (byte)formatType,
            HeaderSize = (ushort)(48 + mmHeaderSize + userDataBytes.Length),
            MipMapTextures = (byte)totalMipmaps,
            GsTex0 = manifest.GsTex0,
            GsTex1 = manifest.GsTex1,
            GsRegs = manifest.GsRegs,
            GsTexClut = manifest.GsTexClut
        };

        img.GsMiptbp1 = manifest.GsMiptbp1;
        img.GsMiptbp2 = manifest.GsMiptbp2;
        img.UserData = userDataBytes;
        img.MMImageSize = new uint[Math.Max(0, totalMipmaps - 1)];

        using var allImageData = new MemoryStream();
        
        // Palette extraction for ALL mipmap levels so they share uniform colors
        if (formatType == IDTEX8 || formatType == IDTEX4)
        {
            int maxColors = formatType == IDTEX8 ? 256 : 16;
            var exactColors = new HashSet<uint>();

            foreach (var bmp in bitmaps)
            {
                unsafe
                {
                    uint* p = (uint*)bmp.GetPixels();
                    for (int i = 0; i < bmp.Width * bmp.Height; i++)
                        exactColors.Add(p[i] & 0xFCFCFCFC);
                }
            }

            if (exactColors.Count > maxColors)
            {
                Console.WriteLine($"WARNING: {basePath} + mipmaps have {exactColors.Count} unique colors but {GetFormatName(manifest.ImageType)} only supports {maxColors}. " +
                                  $"The image will be quantized and may look wrong.");
            }
            
            var colorMap = new Dictionary<uint, byte>();
            var palette = new uint[maxColors];
            int palIdx = 0;

            for (int lvl = 0; lvl < bitmaps.Count; lvl++)
            {
                var bmp = bitmaps[lvl];
                int w = bmp.Width;
                int h = bmp.Height;
                int rawByteSize = GetMipMapPictureSize((byte)formatType, w, h);
                byte[] levelData = new byte[rawByteSize];

                unsafe
                {
                    uint* pixels = (uint*)bmp.GetPixels();
                    for (int i = 0; i < w * h; i++)
                    {
                        uint p = pixels[i] & 0xFCFCFCFC;
                        if (!colorMap.TryGetValue(p, out byte c))
                        {
                            if (palIdx >= maxColors) c = (byte)FindNearestColor(p, palette, palIdx);
                            else
                            {
                                c = (byte)palIdx;
                                colorMap[p] = c;
                                palette[palIdx] = pixels[i];
                                palIdx++;
                            }
                        }

                        if (formatType == IDTEX8)
                        {
                            levelData[i] = c;
                        }
                        else
                        {
                            int byteIdx = i / 2;
                            if (i % 2 == 0) levelData[byteIdx] = (byte)((levelData[byteIdx] & 0xF0) | c);
                            else levelData[byteIdx] = (byte)((levelData[byteIdx] & 0x0F) | (c << 4));
                        }
                    }
                }
                
                allImageData.Write(levelData, 0, levelData.Length);
                if (lvl < totalMipmaps - 1)
                {
                    img.MMImageSize[lvl] = (uint)levelData.Length;
                }
            }

            imgHeader.ClutColors = (ushort)maxColors;
            imgHeader.ClutType = manifest.ClutType > 0 ? (byte)manifest.ClutType : (byte)RGBA32;
            int clutEntrySize = (imgHeader.ClutType & 0x3F) switch { RGBA32 => 4, RGB24 => 3, RGBA16 => 2, _ => 4 };

            imgHeader.ClutSize = (uint)(maxColors * clutEntrySize);
            img.ClutData = new byte[maxColors * clutEntrySize];
            bool needsSwizzle = (imgHeader.ClutType & 0x80) == 0 && imgHeader.ClutColors == 256;

            for (int i = 0; i < maxColors; i++)
            {
                int destIdx = needsSwizzle ? Csm1Index(i) : i;
                uint p = palette[i];
                int off = destIdx * clutEntrySize;

                if (clutEntrySize == 4)
                {
                    img.ClutData[off + 0] = (byte)((p >> 16) & 0xFF);
                    img.ClutData[off + 1] = (byte)((p >> 8) & 0xFF);
                    img.ClutData[off + 2] = (byte)(p & 0xFF);
                    byte a = (byte)((p >> 24) & 0xFF);
                    img.ClutData[off + 3] = a >= 255 ? (byte)0x80 : (byte)(a * 128 / 255);
                }
                else if (clutEntrySize == 3)
                {
                    img.ClutData[off + 0] = (byte)((p >> 16) & 0xFF);
                    img.ClutData[off + 1] = (byte)((p >> 8) & 0xFF);
                    img.ClutData[off + 2] = (byte)(p & 0xFF);
                }
                else if (clutEntrySize == 2)
                {
                    ushort c = ConvertArgbToRgb5551(p);
                    img.ClutData[off + 0] = (byte)(c & 0xFF);
                    img.ClutData[off + 1] = (byte)(c >> 8);
                }
            }
        }
        else // Raw RGB handling (RGBA32, RGB24, RGBA16)
        {
            for (int lvl = 0; lvl < bitmaps.Count; lvl++)
            {
                var bmp = bitmaps[lvl];
                int w = bmp.Width;
                int h = bmp.Height;
                int rawByteSize = GetMipMapPictureSize((byte)formatType, w, h);
                byte[] levelData = new byte[rawByteSize];
                
                unsafe
                {
                    uint* pixels = (uint*)bmp.GetPixels();
                    if (formatType == RGBA32)
                    {
                        for (int i = 0; i < w * h; i++)
                        {
                            uint p = pixels[i];
                            levelData[i * 4 + 0] = (byte)((p >> 16) & 0xFF);
                            levelData[i * 4 + 1] = (byte)((p >> 8) & 0xFF);
                            levelData[i * 4 + 2] = (byte)(p & 0xFF);
                            byte a = (byte)((p >> 24) & 0xFF);
                            levelData[i * 4 + 3] = a >= 255 ? (byte)0x80 : (byte)(a * 128 / 255);
                        }
                    }
                    else if (formatType == RGB24)
                    {
                        for (int i = 0; i < w * h; i++)
                        {
                            uint p = pixels[i];
                            levelData[i * 3 + 0] = (byte)((p >> 16) & 0xFF);
                            levelData[i * 3 + 1] = (byte)((p >> 8) & 0xFF);
                            levelData[i * 3 + 2] = (byte)(p & 0xFF);
                        }
                    }
                    else if (formatType == RGBA16)
                    {
                        for (int i = 0; i < w * h; i++)
                        {
                            uint p = pixels[i];
                            ushort c = ConvertArgbToRgb5551(p);
                            levelData[i * 2 + 0] = (byte)(c & 0xFF);
                            levelData[i * 2 + 1] = (byte)(c >> 8);
                        }
                    }
                }
                
                allImageData.Write(levelData, 0, levelData.Length);
                if (lvl < totalMipmaps - 1)
                {
                    img.MMImageSize[lvl] = (uint)levelData.Length;
                }
            }
            imgHeader.ClutSize = 0;
            imgHeader.ClutColors = 0;
            imgHeader.ClutType = 0;
        }

        img.ImageData = allImageData.ToArray();
        imgHeader.ImageSize = (uint)img.ImageData.Length;
        imgHeader.TotalSize = (uint)(imgHeader.HeaderSize + imgHeader.ImageSize + imgHeader.ClutSize);
        
        img.Header = imgHeader;
        file.Images.Add(img);
        return file;
    }

    private static ushort ConvertArgbToRgb5551(uint p)
    {
        byte a = (byte)((p >> 24) & 0xFF);
        byte r = (byte)((p >> 16) & 0xFF);
        byte g = (byte)((p >> 8) & 0xFF);
        byte b = (byte)(p & 0xFF);

        int a1 = a > 127 ? 1 : 0;
        int r5 = (r * 31) / 255;
        int g5 = (g * 31) / 255;
        int b5 = (b * 31) / 255;

        return (ushort)((a1 << 15) | (b5 << 10) | (g5 << 5) | r5);
    }

    private static int FindNearestColor(uint target, uint[] palette, int count)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        byte ta = (byte)(target >> 24), tr = (byte)(target >> 16), tg = (byte)(target >> 8), tb = (byte)target;

        for (int i = 0; i < count; i++)
        {
            uint p = palette[i];
            int da = ta - (byte)(p >> 24), dr = tr - (byte)(p >> 16), dg = tg - (byte)(p >> 8), db = tb - (byte)p;
            int dist = da * da + dr * dr + dg * dg + db * db;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    public static byte[] WriteTim2(Tim2File file)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(file.Header.Magic);
        bw.Write(file.Header.Version);
        bw.Write(file.Header.Format);
        bw.Write(file.Header.ImageCount);
        bw.Write(file.Header.Reserved);

        // Standard alignment check for TIM2 Format zero vs non-zero
        long paddingTarget = file.Header.Format == 0 ? 0x10 : 0x80;
        while (ms.Position < paddingTarget) bw.Write((byte)0);

        foreach (var img in file.Images)
        {
            long startOfImage = ms.Position;
            WriteStruct(bw, img.Header);

            if (img.Header.MipMapTextures > 1)
            {
                bw.Write(img.GsMiptbp1);
                bw.Write(img.GsMiptbp2);
                for (int m = 0; m < img.MMImageSize.Length; m++)
                {
                    bw.Write(img.MMImageSize[m]);
                }
                
                int[] mmSizes = [0, 0, 32, 32, 32, 48, 48, 48];
                int expectedMmSize = img.Header.MipMapTextures < 8 ? mmSizes[img.Header.MipMapTextures] : 0;
                
                // Pad the rest of the mipmap header
                int written = 16 + (img.MMImageSize.Length * 4);
                while (written < expectedMmSize)
                {
                    bw.Write((byte)0);
                    written++;
                }
            }

            if (img.UserData != null && img.UserData.Length > 0)
                bw.Write(img.UserData);

            // Safety padding to exact header size required
            long imgDataStart = startOfImage + img.Header.HeaderSize;
            while (ms.Position < imgDataStart) bw.Write((byte)0);

            bw.Write(img.ImageData);
            if (img.ClutData.Length > 0)
                bw.Write(img.ClutData);

            // Pad image block to 16-bytes
            long expectedNextImage = startOfImage + img.Header.TotalSize;
            expectedNextImage = (expectedNextImage + 15) & ~15;
            while (ms.Position < expectedNextImage) bw.Write((byte)0);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteStruct<T>(BinaryWriter bw, T obj) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] bytes = new byte[size];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Marshal.StructureToPtr(obj, handle.AddrOfPinnedObject(), false); }
        finally { handle.Free(); }
        bw.Write(bytes);
    }
}