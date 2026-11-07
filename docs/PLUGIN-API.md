
---

# 权限系统与 Java API 完整参考

> 这一节是给 Java 插件作者看的完整手册。XAML 插件不需要权限系统。
>
> 如果你还没写过 Java 插件，先看上面的「Java 插件」章节跑通一个 Hello World，再回来查这里。

## 1. 权限模型

### 1.1 为什么要有权限

插件生态的自由度和安全性天然冲突：

- 完全放开 = 把用户机器交给陌生代码
- 完全锁死 = 没人愿意写插件

折中方案是**按项声明 + 用户显式同意**。插件在清单里写清楚要什么，用户第一次启用时看到完整清单，点同意才生效。

**默认拒绝**是这套设计的前提。如果默认允许，用户根本不会看那个弹窗，授权就形同虚设。

### 1.2 九项权限

| 权限 key | 名称 | 能做什么 | 风险 |
|---|---|---|---|
| `network` | 网络访问 | GET / POST 任意 http(s) 网址 | 中 |
| `fileRead` | 读取文件 | 读 PCL 目录下的文件（敏感文件除外） | 中 |
| `fileWrite` | 写入文件 | 写 PCL 目录下的文件 | **高** |
| `readSettings` | 读取设置 | 读主题、界面偏好 | 低 |
| `writeSettings` | 修改设置 | 改主题色、界面偏好 | 中 |
| `readInstances` | 读取实例 | 读游戏实例列表与版本号 | 低 |
| `notify` | 弹出通知 | 弹提示消息 | 低 |
| `clipboard` | 剪贴板 | 读写剪贴板 | 中 |
| `titlebar` | 标题栏入口 | 在标题栏加按钮 | 低 |

### 1.3 怎么申请

在 `plugin.json` 里写数组：

```json
{
  "id": "com.example.mytool",
  "name": "我的工具",
  "version": "1.0.0",
  "runtime": "java",
  "permissions": ["network", "readSettings", "notify"]
}
```

**只申请真正用到的**。用户看到一长串权限会直接卸载，而不是仔细读。

### 1.4 用户看到的界面

首次启用时，启动器会列出：

```
这个插件需要以下权限：

· 【需注意】网络访问
  向任意网址发送请求。插件可能把你的信息发到第三方服务器。
· 读取设置
  读取主题、界面偏好等设置项。
· 弹出通知
  在界面上弹出提示消息。

[同意并启用]  [取消]
```

点「取消」后插件不加载，但文件保留。用户可以随时到插件页重新授权。

### 1.5 运行时行为

**没申请的能力，调用会直接失败**，返回：

```json
{"type":"error","action":"http_get","message":"操作 http_get 不在允许清单内"}
```

**申请了但用户没同意**，返回：

```json
{"type":"error","action":"http_get","message":"缺少权限：网络访问。到插件页重新授权。"}
```

两种都要处理，否则插件会静默失败。

## 2. 通信协议

### 2.1 基本规则

- 启动器与插件通过 **stdin / stdout** 通信
- **一行一条 JSON**
- 插件写 **stderr** 的内容进日志，不干扰协议

### 2.2 启动器 → 插件

**握手**（插件启动后立刻收到）：

```json
{
  "type": "hello",
  "pluginId": "com.example.mytool",
  "launcherVersion": "1.0.0",
  "actions": ["toast","log","get_version","http_get","http_post"]
}
```

`actions` 是**该插件当前被允许调用的** action 列表，已经按权限过滤过。插件可以据此决定显示哪些功能。

**回复**（对插件请求的响应）：

```json
{"type":"reply","action":"http_get","ok":true,"value":"<响应内容>"}
```

失败时：

```json
{"type":"reply","action":"http_get","ok":false,"message":"请求超时"}
```

**关闭**（启动器要退出）：

```json
{"type":"shutdown"}
```

插件收到后应该尽快退出。启动器会等 3 秒，之后强杀。

### 2.3 插件 → 启动器

```json
{"action":"<动作名>", ...参数}
```

每个 action 的完整参数见下一节。

## 3. Action 完整参考

### 3.1 `log` — 写日志

**权限**：无

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `text` | String | 是 | 日志内容 |

```json
{"action":"log","text":"读取了 128 个区块"}
```

进 `PCL/Log1.txt`，带 `[插件 <id>]` 前缀。**调试时优先用这个**，不要写 stdout。

---

### 3.2 `get_version` — 取启动器版本

**权限**：无

```json
{"action":"get_version"}
```

回复：

```json
{"type":"reply","action":"get_version","ok":true,"value":"1.0.0"}
```

---

### 3.3 `toast` — 弹提示

**权限**：`notify`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `text` | String | 是 | 提示内容，超 200 字截断 |

```json
{"action":"toast","text":"处理完成"}
```

---

### 3.4 `notify` — 弹通知（带颜色）

**权限**：`notify`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `text` | String | 是 | 通知内容 |

与 `toast` 的区别：`notify` 显示为绿色成功样式，适合报告完成状态。

---

### 3.5 `get_setting` — 读设置

**权限**：`readSettings`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | String | 是 | 设置键名 |

**可读的键**：

| 键 | 类型 | 说明 |
|---|---|---|
| `UiLauncherTheme` | Integer | 主题编号 |
| `UiLauncherHue` | Integer | 主题色相 |
| `UiLauncherSat` | Integer | 饱和度 |
| `UiLauncherLight` | Integer | 亮度 |

```json
{"action":"get_setting","key":"UiLauncherHue"}
```

回复：

```json
{"type":"reply","action":"get_setting","ok":true,"value":"220"}
```

**不在表里的键会被拒绝**，即使有权限。这是为了防止插件读到启动参数、账号路径等敏感配置。

---

### 3.6 `set_setting` — 改设置

**权限**：`writeSettings`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | String | 是 | 同上，仅限那 4 个键 |
| `value` | String | 是 | 新值 |

```json
{"action":"set_setting","key":"UiLauncherHue","value":"280"}
```

---

### 3.7 `get_theme` — 读主题

**权限**：`readSettings`

一次拿到主题和色相：

```json
{"action":"get_theme"}
```

回复：

```json
{"type":"reply","action":"get_theme","ok":true,"theme":3,"hue":220}
```

---

### 3.8 `set_theme` — 改主题色相

**权限**：`writeSettings`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `hue` | Integer | 是 | 0-360 |

```json
{"action":"set_theme","hue":280}
```

改完界面会立即刷新。

---

### 3.9 `list_versions` — 列出游戏实例

**权限**：`readInstances`

```json
{"action":"list_versions"}
```

回复：

```json
{"type":"reply","action":"list_versions","ok":true,"value":["1.20.1","1.21.4","整合包A"]}
```

只返回名字，不含路径。路径可能暴露用户名。

---

### 3.10 `http_get` — GET 请求

**权限**：`network`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `url` | String | 是 | 必须是 `http://` 或 `https://` |

```json
{"action":"http_get","url":"https://api.example.com/status"}
```

成功回复：

```json
{"type":"reply","action":"http_get","ok":true,"value":"{\"status\":\"ok\"}","truncated":false}
```

**注意**：

- 超时 20 秒
- 响应超过 200000 字符会被截断，`truncated` 为 `true`
- 只支持 UTF-8 文本，二进制请用其他方式
- 自动解压 gzip / deflate

---

### 3.11 `http_post` — POST 请求

**权限**：`network`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `url` | String | 是 | 同上 |
| `body` | String | 否 | 请求体，按 `application/json` 发送 |

```json
{"action":"http_post","url":"https://api.example.com/upload","body":"{\"name\":\"test\"}"}
```

---

### 3.12 `read_file` — 读文件

**权限**：`fileRead`（读 PCL 目录时）

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `path` | String | 是 | 相对路径 |

**路径规则**：

| 写法 | 实际位置 | 需要权限 |
|---|---|---|
| `data.txt` | 插件自己目录内 | 不需要 |
| `PCL/config.txt` | PCL 目录 | `fileRead` |

```json
{"action":"read_file","path":"PCL/myplugin/data.json"}
```

回复：

```json
{"type":"reply","action":"read_file","ok":true,"value":"<文件内容>"}
```

**超过 200000 字符会被截断。**

**会被拒绝的路径**（即使有权限）：

| 类型 | 例子 |
|---|---|
| 路径穿越 | `../secret.txt`、`..\secret.txt` |
| 绝对路径 | `C:\Windows\win.ini` |
| UNC 路径 | `\\server\share\a.txt` |
| 盘符跳转 | `D:\other\file.txt` |

**会被拒绝的文件**（内容或文件名判定）：

| 类别 | 例子 |
|---|---|
| 账号文件 | `launcher_profiles.json`、`launcher_accounts.json` |
| 启动器密钥 | `api.json`、`login.ini`、`token.ini` |
| 日志 | `Log1.txt`、`Log2.txt` |
| 证书 | `*.pfx`、`*.p12`、`*.key`、`*.pem` |
| 含凭据的配置 | 内容含 `accessToken` / `api_key` / `password` / `secret` |

最后一条是**内容扫描**：即使文件名叫 `config.json`，只要里面有 `"accessToken"` 就拒绝。

---

### 3.13 `write_file` — 写文件

**权限**：`fileWrite`（写 PCL 目录时）

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `path` | String | 是 | 相对路径，规则同 `read_file` |
| `content` | String | 是 | 文件内容 |

```json
{"action":"write_file","path":"PCL/myplugin/cache.json","content":"{}"}
```

目录不存在会自动创建。敏感文件同样拒绝写入。

---

### 3.14 `list_dir` — 列目录

**权限**：`fileRead`（列 PCL 目录时）

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `path` | String | 是 | 相对路径 |

```json
{"action":"list_dir","path":"PCL"}
```

回复：

```json
{"type":"reply","action":"list_dir","ok":true,"value":["Plugins","Skin","Log1.txt"]}
```

只返回名字，不返回大小和修改时间。

---

### 3.15 `clipboard_get` — 读剪贴板

**权限**：`clipboard`

```json
{"action":"clipboard_get"}
```

回复：

```json
{"type":"reply","action":"clipboard_get","ok":true,"value":"<剪贴板文本>"}
```

超过 100000 字符截断。

---

### 3.16 `clipboard_set` — 写剪贴板

**权限**：`clipboard`

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `text` | String | 是 | 要写入的内容 |

```json
{"action":"clipboard_set","text":"1.20.1 服务器地址：mc.example.com"}
```

## 4. 完整可运行示例

一个会联网、会读写文件、会改主题的插件：

```java
import java.io.*;
import java.nio.charset.StandardCharsets;

public class MyTool {
    private static PrintStream out;

    public static void main(String[] args) throws Exception {
        BufferedReader in = new BufferedReader(
                new InputStreamReader(System.in, StandardCharsets.UTF_8));
        out = new PrintStream(System.out, true, "UTF-8");

        String line;
        while ((line = in.readLine()) != null) {
            if (line.trim().isEmpty()) continue;
            String type = str(line, "type");

            if ("hello".equals(type)) {
                log("插件已加载");
                // 演示：联网
                out.println("{\"action\":\"http_get\",\"url\":\"https://api.github.com/zen\"}");
            } else if ("reply".equals(type)) {
                String action = str(line, "action");
                String value = str(line, "value");
                boolean ok = line.contains("\"ok\":true");

                if ("http_get".equals(action)) {
                    if (ok) {
                        log("拿到网络响应：" + value);
                        // 演示：写文件
                        out.println("{\"action\":\"write_file\",\"path\":\"PCL/mytool/cache.txt\","
                                  + "\"content\":\"" + esc(value) + "\"}");
                    } else {
                        log("网络请求失败：" + str(line, "message"));
                    }
                } else if ("write_file".equals(action)) {
                    if (ok) {
                        out.println("{\"action\":\"notify\",\"text\":\"缓存已保存\"}");
                    }
                }
            } else if ("shutdown".equals(type)) {
                break;
            }
        }
    }

    private static void log(String text) {
        out.println("{\"action\":\"log\",\"text\":\"" + esc(text) + "\"}");
    }

    private static String str(String json, String key) {
        String needle = "\"" + key + "\"";
        int i = json.indexOf(needle);
        if (i < 0) return "";
        int c = json.indexOf(':', i + needle.length());
        if (c < 0) return "";
        int q1 = json.indexOf('"', c + 1);
        if (q1 < 0) return "";
        StringBuilder sb = new StringBuilder();
        for (int p = q1 + 1; p < json.length(); p++) {
            char ch = json.charAt(p);
            if (ch == '\\' && p + 1 < json.length()) {
                char nx = json.charAt(++p);
                if (nx == 'n') sb.append('\n');
                else if (nx == '"') sb.append('"');
                else if (nx == '\\') sb.append('\\');
                else sb.append(nx);
            } else if (ch == '"') break;
            else sb.append(ch);
        }
        return sb.toString();
    }

    private static String esc(String s) {
        return s.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n");
    }
}
```

对应的 `plugin.json`：

```json
{
  "id": "com.example.mytool",
  "name": "我的工具",
  "version": "1.0.0",
  "runtime": "java",
  "permissions": ["network", "fileWrite", "notify"]
}
```

## 5. 调试

### 5.1 看日志

插件写 stderr 的内容会进 `PCL/Log1.txt`：

```java
System.err.println("调试信息");
```

### 5.2 常见错误

| 现象 | 原因 |
|---|---|
| `操作 xxx 不在允许清单内` | action 名字拼错，或该 action 不存在 |
| `缺少权限：xxx` | 清单里没申请，或用户没同意 |
| 插件无响应 | 忘了 `out.flush()`，或 stdout 写了非 JSON |
| `UnsupportedClassVersionError` | 编译时没加 `--release 8` |
| 插件启动后立即退出 | 主循环读到 EOF，检查 stdin 是否被关闭 |

### 5.3 自测清单

发布前过一遍：

- [ ] 清单里 `permissions` 只列真正用到的
- [ ] 每个 action 都处理了 `ok:false` 的情况
- [ ] `shutdown` 能正常退出
- [ ] 用 `--release 8` 编译
- [ ] 调试输出走 stderr，不走 stdout
- [ ] 网络请求有超时处理

## 6. 权限设计建议

### 6.1 最小权限

**不要**为了「以后可能用到」就全申请。用户会警惕。

| 场景 | 需要的权限 |
|---|---|
| 只显示信息 | 无 |
| 显示网络数据 | `network` |
| 缓存数据到本地 | `fileWrite` |
| 改主题 | `writeSettings` |
| 复制内容到剪贴板 | `clipboard` |

### 6.2 申请多个权限时

把用途写在 `description` 里：

```json
{
  "description": "查看服务器状态。需要网络权限访问 mcstatus API，需要通知权限报告状态变化。"
}
```

用户看到具体用途，同意率会高很多。

### 6.3 危险权限的替代方案

| 想做的事 | 危险做法 | 更安全的做法 |
|---|---|---|
| 存配置 | `fileWrite` 写 PCL 目录 | 写插件自己目录（不需要权限） |
| 记住用户选择 | 写文件 | `set_setting` 用现有设置项 |
| 通知用户 | `notify` | `toast`（同一权限，但更轻） |

## 7. 安全边界（必读）

插件**永远做不到**：

| 想法 | 现实 |
|---|---|
| 执行系统命令 | 没有对应 action |
| 读注册表 | 没有对应 action |
| 读账号凭据 | 敏感文件被硬拦截 |
| 读游戏存档 | `.minecraft/saves` 不在允许范围 |
| 绕过审计 | 审计在加载前跑，危险内容直接拒绝安装 |
| 提权 | 插件进程以普通用户权限运行，没有 UAC |

**这不是限制得太死，是故意的。** 插件能做什么，取决于启动器提供哪些消息，而不是它能 import 什么。这样恶意插件的上限被锁死了。

如果你觉得某个能力应该有但没有，那是设计决策，可以在仓库提 Issue 讨论。
