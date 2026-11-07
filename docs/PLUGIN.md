# 插件开发文档

Plain Craft Launcher by Slate_xd 的插件系统。写一份 `plugin.json` 加一个 `main.xaml`，放进插件目录就能用，**不需要重新编译启动器**。

---

## 一、为什么要读这份文档

插件让你在不修改启动器源码的前提下，往界面里加自己的内容：公告板、常用工具入口、服务器状态、整合包推荐……

能做到什么、不能做到什么，都由这份文档的规则决定。**先读完「安全边界」再看「快速开始」**，能省掉很多返工。

---

## 二、安全边界（先看这个）

### 插件是 XAML，不是 DLL

我们**故意不支持 DLL 插件**。

DLL 插件意味着反射加载任意代码，等于把整台机器的权限交给插件作者。一旦有人投毒（或者作者的电脑被入侵），受害者没有任何防线。

XAML 只能描述界面结构和声明式事件，能做的事情有明确边界。这和启动器已有的 `Custom.xaml` 主页机制一致，玩家也更容易判断一个插件「看起来安不安全」。

### 权限必须声明

`plugin.json` 里三项权限，默认全部 `false`：

| 字段 | 含义 |
|---|---|
| `needNetwork` | 需要访问网络 |
| `needPlayerData` | 需要读取玩家数据（存档、模组、日志） |
| `needExecute` | 需要执行外部程序 |

声明了不等于自动获得——插件页面会把这些权限**显示给用户看**，用户据此判断是否信任。这是「知情同意」，不是技术沙箱。

### 绝对不能做的事

无论声明什么权限，下面这些都不允许：

- 读取账号凭据、登录令牌、`Setup.ini` 里的敏感字段
- 未经用户操作就写入玩家存档或模组
- 把用户数据上传到作者自己的服务器而不告知

违反的插件会被移出推荐列表。

---

## 三、快速开始

### 1. 建目录

在 `PCL\Plugins\` 下建一个文件夹，名字随便（建议英文）。或者在「更多 → 插件」页面点 **新建插件模板**，会自动生成一份可运行的示例。

### 2. 写 plugin.json

```json
{
  "id": "com.example.hello",
  "name": "示例插件",
  "version": "1.0.0",
  "author": "你的名字",
  "description": "一句话说明这个插件做什么",
  "entry": "main.xaml",
  "needNetwork": false,
  "needPlayerData": false,
  "needExecute": false,
  "minVersionCode": 100
}
```

### 3. 写 main.xaml

```xml
<local:MyCard Title="示例插件" Margin="0,0,0,15">
    <StackPanel Margin="25,38,25,15">
        <TextBlock Text="Hello, 这是一个插件！" TextWrapping="Wrap" />
    </StackPanel>
</local:MyCard>
```

### 4. 重载

回到「更多 → 插件」页面，点 **重新扫描**。插件卡片出现即为成功。

---

## 四、清单字段

| 字段 | 必填 | 说明 |
|---|:---:|---|
| `id` | ✔ | 唯一标识，建议反域名格式。重复会让后者覆盖前者 |
| `name` | ✔ | 显示名称 |
| `version` | ✔ | 版本号，如 `1.0.0` |
| `author` | | 作者 |
| `description` | | 一句话说明 |
| `entry` | | 入口文件，默认 `main.xaml` |
| `needNetwork` | | 默认 `false` |
| `needPlayerData` | | 默认 `false` |
| `needExecute` | | 默认 `false` |
| `minVersionCode` | | 最低启动器版本码，低于此值不加载 |
| `icon` | | 头像文件名，放在插件目录里。留空用自动字母头像 |
| `configPage` | | 自定义配置页的 XAML 文件名。留空则不显示「设置」按钮 |
| `category` | | 分类，用于列表分组 |
| `homepage` | | 主页地址，列表里会显示「主页」按钮 |
| `titlebar` | | `true` 时在启动器标题栏加一个入口（给启动器加新功能） |
| `titlebarText` | | 标题栏按钮文字，留空用 `name` |
| `titlebarIcon` | | 标题栏按钮图标，SVG 路径数据，视口 24×24 |

**缺任何一个必填字段，插件不会被加载**，列表里会显示具体原因。

---

## 五、XAML 能写什么

插件入口是一个普通 WPF `UserControl` 内容片段，可以直接使用启动器已有的控件：

| 控件 | 用途 |
|---|---|
| `local:MyCard` | 卡片容器，带标题 |
| `local:MyButton` | 按钮 |
| `local:MyTextBox` | 输入框 |
| `local:MyComboBox` | 下拉框 |
| `local:MyCheckBox` | 复选框 |
| `local:MyListItem` | 列表项 |
| `local:MyIconButton` | 图标按钮 |
| `local:MyHint` | 提示条 |

### 主题变量

**必须**用 `DynamicResource` 引用颜色，不要写死色值——否则切换深色主题时你的插件会变成一块白板。

```xml
<TextBlock Foreground="{DynamicResource ColorBrush1}" Text="正文" />
<TextBlock Foreground="{DynamicResource ColorBrushGray3}" Text="次要说明" />
```

### 布局注意

- 外层用 `StackPanel` 或 `Grid`，**不要**写死 `Height`（不同 DPI 会错位）
- 文字一律 `TextWrapping="Wrap"`，否则窄窗口会溢出
- 卡片之间留 `Margin="0,0,0,15"`

---

## 六、头像与自定义配置页

### 头像

在清单里写 `"icon": "icon.png"`，把图片放进插件目录。

- 支持 png / jpg / bmp / ico，建议 64×64
- 不写就自动用插件名的首字生成一个字母头像
- 图片路径必须在插件目录内，写成 `../` 会被忽略

### 自定义配置页

在清单里写 `"configPage": "config.xaml"`，插件列表里就会出现「设置」按钮。

配置页的写法和 `main.xaml` 完全一样——它也是一段 XAML，用同一套解析器实例化。
因此**能力边界也完全一样**：只能描述界面，不能执行任意代码。

```xml
<local:MyCard Title="插件设置" Margin="0,0,0,12">
    <StackPanel Margin="20,32,20,14">
        <TextBlock Text="这是插件自己的配置界面。" TextWrapping="Wrap" />
    </StackPanel>
</local:MyCard>
```

点击「设置」会打开一个独立窗口（680×540，内容过高时可滚动）。
这样配置页可以做得比较长，不必挤在弹窗里。

### 配置怎么持久化

配置页本身不能直接写文件。要在配置页里做持久化，用 `main.js` 提供的
`PCL.WriteFile`，路径同样限制在插件目录内：

```javascript
// main.js
function saveConfig(data) {
    return PCL.WriteFile('config.json', JSON.stringify(data));
}
function loadConfig() {
    var raw = PCL.ReadFile('config.json');
    return raw ? JSON.parse(raw) : {};
}
```

**不要**把配置写到插件目录之外——路径越界会被拒绝，而且这是评审插件时的红线。

---

## 七、给启动器加新入口

这是插件最有用的能力：让你的插件像内置功能一样出现在标题栏。

### 三步做到

在 `plugin.json` 里加三行：

```json
{
  "titlebar": true,
  "titlebarText": "我的工具",
  "titlebarIcon": "M448 64h128v320h320v128H576v320H448V512H128V384h320z"
}
```

保存后回插件页点「重新扫描」，标题栏就会出现「我的工具」。
点进去显示的就是你的 `main.xaml`。

### 图标怎么写

`titlebarIcon` 用的是 SVG 的 path 数据（`<path d="...">` 里那一段），视口按 24×24 计算。

常见来源：

- 从 [Lucide](https://lucide.dev) / [Tabler](https://tabler-icons.io) 等图标库复制 path
- 自己用 Illustrator / Figma 画，导出 SVG 后取 `d` 属性
- 不写这个字段，会用内置的通用方块图标

### 什么时候该用

| 场景 | 适合吗 |
|---|---|
| 服务器状态面板 | ✔ 值得独立入口 |
| 快捷工具集合（转码、计算器） | ✔ |
| 单个卡片就能说清的内容 | ✘ 放主页预设更合适 |
| 只是为了「看起来插件多」 | ✘ 没人会点 |

### 限制

标题栏入口和内置页面**没有能力差异**——它同样只能描述界面，不能执行任意代码。
这是有意设计的：给插件一个显眼的位置，但不多给一毫米权限。

---

## 八、诊断

插件不生效时，按顺序检查：

1. **目录对不对**：`PCL\Plugins\你的文件夹\plugin.json`，不要多套一层
2. **必填字段齐不齐**：`id` / `name` / `version`
3. **入口文件名**：`entry` 写的名字要和实际文件名完全一致（区分大小写）
4. **XAML 语法**：标签闭合、属性引号。启动器日志里会有 `XamlParseException` 的详细信息
5. **看插件页**：卡片下方的红字就是失败原因，不用猜

日志位置：`PCL\Log1.txt`。

---

## 九、常见问题

**Q：插件能调用启动器的内部函数吗？**
不能。XAML 是声明式的，只能描述界面和引用已注册的事件类型。

**Q：能读玩家的存档吗？**
需要 `needPlayerData: true`，且必须在界面上让用户知道。偷偷读取会被视为恶意行为。

**Q：插件之间会冲突吗？**
不会。每个插件独立渲染，一个崩溃不影响其它。但 `id` 重复会互相覆盖。

**Q：怎么更新插件？**
直接改文件，然后点「重新扫描」。不需要重启启动器。

**Q：插件能隐藏吗？**
能。插件页每张卡片都有「禁用」按钮，状态会保存，重启后仍然生效。

---

## 十、设计原则

写插件时问自己三个问题：

1. **它解决的问题真实吗？** 只是为了「看起来酷」的插件，用户装一次就卸。
2. **它占了多少屏？** 插件是附加内容，不应该把主界面顶走。
3. **关掉它，启动器还好用吗？** 如果插件成了必需品，说明它应该是内置功能，而不是插件。

---

*文档版本 1.0 · 对应启动器版本码 100*
