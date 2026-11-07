using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OneLauncher.Core.DefaultComponent.Downloader;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Services;

internal static class RecommendedModsChecks
{
#if DEBUG
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Recommended mods check failed: " + message);
            checks++;
        }

        string workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
        string parent = Path.Combine(workspace, "artifacts", "recommended-mods-20261108");
        bool createdParent = !Directory.Exists(parent);
        string fixture = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        string instanceId = "recommended-fixture-" + Guid.NewGuid().ToString("N");
        string legacyDirectory = Path.Combine(workspace, instanceId);
        Check(!Directory.Exists(legacyDirectory), "legacy relative directory is absent before fixture");
        var configProperty = typeof(Init).GetProperty("ConfigManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        var downloadProperty = typeof(Init).GetProperty("Download", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousConfig = configProperty.GetValue(null);
        var previousDownload = downloadProperty.GetValue(null);
        string previousRoot = Init.GameRootPath;
        using var handler = new FixtureHandler();
        using var client = new HttpClient(handler);
        using var download = new Download(client);
        Directory.CreateDirectory(fixture);
        try
        {
            Init.GameRootPath = Path.Combine(fixture, "minecraft");
            var config = new DBManager(Path.Combine(fixture, "config.json"));
            await config.InitializeAsync();
            var loader = new ModType { IsFabric = true };
            // The recommended minimum version differs from the actual installed version.
            var minimum = new ModLimited(loader, "1.16.5", Than.GreaterThan);
            config.Data.DefaultSettings.RecommandedMods =
            [
                new RecommendedMod("compatible", minimum),
                new RecommendedMod("missing-project", minimum),
                new RecommendedMod("incompatible", minimum)
            ];
            config.Data.OlanSettings.MaximumDownloadThreads = 1;
            configProperty.SetValue(null, config);
            downloadProperty.SetValue(null, download);
            var game = new GameData("离线推荐模组测试", "1.21.1", ModEnum.fabric, null)
            {
                InstanceId = instanceId
            };
            var service = new ResourceInstallerService();
            await service.InstallRecommendedMods(
                new Progress<(int current, string file)>(_ => { }),
                game, new ModLimited(loader, game.VersionId, Than.GreaterThan), CancellationToken.None);
            Check(true, "successful install skips 404 and empty versions without nullable or null-dependency errors");

            var requests = handler.Requests.ToArray();
            foreach (string project in new[] { "compatible", "missing-project", "incompatible" })
                Check(requests.Count(uri => uri.AbsolutePath == $"/v2/project/{project}/version") == 1,
                    "metadata requested once for " + project);
            var metadata = requests.Where(uri => uri.Host == "api.modrinth.com").ToArray();
            Check(metadata.All(uri => Uri.UnescapeDataString(uri.Query).Contains("game_versions=[\"1.21.1\"]")),
                "all metadata queries use actual instance version");
            Check(metadata.All(uri => Uri.UnescapeDataString(uri.Query).Contains("loaders=[\"fabric\"]")),
                "metadata queries preserve selected loader");
            Check(requests.Length == 4 && requests.Count(uri => uri.Host == "fixture.invalid") == 1,
                "only the compatible primary file is downloaded");
            string mods = Path.Combine(game.InstancePath, "mods");
            string file = Path.Combine(mods, "fixture.jar");
            Check(Path.IsPathFullyQualified(mods) && File.Exists(file), "file is written to absolute instance mods directory");
            Check((await File.ReadAllBytesAsync(file)).SequenceEqual(FixtureHandler.FileBytes), "installed file content matches fixture");
            Check(Directory.GetFiles(mods).Length == 1 && !Directory.Exists(legacyDirectory),
                "no skipped projects or legacy relative files are installed");
            Check(!Directory.EnumerateFiles(fixture, "*.downloading", SearchOption.AllDirectories).Any(),
                "download temporary files are removed");

            // Two parents share a dependency that is also recommended directly; a parent repeats as well.
            config.Data.DefaultSettings.RecommandedMods =
            [
                new RecommendedMod("parent-one", minimum),
                new RecommendedMod("parent-two", minimum),
                new RecommendedMod("shared-dependency", minimum),
                new RecommendedMod("parent-one", minimum),
                new RecommendedMod("normalized-alias", minimum),
                new RecommendedMod("case-alias", minimum)
            ];
            config.Data.OlanSettings.MaximumDownloadThreads = 4;
            handler.Requests.Clear();
            var duplicateGame = new GameData("重复依赖去重测试", game.VersionId, game.ModLoader, null)
            {
                InstanceId = instanceId + "-duplicates"
            };
            await service.InstallRecommendedMods(
                new Progress<(int current, string file)>(_ => { }),
                duplicateGame, new ModLimited(loader, duplicateGame.VersionId, Than.GreaterThan), CancellationToken.None);
            var duplicateRequests = handler.Requests.ToArray();
            var files = duplicateRequests.Where(uri => uri.Host == "fixture.invalid").ToArray();
            Check(files.Count(uri => uri.AbsolutePath == "/parent-one.jar") == 1,
                "repeated parent recommendation downloads its target once");
            Check(files.Count(uri => uri.AbsolutePath == "/parent-two.jar") == 1,
                "second parent target is downloaded once");
            Check(files.Count(uri => uri.AbsolutePath == "/shared.jar") == 1,
                "shared dependency, direct recommendation and ./ alias download one target once");
            int expectedCaseDownloads = OperatingSystem.IsWindows() ? 0 : 1;
            Check(files.Count(uri => uri.AbsolutePath == "/shared-uppercase.jar") == expectedCaseDownloads,
                "case aliases follow Windows case-insensitive and other-platform case-sensitive path comparison");
            string duplicateMods = Path.Combine(duplicateGame.InstancePath, "mods");
            Check(files.Length == 3 + expectedCaseDownloads &&
                Directory.GetFiles(duplicateMods).Length == 3 + expectedCaseDownloads,
                "parallel batch contains one download per normalized target path");
            foreach (var (name, bytes) in new[]
            {
                ("parent-one.jar", FixtureHandler.ParentOneBytes),
                ("parent-two.jar", FixtureHandler.ParentTwoBytes),
                ("shared.jar", FixtureHandler.SharedBytes)
            })
                Check((await File.ReadAllBytesAsync(Path.Combine(duplicateMods, name))).SequenceEqual(bytes),
                    "deduplicated file content matches " + name);
            if (!OperatingSystem.IsWindows())
                Check((await File.ReadAllBytesAsync(Path.Combine(duplicateMods, "SHARED.jar"))).SequenceEqual(FixtureHandler.CaseBytes),
                    "distinct case-sensitive target preserves its own content");
            Check(!Directory.EnumerateFiles(fixture, "*.downloading", SearchOption.AllDirectories).Any(),
                "parallel deduplicated downloads leave no temporary files");
            Console.WriteLine($"PASS {checks} recommended mods checks: missing/incompatible skips, actual version and instance path, shared dependencies, repeated recommendations and normalized target paths.");
            return 0;
        }
        finally
        {
            configProperty.SetValue(null, previousConfig);
            downloadProperty.SetValue(null, previousDownload);
            Init.GameRootPath = previousRoot;
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
            if (Directory.Exists(legacyDirectory)) Directory.Delete(legacyDirectory, true);
            if (createdParent && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("offline recommended mod fixture");
        public static readonly byte[] ParentOneBytes = Encoding.UTF8.GetBytes("first parent fixture");
        public static readonly byte[] ParentTwoBytes = Encoding.UTF8.GetBytes("second parent fixture");
        public static readonly byte[] SharedBytes = Encoding.UTF8.GetBytes("shared dependency fixture");
        public static readonly byte[] CaseBytes = Encoding.UTF8.GetBytes("separate uppercase target fixture");
        public ConcurrentBag<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Requests.Add(uri);
            HttpResponseMessage response;
            if (uri.Host == "api.modrinth.com")
            {
                response = uri.AbsolutePath switch
                {
                    "/v2/project/compatible/version" => Mod("fixture.jar", "fixture.jar", FileBytes),
                    "/v2/project/parent-one/version" => Mod("parent-one.jar", "parent-one.jar", ParentOneBytes, true),
                    "/v2/project/parent-two/version" => Mod("parent-two.jar", "parent-two.jar", ParentTwoBytes, true),
                    "/v2/project/shared-dependency/version" => Mod("shared.jar", "shared.jar", SharedBytes),
                    "/v2/project/normalized-alias/version" => Mod("./shared.jar", "shared.jar", SharedBytes),
                    "/v2/project/case-alias/version" => Mod("SHARED.jar", "shared-uppercase.jar", CaseBytes),
                    "/v2/project/missing-project/version" => new HttpResponseMessage(HttpStatusCode.NotFound)
                        { Content = new StringContent("{\"error\":\"not_found\"}") },
                    "/v2/project/incompatible/version" => Json("[]"),
                    _ => throw new InvalidOperationException("Unexpected metadata request: " + uri)
                };
            }
            else if (uri.Host == "fixture.invalid")
            {
                byte[] bytes = uri.AbsolutePath switch
                {
                    "/fixture.jar" => FileBytes,
                    "/parent-one.jar" => ParentOneBytes,
                    "/parent-two.jar" => ParentTwoBytes,
                    "/shared.jar" => SharedBytes,
                    "/shared-uppercase.jar" => CaseBytes,
                    _ => throw new InvalidOperationException("Unexpected file request: " + uri)
                };
                // Keep parallel file requests overlapping without contacting a real server.
                await Task.Delay(30, cancellationToken);
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            else
                throw new InvalidOperationException("Unexpected request: " + uri);
            return response;
        }

        private static HttpResponseMessage Mod(string filename, string downloadName, byte[] bytes, bool sharedDependency = false)
        {
            string dependencies = sharedDependency ? """[{"project_id":"shared-dependency"}]""" : "[]";
            return Json($$$"""
                [{"files":[{"filename":"{{{filename}}}","url":"https://fixture.invalid/{{{downloadName}}}","size":{{{bytes.Length}}},"hashes":{"sha1":"{{{Convert.ToHexStringLower(SHA1.HashData(bytes))}}}"}}],"dependencies":{{{dependencies}}}}]
                """);
        }

        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
#else
    public static Task<int> RunAsync()
        => Task.FromException<int>(new NotSupportedException("Recommended mod fixtures require a Debug build."));
#endif
}
