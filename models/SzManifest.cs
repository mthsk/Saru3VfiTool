using Newtonsoft.Json;

namespace Saru3VfiTool.Models;

public sealed class SzManifest
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";

    [JsonProperty("type")]
    public string Type { get; set; } = "sz";

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("originalCompressedSize")]
    public long OriginalCompressedSize { get; set; }

    [JsonProperty("decompressedSize")]
    public long DecompressedSize { get; set; }
}
