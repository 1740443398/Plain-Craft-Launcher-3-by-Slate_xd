# PCL3 更新与公告推送工具
# 用法：右键「使用 PowerShell 运行」，或 powershell -ExecutionPolicy Bypass -File 推送工具.ps1
#
# 首次使用请修改下面的 $ApiBase、$Password、$ClientKey
# token 会在运行时自动登录获取，无需手填

$ApiBase   = "https://pcl3.byethost9.com/api/"
# 管理员密码（用于自动登录换取 token，token 有效期 12 小时）
$Password  = $env:PCL3_ADMIN_PASSWORD   # 从环境变量读取
# 客户端 Key（公开接口需要，与客户端 ModByethost.vb 一致）
$ClientKey = "REPLACE_WITH_YOUR_CLIENT_API_KEY"
# 运行时自动获取，不需要手填
$Token     = ""

# ===== Byethost JS 挑战绕过 =====
# 该主机会返回 AES Cookie 挑战页，普通 HTTP 客户端无法直接访问。
# 这里用 .NET 实现同样的解密，算出 __test cookie 后带上再请求。
Add-Type -TypeDefinition @"
using System;
using System.Net;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class BypassHost
{
    static byte[] HexToBytes(string hex)
    {
        byte[] r = new byte[hex.Length / 2];
        for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return r;
    }

    static string AesDecryptHex(string keyHex, string ivHex, string cipherHex)
    {
        using (Aes a = Aes.Create())
        {
            a.KeySize = keyHex.Length * 4;
            a.BlockSize = 128;
            a.Mode = CipherMode.CBC;
            a.Padding = PaddingMode.None;
            a.Key = HexToBytes(keyHex);
            a.IV = HexToBytes(ivHex);
            using (var d = a.CreateDecryptor())
            {
                byte[] p = d.TransformFinalBlock(HexToBytes(cipherHex), 0, cipherHex.Length / 2);
                return BitConverter.ToString(p).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    static string RawRequest(string url, string cookie, string postData, string authToken, string clientKey)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = string.IsNullOrEmpty(postData) ? "GET" : "POST";
        req.Timeout = 30000;
        req.ReadWriteTimeout = 30000;
        req.UserAgent = "PCL3-PushTool/1.0";
        if (!string.IsNullOrEmpty(authToken)) req.Headers.Add("X-PCL3-Token", authToken);
        if (!string.IsNullOrEmpty(clientKey)) req.Headers.Add("X-PCL3-Key", clientKey);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", "__test=" + cookie);
        if (!string.IsNullOrEmpty(postData))
        {
            req.ContentType = "application/x-www-form-urlencoded";
            byte[] b = Encoding.UTF8.GetBytes(postData);
            req.ContentLength = b.Length;
            using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
        }
        try
        {
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        catch (WebException ex)
        {
            if (ex.Response != null)
                using (var sr = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            return "{\"ok\":false,\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}";
        }
    }

    static bool IsChallenge(string body)
    {
        return !string.IsNullOrEmpty(body) && body.Contains("slowAES");
    }

    static string Solve(string body)
    {
        var m = Regex.Matches(body, "toNumbers\\(\"([0-9a-fA-F]{32,})\"\\)");
        if (m.Count < 3) return "";
        return AesDecryptHex(m[0].Groups[1].Value, m[1].Groups[1].Value, m[2].Groups[1].Value);
    }

    // 取挑战 cookie（单独用 GET 触发，避免 POST 被挑战页吞掉）
    public static string GetCookie(string url)
    {
        string body = RawRequest(url, null, null, null, null);
        if (!IsChallenge(body)) return "";
        return Solve(body);
    }

    // 带挑战处理的请求：先用 GET 拿 cookie，再用它发正式请求
    public static string Request(string url, string postData, string authToken, string clientKey)
    {
        string ck = GetCookie(url);
        if (string.IsNullOrEmpty(ck)) return RawRequest(url, null, postData, authToken, clientKey);
        return RawRequest(url, ck, postData, authToken, clientKey);
    }
}
"@ -ErrorAction SilentlyContinue


# 自动登录获取 token（token 有效期 12 小时）
function Ensure-Login {
    if ($script:Token -ne "") { return $true }

    Write-Host "  正在登录获取 token ..." -ForegroundColor DarkGray
    $pairs = "password=" + [uri]::EscapeDataString($Password)
    try {
        $raw = [BypassHost]::Request($ApiBase + "?action=login", $pairs, "", $ClientKey)
        if ([string]::IsNullOrWhiteSpace($raw) -or $raw -match "<!DOCTYPE") {
            Write-Host "  x 登录失败：服务器无有效响应" -ForegroundColor Red
            return $false
        }
        $obj = $raw | ConvertFrom-Json
        if (-not $obj.ok -or -not $obj.token) {
            Write-Host "  x 登录失败：$(if ($obj.error) { $obj.error } else { "未返回 token" })" -ForegroundColor Red
            Write-Host "     请检查脚本顶部的 `$Password 是否正确。" -ForegroundColor Yellow
            return $false
        }
        $script:Token = $obj.token
        $hours = [math]::Round($obj.expiresIn / 3600, 1)
        Write-Host "  OK  登录成功（有效期 $hours 小时）" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "  x 登录异常：$($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Invoke-Api {
    param([string]$Action, [hashtable]$Body = @{}, [hashtable]$Query = @{}, [switch]$Quiet, [switch]$ExpectFail)

    $url = $ApiBase + "?action=" + $Action
    foreach ($k in $Query.Keys) { $url += "&$k=" + [uri]::EscapeDataString([string]$Query[$k]) }

    $postData = ""
    if ($Body.Count -gt 0) {
        $pairs = @()
        foreach ($k in $Body.Keys) {
            $pairs += "$([uri]::EscapeDataString($k))=$([uri]::EscapeDataString([string]$Body[$k]))"
        }
        $postData = $pairs -join "&"
    }

    # 管理接口需要 token，未登录时自动登录
    $adminActions = @("admin_overview","publish","release_clear","notice","delete_notice","donor","delete_donor","feedback_update","feedback_delete","cave_update","mail_queue","mail_run","mail_clear")
    if ($adminActions -contains $Action) {
        if (-not (Ensure-Login)) { return $null }
    }

    if (-not $Quiet) { Write-Host "  → 请求 $Action ..." -ForegroundColor DarkGray }

    try {
        $raw = [BypassHost]::Request($url, $postData, $Token, $ClientKey)

        if ([string]::IsNullOrWhiteSpace($raw)) {
            Write-Host "  x 服务器返回空内容（$Action）" -ForegroundColor Red
            return $null
        }
        if ($raw -match "slowAES") {
            Write-Host "  x 被主机的 JS 挑战页拦截，绕过失败（$Action）" -ForegroundColor Red
            return $null
        }
        if ($raw -match "<!DOCTYPE|<html") {
            Write-Host "  x 返回了 HTML 而非 JSON，可能地址错误或服务器出错（$Action）" -ForegroundColor Red
            Write-Host "     开头：$($raw.Substring(0, [Math]::Min(160, $raw.Length)))" -ForegroundColor DarkGray
            return $null
        }

        $obj = $null
        try { $obj = $raw | ConvertFrom-Json }
        catch {
            Write-Host "  x 响应不是合法 JSON（$Action）" -ForegroundColor Red
            Write-Host "     开头：$($raw.Substring(0, [Math]::Min(160, $raw.Length)))" -ForegroundColor DarkGray
            return $null
        }

        # 服务端业务失败必须显式提示，不能静默
        if ($obj -and ($obj.PSObject.Properties.Name -contains "ok") -and (-not $obj.ok)) {
            $errMsg = if ($obj.error) { $obj.error } else { "未知错误" }
            # -ExpectFail：调用方本就预期失败（如参数校验测试），把结果交回去自行判断
            if ($ExpectFail) { return $obj }
            Write-Host "  x 服务器拒绝：$errMsg" -ForegroundColor Red
            if ($errMsg -match "凭据|token|鉴权|Unauthorized|401") {
                Write-Host "     提示：请检查脚本顶部的密码是否正确，或登录是否已过期。" -ForegroundColor Yellow
            }
            return $null
        }
        return $obj
    } catch {
        Write-Host "  x 请求异常：$($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Show-Menu {
    Clear-Host
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "   PCL3 更新与公告推送工具" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  服务器：$ApiBase"
    Write-Host ""
    Write-Host "  [1] 发布新版本"
    Write-Host "  [2] 发布公告"
    Write-Host "  [3] 查看当前版本"
    Write-Host "  [4] 查看全部公告"
    Write-Host "  [5] 删除公告"
    Write-Host "  [6] 添加赞助者"
    Write-Host "  [7] 查看赞助者名单"
    Write-Host "  [8] 删除赞助者"
    Write-Host "  [9] 测试连接"
    Write-Host "  [0] 退出"
    Write-Host ""
}

function Publish-Release {
    Write-Host "`n--- 发布新版本 ---" -ForegroundColor Yellow
    $ver = Read-Host "版本号（如 3.0.0.2）"
    if (-not $ver) { return }
    $url = Read-Host "下载地址（直链，建议放网盘直链或你的主机上）"
    if (-not $url) { return }
    $sha = Read-Host "SHA-256（可留空）"
    $notes = Read-Host "更新说明（可留空）"
    $force = Read-Host "是否强制更新？(y/N)"
    $channel = Read-Host "通道 (release/beta，默认 release)"
    if (-not $channel) { $channel = "release" }

    $body = @{
        version = $ver
        url     = $url
        sha256  = $sha
        notes   = $notes
        channel = $channel
        force   = $(if ($force -eq "y" -or $force -eq "Y") { "1" } else { "0" })
    }
    $r = Invoke-Api -Action "publish" -Body $body
    if ($r -and $r.ok) {
        Write-Host "`n✔ 已发布版本 $($r.published.version)，当前共 $($r.total) 个版本" -ForegroundColor Green
    }
}

function Publish-Notice {
    Write-Host "`n--- 发布公告 ---" -ForegroundColor Yellow
    $title = Read-Host "标题"
    if (-not $title) { return }
    $content = Read-Host "内容（可用 \n 换行）"
    $content = $content -replace '\\n', "`n"
    $prio = Read-Host "优先级（数字越大越靠前，默认 0）"
    if (-not $prio) { $prio = "0" }
    $days = Read-Host "有效天数（留空表示永久）"

    $expire = 0
    if ($days -match '^\d+$' -and [int]$days -gt 0) {
        $expire = [int][double]::Parse((Get-Date).AddDays([int]$days).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds)
    }

    $body = @{ title = $title; content = $content; priority = $prio; expire = $expire }
    $r = Invoke-Api -Action "notice" -Body $body
    if ($r -and $r.ok) {
        Write-Host "`n✔ 已发布公告「$($r.notice.title)」，当前共 $($r.total) 条" -ForegroundColor Green
    }
}

function Show-Latest {
    $r = Invoke-Api -Action "admin_overview"
    if (-not $r) { return }
    $list = @($r.releases)
    if ($list.Count -eq 0) {
        Write-Host ""
        Write-Host "服务器上还没有任何版本记录" -ForegroundColor Yellow
        return
    }
    Write-Host ""
    Write-Host "--- 版本列表（共 $($list.Count) 个，上限 5）---" -ForegroundColor Yellow
    $i = 1
    foreach ($v in $list) {
        $fileInfo = if ($v.file) { $v.file } else { "（无安装包，仅备用地址）" }
        Write-Host ("  {0}. {1}  code={2}  {3:N2} MB" -f $i, $v.version, $v.code, ($v.size / 1MB)) -ForegroundColor Cyan
        Write-Host ("     文件：{0}" -f $fileInfo) -ForegroundColor Gray
        if ($v.notes) { Write-Host ("     说明：{0}" -f $v.notes) -ForegroundColor Gray }
        $i++
    }
}

function Show-Notices {
    $r = Invoke-Api -Action "admin_overview"
    if (-not $r) { return }
    $list = @($r.notices)
    Write-Host ""
    Write-Host "--- 公告列表（共 $($list.Count) 条）---" -ForegroundColor Yellow
    if ($list.Count -eq 0) { Write-Host "  （暂无公告）" -ForegroundColor Gray; return }
    foreach ($n in $list) {
        Write-Host "  [$($n.id)] $($n.title)  (优先级 $($n.priority))" -ForegroundColor Cyan
        if ($n.content) { Write-Host "      $($n.content)" -ForegroundColor Gray }
    }
}

function Remove-Notice {
    Show-Notices
    $id = Read-Host "`n要删除的公告 ID"
    if (-not $id) { return }
    $r = Invoke-Api -Action "delete_notice" -Body @{ id = $id }
    if ($r -and $r.ok) {
        Write-Host "✔ 已删除 $($r.removed) 条，剩余 $($r.total) 条" -ForegroundColor Green
    }
}

function Test-Connection {
    Write-Host ""
    Write-Host "=== 连接测试 ===" -ForegroundColor Cyan
    Write-Host "  服务器：$ApiBase"
    Write-Host "  凭据 ：$(if ($Token) { "已登录（" + $Token.Substring(0, [Math]::Min(12, $Token.Length)) + "...）" } else { "尚未登录，首次请求时自动获取" })"
    Write-Host ""

    $pass = 0
    $fail = 0

    # 1. 基础连通性（公开接口）
    Write-Host "[1/3] 测试基础连通性（公开接口 check）" -ForegroundColor Yellow
    $r1 = Invoke-Api -Action "check" -Query @{ version = "0"; channel = "release" }
    if ($r1 -and $r1.ok) {
        $srvTime = ([datetime]"1970-01-01").AddSeconds([double]$r1.serverTime).ToLocalTime()
        Write-Host "      OK  服务器时间 $($srvTime.ToString("yyyy-MM-dd HH:mm:ss"))" -ForegroundColor Green
        Write-Host "      OK  当前最新版本 $(if ($r1.latest) { $r1.latest.version } else { "（无）" })" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "      失败：无法访问服务器" -ForegroundColor Red
        $fail++
    }
    Write-Host ""

    # 2. 管理凭据（需要 token）
    Write-Host "[2/3] 测试管理凭据（admin_overview）" -ForegroundColor Yellow
    $r2 = Invoke-Api -Action "admin_overview"
    if ($r2 -and $r2.ok) {
        Write-Host "      OK  鉴权通过" -ForegroundColor Green
        if ($r2.stats) {
            Write-Host "      OK  回声洞 $($r2.stats.caves) 条 | 反馈 $($r2.stats.feedback) 条 | 赞助者 $($r2.stats.donors) 位 | 版本 $($r2.stats.releases) 个" -ForegroundColor Green
        }
        $pass++
    } else {
        Write-Host "      失败：凭据无效或已过期" -ForegroundColor Red
        Write-Host "      请检查脚本顶部的 `$Token 是否与服务端一致。" -ForegroundColor Yellow
        $fail++
    }
    Write-Host ""

    # 3. 写权限（发布一个空操作会被拒绝，但能验证是否到达业务层）
    # 3. 写权限：发一个空参数请求，看服务端是「鉴权失败」还是「参数校验失败」
    Write-Host "[3/3] 测试写权限（publish）" -ForegroundColor Yellow
    $r3 = Invoke-Api -Action "publish" -Body @{ version = ""; url = "" } -Quiet -ExpectFail
    if ($r3 -and $r3.ok) {
        # 理论上不该成功，成功了说明服务端没做校验，反而是问题
        Write-Host "      !  写接口未做参数校验（异常）" -ForegroundColor Yellow
    } elseif ($r3 -and $r3.error -match "缺少") {
        Write-Host "      OK  写接口可达（已通过鉴权，服务端正确拒绝空参数）" -ForegroundColor Green
        $pass++
    } elseif ($r3 -and $r3.error -match "未登录|凭据|token|鉴权") {
        Write-Host "      失败：写权限不足，$($r3.error)" -ForegroundColor Red
        $fail++
    } else {
        Write-Host "      失败：无法判断写接口状态" -ForegroundColor Red
        $fail++
    }
    Write-Host ""

    Write-Host "=== 结果：$pass 项通过，$fail 项失败 ===" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
    if ($fail -eq 0) {
        Write-Host "连接正常，可以开始使用。" -ForegroundColor Green
    } else {
        Write-Host "存在失败项，请按上面的提示排查。" -ForegroundColor Yellow
    }
}

function Add-Donor {
    Write-Host "`n--- 添加赞助者 ---" -ForegroundColor Yellow
    $name = Read-Host "赞助者名称"
    if (-not $name) { return }
    $amount = Read-Host "赞助金额（元，如 50）"
    if (-not $amount) { $amount = "0" }
    $msg = Read-Host "留言（可留空）"
    $body = @{ name = $name; amount = $amount; message = $msg }
    $r = Invoke-Api -Action "donor" -Body $body
    if ($r -and $r.ok) {
        Write-Host "`n  OK  $($r.donor.name)  累计 $($r.donor.amount) 元，当前共 $($r.total) 位" -ForegroundColor Green
    }
}

function Show-Donors {
    $r = Invoke-Api -Action "donors"
    if ($r -and $r.ok) {
        Write-Host "`n--- 赞助者名单（共 $($r.total) 位）---" -ForegroundColor Yellow
        if ($r.total -eq 0) { Write-Host "  （还没有记录）" -ForegroundColor Gray; return }
        $rank = 1
        foreach ($d in $r.donors) {
            Write-Host ("  {0}. {1,-16} {2,8} 元" -f $rank, $d.name, $d.amount) -ForegroundColor Cyan
            if ($d.message) { Write-Host "       $($d.message)" -ForegroundColor Gray }
            $rank++
        }
    }
}

function Remove-Donor {
    Show-Donors
    $name = Read-Host "`n要删除的赞助者名称"
    if (-not $name) { return }
    $r = Invoke-Api -Action "delete_donor" -Body @{ name = $name }
    if ($r -and $r.ok) {
        Write-Host "  OK  已删除 $($r.removed) 位，剩余 $($r.total) 位" -ForegroundColor Green
    }
}

# 主循环
while ($true) {
    Show-Menu
    $choice = Read-Host "请选择"
    switch ($choice) {
        "1" { Publish-Release }
        "2" { Publish-Notice }
        "3" { Show-Latest }
        "4" { Show-Notices }
        "5" { Remove-Notice }
        "6" { Add-Donor }
        "7" { Show-Donors }
        "8" { Remove-Donor }
        "9" { Test-Connection }
        "0" { return }
        default { Write-Host "无效选择" -ForegroundColor Red }
    }
    Write-Host ""
    Read-Host "按回车继续"
}
