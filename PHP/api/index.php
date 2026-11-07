<?php
/**
 * Plain Craft Launcher by Slate_xd · 后端接口
 *
 * 设计要点：
 *   · 投稿 / 反馈向所有用户开放，无需注册
 *   · 记录 IP（哈希后存储），QQ 号选填
 *   · 多重限流防刷：单 IP 间隔 + 单 IP 每日上限 + 内容重复检测
 *   · 反馈状态变化时通过 SMTP 向用户 QQ 邮箱发通知
 *   · 管理端需要密码登录
 *   · 软件端用 X-PCL3-Key 区分
 */

// ==================== 配置 ====================
const ADMIN_PASSWORD_HASH = '$2y$12$3adkCrYrPCtNttjnkVA0zeXgCePY.SAECGglwQnB7VZARY/5gAKku';
const CLIENT_API_KEY     = '650904d4c38c8f53ee1a96ea6eaa519eb18a3358e1fe3736ed59807c06103a4b';
const SESSION_SALT       = '1f2d1ccdb5a39e187b346e2e58aa18bf';
const SESSION_TTL        = 43200;      // 12 小时
const IP_SALT            = 'pcl3_ip_salt_9f3a2c';

const DATA_DIR = __DIR__ . '/data';

// SMTP 配置（QQ 邮箱）
const SMTP_HOST   = 'ssl://smtp.qq.com';
const SMTP_PORT   = 465;
const SMTP_USER   = '1740443398@qq.com';
const SMTP_PASS   = 'mwydlrxfkkkqdejg';
const SMTP_FROM   = '1740443398@qq.com';
const SMTP_FROM_NAME = 'PCL 反馈中心';
const SMTP_ENABLED = true;

// 限流参数
const RATE_MIN_INTERVAL = 60;      // 同一 IP 两次提交最短间隔（秒）
const RATE_DAILY_LIMIT  = 10;      // 同一 IP 每日最多提交条数
const RATE_DUP_WINDOW   = 3600;    // 相同内容判重窗口（秒）

// ==================== 基础 ====================
// 错误处理：生产环境绝不把错误细节返回给客户端（会泄露路径与内部结构），
// 只写日志；调试时通过 HTTP_X_PCL3_DEBUG 头临时开启。
ini_set('display_errors', '0');
ini_set("log_errors", "1");
error_reporting(E_ALL);
if (($_SERVER['HTTP_X_PCL3_DEBUG'] ?? '') === '1') {
    ini_set('display_errors', '1');
}
header('Content-Type: application/json; charset=utf-8');
header('X-Content-Type-Options: nosniff');
header('Cache-Control: no-store');
// 安全响应头：API 只返回 JSON，不需要 CSP；但应阻止被嵌入与 Referer 泄露
header('X-Frame-Options: DENY');
header('Referrer-Policy: no-referrer');
header('Permissions-Policy: geolocation=(), microphone=(), camera=()');

/**
 * 推断本站基础地址（协议 + 域名 + 路径）。
 * 不硬编码域名，换部署环境后自动适配。
 */
function siteBase() {
    $https = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off')
          || (($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '') === 'https')
          || intval($_SERVER['SERVER_PORT'] ?? 0) === 443;
    $scheme = $https ? 'https' : 'http';
    $host = $_SERVER['HTTP_HOST'] ?? 'localhost';
    $dir = rtrim(str_replace('\\', '/', dirname($_SERVER['SCRIPT_NAME'] ?? '/')), '/');
    if (substr($dir, -4) === '/api') $dir = substr($dir, 0, -4);
    return $scheme . '://' . $host . $dir;
}

function respond($data, $code = 200) {
    http_response_code($code);
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}
function fail($msg, $code = 400) { respond(['ok' => false, 'error' => $msg], $code); }

function ensureDir($p) { if (!is_dir($p)) @mkdir($p, 0755, true); }

/**
 * 校验数据文件名，拒绝任何路径穿越。
 * 目前所有调用点传的都是字面量，但仍要挡住将来可能引入的动态名字：
 * 一旦 $name 里混进 "../"，就会写到 data 目录之外。
 */
function safeDataName($name) {
    if (!is_string($name) || $name === '') fail('非法的数据名', 500);
    if (!preg_match('/^[A-Za-z0-9_\-]+$/', $name)) fail('非法的数据名', 500);
    return $name;
}

function readJson($name, $default) {
    $name = safeDataName($name);
    $p = DATA_DIR . '/' . $name . '.json';
    if (!file_exists($p)) return $default;
    $raw = @file_get_contents($p);
    if ($raw === false || trim($raw) === '') return $default;
    $d = json_decode($raw, true);
    return $d === null ? $default : $d;
}

function writeJson($name, $data) {
    $name = safeDataName($name);
    ensureDir(DATA_DIR);
    $p = DATA_DIR . '/' . $name . '.json';
    $tmp = $p . '.tmp';
    $json = json_encode($data, JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);
    if (@file_put_contents($tmp, $json, LOCK_EX) === false) fail('写入失败', 500);
    if (!@rename($tmp, $p)) { @unlink($tmp); fail('替换失败', 500); }
}

function clientIp() {
    foreach (['HTTP_CF_CONNECTING_IP', 'HTTP_X_FORWARDED_FOR', 'REMOTE_ADDR'] as $k) {
        if (!empty($_SERVER[$k])) {
            $ip = trim(explode(',', $_SERVER[$k])[0]);
            if (filter_var($ip, FILTER_VALIDATE_IP)) return $ip;
        }
    }
    return '0.0.0.0';
}
function ipHash($ip = null) { return substr(hash('sha256', ($ip ?: clientIp()) . IP_SALT), 0, 16); }

// ==================== 限流 ====================
/**
 * 检查并记录一次提交。返回 true 表示放行。
 * 记录结构：rate.json = { "iphash": [timestamp, timestamp, ...] }
 */
function rateCheck($kind, $contentHash = '') {
    ensureDir(DATA_DIR);
    $rate = readJson('_rate', []);
    $h = ipHash();
    $now = time();
    $key = $kind . ':' . $h;

    // 清理 24 小时前的记录
    if (isset($rate[$key])) {
        $rate[$key] = array_values(array_filter($rate[$key], fn($t) => ($now - $t) < 86400));
    } else {
        $rate[$key] = [];
    }

    $list = $rate[$key];
    if (!empty($list)) {
        $last = max($list);
        $wait = RATE_MIN_INTERVAL - ($now - $last);
        if ($wait > 0) {
            fail("提交太频繁，请 {$wait} 秒后再试", 429);
        }
        if (count($list) >= RATE_DAILY_LIMIT) {
            fail('今日提交次数已达上限，请明天再来', 429);
        }
    }

    // 相同内容判重
    if ($contentHash !== '') {
        $dup = readJson('_dup', []);
        $dk = $kind . ':' . $h . ':' . $contentHash;
        if (isset($dup[$dk]) && ($now - $dup[$dk]) < RATE_DUP_WINDOW) {
            return ['dup' => true];
        }
        $dup[$dk] = $now;
        // 清理过期项
        foreach ($dup as $k2 => $t2) { if (($now - $t2) > 86400) unset($dup[$k2]); }
        writeJson('_dup', $dup);
    }

    $rate[$key][] = $now;
    writeJson('_rate', $rate);
    return ['dup' => false];
}

// ==================== 认证 ====================
function makeToken() {
    $exp = time() + SESSION_TTL;
    $payload = $exp . '.' . bin2hex(random_bytes(8));
    return $payload . '.' . hash_hmac('sha256', $payload, SESSION_SALT);
}
function verifyToken($token) {
    if (!$token) return false;
    $parts = explode('.', $token);
    if (count($parts) !== 3) return false;
    [$exp, $nonce, $sig] = $parts;
    if ((int)$exp < time()) return false;
    return hash_equals(hash_hmac('sha256', $exp . '.' . $nonce, SESSION_SALT), $sig);
}
/**
 * 取管理端 token。
 * 注意：Apache/CGI 常把 Authorization 头丢掉，
 * 所以主用自定义头 X-PCL3-Token，Authorization 与查询参数作后备。
 */
function getBearer() {
    // 1) 自定义头（最可靠）
    $t = $_SERVER['HTTP_X_PCL3_TOKEN'] ?? $_SERVER['REDIRECT_HTTP_X_PCL3_TOKEN'] ?? '';
    if ($t === '' && function_exists('getallheaders')) {
        foreach (getallheaders() as $k => $v) {
            if (strcasecmp($k, 'X-PCL3-Token') === 0) { $t = $v; break; }
        }
    }
    if ($t !== '') return trim($t);

    // 2) Authorization: Bearer xxx
    $h = $_SERVER['HTTP_AUTHORIZATION'] ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '';
    if ($h === '' && function_exists('getallheaders')) {
        foreach (getallheaders() as $k => $v) {
            if (strcasecmp($k, 'Authorization') === 0) { $h = $v; break; }
        }
    }
    if ($h !== '' && stripos($h, 'Bearer ') === 0) return substr($h, 7);
    if ($h !== '') return $h;

    // 3) 查询/POST 参数
    return $_POST['token'] ?? $_GET['token'] ?? '';
}
function requireAdmin() {
    if (!verifyToken(getBearer())) fail('未登录或登录已过期', 401);
}
function isClient() {
    $key = $_SERVER['HTTP_X_PCL3_KEY'] ?? $_SERVER['REDIRECT_HTTP_X_PCL3_KEY'] ?? '';
    if ($key === '' && function_exists('getallheaders')) {
        foreach (getallheaders() as $k => $v) {
            if (strcasecmp($k, 'X-PCL3-Key') === 0) { $key = $v; break; }
        }
    }
    if ($key === '' && isset($_POST['key'])) $key = $_POST['key'];
    return hash_equals(CLIENT_API_KEY, $key);
}
function requireClient() { if (!isClient()) fail('禁止访问', 403); }

// ==================== SMTP ====================
/**
 * 极简 SMTP 客户端，支持 SSL/TLS，用于 QQ 邮箱发信。
 * 失败只记录日志，不影响主流程。
 */
// SMTP 限流参数
// ===== 邮件发送：队列 + 每分钟 30 封限速 =====
const MAIL_PER_MINUTE      = 30;    // 每分钟最多发 30 封
const MAIL_PER_ADDR_DAILY  = 20;    // 同一收件人每日上限（防滥用）
const MAIL_QUEUE_MAX       = 500;   // 队列长度上限
const MAIL_MAX_ATTEMPTS    = 3;     // 单封最大重试次数

/**
 * 取当前分钟窗口内的发信记录。
 */
function mailRateState() {
    $s = readJson('_mail_rate', []);
    if (!is_array($s)) $s = [];
    $now = time();

    // 归一化：只保留整数时间戳，兼容任何历史遗留结构
    $sent = [];
    if (isset($s['sent']) && is_array($s['sent'])) {
        foreach ($s['sent'] as $v) {
            $t2 = is_numeric($v) ? (int)$v : 0;
            if ($t2 > 0 && ($now - $t2) < 60) $sent[] = $t2;
        }
    }
    $addrs = [];
    if (isset($s['addrs']) && is_array($s['addrs'])) {
        foreach ($s['addrs'] as $addr => $list) {
            if (!is_array($list)) continue;
            $keep = [];
            foreach ($list as $v) {
                $t2 = is_numeric($v) ? (int)$v : 0;
                if ($t2 > 0 && ($now - $t2) < 86400) $keep[] = $t2;
            }
            if ($keep) $addrs[$addr] = $keep;
        }
    }
    return ['sent' => $sent, 'addrs' => $addrs];
}

/**
 * 入队一封邮件，并立刻尝试投递。
 * 返回 true 表示已入队（不代表已发出）。
 */
function smtpSend($to, $subject, $bodyHtml) {
    if (!SMTP_ENABLED || !$to || !filter_var($to, FILTER_VALIDATE_EMAIL)) return false;

    // 同一收件人每日上限，防止对单个邮箱轰炸
    $state = mailRateState();
    if (isset($state['addrs'][$to]) && count($state['addrs'][$to]) >= MAIL_PER_ADDR_DAILY) {
        smtpLog("跳过：$to 今日已达上限");
        return false;
    }

    $q = readJson('_mail_queue', []);
    if (count($q) >= MAIL_QUEUE_MAX) {
        // 队列满，丢弃最旧的
        $dropped = array_shift($q);
        smtpLog("队列已满，丢弃最旧邮件：" . ($dropped['to'] ?? '?'));
    }
    $q[] = [
        'id'       => 'm' . time() . mt_rand(100, 999),
        'to'       => $to,
        'subject'  => $subject,
        'html'     => $bodyHtml,
        'created'  => time(),
        'attempts' => 0,
    ];
    writeJson('_mail_queue', $q);

    // 立刻尝试发送，剩余的自然留在队列里
    smtpProcessQueue();
    return true;
}

/**
 * 处理队列：本轮最多发到剩下一分钟 30 封的额度。
 */
function smtpProcessQueue() {
    $q = readJson('_mail_queue', []);
    if (!$q) return 0;

    $state = mailRateState();
    $budget = MAIL_PER_MINUTE - count($state['sent']);
    if ($budget <= 0) return 0;

    $remain = [];
    $sent = 0;
    foreach ($q as $item) {
        if ($sent >= $budget) { $remain[] = $item; continue; }

        $to = $item['to'] ?? '';
        $ok = smtpSendRaw($to, $item['subject'] ?? '', $item['html'] ?? '');

        if ($ok) {
            $state['sent'][] = time();
            $state['addrs'][$to][] = time();
            $sent++;
        } else {
            $item['attempts'] = ($item['attempts'] ?? 0) + 1;
            if ($item['attempts'] < MAIL_MAX_ATTEMPTS) {
                $remain[] = $item;
            } else {
                smtpLog("放弃投递（重试 {$item['attempts']} 次）：$to");
            }
        }
    }

    writeJson('_mail_rate', $state);
    writeJson('_mail_queue', $remain);
    return $sent;
}

/**
 * 真正的 SMTP 投递（不含限流，仅供队列内部调用）。
 */
function smtpSendRaw($to, $subject, $bodyHtml) {
    if (!$to || !filter_var($to, FILTER_VALIDATE_EMAIL)) return false;

    $errno = 0; $errstr = '';
    $ctx = stream_context_create(['ssl' => [
        'verify_peer' => false, 'verify_peer_name' => false,
        'allow_self_signed' => true, 'timeout' => 12,
    ]]);
    $fp = @stream_socket_client(SMTP_HOST . ':' . SMTP_PORT, $errno, $errstr, 12,
        STREAM_CLIENT_CONNECT, $ctx);
    if (!$fp) { smtpLog("连接失败 $errno $errstr"); return false; }
    stream_set_timeout($fp, 12);

    $read = function () use ($fp) {
        $out = '';
        while (($line = fgets($fp, 515)) !== false) {
            $out .= $line;
            if (strlen($line) < 4 || $line[3] !== '-') break;
        }
        return $out;
    };
    $cmd = function ($c, $expect = null) use ($fp, $read) {
        fwrite($fp, $c . "\r\n");
        $r = $read();
        if ($expect !== null && strpos($r, $expect) !== 0) return false;
        return $r;
    };

    if (strpos($read(), '220') !== 0) { smtpLog('握手失败'); fclose($fp); return false; }
    $cmd('EHLO pcl3.local');
    if ($cmd('AUTH LOGIN', '334') === false) { smtpLog('AUTH 失败'); fclose($fp); return false; }
    if ($cmd(base64_encode(SMTP_USER), '334') === false) { smtpLog('用户名失败'); fclose($fp); return false; }
    if ($cmd(base64_encode(SMTP_PASS), '235') === false) { smtpLog('密码失败'); fclose($fp); return false; }

    if ($cmd('MAIL FROM:<' . SMTP_FROM . '>', '250') === false) { smtpLog('MAIL FROM 失败'); fclose($fp); return false; }
    $rcpt = $cmd('RCPT TO:<' . $to . '>');
    if ($rcpt === false) { smtpLog("RCPT 失败: $rcpt"); fclose($fp); return false; }
    if (strpos($rcpt, '250') !== 0 && strpos($rcpt, '251') !== 0) {
        smtpLog("RCPT 被拒: " . trim($rcpt)); fclose($fp); return false;
    }
    if ($cmd('DATA', '354') === false) { smtpLog('DATA 失败'); fclose($fp); return false; }

    $headers = [
        'From: =?UTF-8?B?' . base64_encode(SMTP_FROM_NAME) . '?= <' . SMTP_FROM . '>',
        'To: <' . $to . '>',
        'Subject: =?UTF-8?B?' . base64_encode($subject) . '?=',
        'MIME-Version: 1.0',
        'Content-Type: text/html; charset=UTF-8',
        'Content-Transfer-Encoding: base64',
        'Date: ' . date('r'),
        'Message-ID: <' . bin2hex(random_bytes(10)) . '@pcl3>',
    ];
    $data = implode("\r\n", $headers) . "\r\n\r\n" . chunk_split(base64_encode($bodyHtml), 76, "\r\n");
    $data = preg_replace('/^\./m', '..', $data);
    fwrite($fp, $data . "\r\n.\r\n");
    $res = $read();
    $cmd('QUIT');
    fclose($fp);

    $ok = strpos($res, '250') === 0;
    if (!$ok) smtpLog("发送失败: " . trim($res));
    return $ok;
}

function smtpLog($msg) {
    ensureDir(DATA_DIR);
    @file_put_contents(DATA_DIR . '/_smtp.log',
        date('Y-m-d H:i:s') . "  $msg\n", FILE_APPEND);
}

// ==================== 邮件模板 ====================
function mailTemplate($title, $intro, $items, $footer) {
    $rows = '';
    foreach ($items as $k => $v) {
        if ($v === '' || $v === null) continue;
        $rows .= '<tr><td style="padding:7px 14px;color:#5b636d;font-size:13px;white-space:nowrap;vertical-align:top">'
              . htmlspecialchars($k) . '</td><td style="padding:7px 14px;color:#1a1d21;font-size:13px">'
              . nl2br(htmlspecialchars($v)) . '</td></tr>';
    }
    return '<div style="background:#f5f6f8;padding:28px 16px;font-family:-apple-system,Segoe UI,Microsoft YaHei,sans-serif">'
        . '<div style="max-width:560px;margin:0 auto;background:#fff;border:1px solid #e3e6ea;border-radius:10px;overflow:hidden">'
        . '<div style="background:#0b5fd4;padding:20px 24px">'
        . '<div style="color:#fff;font-size:16px;font-weight:600">' . htmlspecialchars($title) . '</div></div>'
        . '<div style="padding:24px">'
        . '<p style="margin:0 0 18px;color:#1a1d21;font-size:14px;line-height:1.7">' . htmlspecialchars($intro) . '</p>'
        . '<table style="width:100%;border-collapse:collapse;background:#fafbfc;border-radius:8px">' . $rows . '</table>'
        . '<p style="margin:20px 0 0;color:#8b939d;font-size:12px;line-height:1.7">' . $footer . '</p>'
        . '</div></div>'
        . '<div style="max-width:560px;margin:14px auto 0;text-align:center;color:#8b939d;font-size:11px">'
        . 'Plain Craft Launcher by Slate_xd · 此邮件由系统自动发送，请勿直接回复</div>'
        . '</div>';
}

function qqToEmail($qq) {
    $qq = preg_replace('/\D/', '', (string)$qq);
    if ($qq === '' || strlen($qq) < 5) return '';
    return $qq . '@qq.com';
}

// ==================== 版本 ====================
function versionToCode($v) {
    $parts = array_pad(preg_split('/[.\-+]/', trim($v)), 4, '0');
    return intval($parts[0]) * 1000 + intval($parts[1]) * 100 + intval($parts[2]) * 10 + intval($parts[3]);
}

// ==================== 路由 ====================
$action = $_GET['action'] ?? '';

switch ($action) {

    // ---------- 登录 ----------
    case 'login':
        $pw = $_POST['password'] ?? '';
        if ($pw === '') fail('请输入密码');
        $locks = readJson('_login_lock', []);
        $ip = ipHash();
        $now = time();
        if (isset($locks[$ip]) && $locks[$ip]['count'] >= 5 && ($now - $locks[$ip]['last']) < 600) {
            fail('尝试次数过多，请 10 分钟后再试', 429);
        }
        if (!password_verify($pw, ADMIN_PASSWORD_HASH)) {
            $locks[$ip] = ['count' => ($locks[$ip]['count'] ?? 0) + 1, 'last' => $now];
            writeJson('_login_lock', $locks);
            fail('密码错误', 401);
        }
        unset($locks[$ip]);
        writeJson('_login_lock', $locks);
        respond(['ok' => true, 'token' => makeToken(), 'expiresIn' => SESSION_TTL]);
        break;

    case 'auth':
        requireAdmin();
        respond(['ok' => true]);
        break;

    // ---------- 管理端总览 ----------
    case 'admin_overview':
        requireAdmin();
        $releases = readJson('releases', []);
        $notices  = readJson('notices', []);
        $donors   = readJson('donors', []);
        $caves    = readJson('caves', []);
        $feedback = readJson('feedback', []);
        $ideas    = readJson('ideas', []);
        usort($ideas, function ($a, $b) {
            $d = ($b['votes'] ?? 0) - ($a['votes'] ?? 0);
            return $d !== 0 ? $d : (($b['time'] ?? 0) - ($a['time'] ?? 0));
        });
        foreach ($donors as $i => $d) {
            $donors[$i]['amount'] = number_format(floatval($d['amount'] ?? 0), 2, '.', '');
        }
        usort($donors, fn($a, $b) => floatval($b['amount'] ?? 0) <=> floatval($a['amount'] ?? 0));
        usort($feedback, fn($a, $b) => ($b['time'] ?? 0) - ($a['time'] ?? 0));
        usort($caves, fn($a, $b) => ($b['time'] ?? 0) - ($a['time'] ?? 0));
        $openCount = count(array_filter($feedback, fn($f) => in_array($f['status'] ?? '', ['open', 'confirmed'], true)));
        respond([
            'ok' => true,
            'stats' => [
                'releases' => count($releases), 'notices' => count($notices),
                'donors' => count($donors), 'caves' => count($caves),
                'feedback' => count($feedback), 'feedback_open' => $openCount,
                'ideas' => count($ideas), 'votes_total' => array_sum(array_map(fn($i) => intval($i['votes'] ?? 0), $ideas)),
            ],
            'releases' => $releases, 'notices' => $notices,
            'donors' => $donors, 'caves' => $caves, 'feedback' => $feedback,
            'ideas' => $ideas,
            'smtp_enabled' => SMTP_ENABLED,
            'smtp_host' => SMTP_HOST . ':' . SMTP_PORT,
        ]);
        break;

    // ---------- 发布版本 ----------
    // ---------- 清空版本记录（管理） ----------
    case 'release_clear':
        requireAdmin();
        $list = readJson('releases', []);
        $pkgDir = DATA_DIR . '/pkgs';
        foreach ($list as $r) {
            $f = $r['file'] ?? '';
            if ($f !== '' && strpos($f, 'pkgs/') === 0) {
                $path = DATA_DIR . '/' . $f;
                if (file_exists($path)) @unlink($path);
            }
        }
        writeJson('releases', []);
        respond(['ok' => true, 'cleared' => count($list)]);
        break;

    case 'publish':
        requireAdmin();
        $v = trim($_POST['version'] ?? '');
        $u = trim($_POST['url'] ?? '');
        if ($v === '') fail('缺少 version');

        $list = readJson('releases', []);
        $entry = [
            'version' => $v,
            'code'    => versionToCode($v),
            'url'     => $u,
            'sha256'  => trim($_POST['sha256'] ?? ''),
            'size'    => intval($_POST['size'] ?? 0),
            'channel' => trim($_POST['channel'] ?? 'release'),
            'notes'   => trim($_POST['notes'] ?? ''),
            'force'   => ($_POST['force'] ?? '0') === '1',
            'time'    => time(),
            'file'    => '',
        ];

        // 若有上传的安装包，落盘到 data/pkgs/
        if (!empty($_FILES['package']) && ($_FILES['package']['error'] ?? 1) === UPLOAD_ERR_OK) {
            // 上传校验：只接受 PE 可执行文件，且限制体积
            $tmpFile = $_FILES['package']['tmp_name'];
            $fileSize = @filesize($tmpFile);
            if ($fileSize === false || $fileSize < 1024) fail('安装包太小，可能不是有效文件');
            if ($fileSize > 200 * 1024 * 1024) fail('安装包超过 200 MB 上限');
            // 读前两个字节确认是 MZ（PE 文件头），防止误传其它文件
            $fh = @fopen($tmpFile, 'rb');
            if ($fh === false) fail('无法读取上传文件', 500);
            $magic = fread($fh, 2);
            fclose($fh);
            if ($magic !== 'MZ') fail('安装包不是有效的 Windows 可执行文件');
            $pkgDir = DATA_DIR . '/pkgs';
            ensureDir($pkgDir);
            $safeName = preg_replace('/[^A-Za-z0-9._-]/', '_', $v) . '.exe';
            $dest = $pkgDir . '/' . $safeName;
            if (!@move_uploaded_file($_FILES['package']['tmp_name'], $dest)) {
                fail('安装包保存失败，请检查 data/pkgs 权限', 500);
            }
            $entry['file'] = 'pkgs/' . $safeName;
            $entry['size'] = filesize($dest);
            $entry['sha256'] = hash_file('sha256', $dest);
        }

        // 同一版本号覆盖，否则新增
        $found = false;
        foreach ($list as $i => $r) {
            if (($r['version'] ?? '') === $v) {
                // 保留旧文件名，避免覆盖时丢失
                if ($entry['file'] === '' && !empty($r['file'])) $entry['file'] = $r['file'];
                $list[$i] = $entry;
                $found = true;
                break;
            }
        }
        if (!$found) $list[] = $entry;

        // 只保留最新 5 个版本（按版本码降序），同时清理旧安装包
        usort($list, fn($a, $b) => ($b['code'] ?? 0) <=> ($a['code'] ?? 0));
        $keep = array_slice($list, 0, 5);
        $dropped = array_slice($list, 5);
        $pkgDir = DATA_DIR . '/pkgs';
        foreach ($dropped as $d) {
            $f = $d['file'] ?? '';
            if ($f !== '' && strpos($f, 'pkgs/') === 0) {
                $path = DATA_DIR . '/' . $f;
                if (file_exists($path)) {
                    @unlink($path);
                    smtpLog('清理旧版本安装包：' . $f);
                }
            }
        }
        writeJson('releases', $keep);

        respond([
            'ok' => true,
            'published' => $entry,
            'kept' => count($keep),
            'dropped' => count($dropped),
        ]);
        break;

    // ---------- 公告 ----------
    case 'notice':
        requireAdmin();
        $t = trim($_POST['title'] ?? '');
        if ($t === '') fail('缺少 title');
        $list = readJson('notices', []);
        $id = trim($_POST['id'] ?? '') ?: ('n' . time() . mt_rand(100, 999));
        $e = ['id' => $id, 'title' => $t, 'content' => trim($_POST['content'] ?? ''),
              'priority' => intval($_POST['priority'] ?? 0), 'expire' => intval($_POST['expire'] ?? 0), 'time' => time()];
        $found = false;
        foreach ($list as $i => $n) { if (($n['id'] ?? '') === $id) { $list[$i] = $e; $found = true; break; } }
        if (!$found) $list[] = $e;
        writeJson('notices', $list);
        respond(['ok' => true, 'notice' => $e]);
        break;

    case 'delete_notice':
        requireAdmin();
        $id = trim($_POST['id'] ?? '');
        $list = array_values(array_filter(readJson('notices', []), fn($n) => ($n['id'] ?? '') !== $id));
        writeJson('notices', $list);
        respond(['ok' => true, 'total' => count($list)]);
        break;

    // ---------- 赞助者 ----------
    case 'donor':
        requireAdmin();
        $name = trim($_POST['name'] ?? '');
        if ($name === '') fail('缺少 name');
        $list = readJson('donors', []);
        $e = ['name' => $name, 'amount' => number_format(floatval($_POST['amount'] ?? 0), 2, '.', ''),
              'message' => trim($_POST['message'] ?? ''), 'hidden' => ($_POST['hidden'] ?? '') === '1', 'time' => time()];
        $found = false;
        foreach ($list as $i => $d) {
            if (($d['name'] ?? '') === $name) {
                $e['amount'] = number_format(floatval($e['amount']) + floatval($d['amount'] ?? 0), 2, '.', '');
                $e['time'] = $d['time'] ?? time();
                $list[$i] = $e; $found = true; break;
            }
        }
        if (!$found) $list[] = $e;
        writeJson('donors', $list);
        respond(['ok' => true, 'donor' => $e]);
        break;

    case 'delete_donor':
        requireAdmin();
        $name = trim($_POST['name'] ?? '');
        $list = array_values(array_filter(readJson('donors', []), fn($d) => ($d['name'] ?? '') !== $name));
        writeJson('donors', $list);
        respond(['ok' => true, 'total' => count($list)]);
        break;

    // ---------- 反馈：状态变更（带邮件通知） ----------
    case 'feedback_update':
        requireAdmin();
        $id = trim($_POST['id'] ?? '');
        if ($id === '') fail('缺少 id');
        $newStatus = trim($_POST['status'] ?? '');
        $reply     = isset($_POST['reply']) ? trim($_POST['reply']) : null;
        $list = readJson('feedback', []);
        $target = null; $idx = -1;
        foreach ($list as $i => $f) {
            if (($f['id'] ?? '') === $id) { $target = $f; $idx = $i; break; }
        }
        if ($target === null) fail('未找到', 404);
        $oldStatus = $target['status'] ?? 'open';
        if ($newStatus !== '') $list[$idx]['status'] = $newStatus;
        if ($reply !== null)  $list[$idx]['reply']  = $reply;
        $list[$idx]['updated'] = time();
        writeJson('feedback', $list);

        // 状态变化才发信
        $mailed = false;
        if ($newStatus !== '' && $newStatus !== $oldStatus) {
            $mailed = notifyFeedback($list[$idx], $oldStatus, $newStatus);
        }
        respond(['ok' => true, 'mailed' => $mailed]);
        break;

    case 'feedback_delete':
        requireAdmin();
        $id = trim($_POST['id'] ?? '');
        $list = array_values(array_filter(readJson('feedback', []), fn($f) => ($f['id'] ?? '') !== $id));
        writeJson('feedback', $list);
        respond(['ok' => true, 'total' => count($list)]);
        break;

    // ---------- 回声洞审核（带邮件通知） ----------
    case 'cave_update':
        requireAdmin();
        $id = trim($_POST['id'] ?? '');
        $op = $_POST['op'] ?? 'approve';
        $list = readJson('caves', []);
        $out = []; $target = null;
        foreach ($list as $c) {
            if (($c['id'] ?? '') === $id) {
                $target = $c;
                if ($op === 'delete') continue;
                if ($op === 'approve') $c['approved'] = true;
                if ($op === 'reject')  $c['approved'] = false;
            }
            $out[] = $c;
        }
        writeJson('caves', $out);
        $mailed = false;
        if ($target !== null && in_array($op, ['approve', 'reject'], true)) {
            $mailed = notifyCave($target, $op);
        }
        respond(['ok' => true, 'total' => count($out), 'mailed' => $mailed]);
        break;

    // ============================================================
    // ---------- 邮件队列：查看状态（管理） ----------
    case 'mail_queue':
        requireAdmin();
        $q = readJson('_mail_queue', []);
        $state = mailRateState();
        $out = [];
        foreach ($q as $m) {
            $out[] = [
                'id' => $m['id'] ?? '', 'to' => $m['to'] ?? '',
                'subject' => $m['subject'] ?? '', 'attempts' => $m['attempts'] ?? 0,
                'created' => $m['created'] ?? 0,
            ];
        }
        respond([
            'ok' => true,
            'queue' => $out,
            'queue_len' => count($out),
            'sent_last_minute' => count($state['sent']),
            'per_minute_limit' => MAIL_PER_MINUTE,
            'per_addr_daily' => MAIL_PER_ADDR_DAILY,
        ]);
        break;

    // ---------- 邮件队列：手动触发投递（管理） ----------
    case 'mail_run':
        requireAdmin();
        $n = smtpProcessQueue();
        $q = readJson('_mail_queue', []);
        respond(['ok' => true, 'sent' => $n, 'remaining' => count($q)]);
        break;

    // ---------- 邮件队列：清空（管理） ----------
    case 'mail_clear':
        requireAdmin();
        writeJson('_mail_queue', []);
        respond(['ok' => true]);
        break;
    // 公开接口（无需登录，浏览器与软件都能用）
    // ============================================================

    // ---------- 公开统计（前台展示用） ----------
    // ---------- 新功能投票：跳转到站点投票页 ----------
    case 'vote_page':
        // 投票已内嵌到首页，不再单独跳 /vote/
        header('Location: ' . siteBase() . '/#vote', true, 302);
        exit;

    // ---------- 反馈页 / 查询页跳转（客户端引流用） ----------
    case 'feedback_page':
        header('Location: ' . siteBase() . '/#feedback', true, 302);
        exit;

    case 'track_page':
        header('Location: ' . siteBase() . '/#track', true, 302);
        exit;

    // ---------- 回声洞投稿页跳转（客户端引流用） ----------
    case 'cave_page':
        header('Location: ' . siteBase() . '/#cave', true, 302);
        exit;

    // ---------- 联机配置（客户端引流联机模块下载用） ----------
    // PCL 开源版把 ServerConfig 写死成"无法联网获取"，这里由自建服务端提供，
    // 让联机功能真正可用。字段与 PCL 原生 ServerConfig.Link 保持一致。
    case 'link_config':
        header('Content-Type: application/json; charset=utf-8');
        echo json_encode([
            // 联机改用 Terracotta（陶瓦联机）：
            // 它自带完整的 HTTP API 与 NAT 难度评估，比手写 EasyTier 命令行可靠。
            // 集成方式遵循其 AGPL 例外条款第 2 条（通过 HTTP API 交互 + 界面标注版权）。
            'Terracotta' => [
                'Version' => '0.4.2',
                'Downloads' => [
                    'https://github.com/burningtnt/Terracotta/releases/download/v0.4.2/terracotta-0.4.2-windows-{arch}-pkg.tar.gz',
                    'https://ghfast.top/https://github.com/burningtnt/Terracotta/releases/download/v0.4.2/terracotta-0.4.2-windows-{arch}-pkg.tar.gz',
                    'https://gh-proxy.com/https://github.com/burningtnt/Terracotta/releases/download/v0.4.2/terracotta-0.4.2-windows-{arch}-pkg.tar.gz',
                ],
                'Copyright' => 'Terracotta 陶瓦联机 by burningtnt',
                'License' => 'AGPL-3.0-or-later（本项目通过 HTTP API 交互，符合其例外条款）',
            ],
            'MinVersionCode' => 100,
        ], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
        exit;

    // ---------- 新功能投票：列表（公开） ----------
    case 'vote_list':
        $list = readJson('ideas', []);
        usort($list, function ($a, $b) {
            $d = ($b['votes'] ?? 0) - ($a['votes'] ?? 0);
            if ($d !== 0) return $d;
            return ($b['time'] ?? 0) - ($a['time'] ?? 0);
        });
        $out = [];
        foreach ($list as $it) {
            $out[] = [
                'id' => $it['id'] ?? '',
                'title' => $it['title'] ?? '',
                'desc' => $it['desc'] ?? '',
                'category' => $it['category'] ?? '',
                'votes' => intval($it['votes'] ?? 0),
                'time' => $it['time'] ?? 0,
            ];
        }
        respond(['ok' => true, 'ideas' => $out, 'total' => count($out)]);
        break;

    // ---------- 新功能投票：提交 ----------
    case 'vote_submit':
        $title = trim($_POST['title'] ?? '');
        $desc = trim($_POST['desc'] ?? '');
        if ($title === '') fail('标题不能为空');
        if (mb_strlen($title, 'UTF-8') > 80) fail('标题过长（最多 80 字）');
        if (mb_strlen($desc, 'UTF-8') > 1000) fail('说明过长（最多 1000 字）');
        $rc = rateCheck('vote', md5($title));
        if ($rc['dup']) fail('这个想法刚刚已经提交过了', 429);
        $list = readJson('ideas', []);
        $now = time();
        $e = [
            'id' => 'v' . $now . mt_rand(100, 999),
            'title' => $title,
            'desc' => $desc,
            'category' => trim($_POST['category'] ?? '其他'),
            'qq' => preg_replace('/\D/', '', $_POST['qq'] ?? ''),
            'votes' => 1,
            'time' => $now,
            'ip' => ipHash(),
        ];
        $list[] = $e;
        writeJson('ideas', $list);
        respond(['ok' => true, 'idea' => ['id' => $e['id']]]);
        break;

    // ---------- 新功能投票：删除（管理） ----------
    case 'vote_delete':
        requireAdmin();
        $id = trim($_POST['id'] ?? $_GET['id'] ?? '');
        if ($id === '') fail('缺少 id');
        $list = readJson('ideas', []);
        $before = count($list);
        $list = array_values(array_filter($list, fn($x) => ($x['id'] ?? '') !== $id));
        if (count($list) === $before) fail('未找到该想法', 404);
        writeJson('ideas', $list);
        respond(['ok' => true, 'removed' => 1, 'total' => count($list)]);
        break;

    // ---------- 新功能投票：投一票 ----------
    case 'vote':
        $id = trim($_POST['id'] ?? $_GET['id'] ?? '');
        if ($id === '') fail('缺少 id');
        $list = readJson('ideas', []);
        $found = false;
        $now = time();
        foreach ($list as $i => $it) {
            if (($it['id'] ?? '') === $id) {
                // 同一 IP 对同一想法 24 小时内只能投一次
                $voters = $it['voters'] ?? [];
                $h = ipHash();
                if (isset($voters[$h]) && ($now - $voters[$h]) < 86400) {
                    respond(['ok' => false, 'error' => '你已经支持过这个想法了']);
                }
                $voters[$h] = $now;
                // 清理 24 小时前的记录
                foreach ($voters as $k => $t2) { if (($now - $t2) > 86400) unset($voters[$k]); }
                $list[$i]['voters'] = $voters;
                $list[$i]['votes'] = intval($it['votes'] ?? 0) + 1;
                $found = true;
                break;
            }
        }
        if (!$found) fail('未找到该想法', 404);
        writeJson('ideas', $list);
        respond(['ok' => true]);
        break;

    case 'stats':
        $caves = readJson('caves', []);
        $okCaves = 0;
        foreach ($caves as $c) { if (($c['approved'] ?? true) === true) $okCaves++; }
        $fb = readJson('feedback', []);
        $resolved = 0;
        foreach ($fb as $f2) {
            if (($f2['status'] ?? '') === 'resolved') $resolved++;
        }
        respond([
            'ok' => true,
            'caves' => $okCaves,
            'feedback' => count($fb),
            'resolved' => $resolved,
            'serverTime' => time(),
        ]);
        break;
    // ---------- 回声洞：随机一条 ----------
    case 'cave':
        $list = readJson('caves', []);
        $ok = array_values(array_filter($list, fn($c) => ($c['approved'] ?? true) === true));
        if (!$ok) respond(['ok' => true, 'cave' => null]);
        respond(['ok' => true, 'cave' => $ok[mt_rand(0, count($ok) - 1)]]);
        break;

    // ---------- 回声洞：投稿（开放） ----------
    case 'cave_submit':
        $text = trim($_POST['text'] ?? '');
        $qq   = preg_replace('/\D/', '', $_POST['qq'] ?? '');
        if ($text === '') fail('留言不能为空');
        if (mb_strlen($text, 'UTF-8') > 200) fail('留言过长（最多 200 字）');
        if ($qq !== '' && (strlen($qq) < 5 || strlen($qq) > 12)) fail('QQ 号格式不正确');

        $rc = rateCheck('cave', md5($text));
        if ($rc['dup']) fail('这条留言刚刚已经投过了', 429);

        $list = readJson('caves', []);
        $now = time();
        $e = ['id' => 'c' . $now . mt_rand(100, 999), 'text' => $text,
              'author' => trim($_POST['author'] ?? ''), 'qq' => $qq,
              'approved' => true, 'time' => $now, 'ip' => ipHash()];
        $list[] = $e;
        writeJson('caves', $list);

        $mailed = false;
        if ($qq !== '') $mailed = notifyCaveSubmit($e);
        respond(['ok' => true, 'cave' => ['id' => $e['id']], 'mailed' => $mailed]);
        break;

    // ---------- 反馈：提交（开放） ----------
    case 'feedback':
        $title   = trim($_POST['title'] ?? '');
        $content = trim($_POST['content'] ?? '');
        $qq      = preg_replace('/\D/', '', $_POST['qq'] ?? '');
        if ($title === '') fail('标题不能为空');
        if (mb_strlen($title, 'UTF-8') > 100) fail('标题过长（最多 100 字）');
        if (mb_strlen($content, 'UTF-8') > 4000) fail('内容过长（最多 4000 字）');
        if ($qq !== '' && (strlen($qq) < 5 || strlen($qq) > 12)) fail('QQ 号格式不正确');

        $rc = rateCheck('fb', md5($title . '|' . $content));
        if ($rc['dup']) fail('这条反馈刚刚已经提交过了', 429);

        $list = readJson('feedback', []);
        $now = time();
        $e = ['id' => 'f' . $now . mt_rand(100, 999), 'title' => $title, 'content' => $content,
              'category' => trim($_POST['category'] ?? '其他'), 'qq' => $qq,
              'contact' => trim($_POST['contact'] ?? ''),
              'version' => trim($_POST['version'] ?? ''), 'system' => trim($_POST['system'] ?? ''),
              'log' => trim($_POST['log'] ?? ''), 'status' => 'open', 'time' => $now, 'ip' => ipHash()];
        $list[] = $e;
        writeJson('feedback', $list);

        $mailed = false;
        if ($qq !== '') $mailed = notifyFeedbackSubmit($e);
        respond(['ok' => true, 'feedback' => ['id' => $e['id']], 'mailed' => $mailed]);
        break;

    // ---------- 查询自己的提交进度 ----------
    case 'track':
        $id = trim($_POST['id'] ?? $_GET['id'] ?? '');
        if ($id === '') fail('缺少编号');
        $fb = readJson('feedback', []);
        foreach ($fb as $f) {
            if (($f['id'] ?? '') === $id) {
                respond(['ok' => true, 'type' => 'feedback', 'item' => [
                    'id' => $f['id'], 'title' => $f['title'], 'status' => $f['status'] ?? 'open',
                    'reply' => $f['reply'] ?? '', 'time' => $f['time'] ?? 0, 'updated' => $f['updated'] ?? 0,
                ]]);
            }
        }
        $cv = readJson('caves', []);
        foreach ($cv as $c) {
            if (($c['id'] ?? '') === $id) {
                respond(['ok' => true, 'type' => 'cave', 'item' => [
                    'id' => $c['id'], 'text' => $c['text'],
                    'status' => ($c['approved'] ?? true) ? 'approved' : 'rejected',
                    'time' => $c['time'] ?? 0,
                ]]);
            }
        }
        fail('未找到该编号', 404);
        break;

    // ============================================================
    // 软件端接口（需要 X-PCL3-Key）
    // ============================================================

    // ---------- 安装包下载（PHP 中转，绕过主机对静态文件的防盗链） ----------
    case 'download':
        $ver = $_GET['version'] ?? '';
        $list = readJson('releases', []);
        $target = null;
        foreach ($list as $r) {
            if (($r['version'] ?? '') === $ver) { $target = $r; break; }
        }
        if ($target === null || empty($target['file'])) {
            http_response_code(404);
            header('Content-Type: text/plain; charset=utf-8');
            echo 'not found';
            exit;
        }
        $path = DATA_DIR . '/' . $target['file'];
        if (!file_exists($path)) {
            http_response_code(404);
            header('Content-Type: text/plain; charset=utf-8');
            echo 'file missing';
            exit;
        }
        $name = preg_replace('/[^A-Za-z0-9._-]/', '_', $target['version']) . '.exe';
        header('Content-Type: application/octet-stream');
        header('Content-Disposition: attachment; filename="' . $name . '"');
        header('Content-Length: ' . filesize($path));
        header('Cache-Control: no-store');
        readfile($path);
        exit;

    case 'check':
        requireClient();
        $curRaw = $_GET['version'] ?? '0';
        $ch = $_GET['channel'] ?? 'release';
        $cur = (strpos($curRaw, '.') !== false) ? versionToCode($curRaw) : intval($curRaw);
        $releases = readJson('releases', []);
        $notices  = readJson('notices', []);
        $best = null;
        foreach ($releases as $r) {
            if (($r['channel'] ?? '') !== $ch) continue;
            if ($best === null || ($r['code'] ?? 0) > ($best['code'] ?? 0)) $best = $r;
        }
        $now = time();
        $active = [];
        foreach ($notices as $n) {
            if (($n['expire'] ?? 0) > 0 && $n['expire'] < $now) continue;
            $active[] = $n;
        }
        usort($active, fn($a, $b) => (($b['priority'] ?? 0) - ($a['priority'] ?? 0)) ?: (($b['time'] ?? 0) - ($a['time'] ?? 0)));
        // 只返回前 5 条（优先级高的在前，同优先级按时间倒序）
        $active = array_slice($active, 0, 5);
        // 为最新版补上可直接下载的地址（服务器自存优先，外部 url 作为备用）
        if ($best !== null) {
            $dl = [];
            if (!empty($best['file'])) {
                $dl[] = 'https://pcl3.byethost9.com/api/index.php?action=download&version=' . rawurlencode($best['version']);
            }
            if (!empty($best['url'])) {
                $dl[] = $best['url'];
            }
            $best['downloadUrls'] = $dl;
            $best['downloadUrl'] = $dl[0] ?? '';
        }

        respond(['ok' => true, 'hasUpdate' => ($best !== null && ($best['code'] ?? 0) > $cur),
                 'current' => $cur, 'latest' => $best, 'notices' => $active, 'serverTime' => $now]);
        break;

    case 'donors':
        requireClient();
        $list = readJson('donors', []);
        $out = [];
        foreach ($list as $d) {
            if (!empty($d['hidden'])) continue;
            $d['amount'] = number_format(floatval($d['amount'] ?? 0), 2, '.', '');
            $out[] = $d;
        }
        // 置顶项排最前，其余按金额降序
        usort($out, function ($a, $b) {
            $pa = !empty($a['pinned']) ? 1 : 0;
            $pb = !empty($b['pinned']) ? 1 : 0;
            if ($pa !== $pb) return $pb - $pa;
            return floatval($b['amount']) <=> floatval($a['amount']);
        });
        respond(['ok' => true, 'donors' => $out, 'total' => count($out)]);
        break;

    default:
        fail('未知接口', 404);
}

// ==================== 邮件通知实现 ====================
function statusLabel($s) {
    $map = [
        'open'      => '已提交，等待处理',
        'confirmed' => '已复现（待解决）',
        'resolved'  => '已解决',
        'rejected'  => '已驳回',
        'cannot'    => '无法复现（无法解决）',
        'duplicate' => '重复反馈',
    ];
    return $map[$s] ?? $s;
}

function notifyFeedbackSubmit($f) {
    $to = qqToEmail($f['qq'] ?? '');
    if (!$to) return false;
    $html = mailTemplate(
        '反馈已提交',
        '你好，你的反馈我们已经收到，处理进展会通过邮件通知你。',
        [
            '反馈编号' => $f['id'],
            '标题'     => $f['title'],
            '分类'     => $f['category'] ?? '',
            '当前状态' => statusLabel('open'),
            '提交时间' => date('Y-m-d H:i:s', $f['time'] ?? time()),
        ],
        '你可以凭反馈编号在 PCL 内查询处理进度。感谢你的反馈！'
    );
    return smtpSend($to, '【PCL】反馈已提交 · ' . ($f['title'] ?? ''), $html);
}

function notifyFeedback($f, $oldStatus, $newStatus) {
    $to = qqToEmail($f['qq'] ?? '');
    if (!$to) return false;
    $label = statusLabel($newStatus);
    $html = mailTemplate(
        '反馈状态更新：' . $label,
        '你好，你提交的反馈状态有了新的进展。',
        [
            '反馈编号' => $f['id'],
            '标题'     => $f['title'],
            '原状态'   => statusLabel($oldStatus),
            '新状态'   => $label,
            '处理说明' => $f['reply'] ?? '',
            '更新时间' => date('Y-m-d H:i:s', time()),
        ],
        '如果问题已解决，无需再回复。若仍有疑问，可重新提交反馈并附上此编号。'
    );
    return smtpSend($to, '【PCL】反馈状态更新：' . $label, $html);
}

function notifyCaveSubmit($c) {
    $to = qqToEmail($c['qq'] ?? '');
    if (!$to) return false;
    $html = mailTemplate(
        '回声洞投稿已收到',
        '你好，你的回声洞留言我们已经收到。',
        [
            '留言编号' => $c['id'],
            '留言内容' => $c['text'],
            '当前状态' => '已通过，会出现在回声洞里',
            '提交时间' => date('Y-m-d H:i:s', $c['time'] ?? time()),
        ],
        '感谢你的投稿！'
    );
    return smtpSend($to, '【PCL】回声洞投稿已收到', $html);
}

function notifyCave($c, $op) {
    $to = qqToEmail($c['qq'] ?? '');
    if (!$to) return false;
    $approved = ($op === 'approve');
    $html = mailTemplate(
        $approved ? '回声洞投稿已通过' : '回声洞投稿已被驳回',
        $approved ? '你好，你的留言已通过审核，现在会出现在回声洞里。'
                  : '你好，很抱歉，你的留言未通过审核。',
        [
            '留言编号' => $c['id'],
            '留言内容' => $c['text'],
            '审核结果' => $approved ? '已通过' : '已驳回',
            '处理时间' => date('Y-m-d H:i:s', time()),
        ],
        $approved ? '感谢你的投稿！' : '如对结果有疑问，可提交反馈说明情况。'
    );
    return smtpSend($to, $approved ? '【PCL】回声洞投稿已通过' : '【PCL】回声洞投稿已驳回', $html);
}
