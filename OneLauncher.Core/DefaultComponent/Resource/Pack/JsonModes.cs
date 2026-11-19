using System.Text.Json.Serialization;

namespace OneLauncher.Core.DefaultComponent.Resource.Pack;

[JsonSerializable(typeof(ModrinthManifest))]
[JsonSerializable(typeof(FinalVersionInfo))]
public partial class MrpackJsonContext : JsonSerializerContext
{
}

/// <summary>modrinth.index.json。规范必填的字段使用 required，缺失时反序列化抛出 JsonException。</summary>
public sealed class ModrinthManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("game")]
    public required string Game { get; set; }

    [JsonPropertyName("versionId")]
    public required string VersionId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("dependencies")]
    public required Dictionary<string, string> Dependencies { get; set; }

    [JsonPropertyName("files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ModrinthFile>? Files { get; set; }
}

/// <summary>清单中的远程文件。外部清单可能缺少 hashes/downloads，读取方需判空。</summary>
public sealed class ModrinthFile
{
    /// <summary>相对实例根目录的路径，统一使用 '/'。</summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>键为 sha1、sha512。</summary>
    [JsonPropertyName("hashes")]
    public Dictionary<string, string>? Hashes { get; set; }

    [JsonPropertyName("downloads")]
    public List<string>? Downloads { get; set; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

public sealed class FinalVersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "release";
}

/// <summary>
/// 实例导出结果。
/// </summary>
/// <param name="OutputPath">生成的 .mrpack 完整路径。</param>
/// <param name="RemoteFileCount">写入清单、导入时在线下载的模组数。</param>
/// <param name="OverrideModCount">未识别来源、打包进 overrides/mods 的模组数。</param>
/// <param name="Warnings">来源识别降级等不影响导出完整性的提示。</param>
public sealed record ModpackExportResult(
    string OutputPath,
    int RemoteFileCount,
    int OverrideModCount,
    IReadOnlyList<string> Warnings);
