Imports System.IO
Imports System.Diagnostics
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading

''' <summary>
''' 一键开服器页面：本地服务端版本 + 服务端下载 + 预设模板。
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
        Public Property Motd As String = ""
    End Class

    Private ReadOnly Templates As New List(Of ServerTemplate) From {
        New ServerTemplate With {.Name = "和朋友玩", .Description = "4 GB 内存，开启正版验证，适合熟人小服。", .MemoryMB = 4096, .OnlineMode = True},
        New ServerTemplate With {.Name = "轻量测试", .Description = "1 GB 内存，关闭正版验证，适合快速验证模组或命令。", .MemoryMB = 1024, .OnlineMode = False},
        New ServerTemplate With {.Name = "大型整合包服", .Description = "8 GB 内存，开启正版验证，给重模组整合包用。", .MemoryMB = 8192, .OnlineMode = True},
        New ServerTemplate With {.Name = "离线局域网", .Description = "2 GB 内存，关闭正版验证，只在家里/宿舍局域网用。", .MemoryMB = 2048, .OnlineMode = False}
    }

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
        RenderTemplates()
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

    ''' <summary>渲染本地服务端列表。</summary>
    Private Sub RenderLocalVersions()
        Try
            PanServerList.Children.Clear()
            Dim All = ModServer.LocalVersions

            If All.Count = 0 Then
                LabLocalSummary.Text = "还没有安装任何服务端。"
                PanServerEmpty.Visibility = Visibility.Visible
                '一个都没有时直接把下载面板摊开，省掉玩家再点一次的功夫
                ShowDownloadPanel()
                Return
            End If
            PanServerEmpty.Visibility = Visibility.Collapsed

            Dim Running = ModServer.IsRunning
            Dim ReadyCount = All.Where(Function(ver) ver.Installed AndAlso ver.EulaAccepted).Count()
            LabLocalSummary.Text = $"共 {All.Count} 个服务端，{ReadyCount} 个可启动" &
                                   If(Running, $"，当前运行：{ModServer.RunningVersion?.Name}", "。")

            For Each V In All
                PanServerList.Children.Add(BuildVersionCard(V))
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染服务端列表失败")
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

        '状态行
        Dim StatusColor = If(V.Installed AndAlso V.EulaAccepted,
                             Color.FromRgb(&H2E, &H8B, &H74), Color.FromRgb(&HC0, &H88, &H20))
        Panel.Children.Add(New TextBlock With {
            .Text = V.ReadyText, .FontSize = 12.5,
            .Foreground = New SolidColorBrush(StatusColor)
        })

        '配置信息
        Dim Info As String = $"端口 {V.Port} · 内存 {V.MemoryMB} MB · " &
                             If(V.OnlineMode, "正版验证", "离线模式") &
                             If(V.Installed, $" · {StringUtils.FormatByteSize(V.JarSize)}", "")
        Panel.Children.Add(New TextBlock With {
            .Text = Info, .Margin = New Thickness(0, 5, 0, 0), .FontSize = 12,
            .TextWrapping = TextWrapping.Wrap,
            .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
        })

        '按钮行
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
        Else
            Dim BtnDl As New MyButton With {
                .Text = "下载服务端", .Height = 32, .MinWidth = 110
            }
            AddHandler BtnDl.Click, Sub()
                                        ShowDownloadPanel()
                                        Hint($"请在下载面板里选择 {V.Name} 对应的版本", HintType.Blue)
                                    End Sub
            Row.Children.Add(BtnDl)
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

    ''' <summary>渲染预设模板。</summary>
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

    ''' <summary>从模板创建服务端。</summary>
    Private Sub CreateFromTemplate(T As ServerTemplate)
        Try
            Dim Name As String = MyMsgBoxInput("服务端版本名",
                "填一个 Minecraft 版本号，会用它作为目录名。下载服务端时也要选同一个版本。",
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
            Hint($"已按「{T.Name}」创建 {Name}，接下来请下载对应的服务端", HintType.Green)
            ShowDownloadPanel()
        Catch ex As Exception
            Logger.Error(ex, "从模板创建失败")
        End Try
    End Sub

    ''' <summary>展开下载面板并异步拉取版本列表。</summary>
    Private Sub ShowDownloadPanel()
        PanDownload.Visibility = Visibility.Visible
        If ComboServerVersion.Items.Count > 0 Then Return

        LabDownloadStatus.Text = "正在获取可下载版本……"
        BtnStartDownload.IsEnabled = False
        Dim T As New Threading.Thread(
            Sub()
                Dim Ok As Boolean = ModServer.FetchRemote()
                RunInUi(
                    Sub()
                        BtnStartDownload.IsEnabled = True
                        If Not Ok Then
                            LabDownloadStatus.Text = "获取失败，请检查网络后重试。"
                            Return
                        End If
                        ComboServerVersion.Items.Clear()
                        For Each R In ModServer.RemoteVersions
                            Dim Item As New MyComboBoxItem With {.Content = $"{R.Id}  （{StringUtils.FormatByteSize(R.Size)}  {R.ReleaseTime}）"}
                            Item.Tag = R
                            ComboServerVersion.Items.Add(Item)
                        Next
                        If ComboServerVersion.Items.Count > 0 Then ComboServerVersion.SelectedIndex = 0
                        LabDownloadStatus.Text = $"共 {ModServer.RemoteVersions.Count} 个可下载的服务端版本。"
                    End Sub)
            End Sub) With {.IsBackground = True}
        T.Start()
    End Sub

    Private Sub BtnServerDownloadNew_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerDownloadNew.MouseLeftButtonUp
        ShowDownloadPanel()
    End Sub

    Private Sub BtnCancelDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCancelDownload.MouseLeftButtonUp
        PanDownload.Visibility = Visibility.Collapsed
    End Sub

    Private Sub BtnStartDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStartDownload.MouseLeftButtonUp
        Try
            Dim Item = TryCast(ComboServerVersion.SelectedItem, MyComboBoxItem)
            Dim R = TryCast(Item?.Tag, ModServer.RemoteVersion)
            If R Is Nothing Then
                Hint("请先选一个版本", HintType.Red)
                Return
            End If

            BtnStartDownload.IsEnabled = False
            LabDownloadStatus.Text = $"正在下载 {R.Id}（{StringUtils.FormatByteSize(R.Size)}）……"
            Hint($"开始下载服务端 {R.Id}", HintType.Blue)

            Dim T As New Threading.Thread(
                Sub()
                    Dim Ok As Boolean = ModServer.DownloadServer(R.Url, R.Id, R.Size)
                    RunInUi(
                        Sub()
                            BtnStartDownload.IsEnabled = True
                            If Ok Then
                                LabDownloadStatus.Text = $"{R.Id} 下载完成。"
                                Refresh()
                            Else
                                LabDownloadStatus.Text = "下载失败，请重试。"
                            End If
                        End Sub)
                End Sub) With {.IsBackground = True}
            T.Start()
        Catch ex As Exception
            BtnStartDownload.IsEnabled = True
            Logger.Error(ex, "启动下载失败")
        End Try
    End Sub

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

End Class
