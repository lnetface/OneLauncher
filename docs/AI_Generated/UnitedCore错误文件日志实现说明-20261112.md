# UnitedCore 错误文件日志实现说明

日期：2026-11-12。

## 实际改动范围

按本轮限定，只修改 `OneLauncher.Core/Global/UnitedCore.cs`，新增 `OneLauncher.Core/Global/Logging/ExceptionFileLogManager.cs`。本说明为项目规定的实现文档。

没有修改 OlanException、Init、桌面/命令行入口或项目依赖。当前 Hosting 10.0.10 的已还原传递依赖包含 Microsoft.Extensions.Logging 10.0.10，可直接使用现有契约。

此前行动文档中的构造链调整、入口归一、删除 crash_log.txt、前端关闭流程调整等不在本轮授权范围，因此没有实施。

## 依赖与调用入口

UnitedCore 内部用一个 `Lazy<LoggingDependencies>` 管理日志专用 DI 容器，注册 Microsoft.Extensions.Logging、自有文件 Provider 和 ILogger<OlanException>。这是仅包含日志依赖的容器，不提前构建 Init 返回的应用服务集合；应用组件仍通过 UnitedCore 获取管理器。

`UnitedCore.Initialize()` 在既有组件初始化前创建日志依赖；第一次直接调用 `Log` 也可提前创建它们，不要求 `UnitedCore.Core` 已经赋值。Lazy 确保并发首次访问只建立一组依赖。目录/文件不在依赖创建阶段生成。

唯一新增记录入口为：

```csharp
internal static void Log(OlanException exception)
```

该方法通过 ILogger<OlanException>.LogError 写入 Title、Action、Message 和异常对象。所有 Action 都记录为 Error。方法捕获日志依赖或记录本身的异常，避免影响调用方的原错误。

**当前 OlanException 构造函数没有调用这个入口，因此创建异常不会自动生成日志。** 后续在完整字段赋值后的构造入口调用 `UnitedCore.Log(this)` 即可接入；应由调用方保证每个实例只调用一次，管理器不自动按实例去重。本轮没有添加任何构造函数调用。

UnitedCore 实例的 `LogManager` 属性暴露同一个 ExceptionFileLogManager，可查询 LogDirectory、LogFilePath、FailedWriteCount 和 LastWriteException。日志容器加入现有 `Init.OnApplicationClosingReleaseSourcesList`，复用已有的释放流程；未新增或改变前端生命周期逻辑。

## 文件保存与故障处理

- 默认位置为 `Path.Combine(Init.BasePath, "logs")`。
- 文件名包含 UTC 会话起始时间、PID 和随机会话标识，独立进程不会覆盖或共享同一个日志文件。
- 首条错误才创建目录和文件。UTF-8 无 BOM，追加写入，同步锁保证同一管理器的完整记录不会交错。
- 每条记录打开文件、写入、刷新并关闭，没有后台队列或待退出刷新的缓冲，读者可以通过 FileShare.Read 读取文件。
- 工厂过滤及 Provider 同时限制为 OlanException 分类的 Error；Provider 进一步检查异常对象确实为 OlanException。
- 记录时间、级别、分类、EventId、PID、递增序号、业务字段和异常链。显式展开 OlanException.OriginalException、InnerException 及 AggregateException 的全部子异常，避免默认 ToString 漏掉 OriginalException。
- 异常链按引用去重、深度限制为 32。尚无抛出栈时记录当前调用栈，并明确标注其性质。
- 格式化/文件写入失败保存失败次数与最近异常；下一条记录仍会重试，成功后清除 LastWriteException。失败不另建 OlanException，不打印另一份错误，不递归写日志。
- 同一线程在格式化过程中再次进入管理器时直接返回，防止递归记录。Dispose 使用同一同步锁且幂等；关闭后丢弃后续调用。

## 范围与拓展

当前未添加自动轮转、历史删除、敏感文本脱敏或其他日志源。日志保存传入的诊断文本，调用方应避免传入凭证；日志累积占用磁盘。现有 Desktop 的 crash_log.txt、Debug/Trace 和游戏自身日志没有变动。

仅增加普通文本格式化和直接代码配置，没有动态加载、反射对象解构或任意对象 JSON 序列化。未来可在文件管理器内增加轮转、保留策略或文本处理，保持 UnitedCore.Log 的调用契约不变。

实际文件写入失败不能保证保存记录；强制杀进程、断电等也不属于物理持久性保证。日志依赖创建失败由 Lazy 缓存，Log 会隔离该失败，但不自动重建整个 DI 容器。现有释放列表的顺序和窗口关闭行为沿用项目实现；CLI 未复用该列表时，文件也已在每次写入中关闭，不存在待刷盘队列。

## 验证

使用 `dotnet build OneLauncher.Core/OneLauncher.Core.csproj --no-restore`，输出与中间编译文件定向到工作区内 `build/logging-implementation-20261112`。编译成功，0 个错误，161 个项目警告；没有运行启动器、真实认证或游戏下载，没有进行功能测试或 Native AOT 发布验证。

编译产物按项目要求通过 `build/clean.ps1 -Task logging-implementation-20261112` 定向清理。
