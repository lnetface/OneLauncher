using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.CurseForge;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.Modrinth;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Interfaces;
using OneLauncher.ViewModel.Page;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Views.Panes;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;

internal partial class ModsBrowserViewModel : BaseViewModel, IPageHost, IListTemplate<ResourceItem>
{
    private readonly InstallModPaneViewModelFactory _paneVMFactory;
    private readonly InstallPackPaneViewModelFactory _packPaneVMFactory;
    private CancellationTokenSource? _searchCancellation;
    private long _searchGeneration;

    // 名称与图标由全局 UiOptionConverter 统一描述。
    /// <summary>提供商下拉选项：CurseForge + Modrinth、仅 CurseForge、仅 Modrinth。</summary>
    public ResourceSource[][] ResourceSourceOptions { get; } =
    [
        [ResourceSource.CurseForge, ResourceSource.Modrinth],
        [ResourceSource.CurseForge],
        [ResourceSource.Modrinth]
    ];
    public ResourceType[] ResourceTypes { get; } = Enum.GetValues<ResourceType>();

    public ModsBrowserViewModel(InstallModPaneViewModelFactory installModPaneViewModelFactory,
        InstallPackPaneViewModelFactory installPackPaneViewModelFactory)
    {
        _paneVMFactory = installModPaneViewModelFactory;
        _packPaneVMFactory = installPackPaneViewModelFactory;
        // CurseForge 需要 API Key，未配置时默认只用 Modrinth，避免首次进入就报错。
        selectedResourceSources = OneLauncher.Core.Global.Init.CurseforgeApiKey != null
            ? ResourceSourceOptions[0]
            : ResourceSourceOptions[2];
        _ = RefList();
    }

    [ObservableProperty] private bool isPaneShow;
    [ObservableProperty] private UserControl? paneContent;
    [ObservableProperty] private string searchContent = string.Empty;
    [ObservableProperty] private ResourceSource[] selectedResourceSources;
    [ObservableProperty] private ResourceType selectedResourceType;
    [ObservableProperty] private ObservableCollection<ResourceItem> items = new();
    [ObservableProperty] private bool isSearching;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSearchStatus))]
    private string searchStatus = string.Empty;

    public bool CanClear => !string.IsNullOrEmpty(SearchContent);
    public bool HasSearchStatus => !string.IsNullOrWhiteSpace(SearchStatus);
    public string SearchWatermark => SelectedResourceType switch
    {
        ResourceType.ShaderPack => "搜索光影包...",
        ResourceType.Modpack => "搜索整合包...",
        _ => "搜索模组..."
    };

    partial void OnSearchContentChanged(string value) => OnPropertyChanged(nameof(CanClear));

    partial void OnSelectedResourceSourcesChanged(ResourceSource[] value)
    {
        if (value == null) return;
        Items.Clear();
        _ = RefList();
    }

    partial void OnSelectedResourceTypeChanged(ResourceType value)
    {
        OnPropertyChanged(nameof(SearchWatermark));
        Items.Clear();
        _ = RefList();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefList()
    {
        var generation = Interlocked.Increment(ref _searchGeneration);
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var type = SelectedResourceType;
        var sources = SelectedResourceSources ?? [];
        string keyword = SearchContent;
        IsSearching = true;
        SearchStatus = "正在搜索...";
        try
        {
            if (sources.Length == 0)
            {
                Items = new ObservableCollection<ResourceItem>();
                SearchStatus = "请至少选择一个资源提供商。";
                return;
            }
            // 各提供商并行检索；单个提供商失败不影响其他提供商的结果。
            var searches = sources.Select(source => SearchAsync(source, keyword, type, cancellation.Token)).ToArray();
            try { await Task.WhenAll(searches); }
            catch when (searches.Any(s => s.IsCompletedSuccessfully)) { }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!searches.Any(s => s.IsCompletedSuccessfully))
                await searches[0]; // 全部失败：抛出首个异常，交给下方统一处理。

            var resources = Interleave(searches.Where(s => s.IsCompletedSuccessfully).Select(s => s.Result).ToList());
            var failed = searches.Select((s, i) => (Task: s, Source: sources[i])).Where(x => x.Task.IsFaulted).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _searchGeneration || cancellation.IsCancellationRequested) return;
                Items = new ObservableCollection<ResourceItem>(resources);
                SearchStatus = failed.Count > 0
                    ? $"{string.Join("、", failed.Select(x => x.Source))} 搜索失败，仅显示其他提供商的结果。"
                    : resources.Count == 0 ? "没有找到匹配的资源，试试其他关键词。" : string.Empty;
            });
            foreach (var (task, _) in failed)
                await ReportSearchError(task.Exception!.InnerException!);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 被后续搜索替代的请求不显示错误。
        }
        catch (Exception ex)
        {
            if (generation != _searchGeneration) return;
            SearchStatus = "搜索失败，请重试。";
            await ReportSearchError(ex);
        }
        finally
        {
            if (generation == _searchGeneration)
            {
                IsSearching = false;
                _searchCancellation = null;
            }
        }
    }

    private static async Task<List<ResourceItem>> SearchAsync(
        ResourceSource source, string keyword, ResourceType type, CancellationToken token) => source switch
    {
        ResourceSource.CurseForge => ResourceItem.Create(
            await SearchCurseForge.SearchAsync(keyword, type, cancellationToken: token), type),
        _ => ResourceItem.Create(
            await SearchModrinth.SearchAsync(keyword, type, cancellationToken: token), type)
    };

    /// <summary>按名次交替合并各提供商结果，避免某一平台的结果全部排在后面。</summary>
    private static List<ResourceItem> Interleave(List<List<ResourceItem>> lists)
    {
        var merged = new List<ResourceItem>(lists.Sum(l => l.Count));
        for (int i = 0; i < (lists.Count == 0 ? 0 : lists.Max(l => l.Count)); i++)
            merged.AddRange(lists.Where(l => i < l.Count).Select(l => l[i]));
        return merged;
    }

    private static Task ReportSearchError(Exception ex)
        => ex is OneLauncher.Core.Global.OlanException known
            ? OlanExceptionWorker.ForOlanException(known)
            : OlanExceptionWorker.ForUnknowException(ex);

    [RelayCommand]
    public void ClearTextBox() => SearchContent = string.Empty;

    [RelayCommand]
    public void ToInstallResource(ResourceItem item)
    {
        if (item.Type == ResourceType.Modpack)
        {
            var packPane = new InstallPackPane();
            var viewModel = _packPaneVMFactory.Create(item, () =>
            {
                if (ReferenceEquals(PaneContent, packPane)) IsPaneShow = false;
            });
            packPane.DataContext = viewModel;
            PaneContent = packPane;
            IsPaneShow = true;
            _ = viewModel.LoadInfo();
            return;
        }
        var pane = new InstallModPane();
        pane.DataContext = _paneVMFactory.Create(item, () =>
        {
            // 通知可能已替换为任务中心；旧侧栏不能关闭当前侧栏。
            if (ReferenceEquals(PaneContent, pane)) IsPaneShow = false;
        });
        PaneContent = pane;
        IsPaneShow = true;
    }
}

/// <summary>搜索结果携带资源类型与来源平台，供安装命令选择对应侧栏和后端分派。</summary>
internal sealed class ResourceItem : BaseViewModel
{
    public string Title { get; init; } = string.Empty;
    public Uri? IconUrl { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ID { get; init; } = string.Empty;
    public List<string> SupportVersions { get; init; } = new();
    public ModType SupportModType { get; init; }
    public ResourceType Type { get; init; }
    public ResourceSource Source { get; init; }
    public DateTime CreationTime { get; init; }

    public static List<ResourceItem> Create(ModrinthSearch info, ResourceType resourceType)
        => (info.Hits ?? []).Select(hit =>
        {
            var categories = hit.Categories ?? [];
            return new ResourceItem
            {
                Title = hit.Title ?? string.Empty,
                Description = hit.Description ?? string.Empty,
                ID = hit.ProjectId,
                Type = resourceType,
                Source = ResourceSource.Modrinth,
                CreationTime = hit.DateCreated,
                IconUrl = Uri.TryCreate(hit.IconUrl, UriKind.Absolute, out var icon) ? icon : null,
                SupportVersions = resourceType == ResourceType.Mod
                    ? Tools.McVsFilter(hit.Versions)
                    : new List<string>(hit.Versions ?? []),
                SupportModType = new ModType
                {
                    IsFabric = categories.Contains("fabric"),
                    IsNeoForge = categories.Contains("neoforge"),
                    IsForge = categories.Contains("forge"),
                    IsQuilt = categories.Contains("quilt")
                }
            };
        }).ToList();

    public static List<ResourceItem> Create(CurseForgeSearchResponse info, ResourceType resourceType)
        => (info.Data ?? []).Select(mod =>
        {
            var versions = mod.GetSupportedGameVersions();
            var loaders = mod.GetSupportedModLoaders();
            var icon = mod.Logo?.ThumbnailUrl ?? mod.Logo?.Url;
            return new ResourceItem
            {
                Title = mod.Name ?? string.Empty,
                Description = mod.Summary ?? string.Empty,
                ID = mod.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Type = resourceType,
                Source = ResourceSource.CurseForge,
                CreationTime = mod.DateCreated,
                IconUrl = Uri.TryCreate(icon, UriKind.Absolute, out var iconUrl) ? iconUrl : null,
                SupportVersions = resourceType == ResourceType.Mod ? Tools.McVsFilter(versions) : versions,
                SupportModType = new ModType
                {
                    IsFabric = loaders.Contains(CurseForgeModLoaderType.Fabric),
                    IsNeoForge = loaders.Contains(CurseForgeModLoaderType.NeoForge),
                    IsForge = loaders.Contains(CurseForgeModLoaderType.Forge),
                    IsQuilt = loaders.Contains(CurseForgeModLoaderType.Quilt)
                }
            };
        }).ToList();
}

