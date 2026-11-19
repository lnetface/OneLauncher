namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

/// <summary>Modrinth 资源检索入口。</summary>
public static class SearchModrinth
{
    /// <summary>Modrinth 单次请求允许的最大条数。</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// 按资源类型检索 Modrinth，结果按下载量排序；空关键词返回热门项目。
    /// </summary>
    /// <param name="keyword">搜索关键词，允许为空。</param>
    /// <param name="resourceType">资源类型，决定 facets 筛选。</param>
    /// <param name="offset">跳过的结果数，用于分页。</param>
    /// <param name="limit">返回条数，范围 1~100。</param>
    /// <param name="cancellationToken">取消请求。</param>
    /// <exception cref="OneLauncher.Core.Global.OlanException">Modrinth 返回了无效的搜索结果。</exception>
    public static Task<ModrinthSearch> SearchAsync(
        string? keyword,
        ResourceType resourceType = ResourceType.Mod,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxPageSize);
        cancellationToken.ThrowIfCancellationRequested();

        return ModrinthApiClient.Shared.SearchAsync(
            keyword, GetFacets(resourceType), offset, limit, cancellationToken);
    }

    // 模组只展示启动器可安装加载器的项目；光影与整合包不按加载器筛选。
    internal static string GetFacets(ResourceType resourceType) => resourceType switch
    {
        ResourceType.Mod => """[["categories:neoforge","categories:fabric","categories:quilt"],["project_type:mod"]]""",
        ResourceType.ShaderPack => """[["project_type:shader"]]""",
        ResourceType.Modpack => """[["project_type:modpack"]]""",
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType))
    };
}
