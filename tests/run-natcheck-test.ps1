# NAT 类型检测的联网测试。需要能访问 STUN 服务器。
# 用法：powershell -NoProfile -File tests\run-natcheck-test.ps1
$ErrorActionPreference = 'Stop'

$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$SRC  = Join-Path $ROOT 'Modules\ModNatCheck.vb'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$EXE  = Join-Path $HERE 'natcheck-test.exe'

foreach ($p in @($VBC, $SRC)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    (Join-Path $HERE 'ModNatCheck.Test.vb') `
    (Join-Path $HERE 'ModNatCheck.Stub.vb') `
    $SRC
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Write-Host ''
& $EXE
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -eq 0) { Write-Host '[OK] 检测成功' -ForegroundColor Green }
elseif ($rc -eq 2) { Write-Host '[!] 网络环境屏蔽了 STUN，属正常情况' -ForegroundColor Yellow }
else { Write-Host '[X] 测试失败' -ForegroundColor Red }
exit $rc