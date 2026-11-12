# Microsoft.Extensions.Logging 错误日志系统行动文档

日期：2026-11-12。任务模式：**说明执行**。本次仅分析当前工作区并新增本行动文档，不修改源码、项目依赖或既有文档，不运行应用和测试。下文类型、方法及行为均为拟实施方案。

## 1. 目标与约定

按本次要求接入 `Microsoft.Extensions.Logging`，错误信息实际追加到文件；日志依赖的创建、注册、持有和释放由 `UnitedCore` 管理。

用户所写的 `OlanExcption` 对应源码中的 `OneLauncher.Core.Global.OlanException`，沿用现有正确类型名称，不新增同义异常类。

**唯一业务日志调用点为 OlanException 的统一构造函数。** 该处调用一次 `ILogger<OlanException>.LogError`；文件 Provider 负责该调用的实际文件 I/O。其他位置不调用 `LogError`，不自行拼接并写错误日志。

所有 `OlanException` 实例均记录为 `LogLevel.Error`，包括 `Action` 为 `Warning` 和 `FatalError` 的实例。`OlanExceptionAction` 继续决定 UI 提示与退出方式，不映射为日志 Warning/Critical。这个选择同时覆盖 `ExceptionDiagnostics.Record` 目前以 Warning 包装的真实后台失败。

普通成功、进度、重试、任务生命周期、正常取消和游戏 stdout/stderr 不接入本次日志系统。已经构造的同一个异常再次被抛出、捕获或展示，不新增记录；后续修改其 `Action`、`OriginalException` 或重试回调，也不触发记录。

## 2. 当前代码核实结果

| 文件 | 当前情况 | 实施影响 |
| --- | --- | --- |
| `OneLauncher.Core/Global/OlanException.cs` | 三个构造函数分别赋值，无日志；底层原因存于可变 `OriginalException`，未接入 `InnerException` | 统一构造链，完成字段赋值后只记录一次，显式展开底层异常 |
| `OneLauncher.Core/Global/UnitedCore.cs` | 多数组件手动创建；静态 `Core` 在所有初始化完成后才赋值 | 日志依赖不能只放在最终 Core 实例上，需要 UnitedCore 管理的早期日志状态 |
| `OneLauncher.Core/Global/Init.cs` | 创建目录、读取和保存配置都在 `UnitedCore.Initialize` 之前；这里建立 `ServiceCollection` | 将 UnitedCore 的日志注册提前到首次目录/配置操作之前 |
| `OneLauncher.Core/Global/ExceptionDiagnostics.cs` | 普通后台异常通过 `Record` 构造 OlanException；已是 OlanException 时直接返回 | 现有入口可直接复用，不加第二个记录钩子 |
| `OneLauncher.Core/Global/TaskDiagnostics.cs` | 观察后台任务并调用 `ExceptionDiagnostics.Record`；正常取消不包装 | 保留现有观察行为，不在任务层另行记录 |
| `OneLauncher/Codes/OlanExceptionWorker.cs` | 展示异常；FatalError 使用 `Environment.Exit(1)` | 构造时同步完成文件写入，无需在展示/退出处重复写入 |
| `OneLauncher.Console/Boot.cs` | 末尾捕获普通 Exception 后直接 `Environment.FailFast` | 已有 OlanException 不重复包装；普通异常先构造 OlanException，再 FailFast |
| `OneLauncher.Desktop/Program.cs` | 入口 catch 直接覆盖 `crash_log.txt` 并向控制台输出；Avalonia 使用 `LogToTrace` | 直接写崩溃文件与唯一日志入口冲突，需最小范围统一 |
| `OneLauncher/Views/MainWindow.axaml.cs` | 在 OnClosing 中按顺序释放公共资源列表，未专门管理日志 | 日志需要在其他释放操作完成后再关闭，并考虑关闭被取消的情况 |
| `OneLauncher.Core/OneLauncher.Core.csproj` | 引用 Hosting 10.0.10；Logging 契约来自传递依赖 | 增加直接 Logging 包引用，并保持 10.0.10 版本一致 |

分析基于当前工作区，包括已有的未提交改动；后续实施应在这些内容上局部增补，不能回退或覆盖它们。`docs/AI_Instruction/后端设计规范.md` 当前为空；其余 AI_Instruction 文档已阅读。

已有《日志系统可行性研究》提出的异步队列、多种级别和游戏输出方案不作为本次实施范围。本次按错误构造统一入口缩小实现规模。

## 3. UnitedCore 管理依赖与初始化

拟在 `UnitedCore` 增加以下职责，方法名可在实施时按代码风格确定：

1. `ConfigureLogging(IServiceCollection services, string? logDirectory = null)`：创建并持有唯一日志运行时，将同一个 `ILoggerFactory` 与 `ILogger<OlanException>` 实例注册到传入的服务集合。默认目录由 `Init.BasePath` 派生；验证时可显式传入工作区目录。
2. 内部只读的异常 Logger 访问入口：供 `OlanException` 构造函数获取早期已配置的 Logger，不依赖 `UnitedCore.Core` 已赋值，也不等待整个业务初始化完成。
3. `ShutdownLogging()`：幂等关闭运行时，由 UnitedCore 决定资源所有权，关闭操作本身不产生业务日志。

工厂使用代码配置的 `LoggerFactory.Create` 建立，只绑定自有文件 Provider；随后注册同一工厂和同一类型 Logger 到现有 DI。遵循项目当前手动创建单例再注册的模式，不提前 `BuildServiceProvider`，不另建临时应用容器，不让前端管理 Provider，也不在业务服务中注入新的日志调用点。

不新增泛用的全局 `IServiceProvider` 服务定位器。异常构造函数只能访问 UnitedCore 暴露的限定 Logger；依赖配置与实例生命周期仍归 UnitedCore。现有异常构造签名保持兼容，避免给所有调用方新增 Logger 参数。

`Init.Initialize` 的顺序拟调整为：创建服务集合 → 调用 UnitedCore 日志配置 → 创建 BasePath → 读取/保存配置 → 初始化既有核心组件 → 注册 UnitedCore。日志配置不读取 config.json，不依赖账户、版本索引、网络或游戏安装路径。

日志目录和文件在首条 Error 到来时创建，正常无错误启动不生成空日志。配置方法重复调用时复用同一运行时，防止叠加 Provider；同一进程不支持运行中随意更换日志目录，测试通过独立进程/明确的生命周期隔离来覆盖不同目录。

未配置日志前直接创建 OlanException 的场景使用 `NullLogger<OlanException>`，维持异常构造的可用性，不在异常类型内部擅自建立另一套文件日志依赖。因此正常启动必须保证配置早于会抛 OlanException 的目录和配置操作。运行时加载失败、Init 静态初始化失败等早于该接入点的故障不承诺落盘。

## 4. 文件 Provider 的具体行为

新增标准 `ILoggerProvider` / `ILogger` 实现，不引入第三方文件日志框架。Logger 可以作为 Provider 的私有小类型，避免为简单转发增加多个文件。

- 正式目录：`Path.Combine(Init.BasePath, "logs")`。当前相当于用户主目录下的 `OneLauncher/logs`，与游戏安装目录独立。
- 每个进程会话使用独立文件，例如 `olan-error-20261112T080000000Z-1234-<随机会话标识>.log`。时间、进程号和随机标识防止桌面与命令行同时启动时争用或覆盖文件。
- 使用 UTF-8 文本、追加写入；首条错误前不创建文件。支持文本编辑器读取。
- 工厂过滤与 Provider 的 `IsEnabled` 均仅放行 OlanException 分类的 `LogLevel.Error`；不使用简单的 MinimumLevel=Error 作为唯一筛选，因为那也会放行 Critical。
- 同一 Provider 使用同步锁串行处理整条记录。采用每次打开、追加、刷新并关闭文件的方式，避免缓冲队列及常驻文件句柄；关闭与正在写入的操作使用同一同步策略。
- 记录成功返回前完成托管缓冲刷新和文件关闭。这样 FatalError / FailFast 路径无需新增刷盘日志调用，也不依赖 finally 执行才能保存已构造的错误。
- 不保证断电、原生崩溃、强制杀进程等情况下的物理持久性。同步错误写入也可能短暂阻塞构造线程；本次仅记录低频错误，若未来需要高频日志，应另行设计异步输出及退出契约。
- 初版不自动删除历史日志，不扩大为轮转和容量管理功能。日志会随错误累积占用磁盘，这是本次最小方案的明确限制。

目录不可写、磁盘满、文件占用、格式化失败等可预期输出故障应在 Provider/格式化边界隔离，不能覆盖业务异常、阻止异常构造或递归构造 OlanException。失败时不再写另一个错误文件，不在控制台/Trace 复制业务日志；运行时保留最近写入失败状态和失败计数供诊断，并在后续错误到来时重新尝试。无法写文件时不声称已经持久化。

## 5. 构造函数与记录内容

保留现有三个公开重载，将前两个委托到一个可接受空底层异常和空回调的完整构造函数。完整构造函数先赋值 Title、Message、Action、OriginalException、TryAgainFunction，再调用一次 LogError，避免构造链导致重复记录或记录时字段尚未完整。

使用固定模板及 EventId，只传明确的字符串/枚举字段，不解构账户对象，不序列化任意 object，不引入反射。每条记录包含：

- 带时区的时间、`Error`、分类、固定 EventId、进程/会话标识和本会话内递增序号。
- Title、Action 与完整业务 Message；已有的步骤、文件和请求上下文随 Message 保存。
- OriginalException 的类型、消息和调用栈，以及底层 InnerException；若底层又是 OlanException，还需展开其 OriginalException。异常遍历限制深度并防止循环。
- 构造位置调用栈。OlanException 在尚未 throw 时通常没有自己的 StackTrace；不能只记录 `this.ToString()` 然后宣称已有抛出栈。记录明确标识为构造栈，不伪装成未来的抛出位置。

在构造时立即生成文本快照，后续异常属性更改不回写文件。回调委托不执行也不记录其内容。

新增格式化辅助代码，处理常见凭证字段、Bearer 内容和带秘密参数的 URL；复用 `ExceptionDiagnostics.Url` 的现有 URL 规则。既有 Title/Message/异常链都可能含自由文本，不能仅脱敏一个结构化 URL 字段。保留文件路径和公开请求上下文的定位价值，不记录完整启动参数、认证缓存或账户模型。常见模式脱敏不等于能够识别任意秘密，实施时用虚构凭证覆盖实际支持的形式。

不为接入日志改动 OriginalException 的公开语义，也不顺带重写所有异常抛出位置。去重以实例为单位：已有实例重新抛出不记录；重新构造一个 OlanException 是新记录。同一个底层 Exception 被包装多次不自动合并成一条记录，避免隐藏不同失败现场。

## 6. 入口与生命周期的一致性

### 6.1 既有观察器与 UI

`ExceptionDiagnostics.Record` 已跳过 OlanException，TaskDiagnostics 已复用该入口，保持这种行为；只更新必要的注释，不加入额外文件写入。OlanExceptionWorker 不新增日志调用，继续展示已经记录的异常。

桌面正常关闭时，将 UnitedCore 的日志关闭放在其他资源释放之后；若窗口关闭被取消，不释放日志。必要的普通释放异常先通过既有构造入口归一，日志运行时最后关闭。关闭操作须幂等，且考虑初始化失败时仍有日志运行时但没有完整 Core 实例。

CLI catch 内先判断是否已是 OlanException：已有实例直接沿用；普通 Exception 仅构造一次带“命令行执行”及脱敏命令类型上下文的 OlanException，不打印完整 args。记录完成后保留现有 FailFast 策略。正常命令返回时由 finally 调用 UnitedCore 关闭日志；不依赖 FailFast 会执行 finally。

### 6.2 Desktop 的 crash_log.txt 冲突

`docs/AI_Instruction/前端设计规范.md` 写明“`OneLauncher.Desktop` 为项目的前端启动项目，不允许更改”。但本次用户明确要求“写日志的唯一发生点是 OlanExcption 构造函数”，而 Desktop/Program.cs 当前直接写错误文件，二者存在具体冲突。

本计划按用户本次要求优先，**将 Program.cs 的入口 catch 列为必要的最小修改**：移除直接写 crash_log.txt 和输出错误文本的逻辑；已是 OlanException 时沿用，普通异常构造一次 OlanException 后保留原来的重新抛出行为。不调整启动配置、Avalonia 平台设置或入口结构。该必要例外不扩大为修改 Desktop 的其他功能。

Avalonia 的 `LogToTrace()` 属于既有框架诊断，不绑定新文件 Provider；本次不新增 Trace 文件 Listener，也不批量改写既有 Debug/Trace 调用。Minecraft 自身日志同样不属于本次启动器错误文件系统。

该方案消除项目入口自行写错误文件的行为，但不承诺所有普通异常都会进入日志：只有现有归一边界或上述两个入口把它构造成 OlanException 时才记录。未经过构造入口的原始异常不会自动写文件。

## 7. 拟新增、修改的文件

路径相对于 `E:/OneLauncherProject/OneLauncher`。本次实际上只新增当前行动文档，以下源码清单待后续实施。

| 操作 | 文件 | 拟执行内容 |
| --- | --- | --- |
| 修改 | `OneLauncher.Core/OneLauncher.Core.csproj` | 显式引用 Microsoft.Extensions.Logging 10.0.10，与 Hosting 统一版本 |
| 新增 | `OneLauncher.Core/Global/Logging/ExceptionFileLoggerProvider.cs` | Error/分类过滤、串行文件追加、每次刷新关闭、输出故障隔离、幂等释放 |
| 新增 | `OneLauncher.Core/Global/Logging/ExceptionLogFormatter.cs` | 固定文本字段、异常链展开、构造栈、脱敏与循环/深度防护 |
| 修改 | `OneLauncher.Core/Global/UnitedCore.cs` | 管理日志工厂、类型 Logger、DI 注册、早期访问和释放；支持显式验证目录 |
| 修改 | `OneLauncher.Core/Global/Init.cs` | 提前建立服务集合并委托 UnitedCore 配置日志，早于目录和配置操作 |
| 修改 | `OneLauncher.Core/Global/OlanException.cs` | 统一三个构造重载，唯一一次 LogError 调用 |
| 修改 | `OneLauncher/Views/MainWindow.axaml.cs` | 正常关闭时最后释放日志，关闭取消时保留日志，处理早期初始化失败情况 |
| 修改 | `OneLauncher.Console/Boot.cs` | FailFast 前归一普通异常；正常结束释放日志，不重复包装 OlanException |
| 修改 | `OneLauncher.Desktop/Program.cs` | 仅替换入口直接 crash_log.txt/控制台错误输出，必要时构造 OlanException，保留重新抛出 |
| 修改文档 | 当前行动文档 | 实施后补充实际改动、验证结果与限制，区分计划与已实现行为 |

`ExceptionDiagnostics.cs`、`TaskDiagnostics.cs`、`OlanExceptionWorker.cs` 为检查复用点，当前无需新增日志行为；若需更新注释，实施报告单独记录。其余下载、认证、启动及 ViewModel 代码不因接入日志而批量注入 ILogger。

## 8. 后续执行顺序与验证

1. 实现 Provider 与格式化器，先明确只接收指定分类 Error 和输出失败隔离。
2. 在 UnitedCore 建立单一工厂及 Logger，调整 Init 的早期配置顺序。
3. 统一 OlanException 构造链，检查所有重载仅记录一次且字段完整。
4. 调整两个进程入口的原始异常归一及 Desktop 旧文件写入，完善桌面/CLI 的资源收尾。
5. 静态检查业务 LogError 调用仅存在于 OlanException 完整构造函数，其他文件错误输出不绕过它。
6. 更新实施说明。是否做实际验证按项目“非特别说明或大型功能变动无需测试”的要求判断；本次不执行测试。

该接入涉及文件副作用、并发和强制退出，实施时建议做小范围有意义的验证；无需真实登录、下载游戏或为每个包装方法新增测试。若采用验证项目，拟放在 `ConsoleTest/agent_test/OneLauncher.LoggingTests`，产物放在 `build/logging-validation-20261112`，日志目录显式注入到该任务目录。不得为了验证写入实际账户/游戏目录。

重点验证：三个构造重载及所有 Action 各产生一条 Error；重复捕获/展示不新增记录；底层异常链与构造栈可读；并发构造不交错；无错误启动不生成空文件；配置损坏时有记录；目录不可写时原异常仍可构造；普通 CLI/Desktop 入口错误只归一一次；独立子进程构造后执行 Exit/FailFast，父进程能够读取已关闭的错误文件；虚构凭证脱敏；正常关闭与关闭取消不造成重复释放或提前禁用日志。

兼容 .NET 10 与项目 Native AOT 要求，采用直接代码配置和明确文本格式化，不使用动态程序集、反射配置或任意对象 JSON 序列化。若进行 AOT 发布验证，只将实际发布/运行结果写入实施报告，不能凭设计宣称已经通过。验证完成后使用 `build/clean.ps1 -Task logging-validation-20261112` 定向清理，避免清理其他工作区任务。

本次交付只有这份行动文档；没有实际接入 Microsoft.Extensions.Logging，也没有更改当前工作区的业务源码和已有未提交改动。
