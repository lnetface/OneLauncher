using System.Text.Json;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;

internal static class JsonAotChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("JSON AOT check failed: " + message);
            checks++;
        }

        void CheckMods(IReadOnlyList<RecommendedMod> expected, IReadOnlyList<RecommendedMod> actual, string label)
        {
            Check(actual.Count == expected.Count, label + " preserves list length");
            for (int i = 0; i < expected.Count; i++)
            {
                var a = expected[i];
                var b = actual[i];
                string item = $"{label}[{i}]";
                Check(a.ModId == b.ModId, item + " preserves project ID/slug and order");
                Check(a.Limited.minecraftVersion == b.Limited.minecraftVersion, item + " preserves version");
                Check(a.Limited.operation == b.Limited.operation, item + " preserves comparison");
                Check(a.Limited.modLoader.IsFabric == b.Limited.modLoader.IsFabric, item + " preserves Fabric");
                Check(a.Limited.modLoader.IsQuilt == b.Limited.modLoader.IsQuilt, item + " preserves Quilt");
                Check(a.Limited.modLoader.IsForge == b.Limited.modLoader.IsForge, item + " preserves Forge");
                Check(a.Limited.modLoader.IsNeoForge == b.Limited.modLoader.IsNeoForge, item + " preserves NeoForge");
            }
        }

        Check(!JsonSerializer.IsReflectionEnabledByDefault, "reflection JSON must be disabled for this run");
        var context = OneLauncherAppConfigsJsonContext.Default;
        foreach (var type in new[] { typeof(AppConfig), typeof(DefaultConfig), typeof(RecommendedMod),
                     typeof(ModLimited), typeof(Than), typeof(List<RecommendedMod>), typeof(ModType) })
            Check(context.GetTypeInfo(type) is not null, "AppConfig source-generated metadata includes " + type.Name);

        string workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
        Check(File.Exists(Path.Combine(workspace, "OneLauncher.sln")), "fixtures run from the project workspace");
        string parent = Path.Combine(workspace, "artifacts");
        bool createdParent = !Directory.Exists(parent);
        string fixture = Path.GetFullPath(Path.Combine(parent, "json-aot-" + Guid.NewGuid().ToString("N")));
        Check(fixture.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "fixture stays within workspace");
        Directory.CreateDirectory(fixture);
        try
        {
            string configPath = Path.Combine(fixture, "config.json");
            var manager = new DBManager(configPath);
            await manager.InitializeAsync();
            var defaults = manager.Data.DefaultSettings.RecommandedMods.ToList();
            Check(defaults.Count > 0, "current default recommended list is populated");
            await manager.Save();
            var reloaded = new DBManager(configPath);
            await reloaded.InitializeAsync();
            CheckMods(defaults, reloaded.Data.DefaultSettings.RecommandedMods, "default DBManager round trip");

            List<RecommendedMod> custom =
            [
                new("aBc123XY", new ModLimited(new ModType { IsFabric = true, IsForge = true },
                    "1.20.1", Than.GreaterThan)),
                new("my-custom-project", new ModLimited(new ModType { IsQuilt = true, IsNeoForge = true },
                    "26.1", Than.LessThan))
            ];
            manager.Data.DefaultSettings.RecommandedMods = custom;
            await manager.Save();
            reloaded = new DBManager(configPath);
            await reloaded.InitializeAsync();
            CheckMods(custom, reloaded.Data.DefaultSettings.RecommandedMods, "custom DBManager round trip");
            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath)))
            {
                var list = document.RootElement.GetProperty("DefaultSettings").GetProperty("RecommandedMods");
                for (int i = 0; i < custom.Count; i++)
                {
                    Check(list[i].GetProperty("ModId").GetString() == custom[i].ModId, "custom ModId JSON format");
                    var operation = list[i].GetProperty("Limited").GetProperty("operation");
                    Check(operation.ValueKind == JsonValueKind.Number && operation.GetInt32() == (int)custom[i].Limited.operation,
                        "Than remains a numeric JSON enum");
                }
            }

            // A pre-update list uses slugs and omitted false flags; loading must preserve it instead of replacing it.
            const string legacyJson = """
                {"DefaultSettings":{"RecommandedMods":[
                  {"ModId":"sodium","Limited":{"modLoader":{"IsFabric":true,"IsQuilt":true},"minecraftVersion":"1.16.5","operation":0}},
                  {"ModId":"jei","Limited":{"modLoader":{"IsForge":true,"IsNeoForge":true},"minecraftVersion":"1.16.5","operation":0}}
                ]}}
                """;
            List<RecommendedMod> legacy =
            [
                new("sodium", new ModLimited(new ModType { IsFabric = true, IsQuilt = true }, "1.16.5", Than.GreaterThan)),
                new("jei", new ModLimited(new ModType { IsForge = true, IsNeoForge = true }, "1.16.5", Than.GreaterThan))
            ];
            string legacyPath = Path.Combine(fixture, "legacy.json");
            await File.WriteAllTextAsync(legacyPath, legacyJson);
            var oldManager = new DBManager(legacyPath);
            await oldManager.InitializeAsync();
            CheckMods(legacy, oldManager.Data.DefaultSettings.RecommandedMods, "legacy slug list");
            await oldManager.Save();
            oldManager = new DBManager(legacyPath);
            await oldManager.InitializeAsync();
            CheckMods(legacy, oldManager.Data.DefaultSettings.RecommandedMods, "saved legacy slug list");

            string missingPath = Path.Combine(fixture, "missing.json");
            await File.WriteAllTextAsync(missingPath, """{"DefaultSettings":{"CrafatarUrl":"https://fixture.invalid/"}}""");
            var missing = new DBManager(missingPath);
            await missing.InitializeAsync();
            CheckMods(defaults, missing.Data.DefaultSettings.RecommandedMods, "missing recommended property defaults");
            Check(missing.Data.DefaultSettings.CrafatarUrl == "https://fixture.invalid/", "old adjacent configuration is preserved");

            Guid accountId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
            var games = new GameDataRoot();
            games.Instances["global"] = new GameData("全局账户", "1.21.1", ModEnum.fabric, null);
            games.Instances["explicit"] = new GameData("独立账户", "1.20.1", ModEnum.forge, accountId);
            string gameJson = JsonSerializer.Serialize(games, GameDataJsonContext.Default.GameDataRoot);
            using (var document = JsonDocument.Parse(gameJson))
            {
                var instances = document.RootElement.GetProperty("instances");
                Check(instances.GetProperty("global").GetProperty("DefaultUserModelID").ValueKind == JsonValueKind.Null,
                    "global account is written as JSON null");
                Check(instances.GetProperty("explicit").GetProperty("DefaultUserModelID").GetGuid() == accountId,
                    "explicit account is written as a GUID");
                Check(!instances.GetProperty("global").TryGetProperty("InstancePath", out _), "runtime instance path is excluded");
            }
            var readGames = JsonSerializer.Deserialize(gameJson, GameDataJsonContext.Default.GameDataRoot)!;
            Check(readGames.Instances["global"].DefaultUserModelID is null, "null account round trip");
            Check(readGames.Instances["explicit"].DefaultUserModelID == accountId, "explicit account round trip");
            Check(readGames.Instances["global"].InstanceId == "global" && readGames.Instances["explicit"].InstanceId == "explicit",
                "instance dictionary IDs are restored by deserialization callback");

            const string modrinthJson = """
                {"name":"fixture-version","version_type":"release",
                 "files":[{"primary":true,"hashes":{"sha1":"0123456789012345678901234567890123456789"},
                   "url":"https://fixture.invalid/mod.jar","filename":"fixture.jar","size":123}],
                 "dependencies":[{"project_id":"dependency-project"}]}
                """;
            var project = JsonSerializer.Deserialize(modrinthJson, ModrinthGetJsonContext.Default.ModrinthProjects)!;
            Check(project.Name == "fixture-version" && project.VersionType == "release", "Modrinth version metadata");
            Check(project.Files.Count == 1 && project.Files[0].Primary && project.Files[0].Size == 123, "Modrinth file metadata");
            Check(project.Files[0].Filename == "fixture.jar" && project.Files[0].Url == "https://fixture.invalid/mod.jar",
                "Modrinth file name and URL");
            Check(project.Files[0].Hashes.Sha1 == "0123456789012345678901234567890123456789", "Modrinth nested hash");
            Check(project.Dependencies.Count == 1 && project.Dependencies[0].ProjectId == "dependency-project", "Modrinth dependency");
            var savedProject = JsonSerializer.Deserialize(
                JsonSerializer.Serialize(project, ModrinthGetJsonContext.Default.ModrinthProjects),
                ModrinthGetJsonContext.Default.ModrinthProjects)!;
            Check(savedProject.Files[0].Hashes.Sha1 == project.Files[0].Hashes.Sha1
                && savedProject.Dependencies[0].ProjectId == project.Dependencies[0].ProjectId, "Modrinth source-generated round trip");
        }
        finally
        {
            // This unique directory was created above and its absolute path was checked against the workspace.
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            if (createdParent && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }

        Check(!Directory.Exists(fixture), "temporary JSON fixtures are removed");
        Console.WriteLine($"PASS JSON AOT: {checks} checks (reflection JSON disabled)");
        return 0;
    }
}
