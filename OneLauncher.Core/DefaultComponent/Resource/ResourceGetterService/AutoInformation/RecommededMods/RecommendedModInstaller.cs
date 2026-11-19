using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;
internal class RecommendedModInstaller
{
    internal RecommendedModInstaller(GameData gameData)
    {
        this.gameData = gameData;
        mods = Init.ConfigManager.Data.DefaultSettings.RecommandedMods;
    }
    private readonly GameData gameData;
    private readonly List<string> mods;
    internal async Task InstallAsync(IProgress<(int current,string file)> progress,CancellationToken token = default)
    {
        string modsDirectory = Path.Combine(gameData.InstancePath, "mods");
        // 没有适配当前版本/加载器的推荐模组返回空列表，直接跳过。
        var resolved = await Task.WhenAll(mods.Select(mod => ResolveAsync(mod, modsDirectory, token)));
        // 共同依赖和重复推荐项指向同一文件时，由 DownloadListAsync 统一按路径去重。
        List<NdDowItem> items = resolved.SelectMany(files => files).ToList();
        await Init.Download.DownloadListAsync(new Progress<(int c, string f)>(p =>
        {
            progress?.Report((p.c, p.f));
        }), items, Init.ConfigManager.Data.OlanSettings.MaximumDownloadThreads, token);
    }

    // 推荐模组是可选增强：单个项目的元数据异常只跳过该项目，不影响其余推荐模组。
    private async Task<List<NdDowItem>> ResolveAsync(string projectId, string modsDirectory, CancellationToken token)
    {
        try
        {
            return await GetModModrinth.GetAsync(
                projectId, gameData.VersionId, gameData.ModLoader, modsDirectory, cancellationToken: token);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OlanException)
        {
            System.Diagnostics.Debug.WriteLine($"[RecommendedModInstaller] 跳过推荐模组 {projectId}：{ex.Message}");
            return [];
        }
    }
}
