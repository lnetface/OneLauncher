using System.Diagnostics;
using System.Globalization;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Launcher.Builders;

internal sealed record JvmArgumentSource(int Priority, IReadOnlyList<string> Arguments, bool CommandLine = false);
internal sealed record JvmArgumentResult(IReadOnlyList<string> Arguments, IReadOnlyList<string> CommandArguments);
internal readonly record struct HeapRecommendation(int? InitialMiB, int? MaximumMiB, double BudgetMiB, double DemandMiB);

/// <summary>确定性预算与参数来源合并。只识别堆和 GC 家族，不重新序列化其他参数。</summary>
internal static class JvmOptimizationPolicy
{
    private const int AutomaticCapMiB = 16384;
    private const int MinimumAutomaticMiB = 512;
    private static readonly string[] Collectors = ["UseG1GC", "UseZGC", "UseShenandoahGC", "UseSerialGC", "UseParallelGC", "UseParallelOldGC", "UseConcMarkSweepGC", "UseParNewGC", "UseEpsilonGC"];

    private static bool CanProbe(string token)
    {
        token = Unquote(token);
        if (IsHeap(token)) return false;
        if (IsCollector(token) || TuningFamily(token) != null) return true;
        string name = token.StartsWith("-XX:", StringComparison.Ordinal) ? token[4..].TrimStart('+', '-').Split('=')[0] : "";
        return name is "UnlockExperimentalVMOptions" or "UnlockDiagnosticVMOptions" or "MaxGCPauseMillis" or "DisableExplicitGC" or "ParallelRefProcEnabled" or "AlwaysPreTouch" or "PerfDisableSharedMem" or "UseNUMA" or "SurvivorRatio" or "MaxTenuringThreshold";
    }

    internal static HeapRecommendation CalculateHeap(OptimizationMode mode, SystemMemoryHelper.MemoryMetrics? memory, bool? is64Bit, bool modded, int modCount, long? explicitInitialBytes = null, long? explicitMaximumBytes = null)
    {
        if (!Enum.IsDefined(mode)) throw Error("未知的 JVM 优化模式。");
        if (explicitInitialBytes is <= 0 || explicitMaximumBytes is <= 0) throw Error("堆大小必须为正数；结构化配置中 0 表示自动。");
        if (explicitMaximumBytes.HasValue)
        {
            if (explicitInitialBytes > explicitMaximumBytes) throw Error("初始堆 Xms 不能大于最大堆 Xmx。");
            return new HeapRecommendation(explicitInitialBytes.HasValue ? null : (int)Math.Min(512, explicitMaximumBytes.Value / 1048576), null, 0, 0);
        }
        if (!memory.HasValue || !SystemMemoryHelper.IsValid(memory.Value))
            return new HeapRecommendation(null, null, 0, 0);
        double reserve = Math.Clamp(memory.Value.TotalMB * .10, 512, 2048);
        double usable = Math.Max(0, memory.Value.FreeMB - reserve);
        double heap = .8 * Math.Min(usable, 8192) + .4 * Math.Min(Math.Max(usable - 8192, 0), 8192) + .2 * Math.Max(usable - 16384, 0);
        // Unknown bitness also uses the conservative 32-bit ceiling.
        double budget = Math.Min(heap, Math.Min(usable, is64Bit == true ? AutomaticCapMiB : 768));
        double multiplier = mode switch { OptimizationMode.Conservative => .75, OptimizationMode.Aggressive => 1.25, _ => 1 };
        double demand = (modded ? 3072 + 8.0 * Math.Max(0, modCount) : 2048) * multiplier;
        int alignedBudget = (int)(Math.Floor(budget / 128) * 128);
        if (alignedBudget < MinimumAutomaticMiB) throw Error("可用内存不足，请关闭其他程序或明确配置内存。");
        double initialMiB = explicitInitialBytes.GetValueOrDefault() / 1048576.0;
        if (initialMiB > alignedBudget) throw Error("自动内存预算无法容纳手动设置的 Xms，请降低初始堆或明确设置 Xmx。");
        int maximum = (int)(Math.Floor(Math.Min(budget, Math.Max(demand, initialMiB)) / 128) * 128);
        if (initialMiB > maximum)
        {
            maximum = (int)(Math.Ceiling(initialMiB / 128) * 128);
            if (maximum > alignedBudget) throw Error("自动内存预算无法容纳手动设置的 Xms，请降低初始堆或明确设置 Xmx。");
        }
        return new HeapRecommendation(explicitInitialBytes.HasValue ? null : Math.Min(maximum, 512), maximum, budget, demand);
    }

    internal static int CountMods(string gamePath)
    {
        string directory = Path.Combine(gamePath, "mods");
        if (!Directory.Exists(directory)) return 0;
        try
        {
            return Directory.EnumerateFiles(directory).Count(path => Path.GetExtension(path).ToLowerInvariant() is ".jar" or ".zip" or ".litemod");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("模组数量检测失败：" + ex.Message);
            return 0;
        }
    }

    internal static async Task<JvmArgumentResult> BuildAsync(JvmArguments? config, JavaRuntimeInfo runtime, SystemMemoryHelper.MemoryMetrics? memory,
        bool modded, int modCount, IReadOnlyList<JvmArgumentSource> sources, CancellationToken cancellationToken = default,
        Func<JavaRuntimeInfo, IEnumerable<string>, CancellationToken, Task<bool>>? supportsArguments = null)
    {
        bool optimize = config != null && config.mode != OptimizationMode.None;
        var all = sources.Select(source => (source, tokens: source.Arguments.SelectMany(Tokenize).ToList())).ToList();
        if (optimize)
        {
            var structured = new List<string>();
            if (config!.MaxHeapSize != 0) structured.Add($"-Xmx{config.MaxHeapSize}m");
            if (config.InitialHeapSize != 0) structured.Add($"-Xms{config.InitialHeapSize}m");
            // G1 alone is the preset baseline; other GC selections are explicit.
            if (config.UseZGC || config.UseShenandoahGC)
            {
                if (config.UseG1GC) structured.Add("-XX:+UseG1GC");
                if (config.UseZGC) structured.Add("-XX:+UseZGC");
                if (config.UseShenandoahGC) structured.Add("-XX:+UseShenandoahGC");
            }
            all.Add((new JvmArgumentSource(30, structured), structured));
        }
        var maximumSelection = SelectHeap(all, false);
        var initialSelection = SelectHeap(all, true);
        string? maximum = maximumSelection.Token, initial = initialSelection.Token;
        long? maximumBytes = maximum != null ? ParseHeapBytes(maximum) : null;
        long? initialBytes = initial != null ? ParseHeapBytes(initial) : null;
        if (initialBytes > maximumBytes) throw Error("初始堆 Xms 不能大于最大堆 Xmx。");
        var generated = new List<string>();
        if (optimize)
        {
            var heap = CalculateHeap(config!.mode, memory, runtime.Is64Bit, modded, modCount, initialBytes, maximumBytes);
            if (heap.MaximumMiB.HasValue) maximum = $"-Xmx{heap.MaximumMiB}m";
            if (heap.InitialMiB.HasValue && heap.InitialMiB > 0) initial = $"-Xms{heap.InitialMiB}m";
            Debug.WriteLine($"JVM 内存：检测={(memory.HasValue ? "已采样" : "未知，使用 JVM 默认分配")}，预算={heap.BudgetMiB:F0}MiB，需求={heap.DemandMiB:F0}MiB，Xms={initial ?? "默认"}，Xmx={maximum ?? "默认"}，Java={runtime.MajorVersion}，VM={runtime.VmName}");
        }

        var gcSource = all.Where(entry => entry.tokens.Any(IsCollector)).GroupBy(entry => entry.source.Priority)
            .OrderByDescending(group => group.Key).Select(group => (source: group.First().source with { CommandLine = group.Any(entry => entry.source.CommandLine) }, tokens: group.SelectMany(entry => entry.tokens).ToList())).FirstOrDefault();
        bool overriddenGc = gcSource.source != null;
        var gcFlags = overriddenGc ? EffectiveCollectors(gcSource.tokens) : new List<string>();
        if (!overriddenGc && optimize && config!.UseG1GC && runtime.IsHotSpot && runtime.MajorVersion >= 8)
            gcFlags.Add("-XX:+UseG1GC");
        string? collector = SelectedCollector(gcFlags);
        bool baseline = !overriddenGc && collector == "UseG1GC";
        if (collector != null && runtime.IsHotSpot)
        {
            if (collector == "UseZGC" && runtime.MajorVersion < 11) throw Error("当前 Java 不支持 ZGC，请使用兼容的运行时或其他 GC。");
            if (collector is "UseZGC" or "UseShenandoahGC" or "UseEpsilonGC" && runtime.MajorVersion is >= 11 and < 15)
                generated.Add("-XX:+UnlockExperimentalVMOptions");
        }
        else if (collector != null && !baseline && !runtime.IsHotSpot)
            throw Error("无法确认此 Java 支持所选的 HotSpot GC 参数，请检查运行时或移除 GC 设置。");

        // Remove collector-specific tuning from lower-priority sources when the collector is overridden.
        var normal = new List<string>();
        var command = new List<string>();
        if (initial != null) (initialSelection.CommandLine ? command : generated).Add(initial);
        if (maximum != null) (maximumSelection.CommandLine ? command : generated).Add(maximum);
        foreach (var entry in all)
        {
            if (entry.source.Priority == 30) continue;
            foreach (string token in entry.tokens)
            {
                if (IsHeap(token) || IsCollector(token)) continue;
                string? family = TuningFamily(token);
                if (family != null && collector != null && family != collector) continue;
                (entry.source.CommandLine ? command : normal).Add(token);
            }
        }

        if (optimize && runtime.IsHotSpot && (!overriddenGc || gcSource.source!.Priority == 30))
        {
            if (collector == "UseG1GC") AddG1Options(generated, config!, runtime);
            if (collector == "UseZGC" && config!.UseGenerationalGCForZOrShenandoah && runtime.MajorVersion is 21 or 22)
                generated.Add("-XX:+ZGenerational");
        }
        if (optimize && runtime.IsHotSpot) AddCommonOptions(generated, config!);
        // A command-line-only collector must keep its location, together with required unlock flags.
        if (gcSource.source?.CommandLine == true)
        {
            foreach (string unlock in generated.Where(arg => arg.Contains("UnlockExperimentalVMOptions", StringComparison.Ordinal)).ToArray())
            {
                generated.Remove(unlock);
                command.Insert(0, unlock);
            }
            command.AddRange(gcFlags);
        }
        else generated.AddRange(gcFlags);

        if (collector != null || generated.Any(arg => arg.StartsWith("-XX:", StringComparison.Ordinal)))
        {
            // Verify optional launcher flags as a group with a tiny heap, never the actual game heap.
            var checkedFlags = command.Concat(generated).Concat(normal)
                .Where(CanProbe).Select(Unquote)
                .ToArray();
            if (checkedFlags.Length > 0 && !await (supportsArguments ?? JavaRuntimeInspector.SupportsArgumentsAsync)(runtime, checkedFlags, cancellationToken))
                throw Error("当前 Java 不接受所选 JVM／GC 参数，请检查自定义参数或重新选择优化模式。");
        }
        normal.InsertRange(0, generated.Distinct());
        return new JvmArgumentResult(normal, command);
    }

    private static void AddG1Options(List<string> args, JvmArguments config, JavaRuntimeInfo runtime)
    {
        if (config.MaxGCPauseMillis < 0 || config.G1HeapRegionSize < 0 || config.G1NewSizePercent < 0 || config.G1MaxNewSizePercent < 0 || config.G1ReservePercent < 0)
            throw Error("G1 参数不能为负数，0 表示不指定。");
        if (config.G1NewSizePercent > 100 || config.G1MaxNewSizePercent > 100 || config.G1ReservePercent > 100 || config.G1NewSizePercent > 0 && config.G1MaxNewSizePercent > 0 && config.G1NewSizePercent > config.G1MaxNewSizePercent)
            throw Error("G1 百分比参数范围不正确。");
        if (config.MaxGCPauseMillis > 0) args.Add($"-XX:MaxGCPauseMillis={config.MaxGCPauseMillis}");
        if (config.G1NewSizePercent > 0 || config.G1MaxNewSizePercent > 0 || config.UseAikarFlags) args.Add("-XX:+UnlockExperimentalVMOptions");
        if (config.G1NewSizePercent > 0) args.Add($"-XX:G1NewSizePercent={config.G1NewSizePercent}");
        if (config.G1MaxNewSizePercent > 0) args.Add($"-XX:G1MaxNewSizePercent={config.G1MaxNewSizePercent}");
        if (config.G1HeapRegionSize > 0) args.Add($"-XX:G1HeapRegionSize={config.G1HeapRegionSize}m");
        if (config.G1ReservePercent > 0) args.Add($"-XX:G1ReservePercent={config.G1ReservePercent}");
        if (config.G1UseStringDeduplication && (runtime.MajorVersion > 8 || runtime.MajorVersion == 8 && runtime.UpdateVersion >= 20)) args.Add("-XX:+UseStringDeduplication");
        if (config.UseAikarFlags) args.AddRange(["-XX:+UseNUMA", "-XX:G1MixedGCCountTarget=4", "-XX:G1MixedGCLiveThresholdPercent=90", "-XX:G1RSetUpdatingPauseTimePercent=5", "-XX:SurvivorRatio=32", "-XX:MaxTenuringThreshold=1", "-Dusing.aikars.flags=true"]);
    }

    private static void AddCommonOptions(List<string> args, JvmArguments config)
    {
        if (config.DisableExplicitGC) args.Add("-XX:+DisableExplicitGC");
        if (config.ParallelRefProcEnabled) args.Add("-XX:+ParallelRefProcEnabled");
        if (config.AlwaysPreTouch) args.Add("-XX:+AlwaysPreTouch");
        if (config.PerfDisableSharedMem) args.Add("-XX:+PerfDisableSharedMem");
    }

    private static (string? Token, bool CommandLine) SelectHeap(List<(JvmArgumentSource source, List<string> tokens)> sources, bool initial)
    {
        foreach (var value in sources.Select((entry, index) => (entry, index)).OrderByDescending(value => value.entry.source.Priority).ThenByDescending(value => value.index))
        {
            string? token = value.entry.tokens.LastOrDefault(token => HeapKind(token) == (initial ? 1 : 2));
            if (token != null) return (token, value.entry.source.CommandLine);
        }
        return (null, false);
    }

    private static int HeapKind(string token)
    {
        token = Unquote(token);
        return token.StartsWith("-Xms", StringComparison.Ordinal) || token.StartsWith("-XX:InitialHeapSize=", StringComparison.Ordinal) ? 1 :
            token.StartsWith("-Xmx", StringComparison.Ordinal) || token.StartsWith("-XX:MaxHeapSize=", StringComparison.Ordinal) ? 2 : 0;
    }
    private static bool IsHeap(string token) => HeapKind(token) != 0;
    internal static long ParseHeapBytes(string token)
    {
        token = Unquote(token);
        string value = token.StartsWith("-XX:", StringComparison.Ordinal) ? token[(token.IndexOf('=') + 1)..] : token[4..];
        long factor = 1;
        if (value.Length > 0 && char.IsLetter(value[^1]))
        {
            factor = char.ToLowerInvariant(value[^1]) switch { 'k' => 1024, 'm' => 1048576, 'g' => 1073741824, 't' => 1099511627776, _ => 0 };
            value = value[..^1];
        }
        if (factor == 0 || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long size) || size <= 0 || size > long.MaxValue / factor)
            throw Error("无效的堆大小，请使用正整数和 k／m／g／t 单位。");
        return size * factor;
    }

    private static bool IsCollector(string token)
    {
        token = Unquote(token);
        return token.StartsWith("-XX:", StringComparison.Ordinal) && token.Length > 5 && token[4] is '+' or '-' && Collectors.Contains(token[5..]);
    }
    private static List<string> EffectiveCollectors(List<string> tokens)
    {
        var values = new Dictionary<string, string>();
        foreach (string token in tokens.Where(IsCollector)) values[Unquote(token)[5..]] = Unquote(token);
        var result = values.Values.ToList();
        _ = SelectedCollector(result);
        return result;
    }
    private static string? SelectedCollector(List<string> tokens)
    {
        var enabled = tokens.Where(token => token.StartsWith("-XX:+", StringComparison.Ordinal)).Select(token => token[5..] switch { "UseParallelOldGC" => "UseParallelGC", "UseParNewGC" => "UseConcMarkSweepGC", var name => name }).Distinct().ToArray();
        if (enabled.Length > 1) throw Error("同时启用了多个互斥 GC，请只保留一种垃圾收集器。");
        return enabled.FirstOrDefault();
    }
    private static string? TuningFamily(string token)
    {
        token = Unquote(token);
        if (!token.StartsWith("-XX:", StringComparison.Ordinal)) return null;
        string name = token[4..].TrimStart('+', '-');
        if (name.StartsWith("G1", StringComparison.Ordinal) || name.StartsWith("UseStringDeduplication", StringComparison.Ordinal)) return "UseG1GC";
        if (name.StartsWith("Z", StringComparison.Ordinal)) return "UseZGC";
        if (name.StartsWith("Shenandoah", StringComparison.Ordinal)) return "UseShenandoahGC";
        return null;
    }

    /// <summary>保留原始引号和转义，仅按未被双引号包围的空白分词。</summary>
    internal static IEnumerable<string> Tokenize(string fragment)
    {
        int start = -1, slashes = 0;
        bool quoted = false;
        for (int i = 0; i < fragment.Length; i++)
        {
            char character = fragment[i];
            if (start < 0 && !char.IsWhiteSpace(character)) start = i;
            if (character == '"' && slashes % 2 == 0) quoted = !quoted;
            if (char.IsWhiteSpace(character) && !quoted && start >= 0)
            {
                yield return fragment[start..i];
                start = -1;
            }
            slashes = character == '\\' ? slashes + 1 : 0;
        }
        if (quoted) throw Error("JVM 参数的双引号没有闭合。");
        if (start >= 0) yield return fragment[start..];
    }

    private static string Unquote(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"' ? token[1..^1] : token;
    private static OlanException Error(string message) => new("JVM 配置无效", message);
}
