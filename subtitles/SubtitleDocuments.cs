using System.Collections.Generic;
using Newtonsoft.Json;

namespace Saru3VfiTool.Subtitles;

public sealed class TextBinField
{
    [JsonProperty("type", Order = 0)]
    public uint Type { get; set; }

    [JsonProperty("text", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; set; }

    [JsonProperty("value", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public uint? Value { get; set; }
}

public sealed class TextBinRecord
{
    [JsonProperty("fields")]
    public List<TextBinField> Fields { get; set; } = new();
}

public sealed class TextBinGroup
{
    [JsonProperty("name", Order = 0)]
    public string Name { get; set; } = "";

    [JsonProperty("records", Order = 1)]
    public List<TextBinRecord> Records { get; set; } = new();
}

public sealed class SubtitleTextDocument
{
    [JsonProperty("type", Order = 0)]
    public string Type { get; set; } = "text-bin";

    [JsonProperty("groups", Order = 1)]
    public List<TextBinGroup> Groups { get; set; } = new();

    [JsonProperty("version", Order = 10)]
    public string Version { get; set; } = "1.0";
}

public sealed class SubtitleTimingCue
{
    [JsonProperty("start")]
    public float Start { get; set; }

    [JsonProperty("end")]
    public float End { get; set; }
}

public sealed class SubtitleTimingDocument
{
    [JsonProperty("type", Order = 0)]
    public string Type { get; set; } = "subtitle-timing";

    [JsonProperty("cues", Order = 1)]
    public List<SubtitleTimingCue> Cues { get; set; } = new();

    [JsonProperty("totalDuration", Order = 2)]
    public float TotalDuration { get; set; }

    [JsonProperty("version", Order = 10)]
    public string Version { get; set; } = "1.0";
}
