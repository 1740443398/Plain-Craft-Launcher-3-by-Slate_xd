# Java plugin host E2E test
# Usage: powershell -NoProfile -File tests\run-javahost-tests.ps1
$ErrorActionPreference = 'Stop'
$HERE = $PSScriptRoot
$PCL3 = Split-Path -Parent $HERE
$ROOT = Join-Path $PCL3 'source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$VBC  = Join-Path $PCL3 '_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$JAR  = Join-Path $PCL3 'docs\plugin-samples\java\plugin.jar'
$EXE  = Join-Path $HERE 'javahost-test.exe'
$SITE = Join-Path $HERE '_javahost'

foreach ($p in @($VBC, $NEWT, $JAR)) {
    if (-not (Test-Path $p)) { Write-Host "[X] missing: $p" -ForegroundColor Red; exit 1 }
}

Write-Host 'Compiling...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT /r:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\PresentationCore.dll" /r:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\PresentationFramework.dll" /r:"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\WindowsBase.dll" `
    (Join-Path $HERE 'ModPluginJava.Test.vb') `
    (Join-Path $HERE 'ModPluginJava.Test.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModPluginPermission.vb') `
    (Join-Path $ROOT 'Modules\ModPluginJava.vb') `
    (Join-Path $ROOT 'Modules\ModPlugin.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] compile failed' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Write-Host ''
& $EXE $SITE $JAR
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -eq 0) { Write-Host '[OK] all passed' -ForegroundColor Green } else { Write-Host '[X] failed' -ForegroundColor Red }
exit $rc