using System.Diagnostics;
using System.Net;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

/// <summary>
/// 解析 Modrinth 模组在指定 Minecraft 版本与加载器下的最新文件及必需依赖。
/// </summary>
internal static class GetModModrinth
{
    /// <summary>
    /// 获取模组下载信息。
    /// </summary>
    /// <param name="projectId">Modrinth 项目 ID 或 slug。</param>
    /// <param name="gameVersion">Minecraft 版本。</param>
    /// <param name="loader">实例加载器；原版实例没有可安装的模组版本。</param>
    /// <param name="modsDirectory">文件保存目录（通常为实例的 mods）。</param>
    /// <param name="includeDependencies">是否解析一级必需依赖。</param>
    /// <param name="cancellationToken">取消元数据请求。</param>
    /// <returns>下载列表，第一项为模组本体，其后为依赖；项目不存在或没有兼容版本时返回空列表。</returns>
    /// <exception cref="OneLauncher.Core.Global.OlanException">兼容版本缺少有效的下载文件。</exception>
    internal static async Task<List<NdDowItem>> GetAsync(
        string projectId,
        string gameVersion,
        ModEnum loader,
        string modsDirectory,
        bool includeDependencies = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (loader == ModEnum.none)
            return [];

        var api = ModrinthApiClient.Shared;
        var mainVersion = await FindLatestVersionAsync(api, projectId, gameVersion, loader, cancellationToken);
        if (mainVersion == null)
            return [];

        NdDowItem main = ToDownloadItem(mainVersion, projectId, gameVersion, loader, modsDirectory);
        if (!includeDependencies)
            return [main];

        // 只解析一级 required 依赖；可选、内嵌和不兼容依赖不下载。
        var requiredDependencies = (mainVersion.Dependencies ?? [])
            .Where(dependency => dependency.IsRequired)
            .Where(dependency => !string.Equals(dependency.ProjectId, mainVersion.ProjectId ?? projectId, StringComparison.Ordinal))
            .DistinctBy(dependency => dependency.ProjectId ?? dependency.VersionId)
            .ToList();
        var resolved = await Task.WhenAll(requiredDependencies.Select(dependency =>
            ResolveDependencyAsync(api, dependency, gameVersion, loader, modsDirectory, cancellationToken)));

        // 重复路径由 Download.DownloadListAsync 统一去重。
        return [main, .. resolved.OfType<NdDowItem>()];
    }

    private static async Task<ModrinthVersion?> FindLatestVersionAsync(
        ModrinthApiClient api,
        string projectId,
        string gameVersion,
        ModEnum loader,
        CancellationToken cancellationToken)
    {
        try
        {
            var versions = await api.GetProjectVersionsAsync(projectId, gameVersion, loader.ToString(), cancellationToken);
            // 版本列表按发布时间从新到旧返回，取第一个即最新兼容版本。
            return versions.FirstOrDefault();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<NdDowItem?> ResolveDependencyAsync(
        ModrinthApiClient api,
        ModrinthDependency dependency,
        string gameVersion,
        ModEnum loader,
        string modsDirectory,
        CancellationToken cancellationToken)
    {
        string dependencyId = dependency.ProjectId ?? dependency.VersionId ?? "<unknown>";
        try
        {
            // 作者指定了具体版本时优先使用该版本，否则按实例环境查找项目最新兼容版本。
            ModrinthVersion? version = !string.IsNullOrWhiteSpace(dependency.VersionId)
                ? await api.GetVersionAsync(dependency.VersionId, cancellationToken)
                : !string.IsNullOrWhiteSpace(dependency.ProjectId)
                    ? await FindLatestVersionAsync(api, dependency.ProjectId, gameVersion, loader, cancellationToken)
                    : null;
            if (version == null)
            {
                Debug.WriteLine($"[GetModModrinth] 依赖 {dependencyId} 没有适配 {gameVersion}/{loader} 的版本，已跳过。");
                return null;
            }
            return ToDownloadItem(version, dependencyId, gameVersion, loader, modsDirectory);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 与旧实现一致：单个依赖解析失败不阻断主模组安装。
            Debug.WriteLine($"[GetModModrinth] 解析依赖 {dependencyId} 失败：{ex.Message}");
            return null;
        }
    }

    private static NdDowItem ToDownloadItem(
        ModrinthVersion version,
        string projectId,
        string gameVersion,
        ModEnum loader,
        string modsDirectory)
    {
        string context = $"选择 Modrinth 模组版本的下载文件；参数 projectId={projectId}；参数 loader={loader}；Minecraft={gameVersion}；文件/目录 modsDirectory={modsDirectory}";
        var file = version.GetPrimaryFile();
        return Download.CreateSafeItem(file?.Url, file?.Filename, modsDirectory, file?.Size ?? 0, file?.Hashes?.Sha1)
            ?? throw new OlanException("安装模组失败", $"{context}；文件/目录 filename={file?.Filename}。项目 {projectId} 的版本缺少有效的下载地址或文件名。");
    }
}
