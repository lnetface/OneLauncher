using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;

namespace OneLauncher.Core.DefaultComponent.Launcher.Builders;

public static class JvmArgumentsExtensions
{
    /// <summary>用于没有版本／加载器参数来源的独立启动，例如原版服务端。</summary>
    public static async Task<IReadOnlyList<string>> GetArgumentsAsync(this JvmArguments config, JavaRuntimeInfo runtime, CancellationToken cancellationToken = default)
    {
        var memory = config.mode != OptimizationMode.None ? await SystemMemoryHelper.GetMemoryMetricsAsync(cancellationToken) : null;
        var result = await JvmOptimizationPolicy.BuildAsync(config, runtime, memory, false, 0, [], cancellationToken);
        return result.Arguments;
    }
}
