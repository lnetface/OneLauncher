using System.Text.Json.Serialization;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

// Modrinth v2 协议模型：只声明启动器使用到的字段，字段名以 JsonPropertyName 对接协议。
[JsonSerializable(typeof(ModrinthSearch))]
[JsonSerializable(typeof(ModrinthVersion))]
[JsonSerializable(typeof(List<ModrinthVersion>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ModrinthHashesRequest))]
[JsonSerializable(typeof(Dictionary<string, ModrinthVersion>))]
internal partial class ModrinthJsonContext : JsonSerializerContext { }

#region 哈希反查 POST /v2/version_files

/// <summary>请求体；响应是以传入哈希为键、版本为值的对象，未命中的哈希不出现在响应中。</summary>
internal sealed class ModrinthHashesRequest
{
    [JsonPropertyName("hashes")]
    public List<string> Hashes { get; set; } = [];

    /// <summary>sha1 或 sha512。</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "sha512";
}

#endregion

#region 搜索 GET /v2/search

public sealed class ModrinthSearch
{
    [JsonPropertyName("hits")]
    public List<ModrinthProjectHit> Hits { get; set; } = [];

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public sealed class ModrinthProjectHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("project_type")]
    public string? ProjectType { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("date_created")]
    public DateTime DateCreated { get; set; }

    [JsonPropertyName("display_categories")]
    public List<string> Categories { get; set; } = [];
}

#endregion

#region 版本 GET /v2/project/{id}/version、GET /v2/version/{id}

/// <summary>Modrinth 项目的一个版本（旧名 ModrinthProjects）。</summary>
public sealed class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version_number")]
    public string? VersionNumber { get; set; }

    [JsonPropertyName("version_type")]
    public string? VersionType { get; set; }

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("files")]
    public List<ModrinthVersionFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<ModrinthDependency> Dependencies { get; set; } = [];

    /// <summary>优先返回 primary 文件，未标记时回退到第一个文件。</summary>
    public ModrinthVersionFile? GetPrimaryFile()
    {
        // 协议可能显式返回 null，不能只依赖属性初始值。
        var files = Files ?? [];
        return files.FirstOrDefault(file => file.Primary) ?? files.FirstOrDefault();
    }
}

public sealed class ModrinthDependency
{
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    /// <summary>required / optional / incompatible / embedded。</summary>
    [JsonPropertyName("dependency_type")]
    public string? DependencyType { get; set; }

    [JsonIgnore]
    public bool IsRequired => string.Equals(DependencyType, "required", StringComparison.OrdinalIgnoreCase);
}

public sealed class ModrinthVersionFile
{
    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("hashes")]
    public ModrinthFileHashes? Hashes { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class ModrinthFileHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

#endregion
