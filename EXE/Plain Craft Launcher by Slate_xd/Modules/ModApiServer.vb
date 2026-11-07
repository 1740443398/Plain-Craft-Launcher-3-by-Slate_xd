Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading
Imports Newtonsoft.Json.Linq

''' <summary>
''' 本地 API 服务：把启动器的能力开放给外部程序（插件、脚本、其他工具）。
''' <para>
''' <b>只监听回环地址</b>（127.0.0.1），外部网络无法访问。
''' </para>
''' <para>
''' <b>敏感信息默认不开放</b>。以下内容永远不会出现在任何响应里：
''' </para>
''' <list type="bullet">
''' <item>登录凭据、AccessToken、RefreshToken、UUID</item>
''' <item>邮箱、密码、设备识别码</item>
''' <item>完整的玩家名（只提供「是否已登录」这类布尔状态）</item>
''' <item>服务器密钥、管理令牌</item>
''' </list>
''' <para>
''' <b>写操作需要令牌</b>。只读接口可以直接调用，任何修改启动器状态的接口都必须带
''' <c>X-PCL-Token</c> 头，令牌在启动时随机生成并写入本地文件，只有本机用户能读到。
''' </para>
''' </summary>
Public Module ModApiServer

    ''' <summary>监听端口。0 表示由系统分配，实际端口写入 ApiInfoPath。</summary>
    Private Port As Integer = -1
    Private Listener As HttpListener = Nothing
    Private Running As Boolean = False
    Private ReadOnly StopLock As New Object()

    ''' <summary>本次运行的访问令牌，写操作必须携带。随机生成，不持久化。</summary>
    Public ReadOnly Property AccessToken As String = Guid.NewGuid().ToString("N")

    ''' <summary>API 信息文件路径，外部程序从这里读端口与令牌。</summary>
    Public ReadOnly Property ApiInfoPath As String
        Get
            Return Paths.Base & "PCL\api.json"
        End Get
    End Property

    ''' <summary>API 是否已启动。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Running
        End Get
    End Property

    ''' <summary>
    ''' 启动 API 服务。失败只记日志，不影响启动器正常使用。
    ''' </summary>
    ''' <param name="PreferredPort">期望端口，0 表示自动分配。</param>
    Public Function Start(Optional PreferredPort As Integer = 0) As Boolean
        If Running Then Return True
        Try
            '只绑回环地址：外部网络无法连入
            Port = If(PreferredPort > 0, PreferredPort, FindFreePort())
            Listener = New HttpListener()
            Listener.Prefixes.Add($"http://127.0.0.1:{Port}/")
            Listener.Start()
            Running = True
            WriteApiInfo()
            '后台接受请求
            Dim Thread As New Threading.Thread(AddressOf AcceptLoop) With {
                .IsBackground = True,
                .Name = "PCL API Server"
            }
            Thread.Start()
            Logger.Info($"本地 API 已启动：http://127.0.0.1:{Port}/")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "本地 API 启动失败")
            Running = False
            Return False
        End Try
    End Function

    ''' <summary>停止 API 服务。</summary>
    Public Sub [Stop]()
        SyncLock StopLock
            If Not Running Then Return
            Running = False
            Try
                Listener?.Stop()
                Listener?.Close()
            Catch ex As Exception
                Logger.Warn(ex, "关闭本地 API 失败")
            End Try
            Listener = Nothing
            Try
                If FileUtils.Exists(ApiInfoPath) Then FileUtils.Delete(ApiInfoPath, True)
            Catch
            End Try
        End SyncLock
    End Sub

    ''' <summary>找一个可用端口。</summary>
    Private Function FindFreePort() As Integer
        Dim L As New Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
        L.Start()
        Dim P = CType(L.LocalEndpoint, Net.IPEndPoint).Port
        L.Stop()
        Return P
    End Function

    ''' <summary>把端口与令牌写到本地文件，供外部程序 discovery。</summary>
    Private Sub WriteApiInfo()
        Try
            DirectoryUtils.Create(Paths.Base & "PCL\")
            Dim Info As New JObject From {
                {"port", Port},
                {"token", AccessToken},
                {"version", VersionBaseName},
                {"versionCode", VersionCode},
                {"note", "只读接口无需令牌；写操作需在请求头带 X-PCL-Token。仅本机可访问。"}
            }
            FileUtils.Write(ApiInfoPath, Info.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "写入 API 信息文件失败")
        End Try
    End Sub

    Private Sub AcceptLoop()
        While Running
            Try
                Dim Context = Listener.GetContext()
                Threading.ThreadPool.QueueUserWorkItem(
                    Sub()
                        Try
                            HandleRequest(Context)
                        Catch ex As Exception
                            Logger.Warn(ex, "处理 API 请求失败")
                            Try
                                WriteJson(Context, New JObject From {{"ok", False}, {"error", "内部错误"}}, 500)
                            Catch
                            End Try
                        End Try
                    End Sub)
            Catch ex As Exception
                'Stop() 时 GetContext 会抛异常，属正常退出
                If Not Running Then Exit While
                Logger.Warn(ex, "API 接受连接失败")
            End Try
        End While
    End Sub

    ''' <summary>路由分发。</summary>
    Private Sub HandleRequest(Context As HttpListenerContext)
        Dim Req = Context.Request
        Dim Path As String = Req.Url.AbsolutePath.TrimEnd("/"c).ToLowerInvariant()
        Dim Method As String = Req.HttpMethod.ToUpperInvariant()

        'CORS：只允许本机页面
        Context.Response.Headers("Access-Control-Allow-Origin") = "http://127.0.0.1"
        Context.Response.Headers("X-Content-Type-Options") = "nosniff"

        If Method = "OPTIONS" Then
            WriteJson(Context, New JObject From {{"ok", True}}, 200)
            Return
        End If

        '写操作鉴权
        If Method <> "GET" Then
            Dim Token = Req.Headers("X-PCL-Token")
            If Token <> AccessToken Then
                WriteJson(Context, New JObject From {{"ok", False}, {"error", "缺少或错误的 X-PCL-Token"}}, 403)
                Return
            End If
        End If

        Select Case Path
            Case "", "/", "/info"
                WriteJson(Context, BuildInfo(), 200)

            Case "/status"
                WriteJson(Context, BuildStatus(), 200)

            Case "/versions"
                WriteJson(Context, BuildVersions(), 200)

            Case "/settings"
                WriteJson(Context, BuildSettings(), 200)

            Case "/plugins"
                WriteJson(Context, BuildPlugins(), 200)

            Case "/notices"
                WriteJson(Context, BuildNotices(), 200)

            Case Else
                WriteJson(Context, New JObject From {
                    {"ok", False},
                    {"error", "未知接口"},
                    {"available", New JArray From {"/info", "/status", "/versions", "/settings", "/plugins", "/notices"}}
                }, 404)
        End Select
    End Sub

    Private Sub WriteJson(Context As HttpListenerContext, Body As JObject, StatusCode As Integer)
        Try
            Dim Bytes = Encoding.UTF8.GetBytes(Body.ToString(Newtonsoft.Json.Formatting.None))
            Context.Response.StatusCode = StatusCode
            Context.Response.ContentType = "application/json; charset=utf-8"
            Context.Response.ContentLength64 = Bytes.Length
            Context.Response.OutputStream.Write(Bytes, 0, Bytes.Length)
            Context.Response.OutputStream.Close()
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' 脱敏状态：只暴露「有没有」而不是「是什么」。
    ''' </summary>
    Public Function BuildStatus() As JObject
        Try
            Dim LoggedIn As Boolean = False
            Dim AccountType As String = "none"
            Try
                '只判断是否已登录，不读取任何凭据内容
                LoggedIn = Not String.IsNullOrWhiteSpace(Settings.Get(Of String)("LaunchLogo"))
                If LoggedIn Then AccountType = "unknown"
            Catch
            End Try

            Return New JObject From {
                {"ok", True},
                {"version", VersionBaseName},
                {"versionCode", VersionCode},
                {"hasInstance", McInstanceSelected IsNot Nothing},
                {"isLoggedIn", LoggedIn},
                {"accountType", AccountType},
                {"debugMode", ModeDebug}
            }
        Catch ex As Exception
            Return New JObject From {{"ok", False}, {"error", ex.Message}}
        End Try
    End Function

    Private Function BuildInfo() As JObject
        Return New JObject From {
            {"ok", True},
            {"name", LauncherDisplayName},
            {"version", VersionBaseName},
            {"versionCode", VersionCode},
            {"apiVersion", 1},
            {"readOnly", New JArray From {"/info", "/status", "/versions", "/settings", "/plugins", "/notices"}},
            {"writeRequiresToken", True},
            {"loopbackOnly", True}
        }
    End Function

    ''' <summary>已安装的游戏版本（不含路径等敏感信息）。</summary>
    Private Function BuildVersions() As JObject
        Try
            Dim Arr As New JArray()
            'McInstanceList 是 Dictionary(Of 卡片类型, List(Of 实例))
            For Each Card In McInstanceList
                For Each V In Card.Value
                    Arr.Add(New JObject From {
                        {"name", V.Name},
                        {"version", If(V.Version, "")}
                    })
                Next
            Next
            Return New JObject From {{"ok", True}, {"count", Arr.Count}, {"versions", Arr}}
        Catch ex As Exception
            Return New JObject From {{"ok", False}, {"error", ex.Message}}
        End Try
    End Function

    ''' <summary>
    ''' 设置项：只暴露白名单内的非敏感项。
    ''' </summary>
    Private Function BuildSettings() As JObject
        Try
            '只用真实存在的设置项，避免整块接口因为一个键名错误而失效
            Dim Safe As New JObject From {
                {"theme", Settings.Get(Of Integer)("UiLauncherTheme")},
                {"hue", Settings.Get(Of Integer)("UiLauncherHue")},
                {"sat", Settings.Get(Of Integer)("UiLauncherSat")},
                {"light", Settings.Get(Of Integer)("UiLauncherLight")},
                {"customType", Settings.Get(Of Integer)("UiCustomType")},
                {"debugMode", ModeDebug}
            }
            Return New JObject From {
                {"ok", True},
                {"note", "仅提供白名单内的非敏感设置。凭据、路径、账号信息不会出现在任何接口中。"},
                {"settings", Safe}
            }
        Catch ex As Exception
            Return New JObject From {{"ok", False}, {"error", ex.Message}}
        End Try
    End Function

    ''' <summary>插件列表（不含绝对路径）。</summary>
    Private Function BuildPlugins() As JObject
        Try
            ModPlugin.Scan()
            Dim Arr As New JArray()
            For Each P In ModPlugin.Plugins
                If P.Manifest Is Nothing Then Continue For
                Arr.Add(New JObject From {
                    {"id", P.Manifest.Id},
                    {"name", P.Manifest.Name},
                    {"version", P.Manifest.Version},
                    {"author", If(P.Manifest.Author, "")},
                    {"enabled", P.Enabled},
                    {"usable", P.IsUsable},
                    {"permissions", New JArray(P.Manifest.DeclaredPermissions.ToArray())}
                })
            Next
            Return New JObject From {{"ok", True}, {"count", Arr.Count}, {"plugins", Arr}}
        Catch ex As Exception
            Return New JObject From {{"ok", False}, {"error", ex.Message}}
        End Try
    End Function

    ''' <summary>公告（公开内容，无敏感信息）。</summary>
    Private Function BuildNotices() As JObject
        Try
            Dim Arr As New JArray()
            For Each N In ModUpdate.FetchNoticesForHome()
                Arr.Add(New JObject From {
                    {"title", N.Title},
                    {"content", N.Content}
                })
            Next
            Return New JObject From {{"ok", True}, {"count", Arr.Count}, {"notices", Arr}}
        Catch ex As Exception
            Return New JObject From {{"ok", True}, {"count", 0}, {"notices", New JArray()}, {"warning", ex.Message}}
        End Try
    End Function

End Module
