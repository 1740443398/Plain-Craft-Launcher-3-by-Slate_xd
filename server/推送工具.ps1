# PCL3 更新与公告推送工具
# 用法：右键「使用 PowerShell 运行」，或 powershell -ExecutionPolicy Bypass -File 推送工具.ps1
#
# 首次使用请修改下面的 $ApiBase 和 $Token

$ApiBase = "https://pcl3.byethost9.com/api/"
$Token   = $env:PCL3_ADMIN_TOKEN   # 从环境变量读取，不要写死在脚本里

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

    static string RawRequest(string url, string cookie, string postData)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = string.IsNullOrEmpty(postData) ? "GET" : "POST";
        req.Timeout = 30000;
        req.ReadWriteTimeout = 30000;
        req.UserAgent = "PCL3-PushTool/1.0";
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
        string body = RawRequest(url, null, null);
        if (!IsChallenge(body)) return "";
        return Solve(body);
    }

    // 带挑战处理的请求：先用 GET 拿 cookie，再用它发正式请求
    public static string Request(string url, string postData)
    {
        string ck = GetCookie(url);
        if (string.IsNullOrEmpty(ck)) return RawRequest(url, null, postData);
        return RawRequest(url, ck, postData);
    }
}
"@ -ErrorAction SilentlyContinue

function Invoke-Api {
    param([string]$Action, [hashtable]$Body = @{}, [hashtable]$Query = @{})
    $url = $ApiBase + "?action=" + $Action
    foreach ($k in $Query.Keys) { $url += "&$k=" + [uri]::EscapeDataString([string]$Query[$k]) }

    $postData = ""
    if ($Body.Count -gt 0) {
        $Body["token"] = $Token
        $pairs = @()
        foreach ($k in $Body.Keys) {
            $pairs += "$([uri]::EscapeDataString($k))=$([uri]::EscapeDataString([string]$Body[$k]))"
        }
        $postData = $pairs -join "&"
    }

    try {
        $raw = [BypassHost]::Request($url, $postData)
        if ($raw -match 'slowAES') {
            Write-Host "请求被主机挑战页拦截，绕过失败" -ForegroundColor Red
            return $null
        }
        return $raw | ConvertFrom-Json
    } catch {
        Write-Host "请求失败：$($_.Exception.Message)" -ForegroundColor Red
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
    $r = Invoke-Api -Action "latest"
    if ($r -and $r.ok) {
        if ($null -eq $r.latest) {
            Write-Host "`n服务器上还没有任何版本记录" -ForegroundColor Yellow
        } else {
            Write-Host "`n--- 最新版本 ---" -ForegroundColor Yellow
            Write-Host "  版本：$($r.latest.version)  (code=$($r.latest.code))"
            Write-Host "  通道：$($r.latest.channel)"
            Write-Host "  地址：$($r.latest.url)"
            Write-Host "  说明：$($r.latest.notes)"
            Write-Host "  强制：$(if($r.latest.force){'是'}else{'否'})"
        }
    }
}

function Show-Notices {
    $r = Invoke-Api -Action "notices"
    if ($r -and $r.ok) {
        Write-Host "`n--- 公告列表（共 $($r.notices.Count) 条）---" -ForegroundColor Yellow
        foreach ($n in $r.notices) {
            Write-Host "  [$($n.id)] $($n.title)  (优先级 $($n.priority))" -ForegroundColor Cyan
            if ($n.content) { Write-Host "      $($n.content)" -ForegroundColor Gray }
        }
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
    $r = Invoke-Api -Action "check" -Query @{ version = "0"; channel = "release" }
    if ($r -and $r.ok) {
        Write-Host "`n✔ 连接正常，服务器时间：$([datetime]'1970-01-01').AddSeconds($r.serverTime).ToLocalTime()" -ForegroundColor Green
        Write-Host "  当前记录版本数：$(if($null -eq $r.latest){0}else{1})"
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
