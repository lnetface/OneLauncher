using System.IO.Compression;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Resource.Pack;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>
/// 下载 CurseForge 整合包的最新文件，并转换为等价的 .mrpack 临时文件，
/// 使预览侧栏和 ModpackImporter 无需区分来源即可复用同一条导入流程。
/// </summary>
internal static class GetModpackCurseForge
{
    private const string ManifestEntryName = "manifest.json";
    private const string MrpackManifestEntryName = "modrinth.index.json";
    private const string MrpackOverridesDirectory = "overrides/";

    /// <summary>
    /// 获取整合包。返回值持有转换后的临时 .mrpack 文件，调用方负责释放。
    /// </summary>
    /// <param name="projectId">CurseForge 项目 ID（整数字符串）。</param>
    /// <param name="cancellationToken">取消元数据请求、下载、校验与转换。</param>
    /// <exception cref="OlanException">没有可安装文件、清单无效，或包内存在不允许第三方下载的文件。</exception>
    internal static async Task<ModpackDownload> GetAsync(string projectId, CancellationToken cancellationToken = default)
    {
        int modId = CurseForgeMapping.ParseProjectId(projectId);
        cancellationToken.ThrowIfCancellationRequested();

        var api = CurseForgeApiClient.Shared;
        string url = CurseForgeApiClient.BuildModFilesUrl(modId, gameVersion: null, modLoaderType: null);
        string context = $"参数 projectId={projectId}；请求URL url={url}";
        var files = await api.GetModFilesAsync(modId, gameVersion: null, modLoaderType: null, cancellationToken);
        var packFile = CurseForgeMapping.SelectLatestFile(files)
            ?? throw new OlanException("获取整合包失败", $"筛选 CurseForge 可安装整合包文件；{context}。项目 {projectId} 没有可安装的整合包文件。");
        if (!Uri.TryCreate(packFile.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp))
            throw new OlanException("获取整合包失败", $"选择 CurseForge 整合包下载文件；{context}。整合包作者不允许第三方启动器下载，请前往 CurseForge 网站手动下载。", OlanExceptionAction.Warning);

        // CurseForge 整合包需要随机读取 manifest.json 与覆盖目录，先写入唯一临时文件。
        string sourcePath = Path.Combine(Path.GetTempPath(), $"olan-pack-{Guid.NewGuid():N}.zip");
        string? mrpackPath = null;
        try
        {
            using (var response = await Init.Download.UnityClient.GetAsync(
                downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous);
                await networkStream.CopyToAsync(destination, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Init.ConfigManager.Data.OlanSettings.IsSha1Enabled && !string.IsNullOrWhiteSpace(packFile.Sha1))
                await Init.Download.CheckAllSha1(
                    [new NdDowItem(packFile.DownloadUrl!, sourcePath, (int)Math.Clamp(packFile.FileLength, 0, int.MaxValue), packFile.Sha1)],
                    1, cancellationToken);

            context = $"{context}；文件/目录 sourcePath={sourcePath}；下载URL={packFile.DownloadUrl}";
            using var source = ZipFile.OpenRead(sourcePath);
            var manifest = ReadManifest(source, context);
            var mrpackManifest = await BuildMrpackManifestAsync(api, manifest, context, cancellationToken);

            mrpackPath = Path.Combine(Path.GetTempPath(), $"olan-pack-{Guid.NewGuid():N}.mrpack");
            await WriteMrpackAsync(source, manifest, mrpackManifest, mrpackPath, cancellationToken);

            using var archive = ZipFile.OpenRead(mrpackPath);
            using var manifestStream = archive.GetEntry(MrpackManifestEntryName)!.Open();
            var parser = new MrpackParser(manifestStream, $"{mrpackPath}；CurseForge 来源={packFile.DownloadUrl}");
            // 提前读取侧栏与导入必需的信息，解析失败时清理临时文件。
            parser.GetMinecraftVersion();
            parser.GetLoaderInfo();
            cancellationToken.ThrowIfCancellationRequested();
            return new ModpackDownload(mrpackPath, parser);
        }
        catch
        {
            if (mrpackPath != null)
                ModpackDownload.DeleteTemporaryFile(mrpackPath);
            throw;
        }
        finally
        {
            ModpackDownload.DeleteTemporaryFile(sourcePath);
        }
    }

    private static CurseForgeModpackManifest ReadManifest(ZipArchive source, string context)
    {
        var entry = source.GetEntry(ManifestEntryName)
            ?? throw new OlanException("整合包无效", $"打开下载的 CurseForge 整合包并定位清单；{context}。压缩包内未找到 {ManifestEntryName} 文件。");
        try
        {
            using var stream = entry.Open();
            var manifest = JsonSerializer.Deserialize(stream, CurseForgeJsonContext.Default.CurseForgeModpackManifest)
                ?? throw new JsonException($"反序列化 {ManifestEntryName} 返回 null。");
            if (string.IsNullOrWhiteSpace(manifest.Minecraft?.Version))
                throw new OlanException("清单格式错误", $"读取 {ManifestEntryName} 的 minecraft.version 字段；{context}。清单缺少 Minecraft 版本。");
            return manifest;
        }
        catch (JsonException ex)
        {
            throw new OlanException("解析失败", $"反序列化 CurseForge 整合包的 {ManifestEntryName}；{context}。文件可能已损坏或格式不正确。", OlanExceptionAction.Error, ex);
        }
    }

    /// <summary>批量查询清单中的文件与项目，转换为 .mrpack 清单。</summary>
    private static async Task<ModrinthManifest> BuildMrpackManifestAsync(
        CurseForgeApiClient api,
        CurseForgeModpackManifest manifest,
        string context,
        CancellationToken cancellationToken)
    {
        // 非必需（作者标记为可选/禁用）的文件不安装。
        var entries = (manifest.Files ?? []).Where(file => file.Required && file.FileId > 0).ToList();
        var fileInfos = (await api.GetFilesAsync([.. entries.Select(file => file.FileId)], cancellationToken))
            .DistinctBy(file => file.Id)
            .ToDictionary(file => file.Id);
        // 项目类别决定文件放入 mods、resourcepacks 还是 shaderpacks。
        var classIds = (await api.GetModsAsync([.. entries.Select(file => file.ProjectId)], cancellationToken))
            .DistinctBy(mod => mod.Id)
            .ToDictionary(mod => mod.Id, mod => mod.ClassId);
        cancellationToken.ThrowIfCancellationRequested();

        var mrpackFiles = new List<ModrinthFile>(entries.Count);
        var missing = new List<string>();
        var blocked = new List<string>();
        foreach (var entry in entries)
        {
            if (!fileInfos.TryGetValue(entry.FileId, out var file))
            {
                missing.Add($"{entry.ProjectId}/{entry.FileId}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(file.DownloadUrl))
            {
                blocked.Add(file.DisplayName ?? file.FileName ?? $"{entry.ProjectId}/{entry.FileId}");
                continue;
            }

            // 用统一的下载项校验检查地址与文件名，再转成 .mrpack 的相对路径。
            string directory = CurseForgeMapping.ToInstanceDirectory(classIds.GetValueOrDefault(entry.ProjectId));
            var item = Download.CreateSafeItem(file.DownloadUrl, file.FileName, directory, file.FileLength, file.Sha1)
                ?? throw new OlanException("整合包无效", $"转换 CurseForge 整合包文件；{context}；参数 fileId={entry.FileId}；文件/目录 filename={file.FileName}。CurseForge 返回了无效的下载地址或文件名。");
            var hashes = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(item.sha1))
                hashes["sha1"] = item.sha1;
            mrpackFiles.Add(new ModrinthFile
            {
                Path = $"{directory}/{file.FileName}",
                Downloads = [item.url],
                Hashes = hashes,
                FileSize = item.size
            });
        }

        // 缺失文件会让整合包不完整，宁可明确失败也不静默安装残缺实例。
        if (blocked.Count > 0)
        {
            throw new OlanException(
                "整合包无法自动安装",
                $"转换 CurseForge 整合包文件列表；{context}。以下 {blocked.Count} 个文件的作者不允许第三方启动器下载：{FormatList(blocked)}。请前往 CurseForge 网站手动下载该整合包。",
                OlanExceptionAction.Warning);
        }
        if (missing.Count > 0)
        {
            throw new OlanException(
                "整合包无效",
                $"转换 CurseForge 整合包文件列表；{context}。CurseForge 未返回以下文件（项目ID/文件ID）：{FormatList(missing)}。");
        }

        var dependencies = new Dictionary<string, string> { ["minecraft"] = manifest.Minecraft!.Version! };
        var loaders = manifest.Minecraft.ModLoaders ?? [];
        var loader = loaders.FirstOrDefault(item => item.Primary) ?? loaders.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(loader?.Id))
        {
            var (key, version) = CurseForgeMapping.ToMrpackLoaderDependency(loader.Id);
            dependencies[key] = version;
        }

        return new ModrinthManifest
        {
            FormatVersion = 1,
            Game = "minecraft",
            VersionId = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0.0" : manifest.Version,
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? "CurseForge 整合包" : manifest.Name,
            Dependencies = dependencies,
            Files = mrpackFiles
        };
    }

    /// <summary>写出 .mrpack：清单 + 将原包覆盖目录下的条目复制到 overrides/。</summary>
    private static async Task WriteMrpackAsync(
        ZipArchive source,
        CurseForgeModpackManifest manifest,
        ModrinthManifest mrpackManifest,
        string mrpackPath,
        CancellationToken cancellationToken)
    {
        string overridesPrefix = (string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides" : manifest.Overrides)
            .Replace('\\', '/').Trim('/') + "/";

        await using var destinationStream = new FileStream(
            mrpackPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create);

        var manifestEntry = destination.CreateEntry(MrpackManifestEntryName, CompressionLevel.Optimal);
        await using (var manifestStream = manifestEntry.Open())
            await JsonSerializer.SerializeAsync(manifestStream, mrpackManifest, MrpackJsonContext.Default.ModrinthManifest, cancellationToken);

        foreach (var entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(overridesPrefix, StringComparison.Ordinal) || name.Length == overridesPrefix.Length)
                continue;

            // 目录条目以 / 结尾，只需创建同名条目；导入时 ExtractToDirectory 会拒绝越界路径。
            var target = destination.CreateEntry(MrpackOverridesDirectory + name[overridesPrefix.Length..], CompressionLevel.Optimal);
            if (name.EndsWith('/'))
                continue;
            await using var input = entry.Open();
            await using var output = target.Open();
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static string FormatList(IReadOnlyList<string> items)
        => items.Count <= 10 ? string.Join("、", items) : $"{string.Join("、", items.Take(10))} 等";
}
