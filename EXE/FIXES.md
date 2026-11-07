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
| **#9003** | 批量更新模组替换失败会丢失旧模组 | 原逻辑先删光旧 Mod 再移新文件，移动失败就两头落空。改为先移入新文件（同名先备份为 `.pclbak`），成功后才删旧文件 | `PageInstanceMod.xaml.vb:595` |

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

## 六、关键陷阱备忘

| 陷阱 | 说明 |
|---|---|
| **双 BOM** | 用 `UTF8Encoding($true)` 写已含 BOM 的文件会产生双 BOM → `BC30037`。必须先去净 BOM 再写 |
| **`End Sub))`** | 修改 `Sub()` 匿名委托体时，替换范围极易连同 `End Sub))` 一起删掉 |
| **注释不能插在续行中间** | VB 的 `OrElse _` 续行里插注释会破坏续行 |
| **修改 `AllowAllThemes`** | 必须复制源码到临时目录构建，不可直接改主源码后忘记改回 |
