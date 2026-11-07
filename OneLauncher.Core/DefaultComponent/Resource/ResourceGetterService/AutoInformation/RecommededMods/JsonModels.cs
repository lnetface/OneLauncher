using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService.AutoInformation.RecommededMods;

public record struct RecommendedMod(
    string ModId,ModLimited Limited
    );
public record struct ModLimited(
    ModType modLoader,
    string minecraftVersion,
    Than operation // 表示模组支持的最大或最小MC版本
    );