using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Core.Global;
using OneLauncher.Views;
using OneLauncher.Views.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Codes;

internal class OlanExceptionWorker
{
    public static Task ForOlanException(OlanException exception, Action? TryAgainFunction = null)
    {
        if(TryAgainFunction != null)
            exception.TryAgainFunction = TryAgainFunction;
        return Dispatcher.UIThread.InvokeAsync(async () => 
        {
            if (exception.Action == OlanExceptionAction.Warning)
                WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(exception.Message,NotificationType.Warning,title:exception.Title));
            if (exception.Action == OlanExceptionAction.Error)
                await new ExceptionTip(exception).ShowDialog(MainWindow.mainwindow);
            if (exception.Action == OlanExceptionAction.FatalError)
            {
                await new ExceptionTip(exception).ShowDialog(MainWindow.mainwindow);
                Environment.Exit(1);
            }
        });
    }
    public static Task ForUnknowException(Exception exception, Action TryAgainFunction = null,
        [System.Runtime.CompilerServices.CallerMemberName] string operation = "",
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await new ExceptionTip(new OlanException("出现未知错误",$"执行 {operation} 时发生未知错误；调用源文件={sourceFile}。{Environment.NewLine}{exception}",OlanExceptionAction.Error,exception,TryAgainFunction)).ShowDialog(MainWindow.mainwindow);
        });
    }

    // 多个后台线程同时请求时排队，保证一次只弹出一个询问窗口
    private static readonly SemaphoreSlim RetryDialogGate = new(1, 1);

    /// <summary>
    /// 在后台线程同步阻塞，显示 ExceptionTip 并等待用户选择。
    /// 点击“重试”返回 true；点击“忽略”、关闭窗口或 token 被取消返回 false。
    /// 不允许在 UI 线程调用（会直接返回 false，避免界面死锁）。
    /// </summary>
    public static bool AskRetryBlocking(OlanException exception, CancellationToken token)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return false;
        try
        {
            RetryDialogGate.Wait(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        try
        {
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task dialogTask = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested)
                {
                    decision.TrySetResult(false);
                    return;
                }
                var tip = new ExceptionTip(exception);
                exception.TryAgainFunction = () =>
                {
                    decision.TrySetResult(true);
                    tip.Close();
                };
                // “忽略”按钮与标题栏关闭都会触发 Closed；TrySetResult 只采用第一次结果
                tip.Closed += (_, _) => decision.TrySetResult(false);
                // 下载任务被取消时自动关闭询问窗口
                using var registration = token.Register(() => Dispatcher.UIThread.Post(tip.Close));
                await tip.ShowDialog(MainWindow.mainwindow);
            });
            // 窗口显示失败时不能让下载线程永久阻塞
            dialogTask.ContinueWith(t =>
            {
                _ = new OlanException("无法显示重试窗口",
                    $"在 UI 线程显示下载失败重试询问窗口；原询问标题={exception.Title}。原因：{t.Exception?.GetBaseException().Message}",
                    OlanExceptionAction.Warning, t.Exception!);
                decision.TrySetResult(false);
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            decision.Task.Wait(token);
            return decision.Task.Result;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            RetryDialogGate.Release();
        }
    }
}
