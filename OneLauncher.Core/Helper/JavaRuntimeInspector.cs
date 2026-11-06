using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.Helper;

/// <summary>有限进程检测。缓存包含 release 和可执行文件时间戳，不写入用户配置。</summary>
public static class JavaRuntimeInspector
{
    private static readonly ConcurrentDictionary<string, (JavaRuntimeInfo Info, DateTimeOffset Expires)> RuntimeCache = new();
    private static readonly ConcurrentDictionary<string, bool> CapabilityCache = new();
    private static readonly SemaphoreSlim ProbeLock = new(1, 1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static string ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return Path.GetFullPath(executable);
        string name = OperatingSystem.IsWindows() && !Path.HasExtension(executable) ? executable + ".exe" : executable;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(directory.Trim('"'), name));
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
        return executable;
    }

    public static async Task<JavaRuntimeInfo> InspectAsync(string executable, CancellationToken cancellationToken = default)
    {
        string resolved = ResolveExecutable(executable);
        string probePath = GetConsoleExecutable(resolved);
        string releasePath = GetReleasePath(probePath);
        string key = CacheKey(probePath, releasePath);
        await ProbeLock.WaitAsync(cancellationToken);
        try
        {
            if (RuntimeCache.TryGetValue(key, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Info with { ExecutablePath = resolved };
            string release = "";
            try { if (File.Exists(releasePath)) release = await File.ReadAllTextAsync(releasePath, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var info = Parse(resolved, release, "");
            // release does not reliably identify VM implementation or bitness in older Java builds.
            if (!info.IsKnown)
            {
                var probe = await RunProbeAsync(probePath, ["-XshowSettings:properties", "-version"], cancellationToken);
                info = probe.ExitCode == 0
                    ? Parse(resolved, release, probe.Output)
                    : info with { Diagnostic = probe.Diagnostic ?? "Java 运行时检测返回错误。" };
            }
            // Briefly cache failed detection so concurrent callers do not repeat the same timeout.
            if (RuntimeCache.Count > 64) RuntimeCache.Clear();
            RuntimeCache[key] = (info, info.IsKnown ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddSeconds(10));
            return info;
        }
        finally { ProbeLock.Release(); }
    }

    public static async Task<bool> SupportsArgumentsAsync(JavaRuntimeInfo runtime, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var flags = arguments.ToArray();
        string probePath = GetConsoleExecutable(runtime.ExecutablePath);
        string key = CacheKey(probePath, GetReleasePath(probePath)) + "|" + string.Join("\n", flags);
        await ProbeLock.WaitAsync(cancellationToken);
        try
        {
            if (CapabilityCache.TryGetValue(key, out bool cached)) return cached;
            var probe = await RunProbeAsync(probePath, ["-Xms16m", "-Xmx64m", .. flags, "-version"], cancellationToken);
            if (probe.Diagnostic != null)
                throw new Global.OlanException("无法检查 JVM 参数", probe.Diagnostic);
            bool supported = probe.ExitCode == 0;
            if (CapabilityCache.Count > 128) CapabilityCache.Clear();
            CapabilityCache[key] = supported;
            return supported;
        }
        finally { ProbeLock.Release(); }
    }

    internal static JavaRuntimeInfo Parse(string executable, string release, string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (release + "\n" + output).Split('\n'))
        {
            int separator = line.IndexOf('=');
            if (separator > 0) values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"');
        }
        string version = values.GetValueOrDefault("java.version") ?? values.GetValueOrDefault("JAVA_VERSION") ?? "";
        if (version.Length == 0)
        {
            var line = output.Split('\n').FirstOrDefault(line => line.Contains("version \"", StringComparison.Ordinal));
            if (line != null) version = line.Split('"').ElementAtOrDefault(1) ?? "";
        }
        var parts = version.Split(['.', '_', '-', '+']);
        int major = 0, update = 0;
        if (parts.Length > 0 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int first))
        {
            if (first == 1 && parts.Length > 1)
            {
                int.TryParse(parts[1], out major);
                if (parts.Length > 3) int.TryParse(parts[3], out update);
            }
            else major = first;
        }
        string vm = values.GetValueOrDefault("java.vm.name") ?? "";
        string? bits = values.GetValueOrDefault("sun.arch.data.model");
        string arch = values.GetValueOrDefault("os.arch") ?? values.GetValueOrDefault("OS_ARCH") ?? "";
        bool? is64 = bits == "64" ? true : bits == "32" ? false : arch.ToLowerInvariant() switch
        {
            "amd64" or "x86_64" or "aarch64" or "arm64" or "ppc64" or "ppc64le" or "s390x" or "riscv64" => true,
            "x86" or "i386" or "i686" or "arm" => false,
            _ => null
        };
        if (vm.Length == 0)
            vm = output.Split('\n').FirstOrDefault(line => line.Contains(" VM", StringComparison.Ordinal))?.Trim() ?? "";
        bool hotspot = !vm.Contains("OpenJ9", StringComparison.OrdinalIgnoreCase) &&
            (vm.Contains("HotSpot", StringComparison.OrdinalIgnoreCase) || vm.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase) || vm.Contains("GraalVM", StringComparison.OrdinalIgnoreCase));
        return new JavaRuntimeInfo { ExecutablePath = executable, MajorVersion = major, UpdateVersion = update, Is64Bit = is64, VmName = vm, IsHotSpot = hotspot };
    }

    private static string GetConsoleExecutable(string path)
    {
        if (OperatingSystem.IsWindows() && Path.GetFileName(path).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            string console = Path.Combine(Path.GetDirectoryName(path) ?? "", "java.exe");
            if (File.Exists(console)) return console;
        }
        return path;
    }

    private static string GetReleasePath(string path) => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? "", "release");
    private static string CacheKey(string path, string release) => path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + File.GetLastWriteTimeUtc(release).Ticks;

    internal static async Task<(int ExitCode, string Output, string? Diagnostic)> RunProbeAsync(string path, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = new Process { StartInfo = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Task<string>? stdout = null, stderr = null;
        try
        {
            process.Start();
            stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string output = await stdout + "\n" + await stderr;
            return (process.ExitCode, output, null);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested) throw;
            return (-1, "", "Java 参数检测超过 3 秒，请检查所配置的 Java 运行时。");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            TryKill(process);
            return (-1, "", "无法检测所配置的 Java：" + ex.Message);
        }
        finally
        {
            if (stdout != null && stderr != null)
            {
                try { await Task.WhenAll(stdout, stderr); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            }
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
