# 下载失败单文件重试（shouldContinue）功能说明

日期：2026-11-12。对应行动文档：`下载失败单文件重试shouldContinue传递行动文档-20261112.md`。

## 1. 用意

竞速下载中，某个文件的所有下载源都拿不到响应头时，过去会直接抛出 `OlanException`。异常一路回退到 `DownloadPaneViewModel`，整个安装失败，异常窗口的“重试”按钮也无法回到原来的位置。

现在的做法是**把“是否重试”的决定放到下载线程内部**。失败时同步回调 `shouldContinue`，由 UI 阻塞询问用户：

- **重试**：只重新竞速下载这一个文件，其他并行文件和后续安装阶段都不受影响。
- **忽略**：抛出该文件最后一次的异常，按原有流程让安装失败。

## 2. 调用链

```
DownloadPaneViewModel.ToDownload           构造 shouldContinue（阻塞询问）
 └ VersionInstallerService.InstallNewVersion(info, progress, cancelToken, shouldContinue = null)
    └ new DownloadMinecraft(info, progress, token, shouldContinue = null)   保存为 _shouldContinue
       ├ DownloadClientTasker           → RaceDownloader.RaceSingleFileWithRetryAsync
       ├ DownloadLibrariesSupportTasker → RaceDownloader.RaceManyFilesAsync(..., _shouldContinue)
       └ DownloadAssetsSupportTasker    → RaceDownloader.RaceManyFilesAsync(..., _shouldContinue)
                                             └ 每个文件 → RaceSingleFileWithRetryAsync
```

各层参数都是可选的，默认为 null。`ImportMain`（PCL2 导入）、`ModpackImporter`（整合包）和回归测试都不传，行为与改动前一致：首次失败即抛出。

## 3. 关键逻辑

### 3.1 `RaceDownloader.RaceSingleFileWithRetryAsync`

- 最多尝试 5 次（`MaxAttempts`）。
- 失败时，下面三种情况直接 `throw;`，异常不会被吞掉，也不会被误报为完成：
  - `shouldContinue` 为 null；
  - 已是最后一次尝试；
  - 回调返回 false。
- 只捕获 `!token.IsCancellationRequested` 的异常，用户取消不会弹出失败询问。
- 传给回调的字符串格式为“第 N/5 次尝试失败”加上原因。`OlanException` 取 `Message`，其他异常取 `ToString()`。

### 3.2 `OlanExceptionWorker.AskRetryBlocking(OlanException, CancellationToken)`

- 只能在后台线程调用。在 UI 线程调用时直接返回 false，避免死锁。
- 用静态 `SemaphoreSlim` 排队，同一时刻只有一个询问窗口。
- 复用 `ExceptionTip`，结果通过 `TaskCompletionSource<bool>` 传回下载线程：
  - `TryAgainFunction` 写入 true 并关闭窗口；
  - 点“忽略”或标题栏关闭都会触发 `Closed`，写入 false；
  - token 被取消时自动关闭窗口，返回 false；
  - 窗口显示失败时记录 Warning 级 `OlanException` 并返回 false，下载线程不会永久阻塞。

### 3.3 `DownloadPaneViewModel` 中的回调

- 用 `userGaveUp` 标志实现“一次忽略即全部放弃”。用户点“忽略”后，排队中的其他询问直接返回 false，安装失败后不会再连续弹窗。
- 任务被取消时直接返回 false。

## 4. 扩展方式

其他安装入口（整合包、PCL2 导入）如需同样的交互，按下面三步接入：

1. 给 `ModpackImporter` 和 `ImportMain` 加一个可选参数 `Func<string,bool>? shouldContinue`，透传给 `InstallNewVersion`。
2. 在对应 ViewModel 中仿照 `DownloadPaneViewModel` 构造回调，调用 `OlanExceptionWorker.AskRetryBlocking`。
3. 确保安装流程运行在 `Task.Run` 等后台线程上。

其他下载器如需单文件重试，也可以直接使用 `RaceSingleFileWithRetryAsync`。

## 5. 注意事项

- 回调会阻塞线程池 worker。最多 `MaxDownloadThreads` 个 worker 可能同时等待：一个在等窗口，其余在信号量上排队。
- `OlanException` 构造时会自动写日志，所以每次失败与每次询问都会留下记录。
- 没有使用反射或动态代码，兼容 AOT。
- 本次没有编译或运行验证（执行环境不可用），请在本地执行 `dotnet build` 确认。
