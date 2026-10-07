$ErrorActionPreference = 'Stop'

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpfFramework = Join-Path $framework 'WPF'
$root = $PSScriptRoot
$outputDirectory = Join-Path $root 'dist'
$outputFile = Join-Path $outputDirectory 'WB-Toolbox-v4.0.0-alpha.37.exe'
$icon = Join-Path $root 'Assets\WBToolbox.ico'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "未找到 C# 编译器：$compiler"
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw "未找到应用图标：$icon"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$sources = @(
    Join-Path $root 'Program.cs'
    Join-Path $root 'Properties\AssemblyInfo.cs'
    Join-Path $root 'Diagnostics\CrashLogger.cs'
    Join-Path $root 'Core\CurrencyModels.cs'
    Join-Path $root 'Core\NumericInput.cs'
    Join-Path $root 'Core\PricingEngine.cs'
    Join-Path $root 'Core\CommissionCatalog.cs'
    Join-Path $root 'Infrastructure\SingleInstanceCoordinator.cs'
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
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/debug-'
    "/out:$outputFile"
    "/win32icon:$icon"
    "/win32manifest:$(Join-Path $root 'app.manifest')"
    "/resource:$(Join-Path $root 'Assets\sky-girl.jpg'),WBToolbox.Native.Assets.sky-girl.jpg"
    "/resource:$(Join-Path $root 'Assets\icon128.png'),WBToolbox.Native.Assets.icon128.png"
    "/resource:$(Join-Path $root 'Assets\icon-mini.png'),WBToolbox.Native.Assets.icon-mini.png"
    "/resource:$(Join-Path $root 'Assets\category-commissions.tsv'),WBToolbox.Native.Assets.category-commissions.tsv"
    '/reference:System.dll'
    '/reference:System.Core.dll'
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

Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination "$outputFile.config" -Force
Write-Output "Built: $outputFile"
