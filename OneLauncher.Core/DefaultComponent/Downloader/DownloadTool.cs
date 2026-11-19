using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System.Diagnostics;

namespace OneLauncher.Core.DefaultComponent.Downloader;

internal partial class Download
{
    public Task DownloadListAsync(
        IProgress<(int completedFiles, string FilesName)> progress,
        List<NdDowItem> downloadNds,
        int maxConcurrentDownloads,
        CancellationToken token)
    {
        return RunDownloadListAsync(progress, downloadNds, maxConcurrentDownloads, token);
    }

    private async Task RunDownloadListAsync(
        IProgress<(int completedFiles, string FilesName)> progress,
        List<NdDowItem> downloadNds,
        int maxConcurrentDownloads,
        CancellationToken token)
    {
        int completedFiles = 0;

        try
        {
            // 不同加载器或下载地址可能指向同一个本地文件，只下载一次。
            var uniqueDownloads = downloadNds.DistinctBy(
                item => Path.GetFullPath(item.path),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            await Parallel.ForEachAsync(
                uniqueDownloads,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrentDownloads,
                    CancellationToken = token
                },
                async (item, ct) =>
                {
                    try
                    {
                        // 原子递增已完成文件数
                        Interlocked.Increment(ref completedFiles);
                        // 报告进度
                        progress?.Report((completedFiles, item.url));
                        // 执行下载操作
                        await DownloadFile(item.url, item.path, ct);
                    }
                    catch (HttpRequestException ex)
                    {
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                            try
                            {
                                await DownloadFile(item.url, item.path, ct);
                                return; // 成功下载，退出重试循环
                            }
                            catch (HttpRequestException ex2)
                            {
                                Debug.WriteLine($"重试下载失败: {ex2.Message}, URL: {item.url}");
                                continue;
                            }
                        }
                        throw new OlanException("下载失败", $"批量下载文件在首次请求失败后重试三次仍失败；请求URL={item.url}；目标文件={item.path}。原因：{ex.Message}", OlanExceptionAction.Error, ex);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // 取消下载不进入重试，也不转换为失败。
                    }
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 并行下载已取消，保持任务的已取消状态。
        }
    }
    public async Task<Stream> HttpStreamGetter(string url,CancellationToken cancellationToken)
    {
        var response = await UnityClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            // 不能在此释放 response：它会连同 Content 释放刚取得的流；调用方释放流即可归还连接。
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
    // 按顺序请求候选地址，返回第一个成功响应的流；全部失败时抛出最后一个地址的异常
    public async Task<Stream> HttpStreamGetter(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (var url in urls)
        {
            try
            {
                return await HttpStreamGetter(url, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
            }
        }
        if (lastException != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(lastException);
        throw new ArgumentException("没有可用的请求地址", nameof(urls));
    }
    public Task DownloadFile(string url,string savepath, CancellationToken? token = null)
        => string.IsNullOrEmpty(url) ? Task.CompletedTask : DownloadFile([url], savepath, token);
    public async Task DownloadFile(IEnumerable<string> urls,string savepath, CancellationToken? token = null)
    {
        CancellationToken cancellationToken = token ?? CancellationToken.None;
        string temporaryPath = GetTemporaryDownloadPath(savepath);
        try
        {
            using (var httpStream = await HttpStreamGetter(urls, cancellationToken))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savepath)
                    ?? throw new OlanException("写入文件失败",$"下载前确定目标文件的父目录；请求URL url={string.Join(", ", urls)}；文件/目录 temporaryPath={temporaryPath}。无法找到文件'{savepath}'所对应的文件夹"));
                using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                {
                    await httpStream.CopyToAsync(fileStream, 8192,cancellationToken);
                }
            }
            File.Move(temporaryPath, savepath, true);
        }
        catch
        {
            DeleteTemporaryDownload(temporaryPath);
            throw;
        }
    }

    // 不同安装任务也可能下载同一目标；每次请求只写入和清理自己的临时文件。
    internal static string GetTemporaryDownloadPath(string savepath) => $"{savepath}.{Guid.NewGuid():N}.downloading";

    private static void DeleteTemporaryDownload(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // 清理失败不能覆盖原始下载异常。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理失败不能覆盖原始下载异常。
        }
    }
    public List<NdDowItem> CheckFilesExists(List<NdDowItem> FDI, CancellationToken token)
    {
        List<NdDowItem> filesToDownload = new List<NdDowItem>(FDI.Count);
        foreach (var item in FDI)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(item.path))
            {
                // 取消下载可能留下旧版本的残缺目标文件；长度明显不符时强制重新下载。
                if (item.size <= 0 || new FileInfo(item.path).Length == item.size)
                    continue;

                DeleteTemporaryDownload(item.path);
            }
            filesToDownload.Add(item);
        }
        return filesToDownload;
    }

    /// <summary>
    /// 由远端元数据生成下载项并做安全校验：地址必须是 http/https 绝对地址，
    /// 文件名必须是单个文件名（防止写出目标目录）。
    /// </summary>
    /// <param name="url">远端下载地址。</param>
    /// <param name="fileName">远端返回的文件名。</param>
    /// <param name="directory">保存目录。</param>
    /// <param name="size">文件大小（字节），超出 int 范围时截断。</param>
    /// <param name="sha1">SHA1，可为空。</param>
    /// <returns>校验不通过时返回 null，由调用方给出业务错误。</returns>
    public static NdDowItem? CreateSafeItem(string? url, string? fileName, string directory, long size, string? sha1)
    {
        if (!IsSafeRemoteUrl(url))
            return null;
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".."
            || Path.IsPathRooted(fileName) || fileName.Contains('/') || fileName.Contains('\\')
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return new NdDowItem(url!, Path.Combine(directory, fileName), (int)Math.Clamp(size, 0, int.MaxValue), sha1);
    }

    /// <summary>远端地址是否为 http/https 绝对地址；<paramref name="httpsOnly"/> 为 true 时只接受 https。</summary>
    internal static bool IsSafeRemoteUrl(string? url, bool httpsOnly = false)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (!httpsOnly && uri.Scheme == Uri.UriSchemeHttp));
}
