<div align="center">

# Plain Craft Launcher by Slate_xd

**基于 Plain Craft Launcher 2.13.1.1 的第三方修改版**

Minecraft 启动器 · 客户端 + 服务端 · 完整开源

[![License](https://img.shields.io/badge/许可证-PCL%20分发有限许可-2f81f7?style=flat-square)](#许可证)
[![Platform](https://img.shields.io/badge/平台-Windows%207%2B-0078d4?style=flat-square)](#系统要求)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4?style=flat-square)](#系统要求)
[![PHP](https://img.shields.io/badge/PHP-8.0%2B-777bb4?style=flat-square)](#php-服务端)

</div>

---

## 这是什么

一个 Minecraft 启动器项目，包含两部分：

| 部分 | 说明 |
|---|---|
| **客户端（EXE）** | WPF 桌面程序，负责启动游戏、下载资源、管理实例 |
| **服务端（PHP）** | 提供回声洞、反馈收集、版本推送、新功能投票 |

两部分**可以独立使用**：客户端不依赖自建服务端也能正常启动游戏；服务端也可以配合其他客户端使用。

本项目基于 [Plain Craft Launcher 2.13.1.1](https://github.com/Meloong-Git/PCL) 修改而来，**不是官方发行版本**。

---

## 目录结构

```
site/
├── PHP/                         服务端，部署到网站主机
│   ├── api/
│   │   ├── index.php            主接口（所有 action 都在这里）
│   │   ├── version.php          版本探测
│   │   └── debug.php            诊断页（部署后建议删除）
│   ├── admin/index.html         后台管理页
│   ├── vote/index.html          新功能投票页
│   ├── index.html               网站首页
│   ├── icon.ico                 站点图标
│   ├── favicon.ico
│   └── 推送工具.ps1             命令行发布工具
│
├── EXE/                         客户端，WPF 桌面程序
│   ├── Plain Craft Launcher/  VB.NET 主项目
│   ├── PCLCS/                   C# 辅助库
│   ├── patches/                 上游补丁
│   └── runtime/
│       └── ndp48-web.exe        .NET Framework 4.8 安装器
│
├── LICENCE                      PCL 分发有限许可（必须保留）
├── .gitignore
├── .gitattributes
└── README.md
```

各目录有独立说明文档：

- 服务端部署 → [`PHP/README.md`](./PHP/README.md)
- 客户端构建 → [`EXE/README.md`](./EXE/README.md)

---

## 快速开始

### 只想用客户端

1. 安装 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)（Windows 10 2004+ 已内置）
2. 下载并运行 `Plain Craft Launcher.exe`
3. 首次启动会让你选择游戏目录，选好后即可启动游戏

### 想自己编译

```bat
git clone <本仓库地址>
cd site\EXE

"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" ^
  "Plain Craft Launcher\Plain Craft Launcher.vbproj" ^
  /p:Configuration=Release /p:Platform=AnyCPU /p:OutputPath="out\"
```

产物在 `EXE\out\Plain Craft Launcher.exe`。

### 想部署服务端

见 [`PHP/README.md`](./PHP/README.md)，包含完整的环境要求、配置补全和接口说明。

---

## 功能概览

### 客户端

<table>
<tr><td width="50%" valign="top">

**启动与实例管理**

- 支持 Vanilla / Forge / NeoForge / Fabric / Quilt / OptiFine
- 多版本隔离，每个实例独立配置
- 正版（微软）与离线登录
- 自动补全缺失的资源文件
- Java 自动检测与版本匹配

**下载**

- 多源并发下载，失败自动切换
- 断点续传与完整性校验
- 模组加载器镜像加速（官方源失败后回退）
- 整合包导入（CurseForge / HMCL 等格式）

</td><td width="50%" valign="top">

**界面**

- 16 种主题配色（发布版仅蓝色系，见[主题说明](#主题与配色)）
- 自定义背景图片、透明度、模糊
- 主页统计卡片、快捷工具
- 深色/浅色自适应

**扩展**

- 回声洞（随机留言）
- 反馈提交与进度查询
- 新功能投票入口
- 投影预览（独立的 .litematic / .nbt / .schem 查看器）

</td></tr>
</table>

### 服务端

| 功能 | 说明 |
|---|---|
| **回声洞** | 用户投稿短留言，随机展示；支持审核与去重 |
| **反馈系统** | 免注册提交，记录 IP 与可选 QQ，支持按编号查询进度 |
| **版本推送** | 上传安装包，自动计算 SHA-256，保留最近 5 个版本 |
| **公告** | 支持优先级与有效期，客户端启动时静默拉取 |
| **新功能投票** | 用户提交想法并投票，按票数排序 |
| **赞助者名单** | 展示与排序，支持置顶 |
| **邮件通知** | 反馈状态变更时通过 SMTP 通知用户，带限流与队列 |
| **后台管理** | 全部数据的管理界面，凭据走 `X-PCL3-Token` 请求头 |

---

## 系统要求

### 客户端

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 7 SP1 或更高 |
| 运行时 | .NET Framework 4.8 |
| 内存 | 建议 4 GB 以上 |
| 硬盘 | 启动器本体约 8 MB，游戏资源另计 |

### 服务端

| 项目 | 要求 |
|---|---|
| PHP | 8.0 或更高 |
| 扩展 | `json`、`openssl`、`mbstring` |
| 数据库 | **不需要**，数据存 JSON 文件 |
| 目录权限 | `api/data/` 需可写 |
| 磁盘 | 每个安装包约 8 MB，保留 5 个约 40 MB |

> 服务端已在共享主机（Apache + CGI 模式）上验证可用。这类环境通常会剥离 `Authorization` 请求头，因此管理凭据改用 `X-PCL3-Token`。

---

## 主题与配色

PCL 的许可证要求衍生作品的**界面主色相必须为蓝色**，因为更换主题色是原版通过赞助解锁的功能。

本项目通过编译常量区分两种构建：

```vb
' EXE/Plain Craft Launcher/Modules/Base/ModBase.vb
Public Const AllowAllThemes As Boolean = False
```

| 值 | 可用主题 |
|---|---|
| `False` | 3 种蓝色系：龙猫蓝（210°）、甜柠青（180°）、极客蓝（195°） |
| `True` | 全部配色 |

蓝色系内部的明度、饱和度调整属于合规范围，所以三种蓝色之间有明显区分度。

> **分发编译产物时必须使用 `AllowAllThemes = False`**，否则会违反许可证。
> `True` 仅用于本地开发调试，不要用于任何对外发布的构建。

---

## 技术栈

<table>
<tr><td width="50%" valign="top">

**客户端**

| 项目 | 说明 |
|---|---|
| 语言 | VB.NET |
| 框架 | .NET Framework 4.8 |
| UI | WPF |
| 根命名空间 | `PCL` |
| 打包 | 单文件（ILMerge + 资源内嵌） |

主要依赖（`EXE/Plain Craft Launcher/Resources/`）：

- `Newtonsoft.Json` — JSON 解析
- `NAudio` — 音频播放
- `Ookii.Dialogs.Wpf` — 系统对话框
- `MeloongCore` — 核心库（上游提供）
- `libwebp64` — WebP 图片解码

</td><td width="50%" valign="top">

**服务端**

| 项目 | 说明 |
|---|---|
| 语言 | PHP 8.0+ |
| 存储 | JSON 文件（无数据库） |
| 鉴权 | HMAC 签名 token + `X-PCL3-Token` 头 |
| 邮件 | 自实现 SMTP 客户端（SSL/TLS） |
| 前端 | 原生 HTML/CSS/JS，无框架 |

服务端不依赖 Composer，所有代码在单个 `index.php` 内，方便部署到受限主机。

</td></tr>
</table>

---

## 安全说明

### 账号数据不会随程序泄露

客户端把账号信息存在注册表 `HKCU\Software\PCLDebug`，使用 **DES 加密**，密钥由 `"PCL" + 本机识别码` 派生：

```vb
Dim KeyText As String = ("PCL" & Identify).GetStableHashCode().ToString().EnsureLength("X"c, 8).Substring(0, 8)
```

`Identify` 基于本机硬件生成。因此：

- 换一台机器，密钥不同，**无法解密**
- 把程序发给别人，对方读到的只是他自己的账号
- 注册表里的 `LoginMsJson` 等字段是密文，不是明文

> 这是上游 PCL 的既有设计，非强加密。能防「换机读取」，防不住同机运行的恶意程序。

### 客户端不含管理凭据

客户端只硬编码一个 `CLIENT_API_KEY`，它仅能：

- 读取公开数据（回声洞、公告、赞助者名单、统计）
- 提交反馈与回声洞

**不能**登录后台、修改数据、或读取他人反馈。

### 仓库中的占位符

出于安全考虑，本仓库所有敏感值都是占位符：

```
REPLACE_WITH_YOUR_CLIENT_API_KEY
CHANGE_ME_SESSION_SALT_32_HEX
$2y$12$REPLACE_WITH_YOUR_OWN_BCRYPT_HASH
YOUR_QQ_SMTP_AUTH_CODE
```

部署前必须替换为你自己的值，方法见 [`PHP/README.md`](./PHP/README.md#配置补全)。

### 部署建议

- `api/data/` 禁止外部直接访问（用 `.htaccess` 或主机面板的目录保护）
- 部署完成后删除 `api/debug.php`
- 定期更换 `CLIENT_API_KEY` 并同步更新客户端
- 后台入口建议改为非常规路径
- 使用 HTTPS

---

## 许可证

本项目沿用 **《PCL 分发有限许可》**，全文见 [LICENCE](./LICENCE)。

### 使用等级

依据《PCL 存储库合理使用指南》，本项目属于 **重度使用**：

> 若不符合轻度使用的情形，即为重度使用。
> 其常见情形例如：**基于本存储库进行修改。**

### 重度使用的义务

| # | 要求 | 本项目 |
|---|---|---|
| 1 | 明确表示是第三方二次创作 | ✅ 见下方声明 |
| 2 | 名称以 `Plain Craft Launcher` 开头 + 第三方修改后缀 | ✅ `Plain Craft Launcher by Slate_xd` |
| 3 | 关于页首位为龙腾猫跃并附赞助链接 | ✅ 已保留 |
| 4 | 公开源代码，继续使用本 LICENCE | ✅ 本仓库公开 |
| 5 | 实现 McLaunchPrecheck 正版购买劝导 | ✅ 保留上游实现 |
| 6 | 无赞助解锁类似功能，主色相为蓝色 | ✅ 发布版仅蓝色系 |

### 二次创作声明

**本项目是第三方基于 PCL 独立进行二次创作的产物，并非 PCL 官方发行版本。**

- 原始作品：Plain Craft Launcher 2.13.1.1
- 原始作者：龙腾猫跃 / 成都瓜皮龙科技有限公司
- 软件著作权登记号：`2020SR0875133`
- 本项目由 **Slate_xd** 独立维护，与原开发者无关
- 不主张对原始作品的任何权利

### 免责

使用本软件所产生的一切后果由使用者自行承担。
若需反馈本项目的问题，请前往本仓库的 Issues，**请勿联系原开发者**。

---

## 贡献

欢迎提交 Issue 与 Pull Request。

### 开发约定

- 客户端使用 VB.NET，遵循现有代码风格
- 服务端保持单文件 `index.php`，不引入 Composer 依赖
- 提交前请确认 `AllowAllThemes` 为 `False`
- 不要提交任何真实凭据

### 本地调试

客户端启动时会在 `PCL\Log*.txt` 写入日志，调试模式（`BuildType = BuildTypes.Debug`）会输出更详细的网络与解码信息。

服务端可访问 `api/debug.php` 查看环境信息（**仅限本地**，部署后删除）。

---

## 致谢

本项目能存在，离不开这些人和项目：

- **龙腾猫跃** — PCL 原作者，提供了这个优秀的启动器基础
- **HerobrineXia** — PCL 协力开发者
- **bangbang93** — BMCLAPI 镜像源与 Forge 安装工具
- **z0z0r4** — MCIM 社区资源镜像
- **零核智能转储镜像（ZCSM）** — 模组加载器 Maven 镜像加速
- **EasyTier** — 联机模块
- **重庆大学开源软件镜像站** — 镜像分发
- **MC 百科** — Mod 中文翻译数据

以及所有为 PCL 提交过反馈、Bug 报告与代码的社区成员。

---

## 相关链接

| 用途 | 地址 |
|---|---|
| 原始项目 | <https://github.com/Meloong-Git/PCL> |
| PCL 帮助文档 | <https://github.com/LTCatt/PCL2Help> |
| 本仓库 Issues | 见仓库页面的 Issues 标签 |

---

<div align="center">

**本项目与 Mojang Studios、Microsoft 无关联。**

Minecraft 是 Mojang Studios 的商标。

</div>
