using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>CurseForge 资源检索入口。</summary>
public static class SearchCurseForge
{
    /// <summary>CurseForge 单次请求允许的最大条数。</summary>
    public const int MaxPageSize = 50;

    /// <summary>
    /// 按资源类型检索 CurseForge，默认按下载量排序；空关键词返回热门项目。
    /// </summary>
    /// <param name="keyword">搜索关键词，允许为空。</param>
    /// <param name="resourceType">资源类型，决定 classId。</param>
    /// <param name="gameVersion">按 Minecraft 版本筛选，可为空。</param>
    /// <param name="loader">按加载器筛选（仅对模组有意义），none 表示不筛选。</param>
    /// <param name="index">结果偏移量，index + limit 不超过 10000。</param>
    /// <param name="limit">返回条数，范围 1~50。</param>
    /// <param name="sortField">排序字段。</param>
    /// <param name="cancellationToken">取消请求。</param>
    /// <exception cref="OneLauncher.Core.Global.OlanException">未配置 API Key、密钥无效或返回了无效结果。</exception>
    public static Task<CurseForgeSearchResponse> SearchAsync(
        string? keyword,
        ResourceType resourceType = ResourceType.Mod,
        string? gameVersion = null,
        ModEnum loader = ModEnum.none,
        int index = 0,
        int limit = 20,
        CurseForgeSortField sortField = CurseForgeSortField.TotalDownloads,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + limit, 10_000);
        cancellationToken.ThrowIfCancellationRequested();

        // 光影与整合包不按加载器筛选，与 Modrinth 搜索保持一致。
        var loaderType = resourceType == ResourceType.Mod ? CurseForgeMapping.ToCurseForgeLoader(loader) : null;
        return CurseForgeApiClient.Shared.SearchModsAsync(
            CurseForgeMapping.ToClassId(resourceType),
            keyword,
            gameVersion,
            loaderType,
            sortField,
            index,
            limit,
            cancellationToken);
    }
}
