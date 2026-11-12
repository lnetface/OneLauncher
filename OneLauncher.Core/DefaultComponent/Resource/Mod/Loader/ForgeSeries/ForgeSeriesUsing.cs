using System.Text;
using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader;
using OneLauncher.Core.Global;
namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries;
public class ForgeSeriesUsing
{
    public ForgeSeriesVersionJson info = null!;
    public async Task Init(string basePath, string version,bool isForge)
    {
        string jsonPath = Installation.ForgePaths.Under(basePath, $"versions/{version}/version.{(isForge ? "forge" : "neoforge")}.json");
        using var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        info = await JsonSerializer.DeserializeAsync(fs, ForgeSeriesJsonContext.Default.ForgeSeriesVersionJson)
            ?? throw new OlanException("加载器配置无效", $"读取并反序列化加载器版本配置；文件/目录 basePath={basePath}；文件/目录 jsonPath={jsonPath}。无法读取 {jsonPath}");
        if (string.IsNullOrWhiteSpace(info.MainClass) || info.Libraries == null ||
            (info.InheritsFrom != null && info.InheritsFrom != version))
            throw new OlanException("加载器配置无效", $"校验加载器配置的 mainClass/libraries/inheritsFrom；文件/目录 basePath={basePath}；文件/目录 jsonPath={jsonPath}。主类、运行库或继承版本无效：{jsonPath}");
        var identity = ModLoaderDetector.Detect(info.MainClass, info.Libraries.Select(l => l.Name),
            info.Arguments == null ? Launch.ForgeLaunchArguments.Tokenize(info.MinecraftArguments ?? "", jsonPath + ": minecraftArguments") : Launch.ForgeLaunchArguments.Expand(info.Arguments.Game, jsonPath + ": arguments.game"), jsonPath);
        if (identity != (isForge ? Helper.Models.ModEnum.forge : Helper.Models.ModEnum.neoforge))
            throw new OlanException("加载器配置不匹配", $"核对加载器配置身份与启动请求；文件/目录 basePath={basePath}；文件/目录 jsonPath={jsonPath}。配置中的加载器身份与请求不一致：{jsonPath}");
    }
    /// <summary>
    /// 获取当前Forge/NeoForge的依赖库列表。
    /// </summary>
    /// <param name="LibBasePath">游戏根目录</param>
    /// <returns>一个以 "groupId:artifactId" 为键，库文件完整路径为值的字典。</returns>
    public Dictionary<string, string> GetLibrariesForLaunch(string LibBasePath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in info.Libraries.Where(l => l.ClientRequired != false && Launch.ForgeLaunchArguments.Allowed(l.Rules)))
        {
            var mavenPath = Installation.ForgePaths.RelativeMaven(item.Name);
            var parts = item.Name.Split('@')[0].Split(':');
            var relative = item.Downloads?.Artifact?.Path is { Length: > 0 } path ? path : mavenPath;
            var key = parts.Length >= 4 ? $"{parts[0]}:{parts[1]}:{parts[3]}" : $"{parts[0]}:{parts[1]}";
            if (item.Name.Contains('@') && !item.Name.EndsWith("@jar", StringComparison.Ordinal)) key += "@" + item.Name.Split('@')[1];
            var library = Installation.ForgePaths.Under(Path.Combine(LibBasePath, "libraries"), relative);
            if (!File.Exists(library)) throw new OlanException("加载器依赖不完整", $"构建启动 classpath 并检查加载器库文件；文件/目录 LibBasePath={LibBasePath}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}。缺少运行库：{library}，请重新安装加载器。");
            result[key] = library;
        }
        return result;
    }
}
