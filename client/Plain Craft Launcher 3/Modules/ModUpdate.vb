'PCL3 更新检查与公告
'不再使用原版 PCL 的更新源，改为自建服务端（pcl3.byethost9.com）。
'服务端是一个 PHP 接口，返回 JSON；这里负责拉取、解析与提示。
Public Module ModUpdate

    ''' <summary>
    ''' 更新与公告接口地址。
    ''' </summary>
    Public Const ApiBase As String = "https://pcl3.byethost9.com/api/"

    ''' <summary>
    ''' 当前版本对应的数字码，用于服务端比较。
    ''' 3.0.0.1 → 3001
    ''' </summary>
    Public ReadOnly Property LocalVersionCode As Integer
        Get
            Return VersionToCode(VersionBaseName)
        End Get
    End Property

    ''' <summary>
    ''' 把 3.0.0.1 这类版本号转成 3001 形式的整数。
    ''' </summary>
    Public Function VersionToCode(Version As String) As Integer
        Try
            Dim Parts As String() = Version.Trim().Split("."c, "-"c, "+"c)
            Dim Numbers As New List(Of Integer)
            For Each P In Parts
                Dim N As Integer = 0
                If Integer.TryParse(P, N) Then Numbers.Add(N)
                If Numbers.Count >= 4 Then Exit For
            Next
            While Numbers.Count < 4
                Numbers.Add(0)
            End While
            Return Numbers(0) * 1000 + Numbers(1) * 100 + Numbers(2) * 10 + Numbers(3)
        Catch ex As Exception
            Logger.Warn(ex, $"解析版本号失败：{Version}")
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 一次检查的结果。
    ''' </summary>
    Public Class UpdateCheckResult
        Public HasUpdate As Boolean
        Public LatestVersion As String = ""
        Public LatestCode As Integer
        Public DownloadUrl As String = ""
        ''' <summary>所有可用下载地址（服务器自存优先，备用地址在后）。</summary>
        Public DownloadUrls As New List(Of String)
        Public Sha256 As String = ""
        Public Notes As String = ""
        Public Force As Boolean
        Public Notices As New List(Of UpdateNotice)
    End Class

    Public Class UpdateNotice
        Public Id As String = ""
        Public Title As String = ""
        Public Content As String = ""
        Public Priority As Integer
    End Class

    ''' <summary>
    ''' 从服务端检查更新并拉取公告。失败时返回 Nothing。
    ''' </summary>
    Public Function CheckUpdate(Optional Channel As String = "release") As UpdateCheckResult
        Try
            Dim Url As String = $"{ApiBase}?action=check&version={LocalVersionCode}&channel={Uri.EscapeDataString(Channel)}"
            Logger.Info($"检查更新：{Url}")
            '服务端在 Byethost 免费主机上，需要先绕过它的 JS Cookie 挑战
            Dim Raw As String = ModByethost.GetWithChallenge(Url, Timeout:=12000)
            If String.IsNullOrWhiteSpace(Raw) Then
                Logger.Warn("更新接口无响应")
                Return Nothing
            End If
            If ModByethost.IsChallengePage(Raw) Then
                Logger.Warn("更新接口仍返回挑战页，挑战绕过失败")
                Return Nothing
            End If
            Dim Json As JObject
            Try
                Json = Raw.DeserializeJson()
            Catch ex As Exception
                Logger.Warn($"更新接口返回的不是有效 JSON：{Raw.Substring(0, Math.Min(200, Raw.Length))}")
                Return Nothing
            End Try
            If Json("ok")?.ToObject(Of Boolean)() <> True Then
                Logger.Warn($"更新接口返回失败：{Raw}")
                Return Nothing
            End If

            Dim Result As New UpdateCheckResult With {
                .HasUpdate = Json("hasUpdate")?.ToObject(Of Boolean)() = True
            }
            Dim Latest As JObject = TryCast(Json("latest"), JObject)
            If Latest IsNot Nothing Then
                Result.LatestVersion = If(Latest("version")?.ToString(), "")
                Result.LatestCode = If(Latest("code")?.ToObject(Of Integer)(), 0)
                '优先取服务端给出的直链列表（服务器自存优先），没有再回退到 url
                Result.DownloadUrl = If(Latest("downloadUrl")?.ToString(), If(Latest("url")?.ToString(), ""))
                Dim UrlArray As JArray = TryCast(Latest("downloadUrls"), JArray)
                If UrlArray IsNot Nothing Then
                    For Each U As JToken In UrlArray
                        Dim S As String = If(U?.ToString(), "").Trim()
                        If S <> "" AndAlso Not Result.DownloadUrls.Contains(S) Then Result.DownloadUrls.Add(S)
                    Next
                End If
                If Result.DownloadUrls.Count = 0 AndAlso Result.DownloadUrl <> "" Then
                    Result.DownloadUrls.Add(Result.DownloadUrl)
                End If
                Result.Sha256 = If(Latest("sha256")?.ToString(), "")
                Result.Notes = If(Latest("notes")?.ToString(), "")
                Result.Force = Latest("force")?.ToObject(Of Boolean)() = True
            End If

            Dim NoticeArray As JArray = TryCast(Json("notices"), JArray)
            If NoticeArray IsNot Nothing Then
                For Each Item As JToken In NoticeArray
                    Dim Obj As JObject = TryCast(Item, JObject)
                    If Obj Is Nothing Then Continue For
                    Result.Notices.Add(New UpdateNotice With {
                        .Id = If(Obj("id")?.ToString(), ""),
                        .Title = If(Obj("title")?.ToString(), ""),
                        .Content = If(Obj("content")?.ToString(), ""),
                        .Priority = If(Obj("priority")?.ToObject(Of Integer)(), 0)
                    })
                Next
            End If
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, "检查更新失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 「检查更新」按钮：检查后给出提示或询问是否下载。
    ''' 注意：方法名不能与 ModSecret.UpdateCheckByButton 重名，否则调用点会报二义性。
    ''' </summary>
    Public Sub CheckUpdateByButton()
        RunInThread(
        Sub()
            Try
                Dim Result As UpdateCheckResult = CheckUpdate()
                If Result Is Nothing Then
                    Hint("检查更新失败，请稍后再试", HintType.Red)
                    Return
                End If
                If Not Result.HasUpdate Then
                    Hint($"已是最新版本（{VersionDisplay}）", HintType.Green)
                    Return
                End If
                RunInUi(
                Sub()
                    Dim Msg As String = $"发现新版本：{Result.LatestVersion}{vbCrLf}" &
                                        $"当前版本：{VersionDisplay}"
                    If Not String.IsNullOrWhiteSpace(Result.Notes) Then
                        Msg &= vbCrLf & vbCrLf & Result.Notes
                    End If
                    If Result.DownloadUrl = "" Then
                        MyMsgBox(Msg & vbCrLf & vbCrLf & "但服务端没有提供下载地址。", "发现新版本", "我知道了", IsWarn:=True)
                        Return
                    End If
                    If MyMsgBox(Msg, "发现新版本", "立即更新", "稍后") = 1 Then
                        DownloadAndRunUpdate(Result)
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "检查更新失败")
                Hint($"检查更新失败：{ex.Message}", HintType.Red)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 启动时静默拉取公告，有内容就显示在主页。
    ''' </summary>
    Public Function FetchNoticesForHome() As List(Of UpdateNotice)
        Try
            Dim Result As UpdateCheckResult = CheckUpdate()
            If Result Is Nothing Then Return New List(Of UpdateNotice)
            Return Result.Notices
        Catch ex As Exception
            Logger.Warn(ex, "拉取公告失败")
            Return New List(Of UpdateNotice)
        End Try
    End Function

    ''' <summary>
    ''' 一条赞助者记录。
    ''' </summary>
    Public Class DonorEntry
        Public Name As String = ""
        Public Amount As String = ""
        Public Message As String = ""
    End Class

    ''' <summary>
    ''' 从服务端拉取赞助者名单。失败时返回空列表。
    ''' </summary>
    Public Function FetchDonors() As List(Of DonorEntry)
        Try
            Dim Url As String = $"{ApiBase}?action=donors"
            Dim Raw As String = ModByethost.GetWithChallenge(Url, Timeout:=12000)
            If String.IsNullOrWhiteSpace(Raw) OrElse ModByethost.IsChallengePage(Raw) Then
                Logger.Warn("赞助者名单无响应")
                Return New List(Of DonorEntry)
            End If
            Dim Json As JObject = Raw.DeserializeJson()
            Dim Result As New List(Of DonorEntry)
            Dim Arr As JArray = TryCast(Json("donors"), JArray)
            If Arr Is Nothing Then Return Result
            For Each Item As JToken In Arr
                Dim Obj As JObject = TryCast(Item, JObject)
                If Obj Is Nothing Then Continue For
                '隐藏的赞助者不显示
                If Obj("hidden")?.ToObject(Of Boolean)() = True Then Continue For
                Result.Add(New DonorEntry With {
                    .Name = If(Obj("name")?.ToString(), ""),
                    .Amount = If(Obj("amount")?.ToString(), ""),
                    .Message = If(Obj("message")?.ToString(), "")
                })
            Next
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, "拉取赞助者名单失败")
            Return New List(Of DonorEntry)
        End Try
    End Function


    ''' <summary>
    ''' 在应用内下载安装包，完成后直接运行。
    ''' 优先用服务器自存的文件，失败时回退到备用地址。
    ''' </summary>
    Public Sub DownloadAndRunUpdate(Result As UpdateCheckResult)
        If Result Is Nothing Then Return
        RunInThread(
        Sub()
            Dim TempFile As String = PathTemp & "PCL3-Update-" & Result.LatestVersion & ".exe"
            Try
                '优先用服务器中转地址
                Dim Urls As New List(Of String)
                If Not String.IsNullOrWhiteSpace(Result.DownloadUrl) Then Urls.Add(Result.DownloadUrl)
                For Each U In Result.DownloadUrls
                    If Not Urls.Contains(U) Then Urls.Add(U)
                Next
                If Urls.Count = 0 Then
                    RunInUi(Sub() Hint("服务端没有提供下载地址", HintType.Red))
                    Return
                End If

                Logger.Info($"开始下载更新：{Result.LatestVersion}，共 {Urls.Count} 个地址")
                Dim Ok As Boolean = False
                Dim LastError As String = ""
                For Each Url In Urls
                    Try
                        RunInUi(Sub() Hint($"正在下载更新（{Result.LatestVersion}）……", HintType.Blue))
                        Logger.Info($"尝试下载：{Url}")
                        Dim Data As Byte() = NetGetBytes(Url, Timeout:=180000)
                        If Data Is Nothing OrElse Data.Length < 1024 Then
                            LastError = "文件过小或为空"
                            Continue For
                        End If
                        '校验 SHA-256
                        If Not String.IsNullOrWhiteSpace(Result.Sha256) Then
                            Dim Actual As String = GetBytesSha256(Data)
                            If Not String.Equals(Actual, Result.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                                LastError = $"校验失败（期望 {Result.Sha256.Substring(0, 12)}…，实际 {Actual.Substring(0, 12)}…）"
                                Logger.Warn($"下载校验失败：{Url}，{LastError}")
                                Continue For
                            End If
                        End If
                        FileUtils.Write(TempFile, Data)
                        Logger.Info($"下载完成，已保存到 {TempFile}")
                        Ok = True
                        Exit For
                    Catch ex As Exception
                        LastError = ex.Message
                        Logger.Warn(ex, $"下载失败：{Url}")
                    End Try
                Next

                If Not Ok Then
                    RunInUi(Sub() Hint($"下载失败：{LastError}", HintType.Red))
                    Return
                End If

                '询问是否立即运行
                Dim DoRun As Boolean = False
                RunInUi(
                Sub()
                    DoRun = MyMsgBox($"新版本 {Result.LatestVersion} 已下载完成。" & vbCrLf &
                                     "是否立即关闭当前启动器并运行安装包？",
                                     "下载完成", "立即运行", "稍后") = 1
                End Sub)
                If DoRun Then
                    Try
                        StartProcess(TempFile)
                        Logger.Info("已启动更新安装包，准备退出")
                        RunInUi(Sub() FrmMain.EndProgram(False))
                    Catch ex As Exception
                        Logger.Error(ex, "启动安装包失败")
                        RunInUi(Sub() Hint($"启动安装包失败：{ex.Message}", HintType.Red))
                    End Try
                Else
                    RunInUi(Sub() Hint($"安装包已保存到：{TempFile}", HintType.Green))
                End If
            Catch ex As Exception
                Logger.Error(ex, "下载更新失败")
                RunInUi(Sub() Hint($"下载更新失败：{ex.Message}", HintType.Red))
            End Try
        End Sub)
    End Sub

    ''' <summary>下载字节流（带挑战 cookie 处理）。</summary>
    Private Function NetGetBytes(Url As String, Optional Timeout As Integer = 120000) As Byte()
        Try
            Dim Cookie As String = ""
            '第一次请求可能返回挑战页
            Dim Probe As Byte() = HttpGetBytesRaw(Url, "", Timeout)
            If Probe IsNot Nothing AndAlso Probe.Length > 0 AndAlso Probe.Length < 4096 Then
                Dim Text As String = Encoding.UTF8.GetString(Probe)
                If ModByethost.IsChallengePage(Text) Then
                    Cookie = ModByethost.SolveChallenge(Text)
                    If Cookie <> "" Then Return HttpGetBytesRaw(Url, Cookie, Timeout)
                End If
            End If
            Return Probe
        Catch ex As Exception
            Logger.Warn(ex, $"下载字节流失败：{Url}")
            Return Nothing
        End Try
    End Function

    Private Function HttpGetBytesRaw(Url As String, Cookie As String, Timeout As Integer) As Byte()
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = Timeout
            Req.ReadWriteTimeout = Timeout
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
            Req.Headers.Add("X-PCL3-Key", ModByethost.ClientApiKey)
            If Not String.IsNullOrEmpty(Cookie) Then Req.Headers.Add("Cookie", $"__test={Cookie}")
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Ms As New IO.MemoryStream()
                    Resp.GetResponseStream().CopyTo(Ms)
                    Return Ms.ToArray()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"HTTP 字节流请求失败：{Url}")
            Return Nothing
        End Try
    End Function

    ''' <summary>计算字节流的 SHA-256（小写十六进制）。</summary>
    Private Function GetBytesSha256(Data As Byte()) As String
        Try
            Using Sha As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(Sha.ComputeHash(Data)).Replace("-", "").ToLowerInvariant()
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "计算 SHA-256 失败")
            Return ""
        End Try
    End Function

End Module
