'PCL3 反馈与回声洞投稿
'全部走自建服务端（pcl3.byethost9.com），不依赖 GitHub 或官方接口。
Public Module ModFeedback

    ''' <summary>
    ''' 反馈分类。
    ''' </summary>
    Public ReadOnly FeedbackCategories As String() = {
        "启动问题", "下载问题", "界面问题", "功能建议", "崩溃报错", "其他"
    }

    ''' <summary>
    ''' 提交一条反馈。成功返回反馈编号，失败返回 Nothing 并已弹出提示。
    ''' </summary>
    Public Function SubmitFeedback(Title As String, Content As String,
                                   Optional Category As String = "其他",
                                   Optional Contact As String = "",
                                   Optional IncludeLog As Boolean = True) As String
        Try
            If String.IsNullOrWhiteSpace(Title) Then
                Hint("请填写反馈标题", HintType.Red)
                Return Nothing
            End If
            If String.IsNullOrWhiteSpace(Content) Then
                Hint("请填写反馈内容", HintType.Red)
                Return Nothing
            End If

            Dim LogText As String = ""
            If IncludeLog Then
                Try
                    LogText = GetRecentLogTail(4000)
                Catch ex As Exception
                    Logger.Warn(ex, "读取日志片段失败")
                End Try
            End If

            Dim Post As New Dictionary(Of String, String) From {
                {"title", Title.Trim()},
                {"content", Content.Trim()},
                {"category", Category},
                {"contact", If(Contact, "").Trim()},
                {"version", VersionBaseName},
                {"system", Environment.OSVersion.VersionString},
                {"log", LogText}
            }
            Dim Raw As String = PostForm($"{ModUpdate.ApiBase}?action=feedback", Post)
            If String.IsNullOrWhiteSpace(Raw) Then
                Hint("提交失败：服务器无响应", HintType.Red)
                Return Nothing
            End If
            Dim Json As JObject = Raw.DeserializeJson()
            If Json("ok")?.ToObject(Of Boolean)() <> True Then
                Hint($"提交失败：{Json("error")?.ToString()}", HintType.Red)
                Return Nothing
            End If
            Dim Id As String = If(Json("feedback")?("id")?.ToString(), "")
            Logger.Info($"反馈已提交：{Id}")
            Hint($"反馈已提交，编号 {Id}，感谢你的反馈！", HintType.Green)
            Return Id
        Catch ex As Exception
            Logger.Error(ex, "提交反馈失败")
            Hint($"提交失败：{ex.Message}", HintType.Red)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 提交一条回声洞留言。
    ''' </summary>
    Public Function SubmitCave(Text As String, Optional Author As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(Text) Then
                Hint("留言内容不能为空", HintType.Red)
                Return False
            End If
            If Text.Trim().Length > 200 Then
                Hint("留言最多 200 字", HintType.Red)
                Return False
            End If
            Dim Post As New Dictionary(Of String, String) From {
                {"text", Text.Trim()},
                {"author", If(Author, "").Trim()}
            }
            Dim Raw As String = PostForm($"{ModUpdate.ApiBase}?action=cave_submit", Post)
            If String.IsNullOrWhiteSpace(Raw) Then
                Hint("投稿失败：服务器无响应", HintType.Red)
                Return False
            End If
            Dim Json As JObject = Raw.DeserializeJson()
            If Json("ok")?.ToObject(Of Boolean)() <> True Then
                Hint($"投稿失败：{Json("error")?.ToString()}", HintType.Red)
                Return False
            End If
            If Json("duplicate")?.ToObject(Of Boolean)() = True Then
                Hint("这条留言刚刚已经投过了", HintType.Blue)
                Return False
            End If
            Hint("投稿成功，感谢你的留言！", HintType.Green)
            Return True
        Catch ex As Exception
            Logger.Error(ex, "回声洞投稿失败")
            Hint($"投稿失败：{ex.Message}", HintType.Red)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 从服务端随机取一条回声洞留言。失败返回空字符串。
    ''' </summary>
    Public Function FetchRandomCave() As String
        Try
            Dim Raw As String = ModByethost.GetWithChallenge($"{ModUpdate.ApiBase}?action=cave", Timeout:=10000)
            If String.IsNullOrWhiteSpace(Raw) Then Return ""
            Dim Json As JObject = Raw.DeserializeJson()
            Dim Cave As JObject = TryCast(Json("cave"), JObject)
            If Cave Is Nothing Then Return ""
            Return If(Cave("text")?.ToString(), "").Trim()
        Catch ex As Exception
            Logger.Warn(ex, "获取回声洞失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 提交表单（自动处理 Byethost 的挑战 cookie）。
    ''' </summary>
    Private Function PostForm(Url As String, Fields As Dictionary(Of String, String)) As String
        Try
            Dim Pairs As New List(Of String)
            For Each Kv In Fields
                Pairs.Add($"{Uri.EscapeDataString(Kv.Key)}={Uri.EscapeDataString(Kv.Value)}")
            Next
            Dim Body As String = String.Join("&", Pairs)
            Return HttpPostWithChallenge(Url, Body)
        Catch ex As Exception
            Logger.Warn(ex, "提交表单失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 带挑战处理的 POST。
    ''' </summary>
    Private Function HttpPostWithChallenge(Url As String, Body As String) As String
        Try
            '先取 cookie
            Dim Cookie As String = ""
            Try
                Dim Probe As String = ModByethost.GetWithChallenge($"{ModUpdate.ApiBase}?action=cave", Timeout:=10000)
                'GetWithChallenge 内部已缓存 cookie，这里通过反射拿不到，改为直接解一次
                Dim RawProbe As String = HttpRaw(Url, "", "")
                If ModByethost.IsChallengePage(RawProbe) Then
                    Cookie = ModByethost.SolveChallenge(RawProbe)
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取挑战 cookie 失败")
            End Try
            Return HttpRaw(Url, Body, Cookie)
        Catch ex As Exception
            Logger.Warn(ex, "POST 失败")
            Return ""
        End Try
    End Function

    Private Function HttpRaw(Url As String, Body As String, Cookie As String) As String
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = If(String.IsNullOrEmpty(Body), "GET", "POST")
            Req.Timeout = 20000
            Req.ReadWriteTimeout = 20000
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
            If Not String.IsNullOrEmpty(Cookie) Then Req.Headers.Add("Cookie", $"__test={Cookie}")
            If Not String.IsNullOrEmpty(Body) Then
                Req.ContentType = "application/x-www-form-urlencoded"
                Dim Bytes As Byte() = Text.Encoding.UTF8.GetBytes(Body)
                Req.ContentLength = Bytes.Length
                Using S As IO.Stream = Req.GetRequestStream()
                    S.Write(Bytes, 0, Bytes.Length)
                End Using
            End If
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Net.WebException
            If ex.Response IsNot Nothing Then
                Try
                    Using Reader As New IO.StreamReader(ex.Response.GetResponseStream(), Text.Encoding.UTF8)
                        Return Reader.ReadToEnd()
                    End Using
                Catch
                End Try
            End If
            Logger.Warn(ex, $"HTTP 请求失败：{Url}")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 读取最近日志的尾部若干字符。
    ''' </summary>
    Private Function GetRecentLogTail(MaxChars As Integer) As String
        Try
            '日志文件固定为 PCL\Log1.txt
            Dim LogPath As String = Paths.Base & "PCL\Log1.txt"
            If Not FileUtils.Exists(LogPath) Then Return ""
            Dim All As String = FileUtils.ReadAsString(LogPath)
            If All.Length <= MaxChars Then Return All
            Return All.Substring(All.Length - MaxChars)
        Catch ex As Exception
            Logger.Warn(ex, "读取日志失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 弹出反馈提交对话框。
    ''' </summary>
    Public Sub ShowSubmitDialog()
        Try
            Dim Title As String = MyMsgInputBox("请用一句话概括问题（例如：下载到 80% 卡死）", "反馈标题")
            If String.IsNullOrWhiteSpace(Title) Then Return

            Dim Content As String = MyMsgInputBox(
                "请描述问题现象、复现步骤、期望结果。" & vbCrLf &
                "可直接粘贴错误信息，提交时会自动附带最近日志。", "反馈内容")
            If String.IsNullOrWhiteSpace(Content) Then Return

            Dim Category As String = "其他"
            Select Case MyMsgBox("请选择反馈分类：" & vbCrLf &
                                 "「是」= 启动问题    「否」= 下载问题" & vbCrLf &
                                 "点「取消」则归为其他", "反馈分类", "启动问题", "下载问题", "其他")
                Case 1 : Category = "启动问题"
                Case 2 : Category = "下载问题"
                Case 3 : Category = "其他"
            End Select

            RunInThread(
            Sub()
                SubmitFeedback(Title, Content, Category, "", IncludeLog:=True)
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "打开反馈对话框失败")
            Hint($"打开失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 查看反馈列表（服务端页面）。
    ''' </summary>
    Public Sub ShowMyFeedback()
        Try
            OpenWebsite($"{ModUpdate.ApiBase}?action=feedback_list")
        Catch ex As Exception
            Logger.Warn(ex, "打开反馈列表失败")
        End Try
    End Sub

End Module