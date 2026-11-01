Imports System.Security.Cryptography
Imports System.Text.RegularExpressions

'Byethost 免费主机的 JS Cookie 挑战绕过
'
'该主机会对每个访客返回一个挑战页：
'   var a=toNumbers("key"), b=toNumbers("iv"), c=toNumbers("cipher");
'   document.cookie = "__test=" + toHex(slowAES.decrypt(c, 2, a, b));
'其中 slowAES.decrypt 是标准 AES-128-CBC，mode=2 表示 CBC、无填充。
'只要算出该 cookie 并在后续请求中带上，就能正常访问接口。
'cookie 有效期 6 小时，这里缓存起来复用。
Public Module ModByethost

    ''' <summary>
    ''' 软件端 API Key。服务端用它区分「PCL3 客户端」与「浏览器」，
    ''' 浏览器直接访问接口会得到 403。
    ''' </summary>
    Public Const ClientApiKey As String = "REPLACE_WITH_YOUR_CLIENT_API_KEY"


    ''' <summary>
    ''' 缓存已算出的挑战 cookie，避免每次请求都多走一轮。
    ''' </summary>
    Private CachedCookie As String = ""
    Private CachedCookieAt As DateTime = DateTime.MinValue
    ''' <summary>
    ''' cookie 官方有效期 6 小时，这里保守一点按 4 小时复用。
    ''' </summary>
    Private ReadOnly CookieLifetime As TimeSpan = TimeSpan.FromHours(4)
    Private ReadOnly CookieLock As New Object()

    ''' <summary>
    ''' 从十六进制字符串还原字节数组。
    ''' </summary>
    Private Function HexToBytes(Hex As String) As Byte()
        If String.IsNullOrWhiteSpace(Hex) OrElse Hex.Length Mod 2 <> 0 Then
            Throw New ArgumentException("无效的十六进制字符串")
        End If
        Dim Result(Hex.Length \ 2 - 1) As Byte
        For i = 0 To Result.Length - 1
            Result(i) = Convert.ToByte(Hex.Substring(i * 2, 2), 16)
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 用 AES-128-CBC（无填充）解密，返回十六进制小写字符串。
    ''' </summary>
    Private Function AesDecryptHex(KeyHex As String, IvHex As String, CipherHex As String) As String
        Dim KeyBytes As Byte() = HexToBytes(KeyHex)
        Dim IvBytes As Byte() = HexToBytes(IvHex)
        Dim CipherBytes As Byte() = HexToBytes(CipherHex)
        Using Aes As Aes = Aes.Create()
            Aes.KeySize = KeyBytes.Length * 8
            Aes.BlockSize = 128
            Aes.Mode = CipherMode.CBC
            Aes.Padding = PaddingMode.None
            Aes.Key = KeyBytes
            Aes.IV = IvBytes
            Using Decryptor As ICryptoTransform = Aes.CreateDecryptor()
                Dim Plain As Byte() = Decryptor.TransformFinalBlock(CipherBytes, 0, CipherBytes.Length)
                Return BitConverter.ToString(Plain).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' 判断响应内容是否为挑战页。
    ''' </summary>
    Public Function IsChallengePage(Content As String) As Boolean
        If String.IsNullOrEmpty(Content) Then Return False
        Return Content.Contains("slowAES.decrypt") OrElse
               (Content.Contains("aes.js") AndAlso Content.Contains("__test"))
    End Function

    ''' <summary>
    ''' 从挑战页中解析出三个参数并算出 __test cookie 的值。
    ''' 解析失败返回空字符串。
    ''' </summary>
    Public Function SolveChallenge(Content As String) As String
        Try
            If Not IsChallengePage(Content) Then Return ""
            '按出现顺序取三个 32 位以上的十六进制串：key、iv、cipher
            Dim Matches = Regex.Matches(Content, "toNumbers\(""([0-9a-fA-F]{32,})""\)")
            If Matches.Count < 3 Then
                Logger.Warn("挑战页参数不足，无法解析")
                Return ""
            End If
            Dim KeyHex As String = Matches(0).Groups(1).Value
            Dim IvHex As String = Matches(1).Groups(1).Value
            Dim CipherHex As String = Matches(2).Groups(1).Value
            Dim Cookie As String = AesDecryptHex(KeyHex, IvHex, CipherHex)
            Logger.Info($"已算出 Byethost 挑战 cookie：{Cookie}")
            Return Cookie
        Catch ex As Exception
            Logger.Warn(ex, "解析 Byethost 挑战失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 获取当前可用的挑战 cookie（带缓存）。
    ''' 传入一个「取原始响应」的委托，因为不同调用点用的请求封装不一样。
    ''' </summary>
    ''' <param name="Fetch">接收 URL 并返回原始响应文本的函数。</param>
    ''' <param name="ProbeUrl">用于触发挑战的 URL。</param>
    Public Function GetCookie(Fetch As Func(Of String, String), ProbeUrl As String) As String
        SyncLock CookieLock
            If CachedCookie <> "" AndAlso (DateTime.Now - CachedCookieAt) < CookieLifetime Then
                Return CachedCookie
            End If
            Try
                Dim Content As String = Fetch(ProbeUrl)
                If Not IsChallengePage(Content) Then
                    '没有挑战，说明该主机不需要 cookie
                    CachedCookie = ""
                    CachedCookieAt = DateTime.Now
                    Return ""
                End If
                Dim Cookie As String = SolveChallenge(Content)
                If Cookie <> "" Then
                    CachedCookie = Cookie
                    CachedCookieAt = DateTime.Now
                End If
                Return Cookie
            Catch ex As Exception
                Logger.Warn(ex, "获取 Byethost 挑战 cookie 失败")
                Return ""
            End Try
        End SyncLock
    End Function

    ''' <summary>
    ''' 带挑战 cookie 发起 GET 请求并返回文本。
    ''' 若首次响应仍是挑战页，会自动重试一次。
    ''' </summary>
    Public Function GetWithChallenge(Url As String, Optional Timeout As Integer = 12000) As String
        Try
            Dim Cookie As String = ""
            '先尝试直接用缓存 cookie
            SyncLock CookieLock
                If CachedCookie <> "" AndAlso (DateTime.Now - CachedCookieAt) < CookieLifetime Then
                    Cookie = CachedCookie
                End If
            End SyncLock

            Dim Content As String = HttpGetRaw(Url, Cookie, Timeout)
            If Not IsChallengePage(Content) Then Return Content

            '需要解挑战
            Dim NewCookie As String = SolveChallenge(Content)
            If NewCookie = "" Then Return Content
            SyncLock CookieLock
                CachedCookie = NewCookie
                CachedCookieAt = DateTime.Now
            End SyncLock

            '带上新 cookie 重试
            Return HttpGetRaw(Url, NewCookie, Timeout)
        Catch ex As Exception
            Logger.Warn(ex, $"请求失败（含挑战处理）：{Url}")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 最底层的 HTTP GET，可附带 cookie。
    ''' </summary>
    Private Function HttpGetRaw(Url As String, Cookie As String, Timeout As Integer) As String
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = Timeout
            Req.ReadWriteTimeout = Timeout
            Req.UserAgent = "PCL3/" & VersionBaseName
            Req.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
            '软件端凭据：服务端据此放行，浏览器直接访问会被拒绝
            Req.Headers.Add("X-PCL3-Key", ClientApiKey)
            If Not String.IsNullOrEmpty(Cookie) Then
                Req.Headers.Add("Cookie", $"__test={Cookie}")
            End If
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Net.WebException
            '尝试从错误响应体里读内容（挑战页有时以 503 返回）
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

End Module
