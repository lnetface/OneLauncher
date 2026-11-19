using System.Diagnostics;
using System.Net;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>解析 CurseForge 模组在指定 Minecraft 版本与加载器下的最新文件及必需依赖。</summary>
internal static class GetModCurseForge
{
    /// <summary>
    /// 获取模组下载信息。
    /// </summary>
    /// <param name="projectId">CurseForge 项目 ID（整数字符串）。</param>
    /// <param name="gameVersion">Minecraft 版本。</param>
    /// <param name="loader">实例加载器；原版实例没有可安装的模组版本。</param>
    /// <param name="modsDirectory">文件保存目录（通常为实例的 mods）。</param>
    /// <param name="includeDependencies">是否解析一级必需依赖。</param>
    /// <param name="cancellationToken">取消元数据请求。</param>
    /// <returns>下载列表，第一项为模组本体，其后为依赖；项目不存在或没有兼容版本时返回空列表。</returns>
    /// <exception cref="OlanException">未配置 API Key，或兼容文件不允许第三方下载。</exception>
    internal static async Task<List<NdDowItem>> GetAsync(
        string projectId,
        string gameVersion,
        ModEnum loader,
        string modsDirectory,
        bool includeDependencies = true,
        CancellationToken cancellationToken = default)
    {
        int modId = CurseForgeMapping.ParseProjectId(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var loaderType = CurseForgeMapping.ToCurseForgeLoader(loader);
        if (loaderType == null)
            return [];

        var api = CurseForgeApiClient.Shared;
        var mainFile = await FindLatestFileAsync(api, modId, gameVersion, loaderType.Value, cancellationToken);
        if (mainFile == null)
            return [];

        NdDowItem main = ToDownloadItem(mainFile, gameVersion, loader, modsDirectory);
        if (!includeDependencies)
            return [main];

        // 只解析一级 RequiredDependency；可选、内嵌、工具与不兼容关系不下载。
        var dependencyIds = (mainFile.Dependencies ?? [])
            .Where(dependency => dependency.RelationType == CurseForgeRelationType.RequiredDependency)
            .Select(dependency => dependency.ModId)
            .Where(id => id > 0 && id != modId)
            .Distinct()
            .ToList();
        var resolved = await Task.WhenAll(dependencyIds.Select(id =>
            ResolveDependencyAsync(api, id, gameVersion, loader, loaderType.Value, modsDirectory, cancellationToken)));

        // 重复路径由 Download.DownloadListAsync 统一去重。
        return [main, .. resolved.OfType<NdDowItem>()];
    }

    private static async Task<CurseForgeFile?> FindLatestFileAsync(
        CurseForgeApiClient api,
        int modId,
        string gameVersion,
        CurseForgeModLoaderType loaderType,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await api.GetModFilesAsync(modId, gameVersion, loaderType, cancellationToken);
            return CurseForgeMapping.SelectLatestFile(files);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<NdDowItem?> ResolveDependencyAsync(
        CurseForgeApiClient api,
        int dependencyId,
        string gameVersion,
        ModEnum loader,
        CurseForgeModLoaderType loaderType,
        string modsDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await FindLatestFileAsync(api, dependencyId, gameVersion, loaderType, cancellationToken);
            if (file == null)
            {
                Debug.WriteLine($"[GetModCurseForge] 依赖 {dependencyId} 没有适配 {gameVersion}/{loader} 的文件，已跳过。");
                return null;
            }
            return ToDownloadItem(file, gameVersion, loader, modsDirectory);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 单个依赖解析失败不阻断主模组安装，与 Modrinth 行为一致。
            Debug.WriteLine($"[GetModCurseForge] 解析依赖 {dependencyId} 失败：{ex.Message}");
            return null;
        }
    }

    private static NdDowItem ToDownloadItem(CurseForgeFile file, string gameVersion, ModEnum loader, string modsDirectory)
    {
        string context = $"选择 CurseForge 模组文件；参数 modId={file.ModId}；参数 fileId={file.Id}；参数 loader={loader}；Minecraft={gameVersion}；文件/目录 modsDirectory={modsDirectory}";
        // 作者关闭第三方分发时 downloadUrl 为空，按 CurseForge API 条款不绕过限制。
        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
            throw new OlanException("安装模组失败", $"{context}。文件 {file.DisplayName ?? file.FileName} 的作者不允许第三方启动器下载，请前往 CurseForge 网站手动下载。", OlanExceptionAction.Warning);
        return Download.CreateSafeItem(file.DownloadUrl, file.FileName, modsDirectory, file.FileLength, file.Sha1)
            ?? throw new OlanException("安装模组失败", $"{context}；文件/目录 filename={file.FileName}。CurseForge 返回了无效的下载地址或文件名。");
    }
}

