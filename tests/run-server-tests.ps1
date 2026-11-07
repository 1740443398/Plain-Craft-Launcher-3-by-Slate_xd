# 开服器模块测试：服务端版本拉取、版本隔离、EULA 流程、启动参数
# 用法：powershell -NoProfile -File tests\run-server-tests.ps1
$ErrorActionPreference = 'Stop'
$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$EXE  = Join-Path $HERE 'server-test.exe'
$SITE = Join-Path $HERE '_servertest'

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT `
    (Join-Path $HERE 'ModServer.Test.vb') `
    (Join-Path $HERE 'ModServer.Test.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModServerCore.vb') `
    (Join-Path $ROOT 'Modules\ModServer.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Write-Host ''
& $EXE $SITE
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -eq 0) { Write-Host '[OK] 全部通过' -ForegroundColor Green } else { Write-Host '[X] 有测试失败' -ForegroundColor Red }
exit $rc