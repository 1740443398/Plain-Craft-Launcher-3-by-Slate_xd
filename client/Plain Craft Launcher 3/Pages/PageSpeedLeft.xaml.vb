Public Class PageSpeedLeft
    '任务管理面板的刷新间隔（毫秒）
    '子任务数量多时（例如下载原版游戏的上百个支持库），
    '即使只改文字，遍历与布局开销依然可观，因此按规模自适应放慢。
    Private Const WatcherInterval As Integer = 300
    Private Const WatcherIntervalBusy As Integer = 800

    '初始化
    Private IsLoad As Boolean = False
    Private Sub Page_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '进入时就刷新一次显示
        Watcher()

        '如果在页面切换动画的 “上一页消失” 部分已经完成了下载，就直接尝试返回
        TryReturnToHome()

        If IsLoad Then Return
        IsLoad = True

        '监控定时器
        Dim timer As New Threading.DispatcherTimer With {.Interval = New TimeSpan(0, 0, 0, 0, WatcherInterval)}
        AddHandler timer.Tick,
            Sub()
                '按当前任务规模动态调整刷新间隔：
                '子任务很多时放慢刷新，避免 UI 线程被持续占用
                Try
                    Dim Busy As Boolean = LoaderTaskbar.Any(Function(l) l IsNot Nothing AndAlso l.Show AndAlso
                                                                 CType(l, Object).GetLoaderList().Count > 60)
                    Dim Target As Integer = If(Busy, WatcherIntervalBusy, WatcherInterval)
                    If CInt(timer.Interval.TotalMilliseconds) <> Target Then
                        timer.Interval = New TimeSpan(0, 0, 0, 0, Target)
                    End If
                Catch
                    '取规模失败时保持原间隔
                End Try
                Watcher()
            End Sub
        timer.Start()

        '非调试模式隐藏线程数
        If Not ModeDebug Then
            RowDefinitions(12).Height = New GridLength(0)
            RowDefinitions(13).Height = New GridLength(0)
            RowDefinitions(14).Height = New GridLength(0)
            RowDefinitions(15).Height = New GridLength(0)
        End If

    End Sub

    '定时器任务
    Private ReadOnly RightCards As New Dictionary(Of String, MyCard)
    Private Sub Watcher()
        If Not FrmMain.PageCurrent = FormMain.PageType.TaskManager Then Return
        Try

#Region "更新左边栏"
            If Not LoaderTaskbar.Any() Then
                '无任务
                LabProgress.Text = "100 %"
                LabSpeed.Text = "0 B/s"
                LabFile.Text = "0"
                LabThread.Text = "0 / " & NetTaskThreadLimit
            Else
                '有任务，输出基本信息
                Dim Tasks = LoaderTaskbar.Where(Function(l) l.Show).ToList() '筛选掉启动 MC 的任务（#6270）
                Dim RawPercent As Double = If(Tasks.Any, Tasks.Select(Function(l) l.Progress).Average().Clamp(0, 1), 1)
                Dim PredictText As String = Math.Floor(RawPercent * 100) & "." & Math.Floor((RawPercent * 100 - Math.Floor(RawPercent * 100)) * 100).ToString.EnsureLength("0", 2) & " %"
                LabProgress.Text = If(RawPercent > 0.999999, "100 %", PredictText)
                LabSpeed.Text = StringUtils.FormatByteSize(NetManager.Speed) & "/s"
                LabFile.Text = If(NetManager.FileRemain < 0, "0*", NetManager.FileRemain)
                LabThread.Text = NetTaskThreadCount & " / " & NetTaskThreadLimit
            End If
#End Region

        Catch ex As Exception
            Logger.Error(ex, "任务管理左栏监视出错")
        End Try
        If FrmSpeedRight Is Nothing OrElse FrmSpeedRight.PanMain Is Nothing Then Return
        Try
            For Each Loader In LoaderTaskbar.ToList
                TaskRefresh(Loader)
            Next
        Catch ex As Exception
            Logger.Error(ex, "任务管理右栏监视出错")
        End Try
    End Sub
    Public Sub TaskRefresh(Loader As LoaderBase)
        If Loader Is Nothing OrElse Not Loader.Show Then Return
        Try
            '获取实际加载器列表
            Dim LoaderList As List(Of LoaderBase) = CType(Loader, Object).GetLoaderList()
            If RightCards.ContainsKey(Loader.Name) Then
                '已有此卡片
                Dim Card As Grid = RightCards(Loader.Name)
                Dim NewValue As Double = Loader.Progress + Loader.State
                If Val(Card.Tag) = NewValue Then Return
                Card.Tag = NewValue
                If Card.Children.Count <= 3 Then
                    Logger.Warn($"元素不足的卡片：{Loader.Name}")
                    Return
                End If
                Card = Card.Children(3)
                Try
                    Select Case Loader.State
                        Case LoadState.Failed
#Region "失败，更新卡片"
                            Card.RowDefinitions.Clear()
                            Card.Children.Clear()
                            Card.Children.Add(GetObjectFromXML("<Path xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" Stretch=""Uniform"" Tag=""Failed"" Data=""F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z"" Height=""15"" Width=""15"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""0"" Fill=""{DynamicResource ColorBrush3}"" Margin=""0,1,0,0"" VerticalAlignment=""Top""/>"))
                            Dim Tb As TextBlock = GetObjectFromXML("<TextBlock xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" TextWrapping=""Wrap"" HorizontalAlignment=""Left"" ToolTip=""单击复制错误详情"" Grid.Column=""1"" Grid.Row=""0"" Margin=""0,0,0,5"" />")
                            Tb.Text = Loader.Error.GetDisplay(True)
                            AddHandler Tb.MouseLeftButtonDown,
                            Sub(sender As TextBlock, e As EventArgs)
                                ClipboardSet(sender.Text, False)
                                Hint("已复制错误详情！", HintType.Green)
                            End Sub
                            Card.Children.Add(Tb)
#End Region
                        Case LoadState.Finished, LoadState.Canceled
#Region "完成或中断，销毁卡片并返回"
                            AniDispose(CType(Card.Parent, MyCard), True, AddressOf TryReturnToHome)
#End Region
                        Case LoadState.Loading, LoadState.Waiting
#Region "进度不同，更新卡片"
                            Try
                                If Card.Children.Count < LoaderList.Count * 2 Then
                                    Logger.Warn($"刷新任务管理卡片 {Loader.Name} 失败：卡片中仅有 {Card.Children.Count} 个子项，要求至少有 {LoaderList.Count * 2} 个子项")
                                    Exit Try
                                End If
                                '性能优化：GetObjectFromXML 每次都要走一遍 XAML 解析，开销极大。
                                '原先在子任务状态从 Waiting 变为 Loading 时会重建整个图标控件，
                                '下载时子任务多达上百个，每 300ms 重建一轮会直接把 UI 线程拖死。
                                '现在改为：图标控件只在「首次创建」时生成，之后仅就地切换可见性与文字，
                                '不再反复 Remove/Insert，也不再重复解析 XAML。
                                Dim Row As Integer = 0
                                For Each SubTask In LoaderList
                                    If Row * 2 >= Card.Children.Count Then Exit For
                                    Dim IconHost As FrameworkElement = TryCast(Card.Children(Row * 2), FrameworkElement)
                                    If IconHost Is Nothing Then
                                        Row += 1
                                        Continue For
                                    End If
                                    Select Case SubTask.State
                                        Case LoadState.Waiting, LoadState.Loading
                                            '两种情况共用同一个 TextBlock：显示百分比，或显示等待圆点
                                            Dim Tb As TextBlock = TryCast(IconHost, TextBlock)
                                            If Tb IsNot Nothing Then
                                                If SubTask.State = LoadState.Waiting Then
                                                    '等待中用省略号占位，避免再创建 Path
                                                    If Tb.Tag <> "Waiting" Then
                                                        Tb.Tag = "Waiting"
                                                        Tb.Text = "…"
                                                    End If
                                                Else
                                                    Dim Percent As String = Math.Floor(SubTask.Progress * 100) & "%"
                                                    '只在文字真的变化时才赋值，减少布局失效
                                                    If Tb.Tag <> "Loading" OrElse Tb.Text <> Percent Then
                                                        Tb.Tag = "Loading"
                                                        If Tb.Text <> Percent Then Tb.Text = Percent
                                                    End If
                                                End If
                                            End If
                                        Case LoadState.Finished
                                            Dim Tb As TextBlock = TryCast(IconHost, TextBlock)
                                            If Tb IsNot Nothing AndAlso Tb.Tag <> "Finished" Then
                                                Tb.Tag = "Finished"
                                                Tb.Text = "√"
                                            End If
                                    End Select
                                    Row += 1
                                Next
                            Catch ex As Exception
                                Logger.Error(ex, $"刷新任务管理卡片 {Loader.Name} 失败")
                            End Try
#End Region
                    End Select
                Catch ex As Exception
                    Logger.Error(ex, $"更新任务管理显示失败（{Loader.State}）")
                End Try
            ElseIf Not (Loader.State = LoadState.Canceled OrElse Loader.State = LoadState.Finished) Then
                Try
#Region "没有卡片且未中断或完成，添加新的卡片"
                    Dim CardXAML As String = "
                        <local:MyCard xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 3""
                            Tag=""" & (Loader.Progress + Loader.State) & """ Title=""" & StringUtils.XmlEscape(Loader.Name) & """ Margin=""0,0,0,15"">
                            <Grid Margin=""14,40,15,10"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""50""/>
                                    <ColumnDefinition/>
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>"
                    For Each SubTask In LoaderList
                        CardXAML += "<RowDefinition Height=""26""/>"
                    Next
                    CardXAML += "</Grid.RowDefinitions>"
                    Dim Row As Integer = 0
                    For Each SubTask In LoaderList
                        '性能优化：所有状态统一用 TextBlock 承载，
                        '这样后续刷新时只需要改文字，无需 Remove/Insert 重建控件，
                        '也无需再调用开销很大的 GetObjectFromXML。
                        Select Case SubTask.State
                            Case LoadState.Waiting
                                CardXAML += "<TextBlock Text=""…"" Tag=""Waiting"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Foreground=""{DynamicResource ColorBrush3}"" />"
                            Case LoadState.Loading
                                CardXAML += "<TextBlock Text=""" & Math.Floor(SubTask.Progress * 100) & "%"" Tag=""Loading"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Foreground=""{DynamicResource ColorBrush3}"" />"
                            Case LoadState.Finished
                                CardXAML += "<TextBlock Text=""√"" Tag=""Finished"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Foreground=""{DynamicResource ColorBrush3}"" />"
                            Case Else
                                CardXAML += "<TextBlock Text=""×"" Tag=""Failed"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Foreground=""{DynamicResource ColorBrush3}"" />"
                        End Select
                        CardXAML += "<TextBlock Text=""" & StringUtils.XmlEscape(SubTask.Name) & """ HorizontalAlignment=""Left"" Grid.Column=""1"" Grid.Row=""" & Row & """/>"
                        Row += 1
                    Next
                    CardXAML += "</Grid></local:MyCard>"
                    '实例化控件
                    Dim Card As MyCard
                    Try
                        Card = GetObjectFromXML(CardXAML)
                    Catch ex As Exception
                        Logger.Warn(ex, "新建任务管理卡片失败")
                        Logger.Info($"出错的卡片内容：{vbCrLf}{CardXAML}")
                        Throw
                    End Try
                    FrmSpeedRight.PanMain.Children.Insert(0, Card)
                    RightCards.Add(Loader.Name, Card)
                    Logger.Info($"新建任务管理卡片：{Loader.Name}")
                    '添加取消按钮
                    Dim Cancel As New MyIconButton With {.Name = "BtnCancel", .Logo = "F1 M2,0 L0,2 8,10 0,18 2,20 10,12 18,20 20,18 12,10 20,2 18,0 10,8 2,0Z", .Height = 20, .Margin = New Thickness(0, 10, 10, 0), .LogoScale = 1.1, .HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Top}
                    Card.Children.Add(Cancel)
                    AddHandler Cancel.Click,
                    Sub(sender As MyIconButton, e As EventArgs)
                        AniDispose(sender, False)
                        AniDispose(Card, True, Sub() If FrmSpeedRight.PanMain.Children.Count = 0 AndAlso FrmMain.PageCurrent = FormMain.PageType.TaskManager Then FrmMain.PageBack())
                        RightCards.Remove(Loader.Name)
                        LoaderTaskbar.Remove(Loader)
                        Logger.Info($"关闭任务管理卡片：{Loader.Name}，且移出任务列表")
                        RunInThread(Sub() Loader.Cancel())
                    End Sub
                    '如果已经失败，再刷新一次，修改成失败的控件
                    If Loader.State = LoadState.Failed Then
                        Card.Tag = Nothing '避免重复导致刷新无效
                        TaskRefresh(Loader)
                    End If
#End Region
                Catch ex As Exception
                    Logger.Error(ex, "添加任务管理卡片失败")
                End Try
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新任务管理显示失败")
        End Try
    End Sub
    Public Sub TaskRemove(Loader As Object)
        If RightCards.ContainsKey(Loader.Name) Then
            RunInUiWait(
            Sub()
                '移除已有的卡片
                Dim Card As Grid = RightCards(Loader.Name)
                FrmSpeedRight.PanMain.Children.Remove(Card)
                RightCards.Remove(Loader.Name)
                Logger.Info($"移除任务管理卡片：{Loader.Name}")
            End Sub)
        End If
    End Sub

    ''' <summary>
    ''' 若没有任务，尝试返回主页。
    ''' </summary>
    Private Sub TryReturnToHome()
        If FrmSpeedRight.PanMain.Children.Count = 0 AndAlso FrmMain.PageCurrent = FormMain.PageType.TaskManager Then
            FrmMain.PageBack()
        End If
    End Sub

End Class
