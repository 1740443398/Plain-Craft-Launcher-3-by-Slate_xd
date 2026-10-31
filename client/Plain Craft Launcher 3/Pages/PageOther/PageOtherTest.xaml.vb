Public Class PageOtherTest

    Public Sub New()
        InitializeComponent()
        '恢复被开源删减的事件绑定（原先保存在 BAML 中，反编译时丢失）
        AddHandler BtnJrrp.Click, Sub(sender As Object, e As MouseButtonEventArgs) Jrrp()
        AddHandler BtnMemory.Click, Sub(sender As Object, e As MouseButtonEventArgs) ModBase.RunInThread(Sub() MemoryOptimize(True))
        AddHandler BtnClear.Click, Sub(sender As Object, e As MouseButtonEventArgs) RubbishClear()
        AddHandler BtnCave.Click, Sub(sender As Object, e As EventArgs) SubmitCaveDialog()
        AddHandler BtnDownloadSelect.Click, Sub(sender As Object, e As EventArgs) DownloadSelect()
        AddHandler BtnDownloadStart.Click, Sub(sender As Object, e As MouseButtonEventArgs) DownloadStart()
        AddHandler BtnDownloadOpen.Click, Sub(sender As Object, e As MouseButtonEventArgs) ModBase.StartProcess(TextDownloadFolder.Text)
        AddHandler Loaded, Sub(sender As Object, e As RoutedEventArgs) PageLoaded()
        AddHandler CardCave.PreviewMouseLeftButtonUp, AddressOf CardCave_Click
        AddHandler BtnClick.Click, AddressOf BtnClickHandler
        AddHandler LabCave.MouseLeftButtonUp, AddressOf CardCave_Click
        AddHandler BtnCave.Click, Sub(sender As Object, e As EventArgs) SubmitCaveDialog()
        '恢复「开始下载」按钮的可用性联动：
        '原先这个逻辑保存在 BAML 里，反编译时丢失，导致按钮永远是禁用状态、点不了。
        AddHandler TextDownloadUrl.ValidateChanged, AddressOf DownloadInputChanged
        AddHandler TextDownloadFolder.ValidateChanged, AddressOf DownloadInputChanged
        AddHandler TextDownloadName.ValidateChanged, AddressOf DownloadInputChanged
        '地址校验通过后自动推断文件名
        AddHandler TextDownloadUrl.ValidateChanged, Sub() AutoFillFileName()
    End Sub

    Private Sub PageLoaded()
        TextDownloadFolder.Text = Settings.Get(Of String)("CacheDownloadFolder")
        If String.IsNullOrWhiteSpace(TextDownloadFolder.Text) Then TextDownloadFolder.Text = Paths.Base & "PCL\MyDownload\"
        CaveHand()
    End Sub

    Private Shared IsCaveEnabled As Boolean = True

    Private Sub CaveHand()
        LabCave.Text = "反复点击这里可以查看 PCL 作者与各位沙雕网友乱七八糟的留言！"
    End Sub

    Private Sub BtnClickHandler(sender As Object, e As MouseButtonEventArgs)
        Try
            '构造彩蛋索引列表
            Dim Effects As New List(Of Integer) From {ModBase.RandomInteger(0, 7)}
            If ModBase.RandomInteger(0, 1) = 1 Then Effects.Add(ModBase.RandomInteger(1, 6))
            If Date.Now.Month = 4 AndAlso Date.Now.Day = 1 Then Effects = New List(Of Integer) From {7}

            If Effects.Contains(1) Then
                If ModMain.MyMsgBox("当暴露在点击确定后的场景时，有极小部分人群会引发癫痫。这种情形可能是由于某些未查出的癫痫症状引起，即使该人员并没有患癫痫病史也有可能造成此类病症。如果您的家人或任何家庭成员曾有过类似症状，请在点击确定前咨询您的医生或医师。如果您在稍后出现任何症状，包括头晕、目眩、眼部或肌肉抽搐、失去意识、失去方向感、抽搐或出现任何自己无法控制的动作，请立即关闭 PCL 并咨询您的医生或医师。" & vbCrLf & "这是最后的警告，是否继续操作？", "警告", "确定", "取消", "", IsWarn:=True) = 2 Then Return
            Else
                ModMain.MyMsgBox("PCL 作者不会受理由于点击千万别点造成的任何 Bug。" & vbCrLf & "这是最后的警告，是否继续操作？", "警告", "确定", "确定", "确定", IsWarn:=True)
            End If

            For Each Idx As Integer In Effects
                Select Case Idx
                    Case 0
                        '无效果
                    Case 1
                        ModSecret.ThemeDontClick = 1
                        ModSecret.ThemeRefresh()
                    Case 2
                        '翻转界面
                        If ModMain.FrmMain.PanBack IsNot Nothing Then ModMain.FrmMain.PanBack.RenderTransformOrigin = New Point(1.25R, 1.25R)
                        Select Case ModBase.RandomInteger(0, 2)
                            Case 1 : ModMain.FrmMain.PanBack.RenderTransform = New ScaleTransform(-1.0R, -1.0R)
                            Case 2 : ModMain.FrmMain.PanBack.RenderTransform = New ScaleTransform(-1.0R, 1.0R)
                        End Select
                    Case 3
                        '放大界面
                        ModMain.FrmMain.IsSizeSaveable = False
                        ModMain.FrmMain.PanBack.LayoutTransform = New ScaleTransform(2.5R, 2.5R)
                        ModMain.FrmMain.Height = ModMain.FrmMain.ActualHeight
                        ModMain.FrmMain.Width = ModMain.FrmMain.ActualWidth
                        ModMain.FrmMain.Left = 0
                        ModMain.FrmMain.Top = 0
                        ModAnimation.AniStop("Don't Click Scale")
                    Case 4
                        '缩小界面
                        ModMain.FrmMain.IsSizeSaveable = False
                        ModMain.FrmMain.RenderTransform = New ScaleTransform()
                        ModAnimation.AniStart(ModAnimation.AaScaleTransform(ModMain.FrmMain, -0.85, 5000), "Don't Click Scale")
                        ModAnimation.AniStop("Don't Click Rotate")
                    Case 5
                        '旋转界面
                        ModMain.FrmMain.RenderTransform = New RotateTransform()
                        ModAnimation.AniStart(ModAnimation.AaRotateTransform(ModMain.FrmMain, 1000000 * ModBase.RandomOne(New Integer() {1, -1}), 10000000), "Don't Click Rotate")
                        ModAnimation.AniStop("Don't Click Scale")
                    Case 6
                        '窗口乱飞
                        ModMain.FrmMain.IsSizeSaveable = False
                        If ModBase.RandomInteger(0, 1) <> 0 Then
                            ModAnimation.AniStart(ModAnimation.AaX(ModMain.FrmMain, 10000000 * ModBase.RandomOne(New Integer() {1, -1}), 30000000), "Don't Click Move X")
                        Else
                            ModAnimation.AniStart(ModAnimation.AaY(ModMain.FrmMain, 10000000 * ModBase.RandomOne(New Integer() {1, -1}), 50000000), "Don't Click Move Y")
                        End If
                    Case 7
                        'B 站视频
                        ModBase.OpenWebsite("https://www.bilibili.com/video/BV1GJ411x7h7")
                End Select
            Next
        Catch ex As Exception
            Logger.Error(ex, "千万别点执行失败")
        End Try
    End Sub

    Private Sub CardCave_Click(sender As Object, e As MouseButtonEventArgs)
        If Not IsCaveEnabled Then Return
        IsCaveEnabled = False
        '1. 取新留言
        Dim NewCave As String = GetRandomCave()
        '2. 保存旧文字
        Dim OldText As String = LabCave.Text
        '3. 先把新文字写进控件，让 AaTextAppear 能捕获到它
        '   （AaTextAppear 在「构造时」就读取 Obj.Text 存进 Value，见 ModAnimation.vb:436）
        LabCave.Text = NewCave
        '4. 构造逐字显现动画（此刻捕获的是新文字）
        Dim AppearAni As ModAnimation.AniData =
            ModAnimation.AaTextAppear(LabCave, Hide:=False, TimePerText:=True, Time:=60, Delay:=400)
        '5. 构造完整闪烁序列：透明度反复抖动，模拟显像管效果
        ModAnimation.AniStart(New List(Of ModAnimation.AniData) From {
            ModAnimation.AaOpacity(LabCave, -1.0, 5),
            ModAnimation.AaOpacity(LabCave, 1.0, 5, 30),
            ModAnimation.AaOpacity(LabCave, -1.0, 5, 70),
            ModAnimation.AaOpacity(LabCave, 0.9, 5, 85),
            ModAnimation.AaOpacity(LabCave, -1.0, 5, 125),
            ModAnimation.AaOpacity(LabCave, 0.8, 5, 145),
            ModAnimation.AaOpacity(LabCave, -1.0, 5, 165),
            ModAnimation.AaOpacity(LabCave, 0.7, 5, 195),
            ModAnimation.AaOpacity(LabCave, -1.0, 5, 210),
            ModAnimation.AaOpacity(LabCave, 0.5, 5, 235),
            ModAnimation.AaOpacity(LabCave, -1.0, 5, 250),
            ModAnimation.AaOpacity(LabCave, 1.0, 1, 400, Nothing, After:=True),
            AppearAni,
            ModAnimation.AaCode(Sub() IsCaveEnabled = True, 0, After:=True)
        }, "Cave")
        '6. 立刻把控件文字改回旧的。
        '   此时透明度已被动画压到 0，视觉上看不到；
        '   等 AaTextAppear 执行时，会把捕获到的新文字逐字写进控件。
        '   少了这一步就会「先看到完整新文字，再播动画」，效果不对。
        LabCave.Text = OldText
    End Sub

    Private Sub DownloadSelect()
        Dim Target As String = Dialogs.SelectFolder("选择目标文件夹", False).FirstOrDefault()
        If Target IsNot Nothing Then TextDownloadFolder.Text = Target
    End Sub

    ''' <summary>
    ''' 三个输入框的校验状态变化时，刷新「开始下载」与「打开文件夹」按钮的可用性。
    ''' </summary>
    Private Sub DownloadInputChanged(sender As Object, e As EventArgs)
        Try
            If BtnDownloadStart Is Nothing Then Return
            Dim UrlOk As Boolean = TextDownloadUrl.IsValidated AndAlso
                                   Not String.IsNullOrWhiteSpace(TextDownloadUrl.Text)
            Dim NameOk As Boolean = TextDownloadName.IsValidated AndAlso
                                    Not String.IsNullOrWhiteSpace(TextDownloadName.Text)
            Dim FolderOk As Boolean = TextDownloadFolder.IsValidated
            BtnDownloadStart.IsEnabled = UrlOk AndAlso NameOk AndAlso FolderOk
            BtnDownloadOpen.IsEnabled = Not String.IsNullOrWhiteSpace(TextDownloadFolder.Text)
        Catch ex As Exception
            Logger.Warn(ex, "刷新自定义下载按钮状态失败")
        End Try
    End Sub

    ''' <summary>
    ''' 从下载地址自动推断文件名，填入文件名输入框。
    ''' </summary>
    Private Sub AutoFillFileName()
        Try
            If TextDownloadName Is Nothing Then Return
            If Not String.IsNullOrWhiteSpace(TextDownloadName.Text) Then Return
            Dim Url As String = If(TextDownloadUrl.Text, "").Trim()
            If Url = "" Then Return
            Dim PathPart As String = Url.Split("?"c)(0).Split("#"c)(0)
            Dim Name As String = PathPart.Substring(PathPart.LastIndexOf("/"c) + 1)
            If Name = "" Then Return
            Name = Uri.UnescapeDataString(Name)
            If Name.Contains(".") Then
                TextDownloadName.Text = Name
                TextDownloadName.Validate()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "自动推断文件名失败")
        End Try
    End Sub

    Private Sub DownloadStart()
        StartCustomDownload(TextDownloadUrl.Text, TextDownloadName.Text, TextDownloadFolder.Text)
        TextDownloadUrl.Text = ""
        TextDownloadUrl.Validate()
        TextDownloadUrl.ForceShowAsSuccess()
        TextDownloadName.Text = ""
        TextDownloadName.Validate()
        TextDownloadName.ForceShowAsSuccess()
    End Sub

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Try
            If String.IsNullOrWhiteSpace(Folder) Then
                Folder = Dialogs.SaveFile("选择文件保存位置", FileName)
                If Folder Is Nothing Then Return
                If Folder.EndsWithF(FileName) Then Folder = Folder.Substring(0, Folder.Length - FileName.Length)
            End If
            Folder = Folder.Replace("/", "\").TrimEnd("\"c) & "\"
            Try
                DirectoryUtils.Create(Folder)
                ModBase.CheckPermissionWithException(Folder)
            Catch ex As Exception
                Logger.Error(ex, $"访问文件夹失败（{Folder}）", LogBehavior.Toast)
                Return
            End Try
            Logger.Info($"自定义下载文件名：{FileName}")
            Logger.Info($"自定义下载文件目标：{Folder}")
            'GitHub 链接自动走加速镜像，其余链接原样下载
            Dim Urls As String() = ModGithubMirror.GetDownloadUrls(Url, EnableMirror:=ModGithubMirror.EnableMirror)
            If Urls.Length > 1 Then
                Logger.Info($"检测到 GitHub 链接，已生成 {Urls.Length - 1} 个加速候选")
                ModMain.Hint("已启用 GitHub 加速下载", ModMain.HintType.Green)
            End If
            Dim Uuid As Integer = ModBase.GetUuid()
            Dim Downloader As New ModNet.LoaderDownload("自定义下载文件：" & FileName & " ",
                {New ModNet.NetFile(Urls, Folder & FileName, Nothing, SimulateBrowserHeaders:=True)})
            Dim Combo As New ModLoader.LoaderCombo(Of Integer)("自定义下载 (" & Uuid.ToString() & ") ", {Downloader})
            Combo.OnStateChanged = AddressOf ModDownloadLib.LoaderStateChangedHintOnly
            Combo.Start()
            ModLoader.LoaderTaskbarAdd(Combo)
            ModMain.FrmMain.BtnExtraDownload.ShowRefresh()
            ModMain.FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始自定义下载失败", LogBehavior.AlertThenFeedback)
        End Try
    End Sub
    Public Shared Sub Jrrp()
        Dim SeedA As ULong = ("asdfgbn" & Date.Now.DayOfYear.ToString() & "12#3$45" & Date.Now.Year.ToString() & "IUY").GetStableHashCode()
        Dim SeedB As ULong = ("QWERTY" & ModBase.Identify & "0*8&6" & Date.Now.Day.ToString() & "kjhg").GetStableHashCode()
        Dim Raw As Integer = CInt(Math.Round(Math.Abs((CDbl(SeedA) / 3.0 + CDbl(SeedB) / 3.0) / 527.0) Mod 1001.0))
        'PCL3 调整：原版把 Raw(0~1000) 均匀映射到 0~99，平均只有 51 分，
        '近六成情况低于 60 分，容易影响心情。
        '改为分档映射：绝大多数落在 80 分以上，分数越低越罕见，但 0 分仍然可达。
        '  80~100 分：约 92%（其中 100 分约 1.2%）
        '  60~ 79 分：约  5%
        '  30~ 59 分：约  2%
        '  10~ 29 分：约 0.7%
        '   0~  9 分：约 0.4%
        Dim Score As Integer
        If Raw <= 919 Then
            '92%：80~100，平方曲线让高分更稀有
            Dim T As Double = CDbl(Raw) / 919.0
            Score = 80 + CInt(Math.Round(20.0 * T * T))
        ElseIf Raw <= 969 Then
            '5%：60~79
            Score = 60 + CInt(Math.Round(19.0 * (Raw - 920) / 49.0))
        ElseIf Raw <= 989 Then
            '2%：30~59
            Score = 30 + CInt(Math.Round(29.0 * (Raw - 970) / 19.0))
        ElseIf Raw <= 996 Then
            '0.7%：10~29
            Score = 10 + CInt(Math.Round(19.0 * (Raw - 990) / 6.0))
        Else
            '0.4%：0~9
            Score = CInt(Math.Round(9.0 * (Raw - 997) / 3.0))
        End If
        Dim Tail As String
        If Score = 100 Then
            Tail = "！100！100！！！！！" & If(ModSecret.ThemeUnlock(13, ShowDoubleHint:=False), vbCrLf & "隐藏主题 欧皇彩 已解锁！", "")
        ElseIf Score >= 98 Then
            Tail = "！差点就到 100 了呢……"
        ElseIf Score >= 90 Then
            Tail = "！好评如潮！"
        ElseIf Score >= 65 Then
            Tail = "！今天运气不错呢！"
        ElseIf Score > 50 Then
            Tail = "！还行啦，还行啦。"
        ElseIf Score = 50 Then
            Tail = "！五五开……"
        ElseIf Score >= 40 Then
            Tail = "！勉强还行吧……？"
        ElseIf Score >= 20 Then
            Tail = "！呜……"
        ElseIf Score >= 11 Then
            Tail = "？！不会吧……"
        ElseIf Score >= 1 Then
            Tail = "……（是百分制哦）"
        Else
            Tail = "？！"
            If ModMain.MyMsgBox("在查看结果前，请先同意以下附加使用条款：" & vbCrLf & vbCrLf &
                                "1. 我知晓并了解 PCL 的今日人品功能完全没有出 Bug。" & vbCrLf &
                                "2. PCL 不对使用本软件所间接造成的一切财产损失（如砸电脑等）等负责。",
                                "今日人品 - 附加使用条款", "同意并查看结果", "再见", "", IsWarn:=True) = 2 Then Return
        End If
        Dim Prefix As String = If(ModMain.FrmMain.IsSystemTimeChanged, "在这个时候，你的人品值会是：", "你今天的人品值是：")
        Dim Title As String = If(ModMain.FrmMain.IsSystemTimeChanged, "今日人品? - ", "今日人品 - ") & Date.Now.ToString("yyyy/M/d")
        ModMain.MyMsgBox(Prefix & Score.ToString() & Tail, Title, "我知道了", "", "", Score < 20)
    End Sub

    Private Shared MemoryOptimizeFlag As Integer = 0

    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        If Threading.Interlocked.Exchange(MemoryOptimizeFlag, 1) <> 0 Then
            If ShowHint Then ModMain.Hint("内存优化尚未结束，请稍等！")
            Return
        End If

        If ShowHint Then ModMain.Hint("正在进行内存优化……")
        Dim Delta As Long
        If ModWindows.HasAdminRole() Then
            Dim Before As Long = CLng(My.Computer.Info.AvailablePhysicalMemory)
            Try
                MemoryOptimizeInternal(ShowHint)
            Catch ex As Exception
                Logger.Warn(ex, "内存优化失败", If(ShowHint, LogBehavior.Toast, LogBehavior.ToastIfDebug))
                Return
            Finally
                Threading.Interlocked.Exchange(MemoryOptimizeFlag, 0)
            End Try
            Delta = CLng(My.Computer.Info.AvailablePhysicalMemory) - Before
        Else
            Logger.Info("没有管理员权限，将以命令行方式进行内存优化")
            Try
                Delta = CLng(ModBase.RunAsAdmin("--memory")) * 1024L
            Catch ex As Exception
                Logger.Warn(ex, "命令行形式内存优化失败", LogBehavior.ToastIfDebug)
                If ShowHint Then ModMain.Hint("获取管理员权限失败，请尝试右键 PCL，选择 " & ModBase.vbLQ & "以管理员身份运行" & ModBase.vbRQ & "！", ModMain.HintType.Red)
                Return
            Finally
                Threading.Interlocked.Exchange(MemoryOptimizeFlag, 0)
            End Try
            If Delta < 0 Then Return
        End If
        Dim Remain As String = StringUtils.FormatByteSize(CLng(My.Computer.Info.AvailablePhysicalMemory))
        Logger.Info($"内存优化完成，可用内存改变量：{StringUtils.FormatByteSize(Delta)}，大致剩余内存：{Remain}")
        If Delta > 0 Then
            If ShowHint Then ModMain.Hint($"内存优化完成，可用内存增加了 {StringUtils.FormatByteSize(CLng(Math.Round(CDbl(Delta) * 0.8)))}，目前剩余内存 {Remain}！", ModMain.HintType.Green)
        ElseIf ShowHint Then
            ModMain.Hint($"内存优化完成，已经优化到了最佳状态，目前剩余内存 {Remain}！")
        End If
    End Sub
    Public Shared Sub RubbishClear()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    ''' <summary>
''' 执行内存优化的实际系统调用。需要管理员权限。
''' </summary>
    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        If Not ModWindows.HasAdminRole() Then
            Throw New Exception("内存优化功能需要管理员权限！" & vbCrLf &
                                "如果需要自动以管理员身份启动 PCL，可以右键 PCL，打开 属性 → 兼容性 → 以管理员身份运行此程序。")
        End If
        Logger.Info("获取内存优化权限")
        Dim TokenHandle As IntPtr = IntPtr.Zero
        Dim StatePtr As IntPtr = IntPtr.Zero
        Try
            '取 LUID：LookupPrivilegeValueW 需要写入 8 字节 LUID
            Dim LuidPtr As IntPtr = Global.System.Runtime.InteropServices.Marshal.AllocHGlobal(8)
            Try
                If Not LookupPrivilegeValue(Nothing, "SeProfileSingleProcessPrivilege", LuidPtr) Then
                    Throw New Exception($"LookupPrivilegeValue 失败（错误代码：{Global.System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）")
                End If
                '手工构造 TOKEN_PRIVILEGES：Count(4) + Luid(8) + Attributes(4) = 16 字节
                StatePtr = Global.System.Runtime.InteropServices.Marshal.AllocHGlobal(16)
                Global.System.Runtime.InteropServices.Marshal.WriteInt32(StatePtr, 0, 1)
                Global.System.Runtime.InteropServices.Marshal.WriteInt32(StatePtr, 4, Global.System.Runtime.InteropServices.Marshal.ReadInt32(LuidPtr, 0))
                Global.System.Runtime.InteropServices.Marshal.WriteInt32(StatePtr, 8, Global.System.Runtime.InteropServices.Marshal.ReadInt32(LuidPtr, 4))
                Global.System.Runtime.InteropServices.Marshal.WriteInt32(StatePtr, 12, 2)
            Finally
                Global.System.Runtime.InteropServices.Marshal.FreeHGlobal(LuidPtr)
            End Try

            '用 OpenProcessToken 拿独立句柄；WindowsIdentity.Token 在 Using 结束时会释放
            If Not OpenProcessToken(GetCurrentProcess(), &H20UI Or &H8UI, TokenHandle) Then
                Throw New Exception($"OpenProcessToken 失败（错误代码：{Global.System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）")
            End If
            If Not AdjustTokenPrivileges(TokenHandle, False, StatePtr, 16, IntPtr.Zero, IntPtr.Zero) Then
                Throw New Exception($"AdjustTokenPrivileges 失败（错误代码：{Global.System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）")
            End If
            Dim AssignErr As Integer = Global.System.Runtime.InteropServices.Marshal.GetLastWin32Error()
            If AssignErr <> 0 Then
                Throw New Exception($"内存优化所需权限未被授予（错误代码：{AssignErr}）。请确认 PCL 以管理员身份运行。")
            End If

            '依次执行三个内存优化命令（2=清空工作集，3=系统文件缓存，4=合并内存列表）
            Dim FailCount As Integer = 0
            For num As Integer = 2 To 4
                Dim Ptr As IntPtr = Global.System.Runtime.InteropServices.Marshal.AllocHGlobal(4)
                Dim Ret As UInteger
                Try
                    Global.System.Runtime.InteropServices.Marshal.WriteInt32(Ptr, num)
                    Ret = NtSetSystemInformation(80, Ptr, 4)
                Finally
                    Global.System.Runtime.InteropServices.Marshal.FreeHGlobal(Ptr)
                End Try
                If Ret <> 0UI Then
                    FailCount += 1
                    Logger.Warn($"内存优化操作 {num} 返回非零：0x{Ret:X8}")
                Else
                    Logger.Info($"内存优化操作 {num} 完成")
                End If
            Next
            If FailCount >= 3 Then
                Throw New Exception("三个内存优化命令全部失败，可能是系统不支持或被安全软件拦截。")
            End If
        Finally
            If StatePtr <> IntPtr.Zero Then Global.System.Runtime.InteropServices.Marshal.FreeHGlobal(StatePtr)
            If TokenHandle <> IntPtr.Zero Then CloseHandle(TokenHandle)
        End Try
    End Sub
    Private Shared CaveCache As String() = Nothing
    ''' <summary>
    ''' 取一条回声洞留言。
    ''' 优先从自建服务端拉取（能看到别人新投的），失败时回退到内置的 757 条。
    ''' </summary>
    Public Shared Function GetRandomCave() As String
        '先试服务端
        Try
            Dim Remote As String = ModFeedback.FetchRandomCave()
            If Not String.IsNullOrWhiteSpace(Remote) Then Return Remote
        Catch ex As Exception
            Logger.Warn(ex, "从服务端取回声洞失败，回退到内置留言")
        End Try
        '回退到内置资源
        If CaveCache Is Nothing Then
            Dim Raw = TryCast(My.Resources.ResourceManager.GetObject("CaveMessages"), String)
            If String.IsNullOrEmpty(Raw) Then Return "开源内容中不包含回声洞留言……"
            '按换行分割：兼容 CRLF、LF，并剔除空行
            CaveCache = Raw.Split(New String() {ChrW(13) & ChrW(10), ChrW(10), ChrW(13)},
                                  StringSplitOptions.RemoveEmptyEntries).
                        Select(Function(s) s.Trim()).
                        Where(Function(s) s.Length > 0).
                        ToArray()
        End If
        If CaveCache.Length = 0 Then Return "开源内容中不包含回声洞留言……"
        Return ModBase.RandomOne(CaveCache)
    End Function

    ''' <summary>
    ''' 弹出回声洞投稿对话框。
    ''' </summary>
    Public Shared Sub SubmitCaveDialog()
        Try
            Dim Text As String = MyMsgInputBox("写下你想说的话（最多 200 字）", "回声洞投稿")
            If String.IsNullOrWhiteSpace(Text) Then Return
            If Text.Trim().Length > 200 Then
                Hint("留言最多 200 字", HintType.Red)
                Return
            End If
            Dim Author As String = MyMsgInputBox("署名（可留空）", "回声洞投稿")
            ModBase.RunInThread(
            Sub()
                ModFeedback.SubmitCave(Text, If(Author, ""))
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "打开回声洞投稿失败")
            Hint($"打开失败：{ex.Message}", HintType.Red)
        End Try
    End Sub
    Public Shared Function GetRandomHint() As String
        Try
            Dim HintFile As String = Paths.Base & "PCL\hints.txt"
            If FileUtils.Exists(HintFile) Then
                Dim Lines As List(Of String) = FileUtils.ReadAsLines(HintFile).Where(Function(s) Not String.IsNullOrWhiteSpace(s)).ToList()
                If Lines.Any() Then Return ModBase.RandomOne(Lines)
            End If
        Catch ex As Exception
            Logger.Error(ex, "获取自定义 你知道吗 提示失败", LogBehavior.Toast)
        End Try
        Return GetRandomPresetHint()
    End Function

    Public Shared Function GetRandomPresetHint() As String
        Return ModBase.RandomOne(New String() {
            "在版本选择页面，右键某个版本也能进入版本设置页面！",
            "你可以在版本设置中调整分类，以将特定版本隐藏！",
            "使用 --memory 参数启动 PCL 可以静默进行内存优化！",
            "在第一次启动游戏时，PCL 会自动将语言设置为中文！",
            "在版本设置中可以开启第三方登录验证，例如 Little Skin 登录！",
            "自动配置游戏内存时，PCL 将会根据剩余内存与 Mod 数量动态决定分配的内存！",
            "主页可以使用特定的预设，在设置中看看吧！",
            "在高级启动选项中，可以设置在游戏启动前运行特定程序！",
            "将鼠标悬浮在设置页的左边栏，可以找到重置设置按钮！",
            "版本设置只对当前版本生效，而设置页面的设置对所有版本生效！",
            "如果同时安装了 OptiFine 与对应的原版，PCL 会展示 OptiFine 版本，折叠原版！",
            "版本选择的 常规版本 分类中，只会列出最新的一个快照或预发布版。",
            "在版本选择页面，右键游戏文件夹可以进行打开、重命名、删除等操作！",
            "下载 Mod 时，PCL 会自动定位对应版本的 Mod 文件夹！",
            "如果缺少 Java，PCL 也能自动下载，不必自己安装啦！",
            "如果你打开了调试模式，启动页右侧就会显示启动日志！",
            "将鼠标悬浮在下载页的左边栏，可以找到刷新按钮！",
            "直接把 Mod 或整合包拖进 PCL 窗口就能安装了！",
            "设置中可以自定义离线皮肤，但这只对单人游戏有效！",
            "如果不想用某项功能，可以在个性化设置中把它隐藏掉！",
            "你可以使用主页来自定义快捷方式！",
            "点击版本右侧的心形就能将该版本加入收藏夹，便于查找。",
            "PCL 的第一个内部版本制作于 2018 年 8 月 13 日！",
            "据调查，有 90.3% 的用户点击了百宝箱中的千万别点按钮！",
            "PCL 的绝大多数代码都在 GitHub 开源了！",
            "PCL 的开发者龙腾猫跃经常被简称为龙猫，但和那只龙猫没有任何关系！",
            $"要将已有的 MC 文件夹加入 PCL，可以在版本选择页的左侧选择 {ModBase.vbLQ}添加已有文件夹{ModBase.vbRQ}！",
            $"如果你在其他地方修改了皮肤，需要手动选择 {ModBase.vbLQ}刷新皮肤{ModBase.vbRQ} 才能更新登录页面的头像……",
            $"将鼠标指向下载页的 MC 版本，可以在右侧找到 {ModBase.vbLQ}更新日志{ModBase.vbRQ} 选项！",
            $"在 PCL 文件夹下新建 hints.txt，可以自定义 {ModBase.vbLQ}你知道吗{ModBase.vbRQ} 的内容！",
            $"如果打开了 {ModBase.vbLQ}游戏更新提示{ModBase.vbRQ} 功能，当 MC 更新时 PCL 会弹窗进行提醒！"
        })
    End Function
    'TOKEN_PRIVILEGES 结构：DWORD PrivilegeCount + LUID_AND_ATTRIBUTES[1]
    'LUID = { DWORD LowPart; LONG HighPart }（8 字节），Attributes = DWORD
    '总大小 4 + 4 + 4 + 4 = 16 字节，按 4 字节对齐。
    '原实现用 Pack:=1 并声明 Luid As Long，导致 Luid 落在偏移 4 而不是 8，
    'AdjustTokenPrivileges 读到的是错位的 LUID，权限永远启用失败（LastError=1300），
    '进而 NtSetSystemInformation(80, ...) 返回 0xC0000061（权限不足）。
    <Global.System.Runtime.InteropServices.StructLayout(Global.System.Runtime.InteropServices.LayoutKind.Sequential, Pack:=4)>
    Private Structure PrivilegeToken
        Public PrivilegeCount As Integer
        Public LuidLowPart As Integer
        Public LuidHighPart As Integer
        Public Attributes As Integer
    End Structure

    Private Declare Function NtSetSystemInformation Lib "ntdll" (SystemInformationClass As Integer, SystemInformation As IntPtr, SystemInformationLength As Integer) As UInteger
    'lpSystemName 传 Nothing，lpName 用 LPWStr，lpLuid 是 8 字节输出缓冲
    Private Declare Function LookupPrivilegeValue Lib "advapi32" Alias "LookupPrivilegeValueW" (
        <Global.System.Runtime.InteropServices.MarshalAs(Global.System.Runtime.InteropServices.UnmanagedType.LPWStr)> lpSystemName As String,
        <Global.System.Runtime.InteropServices.MarshalAs(Global.System.Runtime.InteropServices.UnmanagedType.LPWStr)> lpName As String,
        lpLuid As IntPtr) As Boolean
    'NewState 传 IntPtr，避免结构体封送对齐问题
    Private Declare Function AdjustTokenPrivileges Lib "advapi32" (TokenHandle As IntPtr, DisableAllPrivileges As Boolean, NewState As IntPtr, BufferLength As Integer, PreviousState As IntPtr, ReturnLength As IntPtr) As Boolean
    Private Declare Function OpenProcessToken Lib "advapi32" (ProcessHandle As IntPtr, DesiredAccess As UInteger, ByRef TokenHandle As IntPtr) As Boolean
    Private Declare Function GetCurrentProcess Lib "kernel32" () As IntPtr
    Private Declare Function CloseHandle Lib "kernel32" (Handle As IntPtr) As Boolean

End Class
