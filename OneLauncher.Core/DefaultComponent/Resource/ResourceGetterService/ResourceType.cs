namespace OneLauncher.Core.DefaultComponent.Resource.ResourceGetterService;

/// <summary>资源检索与获取时使用的资源类别，各平台按自己的分类规则映射。</summary>
public enum ResourceType
{
    Mod,
    ShaderPack,
    Modpack
}

/// <summary>资源所属的平台；新增平台时在此扩展并在 ResourceInstallerService 中补齐分派。</summary>
public enum ResourceSource
{
    Modrinth,
    CurseForge
}
