using System.Collections.Generic;
using Newtonsoft.Json;

namespace Saru3VfiTool.Models;

public class PckMemberManifest
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("attributes")]
    public string Attributes { get; set; } = "";

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("originalSize")]
    public int OriginalSize { get; set; }

    [JsonProperty("typeHint")]
    public string TypeHint { get; set; } = "";
}

public class PckManifest
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";

    [JsonProperty("type")]
    public string Type { get; set; } = "pck";

    [JsonProperty("members")]
    public List<PckMemberManifest> Members { get; set; } = new();
}
