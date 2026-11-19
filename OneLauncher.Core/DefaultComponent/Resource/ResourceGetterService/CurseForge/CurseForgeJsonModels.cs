using System.Text.Json.Serialization;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

// CurseForge Core API v1 协议模型：只声明启动器使用到的字段。
[JsonSerializable(typeof(CurseForgeSearchResponse))]
[JsonSerializable(typeof(CurseForgeFilesResponse))]
[JsonSerializable(typeof(CurseForgeFileResponse))]
[JsonSerializable(typeof(CurseForgeModsResponse))]
[JsonSerializable(typeof(CurseForgeFileIdsRequest))]
[JsonSerializable(typeof(CurseForgeModIdsRequest))]
[JsonSerializable(typeof(CurseForgeModpackManifest))]
[JsonSerializable(typeof(CurseForgeFingerprintsRequest))]
[JsonSerializable(typeof(CurseForgeFingerprintMatchesResponse))]
internal partial class CurseForgeJsonContext : JsonSerializerContext { }

#region 常量

/// <summary>CurseForge 中 Minecraft 相关的固定 ID。</summary>
public static class CurseForgeIds
{
    public const int MinecraftGameId = 432;

    public const int ModsClassId = 6;
    public const int ResourcePacksClassId = 12;
    public const int ModpacksClassId = 4471;
    public const int ShadersClassId = 6552;
}

/// <summary>/v1/mods/search 的 sortField。</summary>
public enum CurseForgeSortField
{
    Featured = 1,
    Popularity = 2,
    LastUpdated = 3,
    Name = 4,
    Author = 5,
    TotalDownloads = 6
}

/// <summary>CurseForge 的 ModLoaderType。</summary>
public enum CurseForgeModLoaderType
{
    Any = 0,
    Forge = 1,
    Cauldron = 2,
    LiteLoader = 3,
    Fabric = 4,
    Quilt = 5,
    NeoForge = 6
}

/// <summary>文件依赖关系类型。</summary>
public enum CurseForgeRelationType
{
    EmbeddedLibrary = 1,
    OptionalDependency = 2,
    RequiredDependency = 3,
    Tool = 4,
    Incompatible = 5,
    Include = 6
}

/// <summary>文件发布类型。</summary>
public enum CurseForgeReleaseType
{
    Release = 1,
    Beta = 2,
    Alpha = 3
}

#endregion

#region 通用

public sealed class CurseForgePagination
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    [JsonPropertyName("totalCount")]
    public long TotalCount { get; set; }
}

internal sealed class CurseForgeFileIdsRequest
{
    [JsonPropertyName("fileIds")]
    public List<int> FileIds { get; set; } = [];
}

internal sealed class CurseForgeModIdsRequest
{
    [JsonPropertyName("modIds")]
    public List<int> ModIds { get; set; } = [];
}

#endregion

#region 项目 GET /v1/mods/search、POST /v1/mods

public sealed class CurseForgeSearchResponse
{
    [JsonPropertyName("data")]
    public List<CurseForgeMod> Data { get; set; } = [];

    [JsonPropertyName("pagination")]
    public CurseForgePagination? Pagination { get; set; }
}

public sealed class CurseForgeModsResponse
{
    [JsonPropertyName("data")]
    public List<CurseForgeMod> Data { get; set; } = [];
}

public sealed class CurseForgeMod
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("classId")]
    public int? ClassId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("downloadCount")]
    public double DownloadCount { get; set; }

    [JsonPropertyName("logo")]
    public CurseForgeAsset? Logo { get; set; }

    [JsonPropertyName("authors")]
    public List<CurseForgeAuthor> Authors { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<CurseForgeCategory> Categories { get; set; } = [];

    [JsonPropertyName("latestFilesIndexes")]
    public List<CurseForgeFileIndex> LatestFilesIndexes { get; set; } = [];

    [JsonPropertyName("dateCreated")]
    public DateTime DateCreated { get; set; }

    [JsonPropertyName("dateModified")]
    public DateTime DateModified { get; set; }

    /// <summary>作者是否允许第三方分发；为 false 时文件的 downloadUrl 为空。</summary>
    [JsonPropertyName("allowModDistribution")]
    public bool? AllowModDistribution { get; set; }

    /// <summary>从最新文件索引汇总支持的 Minecraft 版本（去重，保持接口顺序）。</summary>
    public List<string> GetSupportedGameVersions()
        => (LatestFilesIndexes ?? [])
            .Select(index => index.GameVersion)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>从最新文件索引汇总支持的加载器。</summary>
    public HashSet<CurseForgeModLoaderType> GetSupportedModLoaders()
        => (LatestFilesIndexes ?? [])
            .Where(index => index.ModLoader is not null and not CurseForgeModLoaderType.Any)
            .Select(index => index.ModLoader!.Value)
            .ToHashSet();
}

public sealed class CurseForgeAsset
{
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class CurseForgeAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class CurseForgeCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

public sealed class CurseForgeFileIndex
{
    [JsonPropertyName("gameVersion")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("fileId")]
    public int FileId { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("releaseType")]
    public CurseForgeReleaseType ReleaseType { get; set; }

    [JsonPropertyName("modLoader")]
    public CurseForgeModLoaderType? ModLoader { get; set; }
}

#endregion

#region 文件 GET /v1/mods/{id}/files、GET /v1/mods/{id}/files/{fileId}、POST /v1/mods/files

public sealed class CurseForgeFilesResponse
{
    [JsonPropertyName("data")]
    public List<CurseForgeFile> Data { get; set; } = [];

    [JsonPropertyName("pagination")]
    public CurseForgePagination? Pagination { get; set; }
}

public sealed class CurseForgeFileResponse
{
    [JsonPropertyName("data")]
    public CurseForgeFile? Data { get; set; }
}

public sealed class CurseForgeFile
{
    private const int Sha1Algorithm = 1;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("isAvailable")]
    public bool IsAvailable { get; set; } = true;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("releaseType")]
    public CurseForgeReleaseType ReleaseType { get; set; }

    [JsonPropertyName("hashes")]
    public List<CurseForgeFileHash> Hashes { get; set; } = [];

    [JsonPropertyName("fileDate")]
    public DateTime FileDate { get; set; }

    [JsonPropertyName("fileLength")]
    public long FileLength { get; set; }

    /// <summary>作者禁止第三方分发时为 null。</summary>
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    /// <summary>包含 Minecraft 版本，也可能混有 "Fabric"、"Client" 等标签。</summary>
    [JsonPropertyName("gameVersions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<CurseForgeFileDependency> Dependencies { get; set; } = [];

    [JsonPropertyName("isServerPack")]
    public bool? IsServerPack { get; set; }

    /// <summary>CurseForge Murmur2 指纹（无符号 32 位，协议中以数字给出）。</summary>
    [JsonPropertyName("fileFingerprint")]
    public long FileFingerprint { get; set; }

    [JsonIgnore]
    public string? Sha1 => (Hashes ?? []).FirstOrDefault(hash => hash.Algo == Sha1Algorithm)?.Value;
}

public sealed class CurseForgeFileHash
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>1 = SHA1，2 = MD5。</summary>
    [JsonPropertyName("algo")]
    public int Algo { get; set; }
}

public sealed class CurseForgeFileDependency
{
    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("relationType")]
    public CurseForgeRelationType RelationType { get; set; }
}

#endregion

#region 指纹匹配 POST /v1/fingerprints/432

internal sealed class CurseForgeFingerprintsRequest
{
    [JsonPropertyName("fingerprints")]
    public List<uint> Fingerprints { get; set; } = [];
}

public sealed class CurseForgeFingerprintMatchesResponse
{
    [JsonPropertyName("data")]
    public CurseForgeFingerprintMatchesResult? Data { get; set; }
}

public sealed class CurseForgeFingerprintMatchesResult
{
    /// <summary>精确匹配；partialMatches 只能作为候选，不在此声明。</summary>
    [JsonPropertyName("exactMatches")]
    public List<CurseForgeFingerprintMatch> ExactMatches { get; set; } = [];
}

public sealed class CurseForgeFingerprintMatch
{
    /// <summary>项目（mod）ID。</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("file")]
    public CurseForgeFile? File { get; set; }
}

#endregion

#region 整合包清单 manifest.json

public sealed class CurseForgeModpackManifest
{
    [JsonPropertyName("manifestType")]
    public string? ManifestType { get; set; }

    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("minecraft")]
    public CurseForgeManifestMinecraft? Minecraft { get; set; }

    [JsonPropertyName("files")]
    public List<CurseForgeManifestFile> Files { get; set; } = [];

    [JsonPropertyName("overrides")]
    public string? Overrides { get; set; }
}

public sealed class CurseForgeManifestMinecraft
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("modLoaders")]
    public List<CurseForgeManifestModLoader> ModLoaders { get; set; } = [];
}

public sealed class CurseForgeManifestModLoader
{
    /// <summary>形如 "forge-47.2.0"、"neoforge-20.4.80"、"fabric-0.15.7"。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public sealed class CurseForgeManifestFile
{
    [JsonPropertyName("projectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("fileID")]
    public int FileId { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}

#endregion
