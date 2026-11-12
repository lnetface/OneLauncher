using OneLauncher.Interfaces;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Services;
using OneLauncher.ViewModel.Page;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;
internal partial class NewGameDataPaneViewModel : BaseViewModel, IPaneHost
{
    private readonly VersionListManager _versionListManager;
    private readonly AccountManager _accountManager;
    private readonly GameDataManager _gameDataManager;
    private readonly ResourceInstallerService _resourceInstaller;
    private readonly TaskManager _taskManager;
    public Action OnPaneClose { get; init; } = () => { };
    [ObservableProperty] private string gameDataName = string.Empty;
    [ObservableProperty] private List<UserVersion> availableBaseVersions = new();
    [ObservableProperty] private UserVersion? selectedBaseVersion;
    [ObservableProperty] private List<ModEnum> availableModLoaders = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstallRecommendedMods))]
    private ModEnum? selectedModLoader;
    [ObservableProperty] private GameDataAccountOption? selectedAccount;
    [ObservableProperty] private bool installRecommendedMods;

    public List<GameDataAccountOption> AvailableAccounts { get; }
    public bool CanInstallRecommendedMods =>
        SelectedModLoader is ModEnum.fabric or ModEnum.quilt or ModEnum.forge or ModEnum.neoforge;

    public NewGameDataPaneViewModel(
        VersionListManager versionListManager,
        AccountManager accountManager,
        GameDataManager gameDataManager,
        ResourceInstallerService resourceInstaller,
        TaskManager taskManager,
        Action onCloseCallback,
        UserVersion? defaultBasicVerions = null)
    {
        _versionListManager = versionListManager;
        this._gameDataManager = gameDataManager;
        this._accountManager = accountManager;
        _resourceInstaller = resourceInstaller;
        _taskManager = taskManager;
        OnPaneClose = onCloseCallback ?? (() => { });
        // 加载所需数据
        AvailableBaseVersions = _versionListManager.GetVersions();
        AvailableAccounts = GameDataAccountOption.Build(_accountManager.GetAllUsers());
        SelectedAccount = AvailableAccounts[0];

        // 默认情况下，模组加载器列表为空，等待用户选择基础版本
        AvailableModLoaders = new List<ModEnum> { ModEnum.none };
        SelectedModLoader = ModEnum.none;
        // 通过属性设置，才能初始化指定版本的加载器选项和实例名称。
        if (defaultBasicVerions != null)
            SelectedBaseVersion = AvailableBaseVersions.FirstOrDefault(
                version => version.VersionID == defaultBasicVerions.VersionID);
    }
    partial void OnSelectedModLoaderChanged(ModEnum? value)
    {
        if (!CanInstallRecommendedMods)
            InstallRecommendedMods = false;
    }

    partial void OnSelectedBaseVersionChanged(UserVersion? value)
    {
        if (value == null)
        {
            // 如果清空选择，则重置加载器列表
            AvailableModLoaders = new List<ModEnum> { ModEnum.none };
            SelectedModLoader = ModEnum.none;
            GameDataName = string.Empty;
            return;
        }

        var loaders = new List<ModEnum> { ModEnum.none };
        if (value.modType.IsFabric) loaders.Add(ModEnum.fabric);
        if (value.modType.IsQuilt) loaders.Add(ModEnum.quilt);
        if (value.modType.IsNeoForge) loaders.Add(ModEnum.neoforge);
        if (value.modType.IsForge) loaders.Add(ModEnum.forge);
        AvailableModLoaders = loaders;

        SelectedModLoader = value.modType.ToModEnum();
        GameDataName = $"{value.VersionID} - Instance";
    }

    [RelayCommand]
    private async Task Save()
    {
        // 数据验证
        if (SelectedBaseVersion == null)
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("请先选择一个基础游戏版本！",NotificationType.Warning));
            return;
        }
        if (string.IsNullOrWhiteSpace(GameDataName))
        {
            WeakReferenceMessenger.Default.Send(
                new MainWindowShowFlyoutMessage("游戏数据名称不能为空！",NotificationType.Warning));
            return;
        }

        var loaderType = SelectedModLoader ?? ModEnum.none;
        var shouldInstallRecommendedMods = InstallRecommendedMods && CanInstallRecommendedMods;

        var newGameData = new GameData(
            name: GameDataName,
            versionId: SelectedBaseVersion.VersionID,
            loader: loaderType, 
            userModel: SelectedAccount?.UserId
        );

        try
        {
            await _gameDataManager.AddGameDataAsync(newGameData);
        }
        catch (OlanException ex)
        {
            await OlanExceptionWorker.ForOlanException(ex);
            return;
        }
        catch (Exception ex)
        {
            await OlanExceptionWorker.ForUnknowException(ex);
            return;
        }

        WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage($"已成功创建游戏数据：{newGameData.Name}"));
        if (shouldInstallRecommendedMods)
            _ = InstallRecommendedModsAsync(newGameData);
        Cancel();
    }

    private async Task InstallRecommendedModsAsync(GameData gameData)
    {
        var task = _taskManager.CreateTask($"安装推荐模组 {gameData.Name}");
        var token = task.Token!.Token;
        task.SetProgress("正在获取推荐模组信息...", 0);
        NotifyInstallation("已开始在后台安装推荐模组，可在任务列表查看进度");

        var progress = new Progress<(int current, string file)>(p =>
            Dispatcher.UIThread.Post(() =>
            {
                if (task.Status != TaskInfoStatus.Running) return;
                // 接口只报告文件序号，没有总数；成功返回后才标记 100%。
                task.SetProgress($"正在下载第 {p.current} 个文件：{p.file}", 0);
            }));

        try
        {
            await Task.Run(() => _resourceInstaller.InstallRecommendedMods(progress, gameData, token), token);
            token.ThrowIfCancellationRequested();
            task.SetProgress("推荐模组安装完成", 100);
            task.SetDone();
            NotifyInstallation("推荐模组安装完成", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            SetInstallationCanceled(task);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            SetInstallationCanceled(task);
        }
        catch (OlanException ex)
        {
            task.SetFailed(ex);
            NotifyInstallation("推荐模组安装失败，游戏数据已保留", NotificationType.Error);
            await OlanExceptionWorker.ForOlanException(ex);
        }
        catch (Exception ex)
        {
            task.SetFailed(ex);
            NotifyInstallation("推荐模组安装失败，游戏数据已保留", NotificationType.Error);
            await OlanExceptionWorker.ForUnknowException(ex);
        }
    }

    private static void SetInstallationCanceled(TaskInstance task)
    {
        task.Cancel();
        task.SetProgress("推荐模组安装已取消，游戏数据已保留", task.Progress);
        NotifyInstallation("推荐模组安装已取消，游戏数据已保留", NotificationType.Warning);
    }

    private static void NotifyInstallation(string message, NotificationType type = NotificationType.Information)
        => WeakReferenceMessenger.Default.Send(new MainWindowShowFlyoutMessage(message, type, openTaskCenterAfterClick: true));

    [RelayCommand]
    private void Cancel()
    {
        OnPaneClose();
        //MainWindow.mainwindow.gamedataPage.viewmodel.IsPaneShow = false;
    }
}
