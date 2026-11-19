using OneLauncher.Core.DefaultComponent.Resource;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.Pack;
using OneLauncher.Core.DefaultComponent.Resource.Pack.ModOutpoter;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;

namespace OneLauncher.Core.Services;

public sealed class ResourceInstallerService
{
    internal ResourceInstallerService()
    {
    }
    public Task InstallRecommendedMods(IProgress<(int current,string file)> progress, GameData data,CancellationToken token)
    {
        return new RecommendedModInstaller(data).InstallAsync(new Progress<(int c,string f)>(p =>
        {
            progress?.Report((p.c, p.f));
        }),token);
    }

    /// <summary>获取 Modrinth 最新 .mrpack 并解析信息；调用方负责释放返回值。</summary>
    public Task<ModpackDownload> GetModpackInfoAsync(string projectId, CancellationToken cancellationToken = default)
        => GetModpackInfoAsync(projectId, ResourceSource.Modrinth, cancellationToken);

    /// <summary>
    /// 获取指定平台整合包的最新文件并解析信息；调用方负责释放返回值。
    /// CurseForge 整合包会被转换为等价的 .mrpack，后续导入流程与 Modrinth 相同。
    /// </summary>
    public Task<ModpackDownload> GetModpackInfoAsync(
        string projectId,
        ResourceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        cancellationToken.ThrowIfCancellationRequested();
        return source switch
        {
            ResourceSource.Modrinth => GetModpackModrinth.GetAsync(projectId, cancellationToken),
            ResourceSource.CurseForge => GetModpackCurseForge.GetAsync(projectId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    /// <summary>复用预览时的包文件及解析器导入新实例，取消令牌沿既有安装流程传入 DownloadMinecraft。</summary>
    public async Task<GameData> ImportModpack(
        ModpackDownload package,
        IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        return await ModpackImporter.ImportFromMrpackAsync(
            package.FilePath, Init.GameRootPath, progress, cancellationToken, package.Parser);
    }

    /// <summary>
    /// 将实例导出为 .mrpack，输出到实例根目录（同名文件会被覆盖）。
    /// mods 目录中能按哈希在 Modrinth / CurseForge 精确识别的模组写入清单下载链接，
    /// 无法识别的模组与其他实例文件一起打包进 overrides。
    /// </summary>
    /// <param name="instance">要导出的实例。</param>
    /// <param name="progress">（总数、已完成数、当前文件或阶段说明）；先报告来源识别，再报告打包。允许为 null。</param>
    /// <param name="cancellationToken">取消哈希计算、在线查询与打包；取消时删除临时文件。</param>
    /// <returns>输出路径、在线下载与本地打包的模组数，以及来源识别的降级提示。</returns>
    /// <exception cref="OneLauncher.Core.Global.OlanException">实例目录、加载器元数据缺失或写入失败。在线识别失败不会抛出，只记入 Warnings。</exception>
    public Task<ModpackExportResult> ExportModpackAsync(
        GameData instance,
        IProgress<(int TotalFiles, int CompletedFiles, string FileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        cancellationToken.ThrowIfCancellationRequested();
        return ModpackExporter.ExportAsync(instance, progress, cancellationToken);
    }

    /// <summary>
    /// 安装适配实例 Minecraft 版本的最新光影包到 shaderpacks。
    /// 不检查或安装 Iris、OptiFine 等依赖，也不自动启用光影包。
    /// </summary>
    /// <param name="progress">文件数量、百分比和阶段进度；允许为 null。</param>
    /// <param name="shaderPackId">光影项目 ID（Modrinth 可用 slug，CurseForge 为整数 ID）。</param>
    /// <param name="instance">用于确定 Minecraft 版本和安装目录的目标实例。</param>
    /// <param name="verifySha1">是否校验服务端提供的 SHA1，包括已存在的文件。</param>
    /// <param name="source">资源所属平台。</param>
    /// <param name="cancellationToken">取消元数据获取、下载及校验。</param>
    /// <exception cref="OneLauncher.Core.Global.OlanException">没有兼容版本或下载文件信息无效。</exception>
    public async Task InstallShaderPack(
        IProgress<(long AllFiles, long DownedFiles, double Percentage, string DowingFileName)>? progress,
        string shaderPackId,
        GameData instance,
        bool verifySha1 = true,
        ResourceSource source = ResourceSource.Modrinth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderPackId);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report((0, 0, 0, "正在获取光影包版本信息..."));
        string shaderPackPath = Path.Combine(instance.InstancePath, "shaderpacks");
        var file = source switch
        {
            ResourceSource.Modrinth => await GetShaderPackModrinth.GetDownloadInfoAsync(
                shaderPackId, instance.VersionId, shaderPackPath, cancellationToken),
            ResourceSource.CurseForge => await GetShaderPackCurseForge.GetDownloadInfoAsync(
                shaderPackId, instance.VersionId, shaderPackPath, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        await ResourceFileInstaller.InstallAsync(
            [file], progress, "光影包", verifySha1, cancellationToken);
    }

    /// <summary>
    /// 安装适配实例 Minecraft 版本与加载器的最新模组到 mods，可选一并安装一级必需依赖。
    /// </summary>
    /// <param name="progress">文件数量、百分比和阶段进度；允许为 null。</param>
    /// <param name="modId">模组项目 ID（Modrinth 可用 slug，CurseForge 为整数 ID）。</param>
    /// <param name="instance">目标实例。</param>
    /// <param name="modType">按此加载器筛选模组版本。</param>
    /// <param name="includeDependencies">是否安装必需依赖。</param>
    /// <param name="verifySha1">是否校验服务端提供的 SHA1。</param>
    /// <param name="source">资源所属平台。</param>
    /// <param name="cancellationToken">取消元数据获取、下载及校验。</param>
    /// <exception cref="OneLauncher.Core.Global.OlanException">没有兼容版本或下载文件信息无效。</exception>
    public async Task InstallMod(
        IProgress<(long AllFiles, long DownedFiles,double Percentage, string DowingFileName)>? progress,
        string modId,
        GameData instance,
        ModEnum modType,
        bool includeDependencies = true,
        bool verifySha1 = true,
        ResourceSource source = ResourceSource.Modrinth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        cancellationToken.ThrowIfCancellationRequested();

        var modPath = Path.Combine(instance.InstancePath, "mods");
        var files = source switch
        {
            ResourceSource.Modrinth => await GetModModrinth.GetAsync(
                modId, instance.VersionId, modType, modPath, includeDependencies, cancellationToken),
            ResourceSource.CurseForge => await GetModCurseForge.GetAsync(
                modId, instance.VersionId, modType, modPath, includeDependencies, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        if (files.Count == 0)
            throw new OlanException(
                "安装模组失败", $"查询并筛选 {source} 模组兼容版本；参数 modId={modId}；参数 modType={modType}；文件/目录 modPath={modPath}；Minecraft={instance.VersionId}。未找到项目 {modId} 适配 Minecraft {instance.VersionId} 和 {modType} 的模组版本。");

        await ResourceFileInstaller.InstallAsync(
            files, progress, "Mod", verifySha1, cancellationToken);
    }
}
