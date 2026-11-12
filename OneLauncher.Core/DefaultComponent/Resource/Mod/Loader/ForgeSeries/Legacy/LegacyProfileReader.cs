using System.Text.Json;
using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Legacy;

internal static class LegacyProfileReader
{
    public static (ForgeSeriesVersionJson Version, string Json, string Minecraft) Read(
        ForgeSeriesInstallProfile profile, JsonElement root, string diagnosticSource = "内存安装包（未提供来源URL）")
    {
        if (profile.Install == null || profile.VersionInfo == null || string.IsNullOrEmpty(profile.Install.Minecraft))
            throw new OlanException("Forge 安装配置无效", $"读取旧版 install_profile.json 的 install/versionInfo 字段；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。旧安装包缺少 install 或 versionInfo。");
        return (profile.VersionInfo, root.GetProperty("versionInfo").GetRawText(), profile.Install.Minecraft);
    }
}
