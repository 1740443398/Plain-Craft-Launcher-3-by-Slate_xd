# 模组依赖检查的端到端测试。改动 ModDependencyCheck.vb 后请重跑。
# 用法：powershell -NoProfile -File tests\run-depcheck-tests.ps1
$ErrorActionPreference = 'Stop'

$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$REF  = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$MODS = Join-Path $HERE '_depmods'
$EXE  = Join-Path $HERE 'depcheck-test.exe'

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { Write-Host '[X] 需要 python 生成测试 Mod' -ForegroundColor Red; exit 1 }
& python (Join-Path $HERE 'make-depcheck-mods.py') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 生成测试 Mod 失败' -ForegroundColor Red; exit 1 }

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT /r:"$REF\System.IO.Compression.dll" /r:"$REF\System.IO.Compression.FileSystem.dll" `
    (Join-Path $HERE 'ModDependencyCheck.Test.vb') `
    (Join-Path $HERE 'ModDependencyCheck.Stub.vb') `
    (Join-Path $ROOT 'Modules\Minecraft\ModMeta.vb') `
    (Join-Path $ROOT 'Modules\Minecraft\ModVersionRange.vb') `
    (Join-Path $ROOT 'Modules\Minecraft\ModDependencyCheck.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Write-Host ''
& $EXE ($MODS + '\')
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -ne 0) { Write-Host '[X] 有测试未通过' -ForegroundColor Red } else { Write-Host '[OK] 全部通过' -ForegroundColor Green }
exit $rc