Imports System.Windows.Threading

Public Class PageLaunchRight
    Implements IRefreshable, IDispatcherUnhandledException

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        PanLog.Visibility = ModeDebug.ToVisibility
        '快照版提示
        PanHint.Visibility =
            (BuildType <> BuildTypes.Release AndAlso VersionBranchMain = "Official" AndAlso
             CurrentRank < DonationRank.Rank23 AndAlso Not Settings.Get(Of Boolean)("HintSnapshot")).ToVisibility
        RefreshSchematicViewerCard()
        LoadNotices()
        LoadHitokoto()
        RefreshWelcome()
        RefreshStats()
    End Sub

#Region "欢迎与统计"

    ''' <summary>
    ''' 按当前时间给出问候语。
    ''' </summary>
    Private Sub RefreshWelcome()
        Try
            If LabWelcomeTitle Is Nothing Then Return
            Dim Hour As Integer = Date.Now.Hour
            Dim Greeting As String
            If Hour < 5 Then
                Greeting = "夜深了"
            ElseIf Hour < 9 Then
                Greeting = "早上好"
            ElseIf Hour < 12 Then
                Greeting = "上午好"
            ElseIf Hour < 14 Then
                Greeting = "中午好"
            ElseIf Hour < 18 Then
                Greeting = "下午好"
            ElseIf Hour < 23 Then
                Greeting = "晚上好"
            Else
                Greeting = "夜深了"
            End If
            Dim Who As String = ""
            Try
                'McLoginLoader.Output 是结构体，不能用 ?.，这里用反射安全取值
                If McLoginLoader IsNot Nothing Then
                    Dim Output As Object = McLoginLoader.Output
                    If Output IsNot Nothing Then
                        Dim NameField = Output.GetType().GetField("Name")
                        If NameField IsNot Nothing Then
                            Dim LoginName As String = TryCast(NameField.GetValue(Output), String)
                            If Not String.IsNullOrWhiteSpace(LoginName) Then Who = $"，{LoginName}"
                        End If
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, "读取登录用户名失败")
            End Try
            LabWelcomeTitle.Text = $"{Greeting}{Who}"
            LabWelcomeSub.Text = $"Plain Craft Launcher by Slate_xd  ·  {VersionDisplay}"
        Catch ex As Exception
            Logger.Warn(ex, "刷新欢迎语失败")
        End Try
    End Sub

    ''' <summary>
    ''' 刷新顶部三块统计数据。
    ''' 版本列表是异步加载的，页面 Loaded 时可能还没就绪，
    ''' 因此这里轮询等待，拿到版本名后立即更新。
    ''' </summary>
    Private Sub RefreshStats()
        Try
            RunInThread(
            Sub()
                Dim VerName As String = "—"
                Dim FolderName As String = "—"
                '最多等 5 秒（25 次 × 200ms），版本列表就绪后立即退出
                For TryCount = 1 To 25
                    Try
                        If McInstanceSelected IsNot Nothing AndAlso
                           Not String.IsNullOrWhiteSpace(McInstanceSelected.Name) Then
                            VerName = McInstanceSelected.Name
                            Exit For
                        End If
                    Catch ex As Exception
                        '版本列表尚未初始化，继续等
                    End Try
                    Threading.Thread.Sleep(200)
                Next
                Try
                    Dim Loc As String = McFolderSelected
                    If Not String.IsNullOrWhiteSpace(Loc) Then
                        FolderName = IO.Path.GetFileName(Loc.TrimEnd("\"c, "/"c))
                        If String.IsNullOrWhiteSpace(FolderName) Then FolderName = Loc
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "读取游戏目录名失败")
                End Try
                RunInUi(
                Sub()
                    Try
                        If LabStatVersion IsNot Nothing Then LabStatVersion.Text = VerName
                        If LabStatFolder IsNot Nothing Then
                            LabStatFolder.Text = FolderName
                            LabStatFolder.ToolTip = McFolderSelected
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, "显示统计数据失败")
                    End Try
                End Sub)
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "刷新统计数据失败")
        End Try
    End Sub

#End Region

#Region "一言"

    ''' <summary>
    ''' 异步获取一言并显示。
    ''' </summary>
    Private Sub LoadHitokoto()
        Try
            If LabHitokoto Is Nothing Then Return
            RunInThread(
            Sub()
                Dim Result = ModHitokoto.GetHitokoto()
                RunInUi(
                Sub()
                    Try
                        LabHitokoto.Text = Result.Text
                        LabHitokotoFrom.Text = Result.From
                    Catch ex As Exception
                        Logger.Warn(ex, "显示一言失败")
                    End Try
                End Sub)
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "加载一言失败")
        End Try
    End Sub

#End Region

#Region "快捷工具"

    Private Sub BtnQuickJrrp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnQuickJrrp.Click
        Try
            PageOtherTest.Jrrp()
        Catch ex As Exception
            Logger.Error(ex, "今日人品执行失败")
            Hint($"执行失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    'SPT · 按钮加载：内存优化耗时不定，点击后按钮进入「进行中」并禁用，
    '避免重复提交；无论成功失败都在 Finally 恢复，失败可立即重试。
    Private Sub BtnQuickMemory_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnQuickMemory.Click
        If Not BtnQuickMemory.IsEnabled Then Return
        Dim OldText As String = BtnQuickMemory.Text
        BtnQuickMemory.IsEnabled = False
        BtnQuickMemory.Text = "优化中…"
        RunInThread(
        Sub()
            Try
                PageOtherTest.MemoryOptimize(True)
            Catch ex As Exception
                Logger.Error(ex, "内存优化执行失败")
                Hint($"执行失败：{ex.Message}", HintType.Red)
            Finally
                RunInUi(
                Sub()
                    BtnQuickMemory.Text = OldText
                    BtnQuickMemory.IsEnabled = True
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub BtnQuickFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnQuickFolder.Click
        Try
            Dim Loc As String = McFolderSelected
            If String.IsNullOrWhiteSpace(Loc) Then
                Hint("还没有选择游戏目录", HintType.Blue)
                Return
            End If
            If Not DirectoryUtils.Exists(Loc) Then
                Hint("游戏目录不存在，可能已被移动或删除", HintType.Red)
                Return
            End If
            StartProcess(Loc)
        Catch ex As Exception
            Logger.Error(ex, "打开游戏目录失败")
            Hint($"打开失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    Private Sub BtnQuickSettings_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnQuickSettings.Click
        Try
            FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupLaunch)
        Catch ex As Exception
            Logger.Error(ex, "跳转设置页面失败")
            Hint($"跳转失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 点击「今日人品」统计块直接查看人品。
    ''' </summary>
    Private Sub LabStatJrrp_Click(sender As Object, e As MouseButtonEventArgs) Handles LabStatJrrp.MouseLeftButtonUp
        Try
            PageOtherTest.Jrrp()
        Catch ex As Exception
            Logger.Error(ex, "今日人品执行失败")
        End Try
    End Sub

#End Region

#Region "投影预览卡片"

    ''' <summary>
    ''' 按当前安装状态刷新主页的投影预览卡片。已安装时隐藏卡片。
    ''' </summary>
    Private Sub RefreshSchematicViewerCard()
        Try
            If PanSchematicViewer Is Nothing Then Return
            RunInThread(
            Sub()
                Dim Installed As Boolean = ModSchematicPreview.IsSchematicViewerInstalled()
                RunInUi(
                Sub()
                    Try
                        '卡片常驻主页，不因安装与否而隐藏
                        PanSchematicViewer.Visibility = Visibility.Visible
                        If Installed Then
                            BtnSchematicViewerDownload.Visibility = Visibility.Collapsed
                            BtnSchematicViewerOpen.Visibility = Visibility.Visible
                            LabSchematicViewerDesc.Text =
                                "投影预览可以在游戏外查看 .litematic / .nbt / .schem 投影文件。" & vbCrLf &
                                "已安装完成，点「打开一个投影」选择文件即可。"
                        Else
                            BtnSchematicViewerDownload.Visibility = Visibility.Visible
                            BtnSchematicViewerOpen.Visibility = Visibility.Collapsed
                            LabSchematicViewerDesc.Text =
                                "投影预览可以在游戏外查看 .litematic / .nbt / .schem 投影文件。" & vbCrLf &
                                "首次使用需要先下载（约 394 MB），下载后会自动解压到启动器目录。"
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, "刷新投影预览卡片失败")
                    End Try
                End Sub)
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "刷新投影预览卡片失败")
        End Try
    End Sub

    'SPT · 加载反馈：下载投影预览是耗时且可能失败的操作。
    '原实现把「恢复按钮」写在成功路径里，一旦抛异常按钮就永久禁用，玩家只能重启启动器。
    '改为 Try/Finally 保证一定恢复；失败时给出可读原因并保留重试入口。
    Private Sub BtnSchematicViewerDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSchematicViewerDownload.Click
        If Not BtnSchematicViewerDownload.IsEnabled Then Return
        BtnSchematicViewerDownload.IsEnabled = False
        BarSchematicViewer.Visibility = Visibility.Visible
        BarSchematicViewer.Value = 0
        LabSchematicViewerState.Visibility = Visibility.Visible
        LabSchematicViewerState.Text = "准备中……"

        RunInThread(
        Sub()
            Dim Succeeded As Boolean = False
            Try
                ModSchematicPreview.DownloadSchematicViewer(
                    Sub(Ratio As Double) RunInUi(Sub() BarSchematicViewer.Value = Ratio),
                    Sub(State As String) RunInUi(Sub() LabSchematicViewerState.Text = State))
                Succeeded = True
            Catch ex As Exception
                Logger.Error(ex, "下载投影预览失败")
            Finally
                '无论成功失败都要恢复按钮，否则玩家只能重启
                RunInUi(
                Sub()
                    BtnSchematicViewerDownload.IsEnabled = True
                    If Succeeded Then
                        BarSchematicViewer.Visibility = Visibility.Collapsed
                        LabSchematicViewerState.Visibility = Visibility.Collapsed
                        Try
                            RefreshSchematicViewerCard()
                        Catch ex As Exception
                            Logger.Warn(ex, "刷新投影预览卡片失败")
                        End Try
                    Else
                        '失败：进度条归零并显示原因，按钮保持可点以便重试
                        BarSchematicViewer.Visibility = Visibility.Collapsed
                        BarSchematicViewer.Value = 0
                        LabSchematicViewerState.Visibility = Visibility.Visible
                        LabSchematicViewerState.Text = "下载失败，可点击「下载投影预览」重试"
                    End If
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub BtnSchematicViewerOpen_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSchematicViewerOpen.Click
        Try
            If Not ModSchematicPreview.IsSchematicViewerInstalled() Then
                MyMsgBox("还没有安装投影预览，请先点击「下载投影预览」。", "提示", "我知道了", IsWarn:=True)
                Return
            End If
            Dim Dialog As New Microsoft.Win32.OpenFileDialog With {
                .Title = "选择要预览的投影文件",
                .Filter = "投影文件 (*.litematic;*.nbt;*.schem)|*.litematic;*.nbt;*.schem|所有文件 (*.*)|*.*",
                .CheckFileExists = True
            }
            If Dialog.ShowDialog() = True Then
                '投影预览是独立的 .NET 程序，首次启动要加载大量资源，耗时较长
                Hint("正在启动投影预览，首次打开可能需要 10~30 秒，请耐心等待……", HintType.Blue)
                ModSchematicPreview.OpenSchematicPreview(Dialog.FileName)
            End If
        Catch ex As Exception
            Logger.Error(ex, "打开投影预览失败")
            Hint($"打开失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

#End Region

#Region "主页"

    ''' <summary>
    ''' 刷新主页。
    ''' </summary>
    Private Sub Refresh() Handles Me.Loaded
        RunInThread(
        Sub()
            Try
                SyncLock RefreshLock
                    RefreshReal()
                End SyncLock
            Catch ex As Exception
                Logger.Error(ex, "加载 PCL 主页自定义信息失败", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
            End Try
        End Sub)
    End Sub
    Private Sub RefreshReal()
        Dim Content As String = Nothing, Url As String = Nothing
        Select Case Settings.Get(Of Integer)("UiCustomType")
            Case 1
                '加载本地文件
                Logger.Info("主页自定义数据来源：本地文件")
                Content = FileUtils.TryReadAsString(Paths.Base & "PCL\Custom.xaml")
            Case 2
                '联网下载
                Url = Settings.Get(Of String)("UiCustomNet")
            Case 3
                '预设
                Select Case Settings.Get(Of Integer)("UiCustomPreset")
                    Case 0
                        Logger.Info("主页预设：你知道吗")
                        Content = "
                            <local:MyCard Title=""你知道吗？"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{hint}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 1
                        Logger.Info("主页预设：回声洞")
                        Content = "
                            <local:MyCard Title=""回声洞"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{cave}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 2
                        Logger.Info("主页预设：Minecraft 新闻")
                        Url = "https://mcnews.meloong.com"
                    Case 3
                        Logger.Info("主页预设：简单主页")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/MFn233/Custom.xaml"
                    Case 4
                        Logger.Info("主页预设：每日整合包推荐")
                        Url = "https://pclsub.sodamc.com/"
                    Case 5
                        Logger.Info("主页预设：Minecraft 皮肤推荐")
                        Url = "https://forgepixel.com/pcl_sub_file"
                    Case 6
                        Logger.Info("主页预设：OpenBMCLAPI 仪表盘 Lite")
                        Url = "https://pcl-bmcl.milu.ink/"
                    Case 7
                        Logger.Info("主页预设：主页市场")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/p/Homepages/Homepage.Market/Custom.xaml"
                    Case 8
                        Logger.Info("主页预设：更新日志")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Joker2184/UpdateHomepage.xaml"
                    Case 9
                        Logger.Info("主页预设：PCL 新功能说明书")
                        Url = "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml"
                    Case 10
                        Logger.Info("主页预设：OpenMCIM Dashboard")
                        Url = "https://files.mcimirror.top/PCL"
                    Case 11
                        Logger.Info("主页预设：杂志主页")
                        Url = "http://118.195.192.193:26995/d/magazine-homepage-pcl/Custom.xaml"
                    Case 12
                        Logger.Info("主页预设：PCL GitHub 仪表盘")
                        Url = "https://ddf.pcl-community.top/Custom.xaml"
                    Case 13
                        Logger.Info("主页预设：PCL 更新摘要")
                        Url = "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml"
                    Case 14
                        Logger.Info("主页预设：今日新闻热点")
                        Url = "https://pcl.wyc-w.top/index.xaml"
                    Case 15
                        Logger.Info("主页预设：Minecraft 芝士站")
                        Url = "https://www.xxag.top/mkss"
                    Case 16
                        Logger.Info("主页预设：整合包推荐引擎")
                        Url = "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml"
                    Case 17
                        Logger.Info("Bangumi 番剧主页")
                        Url = "https://bangumi.p.kaphia.qzz.io"
                    Case 18
                        Logger.Info("Bilibili 热门")
                        Url = "https://bilibili.p.kaphia.qzz.io"
                    Case 19
                        Logger.Info("Music 云音乐热门")
                        Url = "https://cloudmusic.p.kaphia.qzz.io"
                End Select
        End Select
        '联网下载
        If Not String.IsNullOrWhiteSpace(Url) Then
            If Url = Settings.Get(Of String)("CacheSavedPageUrl") AndAlso FileUtils.Exists(PathTemp & "Cache\Custom.xaml") Then
                '缓存可用
                Logger.Info("主页自定义数据来源：联网缓存文件")
                Content = FileUtils.TryReadAsString(PathTemp & "Cache\Custom.xaml")
                '后台更新缓存
                OnlineLoader.Start((Url, False))
            Else
                '缓存不可用
                Logger.Info("主页自定义数据来源：联网全新下载")
                Hint("正在加载主页……")
                RunInUiWait(Sub() LoadContent(Nothing)) '在加载结束前清空页面
                Settings.Set("CacheSavedPageVersion", "")
                OnlineLoader.Start((Url, True)) '下载完成后将会再次触发更新
                Return
            End If
        End If
        '同步到 UI
        RunInUi(Sub() LoadContent(Content))
    End Sub
    Private RefreshLock As New Object

    '联网获取主页文件
    Private OnlineLoader As New LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer)("下载主页", AddressOf OnlineLoaderSub) With {.ReloadTimeout = 10 * 60 * 1000}
    Private Sub OnlineLoaderSub(Task As LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer))
        Dim Address As String = Task.Input.Address '#3721 中连续触发两次导致内容变化
        Dim ShouldRefresh As Boolean = Task.Input.ShouldRefresh
        Try
            '替换自定义变量与设置
            Address = ArgumentReplace(Address, AddressOf WebUtility.HtmlEncode)
            '获取版本校验地址
            Dim VersionAddress As String
            If Address.Contains(".xaml") Then
                VersionAddress = Address.Replace(".xaml", ".xaml.ini")
            Else
                VersionAddress = Address.BeforeFirst("?")
                If Not VersionAddress.EndsWithF("/") Then VersionAddress += "/"
                VersionAddress += "version"
                If Address.Contains("?") Then VersionAddress += "?" & Address.AfterFirst("?")
            End If
            '校验版本
            Dim Version As String = ""
            Try
                Version = NetRequestByClientRetry(VersionAddress)
                If Version.Length > 1000 Then Throw New Exception($"获取的主页版本过长（{Version.Length} 字符）")
                Dim CurrentVersion As String = Settings.Get(Of String)("CacheSavedPageVersion")
                If Version <> "" AndAlso CurrentVersion <> "" AndAlso Version = CurrentVersion Then
                    Logger.Info($"当前缓存的主页已为最新，当前版本：{Version}，检查源：{VersionAddress}")
                    Return
                End If
                Logger.Info($"需要下载联网主页，当前版本：{Version}，检查源：{VersionAddress}")
            Catch exx As Exception
                Logger.Warn(exx, $"联网获取主页版本失败")
                Logger.Info($"无法检查联网主页版本，将直接下载，检查源：{VersionAddress}")
            End Try
            '实际下载
            Dim FileContent As String = NetRequestByClientRetry(Address)
            Logger.Info($"已联网下载主页，内容长度：{FileContent.Length}，来源：{Address}")
            Settings.Set("CacheSavedPageUrl", Address)
            Settings.Set("CacheSavedPageVersion", Version)
            FileUtils.Write(PathTemp & "Cache\Custom.xaml", FileContent)
            '若内容变更则要求刷新
            If LoadedContentHash <> FileContent.GetStableHashCode() AndAlso ShouldRefresh Then Refresh()
        Catch ex As Exception
            Logger.Error(ex, $"下载主页失败（{Address}）", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
        End Try
    End Sub

    ''' <summary>
    ''' 立即强制刷新主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Sub ForceRefresh() Implements IRefreshable.Refresh
        Logger.Info("要求强制刷新主页")
        ClearCache()
        '实际的刷新
        If FrmMain.PageCurrent.Page = FormMain.PageType.Launch Then
            PanBack.ScrollToHome()
            Refresh()
        Else
            FrmMain.PageChange(FormMain.PageType.Launch)
        End If
    End Sub

    ''' <summary>
    ''' 清空主页缓存信息。
    ''' </summary>
    Private Sub ClearCache()
        LoadedContentHash = Nothing
        OnlineLoader.Input = ("", True)
        Settings.Set("CacheSavedPageUrl", "")
        Settings.Set("CacheSavedPageVersion", "")
        Logger.Info("已清空主页缓存")
    End Sub

    ''' <summary>
    ''' 从文本内容中加载主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Private Sub LoadContent(Content As String)
        Try
            SyncLock LoadContentLock
                '如果加载目标内容一致则不加载
                Dim Hash = If(Content, "").GetStableHashCode()
                If Hash = LoadedContentHash Then Return
                LoadedContentHash = Hash
                '实际加载内容
                PanCustom.Children.Clear()
                If String.IsNullOrWhiteSpace(Content) Then
                    Logger.Info($"实例化：清空主页 UI，来源为空")
                    Return
                End If
                Dim LoadStartTime As Date = Date.Now
                '修改时应同时修改 PageOtherHelpDetail.Init
                Content = ArgumentReplace(Content, AddressOf StringUtils.XmlEscape)
                Do While Content.Contains("xmlns")
                    Content = Content.RegexReplace("xmlns[^""']*(""|')[^""']*(""|')", "").Replace("xmlns", "") '禁止声明命名空间
                Loop
                Content = "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:sys=""clr-namespace:System;assembly=mscorlib"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher by Slate_xd"" xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" & Content & "</StackPanel>"
                Logger.Info($"实例化：加载主页 UI 开始，最终内容长度：{Content.Count}")
                PanCustom.Children.Add(GetObjectFromXML(Content))
                '加载计时
                Dim LoadCostTime = (Date.Now - LoadStartTime).Milliseconds
                Logger.Info($"实例化：加载主页 UI 完成，耗时 {LoadCostTime}ms")
                If LoadCostTime > 3000 Then Hint($"主页加载过于缓慢（花费了 {Math.Round(LoadCostTime / 1000, 1)} 秒），请向主页作者反馈此问题，或暂时停止使用该主页")
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, $"加载失败的主页内容：{vbCrLf}{Content}")
            OnLoadContentFailed(ex)
        End Try
    End Sub
    ''' <summary>
    ''' 加载主页失败时调用。
    ''' </summary>
    Private Sub OnLoadContentFailed(ex As Exception)
        If ModeDebug OrElse Settings.Get(Of Integer)("UiCustomType") = 1 Then
            Logger.Warn(ex, "加载主页失败")
            If MyMsgBox(If(TypeOf ex Is UnauthorizedAccessException, ex.Message, $"主页内容编写有误，请根据下列错误信息进行检查：{vbCrLf}{ex.GetDisplay(False)}"),
                        "加载主页失败", "重试", "取消") = 1 Then ForceRefresh()
        Else
            Logger.Error(ex, "加载主页失败", LogBehavior.Toast)
        End If
    End Sub
    ''' <summary>
    ''' 捕获主页在 Measure 和 Arrange 阶段抛出的异常。
    ''' </summary>
    Private Sub DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Implements IDispatcherUnhandledException.DispatcherUnhandledException
        If TypeOf e.Exception IsNot Markup.XamlParseException Then Return
        e.Handled = True
        LoadContent(Nothing)
        OnLoadContentFailed(e.Exception)
    End Sub

    Private LoadedContentHash As ULong? = Nothing
    Private LoadContentLock As New Object

#End Region


#Region "公告"

    ''' <summary>
    ''' 从服务端拉取公告并渲染到主页。无公告时隐藏卡片；加载失败时显示可重试提示。
    ''' </summary>
    Private Sub LoadNotices()
        Try
            If PanNotice Is Nothing Then Return
            RunInThread(
            Sub()
                Try
                    Dim List As List(Of ModUpdate.UpdateNotice) = ModUpdate.FetchNoticesForHome()
                    RunInUi(
                    Sub()
                        Try
                            RenderNotices(List)
                        Catch ex As Exception
                            Logger.Warn(ex, "渲染公告失败")
                        End Try
                    End Sub)
                Catch ex As Exception
                    Logger.Warn(ex, "加载公告失败")
                    'SPT · 任务反馈：区分「没有公告」与「加载失败」，失败给可重试入口，
                    '不再静默隐藏让玩家以为本来就没有公告。
                    RunInUi(Sub() RenderNoticesFailed())
                End Try
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "刷新公告失败")
        End Try
    End Sub

    ''' <summary>
    ''' SPT · 公告加载失败时的内联提示：说明状态并提供重试，不静默隐藏。
    ''' </summary>
    Private Sub RenderNoticesFailed()
        Try
            If PanNotice Is Nothing OrElse PanNoticeList Is Nothing Then Return
            PanNoticeList.Children.Clear()
            PanNotice.Visibility = Visibility.Visible

            Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal}
            Dim Msg As New TextBlock With {
                .Text = "公告加载失败，可能是网络不通。",
                .FontSize = 13,
                .VerticalAlignment = VerticalAlignment.Center,
                .Foreground = New SolidColorBrush(Color.FromArgb(170, 128, 128, 128))
            }
            Dim Retry As New TextBlock With {
                .Text = "重试",
                .FontSize = 13,
                .Margin = New Thickness(10, 0, 0, 0),
                .VerticalAlignment = VerticalAlignment.Center,
                .Cursor = Cursors.Hand,
                .Foreground = New SolidColorBrush(Color.FromRgb(47, 123, 255)),
                .TextDecorations = TextDecorations.Underline
            }
            AddHandler Retry.MouseLeftButtonUp,
                Sub(sender As Object, e As MouseButtonEventArgs)
                    e.Handled = True
                    Retry.Text = "重试中…"
                    Retry.IsEnabled = False
                    LoadNotices()
                End Sub
            Row.Children.Add(Msg)
            Row.Children.Add(Retry)
            PanNoticeList.Children.Add(Row)
        Catch ex As Exception
            Logger.Warn(ex, "渲染公告失败提示时出错")
        End Try
    End Sub

    ''' <summary>
    ''' 把公告列表渲染成文字块。没有公告时整张卡片隐藏。
    ''' </summary>
    Private Sub RenderNotices(List As List(Of ModUpdate.UpdateNotice))
        Try
            If PanNotice Is Nothing OrElse PanNoticeList Is Nothing Then Return
            PanNoticeList.Children.Clear()

            If List Is Nothing OrElse List.Count = 0 Then
                PanNotice.Visibility = Visibility.Collapsed
                Return
            End If

            Dim First As Boolean = True
            For Each Item In List
                If Not First Then
                    '分隔线
                    PanNoticeList.Children.Add(New Border With {
                        .Height = 1,
                        .Margin = New Thickness(0, 12, 0, 12),
                        .Background = New SolidColorBrush(Color.FromArgb(30, 128, 128, 128))
                    })
                End If
                First = False

                '标题行
                Dim Head As New TextBlock With {
                    .Text = Item.Title,
                    .FontWeight = FontWeights.SemiBold,
                    .FontSize = 14,
                    .TextWrapping = TextWrapping.Wrap
                }
                PanNoticeList.Children.Add(Head)

                '正文
                If Not String.IsNullOrWhiteSpace(Item.Content) Then
                    PanNoticeList.Children.Add(New TextBlock With {
                        .Text = Item.Content,
                        .Margin = New Thickness(0, 6, 0, 0),
                        .TextWrapping = TextWrapping.Wrap,
                        .Opacity = 0.85,
                        .LineHeight = 20
                    })
                End If
            Next

            PanNotice.Visibility = Visibility.Visible
            Logger.Info($"主页已显示 {List.Count} 条公告")
        Catch ex As Exception
            Logger.Warn(ex, "渲染公告失败")
        End Try
    End Sub

#End Region

End Class
