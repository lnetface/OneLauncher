using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;

/// <summary>CurseForge 与启动器内部类型之间的映射。</summary>
internal static class CurseForgeMapping
{
    internal static CurseForgeModLoaderType? ToCurseForgeLoader(ModEnum loader) => loader switch
    {
        ModEnum.forge => CurseForgeModLoaderType.Forge,
        ModEnum.fabric => CurseForgeModLoaderType.Fabric,
        ModEnum.quilt => CurseForgeModLoaderType.Quilt,
        ModEnum.neoforge => CurseForgeModLoaderType.NeoForge,
        _ => null
    };

    internal static int ToClassId(ResourceType resourceType) => resourceType switch
    {
        ResourceType.Mod => CurseForgeIds.ModsClassId,
        ResourceType.ShaderPack => CurseForgeIds.ShadersClassId,
        ResourceType.Modpack => CurseForgeIds.ModpacksClassId,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType))
    };

    /// <summary>项目类别对应的实例内目录，未知类别按模组处理。</summary>
    internal static string ToInstanceDirectory(int? classId) => classId switch
    {
        CurseForgeIds.ResourcePacksClassId => "resourcepacks",
        CurseForgeIds.ShadersClassId => "shaderpacks",
        _ => "mods"
    };

    /// <summary>
    /// 将 manifest.json 中形如 "forge-47.2.0" 的加载器 ID 转为 .mrpack 的 dependencies 键值。
    /// 无法识别时返回原 ID 作为键，由 MrpackParser 报告“加载器不兼容”。
    /// </summary>
    internal static KeyValuePair<string, string> ToMrpackLoaderDependency(string loaderId)
    {
        int separator = loaderId.IndexOf('-');
        if (separator <= 0 || separator == loaderId.Length - 1)
            return new($"{loaderId}-loader", string.Empty);

        string name = loaderId[..separator].ToLowerInvariant();
        string version = loaderId[(separator + 1)..];
        return name switch
        {
            "forge" => new("forge", version),
            "neoforge" => new("neoforge", version),
            "fabric" => new("fabric-loader", version),
            "quilt" => new("quilt-loader", version),
            _ => new($"{name}-loader", version)
        };
    }

    /// <summary>CurseForge 项目 ID 为整数；前端以字符串传递，入口统一解析。</summary>
    internal static int ParseProjectId(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        if (!int.TryParse(projectId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) || id <= 0)
            throw new ArgumentException($"CurseForge 项目 ID 必须是正整数：{projectId}", nameof(projectId));
        return id;
    }

    /// <summary>
    /// 从候选文件中选择最新可用文件：排除不可用文件与服务端包，按发布时间倒序取第一个。
    /// 与 Modrinth 一致，不区分正式版与测试版。
    /// </summary>
    internal static CurseForgeFile? SelectLatestFile(IEnumerable<CurseForgeFile> files)
        => files
            .Where(file => file.IsAvailable && file.IsServerPack != true)
            .MaxBy(file => file.FileDate);
}
