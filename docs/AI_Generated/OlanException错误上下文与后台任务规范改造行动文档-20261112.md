# OlanException 错误上下文与后台任务规范改造行动文档

日期：2026-11-12。任务模式：**说明执行，仅分析并列出拟执行操作，不修改实际代码。** 本次只新增本行动文档，没有运行应用、修改项目文件、生成测试脚本或执行测试。

## 1. 本次要求与边界

本计划落实两项要求：

1. 所有 OlanException 的 message 必须说明当前做到的具体步骤，以及实际出错的文件、归档条目或请求 URL。纯输入校验、内存计算和平台能力检查不一定涉及文件或请求，此时写明具体字段/输入、实际值、校验步骤，并明确尚未执行 I/O；不能编造不存在的失败文件或 URL。
2. 所有未等待的异步执行，业务执行体发生真实错误时必须抛出带完整上下文的 OlanException；外层必须观察异常。后续以 OlanException 构造函数作为错误日志统一执行点。

这里的“必须抛出”是指**失败时抛出**，不是成功或正常取消也抛异常。保留真正的用户取消、预期离线结果和界面关闭控制流，超时、I/O 或代码错误不得借“可选功能”之名无记录吞掉。

范围为当前生产源码 OneLauncher.Core、OneLauncher、OneLauncher.Console、OneLauncher.Desktop 的静态审计。排除 obj/bin、docs 中的代码副本、注释中的未启用代码和测试故意制造的异常。检查覆盖 rg 文本搜索、Roslyn 语法树只读分析，以及对返回类型、调用链和条件编译分支的人工核对；没有进行编译或运行验证。

当前定位结果：

- **204 处 OlanException 构造表达式，分布在 69 个文件**：包含普通 new、完全限定名、目标类型 new 和 Windows 条件编译分支；三个构造函数定义另列。
- **17 处异常工厂抛出分支**：JvmOptimizationPolicy.Error 的 15 处调用，以及 JarModSupport.Unsupported 的 2 处调用。这些与工厂构造位置有关联，不能直接与 204 相加作为异常总数。
- **77 处明确未等待的 Task 调用，分布在 21 个文件**，包含裸调用、丢弃赋值、构造函数、属性回调和重试 Action；其中含一处 DEBUG 设计模式刷新。
- **1 处 ForEach(async ...) 隐式 async void**，以及 **1 处启动后可能始终没有人等待的联机预初始化任务**，单独列出。
- 补充列出 **23 处 Dispatcher.UIThread.Post 调度点、22 处未知异常展示调用**。这些是交叉清单，不是新增独立异常数量。
- 拟改现有生产源码共 **102 个文件**，另建议新增 4 个职责文件。既有详细消息也列入逐项审核/补齐阶段或来源范围，避免只改最明显的“失败”消息。

本文中的行号是当前工作区快照，实施时以方法/操作为定位依据。数量是静态定位结果，不宣称穷尽未来动态调用或第三方内部任务。

## 2. 将要采用的错误消息契约

### 2.1 必填内容

message 用可直接定位问题的语句描述：

- 操作：下载版本、读取配置、刷新认证、写入实例索引、启动 Java 等。
- 步骤：发起 GET、读取响应、反序列化某字段、写入临时文件、替换目标文件、校验第 N 个分片、启动进程等。
- 对象：实际文件完整路径、归档完整路径 + 内部条目，或请求方法 + 实际 URL。保留必要的公开版本/分页筛选参数，令牌、签名秘密、URL 用户凭证脱敏。
- 原因：HTTP 状态/错误码、期望与实际长度/摘要、缺失字段、进程退出码、重试次数与上限，以及原异常类型/消息。
- 关联：可得时包含实例 ID、任务/启动会话和版本/加载器；不输出密码、令牌、完整认证对象或整条游戏命令。

Title 保留简短分类，不把所有信息塞进标题。OriginalException 和标准 InnerException 保存真实底层异常，不能用字符串代替异常链。已有 OlanException 的 catch 优先直接 throw;，避免每层重新构造造成重复错误日志；缺少上下文应在第一次构造之前补足。

示例（示意，不是已经存在的代码或真实失败）：

> 安装 Minecraft 1.21.1：在下载资源文件后的 SHA-1 校验步骤失败。请求 GET https://example.test/assets/example.dat；目标 E:/OneLauncherProject/OneLauncher/build/example/assets/example.dat；已尝试 3/3 次；预期 SHA-1=…，实际 SHA-1=…。底层原因：摘要不一致。

> 导入整合包：打开 E:/…/pack.mrpack 后，在反序列化归档条目 modrinth.index.json 的 dependencies.minecraft 字段时失败；字段缺失。尚未开始下载游戏文件。

> 初始化设置页面：读取 E:/…/config.json 的 OlanSettings.MaximumDownloadThreads 后，在刷新设置列表阶段失败；对应配置对象为 null。

message 中不能只剩 ex.Message、ex.ToString()、一个路径、一个版本号、“内部错误”或“重试到达阈值抛出”。可以保留这些信息作为详细上下文的一部分。

### 2.2 上下文必须沿调用链传递

仅有 Stream、JsonElement 或 Maven 坐标的解析器目前不知道源文件。将为 MrpackParser、ProfileReader、LegacyProfileReader、ForgePaths、ForgeLaunchArguments、ModLoaderDetector、JvmOptimizationPolicy 等增加显式上下文参数或在已有输入快照中保留来源。

URL 在构造请求的地方保存，失败时不能只输出一个 API 根地址。通用总 catch 维护当前阶段/当前对象，不能把配置写入失败、依赖解析失败都解释成 Java 未安装。LINQ 延迟枚举的异常在实际枚举执行时归一，而不是只包住返回枚举对象的语句。

为控制改动，优先增加兼容的可选上下文参数，但生产调用全部填入真实来源；测试的纯算法调用可明确标记“测试/直接输入，无来源文件”。无法获得 SDK 内部实际 URL 时写授权方式、SDK 阶段、错误码，并说明 SDK 未公开端点，不虚构 URL。

## 3. OlanException 构造函数的统一执行点

拟修改 Global/OlanException.cs 的三个构造函数，使其委托到一个完整初始化路径，在 Title、Message、Action、OriginalException、TryAgainFunction 和上下文全部就绪后，只调用一次统一错误入口。

需要同时处理：

1. 一个 OlanException 实例只发布一次记录；重抛、Task.SetFailed 和 UI 展示均复用该实例，不再各写一遍错误。
2. 构造发生在 throw 之前，此时该异常自己的 StackTrace 通常尚未生成。因此保留底层异常堆栈，并使用调用方显式阶段及 CallerMemberName/CallerFilePath/CallerLineNumber 等编译期信息辅助定位；调用位置只是补充，不能代替实际失败文件/URL。
3. 构造函数不 await、不承担文件轮转、不发 UI 消息；统一钩子仅将已经完整的错误交给未来日志实现。不得从构造函数再启动无人观察的 Task。
4. 记录钩子自身失败不能再构造 OlanException，否则递归。钩子隔离故障，并以有限、独立的兜底方式处理，不能覆盖原始错误。
5. 标准 InnerException 与现有 OriginalException 兼容保留。若 OriginalException 仍允许修改，记录点使用完整初始化时的快照；不允许先日志、后填原异常。
6. 不为日志专门构造一个永远不抛、不代表错误的异常。现有 Task.SetFailed(new OlanException(...)) 属于真实失败，可保留构造记录，但需补全 message。

这里仅规划**错误统一入口及契约**，不在本任务中扩展完整日志存储、滚动文件、云端上传或日志浏览 UI。此前的日志可行性研究作为文件输出层的独立后续方案，错误记录边界以本行动文档为准；Information 等正常事件仍可使用常规日志接口。

### 必须纠正的现有语义

SettingsPageViewModel.cs:677 把“安装路径已更改”这一成功通知构造成 FatalError，导致后续构造函数统一入口会把成功操作记成致命错误。计划改为正常通知及独立的重启/退出流程，保留“安装路径重启后生效”的现有产品语义；实际保存失败仍抛详细 OlanException。

MsalAuthenticator/UserModelLoginPaneViewModel 的用户主动取消目前可能由 null 变成“认证失败”，应与真实异常区分。Download.cs 的摘要错误消息不能直接推断“电脑被黑客入侵”，只记录已知证据和诊断上下文。

## 4. 未等待任务的执行与观察规则

### 4.1 保留后台行为时

采用两层边界：

1. **内部执行体**等待真正的业务任务。在知道当前阶段/文件/URL 的位置捕获普通异常，throw new OlanException(...)；已有 OlanException 直接重抛；已确认的用户取消保留取消状态。
2. **外部观察层**立即启动并 await 内部执行体，接收 OlanException，更新任务状态或提交 OlanExceptionWorker。Core 不依赖 Avalonia，前端通过 UI 适配器连接。观察层是明确的异常终点，不能把自己的 Task 再无保护丢弃。

调用必须传 Func<Task>，不能先执行一个可能同步抛错的非 async Task 方法后再交给观察器。同步的参数/对象初始化异常也在内部边界归一。

原来的 catch + 通知 + return 会把失败变成成功完成的 Task；对要求“内部必须抛出”的后台方法，应拆分为执行体与外层 UI 处理。不能仅在外层 new OlanException 然后吞掉，声称执行体已符合要求。后端可预期的降级也应在外层完成，真实内部失败仍经过统一入口。

异常展示本身也可能失败。Worker 的内部执行体归一并抛出，观察层接收后使用非递归兜底，不能再次打开同一个异常窗口。核实 Dispatcher.InvokeAsync 对 async 委托的返回类型和完整等待行为，避免只观察外层调度而漏掉内层异步展示任务。

### 4.2 适合等待时

账户增删、默认账户修改、实例保存/删除、文件写入、安装后实例登记等有完成顺序依赖，优先改为 await。成功通知、窗口关闭、刷新列表和删除磁盘目录，应在对应持久化步骤成功后执行。转换为 await 后，操作内部依旧按统一契约抛出 OlanException。

属性变化、构造函数、同步事件无法直接 await 时，保留后台方式并使用上述观察器；不改成 async void。

### 4.3 必须单独改的异步逃逸点

| 文件/位置 | 当前问题 | 拟执行操作 |
| --- | --- | --- |
| [DownloadPaneViewModel.cs:185](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs:185) | UserInfo.ForEach(async u => ...) 产生隐式 async void；外层安装 catch 不能可靠捕获，默认实例可能在登记完成前设置 | 改为顺序 foreach + await AddGameDataAsync。逐实例登记体内记录 instance.json、InstanceId 和阶段，并抛 OlanException；全部成功后才设置默认实例及完成任务。 |
| [HomePageViewModel.cs:285](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:285) | 联机预初始化 Task 仅在用户打开联机页面时才 await；未打开可能永不观察 | 在创建后立刻附加保留结果的观察器；Factory/底层执行体抛详细 OlanException，后续打开页面复用成功结果或同一失败实例，避免重复日志。 |
| [GameLauncher.cs:37](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs:37) | resh 通常在 62 行被 await，但创建命令等提前失败可能绕过等待；取消 WaitAsync 也可能让原认证 Task 继续 | 为已经启动的认证任务建立所有权，提前失败/取消时仍观察其终态；认证具体错误在内部归一，正常路径保持并行登录。 |

InstallPackPaneViewModel 的 iconBytesTask 已在正常流程及 finally 中 await，Download.cs 的 SHA-1 Task 集合已由 WhenAll 等待，不作为 fire 误改。返回 Task 由调用者等待、await Task.Run 和 Parallel.ForEachAsync 的异步委托也不能仅因缺少局部 await 就误判。

## 5. 全部既有 OlanException 构造位置的修改清单

下表覆盖 204 处构造表达式。每个文件列出全部行号和该文件应补充的上下文；标题/恢复策略原则上保留，仅纠正已明确误用的成功 FatalError 等语义。具体哪个文件/URL 在执行现场确定，不使用固定猜测值。

| 文件 | 当前构造行号 | 将补齐或调整的内容 |
| --- | --- | --- |
| [OneLauncher.Core/DefaultComponent/Account/Microsoft/MojangProfile.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Account/Microsoft/MojangProfile.cs:141) | 141、183 | SetUseUrl/GetSkinHeadImage：记录皮肤服务端点、提交/重试/下载/保存阶段、本地目标图片或来源皮肤 URL、尝试次数及 HTTP 状态；不输出认证头和响应秘密。 |
| [OneLauncher.Core/DefaultComponent/Account/Microsoft/MsalAuthenticator.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Account/Microsoft/MsalAuthenticator.cs:55) | 55、184、193、222、232、242、264、329、333、337、341、345、349、353、357 | WAM/浏览器/MSAL 缓存刷新、Xbox Live、XSTS、Minecraft login_with_xbox、entitlements、profile 各阶段分别记录；各 HTTP 步骤写实际端点与状态，XSTS 附 XErr，MSAL 附错误码；SDK 内部 URL 不可得时明确说明，不编造。 |
| [OneLauncher.Core/DefaultComponent/Account/Yggdrasil/YggdrasilAuthenticator.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Account/Yggdrasil/YggdrasilAuthenticator.cs:32) | 32、36、41、77、84、102 | 区分获取元数据、POST authenticate、读取 selectedProfile 和 POST refresh；记录实际请求 URL、HTTP 状态及解析字段，不写密码/clientToken/accessToken。 |
| [OneLauncher.Core/DefaultComponent/ConnectToolPower/MCTPower.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/ConnectToolPower/MCTPower.cs:44) | 44、48 | InitializationAsync：记录联机核心下载 URL、核心本地文件、校验阶段、期望/实际 MD5；没有摘要与摘要不一致分别解释。 |
| [OneLauncher.Core/DefaultComponent/ConnectToolPower/P2PMode.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/ConnectToolPower/P2PMode.cs:21) | 21 | 初始化联机能力：记录 OS/架构、需要的平台能力及核心程序路径；属于前置校验时不虚构请求。 |
| [OneLauncher.Core/DefaultComponent/Downloader/Download.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/Download.cs:81) | 81、83、93、97、237、264、322、374 | 所有下载/校验分支：记录探测、分段、下载重试、写文件或 SHA-1 阶段；加入实际 URL、savepath/item.path、分片范围、尝试次数/上限、HTTP 状态、期望/实际长度或摘要；删除把校验失败直接归因为黑客入侵的断言。 |
| [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadInfoCreater.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadInfoCreater.cs:41) | 41、67 | Create：记录校验 versionId、检索版本清单或获取版本 JSON 的阶段、VersionListUrl/具体版本 URL、GameRootPath 及清单文件。 |
| [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraft.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraft.cs:55) | 55 | 选择下载策略：记录策略实际值、versionId、游戏目标目录与尚未执行请求的前置阶段。 |
| [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/FabricProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/FabricProvider.cs:40) | 40、50 | GetDependencies：分别记录 loader/profile API 的实际 URL、Minecraft/loader 版本、反序列化阶段与计划保存路径。 |
| [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/QuiltProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/QuiltProvider.cs:38) | 38、88 | 解析 Quilt 元数据或选择 loader：写实际 metadata URL、Minecraft/指定 loader、筛选条件和生成配置目标路径。 |
| [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/SpecifiedVersionConfig/SpecifiedFabricVersionConfig.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/SpecifiedVersionConfig/SpecifiedFabricVersionConfig.cs:28) | 28、51、60 | 获取并解析指定 Fabric 列表、读取缓存状态、匹配指定版本：写 API URL、Minecraft/Fabric 请求版本及未初始化字段。 |
| [OneLauncher.Core/DefaultComponent/Downloader/RaceDownloader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/RaceDownloader.cs:121) | 121 | RaceSingleFileAsync：在全部源失败时列出目标文件、候选请求 URL、各源 HTTP 状态/网络原因、失败步骤；主动取消与真实失败分开。 |
| [OneLauncher.Core/DefaultComponent/JavaProviders/BaseJavaProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/BaseJavaProvider.cs:28) | 28、33、39、60 | Java 版本/平台/架构前置检查与 API 获取链接：写 Provider、Java 版本、OS/架构、实际 API URL 和解析字段；API 返回空/格式无效/请求失败分开。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:284) | 284 | Error 工厂及 15 个调用：记录堆大小解析、预算计算、GC 选择/兼容性/参数探测或引号解析阶段；具体字段值、冲突项、预算、Java 路径和配置/额外参数来源，不只传抽象错误句子。 |
| [OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs:105) | 105 | BasicLaunch：Win32Exception 保留原异常，说明调用 Process.Start 的阶段、Java 文件、WorkingDirectory、实例/版本；后台参数文件保存另加阶段与 LastLaunchArgs.bat 路径，不能落入启动失败的笼统 catch。 |
| [OneLauncher.Core/DefaultComponent/Launcher/LaunchCommandBuilder.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/LaunchCommandBuilder.cs:143) | 143 | CreateAndInitStrategy：记录为哪一实例/版本选择启动策略、modType、versionPath/basePath 和对应加载器配置文件；传递 JVM 参数的来源配置。 |
| [OneLauncher.Core/DefaultComponent/Launcher/LaunchService.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/LaunchService.cs:39) | 39、160 | Play/OnGameClosed：记录检索实例步骤、InstanceId 和实例索引路径；异常退出记录 GameSession/Task、Java/实例路径和实际 exitCode，取消不误记失败。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Strategys/FabricStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/FabricStrategy.cs:27) | 27 | 读取 Fabric 启动配置：注明完整 version.fabric.json 路径、读取/JSON 解析阶段及实例/版本。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Strategys/QuiltStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/QuiltStrategy.cs:26) | 26 | 读取 Quilt 启动配置：注明完整 quiltJsonPath、JSON 解析步骤、Minecraft/loader 版本。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesInstallTasker.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesInstallTasker.cs:46) | 46、53 | StartReadyAsync/RunProcessorsAsync：写 installerUrl、下载的安装包路径、准备/打开归档/读取 profile/处理器执行阶段、Minecraft/loader；准备顺序错误说明缺失状态与当前工作目录。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesUsing.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesUsing.cs:14) | 14、17、21、39 | 读取并校验 version.forge/neoforge.json 与启动库：写 jsonPath、解析字段、请求/实际加载器、缺失库路径；将来源传给检测器和参数解析器。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeVersionListGetter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeVersionListGetter.cs:37) | 37、46、55、61、66、69、83、116、136、144、152、170、178 | 版本输入/匹配/推荐/列表下载/XML 解析：写实际 promotions 或 Maven metadata URL、Minecraft/Forge/NeoForge 请求版本、筛选条件、HTTP 状态或 XML 位置；只显示 fullVersion 的分支补上步骤。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeFileVerifier.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeFileVerifier.cs:30) | 30 | RequireAsync：将文件不存在、长度错误、摘要错误、归档损坏拆成准确原因；写 path、校验阶段、期望/实际值与来源操作。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeInstallerParser.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeInstallerParser.cs:18) | 18、26、31、36、40、58、78、80、85、92、98、102 | Parse：所有分支写 installerPath、install_profile.json/版本条目、请求 Minecraft/loader、当前库坐标、URL/目标路径、期望/实际摘要及操作步骤；25 的工厂调用另列。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePaths.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePaths.cs:11) | 11、17、25、29 | Maven 坐标校验和路径边界验证：写 coordinate、relative、允许 root、规范化目标和调用阶段/来源文件，拒绝值过长时限长。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/JarMod/JarModSupport.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/JarMod/JarModSupport.cs:13) | 13 | Unsupported/RequireInstaller：接收 Minecraft/Forge 版本和来源安装包/请求上下文，说明检测 jar-mod 格式或前置版本兼容校验步骤；仅返回泛化工厂消息不足。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Launch/ForgeLaunchArguments.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Launch/ForgeLaunchArguments.cs:44) | 44、51、64、92 | Expand/LegacyGame/Tokenize：接收来源 JSON/归档条目和 JVM/game 参数字段，写元素位置/类型、tweakClass 或引号出错位置，不输出完整带令牌启动参数。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/ForgeRealmsProcessor.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/ForgeRealmsProcessor.cs:40) | 40 | DEOBF_REALMS：写 installer/profile 来源、处理器索引、当前输出目录、具体不支持的自定义参数名称。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/LegacyProfileReader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/LegacyProfileReader.cs:12) | 12 | Read：接收安装包及 install_profile.json 来源，说明旧版 install/versionInfo 的缺失字段和读取步骤。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorRunner.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorRunner.cs:19) | 19、32、49、72、85、90 | RunAsync/ReadMainClass：写 Java 路径、处理器序号/坐标/JAR、classpath 缺失文件、输出路径、工作目录、exitCode 与 MANIFEST.MF 条目；诊断文本脱敏限长。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorTokens.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorTokens.cs:43) | 43、47、51、53、61、67、74、82、88 | Resolve/Expand：写安装包/profile 来源、处理器序号、占位符名/访问链、资源 entryName、root/目标路径和实际递归深度/长度限制。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Profiles/ProfileReader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Profiles/ProfileReader.cs:14) | 14、16、20 | Read：接收 installerPath，分别写 install_profile.json 或 profile.Json 对应归档条目、spec/缺失字段与读取/反序列化步骤。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ModLoaderDetector.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ModLoaderDetector.cs:32) | 32、35、37、40 | Detect：接收源文件/归档上下文，区分主类、库坐标和 tweakClass 检测阶段，列出产生冲突的身份证据；不要记录全部潜在敏感参数。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Manager/FabricModInfoHelper.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Manager/FabricModInfoHelper.cs:20) | 20 | 解析模组清单：写 filePath、归档条目 fabric.mod.json、读取/JSON 解析步骤和无效字段。 |
| [OneLauncher.Core/DefaultComponent/Resource/Pack/ModOutpoter/ModpackExporter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Pack/ModOutpoter/ModpackExporter.cs:27) | 27、104、157、171 | 导出实例、创建归档、读取加载器元数据：记录 instancePath、导出目标包、当前正在加入的文件、metadataPath 与读/写/解析阶段。 |
| [OneLauncher.Core/DefaultComponent/Resource/Pack/ModpackImporter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Pack/ModpackImporter.cs:81) | 81、101 | 定位清单与创建实例：写来源 .mrpack 路径、归档条目、instancePath、实例索引文件与校验步骤；传给 MrpackParser。 |
| [OneLauncher.Core/DefaultComponent/Resource/Pack/MrpackParser.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Pack/MrpackParser.cs:27) | 27、38、57 | 构造/GetMinecraftVersion/GetLoaderInfo：新增来源上下文，包含 .mrpack 完整路径或下载 URL、归档条目 modrinth.index.json、JSON 字段路径及解析/依赖验证步骤。 |
| [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModpackModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModpackModrinth.cs:20) | 20、28、49 | GetAsync：写实际版本查询 URL、projectId、筛选条件、选中版本、.mrpack 下载 URL/local temporaryPath 和清单条目；把来源传给 MrpackParser。 |
| [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModrinth.cs:31) | 31 | Init：写项目 ID、请求版本 API URL、Minecraft/loader 和匹配/安装阶段；不能只有“模组安装失败”。 |
| [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetShaderPackModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetShaderPackModrinth.cs:29) | 29、31、39、48 | GetDownloadInfoAsync：写实际 API URL、projectId、Minecraft/选中版本、文件名/下载 URL、目标 shaderpacks 目录与筛选/校验阶段。 |
| [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/SearchModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/SearchModrinth.cs:39) | 39 | ToSearch：写编码后的实际搜索 URL、公开筛选条件、分页 offset/limit、反序列化阶段与 HTTP 状态；不写敏感请求头。 |
| [OneLauncher.Core/DefaultComponent/Server/MinecraftServerManger.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Server/MinecraftServerManger.cs:30) | 30 | 初始化服务端：记录版本 ID、服务端目标路径和检测缺少 server 下载项的步骤；尚未请求时明确没有实际请求 URL。 |
| [OneLauncher.Core/Global/Init.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/Init.cs:83) | 83、87、91、95、99、103、107 | Initialize 七个 catch：维护当前初始化阶段及正在处理的路径/URL，涵盖数据目录、config.json、安装目录与 UnitedCore 子初始化；保存 OriginalException，不能只有“权限不足”等大类名称。 |
| [OneLauncher.Core/Global/JavaManager.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/JavaManager.cs:92) | 92 | InstallJava：记录选择 Java Provider 阶段、Provider 实际值、Java 版本、OS/架构与安装目标目录。 |
| [OneLauncher.Core/Global/ModelDataMangers/AccountManger.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/ModelDataMangers/AccountManger.cs:67) | 67、79 | GetDefaultUser/RemoveUser：记录读取默认账户或删除账户步骤、ConfigPath、目标本地账户标识与当前剩余数量；持久化失败仍经 BasicDataManager 归一。 |
| [OneLauncher.Core/Global/ModelDataMangers/BasicDataManger.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/ModelDataMangers/BasicDataManger.cs:79) | 79、115 | ReadDataAsync/SaveDataAsync：区分创建默认值、打开、读取、JSON 反序列化、序列化、写临时文件、原子替换；记录 ConfigPath、temporaryPath、失败步骤和底层异常。 |
| [OneLauncher.Core/Global/ModelDataMangers/VersionListManager.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/ModelDataMangers/VersionListManager.cs:54) | 54、93 | RegisterInstalledVersionAsync/EnsureGameRoot：记录登记阶段、versionId、待登记版本目录、索引文件和期望/实际游戏根目录。 |
| [OneLauncher.Core/Helper/ImportPCL2Version/ImportMain.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/ImportPCL2Version/ImportMain.cs:57) | 57、142 | ImportAsync/FindPclVersionJson：记录选择的来源目录、定位 .minecraft/versions 或查找 JSON 的具体阶段、当前目录和预期文件；传给继承链解析器。 |
| [OneLauncher.Core/Helper/ImportPCL2Version/PclVersionInspector.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/ImportPCL2Version/PclVersionInspector.cs:18) | 18、26、46、50、59、60 | ReadAsync：逐步记录原始 JSON、当前/父版本 JSON 路径、继承链、循环节点/深度、缺失主类或基础版本字段；传递给 Tokenize/Expand 与加载器检测。 |
| [OneLauncher.Core/Helper/JavaRuntimeInspector.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/JavaRuntimeInspector.cs:75) | 75 | SupportsArgumentsAsync：注明探测 JVM 参数阶段、Java 可执行文件路径、工作目录、退出码/超时及已脱敏的参数诊断。 |
| [OneLauncher.Core/Helper/Models/GameData.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/GameData.cs:36) | 36 | GetUserModel：记录解析启动账户阶段、InstanceId、DefaultUserModelID 及账户索引文件；不输出账户对象或令牌。 |
| [OneLauncher.Core/Helper/Models/ServerEntry.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/ServerEntry.cs:66) | 66、124、177、187、206、212、234、243、253 | SaveInServerDAT/GetAASServerInfo/SaveServerIcon：区分地址验证、读取/修改/写入 servers.dat、状态 API 请求/解析、图标解码/保存；记录实际 URL、状态码、目标 path/IconFileUrl、字段与尺寸限制。 |
| [OneLauncher.Core/Helper/Models/SomeClasses.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/SomeClasses.cs:26) | 26 | ServerInfo 输入校验：记录构造或解析服务器地址阶段、被拒绝的 host/port 与校验范围；不存在文件或请求时明确为输入阶段。 |
| [OneLauncher.Core/Helper/Models/UserModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/UserModel.cs:140) | 140 | IntelligentLogin：区分查缓存、刷新认证、读取结果、保存账户；空刷新结果说明认证阶段/端点及本地记录标识；原来的“失败”改为可定位信息。 |
| [OneLauncher.Core/Helper/Tools.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Tools.cs:136) | 136 | OpenFolder：注明调用系统文件管理器阶段、folderPath、使用的执行程序及 OS；为加载器检测转发来源上下文。 |
| [OneLauncher.Core/Minecraft/VersionsList.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Minecraft/VersionsList.cs:39) | 39 | 记录读取/反序列化 version_manifest.json 阶段、filePath 和 VersionListUrl；后台刷新另外区分下载、替换文件、保存 config.json 的阶段。 |
| [OneLauncher.Core/Services/ResourceInstallerService.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Services/ResourceInstallerService.cs:92) | 92 | InstallMod：记录获取兼容版本、筛选下载文件或安装阶段，Modrinth 版本请求 URL、modId、Minecraft/加载器版本、实例 mods 目标目录。 |
| [OneLauncher/Codes/HomeBackgroundStore.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/HomeBackgroundStore.cs:86) | 86 | SaveAsPng：注明读取/解码/转码/写入/替换阶段、sourcePath、目标背景文件及临时文件；保留 OriginalException。 |
| [OneLauncher/Codes/LaunchGame.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/LaunchGame.cs:75) | 75、112、117、122、127 | EasyGameLauncher：加入启动阶段、InstanceId、Minecraft/加载器、实例路径、Java 路径及可得的具体失败文件；异常退出加入 PID/退出码；重试路径复用相同上下文，移除“所有未知异常均是未安装 Java”的归因。 |
| [OneLauncher/Codes/OlanExceptionWorker.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/OlanExceptionWorker.cs:38) | 38 | ForUnknowException：接收调用方显式操作上下文，不能仅使用 exception.ToString()；展示失败也由内层抛 OlanException、外层观察，避免递归打开异常窗。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:125) | 125 | 构建账户列表：写账户配置文件、为空的具体字段、加载列表/读取头像的阶段；修复回调的删除 config.json 与重新初始化分步归一，不能只归因版本不兼容。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:65) | 65 | 快速启动服务器实例定位：写本地服务器标识、InstanceId、实例索引路径和查找步骤；后台新闻/更新/保存/启动的错误另按执行阶段归一。 |
| [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs:243) | 243 | 启动收藏服务器：写本地收藏标识、InstanceId、实例索引路径及定位实例步骤；后台探测另记录目标 host/port 与 UI 更新阶段。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:102) | 102、677、691、718、849 | 保存 JVM/读取设置/文件选择/校验安装路径：写具体字段、config.json、输入路径/解析后路径与失败阶段。677 的安装路径修改成功不能继续构造 FatalError，应改为正常通知及明确的重启流程。 |
| [OneLauncher/ViewModels/Pages/VersionPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/VersionPageViewModel.cs:61) | 61 | 打开服务端目录：写选中 versionId、预期服务端路径和检查初始化状态的步骤。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:74) | 74 | 选择实例图标前置检查：写实例标识、图标来源/目标路径和文件选择能力；后台更新/标签/删除/复制/打包记录对应源、目标、索引文件。 |
| [OneLauncher/ViewModels/Panes/InitServerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InitServerPaneViewModel.cs:42) | 42 | 服务端安装前验证 EULA：写选中版本、服务端目标目录及尚未开始下载的前置步骤。 |
| [OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs:99) | 99、126、129、152、172 | 创建/加入联机房间校验：写具体阶段、所选实例/版本、端口校验值和依赖核心路径；房间码只给长度/格式/短期关联标识，不记录完整秘密。 |
| [OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs:126) | 126、133 | WAM 与浏览器登录空结果分别说明实际授权方式/阶段，区分主动取消与认证失败；保存账户失败给 account.json 路径，不输出账户凭证。 |

### 5.1 异常工厂的全部抛出分支

工厂定义本身已在上表列出，以下调用点也必须修改，保证首次构造时上下文已经完整。

| 文件 | 当前行号 | 将补齐的调用上下文 |
| --- | --- | --- |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:31) | 31、32、35 | 验证优化模式及显式堆配置：实际 mode、Xms/Xmx、来源 config.json 或实例设置。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:48) | 48、50、55 | 计算自动内存预算：可用内存、预算、请求 Xms/Xmx、失败阶段和配置来源。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:100) | 100 | 合并参数后比较堆大小：最终 Xms/Xmx、优先级/来源及实例/Java 上下文。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:120) | 120、125、170 | 验证所选 GC、识别 Java 能力、探测参数：Java 文件/版本、GC 名称、失败阶段、退出码及脱敏诊断。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:179) | 179、181 | 验证 G1 参数：具体字段、实际值、合法范围和配置来源。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:228) | 228 | 解析堆大小文本：出错字段/单位、限长后的输入与配置来源。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:248) | 248 | 选择垃圾回收器：发生冲突的选项、各自来源和解析步骤。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs:279) | 279 | 解析 JVM 参数引号：出错位置、来源字段/文件及步骤，不能输出带认证参数的整条命令。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/JarMod/JarModSupport.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/JarMod/JarModSupport.cs:10) | 10 | RequireInstaller 的版本兼容检查：Minecraft/Forge 版本及请求安装阶段。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeInstallerParser.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeInstallerParser.cs:25) | 25 | 检测安装包 jar-mod 归档：installerPath、mod_MinecraftForge.class 条目、Minecraft/Forge 版本。 |

## 6. 全部明确未等待 Task 调用的修改清单

共 77 处。下表中的“改为 await”表示移除原来的 fire 形式；保留 fire 的位置一律采用“内部抛出 + 外层观察”。

| 文件与行号 | 当前未等待调用 | 将执行的改造 |
| --- | --- | --- |
| [OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs:86):86 | `File.WriteAllTextAsync(Path.Combine(Init.BasePath, "LastLaunchArgs.bat"), finalCommand)` | 改为 await 或专用保存任务；失败体内写完整 LastLaunchArgs.bat 路径和写入阶段；参数内容不得进入 message。 |
| [OneLauncher.Core/DefaultComponent/Launcher/LaunchService.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/LaunchService.cs:184):184 | `StopAfterCancellationAsync()` | 停止体内按进程 PID/Java 路径/强制结束或等待退出步骤抛 OlanException；外层观察，正常取消不包装为失败。 |
| [OneLauncher.Core/Helper/Models/UserModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/UserModel.cs:149):149 | `UnitedCore.Core.AccountManager.Save()` | 优先 await 账户文件保存，区分认证刷新成功与持久化失败；IntelligentLogin 的总 catch 不再静默丢弃此失败。 |
| [OneLauncher.Core/Minecraft/VersionsList.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Minecraft/VersionsList.cs:26):26 | `Task.Run（清单后台刷新）` | 后台刷新体内区分清单下载/写入和刷新时间保存，含 VersionListUrl、filePath/config.json；外层观察并保留已有缓存。 |
| [OneLauncher/Codes/LaunchGame.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/LaunchGame.cs:80):80 | `EasyGameLauncher(unitedCore, gameData, serverInfo, true)` | 启动核心体内按具体阶段抛 OlanException，前端外层观察并展示；保留实例/Java/配置路径或请求 URL，不把未知原因统一写成 Java 缺失。 |
| [OneLauncher/Codes/LaunchGame.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/LaunchGame.cs:84):84 | `OlanExceptionWorker.ForOlanException(ex)` | 观察完整展示 Task；内部展示失败抛带窗口/调度步骤的 OlanException，外层使用不递归展示的兜底。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:121):121 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:133):133 | `Init.Initialize()` | 将删除 config.json 与重建核心拆为可等待的修复流程，按各步骤和路径包装后由重试观察器接收。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:137):137 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:169):169 | `_accountManager.SetDefault(user.um.UserID)` | 改为 await，等账户持久化/认证缓存移除成功后再刷新或关闭页面；写账户文件/缓存步骤并保留原异常。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:180):180 | `_msalAuthenticator.RemoveAccount( Tools.UseAccountIDToFind(user.AccountID).Result)` | 改为 await，等账户持久化/认证缓存移除成功后再刷新或关闭页面；写账户文件/缓存步骤并保留原异常。 |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:182):182 | `_accountManager.RemoveUser(user.UserID)` | 改为 await，等账户持久化/认证缓存移除成功后再刷新或关闭页面；写账户文件/缓存步骤并保留原异常。 |
| [OneLauncher/ViewModels/Pages/DownloadPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/DownloadPageViewModel.cs:43):43 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/DownloadPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/DownloadPageViewModel.cs:50):50 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs:61):61 | `Game.EasyGameLauncher( _unitedCore, gameData, useDebugMode: IsUseDebugModLaunch, serverInfo: quicklyPlayServerInfo )` | 启动核心体内按具体阶段抛 OlanException，前端外层观察并展示；保留实例/Java/配置路径或请求 URL，不把未知原因统一写成 Java 缺失。 |
| [OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs:242):242 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs:249):249 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs:341):341 | `_gameDataManager.Save()` | 可等待命令改为 await，先保存实例索引再反馈；属性回调保留后台但内部抛含实例/标签/索引路径的 OlanException；删除步骤区分登记和磁盘目录。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:289):289 | `InitializeNewsAsync()` | 新闻底层获取/JSON 解析抛含 news.json 实际 URL、索引和阶段的 OlanException；外层仍提供原有非阻断提示。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:295):295 | `Task.Run（检查更新）` | AutoUpdate.CheckUpdate 不再静默返回 false 掩盖故障；版本文件请求/解析体内抛含 CheckUpdateUrl 的 OlanException，再由观察器处理。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:316):316 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:373):373 | `SaveConfigSafelyAsync()` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:374):374 | `item.Launch()` | 启动核心体内按具体阶段抛 OlanException，前端外层观察并展示；保留实例/Java/配置路径或请求 URL，不把未知原因统一写成 Java 缺失。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:386):386 | `item.Launch()` | 启动核心体内按具体阶段抛 OlanException，前端外层观察并展示；保留实例/Java/配置路径或请求 URL，不把未知原因统一写成 Java 缺失。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:463):463 | `ImportVersionByPCL2()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:467):467 | `ImportVersionByPCL2()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:554):554 | `Import()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:559):559 | `Import()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs:45):45 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs:73):73 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs:137):137 | `viewModel.LoadInfo()` | 整合包加载体内按 Modrinth 请求/下载/读取清单步骤抛 OlanException，携带项目、实际 URL 和临时包路径。 |
| [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs:116):116 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs:121):121 | `ProbePingsAsync()` | 按服务器标识、host/port、探测或更新 UI 步骤归一；明确的离线结果保持普通返回，意外异常由内层抛后外层保留可选指示器降级。 |
| [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs:129):129 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs:173):173 | `RefreshPingAsync(serverId)` | 按服务器标识、host/port、探测或更新 UI 步骤归一；明确的离线结果保持普通返回，意外异常由内层抛后外层保留可选指示器降级。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:70):70 | `SaveJvmModeAsync(OptimizationMode.Conservative)` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:80):80 | `SaveJvmModeAsync(OptimizationMode.Standard)` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:90):90 | `SaveJvmModeAsync(OptimizationMode.Aggressive)` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:164):164 | `SaveIdentifierVisibilityAsync(value)` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:204):204 | `_dbManger.Save()` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:219):219 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:362):362 | `SaveHomePageStyleAsync(style)` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:379):379 | `SaveHomeLaunchItemAsync(value)` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:636):636 | `SaveInstallPathAsync("${OLAN_BASE_PATH}/installed")` | 属性/回调触发的保存内部按解析/创建目录/写 config.json 分阶段抛 OlanException，外层观察；成功重启提示单独处理。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:638):638 | `SaveInstallPathAsync("${OLAN_APP_RUNNING_PATH}/installed")` | 属性/回调触发的保存内部按解析/创建目录/写 config.json 分阶段抛 OlanException，外层观察；成功重启提示单独处理。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:740):740 | `_dbManger.Save()` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:753):753 | `_dbManger.Save()` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:766):766 | `_dbManger.Save()` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:786):786 | `_dbManger.Save()` | 保存体内按具体设置字段和 config.json 路径抛 OlanException；外层处理 UI/回滚，不再把内部失败吞成成功 Task。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:818):818 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:834):834 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:857):857 | `Init.Initialize()` | 将删除 config.json 与重建核心拆为可等待的修复流程，按各步骤和路径包装后由重试观察器接收。 |
| [OneLauncher/ViewModels/Pages/VersionPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/VersionPageViewModel.cs:60):60 | `OlanExceptionWorker.ForOlanException( new OlanException("无法打开服务端文件夹","服务端尚未初始化",OlanExceptionAction.Error))` | 观察完整展示 Task；内部展示失败抛带窗口/调度步骤的 OlanException，外层使用不递归展示的兜底。 |
| [OneLauncher/ViewModels/Pages/VersionPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/VersionPageViewModel.cs:106):106 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Pages/VersionPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/VersionPageViewModel.cs:139):139 | `RefList()` | 刷新列表体内按当前页面、数据索引/头像/图标文件和构建或调度步骤归一为 OlanException；构造函数/事件只负责提交与观察。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:88):88 | `_gameDataManager.Save()` | 可等待命令改为 await，先保存实例索引再反馈；属性回调保留后台但内部抛含实例/标签/索引路径的 OlanException；删除步骤区分登记和磁盘目录。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:104):104 | `_gameDataManager.SetTagForInstance(Id, SelectedTag.ID)` | 可等待命令改为 await，先保存实例索引再反馈；属性回调保留后台但内部抛含实例/标签/索引路径的 OlanException；删除步骤区分登记和磁盘目录。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:161):161 | `CopyThisInstance()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:165):165 | `CopyThisInstance()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:193):193 | `ZipThisInstance()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:197):197 | `ZipThisInstance()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:244):244 | `_gameDataManager.SetTagForInstance(editingGameData.InstanceId, SelectedTag.ID)` | 可等待命令改为 await，先保存实例索引再反馈；属性回调保留后台但内部抛含实例/标签/索引路径的 OlanException；删除步骤区分登记和磁盘目录。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:245):245 | `_gameDataManager.Save()` | 可等待命令改为 await，先保存实例索引再反馈；属性回调保留后台但内部抛含实例/标签/索引路径的 OlanException；删除步骤区分登记和磁盘目录。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:256):256 | `_gameDataManager.RemoveGameDataAsync(Id)` | 可等待命令改为 await，先保存实例索引再反馈；属性回调保留后台但内部抛含实例/标签/索引路径的 OlanException；删除步骤区分登记和磁盘目录。 |
| [OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs:119):119 | `GetServerInfo()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs:123):123 | `GetServerInfo()` | 重试委托交给观察器；执行体内保留来源/目标文件或实际请求 URL 和当前导入/复制/压缩/查询步骤。 |
| [OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs:43):43 | `OlanExceptionWorker.ForOlanException(e)` | 观察完整展示 Task；内部展示失败抛带窗口/调度步骤的 OlanException，外层使用不递归展示的兜底。 |
| [OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs:47):47 | `OlanExceptionWorker.ForUnknowException(e)` | 观察完整展示 Task；内部展示失败抛带窗口/调度步骤的 OlanException，外层使用不递归展示的兜底。 |
| [OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs:90):90 | `RefreshModsAsync()` | 加载模组体内抛含实例 mods 目录、当前 JAR/清单与读取阶段的 OlanException；外层观察和展示。 |
| [OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs:141):141 | `InstallRecommendedModsAsync(newGameData)` | 安装体内归一为含项目/下载 URL、目标 mods 目录、当前文件和阶段的 OlanException，任务 SetFailed 使用同一实例。 |
| [OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs:108):108 | `Game.EasyGameLauncher(_unitedCore, SelectedHostGameData, null, false)` | 启动核心体内按具体阶段抛 OlanException，前端外层观察并展示；保留实例/Java/配置路径或请求 URL，不把未知原因统一写成 Java 缺失。 |
| [OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs:196):196 | `Game.EasyGameLauncher(_unitedCore, instanceToLaunch, new ServerInfo { Ip = "127.0.0.1", Port = checked((ushort)localPort) }, false)` | 启动核心体内按具体阶段抛 OlanException，前端外层观察并展示；保留实例/Java/配置路径或请求 URL，不把未知原因统一写成 Java 缺失。 |
| [OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs:91):91 | `ac.AddUser(new UserModel( UserID: Guid.NewGuid(), name: UserName, uuid: Guid.NewGuid() ))` | 改为 await，等账户持久化/认证缓存移除成功后再刷新或关闭页面；写账户文件/缓存步骤并保留原异常。 |
| [OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs:155):155 | `LoginWithMicrosoftHandle(true)` | 浏览器回退登录体内抛带授权方式/阶段和账户保存路径的 OlanException；回退动作由重试观察器接收。 |
| [OneLauncher/Views/MainWindow.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/MainWindow.axaml.cs:102):102 | `OlanExceptionWorker.ForOlanException(e)` | 观察完整展示 Task；内部展示失败抛带窗口/调度步骤的 OlanException，外层使用不递归展示的兜底。 |
| [OneLauncher/Views/MainWindow.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/MainWindow.axaml.cs:132):132 | `OlanExceptionWorker.ForOlanException(ex)` | 观察完整展示 Task；内部展示失败抛带窗口/调度步骤的 OlanException，外层使用不递归展示的兜底。 |
| [OneLauncher/Views/Pages/settings.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Pages/settings.axaml.cs:29):29 | `Task.Run（彩蛋循环）` | 彩蛋后台循环增加生命周期取消；Task 与内部 Post 分别保护，内部失败抛带更新窗口颜色步骤的 OlanException。 |

### 6.1 调度后执行的 UI 委托补充清单

共 23 处 Post，属于额外的异步执行边界，部分与上表属于同一条业务链。将异常处理放在被调度委托内，并由外层接收。进度/状态更新附带现有 task/instance 上下文，不把进度文本里的任意字符串当成真实文件路径。

| 文件 | 当前行号 | 将执行的改造 |
| --- | --- | --- |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:139) | 139 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs:245) | 245 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:201) | 201、221 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs:119) | 119 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:413) | 413 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs:232) | 232 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs:175) | 175、256 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs:188) | 188、304 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs:153) | 153 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs:235) | 235、236 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Panes/TaskCenterPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/TaskCenterPaneViewModel.cs:39) | 39、139、151 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/ViewModels/Windows/GameTaskerViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Windows/GameTaskerViewModel.cs:34) | 34 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/Views/MainWindow.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/MainWindow.axaml.cs:145) | 145、184 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/Views/Pages/gamedata.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Pages/gamedata.axaml.cs:28) | 28、40 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |
| [OneLauncher/Views/Pages/settings.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Pages/settings.axaml.cs:37) | 37 | 在被调度的委托内部归一异常；传入当前操作/任务/文件上下文；外层接收，不能指望提交 Post 的 try/catch 捕获以后发生的异常。 |

### 6.2 已吞掉真实失败的执行体

下列路径除了调用点，还必须修改方法内部；仅给原调用加观察器无法看到已被吞掉的异常：

- UserModel.IntelligentLogin 的总 catch：不能把认证或保存失败静默转成返回旧实例。内层抛出详细 OlanException，是否保留旧账户由外层明确决定。
- LaunchService.StopAfterCancellationAsync 的 catch：不能只有 Debug 输出；内部抛出含进程/步骤的 OlanException。
- AutoUpdate.CheckUpdate 的 catch：请求或解析错误不能与“没有更新”共用 false 返回值；由观察层决定保持主页可用。
- HomePageViewModel.UpdateNewsAsync 的 catch：把获取/解析新闻执行体与非阻断提示分开，内部必须抛 OlanException。
- Game.EasyGameLauncher、Settings 保存方法、ModsBrowser.RefList、InstallPack.LoadInfo、NewGameData.InstallRecommendedModsAsync、UserModelLogin.LoginWithMicrosoftHandle 及导入/复制/压缩重试方法：当前内部展示后结束的异常路径按执行体与外层展示拆分；Task.SetFailed 与 Worker 使用同一个异常实例。
- ServerPageViewModel.ReadPingAsync：预期不可达可以是普通探测结果；格式、平台调用等意外异常不能统统吞掉。内部归一后，外层保持 Ping 可选指标的原有降级体验。
- 同一后台链中保存配置时触发 BasicDataManager.OnDataChanged，订阅回调不能破坏保存结果；失败分别归一并由回调观察边界接收。

## 7. 未知异常展示与任务失败的配套调整

ForUnknowException 当前只能得到裸异常，无法知道正在处理哪一个文件/URL。将接收显式 ExceptionOperationContext；调用方必须在现场传入，不能只增加默认“未知操作”。以下覆盖现有 22 个有效调用点。

| 文件 | 当前调用行号 | 将执行的改造 |
| --- | --- | --- |
| [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs:94) | 94 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs:393) | 393、467、495、559 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs:109) | 109 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs:187) | 187、306、584 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs:211) | 211 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs:165) | 165、197 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs:123) | 123 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs:47) | 47 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs:215) | 215 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs:338) | 338 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/JavaInstallPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/JavaInstallPaneViewModel.cs:150) | 150 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs:135) | 135、186 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs:116) | 116、214 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |
| [OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs:150) | 150 | 从 catch 的操作现场传入阶段、真实文件/URL、目标标识；若内层已有 OlanException，直接交给 Worker，避免再次构造。 |

如果底层已抛 OlanException，Worker 仅展示既有实例。作为兼容的未知异常入口，Worker 可将遗漏的普通异常构造成 OlanException，但保留 fire 的业务链必须在自己的执行体先满足“内部抛出”的要求。

Task.SetFailed 的裸异常调用也要核对：LaunchService.cs:145；HomePageViewModel.cs:459、553、558；NewGameDataPaneViewModel.cs:178、184；JavaInstallPaneViewModel.cs:141、147；InstallPackPaneViewModel.cs:219；InstallModPaneViewModel.cs:235；DownloadPaneViewModel.cs:207。其中已是 OlanException 的分支直接复用，普通异常在具备阶段/目标的执行体先归一，不能等到展示时才形成日志。SetFailed 本身不推测失败 URL 或重复构造。

## 8. 为完成以上要求必须调整的支持文件

下表包含构造/调用清单之外的上下文传递文件，也说明统一入口等核心文件的具体职责。

| 文件 | 定位 | 将执行的改造 |
| --- | --- | --- |
| [OneLauncher.Core/Global/OlanException.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/OlanException.cs) | 22、28、35 三个构造函数 | 保留现有调用兼容性并汇入一个完整初始化路径；补 InnerException/OriginalException 与上下文；只在完整初始化后触发一次错误记录入口。 |
| [OneLauncher.Core/Global/TaskManager.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/TaskManager.cs) | TaskInstance.SetFailed（34）及任务创建/状态事件 | 需要把后台错误交给 SetFailed 的调用处先使用完整上下文归一为 OlanException；这里只保存/发布同一实例，不另构造重复日志。TaskInstance 可增加运行期关联标识。 |
| [OneLauncher.Core/Global/UnitedCore.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/UnitedCore.cs) | Initialize | 给各子初始化提供阶段与具体索引路径，传递统一上下文；不能依靠 Init 总 catch 推测子步骤。 |
| [OneLauncher.Core/Global/AutoUpdate.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/AutoUpdate.cs) | CheckUpdate（12–49） | 去掉吞异常后返回 false 的行为；请求和解析分别抛含 CheckUpdateUrl 的 OlanException，主页外层处理为可恢复后台失败。 |
| [OneLauncher.Core/Minecraft/NewReader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Minecraft/NewReader.cs) | GetNewsByIndexAsync、FetchAndSetNewsAsync | 底层 HTTP/JSON 异常提供 MojangNewsUrl、当前新闻索引与具体阶段，再抛 OlanException；保留真正的空列表/正常边界结果。 |
| [OneLauncher/ViewModels/Panes/Factories/PowerPlayPaneViewModelFactory.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/Factories/PowerPlayPaneViewModelFactory.cs) | CreateAsync | 联机预初始化的具体下载/校验/初始化异常在内部归一，并保存结果供使用时 await；启动时立即加观察器，未打开页面也不遗漏失败。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Strategys/ForgeStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/ForgeStrategy.cs) | GetAdditionalJvmArgs/GetAdditionalGameArgs | 给 ForgeLaunchArguments 传入 version.forge.json 与字段来源，必要时在枚举执行体中带上下文归一。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Strategys/NeoForgeStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/NeoForgeStrategy.cs) | GetAdditionalJvmArgs/GetAdditionalGameArgs | 同上，提供 version.neoforge.json 与字段来源，不能只在创建 LINQ 枚举时包 catch。 |
| [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/ForgeProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/ForgeProvider.cs) | GetDependencies | RequireInstaller 提供请求 Minecraft/指定 Forge 版本与下载阶段；取得 installerUrl 后将来源传到准备/解析。 |
| [OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs) | LoadInfo、ShowError（338）、进度回调 | 被后台调用的 LoadInfo 内层抛带 API/包文件上下文的 OlanException，外层负责 IsLoading/侧栏错误显示；原 iconBytesTask 在 finally 已 await，不误当作 fire。 |
| [OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs) | ToDownload/安装流程与未知异常展示 | 为 Worker 补充项目、版本请求/下载 URL、实例/目标文件和阶段；进度 Post 单独保护。 |
| [OneLauncher/ViewModels/Panes/JavaInstallPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/JavaInstallPaneViewModel.cs) | 安装任务及未知异常展示（150） | 传入 Provider、Java 版本、下载 URL、安装路径和当前阶段，Task.SetFailed 与展示使用同一 OlanException。 |
| [OneLauncher/Views/Pages/gamedata.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Pages/gamedata.axaml.cs) | 28、40 Post 委托 | 将清空选择/刷新绑定回调按 UI 操作阶段内部归一，外层观察；没有 I/O 时不伪造文件错误。 |
| [OneLauncher/ViewModels/Windows/GameTaskerViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Windows/GameTaskerViewModel.cs) | 34 Post 委托 | 输出窗口更新内部按 game session/文本截断/绑定更新步骤归一，避免记录整段可能有秘密的游戏输出。 |
| [OneLauncher/ViewModels/Panes/TaskCenterPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/TaskCenterPaneViewModel.cs) | 39、139、151 Post 委托 | 任务列表与状态刷新回调带 task ID/阶段归一；其同步 RefList() 不误当作未等待 Task。 |
| [OneLauncher/Views/Windows/ExceptionTip.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Windows/ExceptionTip.axaml.cs) | TryAgainFunction（27） | 重试 Action 可能同步抛异常，异步操作由外层观察器接收；逐步兼容 Task 型重试，不能使用 async void 逃逸异常。 |
| [OneLauncher.Console/Boot.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Console/Boot.cs) | RunBoot catch（50） | 命令行异常终止前将未知异常转换为详细 OlanException（指令、阶段、实例/文件/URL），已是 OlanException 时复用；未来日志刷盘在 FailFast 前进行。 |
| [OneLauncher.Core/DefaultComponent/Launcher/Builders/ExtraJvmArgsBuilder.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/ExtraJvmArgsBuilder.cs) | GetArgumentsAsync（9） | 独立 JVM 参数构建也把来源配置、Java 文件、服务端/调用阶段传给 JvmOptimizationPolicy；MinecraftServerManger 的对应调用同步传入。 |
| [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePreparedInstall.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePreparedInstall.cs) | 输入快照模型 | 保留来源 installerUrl/installerPath、profile 条目和当前安装关联信息，供处理器、Tokens、校验器使用；不保存敏感命令文本。 |
| [OneLauncher.Core/DefaultComponent/JavaProviders/AdoptiumAPI.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/AdoptiumAPI.cs) | GetAutoAsync / GetBinaryPackageLinkAsync | 将构造的 apiUrl 与下载目的路径传给 BaseJavaProvider；HTTP/JSON 失败在知道 URL 的步骤内部归一。 |
| [OneLauncher.Core/DefaultComponent/JavaProviders/AzulZuluAPI.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/AzulZuluAPI.cs) | GetAutoAsync / GetDownloadUrl | 同上，保留实际查询 URL 与 Java 版本/平台字段，区分 API 请求失败与没有匹配包。 |
| [OneLauncher.Core/DefaultComponent/JavaProviders/GraalVMGetter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/GraalVMGetter.cs) | GetAutoAsync | 为基类传入已构造的 downloadUrl 与来源步骤，区分无需 API 的直接下载。 |
| [OneLauncher.Core/DefaultComponent/JavaProviders/MicrosoftBuildofOpenJDKGetter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/MicrosoftBuildofOpenJDKGetter.cs) | GetAutoAsync | 为基类传入已构造的下载 URL、平台/版本与来源步骤；可得时保留重定向后的实际地址。 |
| [OneLauncher.Core/DefaultComponent/JavaProviders/OracleJDK.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/OracleJDK.cs) | GetAutoAsync | 为基类传入已构造的 downloadUrl、版本/平台与来源步骤。 |

### 8.1 建议新增文件

可在实现时合并过小的类型，但职责不能丢失。不得另建全局 DI 容器或引入反射异常转换框架。

| 新增文件 | 用途 |
| --- | --- |
| `OneLauncher.Core/Global/ExceptionOperationContext.cs` | 显式承载操作阶段、目标文件/URL、归档条目、实例/任务标识、请求方法/状态等上下文；不依赖反射或任意对象序列化。 |
| `OneLauncher.Core/Global/BackgroundTaskObserver.cs` | 执行体归一并抛 OlanException，观察层 await 并接收；支持 Func<Task>、保留结果的 Task<T>、已知取消，所有自身内部任务均被收尾。 |
| `OneLauncher.Core/Global/OlanExceptionErrorHub.cs` | 为构造函数保留唯一错误记录钩子；日志写入器后续在此挂接，钩子失败不递归构造 OlanException，也不向外破坏原错误。 |
| `OneLauncher/Codes/UiOperationObserver.cs` | 连接 Core 观察器与 OlanExceptionWorker，保护 Post/重试委托，异常展示失败采用不递归展示的兜底。 |

### 8.2 现有生产源码完整文件索引

以下 102 个文件为上文所有清单的去重结果。不是声称每个文件都需要重构，具体改动遵循对应方法的局部错误上下文/观察边界。

| 序号 | 文件 |
| --- | --- |
| 1 | [OneLauncher.Console/Boot.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Console/Boot.cs) |
| 2 | [OneLauncher.Core/DefaultComponent/Account/Microsoft/MojangProfile.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Account/Microsoft/MojangProfile.cs) |
| 3 | [OneLauncher.Core/DefaultComponent/Account/Microsoft/MsalAuthenticator.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Account/Microsoft/MsalAuthenticator.cs) |
| 4 | [OneLauncher.Core/DefaultComponent/Account/Yggdrasil/YggdrasilAuthenticator.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Account/Yggdrasil/YggdrasilAuthenticator.cs) |
| 5 | [OneLauncher.Core/DefaultComponent/ConnectToolPower/MCTPower.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/ConnectToolPower/MCTPower.cs) |
| 6 | [OneLauncher.Core/DefaultComponent/ConnectToolPower/P2PMode.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/ConnectToolPower/P2PMode.cs) |
| 7 | [OneLauncher.Core/DefaultComponent/Downloader/Download.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/Download.cs) |
| 8 | [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadInfoCreater.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadInfoCreater.cs) |
| 9 | [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraft.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraft.cs) |
| 10 | [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/FabricProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/FabricProvider.cs) |
| 11 | [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/ForgeProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/ForgeProvider.cs) |
| 12 | [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/QuiltProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/QuiltProvider.cs) |
| 13 | [OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/SpecifiedVersionConfig/SpecifiedFabricVersionConfig.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/ModSources/SpecifiedVersionConfig/SpecifiedFabricVersionConfig.cs) |
| 14 | [OneLauncher.Core/DefaultComponent/Downloader/RaceDownloader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Downloader/RaceDownloader.cs) |
| 15 | [OneLauncher.Core/DefaultComponent/JavaProviders/AdoptiumAPI.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/AdoptiumAPI.cs) |
| 16 | [OneLauncher.Core/DefaultComponent/JavaProviders/AzulZuluAPI.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/AzulZuluAPI.cs) |
| 17 | [OneLauncher.Core/DefaultComponent/JavaProviders/BaseJavaProvider.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/BaseJavaProvider.cs) |
| 18 | [OneLauncher.Core/DefaultComponent/JavaProviders/GraalVMGetter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/GraalVMGetter.cs) |
| 19 | [OneLauncher.Core/DefaultComponent/JavaProviders/MicrosoftBuildofOpenJDKGetter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/MicrosoftBuildofOpenJDKGetter.cs) |
| 20 | [OneLauncher.Core/DefaultComponent/JavaProviders/OracleJDK.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/JavaProviders/OracleJDK.cs) |
| 21 | [OneLauncher.Core/DefaultComponent/Launcher/Builders/ExtraJvmArgsBuilder.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/ExtraJvmArgsBuilder.cs) |
| 22 | [OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Builders/JvmOptimizationPolicy.cs) |
| 23 | [OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/GameLauncher.cs) |
| 24 | [OneLauncher.Core/DefaultComponent/Launcher/LaunchCommandBuilder.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/LaunchCommandBuilder.cs) |
| 25 | [OneLauncher.Core/DefaultComponent/Launcher/LaunchService.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/LaunchService.cs) |
| 26 | [OneLauncher.Core/DefaultComponent/Launcher/Strategys/FabricStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/FabricStrategy.cs) |
| 27 | [OneLauncher.Core/DefaultComponent/Launcher/Strategys/ForgeStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/ForgeStrategy.cs) |
| 28 | [OneLauncher.Core/DefaultComponent/Launcher/Strategys/NeoForgeStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/NeoForgeStrategy.cs) |
| 29 | [OneLauncher.Core/DefaultComponent/Launcher/Strategys/QuiltStrategy.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Launcher/Strategys/QuiltStrategy.cs) |
| 30 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesInstallTasker.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesInstallTasker.cs) |
| 31 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesUsing.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeSeriesUsing.cs) |
| 32 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeVersionListGetter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/ForgeVersionListGetter.cs) |
| 33 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeFileVerifier.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeFileVerifier.cs) |
| 34 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeInstallerParser.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgeInstallerParser.cs) |
| 35 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePaths.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePaths.cs) |
| 36 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePreparedInstall.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Installation/ForgePreparedInstall.cs) |
| 37 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/JarMod/JarModSupport.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/JarMod/JarModSupport.cs) |
| 38 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Launch/ForgeLaunchArguments.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Launch/ForgeLaunchArguments.cs) |
| 39 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/ForgeRealmsProcessor.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/ForgeRealmsProcessor.cs) |
| 40 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/LegacyProfileReader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Legacy/LegacyProfileReader.cs) |
| 41 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorRunner.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorRunner.cs) |
| 42 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorTokens.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Processors/ForgeProcessorTokens.cs) |
| 43 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Profiles/ProfileReader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ForgeSeries/Profiles/ProfileReader.cs) |
| 44 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ModLoaderDetector.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Loader/ModLoaderDetector.cs) |
| 45 | [OneLauncher.Core/DefaultComponent/Resource/Mod/Manager/FabricModInfoHelper.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Mod/Manager/FabricModInfoHelper.cs) |
| 46 | [OneLauncher.Core/DefaultComponent/Resource/Pack/ModOutpoter/ModpackExporter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Pack/ModOutpoter/ModpackExporter.cs) |
| 47 | [OneLauncher.Core/DefaultComponent/Resource/Pack/ModpackImporter.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Pack/ModpackImporter.cs) |
| 48 | [OneLauncher.Core/DefaultComponent/Resource/Pack/MrpackParser.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/Pack/MrpackParser.cs) |
| 49 | [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModpackModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModpackModrinth.cs) |
| 50 | [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetModrinth.cs) |
| 51 | [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetShaderPackModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/GetShaderPackModrinth.cs) |
| 52 | [OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/SearchModrinth.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Resource/ResourceGetterService/Modrinth/SearchModrinth.cs) |
| 53 | [OneLauncher.Core/DefaultComponent/Server/MinecraftServerManger.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/DefaultComponent/Server/MinecraftServerManger.cs) |
| 54 | [OneLauncher.Core/Global/AutoUpdate.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/AutoUpdate.cs) |
| 55 | [OneLauncher.Core/Global/Init.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/Init.cs) |
| 56 | [OneLauncher.Core/Global/JavaManager.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/JavaManager.cs) |
| 57 | [OneLauncher.Core/Global/ModelDataMangers/AccountManger.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/ModelDataMangers/AccountManger.cs) |
| 58 | [OneLauncher.Core/Global/ModelDataMangers/BasicDataManger.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/ModelDataMangers/BasicDataManger.cs) |
| 59 | [OneLauncher.Core/Global/ModelDataMangers/VersionListManager.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/ModelDataMangers/VersionListManager.cs) |
| 60 | [OneLauncher.Core/Global/OlanException.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/OlanException.cs) |
| 61 | [OneLauncher.Core/Global/TaskManager.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/TaskManager.cs) |
| 62 | [OneLauncher.Core/Global/UnitedCore.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Global/UnitedCore.cs) |
| 63 | [OneLauncher.Core/Helper/ImportPCL2Version/ImportMain.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/ImportPCL2Version/ImportMain.cs) |
| 64 | [OneLauncher.Core/Helper/ImportPCL2Version/PclVersionInspector.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/ImportPCL2Version/PclVersionInspector.cs) |
| 65 | [OneLauncher.Core/Helper/JavaRuntimeInspector.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/JavaRuntimeInspector.cs) |
| 66 | [OneLauncher.Core/Helper/Models/GameData.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/GameData.cs) |
| 67 | [OneLauncher.Core/Helper/Models/ServerEntry.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/ServerEntry.cs) |
| 68 | [OneLauncher.Core/Helper/Models/SomeClasses.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/SomeClasses.cs) |
| 69 | [OneLauncher.Core/Helper/Models/UserModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Models/UserModel.cs) |
| 70 | [OneLauncher.Core/Helper/Tools.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Helper/Tools.cs) |
| 71 | [OneLauncher.Core/Minecraft/NewReader.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Minecraft/NewReader.cs) |
| 72 | [OneLauncher.Core/Minecraft/VersionsList.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Minecraft/VersionsList.cs) |
| 73 | [OneLauncher.Core/Services/ResourceInstallerService.cs](E:/OneLauncherProject/OneLauncher/OneLauncher.Core/Services/ResourceInstallerService.cs) |
| 74 | [OneLauncher/Codes/HomeBackgroundStore.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/HomeBackgroundStore.cs) |
| 75 | [OneLauncher/Codes/LaunchGame.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/LaunchGame.cs) |
| 76 | [OneLauncher/Codes/OlanExceptionWorker.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Codes/OlanExceptionWorker.cs) |
| 77 | [OneLauncher/ViewModels/Pages/AccountPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/AccountPageViewModel.cs) |
| 78 | [OneLauncher/ViewModels/Pages/DownloadPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/DownloadPageViewModel.cs) |
| 79 | [OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/GameDataPageViewModel.cs) |
| 80 | [OneLauncher/ViewModels/Pages/HomePageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/HomePageViewModel.cs) |
| 81 | [OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ModsBrowserViewModel.cs) |
| 82 | [OneLauncher/ViewModels/Pages/ServerPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/ServerPageViewModel.cs) |
| 83 | [OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/SettingsPageViewModel.cs) |
| 84 | [OneLauncher/ViewModels/Pages/VersionPageViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Pages/VersionPageViewModel.cs) |
| 85 | [OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs) |
| 86 | [OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditGameDataPaneViewModel.cs) |
| 87 | [OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/EditServerPaneViewModel.cs) |
| 88 | [OneLauncher/ViewModels/Panes/Factories/PowerPlayPaneViewModelFactory.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/Factories/PowerPlayPaneViewModelFactory.cs) |
| 89 | [OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/GameDataModsManagerPaneViewModel.cs) |
| 90 | [OneLauncher/ViewModels/Panes/InitServerPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InitServerPaneViewModel.cs) |
| 91 | [OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallModPaneViewModel.cs) |
| 92 | [OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/InstallPackPaneViewModel.cs) |
| 93 | [OneLauncher/ViewModels/Panes/JavaInstallPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/JavaInstallPaneViewModel.cs) |
| 94 | [OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/NewGameDataPaneViewModel.cs) |
| 95 | [OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/PowerPlayPaneViewModel.cs) |
| 96 | [OneLauncher/ViewModels/Panes/TaskCenterPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/TaskCenterPaneViewModel.cs) |
| 97 | [OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Panes/UserModelLoginPaneViewModel.cs) |
| 98 | [OneLauncher/ViewModels/Windows/GameTaskerViewModel.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/ViewModels/Windows/GameTaskerViewModel.cs) |
| 99 | [OneLauncher/Views/MainWindow.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/MainWindow.axaml.cs) |
| 100 | [OneLauncher/Views/Pages/gamedata.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Pages/gamedata.axaml.cs) |
| 101 | [OneLauncher/Views/Pages/settings.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Pages/settings.axaml.cs) |
| 102 | [OneLauncher/Views/Windows/ExceptionTip.axaml.cs](E:/OneLauncherProject/OneLauncher/OneLauncher/Views/Windows/ExceptionTip.axaml.cs) |

### 8.3 项目约束与保留边界

- 依据 docs/AI_Instruction/前端设计规范.md 的“OneLauncher.Desktop 为项目的前端启动项目，不允许更改”，本计划不修改 Desktop/Program.cs。其 try/catch 内是同步入口记录，没有本次识别的 fire Task 调用；Init.InitTask 的正常初始化任务有 MainWindow 等待。入口旧 crash_log.txt 与最早期崩溃处理仍是之前研究报告中的独立限制。
- 不批量把所有普通 .NET throw 都改成 OlanException。此次覆盖既有 OlanException 消息，以及未等待执行的真实失败边界和必要调用链。
- 注释中的 JavaManager 旧 throw、QuiltNJParser 注释实现、PowerPlayPane 已注释的异步初始化不启用，也不把它们计入活动代码。
- 设计模式不运行真实认证/下载或写生产账户目录；Settings 的 DEBUG RefList 调用仍列入审计。
- 本轮文档不改 AGENTS.md 或项目规范文件；后续落实后另在 docs/AI_Generated 记录异常契约、上下文来源与后台观察方法的用法。

## 9. 拟执行顺序与验证范围

1. 先定义消息/上下文契约，改 OlanException 的构造链及统一错误钩子，明确取消、去重、记录故障隔离。
2. 实现 Core 后台观察器和 UI 适配器，先用账户保存、版本清单刷新、异常展示三条路径确认边界。
3. 补齐底层 HTTP/文件/解析器上下文和必要调用方，再逐项完成第 5 节的 204 处构造与 17 处工厂抛出分支。
4. 依次调整第 6 节的 77 处未等待调用，修复 ForEach(async ...)、联机预初始化以及认证任务提前失败的观察问题。
5. 统一未知异常和 SetFailed 使用的实例，纠正成功 FatalError 和误导性的原因判断，完成上下文/重复记录审查。
6. 实施后再做针对性验证：这是跨项目的异常契约改动，不能仅替换字符串后声称安全。确认 Debug/Release 构建、Windows 条件分支与 Native AOT 兼容；错误日志绑定实现后验证构造记录次数和刷盘。

届时新增有必要的验证代码放在 ConsoleTest/agent_test 下的专用异常检查项目；不为每条文案增加机械快照测试。重点验证真实失败路径：后台异步与同步异常均成为 OlanException；保存完成前不提示成功；JSON/HTTP/文件错误带实际对象；取消不误判；底层异常保留；重复展示不重复记录；异常展示和记录器失效不递归。所有产物位于工作区内 build/<任务名称>-<日期>，实验结束按 build/clean.ps1 清理。实际实施时选定专用测试项目名称并记录；本次不新增测试文件或运行验证。

## 10. 本次交付

只新增本行动文档。所有现有源码保持原样。上文是后续代码改造的具体执行清单，并不是已经实现的错误日志或异常保证。

