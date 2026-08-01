using System.Collections.Generic;
using Newtonsoft.Json;

namespace Saru3VfiTool.Models;

public class HashEntry
{
    [JsonProperty("h")]
    public ushort Hash { get; set; }

    [JsonProperty("i")]
    public ushort Index { get; set; }
}

public class VfiFileManifest
{
    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("compressed")]
    public bool Compressed { get; set; }

    [JsonProperty("keepSz")]
    public bool KeepSz { get; set; }

    [JsonProperty("originalSize")]
    public long OriginalSize { get; set; }
}

public class VfiFolderManifest
{
    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("parent")]
    public string Parent { get; set; } = "";

    [JsonProperty("next")]
    public string Next { get; set; } = "";

    [JsonProperty("parentOff")]
    public ushort? ParentOff { get; set; }

    [JsonProperty("nextOff")]
    public ushort? NextOff { get; set; }

    [JsonProperty("dummy")]
    public ushort Dummy { get; set; }

    [JsonProperty("entrySize")]
    public ushort? EntrySize { get; set; }
}

public class VfiManifest
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";

    [JsonProperty("type")]
    public string Type { get; set; } = "vfimanifest";

    [JsonProperty("dataOffSectors")]
    public uint? DataOffSectors { get; set; }

    [JsonProperty("totalSize")]
    public long? TotalSize { get; set; }

    [JsonProperty("hashTable")]
    public List<HashEntry>? HashTable { get; set; }

    [JsonProperty("folders")]
    public List<VfiFolderManifest> Folders { get; set; } = new();

    [JsonProperty("files")]
    public List<VfiFileManifest> Files { get; set; } = new();
}