# 插件安全审计测试：验证危险插件会被拒绝加载
# 用法：powershell -NoProfile -File tests\run-pluginaudit-tests.ps1
$ErrorActionPreference = 'Stop'
$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$EXE  = Join-Path $HERE 'audit-test.exe'
$SITE = Join-Path $HERE '_audittest'

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT `
    (Join-Path $HERE 'ModPluginAudit.Test.vb') `
    (Join-Path $HERE 'ModPluginAudit.Test.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModPluginAudit.vb') `
    (Join-Path $ROOT 'Modules\ModPlugin.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Write-Host ''
& $EXE $SITE
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -eq 0) { Write-Host '[OK] 全部通过' -ForegroundColor Green } else { Write-Host '[X] 有测试失败' -ForegroundColor Red }
exit $rc