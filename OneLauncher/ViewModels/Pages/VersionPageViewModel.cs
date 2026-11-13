using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.Compatible.ImportPCL2Version;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.ImportPCL2Version;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.DefaultComponent.Server;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OneLauncher.ViewModel.Page;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Interfaces;

namespace OneLauncher.Views.ViewModels;
internal partial class VersionItem : BaseViewModel
{
    private readonly UnitedCore _unitedCore;
    /// <param Name="a">UserVersion实例</param>
    public VersionItem(UserVersion a, UnitedCore unitedCore)
    {
        _unitedCore = unitedCore;
        versionExp = a;
    }
    public UserVersion versionExp { get; set; }
    [RelayCommand]
    public async Task LaunchGame()
    {
        using var launchService = _unitedCore.GameLaunchService.CreateLaunchService($"启动 MC {versionExp.VersionID}");
        void OnStarted() => WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
            $"MC {versionExp.VersionID} 已启动！", NotificationType.Success, openTaskCenterAfterClick: true));
        void OnClosed(int code) => WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
            code == 0 ? $"MC {versionExp.VersionID} 已关闭。" : $"MC {versionExp.VersionID} 异常退出，代码：{code}。",
            code == 0 ? NotificationType.Information : NotificationType.Error, openTaskCenterAfterClick: true));
        launchService.GameStartedEvent += OnStarted;
        launchService.GameClosedEvent += OnClosed;
        try
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                $"正在启动 MC {versionExp.VersionID}……", openTaskCenterAfterClick: true));
            await launchService.Play(versionExp, useRootMode: true);
        }
        catch (OperationCanceledException)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                $"MC {versionExp.VersionID} 启动已取消。", NotificationType.Warning, openTaskCenterAfterClick: true));
            throw;
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(
                $"MC {versionExp.VersionID} 启动失败：{ex.Message}", NotificationType.Error, openTaskCenterAfterClick: true));
            throw;
        }
        finally
        {
            launchService.GameStartedEvent -= OnStarted;
            launchService.GameClosedEvent -= OnClosed;
        }
    }
    [RelayCommand]
    public void ReadMoreInformations()
    {
        Tools.OpenWebsite($"https://zh.minecraft.wiki/w/Java版{versionExp.VersionID}");
    }
    [RelayCommand]
    public void OpenServerFolder()
    {
        string path = Path.Combine(Init.GameRootPath,"versions",versionExp.VersionID,"servers");
        if (!Directory.Exists(path))
            OlanExceptionWorker.ForOlanException(
                new OlanException("无法打开服务端文件夹",$"检查服务端初始化结果并打开目录；文件/目录 path={path}。服务端尚未初始化",OlanExceptionAction.Error));
        else
            Tools.OpenFolder(path);
    }
}
internal partial class VersionPageViewModel : BaseViewModel, IPageHost, IListTemplate<VersionItem>
{
    private readonly VersionListManager _versionListManager;
    private readonly UnitedCore _unitedCore;
    private readonly NewGameDataPaneViewModelFactory _newGameDataPaneViewModelFactory;
    public Task RefList()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildList();
            return Task.CompletedTask;
        }
        return Dispatcher.UIThread.InvokeAsync(RebuildList).GetTask();
    }

    private void RebuildList()
    {
        Items = new ObservableCollection<VersionItem>(_versionListManager.GetVersions().Select(x => new VersionItem(x, _unitedCore)));
    }
    public VersionPageViewModel(UnitedCore unitedCore, NewGameDataPaneViewModelFactory newGameDataPaneViewModelFactory)
    {
        _versionListManager = unitedCore.VersionListManager;
        _unitedCore = unitedCore;
        _newGameDataPaneViewModelFactory = newGameDataPaneViewModelFactory;
#if DEBUG
        // 设计时数据
        if (Design.IsDesignMode)
        {
            Items = new ObservableCollection<VersionItem>()
            {
                new VersionItem(new UserVersion() 
                {
                    VersionID="1.21.5",
                    AddTime=DateTime.Now
                }, _unitedCore)
            };
        }
        else
#endif
        {
            _ = RefList();
            _versionListManager.OnDataChanged += OnVersionDataChanged;
        } 
    }
    [ObservableProperty]
    private ObservableCollection<VersionItem> items = new();
    [ObservableProperty]
    private UserControl? _paneContent;
    [ObservableProperty]
    public bool _isPaneShow;
    [RelayCommand]
    public async Task Sorting(SortingType type)
    {
        List<VersionItem> orderedList = type switch
        {
            SortingType.AnTime_OldFront => Items.OrderBy(x => x.versionExp.AddTime).ToList(),
            SortingType.AnTime_NewFront => Items.OrderByDescending(x => x.versionExp.AddTime).ToList(),
            SortingType.AnVersion_OldFront => Items.OrderBy(x => new Version(x.versionExp.VersionID)).ToList(),
            SortingType.AnVersion_NewFront => Items.OrderByDescending(x => new Version(x.versionExp.VersionID)).ToList(),
            _ => Items.ToList() // 默认不排序
        };

        try
        {
            await _versionListManager.ReplaceOrderAsync(orderedList.Select(x => x.versionExp).ToList());
            await RefList();
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
        }
    }

    private void OnVersionDataChanged() => _ = RefList();
    [RelayCommand]
    public async Task OpenServer(UserVersion versionExp)
    {
        try
        {
            // 去尝试读取，判断这个服务端版本是否启用了版本隔离
            bool IsVI = true;
            if (Directory.Exists(Path.Combine(Init.GameRootPath, "versions", versionExp.VersionID, "servers")))
                IsVI = true;
            else if (Directory.Exists(Path.Combine(Init.GameRootPath, "servers")))
                IsVI = false;
            string versionPath = Path.Combine(Init.GameRootPath, "versions", versionExp.VersionID);
            // 判断服务端是否已经完成初始化
            if (!File.Exists(Path.Combine(versionPath, "server.jar")))
            {
                IsPaneShow = true;
                PaneContent = new InitServerPane()
                { DataContext = new InitServerPaneViewModel(versionExp.VersionID,() => IsPaneShow = false)};
            }
            else
                using (var fs = File.OpenRead(
                                Path.Combine(versionPath, $"version.json")))
                    await MinecraftServerManger.RunAsync(versionExp.VersionID,
                        // 读取源文件获取Java版本
                        (await JsonNode.ParseAsync(
                                    fs))
                                    ?["javaVersion"]
                                    ?["majorVersion"]
                                    ?.GetValue<int>()
                                    ?? Tools.ForNullJavaVersion(versionExp.VersionID)
                                    , IsVI);
            
            
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
        }
    }
    [RelayCommand]
    public void CreateGameData(UserVersion versionExp)
    {
        IsPaneShow = true;
        PaneContent = new NewGameDataPane()
        { DataContext = _newGameDataPaneViewModelFactory.Create(versionExp, () => IsPaneShow = false) };
    }
}

