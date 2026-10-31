using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using fNbt;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;

internal static class ServerDatChecks
{
    public static async Task<int> RunAsync()
    {
        // 不初始化启动器，所有夹具都在仓库内，结束后清理。
        string root = Path.GetFullPath(Path.Combine("build", "server-dat-20261031", "data", Guid.NewGuid().ToString("N")));
        string previousGameRoot = Init.GameRootPath;
        Init.GameRootPath = Path.Combine(root, "minecraft");
        Directory.CreateDirectory(root);
        try
        {
            CheckBackend();
            await CheckPaneAsync(root);
            Console.WriteLine("PASS server-dat: create, append, host-only duplicates, IPv6, field preservation, corrupt/locked files, atomic failure, concurrency and pane save");
            return 0;
        }
        finally
        {
            Init.GameRootPath = previousGameRoot;
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServerEntry Entry(string instance, string host, ushort port = 25565, string name = "中文服务器 🎮")
        => new(Guid.NewGuid(), instance, new ServerInfo(host, port), name, null);

    private static string DatPath(string instance)
        => Path.Combine(Init.GameRootPath, "instance", instance, "servers.dat");

    private static NbtList Servers(string instance) => new NbtFile(DatPath(instance)).RootTag.Get<NbtList>("servers")!;

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void ExpectFailure(ServerEntry entry)
    {
        try { entry.SaveInServerDAT(); }
        catch (OlanException ex)
        {
            Assert(ex.Title == "保存游戏服务器列表失败", "Failure must have a business error title");
            return;
        }
        throw new InvalidOperationException("Expected dat save to fail");
    }

    private static void AssertUnchanged(ServerEntry entry)
    {
        string path = DatPath(entry.InstanceId);
        byte[] original = File.ReadAllBytes(path);
        DateTime timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        entry.SaveInServerDAT();
        Assert(File.ReadAllBytes(path).SequenceEqual(original) && File.GetLastWriteTimeUtc(path) == timestamp,
            "Duplicate host must not rewrite bytes or timestamp: " + entry.ServerInfo.Ip);
    }

    private static void CheckBackend()
    {
        var entry = Entry("first", "mc.example.net");
        entry.SaveInServerDAT();
        var list = Servers("first");
        Assert(list.Count == 1 && list.Get<NbtCompound>(0)!.Get<NbtString>("name")!.Value == entry.Name &&
               list.Get<NbtCompound>(0)!.Get<NbtString>("ip")!.Value == "mc.example.net:25565" &&
               list.Get<NbtCompound>(0)!.Get<NbtByte>("hidden")!.Value == 0 &&
               File.ReadAllBytes(DatPath("first"))[0] == 10, "Create valid uncompressed NBT with Chinese and emoji");
        AssertUnchanged(Entry("first", "MC.EXAMPLE.NET.", 25566));
        AssertUnchanged(Entry("first", "mc.example.net:25567", 25568));

        // 未指定端口的旧条目同样参与去重，隐藏状态不影响判断。
        var file = new NbtFile(DatPath("first"));
        var existing = file.RootTag.Get<NbtList>("servers")!.Get<NbtCompound>(0)!;
        existing.Get<NbtString>("ip")!.Value = "mc.example.net";
        existing.Get<NbtByte>("hidden")!.Value = 1;
        existing.Add(new NbtString("icon", "existing-icon"));
        existing.Add(new NbtInt("custom", 42));
        file.RootTag.Add(new NbtString("extra", "preserve"));
        file.SaveToFile(DatPath("first"), NbtCompression.None);
        AssertUnchanged(Entry("first", "mc.example.net", 25566));
        Entry("first", "other.example.net").SaveInServerDAT();
        var appended = new NbtFile(DatPath("first"));
        Assert(appended.RootTag.Get<NbtList>("servers")!.Count == 2 &&
               NbtComparer.Instance.Equals(existing, appended.RootTag.Get<NbtList>("servers")![0]) &&
               appended.RootTag.Get<NbtString>("extra")!.Value == "preserve", "Append preserves old entries and unknown fields");

        Entry("second", "mc.example.net").SaveInServerDAT();
        Assert(Servers("second").Count == 1 && Servers("first").Count == 2, "Deduplication is scoped to instance");
        Entry("ipv4", "192.0.2.1").SaveInServerDAT();
        AssertUnchanged(Entry("ipv4", "192.0.2.1", 25566));
        Entry("ipv6", "2001:db8::1").SaveInServerDAT();
        Assert(Servers("ipv6").Get<NbtCompound>(0)!.Get<NbtString>("ip")!.Value == "[2001:db8::1]:25565", "IPv6 port uses brackets");
        AssertUnchanged(Entry("ipv6", "[2001:0db8:0:0:0:0:0:1]:25566"));
        Entry("ipv6", "2001:db8::2").SaveInServerDAT();
        Assert(Servers("ipv6").Count == 2, "Distinct IPv6 hosts must not be collapsed");

        var oldIpv6 = new NbtFile(DatPath("ipv6"));
        oldIpv6.RootTag.Get<NbtList>("servers")!.Get<NbtCompound>(0)!.Get<NbtString>("ip")!.Value = "2001:db8::1";
        oldIpv6.SaveToFile(DatPath("ipv6"), NbtCompression.None);
        AssertUnchanged(Entry("ipv6", "[2001:db8::1]", 25566));

        foreach (var compression in new[] { NbtCompression.None, NbtCompression.GZip, NbtCompression.ZLib })
        {
            string instance = compression.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(DatPath(instance))!);
            var empty = new NbtFile(new NbtCompound("") { new NbtList("servers", NbtTagType.End) });
            empty.SaveToFile(DatPath(instance), compression);
            Entry(instance, "empty.example.net").SaveInServerDAT();
            Assert(Servers(instance).Count == 1 && new NbtFile(DatPath(instance)).FileCompression == compression,
                "Supports empty TAG_End list and preserves compression");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DatPath("missing-list"))!);
        new NbtFile(new NbtCompound("") { new NbtInt("custom", 9) }).SaveToFile(DatPath("missing-list"), NbtCompression.None);
        Entry("missing-list", "mc.example.net").SaveInServerDAT();
        Assert(Servers("missing-list").Count == 1, "Creates absent servers list");

        Directory.CreateDirectory(Path.GetDirectoryName(DatPath("bad"))!);
        foreach (byte[] bad in new[] { Array.Empty<byte>(), new byte[] { 10, 0 }, new byte[] { 1, 2, 3 } })
        {
            File.WriteAllBytes(DatPath("bad"), bad);
            ExpectFailure(Entry("bad", "mc.example.net"));
            Assert(File.ReadAllBytes(DatPath("bad")).SequenceEqual(bad), "Corrupt file must remain untouched");
        }
        new NbtFile(new NbtCompound("") { new NbtString("servers", "wrong-type") }).SaveToFile(DatPath("bad"), NbtCompression.None);
        byte[] malformed = File.ReadAllBytes(DatPath("bad"));
        ExpectFailure(Entry("bad", "mc.example.net"));
        Assert(File.ReadAllBytes(DatPath("bad")).SequenceEqual(malformed), "Wrong list type must remain untouched");
        using (File.Open(DatPath("first"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ExpectFailure(Entry("first", "locked.example.net"));
        byte[] beforeFailure = File.ReadAllBytes(DatPath("first"));
        ExpectFailure(Entry("first", "too-long.example.net", name: new string('a', 65536)));
        Assert(File.ReadAllBytes(DatPath("first")).SequenceEqual(beforeFailure) &&
               Directory.GetFiles(Path.GetDirectoryName(DatPath("first"))!, "*.tmp").Length == 0,
            "Serialization failure preserves original and removes temporary file");

        Parallel.For(0, 12, i => Entry("concurrent", "mc.example.net", (ushort)(25565 + i)).SaveInServerDAT());
        Assert(Servers("concurrent").Count == 1, "Concurrent duplicate saves must append only once");
    }

    private static async Task CheckPaneAsync(string root)
    {
        var coreProperty = typeof(UnitedCore).GetProperty("Core", BindingFlags.NonPublic | BindingFlags.Static)!;
        var downloadProperty = typeof(Init).GetProperty("Download", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previousCore = coreProperty.GetValue(null), previousDownload = downloadProperty.GetValue(null);
        var config = new DBManager(Path.Combine(root, "config.json"));
        await config.InitializeAsync();
        var games = new GameDataManager(Path.Combine(root, "games.json"));
        await games.InitializeAsync();
        var game = new GameData("fixture", "1.21.1", ModEnum.none, Guid.Empty, DateTime.UtcNow, null, null, null)
        { InstanceId = "pane" };
        games.Data.Instances.Add(game.InstanceId, game);
        var core = (UnitedCore)RuntimeHelpers.GetUninitializedObject(typeof(UnitedCore));
        typeof(UnitedCore).GetProperty(nameof(UnitedCore.TaskManager))!.SetValue(core, new TaskManager());
        coreProperty.SetValue(null, core);
        using var client = new HttpClient(new OfflineStatusHandler());
        using var download = (IDisposable)Activator.CreateInstance(
            typeof(Init).Assembly.GetType("OneLauncher.Core.DefaultComponent.Downloader.Download", true)!, client)!;
        downloadProperty.SetValue(null, download);
        try
        {
            Type paneType = typeof(OneLauncher.App).Assembly.GetType("OneLauncher.Views.Panes.PaneViewModels.AddServerPaneViewModel", true)!;
            for (int i = 0; i < 2; i++)
            {
                bool closed = false;
                Guid updated = Guid.Empty;
                object pane = Activator.CreateInstance(paneType, config, games, (Action)(() => closed = true),
                    (Action<Guid>)(id => updated = id))!;
                paneType.GetProperty("ServerAddress")!.SetValue(pane, "pane.example.net");
                paneType.GetProperty("ServerPort")!.SetValue(pane, (25565 + i).ToString());
                await ((IAsyncRelayCommand)paneType.GetProperty("SaveCommand")!.GetValue(pane)!).ExecuteAsync(null);
                var reloaded = new DBManager(Path.Combine(root, "config.json"));
                await reloaded.InitializeAsync();
                Assert(closed && updated == config.Data.ServerList.Last().Id && reloaded.Data.ServerList.Count == i + 1 &&
                       Servers(game.InstanceId).Count == 1, "Pane saves each favorite while dat deduplicates different ports");
            }
        }
        finally
        {
            coreProperty.SetValue(null, previousCore);
            downloadProperty.SetValue(null, previousDownload);
        }
    }

    private sealed class OfflineStatusHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"online\":true}", Encoding.UTF8, "application/json") });
    }
}
