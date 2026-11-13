using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneLauncher.Core.Global.Updater;

[JsonSerializable(typeof(ResourceManifest))]
internal partial class ResourceManifestJsonContext : JsonSerializerContext { }

// resource_manifest.json 根对象；__TEXT__ 为说明文字，反序列化时跳过
internal class ResourceManifest
{
    // 多源时取最大者
    public int? Revision { get; set; }
    // 低于当前启动器版本时忽略整份清单
    public string? LatestLauncherVersion { get; set; }
    // 结构未定，先按任意 JSON 接收，避免以后改成对象时旧版本解析失败
    public JsonElement? Announcement { get; set; }
    public ManifestResources? Resources { get; set; }
    public ManifestFunction? Function { get; set; }
    // 推荐模组的 Modrinth 项目 ID
    public List<string>? RecommendedMods { get; set; }
    // Java 安装面板中可选择下载的 Java 版本
    public List<int>? JavaVersions { get; set; }
}

// 每项为按顺序尝试的候选地址；null 或空列表表示忽略
internal class ManifestResources
{
    public List<string>? WindowsUpdater { get; set; }
    public List<string>? ConnectCore { get; set; }
    // 地址模板，${UUID} 为玩家 UUID 占位符
    public List<string>? SkinBodyRender { get; set; }
}

// null 表示忽略
internal class ManifestFunction
{
    public bool? AllowOfflineLogin { get; set; }
    public bool? AllowYggdrasilLogin { get; set; }
}
