# Avalonia 12 与 Windows 单文件 AOT 发布

## 原因与改动

- 桌面包更新为 12.1.4，但 `Directory.Build.props` 的共享版本仍是 11.3.18。统一为 12.1.4，桌面项目继续引用 `$(AvaloniaVersion)`，避免 UI、Fluent、Headless 和桌面运行时跨主版本混用。
- 旧 `obj` 中的包解析缓存可能继续引用 Avalonia 11，从而出现 XAML `Method Bind not found`。本次使用工作区内独立的 `--artifacts-path` 验证；本地 IDE 应关闭旧运行进程并清理旧 bin/obj 后重新还原。
- Avalonia 12 在 Windows 扩展客户区使用 `WindowDrawnDecorations`。默认 Fluent 模板的 `PART_TitleTextPanel` 在客户区内容上方显示 `Window.Title`，与项目自绘的标题重叠。`MainWindow.axaml` 使用基于 Fluent 默认主题的局部 `WindowDecorationsTheme`，仅隐藏该面板中的 TextBlock，并使用透明的标题栏背景、30px 标题栏高度。保留系统窗口标题、项目原标题、按钮、拖动和边框机制。全屏按钮属于 Avalonia 12 默认装饰。
- 发布命令的 `PulishProfile` 应为 `PublishProfile`。`Directory.Build.targets` 对该拼写错误给出准确命令，不放宽 Release 的 RID 验证。

## Windows 单文件实现

参考用户提供的 [Avalonia-AOT-SingleFile-Starter](https://github.com/fvffv/Avalonia-AOT-SingleFile-Starter) 的静态链接思路，使用 [StaticLink.Avalonia](https://github.com/greepar/StaticLink.Avalonia) 推荐的 Avalonia 12 稳定包 `3.119.4.1`。

`WindowsAot.pubxml` 开启 `OneLauncherNativeSingleFile`。桌面项目条件引用静态包；`build/NativeSingleFile.targets` 指定启动入口、嵌入当前 NuGet 还原得到的 `msalruntime.dll`，并在包清理动态图形资产后验证发布目录只有一个 EXE。保持 `PublishSingleFile=false`：NativeAOT 的静态链接与 CoreCLR 的单文件打包是两种机制。

`NativeAotSingleFileBootstrap.cs.in` 被编译为入口：Skia、HarfBuzz、ANGLE 静态链接，不释放这三种图形 DLL；WAM 的 MSAL 原生库嵌入 EXE，启动时释放到 `%LOCALAPPDATA%/OneLauncher/native/win-x64/<SHA256>/msalruntime.dll` 并通过绝对路径加载。已有缓存也检查 SHA256，写入使用唯一临时文件和原子移动，最终清理临时文件。保留 WAM 功能，不依赖用户把 DLL 放在 System32。

`ONELAUNCHER_NATIVE_CACHE_ROOT` 可覆盖缓存根路径，便于部署和在工作区内验证。单文件发布的 `--verify-native-runtime` 参数只初始化图形与字体并渲染文字，不初始化账户管理器或启动器数据，也不下载游戏。该参数供安装、发布后的离线检查使用。

## 发布命令

安装 .NET 10.0.402、Visual Studio C++ NativeAOT 工具链后，在 Windows PowerShell 运行：

```powershell
./scripts/Publish-WindowsAot.ps1
```

脚本在工作区下载并校验便携 LLVM 工具（提供静态包需要的 llvm-ar 和 lld-link），发布路径由 `WindowsAot.pubxml` 定义，默认输出 `OneLauncher.Desktop/bin/Release/net10.0/publish/win-x64/aot/OneLauncher.Desktop.exe`。如果已在 PATH 配置 LLVM，可直接使用：

```powershell
dotnet publish OneLauncher.Desktop/OneLauncher.Desktop.csproj -p:PublishProfile=WindowsAot
```

应发布桌面项目，避免在解决方案根目录误发布 ConsoleTest 等测试项目。不要使用 `/p:PulishProfile=WindowsAot.pubxml`。

## PublishProfiles 中的统一脚本入口

`OneLauncher.Desktop/Properties/PublishProfiles/Publish.bat` 默认加载 `WindowsAot.pubxml`，也可传入 `WindowsJit`。它调用已有的 PowerShell 工具链辅助脚本。该辅助脚本不再覆盖 `PublishDir`，只准备 Windows AOT 所需 LLVM、指定发布配置、隔离工作区内中间输出，并保留发布失败的退出状态。

同目录的 `Publish.sh` 使用 Bash，Linux 默认加载 `LinuxAot.pubxml`，macOS 默认加载 `MacOSAot.pubxml`。可传入本机平台对应的 Aot/Jit 配置；不支持的参数和跨 OS 配置会明确报错。脚本从自身路径定位工作区，与调用时所在目录无关。配置中的 RID、发布模式、输出路径均复用 pubxml，不在脚本中重复定义。

```bat
OneLauncher.Desktop\Properties\PublishProfiles\Publish.bat
OneLauncher.Desktop\Properties\PublishProfiles\Publish.bat WindowsJit
```

```bash
bash OneLauncher.Desktop/Properties/PublishProfiles/Publish.sh
bash OneLauncher.Desktop/Properties/PublishProfiles/Publish.sh LinuxJit
# macOS:
bash OneLauncher.Desktop/Properties/PublishProfiles/Publish.sh MacOSJit
```

现有六个平台配置已经提供所需默认路径，本次无需改变 pubxml 的目录定义。Linux/macOS 的脚本发布当前配置所定义的产物，尚不承诺这两个平台已实现与 Windows 相同的静态单文件。`.gitattributes` 固定 bat 使用 CRLF、sh 使用 LF，防止 Windows 检出导致 Bash 换行错误。

脚本调整已验证 PowerShell/Bash 语法、bat 辅助脚本路径、从其他目录调用、默认与显式配置选择、非法参数和发布失败退出状态。通过 MSBuild 属性求值核对 Windows/Linux/macOS 的 AOT 默认目录。此次仅修改发布入口，没有重新执行完整 NativeAOT 编译；此前发布和启动验证结果见下文。

## 验证与扩展

完成 Avalonia 12 全解决方案 Debug 编译、ConsoleTest 离线 UI 回归、Windows 标题栏 Light/Dark 实际窗口渲染与重复标题/按钮断言，以及 Windows x64 NativeAOT 发布。发布目录检查只有一个 EXE；导入表不依赖独立的 Skia/HarfBuzz/ANGLE DLL 或 CoreCLR。

最终单文件 EXE 的 `--verify-native-runtime` 离线运行退出码为 0，成功释放并加载嵌入的 MSAL DLL、初始化静态图形库和字体渲染。Windows 的 UCRT、VCRUNTIME140 等系统运行库仍是正常的原生系统依赖，单文件并不意味着把 Windows 系统库也嵌入应用。

本次用户将范围缩小为只发布 Windows。Linux/macOS 配置保留现状；后续静态单文件应分别在对应 OS 的 NativeAOT 工具链验证，macOS 还需要匹配 Avalonia 12.1.4 的 StaticLink.Avalonia.Native。当前 `OneLauncherNativeSingleFile` 明确限制 win-x64，避免其他平台产生未经验证的单文件产物。

启动验证不执行真实微软登录，WAM 的网络授权流程仍需使用者实际登录验证。第三方图形包升级时应对照 StaticLink 推荐的 SkiaSharp 与 HarfBuzzSharp 版本，再检查图形和字体渲染；Avalonia 标题模板更新时应重新检查本地选择器匹配。
