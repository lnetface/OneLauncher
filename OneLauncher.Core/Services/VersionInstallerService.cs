using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using System;
using System.Collections.Generic;
using System.Text;

using OneLauncher.Core.Global.ModelDataMangers;

namespace OneLauncher.Core.Services;
public sealed class VersionInstallerService
{
    private readonly VersionListManager _versionListManager;

    internal VersionInstallerService(VersionListManager versionListManager)
    {
        _versionListManager = versionListManager;
    }

    public async Task InstallNewVersion(
        DownloadInfo info,
        IProgress<(DownProgress Title, int AllFiles, int DownedFiles, string DowingFileName)> progress,
        CancellationToken? cancelToken = null,
        // 单文件下载失败时是否只重试该文件；在下载线程上同步调用，可阻塞等待用户选择
        Func<string, bool>? shouldContinue = null
        )
    {
        _versionListManager.EnsureGameRoot(info.GameRootPath);
        var token = cancelToken ?? CancellationToken.None;
        token.ThrowIfCancellationRequested();
        var downloader = new DownloadMinecraft(info, progress, token, shouldContinue);
        await downloader.MinecraftBasic();
        token.ThrowIfCancellationRequested();
        await _versionListManager.RegisterInstalledVersionAsync(info.VersionInstallInfo, token);
        progress.Report((DownProgress.Done, downloader.alls, downloader.alls, "下载并登记完毕！"));
    }
}
