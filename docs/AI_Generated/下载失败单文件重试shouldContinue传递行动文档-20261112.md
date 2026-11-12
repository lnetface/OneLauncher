# 下载失败单文件重试：shouldContinue 逐层传递到 UI 行动文档

日期：2026-11-12。任务模式：**说明执行，仅分析并列出拟执行操作，不修改实际代码。** 本次只新增本行动文档。

## 1. 需求

人类已在 `RaceDownloader.RunRaceManyFilesAsync` 中加入 `Func<string, bool> shouldContinue` 参数和单文件重试循环（最多 5 次）。本次要做三件事：

1. 把 `shouldContinue` 从 `RaceDownloader` 一层层传到 UI（`DownloadPaneViewModel`）。
2. UI 实现这个回调：失败时弹出 `ExceptionTip`，**阻塞调用线程直到用户点击“重试”或“忽略”**。返回 true 表示只重试这个文件，返回 false 表示放弃。
3. 其余下载线程照常工作，下载管线（资源 → 日志配置 → 校验 → 登记）不因单个文件失败而回退。

## 2. 现状分析（基于当前工作区）

### 2.1 调用链

```
DownloadPaneViewModel.ToDownload            (UI，外层 Task.Run 包裹)
 └ VersionInstallerService.InstallNewVersion(info, progress, cancelToken)
    └ new DownloadMinecraft(info, progress, token).MinecraftBasic()
       ├ DownloadClientTasker      → RaceDownloader.RaceSingleFileAsync   （游戏主文件，单文件）
       ├ DownloadLibrariesSupportTasker → RaceDownloader.RaceManyFilesAsync
       └ DownloadAssetsSupportTasker    → RaceDownloader.RaceManyFilesAsync
```

`InstallNewVersion` 还有另外两个调用方：`ImportMain`（PCL2 导入）和 `ModpackImporter`（整合包安装），回归测试 `ConsoleTest/agent_test/OneLauncher.RegressionTests/Program.cs` 也会调用它。`ImportMain` 另外直接 `new DownloadMinecraft(...)` 来调用 Planner。

### 2.2 当前改动中需要一并修正的问题

| # | 位置 | 问题 | 处理 |
|---|------|------|------|
| A | `RaceManyFilesAsync`（公开包装方法） | 没有传入 `shouldContinue`，调用 `RunRaceManyFilesAsync` 时缺少参数，**当前代码无法编译**。 | 给包装方法加参数并透传。 |
| B | 重试循环 `break` 分支 | 用户选择忽略，或 5 次全部失败后直接 `break`，异常被吞掉，接着还会 `progress.Report` 当作完成。结果是**文件缺失但安装显示成功**。 | 放弃或次数用尽时重新抛出最后一次异常（`throw;` 或保存后 `ExceptionDispatchInfo.Throw`）。 |
| C | `catch (Exception ex)` | 用户取消下载时 `OperationCanceledException` 也会进入这里，导致**取消时弹出失败对话框**。 | 改为 `catch (Exception ex) when (!ct.IsCancellationRequested)`。 |
| D | 第 5 次失败 | 第 5 次失败后仍然询问用户，但无论选什么都会退出循环，按钮失去意义。 | 最后一次失败不再询问，直接抛出。 |
| E | 传给 UI 的字符串 | `ex.ToString()` 带完整堆栈，`OlanException.ToString()` 可读性差。 | 传 `ex is OlanException oe ? oe.Message : ex.ToString()`，签名仍为 `Func<string,bool>`。 |
| F | 游戏主文件 | `DownloadClientTasker` 直接调用 `RaceSingleFileAsync`，不经过重试循环，主文件失败时仍然整体失败。 | 把重试循环提取为私有方法 `RaceSingleFileWithRetryAsync(item, ct, shouldContinue)`，`RaceManyFilesAsync` 与 `DownloadClientTasker` 共用（建议做；如不需要覆盖主文件可去掉这一项）。 |

拟定的重试循环（示意）：

```csharp
internal async Task RaceSingleFileWithRetryAsync(
    IEnumerable<NdDowItem> item, CancellationToken ct, Func<string, bool>? shouldContinue)
{
    const int MaxAttempts = 5;
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            await RaceSingleFileAsync(item, ct);
            return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            string detail = ex is OlanException oe ? oe.Message : ex.ToString();
            if (attempt >= MaxAttempts || shouldContinue == null || !shouldContinue(detail))
                throw;
        }
    }
}
```

`shouldContinue == null` 时行为与改动前一致（首次失败即抛出），所以不传回调的调用方不受影响。

## 3. 逐层传递方案

参数统一用可选参数 `Func<string, bool>? shouldContinue = null`，放在参数列表末尾。这样 `ImportMain`、`ModpackImporter` 和回归测试都不用改，保持最小改动。

| 层 | 文件 | 改动 |
|----|------|------|
| 1 | `OneLauncher.Core/DefaultComponent/Downloader/RaceDownloader.cs` | `RaceManyFilesAsync` 增加 `shouldContinue` 参数并透传；`RunRaceManyFilesAsync` 的循环体改为调用 `RaceSingleFileWithRetryAsync`；修正 2.2 中的 A–F。 |
| 2 | `OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraft.cs` | 构造函数增加 `Func<string,bool>? shouldContinue = null`，保存到 `private readonly Func<string,bool>? _shouldContinue`。 |
| 3 | `OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraftProvider.cs` | `DownloadLibrariesSupportTasker` 与 `DownloadAssetsSupportTasker` 调用 `RaceManyFilesAsync` 时传 `_shouldContinue`；`DownloadClientTasker` 改为调用 `RaceSingleFileWithRetryAsync(..., _shouldContinue)`（对应 F）。 |
| 4 | `OneLauncher.Core/Services/VersionInstallerService.cs` | `InstallNewVersion(info, progress, cancelToken = null, shouldContinue = null)`，构造 `DownloadMinecraft` 时传入。 |
| 5 | `OneLauncher/Codes/OlanExceptionWorker.cs` | 新增同步阻塞方法 `AskRetryBlocking`（见第 4 节）。 |
| 6 | `OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs` | `ToDownload` 中构造回调并传给 `InstallNewVersion`。 |

不改动：`OlanException`、`ExceptionTip`（.axaml / .axaml.cs）、`ImportMain`、`ModpackImporter`、回归测试。

## 4. UI 层阻塞等待的实现

### 4.1 为什么可以安全阻塞

`InstallNewVersion` 在 `DownloadPaneViewModel` 中由 `Task.Run` 包裹，回调运行在 `Parallel.ForEachAsync` 的线程池 worker 上，不在 UI 线程。worker 同步等待期间，UI 线程照常显示对话框、响应点击，不会死锁。

为了防止以后有人在 UI 线程上调用，方法开头加保护：`Dispatcher.UIThread.CheckAccess()` 为 true 时不阻塞，直接返回 false（不重试，按失败处理），避免卡死界面。

### 4.2 `OlanExceptionWorker.AskRetryBlocking`（新增）

```csharp
private static readonly SemaphoreSlim RetryDialogGate = new(1, 1);

/// 在后台线程同步阻塞，直到用户在 ExceptionTip 中选择“重试”(true) 或关闭/忽略(false)。
public static bool AskRetryBlocking(OlanException exception, CancellationToken token)
{
    if (Dispatcher.UIThread.CheckAccess())
        return false;                    // 禁止在 UI 线程阻塞
    try
    {
        RetryDialogGate.Wait(token);     // 多个文件同时失败时排队，一次只弹一个窗口
    }
    catch (OperationCanceledException) { return false; }
    try
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            var tip = new ExceptionTip(exception);
            exception.TryAgainFunction = () => { decision.TrySetResult(true); tip.Close(); };
            tip.Closed += (_, _) => decision.TrySetResult(false);           // 忽略按钮 / 右上角关闭
            using var reg = token.Register(() => Dispatcher.UIThread.Post(tip.Close)); // 任务被取消时自动关窗
            await tip.ShowDialog(MainWindow.mainwindow);
        });
        decision.Task.Wait(token);
        return decision.Task.Result;
    }
    catch (OperationCanceledException) { return false; }
    finally { RetryDialogGate.Release(); }
}
```

要点：

- 继续使用现有的 `ExceptionTip`，通过 `TryAgainFunction` 写入结果并关窗。现在点“重试”按钮不会关窗，这里由回调负责关闭，所以 `ExceptionTip` 不用改。
- “忽略”按钮和标题栏关闭都会触发 `Closed`，结果为 false。`TrySetResult` 保证只采用第一次结果。
- 下载任务被取消时，关闭对话框并返回 false。worker 随后在 `when (!ct.IsCancellationRequested)` 过滤下按取消流程结束。

### 4.3 `DownloadPaneViewModel.ToDownload` 中的回调

```csharp
bool userGaveUp = false;   // 本次下载内共享
Func<string, bool> shouldContinue = detail =>
{
    if (Volatile.Read(ref userGaveUp) || cancellation.IsCancellationRequested)
        return false;
    var ex = new OlanException("下载失败",
        $"下载 MC {VersionName} 时单个文件所有下载源均失败。点击“重试”只重新下载该文件，其他文件继续下载；点击“忽略”结束本次安装。{Environment.NewLine}{detail}",
        OlanExceptionAction.Error);
    bool retry = OlanExceptionWorker.AskRetryBlocking(ex, cancellation);
    if (!retry) Volatile.Write(ref userGaveUp, true);
    return retry;
};
...
await _unitedCore.VersionInstallerService.InstallNewVersion(info, progressReporter, cancellation, shouldContinue);
```

`userGaveUp` 的作用：用户点了一次“忽略”后，该文件的异常向上抛出，`Parallel.ForEachAsync` 开始取消。这时可能还有其他 worker 正在信号量上排队，它们只能看到任务的 `cancellation`，看不到 Parallel 内部的令牌。如果没有这个标志，用户会在安装已经失败后连续收到几个对话框。设置后，排队中的请求直接返回 false。

阻塞期间进度显示为最后一次报告的阶段，不额外修改任务中心状态（保持最小改动）。

## 5. 风险与说明

- **线程池占用：** 最多 `MaxDownloadThreads` 个 worker 可能同时阻塞（其中一个在等对话框，其余在信号量上排队）。这些 worker 本来就在等结果，线程池会按需补充线程，对 UI 没有影响。对话框打开时，那些成功的下载仍会继续完成。
- **日志重复：** `OlanException` 构造时会自动记录日志。`RaceSingleFileAsync` 中的原始异常和 UI 中包装的询问异常会各记一次，每次重试失败也会记录。这符合“每次失败都留痕”，所以不做去重。
- **AOT：** 只用到委托、`TaskCompletionSource`、`SemaphoreSlim`，没有反射或动态代码，不影响 AOT。
- **未覆盖范围：** 整合包安装和 PCL2 导入默认传 null，行为不变（首次失败即抛出）。如需同样的交互，以后在 `ModpackImporter` 和 `ImportMain` 增加同名可选参数，再从对应 ViewModel 传入即可，方式与本文第 3、4 节一致。`UnityModsInstallTasker` 使用 `Download.DownloadListAsync`，本身已有 3 次自动重试，不在本次范围内。

## 6. 文件清单

修改（6 个）：

1. `OneLauncher.Core/DefaultComponent/Downloader/RaceDownloader.cs`
2. `OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraft.cs`
3. `OneLauncher.Core/DefaultComponent/Downloader/DownloadMinecraftProviders/DownloadMinecraftProvider.cs`
4. `OneLauncher.Core/Services/VersionInstallerService.cs`
5. `OneLauncher/Codes/OlanExceptionWorker.cs`
6. `OneLauncher/ViewModels/Panes/DownloadPaneViewModel.cs`

新增：实施完成后在 `docs/AI_Generated/` 写一份功能说明文档（代码用意、阻塞模型、如何接入其他安装入口）。

## 7. 验证

按 AGENTS.md，本次不算大型功能变动，默认只做编译检查（`dotnet build`）。如需手动验证，可以临时把某个下载源 URL 改为不可达地址，确认三点：只弹一个对话框；点“重试”后只重下该文件，安装继续；点“忽略”后安装失败且不再弹窗。临时改动和生成的文件放在工作区内，结束后执行 `build/clean.ps1`。
