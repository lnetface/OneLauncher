using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

/// <summary>
/// Modrinth v2 API 的最小访问层：负责拼接地址、发送请求和源生成反序列化。
/// 非成功状态码以 HttpRequestException（带 StatusCode）抛出，JSON 结构错误以 JsonException 抛出，
/// 由上层 Get***/Search*** 按业务语义决定是否转换为 OlanException。
/// </summary>
internal sealed class ModrinthApiClient
{
    internal const string BaseUrl = "https://api.modrinth.com/v2";

    /// <summary>/v2/version_files 单次提交的哈希数上限（保守取值）。</summary>
    private const int HashBatchSize = 500;

    private readonly HttpClient _httpClient;

    internal ModrinthApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <summary>借用启动器共享的下载客户端；每次调用时读取，便于初始化后替换。不负责释放客户端。</summary>
    internal static ModrinthApiClient Shared => new(Init.Download.UnityClient);

    /// <summary>GET /v2/search。</summary>
    internal async Task<ModrinthSearch> SearchAsync(
        string? query,
        string facets,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        string url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? string.Empty)}"
            + $"&facets={Uri.EscapeDataString(facets)}&index=downloads&offset={offset}&limit={limit}";
        return await GetJsonAsync(url, ModrinthJsonContext.Default.ModrinthSearch, cancellationToken)
            ?? throw new OlanException("无法搜索资源信息", $"GET Modrinth 搜索结果并反序列化；请求URL searchUrl={url}。Modrinth 返回了无效的搜索结果。");
    }

    /// <summary>
    /// GET /v2/project/{id}/version，可按 Minecraft 版本和加载器筛选。
    /// 返回结果按发布时间从新到旧排列（Modrinth 默认顺序）。
    /// </summary>
    internal async Task<IReadOnlyList<ModrinthVersion>> GetProjectVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        string url = BuildProjectVersionsUrl(projectId, gameVersion, loader);
        return await GetJsonAsync(url, ModrinthJsonContext.Default.ListModrinthVersion, cancellationToken) ?? [];
    }

    /// <summary>GET /v2/version/{id}。</summary>
    internal Task<ModrinthVersion?> GetVersionAsync(string versionId, CancellationToken cancellationToken)
        => GetJsonAsync(
            $"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}",
            ModrinthJsonContext.Default.ModrinthVersion,
            cancellationToken);

    /// <summary>
    /// POST /v2/version_files，按 SHA-512 批量反查文件所属版本。
    /// 返回键为小写哈希（忽略大小写比较）；未命中的哈希不在结果中。
    /// 返回的版本可能含多个文件，调用方须在 Files 中再按哈希精确匹配。
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, ModrinthVersion>> GetVersionsByHashesAsync(
        IReadOnlyCollection<string> sha512Hashes,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ModrinthVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in sha512Hashes.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(HashBatchSize))
        {
            string body = JsonSerializer.Serialize(
                new ModrinthHashesRequest { Hashes = [.. chunk], Algorithm = "sha512" },
                ModrinthJsonContext.Default.ModrinthHashesRequest);
            var response = await SendJsonAsync(
                HttpMethod.Post, $"{BaseUrl}/version_files", body,
                ModrinthJsonContext.Default.DictionaryStringModrinthVersion, cancellationToken);
            if (response == null)
                continue;
            foreach (var (hash, version) in response)
                result[hash] = version;
        }
        return result;
    }

    internal static string BuildProjectVersionsUrl(string projectId, string? gameVersion, string? loader)
    {
        var query = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={EscapeJsonArray(gameVersion)}");
        if (!string.IsNullOrWhiteSpace(loader))
            query.Add($"loaders={EscapeJsonArray(loader)}");

        string url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectId)}/version";
        return query.Count == 0 ? url : $"{url}?{string.Join('&', query)}";
    }

    // Modrinth 的数组参数是 JSON 文本，序列化后再做 URL 编码，避免版本号中的特殊字符破坏查询串。
    private static string EscapeJsonArray(string value)
        => Uri.EscapeDataString(JsonSerializer.Serialize(new[] { value }, ModrinthJsonContext.Default.StringArray));

    private Task<T?> GetJsonAsync<T>(string url, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        => SendJsonAsync(HttpMethod.Get, url, null, typeInfo, cancellationToken);

    private async Task<T?> SendJsonAsync<T>(
        HttpMethod method,
        string url,
        string? jsonBody,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"[ModrinthApiClient] {method} {url}");
        using var request = new HttpRequestMessage(method, url);
        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken);
    }
}
