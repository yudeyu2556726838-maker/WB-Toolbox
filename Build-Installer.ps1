param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '4.0.0-alpha.38'
$releaseRoot = Join-Path (Split-Path -Parent $root) 'release'
$releaseDirectory = Join-Path $releaseRoot "WB-Toolbox-v$version"
$installerScript = Join-Path $root 'Installer\WBToolbox.iss'
$installerPath = Join-Path $releaseRoot "WB-Toolbox-v$version-Setup.exe"

if (-not $SkipBuild) {
    & (Join-Path $root 'Build-Release.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "发布包构建失败，退出码：$LASTEXITCODE"
    }
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw '未找到 Inno Setup 6。请先运行：winget install --id JRSoftware.InnoSetup --exact --source winget'
}

& $compiler "/DMyAppSource=$releaseDirectory" "/DMyOutputDir=$releaseRoot" $installerScript
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
Write-Output "Installer: $installerPath"
Write-Output "Installer SHA256: $installerHash"
