using System.IO.Compression;
using System.Text.Json;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Processors;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.JarMod;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Launch;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Legacy;
using OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Profiles;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.Installation;

internal static class ForgeInstallerParser
{
    public static ForgePreparedInstall Parse(byte[] bytes, string modType, string minecraft, string libraries, string diagnosticSource = "内存安装包（未提供来源URL）")
    {
        if (modType is not ("Forge" or "NeoForge")) throw new OlanException("安装配置无效", $"打开安装包前检查加载器类型；参数 modType={modType}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。未知的加载器类型。");
        ForgePaths.Under(Path.GetTempPath(), minecraft);
        using var memory = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var entry = archive.GetEntry("install_profile.json");
        if (entry == null)
        {
            if (archive.GetEntry("mod_MinecraftForge.class") != null) throw JarModSupport.Unsupported($"{diagnosticSource}；归档条目=mod_MinecraftForge.class；Minecraft={minecraft}");
            throw new OlanException("Forge 安装包损坏", $"打开安装包并定位 install_profile.json；参数 modType={modType}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。缺少 install_profile.json。");
        }
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();
        var profile = JsonSerializer.Deserialize(text, ForgeSeriesJsonContext.Default.ForgeSeriesInstallProfile)
            ?? throw new OlanException("安装配置无效", $"反序列化 install_profile.json；参数 modType={modType}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。无法读取 install_profile.json。");
        using var document = JsonDocument.Parse(text);
        var legacy = profile.Install != null || profile.VersionInfo != null;
        var (version, json, actualMc) = legacy ? LegacyProfileReader.Read(profile, document.RootElement, diagnosticSource) : ProfileReader.Read(profile, archive, diagnosticSource);
        if (minecraft != actualMc || (version.InheritsFrom != null && version.InheritsFrom != minecraft) || string.IsNullOrWhiteSpace(version.MainClass))
            throw new OlanException("Forge 版本不匹配", $"核对安装包 Minecraft 版本与继承字段；参数 modType={modType}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。请求 Minecraft {minecraft}，安装包声明 {actualMc} / {version.InheritsFrom}。");
        var loader = ModLoaderDetector.Detect(version.MainClass, version.Libraries.Select(l => l.Name),
            version.Arguments == null ? ForgeLaunchArguments.Tokenize(version.MinecraftArguments ?? "", diagnosticSource + ": minecraftArguments") : ForgeLaunchArguments.Expand(version.Arguments.Game, diagnosticSource + ": arguments.game"), diagnosticSource);
        if (loader != (modType == "Forge" ? ModEnum.forge : ModEnum.neoforge))
            throw new OlanException("加载器版本不匹配", $"核对安装包加载器身份；参数 modType={modType}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。请求 {modType}，安装包声明 {loader}。");
        var prepared = new ForgePreparedInstall { Installer = bytes, DiagnosticSource = diagnosticSource, Version = version, VersionJson = json,
            Minecraft = minecraft, ModType = modType, Profile = profile, Protocol = legacy ? ForgeInstallProtocol.Legacy : ForgeInstallProtocol.Profile };
        // 零处理器 profile 中未使用的安装工具不是客户端运行依赖。
        var sourceLibraries = version.Libraries.Concat(prepared.Processors.Count == 0 ? [] : profile.Libraries);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new Dictionary<string, ForgeLibraryFile>(comparer);
        var generated = new Dictionary<string, string>(comparer);
        var root = Directory.GetParent(Path.GetFullPath(libraries))!.FullName;
        // 仅解析客户端实际声明的输出；准备阶段不提取资源或写文件。
        var tokens = new ForgeProcessorTokens(prepared, root, Path.Combine(root, "installer.jar"),
            Path.Combine(root, "minecraft.jar"), libraries, false);
        foreach (var processor in prepared.Processors)
            foreach (var output in processor.Outputs)
            {
                var path = Path.GetFullPath(tokens.Resolve(output.Key));
                var hash = tokens.Resolve(output.Value);
                if (generated.TryGetValue(path, out var previousHash) && previousHash != hash)
                    throw new OlanException("Forge 输出冲突", $"解析处理器输出时核对同一路径摘要；参数 modType={modType}；文件/目录 path={path}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。同一路径声明了不同输出摘要：{path}");
                generated[path] = hash;
            }
        // 一些旧处理器没有 outputs，但以 PATCHED/PATCHED_SHA 指定最终产物。
        if (prepared.Processors.Any(p => p.Args.Contains("{PATCHED}")) && tokens.Has("PATCHED") && tokens.Has("PATCHED_SHA"))
            generated.TryAdd(tokens.Key("PATCHED"), tokens.Key("PATCHED_SHA"));
        foreach (var library in sourceLibraries.Where(l => l.ClientRequired != false && ForgeLaunchArguments.Allowed(l.Rules)))
        {
            // 显式 path 也不能绕过构件身份校验。
            var mavenPath = ForgePaths.RelativeMaven(library.Name);
            var artifact = library.Downloads?.Artifact;
            var relative = artifact?.Path is { Length: > 0 } ? artifact.Path : mavenPath;
            var target = ForgePaths.Under(libraries, relative);
            string? embedded = null;
            if (legacy && library.Name == profile.Install!.Path) embedded = profile.Install.FilePath;
            else if (archive.GetEntry("maven/" + relative.Replace('\\', '/')) != null) embedded = "maven/" + relative.Replace('\\', '/');
            var url = artifact?.Url;
            var isGenerated = embedded == null && string.IsNullOrWhiteSpace(url) && generated.ContainsKey(target);
            if (embedded == null && !string.IsNullOrWhiteSpace(url) &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != "https"))
                throw new OlanException("Forge 下载地址无效", $"校验安装库下载 URL 的 HTTPS 协议；参数 modType={modType}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}；请求URL url={url}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。库地址必须为 HTTPS：{library.Name}");
            if (embedded == null && !isGenerated && artifact != null && string.IsNullOrWhiteSpace(url))
                throw new OlanException("Forge 安装配置无效", $"确定安装库的下载来源或内嵌条目；参数 modType={modType}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}；请求URL url={url}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。库 {library.Name} 没有下载来源或内嵌文件。");
            if (artifact == null && embedded == null && !isGenerated)
            {
                var repo = library.Url ?? "https://libraries.minecraft.net/";
                if (!Uri.TryCreate(repo, UriKind.Absolute, out var repository) || repository.Scheme != "https")
                    throw new OlanException("Forge 安装配置无效", $"校验安装库 Maven 仓库 URL；参数 modType={modType}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}；请求URL url={url}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。库仓库地址无效：{repo}");
                url = new Uri(new Uri(repo.TrimEnd('/') + "/"), relative.Replace('\\', '/')).AbsoluteUri;
            }
            var hashes = artifact?.Sha1 is { Length: > 0 } hash ? new[] { hash } : library.Checksums.ToArray();
            if (isGenerated)
            {
                if (hashes.Length > 0 && !hashes.Contains(generated[target], StringComparer.OrdinalIgnoreCase))
                    throw new OlanException("Forge 输出摘要不匹配", $"核对运行库与处理器输出摘要；参数 modType={modType}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}；请求URL url={url}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。运行库与处理器声明不同摘要：{library.Name}");
                hashes = [generated[target]];
            }
            var file = new ForgeLibraryFile(library.Name, new NdDowItem(url ?? "", target, artifact?.Size ?? 0,
                hashes.Length == 1 ? hashes[0] : null), hashes, embedded, isGenerated);
            if (embedded != null && archive.GetEntry(ForgePaths.EntryName(embedded)) == null)
                throw new OlanException("Forge 安装包损坏", $"检查安装包内嵌库归档条目；参数 modType={modType}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}；请求URL url={url}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。缺少内嵌库 {embedded}。");
            if (paths.TryGetValue(target, out var previous))
            {
                if (!previous.Hashes.SequenceEqual(hashes) || previous.EmbeddedEntry != embedded || previous.Generated != isGenerated)
                    throw new OlanException("Forge 库冲突", $"核对指向同一目标文件的库构件；参数 modType={modType}；文件/目录 mavenPath={mavenPath}；文件/目录 relative={relative}；请求URL url={url}；来源安装包/URL={diagnosticSource}；归档条目=install_profile.json。同一路径声明了不同的构件：{target}");
                continue;
            }
            paths.Add(target, file);
            prepared.Libraries.Add(file);
        }
        return prepared;
    }
}
