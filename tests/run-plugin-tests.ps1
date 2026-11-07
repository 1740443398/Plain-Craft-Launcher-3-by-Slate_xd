# 插件系统的加载测试。验证扫描、清单解析、权限声明、入口读取。
# 用法：powershell -NoProfile -File tests\run-plugin-tests.ps1
$ErrorActionPreference = 'Stop'

$VBC  = 'E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472\vbc.exe'
$ROOT = 'E:\蕭遞\PCL3\source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'
$NEWT = Join-Path $ROOT 'Resources\Newtonsoft.Json.dll'
$HERE = Split-Path -Parent $MyInvocation.MyCommand.Path
$EXE  = Join-Path $HERE 'plugin-test.exe'
$SITE = Join-Path $HERE '_plugtest'

foreach ($p in @($VBC, $NEWT)) {
    if (-not (Test-Path $p)) { Write-Host "[X] 找不到: $p" -ForegroundColor Red; exit 1 }
}

# 准备测试插件
$pdir = Join-Path $SITE 'PCL\Plugins\test-plugin'
New-Item -ItemType Directory -Force -Path $pdir | Out-Null
$manifest = '{"id":"com.example.hello","name":"示例插件","version":"1.0.0","author":"test","description":"自动化测试用","entry":"main.xaml","needNetwork":false,"needPlayerData":false,"needExecute":false,"minVersionCode":100}'
$manifest | Set-Content (Join-Path $pdir 'plugin.json') -Encoding UTF8
$xaml = '<local:MyCard Title="T"><TextBlock Text="插件系统工作正常" /></local:MyCard>'
$xaml | Set-Content (Join-Path $pdir 'main.xaml') -Encoding UTF8

Write-Host '编译测试...'
& $VBC /nologo /out:$EXE /target:exe /langversion:latest /rootnamespace:PCL `
    /r:$NEWT `
    (Join-Path $HERE 'ModPlugin.Test.vb') `
    (Join-Path $HERE 'ModPlugin.Test.Stub.vb') `
    (Join-Path $ROOT 'Modules\ModPlugin.vb')
if ($LASTEXITCODE -ne 0) { Write-Host '[X] 编译失败' -ForegroundColor Red; exit 1 }

Copy-Item $NEWT $HERE -Force
Write-Host ''
& $EXE $SITE
$rc = $LASTEXITCODE
Remove-Item $EXE -Force -ErrorAction SilentlyContinue
if ($rc -eq 0) { Write-Host '[OK] 全部通过' -ForegroundColor Green } else { Write-Host '[X] 有测试失败' -ForegroundColor Red }
exit $rc