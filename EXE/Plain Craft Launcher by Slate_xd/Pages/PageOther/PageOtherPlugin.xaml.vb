Imports System.IO

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
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        Try
            ModPlugin.Scan()
            RenderPluginList()
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
    Private Sub BtnPluginNew_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPluginNew.MouseLeftButtonUp
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
    Private Sub BtnPluginFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPluginFolder.MouseLeftButtonUp
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

End Class
