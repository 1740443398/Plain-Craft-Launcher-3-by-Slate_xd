Imports System.Windows.Media

''' <summary>
''' 插件宿主页面：把一个插件的 main.xaml 当作独立页面显示。
''' <para>
''' <b>这是插件给启动器加新功能的载体</b>：清单里声明 <c>titlebar: true</c> 后，
''' 启动器会在标题栏加一个入口，点进去就是这里。
''' </para>
''' <para>
''' <b>为什么复用同一个页面类</b>：如果有十个插件，不该产生十个页面类。
''' 用 <see cref="Target"/> 标记当前显示哪个插件，切插件时重新加载内容即可。
''' </para>
''' </summary>
Public Class PagePluginHost
    Inherits MyPageRight
    Implements IRefreshable

    ''' <summary>当前显示的插件。切换时在加载前设置。</summary>
    Public Property Target As ModPlugin.PluginEntry = Nothing

    Private LoadedKey As String = ""

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        Try
            If Target Is Nothing Then
                RenderMessage("没有指定插件")
                Return
            End If
            '标题栏显示当前插件，让用户知道自己在哪
            Try
                If LabHostTitle IsNot Nothing Then LabHostTitle.Text = "当前插件：" & Target.DisplayName
            Catch
            End Try
            If Not Target.IsUsable Then
                RenderMessage($"这个插件无法加载：{Target.FailureReason}")
                Return
            End If
            If Not Target.Enabled Then
                RenderMessage("这个插件已被禁用。到「更多 → 插件」里启用它。")
                Return
            End If

            '同一个插件不重复加载
            Dim Key = Target.Directory & "|" & Target.EntryPath
            If Key = LoadedKey AndAlso PanHost.Children.Count > 0 Then Return

            Dim Raw As String = ModPlugin.ReadEntry(Target)
            If String.IsNullOrWhiteSpace(Raw) Then
                RenderMessage("插件内容为空")
                Return
            End If

            '与主页、配置页同一套处理：去掉命名空间，包一层 StackPanel
            Do While Raw.Contains("xmlns")
                Raw = Raw.RegexReplace("xmlns[^""']*(""|')[^""']*(""|')", "").Replace("xmlns", "")
            Loop
            Dim Wrapped As String =
                "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" " &
                "xmlns:sys=""clr-namespace:System;assembly=mscorlib"" " &
                "xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" " &
                "xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher by Slate_xd"" " &
                "xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" " &
                "xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" &
                Raw & "</StackPanel>"

            Dim Ui As Object = GetObjectFromXML(Wrapped)
            PanHost.Children.Clear()
            If TypeOf Ui Is FrameworkElement Then
                PanHost.Children.Add(CType(Ui, FrameworkElement))
                LoadedKey = Key
            Else
                RenderMessage("插件内容无法解析为界面元素")
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"加载插件页面失败：{Target?.DisplayName}")
            RenderMessage($"插件页面加载出错：{ex.Message}")
        End Try
    End Sub

    ''' <summary>显示一条说明，替代插件内容。</summary>
    Private Sub RenderMessage(Text As String)
        Try
            PanHost.Children.Clear()
            Dim Card As New MyCard With {.Title = "插件", .Margin = New Thickness(0, 0, 0, 15)}
            Dim P As New StackPanel With {.Margin = New Thickness(25, 38, 25, 16)}
            P.Children.Add(New TextBlock With {
                .Text = Text, .TextWrapping = TextWrapping.Wrap,
                .Foreground = New SolidColorBrush(Color.FromRgb(&H99, &H99, &H99))
            })
            Card.Children.Add(P)
            PanHost.Children.Add(Card)
        Catch
        End Try
    End Sub


    ''' <summary>返回插件列表页。</summary>
    Private Sub BtnHostBack_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnHostBack.MouseLeftButtonUp
        Try
            FrmMain.PageChange(FormMain.PageType.Plugin)
        Catch ex As Exception
            Logger.Warn(ex, "返回插件列表失败")
        End Try
    End Sub
End Class
