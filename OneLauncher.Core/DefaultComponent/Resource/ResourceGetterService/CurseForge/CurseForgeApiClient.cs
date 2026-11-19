using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>
/// CurseForge Core API v1 的最小访问层：负责鉴权头、拼接地址、发送请求和源生成反序列化。
/// 非成功状态码以 HttpRequestException（带 StatusCode）抛出，JSON 结构错误以 JsonException 抛出。
/// </summary>
internal sealed class CurseForgeApiClient
{
    internal const string BaseUrl = "https://api.curseforge.com/v1";

    /// <summary>/v1/mods/files、/v1/mods 单次批量查询的条数上限（保守取值）。</summary>
    private const int BatchSize = 500;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    internal CurseForgeApiClient(HttpClient httpClient, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    /// <summary>
    /// 借用启动器共享的下载客户端，密钥只从 Init.CurseforgeApiKey 读取。
    /// 不修改共享客户端的默认请求头，密钥只附加在每个 CurseForge 请求上。
    /// </summary>
    /// <exception cref="OlanException">Init.CurseforgeApiKey 为 null。</exception>
    internal static CurseForgeApiClient Shared => new(Init.Download.UnityClient, ResolveApiKey());

    internal static string ResolveApiKey()
        => Init.CurseforgeApiKey ?? throw new OlanException(
            "无法访问 CurseForge",
            "读取 CurseForge API Key；Init.CurseforgeApiKey 为 null。CurseForge API 需要密钥才能访问。",
            OlanExceptionAction.Warning);

    /// <summary>GET /v1/mods/search。</summary>
    internal async Task<CurseForgeSearchResponse> SearchModsAsync(
        int classId,
        string? searchFilter,
        string? gameVersion,
        CurseForgeModLoaderType? modLoaderType,
        CurseForgeSortField sortField,
        int index,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"gameId={CurseForgeIds.MinecraftGameId}",
            $"classId={classId}",
            $"sortField={(int)sortField}",
            "sortOrder=desc",
            $"index={index}",
            $"pageSize={pageSize}"
        };
        if (!string.IsNullOrWhiteSpace(searchFilter))
            query.Add($"searchFilter={Uri.EscapeDataString(searchFilter)}");
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        if (modLoaderType is not null and not CurseForgeModLoaderType.Any)
            query.Add($"modLoaderType={(int)modLoaderType.Value}");

        string url = $"{BaseUrl}/mods/search?{string.Join('&', query)}";
        return await SendAsync(HttpMethod.Get, url, null, CurseForgeJsonContext.Default.CurseForgeSearchResponse, cancellationToken)
            ?? throw new OlanException("无法搜索资源信息", $"GET CurseForge 搜索结果并反序列化；请求URL searchUrl={url}。CurseForge 返回了无效的搜索结果。");
    }

    /// <summary>
    /// GET /v1/mods/{modId}/files，可按 Minecraft 版本和加载器筛选；只取第一页（最多 50 条）。
    /// </summary>
    internal async Task<IReadOnlyList<CurseForgeFile>> GetModFilesAsync(
        int modId,
        string? gameVersion,
        CurseForgeModLoaderType? modLoaderType,
        CancellationToken cancellationToken)
    {
        string url = BuildModFilesUrl(modId, gameVersion, modLoaderType);
        var response = await SendAsync(HttpMethod.Get, url, null, CurseForgeJsonContext.Default.CurseForgeFilesResponse, cancellationToken);
        return response?.Data ?? [];
    }

    /// <summary>GET /v1/mods/{modId}/files/{fileId}。</summary>
    internal async Task<CurseForgeFile?> GetModFileAsync(int modId, int fileId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            HttpMethod.Get, $"{BaseUrl}/mods/{modId}/files/{fileId}", null,
            CurseForgeJsonContext.Default.CurseForgeFileResponse, cancellationToken);
        return response?.Data;
    }

    /// <summary>POST /v1/mods/files，按批次查询文件信息；返回顺序不保证与输入一致。</summary>
    internal async Task<IReadOnlyList<CurseForgeFile>> GetFilesAsync(IReadOnlyCollection<int> fileIds, CancellationToken cancellationToken)
    {
        var result = new List<CurseForgeFile>(fileIds.Count);
        foreach (var chunk in fileIds.Distinct().Chunk(BatchSize))
        {
            string body = JsonSerializer.Serialize(
                new CurseForgeFileIdsRequest { FileIds = [.. chunk] },
                CurseForgeJsonContext.Default.CurseForgeFileIdsRequest);
            var response = await SendAsync(
                HttpMethod.Post, $"{BaseUrl}/mods/files", body,
                CurseForgeJsonContext.Default.CurseForgeFilesResponse, cancellationToken);
            result.AddRange(response?.Data ?? []);
        }
        return result;
    }

    /// <summary>POST /v1/mods，按批次查询项目信息；返回顺序不保证与输入一致。</summary>
    internal async Task<IReadOnlyList<CurseForgeMod>> GetModsAsync(IReadOnlyCollection<int> modIds, CancellationToken cancellationToken)
    {
        var result = new List<CurseForgeMod>(modIds.Count);
        foreach (var chunk in modIds.Distinct().Chunk(BatchSize))
        {
            string body = JsonSerializer.Serialize(
                new CurseForgeModIdsRequest { ModIds = [.. chunk] },
                CurseForgeJsonContext.Default.CurseForgeModIdsRequest);
            var response = await SendAsync(
                HttpMethod.Post, $"{BaseUrl}/mods", body,
                CurseForgeJsonContext.Default.CurseForgeModsResponse, cancellationToken);
            result.AddRange(response?.Data ?? []);
        }
        return result;
    }

    /// <summary>
    /// POST /v1/fingerprints/432，按 Murmur2 指纹批量匹配 Minecraft 文件，只返回 exactMatches。
    /// 指纹不是完整性校验，调用方须再核对大小与 SHA-1。
    /// </summary>
    internal async Task<IReadOnlyList<CurseForgeFingerprintMatch>> GetFingerprintMatchesAsync(
        IReadOnlyCollection<uint> fingerprints,
        CancellationToken cancellationToken)
    {
        var result = new List<CurseForgeFingerprintMatch>();
        foreach (var chunk in fingerprints.Distinct().Chunk(BatchSize))
        {
            string body = JsonSerializer.Serialize(
                new CurseForgeFingerprintsRequest { Fingerprints = [.. chunk] },
                CurseForgeJsonContext.Default.CurseForgeFingerprintsRequest);
            var response = await SendAsync(
                HttpMethod.Post, $"{BaseUrl}/fingerprints/{CurseForgeIds.MinecraftGameId}", body,
                CurseForgeJsonContext.Default.CurseForgeFingerprintMatchesResponse, cancellationToken);
            result.AddRange(response?.Data?.ExactMatches ?? []);
        }
        return result;
    }

    internal static string BuildModFilesUrl(int modId, string? gameVersion, CurseForgeModLoaderType? modLoaderType)
    {
        var query = new List<string> { "pageSize=50" };
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        if (modLoaderType is not null and not CurseForgeModLoaderType.Any)
            query.Add($"modLoaderType={(int)modLoaderType.Value}");
        return $"{BaseUrl}/mods/{modId}/files?{string.Join('&', query)}";
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string url,
        string? jsonBody,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"[CurseForgeApiClient] {method} {url}");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            // 密钥本身不写入诊断信息。
            throw new OlanException(
                "无法访问 CurseForge",
                $"{method} CurseForge API；请求URL url={url}；状态码={(int)response.StatusCode}。CurseForge 拒绝了请求，请检查 API Key 是否有效。",
                OlanExceptionAction.Warning);
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken);
    }
}
