# 本项目 修复清单

> 版本：1.0.0 (415)
> 基线：Plain Craft Launcher 2.13.1.1
> 日期：2026-11-06

---

## 零、先厘清：什么不是原版的问题

上游开源仓库 `Meloong-Git/PCL` 的 `Modules/PageOther/PageOtherTest.xaml.vb` 中，
8 个函数是**占位实现**：

```vb
Public Shared Sub MemoryOptimize(ShowHint As Boolean)
    If ShowHint Then Hint("为便于维护，开源内容中不包含百宝箱功能……")
End Sub
```

涉及 `MemoryOptimize`、`MemoryOptimizeInternal`、`Jrrp`、`RubbishClear`、
`GetRandomCave`、`GetRandomHint`、`GetRandomPresetHint`、`StartCustomDownload`。

**正式发布的 PCL 中这些功能真实可用**，作者只是未开源实现。因此：

- 本版本补全这些实现 = **补功能**，不是修 Bug
- 本版本曾把「内存优化无效」「公告不显示」「反馈卡死」等写成「原版问题」，
  其中后两者实为本版本自身改造引入，现已更正
- 本文档只记录两类内容：**上游公开 Issue 的真实修复**，以及**本版本自身引入问题的修正**

---

## 一、本轮新增修复（来源：Meloong-Git/PCL 真实 Issue）

抓取上游全部 **1058 个 Issue**（79 开放 / 979 已关闭），筛选可代码层验证的条目逐条排查。

| Issue | 标题 | 修复要点 | 文件 |
|---|---|---|---|
| **#9412** | ModModpack.vb 第 280 行应为 fileName | CurseForge API 返回 `fileName`（小写开头），原代码读 `FileName` 静默失效。改为两者兼容 | `ModModpack.vb:279` |
| **#9374** | 可选文件弹窗可能完全不显示 Mod 名称 | 原用 `displayName`（文件名 `[1.20.1] Forge 1.4.0`），改为优先取项目名（`Factory Blocks`），失败回退 | `ModModpack.vb:265` |
| **#9242** | 输入的链接没 https:// 会默认加 `htps://` | 新增 `NormalizeUrl()`，自动补协议头（只认已知 scheme） | `ModBase.vb:1016` |
| **#9277** | 自定义主页中文域名不转 Punycode | 同 `NormalizeUrl()`，检测非 ASCII 域名并 `IdnMapping.GetAscii()` 转换 | `ModBase.vb:1016` |
| **#9360** | 多 Mod 并发更新时替换失败 | 临时目录原全局共用 `PathTemp\mods\`，改为按实例哈希隔离 | `PageInstanceMod.xaml.vb:580` |
| **#9001** | WebP 不兼容 AE2 Logo 动图 | ① 文件头判断原只看 `RI`（AVI/WAV 也是 RIFF），改为校验 `RIFF....WEBP`<br>② 动图解码失败时降级为 WPF 解首帧，不再整图变占位符 | `MyBitmap.vb:76` |
| **#9343** | 主页替换标记玩家名/版本启动时不加载 | 实例与登录未就绪时**保留原标记**而非替换为空，避免永久空白 | `ModMain.vb:865` |
| **#9188** | 清理 %temp% 后点「查看输出」崩溃 | `FileUtils.Write` 前确保目录存在，整段加 Try 兜底为复制剪贴板 | `ModCrash.vb:870` |
| **#9404** | 无法启动 13w18a / 13w18b | 旧版 `minecraftArguments` 无条件追加 `--height/--width`，极早期版本不认该选项，抛 `UnrecognizedOptionException`。改为仅原参数已含此选项时才补 | `ModLaunch.vb:1513` |
| **#9026** | 下载 jre-legacy 后仍找不到合适 Java | 下载完成后 JavaList 缓存可能未刷新，直接选择得到 Nothing 并抛异常。改为先强制刷新列表再选 | `ModJava.vb:463` |
| **#9370** | 路径 ≥260 字符拖入被静默截断 | 固定 `StringBuilder(MAX_PATH)` 会截断长路径，导致提示「文件不存在」。改为先用 `DragQueryFile` 查实际长度再按需分配 | `DragHelper.vb:140` |
| **#9003** | 批量更新模组替换失败会丢失旧模组 | 原逻辑先删光旧 Mod 再移新文件，移动失败就两头落空。改为**事务式**：先把旧文件全部备份为 `.pclbak`，全部成功才移入新文件，任一步失败按日志逆序回滚（借鉴 PCL-Living-Fork 的备份/回滚事务设计） | `PageInstanceMod.xaml.vb:595` |

**额外修复（非上游 Issue，自查发现）**

| 问题 | 说明 | 文件 |
|---|---|---|
| 今日人品弹窗重复触发 | `Jrrp()` 无防重入，点「我知道了」的落点若命中主页人品卡片会再弹一次。加 `Interlocked.Exchange` 守卫 + Try/Finally | `PageOtherTest.xaml.vb:232` |

---

## 二、基线已包含的上游修复

基线代码已标注并实现以下修复（`git diff` 可验证代码真实存在）：

| Issue | 标题 | 文件 |
|---|---|---|
| #8147 | LiteLoader + OptiFine 组合安装方式错误 | `ModDownloadLib.vb` |
| #8150 | 未能找到 `System.Net.Http.Formatting.resources` | `ModNet.vb:273` |
| #8210 | LWJGL 3 不应被视为 Mod 加载器 | `ModModpack.vb` |
| #8246 | 下载资源包时「要求为负数」 | `ResourceSearcher.vb` |
| #8265 | 版本呢？（搜索结果为空提示） | `ResourceSearcher.vb` |
| #8377 | 无法显示 Modrinth 整合包版本列表 | `ResourceVersion.vb` |
| #8488 | 可能错误地触发下载超时 | `ModNet.vb` |
| #8611 | 微软官网撤销登录权限时报错 | `ModLaunch.vb` |
| #8655 | Xbox 登录支持新封禁信息 | `ModLaunch.vb` |
| #8739 | 1.21.11 离线模式中文游戏名乱码 | `ModLaunch.vb` |
| #8950 | MC 日志编码有误 | `ModLaunch.vb` |
| #9440 | 关闭游戏后再次启动被静默取消 | `ModLoader.vb:325` |
| #9448 | 空白皮肤在启动器显示为透明、游戏内为纯黑 | `MySkin.xaml.vb:110` |
| #9475 | 删除旧版本创造「幽灵版本」 | `ModMinecraft.vb:1458` |
| #9482 | 游戏窗口重建后追踪跟丢 | `ModWatcher.vb:266` |
| #9495 | 启动日志多渲染一行空白 | `ModLaunch.vb:73` |
| #9499 | 扫描访问已失效的旧版本目录 | `ModMinecraft.vb:1458` |
| #9533 | Java 下载列表失败后读 LastResult 抛异常 | `ModJava.vb:406` |
| #9536 | 下载文件导向 bug | `PageDownloadCompDetail.xaml.vb` |

另有约 60 个更早期 Issue 修复（`#184`、`#1057`、`#1344`、`#4505`、`#4868`、`#5195`、`#5879`、`#6683`、`#7825` 等），分布在 `Controls/` 与 `Modules/`。

---

## 三、界面改动

### 标题栏

**改动前**：显示 PCL 字母图形（`ShapeTitleLogo`），窗口 Title 为 `"Plain Craft Launcher　"`

**改动后**：

```
┌─────────────────────────────────────────────┐
│  本项目 by Slate_xd    启动 下载 联机 设置 更多  ─ □ ✕ │
└─────────────────────────────────────────────┘
```

| 元素 | 实现 |
|---|---|
| `本项目` | `TextBlock` 19px SemiBold，纯白 |
| `by Slate_xd` | `TextBlock` 10.5px，`VerticalAlignment="Bottom"` + 底部 3px 边距，形成右下角小字效果 |
| 颜色随主题 | `Foreground="{DynamicResource ColorBrush6}"` — 该画刷在 `ModSecret.vb:L116` 随主题刷新（HSL 色相跟随主题，亮度 91%） |
| 窗口标题 | `Title="Plain Craft Launcher"` |

`PanTitleBrand` 在 `UiLogoType` 的所有分支（0/1/2/3）下均设为 `Visible`，并把原 `ShapeTitleLogo`、`LabTitleLogo`、`ImageTitleLogo` 全部折叠，避免叠加。

设置页的「自定义标题文本」「更换标题图片」选项同步隐藏（`PanLogoText` / `PanLogoChange` 强制 Collapsed）。

---

## 四、验证结果

### 构建

| 版本 | 大小 | SHA-256（前 16） | 状态 |
|---|---|---|---|
| 发布版 `AllowAllThemes = False` | 7.27 MB | `22B3A1619A880AFB` | ✅ |
| 自用版 `AllowAllThemes = True` | 7.26 MB | `1E7A2F69011DD855` | ✅ |

自用版通过复制源码到临时目录构建，**主源码未被改动**，`AllowAllThemes` 保持 `False`。

### 运行

单次启动 20 秒，日志 **68 行，0 条错误、0 条警告**：

```
[Application] 程序版本：内测版 1.0.0 (415)
[FormMain]    从设置更新背景图片与标题栏样式
[FormMain]    窗口已置顶，位置：(20, 10), 1240 x 660
窗口标题: 'Plain Craft Launcher'
[ModByethost] 已算出 Byethost 挑战 cookie
[ModHitokoto] 已获取一言
[ModDownload] Mojang 官方源加载耗时：491ms，可优先使用官方源
[ModUpdate]   公告：服务端返回 1 条
[ModSync]     静默同步：完成，成功 2 项，失败 0 项
```

### 编码

本次改动的 10 个文件全部为**单 UTF-8 BOM**（VB.NET/WPF 编译硬性要求，双 BOM 会导致 `BC30037`）。

---

## 五、未修复的开放 Issue 及原因

上游 79 个开放 Issue 中，以下类型**未在代码层处理**：

| 类型 | 例子 | 原因 |
|---|---|---|
| 环境问题 | #1108 `libjlw-1.3.2.dll` 找不到、#8528 缺 VC++ 运行库 | 用户系统缺失组件，非启动器可修复 |
| 需第三方配合 | #4334 Modrinth 无法访问（TLS/SNI） | 取决于网络环境与 Modrinth 侧策略 |
| 上游未定位 | #8660 罕见竞态、#8126 特定模组识别 | 上游标「需要社区复现」，无稳定复现路径 |
| 功能建议 | #8340 资源包管理、#8957 版本界面排序 | 属新功能，不在「修复」范畴 |
| 已由基线修复 | #9440/#9448/#9475/#9482/#9495/#9499/#9533 | 见第二节 |

---

## 六、SPT 交互检查与优化

依据 SPT 交互设计方法，对客户端高频操作区做了一次证据化检查。

### 发现的问题（代码证据）

| 位置 | 问题 | 证据 |
|---|---|---|
| 主页快捷工具 | 「内存优化」点击后无进行中状态，可重复触发 | `PageLaunchRight.xaml.vb` 原直接 `RunInThread` |
| 公告卡片 | 加载失败静默隐藏，用户无法区分「没公告」与「加载失败」 | 原 `Catch` 只写日志 |
| 今日人品 | `Interlocked` 防重入但重复点击静默返回 | `PageOtherTest.xaml.vb` `If ... <> 0 Then Return` |

### 已实施（选用模式 + 理由）

| 位置 | 模式 | 理由 |
|---|---|---|
| 内存优化按钮 | **01-7 按钮加载** | 耗时不定，禁用 + 改文案最直接；`Finally` 保证失败也能立即重试 |
| 公告失败态 | **22 任务反馈** | 内联提示 + 重试入口，区分空与失败；重试成功后由 `RenderNotices` 自然清空 |

### 本次未采用

- 骨架屏 / 扫光：数据来自本地内存，无网络等待
- 3D 卡片、粒子：主页是高频操作区，动效会干扰
- 全局路由动画：WPF 页面切换已有机制，重做收益低

### 验证

- 构建：★ 成功，0 错误
- 运行：68 行日志，**0 错误 0 警告**，公告正常显示 2 条
- 未实测：内存优化按钮的禁用/恢复时序（需真实点击），公告失败重试（需断网）

---

## 七、从 PCL-Living-Fork 移植的功能

参考 [PCL-Living-Fork](https://github.com/DDZmumo/PCL-Living-Fork)（同为 PCL 第三方修改版，遵循同一份许可）。
按需移植，不照搬其策略层。

### 1. 模组批量更新：事务式替换

原逻辑先删光旧 Mod 再移新文件，移动失败就两头落空（上游 Issue #9003）。

本实现改为事务：

1. **阶段一** 把所有将被替换的旧文件挪到 `.pclbak`，写入日志
2. **阶段二** 全部备份成功后才逐个移入新文件
3. **阶段三** 全部成功才删除备份；任一步失败按日志**逆序回滚**

失败或取消都会恢复成更新前的状态，不会留下半更新。

### 2. 版本范围三态比较

`Modules/Minecraft/ModVersionRange.vb`，为后续模组依赖检查打地基。

返回三态而非布尔值：`Satisfied` / `Unsatisfied` / `Unknown`。

> **Unknown 是刻意设计**：版本写法千奇百怪，猜错会让玩家无法启动本来正常的游戏。
> 宁可标为未知交给玩家判断，也不给假结论。

支持：

- 比较运算符 `>= <= > < =`
- 多段约束（与）、`||`（或）
- Fabric `^` / `~`（含 `^0.2.1` 可匹配 `0.9.0` 的边界）
- 通配 `1.20.x` / `1.*`
- 预发布排序（`1.20-beta` &lt; `1.20`）
- Maven 区间 `[1.0,2.0)` / `[1.20]`

### 验证

单元测试：`tests\run-versionrange-tests.ps1`

```
通过 36，失败 0
```

覆盖 9 个类别，含 Unknown 保护的 6 个用例（未知版本、unknown 字符串、空值、占位符、latest、畸形约束）。

---

## 八、新增功能

### Mod 回收站

**问题**：PCL 删除 Mod 时默认丢进 Windows 回收站，但玩家在启动器里没有任何入口能找回来；
系统回收站里文件名还被系统改写过，根本认不出是哪个 Mod。

**实现**：在实例目录下维护 `.pclrecycle\`，删除时移入并记 JSON 元数据（原路径、原名、时间、大小）。

| 操作 | 行为 |
|---|---|
| 删除 Mod（不按 Shift） | 移入 `.pclrecycle\`，可还原 |
| 删除 Mod（按 Shift） | 保持原行为，直接进系统回收站 |
| 工具栏「回收站」 | 列出条目 → 选还原 / 彻底删除 |
| 上限 | 60 条，超出按时间清理最旧 |

**设计取舍**：

- 不依赖系统回收站（COM 操作复杂且还原需 `SHFileOperation` 以外的路径），改为自建索引，完全可控
- 还原时目标已存在则**不覆盖**，返回失败让玩家自己处理
- 元数据损坏时按空回收站处理，不让坏文件卡死整个 Mod 管理页

**SPT 交互**：分两步（选条目 → 选动作），每步都可取消，取消无副作用。
彻底删除前二次确认并标注「不可恢复」。

---

### 存档自动备份

**问题**：世界误删或存档损坏时没有退路；玩家手动备份容易忘。

**实现**：游戏正常退出后（`McLaunchEnd`），把 `saves\` 下有变化的世界打包到 `.pclbackup\`。

| 设计点 | 做法 |
|---|---|
| 只备份有变化的 | 用 `level.dat` 的大小 + 修改时间做指纹，与上次比对，没变就跳过 |
| 保留份数 | 每个世界默认 3 份，超出按时间清理最旧 |
| 体积上限 | 单世界 800 MB，超过跳过并写日志 |
| 失败处理 | 全部包在 Try 里，只写日志，**不影响游戏退出流程** |
| 还原 | 目标非空时拒绝覆盖，避免误操作毁掉现有存档 |

**为什么用 zip**：存档动辄几百 MB，zip 能显著减小占用；解压也方便玩家自己处理。

---

### 模组元数据解析

`Modules/Minecraft/ModMeta.vb`，为依赖检查提供地基。

按加载器分别读取，**不混着解析**：

| 加载器 | 声明文件 |
|---|---|
| Fabric | `fabric.mod.json` |
| Quilt | `quilt.mod.json`（内容在 `quilt_loader` 下） |
| NeoForge | `META-INF/neoforge.mods.toml` |
| Forge 1.13+ | `META-INF/mods.toml` |
| 旧版 Forge | `mcmod.info` |

**设计原则**：读不出来就说读不出来。解析失败一律 `Parsed=False`，
绝不因为解析不出来就报「缺前置」——那会让玩家无法启动本来正常的游戏。

**踩过的坑**：

- Fabric 的 `depends` 是 `{"modid": ">=1.0"}` 对象，`For Each` 拿到的是 `JProperty`，
  `ToString()` 会返回 `"key": "value"` 整行而不是值。必须用 `.Properties()` 遍历。
- 同时要支持 `[{"id": ..., "version": ...}]` 数组写法。
- VB 里 `"#"c` 会被当成预处理指令，用 `ChrW(35)` 常量绕开。
- `Using Stream = Entry.Open()` 会让 `Stream` 遮蔽 `System.IO.Stream` 类型，必须换名。

**测试**：`tests\run-modmeta-tests.ps1`，用真实 JAR 样本验证四种加载器。

```
通过 20，失败 0
```

---

### 模组依赖检查

`Modules/Minecraft/ModDependencyCheck.vb` + Mod 管理页「检查依赖」按钮。

判断链条：`ModMeta.ReadMeta` 读声明 → 与本地 ID 表比对 → `ModVersionRange.MatchVersion` 校验版本。

**三态原则**：

| 结果 | 处理 |
|---|---|
| 确定不满足 | 列为问题 |
| 确定满足 | 通过 |
| **无法确认** | 单独归类，**不当作错误** |

版本范围写法太杂，猜错会让玩家删掉本来能用的 Mod。所以「无法确认」只作提示，不报警。

**自动跳过**：`fabricloader`、`fabric-api`、`minecraft`、`java`、`forge`、`neoforge`
—— 这些由启动器负责，不是玩家该补的。

**只读**：不执行 JAR 代码、不自动下载、不自动删除。

**测试**：`tests\run-depcheck-tests.ps1`，构造真实 JAR 验证

```
参与检查: 3   解析失败: 0   确定问题: 2
[VersionMismatch] Mod A 需要 cloth-config >=11.0.0，本地是 9.0.0
[Missing] Mod C 需要前置 sodium
通过 5，失败 0
```

---

### 联机功能修复

**问题**：PCL 开源版把联机写死为不可用。

`PageLinkMain.xaml.vb` 原文：

```vb
If VersionBranchMain <> "Official" Then
    Throw New Exception($"$开源版无法联网获取配置。……")
End If
```

`ModSecret.vb` 的 `ServerLoader` 也是空的，只打一行日志，`ServerConfig` 永远是 `Nothing`。
而联机模块（EasyTier）的下载地址、版本号都从 `ServerConfig("Link")` 取，所以整条链路断掉。

**修复**：

1. 服务端新增 `action=link_config`，返回 `Link.EasyTierVersion` / `Downloads` / `MinVersionCode`
2. `ServerLoader` 改为真正向自建服务端请求并填充 `ServerConfig`
3. 去掉 `PageLinkMain` 里的开源版限制

**下载源**：EasyTier v2.6.4，三个地址依次尝试

| 顺序 | 地址 |
|---|---|
| 1 | GitHub Releases 官方 |
| 2 | ghfast.top 加速 |
| 3 | gh-proxy.com 加速 |

`{arch}` 占位符由客户端替换成 `x86_64` / `arm64` / `i686`。

**验证**：接口返回 200，三个字段齐全，`{arch}` 占位符存在。

> **未实测**：实际创建/加入房间需要真实网络环境与 EasyTier 组件，本轮只验证到配置链路。

---

## 九、SPT 二次检查（本轮）

按 SPT 方法复查客户端与新增功能，逐个判断而非套用。

### 发现并修复的真实缺陷

| 位置 | 问题 | 修复 |
|---|---|---|
| 投影预览下载 | 异常时按钮**永久禁用**，恢复逻辑写在成功路径里，玩家只能重启 | 改为 `Try/Finally` 保证恢复；失败显示「下载失败，可点击重试」并归零进度条 |
| 回收站 | `ClearAll` 已实现但无入口，是死代码 | 选择列表首项加「清空回收站（不可恢复）」，二次确认 |
| 存档备份 | `ListBackups` / `RestoreBackup` 无入口，**只写不读** | 实例设置页新增「存档备份」卡片 + 还原流程 |

### 检查后判定「不改」的项

| 位置 | 判定 | 理由 |
|---|---|---|
| 整合包更新进度 | ✔ 保留 | 已有 `ProgressWeight` 粗粒度进度，够用 |
| 微软登录 | ✔ 保留 | `Finally` 恢复按钮，已有 SSL 专项提示 |
| `MyMsgSelect` 确认按钮 | ✔ 保留 | 选中前禁用是**正确设计**，不是缺陷 |
| `ModJava` 迁移 | ✔ 保留 | 有 `Finally` 恢复 |

### 死代码清理结果

```
      ModRecycleBin          ✔ 无死代码
      ModSaveBackup          ✔ 无死代码
      ModDependencyCheck     ✔ 无死代码
      ```

---

### NAT 类型检测（借鉴 LauncherX）

从 LauncherX 的 `zh_cn.axaml` 语言包里整理出它的功能清单（1482 条文案），
其中**NAT 类型检测**对刚修好的联机功能最有价值。

**为什么需要**：联机失败最常见的原因就是网络环境（对称 NAT、UDP 被封）。
与其让玩家点「创建房间」等半分钟再报错，不如进页面就告诉他原因。

**实现**：`Modules/ModNatCheck.vb`，纯 STUN 协议（RFC 5389），无外部依赖。

1. 向 STUN 服务器发 Binding 请求，拿到它看到的公网地址
2. 换服务器再问一次，比对映射端口
3. 端口变了 → 对称 NAT；端口一致 → 锥形；与本地相同 → 公网 IP

支持识别的类型：`OpenInternet` / `FullCone` / `RestrictedCone` /
`PortRestrictedCone` / `Symmetric` / `UdpBlocked` / `UnsupportedServer`。

**实测结果**（本机）：

```
耗时: 3533 ms
类型: Symmetric
公网地址: 120.243.158.237:11434
说明: 当前 NAT 类型为：对称 NAT。P2P 打洞有一定几率失败
```

**设计原则**：检测失败一律返回 `UnsupportedServer` 或 `Unknown`，**绝不猜测**——
误报「你的网络不行」会让玩家放弃本来能用的功能。只有不确定时才显示「重新检测」。

**UI**：联机页「创建房间」上方一行状态文字，进页面时后台检测，不阻塞界面。

---

## 十、Terracotta 联机接入

**为什么换**：原来手写的 EasyTier 命令行方案能跑，但要自己拼参数、自己编码邀请码、自己判断 NAT，
成熟度和维护成本都不如现成方案。Terracotta（陶瓦联机）是 HMCL 也在用的成熟实现。

### 许可合规（关键）

Terracotta 是 **AGPL-3.0**，但有明确例外条款：

> 1. 通过打包方式包含本作品未经修改的二进制形式，而没有静态或动态地链接到本作品；或
> 2. 通过本作品提供的进程间通信接口（如 HTTP API）与未经修改的本作品应用程序进行交互，
>    且在您的程序用户界面明显处标识了本作品的版权信息。

**我们走第 2 条**：只调 HTTP API，不链接任何代码，界面标注版权。
因此本项目不会被 AGPL 涵盖。

### 集成方式

| 环节 | 做法 |
|---|---|
| 启动 | `terracotta.exe --hmcl <tmpfile>`，从临时文件读端口 |
| 状态 | 轮询 `GET /state`，500ms 间隔，只处理 index 增大的响应 |
| 开房 | `GET /state/scanning?player=xxx` |
| 加入 | `GET /state/guesting?room=xxx&player=xxx` |
| 复位 | `GET /state/ide` |
| 退出 | `GET /panic?peaceful=true` |

### 状态映射

| Terracotta | 我们的页面 |
|---|---|
| `waiting` | 待机 |
| `host-scanning` | 提示玩家进入存档并对局域网开放 |
| `host-starting` | 创建房间中 |
| `host-ok` | 完成页，展示邀请码 |
| `guest-connecting` / `guest-starting` | 连接中（带 NAT 难度） |
| `guest-ok` | 完成页，展示直连地址 |
| `exception` | 失败页，按异常码翻译成人话 |

**回退设计**：Terracotta 不可用时自动退回原有 EasyTier 实现，
不让一次组件缺失就把联机功能整个打死。

### 服务端安全加固

| 项 | 问题 | 修复 |
|---|---|---|
| 路径穿越 | `readJson`/`writeJson` 直接拼 `$name` | 新增 `safeDataName()`，只允许 `[A-Za-z0-9_-]` |
| 上传 | 不校验类型与体积 | 限制 1KB~200MB，且必须 `MZ` 开头的 PE 文件 |

**审计结论**：无 `eval`/`exec`/`system` 等危险函数；无 SQL（全 JSON 文件存储）；
密码用 `password_verify` + bcrypt cost 12；令牌比较用 `hash_equals` 防时序攻击；有登录锁定。

> `CLIENT_API_KEY` 明文在源码中，但它只用于区分客户端与浏览器，不是安全边界。

### 死代码清理

删除 `ModNatCheck.IsGood`、`ModTerracotta.IsReady`、`ModTerracotta.ResetToIdle`。
同时清掉误提交的 `*_wpftmp.vbproj`（MSBuild XAML 编译临时文件），并加 `.gitignore` 规则。

---

## 十一、关键陷阱备忘

| 陷阱 | 说明 |
|---|---|
| **双 BOM** | 用 `UTF8Encoding($true)` 写已含 BOM 的文件会产生双 BOM → `BC30037`。必须先去净 BOM 再写 |
| **`End Sub))`** | 修改 `Sub()` 匿名委托体时，替换范围极易连同 `End Sub))` 一起删掉 |
| **注释不能插在续行中间** | VB 的 `OrElse _` 续行里插注释会破坏续行 |
| **修改 `AllowAllThemes`** | 必须复制源码到临时目录构建，不可直接改主源码后忘记改回 |
