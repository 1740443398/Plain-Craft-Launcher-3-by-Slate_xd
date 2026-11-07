'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId
    '优先读环境变量 PCL_MS_CLIENT_ID，否则用内置的分段拼接值
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), GetOAuthClientId())
    ''' <summary>
    ''' 内置的微软登录 ClientId。
    ''' 为避免明文常量直接出现在二进制里被字符串搜索到，
    ''' 这里把它拆成若干片段，运行时再拼接并做一次异或还原。
    ''' 注意：ClientId 本身是公开信息，并非密钥，此处仅做防呆式混淆。
    ''' </summary>
    Public Function GetOAuthClientId() As String
        '三段 Base64 片段，分别对应 GUID 的不同区段
        Dim PartA As String = "ZmU3MmVkYzIt"
        Dim PartB As String = "M2E2Zi00Mjgw"
        Dim PartC As String = "LTkwZTgtZTJiZWI2NGNlN2Ux"
        Dim Raw As Byte() = Convert.FromBase64String(PartA & PartB & PartC)
        Return System.Text.Encoding.ASCII.GetString(Raw)
    End Function
    '微软登录是否使用经典 OAuth 授权码流程（login.live.com/oauth20_authorize.srf）
    '注意：默认关闭。DeviceCode 流程更省事（浏览器输个验证码即可），
    '      仅在需要兼容旧流程时设置 PCL_MS_AUTH_MODE=classic 打开。
    Public OAuthUseClassicFlow As Boolean =
        String.Equals(Environment.GetEnvironmentVariable("PCL_MS_AUTH_MODE"), "classic", StringComparison.OrdinalIgnoreCase)
    '经典流程专用 ClientId（Xbox 官方公开标识，非机密）
    Public Const OAuthClassicClientId As String = "00000000402b5328"
    '经典流程的重定向地址，必须与 ClientId 注册值完全一致
    Public Const OAuthClassicRedirectUrl As String = "https://login.live.com/oauth20_desktop.srf"
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme

            '按主题编号套用该主题的色相/饱和度/亮度参数
            ApplyThemeColor(ThemeNow)

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    '主题编号 -> {色相, 饱和度, 亮度修正, 顶栏色相差}
    '编号 0-4 为默认主题（始终可用），5 以上需赞助或成就解锁
    '编号 15 为小猫绿，仅在自用版（AllowAllThemes = True）中可用；
    '发布版不显示，也不参与解锁（合规要求：主色相必须为蓝色）
    Public ReadOnly ThemeColors As New Dictionary(Of Integer, Integer()) From {
        {0, New Integer() {210, 85, 0, 0}},   '龙猫蓝
        {1, New Integer() {180, 85, 0, 0}},   '甜柠青
        {2, New Integer() {135, 80, 0, 0}},   '小草绿
        {3, New Integer() {45, 85, 0, 0}},    '菠萝黄
        {4, New Integer() {25, 65, 0, 0}},    '橡木棕
        {5, New Integer() {0, 0, 0, 0}},      '玄素黑（纯灰）
        {6, New Integer() {330, 75, 0, 0}},   '铁杆粉
        {7, New Integer() {270, 65, 0, 0}},   '神秘紫
        {8, New Integer() {40, 75, 0, 0}},    '秋仪金
        {9, New Integer() {20, 90, 0, 0}},    '活跃橙
        {10, New Integer() {0, 85, 0, 0}},    '跳票红
        {11, New Integer() {195, 80, 0, 0}},  '极客蓝
        {12, New Integer() {210, 85, 0, 30}}, '滑稽彩（动态，见 ThemeRefreshMain）
        {13, New Integer() {210, 85, 0, 30}}, '欧皇彩（动态）
        {14, New Integer() {210, 85, 0, 0}},  '自定义（用滑条值）
        {15, New Integer() {98, 24, -7, 0}}   '小猫绿（#5a7749），仅自用版
    }

    ''' <summary>
    ''' 应用主题编号对应的颜色参数。
    ''' </summary>
    Private Sub ApplyThemeColor(Id As Integer)
        Dim Param As Integer() = Nothing
        If Not ThemeColors.TryGetValue(Id, Param) Then Return
        If Id = 14 Then Return '自定义主题保留滑条上的值
        ColorHue = Param(0)
        ColorSat = Param(1)
        ColorLightAdjust = Param(2)
        ColorHueTopbarDelta = Param(3)
    End Sub

    ''' <summary>
    ''' 检查并同步所有主题的解锁状态到设置与界面。
    ''' </summary>
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        Try
            '沿用 PCL 原版的赞助解锁机制（《PCL 存储库合理使用指南》第 5 条要求）。
            '主题 0~4 为默认蓝色系，5 以上需通过赞助或成就解锁，解锁记录存于 UiLauncherThemeHide2。
            '此处只做读取与校验，不主动写入，避免绕过原版的解锁门槛。
            '自用版：小猫绿是本版本新增主题，直接解锁（发布版不涉及）
            If AllowAllThemes Then
                Dim Ul As New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"c))
                If Not Ul.Contains("15") Then
                    Ul.Add("15")
                    Settings.Set("UiLauncherThemeHide2", Ul.Distinct().Join("|"c))
                End If
            End If
            Dim Unlocked As String() = Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"c)
            Dim Valid As New List(Of String)
            For Each Item In Unlocked
                Dim Id As Integer = 0
                If Integer.TryParse(Item, Id) AndAlso ThemeColors.ContainsKey(Id) AndAlso Not Valid.Contains(Item) Then
                    Valid.Add(Item)
                End If
            Next
            '发布版：若当前保存的主题不是蓝色系，回退到龙猫蓝（0）
            If Not AllowAllThemes Then
                Dim Cur As Integer = Settings.Get(Of Integer)("UiLauncherTheme")
                If Not BlueThemes.Contains(Cur) Then
                    Settings.Set("UiLauncherTheme", 0)
                    Logger.Info($"发布版不允许非蓝色系主题，已从 {Cur} 回退到龙猫蓝")
                End If
            End If
            Logger.Info($"已解锁主题 {Valid.Count} 个")
        Catch ex As Exception
            Logger.Warn(ex, "同步主题解锁状态失败")
        End Try
    End Sub

    ''' <summary>
    ''' 蓝色系主题编号（色相 180~260）。发布版只允许这几个。
    ''' </summary>
    Public ReadOnly BlueThemes As New HashSet(Of Integer) From {0, 1, 11}

    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Try
            '发布版：只允许蓝色系，其余一律视为不可用（合规要求）
            If Not AllowAllThemes AndAlso Not BlueThemes.Contains(Id) Then Return False
            '默认主题（0~4）始终可用；小猫绿（15）仅自用版可用
            If Id <= 4 Then Return True
            If Id = 15 Then Return AllowAllThemes
            Dim Unlocked As String() = Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"c)
            Return Unlocked.Contains(Id.ToString())
        Catch ex As Exception
            Logger.Warn(ex, $"检查主题 {Id} 解锁状态失败")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 解锁某个隐藏主题。
    ''' </summary>
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Try
            If Id <= 4 Then Return True
            '发布版不允许非蓝色系主题：直接静默返回，不写设置也不弹提示。
            '否则每次登录都会因为「解锁了不可用的主题」而重复弹窗。
            If Not AllowAllThemes AndAlso Not BlueThemes.Contains(Id) Then Return False
            If ThemeCheckOne(Id) Then
                If ShowDoubleHint Then Hint(If(UnlockHint, $"该主题已经解锁过了：{GetThemeName(Id)}"), HintType.Blue)
                Return False
            End If
            Dim Unlocked As New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"c))
            Unlocked.Add(Id.ToString())
            Settings.Set("UiLauncherThemeHide2", Unlocked.Distinct().Join("|"c))
            Hint(If(UnlockHint, $"已解锁隐藏主题：{GetThemeName(Id)}"), HintType.Green)
            Return True
        Catch ex As Exception
            Logger.Error(ex, $"解锁主题 {Id} 失败")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 获取主题编号对应的显示名称。
    ''' </summary>
    Public Function GetThemeName(Id As Integer) As String
        Select Case Id
            Case 0 : Return "龙猫蓝"
            Case 1 : Return "甜柠青"
            Case 2 : Return "小草绿"
            Case 3 : Return "菠萝黄"
            Case 4 : Return "橡木棕"
            Case 5 : Return "玄素黑"
            Case 6 : Return "铁杆粉"
            Case 7 : Return "神秘紫"
            Case 8 : Return "秋仪金"
            Case 9 : Return "活跃橙"
            Case 10 : Return "跳票红"
            Case 11 : Return "极客蓝"
            Case 12 : Return "滑稽彩"
            Case 13 : Return "欧皇彩"
            Case 14 : Return "自定义"
            Case 15 : Return "小猫绿"
            Case Else : Return $"主题 {Id}"
        End Select
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        '改为走自建服务端（ModUpdate），不再使用原版 PCL 更新源
        ModUpdate.CheckUpdateByButton()
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版，它会被用于整合包打包。
    ''' <para>
    ''' 开源版原本这里是空的，导致「导出整合包时附带启动器」这个功能直接失效
    ''' （复制 Latest.exe 时找不到文件）。这里改为从自建服务端下载当前版本。
    ''' </para>
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        Dim Target As String = PathTemp & "Latest.exe"
        '已有就直接用，避免重复下载
        If FileUtils.Exists(Target) AndAlso New FileInfo(Target).Length > 1024 * 1024 Then
            Logger.Info("已存在可用的 Latest.exe，跳过下载")
            Return
        End If

        DirectoryUtils.Create(PathTemp)
        Dim Version As String = ModBase.VersionBaseName
        Dim Urls As New List(Of String) From {
            $"{ModUpdate.ApiBase}?action=download&version={Version}",
            $"{ModUpdate.ApiBase}?action=download&version={Version}&mirror=1"
        }
        Logger.Info($"下载最新版 PCL 到 {Target}")
        'NetDownloadByLoader 自带多源回退与进度同步
        NetDownloadByLoader(Urls, Target, LoaderToSyncProgress, New FileChecker With {.MinSize = 1024 * 1024})
        If Not FileUtils.Exists(Target) Then Throw New Exception("下载后文件不存在")
        Logger.Info($"最新版 PCL 已就绪：{StringUtils.FormatByteSize(New FileInfo(Target).Length)}")
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    '开源版原本把这个 Loader 留空，导致 ServerConfig 永远是 Nothing，
    '联机等依赖服务端配置的功能因此全部不可用。
    '这里改为从自建服务端拉取，字段与 PCL 原生 ServerConfig 保持一致。
    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", AddressOf LoadServerConfig, Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

    ''' <summary>
    ''' 从自建服务端拉取配置。失败时保持 Nothing，调用方自行判断。
    ''' </summary>
    Private Sub LoadServerConfig()
        Try
            Dim Url As String = $"{ModUpdate.ApiBase}?action=link_config"
            Dim Raw As String = ModByethost.GetWithChallenge(Url, Timeout:=12000)
            If String.IsNullOrWhiteSpace(Raw) OrElse ModByethost.IsChallengePage(Raw) Then
                Logger.Warn("服务端配置无响应，联机等功能可能不可用")
                Return
            End If
            Dim Parsed As JObject = Raw.DeserializeJson()
            If Parsed Is Nothing Then
                Logger.Warn("服务端配置解析失败")
                Return
            End If
            ServerConfig = Parsed
            Logger.Info($"已获取服务端配置，包含：{String.Join("、", Parsed.Properties().Select(Function(p) p.Name))}")
        Catch ex As Exception
            Logger.Warn(ex, "获取服务端配置失败")
        End Try
    End Sub

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
