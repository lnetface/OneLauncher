namespace OneLauncher.Core.Helper.Models;

/// <summary>当次启动实际使用的 Java；未知字段不能用游戏要求的版本填充。</summary>
public sealed record JavaRuntimeInfo
{
    public required string ExecutablePath { get; init; }
    public int MajorVersion { get; init; }
    public int UpdateVersion { get; init; }
    public bool? Is64Bit { get; init; }
    public string VmName { get; init; } = "";
    public bool IsHotSpot { get; init; }
    public string? Diagnostic { get; init; }
    public bool IsKnown => MajorVersion > 0 && Is64Bit.HasValue && VmName.Length > 0;
}
