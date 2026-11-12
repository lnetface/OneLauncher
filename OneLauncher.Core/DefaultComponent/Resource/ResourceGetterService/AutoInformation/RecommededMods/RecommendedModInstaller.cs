using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Text;

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
        var modsGetterServices = mods
            .Select(mod => 
                new GetModrinth(
                    mod,
                    gameData.VersionId, 
                    gameData.ModLoader, 
                    Path.Combine(gameData.InstancePath, "mods")))
            .ToList();
        await Task.WhenAll(modsGetterServices.Select(async service =>
            {
                try
                {
                    await service.Init(token);
                }
                catch (OlanException ex)
                {
                    if (ex.Title != "找不到模组")
                        throw;
                }
            }));
        List<NdDowItem> items = new List<NdDowItem>();
        foreach (var service in modsGetterServices)
        {
            var main = service.GetDownloadInfos();
            if (!main.HasValue)
                continue;

            items.Add(main.Value);
            items.AddRange(service.GetDependenciesInfos() ?? []);
        }
        // 共同依赖和重复推荐项可能指向同一文件，批量下载前统一去重。
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        items = items.DistinctBy(item => Path.GetFullPath(item.path), pathComparer).ToList();
        await Init.Download.DownloadListAsync(new Progress<(int c, string f)>(p =>
        {
            progress?.Report((p.c, p.f));
        }), items, Init.ConfigManager.Data.OlanSettings.MaximumDownloadThreads, token);
    }
}
