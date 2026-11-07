using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Services;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Views.Panes;
using OneLauncher.Views.Panes.PaneViewModels;

internal static class GameDataUiChecks
{
#if DEBUG
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Game data UI check failed: " + message);
        _checks++;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Game data UI fixture timed out.");
            await Task.Delay(10);
        }
    }

    public static async Task RunAsync(string output)
    {
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory.GetCurrentDirectory()));
        output = Path.GetFullPath(output);
        if (!output.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("测试输出必须位于当前工作区内。", nameof(output));
        if (Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("测试输出目录必须为新目录。", nameof(output));
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        var previousRoot = Init.GameRootPath;
        Window? window = null;
        var icons = new List<IDisposable>();
        try
        {
            Init.GameRootPath = Path.Combine(fixture, "minecraft");
            var accounts = new AccountManager(Path.Combine(fixture, "accounts.json"));
            await accounts.InitializeAsync();
            accounts.Data.UserDictionary.Clear();
            accounts.Data.DefaultUserID = null;
            var gamesPath = Path.Combine(fixture, "games.json");
            var games = new GameDataManager(gamesPath);
            await games.InitializeAsync();
            var versions = new VersionListManager(Init.GameRootPath);
            var installed = new UserVersion
            {
                VersionID = "1.21.1",
                modType = new ModType { IsFabric = true, IsQuilt = true, IsForge = true, IsNeoForge = true }
            };
            await versions.InitializeAsync(new VersionListData { VersionList = [installed] });
            var tasks = new TaskManager();
            var core = new UnitedCore
            {
                AccountManager = accounts,
                GameDataManager = games,
                VersionListManager = versions,
                TaskManager = tasks,
                ResourceInstallerService = new ResourceInstallerService(),
                VersionInstallerService = null!,
                GameLaunchService = null!,
                MojangVersionList = [],
                MsalAuthenticator = null!,
                JavaManager = null!
            };
            var factory = new NewGameDataPaneViewModelFactory(core);
            var empty = factory.Create(() => { });
            Check(empty.AvailableAccounts.Count == 1 && empty.SelectedAccount!.IsGlobalDefault,
                "empty account list preserves global default option");
            Check(!empty.CanInstallRecommendedMods && !empty.InstallRecommendedMods,
                "initial recommendation option is disabled and unchecked");

            var user = new UserModel(Guid.NewGuid(), "测试账户", Guid.NewGuid());
            await accounts.AddUser(user);
            await accounts.SetDefault(user.UserID);
            bool closed = false;
            var model = factory.Create(installed, () => closed = true);
            Check(model.SelectedBaseVersion?.VersionID == installed.VersionID &&
                model.SelectedAccount!.IsGlobalDefault, "default-version factory initializes version and global account");
            var pane = new NewGameDataPane { DataContext = model };
            window = new Window { Width = 650, Height = 960, Content = pane };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var accountBox = AccountBox(pane, model.AvailableAccounts);
            var recommended = pane.GetVisualDescendants().OfType<CheckBox>().Single();
            Check(ReferenceEquals(accountBox.SelectedItem, model.SelectedAccount), "new account selection binds to real view");
            Check(accountBox.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "使用全局默认账户"),
                "new account template displays global label");
            accountBox.SelectedItem = model.AvailableAccounts.Single(account => account.UserId == user.UserID);
            Check(model.SelectedAccount?.UserId == user.UserID, "new account ComboBox writes explicit ID to model");
            accountBox.SelectedItem = model.AvailableAccounts[0];
            Check(model.SelectedAccount!.UserId is null, "new account ComboBox restores global option");

            foreach (var loader in new[] { ModEnum.fabric, ModEnum.quilt, ModEnum.forge, ModEnum.neoforge })
            {
                model.SelectedModLoader = loader;
                Dispatcher.UIThread.RunJobs();
                Check(model.CanInstallRecommendedMods && recommended.IsEnabled, loader + " enables recommendation option");
                recommended.IsChecked = true;
                Check(model.InstallRecommendedMods, loader + " recommendation CheckBox writes to model");
                model.SelectedModLoader = ModEnum.none;
                Dispatcher.UIThread.RunJobs();
                Check(!model.CanInstallRecommendedMods && !model.InstallRecommendedMods &&
                    !recommended.IsEnabled && recommended.IsChecked == false, loader + " to none clears and disables option");
            }
            model.SelectedModLoader = ModEnum.fabric;
            recommended.IsChecked = true;
            Capture(window, output, "new-game-data-light.png", ThemeVariant.Light);
            Capture(window, output, "new-game-data-dark.png", ThemeVariant.Dark);
            window.Height = 620;
            Capture(window, output, "new-game-data-small.png", ThemeVariant.Light);
            window.Height = 960;
            recommended.IsChecked = false;
            model.GameDataName = "跟随全局账户";
            await model.SaveCommand.ExecuteAsync(null);
            var globalGame = games.AllGameData.Single();
            Check(closed && globalGame.DefaultUserModelID is null, "new global selection saves null and closes pane");
            Check((await Reload(gamesPath)).AllGameData.Single().DefaultUserModelID is null,
                "global account null survives JSON reload");

            var explicitModel = factory.Create(() => { });
            explicitModel.SelectedBaseVersion = explicitModel.AvailableBaseVersions.Single();
            explicitModel.SelectedAccount = explicitModel.AvailableAccounts.Single(account => account.UserId == user.UserID);
            explicitModel.GameDataName = "固定测试账户";
            await explicitModel.SaveCommand.ExecuteAsync(null);
            var explicitGame = games.AllGameData.Single(game => game.Name == explicitModel.GameDataName);
            Check((await Reload(gamesPath)).GetInstanceFromId(explicitGame.InstanceId)!.DefaultUserModelID == user.UserID,
                "explicit account ID survives new-instance JSON reload");

            var tag = new GameDataTag { ID = Guid.NewGuid(), Name = "测试标签" };
            games.Data.Tags.Add(tag.ID, tag);
            var deletedId = Guid.NewGuid();
            var missingGame = new GameData("已删除账户的实例", installed.VersionID, ModEnum.fabric, deletedId);
            await games.AddGameDataAsync(missingGame);
            foreach (var game in games.AllGameData) games.Data.TagMap[game.InstanceId] = tag.ID;
            await games.Save();
            int saves = 0;
            games.OnDataChanged += () => saves++;
            var editFactory = new EditGameDataPaneViewModelFactory(core);
            foreach (var game in new[] { globalGame, explicitGame, missingGame })
            {
                var beforeId = game.DefaultUserModelID;
                var beforeJson = await File.ReadAllBytesAsync(gamesPath);
                int beforeSaves = saves;
                var edit = editFactory.Create(game, () => { });
                icons.Add(edit.CurrentIcon);
                var editPane = new EditGameDataPane { DataContext = edit };
                window.Content = editPane;
                Dispatcher.UIThread.RunJobs();
                Check(edit.SelectedAccount!.UserId == (beforeId == user.UserID ? user.UserID : null),
                    "edit selects correct option for " + game.Name);
                Check(edit.SelectedTag?.ID == tag.ID, "edit initializes existing tag");
                Check(saves == beforeSaves && game.DefaultUserModelID == beforeId &&
                    (await File.ReadAllBytesAsync(gamesPath)).SequenceEqual(beforeJson),
                    "opening edit with account and tag does not mutate or save " + game.Name);
                var editAccountBox = AccountBox(editPane, edit.AvailableAccounts);
                Check(ReferenceEquals(editAccountBox.SelectedItem, edit.SelectedAccount), "edit account selection binds to real view");
                if (game == globalGame)
                {
                    Capture(window, output, "edit-game-data-light.png", ThemeVariant.Light);
                    Capture(window, output, "edit-game-data-dark.png", ThemeVariant.Dark);
                }
                if (edit.SelectedAccount.IsGlobalDefault)
                {
                    editAccountBox.SelectedItem = edit.AvailableAccounts.Single(account => account.UserId == user.UserID);
                    await Until(() => saves > beforeSaves);
                    Check((await Reload(gamesPath)).GetInstanceFromId(game.InstanceId)!.DefaultUserModelID == user.UserID,
                        "edit explicit account persists after ComboBox change");
                }
                int beforeGlobalSave = saves;
                editAccountBox.SelectedItem = edit.AvailableAccounts[0];
                await Until(() => saves > beforeGlobalSave);
                Check((await Reload(gamesPath)).GetInstanceFromId(game.InstanceId)!.DefaultUserModelID is null,
                    "edit global account persists null after ComboBox change");
            }
            Check(tasks.TaskQueue.IsEmpty, "unchecked creation performs no background installation");

            // 在任务创建时取消，验证前端保留实例并终止后台流程；不触发任何网络请求。
            bool canceledPaneClosed = false;
            var canceledModel = factory.Create(installed, () => canceledPaneClosed = true);
            canceledModel.GameDataName = "取消推荐安装";
            canceledModel.InstallRecommendedMods = true;
            void CancelCreatedTask() => tasks.TaskQueue.Last().Cancel();
            tasks.OnTaskCreate += CancelCreatedTask;
            try
            {
                await canceledModel.SaveCommand.ExecuteAsync(null);
            }
            finally
            {
                tasks.OnTaskCreate -= CancelCreatedTask;
            }
            var canceledTask = tasks.TaskQueue.Single();
            await Until(() => canceledTask.Current == "推荐模组安装已取消，游戏数据已保留");
            Check(canceledPaneClosed && canceledTask.Name == "安装推荐模组 取消推荐安装",
                "checked creation registers background task and closes pane");
            Check(canceledTask.Status == TaskInfoStatus.Canceled && canceledTask.Token!.IsCancellationRequested,
                "background cancellation preserves canceled task status and token");
            var canceledGame = games.AllGameData.Single(game => game.Name == canceledModel.GameDataName);
            Check(Directory.Exists(canceledGame.InstancePath) &&
                (await Reload(gamesPath)).GetInstanceFromId(canceledGame.InstanceId) is not null,
                "canceling recommendations preserves instance directory and persisted game data");
            Check(!Directory.Exists(Path.Combine(canceledGame.InstancePath, "mods")),
                "pre-canceled installation performs no downloads");
            Console.WriteLine($"PASS {_checks} game data UI checks: accounts, initialization, loaders, JSON persistence and screenshots.");
        }
        finally
        {
            window?.Close();
            foreach (var icon in icons) icon.Dispose();
            Init.GameRootPath = previousRoot;
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
        }
    }

    private static ComboBox AccountBox(Control pane, List<GameDataAccountOption> options)
        => pane.GetVisualDescendants().OfType<ComboBox>().Single(box => ReferenceEquals(box.ItemsSource, options));

    private static async Task<GameDataManager> Reload(string path)
    {
        var manager = new GameDataManager(path);
        await manager.InitializeAsync();
        return manager;
    }

    private static void Capture(Window window, string output, string name, ThemeVariant theme)
    {
        Application.Current!.RequestedThemeVariant = theme;
        if (window.TryFindResource("PaneBackgroundBrush", out var brush)) window.Background = (IBrush)brush!;
        var deadline = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No rendered frame for " + name);
        frame.Save(Path.Combine(output, name));
    }
#else
    public static Task RunAsync(string output)
        => Task.FromException(new NotSupportedException("Game data UI fixtures require a Debug build."));
#endif
}
