using OneLauncher.Core.Helper.Models;
using System.Collections.Generic;
using System.Linq;

namespace OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders.DownloadSources;
/// <summary>
/// SkyCrystal API 加速源，文档见 https://api.skycrystal.fun
/// </summary>
internal class SkyCrystalSourceDownloadUrlGetter(string version) : IDownloadSourceUrlProvider
{
    const string SKYCRYSTAL_BASE_URL = "https://api.skycrystal.fun/mc/java/";
    readonly string versionId = version;

    public NdDowItem GetClientMainFile(NdDowItem basic)
    {
        string mirrorUrl = $"{SKYCRYSTAL_BASE_URL}client/{versionId}";
        return new NdDowItem(mirrorUrl, basic.path, basic.size, basic.sha1);
    }

    public IEnumerable<NdDowItem> GetAssetsFiles(IEnumerable<NdDowItem> basic)
    {
        return basic.Select(x => new NdDowItem(
            x.url.Replace("https://resources.download.minecraft.net/", $"{SKYCRYSTAL_BASE_URL}assets/"),
            x.path, x.size, x.sha1
        ));
    }

    public IEnumerable<NdDowItem> GetLibrariesFiles(IEnumerable<NdDowItem> basic)
    {
        return basic.Select(x => new NdDowItem(
            x.url.Replace("https://libraries.minecraft.net/", $"{SKYCRYSTAL_BASE_URL}library/"),
            x.path, x.size, x.sha1
        ));
    }
}
