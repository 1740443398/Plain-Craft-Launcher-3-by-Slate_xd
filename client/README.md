<div align="center">

# PCL3 客户端

**Minecraft 启动器 · WPF 桌面程序**

VB.NET · .NET Framework 4.8 · 单文件打包

</div>

---

## 概述

这是 Minecraft 启动器的客户端部分，负责游戏启动、资源下载、实例管理等全部本地操作。

基于 [Plain Craft Launcher 2.13.1.1](https://github.com/Meloong-Git/PCL) 修改，**不是官方发行版本**。

### 能做什么

<table>
<tr><td width="50%" valign="top">

**游戏启动**

- Vanilla / Forge / NeoForge / Fabric / Quilt / OptiFine
- 多实例隔离，各自独立配置
- 正版（微软 OAuth）与离线登录
- 自动补全缺失资源
- Java 自动检测与匹配

**下载**

- 多源并发，失败自动切换
- 断点续传与 SHA-1 校验
- 模组加载器镜像加速
- 整合包导入（CurseForge / HMCL / MultiMC）

</td><td width="50%" valign="top">

**界面**

- 16 种主题配色（发布版仅蓝色系）
- 自定义背景、透明度、模糊
- 主页统计与快捷工具
- 高 DPI 适配

**工具**

- 回声洞（随机留言）
- 反馈提交与进度查询
- 投影预览（.litematic / .nbt / .schem）
- 内存优化
- 崩溃日志分析

</td></tr>
</table>

---

## 本版本改了什么

相对于上游 **Plain Craft Launcher 2.13.1.1**，本版本做了以下修改。
按「问题修复」「新增功能」「体验优化」三类整理。

---

### 问题修复

<table>
<tr><td width="50%" valign="top">

#### 1. 内存优化完全不可用

**现象**：点击「内存优化」提示成功，但可用内存没有任何变化。

**根因**：`PrivilegeToken` 结构体用 `Pack:=1` 布局，同时把 `Luid` 声明为 `Long`。
这导致 LUID 落在偏移 4 而非 8，`AdjustTokenPrivileges` 返回 `LastError = 1300`
（权限无效），后续 `NtSetSystemInformation` 返回 `0xC0000061`（权限不足）。

**修复**：

- 改用 `Pack:=4`，把 `Luid` 拆成 `LuidLowPart` / `LuidHighPart` 两个 `Integer`
- `LookupPrivilegeValue` 改为接收 `IntPtr`，手工 `AllocHGlobal(16)` 布局
- 改用 `OpenProcessToken` / `GetCurrentProcess` / `CloseHandle`，
  避免 `WindowsIdentity` 的 `Using` 提前释放 token 句柄

**验证**：独立测试程序中，操作码 2/3/4 均返回 0（成功）。

</td><td width="50%" valign="top">

#### 2. 部分主题无法点击

**现象**：秋仪金、活跃橙、极客蓝点不动；小猫绿与秋仪金叠在一起。

**根因**：XAML 里覆盖了 4 个 `TextBlock` 透明遮罩（`LabLauncherTheme5Unlock` 等），
原本用于「未解锁时拦截点击」，但主题已全部开放，遮罩却没移除，鼠标事件全被吃掉。
另外主题 `Grid` 只定义了 3 行，`RadioLauncherTheme15` 却用了 `Grid.Row="3"`，越界后被画在同一行。

**修复**：删除 4 个遮罩与关联事件处理，补齐 `RowDefinition` / `ColumnDefinition`。

#### 3. 回声洞动画顺序错误

**现象**：留言先完整显示，然后才开始逐字打出。

**根因**：`AaTextAppear` 在**构造时**捕获 `Obj.Text`，动画过程中逐字写回。
如果先启动动画再改文本，捕获到的是旧文本，就会出现「先显示再打字」。

**修复**：调整为先写入新文本 → 构造动画 → 启动 → 恢复旧文本。
同时把闪烁效果从 2 步扩展到 11 步，过渡更自然。

</td></tr>
</table>

#### 4. 公告从未显示

**现象**：服务端发布了公告，客户端始终收不到。

**根因**：`FetchNoticesForHome()` 写好了但**从未被任何地方调用**。
`ModSync` 只把公告数量写进 `SyncState.NoticeCount`，没有渲染。

**修复**：主页新增「公告」卡片，`LoadNotices()` 在页面加载时拉取并渲染。
无公告时整张卡片隐藏，不占空间。

#### 5. 界面按钮尺寸不统一

**现象**：主页、设置页、登录页的按钮高矮不一（28 / 32 / 34 / 35 / 36 / 54 六种高度混用）。

**修复**：给 `MyButton` 设默认 `Height="35"`，把 79 个未设高度的按钮和 9 个偏离值统一到 35。
只有「正在加载」这类大按钮保留 54。

---

### 新增功能

<table>
<tr><td width="50%" valign="top">

#### 1. 启动时静默同步

启动时在后台拉取公告与赞助者名单，**失败完全不打扰用户**：

- 全程后台线程，不阻塞界面
- 每项独立 `try`，互不影响
- 失败只写 `Logger.Warn`，不弹窗、不提示条
- 调试模式额外输出 `Logger.Trace` 级明细

实测日志：

```
静默同步：开始
静默同步：检查更新成功，有新版本=False，公告 1 条
静默同步：赞助者名单成功，共 383 位
静默同步：完成，成功 2 项，失败 0 项
```

#### 2. 一言多源 + 本地缓存

原来只用一个 API，超时就只能显示固定文案。现在：

**4 个 API 依次尝试**

```
v1.hitokoto.cn
api.uomg.com
api.vvhan.com
international.v1.hitokoto.cn
```

**磁盘持久缓存**：`PCL\hitokoto_cache.json` 最多存 60 条。
断网时从池里随机取，不再是永远同一句话。

</td><td width="50%" valign="top">

#### 3. 模组加载器镜像加速

新增 `ModMirror` 模块，把官方 Maven 地址转换为镜像地址。

**下载顺序**（镜像只作最后后备）：

```
bmclapi（第一源） → 官方源 → ZCSM 镜像
```

官方源可用时**不会**走镜像，不影响原有优先级。
覆盖 NeoForge、Forge、Fabric、Quilt、Architectury。

可在设置中用 `ToolModMirror` 开关关闭。

#### 4. 投影预览卡片

主页常驻卡片，可直接选择 `.litematic` / `.nbt` / `.schem` 文件预览。

首次使用需下载查看器（约 394 MB），下载后自动解压。
打开时有「首次打开可能需要 10~30 秒」的提示。

#### 5. 社区与反馈入口

主页新增卡片，四列等宽布局，与「快捷工具」视觉一致：

- GitHub 仓库
- 反馈问题
- 新功能投票
- 更新日志（预留）

</td></tr>
</table>

#### 6. 回声洞内置 755 条留言

原版回声洞依赖服务端，服务端不可用时无内容可看。
本版本内置 755 条留言，优先从服务端取，失败时回退到内置数据。

#### 7. 主题「小猫绿」

新增第 16 种配色 **小猫绿**（`#5a7749`，HSL 98/24/-7）。

> **仅在自用版（`AllowAllThemes = True`）可用**。发布版只保留 3 种蓝色系，符合许可证要求。

---

### 体验优化

#### 1. 今日人品算法调整

**原算法**：把 `Raw`（0~1000 均匀分布）线性映射到 0~99 分。

实测分布：

| 指标 | 原版 |
|---|---|
| 平均分 | 51.1 |
| 低于 60 分占比 | 58.2% |
| 低于 80 分占比 | 77.8% |

近六成情况低于 60 分，容易影响心情。

**新算法**：分档映射，高分占绝大多数，低分逐级递减但 **0 分仍可达**。

| 区间 | 占比 | 说明 |
|---|---|---|
| 80~100 分 | 92% | 主体，平方曲线让 100 分更稀有（约 1.2%） |
| 60~79 分 | 5% | |
| 30~59 分 | 2% | |
| 10~29 分 | 0.7% | |
| 0~9 分 | 0.4% | 极罕见 |

实测（200 台机器 × 365 天 = 73000 次）：

```
平均分: 84.1    最低: 0    最高: 100
<80 占比: 8.1%
```

#### 2. 主页重新设计

- 渐变欢迎横幅（含一言）
- 三块统计卡片：当前版本 / 游戏目录 / 今日人品
- 快捷工具四列等宽
- 公告卡片（有内容才显示）
- 投影预览卡片
- 社区与反馈卡片

#### 3. 赞助者名单渲染优化

原来 383 个名字各建一个 `TextBlock` 并带 `ToolTip`，UI 线程要创建 383 个控件。
改为单个 `TextBlock` 拼接（`TextWrapping="Wrap"`），显著降低 WPF 布局与命中测试开销。

按需求**移除了悬浮详情**，只显示名字。

#### 4. 按钮抗锯齿

`MyCard`、`MyButton` 及 6 个控件加上 `UseLayoutRounding` 与 `SnapsToDevicePixels`，
消除高 DPI 下的边缘锯齿。

#### 5. 主题网格自适应

发布版只剩 3 个蓝色主题时，不再沿用 6 列 3 行的空网格（会留下大片空白），
而是动态构建 4 列网格（标签 + 3 个按钮），排成一行铺满宽度。

#### 6. 鸣谢区新增

「特别鸣谢」新增 **零核智能转储镜像（ZCSM）**，附头像与跳转按钮。

---

### 已知限制

| 项目 | 说明 |
|---|---|
| 服务端依赖 | 公告、反馈、投票需要自建服务端，未配置时相关功能静默失效 |
| 投影预览体积 | 查看器约 394 MB，需单独下载 |
| 离线安装 | `runtime/ndp48-web.exe` 是在线安装器，装机需联网 |
| 主题解锁 | 发布版仅 3 种蓝色，其余需赞助或成就解锁（许可证要求） |

---

## 环境要求

### 运行

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 7 SP1 或更高 |
| 运行时 | .NET Framework 4.8 |
| 架构 | x86 / x64 均可（AnyCPU） |
| 硬盘 | 本体约 8 MB |

Windows 10 版本 2004 及以后已内置 .NET Framework 4.8。较旧的系统需要安装：

- 在线安装器：`runtime/ndp48-web.exe`（约 1.4 MB，需联网）
- 离线安装包：从微软官网下载 `ndp48-x86-x64-allos-enu.exe`（约 112 MB）

### 编译

| 项目 | 要求 |
|---|---|
| MSBuild | .NET Framework 4.8 自带（`C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe`） |
| Visual Studio | 可选，2019 或更高 |
| Roslyn | 可选，需较新语法时使用 |

---

## 构建

### 命令行

```bat
cd EXE

"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" ^
  "Plain Craft Launcher 3\Plain Craft Launcher 3.vbproj" ^
  /p:Configuration=Release ^
  /p:Platform=AnyCPU ^
  /p:OutputPath="out\"
```

产物：`EXE\out\Plain Craft Launcher 3.exe`（约 7.3 MB，单文件）

### 使用 Roslyn

项目使用了一些较新的 VB 语法。如果系统自带的编译器报语法错误，指定 Roslyn 工具集：

```bat
MSBuild.exe "Plain Craft Launcher 3\Plain Craft Launcher 3.vbproj" ^
  /p:Configuration=Release ^
  /p:VbcToolPath="path\to\roslyn\" ^
  /p:VbcExe=vbc.exe ^
  /p:CscToolPath="path\to\roslyn\" ^
  /p:CscToolExe=csc.exe ^
  /p:LangVersion=latest
```

Roslyn 可从 [NuGet](https://www.nuget.org/packages/Microsoft.Net.Compilers.Toolset/) 下载，解压后指向 `tasks\net472` 目录。

### Visual Studio

1. 打开 `Plain Craft Launcher 3.vbproj`
2. 配置选 `Release`，平台选 `AnyCPU`
3. 生成 → 生成解决方案

### 构建配置

项目有三个条件编译符号：

| 符号 | 效果 |
|---|---|
| `DEBUG` | 开发版，显示调试信息 |
| `RELEASE` | 发布版（默认） |
| 都不定义 | 快照版 |

---

## 发布版 / 自用版

PCL 的许可证要求衍生作品的**界面主色相必须为蓝色**，因为更换主题色是原版通过赞助解锁的功能。

本项目通过一个常量控制：

```vb
' Plain Craft Launcher 3\Modules\Base\ModBase.vb 第 20 行
Public Const AllowAllThemes As Boolean = False
```

| 值 | 类型 | 可用主题 |
|---|---|---|
| `False` | **发布版** | 3 种蓝色系：龙猫蓝（210°）、甜柠青（180°）、极客蓝（195°） |
| `True` | **自用版** | 16 种配色全部可用，含小猫绿 |

### 行为差异

**发布版**（`False`）

- 设置页只显示 3 个蓝色主题按钮，排成一行
- 非蓝色主题的解锁请求静默忽略（不弹提示）
- 若当前保存的主题不是蓝色系，启动时自动回退到龙猫蓝

**自用版**（`True`）

- 全部 16 个主题按钮可见，6 列 3 行布局
- 小猫绿自动解锁
- 其他隐藏主题仍需赞助或成就解锁

> **分发编译产物时请使用 `False`**，否则可能违反许可证。

---

## 配置

### 服务端地址

`Modules/ModUpdate.vb`：

```vb
Public Const ApiBase As String = "https://your-domain.example/api/"
```

改成你自己的服务端地址。留空或指向无效地址时，客户端仍能正常启动游戏，只是收不到公告与更新推送。

### 客户端鉴权

`Modules/ModByethost.vb`：

```vb
Public Const ClientApiKey As String = "REPLACE_WITH_YOUR_CLIENT_API_KEY"
```

必须与服务端 `api/index.php` 里的 `CLIENT_API_KEY` 完全一致。

### 一言 API

`Modules/ModHitokoto.vb`：

```vb
Private ReadOnly ApiList As String() = {
    "https://v1.hitokoto.cn/?encode=json&max_length=60",
    "https://api.uomg.com/api/rand.qinghua?format=json",
    "https://api.vvhan.com/api/ian?type=json",
    "https://international.v1.hitokoto.cn/?encode=json&max_length=60"
}
```

按顺序尝试，全部失败时从本地缓存池取（`PCL\hitokoto_cache.json`，最多 60 条）。可以按需增删。

### 模组加载器镜像

`Modules/ModMirror.vb`：

```vb
Private Const MirrorRoot As String = "https://mirror.zcswo.cn"
```

镜像作为**最后后备**，下载顺序是：

```
bmclapi（第一源） → 官方源 → 镜像
```

官方源可用时不会走镜像。可在设置中通过 `ToolModMirror` 开关关闭。

---

## 项目结构

```
Plain Craft Launcher 3/
├── Controls/              自定义控件
│   ├── MyButton.xaml      按钮（圆角 5，高 35）
│   ├── MyCard.vb          卡片容器
│   ├── MyListItem.xaml    列表项
│   └── ...
├── Images/                图片资源
│   ├── Heads/             头像（鸣谢区用）
│   ├── Icons/             图标
│   ├── Themes/            主题预览
│   └── Blocks/            方块图标
├── Modules/               业务逻辑
│   ├── Base/              基础设施
│   │   ├── ModBase.vb     全局常量、路径
│   │   ├── ModNet.vb      网络层
│   │   └── PclLogger.vb   日志
│   ├── Minecraft/         游戏相关
│   │   ├── ModLaunch.vb   启动流程
│   │   ├── ModDownload.vb 资源下载
│   │   └── ModJava.vb     Java 管理
│   ├── ModMirror.vb       镜像加速
│   ├── ModSync.vb         启动静默同步
│   ├── ModUpdate.vb       版本检查
│   ├── ModHitokoto.vb     一言
│   ├── ModFeedback.vb     反馈
│   ├── ModByethost.vb     挑战绕过
│   └── ModSecret.vb       主题配色
├── Pages/                 页面
│   ├── PageLaunch/        主页、登录
│   ├── PageDownload/      下载
│   ├── PageSetup/         设置
│   └── PageOther/         关于、反馈、工具
├── Resources/             嵌入资源与依赖 DLL
├── My Project/            程序集信息
├── Application.xaml.vb    程序入口
└── FormMain.xaml.vb       主窗口
```

### 关键模块说明

| 模块 | 作用 | 备注 |
|---|---|---|
| `ModNet.vb` | HTTP 封装、多源下载、重试 | 核心，改动需谨慎 |
| `ModMirror.vb` | 官方 Maven 地址 → 镜像地址转换 | 纯字符串处理，无副作用 |
| `ModSync.vb` | 启动时后台拉取公告与赞助者 | 失败只写日志，不打扰用户 |
| `ModUpdate.vb` | 检查更新、下载安装包 | 带 SHA-256 校验 |
| `ModByethost.vb` | 解析 AES 挑战页算出 Cookie | 特定主机才需要 |
| `ModSecret.vb` | 主题色相/饱和度计算与解锁 | 改动会影响许可证合规性 |

---

## 依赖库

`Resources/` 下的 DLL 会在构建时嵌入：

| DLL | 版本 | 用途 |
|---|---|---|
| `Newtonsoft.Json.dll` | 13.x | JSON 解析 |
| `NAudio.dll` | 2.x | 音频播放 |
| `Ookii.Dialogs.Wpf.dll` | 4.x | 系统文件对话框 |
| `MeloongCore.dll` | 上游 | 核心库（日志、设置、动画） |
| `MeloongCore.Wpf.dll` | 上游 | WPF 扩展 |
| `PCLCS.dll` | 本项目 | C# 辅助功能 |
| `libwebp64.dll` | 1.x | WebP 解码 |
| `Imazen.WebP.dll` | 1.x | WebP 托管封装 |
| `CacheCow.*.dll` | — | HTTP 缓存 |
| `System.Net.Http.Formatting.dll` | — | HTTP 格式化 |
| `ThrottleDebounce.dll` | — | 防抖节流 |
| `Microsoft.Win32.Registry.dll` | — | 注册表访问 |

> `MeloongCore` 系列是上游 PCL 提供的二进制库，源码未公开。本项目仅调用其公开 API。

---

## 单文件打包

项目使用 `ModSingleFile` 机制，把所有依赖嵌入 exe：

```vb
' Application.xaml.vb
Shared Sub New()
    ModSingleFile.EnsureInitialized()
End Sub
```

原理是注册 `AppDomain.AssemblyResolve` 回调，在 CLR 解析依赖前从内嵌资源加载。因此**必须是静态构造函数**——这是整个程序里最早能插入代码的位置。

### 添加新的嵌入 DLL

1. 把 DLL 放入 `Resources/`
2. 在 `.vbproj` 中登记：

   ```xml
   <EmbeddedResource Include="Resources\你的库.dll">
     <LogicalName>你的库.dll</LogicalName>
   </EmbeddedResource>
   ```

3. `ModSingleFile` 会自动处理

### 添加图片资源

图片用 `<Resource>` 而非 `<EmbeddedResource>`：

```xml
<Resource Include="Images\Heads\新头像.png" />
```

XAML 里引用：

```xml
<Image Source="/Plain Craft Launcher 3;component/Images/Heads/新头像.png" />
```

> **常见坑**：忘记登记 `<Resource>` 时，XAML 编译不报错，但运行时会找不到图片，显示为空白。新增头像后务必检查 `.vbproj`。

---

## 调试

### 日志

运行日志在 `PCL\Log1.txt`（旧日志会轮转）。默认级别 `Info`，调试模式下为 `Trace`。

```vb
Logger.Info("普通信息")
Logger.Warn("警告，不中断")
Logger.Error(ex, "错误")
Logger.Trace("仅调试模式输出")
```

### 调试模式

`Modules/Base/ModBase.vb`：

```vb
Public Const BuildType As BuildTypes = BuildTypes.Debug
```

调试模式下：

- 日志级别降为 `Trace`
- 主页显示日志面板
- 部分提示改为弹窗
- `ModDevelop.Start()` 会执行测试代码

### 日志脱敏

`PclLogger.Format()` 会自动过滤用户名与 AccessToken：

```vb
Text = FilterUserName(FilterAccessToken(Text, "*"c), "*"c)
```

提交日志到 Issue 时不用担心泄露账号。

---

## 常见问题

<details>
<summary><b>启动时提示缺少 .NET Framework</b></summary>

安装 .NET Framework 4.8。Windows 10 2004+ 已内置，无需安装。

运行 `runtime/ndp48-web.exe` 是在线安装器，需要联网。
</details>

<details>
<summary><b>编译报「关键字作为标识符无效」</b></summary>

通常是用了 VB 关键字作变量名（如 `SyncLock`、`From`、`To`）。改个名字即可，或检查是否用了较新语法但没指定 Roslyn。
</details>

<details>
<summary><b>编译报「根级别上的数据无效」（MC3000）</b></summary>

XAML 文件有重复的 UTF-8 BOM。用十六进制编辑器检查文件开头，应该只有一组 `EF BB BF`。

常见于用脚本批量改写文件时。
</details>

<details>
<summary><b>新增的图片不显示</b></summary>

检查 `.vbproj` 里是否有对应的 `<Resource Include="..." />`。XAML 编译不会因此报错，但运行时找不到资源。
</details>

<details>
<summary><b>主题按钮点了没反应</b></summary>

发布版（`AllowAllThemes = False`）只允许蓝色系。点非蓝色主题会被静默忽略，这是许可证要求的行为。

需要全部主题请用 `AllowAllThemes = True` 重新编译。
</details>

<details>
<summary><b>更新提示点了没反应</b></summary>

检查 `ModUpdate.ApiBase` 是否指向有效的服务端。服务端不可达时，客户端只写日志，不弹错误提示。

可在 `PCL\Log1.txt` 里搜索「静默同步」查看详情。
</details>

<details>
<summary><b>下载模组加载器很慢</b></summary>

默认顺序是 bmclapi → 官方 → 镜像。如果都慢，可能是网络问题。

可在设置里关闭 `ToolModMirror` 排除镜像因素，或检查 `ModNet.vb` 里的并发数配置。
</details>

---

## 安全

### 账号数据

存在注册表 `HKCU\Software\PCLDebug`，DES 加密，密钥由 `"PCL" + 本机识别码` 派生：

```vb
Dim KeyText As String = ("PCL" & Identify).GetStableHashCode().ToString().EnsureLength("X"c, 8).Substring(0, 8)
```

`Identify` 基于本机硬件。换机器后密钥不同，**无法解密**。

> 这是上游设计，非强加密。能防「换机读取」，防不住同机恶意程序。

### 客户端凭据

只硬编码 `CLIENT_API_KEY`，仅能读取公开数据、提交反馈。**不能**登录后台或修改数据。

### 不要提交的内容

- 真实的服务端地址与密钥
- `PCL\` 目录下的日志与缓存
- 编译产物（`obj/`、`bin/`、`*.exe`）
- 任何 `*.user` 文件

`.gitignore` 已覆盖上述内容。

---

## 许可证

本项目遵循 [PCL 分发有限许可](../LICENCE)，属于**重度使用**。

分发编译产物前请确认：

- [ ] `AllowAllThemes` 为 `False`
- [ ] 关于页保留了龙腾猫跃的署名与赞助链接
- [ ] 程序名称以 `Plain Craft Launcher` 开头并带第三方修改后缀
- [ ] 附带了 `LICENCE` 文件
- [ ] 保留了 `McLaunchPrecheck` 正版购买劝导

**本项目是第三方二次创作，并非 PCL 官方发行版本。**

- 原始作者：龙腾猫跃 / 成都瓜皮龙科技有限公司
- 著作权登记号：`2020SR0875133`

---

<div align="center">

**本项目与 Mojang Studios、Microsoft 无关联。**

Minecraft 是 Mojang Studios 的商标。

</div>
