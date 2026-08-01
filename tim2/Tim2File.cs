using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Saru3VfiTool.Tim2;

public enum Tim2Psm : byte
{
    Psmct32 = 0x00,
    Psmct24 = 0x01,
    Psmct16 = 0x02,
    Psmt8   = 0x13,
    Psmt4   = 0x14,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Tim2Header
{
    public uint Magic;      // 'TIM2'
    public byte Version;    // 0x04
    public byte Format;     // flags
    public ushort ImageCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Tim2PictureHeader
{
    public uint TotalSize;
    public uint ClutSize;
    public uint ImageSize;
    public ushort HeaderSize;
    public ushort ClutColors;
    public byte PictFormat;
    public byte MipMapTextures;
    public byte ClutType;
    public byte ImageType;
    public ushort ImageWidth;
    public ushort ImageHeight;
    public ulong GsTex0;
    public ulong GsTex1;
    public uint GsRegs;
    public uint GsTexClut;
}

public class Tim2Image
{
    public Tim2PictureHeader Header { get; set; }
    public byte[] ImageData { get; set; } = [];
    public byte[] ClutData { get; set; } = [];
    
    // Preserved hardware extensions
    public ulong GsMiptbp1 { get; set; }
    public ulong GsMiptbp2 { get; set; }
    public uint[] MMImageSize { get; set; } = [];
    public byte[] UserData { get; set; } = [];
}

public class Tim2File
{
    public Tim2Header Header { get; set; }
    public List<Tim2Image> Images { get; set; } = [];

    public static Tim2File Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var file = new Tim2File
        {
            Header = ReadStruct<Tim2Header>(br)
        };

        if (file.Header.Magic != 0x324D4954)
            throw new InvalidDataException("Invalid TIM2 magic");

        // Align to 0x10 or 0x80 based on Format
        long paddingTarget = file.Header.Format == 0 ? 0x10 : 0x80;
        if (ms.Position < paddingTarget)
            ms.Position = paddingTarget;

        int standardHeaderSize = Marshal.SizeOf<Tim2PictureHeader>(); // 48 bytes
        int[] mmSizes = [0, 0, 32, 32, 32, 48, 48, 48];

        for (int i = 0; i < file.Header.ImageCount; i++)
        {
            long startOfImage = ms.Position;
            var img = new Tim2Image
            {
                Header = ReadStruct<Tim2PictureHeader>(br)
            };

            // Parse Mipmap Header if present
            int mmHeaderSize = img.Header.MipMapTextures < 8 ? mmSizes[img.Header.MipMapTextures] : 0;
            if (mmHeaderSize > 0)
            {
                ms.Position = startOfImage + standardHeaderSize;
                img.GsMiptbp1 = br.ReadUInt64();
                img.GsMiptbp2 = br.ReadUInt64();
                
                int mmCount = img.Header.MipMapTextures - 1;
                img.MMImageSize = new uint[mmCount];
                for (int m = 0; m < mmCount; m++)
                {
                    img.MMImageSize[m] = br.ReadUInt32();
                }
            }

            // Parse UserData block (between mipmap header and actual image data)
            int expectedSize = standardHeaderSize + mmHeaderSize;
            int userDataSize = img.Header.HeaderSize - expectedSize;
            if (userDataSize > 0)
            {
                ms.Position = startOfImage + expectedSize;
                img.UserData = br.ReadBytes(userDataSize);
            }
            else
            {
                img.UserData = [];
            }

            ms.Position = startOfImage + img.Header.HeaderSize;
            img.ImageData = br.ReadBytes((int)img.Header.ImageSize);

            if (img.Header.ClutSize > 0)
                img.ClutData = br.ReadBytes((int)img.Header.ClutSize);

            file.Images.Add(img);

            long totalSize = img.Header.TotalSize;
            ms.Position = startOfImage + ((totalSize + 15) & ~15); // Advance to next image (16-byte aligned)
        }

        return file;
    }

    private static T ReadStruct<T>(BinaryReader br) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] bytes = br.ReadBytes(size);
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}