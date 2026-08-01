using System.Collections.Generic;
using Newtonsoft.Json;

namespace Saru3VfiTool.Models;

public class Tim2MipmapManifest
{
    [JsonProperty("source")]
    public string Source { get; set; } = "";
    [JsonProperty("level")]
    public int Level { get; set; }
    [JsonProperty("width")]
    public int Width { get; set; }
    [JsonProperty("height")]
    public int Height { get; set; }
}

public class Tim2Manifest
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";
    [JsonProperty("type")]
    public string Type { get; set; } = "tim2";
    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("width")]
    public int Width { get; set; }
    [JsonProperty("height")]
    public int Height { get; set; }
    [JsonProperty("hasClut")]
    public bool HasClut { get; set; }
    [JsonProperty("clutColors")]
    public int ClutColors { get; set; }
    [JsonProperty("clutType")]
    public int ClutType { get; set; }
    [JsonProperty("originalSize")]
    public int OriginalSize { get; set; }

    [JsonProperty("fileFormat")]
    public byte FileFormat { get; set; }
    [JsonProperty("timVersion")]
    public byte TimVersion { get; set; } = 4;
    [JsonProperty("imageType")]
    public byte ImageType { get; set; }
    [JsonProperty("mipMapTextures")]
    public byte MipMapTextures { get; set; } = 1;
    [JsonProperty("headerSize")]
    public ushort HeaderSize { get; set; } = 48;
    [JsonProperty("pictFormat")]
    public byte PictFormat { get; set; }
    [JsonProperty("gsTex0")]
    public ulong GsTex0 { get; set; }
    [JsonProperty("gsTex1")]
    public ulong GsTex1 { get; set; }
    [JsonProperty("gsRegs")]
    public uint GsRegs { get; set; }
    [JsonProperty("gsTexClut")]
    public uint GsTexClut { get; set; }
    [JsonProperty("userData")]
    public string UserData { get; set; } = "";
    [JsonProperty("gsMiptbp1")]
    public ulong GsMiptbp1 { get; set; }
    [JsonProperty("gsMiptbp2")]
    public ulong GsMiptbp2 { get; set; }
    [JsonProperty("mipmaps")]
    public List<Tim2MipmapManifest> Mipmaps { get; set; } = new();
}