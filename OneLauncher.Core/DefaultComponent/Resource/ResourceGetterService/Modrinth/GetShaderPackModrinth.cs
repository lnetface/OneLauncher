using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

/// <summary>解析 Modrinth 光影包在指定 Minecraft 版本下的最新文件。</summary>
internal static class GetShaderPackModrinth
{
    /// <summary>
    /// 获取光影包下载信息。只按 Minecraft 版本筛选，不以实例的加载器或已安装依赖限制光影包，
    /// 依赖字段（如 Iris）只作为元数据，不发起请求。
    /// </summary>
    /// <param name="projectId">Modrinth 项目 ID 或 slug。</param>
    /// <param name="gameVersion">Minecraft 版本。</param>
    /// <param name="shaderPackPath">文件保存目录（通常为实例的 shaderpacks）。</param>
    /// <param name="cancellationToken">取消元数据请求。</param>
    /// <exception cref="OlanException">没有兼容版本或下载文件信息无效。</exception>
    internal static async Task<NdDowItem> GetDownloadInfoAsync(
        string projectId,
        string gameVersion,
        string shaderPackPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        cancellationToken.ThrowIfCancellationRequested();

        string url = ModrinthApiClient.BuildProjectVersionsUrl(projectId, gameVersion, loader: null);
        string context = $"参数 projectId={projectId}；参数 gameVersion={gameVersion}；文件/目录 shaderPackPath={shaderPackPath}；请求URL url={url}";
        var versions = await ModrinthApiClient.Shared.GetProjectVersionsAsync(
            projectId, gameVersion, loader: null, cancellationToken);
        if (versions.Count == 0)
            throw new OlanException("安装光影包失败", $"筛选匹配 Minecraft 的光影版本；{context}。项目 {projectId} 没有适配 Minecraft {gameVersion} 的光影包版本。");

        // Modrinth 版本列表按发布时间从新到旧返回；文件名限定为单个文件，防止写出 shaderpacks 目录。
        var file = versions[0].GetPrimaryFile();
        var item = Download.CreateSafeItem(file?.Url, file?.Filename, shaderPackPath, file?.Size ?? 0, file?.Hashes?.Sha1)
            ?? throw new OlanException("安装光影包失败", $"选择光影版本的有效下载文件；{context}；文件/目录 filename={file?.Filename}。项目 {projectId} 的版本缺少有效的下载地址或文件名。");

        cancellationToken.ThrowIfCancellationRequested();
        return item;
    }
}
