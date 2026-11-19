using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.Fabric.quilt;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders.ModSources;
internal class QuiltProvider : IModLoaderConcreteProviders
{
    private readonly DownloadInfo _context;
    public QuiltProvider(DownloadInfo context)
    {
        _context = context;
    }
    public async Task<List<NdDowItem>> GetDependencies(Action<string>? reportStatus = null)
    {
        reportStatus?.Invoke("正在获取 Quilt 加载器元数据...");
        List<NdDowItem> quiltDependencies;
        string quiltMetaFilePath = Path.Combine(
            _context.VersionInstallInfo.VersionPath,
            "version.quilt.json"
        );

        // 从 Quilt Meta 获取加载器信息
        using Stream rep = await Init.Download.UnityClient
            .GetStreamAsync($"https://meta.quiltmc.org/v3/versions/loader/{_context.ID}");
        using JsonDocument document = JsonDocument.Parse(rep);
        JsonElement selectedElement = SelectLoader(document.RootElement);
        var info = selectedElement.Deserialize(FabricJsonContext.Default.FabricRoot)
        ?? throw new OlanException("内部错误", $"读取 Quilt loader 元数据并选择兼容加载器；文件/目录 quiltMetaFilePath={quiltMetaFilePath}；请求URL={$"https://meta.quiltmc.org/v3/versions/loader/{_context.ID}"}。无法解析Quilt元数据");

        // 写入到文件
        using (FileStream fs = new FileStream(quiltMetaFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 0, true))
            await JsonSerializer.SerializeAsync<FabricRoot>(fs, info, FabricJsonContext.Default.FabricRoot);

        var parser = new QuiltNJParser(info, _context.GameRootPath);

        quiltDependencies = parser.GetLibraries();

        // Quilt 同样可以有 API (Quilted Fabric API / QSL)
        if (_context.ISDownloadQuiltWhitQSL) // 复用这个选项
        {
            reportStatus?.Invoke($"已收集 {quiltDependencies.Count} 个 Quilt 库条目，正在查询 QSL 及其依赖信息...");
            // QSL 只是附加内容：没有兼容版本或元数据请求失败时跳过，不中断版本安装。
            try
            {
                var quiltApiFile = await GetModModrinth.GetAsync(
                    "qsl", // Quilt Standard Libraries (QSL)
                    _context.ID,
                    ModEnum.quilt,
                    Path.Combine(_context.UserInfo.FirstOrDefault(x => x.ModLoader == ModEnum.quilt)!.InstancePath, "mods"),
                    includeDependencies: false);
                quiltDependencies.AddRange(quiltApiFile);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OlanException)
            {
                System.Diagnostics.Debug.WriteLine($"[QuiltProvider] 获取 QSL 失败，已跳过：{ex.Message}");
            }
        }
        return quiltDependencies;
    }

    // Quilt Meta 的返回顺序不是版本顺序，优先选择最高稳定版，没有稳定版时才使用接口首项。
    private JsonElement SelectLoader(JsonElement loaders)
    {
        JsonElement? fallback = null;
        JsonElement selected = default;
        Version? selectedVersion = null;
        foreach (var item in loaders.EnumerateArray())
        {
            fallback ??= item;
            string? version = item.GetProperty("loader").GetProperty("version").GetString();
            if (version == null || version.Contains('-') || !Version.TryParse(version, out var parsed))
                continue;
            if (selectedVersion == null || parsed > selectedVersion)
            {
                selectedVersion = parsed;
                selected = item;
            }
        }
        return selectedVersion != null
            ? selected
            : fallback ?? throw new OlanException("下载失败", $"读取 Quilt loader 元数据并选择兼容加载器；请求URL={$"https://meta.quiltmc.org/v3/versions/loader/{_context.ID}"}。Quilt 未提供 Minecraft {_context.ID} 可用的加载器");
    }
}
