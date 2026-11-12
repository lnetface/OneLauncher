#Requires -Version 5.1
<#
.SYNOPSIS
清理工作区内的构建与实验产物，保留源码、发布包和工具链。
.EXAMPLE
pwsh -File build/clean.ps1 -WhatIf
.EXAMPLE
pwsh -File build/clean.ps1 -Task json-aot-check
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string[]] $Task = @(),
    [switch] $IncludeReleases,
    [switch] $IncludeToolchains
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = Join-Path $workspace 'build'

function Assert-WorkspacePath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "清理路径越出工作区：$Path"
    }
    # 检查每一级目录，避免通过 junction / symlink 清理外部文件。
    $current = $full
    while ($current -ne $workspace) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "拒绝清理链接目录：$current"
            }
        }
        $current = Split-Path -Parent $current
    }
}

$targets = [Collections.Generic.List[string]]::new()
if ($Task.Count -gt 0) {
    foreach ($name in $Task) {
        if ($name -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]*$' -or $name -in @('releases', 'toolchains')) {
            throw "Task 必须是 build 下的任务目录名，发布包和工具链请使用专用开关：$name"
        }
        $targets.Add((Join-Path $buildRoot $name))
    }
} else {
    foreach ($directory in Get-ChildItem -LiteralPath $buildRoot -Directory -Force) {
        if ($directory.Name -match '-20\d{6}' -or $directory.Name -in @(
            'output', 'json-aot-check', 'jvm-aot-check', 'game-data-ui-check',
            'settings-regression', 'magic-home-test', 'settings-regression-test',
            'quilt-diagnose', 'fg-tests', 'resource-tests')) {
            $targets.Add($directory.FullName)
        }
    }
    # 仅扫描代码目录；避免进入工具链或任务下载目录。
    function Find-BuildOutput([string] $Directory) {
        Assert-WorkspacePath $Directory
        foreach ($child in Get-ChildItem -LiteralPath $Directory -Directory -Force) {
            Assert-WorkspacePath $child.FullName
            if ($child.Name -in @('bin', 'obj', '.avalonia-build-tasks')) {
                $targets.Add($child.FullName)
            } else {
                Find-BuildOutput $child.FullName
            }
        }
    }
    foreach ($directory in Get-ChildItem -LiteralPath $workspace -Directory) {
        if ($directory.Name -eq 'ConsoleTest' -or $directory.Name -eq 'OneLauncher' -or $directory.Name -like 'OneLauncher.*') {
            Find-BuildOutput $directory.FullName
        }
    }
}
if ($IncludeReleases) { $targets.Add((Join-Path $buildRoot 'releases')) }
if ($IncludeToolchains) { $targets.Add((Join-Path $buildRoot 'toolchains')) }

# 删除前完成全部路径与子目录检查，避免发现链接时已清理一半。
function Assert-Tree([string] $Directory) {
    Assert-WorkspacePath $Directory
    foreach ($child in Get-ChildItem -LiteralPath $Directory -Force) {
        Assert-WorkspacePath $child.FullName
        if ($child.PSIsContainer) { Assert-Tree $child.FullName }
    }
}
$existing = @($targets | Select-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
foreach ($target in $existing) { Assert-Tree $target }
foreach ($target in $existing) {
    if ($PSCmdlet.ShouldProcess($target, '删除构建/实验产物')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
