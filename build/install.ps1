# OneLauncher Windows 安装脚本
param(
    [Parameter(Position = 0)]
    [ValidateSet("update")]
    [string]$Mode,

    [Parameter(Position = 1)]
    [string]$OlanPath
)

$ErrorActionPreference = "Stop"
$OutputEncoding = [System.Text.Encoding]::UTF8

$repository = "lnetface/OneLauncher"
$assetName = "OneLauncher.Desktop.exe"
$softwareFile = Join-Path (Join-Path $env:USERPROFILE "OneLauncher") $assetName
if ($OlanPath) {
    if (Test-Path -LiteralPath $OlanPath -PathType Container) {
        $OlanPath = Join-Path $OlanPath $assetName
    }
    $softwareFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OlanPath)
}
$logFile = Join-Path $env:TEMP "OneLauncherInstall.log"

# 兼容 Windows PowerShell 5.1 访问 GitHub
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Test-Winget {
    return $null -ne (Get-Command winget -ErrorAction SilentlyContinue)
}

function Test-DotNetDesktopRuntime {
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $false
    }

    $runtimes = & dotnet --list-runtimes 2>$null
    return ($runtimes | Where-Object {
        $_ -match '^Microsoft\.WindowsDesktop\.App\s+10\.'
    }).Count -gt 0
}

function Test-Java {
    return $null -ne (Get-Command java -ErrorAction SilentlyContinue)
}

function Get-OneLauncherAsset {
    $headers = @{
        "User-Agent" = "OneLauncher-Installer"
        "Accept"     = "application/vnd.github+json"
    }

    $apiUrl = "https://api.github.com/repos/$repository/releases?per_page=30"

    Write-Host "正在查询 OneLauncher 最新版本..."

    $releases = Invoke-RestMethod -Uri $apiUrl -Headers $headers

    foreach ($release in $releases) {
        if ($release.draft -or $release.prerelease) {
            continue
        }

        $asset = $release.assets |
            Where-Object { $_.name -eq $assetName } |
            Select-Object -First 1

        if ($null -ne $asset) {
            return $asset
        }
    }

    throw "没有找到包含 $assetName 的公开 Release。"
}

function Stop-OneLauncher {
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($softwareFile)
    foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
        if ($process.Path -and [string]::Equals($process.Path, $softwareFile, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "正在结束 OneLauncher 进程（$($process.Id)）..."
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
}

function Download-OneLauncher {
    Stop-OneLauncher
    $asset = Get-OneLauncherAsset
    $size = [Math]::Round($asset.size / 1MB, 1)

    Write-Host "正在下载 $($asset.name)（$size MB）..."

    $installDirectory = Split-Path -Parent $softwareFile
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    $downloadFile = Join-Path $installDirectory ([Guid]::NewGuid().ToString() + ".download")
    $response = $null
    $inputStream = $null
    $outputStream = $null
    try {
        $request = [System.Net.HttpWebRequest]::Create($asset.browser_download_url)
        $request.UserAgent = "OneLauncher-Installer"
        $response = $request.GetResponse()
        $inputStream = $response.GetResponseStream()
        $outputStream = [System.IO.File]::Create($downloadFile)
        $buffer = New-Object byte[] 65536
        $downloaded = 0L
        while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $outputStream.Write($buffer, 0, $read)
            $downloaded += $read
            $percent = if ($asset.size -gt 0) { [Math]::Min(100, [int]($downloaded * 100 / $asset.size)) } else { 0 }
            Write-Progress -Activity "正在下载 OneLauncher" -Status "$([Math]::Round($downloaded / 1MB, 1)) / $size MB" -PercentComplete $percent
        }
        $outputStream.Dispose()
        $outputStream = $null
        if ($downloaded -le 0 -or $downloaded -ne $asset.size) {
            throw "下载文件大小不符，旧版文件未替换。"
        }
        Stop-OneLauncher
        Move-Item -LiteralPath $downloadFile -Destination $softwareFile -Force
    }
    finally {
        if ($outputStream) { $outputStream.Dispose() }
        if ($inputStream) { $inputStream.Dispose() }
        if ($response) { $response.Dispose() }
        Write-Progress -Activity "正在下载 OneLauncher" -Completed
        if (Test-Path -LiteralPath $downloadFile) {
            Remove-Item -LiteralPath $downloadFile -Force
        }
    }

    Write-Host "OneLauncher 下载成功：" -ForegroundColor Green
    Write-Host $softwareFile -ForegroundColor Cyan
}

function Install-DotNet {
    if (Test-DotNetDesktopRuntime) {
        Write-Host ".NET 10 Desktop Runtime 已安装。" -ForegroundColor Green
        return
    }

    if (-not (Test-Winget)) {
        throw "未找到 winget。请先安装 Windows 应用安装程序：https://aka.ms/winget-install"
    }

    Write-Host "正在安装 .NET 10 Desktop Runtime..."

    winget install `
        --exact `
        --id Microsoft.DotNet.DesktopRuntime.10 `
        --silent `
        --accept-source-agreements `
        --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "winget 安装 .NET 10 Desktop Runtime 失败，错误码：$LASTEXITCODE"
    }

    if (-not (Test-DotNetDesktopRuntime)) {
        throw "安装完成后仍未检测到 .NET 10 Desktop Runtime。"
    }

    Write-Host ".NET 10 Desktop Runtime 安装成功。" -ForegroundColor Green
}

function Install-Java {
    if (Test-Java) {
        Write-Host "Java 已安装，跳过安装。" -ForegroundColor Green
        return
    }

    if (-not (Test-Winget)) {
        throw "未找到 winget，无法自动安装 Java。"
    }

    Write-Host "正在安装 Temurin JDK 21..."

    winget install `
        --exact `
        --id EclipseAdoptium.Temurin.21.JDK `
        --silent `
        --accept-source-agreements `
        --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "winget 安装 Java 失败，错误码：$LASTEXITCODE"
    }

    Write-Host "Temurin JDK 21 安装成功。" -ForegroundColor Green
}

Start-Transcript -Path $logFile -Append | Out-Null

try {
    if ($OlanPath -and $Mode -ne "update") {
        throw "旧版路径仅支持与 update 参数一起使用。"
    }
    if ($Mode -eq "update") {
        Download-OneLauncher
        Write-Host "OneLauncher 更新完成。" -ForegroundColor Green
        return
    }

    if (Test-Path -LiteralPath $softwareFile -PathType Leaf) {
        Write-Host "已找到 OneLauncher：$softwareFile"
        Write-Host "1. 打开 OneLauncher"
        Write-Host "2. 更新 OneLauncher"
        $existingChoice = Read-Host "请选择一个选项（1-2）"
        if ($existingChoice -eq "1") {
            Start-Process -FilePath $softwareFile -WorkingDirectory (Split-Path -Parent $softwareFile)
            return
        }
        if ($existingChoice -ne "2") {
            throw "输入无效，请输入 1 或 2。"
        }
        Download-OneLauncher
        Write-Host "OneLauncher 更新完成。" -ForegroundColor Green
        return
    }

    Write-Host ""
    Write-Host "=== OneLauncher 安装程序 ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. 仅下载 OneLauncher"
    Write-Host "2. 下载 OneLauncher，并安装 .NET 10 Desktop Runtime"
    Write-Host "3. 下载 OneLauncher，并安装 .NET 10 和 Java"
    Write-Host ""

    $choice = Read-Host "请选择一个选项（1-3）"

    if ($choice -notmatch '^[1-3]$') {
        throw "输入无效，请输入 1、2 或 3。"
    }

    Download-OneLauncher

    if ($choice -in @("2", "3")) {
        Install-DotNet
    }

    if ($choice -eq "3") {
        Install-Java
    }

    Write-Host ""
    Write-Host "OneLauncher 安装完成。" -ForegroundColor Green

    $runChoice = Read-Host "是否立即运行 OneLauncher？（Y/N）"

    if ($runChoice -match '^[Yy]$') {
        Start-Process -FilePath $softwareFile -WorkingDirectory (Split-Path -Parent $softwareFile)
    }
}
catch {
    Write-Host ""
    Write-Host "安装失败：$($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
