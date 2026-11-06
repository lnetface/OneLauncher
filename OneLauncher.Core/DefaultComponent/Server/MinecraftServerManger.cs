using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Minecraft.JsonModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OneLauncher.Core.Minecraft;
using OneLauncher.Core.DefaultComponent.Launcher.Builders;

namespace OneLauncher.Core.DefaultComponent.Server;

public class MinecraftServerManger
{
    public static async Task<int> Init(
        string version,
        bool IsVI
        )
    {
        string versionPath = Path.Combine(Global.Init.GameRootPath,"versions",version);
        var versionInfo = new VersionInfomations(
            await File.ReadAllTextAsync(Path.Combine(versionPath,"version.json"))
            ,Global.Init.GameRootPath);
        if (versionInfo.info.Downloads?.Server == null)
            throw new OlanException("无法初始化服务端","当前版本不支持服务端",OlanExceptionAction.Error);
        
        await Global.Init.Download.DownloadFileAndSha1(
            versionInfo.info.Downloads.Server.Url,
            Path.Combine(versionPath,"server.jar"),
            versionInfo.info.Downloads.Server.Sha1,CancellationToken.None);
        
        Directory.CreateDirectory(
            (IsVI)
            ? Path.Combine(versionPath, "servers")
            : Path.Combine(Global.Init.GameRootPath, "servers")
            );
        await File.WriteAllTextAsync((
            (IsVI)
            ? Path.Combine(versionPath,"servers","eula.txt")
            : Path.Combine(Global.Init.GameRootPath,"servers","eula.txt"))
            ,"eula=true");
        return versionInfo.GetJavaVersion();
    }
    public static async Task RunAsync(string version, int java, bool IsVI, CancellationToken cancellationToken = default)
    {
        using (Process serverProcess = new Process())
        {
            string versionPath = Path.Combine(Global.Init.GameRootPath, "versions", version);
            var runtime = await UnitedCore.Core.JavaManager.GetRuntimeInfoAsync(java, cancellationToken);
            var arguments = await Global.Init.ConfigManger.Data.DefaultSettings.MinecraftJvmArguments.GetArgumentsAsync(runtime, cancellationToken);
            serverProcess.StartInfo = new ProcessStartInfo()
            {
                FileName = runtime.ExecutablePath,
                UseShellExecute = false,
                WorkingDirectory =
                (IsVI)
                ? Path.Combine(versionPath, "servers")
                : Path.Combine(Global.Init.GameRootPath,"servers")
            };
            foreach (string argument in arguments) serverProcess.StartInfo.ArgumentList.Add(argument);
            serverProcess.StartInfo.ArgumentList.Add("-jar");
            serverProcess.StartInfo.ArgumentList.Add(Path.Combine(versionPath, "server.jar"));
            cancellationToken.ThrowIfCancellationRequested();
            serverProcess.Start();
        }
    }
}
