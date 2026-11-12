using OneLauncher.Core.DefaultComponent.Mod.ModLoader.fabric;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.Fabric.quilt;
using OneLauncher.Core.Global;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneLauncher.Core.DefaultComponent.Launcher.Strategys;
internal class QuiltStrategy : IModArgStrategy
{
    private readonly QuiltNJParser _quiltParser;

    private QuiltStrategy(QuiltNJParser quiltNJParser)
    {
        _quiltParser = quiltNJParser;
    }
    public static async Task<QuiltStrategy> CreateAsync(string versionPath, string basePath)
    {
        string quiltJsonPath = Path.Combine(versionPath, "version.quilt.json");
        await using var fs = File.OpenRead(quiltJsonPath);
        return new QuiltStrategy(new QuiltNJParser(
            await JsonSerializer.DeserializeAsync<FabricRoot>(fs, FabricJsonContext.Default.FabricRoot)
            ?? throw new OlanException("启动失败",$"反序列化 Quilt 启动配置文件；文件/目录 versionPath={versionPath}；文件/目录 basePath={basePath}；文件/目录 quiltJsonPath={quiltJsonPath}。在解析文件'{quiltJsonPath}'时出错"), basePath));
    }
    public string GetMainClassOverride() => _quiltParser.GetMainClass();

    public IDictionary<string, string> GetModLibraries() => _quiltParser.GetLibrariesForUsing();


    public IEnumerable<string> GetAdditionalJvmArgs() => Enumerable.Empty<string>();
    public IEnumerable<string> GetAdditionalGameArgs() => Enumerable.Empty<string>();
}
