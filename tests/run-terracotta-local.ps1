# Terracotta 下载安装的确定性测试（用本地 HTTP 服务，不依赖外网）。
# 需要先准备一个真实包：把 terracotta-0.4.2-windows-x86_64-pkg.tar.gz 放到 tests\_tcdata\ 下。
# 用法：powershell -NoProfile -File tests\run-terracotta-local.ps1
$ErrorActionPreference = 'Stop'

$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$REF  = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$DATA = Join-Path $HERE '_tcdata'
$EXE  = Join-Path $HERE 'tc-local.exe'
$PORT = 18099

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

# 准备数据目录
New-Item -ItemType Directory -Force -Path $DATA | Out-Null
$real = Join-Path $DATA 'tc.tar.gz'
if (-not (Test-Path $real)) {
    Write-Host "[X] 缺少测试包：$real" -ForegroundColor Red
    Write-Host '    请下载 terracotta-0.4.2-windows-x86_64-pkg.tar.gz 并放到该路径。' -ForegroundColor Yellow
    exit 1
}

# 生成哈希不匹配的假包
$fake = Join-Path $DATA 'fake.tar.gz'
if (-not (Test-Path $fake)) {
    Copy-Item $real $fake
    $b = [System.IO.File]::ReadAllBytes($fake)
    $b[5000] = 0xFF
    [System.IO.File]::WriteAllBytes($fake, $b)
}

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT `
    /r:"$REF\System.IO.Compression.dll" `
    /r:"$REF\System.IO.Compression.FileSystem.dll" `
    (Join-Path $HERE 'ModTerracotta.LocalE2E.vb') `
    (Join-Path $HERE 'ModTerracotta.LocalE2E.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModTarGz.vb') `
    (Join-Path $ROOT 'Modules\ModTerracotta.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force

# 起本地服务
$job = Start-Job -ScriptBlock {
    param($h, $d, $p)
    & node (Join-Path $h 'local-http-server.js') $d $p
} -ArgumentList $HERE, $DATA, $PORT
Start-Sleep -Seconds 2

try {
    Write-Host ''
    & $EXE "http://127.0.0.1:$PORT/tc.tar.gz" (Join-Path $HERE '_tcout\tc.exe') "http://127.0.0.1:$PORT/fake.tar.gz"
    $rc = $LASTEXITCODE
} finally {
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
    Remove-Item $EXE -Force -ErrorAction SilentlyContinue
}

if ($rc -eq 0) { Write-Host '[OK] 全部通过' -ForegroundColor Green } else { Write-Host '[X] 有测试失败' -ForegroundColor Red }
exit $rc