# Skin / authlib-injector regression test
# Usage: powershell -NoProfile -File tests\run-skin-tests.ps1
$ErrorActionPreference = 'Stop'

# $PSCommandPath 比 $MyInvocation.MyCommand.Path 更可靠
$HERE = $PSScriptRoot
$PCL3 = Split-Path -Parent $HERE
$ROOT = Join-Path $PCL3 'source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$VBC  = Join-Path $PCL3 '_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$EXE  = Join-Path $HERE 'skin-test.exe'
$SITE = Join-Path $HERE '_skintest'

Write-Host "ROOT = $ROOT"

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] missing: $p" -ForegroundColor Red; exit 1 }
}

Write-Host 'Compiling...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT `
    (Join-Path $HERE 'ModSkin.Test.vb') `
    (Join-Path $HERE 'ModSkin.Test.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModSkin.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] compile failed' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Write-Host ''
& $EXE $SITE
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -eq 0) { Write-Host '[OK] all passed' -ForegroundColor Green } else { Write-Host '[X] failed' -ForegroundColor Red }
exit $rc
