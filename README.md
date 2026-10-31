# Plain Craft Launcher 3

> Minecraft 启动器的二次修改版本，基于 **Plain Craft Launcher 2.13.1.1** 构建。

本项目包含两部分：**WPF 桌面客户端**（VB.NET）与 **PHP 服务端**（回声洞、反馈、版本推送、新功能投票）。

---

## 目录

- [项目结构](#项目结构)
- [客户端](#客户端)
  - [技术栈](#技术栈)
  - [构建方法](#构建方法)
  - [目录说明](#客户端目录说明)
- [服务端](#服务端)
  - [环境要求](#环境要求)
  - [部署步骤](#部署步骤)
  - [接口说明](#接口说明)
- [配置与密钥补全](#配置与密钥补全)
- [安全说明](#安全说明)
- [许可证](#许可证)

---

## 项目结构

```
.
├── client/                     # WPF 桌面客户端（VB.NET）
│   ├── Plain Craft Launcher 3/  # 主项目
│   │   ├── Controls/            # 自定义控件
│   │   ├── Images/              # 图标、头像、主题图
│   │   ├── Modules/             # 业务逻辑
│   │   ├── Pages/               # 页面
│   │   ├── Resources/           # 嵌入资源
│   │   └── My Project/          # 程序集信息与资源清单
│   ├── PCLCS/                   # C# 辅助库
│   └── patches/                 # 上游补丁文件
└── server/                      # PHP 服务端
    ├── api/
    │   ├── index.php            # 主接口（所有 action）
    │   └── debug.php            # 诊断页（部署后可删）
    ├── admin/index.html         # 后台管理页
    ├── vote/index.html          # 新功能投票页
    ├── index.html               # 网站首页
    ├── icon.ico                 # 站点图标
    └── 推送工具.ps1              # 命令行发布工具
```

---

## 客户端

### 技术栈

| 项目 | 说明 |
|---|---|
| 语言 | VB.NET |
| 框架 | .NET Framework 4.8 |
| UI | WPF |
| 根命名空间 | `PCL` |
| 目标平台 | AnyCPU |

### 构建方法

需要 **.NET Framework 4.8 开发工具包** 与 **MSBuild**。

```bat
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" ^
  "client\Plain Craft Launcher 3\Plain Craft Launcher 3.vbproj" ^
  /p:Configuration=Release ^
  /p:Platform=AnyCPU ^
  /p:OutputPath="out\"
```

若使用较新的 C# 语法或 VB 特性，可指定 Roslyn 工具集：

```bat
MSBuild.exe "client\Plain Craft Launcher 3\Plain Craft Launcher 3.vbproj" ^
  /p:Configuration=Release ^
  /p:VbcToolPath="path\to\roslyn\" ^
  /p:VbcToolExe=vbc.exe ^
  /p:LangVersion=latest
```

> 项目使用单文件打包（`ModSingleFile`），构建产物为单一 `.exe`。

### 客户端目录说明

| 路径 | 作用 |
|---|---|
| `Modules/ModNet.vb` | 网络层，多源下载、重试、断点续传 |
| `Modules/ModMirror.vb` | 模组加载器镜像加速（ZCSM），作为官方源失败后的后备 |
| `Modules/ModSync.vb` | 启动时静默同步服务端数据 |
| `Modules/ModUpdate.vb` | 版本检查、安装包下载与校验 |
| `Modules/ModHitokoto.vb` | 一言获取，多 API 备份 + 本地缓存 |
| `Modules/ModFeedback.vb` | 反馈与回声洞提交 |
| `Modules/ModByethost.vb` | 绕过主机的 JS Cookie 挑战 |
| `Modules/ModSecret.vb` | 主题配色与解锁状态 |
| `Pages/PageLaunch/` | 主页、登录页 |
| `Pages/PageSetup/` | 设置页 |
| `Pages/PageOther/` | 关于、反馈、百宝箱 |
| `Pages/PageDownload/` | 下载与安装逻辑 |

---

## 服务端

### 环境要求

- PHP **8.0+**（使用了 `fn()` 箭头函数、`??` 合并运算符）
- 需要扩展：`json`、`openssl`（SMTP over SSL）、`mbstring`
- 数据存储为 **JSON 文件**，不依赖数据库
- 需要可写目录 `api/data/`

### 部署步骤

1. **上传文件**

   把 `server/` 下所有文件上传到站点根目录（如 `htdocs/`）：

   ```
   htdocs/
   ├── index.html
   ├── icon.ico
   ├── api/index.php
   ├── admin/index.html
   └── vote/index.html
   ```

2. **创建数据目录**

   ```
   htdocs/api/data/
   htdocs/api/data/pkgs/      # 存放安装包
   ```

   权限设为 `755`（或 `777`，视主机而定）。

3. **补全配置**

   打开 `api/index.php`，修改文件顶部的配置区（见下一节）。

4. **生成管理员密码哈希**

   在本地 PHP 环境执行：

   ```bash
   php -r "echo password_hash('你的密码', PASSWORD_BCRYPT, ['cost' => 12]);"
   ```

   把输出填入 `ADMIN_PASSWORD_HASH`。

5. **生成随机盐值**

   ```bash
   php -r "echo bin2hex(random_bytes(16));"   # SESSION_SALT
   php -r "echo bin2hex(random_bytes(12));"   # IP_SALT
   php -r "echo bin2hex(random_bytes(32));"   # CLIENT_API_KEY
   ```

6. **删除诊断页**

   部署完成后删除 `api/debug.php`，它会暴露环境信息。

### 接口说明

所有接口通过 `api/index.php?action=xxx` 访问，返回 JSON。

**公开接口**

| action | 说明 | 参数 |
|---|---|---|
| `check` | 检查更新 + 公告 | `version`（版本码）、`channel` |
| `cave` | 随机取一条回声洞 | — |
| `cave_submit` | 提交回声洞 | `text`、`qq` |
| `feedback` | 提交反馈 | `type`、`content`、`contact`、`log` |
| `track` | 查询反馈进度 | `id` |
| `donors` | 赞助者名单 | — |
| `stats` | 站点统计 | — |
| `download` | 下载安装包 | `version` |
| `vote_list` | 投票列表 | — |
| `vote_submit` | 提交想法 | `title`、`desc`、`category`、`qq` |
| `vote` | 投一票 | `id` |

**管理接口**（需 `X-PCL3-Token` 请求头）

| action | 说明 |
|---|---|
| `login` | 登录，返回 token |
| `auth` | 校验 token |
| `admin_overview` | 后台总览 |
| `publish` | 发布版本（支持 `package` 文件上传） |
| `release_clear` | 清空版本记录 |
| `notice` / `delete_notice` | 公告管理 |
| `donor` / `delete_donor` | 赞助者管理 |
| `feedback_update` / `feedback_delete` | 反馈处理 |
| `cave_update` | 回声洞审核 |
| `mail_queue` / `mail_run` / `mail_clear` | 邮件队列 |

**鉴权方式**

管理接口的 token 放在 **`X-PCL3-Token`** 请求头，而不是标准的 `Authorization`。原因是部分共享主机（Apache/CGI 模式）会剥离 `Authorization` 头。

---

## 配置与密钥补全

仓库中所有敏感值都已替换为占位符。首次部署时必须补全以下内容。

### 服务端：`api/index.php`

文件顶部有一块配置区：

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

**SMTP 授权码获取方法**

1. 登录 QQ 邮箱网页版
2. 设置 → 账户 → POP3/IMAP/SMTP 服务
3. 开启「IMAP/SMTP 服务」
4. 按提示发送短信验证，获得 16 位授权码
5. 填入 `SMTP_PASS`（**不是** QQ 登录密码）

### 客户端：`Modules/ModByethost.vb`

```vb
Public Const ClientApiKey As String = "REPLACE_WITH_YOUR_CLIENT_API_KEY"
```

必须与服务端的 `CLIENT_API_KEY` **完全一致**，否则所有接口请求会被拒绝。

### 客户端：`Modules/ModUpdate.vb`

```vb
Public Const ApiBase As String = "https://your-domain.example/api/"
```

改成你自己的服务端地址。

### 推送工具：`server/推送工具.ps1`

```powershell
$ApiBase = "https://your-domain.example/api/index.php"
$Token   = $env:PCL3_ADMIN_TOKEN
```

`$Token` 从环境变量读取，不要写死在脚本里：

```powershell
$env:PCL3_ADMIN_TOKEN = "登录后台后获得的 token"
```

---

## 安全说明

### 账号数据是否会随程序泄露？

**不会。** 账号信息存储在注册表 `HKCU\Software\<程序标识>`，使用 **DES 加密**，密钥由 `"PCL" + 本机识别码` 派生：

```vb
Dim KeyText As String = ("PCL" & Identify).GetStableHashCode().ToString().EnsureLength("X"c, 8).Substring(0, 8)
```

`Identify` 基于本机硬件信息生成。因此：

- 换一台机器，密钥不同，**无法解密**
- 把程序发给别人，对方读到的只是他自己的账号
- 注册表里的 `LoginMsJson` 等字段是密文，不是明文

> 注意：这不是强加密设计（DES + 硬件派生），能防「换机读取」，但防不住在同一台机器上运行恶意程序。这是上游 PCL 的既有设计，本项目未改动。

### 客户端内不含任何管理凭据

客户端只硬编码了 `CLIENT_API_KEY`，它仅能：

- 读取公开数据（回声洞、公告、赞助者名单、统计）
- 提交反馈与回声洞

**不能**登录后台、不能修改数据、不能读取他人反馈。

### 其他建议

- `api/data/` 目录应禁止外部直接访问（若主机支持 `.htaccess`）
- 定期更换 `CLIENT_API_KEY` 并同步更新客户端
- 后台地址建议使用非常规路径
- 生产环境删除 `api/debug.php`

---

## 许可证

本项目是 **Plain Craft Launcher 2.13.1.1** 的二次修改版本。

- 原始作品版权归 **成都瓜皮龙科技有限公司** 所有（计算机软件著作权登记号：`2020SR0875133`）
- 本二次修改遵循原始作品的许可条款，**不主张对原始作品的任何权利**
- 二次修改部分由 **蕭遞** 维护，与原开发者无关
- 使用本版本所产生的一切后果由使用者自行承担
- 若需反馈本版本的问题，请勿联系原开发者

---

## 相关链接

- 项目仓库：<https://github.com/1740443398/Plain-Craft-Launcher-3>
- 问题反馈：<https://pcl3.byethost9.com/>
- 新功能投票：<https://pcl3.byethost9.com/vote/>
- 原始项目：<https://github.com/Meloong-Git/PCL>
