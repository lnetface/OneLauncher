using System.IO.Compression;
using System.Text.Json;
using OneLauncher.Core.Global;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Profiles;

internal static class ProfileReader
{
    public static (ForgeSeriesVersionJson Version, string Json, string Minecraft) Read(
        ForgeSeriesInstallProfile profile, ZipArchive archive, string diagnosticSource = "内存安装包（未提供来源URL）")
    {
        if (profile.Spec is not (0 or 1) || string.IsNullOrEmpty(profile.Minecraft) || string.IsNullOrEmpty(profile.Json))
            throw new OlanException("Forge 安装配置不受支持", $"检查 install_profile.json 的 spec/minecraft/json 字段；来源安装包/URL={diagnosticSource}；版本配置条目={profile.Json}。仅支持 spec 0/1 的客户端 profile（实际 spec={profile.Spec}）。");
        var entry = archive.GetEntry(ForgePaths.EntryName(profile.Json))
            ?? throw new OlanException("Forge 安装包损坏", $"根据 profile.json 定位归档中的版本配置；来源安装包/URL={diagnosticSource}；版本配置条目={profile.Json}。找不到版本配置 {profile.Json}。");
        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        var version = JsonSerializer.Deserialize(json, ForgeSeriesJsonContext.Default.ForgeSeriesVersionJson)
            ?? throw new OlanException("Forge 安装配置无效", $"反序列化安装包中的版本配置 JSON；来源安装包/URL={diagnosticSource}；版本配置条目={profile.Json}。无法读取版本配置。");
        return (version, json, profile.Minecraft);
    }
}
