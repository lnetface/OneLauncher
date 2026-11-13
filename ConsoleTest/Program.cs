using OneLauncher.Core.Global;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OneLauncher.Core.Global.Updater;

var services = await Init.Initialize();
using var serviceProvider = services.BuildServiceProvider();
var unitedCore = serviceProvider.GetRequiredService<UnitedCore>();

await AutoUpdate.Update();