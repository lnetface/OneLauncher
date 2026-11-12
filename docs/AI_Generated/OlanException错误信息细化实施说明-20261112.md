# OlanException 错误信息细化实施说明

2026-11-12。以用户后续要求为准：保留业务逻辑，细化 Message；后台失败只构造异常，不新增 throw，也不观察 Fire 任务失败。

## 最终范围

- 删除本次新增的 ExceptionDiagnostics.cs、TaskDiagnostics.cs，移除所有 Observe/Run 包装；没有新增辅助源文件。
- 错误信息直接在原有 OlanException 构造位置写入 Message，包含操作步骤、相关文件/目录、请求 URL、版本或实例标识，以及适用的响应状态或原始原因。
- 保留异常 Action、重试回调、任务状态、既有通知、返回值和业务回退方式。
- OlanException.cs 本身保持原样。尚未增加日志输出、日志文件或日志服务；未来在构造函数接入日志即可接收这些 Message。
- OneLauncher.Desktop 源码保持原样。

## 为什么部分签名和字段增加了来源参数

整合包清单、Forge 安装包和启动参数解析器原本只有内存数据，无法知道具体输入文件或请求 URL。因此通过可选参数传递来源，仅供 Message 使用；未改变解析、校验或下载流程。现有调用无需参数仍可兼容。

初始化、JSON 配置读写和 Microsoft 授权流程使用局部变量标记当前步骤，使原有 catch 能描述失败阶段。JVM 错误仍使用原有 Error 工厂，各错误分支直接传入步骤和堆预算、Java 路径或 GC 参数细节。

未知异常界面处理通过编译器提供的 CallerMemberName 和 CallerFilePath 补充调用方法和源文件，并保留原异常详细信息；不使用运行时反射。

## 后台失败处的异常构造

在检查更新、启动更新脚本、账户刷新、取消任务后停止游戏、新闻读取、服务器延迟探测的原有 catch 中，对尚未构造过 OlanException 的异常执行 `_ = new OlanException(...)`，保留其原有回退行为。已是 OlanException 的异常不会重复构造。

下载后的 UserInfo.ForEach 仍使用原来的 async 回调，没有改为等待所有保存操作；回调内部捕获保存失败并构造 OlanException，不重新抛出，不增加 UI 提示。

未新增任务观察器、ContinueWith、全局未观察异常事件，也未观察预先创建后续等待的任务。普通 Fire 调用保留原有形式。

## 验证

最终 Debug 构建通过：0 个错误、253 个警告。命令为：

```powershell
dotnet build OneLauncher.Desktop/OneLauncher.Desktop.csproj --no-restore -c Debug -p:BuildAvaloniaTools=false -p:UsedAvaloniaProducts=
```

UsedAvaloniaProducts 置空只作用于本次命令，用于跳过 Avalonia 构建统计任务访问沙箱外许可目录，不修改项目配置。

Release 构建未完成：当前 project.assets.json 缺少 net10.0/win-x64 还原目标。未进行 AOT 发布，也未进行游戏启动或网络端到端测试。git diff --check 通过；源码中没有辅助类残留引用。

本次临时脚本及构建记录在工作区内生成，随后通过 build/clean.ps1 的指定 Task 清理，不清理其他任务或整个 bin/obj。

## 涉及的原有源文件

以下清单是最终工作区源文件差异，包括 AutoUpdate.cs 原有用户改动；原有用户改动予以保留。
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Account\Microsoft\MojangProfile.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Account\Microsoft\MsalAuthenticator.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Account\Yggdrasil\YggdrasilAuthenticator.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\ConnectToolPower\MCTPower.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\ConnectToolPower\P2PMode.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\Download.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\DownloadMinecraftProviders\DownloadInfoCreater.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\DownloadMinecraftProviders\DownloadMinecraft.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\DownloadMinecraftProviders\ModSources\FabricProvider.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\DownloadMinecraftProviders\ModSources\QuiltProvider.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\DownloadMinecraftProviders\ModSources\SpecifiedVersionConfig\SpecifiedFabricVersionConfig.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Downloader\RaceDownloader.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\JavaProviders\AdoptiumAPI.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\JavaProviders\AzulZuluAPI.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\JavaProviders\BaseJavaProvider.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\JavaProviders\GraalVMGetter.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\JavaProviders\MicrosoftBuildofOpenJDKGetter.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\JavaProviders\OracleJDK.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\Builders\JvmOptimizationPolicy.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\GameLauncher.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\LaunchCommandBuilder.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\LaunchService.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\Strategys\FabricStrategy.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\Strategys\ForgeStrategy.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\Strategys\NeoForgeStrategy.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Launcher\Strategys\QuiltStrategy.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\ForgeSeriesInstallTasker.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\ForgeSeriesUsing.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\ForgeVersionListGetter.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Installation\ForgeFileVerifier.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Installation\ForgeInstallerParser.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Installation\ForgePaths.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Installation\ForgePreparedInstall.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\JarMod\JarModSupport.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Launch\ForgeLaunchArguments.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Legacy\ForgeRealmsProcessor.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Legacy\LegacyProfileReader.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Processors\ForgeProcessorRunner.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Processors\ForgeProcessorTokens.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ForgeSeries\Profiles\ProfileReader.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Loader\ModLoaderDetector.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Mod\Manager\FabricModInfoHelper.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Pack\ModOutpoter\ModpackExporter.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Pack\ModpackImporter.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\Pack\MrpackParser.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\ResourceGetterService\Modrinth\GetModpackModrinth.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\ResourceGetterService\Modrinth\GetModrinth.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\ResourceGetterService\Modrinth\GetShaderPackModrinth.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Resource\ResourceGetterService\Modrinth\SearchModrinth.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\DefaultComponent\Server\MinecraftServerManger.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\AutoUpdate.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\Init.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\JavaManager.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\ModelDataMangers\AccountManger.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\ModelDataMangers\BasicDataManger.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\ModelDataMangers\VersionListManager.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Global\UnitedCore.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\ImportPCL2Version\ImportMain.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\ImportPCL2Version\PclVersionInspector.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\JavaRuntimeInspector.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\Models\GameData.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\Models\ServerEntry.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\Models\SomeClasses.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\Models\UserModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Helper\Tools.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Minecraft\VersionsList.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher.Core\Services\ResourceInstallerService.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\Codes\HomeBackgroundStore.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\Codes\LaunchGame.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\Codes\OlanExceptionWorker.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Pages\AccountPageViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Pages\HomePageViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Pages\ServerPageViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Pages\SettingsPageViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Pages\VersionPageViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Panes\DownloadPaneViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Panes\EditGameDataPaneViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Panes\InitServerPaneViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Panes\PowerPlayPaneViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Panes\TaskCenterPaneViewModel.cs
- E:\OneLauncherProject\OneLauncher\OneLauncher\ViewModels\Panes\UserModelLoginPaneViewModel.cs
