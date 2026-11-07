# Concurrency guard regression test (ASCII only)
# Usage: powershell -NoProfile -File tests\run-concurrency-tests.ps1
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

Write-Host '=== Concurrency guard regression ==='
Write-Host ''

$Srv = 'Pages\PageServer\PageServerMain.xaml.vb'
$Plg = 'Pages\PageOther\PageOtherPlugin.xaml.vb'

# 1. generation isolation for build list
Check 'build-generation-field' (HasCode $Srv 'Private BuildRequestGeneration') 'generation counter exists'
Check 'build-generation-bump' (HasCode $Srv 'BuildRequestGeneration += 1') 'bump on each request'
Check 'build-generation-check' (HasCode $Srv 'If MyGen <> BuildRequestGeneration Then Return') 'discard stale result'

# 2. suppress duplicate request from SelectionChanged
Check 'suppress-flag' (HasCode $Srv 'Private SuppressBuildReload') 'suppress flag exists'
Check 'suppress-set' (HasCode $Srv 'SuppressBuildReload = True') 'set before changing selection'
Check 'suppress-clear' (HasCode $Srv 'SuppressBuildReload = False') 'clear in Finally'
Check 'suppress-check' (HasCode $Srv 'If SuppressBuildReload Then Return') 'checked in SelectionChanged'

# 3. store loading flag
Check 'store-loading-flag' (HasCode $Plg 'Private StoreLoading') 'loading flag exists'
Check 'store-loading-check' (HasCode $Plg 'If StoreLoaded OrElse StoreLoading Then Return') 'block re-entry'
Check 'store-loading-clear' (HasCode $Plg 'StoreLoading = False') 'clear on both paths'

# 4. sidebar wiring
Check 'server-sidebar-sub' (HasCode $Srv 'AddHandler FrmServerLeft.PageChanged') 'server sidebar subscribed'
Check 'plugin-sidebar-sub' (HasCode $Plg 'AddHandler FrmPluginLeft.PageChanged') 'plugin sidebar subscribed'
Check 'server-sidebar-handler' (HasCode $Srv 'Private Sub OnSidebarChanged') 'server handler exists'
Check 'plugin-sidebar-handler' (HasCode $Plg 'Private Sub OnSidebarChanged') 'plugin handler exists'

# 5. progress cleanup
Check 'progress-cleanup-catch' (HasCode $Srv 'ModServer.DownloadProgress = Nothing') 'clear progress on failure'

Write-Host ''
Write-Host ("Passed " + $Passed + ", Failed " + $Failed)
if ($Failed -gt 0) {
    Write-Host '[X] some checks failed' -ForegroundColor Red
    exit 1
}
Write-Host '[OK] all passed' -ForegroundColor Green
exit 0
