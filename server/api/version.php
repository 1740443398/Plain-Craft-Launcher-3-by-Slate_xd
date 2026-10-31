<?php
echo json_encode(['php' => PHP_VERSION, 'sapi' => php_sapi_name(), 'hmac' => function_exists('hash_hmac'), 'equals' => function_exists('hash_equals'), 'bcrypt' => function_exists('password_verify')], JSON_UNESCAPED_UNICODE);
