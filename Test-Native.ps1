$ErrorActionPreference = 'Stop'

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = $PSScriptRoot
$output = Join-Path $root 'dist\WBToolbox.CoreTests.exe'

New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null

& $compiler /nologo /target:exe /platform:anycpu /optimize+ "/out:$output" `
    (Join-Path $root 'Core\CurrencyModels.cs') `
    (Join-Path $root 'Core\NumericInput.cs') `
    (Join-Path $root 'Core\PricingEngine.cs') `
    (Join-Path $root 'Core\CommissionCatalog.cs') `
    "/resource:$(Join-Path $root 'Assets\category-commissions.tsv'),WBToolbox.Native.Assets.category-commissions.tsv" `
    (Join-Path $root 'Tests\CoreTests.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $output
exit $LASTEXITCODE
