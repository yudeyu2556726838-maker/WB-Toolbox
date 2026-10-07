param(
    [switch]$Interaction,
    [switch]$IdlePerformance,
    [string]$VideoPath
)

$ErrorActionPreference = 'Stop'

if ($Interaction -and $IdlePerformance) {
    throw 'Interaction and IdlePerformance cannot be used together.'
}

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpfFramework = Join-Path $framework 'WPF'
$root = $PSScriptRoot
$outputRoot = Join-Path (Split-Path -Parent $root) 'work\visual-smoke'
$runId = '{0}-{1}-{2}' -f (
    (Get-Date -Format 'yyyyMMdd-HHmmssfff'),
    $PID,
    ([Guid]::NewGuid().ToString('N').Substring(0, 8)))
$outputDirectory = Join-Path $outputRoot $runId
$outputName = 'WBToolbox.VisualSmoke.exe'
if ($Interaction) {
    $outputName = 'WBToolbox.MiniInteractionSmoke.exe'
}
elseif ($IdlePerformance) {
    $outputName = 'WBToolbox.IdlePerformanceSmoke.exe'
}
$outputFile = Join-Path $outputDirectory $outputName

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$sources = @(
    Join-Path $root 'Tests\VisualSmoke.cs'
    Join-Path $root 'Tests\MiniInteractionSmoke.cs'
    Join-Path $root 'Tests\IdlePerformanceSmoke.cs'
    Join-Path $root 'Diagnostics\CrashLogger.cs'
    Join-Path $root 'Core\CurrencyModels.cs'
    Join-Path $root 'Core\NumericInput.cs'
    Join-Path $root 'Core\PricingEngine.cs'
    Join-Path $root 'Core\CommissionCatalog.cs'
    Join-Path $root 'Services\ExchangeRateService.cs'
    Join-Path $root 'Services\SettingsStore.cs'
    Join-Path $root 'Services\TranslationService.cs'
    Join-Path $root 'UI\NativeWindowEffects.cs'
    Join-Path $root 'UI\EdgeDockController.cs'
    Join-Path $root 'UI\WindowRegionClip.cs'
    Join-Path $root 'UI\EmbeddedAssets.cs'
    Join-Path $root 'UI\VideoBackgroundPresenter.cs'
    Join-Path $root 'UI\VideoCropWindow.cs'
    Join-Path $root 'UI\Motion.cs'
    Join-Path $root 'UI\UiFactory.cs'
    Join-Path $root 'UI\MiniWindow.cs'
    Join-Path $root 'UI\MainWindow.cs'
    Join-Path $root 'UI\MainWindow.Background.cs'
    Join-Path $root 'UI\MainWindow.Responsive.cs'
    Join-Path $root 'UI\MainWindow.Transition.cs'
    Join-Path $root 'UI\MainWindow.Currency.cs'
    Join-Path $root 'UI\MainWindow.Translation.cs'
    Join-Path $root 'UI\MainWindow.Pricing.cs'
)

$arguments = @(
    '/nologo'
    '/target:exe'
    '/platform:anycpu'
    '/optimize+'
    "/out:$outputFile"
    $(if ($Interaction) { '/main:WBToolbox.Native.Tests.MiniInteractionSmoke' } elseif ($IdlePerformance) { '/main:WBToolbox.Native.Tests.IdlePerformanceSmoke' } else { '/main:WBToolbox.Native.Tests.VisualSmoke' })
    "/resource:$(Join-Path $root 'Assets\sky-girl.jpg'),WBToolbox.Native.Assets.sky-girl.jpg"
    "/resource:$(Join-Path $root 'Assets\icon128.png'),WBToolbox.Native.Assets.icon128.png"
    "/resource:$(Join-Path $root 'Assets\icon-mini.png'),WBToolbox.Native.Assets.icon-mini.png"
    "/resource:$(Join-Path $root 'Assets\category-commissions.tsv'),WBToolbox.Native.Assets.category-commissions.tsv"
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    "/reference:$(Join-Path $framework 'System.Net.Http.dll')"
    "/reference:$(Join-Path $framework 'System.Web.Extensions.dll')"
    "/reference:$(Join-Path $framework 'System.Xml.dll')"
    "/reference:$(Join-Path $framework 'System.Xaml.dll')"
    "/reference:$(Join-Path $wpfFramework 'WindowsBase.dll')"
    "/reference:$(Join-Path $wpfFramework 'PresentationCore.dll')"
    "/reference:$(Join-Path $wpfFramework 'PresentationFramework.dll')"
) + $sources

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Interaction) {
    Write-Output "Smoke executable: $outputFile"
    return
}

if ($IdlePerformance) {
    & $outputFile $outputDirectory
}
else {
    if ([string]::IsNullOrWhiteSpace($VideoPath)) {
        & $outputFile $outputDirectory
    }
    else {
        & $outputFile $outputDirectory $VideoPath
    }
}
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($IdlePerformance) {
    Write-Output "Idle performance smoke: $outputDirectory"
}
else {
    Write-Output "Visual smoke: $outputDirectory"
}
