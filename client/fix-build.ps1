# fix-build.ps1 —— 让开源仓库可直接用系统 MSBuild 构建
# 用法：powershell -ExecutionPolicy Bypass -File fix-build.ps1
# 前提：Roslyn 4.14 已放在 $Roslyn；net48 Targeting Pack 已安装；Resources 目录已放入 14 个依赖 DLL

$ErrorActionPreference = "Stop"
$Repo    = $PSScriptRoot
$ProjDir = Join-Path $Repo "Plain Craft Launcher 3"
$Proj    = Join-Path $ProjDir "Plain Craft Launcher 3.vbproj"
$Resx    = Join-Path $ProjDir "My Project\Resources.resx"
$Facades = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades"

Write-Host "[1/6] 移除 SDK 风格 ProjectReference，改为直接引用 Resources 下的 DLL"
$t = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($Proj))
foreach ($pat in @(
    '(?s)\s*<ProjectReference Include="\.\.\\MeloongCore\\Shared\\MeloongCore\.csproj">.*?</ProjectReference>',
    '(?s)\s*<ProjectReference Include="\.\.\\MeloongCore\\Wpf\\MeloongCore\.Wpf\.csproj">.*?</ProjectReference>',
    '(?s)\s*<ProjectReference Include="\.\.\\PCLCS\\PCLCS\.csproj">.*?</ProjectReference>'
)) { $t = [regex]::Replace($t, $pat, '') }

Write-Host "[2/6] 添加 3 个依赖 DLL 引用"
if ($t -notmatch 'Resources\\MeloongCore\.dll') {
    $add = @"
  <ItemGroup>
    <Reference Include="MeloongCore">
      <HintPath>Resources\MeloongCore.dll</HintPath>
      <Private>True</Private>
    </Reference>
    <Reference Include="MeloongCore.Wpf">
      <HintPath>Resources\MeloongCore.Wpf.dll</HintPath>
      <Private>True</Private>
    </Reference>
    <Reference Include="PCLCS">
      <HintPath>Resources\PCLCS.dll</HintPath>
      <Private>True</Private>
    </Reference>
  </ItemGroup>
</Project>
"@
    $t = [regex]::Replace($t, '(?s)\s*</Project>\s*$', ("`r`n" + $add))
}

Write-Host "[3/6] 添加 netstandard 2.0 引用"
if ($t -notmatch 'netstandard\.dll') {
    $add = @"
  <ItemGroup>
    <Reference Include="netstandard">
      <HintPath>$Facades\netstandard.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
</Project>
"@
    $t = [regex]::Replace($t, '(?s)\s*</Project>\s*$', ("`r`n" + $add))
}
[System.IO.File]::WriteAllText($Proj, $t, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[4/6] 修正 Resources.resx 中失效的 DLL 路径"
$r = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($Resx))
$r = $r.Replace('..\..\PCLCS\bin\', '..\Resources\')
[System.IO.File]::WriteAllText($Resx, $r, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[5/6] 给所有 .vb / .xaml 添加 UTF-8 BOM（vbc 需要它识别 UTF-8）"
$n = 0
foreach ($ext in @('*.vb','*.xaml')) {
    foreach ($f in (Get-ChildItem -LiteralPath $ProjDir -Recurse -File -Filter $ext)) {
        $b = [System.IO.File]::ReadAllBytes($f.FullName)
        if ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) { continue }
        $o = New-Object byte[] ($b.Length + 3)
        $o[0] = 0xEF; $o[1] = 0xBB; $o[2] = 0xBF
        [Array]::Copy($b, 0, $o, 3, $b.Length)
        [System.IO.File]::WriteAllBytes($f.FullName, $o)
        $n++
    }
}
Write-Host "      已处理 $n 个文件"

Write-Host "[6/6] 完成。构建命令："
Write-Host @"
  `$msb = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
  `$ros = "E:\蕭遞\PCL3\_rea\tools\roslyn-toolset\tasks\net472"
  & `$msb "$Proj" /p:Configuration=Release /p:Platform=AnyCPU /p:OutputPath="<输出目录>\" ``
    /p:VbcToolPath="`$ros\" /p:VbcToolExe=vbc.exe ``
    /p:CscToolPath="`$ros\" /p:CscToolExe=csc.exe ``
    /p:LangVersion=latest /v:minimal /nologo
"@