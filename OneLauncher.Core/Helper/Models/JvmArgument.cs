namespace OneLauncher.Core.Helper.Models;

public enum OptimizationMode
{
    None,
    Conservative,
    Standard,
    Aggressive
}

/// <summary>JVM 配置，堆大小单位 MiB；0 表示自动。切换预设时完整替换此对象。</summary>
public class JvmArguments
{
    public OptimizationMode mode { get; set; } = OptimizationMode.Standard;
    public int MaxHeapSize { get; set; }
    public int InitialHeapSize { get; set; }
    public bool UseG1GC { get; set; } = true;
    public bool UseZGC { get; set; }
    public bool UseShenandoahGC { get; set; }
    public int MaxGCPauseMillis { get; set; } = 200;
    public int G1HeapRegionSize { get; set; }
    public int G1NewSizePercent { get; set; }
    public int G1MaxNewSizePercent { get; set; }
    public int G1ReservePercent { get; set; }
    public bool G1UseStringDeduplication { get; set; }
    public bool UseGenerationalGCForZOrShenandoah { get; set; } = true;
    public bool DisableExplicitGC { get; set; }
    public bool ParallelRefProcEnabled { get; set; }
    public bool AlwaysPreTouch { get; set; }
    public bool PerfDisableSharedMem { get; set; }
    public bool UseAikarFlags { get; set; }

    /// <summary>纯预设工厂，不读取硬件。三种模式只影响启动时的需求推荐。</summary>
    public static JvmArguments CreateFromMode(OptimizationMode mode = OptimizationMode.Standard)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return new JvmArguments { mode = mode, UseG1GC = mode != OptimizationMode.None };
    }
}
