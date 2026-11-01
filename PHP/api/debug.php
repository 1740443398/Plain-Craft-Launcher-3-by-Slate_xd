<?php
header('Content-Type: application/json; charset=utf-8');
const SESSION_SALT = 'CHANGE_ME_SESSION_SALT_32_HEX';
const SESSION_TTL = 43200;

function makeToken() {
    $exp = time() + SESSION_TTL;
    $payload = $exp . '.' . bin2hex(random_bytes(8));
    $sig = hash_hmac('sha256', $payload, SESSION_SALT);
    return $payload . '.' . $sig;
}
function verifyToken($token) {
    if (!$token) return 'empty';
    $parts = explode('.', $token);
    if (count($parts) !== 3) return 'parts=' . count($parts);
    [$exp, $nonce, $sig] = $parts;
    if ((int)$exp < time()) return 'expired';
    $expect = hash_hmac('sha256', $exp . '.' . $nonce, SESSION_SALT);
    return hash_equals($expect, $sig) ? 'OK' : ('mismatch expect=' . substr($expect,0,16) . ' got=' . substr($sig,0,16));
}

$out = [];
$out['php'] = PHP_VERSION;
$out['get'] = $_GET;
$out['post'] = $_POST;
$out['has_auth_header'] = isset($_SERVER['HTTP_AUTHORIZATION']) ? $_SERVER['HTTP_AUTHORIZATION'] : null;
$out['has_redirect_auth'] = isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION']) ? $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] : null;
$out['all_headers'] = function_exists('getallheaders') ? getallheaders() : null;

// 生成一个新 token 并立即验证
$t = makeToken();
$out['new_token'] = $t;
$out['verify_new'] = verifyToken($t);

// 若请求带了 token，验证它
$incoming = $_GET['token'] ?? $_POST['token'] ?? '';
if ($incoming !== '') {
    $out['incoming_len'] = strlen($incoming);
    $out['incoming'] = $incoming;
    $out['verify_incoming'] = verifyToken($incoming);
}
echo json_encode($out, JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);