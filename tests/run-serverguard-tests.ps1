# Server guard regression test (ASCII only, avoids codepage issues)
# Usage: powershell -NoProfile -File tests\run-serverguard-tests.ps1
$ErrorActionPreference = 'Stop'
$ROOT = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'source_final\opensource-2.13.1.1\Plain Craft Launcher by Slate_xd'

$Passed = 0
$Failed = 0

function HasCode {
    param([string]$RelFile, [string]$Pattern)
    $p = Join-Path $ROOT $RelFile
    if (-not (Test-Path $p)) { return $false }
    $c = Get-Content $p -Raw -Encoding UTF8
    return $c.Contains($Pattern)
}

function Check {
    param([string]$Name, [bool]$Cond, [string]$Detail)
    if ($Cond) {
        $script:Passed++
        Write-Host ("  OK    " + $Name + "  " + $Detail)
    } else {
        $script:Failed++
        Write-Host ("  FAIL  " + $Name + "  " + $Detail) -ForegroundColor Red
    }
}

Write-Host '=== Server guard regression ==='
Write-Host ''

# 1. delete while running
Check 'delete-guard' (HasCode 'Pages\PageServer\PageServerMain.xaml.vb' 'ModServer.RunningVersion Is V') 'block deleting a running server'

# 2. stop server on exit
Check 'exit-detect' (HasCode 'FormMain.xaml.vb' 'If ModServer.IsRunning Then') 'detect running server on exit'
Check 'exit-stop' (HasCode 'FormMain.xaml.vb' 'ModServer.StopServer()') 'stop server before exit'

# 3. temp zip cleanup
Check 'temp-zip-cleanup' (HasCode 'Modules\ModPluginStore.vb' 'TempZip') 'delete temp zip after extract'

# 4. java precheck
Check 'java-precheck' (HasCode 'Pages\PageServer\PageServerMain.xaml.vb' 'Private Sub CheckJava()') 'check java before download'

# 5. progress cleanup on failure
Check 'progress-cleanup' (HasCode 'Pages\PageServer\PageServerMain.xaml.vb' 'HideDownloadProgress()') 'hide progress bar on failure'

# 6. next step after download
Check 'next-step' (HasCode 'Pages\PageServer\PageServerMain.xaml.vb' 'Private Sub ShowDownloadDone') 'offer next step after download'

# 7. plugin audit rejection
Check 'audit-reject' (HasCode 'Modules\ModPluginStore.vb' 'ModPluginAudit.HighestRisk') 'reject dangerous plugin'

# 8. titlebar rebuild
Check 'titlebar-rebuild' (HasCode 'Pages\PageOther\PageOtherPlugin.xaml.vb' 'RebuildPluginTitleButtons') 'rebuild titlebar on enable change'

# 9. plugin host page
Check 'plugin-host' (HasCode 'Pages\PageOther\PagePluginHost.xaml.vb' 'Public Property Target') 'plugin host page exists'

Write-Host ''
Write-Host ("Passed " + $Passed + ", Failed " + $Failed)
if ($Failed -gt 0) {
    Write-Host '[X] some checks failed' -ForegroundColor Red
    exit 1
}
Write-Host '[OK] all passed' -ForegroundColor Green
exit 0
