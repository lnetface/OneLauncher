using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OneLauncher.Core.Helper;

public static class SystemMemoryHelper
{
    /// <summary>所有字段均为 MiB。FreeMB 表示可供新分配使用的估计量。</summary>
    public struct MemoryMetrics
    {
        public double TotalMB { get; set; }
        public double UsedMB { get; set; }
        public double FreeMB { get; set; }
    }

    public static MemoryMetrics GetMemoryMetrics() => TryGetMemoryMetrics(out var metrics) ? metrics : default;

    /// <summary>检测失败返回 false，不用虚构的内存值代替真实采样。</summary>
    public static bool TryGetMemoryMetrics(out MemoryMetrics metrics)
    {
        metrics = default;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(status)) return false;
                metrics = Create(status.ullTotalPhys / 1048576.0, status.ullAvailPhys / 1048576.0);
            }
            else if (OperatingSystem.IsLinux())
            {
                var entries = File.ReadLines("/proc/meminfo")
                    .Select(line => line.Split([':', ' '], StringSplitOptions.RemoveEmptyEntries))
                    .Where(parts => parts.Length >= 2)
                    .ToDictionary(parts => parts[0], parts => double.Parse(parts[1], CultureInfo.InvariantCulture));
                if (!entries.TryGetValue("MemTotal", out double total) || !entries.TryGetValue("MemAvailable", out double available)) return false;
                metrics = Create(total / 1024, available / 1024);
            }
            else return false;
            return IsValid(metrics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Debug.WriteLine("内存检测失败：" + ex.Message);
            metrics = default;
            return false;
        }
    }

    internal static bool IsValid(MemoryMetrics metrics) => double.IsFinite(metrics.TotalMB) && double.IsFinite(metrics.FreeMB) && metrics.TotalMB > 0 && metrics.FreeMB >= 0 && metrics.FreeMB <= metrics.TotalMB;
    private static MemoryMetrics Create(double total, double available) => new() { TotalMB = total, FreeMB = available, UsedMB = total - available };

    /// <summary>macOS 查询使用可取消的异步进程，不阻塞 UI；其他平台直接采样。</summary>
    public static async Task<MemoryMetrics?> GetMemoryMetricsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return TryGetMemoryMetrics(out var metrics) ? metrics : null;
        try
        {
            var total = await JavaRuntimeInspector.RunProbeAsync("/usr/sbin/sysctl", ["-n", "hw.memsize"], cancellationToken);
            var pages = await JavaRuntimeInspector.RunProbeAsync("/usr/bin/vm_stat", [], cancellationToken);
            if (total.ExitCode != 0 || pages.ExitCode != 0) return null;
            return ParseMacMemory(total.Output, pages.Output);
        }
        catch (Exception ex) when (ex is IOException or FormatException or OverflowException or ArgumentException)
        {
            Debug.WriteLine("内存检测失败：" + ex.Message);
            return null;
        }
    }

    internal static MemoryMetrics? ParseMacMemory(string totalOutput, string output)
    {
        string header = output.Split('\n')[0];
        int start = header.IndexOf("page size of ", StringComparison.Ordinal);
        if (start < 0) return null;
        double pageSize = double.Parse(header[(start + 13)..].Split(' ')[0], CultureInfo.InvariantCulture);
        var pages = new Dictionary<string, double>();
        foreach (string line in output.Split('\n').Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && double.TryParse(line[(colon + 1)..].Trim().TrimEnd('.'), NumberStyles.None, CultureInfo.InvariantCulture, out double count))
                pages[line[..colon].Trim()] = count;
        }
        if (!pages.ContainsKey("Pages free") || pageSize <= 0) return null;
        // 压缩、活动和 wired 页不计入可用内存。
        double availablePages = pages.GetValueOrDefault("Pages free") + pages.GetValueOrDefault("Pages inactive") + pages.GetValueOrDefault("Pages speculative");
        var metrics = Create(double.Parse(totalOutput.Trim(), CultureInfo.InvariantCulture) / 1048576, availablePages * pageSize / 1048576);
        return IsValid(metrics) ? metrics : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX status);
}
