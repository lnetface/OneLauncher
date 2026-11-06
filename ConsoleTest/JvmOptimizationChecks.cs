using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using OneLauncher.Core.DefaultComponent.Launcher.Builders;
using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Codes;
using OneLauncher.Views.ViewModels;

internal static class JvmOptimizationChecks
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("JVM check failed: " + name);
        checks++;
    }

    private static void Fails(Action action, string name)
    {
        try { action(); }
        catch (OlanException) { checks++; return; }
        throw new InvalidOperationException("Expected failure: " + name);
    }

    private static async Task FailsAsync(Func<Task> action, string name)
    {
        try { await action(); }
        catch (OlanException) { checks++; return; }
        throw new InvalidOperationException("Expected failure: " + name);
    }

    private static SystemMemoryHelper.MemoryMetrics Memory(double totalGiB, double availableGiB) => new() { TotalMB = totalGiB * 1024, FreeMB = availableGiB * 1024 };
    private static JavaRuntimeInfo Runtime(int major = 21, bool hotspot = true) => new() { ExecutablePath = "fixture-java", MajorVersion = major, Is64Bit = true, IsHotSpot = hotspot, VmName = hotspot ? "OpenJDK 64-Bit Server VM" : "Eclipse OpenJ9 VM" };

    private static Task<JvmArgumentResult> Build(JvmArguments? config, int major = 21, params JvmArgumentSource[] sources)
        => JvmOptimizationPolicy.BuildAsync(config, Runtime(major), Memory(16, 10), true, 100, sources,
            supportsArguments: (_, _, _) => Task.FromResult(true));

    public static async Task<int> RunAsync(string[] arguments)
    {
        string workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
        string parent = Path.Combine(workspace, "artifacts", "jvm-config-20261106");
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var examples = new[] { (8d,4d,false,0,OptimizationMode.Standard,2048), (8d,2d,true,100,OptimizationMode.Aggressive,896),
                (16d,10d,true,100,OptimizationMode.Standard,3840), (32d,24d,true,300,OptimizationMode.Aggressive,6784), (64d,56d,true,2000,OptimizationMode.Aggressive,16384) };
            foreach (var (total, available, modded, mods, mode, expected) in examples)
            {
                var heap = JvmOptimizationPolicy.CalculateHeap(mode, Memory(total, available), true, modded, mods);
                Check(heap.MaximumMiB == expected && heap.InitialMiB == 512, $"example {total}/{available}");
            }
            foreach (double total in new[] { 4d, 8, 16, 32, 64 })
            foreach (var mode in new[] { OptimizationMode.Conservative, OptimizationMode.Standard, OptimizationMode.Aggressive })
            {
                int previous = 0;
                for (double available = 2; available <= total; available += 1)
                {
                    if (available * 1024 - Math.Clamp(total * 1024 * .1, 512, 2048) < 640) continue;
                    var heap = JvmOptimizationPolicy.CalculateHeap(mode, Memory(total, available), true, true, 2000);
                    Check(heap.MaximumMiB >= previous && heap.MaximumMiB <= heap.BudgetMiB && heap.MaximumMiB <= 16384 && heap.InitialMiB <= heap.MaximumMiB, "budget monotonic and bounded");
                    previous = heap.MaximumMiB!.Value;
                }
            }
            Check(JvmOptimizationPolicy.CalculateHeap(OptimizationMode.Aggressive, Memory(64,56), false, true,2000).MaximumMiB == 768, "32-bit cap");
            Fails(() => JvmOptimizationPolicy.CalculateHeap(OptimizationMode.Standard, Memory(8,.5),true,false,0), "low memory");
            Check(JvmOptimizationPolicy.CalculateHeap(OptimizationMode.Standard, null,true,false,0).MaximumMiB == null, "unknown memory");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
                Check(!SystemMemoryHelper.IsValid(new() { TotalMB = 4096, FreeMB = invalid }), "invalid metric");
            Check(!SystemMemoryHelper.IsValid(new() { TotalMB = 4096, FreeMB = 8192 }), "available > total");
            var macMemory = SystemMemoryHelper.ParseMacMemory("8589934592", "Mach Virtual Memory Statistics: (page size of 16384 bytes)\nPages free: 65536.\nPages inactive: 65536.\nPages speculative: 0.\nPages occupied by compressor: 400000.");
            Check(macMemory?.FreeMB == 2048 && macMemory?.TotalMB == 8192, "mac page size and compressor excluded");
            Check(JvmOptimizationPolicy.CalculateHeap(OptimizationMode.Standard, Memory(16,10),true,false,0,3000L*1048576).MaximumMiB >= 3000, "Xms only fits");
            Fails(() => JvmOptimizationPolicy.CalculateHeap(OptimizationMode.Standard,Memory(8,2),true,false,0,2048L*1048576), "Xms budget");
            Fails(() => JvmOptimizationPolicy.CalculateHeap(OptimizationMode.Standard,Memory(64,56),true,false,0,long.MaxValue), "huge Xms no arithmetic overflow");
            Check(JvmOptimizationPolicy.ParseHeapBytes("-XX:MaxHeapSize=2g") == 2147483648, "heap alias");
            foreach (string invalid in new[] { "-Xmx-1m", "-Xmx0m", "-Xmx1.5g", "-Xmx2q", "-Xmx999999999999999g" }) Fails(() => JvmOptimizationPolicy.ParseHeapBytes(invalid), "invalid heap");
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            foreach (string file in new[] { "a.jar", "b.JAR", "c.zip", "d.litemod", "e.jar.disabled", "readme.txt" }) File.WriteAllText(Path.Combine(root,"mods",file), "");
            File.WriteAllText(Path.Combine(root,"server.jar"), "");
            Check(JvmOptimizationPolicy.CountMods(root) == 4 && JvmOptimizationPolicy.CountMods(Path.Combine(root,"missing")) == 0, "mods directory and enabled extensions");
            var preset = JvmArguments.CreateFromMode(OptimizationMode.Aggressive);
            Check(preset.MaxHeapSize == 0 && preset.InitialHeapSize == 0 && !preset.AlwaysPreTouch && !preset.UseAikarFlags && preset.G1HeapRegionSize == 0 && preset.MaxGCPauseMillis == 200, "lean preset");
            var result = await Build(preset,21,new JvmArgumentSource(10,["-Xmx2g", "-XX:+UseG1GC", "-XX:G1HeapRegionSize=32m", "-cp \"C:\\game path\\libs.jar\"", "--add-opens=java.base/java.lang=ALL-UNNAMED"]),
                new JvmArgumentSource(40,["-Xmx20g", "-Xms1g", "-XX:+UseZGC", "-Dsample=\"hello world\""]),new JvmArgumentSource(40,["-javaagent:\"C:\\auth lib.jar\""],true));
            Check(result.Arguments.Count(arg => arg.StartsWith("-Xmx")) == 1 && result.Arguments.Contains("-Xmx20g") && result.Arguments.Contains("-Xms1g"), "user heaps win over automatic cap");
            Check(result.Arguments.Contains("-XX:+UseZGC") && !result.Arguments.Any(arg => arg.Contains("G1")), "GC family override");
            Check(result.Arguments.Contains("\"C:\\game path\\libs.jar\"") && result.Arguments.Contains("-Dsample=\"hello world\"") && result.CommandArguments.Single().StartsWith("-javaagent:"), "unrelated quotes and command location preserved");
            var zgc = JvmArguments.CreateFromMode(); zgc.UseG1GC = false; zgc.UseZGC = true;
            foreach (int major in new[] { 21,22,23,24,25 })
            {
                result = await Build(zgc,major);
                Check(result.Arguments.Contains("-XX:+ZGenerational") == (major is 21 or 22), "generational boundary " + major);
            }
            await FailsAsync(() => Build(zgc,8), "Java8 ZGC");
            var conflict = JvmArguments.CreateFromMode(); conflict.UseZGC = true;
            await FailsAsync(() => Build(conflict), "structured GC conflict");
            await FailsAsync(() => Build(preset,21,new JvmArgumentSource(40,["-XX:+UseG1GC", "-XX:+UseZGC"])), "raw GC conflict");
            result = await Build(preset,21,new JvmArgumentSource(40,["-XX:+UseG1GC", "-XX:-UseG1GC", "-XX:+UseZGC"]));
            Check(result.Arguments.Contains("-XX:-UseG1GC") && result.Arguments.Contains("-XX:+UseZGC"), "last GC enable state");
            result = await Build(preset,21,new JvmArgumentSource(40,["-Xmx1g", "-Xmx3g"]),new JvmArgumentSource(40,["-Xmx4g"],true));
            Check(result.CommandArguments.Contains("-Xmx4g") && !result.Arguments.Any(arg => arg.StartsWith("-Xmx")), "last heap and command-only location");
            result = await Build(preset,21,new JvmArgumentSource(40,["-XX:+UseG1GC"]),new JvmArgumentSource(40,["-XX:-UseG1GC", "-XX:+UseZGC"],true));
            Check(result.CommandArguments.Contains("-XX:+UseZGC") && !result.Arguments.Contains("-XX:+UseG1GC"), "GC command-only location");
            await FailsAsync(() => Build(preset,21,new JvmArgumentSource(40,["-Xms4g", "-Xmx2g"])), "heap conflict");
            preset.MaxHeapSize = -1;
            result = await Build(preset,21,new JvmArgumentSource(40,["-Xmx4g"]));
            Check(result.Arguments.Contains("-Xmx4g"), "overridden invalid setting ignored");
            result = await Build(JvmArguments.CreateFromMode(OptimizationMode.None));
            Check(result.Arguments.Count == 0, "None no optimization");
            result = await JvmOptimizationPolicy.BuildAsync(JvmArguments.CreateFromMode(),Runtime(21,false),Memory(16,10),false,0,[],supportsArguments: (_,_,_)=>Task.FromResult(true));
            Check(!result.Arguments.Any(arg => arg.StartsWith("-XX:")), "OpenJ9 standard heaps only");
            var runtime = JavaRuntimeInspector.Parse("fixture", "JAVA_VERSION=\"1.8.0_202\"\nOS_ARCH=\"x86\"", "java.vm.name = OpenJDK Client VM\nsun.arch.data.model = 32");
            Check(runtime.MajorVersion == 8 && runtime.UpdateVersion == 202 && runtime.Is64Bit == false && runtime.IsHotSpot, "Java8 parser");
            runtime = JavaRuntimeInspector.Parse("fixture", "JAVA_VERSION=\"25.0.1\"\nOS_ARCH=\"aarch64\"", "java.vm.name = Eclipse OpenJ9 VM");
            Check(runtime.MajorVersion == 25 && runtime.Is64Bit == true && !runtime.IsHotSpot, "OpenJ9 parser");
            try { await JavaRuntimeInspector.InspectAsync("fixture", new CancellationToken(true)); throw new InvalidOperationException("cancel not observed"); }
            catch (OperationCanceledException) { checks++; }

            if (OperatingSystem.IsWindows())
            {
                string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
                if (File.Exists(shell))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var timed = await JavaRuntimeInspector.RunProbeAsync(shell,["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], CancellationToken.None);
                    Check(timed.Diagnostic != null && watch.Elapsed < TimeSpan.FromSeconds(8), "probe timeout kills process");
                    using var cancelProbe = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                    try { await JavaRuntimeInspector.RunProbeAsync(shell,["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],cancelProbe.Token); throw new InvalidOperationException("probe cancel not observed"); }
                    catch (OperationCanceledException) { checks++; }
                    var outputProbe = await JavaRuntimeInspector.RunProbeAsync(shell,["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write('stdout'); [Console]::Error.Write('stderr')"],CancellationToken.None);
                    Check(outputProbe.ExitCode == 0 && outputProbe.Output.Contains("stdout") && outputProbe.Output.Contains("stderr"), "probe drains both streams");
                }
            }

            // Exercise the UI save transaction without initializing accounts or the application.
            var settings = new DefaultConfig { MinecraftJvmArguments = new() { MaxHeapSize = 9000, AlwaysPreTouch = true, UseAikarFlags = true } };
            string json = Path.Combine(root,"config.json");
            Func<Task> save = () => File.WriteAllTextAsync(json, JsonSerializer.Serialize(settings, OneLauncherAppConfigsJsonContext.Default.DefaultConfig));
            await SettingsPageViewModel.ReplaceJvmPresetAsync(settings,OptimizationMode.Conservative,save);
            var written = JsonSerializer.Deserialize(await File.ReadAllTextAsync(json), OneLauncherAppConfigsJsonContext.Default.DefaultConfig)!;
            Check(written.MinecraftJvmArguments.mode == OptimizationMode.Conservative && written.MinecraftJvmArguments.MaxHeapSize == 0 && !written.MinecraftJvmArguments.UseAikarFlags && !written.MinecraftJvmArguments.AlwaysPreTouch, "UI full JSON reset");
            // Construct the real settings ViewModel with isolated managers; initialization must not rewrite JVM settings.
            string originalBackgroundPath = HomeBackgroundStore.CustomDataPath;
            HomeBackgroundStore.CustomDataPath = Path.Combine(root,"background");
            try
            {
                var manager = new DBManager(Path.Combine(root,"settings.json"));
                var custom = new JvmArguments { mode = OptimizationMode.Aggressive, MaxHeapSize = 9000, AlwaysPreTouch = true };
                await manager.InitializeAsync(new AppConfig { DefaultSettings = new DefaultConfig { MinecraftJvmArguments = custom } });
                var games = new GameDataManager(Path.Combine(root,"games.json"));
                await games.InitializeAsync();
                var isolatedCore = new UnitedCore
                {
                    GameDataManager = games,
                    TaskManager = null!, VersionInstallerService = null!, VersionListManager = null!,
                    ResourceInstallerService = null!, GameLaunchService = null!, MojangVersionList = [],
                    AccountManager = null!, MsalAuthenticator = null!, JavaManager = null!
                };
                var originalJvmJson = JsonSerializer.Serialize(custom, OneLauncherAppConfigsJsonContext.Default.JvmArguments);
                var previousContext = SynchronizationContext.Current;
                Avalonia.AppBuilder.Configure<OneLauncher.App>().UseSkia()
                    .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                _ = new SettingsPageViewModel(manager,isolatedCore);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                // Existing non-JVM settings callbacks may save the full document; pump those before cleanup.
                var until = DateTime.UtcNow.AddSeconds(3);
                while (Directory.EnumerateFiles(root,"settings.json.*.tmp").Any() && DateTime.UtcNow < until)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(10);
                }
                SynchronizationContext.SetSynchronizationContext(previousContext);
                var savedConfig = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(root,"settings.json")),OneLauncherAppConfigsJsonContext.Default.AppConfig)!;
                Check(ReferenceEquals(custom,manager.Data.DefaultSettings.MinecraftJvmArguments) && originalJvmJson == JsonSerializer.Serialize(savedConfig.DefaultSettings.MinecraftJvmArguments,OneLauncherAppConfigsJsonContext.Default.JvmArguments), "settings construction preserves JVM config");
            }
            finally { HomeBackgroundStore.CustomDataPath = originalBackgroundPath; }
            var previousSettings = settings.MinecraftJvmArguments;
            Func<Task> failSave = () => Task.FromException(new IOException("fixture failure"));
            try { await SettingsPageViewModel.ReplaceJvmPresetAsync(settings,OptimizationMode.Aggressive,failSave); throw new InvalidOperationException("save should fail"); }
            catch (IOException) { Check(ReferenceEquals(previousSettings,settings.MinecraftJvmArguments), "save rollback"); }

            for (int javaIndex = 0; javaIndex + 1 < arguments.Length; javaIndex++)
            {
                if (arguments[javaIndex] != "--java") continue;
                runtime = await JavaRuntimeInspector.InspectAsync(arguments[javaIndex+1]);
                Check(runtime.IsKnown && runtime.IsHotSpot, "real Java inspection");
                var smoke = JvmArguments.CreateFromMode(); smoke.MaxHeapSize = 64; smoke.InitialHeapSize = 16;
                _ = await JvmOptimizationPolicy.BuildAsync(smoke,runtime,null,false,0,[]);
                smoke.UseAikarFlags = true;
                _ = await JvmOptimizationPolicy.BuildAsync(smoke,runtime,null,false,0,[]);
                if (runtime.MajorVersion >= 17)
                {
                    smoke.UseAikarFlags = false; smoke.UseG1GC = false; smoke.UseZGC = true;
                    var zResult = await JvmOptimizationPolicy.BuildAsync(smoke,runtime,null,false,0,[]);
                    Check(zResult.Arguments.Contains("-XX:+ZGenerational") == (runtime.MajorVersion is 21 or 22), "real ZGC boundary");
                }
                Console.WriteLine($"Java smoke passed: {runtime.MajorVersion}, {runtime.VmName}");
            }
            Console.WriteLine($"JVM checks passed: {checks}");
            return 0;
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup path");
            Directory.Delete(root,true);
            if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
    }
}
