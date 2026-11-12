using OneLauncher.Core.Global;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Loader.ForgeSeries.JarMod;

internal static class JarModSupport
{
    public static void RequireInstaller(string minecraft)
    {
        if (Version.TryParse(minecraft, out var version) && version < new Version(1, 6))
            throw Unsupported($"Minecraft={minecraft}；步骤=校验历史 Forge 安装器支持");
    }

    public static OlanException Unsupported(string diagnosticSource = "直接校验历史 Forge 安装协议") => new("Forge 历史版本暂不支持",
        $"检查历史 Forge JarMod 安装协议支持；请求版本/安装包={diagnosticSource}。此版本使用 JarMod。需要兼容的 ModLoader/ModLoaderMP 前置组件及旧版启动适配，不能直接用 Forge ZIP 替换原版游戏。请使用已验证的 Forge installer 版本（采样范围为 1.7.10 及以后）。");
}
