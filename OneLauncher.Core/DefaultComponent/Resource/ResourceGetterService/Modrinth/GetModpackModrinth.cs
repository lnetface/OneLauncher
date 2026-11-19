using System.IO.Compression;
using OneLauncher.Core.DefaultComponent.Resource.Pack;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;

/// <summary>下载 Modrinth 整合包的最新 .mrpack 并解析清单，供预览与导入复用。</summary>
internal static class GetModpackModrinth
{
    /// <summary>
    /// 获取整合包。返回值持有临时文件，调用方负责释放。
    /// </summary>
    /// <param name="projectId">Modrinth 项目 ID 或 slug。</param>
    /// <param name="cancellationToken">取消元数据请求、下载及校验。</param>
    /// <exception cref="OlanException">没有可安装版本、缺少 .mrpack 文件或清单无效。</exception>
    internal static async Task<ModpackDownload> GetAsync(string projectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        cancellationToken.ThrowIfCancellationRequested();

        string url = ModrinthApiClient.BuildProjectVersionsUrl(projectId, gameVersion: null, loader: null);
        var versions = await ModrinthApiClient.Shared.GetProjectVersionsAsync(
            projectId, gameVersion: null, loader: null, cancellationToken);
        if (versions.Count == 0)
            throw new OlanException("获取整合包失败", $"筛选 Modrinth 可安装整合包版本；参数 projectId={projectId}；请求URL url={url}。项目 {projectId} 没有可安装的整合包版本。");

        var packFiles = (versions[0].Files ?? [])
            .Where(file => file.Filename?.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        var file = packFiles.FirstOrDefault(file => file.Primary) ?? packFiles.FirstOrDefault();
        if (file == null || !Uri.TryCreate(file.Url, UriKind.Absolute, out var downloadUri)
            || (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp))
            throw new OlanException("获取整合包失败", $"选择整合包 .mrpack 下载文件；参数 projectId={projectId}；请求URL url={url}。项目 {projectId} 缺少有效的 .mrpack 下载文件。");

        // 网络流先写入唯一临时文件，供 ZIP 随机读取和后续安装复用。
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"olan-pack-{Guid.NewGuid():N}.mrpack");
        try
        {
            using (var packageResponse = await Init.Download.UnityClient.GetAsync(
                downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                packageResponse.EnsureSuccessStatusCode();
                await using var networkStream = await packageResponse.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous);
                await networkStream.CopyToAsync(destination, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Init.ConfigManager.Data.OlanSettings.IsSha1Enabled && !string.IsNullOrWhiteSpace(file.Hashes?.Sha1))
                await Init.Download.CheckAllSha1(
                    [new NdDowItem(file.Url!, temporaryPath, (int)Math.Clamp(file.Size, 0, int.MaxValue), file.Hashes.Sha1)],
                    1, cancellationToken);

            using var archive = ZipFile.OpenRead(temporaryPath);
            var entry = archive.GetEntry("modrinth.index.json")
                ?? throw new OlanException("整合包无效", $"打开下载的 .mrpack 并定位清单；参数 projectId={projectId}；请求URL url={url}；文件/目录 temporaryPath={temporaryPath}。压缩包内未找到 modrinth.index.json 文件。");
            using var manifestStream = entry.Open();
            var parser = new MrpackParser(manifestStream, $"{temporaryPath}；下载URL={file.Url}");
            // 提前读取侧栏与导入必需的信息，解析失败时清理下载文件。
            parser.GetMinecraftVersion();
            parser.GetLoaderInfo();
            cancellationToken.ThrowIfCancellationRequested();
            return new ModpackDownload(temporaryPath, parser);
        }
        catch
        {
            ModpackDownload.DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }
}
