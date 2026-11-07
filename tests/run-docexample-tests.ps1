# Doc example verification: extract the Java sample from docs and prove it compiles + runs
# Usage: powershell -NoProfile -File tests\run-docexample-tests.ps1
$ErrorActionPreference = 'Stop'
$HERE = $PSScriptRoot
$PCL3 = Split-Path -Parent $HERE
$MD   = Join-Path $PCL3 'docs\PLUGIN-API.md'
$DIR  = Join-Path $HERE '_docexample'

if (-not (Test-Path $MD)) { Write-Host "[X] missing: $MD" -ForegroundColor Red; exit 1 }

# 找 javac
$javac = Get-Command javac -ErrorAction SilentlyContinue
if (-not $javac) {
    $jdk = Get-ChildItem 'C:\Program Files\Java','C:\Program Files (x86)\Java' -Directory -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if ($jdk) { $javac = @{ Source = (Join-Path $jdk.FullName 'bin\javac.exe') } }
}
if (-not $javac) { Write-Host '[X] javac not found' -ForegroundColor Red; exit 1 }

if (Test-Path $DIR) { Remove-Item $DIR -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DIR | Out-Null

$Passed = 0
$Failed = 0
function Check {
    param([string]$Name, [bool]$Cond, [string]$Detail)
    if ($Cond) { $script:Passed++; Write-Host ("  OK    " + $Name + "  " + $Detail) }
    else { $script:Failed++; Write-Host ("  FAIL  " + $Name + "  " + $Detail) -ForegroundColor Red }
}

Write-Host '=== Doc example verification ==='
Write-Host ''

# 1. 文档存在且包含完整示例
$md = [System.IO.File]::ReadAllText($MD, [System.Text.Encoding]::UTF8)
Check 'doc-exists' ($md.Length -gt 10000) "$($md.Length) chars"
Check 'has-permission-section' ($md.Contains('九项权限')) ''
Check 'has-all-actions' ($md.Contains('http_get') -and $md.Contains('write_file') -and $md.Contains('clipboard_set')) ''

# 2. 提取 Java 示例
$m = [regex]::Match($md, '```java\r?\n(import java\.io\.\*;[\s\S]*?)```')
Check 'extract-java-sample' $m.Success ''
if (-not $m.Success) { Write-Host "[X] cannot extract"; exit 1 }

$javaFile = Join-Path $DIR 'MyTool.java'
[System.IO.File]::WriteAllText($javaFile, $m.Groups[1].Value, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  sample: $((Get-Content $javaFile).Count) lines"
Write-Host ''

# 3. 编译
Write-Host 'Compiling...'
$null = & $javac.Source --release 8 -encoding UTF-8 -Xlint:-options -d $DIR $javaFile 2>$null
$classFile = Join-Path $DIR 'MyTool.class'
Check 'compiles' (Test-Path $classFile) $(if (Test-Path $classFile) { "$((Get-Item $classFile).Length) B" } else { 'no class' })
if (-not (Test-Path $classFile)) { Write-Host "[X] compile failed"; exit 1 }

# 4. 打包
$jarExe = Join-Path (Split-Path $javac.Source) 'jar.exe'
Push-Location $DIR
& $jarExe cfe plugin.jar MyTool MyTool.class 2>&1 | Out-Null
Pop-Location
$jarFile = Join-Path $DIR 'plugin.jar'
Check 'packs-jar' (Test-Path $jarFile) $(if (Test-Path $jarFile) { "$((Get-Item $jarFile).Length) B" } else { '' })
Write-Host ''

# 5. 跑协议
Write-Host 'Running protocol test...'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'java'
$psi.Arguments = "-Dfile.encoding=UTF-8 -jar `"$jarFile`""
$psi.WorkingDirectory = $DIR
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
$null = $proc.Start()

$proc.StandardInput.WriteLine('{"type":"hello","pluginId":"com.example.mytool","launcherVersion":"1.0.0","actions":["log","http_get"]}')
$proc.StandardInput.Flush()
Start-Sleep -Milliseconds 1200

$lines = @()
while ($proc.StandardOutput.Peek() -ge 0) {
    $l = $proc.StandardOutput.ReadLine()
    if ($l) { $lines += $l }
}
$proc.StandardInput.WriteLine('{"type":"shutdown"}')
$proc.StandardInput.Flush()
$null = $proc.WaitForExit(3000)
if (-not $proc.HasExited) { $proc.Kill() }

Write-Host ''
Check 'responds' ($lines.Count -gt 0) "$($lines.Count) line(s)"
Check 'sends-log' (($lines | Where-Object { $_ -match '"action"\s*:\s*"log"' }).Count -gt 0) ''
Check 'sends-http-get' (($lines | Where-Object { $_ -match '"action"\s*:\s*"http_get"' }).Count -gt 0) ''

foreach ($l in $lines) { Write-Host "  <- $l" }

Write-Host ''
Write-Host ("Passed $Passed, Failed $Failed")
if ($Failed -gt 0) { Write-Host '[X] failed' -ForegroundColor Red; exit 1 }
Write-Host '[OK] all passed' -ForegroundColor Green
exit 0
