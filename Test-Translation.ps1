$ErrorActionPreference = 'Stop'
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$compiler = Join-Path $framework 'csc.exe'
$output = Join-Path $PSScriptRoot 'dist\WBToolbox.TranslationTests.exe'
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
& $compiler /nologo /target:exe /platform:anycpu /optimize+ "/out:$output" `
    "/reference:$(Join-Path $framework 'System.Net.Http.dll')" `
    "/reference:$(Join-Path $framework 'System.Web.Extensions.dll')" `
    (Join-Path $PSScriptRoot 'Services\TranslationService.cs') `
    (Join-Path $PSScriptRoot 'Tests\TranslationTests.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $output
exit $LASTEXITCODE
