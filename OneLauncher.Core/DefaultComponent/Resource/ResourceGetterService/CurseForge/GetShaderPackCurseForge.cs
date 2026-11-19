using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>解析 CurseForge 光影包在指定 Minecraft 版本下的最新文件。</summary>
internal static class GetShaderPackCurseForge
{
    /// <summary>
    /// 获取光影包下载信息。只按 Minecraft 版本筛选，不按加载器筛选，也不下载依赖（如 Iris/OptiFine）。
    /// </summary>
    /// <param name="projectId">CurseForge 项目 ID（整数字符串）。</param>
    /// <param name="gameVersion">Minecraft 版本。</param>
    /// <param name="shaderPackPath">文件保存目录（通常为实例的 shaderpacks）。</param>
    /// <param name="cancellationToken">取消元数据请求。</param>
    /// <exception cref="OlanException">没有兼容文件、文件不允许第三方下载或文件名无效。</exception>
    internal static async Task<NdDowItem> GetDownloadInfoAsync(
        string projectId,
        string gameVersion,
        string shaderPackPath,
        CancellationToken cancellationToken = default)
    {
        int modId = CurseForgeMapping.ParseProjectId(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        cancellationToken.ThrowIfCancellationRequested();

        string url = CurseForgeApiClient.BuildModFilesUrl(modId, gameVersion, modLoaderType: null);
        string context = $"参数 projectId={projectId}；参数 gameVersion={gameVersion}；文件/目录 shaderPackPath={shaderPackPath}；请求URL url={url}";
        var files = await CurseForgeApiClient.Shared.GetModFilesAsync(modId, gameVersion, modLoaderType: null, cancellationToken);
        var file = CurseForgeMapping.SelectLatestFile(files)
            ?? throw new OlanException("安装光影包失败", $"筛选匹配 Minecraft 的光影文件；{context}。项目 {projectId} 没有适配 Minecraft {gameVersion} 的光影包文件。");

        // 作者关闭第三方分发时 downloadUrl 为空，按 CurseForge API 条款不绕过限制。
        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
            throw new OlanException("安装光影包失败", $"选择光影文件；{context}。文件 {file.DisplayName ?? file.FileName} 的作者不允许第三方启动器下载，请前往 CurseForge 网站手动下载。", OlanExceptionAction.Warning);
        var item = Download.CreateSafeItem(file.DownloadUrl, file.FileName, shaderPackPath, file.FileLength, file.Sha1)
            ?? throw new OlanException("安装光影包失败", $"选择光影文件；{context}；文件/目录 filename={file.FileName}。CurseForge 返回了无效的下载地址或文件名。");
        cancellationToken.ThrowIfCancellationRequested();
        return item;
    }
}
