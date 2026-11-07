# 版本范围比较的单元测试。改动 ModVersionRange.vb 后请重跑。
# 用法：powershell -NoProfile -File tests\run-versionrange-tests.ps1
$ErrorActionPreference = 'Stop'

$VBC = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$SRC = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd\Modules\Minecraft\ModVersionRange.vb'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$EXE = Join-Path $HERE 'test.exe'

if (-not (Test-Path $VBC)) { Write-Host "[X] 找不到 vbc.exe: $VBC" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $SRC)) { Write-Host "[X] 找不到源码: $SRC" -ForegroundColor Red; exit 1 }

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    (Join-Path $HERE 'ModVersionRange.Test.vb') $SRC
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Write-Host ''
& $EXE
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -ne 0) { Write-Host '[X] 有测试未通过' -ForegroundColor Red } else { Write-Host '[OK] 全部通过' -ForegroundColor Green }
exit $rc