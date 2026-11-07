Imports System.Text
Imports System.Diagnostics
Imports System.IO
Imports Newtonsoft.Json.Linq

''' <summary>
''' Terracotta（陶瓦联机）集成。
''' <para>
''' 不再自己拼 EasyTier 命令行，改为启动 Terracotta 进程并通过它的本地 HTTP API 驱动。
''' Terracotta 是成熟方案（HMCL 也用它），自带 NAT 难度评估与完整状态机。
''' </para>
''' <para>
''' 集成方式遵循 Terracotta 的 AGPL 例外条款第 2 条：
''' 通过其提供的进程间通信接口（HTTP API）与<em>未经修改</em>的本作品交互，
''' 并在界面明显处标识版权。因此本项目不会因此被 AGPL 涵盖。
''' </para>
''' <para>
''' HTTP 服务只监听 127.0.0.1，端口随机，通过 <c>--hmcl &lt;file&gt;</c> 写出的 JSON 发现。
''' </para>
''' </summary>
Public Module ModTerracotta

    ''' <summary>Terracotta 的版权标识，必须在界面显示（AGPL 例外条款要求）。</summary>
    Public Const CopyrightNotice As String = "联机功能由 Terracotta（陶瓦联机）提供"

    ''' <summary>房间状态。</summary>
    Public Enum RoomState
        Unknown = 0
        ''' <summary>待机。</summary>
        Waiting = 1
        ''' <summary>房主：扫描本机 Minecraft 端口。</summary>
        HostScanning = 2
        ''' <summary>房主：创建房间中。</summary>
        HostStarting = 3
        ''' <summary>房主：房间就绪。</summary>
        HostOk = 4
        ''' <summary>加入方：连接中。</summary>
        GuestConnecting = 5
        ''' <summary>加入方：启动中。</summary>
        GuestStarting = 6
        ''' <summary>加入方：已加入。</summary>
        GuestOk = 7
        ''' <summary>异常。</summary>
        Exception = 8
    End Enum

    ''' <summary>一次状态快照。</summary>
    Public Class StateSnapshot
        Public Property State As RoomState = RoomState.Unknown
        ''' <summary>单调递增的状态序号，用于忽略过期响应。</summary>
        Public Property Index As Long = 0
        ''' <summary>邀请码（房主时）。</summary>
        Public Property Room As String = ""
        ''' <summary>加入方可用直连地址。</summary>
        Public Property Url As String = ""
        ''' <summary>NAT 连接难度（加入方）。</summary>
        Public Property Difficulty As String = ""
        ''' <summary>异常类型码。</summary>
        Public Property ExceptionType As Integer = -1
        ''' <summary>成员显示名列表。</summary>
        Public Property Players As New List(Of String)
    End Class

    Private Port As Integer = -1
    Private Proc As Process = Nothing
    Private PortFile As String = Nothing

    ''' <summary>
    ''' 启动 Terracotta 并等待 HTTP 端口。失败返回 False 并写日志。
    ''' </summary>
    ''' <param name="ExePath">terracotta 可执行文件路径。</param>
    Public Function Start(exePath As String) As Boolean
        Try
            If Port > 0 Then Return True
            If String.IsNullOrWhiteSpace(exePath) OrElse Not FileUtils.Exists(exePath) Then
                Logger.Warn($"找不到 Terracotta：{exePath}")
                Return False
            End If

            PortFile = PathTemp & $"terracotta-port-{Guid.NewGuid():N}.json"
            DirectoryUtils.Create(PathUtils.RemoveLastPart(PortFile))

            Dim Info As New ProcessStartInfo With {
                .FileName = exePath,
                .Arguments = $"--hmcl ""{PortFile}""",
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }
            Proc = New Process With {.StartInfo = Info}
            Proc.Start()

            '等待端口文件出现（文档说最长 8 秒，这里给 12 秒余量）
            For i = 1 To 120
                Threading.Thread.Sleep(100)
                If Proc.HasExited Then
                    Logger.Warn("Terracotta 进程提前退出")
                    Cleanup()
                    Return False
                End If
                If FileUtils.Exists(PortFile) Then
                    Try
                        Dim Raw As String = FileUtils.ReadAsString(PortFile)
                        If Not String.IsNullOrWhiteSpace(Raw) Then
                            Dim Json = JObject.Parse(Raw)
                            Dim P = Json("port")?.ToObject(Of Integer)()
                            If P.HasValue AndAlso P.Value > 0 Then
                                Port = P.Value
                                Logger.Info($"Terracotta 已就绪，端口 {Port}")
                                Return True
                            End If
                        End If
                    Catch ex As Exception
                        '文件可能正写到一半，下一轮再试
                    End Try
                End If
            Next
            Logger.Warn("等待 Terracotta 端口超时")
            Cleanup()
            Return False
        Catch ex As Exception
            Logger.Error(ex, "启动 Terracotta 失败")
            Cleanup()
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 查询当前状态。失败返回 Nothing。
    ''' </summary>
    Public Function GetState() As StateSnapshot
        Dim Raw = HttpGet("/state")
        If Raw Is Nothing Then Return Nothing
        Try
            Dim Json = JObject.Parse(Raw)
            Dim Snap As New StateSnapshot
            Snap.Index = If(Json("index")?.ToObject(Of Long)(), 0L)
            Snap.Room = If(Json("room")?.ToString(), "")
            Snap.Url = If(Json("url")?.ToString(), "")
            Snap.Difficulty = If(Json("difficulty")?.ToString(), "")
            If Json("type") IsNot Nothing Then Snap.ExceptionType = Json("type").ToObject(Of Integer)()
            Dim Profiles = TryCast(Json("profiles"), JArray)
            If Profiles IsNot Nothing Then
                For Each P In Profiles
                    Dim Name = P("name")?.ToString()
                    If Not String.IsNullOrWhiteSpace(Name) Then Snap.Players.Add(Name)
                Next
            End If
            Select Case If(Json("state")?.ToString(), "").ToLowerInvariant()
                Case "waiting" : Snap.State = RoomState.Waiting
                Case "host-scanning" : Snap.State = RoomState.HostScanning
                Case "host-starting" : Snap.State = RoomState.HostStarting
                Case "host-ok" : Snap.State = RoomState.HostOk
                Case "guest-connecting" : Snap.State = RoomState.GuestConnecting
                Case "guest-starting" : Snap.State = RoomState.GuestStarting
                Case "guest-ok" : Snap.State = RoomState.GuestOk
                Case "exception" : Snap.State = RoomState.Exception
                Case Else : Snap.State = RoomState.Unknown
            End Select
            Return Snap
        Catch ex As Exception
            Logger.Warn(ex, "解析 Terracotta 状态失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>以房主身份开房。</summary>
    Public Function StartHost(Optional PlayerName As String = "") As Boolean
        Dim Q = $"/state/scanning?player={Uri.EscapeDataString(If(PlayerName, ""))}"
        Return HttpGet(Q) IsNot Nothing
    End Function

    ''' <summary>以加入方身份加入房间。</summary>
    Public Function JoinRoom(RoomCode As String, Optional PlayerName As String = "") As Boolean
        If String.IsNullOrWhiteSpace(RoomCode) Then Return False
        Dim Q = $"/state/guesting?room={Uri.EscapeDataString(RoomCode)}&player={Uri.EscapeDataString(If(PlayerName, ""))}"
        Return HttpGet(Q) IsNot Nothing
    End Function

    ''' <summary>优雅退出 Terracotta。</summary>
    Public Sub [Stop]()
        Try
            If Port > 0 Then HttpGet("/panic?peaceful=true", 2000)
        Catch ex As Exception
            Logger.Warn(ex, "请求 Terracotta 退出失败")
        End Try
        Cleanup()
    End Sub

    Private Sub Cleanup()
        Try
            If Proc IsNot Nothing AndAlso Not Proc.HasExited Then
                Try
                    Proc.Kill()
                Catch
                End Try
            End If
        Catch
        End Try
        Proc = Nothing
        Port = -1
        Try
            If PortFile IsNot Nothing AndAlso FileUtils.Exists(PortFile) Then FileUtils.Delete(PortFile, True)
        Catch
        End Try
        PortFile = Nothing
    End Sub

    ''' <summary>
    ''' 向本地 HTTP API 发 GET 请求。失败返回 Nothing。
    ''' </summary>
    Private Function HttpGet(PathAndQuery As String, Optional TimeoutMs As Integer = 5000) As String
        If Port <= 0 Then Return Nothing
        Try
            Dim Url = $"http://127.0.0.1:{Port}{PathAndQuery}"
            Dim Req = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = TimeoutMs
            Req.ReadWriteTimeout = TimeoutMs
            Req.Proxy = Nothing '本地回环，不走代理
            Using Resp = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Net.WebException
            '连接失败通常意味着 Terracotta 已退出
            Logger.Warn(ex, $"请求 Terracotta 失败：{PathAndQuery}")
            Return Nothing
        Catch ex As Exception
            Logger.Warn(ex, $"请求 Terracotta 出错：{PathAndQuery}")
            Return Nothing
        End Try
    End Function

End Module
