<div align="center">

# 本项目 服务端

**回声洞 · 反馈中心 · 版本推送 · 新功能投票**

纯 PHP 实现 · 无数据库 · 单文件接口 · 零依赖

</div>

---

## 概述

这是一个为 Minecraft 启动器配套的后端服务，提供四类功能：

<table>
<tr><td width="50%" valign="top">

**内容互动**

- **回声洞** — 用户投稿短留言，随机展示
- **反馈系统** — 免注册提交，按编号查进度
- **新功能投票** — 提交想法并投票

</td><td width="50%" valign="top">

**运维支撑**

- **版本推送** — 上传安装包，保留最近 5 个
- **公告** — 优先级 + 有效期
- **邮件通知** — 反馈状态变更时发信
- **后台管理** — 全数据可视化操作

</td></tr>
</table>

### 设计取舍

| 决定 | 原因 |
|---|---|
| **JSON 文件而非数据库** | 共享主机常限制外部数据库连接；JSON 无需配置，拷贝即用 |
| **单文件 `index.php`** | 所有逻辑集中，避免 `include` 路径问题；便于审查 |
| **无 Composer 依赖** | 受限主机可能没有 Composer 或无法外网访问 |
| **`X-PCL3-Token` 而非 `Authorization`** | Apache/CGI 模式会剥离标准 Authorization 头 |
| **自带 SMTP 客户端** | 避免依赖 `mail()`（多数主机禁用）或 PHPMailer |

---

## 环境要求

| 项目 | 最低 | 说明 |
|---|---|---|
| PHP | 8.0 | 使用 `fn()` 箭头函数、`??`、`match` |
| 扩展 `json` | — | 默认启用 |
| 扩展 `openssl` | — | SMTP over SSL 需要 |
| 扩展 `mbstring` | — | 中文长度校验 |
| 目录权限 | `755` | `api/data/` 需可写 |

### 兼容性说明

已在以下环境验证：

- 共享主机（Apache 2.4 + PHP 8.4 CGI/FastCGI）
- 宝塔面板（Nginx + PHP-FPM）
- 本地开发（PHP 内置服务器）

> **CGI/FastCGI 模式注意**：这类环境通常不把 `Authorization` 头传给 PHP。
> 本项目已改用 `X-PCL3-Token`，并用 `getallheaders()` 兜底，两种模式都能工作。

---

## 部署

### 方式一：直接上传

1. 把本目录下所有文件上传到站点根目录

   ```
   htdocs/
   ├── index.html
   ├── icon.ico
   ├── favicon.ico
   ├── api/
   │   ├── index.php
   │   └── version.php
   ├── admin/
   │   └── index.html
   └── vote/
       └── index.html
   ```

2. 创建数据目录

   ```bash
   mkdir -p api/data/pkgs
   chmod 755 api/data api/data/pkgs
   ```

3. 补全配置（见下一节）

4. 访问 `https://你的域名/api/index.php?action=stats` 验证

### 方式二：本地测试

```bash
cd PHP
php -S 127.0.0.1:8080
```

然后访问 <http://127.0.0.1:8080/>。注意 PHP 内置服务器不支持 `.htaccess`，如果需要保护 `api/data/` 请用真实 Web 服务器。

---

## 配置补全

**仓库中的所有敏感值都是占位符，必须替换后才能使用。**

打开 `api/index.php`，文件顶部有一块配置区：

```php
// ---------- 管理员 ----------
const ADMIN_PASSWORD_HASH = '$2y$12$REPLACE_WITH_YOUR_OWN_BCRYPT_HASH';
const SESSION_SALT        = 'CHANGE_ME_SESSION_SALT_32_HEX';
const IP_SALT             = 'CHANGE_ME_IP_SALT';

// ---------- 客户端鉴权 ----------
const CLIENT_API_KEY = 'REPLACE_WITH_YOUR_CLIENT_API_KEY';

// ---------- SMTP ----------
const SMTP_HOST = 'ssl://smtp.qq.com';
const SMTP_PORT = 465;
const SMTP_USER = 'YOUR_EMAIL@example.com';
const SMTP_PASS = 'YOUR_QQ_SMTP_AUTH_CODE';
```

### 1. 生成管理员密码哈希

```bash
php -r "echo password_hash('你的密码', PASSWORD_BCRYPT, ['cost' => 12]);"
```

输出形如 `$2y$12$abc...xyz`，整串填入 `ADMIN_PASSWORD_HASH`（**保留单引号**，否则 `$` 会被 PHP 解析为变量）。

### 2. 生成随机盐

```bash
php -r "echo bin2hex(random_bytes(16));"   # SESSION_SALT，32 位十六进制
php -r "echo bin2hex(random_bytes(12));"   # IP_SALT
php -r "echo bin2hex(random_bytes(32));"   # CLIENT_API_KEY，64 位十六进制
```

- `SESSION_SALT` — 用于签发登录 token，**更换后所有已登录会话失效**
- `IP_SALT` — 用于哈希 IP，**更换后限流记录失效**
- `CLIENT_API_KEY` — 客户端调用公开接口的凭证

### 3. 获取 SMTP 授权码

以 QQ 邮箱为例：

1. 登录邮箱网页版
2. 设置 → 账户
3. 找到「POP3/IMAP/SMTP 服务」，点击开启
4. 按提示发送短信验证
5. 获得 **16 位授权码**

> **注意**：授权码不是登录密码。其他邮箱服务商（163、Gmail）流程类似，请查阅各自文档。

### 4. 同步客户端

客户端 `Modules/ModByethost.vb` 里的 `ClientApiKey` 必须与服务端的 `CLIENT_API_KEY` **完全一致**，否则所有接口返回 403。

---

## 数据结构

所有数据存在 `api/data/` 下的 JSON 文件：

| 文件 | 内容 | 是否自动创建 |
|---|---|---|
| `caves.json` | 回声洞留言 | 是 |
| `feedback.json` | 反馈记录 | 是 |
| `donors.json` | 赞助者名单 | 是 |
| `releases.json` | 版本记录 | 是 |
| `notices.json` | 公告 | 是 |
| `ideas.json` | 新功能投票 | 是 |
| `pkgs/` | 安装包文件 | 是 |
| `_rate.json` | 限流计数 | 是 |
| `_dup.json` | 去重指纹 | 是 |
| `_login_lock.json` | 登录失败锁定 | 是 |
| `_mail_queue.json` | 邮件队列 | 是 |
| `_mail_rate.json` | 邮件发送计数 | 是 |

### 备份

直接打包 `api/data/` 整个目录即可：

```bash
tar -czf backup-$(date +%F).tar.gz api/data/
```

### 迁移

把 `api/data/` 连同代码一起拷到新主机，改好配置即可，无需额外导入步骤。

---

## 接口参考

所有接口通过 `api/index.php?action=xxx` 访问，返回 JSON。

### 公开接口

无需鉴权，但部分接口需要 `X-PCL3-Key` 请求头。

| action | 方法 | 参数 | 说明 |
|---|---|---|---|
| `check` | GET | `version`、`channel` | 检查更新，同时返回公告 |
| `cave` | GET | — | 随机取一条回声洞 |
| `cave_submit` | POST | `text`、`qq` | 提交回声洞 |
| `feedback` | POST | `type`、`content`、`contact`、`log` | 提交反馈 |
| `track` | GET | `id` | 按编号查询反馈进度 |
| `donors` | GET | — | 赞助者名单 |
| `stats` | GET | — | 站点统计（回声洞数、反馈数） |
| `download` | GET | `version` | 下载安装包（PHP 中转） |
| `vote_list` | GET | — | 投票列表，按票数排序 |
| `vote_submit` | POST | `title`、`desc`、`category`、`qq` | 提交想法 |
| `vote` | POST | `id` | 给某个想法投票 |
| `vote_page` | GET | — | 302 跳转到投票页 |

### 管理接口

需要 `X-PCL3-Token` 请求头，值来自 `login` 接口。

| action | 参数 | 说明 |
|---|---|---|
| `login` | `password` | 登录，返回 token（有效期 12 小时） |
| `auth` | — | 校验 token 是否有效 |
| `admin_overview` | — | 后台总览（全部数据 + 统计） |
| `publish` | `version`、`url`、`package`（文件）、`notes` | 发布版本，自动保留最近 5 个 |
| `release_clear` | — | 清空所有版本记录与安装包 |
| `notice` | `title`、`content`、`priority`、`expire` | 发布公告 |
| `delete_notice` | `id` | 删除公告 |
| `donor` | `name`、`amount`、`message` | 添加赞助者 |
| `delete_donor` | `name` | 删除赞助者 |
| `feedback_update` | `id`、`status`、`reply` | 更新反馈状态 |
| `feedback_delete` | `id` | 删除反馈 |
| `cave_update` | `id`、`approved` | 审核回声洞 |
| `mail_queue` | — | 查看邮件队列 |
| `mail_run` | — | 立即投递队列 |
| `mail_clear` | — | 清空队列 |

### 请求示例

```bash
# 登录
curl -X POST "https://your-domain/api/index.php?action=login" \
     -d "password=你的密码"

# 用返回的 token 查询总览
curl "https://your-domain/api/index.php?action=admin_overview" \
     -H "X-PCL3-Token: 上一步返回的token"

# 发布版本（带安装包）
curl -X POST "https://your-domain/api/index.php?action=publish" \
     -H "X-PCL3-Token: token" \
     -F "version=3.0.0.1" \
     -F "notes=更新说明" \
     -F "package=@./app.exe"

# 检查更新
curl "https://your-domain/api/index.php?action=check&version=3001" \
     -H "X-PCL3-Key: 你的CLIENT_API_KEY"
```

### 响应格式

成功：

```json
{ "ok": true, "data": ... }
```

失败：

```json
{ "ok": false, "error": "错误说明" }
```

HTTP 状态码：`200` 成功、`400` 参数错误、`401` 未登录、`403` 无权限、`404` 未找到、`429` 触发限流、`500` 服务端错误。

---

## 限流与防护

### 提交限流

| 限制 | 默认值 | 常量 |
|---|---|---|
| 同 IP 提交最小间隔 | 60 秒 | `RATE_MIN_INTERVAL` |
| 同 IP 每日提交上限 | 10 次 | `RATE_DAILY_LIMIT` |
| 重复内容检测窗口 | 3600 秒 | `RATE_DUP_WINDOW` |

### 邮件限流

| 限制 | 默认值 | 常量 |
|---|---|---|
| 每分钟最多发送 | 30 封 | `MAIL_PER_MINUTE` |
| 单地址每日上限 | 20 封 | `MAIL_PER_ADDR_DAILY` |
| 队列长度上限 | 500 | `MAIL_QUEUE_MAX` |
| 单封最大重试 | 3 次 | `MAIL_MAX_ATTEMPTS` |

超出后进入队列，由后续请求触发投递，不会丢失。

### 登录防护

- 密码使用 bcrypt（cost 12）存储
- 连续失败会写入 `_login_lock.json`，触发临时锁定
- Token 用 HMAC 签名，12 小时后自动失效
- Token 不落盘，只在内存与响应中传递

### 防重复提交

`cave_submit` 与 `vote_submit` 会对内容计算指纹，写入 `_dup.json`。窗口期内相同内容会被拒绝。

---

## 后台管理

访问 `https://你的域名/admin/`，用配置的管理员密码登录。

### 功能

- **总览** — 各表数据量、版本列表、最近反馈
- **回声洞** — 审核、删除、查看投稿 IP 哈希
- **反馈** — 查看详情、修改状态、回复（触发邮件）
- **版本** — 上传安装包、查看历史、清空
- **公告** — 发布、删除
- **赞助者** — 添加、删除、调整金额
- **邮件** — 队列状态、手动投递、清空
- **投票** — 查看提交的想法与票数

### 安全建议

- 把 `admin/` 改成不易猜到的路径
- 用主机面板的「目录密码保护」再加一层
- 不要在公共网络登录后台

---

## 推送工具

`推送工具.ps1` 是一个命令行发布工具，适合频繁发布时使用。

### 配置

```powershell
$ApiBase   = "https://your-domain/api/"
$Password  = $env:本项目_ADMIN_PASSWORD    # 从环境变量读取
$ClientKey = "你的CLIENT_API_KEY"
```

设置环境变量：

```powershell
$env:本项目_ADMIN_PASSWORD = "你的管理员密码"
```

### 使用

```powershell
powershell -ExecutionPolicy Bypass -File 推送工具.ps1
```

菜单提供：发布版本、发布公告、查看版本、查看公告、删除公告、添加赞助者、查看赞助者、删除赞助者、测试连接。

`token` 会在运行时自动登录获取，无需手填。

---

## 常见问题

<details>
<summary><b>访问接口返回挑战页（含 slowAES）</b></summary>

部分共享主机会给每个访客下发一个 JS Cookie 挑战。客户端已实现绕过（`Modules/ModByethost.vb`），但浏览器直接访问时会被重定向。

如果自建服务端遇到这个问题，通常是主机启用了「防爬虫」功能，可在主机面板关闭。
</details>

<details>
<summary><b>管理接口返回 401 未登录</b></summary>

按顺序排查：

1. Token 是否过期（有效期 12 小时）
2. 请求头名称是否为 `X-PCL3-Token`（不是 `Authorization`）
3. 主机是否剥离了自定义头——用 `api/debug.php` 查看 `getallheaders()` 输出
4. `SESSION_SALT` 是否在登录后被改动过
</details>

<details>
<summary><b>邮件发不出去</b></summary>

1. 检查 `SMTP_PASS` 是否为**授权码**而非登录密码
2. 确认 `SMTP_PORT` 与加密方式匹配（465 = SSL，587 = STARTTLS）
3. 查看 `api/data/_smtp.log` 的错误记录
4. 部分主机封锁 465 端口，可改用 587
</details>

<details>
<summary><b>上传安装包失败</b></summary>

1. 检查 `php.ini` 的 `upload_max_filesize` 与 `post_max_size`（建议 ≥ 32M）
2. 确认 `api/data/pkgs/` 可写
3. 如果返回 403，可能是主机对静态文件的防盗链——本项目已改用 PHP 中转下载，不受影响
</details>

<details>
<summary><b>数据文件损坏</b></summary>

JSON 文件被并发写入可能损坏。本项目已用文件锁（`flock`）保护，但极端情况仍可能出问题。

恢复方法：从备份还原，或删除对应文件让系统重建（**会丢失该表全部数据**）。
</details>

<details>
<summary><b>如何只保留部分功能</b></summary>

删除对应的前端页面即可：

- 不要回声洞 → 删 `index.html` 里的相关区块
- 不要投票 → 删 `vote/` 目录
- 不要后台 → 删 `admin/` 目录（接口仍在，只是没有界面）

接口本身不会因为前端缺失而报错。
</details>

---

## 目录权限参考

```
drwxr-xr-x  api/
drwxrwxrwx  api/data/          ← 需可写
drwxrwxrwx  api/data/pkgs/     ← 需可写
-rw-r--r--  api/index.php
-rw-r--r--  index.html
```

若主机不支持 `755` 写入，可临时用 `777`，但建议配置好属主后改回 `755`。

---

## 许可证

服务端代码作为本项目的一部分，同样遵循 [PCL 分发有限许可](../LICENCE)。

使用时请保留根目录的 `LICENCE` 文件。

---

<div align="center">

**本服务端与 Mojang Studios、Microsoft 无关联。**

</div>
