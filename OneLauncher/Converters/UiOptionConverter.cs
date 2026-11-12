using Avalonia.Data.Converters;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Helper.Models;
using System;
using System.Globalization;

namespace OneLauncher.Views.Converters;

/// <summary>把设置值映射成图标键、名称和说明；ComboBox 仍保存原始值。</summary>
public sealed class UiOptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var option = Describe(value);
        return parameter as string switch
        {
            "Icon" => option.IconKey,
            "Description" => option.Description,
            _ => option.Name
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static (string IconKey, string Name, string Description) Describe(object? value) => value switch
    {
        "全部设置" => ("IconSettings1", "全部设置", "显示所有设置板块"),
        "界面" => ("IconHome", "界面", "主页、导航和显示方式"),
        "游戏启动" => ("IconLaunch", "游戏启动", "JVM 参数与内存"),
        "下载" => ("IconDownload", "下载", "线程、校验和下载源"),
        "运行环境" => ("JavaLogo", "运行环境", "Java 运行环境与游戏安装路径"),
        "基本路径" => ("IconHome", "基本路径", "用户目录下 OneLauncher 的 installed 文件夹"),
        "运行路径" => ("IconLaunch", "运行路径", "启动器所在目录的 installed 文件夹"),
        "自定义" => ("IconEdit", "自定义", "选择文件夹或手动输入路径"),
        "默认" => ("IconDashboard", "默认", "右侧选择启动项并快速游戏"),
        "魔法按钮" => ("IconMagic", "魔法按钮", "大按钮启动，可设置背景和板块"),
        DownloadSourceStrategy.OfficialOnly => ("IconCloud", "官方源", "只从 Mojang 官方服务器下载"),
        DownloadSourceStrategy.RaceWithBmcl => ("IconCloudDownload", "BMCLAPI源竞速", "官方源与 BMCLAPI 同时尝试"),
        DownloadSourceStrategy.RaceWithOlan => ("IconCloudDownload", "OLON源竞速", "官方源与 OLON 镜像同时尝试"),
        DownloadSourceStrategy.RaceWithSkyCrystal => ("IconCloudDownload", "SkyCrystal源竞速", "官方源与 SkyCrystal 镜像同时尝试"),
        OptimizationMode.Conservative => ("IconShield", "保守模式", "兼容优先，适合旧设备或排查问题"),
        OptimizationMode.Standard => ("IconSpeed", "标准模式", "兼顾性能与稳定性"),
        OptimizationMode.Aggressive => ("IconLightning", "激进模式", "性能优先，占用更多内存"),
        ModEnum.fabric => ("avares://OneLauncher/Assets/Imgs/fabric.png", "Fabric", "轻量模组加载器"),
        ModEnum.quilt => ("avares://OneLauncher/Assets/Imgs/quilt.png", "Quilt", "兼容 Fabric 生态的加载器"),
        ModEnum.neoforge => ("avares://OneLauncher/Assets/Imgs/neoforge.png", "NeoForge", "Forge 社区分支"),
        ModEnum.forge => ("avares://OneLauncher/Assets/Imgs/forge.jpg", "Forge", "经典模组加载器"),
        ModEnum.none => ("avares://OneLauncher/Assets/Imgs/basic.png", "原版", "不安装模组加载器"),
        AccountType.Offline => ("IconUser", "离线账户", "只保存在本机，不进行在线验证"),
        AccountType.Msa => ("MicrosoftLogo", "微软账户", "使用 Microsoft 账户登录正版"),
        AccountType.Yggdrasil => ("IconKey", "外置登入", "使用 LittleSkin 等外置验证服务"),
        UserModel user => (Describe(user.UserType).IconKey, user.Name, Describe(user.UserType).Name),
        int java => ("JavaLogo", $"Java {java}", java is 8 or 11 or 17 or 21 or 25 ? "长期支持版本" : "非长期支持版本"),
        _ => (string.Empty, value?.ToString() ?? string.Empty, string.Empty)
    };
}
