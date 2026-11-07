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
            $" · ID {Entry.Manifest.Id}", "清单读取失败")
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
            Dim Card1 As New MyCard With {.Title = "一、plugin.json 清单", .Margin = New Thickness(0, 0, 0, 12)}
            Dim G1 As New Grid With {.Margin = New Thickness(25, 36, 25, 16)}
            Dim FNames = {"id", "name", "version", "author", "description", "entry", "icon", "configPage", "category", "homepage", "titlebar", "titlebarText", "titlebarIcon", "needNetwork", "needPlayerData", "needExecute", "minVersionCode"}
            Dim FReq = {"必填", "必填", "必填", "", "", "", "", "", "", "", "", "", "", "", "", "", ""}
            Dim FDesc = {
                "唯一标识，建议反域名格式，如 com.example.hello",
                "显示名称",
                "版本号，如 1.0.0",
                "作者",
                "一句话说明",
                "入口 XAML，默认 main.xaml",
                "头像文件名，留空用字母头像",
                "自定义配置页 XAML，留空不显示设置按钮",
                "分类，用于列表分组",
                "主页地址",
                "true 时在标题栏加一个入口（给启动器加新功能）",
                "标题栏按钮文字，留空用 name",
                "标题栏按钮图标（SVG 路径，视口 24x24）",
                "需要网络访问（默认 false）",
                "需要读玩家数据（默认 false）",
                "需要执行外部程序（注：该项无实际作用）",
                "最低启动器版本码"
            }
            For k = 0 To FNames.Length - 1
                G1.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            Next
            G1.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(140)})
            G1.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(55)})
            G1.ColumnDefinitions.Add(New ColumnDefinition)

            For k = 0 To FNames.Length - 1
                Dim K1 As New TextBlock With {
                    .Text = FNames(k), .FontFamily = New FontFamily("Consolas"),
                    .Margin = New Thickness(0, 5, 0, 5), .VerticalAlignment = VerticalAlignment.Top
                }
                Grid.SetRow(K1, k) : Grid.SetColumn(K1, 0) : G1.Children.Add(K1)

                Dim K2 As New TextBlock With {
                    .Text = FReq(k), .FontSize = 11.5,
                    .Foreground = New SolidColorBrush(Color.FromRgb(&HD0, &H80, &H20)),
                    .Margin = New Thickness(0, 5, 0, 5), .VerticalAlignment = VerticalAlignment.Top
                }
                Grid.SetRow(K2, k) : Grid.SetColumn(K2, 1) : G1.Children.Add(K2)

                Dim K3 As New TextBlock With {
                    .Text = FDesc(k), .TextWrapping = TextWrapping.Wrap, .FontSize = 12.5,
                    .Margin = New Thickness(0, 5, 0, 5), .VerticalAlignment = VerticalAlignment.Top
                }
                Grid.SetRow(K3, k) : Grid.SetColumn(K3, 2) : G1.Children.Add(K3)
            Next
            Card1.Children.Add(G1)
            PanDocContent.Children.Add(Card1)

            '可用控件
            '给启动器加新功能：这是插件最重要的能力
            Dim CardTb As New MyCard With {.Title = "一·补、给启动器加新入口", .Margin = New Thickness(0, 0, 0, 12)}
            Dim STb As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            STb.Children.Add(New TextBlock With {
                .Text = "这是插件最有用的能力：让你的插件像内置功能一样出现在标题栏。",
                .TextWrapping = TextWrapping.Wrap
            })
            STb.Children.Add(New TextBlock With {
                .Text = "在 plugin.json 里加三行：" & vbCrLf &
                        "  ""titlebar"": true" & vbCrLf &
                        "  ""titlebarText"": ""我的工具""" & vbCrLf &
                        "  ""titlebarIcon"": ""M448 64h128v320h320v128H576v320H448V512H128V384h320z""",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0),
                .FontFamily = New FontFamily("Consolas"), .FontSize = 12
            })
            STb.Children.Add(New TextBlock With {
                .Text = "改完保存，回插件页点「重新扫描」，标题栏就会出现「我的工具」，点进去显示你的 main.xaml。",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0), .FontSize = 12.5
            })
            STb.Children.Add(New TextBlock With {
                .Text = "titlebarIcon 用 SVG 路径（path 的 d 属性），视口 24x24。不写会用通用方块图标。",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 8, 0, 0), .FontSize = 12,
                .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
            })
            CardTb.Children.Add(STb)
            PanDocContent.Children.Add(CardTb)
            
            Dim Card2 As New MyCard With {.Title = "二、可用的界面控件", .Margin = New Thickness(0, 0, 0, 12)}
            Dim S2 As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            Dim Controls = {
                "local:MyCard —— 卡片容器，带标题",
                "local:MyButton —— 按钮",
                "local:MyTextBox —— 输入框",
                "local:MyComboBox —— 下拉框",
                "local:MyCheckBox —— 复选框",
                "local:MyListItem —— 列表项",
                "local:MyIconButton —— 图标按钮",
                "local:MyHint —— 提示条",
                "local:MyImage —— 图片"
            }
            For Each C In Controls
                S2.Children.Add(New TextBlock With {
                    .Text = "· " & C, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 3, 0, 3)
                })
            Next
            S2.Children.Add(New TextBlock With {
                .Text = "颜色必须用 {DynamicResource ColorBrush1} 这类主题变量，不要写死色值，否则切深色主题会变白板。",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0), .FontSize = 12,
                .Foreground = New SolidColorBrush(Color.FromRgb(&HC0, &H88, &H20))
            })
            Card2.Children.Add(S2)
            PanDocContent.Children.Add(Card2)

            '脚本能力
            Dim Card3 As New MyCard With {.Title = "三、main.js 脚本能力", .Margin = New Thickness(0, 0, 0, 12)}
            Dim S3 As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            Dim Apis = {
                "PCL.ReadFile(相对路径) —— 读插件目录内的文件",
                "PCL.WriteFile(相对路径, 内容) —— 写插件目录内的文件",
                "PCL.FileExists(相对路径) —— 判断文件是否存在",
                "PCL.Toast(文本) —— 弹一条提示",
                "PCL.HttpGet(网址) —— 发 GET 请求（需 needNetwork）",
                "PCL.GetSetting(键) —— 读主题等白名单设置",
                "PCL.LauncherVersion —— 启动器版本"
            }
            For Each A In Apis
                S3.Children.Add(New TextBlock With {
                    .Text = "· " & A, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 3, 0, 3),
                    .FontFamily = New FontFamily("Consolas"), .FontSize = 12.5
                })
            Next
            S3.Children.Add(New TextBlock With {
                .Text = "脚本运行在受限宿主里：没有 System.IO、没有 Process、没有反射。" & vbCrLf &
                        "所有文件路径都被限制在插件自己的目录内，写成 ../ 会被拒绝。" & vbCrLf &
                        "这是故意的：DLL 插件等于把整台机器的权限交出去。",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0), .FontSize = 12,
                .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
            })
            Card3.Children.Add(S3)
            PanDocContent.Children.Add(Card3)

            '最小示例
            Dim Card4 As New MyCard With {.Title = "四、最小可用示例", .Margin = New Thickness(0, 0, 0, 12)}
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
            Dim Card5 As New MyCard With {.Title = "五、插件不生效怎么查", .Margin = New Thickness(0, 0, 0, 12)}
            Dim S5 As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            Dim Checks = {
                "目录对不对：必须是 PCL\Plugins\你的文件夹\plugin.json，不要多套一层。",
                "必填字段：id / name / version 三个都不能少。",
                "入口文件名：entry 写的名字要和实际文件名完全一致（区分大小写）。",
                "XAML 语法：标签要闭合、属性值要加引号。",
                "看插件页：卡片下方的红字就是失败原因，不用猜。"
            }
            For Each Chk In Checks
                S5.Children.Add(New TextBlock With {
                    .Text = "· " & Chk, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 3, 0, 3)
                })
            Next
            S5.Children.Add(New TextBlock With {
                .Text = "日志在 PCL\Log1.txt，会写明哪个文件哪一行出错。",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0), .FontSize = 12,
                .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
            })
            Card5.Children.Add(S5)
            PanDocContent.Children.Add(Card5)
            
            Dim Card6 As New MyCard With {.Title = "六、常见问题", .Margin = New Thickness(0, 0, 0, 12)}
            Dim S6 As New StackPanel With {.Margin = New Thickness(25, 36, 25, 16)}
            Dim QAs = {
                "Q：插件能调用启动器内部函数吗？  不能。XAML 只能描述界面，不能执行任意代码。",
                "Q：能读玩家存档吗？  需要 needPlayerData，而且必须让用户知道。",
                "Q：插件之间会冲突吗？  不会。每个独立渲染，一个崩了不影响其它。id 重复会互相覆盖。",
                "Q：怎么更新插件？  直接改文件，点「重新扫描」，不用重启启动器。",
                "Q：能隐藏插件吗？  能。每张卡片都有「禁用」按钮，状态会保存。",
                "Q：怎么让别人也能装到我的插件？  给 GitHub 仓库加 pcl-plugin topic，广场就能搜到。"
            }
            For Each QA In QAs
                S6.Children.Add(New TextBlock With {
                    .Text = QA, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 4, 0, 4),
                    .FontSize = 12.5
                })
            Next
            Card6.Children.Add(S6)
            PanDocContent.Children.Add(Card6)
            
            '一键生成可运行示例
            Dim Card7 As New MyCard With {.Title = "七、不想手写？直接生成一个", .Margin = New Thickness(0, 0, 0, 12)}
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

    ''' <summary>从 GitHub 拉取插件广场列表。</summary>
    Private Sub LoadStore()
        If StoreLoaded Then Return
        LabStoreStatus.Text = "正在从 GitHub 获取插件列表……"
        Dim T2 As New Threading.Thread(
            Sub()
                Dim Ok As Boolean = ModPluginStore.Fetch()
                RunInUi(
                    Sub()
                        If Not Ok Then
                            LabStoreStatus.Text = $"获取失败：{ModPluginStore.LastError}"
                            Return
                        End If
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
End Class
