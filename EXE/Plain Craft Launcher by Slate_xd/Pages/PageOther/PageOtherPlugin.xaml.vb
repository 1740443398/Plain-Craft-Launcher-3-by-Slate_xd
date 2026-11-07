Imports System.Windows.Media.Imaging
Imports System.Windows.Media
Imports System.IO
Imports System.Collections.Generic
Imports System.Linq


''' <summary>
''' 插件管理页面：列出已安装插件、开关、创建模板。
''' <para>
''' 放在「更多」分类下，与百宝箱并列。
''' </para>
''' </summary>
Public Class PageOtherPlugin
    Inherits MyPageRight
    Implements IRefreshable

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
        '订阅左侧栏：不订阅的话点了没反应（事件发了没人听）
        If FrmPluginLeft IsNot Nothing Then
            RemoveHandler FrmPluginLeft.PageChanged, AddressOf OnSidebarChanged
            AddHandler FrmPluginLeft.PageChanged, AddressOf OnSidebarChanged
        End If
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        Try
            ModPlugin.Scan()
            RenderPluginList()
            '插件启用状态变了，标题栏入口也要跟着变
            Try
                FrmMain?.RebuildPluginTitleButtons()
            Catch ex2 As Exception
                Logger.Warn(ex2, "重建标题栏入口失败")
            End Try
        Catch ex As Exception
            Logger.Error(ex, "刷新插件列表失败")
            Hint($"刷新失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>把插件列表渲染到界面。</summary>
    Private Sub RenderPluginList()
        Try
            PanPluginList.Children.Clear()
            Dim All = ModPlugin.Plugins

            '概览
            Dim Usable = All.Where(Function(p) p.IsUsable).Count()
            Dim Enabled = All.Where(Function(p) p.IsUsable AndAlso p.Enabled).Count()
            LabPluginSummary.Text = If(All.Count = 0,
                "还没有安装任何插件。点击下方「新建插件模板」快速开始，或把插件文件夹放进 PCL\Plugins。",
                $"已发现 {All.Count} 个插件，可用 {Usable} 个，已启用 {Enabled} 个。")

            If All.Count = 0 Then
                PanPluginEmpty.Visibility = Visibility.Visible
                Return
            End If
            PanPluginEmpty.Visibility = Visibility.Collapsed

            For Each Entry In All
                PanPluginList.Children.Add(BuildPluginCard(Entry))
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染插件列表失败")
        End Try
    End Sub

    ''' <summary>为一个插件生成卡片。</summary>
    Private Function BuildPluginCard(Entry As ModPlugin.PluginEntry) As FrameworkElement
        Dim Card As New MyCard With {
            .Title = Entry.DisplayName,
            .Margin = New Thickness(0, 0, 0, 12)
        }
        Dim Panel As New StackPanel With {.Margin = New Thickness(25, 38, 25, 16)}

        '头像 + 名字的一行（头像是插件的门面，AstrBot 那种感觉）
        Dim HeadRow As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 0, 0, 10)}

        Dim Head As New Grid With {.Width = 48, .Height = 48, .Margin = New Thickness(0, 0, 14, 0)}
        '底色圆
        Dim Bg As New Border With {
            .CornerRadius = New CornerRadius(24),
            .Background = New SolidColorBrush(Color.FromRgb(&H4A, &H7C, &HD0))
        }
        Head.Children.Add(Bg)
        If Entry.HasIcon Then
            '自定义头像
            Try
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.UriSource = New Uri(Entry.IconPath, UriKind.Absolute)
                Bmp.EndInit()
                Dim Img As New Image With {.Source = Bmp, .Stretch = Stretch.UniformToFill}
                Dim Clip As New Border With {.CornerRadius = New CornerRadius(24), .Child = Img}
                Head.Children.Add(Clip)
            Catch ex As Exception
                Logger.Warn(ex, $"加载插件头像失败：{Entry.DisplayName}")
                Head.Children.Add(New TextBlock With {
                    .Text = Entry.IconInitial, .FontSize = 22, .FontWeight = FontWeights.Bold,
                    .Foreground = Brushes.White,
                    .HorizontalAlignment = HorizontalAlignment.Center,
                    .VerticalAlignment = VerticalAlignment.Center
                })
            End Try
        Else
            '自动字母头像
            Head.Children.Add(New TextBlock With {
                .Text = Entry.IconInitial, .FontSize = 22, .FontWeight = FontWeights.Bold,
                .Foreground = Brushes.White,
                .HorizontalAlignment = HorizontalAlignment.Center,
                .VerticalAlignment = VerticalAlignment.Center
            })
        End If
        HeadRow.Children.Add(Head)

        '名字 + 分类
        Dim NameStack As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        NameStack.Children.Add(New TextBlock With {
            .Text = Entry.DisplayName, .FontSize = 16, .FontWeight = FontWeights.SemiBold
        })
        If Entry.Manifest IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Entry.Manifest.Category) Then
            NameStack.Children.Add(New TextBlock With {
                .Text = Entry.Manifest.Category, .FontSize = 11.5, .Margin = New Thickness(0, 3, 0, 0),
                .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
            })
        End If
        HeadRow.Children.Add(NameStack)
        Panel.Children.Add(HeadRow)

        '描述
        Dim Desc As String = If(Entry.Manifest?.Description, "")
        If Not String.IsNullOrWhiteSpace(Desc) Then
            Panel.Children.Add(New TextBlock With {
                .Text = Desc, .TextWrapping = TextWrapping.Wrap,
                .Foreground = New SolidColorBrush(Color.FromRgb(&H88, &H88, &H88))
            })
        End If

        '元信息
        Dim Meta As String = If(Entry.Manifest IsNot Nothing,
            $"版本 {Entry.Manifest.Version}" &
            If(String.IsNullOrWhiteSpace(Entry.Manifest.Author), "", $" · 作者 {Entry.Manifest.Author}") &
            $" · ID {Entry.Manifest.Id}" & If(Entry.IsJava, " · Java 插件", ""), "清单读取失败")
        Panel.Children.Add(New TextBlock With {
            .Text = Meta, .Margin = New Thickness(0, 6, 0, 0), .FontSize = 12,
            .TextWrapping = TextWrapping.Wrap,
            .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
        })

        '权限提示：这是安全边界的可见部分
        Dim Perms = Entry.Manifest?.DeclaredPermissions
        If Perms IsNot Nothing AndAlso Perms.Count > 0 Then
            Panel.Children.Add(New TextBlock With {
                .Text = "声明权限：" & String.Join("、", Perms),
                .Margin = New Thickness(0, 6, 0, 0), .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                .Foreground = New SolidColorBrush(Color.FromRgb(&HC0, &H88, &H20))
            })
        End If

        '失败原因
        If Not String.IsNullOrWhiteSpace(Entry.FailureReason) Then
            Panel.Children.Add(New TextBlock With {
                .Text = "无法加载：" & Entry.FailureReason,
                .Margin = New Thickness(0, 6, 0, 0), .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                .Foreground = New SolidColorBrush(Color.FromRgb(&HD0, &H50, &H50))
            })
        End If

            'Java 插件缺 jar：这不是失败，是还没放文件
            If Entry.IsJava AndAlso Not Entry.JavaReady Then
                Panel.Children.Add(New TextBlock With {
                    .Text = "缺少 plugin.jar，把这个插件编译好的 jar 放到插件目录里即可。",
                    .Margin = New Thickness(0, 6, 0, 0), .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                    .Foreground = New SolidColorBrush(Color.FromRgb(&HC0, &H88, &H20))
                })
            End If

        '操作区
        Dim Row As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .Margin = New Thickness(0, 12, 0, 0)
        }
        Dim BtnToggle As New MyButton With {
            .Text = If(Entry.Enabled, "禁用", "启用"),
            .Height = 32, .MinWidth = 90,
            .IsEnabled = Entry.IsUsable
        }
        AddHandler BtnToggle.Click, Sub()
                                        ModPlugin.SetEnabled(Entry, Not Entry.Enabled)
                                        Refresh()
                                    End Sub
        Row.Children.Add(BtnToggle)

        'Java 插件：可以启动/停止它自己的进程
        If Entry.IsJava AndAlso Entry.IsUsable AndAlso Entry.Enabled AndAlso Entry.JavaReady Then
            Dim IsAlive As Boolean = ModPluginJava.IsRunning(Entry.Manifest.Id)
            Dim BtnRun As New MyButton With {
                .Text = If(IsAlive, "停止", "运行"), .Height = 32, .MinWidth = 80,
                .Margin = New Thickness(10, 0, 0, 0)
            }
            AddHandler BtnRun.Click, Sub()
                                            Try
                                                If ModPluginJava.IsRunning(Entry.Manifest.Id) Then
                                                    ModPluginJava.Stop(Entry.Manifest.Id)
                                                    Hint($"已停止 {Entry.DisplayName}", HintType.Green)
                                                Else
                                                    Dim Inst = ModPluginJava.Start(Entry)
                                                    If Inst IsNot Nothing Then
                                                        Hint($"{Entry.DisplayName} 已启动", HintType.Green)
                                                    End If
                                                End If
                                            Catch ex As Exception
                                                Logger.Error(ex, "启停 Java 插件失败")
                                            End Try
                                            Refresh()
                                        End Sub
            Row.Children.Add(BtnRun)
        
            '运行中显示状态
            If IsAlive Then
                Dim Inst = ModPluginJava.Running(Entry.Manifest.Id)
                Panel.Children.Add(New TextBlock With {
                    .Text = $"运行中 · 已 {CInt(Inst.Uptime.TotalSeconds)} 秒 · 占用 {Math.Round(Inst.MemoryMB, 1)} MB",
                    .Margin = New Thickness(0, 8, 0, 0), .FontSize = 12,
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H2E, &H8B, &H74))
                })
            End If
        End If

        '有自定义配置页才显示「设置」
        If Entry.HasConfigPage Then
            Dim BtnConfig As New MyButton With {
                .Text = "设置", .Height = 32, .MinWidth = 80, .Margin = New Thickness(10, 0, 0, 0)
            }
            AddHandler BtnConfig.Click, Sub() ShowPluginConfig(Entry)
            Row.Children.Add(BtnConfig)
        End If

        '有主页才显示「主页」
        If Entry.Manifest IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Entry.Manifest.Homepage) Then
            Dim BtnHome As New MyButton With {
                .Text = "主页", .Height = 32, .MinWidth = 80, .Margin = New Thickness(10, 0, 0, 0)
            }
            AddHandler BtnHome.Click, Sub()
                Try
                    Process.Start(Entry.Manifest.Homepage)
                Catch ex As Exception
                    Logger.Warn(ex, "打开插件主页失败")
                End Try
            End Sub
            Row.Children.Add(BtnHome)
        End If

        Dim BtnFolder As New MyButton With {
            .Text = "打开文件夹", .Height = 32, .MinWidth = 110, .Margin = New Thickness(10, 0, 0, 0)
        }
        AddHandler BtnFolder.Click, Sub()
                                        Try
                                            If DirectoryUtils.Exists(Entry.Directory) Then
                                                Process.Start("explorer.exe", $"""{Entry.Directory}""")
                                            End If
                                        Catch ex As Exception
                                            Logger.Warn(ex, "打开插件目录失败")
                                        End Try
                                    End Sub
        Row.Children.Add(BtnFolder)
        Panel.Children.Add(Row)

        Card.Children.Add(Panel)
        Return Card
    End Function

    ''' <summary>新建插件模板。</summary>
    Private Sub BtnPluginNew_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCreatePlugin.MouseLeftButtonUp
        Try
            Dim Name As String = MyMsgBoxInput("新建插件模板", "输入插件名称，会据此生成文件夹与清单。",
                                               HintText:="例如：我的第一个插件")
            If String.IsNullOrWhiteSpace(Name) Then Return
            If ModPlugin.CreateTemplate(Name.Trim()) Then
                Refresh()
            End If
        Catch ex As Exception
            Logger.Error(ex, "新建插件模板失败")
        End Try
    End Sub

    ''' <summary>打开插件根目录。</summary>
    Private Sub BtnPluginFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCreateFolder.MouseLeftButtonUp
        Try
            DirectoryUtils.Create(ModPlugin.PluginDir)
            Process.Start("explorer.exe", $"""{ModPlugin.PluginDir}""")
        Catch ex As Exception
            Logger.Warn(ex, "打开插件目录失败")
        End Try
    End Sub

    ''' <summary>重新扫描。</summary>
    Private Sub BtnPluginRescan_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPluginRescan.MouseLeftButtonUp
        Refresh()
        Hint("已重新扫描插件目录", HintType.Green)
    End Sub


    ''' <summary>
    ''' 打开插件的自定义配置页。
    ''' <para>
    ''' 配置页是一段 XAML，用与主页相同的解析方式实例化。
    ''' 它能用的控件与主页一致，因此能力边界也一致：只能描述界面，不能执行任意代码。
    ''' </para>
    ''' </summary>
    ''' <summary>
    ''' 打开插件的自定义配置页。
    ''' <para>
    ''' 配置页是一段 XAML，用与主页相同的解析方式实例化。
    ''' 它能用的控件与主页一致，因此能力边界也一致：只能描述界面，不能执行任意代码。
    ''' </para>
    ''' </summary>
    Private Sub ShowPluginConfig(Entry As ModPlugin.PluginEntry)
        Try
            If Entry Is Nothing OrElse Not Entry.HasConfigPage Then Return
            Dim Raw As String = FileUtils.TryReadAsString(Entry.ConfigPath)
            If String.IsNullOrWhiteSpace(Raw) Then
                Hint("插件的配置页是空的", HintType.Red)
                Return
            End If

            '去掉命名空间声明（与主页处理一致，避免插件自带 xmlns 冲突）
            Do While Raw.Contains("xmlns")
                Raw = Raw.RegexReplace("xmlns[^" + ChrW(34) + ChrW(39) + "]*(" + ChrW(34) + "|" + ChrW(39) + ")[^" + ChrW(34) + ChrW(39) + "]*(" + ChrW(34) + "|" + ChrW(39) + ")", "").Replace("xmlns", "")
            Loop
            Dim Wrapped As String =
                "<StackPanel xmlns=" + ChrW(34) + "http://schemas.microsoft.com/winfx/2006/xaml/presentation" + ChrW(34) + " " &
                "xmlns:sys=" + ChrW(34) + "clr-namespace:System;assembly=mscorlib" + ChrW(34) + " " &
                "xmlns:x=" + ChrW(34) + "http://schemas.microsoft.com/winfx/2006/xaml" + ChrW(34) + " " &
                "xmlns:local=" + ChrW(34) + "clr-namespace:PCL;assembly=Plain Craft Launcher by Slate_xd" + ChrW(34) + ">" &
                Raw & "</StackPanel>"

            Dim Ui As Object = GetObjectFromXML(Wrapped)
            If TypeOf Ui IsNot FrameworkElement Then
                Hint("配置页内容无法解析为界面元素", HintType.Red)
                Return
            End If

            '放进独立窗口：配置页可能很高，弹窗装不下
            Dim ConfigWin As New Window With {
                .Title = $"{Entry.DisplayName} 的设置",
                .Width = 680,
                .Height = 540,
                .WindowStartupLocation = WindowStartupLocation.CenterOwner,
                .Owner = FrmMain
            }
            Try
                ConfigWin.Background = New SolidColorBrush(Color.FromRgb(&HF7, &HF7, &HF7))
            Catch
            End Try
            Dim Scroll As New ScrollViewer With {
                .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                .Padding = New Thickness(20)
            }
            Scroll.Content = CType(Ui, FrameworkElement)
            ConfigWin.Content = Scroll
            ConfigWin.ShowDialog()
        Catch ex As Exception
            Logger.Warn(ex, $"加载插件配置页失败：{Entry?.DisplayName}")
            Hint($"配置页加载失败：{ex.Message}", HintType.Red)
        End Try
    End Sub


    ''' <summary>
    ''' 切换到指定子页面（0=已安装 1=创建插件 2=开发文档）。
    ''' </summary>
    Public Sub ShowSection(ID As Integer)
        Try
            PanSecInstalled.Visibility = If(ID = 0, Visibility.Visible, Visibility.Collapsed)
            PanSecCreate.Visibility = If(ID = 1, Visibility.Visible, Visibility.Collapsed)
            PanSecDoc.Visibility = If(ID = 2, Visibility.Visible, Visibility.Collapsed)
            PanSecStore.Visibility = If(ID = 3, Visibility.Visible, Visibility.Collapsed)
            If ID = 0 Then Refresh()
            If ID = 2 Then RenderDoc()
            If ID = 3 Then LoadStore()
        Catch ex As Exception
            Logger.Error(ex, $"切换插件子页失败：{ID}")
        End Try
    End Sub


    ''' <summary>
    ''' 渲染内置开发文档。内容与 docs/PLUGIN.md 保持一致。
    ''' </summary>
    Private Sub RenderDoc()
        Try
            If PanDocContent Is Nothing Then Return
            If PanDocContent.Children.Count > 0 Then Return

            '清单字段
        '===== 完整 API 参考 =====

        Dim ManifestRows As String(,) = {
            {"id", "String", "必填", "唯一标识，反域名格式，如 com.example.hello。重复会互相覆盖"},
            {"name", "String", "必填", "显示名称，也是默认的标题栏文字"},
            {"version", "String", "必填", "版本号，如 1.0.0"},
            {"author", "String", "", "作者名"},
            {"description", "String", "", "一句话说明，显示在插件卡片上"},
            {"entry", "String", "main.xaml", "入口 XAML 文件名"},
            {"icon", "String", "", "头像文件名。留空自动生成首字母头像"},
            {"configPage", "String", "", "配置页 XAML。留空则不显示「设置」按钮"},
            {"category", "String", "", "分类，用于列表分组"},
            {"homepage", "String", "", "主页地址，显示「主页」按钮"},
            {"titlebar", "Boolean", "false", "为 true 时在标题栏加入口"},
            {"titlebarText", "String", "", "标题栏按钮文字，留空用 name"},
            {"titlebarIcon", "String", "", "SVG path 数据，视口 24x24"},
            {"needNetwork", "Boolean", "false", "声明需要网络，影响审计提示"},
            {"needPlayerData", "Boolean", "false", "声明要读玩家数据，供用户判断"},
            {"needExecute", "Boolean", "false", "已废弃：启动器不提供执行外部程序的能力"},
            {"minVersionCode", "Integer", "0", "最低启动器版本码，低于此值拒绝加载"}
        }
        AddDocTable(PanDocContent, "一、plugin.json 全部字段", ManifestRows, "字段", "类型", "默认", "说明")

        Dim ControlRows As String(,) = {
            {"local:MyCard", "Title", "-", "卡片容器，最外层通常用它"},
            {"local:MyButton", "Text", "-", "按钮，事件用 AddHandler 或 Handles"},
            {"local:MyTextBox", "Text, HintText", "-", "输入框"},
            {"local:MyComboBox", "Items, SelectedIndex", "-", "下拉框，配合 MyComboBoxItem"},
            {"local:MyCheckBox", "Text, Checked", "-", "复选框"},
            {"local:MyListItem", "Title, Info", "RadioBox", "列表项，Type 可设 RadioBox"},
            {"local:MyIconButton", "Logo, ToolTip", "-", "图标按钮，Logo 是 SVG path"},
            {"local:MyHint", "Text, Theme", "Blue", "提示条，Theme 可为 Blue/Green/Yellow/Red"},
            {"local:MyImage", "Source", "-", "图片，可读插件目录内的文件"},
            {"local:MySlider", "Value, Min, Max", "-", "滑块"},
            {"local:MyLoading", "-", "-", "加载动画"},
            {"TextBlock", "Text, FontSize", "-", "文本，必须设 TextWrapping=Wrap"},
            {"Grid / StackPanel", "-", "-", "布局容器，不要写死 Height"}
        }
        AddDocTable(PanDocContent, "二、可用的界面控件", ControlRows, "字段", "类型", "默认", "说明")

        Dim ThemeRows As String(,) = {
            {"ColorBrush1", "Brush", "-", "正文文字"},
            {"ColorBrush2", "Brush", "-", "次要文字"},
            {"ColorBrush3", "Brush", "-", "强调与标题"},
            {"ColorBrushGray1", "Brush", "-", "浅色背景块"},
            {"ColorBrushGray2", "Brush", "-", "分隔线与占位"},
            {"ColorBrushGray3", "Brush", "-", "小字说明"},
            {"ColorBrushBackground", "Brush", "-", "页面背景"},
            {"ColorBrush8", "Brush", "-", "标题栏文字"}
        }
        AddDocTable(PanDocContent, "三、主题资源（颜色）", ThemeRows, "字段", "类型", "默认", "说明")

        Dim ThemeNote As New MyCard With {.Title = "为什么必须用主题变量", .Margin = New Thickness(0, 0, 0, 12)}
        Dim ThemeNoteS As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
        ThemeNoteS.Children.Add(New TextBlock With {
            .Text = "写死色值（如 #FFFFFF）在深色主题下会变成刺眼白块。" & vbCrLf &
                    "正确写法：Foreground=""{DynamicResource ColorBrush1}""",
            .TextWrapping = TextWrapping.Wrap, .FontSize = 12.5
        })
        ThemeNote.Children.Add(ThemeNoteS)
        PanDocContent.Children.Add(ThemeNote)

        Dim ApiRows As String(,) = {
            {"PCL.ReadFile(path)", "String", "-", "读插件目录内文本文件，越界返回 null"},
            {"PCL.WriteFile(path, text)", "Boolean", "-", "写文件，自动建目录，失败返回 false"},
            {"PCL.FileExists(path)", "Boolean", "-", "判断插件目录内文件是否存在"},
            {"PCL.Toast(text)", "void", "-", "弹一条提示"},
            {"PCL.HttpGet(url)", "String", "-", "GET 请求，仅限 http/https"},
            {"PCL.GetSetting(key)", "String", "-", "读白名单设置：UiLauncherTheme / UiLauncherHue"},
            {"PCL.Home", "String", "-", "插件目录绝对路径（只读）"},
            {"PCL.Id", "String", "-", "插件 id（只读）"},
            {"PCL.LauncherVersion", "String", "-", "启动器版本号（只读）"}
        }
        AddDocTable(PanDocContent, "四、main.js 脚本 API", ApiRows, "字段", "类型", "默认", "说明")

        Dim PermRows As String(,) = {
            {"needNetwork", "false", "-", "为 true 才能用 PCL.HttpGet"},
            {"needPlayerData", "false", "-", "仅作声明，让用户看到你要读玩家数据"},
            {"needExecute", "false", "-", "已废弃，填了也没有作用"},
            {"（都不声明）", "-", "-", "只能读写自己目录、弹提示，不能联网"}
        }
        AddDocTable(PanDocContent, "五、权限声明与实际影响", PermRows, "字段", "类型", "默认", "说明")

        Dim AuditRows As String(,) = {
            {"进程执行", "Process.Start / Shell", "cmd.exe", "危险 · 拒绝加载"},
            {"注册表写入", "Registry / HKEY_", "-", "危险 · 拒绝加载"},
            {"本地路径读取", "file:// / C:\", "\\?\", "危险 · 拒绝加载"},
            {"动态执行", "eval() / new Function()", "ActiveXObject", "危险 · 拒绝加载"},
            {"路径穿越", "../ 或 ..\", "-", "危险 · 拒绝加载"},
            {"仅网络访问", "http:// 或 https://", "-", "需注意 · 允许"},
            {"剪贴板", "Clipboard / GetText", "SetText", "需注意 · 允许"},
            {"输入模拟", "SendKeys / keybd_event", "-", "需注意 · 允许"},
            {"needExecute", "清单里声明该项", "-", "需注意 · 提示无效"}
        }
        AddDocTable(PanDocContent, "六、安全审计规则", AuditRows, "字段", "类型", "默认", "说明")

        Dim LifeRows As String(,) = {
            {"加载", "启动器启动或点「重新扫描」时扫描 PCL\Plugins"},
            {"解析", "读 plugin.json，缺必填字段则标记不可用"},
            {"审计", "静态扫描危险模式，命中即拒绝"},
            {"渲染", "把 main.xaml 解析成 WPF 控件树"},
            {"标题栏", "titlebar=true 时加入标题栏入口"},
            {"禁用", "保留在磁盘，但不加载、不显示入口"}
        }
        AddDocTable(PanDocContent, "七、插件生命周期", LifeRows, "阶段", "发生什么")


            Dim Card4 As New MyCard With {.Title = "最小可用示例（可直接复制）", .Margin = New Thickness(0, 0, 0, 12)}
            Dim S4 As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            Dim Sample = "plugin.json：" & vbCrLf &
                         "{""id"":""com.example.hello"",""name"":""示例"",""version"":""1.0.0""}" & vbCrLf & vbCrLf &
                         "main.xaml：" & vbCrLf &
                         "<local:MyCard Title=""示例"">" & vbCrLf &
                         "  <TextBlock Margin=""25,38,25,15"" Text=""你好"" />" & vbCrLf &
                         "</local:MyCard>"
            S4.Children.Add(New TextBlock With {
                .Text = Sample, .TextWrapping = TextWrapping.Wrap,
                .FontFamily = New FontFamily("Consolas"), .FontSize = 12
            })
            Card4.Children.Add(S4)
            PanDocContent.Children.Add(Card4)
            
            '诊断与常见问题：新手最容易卡住的地方
            Dim Card7 As New MyCard With {.Title = "八、不想手写？直接生成一个", .Margin = New Thickness(0, 0, 0, 12)}
            Dim S7 As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            S7.Children.Add(New TextBlock With {
                .Text = "点下面按钮会在插件目录生成一份能直接运行的骨架（含清单、界面、头像、配置页）。",
                .TextWrapping = TextWrapping.Wrap
            })
            Dim BtnDemo As New MyButton With {
                .Text = "生成示例插件", .Height = 32, .MinWidth = 140,
                .Margin = New Thickness(0, 12, 0, 0), .HorizontalAlignment = HorizontalAlignment.Left
            }
            AddHandler BtnDemo.Click, Sub() CreateDemoPlugin()
            S7.Children.Add(BtnDemo)
            Card7.Children.Add(S7)
            PanDocContent.Children.Add(Card7)
        Catch ex As Exception
            Logger.Error(ex, "渲染插件文档失败")
        End Try

    End Sub


    ''' <summary>
    ''' 生成一份完整可运行的示例插件（带头像与配置页），
    ''' 让新手先跑通再改，比对着文档空想容易得多。
    ''' </summary>
    Private Sub CreateDemoPlugin()
        Try
            Dim Root = ModPlugin.PluginDir
            Dim Dir = System.IO.Path.Combine(Root, "example-plugin")
            If DirectoryUtils.Exists(Dir) Then
                If MyMsgBox("示例插件目录已存在，要覆盖吗？", "生成示例", "覆盖", "取消", IsWarn:=True) <> 1 Then Return
                Try
                    Directory.Delete(Dir, True)
                Catch ex As Exception
                    Hint($"删除旧目录失败：{ex.Message}", HintType.Red)
                    Return
                End Try
            End If
            DirectoryUtils.Create(Dir)

            Dim Manifest =
                "{" & vbCrLf &
                "  ""id"": ""com.example.demo""," & vbCrLf &
                "  ""name"": ""示例插件""," & vbCrLf &
                "  ""version"": ""1.0.0""," & vbCrLf &
                "  ""author"": ""你"" ," & vbCrLf &
                "  ""description"": ""这是一个可直接运行的示例，改 main.xaml 就能看到效果""," & vbCrLf &
                "  ""entry"": ""main.xaml""," & vbCrLf &
                "  ""icon"": ""icon.png""," & vbCrLf &
                "  ""configPage"": ""config.xaml""," & vbCrLf &
                "  ""category"": ""示例""," & vbCrLf &
                "  ""minVersionCode"": 100" & vbCrLf &
                "}"
            FileUtils.Write(System.IO.Path.Combine(Dir, "plugin.json"), Manifest, Text.Encoding.UTF8)

            Dim MainXaml =
                "<local:MyCard Title=""示例插件"" Margin=""0,0,0,15"">" & vbCrLf &
                "    <StackPanel Margin=""25,38,25,15"">" & vbCrLf &
                "        <TextBlock Text=""插件加载成功！"" FontSize=""15"" FontWeight=""SemiBold"" />" & vbCrLf &
                "        <TextBlock Margin=""0,8,0,0"" TextWrapping=""Wrap""" & vbCrLf &
                "                   Foreground=""{DynamicResource ColorBrushGray3}""" & vbCrLf &
                "                   Text=""改这个文件（main.xaml）再点「重新扫描」就能看到效果。"" />" & vbCrLf &
                "    </StackPanel>" & vbCrLf &
                "</local:MyCard>"
            FileUtils.Write(System.IO.Path.Combine(Dir, "main.xaml"), MainXaml, Text.Encoding.UTF8)

            Dim CfgXaml =
                "<local:MyCard Title=""示例设置页"">" & vbCrLf &
                "    <StackPanel Margin=""20,32,20,14"">" & vbCrLf &
                "        <TextBlock Text=""这是插件自己的配置界面。"" TextWrapping=""Wrap"" />" & vbCrLf &
                "        <local:MyCheckBox Text=""启用某个功能"" Margin=""0,10,0,0"" />" & vbCrLf &
                "    </StackPanel>" & vbCrLf &
                "</local:MyCard>"
            FileUtils.Write(System.IO.Path.Combine(Dir, "config.xaml"), CfgXaml, Text.Encoding.UTF8)

            '头像：生成一个简单的纯色 PNG
            Try
                WriteDemoIcon(System.IO.Path.Combine(Dir, "icon.png"))
            Catch ex As Exception
                Logger.Warn(ex, "生成示例头像失败，将使用字母头像")
            End Try

            ModPlugin.Scan()
            Refresh()
            If FrmPluginLeft IsNot Nothing Then FrmPluginLeft.SetPage(0)
            ShowSection(0)
            Hint("示例插件已生成，可以在「已安装」里看到它", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "生成示例插件失败")
            Hint($"生成失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>画一个 64x64 的圆形头像，避免示例依赖外部图片。</summary>
    Private Sub WriteDemoIcon(Path As String)
        Dim Size As Integer = 64
        Dim Bmp As New Imaging.WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, Nothing)
        Dim Pixels(Size * Size * 4 - 1) As Byte
        Dim Cx As Double = Size / 2.0, Cy As Double = Size / 2.0, R As Double = Size / 2.0 - 1
        For Y = 0 To Size - 1
            For X = 0 To Size - 1
                Dim Dx = X + 0.5 - Cx, Dy = Y + 0.5 - Cy
                Dim I = (Y * Size + X) * 4
                If Dx * Dx + Dy * Dy <= R * R Then
                    Pixels(I) = 176      ' B
                    Pixels(I + 1) = 108  ' G
                    Pixels(I + 2) = 74   ' R
                    Pixels(I + 3) = 255  ' A
                Else
                    Pixels(I + 3) = 0
                End If
            Next
        Next
        Bmp.WritePixels(New Int32Rect(0, 0, Size, Size), Pixels, Size * 4, 0)
        Dim Enc As New Imaging.PngBitmapEncoder()
        Enc.Frames.Add(Imaging.BitmapFrame.Create(Bmp))
        Using Fs = IO.File.Create(Path)
            Enc.Save(Fs)
        End Using
    End Sub


    '==== 插件广场 ====

    Private StoreLoaded As Boolean = False
    '正在加载中：防止重复点击触发多次请求
    Private StoreLoading As Boolean = False

    ''' <summary>从 GitHub 拉取插件广场列表。</summary>
    Private Sub LoadStore()
        If StoreLoaded OrElse StoreLoading Then Return
        StoreLoading = True
        LabStoreStatus.Text = "正在从 GitHub 获取插件列表……"
        Dim T2 As New Threading.Thread(
            Sub()
                Dim Ok As Boolean = ModPluginStore.Fetch()
                RunInUi(
                    Sub()
                        StoreLoading = False
                        If Not Ok Then
                            LabStoreStatus.Text = $"获取失败：{ModPluginStore.LastError}"
                            Return
                        End If
                        StoreLoading = False
                        StoreLoaded = True
                        LabStoreStatus.Text = $"共 {ModPluginStore.Entries.Count} 个插件。点「安装」会自动下载并审计。"
                        RenderStore()
                    End Sub)
            End Sub) With {.IsBackground = True}
        T2.Start()
    End Sub

    Private Sub RenderStore()
        Try
            PanStoreList.Children.Clear()
            If ModPluginStore.Entries.Count = 0 Then
                PanStoreList.Children.Add(New TextBlock With {
                    .Text = "广场里还没有插件。第一个发布的人可以直接在 GitHub 仓库上加 topic：" & ModPluginStore.Topic,
                    .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0),
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                })
                Return
            End If

            For Each E In ModPluginStore.Entries
                LoadStoreCard(E)
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染广场失败")
        End Try
    End Sub

    Private Sub LoadStoreCard(E As ModPluginStore.StoreEntry)
        Dim Card As New MyCard With {.Title = E.Name, .Margin = New Thickness(0, 0, 0, 10)}
        Dim P As New StackPanel With {.Margin = New Thickness(25, 36, 25, 14)}

        If Not String.IsNullOrWhiteSpace(E.Description) Then
            P.Children.Add(New TextBlock With {.Text = E.Description, .TextWrapping = TextWrapping.Wrap})
        End If
        P.Children.Add(New TextBlock With {
            .Text = $"作者 {E.Author} · ★ {E.Stars} · 更新 {E.UpdatedAt}",
            .FontSize = 12, .Margin = New Thickness(0, 5, 0, 0),
            .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
        })

        Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 12, 0, 0)}
        Dim BtnInstall As New MyButton With {.Text = "安装", .Height = 30, .MinWidth = 90}
        AddHandler BtnInstall.Click, Sub() InstallFromStore(E, BtnInstall)
        Row.Children.Add(BtnInstall)

        Dim BtnSite As New MyButton With {.Text = "GitHub", .Height = 30, .MinWidth = 90, .Margin = New Thickness(10, 0, 0, 0)}
        AddHandler BtnSite.Click, Sub()
                                      Try
                                          Process.Start(E.Homepage)
                                      Catch ex As Exception
                                          Logger.Warn(ex, "打开 GitHub 失败")
                                      End Try
                                  End Sub
        Row.Children.Add(BtnSite)

        P.Children.Add(Row)
        Card.Children.Add(P)
        PanStoreList.Children.Add(Card)
    End Sub

    ''' <summary>安装并审计。危险插件会被自动删除。</summary>
    Private Sub InstallFromStore(E As ModPluginStore.StoreEntry, Btn As MyButton)
        Try
            If MyMsgBox($"要安装插件「{E.Name}」吗？" & vbCrLf & vbCrLf &
                        "插件来自第三方作者，安装前会做一次内容审计，" & vbCrLf &
                        "但审计不能保证绝对安全。只装你信任的来源。",
                        "安装插件", "安装", "取消") <> 1 Then Return

            Btn.IsEnabled = False
            Btn.Text = "安装中…"
            Dim T2 As New Threading.Thread(
                Sub()
                    Dim Dir = ModPluginStore.Install(E)
                    RunInUi(
                        Sub()
                            Btn.IsEnabled = True
                            Btn.Text = If(Dir Is Nothing, "安装失败", "已安装")
                            Refresh()
                        End Sub)
                End Sub) With {.IsBackground = True}
            T2.Start()
        Catch ex As Exception
            Logger.Error(ex, "安装广场插件失败")
            Btn.IsEnabled = True
            Btn.Text = "安装"
        End Try
    End Sub

    Private Sub BtnStoreRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStoreRefresh.MouseLeftButtonUp
        StoreLoaded = False
        LoadStore()
    End Sub

    ''' <summary>告诉作者怎么发布插件。</summary>
    Private Sub BtnStoreUpload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStoreUpload.Click
        Try
            MyMsgBox(
                "发布插件只有三步，不需要联系我们审核：" & vbCrLf & vbCrLf &
                "1. 在 GitHub 建一个仓库，放上 plugin.json 和 main.xaml" & vbCrLf &
                "2. 给仓库加上 topic：" & ModPluginStore.Topic & vbCrLf &
                "3. 保持仓库公开，广场就能搜到你" & vbCrLf & vbCrLf &
                "用户装的时候程序会自动审计，含危险内容的会被拒绝下载。" & vbCrLf &
                "主题地址：https://github.com/topics/" & ModPluginStore.Topic,
                "怎么发布插件", "我知道了")
        Catch ex As Exception
            Logger.Warn(ex, "显示发布说明失败")
        End Try
    End Sub


    ''' <summary>左侧栏切换时同步右侧内容。</summary>
    Private Sub OnSidebarChanged(sender As Object, id As Integer)
        ShowSection(id)
    End Sub

    ''' <summary>
    ''' 在文档区追加一张表格卡片。第一行是表头，其余为数据行。
    ''' <para>
    ''' 统一用这个方法生成表格，避免每张卡片重复写一遍 Grid 布局代码，
    ''' 也保证列宽、行距、对齐在各章节之间一致。
    ''' </para>
    ''' </summary>
    ''' <param name="Host">放卡片的容器。</param>
    ''' <param name="Title">卡片标题。</param>
    ''' <param name="Rows">二维数组，Rows(0) 是表头。</param>
    ''' <param name="Headers">表头列名，数量要和 Rows 的列数一致。</param>
    Private Sub AddDocTable(Host As StackPanel, Title As String, Rows As String(,), ParamArray Headers As String())
        Try
            If Host Is Nothing OrElse Rows Is Nothing Then Return
            Dim RowCount = Rows.GetLength(0)
            Dim ColCount = Rows.GetLength(1)
            If RowCount = 0 Then Return

            Dim Card As New MyCard With {.Title = Title, .Margin = New Thickness(0, 0, 0, 12)}
            Dim G As New Grid With {.Margin = New Thickness(25, 36, 25, 16)}

            '第一列给固定宽度，其余均分
            For c = 0 To ColCount - 1
                Dim Cd As New ColumnDefinition
                If c = 0 Then
                    Cd.Width = New GridLength(If(ColCount >= 3, 130, 150))
                Else
                    Cd.Width = New GridLength(1, GridUnitType.Star)
                End If
                G.ColumnDefinitions.Add(Cd)
            Next

            '表头 + 数据行
            For r = 0 To RowCount - 1
                G.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
                For c = 0 To ColCount - 1
                    Dim IsHeader As Boolean = (r = 0)
                    Dim Tb As New TextBlock With {
                        .Text = If(Rows(r, c), ""),
                        .TextWrapping = TextWrapping.Wrap,
                        .VerticalAlignment = VerticalAlignment.Top,
                        .Margin = New Thickness(0, If(IsHeader, 4, 5), 14, If(IsHeader, 8, 5)),
                        .FontSize = If(IsHeader, 12.5, 12)
                    }
                    If IsHeader Then
                        Tb.FontWeight = FontWeights.SemiBold
                    ElseIf c = 0 Then
                        '第一列是字段名，用等宽字体更像文档
                        Tb.FontFamily = New FontFamily("Consolas")
                        Tb.Foreground = New SolidColorBrush(Color.FromRgb(&H6A, &H9E, &HD8))
                    Else
                        Tb.Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                    End If
                    If c = ColCount - 1 Then Tb.Margin = New Thickness(0, Tb.Margin.Top, 0, Tb.Margin.Bottom)
                    Grid.SetRow(Tb, r)
                    Grid.SetColumn(Tb, c)
                    G.Children.Add(Tb)
                Next
            Next

            Card.Children.Add(G)
            Host.Children.Add(Card)
        Catch ex As Exception
            Logger.Warn(ex, $"渲染文档表格失败：{Title}")
        End Try
    End Sub
End Class
