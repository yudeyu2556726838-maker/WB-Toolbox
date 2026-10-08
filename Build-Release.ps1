param(
    [switch]$RunLiveIntegration
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '4.0.0-alpha.40'
$executableName = "WB-Toolbox-v$version.exe"
$releaseRoot = Join-Path (Split-Path -Parent $root) 'release'
$releaseDirectory = Join-Path $releaseRoot "WB-Toolbox-v$version"
$archivePath = "$releaseDirectory.zip"

& (Join-Path $root 'Test-Native.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "核心测试失败，退出码：$LASTEXITCODE"
}

& (Join-Path $root 'Test-Translation.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "翻译回归测试失败，退出码：$LASTEXITCODE"
}

& (Join-Path $root 'Build-VisualSmoke.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "界面与转场回归测试失败，退出码：$LASTEXITCODE"
}

& (Join-Path $root 'Build-VisualSmoke.ps1') -IdlePerformance
if ($LASTEXITCODE -ne 0) {
    throw "空闲性能测试失败，退出码：$LASTEXITCODE"
}

& (Join-Path $root 'Build-Native.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "应用构建失败，退出码：$LASTEXITCODE"
}

$builtExecutable = Join-Path $root "dist\$executableName"
$processorArchitecture = [System.Reflection.AssemblyName]::GetAssemblyName($builtExecutable).ProcessorArchitecture.ToString()
if ($processorArchitecture -notin @('None', 'MSIL')) {
    throw "发布程序不是 AnyCPU 架构：$processorArchitecture"
}

if ($RunLiveIntegration) {
    & (Join-Path $root 'Test-Integration.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "在线集成测试失败，退出码：$LASTEXITCODE"
    }
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-Item -LiteralPath $builtExecutable -Destination (Join-Path $releaseDirectory $executableName) -Force
Copy-Item -LiteralPath (Join-Path $root "dist\$executableName.config") -Destination (Join-Path $releaseDirectory "$executableName.config") -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $releaseDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $releaseDirectory 'THIRD-PARTY-NOTICES.txt') -Force

$executableHash = (Get-FileHash -LiteralPath (Join-Path $releaseDirectory $executableName) -Algorithm SHA256).Hash
@(
    "SHA256  $executableName"
    $executableHash
) | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding UTF8

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash

Write-Output "Release: $releaseDirectory"
Write-Output "Archive: $archivePath"
Write-Output "Archive SHA256: $archiveHash"
