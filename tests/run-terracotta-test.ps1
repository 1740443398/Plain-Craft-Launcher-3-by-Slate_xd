# Terracotta 下载与安装的端到端测试（联网，约 3 分钟）。
# 用法：powershell -NoProfile -File tests\run-terracotta-test.ps1
$ErrorActionPreference = 'Stop'

$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$REF  = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$EXE  = Join-Path $HERE 'tc-test.exe'
$OUT  = Join-Path $HERE '_tcout'

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT `
    /r:"$REF\System.IO.Compression.dll" `
    /r:"$REF\System.IO.Compression.FileSystem.dll" `
    (Join-Path $HERE 'ModTerracotta.Test.vb') `
    (Join-Path $HERE 'ModTerracotta.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModTarGz.vb') `
    (Join-Path $ROOT 'Modules\ModTerracotta.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Remove-Item $OUT -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '开始下载（约 3 分钟）...' -ForegroundColor Cyan
& $EXE (Join-Path $OUT 'terracotta.exe')
$rc = $LASTEXITCODE
if ($rc -eq 0) {
    Write-Host ''
    Write-Host '[OK] 下载解压安装全部成功' -ForegroundColor Green
    # 校验产物
    $f = Join-Path $OUT 'terracotta.exe'
    if (Test-Path $f) {
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $magic = [System.Text.Encoding]::ASCII.GetString($bytes[0..1])
        Write-Host ("  文件头: {0}  {1}" -f $magic, $(if($magic -eq 'MZ'){'✔ PE'}else{'✘'}))
    }
} else {
    Write-Host '[X] 测试失败' -ForegroundColor Red
}
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
exit $rc