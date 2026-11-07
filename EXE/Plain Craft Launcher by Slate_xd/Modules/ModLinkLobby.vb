Imports System.Collections.Generic
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 联机大厅：浏览公开房间。
''' <para>
''' 只有房主选择「公开」时房间才会出现在这里。密码房只暴露「需要密码」这个事实，
''' 密码本身从不上传，客人仍需向房主索要。
''' </para>
''' </summary>
Public Module ModLinkLobby

    ''' <summary>大厅里的一个房间。</summary>
    Public Class LobbyRoom
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property Host As String = ""
        Public Property Version As String = ""
        Public Property Loader As String = ""
        Public Property Players As Integer = 0
        Public Property MaxPlayers As Integer = 8
        Public Property HasPassword As Boolean = False
        Public Property Motd As String = ""
        Public Property Beat As Long = 0

        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace(Name), "未命名房间", Name)
            End Get
        End Property

        Public ReadOnly Property LockText As String
            Get
                Return If(HasPassword, "需要密码", "无密码")
            End Get
        End Property

        Public ReadOnly Property PlayersText As String
            Get
                Return $"{Players}/{MaxPlayers}"
            End Get
        End Property

        Public ReadOnly Property SubtitleText As String
            Get
                Dim Parts As New List(Of String)
                If Not String.IsNullOrWhiteSpace(Host) Then Parts.Add($"房主 {Host}")
                If Not String.IsNullOrWhiteSpace(Version) Then Parts.Add($"MC {Version}")
                If Not String.IsNullOrWhiteSpace(Loader) Then Parts.Add(Loader)
                Parts.Add(LockText)
                Return String.Join(" · ", Parts)
            End Get
        End Property
    End Class

    ''' <summary>最近一次拉取到的房间。</summary>
    Public ReadOnly LastRooms As New List(Of LobbyRoom)
    ''' <summary>最近一次拉取是否成功。</summary>
    Public Property LastFetchOK As Boolean = False
    ''' <summary>最近一次失败的原因（成功时为空）。</summary>
    Public Property LastError As String = ""

    ''' <summary>
    ''' 拉取公开房间列表。
    ''' </summary>
    Public Function Fetch() As Boolean
        Try
            Dim Url As String = $"{ModUpdate.ApiBase}?action=lobby_list"
            Dim Raw As String = HttpGet(Url)
            If String.IsNullOrWhiteSpace(Raw) Then Return Fail("服务端没有响应")
            If ModByethost.IsChallengePage(Raw) Then Return Fail("被服务端挑战页拦截")

            Dim Json As JObject = Raw.DeserializeJson()
            If Json Is Nothing Then Return Fail("返回内容不是合法 JSON")
            If Json("ok")?.ToObject(Of Boolean)() <> True Then
                Return Fail(If(Json("error")?.ToString(), "服务端返回失败"))
            End If

            Dim Arr As JArray = Json("rooms")
            LastRooms.Clear()
            If Arr IsNot Nothing Then
                For Each Item In Arr
                    LastRooms.Add(New LobbyRoom With {
                        .Code = If(Item("code")?.ToString(), ""),
                        .Name = If(Item("name")?.ToString(), ""),
                        .Host = If(Item("host")?.ToString(), ""),
                        .Version = If(Item("version")?.ToString(), ""),
                        .Loader = If(Item("loader")?.ToString(), ""),
                        .Players = If(Item("players")?.ToObject(Of Integer)(), 0),
                        .MaxPlayers = If(Item("maxPlayers")?.ToObject(Of Integer)(), 8),
                        .HasPassword = If(Item("hasPassword")?.ToObject(Of Boolean)(), False),
                        .Motd = If(Item("motd")?.ToString(), ""),
                        .Beat = If(Item("beat")?.ToObject(Of Long)(), 0L)
                    })
                Next
            End If
            LastFetchOK = True
            LastError = ""
            Logger.Info($"联机大厅：拉取到 {LastRooms.Count} 个房间")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "拉取联机大厅失败")
            Return Fail(ex.Message)
        End Try
    End Function

    ''' <summary>房主心跳：把自己的房间登记到大厅。</summary>
    Public Function Beat(Room As ModLinkPassword.RoomState, Optional PlayerCount As Integer = 1) As Boolean
        Try
            If Room Is Nothing OrElse Not Room.IsPublic Then Return False
            If String.IsNullOrWhiteSpace(Room.InviteCode) Then Return False
            Dim Body As New JObject From {
                {"code", Room.InviteCode},
                {"name", $"{Room.PlayerName} 的房间"},
                {"host", Room.PlayerName},
                {"players", PlayerCount},
                {"maxPlayers", 8},
                {"hasPassword", Room.HasPassword},
                {"motd", ""}
            }
            Dim Raw As String = HttpPost($"{ModUpdate.ApiBase}?action=lobby_beat",
                                         Body.ToString(Newtonsoft.Json.Formatting.None))
            If String.IsNullOrWhiteSpace(Raw) Then Return False
            Dim Json As JObject = Raw.DeserializeJson()
            Return Json IsNot Nothing AndAlso Json("ok")?.ToObject(Of Boolean)() = True
        Catch ex As Exception
            Logger.Warn(ex, "大厅心跳失败")
            Return False
        End Try
    End Function

    ''' <summary>房主下线：从大厅移除。</summary>
    Public Sub Remove(Room As ModLinkPassword.RoomState)
        Try
            If Room Is Nothing OrElse String.IsNullOrWhiteSpace(Room.InviteCode) Then Return
            Dim Body As New JObject From {{"code", Room.InviteCode}}
            HttpPost($"{ModUpdate.ApiBase}?action=lobby_remove",
                     Body.ToString(Newtonsoft.Json.Formatting.None))
            Logger.Info("已从联机大厅移除房间")
        Catch ex As Exception
            Logger.Warn(ex, "从大厅移除房间失败")
        End Try
    End Sub

    ''' <summary>记录失败原因并返回 False。</summary>
    Private Function Fail(Msg As String) As Boolean
        LastFetchOK = False
        LastError = If(Msg, "未知错误")
        Logger.Warn($"联机大厅：{LastError}")
        Return False
    End Function

    '==== 内部 HTTP（与 ModUpdate 的写法保持一致）====

    Private Function HttpGet(Url As String) As String
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 12000
            Req.ReadWriteTimeout = 12000
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
            Req.Headers.Add("X-PCL3-Key", ModByethost.ClientApiKey)
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "大厅 HTTP GET 失败")
            Return Nothing
        End Try
    End Function

    Private Function HttpPost(Url As String, JsonBody As String) As String
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "POST"
            Req.Timeout = 12000
            Req.ReadWriteTimeout = 12000
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.ContentType = "application/json"
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
            Req.Headers.Add("X-PCL3-Key", ModByethost.ClientApiKey)
            Dim Bytes As Byte() = Encoding.UTF8.GetBytes(If(JsonBody, "{}"))
            Req.ContentLength = Bytes.Length
            Using S = Req.GetRequestStream()
                S.Write(Bytes, 0, Bytes.Length)
            End Using
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "大厅 HTTP POST 失败")
            Return Nothing
        End Try
    End Function

End Module
