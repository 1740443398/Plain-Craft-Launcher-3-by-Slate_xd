param($jarPath)
# 模拟启动器与 Java 插件通信（ASCII only，避免编码问题）
$ErrorActionPreference = 'Stop'

Write-Host "=== Java plugin protocol test ==="
Write-Host ""

if (-not (Test-Path $jarPath)) { Write-Host "[X] jar not found: $jarPath"; exit 1 }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'java'
$psi.Arguments = "-Dfile.encoding=UTF-8 -jar `"$jarPath`""
$psi.WorkingDirectory = Split-Path $jarPath
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

# 1. handshake
$hello = '{"type":"hello","pluginId":"com.example.java","launcherVersion":"1.0.0","actions":["toast","get_version","log","get_setting","set_theme"]}'
$proc.StandardInput.WriteLine($hello)
$proc.StandardInput.Flush()

Start-Sleep -Milliseconds 900

# 2. collect output
$out = @()
while ($proc.StandardOutput.Peek() -ge 0) {
    $line = $proc.StandardOutput.ReadLine()
    if ($line) { $out += $line }
}

$proc.StandardInput.WriteLine('{"type":"shutdown"}')
$proc.StandardInput.Flush()
$null = $proc.WaitForExit(3000)
if (-not $proc.HasExited) { $proc.Kill() }

Write-Host "Lines received: $($out.Count)"
Write-Host ""
$passed = 0
$failed = 0

foreach ($l in $out) {
    Write-Host "  <- $l"
    if ($l -match '"action"\s*:\s*"get_version"') { $passed++ }
    if ($l -match '"action"\s*:\s*"toast"') { $passed++ }
    if ($l -match '"action"\s*:\s*"log"') { $passed++ }
}

Write-Host ""
if ($out.Count -gt 0) {
    Write-Host "[OK] plugin responded, $passed action(s) detected"
    exit 0
} else {
    Write-Host "[X] no output from plugin"
    $err = $proc.StandardError.ReadToEnd()
    if ($err) { Write-Host "stderr: $err" }
    exit 1
}
