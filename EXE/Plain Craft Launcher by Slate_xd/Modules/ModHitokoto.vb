'一言（Hitokoto）获取
'多 API 备份 + 本地持久缓存，断网时也能显示。
Public Module ModHitokoto

    ''' <summary>
    ''' 多个一言 API，按顺序尝试。任意一个成功即停止。
    ''' </summary>
    Private ReadOnly ApiList As String() = {
        "https://v1.hitokoto.cn/?encode=json&max_length=60",
        "https://api.uomg.com/api/rand.qinghua?format=json",
        "https://api.vvhan.com/api/ian?type=json",
        "https://international.v1.hitokoto.cn/?encode=json&max_length=60"
    }

    '缓存文件：PCL\hitokoto_cache.json
    Private ReadOnly CacheFileName As String = "hitokoto_cache.json"
    '缓存池上限，够断网用好几天
    Private Const CachePoolSize As Integer = 60

    Private CachedText As String = ""
    Private CachedFrom As String = ""
    Private CachedDate As Date = Date.MinValue
    Private ReadOnly CacheLock As New Object()

    Private Function CachePath() As String
        Return Paths.Base & "PCL\" & CacheFileName
    End Function

    ''' <summary>
    ''' 读取本地缓存池。返回 List(Of (Text, From))。
    ''' </summary>
    Private Function LoadPool() As List(Of KeyValuePair(Of String, String))
        Dim Result As New List(Of KeyValuePair(Of String, String))()
        Try
            Dim P As String = CachePath()
            If Not FileUtils.Exists(P) Then Return Result
            Dim Raw As String = FileUtils.ReadAsString(P)
            If String.IsNullOrWhiteSpace(Raw) Then Return Result
            Dim Json As JObject = Raw.DeserializeJson()
            Dim Arr = TryCast(Json("pool"), JArray)
            If Arr Is Nothing Then Return Result
            For Each Item In Arr
                Dim T As String = If(Item("t")?.ToString(), "").Trim()
                Dim F As String = If(Item("f")?.ToString(), "").Trim()
                If T <> "" Then Result.Add(New KeyValuePair(Of String, String)(T, F))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取一言缓存池失败")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 把一条新的写入缓存池（去重，超出上限时丢弃最旧的）。
    ''' </summary>
    Private Sub PushPool(Text As String, From As String)
        Try
            If String.IsNullOrWhiteSpace(Text) Then Return
            Dim Pool As List(Of KeyValuePair(Of String, String)) = LoadPool()
            '已存在就不重复添加
            For Each Kv In Pool
                If Kv.Key = Text Then Return
            Next
            Pool.Add(New KeyValuePair(Of String, String)(Text, From))
            '裁掉最旧的
            While Pool.Count > CachePoolSize
                Pool.RemoveAt(0)
            End While
            Dim Arr As New JArray()
            For Each Kv In Pool
                Arr.Add(New JObject From {
                    {"t", Kv.Key},
                    {"f", Kv.Value}
                })
            Next
            Dim Obj As New JObject From {{"pool", Arr}, {"updated", Date.Now.ToString("yyyy-MM-dd HH:mm:ss")}}
            Dim Dir As String = Paths.Base & "PCL"
            If Not DirectoryUtils.Exists(Dir) Then DirectoryUtils.Create(Dir)
            FileUtils.Write(CachePath(), Obj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "写入一言缓存池失败")
        End Try
    End Sub

    ''' <summary>
    ''' 从缓存池随机取一条。
    ''' </summary>
    Private Function PickFromPool() As (Text As String, From As String)
        Try
            Dim Pool As List(Of KeyValuePair(Of String, String)) = LoadPool()
            If Pool.Count = 0 Then Return Nothing
            Dim Pick As KeyValuePair(Of String, String) = Pool(RandomInteger(0, Pool.Count - 1))
            Return (Pick.Key, Pick.Value)
        Catch ex As Exception
            Logger.Warn(ex, "从缓存池取一言失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 依次尝试各个 API。成功返回 (Text, From)，全失败返回 Nothing。
    ''' </summary>
    Private Function FetchFromApi(Url As String) As (Text As String, From As String)
        Try
            Dim Raw As String = NetRequestByClient(Url, HttpMethod.Get, Timeout:=7000, RequireJson:=True)
            If String.IsNullOrWhiteSpace(Raw) Then Return Nothing
            Dim Json As JObject = Raw.DeserializeJson()

            Dim Text As String = ""
            Dim From As String = ""

            '兼容三种返回格式
            If Json("hitokoto") IsNot Nothing Then
                'hitokoto.cn
                Text = If(Json("hitokoto")?.ToString(), "").Trim()
                Dim Src As String = If(Json("from")?.ToString(), "").Trim()
                Dim Who As String = If(Json("from_who")?.ToString(), "").Trim()
                If Who <> "" AndAlso Src <> "" Then
                    From = $"—— {Who}《{Src}》"
                ElseIf Src <> "" Then
                    From = $"—— {Src}"
                ElseIf Who <> "" Then
                    From = $"—— {Who}"
                End If
            ElseIf Json("data") IsNot Nothing Then
                'uomg / vvhan 风格
                Dim D = Json("data")
                If TypeOf D Is JValue Then
                    Text = D.ToString().Trim()
                ElseIf D IsNot Nothing Then
                    Text = If(D("content")?.ToString(), If(D("text")?.ToString(), "")).Trim()
                End If
                From = "—— 一言"
            ElseIf Json("msg") IsNot Nothing Then
                Text = If(Json("msg")?.ToString(), "").Trim()
                From = "—— 一言"
            End If

            If Text = "" Then Return Nothing
            If Text.Length > 60 Then Text = Text.Substring(0, 60) & "…"
            Return (Text, From)
        Catch ex As Exception
            Logger.Warn(ex, $"一言 API 失败：{Url}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 获取一言。顺序：当天缓存 → 各 API → 本地缓存池 → 内置兜底。
    ''' 返回 (正文, 出处)。
    ''' </summary>
    Public Function GetHitokoto() As (Text As String, From As String)
        SyncLock CacheLock
            '1. 当天已有缓存，直接复用
            If CachedDate = Date.Today AndAlso CachedText <> "" Then
                Return (CachedText, CachedFrom)
            End If

            '2. 依次尝试各 API
            For Each Url In ApiList
                Dim R = FetchFromApi(Url)
                If R.Text IsNot Nothing AndAlso R.Text <> "" Then
                    CachedText = R.Text
                    CachedFrom = R.From
                    CachedDate = Date.Today
                    Logger.Info($"已获取一言（{New Uri(Url).Host}）：{R.Text}")
                    PushPool(R.Text, R.From)
                    Return (CachedText, CachedFrom)
                End If
            Next

            '3. 全部失败，从本地缓存池取
            Logger.Warn("所有一言 API 均失败，尝试本地缓存")
            Dim Cached = PickFromPool()
            If Cached.Text IsNot Nothing AndAlso Cached.Text <> "" Then
                CachedText = Cached.Text
                CachedFrom = Cached.From
                CachedDate = Date.Today
                Logger.Info($"使用缓存的一言：{CachedText}")
                Return (Cached.Text, Cached.From)
            End If

            '4. 完全没辙，内置兜底（不写缓存，下次仍重试）
            Logger.Warn("缓存池为空，使用内置文案")
            Return ("愿你今天也能玩得开心。", "—— Plain Craft Launcher by Slate_xd")
        End SyncLock
    End Function

    ''' <summary>
    ''' 后台预拉一条放进缓存池，不阻塞界面、不弹提示。
    ''' 供启动时的静默同步调用。
    ''' </summary>
    Public Sub WarmUp()
        Try
            For Each Url In ApiList
                Dim R = FetchFromApi(Url)
                If R.Text IsNot Nothing AndAlso R.Text <> "" Then
                    PushPool(R.Text, R.From)
                    Logger.Trace($"一言预热成功：{R.Text}")
                    Return
                End If
            Next
            Logger.Trace("一言预热失败（全部 API 不可用），已有缓存仍可用")
        Catch ex As Exception
            Logger.Warn(ex, "一言预热异常")
        End Try
    End Sub

End Module
