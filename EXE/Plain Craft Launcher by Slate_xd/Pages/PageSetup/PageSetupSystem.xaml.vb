Public Class PageSetupSystem


    Private Sub PageSetupSystem_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        If BuildType = BuildTypes.Release Then
            PanDonate.Visibility = Visibility.Collapsed
        Else
            PanDonate.Visibility = Visibility.Visible
            ItemSystemUpdateDownload.Content = "在有新版本时自动下载（更新快照版前需要先输入土豆码）"
        End If

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True

        AniControlEnabled += 1
        Reload()
        SliderLoad()
        RefreshSchematicViewerState()
        AniControlEnabled -= 1

    End Sub
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
    End Sub
    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化启动器页设置")
            Hint("已初始化启动器页设置！", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化启动器页设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    '滑动条
    Private Sub SliderLoad()
        SliderDownloadThread.GetHintText = Function(v) v + 1
        SliderDownloadSpeed.GetHintText =
        Function(v)
            Select Case v
                Case Is <= 14
                    Return (v + 1) * 0.1 & " M/s"
                Case Is <= 31
                    Return (v - 11) * 0.5 & " M/s"
                Case Is <= 41
                    Return (v - 21) & " M/s"
                Case Else
                    Return "无限制"
            End Select
        End Function
        SliderDebugAnim.GetHintText = Function(v) If(v > 29, "关闭", (v / 10 + 0.1) & "x")
    End Sub
    Private Sub SliderDownloadThread_PreviewChange(sender As Object, e As RouteEventArgs) Handles SliderDownloadThread.PreviewChange
        If SliderDownloadThread.Value < 100 Then Return
        If Not Settings.Get(Of Boolean)("HintDownloadThread") Then
            Settings.Set("HintDownloadThread", True)
            MyMsgBox("如果设置过多的下载线程，可能会导致下载时出现非常严重的卡顿。" & vbCrLf &
                     "一般设置 64 线程即可满足大多数下载需求，除非你知道你在干什么，否则不建议设置更多的线程数！", "警告", "我知道了", IsWarn:=True)
        End If
    End Sub

    '识别码/土豆码替代入口
    Private Sub BtnSystemIdentify_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemIdentify.Click
        PageOtherAbout.CopyIdentify()
    End Sub
    Private Sub BtnSystemUnlock_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemUnlock.Click
        InputPotatoCode(False)
    End Sub

    '调试模式
    Private Sub CheckDebugMode_Change() Handles CheckDebugMode.Change
        If AniControlEnabled = 0 Then Hint("部分调试信息将在刷新或启动器重启后切换显示！",, False)
    End Sub

    '自动更新
    Private Sub ComboSystemActivity_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSystemActivity.SelectionChanged
        If AniControlEnabled <> 0 Then Return
        If ComboSystemActivity.SelectedIndex <> 2 Then Return
        If MyMsgBox("若选择此项，即使在将来出现严重问题时，你也无法获取相关通知。" & vbCrLf &
                    "例如，如果发现某个版本游戏存在严重 Bug，你可能就会因为无法得到通知而导致无法预知的后果。" & vbCrLf & vbCrLf &
                    "一般选择 仅在有重要通知时显示公告 就可以让你尽量不受打扰了。" & vbCrLf &
                    "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn:=True) = 2 Then
            If e.RemovedItems.Count > 0 Then ComboSystemActivity.SelectedItem = e.RemovedItems(0)
        End If
    End Sub
    Private Sub ComboSystemUpdate_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSystemUpdate.SelectionChanged
        If AniControlEnabled <> 0 Then Return
        If ComboSystemUpdate.SelectedIndex <> 3 Then Return
        If MyMsgBox("若选择此项，即使在启动器将来出现严重问题时，你也无法获取更新并获得修复。" & vbCrLf &
                    "例如，如果官方修改了登录方式，从而导致现有启动器无法登录，你可能就会因为无法更新而无法开始游戏。" & vbCrLf & vbCrLf &
                    "一般选择 仅在有重大漏洞更新时显示提示 就可以让你尽量不受打扰了。" & vbCrLf &
                    "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn:=True) = 2 Then
            If e.RemovedItems.Count > 0 Then ComboSystemUpdate.SelectedItem = e.RemovedItems(0)
        End If
    End Sub
    Private Sub BtnSystemUpdate_Click(sender As Object, e As EventArgs) Handles BtnSystemUpdate.Click
        UpdateCheckByButton()
    End Sub
    ''' <summary>
    ''' 启动器是否已经是最新版？
    ''' 若返回 Nothing，则代表无更新缓存文件或出错。
    ''' </summary>
    Public Shared Function IsLauncherNewest() As Boolean?
        Try
            '确认服务器公告是否正常
            Dim ServerContent As String = If(FileUtils.TryReadAsString(PathTemp & "Cache\Notice.cfg"), "")
            If ServerContent.Split("|").Count < 3 Then Return Nothing
            '确认是否为最新
            Return ServerContent.Split("|")(If(BuildType = BuildTypes.Release, 2, 1)) <= VersionCode
        Catch ex As Exception
            Logger.Error(ex, "确认启动器更新失败")
            Return Nothing
        End Try
    End Function

#Region "导出 / 导入设置"

    Private Sub BtnSystemSettingExp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingExp.Click
        Hint("该功能尚在开发中！")
    End Sub
    Private Sub BtnSystemSettingImp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingImp.Click
        Hint("该功能尚在开发中！")
    End Sub

#End Region

#Region "从 PCL 导入登录"

    Private Sub BtnImportPclLogin_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnImportPclLogin.Click
        Try
            ModPclImport.ImportPclMicrosoftLoginManual()
        Catch ex As Exception
            Logger.Error(ex, "从 PCL 导入登录失败")
            Hint("导入失败：" & ex.Message, HintType.Red)
        End Try
    End Sub
    Private Sub BtnImportPclLoginHelp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnImportPclLoginHelp.Click
        MyMsgBox(
            "从 PCL 导入登录信息" & vbCrLf & vbCrLf &
            "PCL 2.x 会把微软账号的登录凭据加密后保存在注册表中。" & vbCrLf &
            "本功能会读取并解密这些数据，直接接管已有的登录状态，无需重新走一遍微软登录流程。" & vbCrLf & vbCrLf &
            "查找位置：" & vbCrLf &
            "· HKEY_CURRENT_USER\Software\PCL" & vbCrLf &
            "· HKEY_CURRENT_USER\Software\PCLDebug（开源版使用）" & vbCrLf & vbCrLf &
            "注意：如果 PCL 那边已经退出登录或清除过数据，这里也无法导入。",
            "说明", "我知道了")
    End Sub

#End Region
#Region "投影预览"

    Private Sub BtnSchematicViewerSelect_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSchematicViewerSelect.Click
        ModSchematicPreview.SelectSchematicViewer()
        RefreshSchematicViewerState()
    End Sub

    Private Sub BtnSchematicViewerTest_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSchematicViewerTest.Click
        Try
            Dim Dialog As New Microsoft.Win32.OpenFileDialog With {
                .Title = "选择要预览的投影文件",
                .Filter = "投影文件 (*.litematic;*.nbt;*.schem)|*.litematic;*.nbt;*.schem|所有文件 (*.*)|*.*",
                .CheckFileExists = True
            }
            If Dialog.ShowDialog() = True Then ModSchematicPreview.OpenSchematicPreview(Dialog.FileName)
        Catch ex As Exception
            Logger.Error(ex, "打开投影预览失败")
            Hint($"打开失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 刷新投影预览程序的状态显示。
    ''' </summary>
    Public Sub RefreshSchematicViewerState()
        Try
            If LabSchematicViewerState Is Nothing Then Return
            Dim Viewer As String = ModSchematicPreview.FindSchematicViewer(ForceRefresh:=True)
            If Viewer = "" Then
                LabSchematicViewerState.Text = "未找到投影预览程序。请点击「指定程序位置」，或把解压后的投影预览文件夹放到本启动器目录下。"
            Else
                LabSchematicViewerState.Text = $"已找到：{Viewer}"
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新投影预览状态失败")
        End Try
    End Sub

#End Region

End Class