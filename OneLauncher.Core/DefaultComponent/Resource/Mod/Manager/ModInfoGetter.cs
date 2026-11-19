using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static OneLauncher.Core.Helper.ImportPCL2Version.PclVersionInspector;

namespace OneLauncher.Core.DefaultComponent.Resource.Mod.Manager;

public class ModInfoGetter
{
    public static async Task<ModInfo?> GetModInfoAsync(string filePath)
    {
        try
        {
            await using FileStream modFileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using ZipArchive archive = new(modFileStream, ZipArchiveMode.Read);

            // 1. 尝试作为 Fabric/Quilt 模组处理
            // 使用独立的变量，避免互相干扰
            ZipArchiveEntry? fabricConfig = archive.GetEntry("fabric.mod.json");
            if (fabricConfig != null)
            {
                var result =  await new FabricModInfoHelper()
                    .GetModInfoAsync(filePath, archive, fabricConfig);
                result.mod = ModEnum.fabric;
                return result;
            }

            // 2. 尝试作为 NeoForge/Forge 模组处理
            ZipArchiveEntry? neoforgeConfig = archive.GetEntry("META-INF/neoforge.mods.toml");
            ZipArchiveEntry? forgeConfig = archive.GetEntry("META-INF/mods.toml");
                                       
            if(neoforgeConfig != null)
            {
                var result = await new ForgeSeriesModInfoHelper().GetModInfoAsync(filePath, archive, neoforgeConfig);
                result.mod = ModEnum.neoforge;
                return result;
            }
            if (forgeConfig != null)
            {
                var result = await new ForgeSeriesModInfoHelper().GetModInfoAsync(filePath, archive, forgeConfig);
                result.mod = ModEnum.forge;
                return result;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}