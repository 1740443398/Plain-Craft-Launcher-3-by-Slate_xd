Imports System.IO
Imports System.Diagnostics
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading

''' <summary>
''' 一键开服器页面：按用途选核心 → 选版本 → 下载 → 启动。
''' </summary>
Public Class PageServerMain
    Inherits MyPageRight
    Implements IRefreshable

    ''' <summary>内置预设模板。</summary>
    Private Class ServerTemplate
        Public Property Name As String = ""
        Public Property Description As String = ""
        Public Property MemoryMB As Integer = 2048
        Public Property OnlineMode As Boolean = True
    End Class

    Private ReadOnly Templates As New List(Of ServerTemplate) From {
        New ServerTemplate With {.Name = "和朋友玩", .Description = "4 GB 内存，开启正版验证，适合熟人小服。", .MemoryMB = 4096, .OnlineMode = True},
        New ServerTemplate With {.Name = "轻量测试", .Description = "1 GB 内存，关闭正版验证，适合快速验证模组或命令。", .MemoryMB = 1024, .OnlineMode = False},
        New ServerTemplate With {.Name = "大型整合包服", .Description = "8 GB 内存，开启正版验证，给重模组整合包用。", .MemoryMB = 8192, .OnlineMode = True},
        New ServerTemplate With {.Name = "离线局域网", .Description = "2 GB 内存，关闭正版验证，只在家里/宿舍局域网用。", .MemoryMB = 2048, .OnlineMode = False}
    }

    '当前选中的核心与类别
    Private SelectedCore As ModServerCore.CoreOption = Nothing
    Private SelectedKind As ModServerCore.CoreKind = ModServerCore.CoreKind.Plugin
    Private CoresLoaded As Boolean = False

    '当前下载目标目录名
    Private DownloadTarget As String = ""
    
    '设置下拉框选中项时会触发 SelectionChanged，导致同一个构建列表被请求两次
    Private SuppressBuildReload As Boolean = False
    '每次请求构建时递增。回调回来时比对，不一致说明用户已经换过选择，丢弃旧结果
    Private BuildRequestGeneration As Integer = 0

    Private Sub PageLoaded() Handles Me.Loaded
        '把服务端输出接到控制台
        ModServer.OutputHandler = Sub(Line As String) RunInUi(Sub() AppendConsole(Line))
        Refresh()
        RenderTemplates()
        '订阅左侧栏：不订阅的话点了没反应（事件发了没人听）
        If FrmServerLeft IsNot Nothing Then
            RemoveHandler FrmServerLeft.PageChanged, AddressOf OnSidebarChanged
            AddHandler FrmServerLeft.PageChanged, AddressOf OnSidebarChanged
        End If
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        Try
            ModServer.ScanLocal()
            RenderLocalVersions()
        Catch ex As Exception
            Logger.Error(ex, "刷新服务端列表失败")
            Hint($"刷新失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    '==== 本地服务端 ====

    Private Sub RenderLocalVersions()
        Try
            PanServerList.Children.Clear()
            Dim All = ModServer.LocalVersions

            If All.Count = 0 Then
                LabLocalSummary.Text = "还没有安装任何服务端。"
                PanServerEmpty.Visibility = Visibility.Visible
                RenderGuideSteps()
                Return
            End If
            PanServerEmpty.Visibility = Visibility.Collapsed

            Dim ReadyCount = All.Where(Function(ver) ver.Installed AndAlso ver.EulaAccepted).Count()
            LabLocalSummary.Text = $"共 {All.Count} 个服务端，{ReadyCount} 个可启动" &
                                   If(ModServer.IsRunning, $"，当前运行：{ModServer.RunningVersion?.Name}", "。")

            For Each V In All
                PanServerList.Children.Add(BuildVersionCard(V))
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染服务端列表失败")
        End Try
    End Sub

    ''' <summary>新手引导：三步走，每步都能直接点。</summary>
    Private Sub RenderGuideSteps()
        Try
            If PanServerEmptyInner Is Nothing Then Return
            PanServerEmptyInner.Children.Clear()

            PanServerEmptyInner.Children.Add(New TextBlock With {
                .Text = "三步开服", .FontSize = 15, .FontWeight = FontWeights.SemiBold,
                .HorizontalAlignment = HorizontalAlignment.Center, .Margin = New Thickness(0, 0, 0, 16)
            })

            Dim StepNums = {"1", "2", "3"}
            Dim StepTitles = {"下载服务端核心", "同意 EULA", "启动并进游戏"}
            Dim StepDescs = {
                "到左侧「下载核心」，先按用途选类型，再选核心和版本下载。",
                "首次启动会提示同意 Minecraft EULA，这是 Mojang 的要求。",
                "点「启动」，然后在游戏里用「直连」连 127.0.0.1:端口。"
            }
            For k = 0 To StepNums.Length - 1
                Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 0, 0, 14)}
                Dim Num As New Border With {
                    .Width = 26, .Height = 26, .CornerRadius = New CornerRadius(13),
                    .Background = New SolidColorBrush(Color.FromRgb(&H4A, &H7C, &HD0)),
                    .Margin = New Thickness(0, 0, 12, 0),
                    .Child = New TextBlock With {
                        .Text = StepNums(k), .Foreground = Brushes.White, .FontWeight = FontWeights.Bold,
                        .HorizontalAlignment = HorizontalAlignment.Center,
                        .VerticalAlignment = VerticalAlignment.Center
                    }
                }
                Row.Children.Add(Num)
                Dim Txt As New StackPanel
                Txt.Children.Add(New TextBlock With {.Text = StepTitles(k), .FontWeight = FontWeights.SemiBold})
                Txt.Children.Add(New TextBlock With {
                    .Text = StepDescs(k), .TextWrapping = TextWrapping.Wrap, .FontSize = 12,
                    .Margin = New Thickness(0, 3, 0, 0),
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                })
                Row.Children.Add(Txt)
                PanServerEmptyInner.Children.Add(Row)
            Next

            Dim Go As New MyButton With {
                .Text = "去下载服务端", .Height = 34, .MinWidth = 150,
                .HorizontalAlignment = HorizontalAlignment.Center, .Margin = New Thickness(0, 6, 0, 0)
            }
            AddHandler Go.Click, Sub()
                                     Try
                                         If FrmServerLeft IsNot Nothing Then FrmServerLeft.SetPage(1)
                                         ShowSection(1)
                                     Catch ex As Exception
                                         Logger.Warn(ex, "跳转到下载页失败")
                                     End Try
                                 End Sub
            PanServerEmptyInner.Children.Add(Go)
        Catch ex As Exception
            Logger.Error(ex, "渲染引导失败")
        End Try
    End Sub

    ''' <summary>一个服务端版本的卡片。</summary>
    Private Function BuildVersionCard(V As ModServer.ServerVersion) As FrameworkElement
        Dim IsThis = ModServer.RunningVersion Is V AndAlso ModServer.IsRunning
        Dim Card As New MyCard With {
            .Title = V.Name & If(IsThis, "（运行中）", ""),
            .Margin = New Thickness(0, 0, 0, 10)
        }
        Dim Panel As New StackPanel With {.Margin = New Thickness(25, 36, 25, 14)}

        Dim StatusColor = If(V.Installed AndAlso V.EulaAccepted,
                             Color.FromRgb(&H2E, &H8B, &H74), Color.FromRgb(&HC0, &H88, &H20))
        Panel.Children.Add(New TextBlock With {
            .Text = V.ReadyText, .FontSize = 12.5,
            .Foreground = New SolidColorBrush(StatusColor)
        })

        Dim Info As String = $"端口 {V.Port} · 内存 {V.MemoryMB} MB · " &
                             If(V.OnlineMode, "正版验证", "离线模式") &
                             If(V.Installed, $" · {StringUtils.FormatByteSize(V.JarSize)}", "")
        Panel.Children.Add(New TextBlock With {
            .Text = Info, .Margin = New Thickness(0, 5, 0, 0), .FontSize = 12,
            .TextWrapping = TextWrapping.Wrap,
            .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
        })

        Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 12, 0, 0)}

        If V.Installed Then
            Dim BtnStart As New MyButton With {
                .Text = If(IsThis, "停止", "启动"), .Height = 32, .MinWidth = 90
            }
            AddHandler BtnStart.Click, Sub()
                                           If ModServer.IsRunning AndAlso ModServer.RunningVersion Is V Then
                                               ModServer.StopServer()
                                           Else
                                               If Not V.EulaAccepted Then
                                                   If MyMsgBox("开服前必须同意 Minecraft EULA（https://aka.ms/MinecraftEULA）。" & vbCrLf & vbCrLf &
                                                               "同意后会在该版本目录写入 eula.txt。要同意并继续吗？",
                                                               "Minecraft EULA", "同意", "取消") <> 1 Then Return
                                                   ModServer.AcceptEula(V)
                                               End If
                                               ModServer.StartServer(V)
                                           End If
                                           Refresh()
                                       End Sub
            Row.Children.Add(BtnStart)
        End If

        Dim BtnSettings As New MyButton With {.Text = "配置", .Height = 32, .MinWidth = 80, .Margin = New Thickness(10, 0, 0, 0)}
        AddHandler BtnSettings.Click, Sub() EditVersionConfig(V)
        Row.Children.Add(BtnSettings)

        Dim BtnFolder As New MyButton With {.Text = "打开目录", .Height = 32, .MinWidth = 100, .Margin = New Thickness(10, 0, 0, 0)}
        AddHandler BtnFolder.Click, Sub()
                                        Try
                                            DirectoryUtils.Create(V.Dir)
                                            Process.Start("explorer.exe", $"""{V.Dir}""")
                                        Catch ex As Exception
                                            Logger.Warn(ex, "打开服务端目录失败")
                                        End Try
                                    End Sub
        Row.Children.Add(BtnFolder)

        Dim BtnDelete As New MyButton With {.Text = "删除", .Height = 32, .MinWidth = 80, .Margin = New Thickness(10, 0, 0, 0)}
        AddHandler BtnDelete.Click, Sub()
                               '正在运行的服务端不能删：目录被占用，世界存档也可能写一半
                               If ModServer.IsRunning AndAlso ModServer.RunningVersion Is V Then
                                   Hint("这个服务端正在运行，请先停止再删除", HintType.Red)
                                   Return
                               End If
                               
                               If MyMsgBox($"确定删除服务端「{V.Name}」吗？" & vbCrLf & vbCrLf &
                                           "整个目录（含世界存档）都会被删除，无法撤销。",
                                           "删除服务端", "删除", "取消", IsWarn:=True) <> 1 Then Return
                               ModServer.DeleteVersion(V)
                               Refresh()
        End Sub
        Row.Children.Add(BtnDelete)

        Panel.Children.Add(Row)
        Card.Children.Add(Panel)
        Return Card
    End Function

    ''' <summary>修改某个服务端的端口/内存/正版验证。</summary>
    Private Sub EditVersionConfig(V As ModServer.ServerVersion)
        Try
            Dim PortStr As String = MyMsgBoxInput("端口", "服务端监听端口，默认 25565。",
                DefaultInput:=V.Port.ToString(),
                ValidateRules:=New System.Collections.ObjectModel.Collection(Of Validate) From {New ValidateInteger(1024, 65535)})
            If PortStr Is Nothing Then Return

            Dim MemStr As String = MyMsgBoxInput("内存", "分配给服务端的内存（MB）。建议不低于 1024。",
                DefaultInput:=V.MemoryMB.ToString(),
                ValidateRules:=New System.Collections.ObjectModel.Collection(Of Validate) From {New ValidateInteger(512, 65536)})
            If MemStr Is Nothing Then Return

            Dim OnlineChoice As Integer = MyMsgBox(
                "开启正版验证吗？" & vbCrLf & vbCrLf &
                "开启：只有正版账号能进（online-mode=true）。" & vbCrLf &
                "关闭：离线账号也能进，但无法防冒名（online-mode=false）。",
                "正版验证", If(V.OnlineMode, "保持开启", "关闭"), If(V.OnlineMode, "关闭", "保持开启"))
            If OnlineChoice = 0 Then Return

            V.Port = CInt(Val(PortStr))
            V.MemoryMB = CInt(Val(MemStr))
            If OnlineChoice = 1 Then V.OnlineMode = V.OnlineMode Else V.OnlineMode = Not V.OnlineMode
            ModServer.SaveConfig(V)
            Refresh()
            Hint("配置已保存", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "修改服务端配置失败")
        End Try
    End Sub

    '==== 下载核心 ====

    ''' <summary>加载核心列表并渲染类别筛选。</summary>
    Private Sub LoadCores()
        '先看 Java：没有 Java 开不了服，这一步不该等到最后
        CheckJava()
        If CoresLoaded Then Return
        LabCoreStatus.Text = "正在获取核心列表……"
        Dim T As New Threading.Thread(
            Sub()
                Dim Ok As Boolean = ModServerCore.FetchCores()
                RunInUi(
                    Sub()
                        If Not Ok Then
                            LabCoreStatus.Text = "获取失败，请检查网络后点左侧「下载核心」重试。"
                            Return
                        End If
                        CoresLoaded = True
                        LabCoreStatus.Text = $"共 {ModServerCore.Cores.Count} 个核心，按用途分成 5 类。点下面的类别筛选。"
                        RenderKindFilter()
                        RenderCoreList()
                    End Sub)
            End Sub) With {.IsBackground = True}
        T.Start()
    End Sub

    ''' <summary>渲染类别筛选按钮。</summary>
    Private Sub RenderKindFilter()
        Try
            PanKindFilter.Children.Clear()
            Dim Kinds = {
                {ModServerCore.CoreKind.Vanilla, "原版"},
                {ModServerCore.CoreKind.Plugin, "插件服"},
                {ModServerCore.CoreKind.Modded, "模组服"},
                {ModServerCore.CoreKind.Bedrock, "基岩版"},
                {ModServerCore.CoreKind.Proxy, "代理端"}
            }
            For Each K In Kinds
                Dim Kind = CType(K(0), ModServerCore.CoreKind)
                Dim Count = ModServerCore.GetByKind(Kind).Count
                Dim Btn As New MyButton With {
                    .Text = $"{K(1)} ({Count})", .Height = 30, .MinWidth = 90,
                    .Margin = New Thickness(0, 0, 8, 0),
                    .IsEnabled = Count > 0
                }
                If Kind = SelectedKind Then Btn.Text = "● " & Btn.Text
                AddHandler Btn.Click, Sub()
                                          SelectedKind = Kind
                                          RenderKindFilter()
                                          RenderCoreList()
                                      End Sub
                PanKindFilter.Children.Add(Btn)
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染类别筛选失败")
        End Try
    End Sub

    ''' <summary>渲染当前类别下的核心。</summary>
    Private Sub RenderCoreList()
        Try
            PanCoreList.Children.Clear()
            Dim List = ModServerCore.GetByKind(SelectedKind)
            If List.Count = 0 Then
                PanCoreList.Children.Add(New TextBlock With {
                    .Text = "这个类别暂时没有可用核心。", .TextWrapping = TextWrapping.Wrap,
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                })
                Return
            End If

            For Each C In List
                Dim Card As New MyCard With {
                    .Title = C.Name & If(C.Recommend, "  ★ 推荐", ""),
                    .Margin = New Thickness(0, 0, 0, 8)
                }
                Dim P As New StackPanel With {.Margin = New Thickness(20, 32, 20, 12)}
                P.Children.Add(New TextBlock With {
                    .Text = C.Description, .TextWrapping = TextWrapping.Wrap, .FontSize = 12.5
                })
                P.Children.Add(New TextBlock With {
                    .Text = $"{C.KindText} · {C.AbilityText} · 支持 {C.Versions.Count} 个版本",
                    .TextWrapping = TextWrapping.Wrap, .FontSize = 12, .Margin = New Thickness(0, 5, 0, 0),
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                })
                Dim Btn As New MyButton With {
                    .Text = "选这个核心", .Height = 30, .MinWidth = 120,
                    .Margin = New Thickness(0, 10, 0, 0), .HorizontalAlignment = HorizontalAlignment.Left
                }
                AddHandler Btn.Click, Sub() SelectCore(C)
                P.Children.Add(Btn)
                Card.Children.Add(P)
                PanCoreList.Children.Add(Card)
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染核心列表失败")
        End Try
    End Sub

    ''' <summary>选中一个核心，填充版本下拉框。</summary>
    Private Sub SelectCore(C As ModServerCore.CoreOption)
        Try
            SelectedCore = C
            ComboGameVersion.Items.Clear()
            ComboBuild.Items.Clear()
            '版本倒序，新版在前
            For Each V In C.Versions.OrderByDescending(Function(x) x)
                Dim Item As New MyComboBoxItem With {.Content = V}
                Item.Tag = V
                ComboGameVersion.Items.Add(Item)
            Next
            '设置选中项会触发 SelectionChanged，先抑制，末尾统一加载一次
            SuppressBuildReload = True
            Try
                If ComboGameVersion.Items.Count > 0 Then ComboGameVersion.SelectedIndex = 0
            Finally
                SuppressBuildReload = False
            End Try

            '原版才有官方直连选项
            BtnUseMojang.Visibility = If(C.Kind = ModServerCore.CoreKind.Vanilla, Visibility.Visible, Visibility.Collapsed)

            LabDownloadStatus.Text = $"已选 {C.Name}（{C.KindText}）。选好版本后点「开始下载」。"
            LoadBuilds()
        Catch ex As Exception
            Logger.Error(ex, "选中核心失败")
        End Try
    End Sub

    ''' <summary>按当前核心与版本拉取构建列表。</summary>
    Private Sub LoadBuilds()
        Try
            ComboBuild.Items.Clear()
            If SelectedCore Is Nothing Then Return
            Dim VerItem = TryCast(ComboGameVersion.SelectedItem, MyComboBoxItem)
            Dim Ver As String = TryCast(VerItem?.Tag, String)
            If String.IsNullOrWhiteSpace(Ver) Then Return

            '记下这次请求属于第几代，回调时用来判断是否过期
            BuildRequestGeneration += 1
            Dim MyGen As Integer = BuildRequestGeneration
            Dim CoreName As String = SelectedCore.Name

            LabDownloadStatus.Text = $"正在查 {CoreName} {Ver} 的构建……"
            Dim T As New Threading.Thread(
                Sub()
                    Dim Builds = ModServerCore.FetchBuilds(CoreName, Ver)
                    RunInUi(
                        Sub()
                            '用户已经换过核心/版本，这份结果作废，不能覆盖当前界面
                            If MyGen <> BuildRequestGeneration Then Return
                            
                            ComboBuild.Items.Clear()
                            If Builds.Count = 0 Then
                                LabDownloadStatus.Text = $"{CoreName} {Ver} 暂时没有可用构建，换个版本试试。"
                                Return
                            End If
                            For Each B In Builds
                                Dim Item As New MyComboBoxItem With {
                                    .Content = $"{B.Build}  {If(B.UpdateTime, "").Replace("T", " ")}"
                                }
                                Item.Tag = B
                                ComboBuild.Items.Add(Item)
                            Next
                            ComboBuild.SelectedIndex = 0
                            LabDownloadStatus.Text = $"找到 {Builds.Count} 个构建，点「开始下载」。"
                        End Sub)
                End Sub) With {.IsBackground = True}
            T.Start()
        Catch ex As Exception
            Logger.Error(ex, "加载构建失败")
        End Try
    End Sub

    Private Sub BtnStartDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStartDownload.MouseLeftButtonUp
        DownloadSelected(False)
    End Sub

    Private Sub BtnUseMojang_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnUseMojang.MouseLeftButtonUp
        DownloadSelected(True)
    End Sub

    ''' <summary>下载当前选中的核心。</summary>
    Private Sub DownloadSelected(UseMojang As Boolean)
        Try
            If SelectedCore Is Nothing Then
                Hint("请先选一个核心", HintType.Red)
                Return
            End If
            Dim VerItem = TryCast(ComboGameVersion.SelectedItem, MyComboBoxItem)
            Dim Ver As String = TryCast(VerItem?.Tag, String)
            If String.IsNullOrWhiteSpace(Ver) Then
                Hint("请先选一个游戏版本", HintType.Red)
                Return
            End If

            Dim File As ModServerCore.CoreFile = Nothing
            If UseMojang Then
                File = ModServerCore.FetchVanillaOfficial(Ver)
                If File Is Nothing Then
                    Hint("Mojang 官方源没有这个版本的服务端", HintType.Red)
                    Return
                End If
            Else
                Dim BuildItem = TryCast(ComboBuild.SelectedItem, MyComboBoxItem)
                File = TryCast(BuildItem?.Tag, ModServerCore.CoreFile)
                If File Is Nothing Then
                    Hint("请先选一个构建", HintType.Red)
                    Return
                End If
            End If

            BtnStartDownload.IsEnabled = False
            DownloadTarget = If(SelectedCore.Kind = ModServerCore.CoreKind.Vanilla, Ver, $"{Ver}-{SelectedCore.Name}")
            ShowDownloadProgress(0, SelectedCore.Name & " " & Ver)
            Hint($"开始下载 {SelectedCore.Name} {Ver}", HintType.Blue)

            '接真实进度：比例来自实际读取的字节数，不是假动画
            ModServer.DownloadProgress = Sub(Ratio As Double) RunInUi(Sub() ShowDownloadProgress(Ratio, SelectedCore.Name & " " & Ver))

            Dim T As New Threading.Thread(
                Sub()
                    Dim Ok As Boolean = ModServer.DownloadCore(File, DownloadTarget)
                    RunInUi(
                        Sub()
                            ModServer.DownloadProgress = Nothing
                            BtnStartDownload.IsEnabled = True
                            If Ok Then
                                ShowDownloadDone(SelectedCore.Name, Ver, DownloadTarget)
                                Refresh()
                            Else
                                HideDownloadProgress()
                                LabDownloadStatus.Text = "下载失败，请重试。"
                            End If
                        End Sub)
                End Sub) With {.IsBackground = True}
            T.Start()
        Catch ex As Exception
            ModServer.DownloadProgress = Nothing
            HideDownloadProgress()
            BtnStartDownload.IsEnabled = True
            Logger.Error(ex, "启动下载失败")
        End Try
    End Sub

    Private Sub ComboGameVersion_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboGameVersion.SelectionChanged
        If SuppressBuildReload Then Return
        If SelectedCore IsNot Nothing Then LoadBuilds()
    End Sub

    '==== 模板 ====

    Private Sub RenderTemplates()
        Try
            PanTemplateList.Children.Clear()
            For Each T In Templates
                Dim Card As New MyCard With {.Title = T.Name, .Margin = New Thickness(0, 0, 0, 8)}
                Dim P As New StackPanel With {.Margin = New Thickness(25, 34, 25, 12)}
                P.Children.Add(New TextBlock With {
                    .Text = T.Description, .TextWrapping = TextWrapping.Wrap, .FontSize = 12.5,
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                })
                Dim Btn As New MyButton With {
                    .Text = "用此模板创建", .Height = 30, .MinWidth = 130, .Margin = New Thickness(0, 10, 0, 0),
                    .HorizontalAlignment = HorizontalAlignment.Left
                }
                AddHandler Btn.Click, Sub() CreateFromTemplate(T)
                P.Children.Add(Btn)
                Card.Children.Add(P)
                PanTemplateList.Children.Add(Card)
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染模板失败")
        End Try
    End Sub

    Private Sub CreateFromTemplate(T As ServerTemplate)
        Try
            Dim Name As String = MyMsgBoxInput("服务端版本名",
                "填一个名字作为目录名，建议直接用游戏版本号，方便对应。",
                HintText:="例如：1.20.1")
            If String.IsNullOrWhiteSpace(Name) Then Return
            Name = Name.Trim()
            For Each C In Path.GetInvalidFileNameChars()
                Name = Name.Replace(C, "_"c)
            Next
            If ModServer.LocalVersions.Any(Function(ver) ver.Name = Name) Then
                Hint("同名服务端已存在", HintType.Red)
                Return
            End If

            Dim V As New ModServer.ServerVersion With {
                .Name = Name, .MemoryMB = T.MemoryMB, .OnlineMode = T.OnlineMode,
                .Motd = $"{T.Name} - {Name}"
            }
            ModServer.SaveConfig(V)
            ModServer.ScanLocal()
            Refresh()
            Hint($"已按「{T.Name}」创建 {Name}，接下来请下载对应核心", HintType.Green)
            If FrmServerLeft IsNot Nothing Then FrmServerLeft.SetPage(1)
            ShowSection(1)
        Catch ex As Exception
            Logger.Error(ex, "从模板创建失败")
        End Try
    End Sub

    '==== 控制台 ====

    Public Sub AppendConsole(Line As String)
        Try
            If TextConsoleOutput Is Nothing Then Return
            Dim Stamp = Date.Now.ToString("HH:mm:ss")
            TextConsoleOutput.Text &= $"[{Stamp}] {Line}" & vbCrLf
            If TextConsoleOutput.Text.Length > 200000 Then
                TextConsoleOutput.Text = TextConsoleOutput.Text.Substring(TextConsoleOutput.Text.Length - 100000)
            End If
            ScrollConsole.ScrollToEnd()
        Catch
        End Try
    End Sub

    Private Sub RenderConsole()
        Try
            If ModServer.IsRunning Then
                LabConsoleStatus.Text = $"正在运行：{ModServer.RunningVersion?.Name}（端口 {ModServer.RunningVersion?.Port}）"
                BtnConsoleSend.IsEnabled = True
                BtnConsoleStop.IsEnabled = True
            Else
                LabConsoleStatus.Text = "当前没有运行中的服务器。到「服务端」页启动一个。"
                BtnConsoleSend.IsEnabled = False
                BtnConsoleStop.IsEnabled = False
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新控制台状态失败")
        End Try
    End Sub

    Private Sub BtnConsoleSend_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnConsoleSend.MouseLeftButtonUp
        Try
            Dim Cmd As String = If(TextConsoleCommand.Text, "").Trim()
            If Cmd = "" Then Return
            If Not ModServer.IsRunning Then
                Hint("服务器没有在运行", HintType.Red)
                Return
            End If
            ModServer.SendCommand(Cmd)
            AppendConsole("> " & Cmd)
            TextConsoleCommand.Text = ""
        Catch ex As Exception
            Logger.Error(ex, "发送命令失败")
        End Try
    End Sub

    Private Sub BtnConsoleStop_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnConsoleStop.MouseLeftButtonUp
        If Not ModServer.IsRunning Then Return
        If MyMsgBox("确定停止服务器吗？" & vbCrLf & vbCrLf &
                    "会先发送 stop 命令让服务器保存世界，15 秒未退出才会强制结束。",
                    "停止服务器", "停止", "取消", IsWarn:=True) <> 1 Then Return
        ModServer.StopServer()
        RenderConsole()
    End Sub

    Private Sub BtnConsoleClear_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnConsoleClear.MouseLeftButtonUp
        Try
            TextConsoleOutput.Text = ""
        Catch
        End Try
    End Sub

    '==== 状态 ====

    Private Sub RenderStatus()
        Try
            PanStatusDetail.Children.Clear()
            If Not ModServer.IsRunning Then
                LabStatusSummary.Text = "当前没有运行中的服务器。"
                Return
            End If

            Dim V = ModServer.RunningVersion
            Dim Proc = ModServer.Running
            Dim Uptime As TimeSpan = TimeSpan.Zero
            Dim MemMB As Double = 0
            Try
                If Proc IsNot Nothing AndAlso Not Proc.HasExited Then
                    Uptime = Date.Now - Proc.StartTime
                    MemMB = Proc.WorkingSet64 / 1048576.0
                End If
            Catch
            End Try

            LabStatusSummary.Text = $"正在运行：{V?.Name}"

            Dim Card As New MyCard With {.Title = "实时数据", .Margin = New Thickness(0, 0, 0, 12)}
            Dim G As New Grid With {.Margin = New Thickness(25, 36, 25, 16)}
            For k = 0 To 3
                G.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(30)})
            Next
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(130)})
            G.ColumnDefinitions.Add(New ColumnDefinition)

            Dim RowLabels = {"服务器", "端口", "已运行", "内存占用"}
            Dim RowValues = {
                If(V?.Name, "-"),
                If(V?.Port.ToString(), "-"),
                If(Uptime.TotalHours >= 1, $"{CInt(Uptime.TotalHours)} 小时 {Uptime.Minutes} 分", $"{Uptime.Minutes} 分 {Uptime.Seconds} 秒"),
                $"{Math.Round(MemMB, 1)} MB"
            }
            For k = 0 To 3
                Dim Lbl As New TextBlock With {
                    .Text = RowLabels(k), .VerticalAlignment = VerticalAlignment.Center,
                    .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
                }
                Grid.SetRow(Lbl, k) : Grid.SetColumn(Lbl, 0) : G.Children.Add(Lbl)

                Dim Val As New TextBlock With {.Text = RowValues(k), .VerticalAlignment = VerticalAlignment.Center}
                Grid.SetRow(Val, k) : Grid.SetColumn(Val, 1) : G.Children.Add(Val)
            Next
            Card.Children.Add(G)
            PanStatusDetail.Children.Add(Card)
        Catch ex As Exception
            Logger.Error(ex, "渲染状态失败")
        End Try
    End Sub

    Private Sub BtnStatusRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStatusRefresh.MouseLeftButtonUp
        RenderStatus()
    End Sub

    '==== 其它 ====

    Private Sub BtnServerRescan_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerRescan.MouseLeftButtonUp
        Refresh()
        Hint("已重新扫描", HintType.Green)
    End Sub

    Private Sub BtnServerFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerFolder.MouseLeftButtonUp
        Try
            DirectoryUtils.Create(ModServer.ServerDir)
            Process.Start("explorer.exe", $"""{ModServer.ServerDir}""")
        Catch ex As Exception
            Logger.Warn(ex, "打开目录失败")
        End Try
    End Sub

    ''' <summary>切换到指定子页面（0=服务端 1=下载核心 2=控制台 3=状态 4=模板）。</summary>
    Public Sub ShowSection(ID As Integer)
        Try
            PanSecVersions.Visibility = If(ID = 0, Visibility.Visible, Visibility.Collapsed)
            PanSecDownload.Visibility = If(ID = 1, Visibility.Visible, Visibility.Collapsed)
            PanSecConsole.Visibility = If(ID = 2, Visibility.Visible, Visibility.Collapsed)
            PanSecStatus.Visibility = If(ID = 3, Visibility.Visible, Visibility.Collapsed)
            PanSecTemplates.Visibility = If(ID = 4, Visibility.Visible, Visibility.Collapsed)
            If ID = 0 Then Refresh()
            If ID = 1 Then
                LoadCores()
                PanDownloadDone.Visibility = Visibility.Collapsed
            End If
            If ID = 2 Then RenderConsole()
            If ID = 3 Then RenderStatus()
            If ID = 4 Then RenderTemplates()
        Catch ex As Exception
            Logger.Error(ex, $"切换服务端子页失败：{ID}")
        End Try
    End Sub


    '==== 下载进度与下一步 ====

    ''' <summary>
    ''' 显示下载进度。Ratio 为 -1 表示总量未知（不显示百分比，只显示已下载量）。
    ''' </summary>
    Private Sub ShowDownloadProgress(Ratio As Double, Label As String)
        Try
            PanDownloadProgress.Visibility = Visibility.Visible
            PanDownloadDone.Visibility = Visibility.Collapsed
            Dim Pct As Integer = CInt(Math.Max(0, Math.Min(100, Ratio * 100)))
            LabProgressText.Text = If(Ratio < 0,
                $"{Label}：正在下载……",
                $"{Label}：{Pct}%")

            '按可用宽度算条宽，不写死像素
            Dim Full As Double = 0
            Try
                Full = Math.Max(0, PanDownloadProgress.ActualWidth)
            Catch
            End Try
            If Full <= 0 Then Full = 400
            BarDownload.Width = Math.Max(0, Full * Math.Max(0, Math.Min(1, Ratio)))
        Catch ex As Exception
            Logger.Warn(ex, "更新下载进度失败")
        End Try
    End Sub

    Private Sub HideDownloadProgress()
        Try
            PanDownloadProgress.Visibility = Visibility.Collapsed
            BarDownload.Width = 0
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' 下载完成：给出下一步，而不是只留一行文字。
    ''' 开服是连续动作，玩家下完 jar 往往不知道接下来该干什么。
    ''' </summary>
    Private Sub ShowDownloadDone(CoreName As String, Ver As String, TargetName As String)
        Try
            HideDownloadProgress()
            LabDownloadStatus.Text = $"{CoreName} {Ver} 下载完成。"

            PanDownloadDone.Visibility = Visibility.Visible
            LabDoneTitle.Text = "服务端核心已准备好"
            LabDoneDesc.Text = $"已下载到 服务端「{TargetName}」目录。下一步可以配端口与内存，或直接去启动。" &
                               vbCrLf & "首次启动会要求同意 Minecraft EULA。"
        Catch ex As Exception
            Logger.Warn(ex, "显示下载完成提示失败")
        End Try
    End Sub

    Private Sub BtnGoConfig_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnGoConfig.MouseLeftButtonUp
        Try
            Dim V = ModServer.LocalVersions.FirstOrDefault(Function(ver) ver.Name = DownloadTarget)
            If V Is Nothing Then
                Refresh()
                V = ModServer.LocalVersions.FirstOrDefault(Function(ver) ver.Name = DownloadTarget)
            End If
            If V Is Nothing Then
                Hint("找不到刚下载的服务端，请点「重新扫描」", HintType.Red)
                Return
            End If
            EditVersionConfig(V)
        Catch ex As Exception
            Logger.Warn(ex, "跳转配置失败")
        End Try
    End Sub

    Private Sub BtnGoStart_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnGoStart.MouseLeftButtonUp
        Try
            If FrmServerLeft IsNot Nothing Then FrmServerLeft.SetPage(0)
            ShowSection(0)
        Catch ex As Exception
            Logger.Warn(ex, "跳转服务端列表失败")
        End Try
    End Sub


    '==== Java 前置检查 ====

    ''' <summary>
    ''' 检查是否有可用 Java，并把结论直接显示在下载页。
    ''' <para>
    ''' 服务端需要 Java 才能跑。原来的流程要等下载完、点了启动才提示，
    ''' 玩家已经花时间下了几十兆才发现跑不起来。这里提到最前面。
    ''' </para>
    ''' </summary>
    Private Sub CheckJava()
        Try
            Dim Java = ModServer.FindJava()
            If Not String.IsNullOrWhiteSpace(Java) Then
                PanJavaHint.Visibility = Visibility.Collapsed
                Return
            End If

            PanJavaHint.Visibility = Visibility.Visible
            LabJavaHint.Text = "没有检测到 Java。服务端必须靠 Java 运行，装好再下载能少走弯路。"
        Catch ex As Exception
            Logger.Warn(ex, "检测 Java 失败")
        End Try
    End Sub

    Private Sub BtnJavaRetry_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJavaRetry.MouseLeftButtonUp
        CheckJava()
        If PanJavaHint.Visibility = Visibility.Collapsed Then
            Hint("已检测到 Java", HintType.Green)
        Else
            Hint("仍然没有找到 Java", HintType.Red)
        End If
    End Sub

    Private Sub BtnJavaGoSetup_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJavaGoSetup.MouseLeftButtonUp
        Try
            FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupLaunch)
        Catch ex As Exception
            Logger.Warn(ex, "跳转到 Java 设置失败")
        End Try
    End Sub

    ''' <summary>左侧栏切换时同步右侧内容。</summary>
    Private Sub OnSidebarChanged(sender As Object, id As Integer)
        ShowSection(id)
    End Sub
End Class
