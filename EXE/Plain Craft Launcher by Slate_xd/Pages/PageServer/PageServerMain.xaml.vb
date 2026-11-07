Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.IO
Imports System.Diagnostics

''' <summary>
''' 一键开服器页面。
''' </summary>
Public Class PageServerMain
    Inherits MyPageRight
    Implements IRefreshable

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        Try
            ModServer.Scan()
            RenderServers()
        Catch ex As Exception
            Logger.Error(ex, "刷新服务器列表失败")
            Hint($"刷新失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    Private Sub RenderServers()
        Try
            PanServerList.Children.Clear()
            Dim All = ModServer.Servers
            LabServerSummary.Text = If(All.Count = 0,
                "还没有服务器。点击「新建服务器」开始。",
                $"共 {All.Count} 个服务器，{(If(ModServer.IsRunning, "有 1 个正在运行", "当前没有运行中的服务器"))}。")

            If All.Count = 0 Then
                PanServerEmpty.Visibility = Visibility.Visible
                Return
            End If
            PanServerEmpty.Visibility = Visibility.Collapsed

            For Each S In All
                PanServerList.Children.Add(BuildServerCard(S))
            Next
        Catch ex As Exception
            Logger.Error(ex, "渲染服务器列表失败")
        End Try
    End Sub

    Private Function BuildServerCard(S As ModServer.ServerInstance) As FrameworkElement
        Dim IsThis = ModServer.RunningServer Is S AndAlso ModServer.IsRunning
        Dim Card As New MyCard With {
            .Title = S.Name & If(IsThis, "（运行中）", ""),
            .Margin = New Thickness(0, 0, 0, 12)
        }
        Dim Panel As New StackPanel With {.Margin = New Thickness(25, 38, 25, 16)}

        Dim Meta As String = $"{If(S.Loader = "vanilla", "原版", S.Loader)}" &
                             If(String.IsNullOrEmpty(S.LoaderVersion), "", $" {S.LoaderVersion}") &
                             If(String.IsNullOrEmpty(S.GameVersion), "", $" · MC {S.GameVersion}") &
                             $" · 端口 {S.Port} · 内存 {S.MemoryMB} MB"
        Panel.Children.Add(New TextBlock With {.Text = Meta, .TextWrapping = TextWrapping.Wrap})

        '状态提示
        Dim Issues As New List(Of String)
        If Not S.EulaAccepted Then Issues.Add("未同意 EULA")
        If Not FileUtils.Exists(S.JarPath) Then Issues.Add("缺少 server.jar")
        If Issues.Count > 0 Then
            Panel.Children.Add(New TextBlock With {
                .Text = "需要处理：" & String.Join("、", Issues),
                .Margin = New Thickness(0, 6, 0, 0), .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                .Foreground = New SolidColorBrush(Color.FromRgb(&HD0, &H80, &H20))
            })
        End If

        Dim Row As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .Margin = New Thickness(0, 12, 0, 0)
        }

        Dim BtnStart As New MyButton With {
            .Text = If(IsThis, "停止", "启动"),
            .Height = 32, .MinWidth = 90
        }
        AddHandler BtnStart.Click, Sub()
                                       If ModServer.IsRunning AndAlso ModServer.RunningServer Is S Then
                                           ModServer.StopServer()
                                       Else
                                           If Not S.EulaAccepted Then
                                               If MyMsgBox("开服前必须同意 Minecraft EULA（https://aka.ms/MinecraftEULA）。" & vbCrLf & vbCrLf &
                                                           "同意后会在服务器目录写入 eula.txt。要同意并继续吗？",
                                                           "Minecraft EULA", "同意", "取消") <> 1 Then Return
                                               ModServer.AcceptEula(S)
                                           End If
                                           ModServer.StartServer(S)
                                       End If
                                       Refresh()
                                   End Sub
        Row.Children.Add(BtnStart)

        Dim BtnFolder As New MyButton With {.Text = "打开目录", .Height = 32, .MinWidth = 100, .Margin = New Thickness(10, 0, 0, 0)}
        AddHandler BtnFolder.Click, Sub()
                                        Try
                                            DirectoryUtils.Create(S.Dir)
                                            Process.Start("explorer.exe", $"""{S.Dir}""")
                                        Catch ex As Exception
                                            Logger.Warn(ex, "打开服务器目录失败")
                                        End Try
                                    End Sub
        Row.Children.Add(BtnFolder)

        Dim BtnDelete As New MyButton With {.Text = "删除", .Height = 32, .MinWidth = 80, .Margin = New Thickness(10, 0, 0, 0)}
        AddHandler BtnDelete.Click, Sub()
                                        If MyMsgBox($"确定删除服务器「{S.Name}」吗？" & vbCrLf & vbCrLf &
                                                    "整个服务器目录（含世界存档）都会被删除，无法撤销。",
                                                    "删除服务器", "删除", "取消", IsWarn:=True) <> 1 Then Return
                                        ModServer.Delete(S)
                                        Refresh()
                                    End Sub
        Row.Children.Add(BtnDelete)

        Panel.Children.Add(Row)
        Card.Children.Add(Panel)
        Return Card
    End Function

    ''' <summary>新建服务器。</summary>
    Private Sub BtnServerNew_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerNew.MouseLeftButtonUp
        Try
            Dim Name As String = MyMsgBoxInput("新建服务器", "给服务器起个名字，会用它作为文件夹名。",
                                               HintText:="例如：和朋友一起玩")
            If String.IsNullOrWhiteSpace(Name) Then Return
            Name = Name.Trim()
            For Each C In Path.GetInvalidFileNameChars()
                Name = Name.Replace(C, "_"c)
            Next
            If String.IsNullOrWhiteSpace(Name) Then Return

            If ModServer.Servers.Any(Function(x) x.Name = Name) Then
                Hint("同名服务器已存在", HintType.Red)
                Return
            End If

            Dim S As New ModServer.ServerInstance With {.Name = Name}
            ModServer.Save(S)
            ModServer.Scan()
            Refresh()
            Hint($"已创建服务器「{Name}」，把 server.jar 放进它的目录即可开服", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "新建服务器失败")
            Hint($"创建失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    Private Sub BtnServerRescan_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerRescan.MouseLeftButtonUp
        Refresh()
        Hint("已重新扫描服务器目录", HintType.Green)
    End Sub

    Private Sub BtnServerFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerFolder.MouseLeftButtonUp
        Try
            DirectoryUtils.Create(ModServer.ServerDir)
            Process.Start("explorer.exe", $"""{ModServer.ServerDir}""")
        Catch ex As Exception
            Logger.Warn(ex, "打开服务器目录失败")
        End Try
    End Sub

End Class
