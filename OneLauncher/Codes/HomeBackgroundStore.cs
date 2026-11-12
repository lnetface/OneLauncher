using Avalonia.Media.Imaging;
using OneLauncher.Core.Global;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Codes;

/// <summary>
/// 主页背景只由物理文件决定，不写入配置：OneLauncher\customdata\home-background.png。
/// 用户选择的任意受支持格式都会重新编码为 PNG，全局只保留这一份背景。
/// </summary>
internal static class HomeBackgroundStore
{
    private const string FileName = "home-background.png";
    private const string LegacyFolderName = "home-background";
    public static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"];
    private static readonly string[] ImageExtensions = ImagePatterns.Select(p => p[1..]).ToArray();
    private static int _version;

    /// <summary>背景文件被导入或移除后触发。</summary>
    public static event Action? Changed;

    /// <summary>customdata 目录；测试可替换为工作区内的临时目录。</summary>
    internal static string CustomDataPath { get; set; } = Path.Combine(Init.BasePath, "customdata");

    /// <summary>固定路径不会变化，主页用版本号判断是否需要重新解码。</summary>
    public static int Version => Volatile.Read(ref _version);

    private static string CurrentPath => Path.Combine(CustomDataPath, FileName);
    private static string LegacyRoot => Path.Combine(CustomDataPath, LegacyFolderName);

    /// <summary>返回当前背景图片路径，不存在时返回 null。图片能否解码由加载方判断。</summary>
    public static string? FindCurrent()
    {
        if (File.Exists(CurrentPath))
            return CurrentPath;
        TryMigrateLegacyBackground();
        return File.Exists(CurrentPath) ? CurrentPath : null;
    }

    /// <summary>复制用户选择的图片作为新背景；用户的原始文件不会被修改或删除。</summary>
    public static async Task<string> ImportAsync(string sourcePath)
    {
        await Task.Run(() => SaveAsPng(sourcePath, warnOnInvalid: true));
        TryDeleteDirectory(LegacyRoot);
        RaiseChanged();
        return CurrentPath;
    }

    public static void Clear()
    {
        TryDeleteFile(CurrentPath);
        TryDeleteDirectory(LegacyRoot);
        RaiseChanged();
    }

    /// <summary>完整解码后立即关闭文件，避免主页持有句柄导致下一次覆盖失败。</summary>
    internal static Bitmap LoadBitmap(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bitmap = new Bitmap(stream);
        if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0)
        {
            bitmap.Dispose();
            throw new InvalidDataException("图片尺寸无效");
        }
        return bitmap;
    }

    private static void SaveAsPng(string sourcePath, bool warnOnInvalid)
    {
        Directory.CreateDirectory(CustomDataPath);
        var temp = Path.Combine(CustomDataPath, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var bitmap = LoadBitmap(sourcePath))
                bitmap.Save(temp);
            using (LoadBitmap(temp)) { }
            File.Move(temp, CurrentPath, true);
        }
        catch (Exception ex) when (warnOnInvalid && ex is not OlanException)
        {
            throw new OlanException("无法设置主页背景", $"读取图片并转码保存主页背景；文件/目录 sourcePath={sourcePath}；目标背景文件={Path.Combine(Init.BasePath, FileName)}。所选文件不是可用的图片：{ex.Message}", OlanExceptionAction.Warning, ex);
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private static void TryMigrateLegacyBackground()
    {
        if (!Directory.Exists(LegacyRoot))
            return;
        try
        {
            var legacy = new DirectoryInfo(LegacyRoot).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (legacy != null)
            {
                SaveAsPng(legacy.FullName, warnOnInvalid: false);
                Interlocked.Increment(ref _version);
            }
            TryDeleteDirectory(LegacyRoot);
        }
        catch (Exception)
        {
            // 旧背景损坏或被占用时视为未设置，不能影响主页和设置页启动。
        }
    }

    private static void RaiseChanged()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
