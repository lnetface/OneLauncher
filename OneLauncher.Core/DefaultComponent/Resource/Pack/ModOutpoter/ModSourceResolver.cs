using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Pack.ModOutpoter;

/// <summary>
/// 导出整合包前识别 mods 目录中模组文件的公开下载来源。
/// 只按文件哈希精确反查（Modrinth SHA-512，其次 CurseForge 指纹），并用大小与 SHA-1 交叉校验；
/// 不根据文件名或模组 ID 推测地址。未命中的文件由调用方打包进 overrides。
/// </summary>
internal static class ModSourceResolver
{
    private const int HashBufferSize = 128 * 1024;

    /// <summary>一次读取得到的本地文件身份；只在一次导出中使用，不持久化。</summary>
    private readonly record struct LocalModHash(string Path, long Size, string Sha1, string Sha512);

    /// <summary>
    /// 扫描 <paramref name="modsDirectory"/> 顶层的 *.jar 与 *.jar.disabled 并反查来源。
    /// </summary>
    /// <param name="modsDirectory">实例的 mods 目录；不存在时返回空结果。</param>
    /// <param name="progress">哈希计算与在线查询进度（总数、已完成数、说明）；允许为 null。</param>
    /// <param name="cancellationToken">取消哈希计算和网络请求。</param>
    /// <returns>
    /// RemoteFiles：键为文件完整路径，值为可写入 modrinth.index.json 的条目（path 为 mods/&lt;文件名&gt;，保留 .disabled 后缀）；
    /// UnmatchedCount：未识别来源的模组数；Warnings：平台查询失败或被跳过的说明。
    /// 网络、限流和 API 错误只降级为警告，不会使导出失败；取消与文件读取错误照常抛出。
    /// </returns>
    internal static async Task<(IReadOnlyDictionary<string, ModrinthFile> RemoteFiles, int UnmatchedCount, IReadOnlyList<string> Warnings)> ResolveAsync(
        string modsDirectory,
        IProgress<(int TotalFiles, int CompletedFiles, string FileName)>? progress,
        CancellationToken cancellationToken = default)
    {
        var remoteFiles = new Dictionary<string, ModrinthFile>(StringComparer.Ordinal);
        var warnings = new List<string>();
        if (!Directory.Exists(modsDirectory))
            return (remoteFiles, 0, warnings);

        string[] modFiles = Directory.EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsModFile)
            .Select(Path.GetFullPath)
            .ToArray();
        if (modFiles.Length == 0)
            return (remoteFiles, 0, warnings);

        var localHashes = await HashFilesAsync(modFiles, progress, cancellationToken);

        // Modrinth：SHA-512 批量反查。
        progress?.Report((modFiles.Length, modFiles.Length, "正在从 Modrinth 识别模组来源..."));
        await TryResolveAsync(
            "Modrinth",
            () => ResolveFromModrinthAsync(localHashes, remoteFiles, cancellationToken),
            warnings,
            cancellationToken);

        // CurseForge：只处理 Modrinth 未命中的文件，未配置密钥时跳过。
        var unmatched = localHashes.Where(local => !remoteFiles.ContainsKey(local.Path)).ToList();
        if (unmatched.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(Init.CurseforgeApiKey))
            {
                warnings.Add($"未配置 CurseForge API Key，已跳过 CurseForge 识别；{unmatched.Count} 个模组将打包进整合包。");
            }
            else
            {
                progress?.Report((modFiles.Length, modFiles.Length, "正在从 CurseForge 识别模组来源..."));
                await TryResolveAsync(
                    "CurseForge",
                    () => ResolveFromCurseForgeAsync(unmatched, remoteFiles, cancellationToken),
                    warnings,
                    cancellationToken);
            }
        }

        return (remoteFiles, modFiles.Length - remoteFiles.Count, warnings);
    }

    private static bool IsModFile(string path)
        => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<LocalModHash>> HashFilesAsync(
        string[] files,
        IProgress<(int TotalFiles, int CompletedFiles, string FileName)>? progress,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<LocalModHash>();
        int completed = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };
        await Parallel.ForEachAsync(files, options, async (path, token) =>
        {
            results.Add(await HashFileAsync(path, token));
            int done = Interlocked.Increment(ref completed);
            progress?.Report((files.Length, done, $"正在识别模组来源：{Path.GetFileName(path)}"));
        });
        return [.. results];
    }

    /// <summary>一次读取同时计算大小、SHA-1 与 SHA-512（小写十六进制）。</summary>
    private static async Task<LocalModHash> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[HashBufferSize];
        long size = 0;

        await using (var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            HashBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                sha1.AppendData(buffer, 0, read);
                sha512.AppendData(buffer, 0, read);
                size += read;
            }
        }

        return new LocalModHash(
            path,
            size,
            Convert.ToHexStringLower(sha1.GetHashAndReset()),
            Convert.ToHexStringLower(sha512.GetHashAndReset()));
    }

    private static async Task ResolveFromModrinthAsync(
        IReadOnlyList<LocalModHash> locals,
        Dictionary<string, ModrinthFile> remoteFiles,
        CancellationToken cancellationToken)
    {
        var versions = await ModrinthApiClient.Shared.GetVersionsByHashesAsync(
            [.. locals.Select(local => local.Sha512)], cancellationToken);

        foreach (var local in locals)
        {
            if (!versions.TryGetValue(local.Sha512, out var version))
                continue;

            // 一个版本可能有主文件、源码等多个文件，必须按哈希找到同一个文件再核对大小与 SHA-1。
            var file = (version.Files ?? []).FirstOrDefault(candidate =>
                string.Equals(candidate.Hashes?.Sha512, local.Sha512, StringComparison.OrdinalIgnoreCase));
            if (file == null
                || file.Size != local.Size
                || !string.Equals(file.Hashes?.Sha1, local.Sha1, StringComparison.OrdinalIgnoreCase)
                || !Download.IsSafeRemoteUrl(file.Url, httpsOnly: true))
                continue;

            remoteFiles[local.Path] = CreateEntry(local, file.Url!);
        }
    }

    private static async Task ResolveFromCurseForgeAsync(
        IReadOnlyList<LocalModHash> locals,
        Dictionary<string, ModrinthFile> remoteFiles,
        CancellationToken cancellationToken)
    {
        // 指纹只为 Modrinth 未命中的文件计算，多数模组只需读取一次。
        var byFingerprint = new Dictionary<uint, List<LocalModHash>>();
        foreach (var local in locals)
        {
            uint fingerprint = await CurseForgeFingerprint.ComputeAsync(local.Path, cancellationToken);
            if (!byFingerprint.TryGetValue(fingerprint, out var list))
                byFingerprint[fingerprint] = list = [];
            list.Add(local);
        }

        var matches = await CurseForgeApiClient.Shared.GetFingerprintMatchesAsync(
            byFingerprint.Keys, cancellationToken);

        foreach (var file in matches.Select(match => match.File))
        {
            // downloadUrl 为空表示作者禁止第三方分发，只能打包本地文件。
            if (file == null
                || file.FileFingerprint is < 0 or > uint.MaxValue
                || !byFingerprint.TryGetValue((uint)file.FileFingerprint, out var candidates)
                || !Download.IsSafeRemoteUrl(file.DownloadUrl, httpsOnly: true))
                continue;

            foreach (var local in candidates)
            {
                if (remoteFiles.ContainsKey(local.Path)
                    || file.FileLength != local.Size
                    || !string.Equals(file.Sha1, local.Sha1, StringComparison.OrdinalIgnoreCase))
                    continue;

                remoteFiles[local.Path] = CreateEntry(local, file.DownloadUrl!);
            }
        }
    }

    /// <summary>清单记录本地重新计算的双哈希和大小，导入时据此校验下载结果。</summary>
    private static ModrinthFile CreateEntry(LocalModHash local, string url) => new()
    {
        Path = $"mods/{Path.GetFileName(local.Path)}",
        Hashes = new Dictionary<string, string>
        {
            ["sha1"] = local.Sha1,
            ["sha512"] = local.Sha512
        },
        Downloads = [url],
        FileSize = local.Size
    };

    /// <summary>平台查询失败只降级为警告：远程链接是体积优化，未命中的文件仍会完整打包。</summary>
    private static async Task TryResolveAsync(
        string platform,
        Func<Task> resolve,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await resolve();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException or OlanException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // OperationCanceledException 且令牌未取消时是请求超时。
            Debug.WriteLine($"[ModSourceResolver] {platform} 识别失败：{ex}");
            string reason = ex is OlanException olan ? olan.Title : ex.Message;
            warnings.Add($"无法从 {platform} 识别模组来源（{reason}），相关模组已打包进整合包。");
        }
    }
}
