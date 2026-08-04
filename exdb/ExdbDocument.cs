using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Saru3VfiTool.Exdb;

public sealed class ExdbField
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("offset")]
    public int Offset { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("jsonName")]
    public string JsonName { get; set; } = "";

    [JsonProperty("span")]
    public int Span { get; set; }
}

public sealed class ExdbDocument
{
    // Keep the user-editable records at the top of the JSON document.
    [JsonProperty("type", Order = 0)]
    public string Type { get; set; } = "exdb";

    [JsonProperty("records", Order = 1)]
    public List<JObject> Records { get; set; } = new();

    [JsonProperty("schemaName", Order = 10)]
    public string SchemaName { get; set; } = "";

    [JsonProperty("recordSize", Order = 11)]
    public int RecordSize { get; set; }

    [JsonProperty("fields", Order = 12)]
    public List<ExdbField> Fields { get; set; } = new();

    [JsonProperty("version", Order = 20)]
    public string Version { get; set; } = "1.0";

    [JsonProperty("headerBlockSize", Order = 21)]
    public int HeaderBlockSize { get; set; }

    // The original header block is reused when its schema and record count still match.
    [JsonProperty("headerBase64", Order = 90)]
    public string HeaderBase64 { get; set; } = "";

    // Bytes after the declared record table are preserved verbatim.
    [JsonProperty("trailingDataBase64", Order = 91)]
    public string TrailingDataBase64 { get; set; } = "";
}
