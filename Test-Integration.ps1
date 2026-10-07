param([switch]$TranslationOnly)

$ErrorActionPreference = 'Stop'

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$root = $PSScriptRoot
$output = Join-Path $root 'dist\WBToolbox.IntegrationTests.exe'

New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null

& $compiler /nologo /target:exe /platform:anycpu /optimize+ "/out:$output" `
    "/reference:$(Join-Path $framework 'System.Net.Http.dll')" `
    "/reference:$(Join-Path $framework 'System.Web.Extensions.dll')" `
    "/reference:$(Join-Path $framework 'System.Xml.dll')" `
    (Join-Path $root 'Core\CurrencyModels.cs') `
    (Join-Path $root 'Services\ExchangeRateService.cs') `
    (Join-Path $root 'Services\TranslationService.cs') `
    (Join-Path $root 'Tests\IntegrationTests.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($TranslationOnly) { & $output --translation-only }
else { & $output }
exit $LASTEXITCODE
